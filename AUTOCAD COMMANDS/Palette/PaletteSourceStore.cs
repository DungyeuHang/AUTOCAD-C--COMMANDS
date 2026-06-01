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

    // Lưu danh sách source ngoài do người dùng thêm vào DXPALETTE.
    internal static class PaletteSourceStore
    {
        private static readonly string SourceFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_sources.tsv");

        public static List<PaletteSourceFile> LoadSources()
        {
            List<PaletteSourceFile> result = new List<PaletteSourceFile>();
            if (!File.Exists(SourceFilePath))
            {
                return result;
            }

            foreach (string rawLine in File.ReadAllLines(SourceFilePath, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string filePath = line.Split('\t')[0].Trim();
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                if (!TryCreateSource(filePath, out PaletteSourceFile source))
                {
                    continue;
                }

                result.Add(source);
            }

            return result
                .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public static int AddSources(IEnumerable<string> filePaths)
        {
            List<string> existing = LoadRawPaths();
            int added = 0;

            foreach (string filePath in filePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string normalized = Path.GetFullPath(filePath);
                if (existing.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!TryCreateSource(normalized, out _))
                {
                    continue;
                }

                existing.Add(normalized);
                added++;
            }

            SaveRawPaths(existing);
            return added;
        }

        public static void RemoveSource(string filePath)
        {
            List<string> existing = LoadRawPaths()
                .Where(path => !string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveRawPaths(existing);
        }

        public static bool Contains(string filePath)
        {
            return LoadRawPaths().Any(
                path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryCreateSource(string filePath, out PaletteSourceFile source)
        {
            source = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            string displayName = Path.GetFileName(filePath);

            switch (extension)
            {
                case ".lsp":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.Lisp);
                    return true;
                case ".vlx":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.Vlx);
                    return true;
                case ".dll":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.ManagedDll);
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> LoadRawPaths()
        {
            if (!File.Exists(SourceFilePath))
            {
                return new List<string>();
            }

            return File.ReadAllLines(SourceFilePath, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void SaveRawPaths(IEnumerable<string> filePaths)
        {
            List<string> lines = filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            File.WriteAllLines(SourceFilePath, lines, Encoding.UTF8);
        }
    }
}
