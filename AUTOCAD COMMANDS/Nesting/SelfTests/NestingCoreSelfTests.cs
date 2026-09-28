using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using AUTOCAD_COMMANDS.Nesting.Core;
using static AUTOCAD_COMMANDS.Nesting.SelfTests.NestingTestHarness;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    /// <summary>Unit tests for Nesting/Core. Pure C#, no AutoCAD. All checks use real polygon geometry.</summary>
    public static class NestingCoreSelfTests
    {
        public static void Run(NestingTestReport report)
        {
            NestingTestHarness.Run(report, "C01. Mot chi tiet chu nhat - dat dung tai (EdgeMargin, EdgeMargin)", C01_OneRectangle);
            NestingTestHarness.Run(report, "C02. Nhieu chi tiet giong nhau - khe do duoc = Gap", C02_IdenticalRectangles);
            NestingTestHarness.Run(report, "C03. Chu nhat nhieu kich thuoc", C03_DifferentRectangles);
            NestingTestHarness.Run(report, "C04. Xoay 90 do khi bat buoc", C04_RotationRequired);
            NestingTestHarness.Run(report, "C05. Nhieu to phoi", C05_MultipleSheets);
            NestingTestHarness.Run(report, "C06. Chi tiet lon hon kho phoi", C06_PartLargerThanSheet);
            NestingTestHarness.Run(report, "C07. Engine: vua dung Gap -> 1 to, thieu 0.001 -> 2 to", C07_EngineExactGap);
            NestingTestHarness.Run(report, "E1. To 100x100, margin 5, chi tiet 90x90 tai (5,5) -> VALID", E1_ExactEdgeMarginValid);
            NestingTestHarness.Run(report, "E2. Dich them 0.001 -> INVALID (moi phia)", E2_EdgeMarginPlusOneUnitInvalid);
            NestingTestHarness.Run(report, "E3. Hai chi tiet cach dung Gap -> VALID (ca hinh xien)", E3_ExactGapValid);
            NestingTestHarness.Run(report, "E4. Nho hon Gap 0.001 -> INVALID (ca hinh xien)", E4_GapMinusOneUnitInvalid);
            NestingTestHarness.Run(report, "E5. Gap KHONG anh huong EdgeMargin (va nguoc lai)", E5_GapIndependentOfEdgeMargin);
            NestingTestHarness.Run(report, "E6. Engine dat 90x90 dung (5,5) tren to 100x100", E6_EngineExactFit);
            NestingTestHarness.Run(report, "E7. Chi tiet co cung: margin/gap cong dung sai day cung", E7_ToleranceOnlyForArcParts);
            NestingTestHarness.Run(report, "E8. Gap = 0: van cam chong / cham", E8_ZeroGapStillNoOverlap);
            NestingTestHarness.Run(report, "C09. Chi tiet lom (chu L) nhieu cai", C09_ConcavePart);
            NestingTestHarness.Run(report, "N-A. BAT BUOC: chu nhat vao hoc chu L", NA_RectangleIntoLNotch);
            NestingTestHarness.Run(report, "N-B. BAT BUOC: chu nhat vao hoc chu U", NB_RectangleIntoUPocket);
            NestingTestHarness.Run(report, "N-C. BAT BUOC: chu nhat vao mieng chu C (khong xoay)", NC_RectangleIntoCMouth);
            NestingTestHarness.Run(report, "H-C. Lo kin + AllowPartInsideHole=false -> khong dat vao lo", HC_ClosedHoleDisallowed);
            NestingTestHarness.Run(report, "H-D. Lo kin + AllowPartInsideHole=true -> dat vao lo", HD_ClosedHoleAllowed);
            NestingTestHarness.Run(report, "H-E. Lo khong du lon -> khong dat vao lo", HE_HoleTooSmall);
            NestingTestHarness.Run(report, "C12. SL 7 -> dung 7 placement, id rieng", C12_QuantityExpansion);
            NestingTestHarness.Run(report, "C12b. SL tach nhieu to: tong = SL", C12b_QuantitySplitAcrossSheets);
            NestingTestHarness.Run(report, "C13. Tach vat lieu - khong ghep chung", C13_MaterialGrouping);
            NestingTestHarness.Run(report, "C13b. Validator bat chi tiet 1.2 tren to 1.5", C13b_ValidatorMaterialMismatch);
            NestingTestHarness.Run(report, "C14. Chi tiet khong xep duoc duoc bao cao", C14_UnplacedReported);
            NestingTestHarness.Run(report, "C15. Validator bat chong hinh", C15_ValidatorOverlap);
            NestingTestHarness.Run(report, "C16. Validator bat khe ho khong du", C16_ValidatorInsufficientGap);
            NestingTestHarness.Run(report, "C17. Validator bat chi tiet ra ngoai to", C17_ValidatorOutOfSheet);
            NestingTestHarness.Run(report, "C18. Cung input + seed -> cung ket qua (3 lan)", C18_Deterministic);
            NestingTestHarness.Run(report, "C18b. Chay song song == chay tuan tu", C18b_ParallelEqualsSequential);
            NestingTestHarness.Run(report, "C19. Validator bat chi tiet nam trong vat lieu chi tiet khac", C19_ValidatorContainment);
            NestingTestHarness.Run(report, "C20. Validator bat thieu / trung / thua", C20_ValidatorQuantity);
            NestingTestHarness.Run(report, "C21. Mac dinh KHONG lat guong; validator chan lat guong", C21_MirrorOffByDefault);
            NestingTestHarness.Run(report, "C22. Loai huong trung do doi xung (ca khi bat lat guong)", C22_SymmetricOrientationsDeduplicated);
            NestingTestHarness.Run(report, "C23. Hop bao chong nhau nhung da giac khong cham", C23_BoundingBoxOverlapButNoCollision);
            NestingTestHarness.Run(report, "C24. Fixture ghi/doc giu nguyen du lieu", C24_FixtureRoundTrip);
            NestingTestHarness.Run(report, "C25. Hieu nang ~100 chi tiet hon hop", C25_Performance);
            NestingTestHarness.Run(report, "C26. Xep hang: it to thang du utilization thap hon", C26_EvaluatorOrder);
            NestingTestHarness.Run(report, "C27. Transform: engine == validator == cong thuc (8 huong)", C27_TransformConsistency);
            NestingTestHarness.Run(report, "C28. Lat guong BAT: ket qua hop le, chi dung huong cho phep", C28_MirrorOnValid);
            NestingTestHarness.Run(report, "C29. Vuot thoi gian cho phep thi phai bao", C29_TimeBudgetOvershootIsReported);
            NestingTestHarness.Run(report, "C30. Tien do chay 0 -> 100%, khong lui", C30_ProgressReachesHundred);
            NestingTestHarness.Run(report, "C31. Han muc theo KHOI LUONG VIEC: song song == tuan tu du het gio", C31_WorkBudgetIsMachineIndependent);
            NestingTestHarness.Run(report, "C32. Muc Nhanh == V1 (ghim bo cuc HEAD 731002d)", C32_FastEqualsV1);
            NestingTestHarness.Run(report, "C33. Muc Can bang khong bao gio te hon Nhanh", C33_BalancedNeverWorse);
            NestingTestHarness.Run(report, "C34. Can bang: song song == tuan tu", C34_BalancedParallelEqualsSequential);
            NestingTestHarness.Run(report, "C35. Can bang: chay lai 3 lan cung ket qua", C35_BalancedDeterministic);
            NestingTestHarness.Run(report, "C36. Cat som == khong cat (cung ket qua)", C36_PruningDoesNotChangeResult);
            NestingTestHarness.Run(report, "C37. Lan can doi cho: khac nhom, trong cua so, khong trung", C37_NeighbourGeneration);
            NestingTestHarness.Run(report, "C38. Can bang khong qua validator -> lui ve nghiem V1", C38_InvalidBalancedFallsBackToV1);
            NestingTestHarness.Run(report, "C39. Muc tim kiem: Clone + fixture ghi/doc", C39_SearchEffortSettingRoundTrip);
            NestingTestHarness.Run(report, "C40. Can bang: tien do cham 100%", C40_BalancedProgressReachesHundred);
            NestingTestHarness.Run(report, "C41. Can bang: bam Dung giu ket qua tot nhat", C41_BalancedCancelKeepsBest);
            NestingTestHarness.Run(report, "C42. Can bang: dong ho chi co tac dung khi tat Tim du", C42_BalancedClockOnlyWhenNotDeterministic);
            NestingTestHarness.Run(report, "C43. Kernel diem-trong-da-giac moi == GeometryMath (diem hiem + ngau nhien)", C43_PointKernelEquivalence);
            NestingTestHarness.Run(report, "C44. Collides moi == Collides cu (vi tri ngau nhien, co/khong lo kin)", C44_CollidesLegacyEquivalence);
            NestingTestHarness.Run(report, "C45. Ca bo ghep: kernel moi == cu (bo cuc + so lan goi Collides)", C45_EngineLegacyKernelEquivalence);
            NestingTestHarness.Run(report, "C46. NaN / vo cuc / qua lon (khe, le, kho) -> tu choi ro rang", C46_NonsenseInputsRejected);
            NestingTestHarness.Run(report, "C47. Fuzz co seed (200 bai): validator + kiem doc lap + du SL + tai lap", C47_FuzzProperties);
            NestingTestHarness.Run(report, "C48. Dau vao benh ly: khong crash, khong treo, khong bao DAT sai", C48_PathologicalInputs);
        }

        // ==================================================================================
        // helpers
        // ==================================================================================

        internal static NestingRequest Request(double sheetLength, double sheetWidth, double gap = 5, double margin = 5)
        {
            NestingRequest r = new NestingRequest();
            r.DefaultSheet = new SheetSpec("TEST", sheetLength, sheetWidth);
            r.Settings.GapMm = gap;
            r.Settings.EdgeMarginMm = margin;
            r.Settings.TimeBudgetSeconds = 120;
            r.Settings.ExtraSeededOrderings = 1;
            return r;
        }

        internal static PartGroup Rect(string id, double w, double h, int qty, string material = "1.2MM")
        {
            return new PartGroup(id, new PartShape(PolyShape.Rectangle(w, h)), qty, material);
        }

        internal static PartGroup Poly(string id, int qty, double[] outer, params double[][] holes)
        {
            return PolyTol(id, qty, 0.0, outer, holes);
        }

        internal static PartGroup PolyTol(string id, int qty, double tol, double[] outer, params double[][] holes)
        {
            List<IList<IntPoint>> hs = new List<IList<IntPoint>>();
            foreach (double[] h in holes) hs.Add(Pts(h));
            return new PartGroup(id, new PartShape(PolyShape.Create(Pts(outer), hs), tol), qty, "1.2MM");
        }

        private static List<IntPoint> Pts(double[] xy)
        {
            List<IntPoint> p = new List<IntPoint>();
            for (int i = 0; i + 1 < xy.Length; i += 2) p.Add(IntPoint.FromMm(xy[i], xy[i + 1]));
            return p;
        }

        internal static NestingResult Nest(NestingRequest r)
        {
            return new SimpleNestingEngine().Nest(r, CancellationToken.None, null);
        }

        private static void AssertValid(NestingResult res)
        {
            if (!res.Validation.IsValid)
            {
                throw new NestingAssertException("validator: " + res.Validation.Issues[0]);
            }
        }

        private static ValidationResult Check(NestingRequest r, params Placement[] placements)
        {
            return new NestingValidator().Validate(r, ManualResult(r, placements));
        }

        private static List<Placement> AllPlacements(NestingResult res)
        {
            return new List<Placement>(res.Placements);
        }

        private static PolyShape World(NestingRequest r, Placement p)
        {
            foreach (PartGroup g in r.Groups)
            {
                if (g.Id == p.PartGroupId) return g.Shape.Polygon.Transform(p.Orientation, p.TranslationX, p.TranslationY);
            }

            throw new NestingAssertException("group not found " + p.PartGroupId);
        }

        private static Placement Find(NestingResult res, string group)
        {
            foreach (Placement p in res.Placements)
            {
                if (p.PartGroupId == group) return p;
            }

            throw new NestingAssertException("no placement for " + group);
        }

        /// <summary>A result where the placements are the whole request (missing copies reported unplaced).</summary>
        private static NestingResult ManualResult(NestingRequest r, params Placement[] placements)
        {
            NestingResult res = new NestingResult();
            SheetResult s = new SheetResult(0, "1.2MM", r.DefaultSheet) { NumberInMaterial = 1 };
            s.Placements.AddRange(placements);
            res.Sheets.Add(s);

            HashSet<string> placed = new HashSet<string>();
            foreach (Placement p in placements) placed.Add(p.InstanceId);
            foreach (PartGroup g in r.Groups)
            {
                for (int k = 1; k <= g.Quantity; k++)
                {
                    string id = g.Id + "#" + k.ToString(CultureInfo.InvariantCulture);
                    if (!placed.Contains(id)) res.Unplaced.Add(new UnplacedPart(id, g.Id, "manual"));
                }
            }

            return res;
        }

        private static Placement At(string group, int copy, double x, double y, double rot = 0, bool mirror = false)
        {
            return new Placement
            {
                InstanceId = group + "#" + copy.ToString(CultureInfo.InvariantCulture),
                PartGroupId = group,
                SheetIndex = 0,
                RotationDeg = rot,
                Mirror = mirror,
                TranslationX = NestUnits.ToUnits(x),
                TranslationY = NestUnits.ToUnits(y)
            };
        }

        private static readonly double[] LShape = { 0, 0, 300, 0, 300, 80, 80, 80, 80, 300, 0, 300 };

        // Right triangle with the hypotenuse x + y = 100 facing up-right.
        private static readonly double[] TriA = { 0, 0, 100, 0, 0, 100 };

        // Right triangle with the hypotenuse x + y = 100 facing down-left (occupies x, y <= 100).
        private static readonly double[] TriB = { 100, 0, 100, 100, 0, 100 };

        // ==================================================================================
        // basic
        // ==================================================================================

        private static void C01_OneRectangle()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 50, 1));
            NestingResult res = Nest(r);

            AssertValid(res);
            Equal(1, res.Sheets.Count, "sheets");
            Equal(1, res.Statistics.PlacedQuantity, "placed");
            LongRect b = World(r, AllPlacements(res)[0]).Bounds;
            Equal(5000L, b.MinX, "left edge exactly EdgeMargin (units)");
            Equal(5000L, b.MinY, "bottom edge exactly EdgeMargin (units)");
            Close(5.0, res.Validation.MinEdgeDistanceMm, 1e-9, "measured edge distance");
        }

        private static void C02_IdenticalRectangles()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 50, 20));
            NestingResult res = Nest(r);

            AssertValid(res);
            Equal(1, res.Sheets.Count, "20 x (100x50) fit one 1000x500 sheet");
            Equal(20, res.Statistics.PlacedQuantity, "placed");
            Close(5.0, res.Validation.MinPartDistanceMm, 1e-9, "tight packing: measured gap == Gap");
        }

        private static void C03_DifferentRectangles()
        {
            NestingRequest r = Request(1500, 1000);
            r.Groups.Add(Rect("A", 400, 300, 3));
            r.Groups.Add(Rect("B", 200, 150, 6));
            r.Groups.Add(Rect("C", 90, 60, 10));
            NestingResult res = Nest(r);

            AssertValid(res);
            Equal(19, res.Statistics.PlacedQuantity, "placed");
            Equal(1, res.Sheets.Count, "sheets");
        }

        private static void C04_RotationRequired()
        {
            // 400 long does not fit the 300 sheet length, but fits across the 500 width.
            NestingRequest r = Request(300, 500);
            r.Groups.Add(Rect("A", 400, 100, 1));
            NestingResult res = Nest(r);

            AssertValid(res);
            Equal(1, res.Statistics.PlacedQuantity, "placed");
            double rot = AllPlacements(res)[0].RotationDeg;
            True(Math.Abs(rot - 90) < 1e-9 || Math.Abs(rot - 270) < 1e-9, "must be rotated 90/270, was " + rot);

            NestingRequest r0 = Request(300, 500);
            r0.Settings.AllowedRotations = new List<double> { 0 };
            r0.Groups.Add(Rect("A", 400, 100, 1));
            NestingResult res0 = Nest(r0);
            Equal(1, res0.Unplaced.Count, "without rotation it cannot be placed");
            AssertValid(res0);
        }

        private static void C05_MultipleSheets()
        {
            NestingRequest r = Request(500, 500);
            r.Groups.Add(Rect("A", 400, 400, 3));
            NestingResult res = Nest(r);

            AssertValid(res);
            Equal(3, res.Sheets.Count, "one 400x400 per 500x500 sheet");
            foreach (SheetResult s in res.Sheets) Equal(1, s.Placements.Count, "per sheet");
        }

        private static void C06_PartLargerThanSheet()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("BIG", 2000, 2000, 2));
            NestingResult res = Nest(r);

            AssertValid(res);
            Equal(0, res.Sheets.Count, "no sheet opened");
            Equal(2, res.Unplaced.Count, "both copies reported");
            True(res.Unplaced[0].Reason.Contains("khong vua"), "reason explains size: " + res.Unplaced[0].Reason);
        }

        private static void C07_EngineExactGap()
        {
            // Two 100x100 side by side: L = 5 + 100 + 5 + 100 + 5 = 215 exactly; W = 110 = one row.
            NestingRequest fits = Request(215, 110);
            fits.Groups.Add(Rect("A", 100, 100, 2));
            NestingResult a = Nest(fits);
            AssertValid(a);
            Equal(1, a.Sheets.Count, "exactly enough room -> 1 sheet");
            Close(5.0, a.Validation.MinPartDistanceMm, 1e-9, "gap exactly 5");

            NestingRequest tight = Request(214.999, 110);
            tight.Groups.Add(Rect("A", 100, 100, 2));
            NestingResult b = Nest(tight);
            AssertValid(b);
            Equal(2, b.Sheets.Count, "0.001 mm short -> second sheet");
        }

        // ==================================================================================
        // EdgeMargin / Gap semantics (real distances, independent)
        // ==================================================================================

        private static void E1_ExactEdgeMarginValid()
        {
            NestingRequest r = Request(100, 100, gap: 5, margin: 5);
            r.Groups.Add(Rect("A", 90, 90, 1));
            ValidationResult v = Check(r, At("A", 1, 5, 5));
            True(v.IsValid, "90x90 at (5,5) on 100x100 with margin 5: " + (v.IsValid ? "" : v.Issues[0].ToString()));
            Close(5.0, v.MinEdgeDistanceMm, 1e-9, "edge distance measured on the polygon");
        }

        private static void E2_EdgeMarginPlusOneUnitInvalid()
        {
            NestingRequest r = Request(100, 100, gap: 5, margin: 5);
            r.Groups.Add(Rect("A", 90, 90, 1));
            foreach (double[] d in new[] { new[] { 0.001, 0 }, new[] { -0.001, 0 }, new[] { 0, 0.001 }, new[] { 0, -0.001 } })
            {
                ValidationResult v = Check(r, At("A", 1, 5 + d[0], 5 + d[1]));
                True(v.Has(ValidationIssueKind.EdgeMargin), string.Format(CultureInfo.InvariantCulture, "shift ({0},{1}) must violate EdgeMargin", d[0], d[1]));
            }

            // Non-rectangular: a triangle whose tip is 0.001 mm too close to the right edge.
            NestingRequest t = Request(200, 200, gap: 5, margin: 5);
            t.Groups.Add(Poly("T", 1, TriA));
            True(Check(t, At("T", 1, 95, 5)).IsValid, "triangle tip exactly 5 mm from the right edge");
            True(Check(t, At("T", 1, 95.001, 5)).Has(ValidationIssueKind.EdgeMargin), "triangle tip 4.999 mm from the right edge");
        }

        private static void E3_ExactGapValid()
        {
            NestingRequest r = Request(1000, 500, gap: 5, margin: 5);
            r.Groups.Add(Rect("A", 100, 100, 2));
            ValidationResult v = Check(r, At("A", 1, 10, 10), At("A", 2, 115, 10));
            True(v.IsValid, "exactly 5 mm apart: " + (v.IsValid ? "" : v.Issues[0].ToString()));
            Close(5.0, v.MinPartDistanceMm, 1e-9, "measured");

            // Diagonal: parallel hypotenuses, bounding boxes overlap, polygon distance sqrt(2)*t.
            NestingRequest d = Request(1000, 500, gap: 5, margin: 5);
            d.Groups.Add(Poly("TA", 1, TriA));
            d.Groups.Add(Poly("TB", 1, TriB));
            ValidationResult vd = Check(d, At("TA", 1, 10, 10), At("TB", 1, 10 + 3.536, 10 + 3.536));
            True(vd.IsValid, "diagonal distance 5.0008 mm is valid");
            True(vd.MinPartDistanceMm >= 5.0 && vd.MinPartDistanceMm < 5.001, "polygon (not bbox) distance measured: " + vd.MinPartDistanceMm);
        }

        private static void E4_GapMinusOneUnitInvalid()
        {
            NestingRequest r = Request(1000, 500, gap: 5, margin: 5);
            r.Groups.Add(Rect("A", 100, 100, 2));
            ValidationResult v = Check(r, At("A", 1, 10, 10), At("A", 2, 114.999, 10));
            True(v.Has(ValidationIssueKind.InsufficientGap), "4.999 mm apart must be rejected");
            True(!v.Has(ValidationIssueKind.Overlap), "it is a gap violation, not an overlap");

            NestingRequest d = Request(1000, 500, gap: 5, margin: 5);
            d.Groups.Add(Poly("TA", 1, TriA));
            d.Groups.Add(Poly("TB", 1, TriB));
            True(Check(d, At("TA", 1, 10, 10), At("TB", 1, 10 + 3.535, 10 + 3.535)).Has(ValidationIssueKind.InsufficientGap),
                "diagonal distance 4.9992 mm must be rejected");

            // The engine's collision model agrees with the validator on the same geometry.
            PolygonCollisionModel cm = new PolygonCollisionModel(r.Settings);
            PreparedShape s = cm.Prepare(r.Groups[0], OrientationTransform.Identity);
            PlacedShape placed = cm.Place(s, NestUnits.ToUnits(10), NestUnits.ToUnits(10));
            True(!cm.Collides(s, NestUnits.ToUnits(115), NestUnits.ToUnits(10), placed), "collision model: exactly Gap is free");
            True(cm.Collides(s, NestUnits.ToUnits(114.999), NestUnits.ToUnits(10), placed), "collision model: 0.001 less collides");
        }

        private static void E5_GapIndependentOfEdgeMargin()
        {
            foreach (double gap in new[] { 0.0, 5.0, 50.0 })
            {
                NestingRequest r = Request(100, 100, gap: gap, margin: 5);
                r.Groups.Add(Rect("A", 90, 90, 1));
                True(Check(r, At("A", 1, 5, 5)).IsValid, "gap " + gap + " must not change the edge requirement");
                NestingResult res = Nest(r);
                Equal(1, res.Statistics.PlacedQuantity, "engine places it for gap " + gap);
                Equal(5000L, World(r, AllPlacements(res)[0]).Bounds.MinX, "at x = 5 for gap " + gap);
            }

            foreach (double margin in new[] { 0.0, 5.0, 20.0 })
            {
                NestingRequest r = Request(1000, 500, gap: 5, margin: margin);
                r.Groups.Add(Rect("A", 100, 100, 2));
                True(Check(r, At("A", 1, 30, 30), At("A", 2, 135, 30)).IsValid, "margin " + margin + " must not change the gap requirement");
                True(Check(r, At("A", 1, 30, 30), At("A", 2, 134.999, 30)).Has(ValidationIssueKind.InsufficientGap), "gap still 5 for margin " + margin);
            }

            ClearanceRules rules = new ClearanceRules(Request(100, 100, gap: 5, margin: 7).Settings);
            PartShape exact = new PartShape(PolyShape.Rectangle(10, 10));
            Equal(7000L, rules.BoundaryInset(exact), "boundary inset = EdgeMargin only");
            Equal(5000L, rules.PartClearance(exact, exact), "part clearance = Gap only");
        }

        private static void E6_EngineExactFit()
        {
            NestingRequest r = Request(100, 100, gap: 5, margin: 5);
            r.Groups.Add(Rect("A", 90, 90, 1));
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(1, res.Statistics.PlacedQuantity, "90x90 fits 100x100 with margin 5");
            LongRect b = World(r, AllPlacements(res)[0]).Bounds;
            Equal(5000L, b.MinX, "x");
            Equal(5000L, b.MinY, "y");

            NestingRequest over = Request(100, 100, gap: 5, margin: 5);
            over.Groups.Add(Rect("A", 90.001, 90, 1));
            Equal(1, Nest(over).Unplaced.Count, "0.001 mm too big -> unplaced");
        }

        private static void E7_ToleranceOnlyForArcParts()
        {
            // Same 90x90 outline, but flagged as an arc approximation (tol 0.05): the real part may
            // stick out 0.05 mm, so it needs 5.05 mm to the edge.
            NestingRequest r = Request(100, 100, gap: 5, margin: 5);
            r.Groups.Add(PolyTol("A", 1, 0.05, new double[] { 0, 0, 90, 0, 90, 90, 0, 90 }));
            Equal(1, Nest(r).Unplaced.Count, "arc part does not fit a sheet that only fits the exact polygon");
            True(Check(r, At("A", 1, 5, 5)).Has(ValidationIssueKind.EdgeMargin), "validator adds the tolerance");

            NestingRequest g = Request(1000, 500, gap: 5, margin: 5);
            g.Groups.Add(PolyTol("A", 1, 0.05, new double[] { 0, 0, 100, 0, 100, 100, 0, 100 }));
            g.Groups.Add(Rect("B", 100, 100, 1));
            True(Check(g, At("A", 1, 10, 10), At("B", 1, 115.05, 10)).IsValid, "gap 5 + 0.05 (one arc part)");
            True(Check(g, At("A", 1, 10, 10), At("B", 1, 115.049, 10)).Has(ValidationIssueKind.InsufficientGap), "one unit less");
        }

        private static void E8_ZeroGapStillNoOverlap()
        {
            NestingRequest r = Request(1000, 500, gap: 0, margin: 0);
            r.Groups.Add(Rect("A", 100, 100, 2));
            True(Check(r, At("A", 1, 0, 0), At("A", 2, 100.001, 0)).IsValid, "0.001 apart ok with gap 0");
            True(Check(r, At("A", 1, 0, 0), At("A", 2, 100, 0)).Has(ValidationIssueKind.Overlap), "touching edges = overlap");
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(2, res.Statistics.PlacedQuantity, "placed");
        }

        // ==================================================================================
        // concave nesting (forced) and closed holes
        // ==================================================================================

        private static void C09_ConcavePart()
        {
            NestingRequest r = Request(1000, 700);
            r.Groups.Add(Poly("L", 6, LShape));
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(6, res.Statistics.PlacedQuantity, "placed");
            Equal(1, res.Sheets.Count, "sheets");
        }

        /// <summary>
        /// Both parts on one 310x310 sheet is only possible with B inside A's notch: A alone takes
        /// the full usable 300x300 area. Checks B lies inside A's bounding box, which (A having
        /// no holes and no overlap being allowed) means inside the notch.
        /// </summary>
        private static void AssertInsideNotch(NestingRequest r, NestingResult res, string a, string b)
        {
            AssertValid(res);
            Equal(2, res.Statistics.PlacedQuantity, "both placed");
            Equal(1, res.Sheets.Count, "one sheet -> B must be in the notch");
            LongRect ba = World(r, Find(res, a)).Bounds, bb = World(r, Find(res, b)).Bounds;
            True(bb.MinX >= ba.MinX && bb.MaxX <= ba.MaxX && bb.MinY >= ba.MinY && bb.MaxY <= ba.MaxY,
                "B's bounding box lies within A's bounding box (the notch)");
            True(res.Validation.MinEdgeDistanceMm >= 5.0, "edge margin");
            True(res.Validation.MinPartDistanceMm >= 5.0, "gap " + res.Validation.MinPartDistanceMm);
        }

        private static void NA_RectangleIntoLNotch()
        {
            // L 300x300 with 100 mm arms; notch = [100,300] x [100,300]; 180x180 needs 5 mm each side.
            NestingRequest r = Request(310, 310);
            r.Groups.Add(Poly("L", 1, new double[] { 0, 0, 300, 0, 300, 100, 100, 100, 100, 300, 0, 300 }));
            r.Groups.Add(Rect("B", 180, 180, 1));
            AssertInsideNotch(r, Nest(r), "L", "B");
        }

        private static void NB_RectangleIntoUPocket()
        {
            double[] u = { 0, 0, 300, 0, 300, 300, 250, 300, 250, 50, 50, 50, 50, 300, 0, 300 };
            NestingRequest r = Request(310, 310);
            r.Groups.Add(Poly("U", 1, u));
            r.Groups.Add(Rect("B", 150, 150, 1));
            AssertInsideNotch(r, Nest(r), "U", "B");
        }

        private static void NC_RectangleIntoCMouth()
        {
            // C opening to the left (-X), mouth 200 tall and 250 deep; no rotation allowed, so B
            // has to enter the mouth from the sheet's left margin.
            double[] c = { 0, 0, 300, 0, 300, 300, 0, 300, 0, 250, 250, 250, 250, 50, 0, 50 };
            NestingRequest r = Request(310, 310);
            r.Settings.AllowedRotations = new List<double> { 0 };
            r.Groups.Add(Poly("C", 1, c));
            r.Groups.Add(Rect("B", 150, 150, 1));
            AssertInsideNotch(r, Nest(r), "C", "B");
        }

        private static readonly double[] Frame = { 0, 0, 300, 0, 300, 300, 0, 300 };
        private static readonly double[] FrameHole = { 50, 50, 250, 50, 250, 250, 50, 250 };

        private static void HC_ClosedHoleDisallowed()
        {
            NestingRequest r = Request(310, 310);
            True(!r.Settings.AllowPartInsideHole, "AllowPartInsideHole defaults to false");
            r.Groups.Add(Poly("F", 1, Frame, FrameHole));
            r.Groups.Add(Rect("S", 100, 100, 1));
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(2, res.Sheets.Count, "the square is NOT put into the closed hole");

            ValidationResult v = Check(r, At("F", 1, 5, 5), At("S", 1, 105, 105));
            True(v.Has(ValidationIssueKind.PartInsideHole), "validator rejects a manual part-in-hole placement");

            // The collision model gives the same answer.
            PolygonCollisionModel cm = new PolygonCollisionModel(r.Settings);
            PlacedShape frame = cm.Place(cm.Prepare(r.Groups[0], OrientationTransform.Identity), 5000, 5000);
            True(cm.Collides(cm.Prepare(r.Groups[1], OrientationTransform.Identity), 105000, 105000, frame), "collision model rejects it too");
        }

        private static void HD_ClosedHoleAllowed()
        {
            NestingRequest r = Request(310, 310);
            r.Settings.AllowPartInsideHole = true;
            r.Groups.Add(Poly("F", 1, Frame, FrameHole));
            r.Groups.Add(Rect("S", 100, 100, 1));
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(1, res.Sheets.Count, "square inside the frame hole");
            True(res.Validation.MinPartDistanceMm >= 5.0, "clearance to the hole wall");
            True(Check(r, At("F", 1, 5, 5), At("S", 1, 105, 105)).IsValid, "validator accepts part-in-hole when allowed");
        }

        private static void HE_HoleTooSmall()
        {
            // Hole 200 wide: 190 + 2 x 5 = 200 fits exactly, 190.001 does not.
            NestingRequest ok = Request(310, 310);
            ok.Settings.AllowPartInsideHole = true;
            ok.Groups.Add(Poly("F", 1, Frame, FrameHole));
            ok.Groups.Add(Rect("S", 190, 190, 1));
            NestingResult a = Nest(ok);
            AssertValid(a);
            Equal(1, a.Sheets.Count, "190 fits the 200 hole with 5 mm each side");

            NestingRequest big = Request(310, 310);
            big.Settings.AllowPartInsideHole = true;
            big.Groups.Add(Poly("F", 1, Frame, FrameHole));
            big.Groups.Add(Rect("S", 190.001, 190.001, 1));
            NestingResult b = Nest(big);
            AssertValid(b);
            Equal(2, b.Sheets.Count, "0.001 mm too big for the hole -> separate sheet");
            True(Check(big, At("F", 1, 5, 5), At("S", 1, 59.9995, 59.9995)).Has(ValidationIssueKind.InsufficientGap), "validator: too close to hole wall");
        }

        // ==================================================================================
        // quantities / materials
        // ==================================================================================

        private static void C12_QuantityExpansion()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 50, 50, 7));
            NestingResult res = Nest(r);
            AssertValid(res);

            HashSet<string> ids = new HashSet<string>();
            foreach (Placement p in res.Placements) ids.Add(p.InstanceId);
            Equal(7, AllPlacements(res).Count, "exactly 7 placements");
            Equal(7, ids.Count, "7 distinct instances");
            True(ids.Contains("A#1") && ids.Contains("A#7"), "instance ids A#1..A#7");
        }

        private static void C12b_QuantitySplitAcrossSheets()
        {
            NestingRequest r = Request(420, 420);
            r.Groups.Add(Rect("A", 200, 200, 7));   // 4 per sheet (5+200+5+200+5 = 415)
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(2, res.Sheets.Count, "sheets");
            Equal(4, res.Sheets[0].Placements.Count, "first sheet full");
            Equal(3, res.Sheets[1].Placements.Count, "rest on second");
            Equal(7, AllPlacements(res).Count, "sum over sheets == quantity");
        }

        private static void C13_MaterialGrouping()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 2, "1.2MM"));
            r.Groups.Add(Rect("B", 100, 100, 2, "1.5MM"));
            NestingResult res = Nest(r);
            AssertValid(res);

            Equal(2, res.Sheets.Count, "one sheet per material although all would fit one");
            foreach (SheetResult s in res.Sheets)
            {
                foreach (Placement p in s.Placements)
                {
                    string expected = p.PartGroupId == "A" ? "1.2MM" : "1.5MM";
                    Equal(expected, s.Material, "sheet material of " + p.InstanceId);
                }
            }

            Equal(2, res.Statistics.Materials.Count, "stats per material");
        }

        private static void C13b_ValidatorMaterialMismatch()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 1, "1.2MM"));
            NestingResult res = new NestingResult();
            SheetResult s = new SheetResult(0, "1.5MM", r.DefaultSheet) { NumberInMaterial = 1 };
            s.Placements.Add(At("A", 1, 10, 10));
            res.Sheets.Add(s);
            True(new NestingValidator().Validate(r, res).Has(ValidationIssueKind.MaterialMismatch), "1.2MM part on a 1.5MM sheet");
        }

        private static void C14_UnplacedReported()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 12));
            r.Groups.Add(Rect("B", 200, 100, 4));
            r.Groups.Add(Rect("C", 1200, 100, 2));   // longer than the sheet in both directions
            NestingResult res = Nest(r);
            AssertValid(res);

            Equal(16, res.Statistics.PlacedQuantity, "A=12, B=4 placed");
            Equal(2, res.Statistics.UnplacedQuantity, "C=2 unplaced");
            Equal(18, res.Statistics.RequestedQuantity, "requested");
            foreach (UnplacedPart u in res.Unplaced)
            {
                Equal("C", u.PartGroupId, "unplaced group");
                True(!string.IsNullOrEmpty(u.Reason), "has reason");
            }
        }

        // ==================================================================================
        // validator
        // ==================================================================================

        private static void C15_ValidatorOverlap()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 2));
            True(Check(r, At("A", 1, 20, 20), At("A", 2, 60, 20)).Has(ValidationIssueKind.Overlap), "overlap detected");
        }

        private static void C16_ValidatorInsufficientGap()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 2));
            ValidationResult v = Check(r, At("A", 1, 20, 20), At("A", 2, 122, 20));   // 2 mm apart
            True(v.Has(ValidationIssueKind.InsufficientGap), "gap violation detected");
            True(!v.Has(ValidationIssueKind.Overlap), "not an overlap");
        }

        private static void C17_ValidatorOutOfSheet()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 2));
            ValidationResult v = Check(r, At("A", 1, -10, 20), At("A", 2, 300, 2));
            True(v.Has(ValidationIssueKind.OutsideSheet), "outside detected");
            True(v.Has(ValidationIssueKind.EdgeMargin), "edge margin detected (2 mm < 5 mm)");
        }

        private static string Signature(NestingResult res)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Placement p in res.Placements)
            {
                sb.Append(p.InstanceId).Append('@').Append(p.SheetIndex).Append(':')
                  .Append(p.TranslationX).Append(',').Append(p.TranslationY).Append(',')
                  .Append(p.RotationDeg.ToString(CultureInfo.InvariantCulture)).Append(p.Mirror ? "M" : "").Append(';');
            }

            return sb.ToString();
        }

        private static void C18_Deterministic()
        {
            Func<string> once = () =>
            {
                NestingRequest r = Request(1200, 600);
                r.Settings.Seed = 42;
                r.Settings.ExtraSeededOrderings = 3;
                r.Settings.AllowMirror = true;
                r.Groups.Add(Poly("L", 5, LShape));
                r.Groups.Add(Rect("A", 120, 70, 9));
                r.Groups.Add(Rect("B", 60, 45, 11));
                NestingResult res = Nest(r);
                AssertValid(res);
                return Signature(res);
            };

            string a = once(), b = once(), c = once();
            True(a.Length > 0, "has placements");
            Equal(a, b, "run 2 identical");
            Equal(a, c, "run 3 identical");
        }

        private static void C18b_ParallelEqualsSequential()
        {
            Func<int, string> run = degree =>
            {
                NestingRequest r = Request(1200, 600);
                r.Settings.Seed = 7;
                r.Settings.ExtraSeededOrderings = 3;
                r.Settings.MaxParallelism = degree;
                r.Groups.Add(Poly("L", 6, LShape));
                r.Groups.Add(Poly("P", 4, Chiral));
                r.Groups.Add(Rect("A", 120, 70, 9));
                NestingResult res = Nest(r);
                AssertValid(res);
                return Signature(res);
            };

            string sequential = run(1);
            Equal(sequential, run(0), "parallel (all cores) == sequential");
            Equal(sequential, run(3), "3 threads == sequential");
        }

        private static void C19_ValidatorContainment()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("BIG", 300, 300, 1));
            r.Groups.Add(Rect("S", 50, 50, 1));
            True(Check(r, At("BIG", 1, 20, 20), At("S", 1, 100, 100)).Has(ValidationIssueKind.Overlap), "part inside material = overlap");
        }

        private static void C20_ValidatorQuantity()
        {
            NestingRequest r = Request(1000, 500);
            r.Groups.Add(Rect("A", 100, 100, 3));
            Func<NestingResult> baseResult = () =>
            {
                NestingResult res = new NestingResult();
                res.Sheets.Add(new SheetResult(0, "1.2MM", r.DefaultSheet) { NumberInMaterial = 1 });
                return res;
            };

            // duplicate + missing
            NestingResult dup = baseResult();
            dup.Sheets[0].Placements.Add(At("A", 1, 20, 20));
            dup.Sheets[0].Placements.Add(At("A", 1, 200, 20));
            dup.Sheets[0].Placements.Add(At("A", 2, 400, 20));
            ValidationResult v1 = new NestingValidator().Validate(r, dup);
            True(v1.Has(ValidationIssueKind.DuplicatePlacement), "duplicate detected");
            True(v1.Has(ValidationIssueKind.QuantityMismatch), "A#3 missing detected");

            // missing only (2 of 3, nothing reported unplaced)
            NestingResult missing = baseResult();
            missing.Sheets[0].Placements.Add(At("A", 1, 20, 20));
            missing.Sheets[0].Placements.Add(At("A", 2, 200, 20));
            True(new NestingValidator().Validate(r, missing).Has(ValidationIssueKind.QuantityMismatch), "missing detected");

            // extra (a 4th copy of a quantity-3 part)
            NestingResult extra = baseResult();
            for (int k = 1; k <= 4; k++) extra.Sheets[0].Placements.Add(At("A", k, 20 + 150 * (k - 1), 20));
            ValidationResult v3 = new NestingValidator().Validate(r, extra);
            True(v3.Has(ValidationIssueKind.ExtraPlacement), "extra detected");
            True(v3.Has(ValidationIssueKind.QuantityMismatch), "count 4 != 3");

            // exactly right
            NestingResult ok = baseResult();
            for (int k = 1; k <= 3; k++) ok.Sheets[0].Placements.Add(At("A", k, 20 + 150 * (k - 1), 20));
            True(new NestingValidator().Validate(r, ok).IsValid, "3 of 3 valid");
        }

        // ==================================================================================
        // orientation / mirror
        // ==================================================================================

        // Chiral part: its mirror image is not a rotation of it.
        private static readonly double[] Chiral = { 0, 0, 300, 0, 300, 60, 100, 60, 100, 200, 0, 200 };

        private static void C21_MirrorOffByDefault()
        {
            NestingRequest r = Request(1000, 700);
            True(!r.Settings.AllowMirror, "AllowMirror defaults to false");
            r.Groups.Add(Poly("P", 8, Chiral));
            NestingResult res = Nest(r);
            AssertValid(res);
            foreach (Placement p in res.Placements) True(!p.Mirror, "no mirrored placement");

            True(Check(r, At("P", 1, 20, 20, 0, true)).Has(ValidationIssueKind.InvalidTransform), "validator rejects mirror when not allowed");
            True(Check(r, At("P", 1, 20, 20, 45)).Has(ValidationIssueKind.InvalidTransform), "validator rejects a non-allowed rotation");
        }

        private static void C22_SymmetricOrientationsDeduplicated()
        {
            NestingSettings off = new NestingSettings();
            NestingSettings on = new NestingSettings { AllowMirror = true };
            BasicRotationCandidateProvider p = new BasicRotationCandidateProvider();
            Equal(2, p.GetOrientations(Rect("R", 100, 50, 1), off).Count, "rectangle: 0 and 90");
            Equal(2, p.GetOrientations(Rect("R", 100, 50, 1), on).Count, "rectangle + mirror: still 2");
            Equal(1, p.GetOrientations(Rect("Q", 80, 80, 1), on).Count, "square: one orientation");
            Equal(4, p.GetOrientations(Poly("L", 1, LShape), off).Count, "L: 4 rotations");
            Equal(4, p.GetOrientations(Poly("L", 1, LShape), on).Count, "L is achiral: mirrored copies are duplicates");
            Equal(4, p.GetOrientations(Poly("P", 1, Chiral), off).Count, "chiral: 4 without mirror");
            Equal(8, p.GetOrientations(Poly("P", 1, Chiral), on).Count, "chiral: 8 with mirror");
        }

        private static void C27_TransformConsistency()
        {
            // Hand formula: mirror x -> -x, then rotate CCW.
            Func<long, long, double, bool, IntPoint> formula = (x, y, rot, mirror) =>
            {
                long mx = mirror ? -x : x;
                switch ((int)Math.Round(rot))
                {
                    case 0: return new IntPoint(mx, y);
                    case 90: return new IntPoint(-y, mx);
                    case 180: return new IntPoint(-mx, -y);
                    default: return new IntPoint(y, -mx);
                }
            };

            NestingSettings s = new NestingSettings { AllowMirror = true };
            PolygonCollisionModel cm = new PolygonCollisionModel(s);
            List<PartGroup> shapes = new List<PartGroup>
            {
                Rect("R", 120, 40, 1),
                Poly("L", 1, LShape),
                Poly("P", 1, Chiral)
            };

            foreach (PartGroup g in shapes)
            {
                foreach (bool mirror in new[] { false, true })
                {
                    foreach (double rot in new[] { 0.0, 90.0, 180.0, 270.0 })
                    {
                        OrientationTransform o = new OrientationTransform(rot, mirror);
                        long tx = 123456, ty = 654321;
                        PolyShape engine = cm.Place(cm.Prepare(g, o), tx, ty).Shape;
                        Placement pl = new Placement { RotationDeg = rot, Mirror = mirror, TranslationX = tx, TranslationY = ty };
                        PolyShape validator = g.Shape.Polygon.Transform(pl.Orientation, pl.TranslationX, pl.TranslationY);

                        HashSet<IntPoint> expected = new HashSet<IntPoint>();
                        foreach (IntPoint v in g.Shape.Polygon.Outer)
                        {
                            IntPoint f = formula(v.X, v.Y, rot, mirror);
                            expected.Add(new IntPoint(f.X + tx, f.Y + ty));
                        }

                        string tag = g.Id + " rot " + rot + (mirror ? " mirror" : "");
                        Equal(expected.Count, engine.Outer.Length, tag + " vertex count");
                        foreach (IntPoint v in engine.Outer) True(expected.Contains(v), tag + ": engine vertex == formula");
                        foreach (IntPoint v in validator.Outer) True(expected.Contains(v), tag + ": validator vertex == formula");
                        Equal(Math.Round(g.Shape.Polygon.NetArea), Math.Round(engine.NetArea), tag + " area preserved");
                    }
                }
            }
        }

        /// <summary>
        /// HOI QUY (ban ve san xuat that): dat thoi gian cho phep 15 s, lan ghep chay 169,7 s
        /// ma bao cao KHONG he noi gi - vi phep kiem thoi gian chi chan duoc nhung lan chay
        /// chua bat dau, ma tren may 16 nhan thi ca 16 lan chay deu kip bat dau ngay.
        ///
        /// Bo fixture tong hop khong bat duoc loi nay vi moi fixture chi chay vai mili giay,
        /// khong bao gio cham toi thoi gian cho phep.
        ///
        /// Phep thu nay kiem dung BAT BIEN da hua: chay lau hon thoi gian cho phep thi phai
        /// duoc BAO. No khong doi hoi phai dung dung luc - viec cat ngang mot lan chay dang
        /// do lai la chuyen khac.
        /// </summary>
        /// <summary>
        /// Thanh tien trinh chi co nghia khi con so bao ra la that: khong duoc lui, va phai
        /// den dung 100% khi xong. Phep thu nay con giu cho MultiOrderOptimizer.RunCount khong
        /// lech voi so luot thuc su chay - neu ai do them mot thu tu xep moi ma quen sua hang
        /// so thi tien do se khong bao gio den 100% va phep thu do ngay.
        /// </summary>
        private static void C30_ProgressReachesHundred()
        {
            NestingRequest r = Request(2500, 1250);
            r.Settings.ExtraSeededOrderings = 2;
            r.Groups.Add(Rect("A", 300, 200, 6));
            r.Groups.Add(Rect("B", 180, 120, 5, "1.5MM"));      // vat lieu thu hai

            // Ham bao tien do duoc goi TU NHIEU LUONG cung luc (cac luot ghep chay song song),
            // nen cho nao nhan tien do cung phai tu lo an toan luong - ke ca phep thu nay.
            List<NestingProgress> seen = new List<NestingProgress>();
            object gate = new object();
            NestingResult res = new SimpleNestingEngine().Nest(r, CancellationToken.None,
                p => { lock (gate) { seen.Add(p); } });

            AssertValid(res);
            True(seen.Count > 0, "phai co bao tien do");

            int expected = 2 * MultiOrderOptimizer.RunCount(r.Settings);
            Equal(expected, seen[seen.Count - 1].Total, "tong so luot phai dung");

            // Cac luot chay song song nen lan bao ve khong dam bao dung thu tu - bat buoc
            // tung lan phai tang dan la doi hoi sai. Bat bien that la: moi con so deu nam
            // trong khoang hop le, va gia tri LON NHAT phai cham day (do la cai hop thoai ve).
            int high = 0;
            foreach (NestingProgress p in seen)
            {
                True(p.Total > 0, "moi lan bao phai co tong");
                True(p.Done >= 0 && p.Done <= p.Total, "khong duoc vuot tong: " + p.Done + "/" + p.Total);
                Equal(expected, p.Total, "tong phai giong nhau o moi lan bao");
                high = Math.Max(high, p.Done);
            }

            Equal(expected, high, "tien do phai cham 100% khi xong");
        }

        /// <summary>
        /// Het gio thi phai BAO, khong duoc im lang - nhung "het gio" bay gio chi co nghia o
        /// che do cu (co cat bot theo dong ho). O che do tat dinh, chay qua gio KHONG phai la
        /// van de: khong luot nao bi bo, ket qua van day du va van lap lai duoc.
        /// </summary>
        private static void C29_TimeBudgetOvershootIsReported()
        {
            // Ep chay TUAN TU cho nhanh che do cu: chay song song thi moi luot deu kip khoi
            // dong truoc khi het gio, nen gan nhu khong luot nao bi bo - dung cai da lam phep
            // thu nay im lang di qua truoc day.
            Func<bool, NestingResult> nest = deterministic =>
            {
                NestingRequest r = Request(2500, 1250);
                r.Settings.TimeBudgetSeconds = 0.05;
                r.Settings.ExtraSeededOrderings = 3;
                r.Settings.DeterministicSearch = deterministic;
                r.Settings.MaxParallelism = 1;

                for (int i = 0; i < 20; i++)
                {
                    r.Groups.Add(Rect("R" + i.ToString(CultureInfo.InvariantCulture), 90 + i, 60 + (i % 7), 3));
                }

                Stopwatch watch = Stopwatch.StartNew();
                NestingResult res = Nest(r);
                watch.Stop();

                AssertValid(res);

                // Neu lan ghep nay lai chay nhanh hon thoi gian cho phep thi phep thu thanh vo
                // nghia - noi ro ra thay vi lang le di qua.
                True(watch.Elapsed.TotalSeconds > r.Settings.TimeBudgetSeconds,
                    "phep thu chi co nghia khi lan ghep chay lau hon thoi gian cho phep");
                Equal(1, res.Statistics.Materials.Count, "mot vat lieu");
                return res;
            };

            // Che do cu: co cat bot, va phai bao la da cat.
            MaterialStatistics old = nest(false).Statistics.Materials[0];
            True(old.TimeBudgetHit, "che do cu: het gio thi phai bao, khong duoc im lang");
            True(old.OrderingsTried < old.OrderingsPlanned,
                "che do cu: het gio thi phai co luot bi bo (" + old.OrderingsTried + "/" + old.OrderingsPlanned + ")");

            // Che do tat dinh: khong cat bot gi ca, nen khong co gi de bao.
            MaterialStatistics now = nest(true).Statistics.Materials[0];
            Equal(now.OrderingsPlanned, now.OrderingsTried, "che do tat dinh: phai chay DU so luot da dinh");
            True(!now.TimeBudgetHit, "che do tat dinh: chay qua gio khong phai la mat mat, khong bao het gio");
            True(now.ElapsedSeconds > 0, "phai bao ca thoi gian thuc te da chay");
        }

        /// <summary>
        /// PHEP KIEM CHUNG BAT BUOC: cung dau vao + cung han muc KHOI LUONG VIEC + khac che do
        /// chay (song song / tuan tu) => ket qua GIONG HET.
        ///
        /// Han muc gio de cuc nho co chu dich: theo cach cu thi chac chan bi cat bot, va tap
        /// luot chay duoc se khac nhau giua hai che do - dung cai da lam ban ve that cua nguoi
        /// dung ra hai ket qua lech nhau 2%.
        /// </summary>
        private static void C31_WorkBudgetIsMachineIndependent()
        {
            Func<int, bool, NestingResult> nest = (degree, deterministic) =>
            {
                NestingRequest r = Request(2500, 1250);
                r.Settings.Seed = 11;
                r.Settings.ExtraSeededOrderings = 3;
                r.Settings.TimeBudgetSeconds = 0.001;
                r.Settings.DeterministicSearch = deterministic;
                r.Settings.MaxParallelism = degree;

                for (int i = 0; i < 18; i++)
                {
                    r.Groups.Add(Rect("R" + i.ToString(CultureInfo.InvariantCulture), 110 + i * 3, 70 + (i % 5) * 4, 3));
                }

                NestingResult res = Nest(r);
                AssertValid(res);
                return res;
            };

            NestingResult sequential = nest(1, true);
            NestingResult parallel = nest(0, true);
            NestingResult threeThreads = nest(3, true);

            Equal(Signature(sequential), Signature(parallel), "tuan tu == song song (toan bo nhan)");
            Equal(Signature(sequential), Signature(threeThreads), "tuan tu == 3 luong");

            // Va ly do no giong nhau: ca hai deu chay DU so luot da dinh.
            foreach (NestingResult res in new[] { sequential, parallel, threeThreads })
            {
                MaterialStatistics m = res.Statistics.Materials[0];
                Equal(m.OrderingsPlanned, m.OrderingsTried, "phai chay du so luot da dinh");
            }

            // Kiem nguoc: theo cach cu, chinh bo du lieu nay BI cat bot - tuc la phep thu tren
            // that su dang kiem mot thu co the sai, chu khong phai mot thu luon dung.
            MaterialStatistics oldWay = nest(1, false).Statistics.Materials[0];
            True(oldWay.OrderingsTried < oldWay.OrderingsPlanned,
                "phep thu chi co nghia neu cach cu that su cat bot (" + oldWay.OrderingsTried + "/" + oldWay.OrderingsPlanned + ")");
        }

        private static void C28_MirrorOnValid()
        {
            NestingRequest r = Request(1000, 700);
            r.Settings.AllowMirror = true;
            r.Groups.Add(Poly("P", 8, Chiral));
            r.Groups.Add(Poly("L", 4, LShape));
            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(12, res.Statistics.PlacedQuantity, "placed");
        }

        private static void C23_BoundingBoxOverlapButNoCollision()
        {
            // Two L shapes interlocked: bounding boxes overlap, polygons keep the gap.
            NestingRequest r = Request(1000, 1000);
            r.Groups.Add(Poly("L", 2, LShape));
            ValidationResult v = Check(r, At("L", 1, 20, 20), At("L", 2, 20 + 300 + 20 + 90, 20 + 300 + 20 + 90, 180));
            True(v.IsValid, "interlocked L parts are valid: " + (v.IsValid ? "" : v.Issues[0].ToString()));

            PolyShape a = World(r, At("L", 1, 20, 20));
            PolyShape b = World(r, At("L", 2, 20 + 300 + 20 + 90, 20 + 300 + 20 + 90, 180));
            True(a.Bounds.Overlaps(b.Bounds, 0), "bounding boxes overlap");
        }

        private static void C24_FixtureRoundTrip()
        {
            NestingRequest r = Request(3000, 1500, gap: 6, margin: 4);
            r.Settings.AllowMirror = true;
            r.Settings.AllowPartInsideHole = true;
            r.Groups.Add(Poly("F", 3, Frame, FrameHole));
            r.Groups.Add(PolyTol("ARC", 2, 0.05, new double[] { 0, 0, 100, 0, 100, 50 }));
            r.Groups.Add(Rect("A", 120.125, 70.5, 9, "1.5MM"));
            r.SheetByMaterial["1.5MM"] = new SheetSpec("1250x2500", 2500, 1250);

            string text = NestingFixture.Write(r, "roundtrip");
            NestingFixture.Fixture fx = NestingFixture.Parse(text);
            Equal(text, NestingFixture.Write(fx.Request, "roundtrip"), "write(parse(write(x))) == write(x)");
            Equal(3, fx.Request.Groups.Count, "groups");
            Equal(1, fx.Request.Groups[0].Shape.Polygon.Holes.Length, "hole kept");
            Close(0.05, fx.Request.Groups[1].Shape.ToleranceMm, 1e-12, "part tolerance kept");
            Close(0.0, fx.Request.Groups[0].Shape.ToleranceMm, 1e-12, "exact part tolerance 0");
            Close(6, fx.Request.Settings.GapMm, 1e-9, "gap");
            True(fx.Request.Settings.AllowMirror, "mirror flag");
            True(fx.Request.Settings.AllowPartInsideHole, "inhole flag");
            Equal("1250x2500", fx.Request.ResolveSheet("1.5MM").Name, "material sheet");

            // Legacy fixtures: SETTINGS tol= is the default for PART lines without tol=.
            NestingFixture.Fixture legacy = NestingFixture.Parse(
                "SETTINGS gap=5 margin=5 tol=0.05\nSHEET name=S length=100 width=100\nPART id=A qty=1 material=1.2MM\nOUTER 0,0 10,0 10,10\nEND\n");
            Close(0.05, legacy.Request.Groups[0].Shape.ToleranceMm, 1e-12, "legacy default tolerance");
        }

        private static void C25_Performance()
        {
            NestingRequest r = Request(3000, 1500);
            r.Settings.ExtraSeededOrderings = 0;
            r.Groups.Add(Poly("L", 10, LShape));
            r.Groups.Add(Poly("F", 5, Frame, FrameHole));
            r.Groups.Add(Rect("A", 250, 120, 20));
            r.Groups.Add(Rect("B", 90, 60, 25));

            // A 64-gon "disc" with a round hole, like an arc-approximated flange.
            List<IntPoint> disc = new List<IntPoint>(), bore = new List<IntPoint>();
            for (int i = 0; i < 64; i++)
            {
                double t = 2 * Math.PI * i / 64;
                disc.Add(IntPoint.FromMm(100 + 100 * Math.Cos(t), 100 + 100 * Math.Sin(t)));
                bore.Add(IntPoint.FromMm(100 + 30 * Math.Cos(t), 100 + 30 * Math.Sin(t)));
            }

            r.Groups.Add(new PartGroup("D", new PartShape(PolyShape.Create(disc, new[] { bore }), 0.05), 20, "1.2MM"));

            Stopwatch w = Stopwatch.StartNew();
            NestingResult res = Nest(r);
            w.Stop();
            AssertValid(res);
            Equal(80, res.Statistics.PlacedQuantity + res.Statistics.UnplacedQuantity, "all accounted");
            Equal(0, res.Statistics.UnplacedQuantity, "all placed");
            True(w.Elapsed.TotalSeconds < 60, "should finish well under a minute, took " + w.Elapsed.TotalSeconds);
        }

        private static void C26_EvaluatorOrder()
        {
            LexicographicSolutionEvaluator e = new LexicographicSolutionEvaluator();

            // A: two sheets, each packed tightly (high utilization of the used area).
            DecodedLayout a = new DecodedLayout();
            a.Sheets.Add(new DecodedSheet { MaxX = 1000000, MaxY = 1000000 });
            a.Sheets.Add(new DecodedSheet { MaxX = 300000, MaxY = 300000 });
            // B: one sheet, longer and looser (lower utilization), but fewer sheets.
            DecodedLayout b = new DecodedLayout();
            b.Sheets.Add(new DecodedSheet { MaxX = 2900000, MaxY = 1400000 });
            True(e.Compare(b, a) < 0, "fewer sheets wins over better utilization / shorter length");
            True(e.Compare(a, b) > 0, "and the comparison is antisymmetric");

            DecodedLayout shorter = new DecodedLayout();
            shorter.Sheets.Add(new DecodedSheet { MaxX = 2000000, MaxY = 1400000 });
            True(e.Compare(shorter, b) < 0, "same sheet count -> shorter used length wins");

            DecodedLayout tighter = new DecodedLayout();
            tighter.Sheets.Add(new DecodedSheet { MaxX = 2000000, MaxY = 1000000 });
            True(e.Compare(tighter, shorter) < 0, "same sheets and length -> tighter wins");

            DecodedLayout withUnplaced = new DecodedLayout();
            withUnplaced.Unplaced.Add(new UnplacedPart("x", "g", "r"));
            True(e.Compare(a, withUnplaced) < 0, "placing everything beats fewer sheets");
            Equal(0, e.Compare(b, b), "equal to itself");
        }

        // ==================================================================================
        // MUC TIM KIEM (SearchEffort): Nhanh = V1, Can bang = V1 + doi cho tung chi tiet
        // ==================================================================================

        /// <summary>SHA1 (16 ky tu dau) cua <see cref="Signature"/> - de ghim bo cuc V1.</summary>
        private static string SignatureHash(NestingResult res)
        {
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(Signature(res)));
                return BitConverter.ToString(h).Replace("-", string.Empty).Substring(0, 16);
            }
        }

        private static NestingResult Nest(NestingRequest r, bool pruning)
        {
            MultiOrderOptimizer optimizer = new MultiOrderOptimizer(new CandidatePointDecoder(), new LexicographicSolutionEvaluator())
            {
                LocalSearchPruning = pruning
            };
            SimpleNestingEngine engine = new SimpleNestingEngine(
                new BasicRotationCandidateProvider(), s => new PolygonCollisionModel(s), optimizer, new NestingValidator());
            return engine.Nest(r, CancellationToken.None, null);
        }

        // Ba yeu cau co dinh cho phep ghim V1: y het C18 / C18b / C31 (che do tat dinh).
        private static NestingRequest PinRequest(int which)
        {
            NestingRequest r;
            switch (which)
            {
                case 0:
                    r = Request(1200, 600);
                    r.Settings.Seed = 42;
                    r.Settings.ExtraSeededOrderings = 3;
                    r.Settings.AllowMirror = true;
                    r.Groups.Add(Poly("L", 5, LShape));
                    r.Groups.Add(Rect("A", 120, 70, 9));
                    r.Groups.Add(Rect("B", 60, 45, 11));
                    return r;
                case 1:
                    r = Request(1200, 600);
                    r.Settings.Seed = 7;
                    r.Settings.ExtraSeededOrderings = 3;
                    r.Groups.Add(Poly("L", 6, LShape));
                    r.Groups.Add(Poly("P", 4, Chiral));
                    r.Groups.Add(Rect("A", 120, 70, 9));
                    return r;
                default:
                    r = Request(2500, 1250);
                    r.Settings.Seed = 11;
                    r.Settings.ExtraSeededOrderings = 3;
                    for (int i = 0; i < 18; i++)
                    {
                        r.Groups.Add(Rect("R" + i.ToString(CultureInfo.InvariantCulture), 110 + i * 3, 70 + (i % 5) * 4, 3));
                    }

                    return r;
            }
        }

        /// <summary>Bo cuc cua V1 (HEAD 731002d) tren ba yeu cau ghim o tren.</summary>
        private static readonly string[] V1PinnedHashes = { "D44EA337FDC55DC9", "BF448A0E40D00887", "725CD0A8EDE308ED" };

        /// <summary>
        /// Mot yeu cau nho ma buoc doi cho CO go duoc chieu dai (va co luot bi cat som) - de
        /// cac phep thu Can bang kiem mot thu that su xay ra, khong phai mot vong lap rong.
        /// </summary>
        // Chi tiet chu U 250x140 (hoc 110x90) va tam giac vuong 200x150.
        private static readonly double[] UHookShape = { 0, 0, 250, 0, 250, 140, 180, 140, 180, 50, 70, 50, 70, 140, 0, 140 };
        private static readonly double[] TriLarge = { 0, 0, 200, 0, 0, 150 };

        /// <remarks>Da do: Nhanh 500 mm, Can bang 475 mm (3 lan tot hon, co luot bi cat som).</remarks>
        private static NestingRequest LocalSearchRequest()
        {
            NestingRequest r = Request(1200, 450);
            r.Groups.Add(Poly("U", 2, UHookShape));
            r.Groups.Add(Poly("T", 3, TriLarge));
            r.Groups.Add(Rect("A", 160, 80, 4));
            r.Groups.Add(Rect("C", 60, 60, 4));
            return r;
        }

        private static NestingRequest Balanced(NestingRequest r)
        {
            r.Settings.SearchEffort = SearchEffort.Balanced;
            return r;
        }

        /// <summary>
        /// BAT BUOC: muc Nhanh (va mac dinh) phai ra DUNG bo cuc V1 - ghim bang ma bam lay tu
        /// HEAD 731002d, khong phai so voi chinh no.
        /// </summary>
        private static void C32_FastEqualsV1()
        {
            Equal(SearchEffort.Fast, new NestingSettings().SearchEffort, "mac dinh phai la Nhanh");
            for (int k = 0; k < V1PinnedHashes.Length; k++)
            {
                NestingResult byDefault = Nest(PinRequest(k));
                AssertValid(byDefault);
                Equal(V1PinnedHashes[k], SignatureHash(byDefault), "yeu cau " + k + ": mac dinh == V1");

                NestingRequest fast = PinRequest(k);
                fast.Settings.SearchEffort = SearchEffort.Fast;
                NestingResult explicitFast = Nest(fast);
                Equal(V1PinnedHashes[k], SignatureHash(explicitFast), "yeu cau " + k + ": Nhanh == V1");
                Equal(0, explicitFast.Statistics.Materials[0].LocalSearchDecodes, "Nhanh khong chay doi cho");
            }
        }

        /// <summary>
        /// Can bang KHONG BAO GIO te hon Nhanh theo dung thu tu khoa cua bo xep hang: it chi
        /// tiet chua xep hon, roi it to hon, roi chieu dai (dung sai 1 mm).
        /// </summary>
        private static void C33_BalancedNeverWorse()
        {
            List<NestingRequest> fast = new List<NestingRequest> { PinRequest(0), PinRequest(1), PinRequest(2), LocalSearchRequest() };
            List<NestingRequest> bal = new List<NestingRequest> { Balanced(PinRequest(0)), Balanced(PinRequest(1)), Balanced(PinRequest(2)), Balanced(LocalSearchRequest()) };
            bool anyBetter = false;
            for (int k = 0; k < fast.Count; k++)
            {
                NestingResult f = Nest(fast[k]), b = Nest(bal[k]);
                AssertValid(f);
                AssertValid(b);
                int c = CompareResults(b, f);
                True(c <= 0, "yeu cau " + k + ": Can bang te hon Nhanh (" + UsedLength(b) + " vs " + UsedLength(f) + ")");
                if (c < 0) anyBetter = true;
                True(b.Statistics.Materials[0].LocalSearchDecodes > 0, "yeu cau " + k + ": Can bang phai chay doi cho");
            }

            True(anyBetter, "it nhat mot yeu cau phai tot hon - neu khong phep thu khong kiem gi");
        }

        private static double UsedLength(NestingResult res)
        {
            double sum = 0;
            foreach (SheetResult s in res.Sheets) sum += s.UsedLengthMm;
            return sum;
        }

        /// <summary>&lt; 0 khi a tot hon b (so theo khoa 1..3 cua LexicographicSolutionEvaluator).</summary>
        private static int CompareResults(NestingResult a, NestingResult b)
        {
            if (a.Unplaced.Count != b.Unplaced.Count) return a.Unplaced.Count.CompareTo(b.Unplaced.Count);
            if (a.Sheets.Count != b.Sheets.Count) return a.Sheets.Count.CompareTo(b.Sheets.Count);
            double la = UsedLength(a), lb = UsedLength(b);
            if (Math.Abs(la - lb) > 1.0 + 1e-9) return la.CompareTo(lb);
            return 0;
        }

        private static void C34_BalancedParallelEqualsSequential()
        {
            Func<int, string> run = degree =>
            {
                NestingRequest r = Balanced(LocalSearchRequest());
                r.Settings.MaxParallelism = degree;
                NestingResult res = Nest(r);
                AssertValid(res);
                True(res.Statistics.Materials[0].LocalSearchImprovements > 0, "phai co lan doi cho tot hon");
                return Signature(res);
            };

            string sequential = run(1);
            Equal(sequential, run(0), "Can bang: song song (toan bo nhan) == tuan tu");
            Equal(sequential, run(3), "Can bang: 3 luong == tuan tu");
        }

        private static void C35_BalancedDeterministic()
        {
            string a = Signature(Nest(Balanced(LocalSearchRequest())));
            Equal(a, Signature(Nest(Balanced(LocalSearchRequest()))), "lan 2 giong het");
            Equal(a, Signature(Nest(Balanced(LocalSearchRequest()))), "lan 3 giong het");
        }

        /// <summary>
        /// Cat som CHI de nhanh hon: cung day lan can thi co cat va khong cat phai ra cung ket
        /// qua. Kiem ca rang cat som that su xay ra - neu khong phep thu vo nghia.
        /// </summary>
        private static void C36_PruningDoesNotChangeResult()
        {
            int pruned = 0;
            List<NestingRequest> set = new List<NestingRequest> { LocalSearchRequest(), PinRequest(0), PinRequest(1), PinRequest(2) };
            for (int k = 0; k < set.Count; k++)
            {
                NestingResult with = Nest(Balanced(set[k]), true);
                NestingRequest again = k == 0 ? LocalSearchRequest() : PinRequest(k - 1);
                NestingResult without = Nest(Balanced(again), false);
                AssertValid(with);
                Equal(Signature(without), Signature(with), "yeu cau " + k + ": co cat == khong cat");
                MaterialStatistics mw = with.Statistics.Materials[0], mo = without.Statistics.Materials[0];
                Equal(mo.LocalSearchDecodes, mw.LocalSearchDecodes, "cung so luot doi cho");
                Equal(mo.LocalSearchImprovements, mw.LocalSearchImprovements, "cung so lan tot hon");
                Equal(0, mo.LocalSearchPruned, "tat cat som thi khong co luot nao bi cat");
                pruned += mw.LocalSearchPruned;
            }

            True(pruned > 0, "phai co luot bi cat som");
        }

        /// <summary>
        /// Lan can: chi hoan doi hai chi tiet KHAC nhom, cach nhau toi da Window, khong trung
        /// chuoi ma nhom, dung thu tu sinh.
        /// </summary>
        private static void C37_NeighbourGeneration()
        {
            PartGroup a = Rect("A", 10, 10, 2), b = Rect("B", 10, 10, 1), c = Rect("C", 10, 10, 1);
            List<PartInstance> cur = new List<PartInstance>
            {
                new PartInstance("A#1", a, 0), new PartInstance("A#2", a, 1), new PartInstance("B#1", b, 0), new PartInstance("C#1", c, 0)
            };

            List<string> got = new List<string>();
            foreach (List<PartInstance> n in OrderLocalSearch.Neighbours(cur))
            {
                StringBuilder sb = new StringBuilder();
                foreach (PartInstance i in n) sb.Append(i.Id).Append(' ');
                got.Add(sb.ToString().Trim());
            }

            // (0,1) cung nhom -> bo. (0,2) A B -> B A A C. (0,3) -> C A B A. (1,2) -> A B A C.
            // (1,3) -> A C B A. (2,3) -> A A C B. Khong co cap trung chuoi ma nhom o day.
            string[] expected =
            {
                "B#1 A#2 A#1 C#1",
                "C#1 A#2 B#1 A#1",
                "A#1 B#1 A#2 C#1",
                "A#1 C#1 B#1 A#2",
                "A#1 A#2 C#1 B#1"
            };
            Equal(string.Join(" | ", expected), string.Join(" | ", got.ToArray()), "lan can");

            // Xen ke hai nhom A B A B: (0,2) va (1,3) cung nhom -> bo; con lai moi lan can mot
            // chuoi ma nhom rieng.
            List<PartInstance> dup = new List<PartInstance>
            {
                new PartInstance("A#1", a, 0), new PartInstance("B#1", b, 0), new PartInstance("A#2", a, 1), new PartInstance("X", b, 1)
            };
            HashSet<string> keys = new HashSet<string>();
            foreach (List<PartInstance> n in OrderLocalSearch.Neighbours(dup))
            {
                StringBuilder sb = new StringBuilder();
                foreach (PartInstance i in n) sb.Append(i.PartGroupId);
                True(keys.Add(sb.ToString()), "lan can trung chuoi ma nhom: " + sb);
            }

            // (0,1) BAAB, (0,3) BBAA, (1,2) AABB, (2,3) ABBA - (0,2) va (1,3) cung nhom.
            Equal(4, keys.Count, "so lan can khong trung");
            Equal(0, OrderLocalSearch.Neighbours(new List<PartInstance> { new PartInstance("A#1", a, 0), new PartInstance("A#2", a, 1) }).Count,
                "mot nhom duy nhat -> khong co lan can");
        }

        /// <summary>
        /// Duong lui: neu ket qua Can bang khong qua validator thi phai dung lai tu nghiem V1,
        /// kiem lai va canh bao. Muc Nhanh thi khong co gi de lui - loi phai hien nguyen.
        /// </summary>
        private sealed class BrokenBestOptimizer : IOptimizer
        {
            private readonly MultiOrderOptimizer _inner =
                new MultiOrderOptimizer(new CandidatePointDecoder(), new LexicographicSolutionEvaluator());

            public OptimizationOutcome Optimize(MaterialJob job, CancellationToken cancellation, Action<NestingProgress> progress)
            {
                OptimizationOutcome o = _inner.Optimize(job, cancellation, progress);

                // Dat chi tiet dau tien HAI LAN cung mot cho: chong hinh + trung placement.
                DecodedLayout broken = new DecodedLayout { OrderingName = "hong" };
                DecodedSheet sheet = new DecodedSheet();
                foreach (PlacedItem i in o.Best.Sheets[0].Items) sheet.Items.Add(i);
                sheet.Items.Add(o.Best.Sheets[0].Items[0]);
                sheet.MaxX = o.Best.Sheets[0].MaxX;
                sheet.MaxY = o.Best.Sheets[0].MaxY;
                broken.Sheets.Add(sheet);
                for (int s = 1; s < o.Best.Sheets.Count; s++) broken.Sheets.Add(o.Best.Sheets[s]);
                broken.Unplaced.AddRange(o.Best.Unplaced);
                o.Best = broken;
                return o;
            }
        }

        private static void C38_InvalidBalancedFallsBackToV1()
        {
            SimpleNestingEngine broken = new SimpleNestingEngine(
                new BasicRotationCandidateProvider(), s => new PolygonCollisionModel(s), new BrokenBestOptimizer(), new NestingValidator());

            NestingRequest fastReq = PinRequest(1);
            NestingResult fastBroken = broken.Nest(fastReq, CancellationToken.None, null);
            True(!fastBroken.Validation.IsValid, "muc Nhanh: bo cuc hong phai bi validator bat");

            NestingRequest balReq = Balanced(PinRequest(1));
            NestingResult res = broken.Nest(balReq, CancellationToken.None, null);
            AssertValid(res);
            bool warned = false;
            foreach (string w in res.Warnings) if (w.IndexOf("Can bang", StringComparison.Ordinal) >= 0) warned = true;
            True(warned, "phai canh bao da dung lai ket qua muc Nhanh");
            Equal(V1PinnedHashes[1], SignatureHash(res), "ket qua lui ve phai DUNG la nghiem V1");
            Equal(res.Statistics.PlacedQuantity + res.Statistics.UnplacedQuantity, res.Statistics.RequestedQuantity, "so luong khop");
        }

        private static void C39_SearchEffortSettingRoundTrip()
        {
            NestingSettings s = new NestingSettings { SearchEffort = SearchEffort.Balanced };
            Equal(SearchEffort.Balanced, s.Clone().SearchEffort, "Clone giu muc tim kiem");

            NestingRequest r = Balanced(Request(1000, 500));
            r.Groups.Add(Rect("A", 100, 50, 2));
            string text = NestingFixture.Write(r, "search");
            True(text.IndexOf("search=balanced", StringComparison.Ordinal) >= 0, "Write ghi search=balanced");
            Equal(SearchEffort.Balanced, NestingFixture.Parse(text).Request.Settings.SearchEffort, "Parse doc lai balanced");
            Equal(text, NestingFixture.Write(NestingFixture.Parse(text).Request, "search"), "write(parse(write(x))) == write(x)");

            Equal(SearchEffort.Fast, NestingFixture.Parse("SETTINGS gap=5\nSHEET name=S length=100 width=100\n").Request.Settings.SearchEffort,
                "fixture cu khong co search= -> fast");
            Equal(SearchEffort.Fast, NestingFixture.Parse("SETTINGS search=FAST\n").Request.Settings.SearchEffort, "search=FAST");

            bool threw = false;
            try
            {
                NestingFixture.Parse("SETTINGS search=optimal\n");
            }
            catch (FormatException)
            {
                threw = true;
            }

            True(threw, "search= la phai bao loi, khong lang le chay muc khac");
        }

        /// <summary>Tien do muc Can bang: tong tinh ca ngan sach doi cho, van cham 100%.</summary>
        private static void C40_BalancedProgressReachesHundred()
        {
            NestingRequest r = Balanced(LocalSearchRequest());
            r.Groups.Add(Rect("M2", 180, 120, 3, "1.5MM"));      // vat lieu thu hai
            List<NestingProgress> seen = new List<NestingProgress>();
            object gate = new object();
            NestingResult res = new SimpleNestingEngine().Nest(r, CancellationToken.None, p => { lock (gate) { seen.Add(p); } });
            AssertValid(res);

            int expected = 2 * MultiOrderOptimizer.RunCount(r.Settings);
            Equal(expected, 2 * (MultiOrderOptimizer.RunCount(new NestingSettings { ExtraSeededOrderings = r.Settings.ExtraSeededOrderings }) + OrderLocalSearch.Budget),
                "RunCount cong them ngan sach doi cho");
            int high = 0;
            foreach (NestingProgress p in seen)
            {
                Equal(expected, p.Total, "tong phai giong nhau o moi lan bao");
                True(p.Done >= 0 && p.Done <= p.Total, "khong duoc vuot tong: " + p.Done + "/" + p.Total);
                high = Math.Max(high, p.Done);
            }

            Equal(expected, high, "tien do phai cham 100% khi xong");
        }

        /// <summary>Bam Dung giua buoc doi cho: dung ngay sau lo dang chay, giu ket qua tot nhat, van hop le.</summary>
        private static void C41_BalancedCancelKeepsBest()
        {
            NestingResult fast = Nest(LocalSearchRequest());
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                NestingRequest r = Balanced(LocalSearchRequest());
                r.Settings.MaxParallelism = 1;
                NestingResult res = new SimpleNestingEngine().Nest(r, cts.Token, p =>
                {
                    if (p.Message.IndexOf("doi cho", StringComparison.Ordinal) >= 0) cts.Cancel();
                });

                AssertValid(res);
                True(res.Cancelled, "phai bao da dung");
                Equal(OrderLocalSearch.Batch, res.Statistics.Materials[0].LocalSearchDecodes, "dung ngay sau lo dau tien");
                Equal(0, res.Statistics.UnplacedQuantity, "khong mat chi tiet nao");
                True(CompareResults(res, fast) <= 0, "khong te hon muc Nhanh");
            }
        }

        /// <summary>
        /// Tat "Tim du": het gio thi khong bat dau lo doi cho moi (va phai bao). Bat "Tim du":
        /// dong ho khong duoc anh huong - chay du ngan sach du han muc gio cuc nho.
        /// </summary>
        private static void C42_BalancedClockOnlyWhenNotDeterministic()
        {
            Func<bool, MaterialStatistics> run = deterministic =>
            {
                NestingRequest r = Balanced(LocalSearchRequest());
                r.Settings.TimeBudgetSeconds = 0.000001;
                r.Settings.DeterministicSearch = deterministic;
                r.Settings.MaxParallelism = 1;
                NestingResult res = Nest(r);
                AssertValid(res);
                return res.Statistics.Materials[0];
            };

            MaterialStatistics clock = run(false);
            Equal(0, clock.LocalSearchDecodes, "het gio -> khong chay lo doi cho nao");
            True(clock.TimeBudgetHit, "het gio phai bao");

            MaterialStatistics full = run(true);
            True(full.LocalSearchDecodes > 0, "che do tat dinh: van chay doi cho");
            True(!full.TimeBudgetHit, "che do tat dinh: khong bao het gio");
            Equal(Signature(Nest(Balanced(LocalSearchRequest()))), SignatureOf(true), "tat dinh: han muc gio khong doi ket qua");
        }

        private static string SignatureOf(bool deterministic)
        {
            NestingRequest r = Balanced(LocalSearchRequest());
            r.Settings.TimeBudgetSeconds = 0.000001;
            r.Settings.DeterministicSearch = deterministic;
            return Signature(Nest(r));
        }

        // ==================================================================================
        // PHASE 1: diem-trong-da-giac nhanh trong PolygonCollisionModel == ban cu (GeometryMath)
        // ==================================================================================

        /// <summary>
        /// Bo hinh dung de so hai kernel: canh ngang/doc/xien, hinh lom, nhieu lo, lo sat nhau,
        /// lo long trong lo, hinh cung xap xi (64 canh) co lo tron - o ca 8 huong xoay/lat.
        /// </summary>
        private static List<PolyShape> KernelShapes()
        {
            List<PartGroup> groups = new List<PartGroup>
            {
                Rect("R", 120, 70, 1),
                Poly("TRI", 1, TriA),
                Poly("L", 1, LShape),
                Poly("U", 1, UHookShape),
                Poly("P", 1, Chiral),
                Poly("F", 1, Frame, FrameHole),
                Poly("MH", 1, new double[] { 0, 0, 400, 0, 400, 200, 0, 200 },
                    new double[] { 20, 20, 120, 20, 120, 180, 20, 180 },
                    new double[] { 120, 60, 200, 60, 200, 140, 120, 140 },
                    new double[] { 260, 50, 330, 30, 360, 120, 280, 160 },
                    new double[] { 40, 40, 80, 40, 80, 80, 40, 80 })     // lo LONG trong lo dau (ca kernel cung phai xu ly)
            };

            List<IntPoint> disc = new List<IntPoint>(), bore = new List<IntPoint>(), bore2 = new List<IntPoint>();
            for (int i = 0; i < 64; i++)
            {
                double t = 2 * Math.PI * i / 64;
                disc.Add(IntPoint.FromMm(100 + 100 * Math.Cos(t), 100 + 100 * Math.Sin(t)));
                bore.Add(IntPoint.FromMm(60 + 25 * Math.Cos(t), 100 + 25 * Math.Sin(t)));
                bore2.Add(IntPoint.FromMm(140 + 25 * Math.Cos(t), 100 + 25 * Math.Sin(t)));
            }

            groups.Add(new PartGroup("D", new PartShape(PolyShape.Create(disc, new[] { bore, bore2 }), 0.05), 1, "1.2MM"));

            List<PolyShape> shapes = new List<PolyShape>();
            foreach (PartGroup g in groups)
            {
                foreach (bool mirror in new[] { false, true })
                {
                    foreach (double rot in new[] { 0.0, 90.0, 180.0, 270.0 })
                    {
                        shapes.Add(g.Shape.Polygon.Transform(new OrientationTransform(rot, mirror), 1234, -5678));
                    }
                }
            }

            return shapes;
        }

        /// <summary>Diem hiem: moi dinh, moi diem nguyen tren canh, trung diem, lan can +-1, bien hop bao.</summary>
        private static List<IntPoint> CriticalPoints(PolyShape s)
        {
            List<IntPoint> pts = new List<IntPoint>();
            List<IntPoint[]> rings = new List<IntPoint[]> { s.Outer };
            rings.AddRange(s.Holes);
            foreach (IntPoint[] ring in rings)
            {
                LongRect rb = LongRect.FromPoints(ring);
                foreach (long x in new[] { rb.MinX - 1, rb.MinX, rb.MinX + 1, (rb.MinX + rb.MaxX) / 2, rb.MaxX - 1, rb.MaxX, rb.MaxX + 1 })
                {
                    foreach (long y in new[] { rb.MinY - 1, rb.MinY, rb.MinY + 1, (rb.MinY + rb.MaxY) / 2, rb.MaxY - 1, rb.MaxY, rb.MaxY + 1 })
                    {
                        pts.Add(new IntPoint(x, y));
                    }
                }

                for (int i = 0; i < ring.Length; i++)
                {
                    IntPoint a = ring[i], b = ring[(i + 1) % ring.Length];
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++) pts.Add(new IntPoint(a.X + dx, a.Y + dy));
                    }

                    // Diem NGUYEN nam dung tren canh (buoc = vector canh / gcd), ca canh xien.
                    long ex = b.X - a.X, ey = b.Y - a.Y;
                    long g = Gcd(Math.Abs(ex), Math.Abs(ey));
                    if (g > 0)
                    {
                        long sx = ex / g, sy = ey / g;
                        foreach (long k in new[] { 1L, g / 3, g / 2, g - 1 })
                        {
                            if (k <= 0 || k >= g) continue;
                            IntPoint on = new IntPoint(a.X + sx * k, a.Y + sy * k);
                            pts.Add(on);
                            pts.Add(new IntPoint(on.X + 1, on.Y));
                            pts.Add(new IntPoint(on.X, on.Y - 1));
                        }
                    }

                    pts.Add(new IntPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2));
                }
            }

            return pts;
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                long t = a % b;
                a = b;
                b = t;
            }

            return a;
        }

        /// <summary>
        /// BAT BUOC: kernel moi cho DUNG cung ket qua voi GeometryMath tren moi diem hiem va
        /// hang chuc nghin diem ngau nhien (seed co dinh) - ca 1 / 0 / -1 cua tung vong, ket qua
        /// vat lieu, va ket qua vong ngoai tra kem.
        /// </summary>
        private static void C43_PointKernelEquivalence()
        {
            Random rnd = new Random(20260928);
            int compared = 0, onBoundary = 0, inside = 0;
            foreach (PolyShape s in KernelShapes())
            {
                LongRect[] rb = RingPointTests.RingBoundsOf(s);
                List<IntPoint> pts = CriticalPoints(s);
                LongRect b = s.Bounds;
                long mx = b.Width / 10 + 2, my = b.Height / 10 + 2;
                for (int k = 0; k < 5000; k++)
                {
                    pts.Add(new IntPoint(
                        b.MinX - mx + (long)(rnd.NextDouble() * (b.Width + 2 * mx)),
                        b.MinY - my + (long)(rnd.NextDouble() * (b.Height + 2 * my))));
                }

                foreach (IntPoint p in pts)
                {
                    int oldOuter = GeometryMath.PointInRing(p, s.Outer);
                    Equal(oldOuter, RingPointTests.PointInRing(p, s.Outer, rb[0]), "vong ngoai tai " + p.X + "," + p.Y);
                    for (int h = 0; h < s.Holes.Length; h++)
                    {
                        Equal(GeometryMath.PointInRing(p, s.Holes[h]), RingPointTests.PointInRing(p, s.Holes[h], rb[h + 1]), "lo " + h + " tai " + p.X + "," + p.Y);
                    }

                    int outer;
                    bool fast = RingPointTests.PointInMaterial(p, s, rb, out outer);
                    Equal(GeometryMath.PointInMaterial(p, s), fast, "vat lieu tai " + p.X + "," + p.Y);
                    Equal(oldOuter, outer, "vong ngoai tra kem tai " + p.X + "," + p.Y);
                    compared++;
                    if (oldOuter == 0) onBoundary++;
                    if (fast) inside++;
                }
            }

            True(compared > 300000, "phai so du nhieu diem: " + compared);
            True(onBoundary > 1000, "phai co nhieu diem NAM TREN BIEN: " + onBoundary);
            True(inside > 10000, "phai co nhieu diem trong vat lieu: " + inside);
        }

        /// <summary>
        /// Collides ban moi == ban cu tren hang chuc nghin cap vi tri ngau nhien (seed co dinh),
        /// ca khi cam va cho phep dat vao lo kin, ca chi tiet co dung sai cung.
        /// </summary>
        private static void C44_CollidesLegacyEquivalence()
        {
            List<PartGroup> groups = new List<PartGroup>
            {
                Rect("R", 120, 70, 1), Rect("S", 30, 30, 1), Poly("L", 1, LShape), Poly("U", 1, UHookShape),
                Poly("F", 1, Frame, FrameHole), PolyTol("T", 1, 0.05, TriLarge)
            };

            Random rnd = new Random(7);
            int hits = 0, total = 0;
            foreach (bool inHole in new[] { false, true })
            {
                NestingSettings st = new NestingSettings { AllowPartInsideHole = inHole, AllowMirror = true };
                PolygonCollisionModel fresh = new PolygonCollisionModel(st);
                PolygonCollisionModel legacy = new PolygonCollisionModel(st) { LegacyPointTests = true };
                foreach (PartGroup ga in groups)
                {
                    foreach (PartGroup gb in groups)
                    {
                        for (int k = 0; k < 400; k++)
                        {
                            OrientationTransform oa = new OrientationTransform(90.0 * rnd.Next(4), rnd.Next(2) == 1);
                            OrientationTransform ob = new OrientationTransform(90.0 * rnd.Next(4), rnd.Next(2) == 1);
                            PreparedShape moving = fresh.Prepare(ga, oa);
                            PlacedShape placed = fresh.Place(fresh.Prepare(gb, ob), 500000, 500000);
                            long tx = 500000 + (long)((rnd.NextDouble() - 0.5) * 2 * 400000);
                            long ty = 500000 + (long)((rnd.NextDouble() - 0.5) * 2 * 400000);
                            bool a = fresh.Collides(moving, tx, ty, placed);
                            bool b = legacy.Collides(moving, tx, ty, placed);
                            Equal(b, a, ga.Id + "/" + gb.Id + " inhole=" + inHole + " tai " + tx + "," + ty);
                            if (a) hits++;
                            total++;
                        }
                    }
                }
            }

            True(hits > total / 10 && hits < total * 9 / 10, "phai co ca va cham lan khong va cham: " + hits + "/" + total);
        }

        private sealed class CountingModel : ICollisionModel
        {
            private readonly PolygonCollisionModel _inner;
            public long Count;
            public CountingModel(PolygonCollisionModel inner) { _inner = inner; }
            public ClearanceRules Rules { get { return _inner.Rules; } }
            public PreparedShape Prepare(PartGroup g, OrientationTransform o) { return _inner.Prepare(g, o); }
            public bool FitsInsideSheet(PreparedShape s, long tx, long ty, SheetSpec sheet) { return _inner.FitsInsideSheet(s, tx, ty, sheet); }
            public bool Collides(PreparedShape m, long tx, long ty, PlacedShape p) { Interlocked.Increment(ref Count); return _inner.Collides(m, tx, ty, p); }
            public PlacedShape Place(PreparedShape s, long tx, long ty) { return _inner.Place(s, tx, ty); }
        }

        private static NestingResult NestWithKernel(NestingRequest r, bool legacy, out long collides)
        {
            CountingModel counter = null;
            SimpleNestingEngine engine = new SimpleNestingEngine(
                new BasicRotationCandidateProvider(),
                s => counter = new CountingModel(new PolygonCollisionModel(s) { LegacyPointTests = legacy }),
                new MultiOrderOptimizer(new CandidatePointDecoder(), new LexicographicSolutionEvaluator()),
                new NestingValidator());
            NestingResult res = engine.Nest(r, CancellationToken.None, null);
            collides = counter.Count;
            return res;
        }

        /// <summary>
        /// Ca bo ghep: kernel cu va moi phai ra cung bo cuc VA cung so lan goi Collides (cung
        /// so lan goi = moi quyet dinh doc duong deu giong nhau), o ca Nhanh va Can bang, ca co
        /// lo kin (cam / cho phep dat vao lo).
        /// </summary>
        private static void C45_EngineLegacyKernelEquivalence()
        {
            List<Func<NestingRequest>> set = new List<Func<NestingRequest>>
            {
                () => PinRequest(0),
                () => PinRequest(1),
                () => Balanced(LocalSearchRequest()),
                () =>
                {
                    NestingRequest r = Request(1000, 700);
                    r.Groups.Add(Poly("F", 3, Frame, FrameHole));
                    r.Groups.Add(Rect("S", 80, 60, 6));
                    r.Groups.Add(Poly("L", 2, LShape));
                    return r;
                },
                () =>
                {
                    NestingRequest r = Request(1000, 700);
                    r.Settings.AllowPartInsideHole = true;
                    r.Groups.Add(Poly("F", 3, Frame, FrameHole));
                    r.Groups.Add(Rect("S", 80, 60, 6));
                    return Balanced(r);
                }
            };

            for (int k = 0; k < set.Count; k++)
            {
                long cNew, cOld;
                NestingResult fresh = NestWithKernel(set[k](), false, out cNew);
                NestingResult legacy = NestWithKernel(set[k](), true, out cOld);
                AssertValid(fresh);
                Equal(Signature(legacy), Signature(fresh), "yeu cau " + k + ": cung bo cuc");
                Equal(cOld, cNew, "yeu cau " + k + ": cung so lan goi Collides");
                True(cNew > 0, "phai co goi Collides");
            }
        }

        // ==================================================================================
        // HARDENING: dau vao vo nghia, fuzz co seed, dau vao benh ly
        // ==================================================================================

        private static void ExpectArgument(Action a, string what)
        {
            try
            {
                a();
            }
            catch (ArgumentException)
            {
                return;
            }

            throw new NestingAssertException(what + ": phai bi TU CHOI (ArgumentException)");
        }

        /// <summary>
        /// HOI QUY: Gap = Infinity / NaN / 1e20 truoc day thanh khe 0.001 mm ma validator van DAT
        /// (NestUnits.ToUnits ep NaN / vo cuc ra long.MinValue). Gio phai bi tu choi ro rang;
        /// gia tri hop le (ke ca 0 va am -> 0 nhu truoc) van chay binh thuong.
        /// </summary>
        private static void C46_NonsenseInputsRejected()
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e20, -5e15 })
            {
                double v = bad;
                ExpectArgument(() => NestUnits.ToUnits(v), "ToUnits(" + v + ")");
            }

            Equal(1000000000L, NestUnits.ToUnits(1e6), "ToUnits 1 km van dung");
            Equal(-5000L, NestUnits.ToUnits(-5), "ToUnits am van dung");

            Func<NestingRequest> ok = () =>
            {
                NestingRequest r = Request(1000, 500);
                r.Groups.Add(Rect("A", 100, 50, 3));
                return r;
            };

            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 1e20, 10000.001 })
            {
                double v = bad;
                ExpectArgument(() => { NestingRequest r = ok(); r.Settings.GapMm = v; Nest(r); }, "Gap = " + v);
                ExpectArgument(() => { NestingRequest r = ok(); r.Settings.EdgeMarginMm = v; Nest(r); }, "EdgeMargin = " + v);
            }

            ExpectArgument(() => { NestingRequest r = ok(); r.Settings.TimeBudgetSeconds = double.NaN; Nest(r); }, "TimeBudget = NaN");

            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 0.0, -100.0, 1e13 })
            {
                double v = bad;
                ExpectArgument(() => { NestingRequest r = ok(); r.DefaultSheet = new SheetSpec("X", v, 500); Nest(r); }, "chieu dai kho = " + v);
                ExpectArgument(() => { NestingRequest r = ok(); r.SheetByMaterial["1.2MM"] = new SheetSpec("Y", 1000, v); Nest(r); }, "chieu rong kho vat lieu = " + v);
            }

            // Hop le o bien: khe 0, khe am (= 0, nhu truoc day), le 0.
            foreach (double gap in new[] { 0.0, -3.0 })
            {
                NestingRequest r = ok();
                r.Settings.GapMm = gap;
                r.Settings.EdgeMarginMm = 0;
                NestingResult res = Nest(r);
                AssertValid(res);
                Equal(3, res.Statistics.PlacedQuantity, "khe " + gap + ": van xep du");
            }
        }

        /// <summary>Sinh mot hinh ngau nhien (mm, toa do cuc bo), co the co lo.</summary>
        private static PartGroup FuzzShape(Random rnd, string id, int qty, string material)
        {
            int kind = rnd.Next(9);
            double w = 20 + rnd.NextDouble() * 280, h = 20 + rnd.NextDouble() * 180;
            List<double> outer = new List<double>();
            List<double[]> holes = new List<double[]>();
            double tol = 0;
            switch (kind)
            {
                case 0:
                    outer.AddRange(new[] { 0, 0, w, 0, w, h, 0, h });
                    break;
                case 1:
                    outer.AddRange(new[] { 0, 0, w, 0, rnd.NextDouble() * w, h });
                    break;
                case 2:
                    {
                        double t = Math.Max(8, Math.Min(w, h) * (0.2 + rnd.NextDouble() * 0.4));
                        outer.AddRange(new[] { 0, 0, w, 0, w, t, t, t, t, h, 0, h });
                        break;
                    }

                case 3:
                    {
                        double t = Math.Max(6, w * (0.15 + rnd.NextDouble() * 0.2)), d = h * (0.3 + rnd.NextDouble() * 0.5);
                        outer.AddRange(new[] { 0, 0, w, 0, w, h, w - t, h, w - t, h - d, t, h - d, t, h, 0, h });
                        break;
                    }

                case 4:
                case 5:
                    {
                        // Da giac loi (kind 4) hoac hinh sao LOM (kind 5), n dinh; kind 5 co dung sai cung.
                        int n = 5 + rnd.Next(20);
                        double r0 = Math.Min(w, h) / 2;
                        for (int i = 0; i < n; i++)
                        {
                            double a = 2 * Math.PI * i / n;
                            double r = kind == 5 && i % 2 == 1 ? r0 * (0.45 + rnd.NextDouble() * 0.3) : r0;
                            outer.Add(r0 + r * Math.Cos(a));
                            outer.Add(r0 + r * Math.Sin(a));
                        }

                        tol = kind == 5 ? 0.05 : 0;
                        break;
                    }

                case 6:
                    {
                        // Tam co NHIEU lo, co cap lo sat nhau (cach 0.5 mm).
                        outer.AddRange(new[] { 0, 0, w + 60, 0, w + 60, h + 40, 0, h + 40 });
                        double hw = Math.Max(4, (w - 10) / 3);
                        holes.Add(new[] { 10, 10, 10 + hw, 10, 10 + hw, 30, 10, 30 });
                        holes.Add(new[] { 10.5 + hw, 10, 10.5 + 2 * hw, 10, 10.5 + 2 * hw, 30, 10.5 + hw, 30 });
                        if (h > 40) holes.Add(new[] { 20, 40, 40, 40, 30, h + 20 });
                        break;
                    }

                case 7:
                    {
                        // Dia 48 canh (xap xi cung) co lo tron: dung sai cung 0.05 mm.
                        double r0 = Math.Min(w, h) / 2 + 10;
                        double[] hole = new double[48 * 2];
                        for (int i = 0; i < 48; i++)
                        {
                            double a = 2 * Math.PI * i / 48;
                            outer.Add(r0 + r0 * Math.Cos(a));
                            outer.Add(r0 + r0 * Math.Sin(a));
                            hole[2 * i] = r0 + r0 * 0.4 * Math.Cos(a);
                            hole[2 * i + 1] = r0 + r0 * 0.4 * Math.Sin(a);
                        }

                        holes.Add(hole);
                        tol = 0.05;
                        break;
                    }

                default:
                    // Khung co 1 lo lon (cho phep / cam dat vao lo tuy cai dat).
                    outer.AddRange(new[] { 0, 0, w + 80, 0, w + 80, h + 80, 0, h + 80 });
                    holes.Add(new[] { 20, 20, w + 60, 20, w + 60, h + 60, 20, h + 60 });
                    break;
            }

            PartGroup g = PolyTol(id, qty, tol, outer.ToArray(), holes.ToArray());
            return new PartGroup(g.Id, g.Shape, qty, material) { Name = g.Id };
        }

        /// <summary>
        /// Kiem DOC LAP voi validator (tu viet lai, KHONG dung validator va KHONG dung ClearanceRules
        /// cua loi - tu tinh khe / le tu cai dat, de mot loi trong ClearanceRules khong the lam ca
        /// hai ben cung sai theo): moi placement nam
        /// trong kho cach mep &gt;= EdgeMargin + dung sai; moi cap cung to khong chong, khong nam
        /// trong lo kin khi cam, khe &gt;= Gap + dung sai hai ben (so nguyen, chinh xac).
        /// </summary>
        private static string IndependentCheck(NestingRequest r, NestingResult res)
        {
            long gapU = (long)Math.Round(Math.Max(0.0, r.Settings.GapMm) * 1000.0, MidpointRounding.AwayFromZero);
            long marginU = (long)Math.Round(Math.Max(0.0, r.Settings.EdgeMarginMm) * 1000.0, MidpointRounding.AwayFromZero);
            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>();
            foreach (PartGroup g in r.Groups) groups[g.Id] = g;
            foreach (SheetResult s in res.Sheets)
            {
                List<PolyShape> world = new List<PolyShape>();
                List<PartShape> shapes = new List<PartShape>();
                foreach (Placement p in s.Placements)
                {
                    PartGroup g = groups[p.PartGroupId];
                    if (!r.Settings.AllowMirror && p.Mirror) return p.InstanceId + ": lat guong khi khong cho phep";
                    if (!r.Settings.AllowedRotations.Contains(p.RotationDeg)) return p.InstanceId + ": goc xoay khong cho phep";
                    if (!string.Equals(SimpleNestingEngine.NormalizeMaterial(g.Material), s.Material, StringComparison.Ordinal)) return p.InstanceId + ": sai vat lieu to";
                    PolyShape w = g.Shape.Polygon.Transform(p.Orientation, p.TranslationX, p.TranslationY);
                    long inset = marginU + (long)Math.Ceiling(g.Shape.ToleranceMm * 1000.0 - 1e-9);
                    if (w.Bounds.MinX < inset || w.Bounds.MinY < inset || w.Bounds.MaxX > s.Sheet.LengthUnits - inset || w.Bounds.MaxY > s.Sheet.WidthUnits - inset)
                    {
                        return p.InstanceId + ": vi pham le mep / ra ngoai to";
                    }

                    world.Add(w);
                    shapes.Add(g.Shape);
                }

                for (int i = 0; i < world.Count; i++)
                {
                    for (int j = i + 1; j < world.Count; j++)
                    {
                        PolyShape a = world[i], b = world[j];
                        long c = Math.Max(1L, gapU + (long)Math.Ceiling(shapes[i].ToleranceMm * 1000.0 - 1e-9) + (long)Math.Ceiling(shapes[j].ToleranceMm * 1000.0 - 1e-9));
                        if (!a.Bounds.Overlaps(b.Bounds, c)) continue;
                        if (GeometryMath.PointInMaterial(a.Outer[0], b) || GeometryMath.PointInMaterial(b.Outer[0], a)) return s.Placements[i].InstanceId + " CHONG " + s.Placements[j].InstanceId;
                        if (!r.Settings.AllowPartInsideHole &&
                            ((b.Holes.Length > 0 && GeometryMath.PointInRing(a.Outer[0], b.Outer) >= 0) ||
                             (a.Holes.Length > 0 && GeometryMath.PointInRing(b.Outer[0], a.Outer) >= 0)))
                        {
                            return s.Placements[i].InstanceId + " nam trong lo kin cua " + s.Placements[j].InstanceId;
                        }

                        double limit2 = (double)c * c;
                        foreach (IntPoint[] ra in RingsOf(a))
                        {
                            foreach (IntPoint[] rb in RingsOf(b))
                            {
                                for (int x = 0; x < ra.Length; x++)
                                {
                                    for (int y = 0; y < rb.Length; y++)
                                    {
                                        if (GeometryMath.SegmentDistanceSquared(ra[x], ra[(x + 1) % ra.Length], rb[y], rb[(y + 1) % rb.Length]) < limit2)
                                        {
                                            return s.Placements[i].InstanceId + " / " + s.Placements[j].InstanceId + ": khe nho hon Gap + dung sai";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<IntPoint[]> RingsOf(PolyShape s)
        {
            yield return s.Outer;
            foreach (IntPoint[] h in s.Holes) yield return h;
        }

        /// <summary>
        /// FUZZ co seed: 200 bai ngau nhien (hinh chu nhat / tam giac / L / U / loi / sao lom /
        /// nhieu lo sat nhau / dia co lo / khung co lo; xoay, lat, lo kin cho phep hay khong, khe
        /// va le ngau nhien, 1-2 vat lieu). Moi bai: validator DAT, kiem doc lap DAT, du so luong,
        /// chay lai cung ket qua. Hong thi in seed + bai + fixture day du de tai tao.
        /// </summary>
        private static void C47_FuzzProperties()
        {
            const int seed = 20260928;
            Random rnd = new Random(seed);
            int totalPlaced = 0, totalUnplaced = 0, withHoles = 0, multiSheet = 0;
            for (int k = 0; k < 200; k++)
            {
                NestingRequest r = Request(600 + rnd.Next(1400), 300 + rnd.Next(900), Math.Round(rnd.NextDouble() * 10, 2), Math.Round(rnd.NextDouble() * 10, 2));
                r.Settings.ExtraSeededOrderings = rnd.Next(2);
                r.Settings.Seed = rnd.Next(1000);
                r.Settings.AllowMirror = rnd.Next(2) == 1;
                r.Settings.AllowPartInsideHole = rnd.Next(3) == 0;
                int rot = rnd.Next(3);
                r.Settings.AllowedRotations = rot == 0 ? new List<double> { 0, 90, 180, 270 } : rot == 1 ? new List<double> { 0, 180 } : new List<double> { 0 };
                if (rnd.Next(4) == 0) r.SheetByMaterial["1.5MM"] = new SheetSpec("M15", 500 + rnd.Next(800), 300 + rnd.Next(500));
                int groups = 1 + rnd.Next(5);
                for (int g = 0; g < groups; g++)
                {
                    string mat = r.SheetByMaterial.Count > 0 && rnd.Next(2) == 0 ? "1.5MM" : "1.2MM";
                    r.Groups.Add(FuzzShape(rnd, "G" + g.ToString(CultureInfo.InvariantCulture), 1 + rnd.Next(5), mat));
                }

                // Thinh thoang mot chi tiet DAI hon kho: phai "chua xep", khong duoc lam hong phan con lai.
                if (rnd.Next(8) == 0) r.Groups.Add(Rect("BIG", r.DefaultSheet.LengthMm + 50, 40, 1 + rnd.Next(2)));

                string repro = "seed=" + seed + " bai=" + k + "\n" + NestingFixture.Write(r, "fuzz");
                try
                {
                    NestingResult res = Nest(r);
                    True(res.Validation.IsValid, "validator: " + (res.Validation.IsValid ? "" : res.Validation.Issues[0].ToString()));
                    string bad = IndependentCheck(r, res);
                    True(bad == null, "kiem doc lap: " + bad);

                    int requested = 0;
                    foreach (PartGroup g in r.Groups) requested += g.Quantity;
                    Equal(requested, res.Statistics.PlacedQuantity + res.Statistics.UnplacedQuantity, "du so luong");
                    int placements = 0;
                    foreach (SheetResult s in res.Sheets) placements += s.Placements.Count;
                    Equal(res.Statistics.PlacedQuantity, placements, "so placement == so da xep");
                    Equal(res.Statistics.UnplacedQuantity, res.Unplaced.Count, "so chua xep == danh sach chua xep");
                    Equal(Signature(res), Signature(Nest(NestingFixture.Parse(NestingFixture.Write(r, "fuzz")).Request)), "chay lai (qua fixture) cung ket qua");
                    totalPlaced += res.Statistics.PlacedQuantity;
                    totalUnplaced += res.Statistics.UnplacedQuantity;
                    if (res.Sheets.Count > 1) multiSheet++;
                    foreach (PartGroup g in r.Groups)
                    {
                        if (g.Shape.Polygon.Holes.Length > 0)
                        {
                            withHoles++;
                            break;
                        }
                    }
                }
                catch (NestingAssertException ex)
                {
                    throw new NestingAssertException(ex.Message + "\nTAI TAO:\n" + repro);
                }
            }

            // Bo sinh phai that su tao ra bai kho, khong phai 200 bai rong.
            True(totalPlaced > 500, "fuzz phai xep duoc nhieu chi tiet: " + totalPlaced);
            True(totalUnplaced > 0, "fuzz phai co ca truong hop khong xep het");
            True(withHoles > 30 && multiSheet > 10, "fuzz phai co bai co lo (" + withHoles + ") va nhieu to (" + multiSheet + ")");
        }

        /// <summary>Chay mot bai benh ly voi han gio: khong duoc treo, khong duoc nem loi ngoai du kien.</summary>
        private static NestingResult NestWithin(NestingRequest r, int seconds, string what)
        {
            NestingResult res = null;
            Exception error = null;
            System.Threading.Tasks.Task t = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    res = Nest(r);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            True(t.Wait(TimeSpan.FromSeconds(seconds)), what + ": TREO (> " + seconds + " s)");
            if (error != null) throw new NestingAssertException(what + ": nem loi " + error.GetType().Name + ": " + error.Message);
            True(res.Validation.IsValid, what + ": validator " + (res.Validation.IsValid ? "" : res.Validation.Issues[0].ToString()));
            int requested = 0;
            foreach (PartGroup g in r.Groups) requested += Math.Max(0, g.Quantity);
            Equal(requested, res.Statistics.PlacedQuantity + res.Statistics.UnplacedQuantity, what + ": du so luong");
            return res;
        }

        /// <summary>
        /// Dau vao BENH LY: khong crash, khong treo, khong bao DAT cho bo cuc sai. Hinh hong thi
        /// phai "chua xep" kem ly do, khong duoc sua ho.
        /// </summary>
        private static void C48_PathologicalInputs()
        {
            Func<NestingRequest> sheet = () => Request(1000, 500);

            NestingRequest empty = sheet();
            NestingResult none = NestWithin(empty, 30, "khong co chi tiet");
            Equal(0, none.Sheets.Count, "khong co chi tiet -> khong to nao");

            NestingRequest zero = sheet();
            zero.Groups.Add(Poly("ZERO", 2, new double[] { 0, 0, 100, 0, 200, 0 }));        // dien tich 0
            zero.Groups.Add(Poly("LINE", 1, new double[] { 0, 0, 100, 100 }));            // < 3 dinh
            NestingResult z = NestWithin(zero, 30, "dien tich 0 / < 3 dinh");
            Equal(3, z.Statistics.UnplacedQuantity, "hinh hong -> CHUA XEP");
            foreach (UnplacedPart u in z.Unplaced) True(!string.IsNullOrEmpty(u.Reason), "co ly do");
            Equal(0, z.Sheets.Count, "chi co hinh hong -> khong mo to nao");

            NestingRequest odd = sheet();
            odd.Groups.Add(Poly("DUP", 2, new double[] { 0, 0, 100, 0, 100, 0, 100, 50, 0, 50, 0, 50 }));             // dinh lap
            odd.Groups.Add(Poly("COL", 2, new double[] { 0, 0, 50, 0, 100, 0, 100, 50, 50, 50, 0, 50 }));             // canh thang hang
            odd.Groups.Add(Poly("EPS", 2, new double[] { 0, 0, 100, 0, 100, 0.001, 100, 60, 0, 60 }));                 // canh 0.001 mm
            odd.Groups.Add(Poly("THIN", 2, new double[] { 0, 0, 400, 0, 400, 0.05, 0, 0.05 }));                        // day 0.05 mm
            NestingResult o = NestWithin(odd, 60, "dinh lap / thang hang / canh sieu ngan / sieu mong");
            Equal(8, o.Statistics.PlacedQuantity, "cac hinh hop le ky quac van xep duoc");

            NestingRequest holes = sheet();
            holes.Groups.Add(Poly("TINYHOLE", 2, new double[] { 0, 0, 120, 0, 120, 80, 0, 80 }, new double[] { 60, 40, 60.01, 40, 60.01, 40.01 }));
            holes.Groups.Add(Poly("NEAREDGE", 2, new double[] { 0, 0, 120, 0, 120, 80, 0, 80 }, new double[] { 0.001, 10, 50, 10, 50, 70, 0.001, 70 }));
            holes.Groups.Add(Poly("TOUCH", 1, new double[] { 0, 0, 120, 0, 120, 80, 0, 80 }, new double[] { 0, 10, 50, 10, 50, 70, 0, 70 }));
            holes.Groups.Add(Rect("S", 20, 20, 3));
            NestWithin(holes, 60, "lo sieu nho / lo sat bien / lo cham bien");

            NestingRequest far = sheet();
            far.Groups.Add(Poly("FAR", 2, new double[] { 900000, -900000, 900100, -900000, 900100, -899950, 900000, -899950 }));
            far.Groups.Add(Poly("NEG", 2, new double[] { -500, -300, -380, -300, -380, -220, -500, -220 }));
            NestingResult f = NestWithin(far, 60, "toa do rat xa / am");
            Equal(4, f.Statistics.PlacedQuantity, "toa do xa / am van xep duoc (hinh tinh theo he rieng)");

            NestingRequest huge = sheet();
            huge.Groups.Add(Rect("HUGE", 5000, 3000, 2));
            huge.Groups.Add(Rect("OK", 100, 100, 2));
            NestingResult hg = NestWithin(huge, 30, "chi tiet lon hon kho");
            Equal(2, hg.Statistics.UnplacedQuantity, "chi tiet lon hon kho -> CHUA XEP");
            Equal(2, hg.Statistics.PlacedQuantity, "chi tiet khac van xep");

            NestingRequest mat = sheet();
            mat.SheetByMaterial["2MM"] = new SheetSpec("M2", 1000, 500);
            mat.Groups.Add(new PartGroup("BAD2", new PartShape(PolyShape.Create(new List<IntPoint>(), null)), 2, "2MM"));
            mat.Groups.Add(Rect("GOOD", 100, 50, 2));
            NestingResult m = NestWithin(mat, 30, "vat lieu chi co hinh hong");
            Equal(2, m.Statistics.UnplacedQuantity, "vat lieu hong -> chua xep");
            Equal(2, m.Statistics.PlacedQuantity, "vat lieu khac van xep");

            NestingRequest qty0 = sheet();
            qty0.Groups.Add(Rect("Q0", 100, 50, 0));
            NestingResult q = NestWithin(qty0, 30, "so luong 0");
            Equal(0, q.Statistics.PlacedQuantity + q.Statistics.UnplacedQuantity, "SL 0 -> khong co ban sao nao");
            ExpectArgument(() => { NestingRequest r = sheet(); r.Groups.Add(Rect("QN", 100, 50, -3)); Nest(r); }, "SL am");

            NestingRequest many = sheet();
            many.Settings.ExtraSeededOrderings = 0;
            many.Groups.Add(Rect("SMALL", 10, 10, 600));
            NestingResult mn = NestWithin(many, 240, "so luong lon (600)");
            Equal(600, mn.Statistics.PlacedQuantity + mn.Statistics.UnplacedQuantity, "600 ban sao du");
        }
    }
}
