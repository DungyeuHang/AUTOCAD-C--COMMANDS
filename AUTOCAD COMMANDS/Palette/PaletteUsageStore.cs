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

    // Lưu số lần dùng command để giữ thống kê qua các lần mở AutoCAD.
    internal static class PaletteUsageStore
    {
        private static readonly string UsageFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_usage.tsv");

        public static void ApplyUsage(IEnumerable<PaletteCommandItem> items)
        {
            Dictionary<string, int> usageMap = Load();
            foreach (PaletteCommandItem item in items ?? Enumerable.Empty<PaletteCommandItem>())
            {
                item.UsageCount = usageMap.TryGetValue(item.CommandName, out int count)
                    ? count
                    : 0;
            }
        }

        public static int Increment(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return 0;
            }

            Dictionary<string, int> usageMap = Load();
            usageMap[commandName] = usageMap.TryGetValue(commandName, out int count)
                ? count + 1
                : 1;
            SaveAll(usageMap);
            return usageMap[commandName];
        }

        public static void Reset()
        {
            if (File.Exists(UsageFilePath))
            {
                File.Delete(UsageFilePath);
            }
        }

        private static Dictionary<string, int> Load()
        {
            Dictionary<string, int> map =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(UsageFilePath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(UsageFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length < 2)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(commandName) ||
                    !int.TryParse(parts[1].Trim(), out int usageCount))
                {
                    continue;
                }

                map[commandName] = Math.Max(0, usageCount);
            }

            return map;
        }

        private static void SaveAll(Dictionary<string, int> usageMap)
        {
            List<string> lines = usageMap
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + Math.Max(0, kvp.Value).ToString(CultureInfo.InvariantCulture))
                .ToList();

            File.WriteAllLines(UsageFilePath, lines, Encoding.UTF8);
        }
    }
}
