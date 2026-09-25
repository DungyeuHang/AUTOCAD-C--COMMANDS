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

        private static void C29_TimeBudgetOvershootIsReported()
        {
            NestingRequest r = Request(2500, 1250);
            r.Settings.TimeBudgetSeconds = 0.05;
            r.Settings.ExtraSeededOrderings = 3;

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
            True(res.Statistics.Materials[0].TimeBudgetHit,
                "da chay qua thoi gian cho phep thi phai bao, khong duoc im lang");
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
    }
}
