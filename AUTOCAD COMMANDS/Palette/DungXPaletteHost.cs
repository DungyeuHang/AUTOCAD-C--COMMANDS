using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcCoreApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;


namespace AUTOCAD_COMMANDS
{

    // ======================================================
    // DXPALETTE HOST
    // Quản lý vòng đời PaletteSet: tạo palette, reload data, auto-open khi mở AutoCAD.
    // Phần UI thật nằm trong DungXPaletteControl bên dưới.
    // ======================================================
    internal static class DungXPaletteHost
    {
        private static readonly Guid PaletteGuid =
            new Guid("2E5D6E63-70A5-4D41-B72B-50BFC66F37D1");

        private static PaletteSet _paletteSet;
        private static DungXPaletteControl _paletteControl;
        private static bool _idleHooked;
        private static bool _lastKnownVisible;
        private static bool _pendingHide;
        private static bool _visibilityIdleHooked;
        private static bool _isTerminating;
        private static bool _quitHooked;

        public static void Initialize()
        {
            _isTerminating = false;
            HookQuitEvent();
            _lastKnownVisible = LoadLastVisible();
            EnsurePalette();
            PaletteCommandUsageTracker.Initialize();
            if (_lastKnownVisible)
            {
                try
                {
                    RestorePaletteState();
                    _paletteSet.Visible = true;
                    _pendingHide = false;
                    _lastKnownVisible = true;
                }
                catch
                {
                }

                EnsureIdleHook();
            }
            else
            {
                _paletteSet.Visible = false;
            }
        }

        public static void Terminate()
        {
            _isTerminating = true;
            UnhookQuitEvent();
            RemoveVisibilityIdleHook();

            // Do not read PaletteSet.Visible here: AutoCAD may already have
            // hidden it as part of shutdown. Use the last user-known state.
            SavePaletteState(_lastKnownVisible);
            RemoveIdleHook();
            PaletteCommandUsageTracker.Terminate();
        }

        public static void ShowPalette()
        {
            EnsurePalette();
            RestorePaletteState();
            ReloadPaletteData(false);
            _paletteSet.Visible = true;
            _lastKnownVisible = true;
            _pendingHide = false;
            SavePaletteState(true);
        }

        public static bool IsAutoShowEnabled()
        {
            return LoadLastVisible();
        }

        public static void SetAutoShowEnabled(bool enabled)
        {
            _lastKnownVisible = enabled;
            _pendingHide = false;
            PaletteStartupStore.SaveAutoShow(enabled);
            WorkspaceUiStateStore.SaveValues(
                new Dictionary<string, string>
                {
                    ["palette.visible"] = enabled ? "1" : "0"
                });

            if (_paletteSet != null && !_paletteSet.IsDisposed)
            {
                if (enabled)
                {
                    EnsurePalette();
                    ReloadPaletteData(false);
                }

                _paletteSet.Visible = enabled;
                SavePaletteState(enabled);
            }
        }

        public static void ReloadPaletteData(bool showMessage)
        {
            EnsurePalette();
            _paletteControl.ReloadData(showMessage);
        }

        public static bool ChooseLispFolder(bool showMessage)
        {
            EnsurePalette();
            bool selected = DungXLispResolver.PickLispRoot(showMessage);
            if (selected)
            {
                _paletteControl.ReloadData(showMessage);
            }
            return selected;
        }

        public static void RunCommand(PaletteCommandItem item)
        {
            if (item == null)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WF.MessageBox.Show(
                    "Khong co ban ve dang active.",
                    "DungX Palette",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return;
            }

            if (!EnsureSourceLoaded(doc, item))
            {
                _paletteControl.ReloadData(true);
                return;
            }

            doc.SendStringToExecute(item.CommandName + " ", true, false, false);
            _paletteControl?.SetStatus(
                $"Dang chay {item.CommandName} | {item.SourceLabel}");
        }

        public static void NotifyCommandUsage(string commandName, int usageCount)
        {
            _paletteControl?.RecordUsage(commandName, usageCount);
        }

        private static bool EnsureSourceLoaded(Document doc, PaletteCommandItem item)
        {
            if (item.SourceKind == PaletteSourceKind.BuiltInDll ||
                item.SourceKind == PaletteSourceKind.ActionMacro ||
                item.SourceKind == PaletteSourceKind.ManualAlias)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                WF.MessageBox.Show(
                    "Khong tim thay file nguon:\n" + item.SourcePath,
                    "DungX Palette",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return false;
            }

            if (item.SourceKind == PaletteSourceKind.ManagedDll)
            {
                string netloadExpr =
                    "_.NETLOAD \"" + item.SourcePath.Replace("\"", "\"\"") + "\" ";
                doc.SendStringToExecute(netloadExpr, true, false, false);
                return true;
            }

            string loadExpr = $"(load \"{EscapeForLisp(item.SourcePath)}\") ";
            doc.SendStringToExecute(loadExpr, true, false, false);
            return true;
        }

        private static string EscapeForLisp(string path)
        {
            return path
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static void EnsurePalette()
        {
            if (_paletteSet != null && _paletteControl != null)
            {
                return;
            }

            _paletteControl = new DungXPaletteControl();
            _paletteSet = new PaletteSet("DungX Commands", PaletteGuid)
            {
                Style = PaletteSetStyles.ShowAutoHideButton
                      | PaletteSetStyles.ShowCloseButton
                      | PaletteSetStyles.Snappable,
                MinimumSize = new Size(110, 220),
                Size = new Size(560, 700),
                DockEnabled = DockSides.Left | DockSides.Right,
                KeepFocus = false
            };

            _paletteSet.Add("Command List", _paletteControl);
            RestorePaletteState();
            _paletteSet.PaletteSetMoved += (_, __) => SavePaletteState();
            _paletteSet.SizeChanged += (_, __) => SavePaletteState();
            _paletteSet.StateChanged += PaletteSet_StateChanged;
            _paletteSet.PaletteSetDestroy += PaletteSet_Destroy;
        }

        private static void PaletteSet_StateChanged(
            object sender,
            PaletteSetStateEventArgs e)
        {
            if (_isTerminating)
            {
                return;
            }

            if (e.NewState == StateEventIndex.Show)
            {
                _pendingHide = false;
                _lastKnownVisible = true;
                SavePaletteState(true);
                return;
            }

            if (e.NewState == StateEventIndex.Hide)
            {
                // A user close is committed on the next idle event. During AutoCAD
                // shutdown the palette can be hidden before Terminate is called;
                // deferring the write prevents that shutdown hide from overwriting
                // the user's last visible state.
                _pendingHide = true;
                EnsureVisibilityIdleHook();
            }
        }

        private static void PaletteSet_Destroy(object sender, EventArgs e)
        {
            _isTerminating = true;
            _pendingHide = false;
            RemoveVisibilityIdleHook();
            SavePaletteState(_lastKnownVisible);
        }

        private static void HookQuitEvent()
        {
            if (_quitHooked)
            {
                return;
            }

            AcCoreApplication.QuitWillStart += OnAutoCadQuitWillStart;
            _quitHooked = true;
        }

        private static void UnhookQuitEvent()
        {
            if (!_quitHooked)
            {
                return;
            }

            AcCoreApplication.QuitWillStart -= OnAutoCadQuitWillStart;
            _quitHooked = false;
        }

        private static void OnAutoCadQuitWillStart(object sender, EventArgs e)
        {
            // QuitWillStart occurs before AutoCAD hides/destroys PaletteSet.
            // Commit a pending user close now; later teardown events are ignored.
            if (_pendingHide &&
                _paletteSet != null &&
                !_paletteSet.IsDisposed &&
                !_paletteSet.Visible)
            {
                _pendingHide = false;
                _lastKnownVisible = false;
                SavePaletteState(false);
            }

            _isTerminating = true;
            RemoveVisibilityIdleHook();
            RemoveIdleHook();
        }

        private static bool LoadLastVisible()
        {
            return WorkspaceUiStateStore.TryGetBool("palette.visible", out bool visible)
                ? visible
                : PaletteStartupStore.LoadAutoShow();
        }

        private static void RestorePaletteState()
        {
            if (_paletteSet == null || _paletteSet.IsDisposed)
            {
                return;
            }

            if (WorkspaceUiStateStore.TryGetInt("palette.dock", out int dockValue))
            {
                try
                {
                    _paletteSet.Dock = (DockSides)dockValue;
                }
                catch
                {
                    // Let AutoCAD use its normal docking state when the saved value is invalid.
                }
            }

            if (WorkspaceUiStateStore.TryGetSize("palette", out Size savedSize) &&
                savedSize.Width >= _paletteSet.MinimumSize.Width &&
                savedSize.Height >= _paletteSet.MinimumSize.Height)
            {
                try
                {
                    _paletteSet.Size = savedSize;
                }
                catch
                {
                    // Ignore an invalid size and keep AutoCAD's default.
                }
            }

            if (WorkspaceUiStateStore.TryGetPoint("palette", out Point savedLocation) &&
                IsLocationUsable(savedLocation))
            {
                try
                {
                    _paletteSet.Location = savedLocation;
                }
                catch
                {
                    // Ignore an invalid location and keep AutoCAD's default.
                }
            }
        }

        private static void SavePaletteState(bool? visibleOverride = null)
        {
            if (_paletteSet == null || _paletteSet.IsDisposed)
            {
                return;
            }

            try
            {
                Point location = _paletteSet.Location;
                Size size = _paletteSet.Size;
                WorkspaceUiStateStore.SaveValues(
                    new Dictionary<string, string>
                    {
                        ["palette.visible"] = (visibleOverride ?? _lastKnownVisible) ? "1" : "0",
                        ["palette.x"] = WorkspaceUiStateStore.ToInvariant(location.X),
                        ["palette.y"] = WorkspaceUiStateStore.ToInvariant(location.Y),
                        ["palette.width"] = WorkspaceUiStateStore.ToInvariant(size.Width),
                        ["palette.height"] = WorkspaceUiStateStore.ToInvariant(size.Height),
                        ["palette.dock"] = WorkspaceUiStateStore.ToInvariant((int)_paletteSet.Dock)
                    });
            }
            catch
            {
                // PaletteSet can be mid-destruction during AutoCAD shutdown.
            }
        }

        private static bool IsLocationUsable(Point location)
        {
            foreach (WF.Screen screen in WF.Screen.AllScreens)
            {
                if (screen.WorkingArea.Contains(location))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureVisibilityIdleHook()
        {
            if (_visibilityIdleHooked)
            {
                return;
            }

            Application.Idle += OnPaletteVisibilityIdle;
            _visibilityIdleHooked = true;
        }

        private static void RemoveVisibilityIdleHook()
        {
            if (!_visibilityIdleHooked)
            {
                return;
            }

            Application.Idle -= OnPaletteVisibilityIdle;
            _visibilityIdleHooked = false;
        }

        private static void OnPaletteVisibilityIdle(object sender, EventArgs e)
        {
            RemoveVisibilityIdleHook();

            if (_isTerminating || !_pendingHide)
            {
                return;
            }

            _pendingHide = false;
            if (_isTerminating || _paletteSet == null || _paletteSet.IsDisposed || _paletteSet.Visible)
            {
                return;
            }

            _lastKnownVisible = false;
            SavePaletteState(false);
        }

        private static void EnsureIdleHook()
        {
            if (_idleHooked)
            {
                return;
            }

            Application.Idle += OnApplicationIdle;
            _idleHooked = true;
        }

        private static void RemoveIdleHook()
        {
            if (!_idleHooked)
            {
                return;
            }

            Application.Idle -= OnApplicationIdle;
            _idleHooked = false;
        }

        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            try
            {
                if (!LoadLastVisible())
                {
                    RemoveIdleHook();
                    return;
                }

                RestorePaletteState();
                ReloadPaletteData(false);
                _paletteSet.Visible = true;
                _lastKnownVisible = true;
                _pendingHide = false;
                SavePaletteState(true);
                RemoveIdleHook();
            }
            catch
            {
                // AutoCAD may still be creating its document/UI. Retry on the next idle event.
            }
        }
    }
}
