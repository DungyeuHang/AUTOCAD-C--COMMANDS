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

    // Lưu giá trị L gần nhất của SS để Enter lần sau dùng lại nhanh.
    internal static class SmartStretchSettingsStore
    {
        private static readonly string LengthFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_smart_stretch_length.txt");

        public static double LoadLength()
        {
            try
            {
                if (!File.Exists(LengthFilePath))
                {
                    return 100.0;
                }

                string text = (File.ReadAllText(LengthFilePath, Encoding.UTF8) ?? string.Empty).Trim();
                if (double.TryParse(
                    text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out double value) &&
                    value > 0.0)
                {
                    return value;
                }
            }
            catch
            {
            }

            return 100.0;
        }

        public static void SaveLength(double value)
        {
            if (value <= 0.0)
            {
                return;
            }

            File.WriteAllText(
                LengthFilePath,
                value.ToString("0.###", CultureInfo.InvariantCulture),
                Encoding.UTF8);
        }
    }
}
