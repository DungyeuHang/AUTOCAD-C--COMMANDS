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
    // LISP RESOLVER
    // Quản lý thư mục LISP và tìm các file LISP cần load/chạy từ DXPALETTE.
    // ======================================================
    internal static class DungXLispResolver
    {
        private static readonly string[] RequiredLispFiles =
        {
            "DUNGX Custom Command.LSP",
            "DUNGX 2.LSP"
        };

        private static readonly string ConfigFilePath =
            Path.Combine(GetAssemblyDirectory(), "dungx_lisp_root.txt");

        public static string GetDisplayRoot()
        {
            return GetCurrentRoot() ?? "<chua set>";
        }

        public static bool TryEnsureAllLispFiles(bool showPrompt, out List<string> missing)
        {
            if (TryResolveAllLispFiles(out _, out missing))
            {
                return true;
            }

            if (!showPrompt)
            {
                return false;
            }

            bool selected = PickLispRoot(true);
            if (!selected)
            {
                return false;
            }

            return TryResolveAllLispFiles(out _, out missing);
        }

        public static bool TryResolveAllLispFiles(out List<string> paths, out List<string> missing)
        {
            paths = new List<string>();
            missing = new List<string>();

            string root = GetCurrentRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                missing.AddRange(RequiredLispFiles);
                return false;
            }

            foreach (string fileName in RequiredLispFiles)
            {
                string fullPath = Path.Combine(root, fileName);
                if (File.Exists(fullPath))
                {
                    paths.Add(fullPath);
                }
                else
                {
                    missing.Add(fullPath);
                }
            }

            return missing.Count == 0;
        }

        public static IReadOnlyList<string> GetResolvedLispFiles()
        {
            if (TryResolveAllLispFiles(out List<string> paths, out _))
            {
                return paths;
            }

            return new List<string>();
        }

        public static bool PickLispRoot(bool showMessage)
        {
            using (WF.FolderBrowserDialog dialog = new WF.FolderBrowserDialog())
            {
                dialog.Description = "Chon thu muc chua DUNGX Custom Command.LSP va DUNGX 2.LSP";
                dialog.SelectedPath = GetCurrentRoot() ?? GetAssemblyDirectory();

                if (dialog.ShowDialog() != WF.DialogResult.OK)
                {
                    return false;
                }

                File.WriteAllText(ConfigFilePath, dialog.SelectedPath);

                if (showMessage)
                {
                    WF.MessageBox.Show(
                        "Da luu thu muc LISP:\n" + dialog.SelectedPath,
                        "DungX Palette",
                        WF.MessageBoxButtons.OK,
                        WF.MessageBoxIcon.Information);
                }

                return true;
            }
        }

        private static string GetCurrentRoot()
        {
            string[] candidates =
            {
                TryReadConfigFile(),
                GetAssemblyDirectory(),
                Path.Combine(GetAssemblyDirectory(), "LISP"),
                Path.GetDirectoryName(GetAssemblyDirectory())
            };

            foreach (string candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                bool hasAllFiles = RequiredLispFiles.All(file => File.Exists(Path.Combine(candidate, file)));
                if (hasAllFiles)
                {
                    return candidate;
                }
            }

            return TryReadConfigFile() ?? GetAssemblyDirectory();
        }

        private static string TryReadConfigFile()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return null;
            }

            string path = File.ReadAllText(ConfigFilePath).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        private static string GetAssemblyDirectory()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(assemblyPath) ?? string.Empty;
        }
    }
}
