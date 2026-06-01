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

    // Lưu mô tả command người dùng nhập trong DXPALETTE.
    internal static class PaletteDescriptionStore
    {
        private static readonly string DescriptionFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_descriptions.tsv");

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(DescriptionFilePath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(DescriptionFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length == 0)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                string description = parts.Length > 1 ? parts[1] : string.Empty;
                if (!string.IsNullOrWhiteSpace(commandName))
                {
                    map[commandName] = description;
                }
            }

            return map;
        }

        public static void SaveDescription(string commandName, string description)
        {
            Dictionary<string, string> map = Load();
            map[commandName] = description ?? string.Empty;

            List<string> lines = map
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + (kvp.Value ?? string.Empty))
                .ToList();

            File.WriteAllLines(DescriptionFilePath, lines, Encoding.UTF8);
        }
    }
}
