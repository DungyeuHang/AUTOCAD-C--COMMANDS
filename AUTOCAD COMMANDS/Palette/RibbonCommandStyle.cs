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
    // Pictogram drawn for a ribbon button. Each value maps to a small,
    // purposeful vector glyph in DungXRibbonHost.DrawGlyph rather than a
    // text-badge monogram, so the icon actually hints at what the command does.
    internal enum IconGlyph
    {
        Generic,
        DimAuto,
        Dim4Direction,
        DimXY,
        MoveDimPosition,
        SplitDim,
        Stretch,
        StretchByDim,
        StretchByDim2,
        StretchX,
        StretchY,
        Palette,
        Refresh,
        Folder,
        Ribbon,
        PointsOnPolyline,
        BlockToCenter,
        CopyToCenter,
        NormalizePolyline,
        DimAutoPolyline,
        Calculator,
        TextSync,
        SettingsGear,
        UnfilletCorner,
        InsertMarkerSingle,
        InsertMarkerSeries,
        PointSequence
    }

    internal sealed class RibbonCommandStyle
    {
        public RibbonCommandStyle(
            string title,
            string largeText,
            string smallText,
            string iconText,
            string description,
            string keyTip,
            Color backColor,
            Color accentColor,
            IconGlyph glyph = IconGlyph.Generic)
        {
            Title = title;
            LargeText = largeText;
            SmallText = smallText;
            IconText = iconText;
            Description = description;
            KeyTip = keyTip;
            BackColor = backColor;
            AccentColor = accentColor;
            Glyph = glyph;
        }

        public string Title { get; }

        public string LargeText { get; }

        public string SmallText { get; }

        public string IconText { get; }

        public string Description { get; }

        public string KeyTip { get; }

        public Color BackColor { get; }

        public Color AccentColor { get; }

        public IconGlyph Glyph { get; }

        public static RibbonCommandStyle CreateDefault(string commandName)
        {
            string cleaned = string.IsNullOrWhiteSpace(commandName)
                ? "CMD"
                : commandName.Replace("_", " ").Trim();

            string[] words = cleaned
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string title = words.Length == 0 ? "Command" : string.Join(" ", words);
            string smallText = words.Length >= 2 ? words[0] : title;
            string largeText = words.Length >= 2
                ? words[0] + "\n" + words[1]
                : title;
            string icon = words.Length >= 2
                ? (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant()
                : title.Substring(0, Math.Min(2, title.Length)).ToUpperInvariant();

            return new RibbonCommandStyle(
                title,
                largeText,
                smallText,
                icon,
                title,
                icon,
                RibbonPalette.NeutralTile,
                RibbonPalette.NeutralAccent,
                IconGlyph.Generic);
        }
    }

    // Shared, minimal color system for the DUNGX ribbon: one flat neutral
    // tile behind every icon plus a small set of category accents, instead
    // of a different saturated background per command.
    internal static class RibbonPalette
    {
        public static readonly Color NeutralTile = Color.FromArgb(255, 45, 48, 54);
        public static readonly Color NeutralAccent = Color.FromArgb(255, 175, 180, 190);
        public static readonly Color DimensionAccent = Color.FromArgb(255, 96, 165, 255);
        public static readonly Color StretchAccent = Color.FromArgb(255, 255, 152, 72);
        public static readonly Color WorkspaceAccent = Color.FromArgb(255, 110, 210, 170);
        public static readonly Color MoreAccent = Color.FromArgb(255, 190, 195, 205);
    }
}
