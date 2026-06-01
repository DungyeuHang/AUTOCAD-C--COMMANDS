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

    internal static class SdxyTargetSettingsStore
    {
        private static readonly string FilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "sdxy_target_settings.tsv");

        public static SdxyTargetSettings Load()
        {
            if (!File.Exists(FilePath))
            {
                return new SdxyTargetSettings();
            }

            try
            {
                return ParseLines(File.ReadAllLines(FilePath, Encoding.UTF8));
            }
            catch
            {
                return new SdxyTargetSettings();
            }
        }

        public static void Save(SdxyTargetSettings settings)
        {
            settings = settings ?? new SdxyTargetSettings();
            File.WriteAllLines(FilePath, BuildLines(settings), Encoding.UTF8);
        }

        internal static SdxyTargetSettings ParseLines(IEnumerable<string> rawLines)
        {
            SdxyTargetSettings settings = new SdxyTargetSettings();
            string sampleTypeName = string.Empty;
            string sampleTypeDisplay = string.Empty;
            string sampleLayer = string.Empty;
            string sampleLinetype = string.Empty;
            string sampleColorKey = string.Empty;
            string sampleColorDisplay = string.Empty;
            string sampleBlock = string.Empty;
            Dictionary<int, Dictionary<string, string>> indexedSamples =
                new Dictionary<int, Dictionary<string, string>>();

            foreach (string rawLine in rawLines ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string[] parts = rawLine.Split(new[] { '\t' }, 2);
                string key = parts[0].Trim();
                string value = parts.Length > 1 ? parts[1] : string.Empty;

                if (TryParseIndexedSampleKey(key, out int sampleIndex, out string sampleField))
                {
                    if (!indexedSamples.TryGetValue(sampleIndex, out Dictionary<string, string> sampleValues))
                    {
                        sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        indexedSamples[sampleIndex] = sampleValues;
                    }

                    sampleValues[sampleField] = value.Trim();
                    continue;
                }

                switch (key)
                {
                    case "Type":
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            settings.AllowedTypeNames.Add(value.Trim());
                        }
                        break;
                    case "Layer":
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            settings.AllowedLayers.Add(value.Trim());
                        }
                        break;
                    case "UseSampleType":
                        settings.UseSampleType = ParseBoolean(value);
                        break;
                    case "UseSampleLayer":
                        settings.UseSampleLayer = ParseBoolean(value);
                        break;
                    case "UseSampleLinetype":
                        settings.UseSampleLinetype = ParseBoolean(value);
                        break;
                    case "UseSampleColor":
                        settings.UseSampleColor = ParseBoolean(value);
                        break;
                    case "UseSampleBlockName":
                        settings.UseSampleBlockName = ParseBoolean(value);
                        break;
                    case "SampleType":
                        sampleTypeName = value.Trim();
                        break;
                    case "SampleTypeDisplay":
                        sampleTypeDisplay = value.Trim();
                        break;
                    case "SampleLayer":
                        sampleLayer = value.Trim();
                        break;
                    case "SampleLinetype":
                        sampleLinetype = value.Trim();
                        break;
                    case "SampleColorKey":
                        sampleColorKey = value.Trim();
                        break;
                    case "SampleColorDisplay":
                        sampleColorDisplay = value.Trim();
                        break;
                    case "SampleBlock":
                        sampleBlock = value.Trim();
                        break;
                }
            }

            foreach (KeyValuePair<int, Dictionary<string, string>> pair in indexedSamples
                .OrderBy(item => item.Key))
            {
                SdxySampleDescriptor indexedSample = BuildSampleDescriptor(pair.Value);
                if (indexedSample != null)
                {
                    settings.SampleDescriptors.Add(indexedSample);
                }
            }

            if (settings.SampleDescriptors.Count == 0 &&
                (!string.IsNullOrWhiteSpace(sampleTypeName) ||
                 !string.IsNullOrWhiteSpace(sampleLayer) ||
                 !string.IsNullOrWhiteSpace(sampleLinetype) ||
                 !string.IsNullOrWhiteSpace(sampleColorKey) ||
                 !string.IsNullOrWhiteSpace(sampleBlock)))
            {
                settings.SampleDescriptors.Add(new SdxySampleDescriptor(
                    sampleTypeName,
                    sampleTypeDisplay,
                    sampleLayer,
                    sampleLinetype,
                    sampleColorKey,
                    sampleColorDisplay,
                    sampleBlock));
            }

            if (settings.SampleDescriptors.Count == 0)
            {
                settings.UseSampleType = false;
                settings.UseSampleLayer = false;
                settings.UseSampleLinetype = false;
                settings.UseSampleColor = false;
                settings.UseSampleBlockName = false;
            }

            return settings;
        }

        internal static List<string> BuildLines(SdxyTargetSettings settings)
        {
            settings = settings ?? new SdxyTargetSettings();

            List<string> lines = new List<string>();
            foreach (string typeName in settings.AllowedTypeNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add("Type\t" + typeName);
            }

            foreach (string layerName in settings.AllowedLayers.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add("Layer\t" + layerName);
            }

            lines.Add("UseSampleType\t" + (settings.UseSampleType ? "1" : "0"));
            lines.Add("UseSampleLayer\t" + (settings.UseSampleLayer ? "1" : "0"));
            lines.Add("UseSampleLinetype\t" + (settings.UseSampleLinetype ? "1" : "0"));
            lines.Add("UseSampleColor\t" + (settings.UseSampleColor ? "1" : "0"));
            lines.Add("UseSampleBlockName\t" + (settings.UseSampleBlockName ? "1" : "0"));

            List<SdxySampleDescriptor> samples = settings.SampleDescriptors
                .Where(sample => sample != null)
                .ToList();
            for (int i = 0; i < samples.Count; i++)
            {
                SdxySampleDescriptor sample = samples[i];
                lines.Add("Sample" + i + "Type\t" + (sample.TypeName ?? string.Empty));
                lines.Add("Sample" + i + "TypeDisplay\t" + (sample.TypeDisplayName ?? string.Empty));
                lines.Add("Sample" + i + "Layer\t" + (sample.LayerName ?? string.Empty));
                lines.Add("Sample" + i + "Linetype\t" + (sample.LinetypeName ?? string.Empty));
                lines.Add("Sample" + i + "ColorKey\t" + (sample.ColorKey ?? string.Empty));
                lines.Add("Sample" + i + "ColorDisplay\t" + (sample.ColorDisplayName ?? string.Empty));
                lines.Add("Sample" + i + "Block\t" + (sample.BlockName ?? string.Empty));
            }

            return lines;
        }

        private static SdxySampleDescriptor BuildSampleDescriptor(IReadOnlyDictionary<string, string> values)
        {
            if (values == null)
            {
                return null;
            }

            values.TryGetValue("Type", out string typeName);
            values.TryGetValue("TypeDisplay", out string typeDisplay);
            values.TryGetValue("Layer", out string layerName);
            values.TryGetValue("Linetype", out string linetypeName);
            values.TryGetValue("ColorKey", out string colorKey);
            values.TryGetValue("ColorDisplay", out string colorDisplay);
            values.TryGetValue("Block", out string blockName);

            if (string.IsNullOrWhiteSpace(typeName) &&
                string.IsNullOrWhiteSpace(layerName) &&
                string.IsNullOrWhiteSpace(linetypeName) &&
                string.IsNullOrWhiteSpace(colorKey) &&
                string.IsNullOrWhiteSpace(blockName))
            {
                return null;
            }

            return new SdxySampleDescriptor(
                typeName,
                typeDisplay,
                layerName,
                linetypeName,
                colorKey,
                colorDisplay,
                blockName);
        }

        private static bool TryParseIndexedSampleKey(string key, out int sampleIndex, out string sampleField)
        {
            sampleIndex = -1;
            sampleField = string.Empty;

            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith("Sample", StringComparison.Ordinal) ||
                key.Length <= "Sample".Length ||
                !char.IsDigit(key["Sample".Length]))
            {
                return false;
            }

            int index = "Sample".Length;
            while (index < key.Length && char.IsDigit(key[index]))
            {
                index++;
            }

            if (index <= "Sample".Length || index >= key.Length)
            {
                return false;
            }

            if (!int.TryParse(key.Substring("Sample".Length, index - "Sample".Length), out sampleIndex))
            {
                sampleIndex = -1;
                return false;
            }

            sampleField = key.Substring(index);
            return !string.IsNullOrWhiteSpace(sampleField);
        }

        private static bool ParseBoolean(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
