using System;
using System.Collections.Generic;
using System.Globalization;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    /// <summary>
    /// Assigns metadata texts (SL / material) to parts. Never relies on selection order.
    ///   1. text inside a part's MATERIAL (outer contour, not inside one of its holes) -> that
    ///      part (innermost when nested); a text in the hole of a frame is NOT inside the frame,
    ///   2. otherwise the nearest part geometry (outline AND hole edges) within MaxTextDistance,
    ///   3. if the second-nearest part is nearly as close -> AMBIGUOUS (both parts flagged,
    ///      value NOT assigned to either),
    ///   4. too far from every part -> global warning,
    ///   5. parts without metadata keep the defaults (SL = 1, material = 1.2MM) with a note.
    /// Conflicting values assigned to one part (e.g. two different SL) -> AMBIGUOUS.
    /// Text that LOOKS like metadata but is invalid ("SL: abc", "SL: 0", "-1.2MM") is routed
    /// exactly like metadata and makes its part AMBIGUOUS with the offending text in the note -
    /// it is never cut into the part and never silently replaced by a default.
    /// Dimension text ("R12.5MM", "D10MM", "150MM") is neither metadata nor cut text: ignored.
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
            /// <summary>Gia tri hop le, HOAC <see cref="Issue"/> (chu co dang thong tin nhung sai).</summary>
            public MetadataFact Fact;
            public MetadataIssue Issue;
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
            List<PendingText> pending = new List<PendingText>();

            foreach (TextItem text in texts)
            {
                MetadataReading reading = _parser.Classify(text.Text);
                List<MetadataFact> facts = reading.Facts;
                string shortText = Shorten(text.Text);

                // Chu kich thuoc: khong phai thong tin, cung KHONG phai chu cat (truoc day "150MM",
                // "R12.5MM" nam trong chi tiet se bi mang di cat).
                if (reading.IsDimensionOnly) continue;

                RecognizedPart inside = InnermostContaining(parts, text.Position);
                if (!reading.IsMetadata)
                {
                    if (inside != null)
                    {
                        // Chu nam TRONG duong bao ma khong doc ra SL / vat lieu nao: day la chu
                        // CAT tren chinh chi tiet do (ma chi tiet). No phai di theo chi tiet ra
                        // ban ve moi va xoay / lat cung chi tiet - vi may se cat no that.
                        //
                        // Khong bat nguoi dung chuyen no sang layer rieng: tren ban ve that no
                        // nam ngay tren layer duong bao, dung nhu ban chat cua no.
                        if (!inside.EngravingSources.Contains(text.SourceIndex)) inside.EngravingSources.Add(text.SourceIndex);
                        if (!inside.GeometrySources.Contains(text.SourceIndex)) inside.GeometrySources.Add(text.SourceIndex);

                        // ... va van dung lam ten ban ghi cho de nhan mat o bang kiem tra.
                        if (!nameCandidates.ContainsKey(inside) && text.Text.Trim().Length > 0 && text.Text.Trim().Length <= 40)
                        {
                            nameCandidates[inside] = text.Text.Trim();
                        }
                    }

                    continue;
                }

                List<Assigned> items = new List<Assigned>();
                foreach (MetadataFact f in facts) items.Add(new Assigned { Fact = f, Text = text });
                foreach (MetadataIssue i in reading.Issues)
                {
                    if (i.Kind != MetadataIssueKind.Dimension) items.Add(new Assigned { Issue = i, Text = text });
                }

                if (inside != null)
                {
                    foreach (Assigned x in items)
                    {
                        x.How = "trong chi tiet";
                        assigned[inside].Add(x);
                    }

                    inside.TextSources.Add(text.SourceIndex);
                    continue;
                }

                RecognizedPart best = null, second = null;
                double d1 = double.MaxValue, d2 = double.MaxValue;
                foreach (RecognizedPart p in parts)
                {
                    double d = DistanceToPart(p, text.Position);
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
                    // Chua ket luan o day: can biet hai chi tiet da co SL / vat lieu RIENG chua,
                    // ma dieu do chi biet khi da duyet het moi chu. Xem ResolvePending.
                    pending.Add(new PendingText
                    {
                        Items = items, Text = text, ShortText = shortText,
                        Best = best, Second = second, D1 = d1, D2 = d2
                    });
                    continue;
                }

                foreach (Assigned x in items)
                {
                    x.How = string.Format(CultureInfo.InvariantCulture, "ngoai chi tiet, cach {0:0.#} mm", d1);
                    assigned[best].Add(x);
                }

                best.TextSources.Add(text.SourceIndex);
            }

            ResolvePending(pending, assigned);

            foreach (RecognizedPart p in parts)
            {
                string name;
                if (nameCandidates.TryGetValue(p, out name)) p.Name = p.Name + " " + name;
                ApplyFacts(p, assigned[p]);
            }
        }

        /// <summary>Chu thong tin nam gan HAI chi tiet gan nhu nhau - cho ket luan.</summary>
        private sealed class PendingText
        {
            public List<Assigned> Items;
            public TextItem Text;
            public string ShortText;
            public RecognizedPart Best, Second;
            public double D1, D2;
        }

        /// <summary>
        /// Chu nam giua hai chi tiet. Truoc day LUON bao mo ho, ke ca khi nhin la biet ngay:
        /// tren ban ve that moi chi tiet co chu SL rieng, va chu cua chi tiet ben canh nam sat
        /// ca hai. Gio chi con mo ho khi THAT SU khong biet:
        ///   - mot ben DA CO du thong tin (SL / vat lieu) cua chu nay, ben kia CHUA CO -> gan cho
        ///     ben chua co;
        ///   - ca hai ben deu da co du -> chu thua, bo qua (ghi chu cho biet);
        ///   - con lai (ca hai cung thieu, hoac chu sai dang) -> MO HO nhu cu, vi gan bua thi
        ///     sai SL - loi nang nhat.
        /// </summary>
        private static void ResolvePending(List<PendingText> pending, Dictionary<RecognizedPart, List<Assigned>> assigned)
        {
            foreach (PendingText t in pending)
            {
                bool hasIssue = t.Items.Exists(x => x.Issue != null);
                List<MetadataKind> kinds = new List<MetadataKind>();
                foreach (Assigned x in t.Items)
                {
                    if (x.Fact != null && !kinds.Contains(x.Fact.Kind)) kinds.Add(x.Fact.Kind);
                }

                bool bestHas = kinds.Count > 0 && kinds.TrueForAll(k => HasKind(assigned[t.Best], k));
                bool secondHas = kinds.Count > 0 && kinds.TrueForAll(k => HasKind(assigned[t.Second], k));
                bool bestLacks = kinds.TrueForAll(k => !HasKind(assigned[t.Best], k));
                bool secondLacks = kinds.TrueForAll(k => !HasKind(assigned[t.Second], k));

                RecognizedPart target = null;
                if (!hasIssue && kinds.Count > 0)
                {
                    if (bestLacks && secondHas) target = t.Best;
                    else if (secondLacks && bestHas) target = t.Second;
                }

                if (target != null)
                {
                    RecognizedPart other = target == t.Best ? t.Second : t.Best;
                    double d = target == t.Best ? t.D1 : t.D2;
                    foreach (Assigned x in t.Items)
                    {
                        x.How = string.Format(CultureInfo.InvariantCulture,
                            "ngoai chi tiet, cach {0:0.#} mm; #{1} da co thong tin rieng", d, other.Index);
                        assigned[target].Add(x);
                    }

                    target.TextSources.Add(t.Text.SourceIndex);
                    target.Escalate(PartStatus.Warning, string.Format(CultureInfo.InvariantCulture,
                        "Text \"{0}\" gan ca #{1} va #{2} - tu gan cho chi tiet nay vi #{3} da co thong tin rieng",
                        t.ShortText, t.Best.Index, t.Second.Index, other.Index));
                    continue;
                }

                if (!hasIssue && bestHas && secondHas)
                {
                    string skip = string.Format(CultureInfo.InvariantCulture,
                        "Text \"{0}\" gan ca #{1} va #{2}, nhung ca hai da co thong tin rieng - bo qua chu nay",
                        t.ShortText, t.Best.Index, t.Second.Index);
                    t.Best.Escalate(PartStatus.Warning, skip);
                    t.Second.Escalate(PartStatus.Warning, skip);
                    continue;
                }

                string msg = string.Format(CultureInfo.InvariantCulture,
                    "Text \"{0}\" gan {1} ({2:0.#} mm) va {3} ({4:0.#} mm) gan nhu nhau - can xac nhan",
                    t.ShortText, "#" + t.Best.Index, t.D1, "#" + t.Second.Index, t.D2);
                t.Best.Escalate(PartStatus.Ambiguous, msg);
                t.Second.Escalate(PartStatus.Ambiguous, msg);
                t.Best.TextSources.Add(t.Text.SourceIndex);
                t.Second.TextSources.Add(t.Text.SourceIndex);
            }
        }

        private static bool HasKind(List<Assigned> facts, MetadataKind kind)
        {
            return facts.Exists(a => a.Fact != null && a.Fact.Kind == kind);
        }

        private void ApplyFacts(RecognizedPart part, List<Assigned> facts)
        {
            MetadataRules rules = _settings.Metadata;
            List<string> quantities = new List<string>();
            List<string> materials = new List<string>();
            bool badQuantity = false, badMaterial = false;
            foreach (Assigned a in facts)
            {
                if (a.Issue != null)
                {
                    // Chu co dang thong tin nhung sai: bao DUNG chu do, bat nguoi dung sua / xac nhan.
                    bool isQty = a.Issue.Kind == MetadataIssueKind.InvalidQuantity;
                    if (isQty) badQuantity = true;
                    else badMaterial = true;
                    part.Escalate(PartStatus.Ambiguous, string.Format(CultureInfo.InvariantCulture,
                        "{0} KHONG HOP LE trong chu \"{1}\" ({2}) - phai sua / xac nhan",
                        isQty ? "SL" : "Vat lieu", a.Issue.Token, a.How));
                    continue;
                }

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
                if (badQuantity)
                {
                    part.Escalate(PartStatus.Ambiguous, "Chua co SL hop le -> TAM dat " + rules.DefaultQuantity.ToString(CultureInfo.InvariantCulture) + ", phai sua");
                }
                else if (part.Status < PartStatus.Ambiguous)
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
                if (badMaterial)
                {
                    part.Escalate(PartStatus.Ambiguous, "Chua co vat lieu hop le -> TAM dat " + rules.DefaultMaterial + ", phai sua");
                }
                else if (part.Status < PartStatus.Ambiguous)
                {
                    part.Escalate(PartStatus.Warning, "Khong tim thay vat lieu -> mac dinh " + rules.DefaultMaterial);
                }
            }
        }

        /// <summary>
        /// Chi tiet NHO NHAT ma diem nam trong VAT LIEU cua no. Diem nam trong lo cua khung khong
        /// thuoc khung (truoc day chi xet vong ngoai nen chu cua chi tiet nho trong lo bi gan cho
        /// khung - hoac bi mang di cat vao khoang trong).
        /// </summary>
        private static RecognizedPart InnermostContaining(List<RecognizedPart> parts, Pt p)
        {
            RecognizedPart best = null;
            foreach (RecognizedPart part in parts)
            {
                if (best != null && part.Outer.Area >= best.Outer.Area) continue;
                if (PartRecognizer.InMaterial(part, p)) best = part;
            }

            return best;
        }

        /// <summary>Khoang cach toi HINH cua chi tiet: vong ngoai VA mep lo (chu nam trong lo gan mep lo).</summary>
        private static double DistanceToPart(RecognizedPart part, Pt p)
        {
            double d = DistanceToOutline(part.Outer.Points, p);
            foreach (RecognizedLoop h in part.Holes) d = Math.Min(d, DistanceToOutline(h.Points, p));
            return d;
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
