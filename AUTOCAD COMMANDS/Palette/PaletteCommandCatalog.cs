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

    // Tập hợp command từ nhiều nguồn: DLL hiện tại, LISP, VLX, Action Macro, manual alias.
    internal static class PaletteCommandCatalog
    {
        private static readonly Regex LispCommandRegex =
            new Regex(@"\(\s*defun\s+[cC]:(?<name>[^\s\(/]+)", RegexOptions.Compiled);
        private static readonly Regex BinaryCommandRegex =
            new Regex(@"(?i)(?:\(\s*defun\s+c:|c:)(?<name>[a-z0-9_\-$]+)", RegexOptions.Compiled);

        public static List<PaletteCommandItem> BuildItems()
        {
            Dictionary<string, string> savedDescriptions = PaletteDescriptionStore.Load();
            List<PaletteCommandItem> result = new List<PaletteCommandItem>();
            Dictionary<string, PaletteCommandItem> unique =
                new Dictionary<string, PaletteCommandItem>(StringComparer.OrdinalIgnoreCase);

            foreach (PaletteCommandItem item in ParseManagedDll(
                Assembly.GetExecutingAssembly(),
                "This DLL",
                Assembly.GetExecutingAssembly().Location,
                PaletteSourceKind.BuiltInDll))
            {
                AddOrReplace(result, unique, item, savedDescriptions);
            }

            if (DungXLispResolver.TryResolveAllLispFiles(out List<string> coreLispFiles, out _))
            {
                foreach (string filePath in coreLispFiles)
                {
                    string sourceLabel = filePath.EndsWith("DUNGX 2.LSP", StringComparison.OrdinalIgnoreCase)
                        ? "DUNGX 2"
                        : "DUNGX Custom";

                    foreach (PaletteCommandItem item in ParseLispFile(
                        filePath,
                        sourceLabel,
                        PaletteSourceKind.Lisp))
                    {
                        AddOrReplace(result, unique, item, savedDescriptions);
                    }
                }
            }

            foreach (KeyValuePair<string, string> manual in PaletteManualCommandStore.Load())
            {
                string description = savedDescriptions.TryGetValue(manual.Key, out string saved)
                    ? saved
                    : manual.Value;

                AddOrReplace(
                    result,
                    unique,
                    new PaletteCommandItem(
                        manual.Key,
                        description,
                        "Manual Alias",
                        PaletteSourceKind.ManualAlias,
                        manual.Key),
                    savedDescriptions);
            }

            foreach (PaletteCommandItem item in ActionMacroCatalog.BuildItems(savedDescriptions))
            {
                AddOrReplace(result, unique, item, savedDescriptions);
            }

            foreach (PaletteSourceFile source in PaletteSourceStore.LoadSources())
            {
                IEnumerable<PaletteCommandItem> items = Enumerable.Empty<PaletteCommandItem>();

                switch (source.SourceKind)
                {
                    case PaletteSourceKind.Lisp:
                        items = ParseLispFile(source.FilePath, source.DisplayName, source.SourceKind);
                        break;
                    case PaletteSourceKind.ManagedDll:
                        items = ParseManagedDll(source.FilePath, source.DisplayName, source.SourceKind);
                        break;
                    case PaletteSourceKind.Vlx:
                        items = ParseVlxFile(source.FilePath, source.DisplayName);
                        break;
                }

                foreach (PaletteCommandItem item in items)
                {
                    AddOrReplace(result, unique, item, savedDescriptions);
                }
            }

            return result;
        }

        private static void AddOrReplace(
            List<PaletteCommandItem> result,
            Dictionary<string, PaletteCommandItem> unique,
            PaletteCommandItem item,
            Dictionary<string, string> savedDescriptions)
        {
            if (savedDescriptions.TryGetValue(item.CommandName, out string savedDescription))
            {
                item.Description = savedDescription;
            }

            if (unique.TryGetValue(item.CommandName, out PaletteCommandItem existing))
            {
                result.Remove(existing);
            }

            unique[item.CommandName] = item;
            result.Add(item);
        }

        private static IEnumerable<PaletteCommandItem> ParseLispFile(
            string filePath,
            string sourceLabel,
            PaletteSourceKind sourceKind)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();
            string pendingComment = string.Empty;

            foreach (string rawLine in File.ReadAllLines(filePath, Encoding.Default))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith(";", StringComparison.Ordinal))
                {
                    string cleaned = CleanComment(line);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        pendingComment = cleaned;
                    }
                    continue;
                }

                Match match = LispCommandRegex.Match(line);
                if (!match.Success)
                {
                    pendingComment = string.Empty;
                    continue;
                }

                string commandName = match.Groups["name"].Value.Trim();
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    pendingComment = string.Empty;
                    continue;
                }

                items.Add(new PaletteCommandItem(
                    commandName,
                    pendingComment,
                    sourceLabel,
                    sourceKind,
                    filePath));
                pendingComment = string.Empty;
            }

            return items;
        }

        private static IEnumerable<PaletteCommandItem> ParseManagedDll(
            string assemblyPath,
            string sourceLabel,
            PaletteSourceKind sourceKind)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                return ParseManagedDll(assembly, sourceLabel, assemblyPath, sourceKind);
            }
            catch
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }
        }

        private static IEnumerable<PaletteCommandItem> ParseManagedDll(
            Assembly assembly,
            string sourceLabel,
            string assemblyPath,
            PaletteSourceKind sourceKind)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();

            foreach (Type type in GetLoadableTypes(assembly))
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static))
                {
                    object[] attrs = method.GetCustomAttributes(typeof(CommandMethodAttribute), false);
                    foreach (CommandMethodAttribute attr in attrs.OfType<CommandMethodAttribute>())
                    {
                        string commandName = attr.GlobalName;
                        if (string.IsNullOrWhiteSpace(commandName))
                        {
                            continue;
                        }

                        items.Add(new PaletteCommandItem(
                            commandName,
                            type.Name + "." + method.Name,
                            sourceLabel,
                            sourceKind,
                            assemblyPath));
                    }
                }
            }

            return items;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private static IEnumerable<PaletteCommandItem> ParseVlxFile(string filePath, string sourceLabel)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();

            try
            {
                string text = Encoding.Default.GetString(File.ReadAllBytes(filePath));
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match match in BinaryCommandRegex.Matches(text))
                {
                    string name = match.Groups["name"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(name) || names.Contains(name))
                    {
                        continue;
                    }

                    names.Add(name);
                    items.Add(new PaletteCommandItem(
                        name,
                        "VLX scan (best effort)",
                        sourceLabel,
                        PaletteSourceKind.Vlx,
                        filePath));
                }
            }
            catch
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }

            return items;
        }

        private static string CleanComment(string line)
        {
            string cleaned = Regex.Replace(line, @"^\s*;+", string.Empty).Trim();
            cleaned = cleaned.Trim('-', '=', '<', '>', '*', ':', ';', ' ');

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (cleaned.Length > 80)
            {
                cleaned = cleaned.Substring(0, 80).Trim();
            }

            return cleaned;
        }
    }

}
