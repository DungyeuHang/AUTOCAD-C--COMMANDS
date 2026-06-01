using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
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

        public static void Initialize()
        {
            EnsurePalette();
            PaletteCommandUsageTracker.Initialize();
            if (PaletteStartupStore.LoadAutoShow())
            {
                ReloadPaletteData(false);
                _paletteSet.Visible = true;
            }
        }

        public static void Terminate()
        {
            PaletteCommandUsageTracker.Terminate();
        }

        public static void ShowPalette()
        {
            EnsurePalette();
            ReloadPaletteData(false);
            _paletteSet.Visible = true;
        }

        public static bool IsAutoShowEnabled()
        {
            return PaletteStartupStore.LoadAutoShow();
        }

        public static void SetAutoShowEnabled(bool enabled)
        {
            PaletteStartupStore.SaveAutoShow(enabled);
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
        }
    }
}
