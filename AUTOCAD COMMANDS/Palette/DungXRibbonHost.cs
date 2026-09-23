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
    // RIBBON DUNGX
    // Tạo tab/panel/nút ribbon từ danh sách command trong project.
    // Nếu đổi tên CommandMethod thủ công, nhớ cập nhật các mảng command dưới đây nếu muốn Ribbon chạy đúng.
    // ======================================================
    internal static class DungXRibbonHost
    {
        private const int MaxRibbonRestoreAttempts = 30;

        private const string TabId = "DUNGX_RIBBON_TAB";
        // Các command hiện lên panel Dimension.
        private static readonly string[] DimensionCommands =
            { "DAA_Dim_auto", "DDD_Dim_4_direction", "SDXY", "BD", "CDD2_CHIADIM" };

        // Các command hiện lên panel Stretch.
        private static readonly string[] StretchCommands =
            { "SS", "SSD", "SSD2_SMART_STRETCH_BY_DIM2", "SX", "SY" };

        // Các command hiện lên panel Cut / Trim.
        private static readonly string[] CutTrimCommands =
            { "ACC_AUTO_CUT", "ACC_TRIM_OUTSIDE", "ACC_TRIM_INSIDE" };

        // Các command tiện ích workspace/palette/ribbon.
        private static readonly string[] ToolCommands =
            { "DXPALETTE", "DXPALETTERELOAD", "DXPALETTESETFOLDER", "DXRIBBONRELOAD" };

        // Các command tôn chấn / dãn phôi.
        private static readonly string[] SheetMetalCommands =
            { "DX_FOIL" };

        private static readonly string[] HiddenCommands =
            {
                "DXRIBBON",
                "ACC",
                "ACT",
                "ACC_AUTO_CUT_SETTINGS",
                "ACC_AUTO_CUT_TEST",
                "ACC_AUTO_TRIM_OUTSIDE",
                "ACC_TRIM_TEST",
                // DX_FOIL chỉ hiện đúng 1 nút trên ribbon. Alias và các lệnh phụ
                // (cài đặt / hiệu chuẩn / kiểm thử) vẫn gõ được ở dòng lệnh.
                "DANPHOI",
                "DX_FOIL_SETTINGS",
                "DX_FOIL_CALIB",
                "DX_FOIL_TEST"
            };

        private static readonly HashSet<string> KnownCommands =
            new HashSet<string>(
                DimensionCommands
                    .Concat(StretchCommands)
                    .Concat(CutTrimCommands)
                    .Concat(ToolCommands)
                    .Concat(SheetMetalCommands)
                    .Concat(HiddenCommands),
                StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, RibbonCommandStyle> RibbonStyles =
            BuildRibbonStyles();
        private static readonly Dictionary<string, Media.ImageSource> LargeImageCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Media.ImageSource> SmallImageCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static bool _idleHooked;
        private static bool _ribbonCommandSent;
        private static bool _ribbonCloseCommandSent;
        private static bool _restoreRibbonVisible = true;
        private static bool _lastKnownRibbonVisible = true;
        private static bool _pendingRibbonHide;
        private static bool _ribbonVisibilityIdleHooked;
        private static bool _isTerminating;
        private static RibbonControl _observedRibbon;
        private static bool _quitHooked;
        private static int _ribbonRestoreAttempts;
        private static DateTime _nextRibbonRestoreAttemptUtc;

        public static void Initialize()
        {
            _isTerminating = false;
            HookQuitEvent();
            // The DUNGX tab is a startup surface.  In particular, do not send
            // RIBBONCLOSE merely because an older session captured AutoCAD's
            // temporary Ribbon hide while it was shutting down.
            _restoreRibbonVisible = true;
            _lastKnownRibbonVisible = true;
            _isTerminating = false;
            _ribbonRestoreAttempts = 0;
            _nextRibbonRestoreAttemptUtc = DateTime.MinValue;

            if (_restoreRibbonVisible)
            {
                EnsureRibbonCreated(false);
                try
                {
                    ShowRibbon();
                }
                catch
                {
                }
            }

            EnsureIdleHook();
        }

        public static void Terminate()
        {
            _isTerminating = true;
            UnhookQuitEvent();
            RemoveRibbonVisibilityIdleHook();
            SaveRibbonState(_lastKnownRibbonVisible);

            if (_idleHooked)
            {
                Application.Idle -= OnApplicationIdle;
                _idleHooked = false;
            }

            UnobserveRibbon();
            _ribbonCommandSent = false;
            _ribbonCloseCommandSent = false;
        }

        public static void ShowRibbon()
        {
            _restoreRibbonVisible = true;

            if (!EnsureRibbonCreated(false))
            {
                RequestRibbonCommand();
                EnsureIdleHook();
                return;
            }

            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                return;
            }

            if (!ribbon.IsVisible)
            {
                RequestRibbonCommand();
                EnsureIdleHook();
                return;
            }

            RibbonTab tab = FindRibbonTab(ribbon);
            if (tab == null)
            {
                return;
            }

            tab.IsVisible = true;
            ribbon.ActiveTab = tab;
            _ribbonCommandSent = false;
            SaveRibbonState(true);
        }

        public static void ReloadRibbon(bool showMessage)
        {
            _restoreRibbonVisible = true;
            bool created = EnsureRibbonCreated(true);
            if (created)
            {
                ShowRibbon();
            }

            if (!showMessage)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage(
                created
                    ? "\nDX Ribbon da duoc reload."
                    : "\nDX Ribbon se duoc tao khi AutoCAD san sang giao dien Ribbon.");
        }

        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            // AutoCAD can raise Idle before a drawing document exists.  Keep
            // waiting instead of exhausting the restore retry count then.
            if (Application.DocumentManager.MdiActiveDocument == null)
            {
                return;
            }

            // A burst of early Idle events must not consume all restore
            // attempts before AutoCAD has processed the queued RIBBON command.
            if (DateTime.UtcNow < _nextRibbonRestoreAttemptUtc)
            {
                return;
            }

            _nextRibbonRestoreAttemptUtc = DateTime.UtcNow.AddMilliseconds(250);

            _ribbonRestoreAttempts++;

            if (!EnsureRibbonCreated(false))
            {
                RequestRibbonCommandForRestore();
                return;
            }

            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null || !ribbon.IsVisible)
            {
                RequestRibbonCommandForRestore();
                return;
            }

            ShowRibbonTab();
            SaveRibbonState(true);
            Application.Idle -= OnApplicationIdle;
            _idleHooked = false;
            _ribbonRestoreAttempts = 0;
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

        private static bool EnsureRibbonCreated(bool forceReload)
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                EnsureIdleHook();
                return false;
            }

            ObserveRibbon(ribbon);

            RibbonTab existing = FindRibbonTab(ribbon);
            if (existing != null)
            {
                if (!forceReload)
                {
                    return true;
                }

                ribbon.Tabs.Remove(existing);
            }

            RibbonTab tab = new RibbonTab
            {
                Title = "DUNGX",
                Id = TabId,
                Name = TabId
            };

            foreach (RibbonPanel panel in BuildPanels())
            {
                tab.Panels.Add(panel);
            }

            ribbon.Tabs.Add(tab);
            return true;
        }

        private static void ShowRibbonTab()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                return;
            }

            if (!ribbon.IsVisible)
            {
                RequestRibbonCommand();
                EnsureIdleHook();
                return;
            }

            RibbonTab tab = FindRibbonTab(ribbon);
            if (tab == null)
            {
                return;
            }

            tab.IsVisible = true;
            ribbon.ActiveTab = tab;
            _ribbonCommandSent = false;
            SaveRibbonState(true);
        }

        private static void RequestRibbonCommandForRestore()
        {
            if (_ribbonRestoreAttempts >= MaxRibbonRestoreAttempts)
            {
                Application.Idle -= OnApplicationIdle;
                _idleHooked = false;
                _ribbonRestoreAttempts = 0;
                _nextRibbonRestoreAttemptUtc = DateTime.MinValue;
                return;
            }

            if (_ribbonCommandSent && _ribbonRestoreAttempts % 3 == 0)
            {
                _ribbonCommandSent = false;
            }

            RequestRibbonCommand();
        }

        private static void RequestRibbonCommand()
        {
            if (_ribbonCommandSent)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.SendStringToExecute("_.RIBBON ", true, false, false);
            _ribbonCommandSent = true;
        }

        private static void RequestRibbonCloseCommand()
        {
            if (_ribbonCloseCommandSent)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.SendStringToExecute("_.RIBBONCLOSE ", true, false, false);
            _ribbonCloseCommandSent = true;
        }

        private static void ObserveRibbon(RibbonControl ribbon)
        {
            if (ribbon == null || ReferenceEquals(_observedRibbon, ribbon))
            {
                return;
            }

            UnobserveRibbon();
            _observedRibbon = ribbon;
            _observedRibbon.IsVisibleChanged += Ribbon_IsVisibleChanged;
        }

        private static void UnobserveRibbon()
        {
            if (_observedRibbon == null)
            {
                return;
            }

            _observedRibbon.IsVisibleChanged -= Ribbon_IsVisibleChanged;
            _observedRibbon = null;
        }

        private static void Ribbon_IsVisibleChanged(
            object sender,
            System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (_isTerminating || !(sender is RibbonControl ribbon))
            {
                return;
            }

            if (ribbon.IsVisible)
            {
                _pendingRibbonHide = false;
                _lastKnownRibbonVisible = true;
                _ribbonCommandSent = false;
                SaveRibbonState(true);
                return;
            }

            // Defer a hide by one idle cycle. AutoCAD also hides/destroys the
            // Ribbon during shutdown; that transition must not overwrite the
            // last state chosen by the user.
            _pendingRibbonHide = true;
            EnsureRibbonVisibilityIdleHook();
        }

        private static void EnsureRibbonVisibilityIdleHook()
        {
            if (_ribbonVisibilityIdleHooked)
            {
                return;
            }

            Application.Idle += OnRibbonVisibilityIdle;
            _ribbonVisibilityIdleHooked = true;
        }

        private static void RemoveRibbonVisibilityIdleHook()
        {
            if (!_ribbonVisibilityIdleHooked)
            {
                return;
            }

            Application.Idle -= OnRibbonVisibilityIdle;
            _ribbonVisibilityIdleHooked = false;
        }

        private static void OnRibbonVisibilityIdle(object sender, EventArgs e)
        {
            RemoveRibbonVisibilityIdleHook();

            if (_isTerminating || !_pendingRibbonHide)
            {
                return;
            }

            _pendingRibbonHide = false;
            if (_isTerminating || _observedRibbon == null || _observedRibbon.IsVisible)
            {
                return;
            }

            _lastKnownRibbonVisible = false;
            SaveRibbonState(false);
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
            // Commit a user hide before AutoCAD starts tearing down Ribbon.
            if (_pendingRibbonHide &&
                _observedRibbon != null &&
                !_observedRibbon.IsVisible)
            {
                _pendingRibbonHide = false;
                _lastKnownRibbonVisible = false;
                SaveRibbonState(false);
            }

            _isTerminating = true;
            RemoveRibbonVisibilityIdleHook();
            if (_idleHooked)
            {
                Application.Idle -= OnApplicationIdle;
                _idleHooked = false;
            }
        }

        private static void SaveRibbonState(bool? visibleOverride = null)
        {
            if (visibleOverride.HasValue)
            {
                _lastKnownRibbonVisible = visibleOverride.Value;
                WorkspaceUiStateStore.SaveValues(
                    new Dictionary<string, string>
                    {
                        ["ribbon.visible"] = visibleOverride.Value ? "1" : "0"
                    });
                return;
            }

            RibbonControl ribbon = null;
            try
            {
                ribbon = ComponentManager.Ribbon;
                if (ribbon != null && ribbon.IsVisible)
                {
                    _lastKnownRibbonVisible = true;
                }
                else if (ribbon != null && !_pendingRibbonHide)
                {
                    _lastKnownRibbonVisible = false;
                }
            }
            catch
            {
                // During AutoCAD shutdown the Ribbon object may already be gone.
            }

            if (_ribbonCloseCommandSent)
            {
                _lastKnownRibbonVisible = false;
            }

            WorkspaceUiStateStore.SaveValues(
                new Dictionary<string, string>
                {
                    ["ribbon.visible"] = _lastKnownRibbonVisible ? "1" : "0"
                });
        }

        private static RibbonTab FindRibbonTab(RibbonControl ribbon)
        {
            if (ribbon == null)
            {
                return null;
            }

            return ribbon.Tabs
                .OfType<RibbonTab>()
                .FirstOrDefault(tab => string.Equals(tab.Id, TabId, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<RibbonPanel> BuildPanels()
        {
            List<PaletteCommandItem> builtInItems = PaletteCommandCatalog.BuildItems()
                .Where(item => item.SourceKind == PaletteSourceKind.BuiltInDll)
                .Where(item => !HiddenCommands.Contains(item.CommandName, StringComparer.OrdinalIgnoreCase))
                .OrderBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<PaletteCommandItem> dimensions = PickCommands(builtInItems, DimensionCommands);
            List<PaletteCommandItem> stretches = PickCommands(builtInItems, StretchCommands);
            List<PaletteCommandItem> cutTrims = PickCommands(builtInItems, CutTrimCommands);
            List<PaletteCommandItem> sheetMetal = PickCommands(builtInItems, SheetMetalCommands);
            List<PaletteCommandItem> tools = PickCommands(builtInItems, ToolCommands);
            List<PaletteCommandItem> more = builtInItems
                .Where(item => !KnownCommands.Contains(item.CommandName))
                .OrderBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dimensions.Count > 0)
            {
                yield return CreatePanel("Dimension", "Smart dim and split-dimension tools.", dimensions);
            }

            if (stretches.Count > 0)
            {
                yield return CreatePanel("Stretch", "Native-like smart stretch workflow.", stretches);
            }

            if (cutTrims.Count > 0)
            {
                yield return CreatePanel("Cut / Trim", "Tự động cắt và trim theo đường mốc.", cutTrims);
            }

            if (sheetMetal.Count > 0)
            {
                yield return CreatePanel("Tôn chấn", "Dãn phôi tôn chấn từ polyline biên dạng.", sheetMetal);
            }

            if (tools.Count > 0)
            {
                yield return CreatePanel("Workspace", "Palette and ribbon management.", tools);
            }

            if (more.Count > 0)
            {
                yield return CreatePanel("More", "Other commands discovered from this DLL.", more);
            }

            yield return CreateBrandPanel();
        }

        private static List<PaletteCommandItem> PickCommands(
            IEnumerable<PaletteCommandItem> items,
            IEnumerable<string> orderedNames)
        {
            Dictionary<string, PaletteCommandItem> map = items
                .GroupBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            List<PaletteCommandItem> result = new List<PaletteCommandItem>();
            foreach (string commandName in orderedNames)
            {
                if (map.TryGetValue(commandName, out PaletteCommandItem item))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        // Every button in a panel gets the same Standard size/icon so nothing
        // is arbitrarily "featured", matching AutoCAD's own native panel look
        // instead of one oversized hero button next to small ones.
        private static RibbonPanel CreatePanel(
            string title,
            string description,
            IEnumerable<PaletteCommandItem> items)
        {
            RibbonPanelSource source = new RibbonPanelSource
            {
                Title = title,
                Name = "DUNGX_" + title.ToUpperInvariant(),
                Description = description
            };

            List<PaletteCommandItem> itemList = items.Where(item => item != null).ToList();

            if (itemList.Count > 0)
            {
                RibbonRowPanel row = new RibbonRowPanel
                {
                    Text = title,
                    ShowText = false,
                    IsTopJustified = true
                };

                foreach (PaletteCommandItem item in itemList)
                {
                    row.Items.Add(CreateButton(item));
                }

                source.Items.Add(row);
            }

            return new RibbonPanel
            {
                Source = source
            };
        }

        private static RibbonButton CreateButton(PaletteCommandItem item)
        {
            RibbonCommandStyle style = GetStyle(item.CommandName);
            string description = string.IsNullOrWhiteSpace(style.Description)
                ? item.CommandName
                : style.Description;
            RibbonToolTip toolTip = new RibbonToolTip
            {
                Title = style.Title,
                Content = description,
                Command = item.CommandName
            };

            return new RibbonButton
            {
                Id = "DUNGX_BTN_" + item.CommandName.ToUpperInvariant(),
                Name = item.CommandName,
                Text = style.LargeText,
                ShowText = true,
                ShowImage = true,
                Image = GetIcon(item.CommandName, true),
                LargeImage = GetIcon(item.CommandName, true),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Description = description,
                ToolTip = toolTip,
                CommandHandler = new DungXRibbonCommandHandler(item),
                CommandParameter = item,
                Tag = item,
                KeyTip = style.KeyTip
            };
        }

        private static RibbonCommandStyle GetStyle(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return RibbonCommandStyle.CreateDefault(string.Empty);
            }

            if (RibbonStyles.TryGetValue(commandName, out RibbonCommandStyle style))
            {
                return style;
            }

            return RibbonCommandStyle.CreateDefault(commandName);
        }

        private static Media.ImageSource GetIcon(string commandName, bool large)
        {
            Dictionary<string, Media.ImageSource> cache = large ? LargeImageCache : SmallImageCache;
            if (cache.TryGetValue(commandName, out Media.ImageSource cached))
            {
                return cached;
            }

            RibbonCommandStyle style = GetStyle(commandName);
            Media.ImageSource created = CreateIcon(style, large ? 32 : 16);
            cache[commandName] = created;
            return created;
        }

        // Flat, single-tile icon: one neutral card color for every command
        // (no more per-button rainbow backgrounds) plus a small line-art
        // pictogram in the command's category accent color.
        private static Media.ImageSource CreateIcon(RibbonCommandStyle style, int size)
        {
            using (Bitmap bitmap = new Bitmap(size, size))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                Rectangle bounds = new Rectangle(0, 0, size - 1, size - 1);
                int radius = Math.Max(3, size / 6);

                using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
                using (SolidBrush backgroundBrush = new SolidBrush(RibbonPalette.NeutralTile))
                using (Pen borderPen = new Pen(Color.FromArgb(35, 255, 255, 255), 1f))
                {
                    graphics.FillPath(backgroundBrush, path);
                    graphics.DrawPath(borderPen, path);
                }

                float inset = size * 0.2f;
                RectangleF glyphRect = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
                float strokeWidth = size >= 32 ? 1.9f : 1.3f;
                DrawGlyph(graphics, style.Glyph, glyphRect, style.AccentColor, strokeWidth);

                return ToImageSource(bitmap);
            }
        }

        private static Media.ImageSource ToImageSource(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                Imaging.BitmapImage image = new Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private static Media.ImageSource _brandLogoLarge;
        private static Media.ImageSource _brandLogoSmall;

        private static Media.ImageSource GetBrandLogoIcon(bool large)
        {
            if (large && _brandLogoLarge != null) { return _brandLogoLarge; }
            if (!large && _brandLogoSmall != null) { return _brandLogoSmall; }

            using (Bitmap bitmap = DungXLogo.CreateBitmap(large ? 32 : 16))
            {
                Media.ImageSource created = ToImageSource(bitmap);
                if (large) { _brandLogoLarge = created; } else { _brandLogoSmall = created; }
                return created;
            }
        }

        // Panel thương hiệu ở cuối tab: chỉ logo DungX, bấm vào mở DungX Palette.
        private static RibbonPanel CreateBrandPanel()
        {
            RibbonPanelSource source = new RibbonPanelSource
            {
                Title = "DungX",
                Name = "DUNGX_BRAND",
                Description = "DungX Custom Command Suite"
            };

            RibbonButton brandButton = new RibbonButton
            {
                Id = "DUNGX_BRAND_LOGO",
                Name = "DungX",
                Text = "DungX",
                ShowText = true,
                ShowImage = true,
                Image = GetBrandLogoIcon(false),
                LargeImage = GetBrandLogoIcon(true),
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Description = "Mo DungX Palette - trung tam quan ly lenh tuy chinh.",
                ToolTip = new RibbonToolTip
                {
                    Title = "DungX",
                    Content = "Mo DungX Palette - trung tam quan ly lenh tuy chinh."
                },
                CommandHandler = new RelayRibbonCommand(() => DungXPaletteHost.ShowPalette())
            };

            source.Items.Add(brandButton);

            return new RibbonPanel
            {
                Source = source
            };
        }

        private sealed class RelayRibbonCommand : System.Windows.Input.ICommand
        {
            private readonly Action _action;

            public RelayRibbonCommand(Action action)
            {
                _action = action;
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _action();
        }

        private static void DrawGlyph(Graphics g, IconGlyph glyph, RectangleF r, Color color, float strokeWidth)
        {
            using (Pen pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (SolidBrush brush = new SolidBrush(color))
            {
                float x0 = r.Left, x1 = r.Right, xm = r.Left + r.Width / 2f;
                float y0 = r.Top, y1 = r.Bottom, ym = r.Top + r.Height / 2f;
                float aw = r.Width * 0.16f;   // arrowhead half-width
                float ah = r.Height * 0.16f;  // arrowhead length

                switch (glyph)
                {
                    case IconGlyph.DimAuto:
                        // Extension lines with an inward double-headed dimension arrow.
                        g.DrawLine(pen, x0, y0, x0, y1);
                        g.DrawLine(pen, x1, y0, x1, y1);
                        g.DrawLine(pen, x0, ym, x1, ym);
                        DrawArrowHead(g, brush, new PointF(x0, ym), 1f, aw, ah);
                        DrawArrowHead(g, brush, new PointF(x1, ym), -1f, aw, ah);
                        break;

                    case IconGlyph.Dim4Direction:
                        g.DrawLine(pen, xm, y0, xm, y1);
                        g.DrawLine(pen, x0, ym, x1, ym);
                        DrawArrowHead(g, brush, new PointF(xm, y0), 1f, aw, ah, true);
                        DrawArrowHead(g, brush, new PointF(xm, y1), -1f, aw, ah, true);
                        DrawArrowHead(g, brush, new PointF(x0, ym), 1f, aw, ah);
                        DrawArrowHead(g, brush, new PointF(x1, ym), -1f, aw, ah);
                        break;

                    case IconGlyph.DimXY:
                        // Corner bracket with an arrow along each axis (X/Y).
                        g.DrawLine(pen, x0, y1, x0, y0);
                        g.DrawLine(pen, x0, y1, x1, y1);
                        DrawArrowHead(g, brush, new PointF(x0, y0), -1f, aw, ah, true);
                        DrawArrowHead(g, brush, new PointF(x1, y1), -1f, aw, ah);
                        break;

                    case IconGlyph.MoveDimPosition:
                        // Dimension line plus a 4-way move cross to hint at repositioning.
                        g.DrawLine(pen, x0, y1, x1, y1);
                        DrawArrowHead(g, brush, new PointF(x0, y1), 1f, aw * 0.8f, ah * 0.8f);
                        DrawArrowHead(g, brush, new PointF(x1, y1), -1f, aw * 0.8f, ah * 0.8f);
                        g.DrawLine(pen, xm, y0, xm, y0 + r.Height * 0.55f);
                        g.DrawLine(pen, x0 + r.Width * 0.2f, y0 + r.Height * 0.28f, x1 - r.Width * 0.2f, y0 + r.Height * 0.28f);
                        DrawArrowHead(g, brush, new PointF(xm, y0), 1f, aw * 0.7f, ah * 0.7f, true);
                        break;

                    case IconGlyph.SplitDim:
                        // Dimension line cut by a divider, arrows fanning away from the split.
                        g.DrawLine(pen, x0, ym, x1, ym);
                        g.DrawLine(pen, xm, ym - r.Height * 0.3f, xm, ym + r.Height * 0.3f);
                        DrawArrowHead(g, brush, new PointF(x0, ym), 1f, aw, ah);
                        DrawArrowHead(g, brush, new PointF(x1, ym), -1f, aw, ah);
                        break;

                    case IconGlyph.Stretch:
                    case IconGlyph.StretchByDim:
                    case IconGlyph.StretchByDim2:
                        {
                            RectangleF box = new RectangleF(
                                x0 + r.Width * 0.28f, y0 + r.Height * 0.2f,
                                r.Width * 0.44f, r.Height * 0.6f);
                            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                            g.DrawLine(pen, x0, ym, box.Left, ym);
                            g.DrawLine(pen, box.Right, ym, x1, ym);
                            DrawArrowHead(g, brush, new PointF(x0, ym), 1f, aw, ah);
                            DrawArrowHead(g, brush, new PointF(x1, ym), -1f, aw, ah);

                            int ticks = glyph == IconGlyph.StretchByDim2 ? 2 : (glyph == IconGlyph.StretchByDim ? 1 : 0);
                            if (ticks > 0)
                            {
                                g.DrawLine(pen, x0, y0, x1, y0);
                                if (ticks == 2)
                                {
                                    g.DrawLine(pen, xm, y0 - r.Height * 0.02f, xm, y0 + r.Height * 0.08f);
                                }
                            }
                            break;
                        }

                    case IconGlyph.StretchX:
                        g.DrawLine(pen, x0, ym, x1, ym);
                        DrawArrowHead(g, brush, new PointF(x0, ym), 1f, aw, ah);
                        DrawArrowHead(g, brush, new PointF(x1, ym), -1f, aw, ah);
                        break;

                    case IconGlyph.StretchY:
                        g.DrawLine(pen, xm, y0, xm, y1);
                        DrawArrowHead(g, brush, new PointF(xm, y0), 1f, aw, ah, true);
                        DrawArrowHead(g, brush, new PointF(xm, y1), -1f, aw, ah, true);
                        break;

                    case IconGlyph.Palette:
                        {
                            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                            float divider = r.X + r.Width * 0.34f;
                            g.DrawLine(pen, divider, r.Y, divider, r.Bottom);
                            break;
                        }

                    case IconGlyph.Refresh:
                        {
                            RectangleF oval = new RectangleF(r.X, r.Y, r.Width, r.Height);
                            g.DrawArc(pen, oval, -30, 300);
                            double angle = (-30 + 300) * Math.PI / 180.0;
                            PointF tip = new PointF(
                                (float)(oval.X + oval.Width / 2 + oval.Width / 2 * Math.Cos(angle)),
                                (float)(oval.Y + oval.Height / 2 + oval.Height / 2 * Math.Sin(angle)));
                            double tangent = angle + Math.PI / 2;
                            DrawArrowHeadAt(g, brush, tip, tangent, aw, ah);
                            break;
                        }

                    case IconGlyph.Folder:
                        {
                            float tabWidth = r.Width * 0.45f;
                            float tabHeight = r.Height * 0.14f;
                            using (GraphicsPath folder = new GraphicsPath())
                            {
                                folder.AddLine(r.X, r.Y + tabHeight, r.X + tabWidth * 0.5f, r.Y + tabHeight);
                                folder.AddLine(r.X + tabWidth * 0.5f, r.Y + tabHeight, r.X + tabWidth, r.Y);
                                folder.AddLine(r.X + tabWidth, r.Y, r.Right, r.Y);
                                folder.AddLine(r.Right, r.Y, r.Right, r.Y + tabHeight);
                                g.DrawPath(pen, folder);
                            }
                            g.DrawRectangle(pen, r.X, r.Y + tabHeight, r.Width, r.Height - tabHeight);
                            break;
                        }

                    case IconGlyph.Ribbon:
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height * 0.42f);
                        g.DrawLine(pen, r.X + r.Width * 0.34f, r.Y, r.X + r.Width * 0.34f, r.Y + r.Height * 0.42f);
                        g.DrawLine(pen, r.X + r.Width * 0.67f, r.Y, r.X + r.Width * 0.67f, r.Y + r.Height * 0.42f);
                        g.DrawLine(pen, r.X, r.Bottom, r.Right, r.Bottom);
                        break;

                    case IconGlyph.PointsOnPolyline:
                        {
                            PointF a = new PointF(x0, y1);
                            PointF b = new PointF(xm, y0);
                            PointF c = new PointF(x1, y1);
                            g.DrawLines(pen, new[] { a, b, c });
                            float d = r.Width * 0.12f;
                            foreach (PointF p in new[] { a, b, c })
                            {
                                g.FillEllipse(brush, p.X - d / 2, p.Y - d / 2, d, d);
                            }
                            break;
                        }

                    case IconGlyph.BlockToCenter:
                        {
                            DrawDashedRectangle(g, color, strokeWidth, r);
                            float s = r.Width * 0.3f;
                            g.FillRectangle(brush, xm - s / 2, ym - s / 2, s, s);
                            break;
                        }

                    case IconGlyph.CopyToCenter:
                        {
                            DrawDashedRectangle(g, color, strokeWidth, r);
                            float s = r.Width * 0.26f;
                            RectangleF back = new RectangleF(r.X, r.Y, s, s);
                            RectangleF front = new RectangleF(r.X + s * 0.4f, r.Y + s * 0.4f, s, s);
                            g.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
                            g.DrawRectangle(pen, front.X, front.Y, front.Width, front.Height);
                            float d = r.Width * 0.14f;
                            g.FillEllipse(brush, xm - d / 2, ym - d / 2, d, d);
                            break;
                        }

                    case IconGlyph.NormalizePolyline:
                        {
                            PointF p1 = new PointF(x0, y1);
                            PointF p2 = new PointF(xm, y0);
                            PointF p3 = new PointF(x1, y1);
                            g.DrawPolygon(pen, new[] { p1, p2, p3 });
                            float d = r.Width * 0.16f;
                            g.FillEllipse(brush, p1.X - d / 2, p1.Y - d / 2, d, d);
                            RectangleF arcBox = new RectangleF(xm - r.Width * 0.16f, ym - r.Height * 0.1f, r.Width * 0.32f, r.Height * 0.32f);
                            g.DrawArc(pen, arcBox, 30, 250);
                            DrawArrowHeadAt(g, brush, new PointF(arcBox.Right - arcBox.Width * 0.05f, arcBox.Y + arcBox.Height * 0.15f), (30 + 250) * Math.PI / 180.0 + Math.PI / 2, aw * 0.7f, ah * 0.7f);
                            break;
                        }

                    case IconGlyph.DimAutoPolyline:
                        {
                            // Polyline 2 doan, moi doan co duong dim rieng dat song song phia ngoai.
                            PointF a = new PointF(x0 + r.Width * 0.12f, y1);
                            PointF b = new PointF(xm, y0 + r.Height * 0.32f);
                            PointF c = new PointF(x1 - r.Width * 0.1f, y1 - r.Height * 0.18f);
                            g.DrawLines(pen, new[] { a, b, c });

                            float offset = Math.Min(r.Width, r.Height) * 0.24f;
                            DrawSegmentDim(g, pen, brush, a, b, offset, aw * 0.45f, ah * 0.45f);
                            DrawSegmentDim(g, pen, brush, b, c, offset, aw * 0.45f, ah * 0.45f);
                            break;
                        }

                    case IconGlyph.Calculator:
                        {
                            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                            float displayH = r.Height * 0.22f;
                            g.DrawLine(pen, r.X, r.Y + displayH, r.Right, r.Y + displayH);
                            float gridTop = r.Y + displayH + r.Height * 0.1f;
                            float cellW = r.Width / 3f;
                            float cellH = (r.Bottom - gridTop) / 2f;
                            for (int row = 0; row < 2; row++)
                            {
                                for (int col = 0; col < 3; col++)
                                {
                                    float cx = r.X + cellW * col + cellW / 2;
                                    float cy = gridTop + cellH * row + cellH / 2;
                                    float d = Math.Min(cellW, cellH) * 0.28f;
                                    g.FillEllipse(brush, cx - d / 2, cy - d / 2, d, d);
                                }
                            }
                            break;
                        }

                    case IconGlyph.TextSync:
                        {
                            float blockW = r.Width * 0.32f;
                            DrawTextLines(g, pen, new RectangleF(r.X, r.Y, blockW, r.Height));
                            DrawTextLines(g, pen, new RectangleF(r.Right - blockW, r.Y, blockW, r.Height));
                            PointF arrowStart = new PointF(r.X + blockW + r.Width * 0.06f, ym);
                            PointF arrowEnd = new PointF(r.Right - blockW - r.Width * 0.06f, ym);
                            g.DrawLine(pen, arrowStart, arrowEnd);
                            DrawArrowHead(g, brush, arrowEnd, 1f, aw * 0.8f, ah * 0.8f);
                            break;
                        }

                    case IconGlyph.SettingsGear:
                        {
                            float outerR = r.Width * 0.42f;
                            float innerR = r.Width * 0.2f;
                            g.DrawEllipse(pen, xm - innerR, ym - innerR, innerR * 2, innerR * 2);
                            for (int i = 0; i < 6; i++)
                            {
                                double angle = i * Math.PI / 3.0;
                                PointF p1 = new PointF(xm + (float)(Math.Cos(angle) * outerR * 0.72f), ym + (float)(Math.Sin(angle) * outerR * 0.72f));
                                PointF p2 = new PointF(xm + (float)(Math.Cos(angle) * outerR), ym + (float)(Math.Sin(angle) * outerR));
                                g.DrawLine(pen, p1, p2);
                            }
                            break;
                        }

                    case IconGlyph.UnfilletCorner:
                        {
                            float armLen = r.Width * 0.36f;
                            RectangleF roundBox = new RectangleF(x0, y0, armLen, armLen);
                            g.DrawArc(pen, roundBox.X - armLen * 0.3f, roundBox.Y, armLen, armLen, 180, 90);
                            g.DrawLine(pen, x0, y0 + armLen * 0.5f, x0, y0 + armLen);
                            g.DrawLine(pen, x0 + armLen * 0.5f, y0, x0 + armLen, y0);
                            PointF sharpTop = new PointF(x1 - armLen, y1 - armLen);
                            g.DrawLine(pen, sharpTop, new PointF(x1, sharpTop.Y));
                            g.DrawLine(pen, x1, sharpTop.Y, x1, y1);
                            DrawArrowHead(g, brush, new PointF(xm + r.Width * 0.06f, ym), 1f, aw * 0.7f, ah * 0.7f);
                            break;
                        }

                    case IconGlyph.InsertMarkerSingle:
                        DrawMarkerBlock(g, pen, brush, new RectangleF(x0 + r.Width * 0.12f, y0 + r.Height * 0.1f, r.Width * 0.76f, r.Height * 0.5f), aw, ah);
                        break;

                    case IconGlyph.InsertMarkerSeries:
                        {
                            float mw = r.Width * 0.36f;
                            float mh = r.Height * 0.34f;
                            DrawMarkerBlock(g, pen, brush, new RectangleF(x0, y0 + r.Height * 0.06f, mw, mh), aw * 0.6f, ah * 0.6f);
                            DrawMarkerBlock(g, pen, brush, new RectangleF(xm - mw / 2, y0 + r.Height * 0.18f, mw, mh), aw * 0.6f, ah * 0.6f);
                            DrawMarkerBlock(g, pen, brush, new RectangleF(x1 - mw, y0 + r.Height * 0.06f, mw, mh), aw * 0.6f, ah * 0.6f);
                            break;
                        }

                    case IconGlyph.PointSequence:
                        {
                            PointF p1 = new PointF(x0, y1);
                            PointF p2 = new PointF(xm, y0 + r.Height * 0.15f);
                            PointF p3 = new PointF(x1, ym);
                            using (Pen dashed = new Pen(color, strokeWidth) { DashStyle = DashStyle.Dash })
                            {
                                g.DrawLines(dashed, new[] { p1, p2, p3 });
                            }
                            float d = r.Width * 0.16f;
                            g.FillEllipse(brush, p1.X - d / 2, p1.Y - d / 2, d, d);
                            g.DrawEllipse(pen, p2.X - d / 2, p2.Y - d / 2, d, d);
                            g.DrawEllipse(pen, p3.X - d / 2, p3.Y - d / 2, d, d);
                            break;
                        }

                    case IconGlyph.AutoCut:
                        {
                            // Net ve nam ngang bi dao cat (net dut) cat dut -> chua khe ho tai cho cat,
                            // hai dau cat danh dau bang gach vuong goc cho ro la mat cat.
                            float gap = r.Width * 0.17f;
                            float capH = r.Height * 0.11f;
                            g.DrawLine(pen, x0, ym, xm - gap, ym);
                            g.DrawLine(pen, xm + gap, ym, x1, ym);
                            g.DrawLine(pen, xm - gap, ym - capH, xm - gap, ym + capH);
                            g.DrawLine(pen, xm + gap, ym - capH, xm + gap, ym + capH);

                            using (Pen cutter = CreateCutPen(color, strokeWidth))
                            {
                                g.DrawLine(cutter, x0 + r.Width * 0.22f, y1, x1 - r.Width * 0.22f, y0);
                            }
                            break;
                        }

                    case IconGlyph.TrimOutside:
                        {
                            // Bien PLINE net lien; phan doi tuong NAM NGOAI bien la net dut = se bi cat bo.
                            RectangleF box = CreateTrimBoundary(r);
                            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);

                            float yTop = box.Y + box.Height * 0.3f;
                            float yBottom = box.Bottom - box.Height * 0.3f;
                            g.DrawLine(pen, box.X + box.Width * 0.3f, yTop, box.Right, yTop);
                            g.DrawLine(pen, box.X, yBottom, box.Right - box.Width * 0.3f, yBottom);

                            using (Pen cut = CreateCutPen(color, strokeWidth))
                            {
                                g.DrawLine(cut, box.Right, yTop, x1, yTop);
                                g.DrawLine(cut, x0, yBottom, box.X, yBottom);
                            }
                            break;
                        }

                    case IconGlyph.TrimInside:
                        {
                            // Anh guong cua TrimOutside: phan NAM TRONG bien la net dut = se bi cat bo.
                            RectangleF box = CreateTrimBoundary(r);
                            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);

                            float yTop = box.Y + box.Height * 0.3f;
                            float yBottom = box.Bottom - box.Height * 0.3f;
                            g.DrawLine(pen, box.Right, yTop, x1, yTop);
                            g.DrawLine(pen, x0, yBottom, box.X, yBottom);

                            using (Pen cut = CreateCutPen(color, strokeWidth))
                            {
                                g.DrawLine(cut, box.X + box.Width * 0.3f, yTop, box.Right, yTop);
                                g.DrawLine(cut, box.X, yBottom, box.Right - box.Width * 0.3f, yBottom);
                            }
                            break;
                        }

                    case IconGlyph.SheetMetalUnfold:
                        {
                            // Tiet dien ton da chan, co ban kinh uon (tren) -> mui ten -> dai phoi
                            // phang da dan (duoi).
                            float bendRadius = r.Width * 0.18f;
                            float legY = y0 + r.Height * 0.46f;
                            float cx = x0 + r.Width * 0.54f;
                            float cy = legY - bendRadius;
                            g.DrawLine(pen, x0, legY, cx, legY);
                            g.DrawArc(pen, cx - bendRadius, cy - bendRadius, bendRadius * 2f, bendRadius * 2f, 0f, 90f);
                            g.DrawLine(pen, cx + bendRadius, cy, cx + bendRadius, y0);

                            g.DrawLine(pen, xm, y0 + r.Height * 0.6f, xm, y0 + r.Height * 0.73f);
                            DrawArrowHead(g, brush, new PointF(xm, y0 + r.Height * 0.79f), -1f, aw * 0.7f, ah * 0.7f, true);

                            float blankH = r.Height * 0.13f;
                            g.DrawRectangle(pen, x0, y1 - blankH, r.Width, blankH);
                            break;
                        }

                    default:
                        {
                            float d = Math.Min(r.Width, r.Height) * 0.16f;
                            g.FillEllipse(brush, xm - d / 2, ym - d / 2, d, d);
                            g.FillEllipse(brush, xm - d / 2 - r.Width * 0.28f, ym - d / 2, d, d);
                            g.FillEllipse(brush, xm - d / 2 + r.Width * 0.28f, ym - d / 2, d, d);
                            break;
                        }
                }
            }
        }

        private static void DrawDashedRectangle(Graphics g, Color color, float strokeWidth, RectangleF r)
        {
            using (Pen dashed = new Pen(color, strokeWidth) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(dashed, r.X, r.Y, r.Width, r.Height);
            }
        }

        // Net dut dung chung cho phan "se bi cat bo" va cho duong dao cat. Dash ngan de o
        // co 16px van con doc ra la net dut chu khong dinh lai thanh net lien.
        private static Pen CreateCutPen(Color color, float strokeWidth)
        {
            return new Pen(color, strokeWidth)
            {
                DashStyle = DashStyle.Custom,
                DashPattern = new[] { 1.6f, 1.3f }
            };
        }

        // Khung bien PLINE dung chung cho cap icon Trim Outside / Trim Inside, de hai nut
        // doc ra la mot cap doi xung chu khong phai hai hinh khac nhau.
        private static RectangleF CreateTrimBoundary(RectangleF r)
        {
            return new RectangleF(
                r.X + r.Width * 0.22f,
                r.Y + r.Height * 0.16f,
                r.Width * 0.56f,
                r.Height * 0.68f);
        }

        // Duong dim song song voi doan a->b, dat lech ve phia phap tuyen trai cua huong di,
        // kem hai mui ten va hai duong giong ngan.
        private static void DrawSegmentDim(
            Graphics g, Pen pen, Brush brush, PointF a, PointF b, float offset, float halfWidth, float length)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f)
            {
                return;
            }

            float nx = dy / len * offset;
            float ny = -dx / len * offset;
            PointF a2 = new PointF(a.X + nx, a.Y + ny);
            PointF b2 = new PointF(b.X + nx, b.Y + ny);

            g.DrawLine(pen, a2, b2);
            g.DrawLine(pen, a.X + nx * 0.55f, a.Y + ny * 0.55f, a2.X, a2.Y);
            g.DrawLine(pen, b.X + nx * 0.55f, b.Y + ny * 0.55f, b2.X, b2.Y);

            double tangent = Math.Atan2(dy, dx);
            DrawArrowHeadAt(g, brush, a2, tangent + Math.PI, halfWidth, length);
            DrawArrowHeadAt(g, brush, b2, tangent, halfWidth, length);
        }

        private static void DrawTextLines(Graphics g, Pen pen, RectangleF box)
        {
            float lineH = box.Height / 3f;
            for (int i = 0; i < 3; i++)
            {
                float y = box.Y + lineH * (i + 0.5f);
                float width = i == 2 ? box.Width * 0.6f : box.Width;
                g.DrawLine(pen, box.X, y, box.X + width, y);
            }
        }

        private static void DrawMarkerBlock(Graphics g, Pen pen, Brush brush, RectangleF box, float aw, float ah)
        {
            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            PointF tip = new PointF(box.X + box.Width / 2, box.Bottom + box.Height * 0.5f);
            g.DrawLine(pen, box.X + box.Width * 0.5f, box.Bottom, tip.X, tip.Y);
            float d = Math.Min(box.Width, box.Height) * 0.22f;
            g.FillEllipse(brush, tip.X - d / 2, tip.Y - d / 2, d, d);
        }

        // direction: 1 = arrow points toward +X (or +Y if vertical), -1 = toward -X/-Y.
        private static void DrawArrowHead(Graphics g, Brush brush, PointF tip, float direction, float halfWidth, float length, bool vertical = false)
        {
            PointF p1, p2, p3;
            if (vertical)
            {
                p1 = tip;
                p2 = new PointF(tip.X - halfWidth, tip.Y + direction * length);
                p3 = new PointF(tip.X + halfWidth, tip.Y + direction * length);
            }
            else
            {
                p1 = tip;
                p2 = new PointF(tip.X + direction * length, tip.Y - halfWidth);
                p3 = new PointF(tip.X + direction * length, tip.Y + halfWidth);
            }

            g.FillPolygon(brush, new[] { p1, p2, p3 });
        }

        private static void DrawArrowHeadAt(Graphics g, Brush brush, PointF tip, double tangentAngle, float halfWidth, float length)
        {
            double back = tangentAngle + Math.PI;
            PointF baseCenter = new PointF(
                (float)(tip.X + length * Math.Cos(back)),
                (float)(tip.Y + length * Math.Sin(back)));
            double perp = tangentAngle + Math.PI / 2;
            PointF p2 = new PointF(
                (float)(baseCenter.X + halfWidth * Math.Cos(perp)),
                (float)(baseCenter.Y + halfWidth * Math.Sin(perp)));
            PointF p3 = new PointF(
                (float)(baseCenter.X - halfWidth * Math.Cos(perp)),
                (float)(baseCenter.Y - halfWidth * Math.Sin(perp)));

            g.FillPolygon(brush, new[] { tip, p2, p3 });
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();

            if (diameter > bounds.Width)
            {
                diameter = bounds.Width;
            }

            if (diameter > bounds.Height)
            {
                diameter = bounds.Height;
            }

            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Dictionary<string, RibbonCommandStyle> BuildRibbonStyles()
        {
            Color dim = RibbonPalette.DimensionAccent;
            Color str = RibbonPalette.StretchAccent;
            Color ws = RibbonPalette.WorkspaceAccent;
            Color more = RibbonPalette.MoreAccent;
            Color tile = RibbonPalette.NeutralTile;

            return new Dictionary<string, RibbonCommandStyle>(StringComparer.OrdinalIgnoreCase)
            {
                ["ACC_AUTO_CUT"] = new RibbonCommandStyle(
                    "ACC Auto Cut",
                    "Auto\nCut",
                    "Auto Cut",
                    "ACC",
                    "Tự động cắt đối tượng (Line, Pline, Arc...) theo dao cắt.",
                    "AC",
                    tile, Color.FromArgb(230, 81, 0), IconGlyph.AutoCut),
                ["ACC_TRIM_OUTSIDE"] = new RibbonCommandStyle(
                    "Trim Outside",
                    "Trim\nOutside",
                    "Trim Out",
                    "OUT",
                    "Tự động cắt sạch các hình thò ra ngoài đường mốc PLINE.",
                    "TO",
                    tile, Color.FromArgb(0, 150, 136), IconGlyph.TrimOutside),
                ["ACC_TRIM_INSIDE"] = new RibbonCommandStyle(
                    "Trim Inside",
                    "Trim\nInside",
                    "Trim In",
                    "IN",
                    "Tự động cắt sạch các hình nằm bên trong đường mốc PLINE.",
                    "TI",
                    tile, Color.FromArgb(0, 150, 136), IconGlyph.TrimInside),
                ["DAA_Dim_auto"] = new RibbonCommandStyle(
                    "DAA Auto Dim",
                    "DAA\nAuto",
                    "DAA",
                    "DAA",
                    "Center-based auto dimension workflow.",
                    "DA",
                    tile, dim, IconGlyph.DimAuto),
                ["DDD_Dim_4_direction"] = new RibbonCommandStyle(
                    "Dim 4 Directions",
                    "Dim 4\nDirections",
                    "Dim 4-Dir",
                    "DDD",
                    "Place dimensions along all four directions in one pass.",
                    "DD",
                    tile, dim, IconGlyph.Dim4Direction),
                ["SDXY"] = new RibbonCommandStyle(
                    "Smart Dim XY",
                    "Smart\nDim XY",
                    "Dim XY",
                    "DXY",
                    "Dimension to the nearest object along X or Y based on click direction.",
                    "SX",
                    tile, dim, IconGlyph.DimXY),
                ["CDD2_CHIADIM"] = new RibbonCommandStyle(
                    "Split Dimension",
                    "Split\nDim",
                    "Split",
                    "CD",
                    "Split an existing dimension into multiple segments.",
                    "CD",
                    tile, dim, IconGlyph.SplitDim),
                ["BD"] = new RibbonCommandStyle(
                    "Move Dim Placement",
                    "Move\nDim Pos",
                    "Dim Pos",
                    "BD",
                    "Move the placement point of selected dimensions to a clicked point.",
                    "BD",
                    tile, dim, IconGlyph.MoveDimPosition),
                ["SS"] = new RibbonCommandStyle(
                    "Smart Stretch",
                    "Smart\nStretch",
                    "Stretch",
                    "SS",
                    "Window-based smart stretch with preview.",
                    "SS",
                    tile, str, IconGlyph.Stretch),
                ["SSD"] = new RibbonCommandStyle(
                    "Stretch By Dim",
                    "Stretch\nBy Dim",
                    "By Dim",
                    "SD",
                    "Smart stretch with L derived from two dimensions.",
                    "SB",
                    tile, str, IconGlyph.StretchByDim),
                ["SSD2_SMART_STRETCH_BY_DIM2"] = new RibbonCommandStyle(
                    "Stretch By Dim2",
                    "Stretch\nBy Dim2",
                    "By Dim2",
                    "S2",
                    "Smart stretch with L = |dim1 - dim2| / 2 and two stretch passes.",
                    "S2",
                    tile, str, IconGlyph.StretchByDim2),
                ["SX"] = new RibbonCommandStyle(
                    "Stretch X Symmetric",
                    "Stretch\nX",
                    "Stretch X",
                    "SX",
                    "Stretch two selected groups in opposite X directions using L or calculator value.",
                    "SX",
                    tile, str, IconGlyph.StretchX),
                ["SY"] = new RibbonCommandStyle(
                    "Stretch Y Symmetric",
                    "Stretch\nY",
                    "Stretch Y",
                    "SY",
                    "Stretch two selected groups in opposite Y directions using L or calculator value.",
                    "SY",
                    tile, str, IconGlyph.StretchY),
                ["DXPALETTE"] = new RibbonCommandStyle(
                    "DX Palette",
                    "DX\nPalette",
                    "Palette",
                    "PL",
                    "Open the DungX command palette.",
                    "DP",
                    tile, ws, IconGlyph.Palette),
                ["DXPALETTERELOAD"] = new RibbonCommandStyle(
                    "Refresh Palette",
                    "Refresh",
                    "Refresh",
                    "RF",
                    "Reload palette commands and sources.",
                    "RP",
                    tile, ws, IconGlyph.Refresh),
                ["DXPALETTESETFOLDER"] = new RibbonCommandStyle(
                    "Set Lisp Folder",
                    "Set Lisp\nFolder",
                    "Lisp",
                    "LS",
                    "Choose the root folder for DungX Lisp files.",
                    "LF",
                    tile, ws, IconGlyph.Folder),
                ["DXRIBBONRELOAD"] = new RibbonCommandStyle(
                    "Refresh Ribbon",
                    "Refresh\nRibbon",
                    "Ribbon",
                    "RB",
                    "Reload the DUNGX ribbon layout.",
                    "RR",
                    tile, ws, IconGlyph.Ribbon),

                ["APOINT"] = new RibbonCommandStyle(
                    "Points On Polyline",
                    "Points On\nPolyline",
                    "APoint",
                    "AP",
                    "Place numbered POINT entities at every vertex of a selected polyline.",
                    "AP",
                    tile, more, IconGlyph.PointsOnPolyline),
                ["APOINT2"] = new RibbonCommandStyle(
                    "Points On Polyline 2",
                    "Points On\nPolyline 2",
                    "APoint2",
                    "A2",
                    "Like APOINT but each x/y refers to the earliest matching vertex instead of the previous one.",
                    "A2",
                    tile, more, IconGlyph.PointsOnPolyline),
                ["BBB_BLOCK_TO_CENTER"] = new RibbonCommandStyle(
                    "Block To Center",
                    "Block To\nCenter",
                    "Blk Center",
                    "BC",
                    "Insert a chosen block centered inside each clicked closed region.",
                    "BB",
                    tile, more, IconGlyph.BlockToCenter),
                ["CCC_SMART_COPY_TO_CENTER"] = new RibbonCommandStyle(
                    "Copy To Center",
                    "Copy To\nCenter",
                    "Copy Center",
                    "CC",
                    "Copy the selected objects centered into each clicked closed region.",
                    "CC",
                    tile, more, IconGlyph.CopyToCenter),
                ["CAA_change_pline"] = new RibbonCommandStyle(
                    "Normalize Polyline",
                    "Normalize\nPolyline",
                    "Norm Pline",
                    "CA",
                    "Close, set winding direction, and move the start point of a polyline.",
                    "CA",
                    tile, more, IconGlyph.NormalizePolyline),
                ["DPA_DimAutoPline"] = new RibbonCommandStyle(
                    "Dim Auto Polyline",
                    "Dim Auto\nPolyline",
                    "Dim Pline",
                    "DP",
                    "Automatically place dimensions along every segment of a polyline.",
                    "DP",
                    tile, more, IconGlyph.DimAutoPolyline),
                ["DXCALC"] = new RibbonCommandStyle(
                    "DX Calculator",
                    "DX\nCalculator",
                    "Calculator",
                    "CL",
                    "Open the floating quick calculator tool.",
                    "DC",
                    tile, more, IconGlyph.Calculator),
                ["TT_TEXT_CHANGE_5"] = new RibbonCommandStyle(
                    "Text Sync (H5)",
                    "Text Sync\n(H5)",
                    "Text Sync",
                    "TT",
                    "Copy text content onto other text objects with height 5.",
                    "TT",
                    tile, more, IconGlyph.TextSync),
                ["SDXYSETTINGS"] = new RibbonCommandStyle(
                    "Dim XY Settings",
                    "Dim XY\nSettings",
                    "XY Settings",
                    "ST",
                    "Configure target parameters used by Smart Dim XY.",
                    "XS",
                    tile, more, IconGlyph.SettingsGear),
                ["DX_FOIL"] = new RibbonCommandStyle(
                    "Dãn Phôi Tôn Chấn",
                    "Dãn\nPhôi",
                    "Dãn Phôi",
                    "DP",
                    "Dãn phôi tôn chấn từ polyline biên dạng: tính chiều rộng triển khai, " +
                    "vẽ phôi, đường chấn và hình các bước chấn.",
                    "DF",
                    tile, Color.FromArgb(124, 77, 255), IconGlyph.SheetMetalUnfold),
                ["UFF"] = new RibbonCommandStyle(
                    "Un-Fillet Polyline",
                    "Un-Fillet\nPolyline",
                    "Un-Fillet",
                    "UF",
                    "Strip rounded corners from a polyline and reconnect sharp corners.",
                    "UF",
                    tile, more, IconGlyph.UnfilletCorner),
                ["IPP_INSERT_PG"] = new RibbonCommandStyle(
                    "Insert PG Block",
                    "Insert PG\nBlock",
                    "Insert PG",
                    "PG",
                    "Insert a single PG marker block sized from two picked dimensions.",
                    "IP",
                    tile, more, IconGlyph.InsertMarkerSingle),
                ["IPS_INSERT_PGS"] = new RibbonCommandStyle(
                    "Insert PGS Series",
                    "Insert PGS\nSeries",
                    "Insert PGS",
                    "PS",
                    "Insert a series of PGS marker blocks sized from two picked dimensions.",
                    "IS",
                    tile, more, IconGlyph.InsertMarkerSeries),
                ["VVD"] = new RibbonCommandStyle(
                    "Point Sequence",
                    "Point\nSequence",
                    "Point Seq",
                    "VV",
                    "Place numbered point markers along a clicked sequence and list their coordinates.",
                    "VV",
                    tile, more, IconGlyph.PointSequence),
                ["VE_DIEM"] = new RibbonCommandStyle(
                    "Point Sequence",
                    "Point\nSequence",
                    "Point Seq",
                    "VE",
                    "Place numbered point markers along a clicked sequence and list their coordinates.",
                    "VE",
                    tile, more, IconGlyph.PointSequence)
            };
        }
    }
}
