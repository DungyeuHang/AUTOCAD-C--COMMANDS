using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - LUU THONG SO
    // Ghi ra AppData theo dung quy uoc cua cac module khac (AutoCut, Foil) nen cap nhat DLL
    // khong lam mat cai dat cua nguoi dung.
    // ==========================================================================================
    internal static class AutoDimPlineSettingsStore
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

            string directory = Path.Combine(appData, "DUNGX", "AUTOCAD_COMMANDS");
            _filePath = Path.Combine(directory, "autodimpline_settings.tsv");
            return _filePath;
        }

        public static AutoDimPlineSettings Load()
        {
            lock (SyncRoot)
            {
                AutoDimPlineSettings settings = new AutoDimPlineSettings();
                string path = GetFilePath();

                if (!File.Exists(path))
                {
                    return settings;
                }

                try
                {
                    foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(rawLine))
                        {
                            continue;
                        }

                        string[] parts = rawLine.Split(new[] { '\t' }, 2);
                        if (parts.Length < 2)
                        {
                            continue;
                        }

                        Apply(settings, parts[0].Trim(), parts[1].Trim());
                    }
                }
                catch (Exception)
                {
                    // File hong thi quay ve mac dinh, khong bao gio lam hong lenh.
                    return new AutoDimPlineSettings();
                }

                // Gia tri luu co the da bi sua tay ngoai file -> chi nhan neu con hop le.
                return settings.Validate().Count == 0 ? settings : new AutoDimPlineSettings();
            }
        }

        private static void Apply(AutoDimPlineSettings settings, string key, string value)
        {
            switch (key)
            {
                case "LinearScale":
                    settings.LinearScale = ParseDouble(value, settings.LinearScale);
                    break;
                case "DimensionLayer":
                    if (!string.IsNullOrWhiteSpace(value)) settings.DimensionLayer = value;
                    break;
                case "DistanceFromPline":
                    settings.DistanceFromPline = ParseDouble(value, settings.DistanceFromPline);
                    break;
                case "DimensionSpacing":
                    settings.DimensionSpacing = ParseDouble(value, settings.DimensionSpacing);
                    break;
                case "MinSegmentLength":
                    settings.MinSegmentLength = ParseDouble(value, settings.MinSegmentLength);
                    break;
                case "CreateOverall":
                    settings.CreateOverall = ParseBool(value, settings.CreateOverall);
                    break;
                case "AutoLayout":
                    settings.AutoLayout = ParseBool(value, settings.AutoLayout);
                    break;
                case "PlaceNearFeature":
                    settings.PlaceNearFeature = ParseBool(value, settings.PlaceNearFeature);
                    break;
                case "SideBias":
                    {
                        DimPlineSideBias bias;
                        if (TryParseEnum(value, out bias)) settings.SideBias = bias;
                        break;
                    }
                case "ArcMode":
                    {
                        DimPlineArcMode arc;
                        if (TryParseEnum(value, out arc)) settings.ArcMode = arc;
                        break;
                    }
                case "SkewMode":
                    {
                        DimPlineSkewMode skew;
                        if (TryParseEnum(value, out skew)) settings.SkewMode = skew;
                        break;
                    }
                case "AngleToleranceDegrees":
                    settings.AngleToleranceDegrees = ParseDouble(value, settings.AngleToleranceDegrees);
                    break;
                case "Verbose":
                    settings.Verbose = ParseBool(value, settings.Verbose);
                    break;
            }
        }

        public static void Save(AutoDimPlineSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                try
                {
                    string path = GetFilePath();
                    string directory = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(directory))
                    {
                        return;
                    }

                    Directory.CreateDirectory(directory);

                    StringBuilder sb = new StringBuilder();
                    Append(sb, "LinearScale", settings.LinearScale);
                    Append(sb, "DimensionLayer", settings.DimensionLayer);
                    Append(sb, "DistanceFromPline", settings.DistanceFromPline);
                    Append(sb, "DimensionSpacing", settings.DimensionSpacing);
                    Append(sb, "MinSegmentLength", settings.MinSegmentLength);
                    Append(sb, "CreateOverall", settings.CreateOverall);
                    Append(sb, "AutoLayout", settings.AutoLayout);
                    Append(sb, "PlaceNearFeature", settings.PlaceNearFeature);
                    Append(sb, "SideBias", settings.SideBias);
                    Append(sb, "ArcMode", settings.ArcMode);
                    Append(sb, "SkewMode", settings.SkewMode);
                    Append(sb, "AngleToleranceDegrees", settings.AngleToleranceDegrees);
                    Append(sb, "Verbose", settings.Verbose);

                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                    // Khong luu duoc thi thoi - khong duoc lam hong lenh dang chay.
                }
            }
        }

        private static void Append(StringBuilder sb, string key, object value)
        {
            string text = value is double
                ? ((double)value).ToString("0.######", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);

            sb.Append(key).Append('\t').AppendLine(text ?? string.Empty);
        }

        private static double ParseDouble(string text, double fallback)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            bool value;
            return bool.TryParse(text, out value) ? value : fallback;
        }

        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            value = default(T);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                value = (T)Enum.Parse(typeof(T), text, true);
                return Enum.IsDefined(typeof(T), value);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
