using System;
using System.Collections.Generic;
using AUTOCAD_COMMANDS.Nesting.Recognition;
using static AUTOCAD_COMMANDS.Nesting.SelfTests.NestingTestHarness;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    /// <summary>Tests for contour building, SL/material parsing and text association (no AutoCAD).</summary>
    public static class RecognitionSelfTests
    {
        public static void Run(NestingTestReport report)
        {
            NestingTestHarness.Run(report, "R01. Doc SL nhieu dinh dang", R01_QuantityFormats);
            NestingTestHarness.Run(report, "R02. Doc vat lieu nhieu dinh dang", R02_MaterialFormats);
            NestingTestHarness.Run(report, "R03. Khong qua de dai (150MM, ASL 3, SL 0)", R03_NotTooPermissive);
            NestingTestHarness.Run(report, "R04. Duong bao tu 4 LINE roi -> 1 chi tiet", R04_LinesFormOnePart);
            NestingTestHarness.Run(report, "R05. Lo ben trong + chi tiet nam trong lo", R05_HoleAndPartInHole);
            NestingTestHarness.Run(report, "R06. Duong bao ho -> INVALID GEOMETRY", R06_OpenContourInvalid);
            NestingTestHarness.Run(report, "R07. Duong chan ben trong -> marking, khong loi", R07_BendLineInsideIsMarking);
            NestingTestHarness.Run(report, "R08. Duong bao tu cat -> INVALID GEOMETRY", R08_SelfIntersectionInvalid);
            NestingTestHarness.Run(report, "R09. Text trong chi tiet duoc gan", R09_TextInside);
            NestingTestHarness.Run(report, "R10. Text NGOAI chi tiet -> gan chi tiet gan nhat", R10_TextOutsideNearest);
            NestingTestHarness.Run(report, "R11. Text o giua 2 chi tiet -> AMBIGUOUS", R11_TextAmbiguous);
            NestingTestHarness.Run(report, "R12. Thieu SL -> 1, thieu vat lieu -> 1.2MM", R12_Defaults);
            NestingTestHarness.Run(report, "R13. Hai SL khac nhau cho 1 chi tiet -> AMBIGUOUS", R13_ConflictingQuantities);
            NestingTestHarness.Run(report, "R14. Text qua xa -> canh bao chung", R14_TextTooFar);
            NestingTestHarness.Run(report, "R15. Hinh re nhanh -> INVALID GEOMETRY", R15_BranchingInvalid);
            NestingTestHarness.Run(report, "R16. Duong trung nhau (dien tich 0) -> INVALID", R16_ZeroArea);
            NestingTestHarness.Run(report, "R17. Ban ghi mo ho chua xac nhan bi chan", R17_UnconfirmedAmbiguousBlocked);
            NestingTestHarness.Run(report, "R18. 1 block chua 2 chi tiet -> INVALID (khong nhan doi)", R18_SharedSourceInvalid);
            NestingTestHarness.Run(report, "R19. Hinh tren layer danh dau gan vao chi tiet chua no", R19_AttachMarking);
            NestingTestHarness.Run(report, "R20. SL trong + vat lieu ngoai (2 vi tri khac nhau)", R20_SlInsideMaterialOutside);
            NestingTestHarness.Run(report, "R21. Vat lieu ngoai, SL ngoai o 2 phia", R21_BothOutsideDifferentSides);
            NestingTestHarness.Run(report, "R22. Nguong khoang cach: 300 gan, 300.01 khong", R22_DistanceThresholdExact);
            NestingTestHarness.Run(report, "R23. Do day ngoai khoang 0.3-25 bi loai", R23_ThicknessRange);
            NestingTestHarness.Run(report, "R24. Chi tiet toan doan thang -> dung sai 0; co cung -> dung sai cung", R24_ToleranceFlag);
            NestingTestHarness.Run(report, "R25. Text gan 2 chi tiet khac nhau ro -> gan dung, khong mo ho", R25_ClearlyCloserNotAmbiguous);
        }

        private static void R20_SlInsideMaterialOutside()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100), Box(600, 0, 200, 100) },
                Text("SL: 12", 100, 50), Text("1.5MM", 100, -25));
            RecognizedPart a = Valid(r).Find(p => p.MinX < 1), b = Valid(r).Find(p => p.MinX > 1);
            Equal(12, a.Quantity, "SL inside");
            Equal("1.5MM", a.Material, "material outside below");
            Equal(PartStatus.Ok, a.Status, "A ok");
            Equal("1.2MM", b.Material, "B untouched -> default");
            Equal(PartStatus.Warning, b.Status, "B defaults visible");
        }

        private static void R21_BothOutsideDifferentSides()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100) },
                Text("SL:4", 100, 130), Text("1,2 MM", 250, 50));
            RecognizedPart a = Valid(r)[0];
            Equal(4, a.Quantity, "SL above");
            Equal("1.2MM", a.Material, "material to the right");
            True(a.QuantityFromText && a.MaterialFromText, "both from text");
        }

        private static void R22_DistanceThresholdExact()
        {
            RecognitionResult near = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 3", 400, 50));   // exactly 300
            Equal(3, Valid(near)[0].Quantity, "at the threshold -> assigned");
            RecognitionResult far = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 3", 400.01, 50));
            Equal(1, Valid(far)[0].Quantity, "beyond -> not assigned");
            Equal(1, far.GlobalWarnings.Count, "and reported");
        }

        private static void R23_ThicknessRange()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            Equal(0, p.Parse("0.1MM").Count, "0.1 below range");
            Equal(0, p.Parse("30MM").Count, "30 above range");
            Equal("0.3MM", p.Parse("0.3MM")[0].Value, "lower bound inclusive");
            Equal("25MM", p.Parse("25MM")[0].Value, "upper bound inclusive");
        }

        private static void R24_ToleranceFlag()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 100, 100),
                new CurveChain(_src++, new List<Pt> { new Pt(300, 0), new Pt(400, 0), new Pt(400, 100), new Pt(300, 100) }, true, true)
            };
            RecognitionResult r = Recognize(c);
            foreach (RecognizedPart p in r.Parts) p.Include = true;
            List<Core.PartGroup> groups = PartRecognizer.ToPartGroups(r.Parts, 0.05);
            Core.PartGroup exact = groups.Find(g => ((RecognizedPart)g.SourceReference).MinX < 1);
            Core.PartGroup arc = groups.Find(g => ((RecognizedPart)g.SourceReference).MinX > 1);
            Close(0.0, exact.Shape.ToleranceMm, 1e-12, "line-only part is an exact polygon");
            Close(0.05, arc.Shape.ToleranceMm, 1e-12, "curve part carries the chord tolerance");
        }

        private static void R25_ClearlyCloserNotAmbiguous()
        {
            // 10 mm from A, 40 mm from B: clearly A.
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(250, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 9", 210, 50));
            RecognizedPart a = Valid(r).Find(p => p.MinX < 1), b = Valid(r).Find(p => p.MinX > 1);
            Equal(9, a.Quantity, "nearest");
            True(a.Status != PartStatus.Ambiguous && b.Status != PartStatus.Ambiguous, "not ambiguous");
            Equal(1, b.Quantity, "B untouched");
        }

        private static void R18_SharedSourceInvalid()
        {
            // Two closed loops coming from the same source entity (e.g. one exploded block).
            List<CurveChain> c = new List<CurveChain>
            {
                new CurveChain(900, new List<Pt> { new Pt(0, 0), new Pt(100, 0), new Pt(100, 100), new Pt(0, 100) }, true),
                new CurveChain(900, new List<Pt> { new Pt(300, 0), new Pt(400, 0), new Pt(400, 100), new Pt(300, 100) }, true)
            };
            RecognitionResult r = Recognize(c);
            Equal(2, r.Parts.Count, "two records");
            foreach (RecognizedPart p in r.Parts) Equal(PartStatus.InvalidGeometry, p.Status, "shared source flagged");
        }

        private static void R19_AttachMarking()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 200, 100), Box(500, 0, 50, 50) });
            True(PartRecognizer.AttachMarking(r.Parts, 777, new List<Pt> { new Pt(60, 0), new Pt(60, 100) }), "inside first part");
            True(!PartRecognizer.AttachMarking(r.Parts, 778, new List<Pt> { new Pt(250, 0), new Pt(260, 10) }), "outside every part");
            RecognizedPart big = Valid(r).Find(p => p.Outer.Area > 10000);
            True(big.MarkingSources.Contains(777) && big.GeometrySources.Contains(777), "attached as marking + output source");
        }

        private static int _src;

        private static CurveChain Chain(bool closed, params double[] xy)
        {
            List<Pt> pts = new List<Pt>();
            for (int i = 0; i + 1 < xy.Length; i += 2) pts.Add(new Pt(xy[i], xy[i + 1]));
            return new CurveChain(_src++, pts, closed);
        }

        private static CurveChain Box(double x, double y, double w, double h)
        {
            return Chain(true, x, y, x + w, y, x + w, y + h, x, y + h);
        }

        private static TextItem Text(string s, double x, double y)
        {
            return new TextItem(_src++, s, new Pt(x, y));
        }

        private static RecognitionResult Recognize(List<CurveChain> chains, params TextItem[] texts)
        {
            return new PartRecognizer(new RecognitionSettings()).Recognize(chains, texts);
        }

        private static List<RecognizedPart> Valid(RecognitionResult r)
        {
            return r.Parts.FindAll(p => p.Outer != null);
        }

        private static void R01_QuantityFormats()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            foreach (string s in new[] { "SL: 12", "SL:12", "SL 12", "sl: 12", "SL=12", "  SL :  12 " })
            {
                List<MetadataFact> f = p.Parse(s);
                True(f.Count == 1 && f[0].Kind == MetadataKind.Quantity && f[0].QuantityValue == 12, "parse '" + s + "'");
            }

            List<MetadataFact> both = p.Parse("SL: 4\n1.2MM");
            Equal(2, both.Count, "SL and material in one MTEXT");
        }

        private static void R02_MaterialFormats()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            foreach (string s in new[] { "1.2MM", "1.2 MM", "1,2MM", "1,2 MM", "1.2mm", "INOX 1.2MM" })
            {
                List<MetadataFact> f = p.Parse(s);
                True(f.Count == 1 && f[0].Kind == MetadataKind.Material && f[0].Value == "1.2MM", "parse '" + s + "'");
            }

            Equal("1.5MM", p.Parse("1.50 mm")[0].Value, "normalised");
            Equal("2MM", p.Parse("2MM")[0].Value, "integer thickness");
        }

        private static void R03_NotTooPermissive()
        {
            MetadataParser p = new MetadataParser(new MetadataRules());
            Equal(0, p.Parse("150MM").Count, "150 mm is a dimension, not a thickness");
            Equal(0, p.Parse("ASL 3").Count, "SL must not be part of another word");
            Equal(0, p.Parse("SL: 0").Count, "quantity 0 rejected");
            Equal(0, p.Parse("SL: 1.5").Count, "decimal quantity rejected");
            Equal(0, p.Parse("MAT BICH").Count, "plain text");
            Equal(0, p.Parse("12.5.3MM").Count, "garbage number");
        }

        private static void R04_LinesFormOnePart()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 50, 0, 50),          // drawn in reverse direction
                Chain(false, 100, 0, 100, 50),
                Chain(false, 0, 50, 0.01, 0.01)        // 0.014 mm gap -> joined (tol 0.05)
            };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "one record");
            RecognizedPart part = r.Parts[0];
            True(part.IsNestable, "valid part: " + part.NotesText);
            Equal(4, part.GeometrySources.Count, "all 4 lines belong to the part");
            Close(5000, part.Outer.Area, 2, "area");
        }

        private static void R05_HoleAndPartInHole()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 300, 300),
                Box(50, 50, 200, 200),     // hole
                Box(100, 100, 50, 50)      // separate part inside the hole
            };
            List<RecognizedPart> parts = Valid(Recognize(c));
            Equal(2, parts.Count, "frame + inner part");
            RecognizedPart frame = parts.Find(p => p.Outer.Area > 80000);
            Equal(1, frame.Holes.Count, "frame has one hole");
            RecognizedPart inner = parts.Find(p => p.Outer.Area < 5000);
            Equal(0, inner.Holes.Count, "inner part has no hole");
        }

        private static void R06_OpenContourInvalid()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 0, 100, 50),
                Chain(false, 100, 50, 0, 50),
                Chain(false, 0, 50, 0, 2)              // 2 mm gap
            };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "one record");
            Equal(PartStatus.InvalidGeometry, r.Parts[0].Status, "open contour is invalid");
            True(!r.Parts[0].Include, "excluded by default");
            True(r.Parts[0].NotesText.Contains("HO"), "reason mentions open contour: " + r.Parts[0].NotesText);
        }

        private static void R07_BendLineInsideIsMarking()
        {
            List<CurveChain> c = new List<CurveChain>
            {
                Box(0, 0, 200, 100),
                Chain(false, 60, 0, 60, 100),          // bend line touching the outline
                Chain(false, 140, 0, 140, 100)
            };
            List<RecognizedPart> parts = Valid(Recognize(c));
            Equal(1, parts.Count, "one part");
            True(parts[0].IsNestable, "still nestable");
            Equal(2, parts[0].MarkingSources.Count, "2 markings kept for output");
        }

        private static void R08_SelfIntersectionInvalid()
        {
            List<CurveChain> c = new List<CurveChain> { Chain(true, 0, 0, 100, 100, 100, 0, 0, 100) };
            RecognitionResult r = Recognize(c);
            Equal(PartStatus.InvalidGeometry, r.Parts[0].Status, "bow-tie is invalid");
        }

        private static void R09_TextInside()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(300, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 50, 50), Text("1.5MM", 60, 30), Text("SL: 4", 400, 50));
            List<RecognizedPart> parts = Valid(r);
            RecognizedPart a = parts.Find(p => p.MinX < 1), b = parts.Find(p => p.MinX > 1);
            Equal(12, a.Quantity, "A qty");
            Equal("1.5MM", a.Material, "A material");
            Equal(4, b.Quantity, "B qty");
            Equal("1.2MM", b.Material, "B default material");
            Equal(PartStatus.Ok, a.Status, "A ok");
        }

        private static void R10_TextOutsideNearest()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(600, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 100, -20), Text("1.2MM", 100, -40), Text("SL: 4", 700, 130));
            List<RecognizedPart> parts = Valid(r);
            RecognizedPart a = parts.Find(p => p.MinX < 1), b = parts.Find(p => p.MinX > 1);
            Equal(12, a.Quantity, "A takes the text below it");
            Equal(4, b.Quantity, "B takes the text above it");
            True(a.QuantityFromText && a.MaterialFromText, "A from text");
        }

        private static void R11_TextAmbiguous()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(260, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 230, 50));   // 30 mm from both
            foreach (RecognizedPart p in Valid(r))
            {
                Equal(PartStatus.Ambiguous, p.Status, "both flagged");
                Equal(1, p.Quantity, "value not silently assigned");
            }
        }

        private static void R12_Defaults()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) });
            RecognizedPart p = Valid(r)[0];
            Equal(1, p.Quantity, "default qty");
            Equal("1.2MM", p.Material, "default material");
            Equal(PartStatus.Warning, p.Status, "defaults are visible as warning");
            True(p.NotesText.Contains("mac dinh"), "note explains default");
        }

        private static void R13_ConflictingQuantities()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 2", 50, 50), Text("SL: 4", 50, 20));
            Equal(PartStatus.Ambiguous, Valid(r)[0].Status, "two SL values");
        }

        private static void R14_TextTooFar()
        {
            RecognitionResult r = Recognize(new List<CurveChain> { Box(0, 0, 100, 100) }, Text("SL: 7", 5000, 5000));
            Equal(1, r.GlobalWarnings.Count, "warning for far text");
            Equal(1, Valid(r)[0].Quantity, "not assigned");
        }

        private static void R15_BranchingInvalid()
        {
            // A square whose outline lines meet a third line at the same vertex (T-junction at node).
            List<CurveChain> c = new List<CurveChain>
            {
                Chain(false, 0, 0, 100, 0),
                Chain(false, 100, 0, 100, 100),
                Chain(false, 100, 100, 0, 100),
                Chain(false, 0, 100, 0, 0),
                Chain(false, 100, 0, 300, 0),
                Chain(false, 300, 0, 300, 100),
                Chain(false, 300, 100, 100, 100)
            };
            RecognitionResult r = Recognize(c);
            True(r.Parts.Exists(p => p.Status == PartStatus.InvalidGeometry && p.NotesText.Contains("re nhanh")),
                "branching reported, not guessed");
        }

        private static void R16_ZeroArea()
        {
            List<CurveChain> c = new List<CurveChain> { Chain(false, 0, 0, 100, 0), Chain(false, 100, 0, 0, 0) };
            RecognitionResult r = Recognize(c);
            Equal(1, r.Parts.Count, "one record");
            Equal(PartStatus.InvalidGeometry, r.Parts[0].Status, "zero area invalid");
        }

        private static void R17_UnconfirmedAmbiguousBlocked()
        {
            List<CurveChain> c = new List<CurveChain> { Box(0, 0, 200, 100), Box(260, 0, 200, 100) };
            RecognitionResult r = Recognize(c, Text("SL: 12", 230, 50));
            bool threw = false;
            try
            {
                PartRecognizer.ToPartGroups(r.Parts, 0.05);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            True(threw, "unconfirmed ambiguous record must block nesting");

            foreach (RecognizedPart p in r.Parts) p.Confirmed = true;
            Equal(2, PartRecognizer.ToPartGroups(r.Parts, 0.05).Count, "after confirmation");
        }
    }
}
