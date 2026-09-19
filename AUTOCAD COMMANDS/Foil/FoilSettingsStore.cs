using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Luu cau hinh DX_FOIL theo dung co che cua project (TSV trong
    /// %AppData%\DUNGX\AUTOCAD_COMMANDS), giong AutoCutSettingsStore.
    /// </summary>
    internal static class FoilSettingsStore
    {
        private static readonly object SyncRoot = new object();
        private static string _filePath;
        private static string _folder;

        private static string GetFolder()
        {
            if (!string.IsNullOrEmpty(_folder))
            {
                return _folder;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            }

            _folder = Path.Combine(appData, "DUNGX", "AUTOCAD_COMMANDS");
            return _folder;
        }

        private static string GetFilePath()
        {
            if (!string.IsNullOrEmpty(_filePath))
            {
                return _filePath;
            }

            _filePath = Path.Combine(GetFolder(), "foil_settings.tsv");
            return _filePath;
        }

        /// <summary>Duong dan bang chan mac dinh khi nguoi dung khong chi dinh file rieng.</summary>
        public static string GetDefaultBendTablePath()
        {
            return Path.Combine(GetFolder(), "foil_bend_table.tsv");
        }

        public static FoilSettings Load()
        {
            lock (SyncRoot)
            {
                FoilSettings settings = new FoilSettings();
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
                            case "BlankLength":
                                settings.BlankLength = ParseDouble(val, settings.BlankLength);
                                break;
                            case "Thickness":
                                settings.Thickness = ParseDouble(val, settings.Thickness);
                                break;
                            case "InsideRadius":
                                settings.InsideRadius = ParseDouble(val, settings.InsideRadius);
                                break;
                            case "ThicknessCompensation":
                                if (Enum.TryParse(val, out FoilThicknessCompensationMode thickComp)) settings.ThicknessCompensation = thickComp;
                                break;
                            case "Method":
                                if (Enum.TryParse(val, out FoilBendMethod method)) settings.Method = method;
                                break;
                            case "KFactor":
                                settings.KFactor = ParseDouble(val, settings.KFactor);
                                break;
                            case "CustomFactor":
                                settings.CustomFactor = ParseDouble(val, settings.CustomFactor);
                                break;
                            case "CustomRuleScaleByAngle":
                                if (bool.TryParse(val, out bool scaleByAngle)) settings.CustomRuleScaleByAngle = scaleByAngle;
                                break;
                            case "BendTablePath":
                                settings.BendTablePath = val;
                                break;
                            case "ArcRadiusInterpretation":
                                if (Enum.TryParse(val, out FoilArcRadiusInterpretation arc)) settings.ArcRadiusInterpretation = arc;
                                break;
                            case "MinBendAngleDeg":
                                settings.MinBendAngleDeg = ParseDouble(val, settings.MinBendAngleDeg);
                                break;
                            case "MaxBendAngleDeg":
                                settings.MaxBendAngleDeg = ParseDouble(val, settings.MaxBendAngleDeg);
                                break;
                            case "DuplicatePointTolerance":
                                settings.DuplicatePointTolerance = ParseDouble(val, settings.DuplicatePointTolerance);
                                break;
                            case "AllowThicknessOutlineInput":
                                if (bool.TryParse(val, out bool allowOutline)) settings.AllowThicknessOutlineInput = allowOutline;
                                break;
                            case "InvertBendDirection":
                                if (bool.TryParse(val, out bool invert)) settings.InvertBendDirection = invert;
                                break;
                            case "BendLayerName":
                                if (!string.IsNullOrWhiteSpace(val)) settings.BendLayerName = val;
                                break;
                            case "OutlineLayerName":
                                settings.OutlineLayerName = val;
                                break;
                            case "BendUpColorMode":
                                if (Enum.TryParse(val, out FoilColorMode upMode)) settings.BendUpColorMode = upMode;
                                break;
                            case "BendUpColorIndex":
                                settings.BendUpColorIndex = ParseInt(val, settings.BendUpColorIndex);
                                break;
                            case "BendDownColorMode":
                                if (Enum.TryParse(val, out FoilColorMode downMode)) settings.BendDownColorMode = downMode;
                                break;
                            case "BendDownColorIndex":
                                settings.BendDownColorIndex = ParseInt(val, settings.BendDownColorIndex);
                                break;
                            case "BendLineMode":
                                if (Enum.TryParse(val, out FoilBendLineMode lineMode)) settings.BendLineMode = lineMode;
                                break;
                            case "DrawBendSteps":
                                if (bool.TryParse(val, out bool drawSteps)) settings.DrawBendSteps = drawSteps;
                                break;
                            case "BendSequenceOrder":
                                if (Enum.TryParse(val, out FoilBendSequenceOrder seqOrder)) settings.BendSequenceOrder = seqOrder;
                                break;
                            case "StepLayerName":
                                if (!string.IsNullOrWhiteSpace(val)) settings.StepLayerName = val;
                                break;
                            case "StepTextHeight":
                                settings.StepTextHeight = ParseDouble(val, settings.StepTextHeight);
                                break;
                            case "StepGapFactor":
                                settings.StepGapFactor = ParseDouble(val, settings.StepGapFactor);
                                break;
                            case "BlankRotationDeg":
                                settings.BlankRotationDeg = ParseDouble(val, settings.BlankRotationDeg);
                                break;
                            case "Precision":
                                settings.Precision = ParseInt(val, settings.Precision);
                                break;
                            case "ZoomToResult":
                                if (bool.TryParse(val, out bool zoom)) settings.ZoomToResult = zoom;
                                break;
                        }
                    }
                }
                catch
                {
                    // Loi doc cau hinh: quay ve gia tri mac dinh, khong lam gian doan lenh.
                }

                return settings;
            }
        }

        public static void Save(FoilSettings settings)
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

                    CultureInfo ci = CultureInfo.InvariantCulture;
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("BlankLength\t" + settings.BlankLength.ToString(ci));
                    sb.AppendLine("Thickness\t" + settings.Thickness.ToString(ci));
                    sb.AppendLine("InsideRadius\t" + settings.InsideRadius.ToString(ci));
                    sb.AppendLine("ThicknessCompensation	" + settings.ThicknessCompensation);
                    sb.AppendLine("Method\t" + settings.Method);
                    sb.AppendLine("KFactor\t" + settings.KFactor.ToString(ci));
                    sb.AppendLine("CustomFactor\t" + settings.CustomFactor.ToString(ci));
                    sb.AppendLine("CustomRuleScaleByAngle\t" + settings.CustomRuleScaleByAngle);
                    sb.AppendLine("BendTablePath\t" + (settings.BendTablePath ?? string.Empty));
                    sb.AppendLine("ArcRadiusInterpretation\t" + settings.ArcRadiusInterpretation);
                    sb.AppendLine("MinBendAngleDeg\t" + settings.MinBendAngleDeg.ToString(ci));
                    sb.AppendLine("MaxBendAngleDeg\t" + settings.MaxBendAngleDeg.ToString(ci));
                    sb.AppendLine("DuplicatePointTolerance\t" + settings.DuplicatePointTolerance.ToString(ci));
                    sb.AppendLine("AllowThicknessOutlineInput\t" + settings.AllowThicknessOutlineInput);
                    sb.AppendLine("InvertBendDirection\t" + settings.InvertBendDirection);
                    sb.AppendLine("BendLayerName\t" + settings.BendLayerName);
                    sb.AppendLine("OutlineLayerName\t" + (settings.OutlineLayerName ?? string.Empty));
                    sb.AppendLine("BendUpColorMode\t" + settings.BendUpColorMode);
                    sb.AppendLine("BendUpColorIndex\t" + settings.BendUpColorIndex.ToString(ci));
                    sb.AppendLine("BendDownColorMode\t" + settings.BendDownColorMode);
                    sb.AppendLine("BendDownColorIndex\t" + settings.BendDownColorIndex.ToString(ci));
                    sb.AppendLine("BendLineMode\t" + settings.BendLineMode);
                    sb.AppendLine("DrawBendSteps	" + settings.DrawBendSteps);
                    sb.AppendLine("BendSequenceOrder	" + settings.BendSequenceOrder);
                    sb.AppendLine("StepLayerName	" + settings.StepLayerName);
                    sb.AppendLine("StepTextHeight	" + settings.StepTextHeight.ToString(ci));
                    sb.AppendLine("StepGapFactor	" + settings.StepGapFactor.ToString(ci));
                    sb.AppendLine("BlankRotationDeg\t" + settings.BlankRotationDeg.ToString(ci));
                    sb.AppendLine("Precision\t" + settings.Precision.ToString(ci));
                    sb.AppendLine("ZoomToResult\t" + settings.ZoomToResult);

                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Bo qua loi ghi cau hinh.
                }
            }
        }

        private static double ParseDouble(string text, double fallback)
        {
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                FoilValidation.IsFinite(value))
            {
                return value;
            }

            return fallback;
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return fallback;
        }
    }
}
