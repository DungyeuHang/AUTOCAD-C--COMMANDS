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

    // Lưu layout DXPALETTE: favorite, thứ tự custom, sort mode, width cột.
    internal static class PaletteLayoutStore
    {
        private static readonly string LayoutFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_layout.tsv");

        private static readonly string SortModeFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_sort.txt");

        private static readonly string ColumnWidthsFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_columns.tsv");

        public static void ApplyLayout(List<PaletteCommandItem> items)
        {
            Dictionary<string, Tuple<bool, int>> saved = LoadLayout();
            int nextOrder = saved.Count == 0 ? 0 : saved.Max(kvp => kvp.Value.Item2) + 1;

            foreach (PaletteCommandItem item in items.OrderBy(
                current => current.CommandName,
                StringComparer.OrdinalIgnoreCase))
            {
                if (saved.TryGetValue(item.CommandName, out Tuple<bool, int> state))
                {
                    item.IsFavorite = state.Item1;
                    item.ManualOrder = state.Item2;
                }
                else
                {
                    item.IsFavorite = false;
                    item.ManualOrder = nextOrder++;
                }
            }

            NormalizeManualOrder(items);
        }

        public static void SaveLayout(IEnumerable<PaletteCommandItem> items)
        {
            List<PaletteCommandItem> ordered = items
                .OrderBy(item => item.ManualOrder)
                .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            NormalizeManualOrder(ordered);

            List<string> lines = ordered
                .Select(item =>
                    item.CommandName + "\t" +
                    (item.IsFavorite ? "1" : "0") + "\t" +
                    item.ManualOrder.ToString())
                .ToList();

            File.WriteAllLines(LayoutFilePath, lines, Encoding.UTF8);
        }

        public static PaletteSortMode LoadSortMode()
        {
            if (!File.Exists(SortModeFilePath))
            {
                return PaletteSortMode.Custom;
            }

            string mode = (File.ReadAllText(SortModeFilePath, Encoding.UTF8) ?? string.Empty).Trim();
            if (string.Equals(mode, "A-Z", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Alphabetical;
            }

            if (string.Equals(mode, "Used", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Used;
            }

            return PaletteSortMode.Custom;
        }

        public static void SaveSortMode(PaletteSortMode mode)
        {
            string value =
                mode == PaletteSortMode.Alphabetical
                    ? "A-Z"
                    : mode == PaletteSortMode.Used
                        ? "Used"
                        : "Custom";
            File.WriteAllText(SortModeFilePath, value, Encoding.UTF8);
        }

        public static Dictionary<string, int> LoadColumnWidths()
        {
            Dictionary<string, int> widths =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(ColumnWidthsFilePath))
            {
                return widths;
            }

            foreach (string rawLine in File.ReadAllLines(ColumnWidthsFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string[] parts = rawLine.Split('\t');
                if (parts.Length < 2)
                {
                    continue;
                }

                string columnName = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(columnName) ||
                    !int.TryParse(parts[1].Trim(), out int width) ||
                    width <= 0)
                {
                    continue;
                }

                widths[columnName] = width;
            }

            return widths;
        }

        public static void SaveColumnWidths(IReadOnlyDictionary<string, int> widths)
        {
            if (widths == null || widths.Count == 0)
            {
                return;
            }

            List<string> lines = widths
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > 0)
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + kvp.Value.ToString())
                .ToList();

            if (lines.Count == 0)
            {
                return;
            }

            File.WriteAllLines(ColumnWidthsFilePath, lines, Encoding.UTF8);
        }

        private static Dictionary<string, Tuple<bool, int>> LoadLayout()
        {
            Dictionary<string, Tuple<bool, int>> map =
                new Dictionary<string, Tuple<bool, int>>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(LayoutFilePath))
            {
                return map;
            }

            foreach (string rawLine in File.ReadAllLines(LayoutFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string[] parts = rawLine.Split('\t');
                if (parts.Length < 3)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                bool isFavorite = string.Equals(parts[1].Trim(), "1", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(commandName) ||
                    !int.TryParse(parts[2].Trim(), out int manualOrder))
                {
                    continue;
                }

                map[commandName] = Tuple.Create(isFavorite, manualOrder);
            }

            return map;
        }

        private static void NormalizeManualOrder(IEnumerable<PaletteCommandItem> items)
        {
            int index = 0;
            foreach (PaletteCommandItem item in items
                .OrderBy(current => current.ManualOrder)
                .ThenBy(current => current.CommandName, StringComparer.OrdinalIgnoreCase))
            {
                item.ManualOrder = index++;
            }
        }
    }
}
