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

    internal static class SdxyNamedFilterStore
    {
        private static readonly string FilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "sdxy_named_filters.tsv");

        private static readonly string CurrentNameFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "sdxy_named_filter_current.txt");

        public static Dictionary<string, SdxyTargetSettings> LoadAll()
        {
            Dictionary<string, SdxyTargetSettings> result =
                new Dictionary<string, SdxyTargetSettings>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(FilePath))
            {
                return result;
            }

            try
            {
                string currentName = null;
                List<string> blockLines = new List<string>();
                foreach (string rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (rawLine.StartsWith("[Filter]\t", StringComparison.Ordinal))
                    {
                        SaveCurrentBlock(result, currentName, blockLines);
                        currentName = NormalizeName(rawLine.Substring("[Filter]\t".Length));
                        blockLines = new List<string>();
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        SaveCurrentBlock(result, currentName, blockLines);
                        currentName = null;
                        blockLines = new List<string>();
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(currentName))
                    {
                        blockLines.Add(rawLine);
                    }
                }

                SaveCurrentBlock(result, currentName, blockLines);
            }
            catch
            {
                return new Dictionary<string, SdxyTargetSettings>(StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }

        public static void SaveAll(IReadOnlyDictionary<string, SdxyTargetSettings> namedFilters)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, SdxyTargetSettings> pair in (namedFilters ?? new Dictionary<string, SdxyTargetSettings>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name = NormalizeName(pair.Key);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                lines.Add("[Filter]\t" + name);
                lines.AddRange(SdxyTargetSettingsStore.BuildLines(pair.Value));
                lines.Add(string.Empty);
            }

            File.WriteAllLines(FilePath, lines, Encoding.UTF8);
        }

        public static string LoadCurrentName()
        {
            try
            {
                if (!File.Exists(CurrentNameFilePath))
                {
                    return string.Empty;
                }

                return NormalizeName(File.ReadAllText(CurrentNameFilePath, Encoding.UTF8));
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void SaveCurrentName(string name)
        {
            try
            {
                string normalized = NormalizeName(name);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    if (File.Exists(CurrentNameFilePath))
                    {
                        File.Delete(CurrentNameFilePath);
                    }

                    return;
                }

                File.WriteAllText(CurrentNameFilePath, normalized, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void SaveCurrentBlock(
            Dictionary<string, SdxyTargetSettings> result,
            string currentName,
            List<string> blockLines)
        {
            if (string.IsNullOrWhiteSpace(currentName))
            {
                return;
            }

            result[currentName] = SdxyTargetSettingsStore.ParseLines(blockLines ?? Enumerable.Empty<string>());
        }

        private static string NormalizeName(string name)
        {
            string normalized = (name ?? string.Empty).Trim();
            normalized = normalized.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized;
        }
    }
}
