using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    internal static class AutoCutSettingsStore
    {
        private static readonly object SyncRoot = new object();
        private static string _filePath;

        private static string GetFilePath()
        {
            if (!string.IsNullOrEmpty(_filePath))
            {
                return _filePath;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            }

            string dir = Path.Combine(appData, "DUNGX", "AUTOCAD_COMMANDS");
            _filePath = Path.Combine(dir, "autocut_settings.tsv");
            return _filePath;
        }

        public static AutoCutSettings Load()
        {
            lock (SyncRoot)
            {
                AutoCutSettings settings = new AutoCutSettings();
                string path = GetFilePath();

                if (!File.Exists(path))
                {
                    return settings;
                }

                try
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    foreach (string rawLine in lines)
                    {
                        if (string.IsNullOrWhiteSpace(rawLine)) continue;
                        string[] parts = rawLine.Split(new[] { '\t' }, 2);
                        if (parts.Length < 2) continue;

                        string key = parts[0].Trim();
                        string val = parts[1].Trim();

                        switch (key)
                        {
                            case "TargetTypes":
                                if (Enum.TryParse(val, out AutoCutTargetTypeFlags ttf)) settings.TargetTypes = ttf;
                                break;
                            case "TargetLayerFilterMode":
                                if (Enum.TryParse(val, out AutoCutTargetLayerFilterMode tlm)) settings.TargetLayerFilterMode = tlm;
                                break;
                            case "TargetLayerName":
                                settings.TargetLayerName = val;
                                break;
                            case "TargetType":
                                if (Enum.TryParse(val, out AutoCutTargetType tt)) settings.TargetType = tt;
                                break;
                            case "CutterTypes":
                                if (Enum.TryParse(val, out AutoCutCutterTypeFlags cf)) settings.CutterTypes = cf;
                                break;
                            case "LayerFilterMode":
                                if (Enum.TryParse(val, out AutoCutLayerFilterMode lf)) settings.LayerFilterMode = lf;
                                break;
                            case "CustomLayerName":
                                settings.CustomLayerName = val;
                                break;
                            case "ColorFilterMode":
                                if (Enum.TryParse(val, out AutoCutColorFilterMode cm)) settings.ColorFilterMode = cm;
                                break;
                            case "SpecificColorIndex":
                                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sc)) settings.SpecificColorIndex = sc;
                                break;
                            case "SearchTolerance":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double st)) settings.SearchTolerance = Math.Max(1e-5, st);
                                break;
                            case "DeduplicationTolerance":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double dt)) settings.DeduplicationTolerance = Math.Max(1e-7, dt);
                                break;
                            case "CutMode":
                                if (Enum.TryParse(val, out AutoCutCutMode md)) settings.CutMode = md;
                                break;
                            case "SingleIntersectionRule":
                                if (Enum.TryParse(val, out AutoCutSingleIntersectionRule sr)) settings.SingleIntersectionRule = sr;
                                break;
                            case "ShowSettingsBeforeSelection":
                                if (bool.TryParse(val, out bool sb)) settings.ShowSettingsBeforeSelection = sb;
                                break;
                            case "IgnoreElevation":
                                if (bool.TryParse(val, out bool ie)) settings.IgnoreElevation = ie;
                                break;
                        }
                    }
                }
                catch
                {
                    // If error loading, return defaults
                }

                return settings;
            }
        }

        public static void Save(AutoCutSettings settings)
        {
            if (settings == null) return;

            lock (SyncRoot)
            {
                try
                {
                    string path = GetFilePath();
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"TargetTypes\t{settings.TargetTypes}");
                    sb.AppendLine($"TargetLayerFilterMode\t{settings.TargetLayerFilterMode}");
                    sb.AppendLine($"TargetLayerName\t{settings.TargetLayerName}");
                    sb.AppendLine($"TargetType\t{settings.TargetType}");
                    sb.AppendLine($"CutterTypes\t{settings.CutterTypes}");
                    sb.AppendLine($"LayerFilterMode\t{settings.LayerFilterMode}");
                    sb.AppendLine($"CustomLayerName\t{settings.CustomLayerName}");
                    sb.AppendLine($"ColorFilterMode\t{settings.ColorFilterMode}");
                    sb.AppendLine($"SpecificColorIndex\t{settings.SpecificColorIndex.ToString(CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"SearchTolerance\t{settings.SearchTolerance.ToString(CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"DeduplicationTolerance\t{settings.DeduplicationTolerance.ToString(CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"CutMode\t{settings.CutMode}");
                    sb.AppendLine($"SingleIntersectionRule\t{settings.SingleIntersectionRule}");
                    sb.AppendLine($"ShowSettingsBeforeSelection\t{settings.ShowSettingsBeforeSelection}");
                    sb.AppendLine($"IgnoreElevation\t{settings.IgnoreElevation}");

                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Ignore save errors
                }
            }
        }
    }
}

