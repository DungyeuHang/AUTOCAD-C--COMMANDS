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

    // Lưu trạng thái Auto Open của DXPALETTE.
    internal static class PaletteStartupStore
    {
        private static readonly string AutoShowFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_autoshow.txt");

        public static bool LoadAutoShow()
        {
            try
            {
                if (!File.Exists(AutoShowFilePath))
                {
                    // Auto-open is enabled by default for a new installation.
                    // An explicit "0" saved by the user still disables it.
                    return true;
                }

                string text = (File.ReadAllText(AutoShowFilePath, Encoding.UTF8) ?? string.Empty).Trim();
                return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void SaveAutoShow(bool enabled)
        {
            File.WriteAllText(AutoShowFilePath, enabled ? "1" : "0", Encoding.UTF8);
        }
    }
}
