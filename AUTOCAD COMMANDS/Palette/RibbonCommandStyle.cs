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
            Color accentColor)
        {
            Title = title;
            LargeText = largeText;
            SmallText = smallText;
            IconText = iconText;
            Description = description;
            KeyTip = keyTip;
            BackColor = backColor;
            AccentColor = accentColor;
        }

        public string Title { get; }

        public string LargeText { get; }

        public string SmallText { get; }

        public string IconText { get; }

        public string Description { get; }

        public string KeyTip { get; }

        public Color BackColor { get; }

        public Color AccentColor { get; }

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
                Color.FromArgb(58, 62, 70),
                Color.FromArgb(120, 170, 255));
        }
    }
}
