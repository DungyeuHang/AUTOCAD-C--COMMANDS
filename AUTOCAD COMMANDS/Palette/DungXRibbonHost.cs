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
    // RIBBON DUNGX
    // Tạo tab/panel/nút ribbon từ danh sách command trong project.
    // Nếu đổi tên CommandMethod thủ công, nhớ cập nhật các mảng command dưới đây nếu muốn Ribbon chạy đúng.
    // ======================================================
    internal static class DungXRibbonHost
    {
        private const string TabId = "DUNGX_RIBBON_TAB";
        // Các command hiện lên panel Dimension.
        private static readonly string[] DimensionCommands =
            { "DAA_Dim_auto", "DDD_Dim_4_direction", "SDXY", "BD_CHANGE_POSITION_DIM", "CDD2_CHIADIM" };

        // Các command hiện lên panel Stretch.
        private static readonly string[] StretchCommands =
            { "SS", "SSD", "SSD2_SMART_STRETCH_BY_DIM2", "SX", "SY" };

        // Các command tiện ích workspace/palette/ribbon.
        private static readonly string[] ToolCommands =
            { "DXPALETTE", "DXPALETTERELOAD", "DXPALETTESETFOLDER", "DXRIBBONRELOAD" };

        private static readonly string[] HiddenCommands =
            { "DXRIBBON" };

        private static readonly HashSet<string> KnownCommands =
            new HashSet<string>(
                DimensionCommands
                    .Concat(StretchCommands)
                    .Concat(ToolCommands)
                    .Concat(HiddenCommands),
                StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, RibbonCommandStyle> RibbonStyles =
            BuildRibbonStyles();
        private static readonly Dictionary<string, Media.ImageSource> LargeImageCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Media.ImageSource> SmallImageCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static bool _idleHooked;

        public static void Initialize()
        {
            EnsureRibbonCreated(false);
        }

        public static void Terminate()
        {
            if (_idleHooked)
            {
                Application.Idle -= OnApplicationIdle;
                _idleHooked = false;
            }
        }

        public static void ShowRibbon()
        {
            EnsureRibbonCreated(false);

            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                return;
            }

            RibbonTab tab = FindRibbonTab(ribbon);
            if (tab == null)
            {
                return;
            }

            tab.IsVisible = true;
            ribbon.ActiveTab = tab;
        }

        public static void ReloadRibbon(bool showMessage)
        {
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
            if (!EnsureRibbonCreated(false))
            {
                return;
            }

            Application.Idle -= OnApplicationIdle;
            _idleHooked = false;
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
            List<PaletteCommandItem> tools = PickCommands(builtInItems, ToolCommands);
            List<PaletteCommandItem> more = builtInItems
                .Where(item => !KnownCommands.Contains(item.CommandName))
                .OrderBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dimensions.Count > 0)
            {
                yield return CreatePanel(
                    "Dimension",
                    "Smart dim and split-dimension tools.",
                    dimensions.First(),
                    dimensions.Skip(1));
            }

            if (stretches.Count > 0)
            {
                yield return CreatePanel(
                    "Stretch",
                    "Native-like smart stretch workflow.",
                    stretches.First(),
                    stretches.Skip(1));
            }

            if (tools.Count > 0)
            {
                yield return CreatePanel(
                    "Workspace",
                    "Palette and ribbon management.",
                    tools.First(),
                    tools.Skip(1));
            }

            if (more.Count > 0)
            {
                yield return CreatePanel(
                    "More",
                    "Other commands discovered from this DLL.",
                    more.First(),
                    more.Skip(1));
            }
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

        private static RibbonPanel CreatePanel(
            string title,
            string description,
            PaletteCommandItem featuredItem,
            IEnumerable<PaletteCommandItem> secondaryItems)
        {
            RibbonPanelSource source = new RibbonPanelSource
            {
                Title = title,
                Name = "DUNGX_" + title.ToUpperInvariant(),
                Description = description
            };

            if (featuredItem != null)
            {
                source.Items.Add(CreateButton(featuredItem, true));
            }

            List<PaletteCommandItem> secondaryList = secondaryItems
                .Where(item => item != null)
                .ToList();

            if (secondaryList.Count > 0)
            {
                RibbonRowPanel row = new RibbonRowPanel
                {
                    Text = title + " Quick",
                    ShowText = false,
                    IsTopJustified = true
                };

                foreach (PaletteCommandItem item in secondaryList)
                {
                    row.Items.Add(CreateButton(item, false));
                }

                source.Items.Add(row);
            }

            return new RibbonPanel
            {
                Source = source
            };
        }

        private static RibbonButton CreateButton(PaletteCommandItem item, bool large)
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
                Text = large ? style.LargeText : style.SmallText,
                ShowText = true,
                ShowImage = true,
                Image = GetIcon(item.CommandName, false),
                LargeImage = GetIcon(item.CommandName, true),
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
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

        private static Media.ImageSource CreateIcon(RibbonCommandStyle style, int size)
        {
            using (Bitmap bitmap = new Bitmap(size, size))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                Rectangle bounds = new Rectangle(0, 0, size - 1, size - 1);
                int radius = Math.Max(4, size / 5);

                using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
                using (SolidBrush backgroundBrush = new SolidBrush(style.BackColor))
                using (SolidBrush accentBrush = new SolidBrush(style.AccentColor))
                using (Pen borderPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                {
                    graphics.FillPath(backgroundBrush, path);

                    Rectangle accentRect = new Rectangle(0, 0, size, Math.Max(3, size / 5));
                    graphics.FillRectangle(accentBrush, accentRect);
                    graphics.DrawPath(borderPen, path);
                }

                float fontSize = size >= 32 ? 12f : 7f;
                FontStyle fontStyle = style.IconText.Length >= 3 ? FontStyle.Bold : FontStyle.Regular;
                using (System.Drawing.Font font = new System.Drawing.Font(
                    "Segoe UI",
                    fontSize,
                    fontStyle,
                    GraphicsUnit.Pixel))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                using (StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    RectangleF textRect = new RectangleF(1, size * 0.18f, size - 2, size * 0.72f);
                    graphics.DrawString(style.IconText, font, textBrush, textRect, format);
                }

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
            return new Dictionary<string, RibbonCommandStyle>(StringComparer.OrdinalIgnoreCase)
            {
                ["DAA_Dim_auto"] = new RibbonCommandStyle(
                    "DAA Auto Dim",
                    "DAA\nAuto",
                    "DAA",
                    "DAA",
                    "Center-based auto dimension workflow.",
                    "DA",
                    Color.FromArgb(33, 45, 74),
                    Color.FromArgb(72, 140, 255)),
                ["SDXY"] = new RibbonCommandStyle(
                    "Smart Dim XY",
                    "Smart\nDim XY",
                    "Dim XY",
                    "DXY",
                    "Dimension to the nearest object along X or Y based on click direction.",
                    "SX",
                    Color.FromArgb(25, 67, 75),
                    Color.FromArgb(0, 196, 176)),
                ["CDD2_CHIADIM"] = new RibbonCommandStyle(
                    "Split Dimension",
                    "Split\nDim",
                    "Split",
                    "CD",
                    "Split an existing dimension into multiple segments.",
                    "CD",
                    Color.FromArgb(63, 45, 86),
                    Color.FromArgb(176, 112, 255)),
                ["BD_CHANGE_POSITION_DIM"] = new RibbonCommandStyle(
                    "Move Dim Placement",
                    "Move\nDim Pos",
                    "Dim Pos",
                    "BD",
                    "Move the placement point of selected dimensions to a clicked point.",
                    "BD",
                    Color.FromArgb(48, 62, 82),
                    Color.FromArgb(116, 172, 255)),
                ["SS"] = new RibbonCommandStyle(
                    "Smart Stretch",
                    "Smart\nStretch",
                    "Stretch",
                    "SS",
                    "Window-based smart stretch with preview.",
                    "SS",
                    Color.FromArgb(90, 48, 32),
                    Color.FromArgb(255, 144, 64)),
                ["SSD"] = new RibbonCommandStyle(
                    "Stretch By Dim",
                    "Stretch\nBy Dim",
                    "By Dim",
                    "SD",
                    "Smart stretch with L derived from two dimensions.",
                    "SB",
                    Color.FromArgb(96, 58, 28),
                    Color.FromArgb(255, 172, 82)),
                ["SSD2_SMART_STRETCH_BY_DIM2"] = new RibbonCommandStyle(
                    "Stretch By Dim2",
                    "Stretch\nBy Dim2",
                    "By Dim2",
                    "S2",
                    "Smart stretch with L = |dim1 - dim2| / 2 and two stretch passes.",
                    "S2",
                    Color.FromArgb(110, 70, 34),
                    Color.FromArgb(255, 196, 106)),
                ["SX"] = new RibbonCommandStyle(
                    "Stretch X Symmetric",
                    "Stretch\nX",
                    "Stretch X",
                    "SX",
                    "Stretch two selected groups in opposite X directions using L or calculator value.",
                    "SX",
                    Color.FromArgb(79, 55, 37),
                    Color.FromArgb(255, 154, 76)),
                ["SY"] = new RibbonCommandStyle(
                    "Stretch Y Symmetric",
                    "Stretch\nY",
                    "Stretch Y",
                    "SY",
                    "Stretch two selected groups in opposite Y directions using L or calculator value.",
                    "SY",
                    Color.FromArgb(70, 55, 45),
                    Color.FromArgb(255, 180, 92)),
                ["DXPALETTE"] = new RibbonCommandStyle(
                    "DX Palette",
                    "DX\nPalette",
                    "Palette",
                    "PL",
                    "Open the DungX command palette.",
                    "DP",
                    Color.FromArgb(46, 62, 49),
                    Color.FromArgb(110, 201, 124)),
                ["DXPALETTERELOAD"] = new RibbonCommandStyle(
                    "Refresh Palette",
                    "Refresh",
                    "Refresh",
                    "RF",
                    "Reload palette commands and sources.",
                    "RP",
                    Color.FromArgb(52, 52, 57),
                    Color.FromArgb(173, 181, 189)),
                ["DXPALETTESETFOLDER"] = new RibbonCommandStyle(
                    "Set Lisp Folder",
                    "Set Lisp\nFolder",
                    "Lisp",
                    "LS",
                    "Choose the root folder for DungX Lisp files.",
                    "LF",
                    Color.FromArgb(55, 58, 41),
                    Color.FromArgb(209, 174, 79)),
                ["DXRIBBONRELOAD"] = new RibbonCommandStyle(
                    "Refresh Ribbon",
                    "Refresh\nRibbon",
                    "Ribbon",
                    "RB",
                    "Reload the DUNGX ribbon layout.",
                    "RR",
                    Color.FromArgb(53, 46, 58),
                    Color.FromArgb(220, 120, 255))
            };
        }
    }
}
