using System;
using System.Collections.Generic;
using System.Globalization;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    /// <summary>
    /// Assigns metadata texts (SL / material) to parts. Never relies on selection order.
    ///   1. text inside a part's outer contour -> that part (innermost when nested),
    ///   2. otherwise the nearest part outline within MaxTextDistance,
    ///   3. if the second-nearest part is nearly as close -> AMBIGUOUS (both parts flagged,
    ///      value NOT assigned to either),
    ///   4. too far from every part -> global warning,
    ///   5. parts without metadata keep the defaults (SL = 1, material = 1.2MM) with a note.
    /// Conflicting values assigned to one part (e.g. two different SL) -> AMBIGUOUS.
    /// </summary>
    public sealed class TextPartAssociator
    {
        private readonly RecognitionSettings _settings;
        private readonly MetadataParser _parser;

        public TextPartAssociator(RecognitionSettings settings)
        {
            _settings = settings ?? new RecognitionSettings();
            _parser = new MetadataParser(_settings.Metadata);
        }

        private sealed class Assigned
        {
            public MetadataFact Fact;
            public TextItem Text;
            public string How;
        }

        public void Associate(List<RecognizedPart> records, IList<TextItem> texts, List<string> globalWarnings)
        {
            List<RecognizedPart> parts = new List<RecognizedPart>();
            foreach (RecognizedPart r in records)
            {
                if (r.Outer != null) parts.Add(r);
            }

            Dictionary<RecognizedPart, List<Assigned>> assigned = new Dictionary<RecognizedPart, List<Assigned>>();
            foreach (RecognizedPart p in parts) assigned[p] = new List<Assigned>();

            Dictionary<RecognizedPart, string> nameCandidates = new Dictionary<RecognizedPart, string>();

            foreach (TextItem text in texts)
            {
                List<MetadataFact> facts = _parser.Parse(text.Text);
                string shortText = Shorten(text.Text);

                RecognizedPart inside = InnermostContaining(parts, text.Position);
                if (facts.Count == 0)
                {
                    // Plain text inside a part = candidate part name.
                    if (inside != null && !nameCandidates.ContainsKey(inside) && text.Text.Trim().Length > 0 && text.Text.Trim().Length <= 40)
                    {
                        nameCandidates[inside] = text.Text.Trim();
                    }

                    continue;
                }

                if (inside != null)
                {
                    foreach (MetadataFact f in facts) assigned[inside].Add(new Assigned { Fact = f, Text = text, How = "trong chi tiet" });
                    inside.TextSources.Add(text.SourceIndex);
                    continue;
                }

                RecognizedPart best = null, second = null;
                double d1 = double.MaxValue, d2 = double.MaxValue;
                foreach (RecognizedPart p in parts)
                {
                    double d = DistanceToOutline(p.Outer.Points, text.Position);
                    if (d < d1)
                    {
                        second = best;
                        d2 = d1;
                        best = p;
                        d1 = d;
                    }
                    else if (d < d2)
                    {
                        second = p;
                        d2 = d;
                    }
                }

                if (best == null || d1 > _settings.MaxTextDistance)
                {
                    globalWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Text \"{0}\" tai {1} qua xa moi chi tiet (> {2:0.#} mm) - KHONG duoc gan.",
                        shortText, text.Position, _settings.MaxTextDistance));
                    continue;
                }

                if (second != null && d2 <= _settings.MaxTextDistance &&
                    d2 - d1 < Math.Max(_settings.AmbiguityAbsolute, d1 * (_settings.AmbiguityRatio - 1.0)))
                {
                    string msg = string.Format(CultureInfo.InvariantCulture,
                        "Text \"{0}\" gan {1} ({2:0.#} mm) va {3} ({4:0.#} mm) gan nhu nhau - can xac nhan",
                        shortText, "#" + best.Index, d1, "#" + second.Index, d2);
                    best.Escalate(PartStatus.Ambiguous, msg);
                    second.Escalate(PartStatus.Ambiguous, msg);
                    best.TextSources.Add(text.SourceIndex);
                    second.TextSources.Add(text.SourceIndex);
                    continue;
                }

                foreach (MetadataFact f in facts)
                {
                    assigned[best].Add(new Assigned
                    {
                        Fact = f,
                        Text = text,
                        How = string.Format(CultureInfo.InvariantCulture, "ngoai chi tiet, cach {0:0.#} mm", d1)
                    });
                }

                best.TextSources.Add(text.SourceIndex);
            }

            foreach (RecognizedPart p in parts)
            {
                string name;
                if (nameCandidates.TryGetValue(p, out name)) p.Name = p.Name + " " + name;
                ApplyFacts(p, assigned[p]);
            }
        }

        private void ApplyFacts(RecognizedPart part, List<Assigned> facts)
        {
            MetadataRules rules = _settings.Metadata;
            List<string> quantities = new List<string>();
            List<string> materials = new List<string>();
            foreach (Assigned a in facts)
            {
                List<string> target = a.Fact.Kind == MetadataKind.Quantity ? quantities : materials;
                if (!target.Contains(a.Fact.Value)) target.Add(a.Fact.Value);
            }

            if (quantities.Count == 1)
            {
                part.Quantity = int.Parse(quantities[0], CultureInfo.InvariantCulture);
                part.QuantityFromText = true;
            }
            else if (quantities.Count > 1)
            {
                part.Quantity = rules.DefaultQuantity;
                part.Escalate(PartStatus.Ambiguous, "Nhieu SL khac nhau: " + string.Join(", ", quantities.ToArray()));
            }
            else
            {
                part.Quantity = rules.DefaultQuantity;
                if (part.Status < PartStatus.Ambiguous)
                {
                    part.Escalate(PartStatus.Warning, "Khong tim thay SL -> mac dinh " + rules.DefaultQuantity.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (materials.Count == 1)
            {
                part.Material = materials[0];
                part.MaterialFromText = true;
            }
            else if (materials.Count > 1)
            {
                part.Material = rules.DefaultMaterial;
                part.Escalate(PartStatus.Ambiguous, "Nhieu vat lieu khac nhau: " + string.Join(", ", materials.ToArray()));
            }
            else
            {
                part.Material = rules.DefaultMaterial;
                if (part.Status < PartStatus.Ambiguous)
                {
                    part.Escalate(PartStatus.Warning, "Khong tim thay vat lieu -> mac dinh " + rules.DefaultMaterial);
                }
            }
        }

        private static RecognizedPart InnermostContaining(List<RecognizedPart> parts, Pt p)
        {
            RecognizedPart best = null;
            IntPoint ip = IntPoint.FromMm(p.X, p.Y);
            foreach (RecognizedPart part in parts)
            {
                if (p.X < part.Outer.MinX || p.X > part.Outer.MaxX || p.Y < part.Outer.MinY || p.Y > part.Outer.MaxY) continue;
                if (best != null && part.Outer.Area >= best.Outer.Area) continue;

                List<IntPoint> ring = new List<IntPoint>(part.Outer.Points.Count);
                foreach (Pt q in part.Outer.Points) ring.Add(IntPoint.FromMm(q.X, q.Y));
                if (GeometryMath.PointInRing(ip, ring) >= 0) best = part;
            }

            return best;
        }

        private static double DistanceToOutline(List<Pt> ring, Pt p)
        {
            double best = double.MaxValue;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double d = GeometryMath.PointSegmentDistanceSquared(p.X, p.Y, ring[j].X, ring[j].Y, ring[i].X, ring[i].Y);
                if (d < best) best = d;
            }

            return Math.Sqrt(best);
        }

        private static string Shorten(string text)
        {
            string t = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 30 ? t.Substring(0, 30) + "..." : t;
        }
    }
}
