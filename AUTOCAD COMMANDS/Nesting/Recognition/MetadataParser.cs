using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    public enum MetadataKind
    {
        Quantity,
        Material
    }

    public sealed class MetadataFact
    {
        public MetadataFact(MetadataKind kind, string value, int quantity)
        {
            Kind = kind;
            Value = value;
            QuantityValue = quantity;
        }

        public MetadataKind Kind { get; private set; }

        /// <summary>Normalised value ("12", "1.2MM").</summary>
        public string Value { get; private set; }

        public int QuantityValue { get; private set; }
    }

    /// <summary>
    /// Configurable metadata rules. Each pattern must expose a named group "v".
    /// Defaults accept: "SL: 12", "SL:12", "SL 12", "sl: 12", "SL=12" and
    /// "1.2MM", "1.2 MM", "1,2MM", "1,2 MM", "1.2mm". Thickness outside
    /// [MinThicknessMm, MaxThicknessMm] is rejected so ordinary dimensions ("150MM") are not
    /// mistaken for a material.
    /// </summary>
    public sealed class MetadataRules
    {
        public List<string> QuantityPatterns { get; set; } = new List<string>
        {
            @"(?<![A-Z0-9])SL\s*[:=]?\s*(?<v>\d{1,5})(?![\d.,])"
        };

        public List<string> MaterialPatterns { get; set; } = new List<string>
        {
            @"(?<![\d.,])(?<v>\d{1,2}(?:[.,]\d{1,3})?)\s*MM(?![A-Z])"
        };

        public double MinThicknessMm { get; set; } = 0.3;

        public double MaxThicknessMm { get; set; } = 25.0;

        public int MaxQuantity { get; set; } = 100000;

        public string DefaultMaterial { get; set; } = "1.2MM";

        public int DefaultQuantity { get; set; } = 1;
    }

    public sealed class MetadataParser
    {
        private readonly MetadataRules _rules;
        private readonly List<Regex> _quantity = new List<Regex>();
        private readonly List<Regex> _material = new List<Regex>();

        public MetadataParser(MetadataRules rules)
        {
            _rules = rules ?? new MetadataRules();
            foreach (string p in _rules.QuantityPatterns) _quantity.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            foreach (string p in _rules.MaterialPatterns) _material.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        public MetadataRules Rules { get { return _rules; } }

        public static string NormalizeMaterial(double thickness)
        {
            return thickness.ToString("0.###", CultureInfo.InvariantCulture) + "MM";
        }

        /// <summary>Returns all quantity / material facts found in one text (in order of appearance).</summary>
        public List<MetadataFact> Parse(string text)
        {
            List<MetadataFact> facts = new List<MetadataFact>();
            if (string.IsNullOrWhiteSpace(text)) return facts;

            // Match SL first so that "SL 12" is never read as a material, then materials on the rest.
            string remaining = text;
            foreach (Regex rx in _quantity)
            {
                foreach (Match m in rx.Matches(remaining))
                {
                    int q;
                    if (int.TryParse(m.Groups["v"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out q) &&
                        q >= 1 && q <= _rules.MaxQuantity)
                    {
                        facts.Add(new MetadataFact(MetadataKind.Quantity, q.ToString(CultureInfo.InvariantCulture), q));
                    }
                }

                remaining = rx.Replace(remaining, " ");
            }

            foreach (Regex rx in _material)
            {
                foreach (Match m in rx.Matches(remaining))
                {
                    double t;
                    string v = m.Groups["v"].Value.Replace(',', '.');
                    if (double.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out t) &&
                        t >= _rules.MinThicknessMm && t <= _rules.MaxThicknessMm)
                    {
                        facts.Add(new MetadataFact(MetadataKind.Material, NormalizeMaterial(t), 0));
                    }
                }
            }

            return facts;
        }
    }
}
