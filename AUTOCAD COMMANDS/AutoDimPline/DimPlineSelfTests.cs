using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - BO KIEM THU ENGINE PHAN TICH + BO TRI
    // ------------------------------------------------------------------------------------------
    // File nay KHONG tham chieu AutoCAD nen chay duoc o 2 noi:
    //   1. Trong AutoCAD qua lenh DPA_TEST.
    //   2. Ngoai AutoCAD, bien cung cac file DimPline*.cs thuan thanh console exe.
    // Ngoai cac khang dinh ve GIA TRI, moi test hinh hoc thuc te deu chay them 2 khang dinh
    // ve CHAT LUONG BO CUC:
    //   - khong co cap text dim nao de len nhau
    //   - khong co duong kich thuoc thang nao nam trong bao hinh polyline
    // ==========================================================================================

    public class DimPlineTestReport
    {
        public int Passed { get; set; }

        public int Failed { get; set; }

        public List<string> Lines { get; private set; }

        public DimPlineTestReport()
        {
            Lines = new List<string>();
        }

        public int Total { get { return Passed + Failed; } }

        public bool AllPassed { get { return Failed == 0; } }
    }

    public static class DimPlineSelfTests
    {
        public static DimPlineTestReport Run()
        {
            DimPlineTestReport report = new DimPlineTestReport();

            RunTest(report, "01. Doan ngang don gian", Test01_SimpleHorizontal);
            RunTest(report, "02. Doan doc don gian", Test02_SimpleVertical);
            RunTest(report, "03. Hinh chu nhat kin", Test03_Rectangle);
            RunTest(report, "04. Bien dang chu L", Test04_LShape);
            RunTest(report, "05. Bien dang chu Z", Test05_ZShape);
            RunTest(report, "06. Bien dang nhieu bac", Test06_SteppedProfile);
            RunTest(report, "07. Bien dang kin bat quy tac", Test07_ClosedIrregular);
            RunTest(report, "08. Polyline mo bat quy tac", Test08_OpenIrregular);
            RunTest(report, "09. Doan qua ngan bi bo qua", Test09_ShortSegments);
            RunTest(report, "10. Hinh hoc suy bien khong crash", Test10_Degenerate);
            RunTest(report, "11. Nhieu dim phai xep chong", Test11_Stacking);
            RunTest(report, "12. Va cham giua cac candidate", Test12_CandidateCollision);
            RunTest(report, "13. DIMLFAC = 1", Test13_LinearScaleOne);
            RunTest(report, "14. DIMLFAC = 0.25 khong doi hinh hoc", Test14_LinearScaleQuarter);
            RunTest(report, "15. Layer tuy chon va validation", Test15_LayerAndValidation);
            RunTest(report, "16. Ket qua on dinh khi chay lai", Test16_Deterministic);
            RunTest(report, "17. Doan cung: bo qua an toan / dim ban kinh", Test17_ArcHandling);
            RunTest(report, "18. Gop doan thang hang va dinh trung", Test18_MergeCollinear);
            RunTest(report, "19. Dim bao tong the nam ngoai cung", Test19_OverallOutside);
            RunTest(report, "20. SideBias cho bo cuc lat nguoc", Test20_SideBiasFlip);
            RunTest(report, "21. Feature sau ben trong duoc dim canh feature", Test21_NearFeaturePlacement);
            RunTest(report, "22. Tat dat-canh-feature: moi dim ra bang ngoai", Test22_BandOnlyMode);
            RunTest(report, "23. Stress 400 bien dang ngau nhien: bo cuc luon sach", Test23_RandomStress);
            RunTest(report, "24. Doan xien va cung tron chung mot bien dang", Test24_MixedSkewAndArc);

            report.Lines.Add(string.Empty);
            report.Lines.Add("==================================================");
            report.Lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                " KET QUA: {0} PASS / {1} FAIL  (tong {2})",
                report.Passed, report.Failed, report.Total));
            report.Lines.Add("==================================================");

            return report;
        }

        // ==================================================================================
        // CAC TEST
        // ==================================================================================

        private static void Test01_SimpleHorizontal()
        {
            DimPlinePlan plan = Run(Input(false, 0, 0, 100, 0), Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertEqual(1, plan.Placements.Count, "so dim (feature trung overall -> gop lam 1)");

            DimPlinePlacement p = plan.Placements[0];
            AssertEqual(DimOrientation.Horizontal, p.Orientation, "huong dim");
            AssertClose(100.0, p.MeasuredValue, "gia tri do");
            Assert(p.DimLinePoint.Y < 0.0, "duong kich thuoc phai nam duoi hinh hoc");
            AssertLayoutClean(plan);
        }

        private static void Test02_SimpleVertical()
        {
            DimPlinePlan plan = Run(Input(false, 0, 0, 0, 60), Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertEqual(1, plan.Placements.Count, "so dim");

            DimPlinePlacement p = plan.Placements[0];
            AssertEqual(DimOrientation.Vertical, p.Orientation, "huong dim");
            AssertClose(60.0, p.MeasuredValue, "gia tri do");
            Assert(p.DimLinePoint.X < 0.0, "duong kich thuoc phai nam ben trai hinh hoc");
            AssertLayoutClean(plan);
        }

        private static void Test03_Rectangle()
        {
            // 4 canh nhung chi can 2 kich thuoc - day la phep thu chong "rung dimension".
            DimPlinePlan plan = Run(Input(true, 0, 0, 200, 0, 200, 100, 0, 100), Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertEqual(2, plan.Placements.Count, "hinh chu nhat chi can 2 dim");
            AssertEqual(1, CountOrientation(plan, DimOrientation.Horizontal), "1 dim ngang");
            AssertEqual(1, CountOrientation(plan, DimOrientation.Vertical), "1 dim doc");
            AssertClose(200.0, FindValue(plan, DimOrientation.Horizontal), "chieu rong");
            AssertClose(100.0, FindValue(plan, DimOrientation.Vertical), "chieu cao");
            AssertLayoutClean(plan);

            // Khong bat dim tong the: van phai la 2 dim (2 canh doi dien la trung lap).
            AutoDimPlineSettings noOverall = Settings();
            noOverall.CreateOverall = false;
            DimPlinePlan plan2 = Run(Input(true, 0, 0, 200, 0, 200, 100, 0, 100), noOverall);
            AssertEqual(2, plan2.Placements.Count, "khong dim tong the van chi 2 dim");
            AssertLayoutClean(plan2);
        }

        private static void Test04_LShape()
        {
            DimPlinePlan plan = Run(LShape(), Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertLayoutClean(plan);

            // Canh duoi va canh trai trung voi kich thuoc bao -> chi con dim tong the.
            AssertEqual(2, CountOverall(plan), "2 dim tong the");
            AssertClose(60.0, FindOverall(plan, DimOrientation.Horizontal), "bao ngang");
            AssertClose(40.0, FindOverall(plan, DimOrientation.Vertical), "bao doc");

            // Mat trong cua chu L huong len / sang phai -> dim phai nam o phia do.
            DimPlinePlacement innerHorizontal = FindBySegmentValue(plan, DimOrientation.Horizontal, 40.0);
            Assert(innerHorizontal != null, "phai co dim ngang 40 cua bac trong");
            AssertEqual(DimSide.Top, innerHorizontal.Side, "dim bac trong phai nam phia tren");

            DimPlinePlacement innerVertical = FindBySegmentValue(plan, DimOrientation.Vertical, 20.0);
            Assert(innerVertical != null, "phai co dim doc 20");
            AssertEqual(DimSide.Right, innerVertical.Side, "dim doc trong phai nam phia phai");
        }

        private static void Test05_ZShape()
        {
            DimPlineInput z = Input(false, 0, 0, 40, 0, 40, 30, 80, 30, 80, 60);
            DimPlinePlan plan = Run(z, Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertLayoutClean(plan);
            Assert(plan.Placements.Count >= 4, "bien dang Z phai co it nhat 4 dim");

            foreach (DimPlinePlacement p in plan.Placements)
            {
                Assert(p.Kind == DimKind.Linear, "tat ca doan deu ngang/doc -> dim thang");
            }
        }

        private static void Test06_SteppedProfile()
        {
            DimPlinePlan plan = Run(SteppedProfile(), Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertLayoutClean(plan);

            // Khong duoc gom het ve mot phia: phai dung ca 2 phia cua it nhat mot truc.
            bool horizontalSplit = plan.CountOnSide(DimSide.Bottom) > 0 && plan.CountOnSide(DimSide.Top) > 0;
            bool verticalSplit = plan.CountOnSide(DimSide.Left) > 0 && plan.CountOnSide(DimSide.Right) > 0;
            Assert(horizontalSplit || verticalSplit, "bien dang nhieu bac phai duoc chia ra 2 phia");

            // Bo cuc phai gon: khong duoc sinh ra hang tang vo han.
            foreach (DimSide side in AllSides())
            {
                Assert(plan.RowCount(side) <= 5, "so hang moi phia phai <= 5, thuc te " + plan.RowCount(side));
            }

            AssertOverallOutermost(plan);
        }

        private static void Test07_ClosedIrregular()
        {
            DimPlineInput profile = Input(
                true,
                0, 0,
                120, 0,
                120, 40,
                80, 40,
                80, 70,
                140, 70,
                140, 110,
                0, 110);

            DimPlinePlan plan = Run(profile, Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertLayoutClean(plan);
            AssertOverallOutermost(plan);
            Assert(plan.Placements.Count >= 5, "bien dang kin bat quy tac phai co nhieu dim");
        }

        private static void Test08_OpenIrregular()
        {
            DimPlineInput profile = Input(
                false,
                0, 0,
                50, 0,
                50, 25,
                20, 25,
                20, 60,
                90, 60);

            DimPlinePlan plan = Run(profile, Settings());

            Assert(plan.IsValid, "polyline mo phai xu ly duoc, khong crash");
            AssertLayoutClean(plan);
            Assert(!plan.Analysis.Closed, "phai nhan biet la polyline mo");

            foreach (DimPlineSegment s in plan.Analysis.Segments)
            {
                Assert(!s.HasOutwardNormal, "polyline mo khong duoc bia ra phap tuyen ngoai");
            }
        }

        private static void Test09_ShortSegments()
        {
            // Bac rat nho 0.4 nam giua canh duoi.
            DimPlineInput profile = Input(
                true,
                0, 0,
                40, 0,
                40, 0.4,
                80, 0.4,
                80, 50,
                0, 50);

            AutoDimPlineSettings s = Settings();
            s.MinSegmentLength = 1.0;
            DimPlinePlan plan = Run(profile, s);

            Assert(plan.IsValid, "plan phai hop le");
            Assert(plan.SkippedTooShort >= 1, "phai bo qua it nhat 1 doan ngan hon nguong");

            foreach (DimPlinePlacement p in plan.Placements)
            {
                Assert(p.MeasuredValue >= 1.0 - 1e-9 || p.IsOverall,
                    "khong duoc tao dim cho doan ngan hon nguong: " + p.MeasuredValue);
            }

            // Ha nguong xuong thi doan 0.4 phai duoc dim.
            AutoDimPlineSettings s2 = Settings();
            s2.MinSegmentLength = 0.1;
            DimPlinePlan plan2 = Run(profile, s2);
            Assert(plan2.SkippedTooShort == 0, "nguong 0.1 thi khong duoc bo qua doan nao");
            AssertLayoutClean(plan2);
        }

        private static void Test10_Degenerate()
        {
            // Hai dinh trung nhau.
            DimPlinePlan a = Run(Input(false, 5, 5, 5, 5), Settings());
            Assert(!a.IsValid, "hinh hoc suy bien phai bao loi thay vi tao dim rac");
            Assert(a.Placements.Count == 0, "khong duoc tao dim nao");

            // Mot dinh duy nhat.
            DimPlinePlan b = Run(Input(false, 1, 1), Settings());
            Assert(!b.IsValid, "1 dinh phai bi tu choi");

            // Null input.
            DimPlinePlan c = DimPlinePlanner.Plan((DimPlineInput)null, Settings(), Style());
            Assert(!c.IsValid, "input null phai bi tu choi chu khong nem exception");

            // Chuoi dinh co dinh trung xen giua nhung van hop le.
            DimPlinePlan d = Run(Input(false, 0, 0, 0, 0, 100, 0, 100, 0, 100, 50), Settings());
            Assert(d.IsValid, "dinh trung xen giua van phai xu ly duoc");
            Assert(d.Analysis.DroppedZeroLengthSegments == 2, "phai loai dung 2 doan dai 0");
            AssertLayoutClean(d);
        }

        private static void Test11_Stacking()
        {
            // Cac canh ngang chong lan nhau tren truc X -> bat buoc phai xep nhieu hang.
            DimPlineInput profile = Staircase();

            // Tat che do dat canh feature de kiem dung thuat toan xep hang cua bang ngoai.
            AutoDimPlineSettings bandOnly = Settings();
            bandOnly.PlaceNearFeature = false;
            DimPlinePlan plan = Run(profile, bandOnly);

            Assert(plan.IsValid, "plan phai hop le");
            AssertLayoutClean(plan);

            int maxRow = 0;
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Row > maxRow) maxRow = p.Row;
            }

            Assert(maxRow >= 1, "phai co it nhat 2 tang dim (xep chong)");

            // Buoc giua 2 hang lien tiep phai dung bang spacing hieu dung.
            AssertRowSpacing(plan);

            // Bat lai che do dat canh feature: bo cuc van phai sach.
            AssertLayoutClean(Run(Staircase(), Settings()));
        }

        private static void Test12_CandidateCollision()
        {
            // Hai canh ngang cung phia, khoang do chong nhau hoan toan.
            DimPlineInput profile = Input(
                true,
                0, 0,
                100, 0,
                100, 6,
                5, 6,
                5, 40,
                0, 40);

            DimPlinePlan plan = Run(profile, Settings());

            Assert(plan.IsValid, "plan phai hop le");
            Assert(CountOrientation(plan, DimOrientation.Horizontal) >= 2, "phai co it nhat 2 dim ngang");

            // Tieu chi that: khong cap dim cung phuong nao vua trung toa do vua chong khoang do.
            AssertLayoutClean(plan);

            // Va cham cung phai duoc giai quyet khi ep het ve mot phia.
            AutoDimPlineSettings forced = Settings();
            forced.SideBias = DimPlineSideBias.BottomLeft;
            forced.PlaceNearFeature = false;
            AssertLayoutClean(Run(profile, forced));
        }

        private static void Test13_LinearScaleOne()
        {
            AutoDimPlineSettings s = Settings();
            s.LinearScale = 1.0;
            DimPlinePlan plan = Run(Input(true, 0, 0, 200, 0, 200, 100, 0, 100), s);

            AssertClose(200.0, FindValue(plan, DimOrientation.Horizontal), "gia tri do hinh hoc");
            AssertEqual("200.00", FindText(plan, DimOrientation.Horizontal), "text hien thi");
            AssertEqual("100.00", FindText(plan, DimOrientation.Vertical), "text hien thi doc");
            AssertLayoutClean(plan);
        }

        private static void Test14_LinearScaleQuarter()
        {
            DimPlineInput geometry = Input(true, 0, 0, 200, 0, 200, 100, 0, 100);

            AutoDimPlineSettings one = Settings();
            one.LinearScale = 1.0;
            DimPlinePlan planOne = Run(geometry, one);

            AutoDimPlineSettings quarter = Settings();
            quarter.LinearScale = 0.25;
            DimPlinePlan planQuarter = Run(Input(true, 0, 0, 200, 0, 200, 100, 0, 100), quarter);

            AssertEqual(planOne.Placements.Count, planQuarter.Placements.Count, "so dim phai giong nhau");

            // Gia tri DO va MOI TOA DO phai y het nhau - DIMLFAC chi doi CHUOI HIEN THI.
            for (int i = 0; i < planOne.Placements.Count; i++)
            {
                DimPlinePlacement a = planOne.Placements[i];
                DimPlinePlacement b = planQuarter.Placements[i];

                AssertClose(a.MeasuredValue, b.MeasuredValue, "gia tri do hinh hoc khong duoc doi");
                AssertClose(a.DimLinePoint.X, b.DimLinePoint.X, "toa do X duong kich thuoc khong duoc doi");
                AssertClose(a.DimLinePoint.Y, b.DimLinePoint.Y, "toa do Y duong kich thuoc khong duoc doi");
                AssertClose(a.DefPoint1.X, b.DefPoint1.X, "diem goc 1 X khong doi");
                AssertClose(a.DefPoint1.Y, b.DefPoint1.Y, "diem goc 1 Y khong doi");
            }

            AssertClose(200.0, FindValue(planQuarter, DimOrientation.Horizontal), "gia tri do van la 200 drawing unit");
            AssertEqual("50.00", FindText(planQuarter, DimOrientation.Horizontal), "text hien thi phai la 50");
            AssertEqual("25.00", FindText(planQuarter, DimOrientation.Vertical), "text hien thi doc phai la 25");

            // Offset dat dim phai tinh theo drawing unit, khong duoc nhan DIMLFAC.
            DimPlinePlacement horizontal = First(planQuarter, DimOrientation.Horizontal);
            double expectedOffset = planQuarter.EffectiveDistance + planQuarter.EffectiveSpacing * 0.5;
            AssertClose(-expectedOffset, horizontal.DimLinePoint.Y, "khoang cach dat dim la drawing unit");
        }

        private static void Test15_LayerAndValidation()
        {
            AutoDimPlineSettings s = Settings();
            s.DimensionLayer = "MY_DIM_LAYER";
            DimPlinePlan plan = Run(LShape(), s);
            Assert(plan.IsValid, "layer tuy chon khong duoc anh huong toi plan");

            AutoDimPlineSettings bad = Settings();
            bad.DimensionLayer = "   ";
            Assert(bad.Validate().Count == 1, "layer rong phai bao loi");

            bad = Settings();
            bad.LinearScale = 0.0;
            Assert(bad.Validate().Count == 1, "linear scale 0 phai bao loi");

            bad = Settings();
            bad.DimensionSpacing = -5.0;
            Assert(bad.Validate().Count == 1, "spacing am phai bao loi");

            bad = Settings();
            bad.MinSegmentLength = double.NaN;
            Assert(bad.Validate().Count == 1, "NaN phai bao loi");

            DimPlinePlan rejected = DimPlinePlanner.Plan(LShape(), bad, Style());
            Assert(!rejected.IsValid, "settings sai phai lam plan that bai co thong bao");
            Assert(rejected.ErrorMessage.Length > 0, "phai co thong bao loi");
        }

        private static void Test16_Deterministic()
        {
            DimPlinePlan a = Run(LShape(), Settings());
            DimPlinePlan b = Run(LShape(), Settings());

            AssertEqual(a.Placements.Count, b.Placements.Count, "so dim phai on dinh giua 2 lan chay");
            for (int i = 0; i < a.Placements.Count; i++)
            {
                AssertClose(a.Placements[i].DimLinePoint.X, b.Placements[i].DimLinePoint.X, "toa do on dinh X");
                AssertClose(a.Placements[i].DimLinePoint.Y, b.Placements[i].DimLinePoint.Y, "toa do on dinh Y");
                AssertEqual(a.Placements[i].Side, b.Placements[i].Side, "phia on dinh");
                AssertEqual(a.Placements[i].Row, b.Placements[i].Row, "hang on dinh");
            }
        }

        private static void Test17_ArcHandling()
        {
            // Hinh chu nhat bo tron mot goc: bulge 1 = nua duong tron, dung bulge nho hon cho fillet.
            DimPlineInput profile = new DimPlineInput();
            profile.Closed = true;
            profile.Add(0, 0);
            profile.Add(100, 0);
            profile.Add(100, 40, Math.Tan(Math.PI / 8.0)); // cung 90 do
            profile.Add(80, 60);
            profile.Add(0, 60);

            AutoDimPlineSettings skip = Settings();
            skip.ArcMode = DimPlineArcMode.Skip;
            DimPlinePlan planSkip = Run(profile, skip);

            Assert(planSkip.IsValid, "polyline co cung phai xu ly duoc");
            AssertEqual(1, planSkip.SkippedArcs, "phai bao cao dung 1 cung bi bo qua");
            foreach (DimPlinePlacement p in planSkip.Placements)
            {
                Assert(p.Kind != DimKind.Radial, "che do Skip khong duoc tao dim ban kinh");
                Assert(p.Kind != DimKind.Aligned || p.MeasuredValue > 0.0, "khong tao dim rac");
            }

            // Bao hinh phai tinh ca phan phinh cua cung, khong chi 2 dau mut.
            Assert(planSkip.Analysis.Bounds.MaxX >= 100.0 - 1e-9, "bao hinh phai bao ca cung");

            AutoDimPlineSettings radius = Settings();
            radius.ArcMode = DimPlineArcMode.Radius;
            DimPlinePlan planRadius = Run(profile, radius);
            AssertEqual(0, planRadius.SkippedArcs, "che do Radius khong bo qua cung");
            Assert(CountKind(planRadius, DimKind.Radial) == 1, "phai co dung 1 dim ban kinh");
            AssertClose(20.0, FindKindValue(planRadius, DimKind.Radial), "ban kinh fillet", 1e-6);
        }

        private static void Test18_MergeCollinear()
        {
            // Canh duoi bi chia lam 3 dinh -> chi duoc sinh ra 1 dim, khong phai 3 dim vun.
            DimPlineInput profile = Input(
                true,
                0, 0,
                30, 0,
                60, 0,
                100, 0,
                100, 50,
                0, 50);

            DimPlinePlan plan = Run(profile, Settings());

            Assert(plan.IsValid, "plan phai hop le");
            AssertEqual(4, plan.Analysis.Segments.Count, "3 doan thang hang phai duoc gop lam 1");
            AssertEqual(2, plan.Analysis.MergedCollinearSegments, "phai gop dung 2 lan");
            AssertEqual(2, plan.Placements.Count, "ket qua phai la 2 dim nhu hinh chu nhat thuong");
            AssertLayoutClean(plan);
        }

        private static void Test19_OverallOutside()
        {
            DimPlinePlan plan = Run(LShape(), Settings());
            AssertOverallOutermost(plan);

            // Tang tong the phai cach tang feature it nhat mot buoc spacing.
            foreach (DimPlinePlacement overall in plan.Placements)
            {
                if (!overall.IsOverall)
                {
                    continue;
                }

                foreach (DimPlinePlacement feature in plan.Placements)
                {
                    if (feature.IsOverall || feature.Side != overall.Side ||
                        feature.Orientation != overall.Orientation)
                    {
                        continue;
                    }

                    double gap = overall.Orientation == DimOrientation.Horizontal
                        ? Math.Abs(overall.DimLinePoint.Y - feature.DimLinePoint.Y)
                        : Math.Abs(overall.DimLinePoint.X - feature.DimLinePoint.X);

                    Assert(gap >= plan.EffectiveSpacing - 1e-9,
                        "dim tong the phai cach dim feature it nhat 1 buoc, thuc te " + gap);
                }
            }
        }

        private static void Test20_SideBiasFlip()
        {
            AutoDimPlineSettings auto = Settings();
            DimPlinePlan planAuto = Run(LShape(), auto);

            AutoDimPlineSettings flip = Settings();
            flip.SideBias = DimPlineSideBias.Flip;
            DimPlinePlan planFlip = Run(LShape(), flip);

            AssertEqual(planAuto.Placements.Count, planFlip.Placements.Count, "so dim khong doi khi lat phia");
            AssertLayoutClean(planFlip);

            int flipped = 0;
            for (int i = 0; i < planAuto.Placements.Count; i++)
            {
                if (planAuto.Placements[i].IsOverall)
                {
                    continue;
                }

                if (planAuto.Placements[i].Side != planFlip.Placements[i].Side)
                {
                    flipped++;
                }
            }

            Assert(flipped > 0, "SideBias=Flip phai doi phia cua it nhat mot dim");

            AutoDimPlineSettings forced = Settings();
            forced.SideBias = DimPlineSideBias.BottomLeft;
            DimPlinePlan planForced = Run(LShape(), forced);
            foreach (DimPlinePlacement p in planForced.Placements)
            {
                if (p.Kind != DimKind.Linear)
                {
                    continue;
                }

                Assert(p.Side == DimSide.Bottom || p.Side == DimSide.Left,
                    "BottomLeft phai ep moi dim ve duoi/trai, thuc te " + p.Side);
            }

            AssertLayoutClean(planForced);
        }

        private static void Test21_NearFeaturePlacement()
        {
            DimPlineInput stepped = SteppedProfile();

            AutoDimPlineSettings near = Settings();
            near.PlaceNearFeature = true;
            DimPlinePlan planNear = Run(stepped, near);

            AutoDimPlineSettings band = Settings();
            band.PlaceNearFeature = false;
            DimPlinePlan planBand = Run(SteppedProfile(), band);

            Assert(planNear.IsValid && planBand.IsValid, "ca 2 che do phai chay duoc");
            AssertLayoutClean(planNear);
            AssertLayoutClean(planBand);

            AssertEqual(planBand.Placements.Count, planNear.Placements.Count,
                "che do dat canh feature khong duoc lam mat hay them dim");

            Assert(planNear.NearFeatureCount > 0,
                "bien dang nhieu bac phai co it nhat mot dim dat canh feature");

            // Loi ich phai do duoc: tong chieu dai duong giong giam han.
            double nearTotal = TotalExtensionLength(planNear);
            double bandTotal = TotalExtensionLength(planBand);
            Assert(nearTotal < bandTotal,
                "tong chieu dai duong giong phai giam, " + nearTotal + " so voi " + bandTotal);

            // Va moi dim dat canh feature phai that su ngan.
            foreach (DimPlinePlacement p in planNear.Placements)
            {
                if (!p.IsNearFeature || p.Kind != DimKind.Linear)
                {
                    continue;
                }

                double ext = p.Orientation == DimOrientation.Horizontal
                    ? Math.Abs(p.DimLinePoint.Y - p.DefPoint1.Y)
                    : Math.Abs(p.DimLinePoint.X - p.DefPoint1.X);

                double maxAllowed = planNear.EffectiveDistance + 2.0 * planNear.EffectiveSpacing + 1e-9;
                Assert(ext <= maxAllowed, "duong giong cua dim canh feature phai ngan, thuc te " + ext);
            }
        }

        private static void Test22_BandOnlyMode()
        {
            AutoDimPlineSettings band = Settings();
            band.PlaceNearFeature = false;

            DimPlinePlan plan = Run(SteppedProfile(), band);
            Assert(plan.IsValid, "plan phai hop le");
            AssertEqual(0, plan.NearFeatureCount, "tat che do thi khong duoc co dim canh feature");
            AssertLayoutClean(plan);

            // Tat ca dim thang phai nam ngoai bao hinh.
            DimBox b = plan.Analysis.Bounds;
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Kind != DimKind.Linear)
                {
                    continue;
                }

                if (p.Orientation == DimOrientation.Horizontal)
                {
                    Assert(p.DimLinePoint.Y <= b.MinY + 1e-9 || p.DimLinePoint.Y >= b.MaxY - 1e-9,
                        "dim ngang phai nam ngoai bao hinh");
                }
                else
                {
                    Assert(p.DimLinePoint.X <= b.MinX + 1e-9 || p.DimLinePoint.X >= b.MaxX - 1e-9,
                        "dim doc phai nam ngoai bao hinh");
                }
            }
        }

        /// <summary>
        /// Sinh hang tram bien dang truc giao NGAU NHIEN (seed co dinh nen lap lai duoc) va
        /// doi hoi MOI bien dang deu qua duoc 4 tieu chi bo cuc. Day la bang chung thuat toan
        /// tong quat chu khong phai duoc chinh rieng cho vai hinh mau.
        /// </summary>
        private static void Test23_RandomStress()
        {
            Random random = new Random(20260921);
            int checkedShapes = 0;

            for (int iteration = 0; iteration < 400; iteration++)
            {
                bool closed = (iteration % 2) == 0;
                DimPlineInput input = RandomOrthogonalProfile(random, closed);

                AutoDimPlineSettings settings = Settings();
                settings.PlaceNearFeature = (iteration % 4) < 2;
                settings.CreateOverall = (iteration % 3) != 0;
                settings.SideBias = (DimPlineSideBias)(iteration % 4);
                settings.LinearScale = (iteration % 5) == 0 ? 0.25 : 1.0;

                DimPlinePlan plan = DimPlinePlanner.Plan(input, settings, Style());

                if (!plan.IsValid)
                {
                    // Hinh suy bien bi tu choi la hop le, mien la co ly do ro rang.
                    Assert(plan.ErrorMessage.Length > 0, "plan khong hop le thi phai co thong bao");
                    continue;
                }

                checkedShapes++;

                string where = " (iteration " + iteration + ", " + input.Vertices.Count + " dinh, closed=" + closed + ")";
                AssertEqual(0, plan.CountTextOverlaps(0.0), "text dim de len nhau" + where);
                AssertEqual(0, plan.CountOverlappingDimLines(1e-9), "duong kich thuoc de len nhau" + where);
                AssertEqual(0, plan.CountDimLinesCrossingGeometry(), "duong kich thuoc cat qua net ve" + where);
                AssertEqual(0, plan.CountExtensionLinesCrossingGeometry(true),
                    "duong giong cua dim canh feature cat qua net ve" + where);

                // Bo cuc phai gon: khong duoc no ra vo han vi mot hinh la.
                foreach (DimSide side in AllSides())
                {
                    Assert(plan.RowCount(side) <= 12,
                        "so hang mot phia qua nhieu: " + plan.RowCount(side) + where);
                }

                // Gia tri do luon la drawing unit that, khong bao gio bi nhan DIMLFAC.
                foreach (DimPlinePlacement p in plan.Placements)
                {
                    if (p.Kind != DimKind.Linear)
                    {
                        continue;
                    }

                    double span = p.Orientation == DimOrientation.Horizontal
                        ? Math.Abs(p.DefPoint2.X - p.DefPoint1.X)
                        : Math.Abs(p.DefPoint2.Y - p.DefPoint1.Y);

                    AssertClose(span, p.MeasuredValue, "gia tri do phai bang khoang hinh hoc that" + where);
                }
            }

            Assert(checkedShapes > 300, "phai kiem duoc phan lon so hinh, thuc te " + checkedShapes);
        }

        /// <summary>
        /// Bien dang bac thang truc giao ngau nhien: luon xen ke ngang - doc nen la hinh hoc
        /// thuc te ma lenh se gap, khong phai nhieu vo nghia.
        /// </summary>
        private static DimPlineInput RandomOrthogonalProfile(Random random, bool closed)
        {
            DimPlineInput input = new DimPlineInput();
            input.Closed = closed;

            int steps = 2 + random.Next(7);
            double x = 0.0;
            double y = 0.0;
            input.Add(x, y);

            for (int i = 0; i < steps; i++)
            {
                x += 1.0 + random.Next(80);
                input.Add(x, y);

                y += 1.0 + random.Next(60);
                input.Add(x, y);
            }

            // Dong ve truc X roi truc Y de bien dang kin khong tu cat chinh no.
            input.Add(0.0, y);
            return input;
        }

        private static void Test24_MixedSkewAndArc()
        {
            // Bien dang co ca canh vat (xien) lan bo tron (cung) cung luc.
            DimPlineInput profile = new DimPlineInput();
            profile.Closed = true;
            profile.Add(0, 0);
            profile.Add(100, 0);
            profile.Add(100, 30, Math.Tan(Math.PI / 8.0)); // fillet 90 do
            profile.Add(80, 50);
            profile.Add(50, 80);                            // canh vat xien
            profile.Add(0, 80);

            AutoDimPlineSettings settings = Settings();
            settings.ArcMode = DimPlineArcMode.Radius;
            settings.SkewMode = DimPlineSkewMode.Aligned;

            DimPlinePlan plan = Run(profile, settings);

            Assert(plan.IsValid, "plan phai hop le");
            AssertLayoutClean(plan);
            Assert(CountKind(plan, DimKind.Radial) == 1, "phai co 1 dim ban kinh");
            Assert(CountKind(plan, DimKind.Aligned) >= 1, "phai co it nhat 1 dim xien");

            // Bo qua ca hai loai thi khong con dim cong/xien nao va phai bao cao so luong.
            AutoDimPlineSettings skipBoth = Settings();
            skipBoth.ArcMode = DimPlineArcMode.Skip;
            skipBoth.SkewMode = DimPlineSkewMode.Skip;

            DimPlinePlan planSkip = Run(profile, skipBoth);
            AssertEqual(0, CountKind(planSkip, DimKind.Radial), "khong duoc tao dim ban kinh");
            AssertEqual(0, CountKind(planSkip, DimKind.Aligned), "khong duoc tao dim xien");
            Assert(planSkip.SkippedArcs == 1, "phai bao cao 1 cung bi bo qua");
            Assert(planSkip.SkippedSkew >= 1, "phai bao cao doan xien bi bo qua");
            AssertLayoutClean(planSkip);
        }

        private static double TotalExtensionLength(DimPlinePlan plan)
        {
            double total = 0.0;
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Kind != DimKind.Linear)
                {
                    continue;
                }

                total += p.Orientation == DimOrientation.Horizontal
                    ? Math.Abs(p.DimLinePoint.Y - p.DefPoint1.Y) * 2.0
                    : Math.Abs(p.DimLinePoint.X - p.DefPoint1.X) * 2.0;
            }

            return total;
        }

        // ==================================================================================
        // TIEN ICH TEST
        // ==================================================================================

        private static AutoDimPlineSettings Settings()
        {
            AutoDimPlineSettings s = new AutoDimPlineSettings();
            s.DistanceFromPline = 20.0;
            s.DimensionSpacing = 15.0;
            s.MinSegmentLength = 1.0;
            return s;
        }

        private static DimPlineStyle Style()
        {
            return new DimPlineStyle(2.5, 2.5, 2);
        }

        private static DimPlinePlan Run(DimPlineInput input, AutoDimPlineSettings settings)
        {
            return DimPlinePlanner.Plan(input, settings, Style());
        }

        private static DimPlineInput Input(bool closed, params double[] coordinates)
        {
            DimPlineInput input = new DimPlineInput();
            input.Closed = closed;
            for (int i = 0; i + 1 < coordinates.Length; i += 2)
            {
                input.Add(coordinates[i], coordinates[i + 1]);
            }

            return input;
        }

        private static DimPlineInput LShape()
        {
            return Input(true, 0, 0, 60, 0, 60, 20, 20, 20, 20, 40, 0, 40);
        }

        private static DimPlineInput Staircase()
        {
            return Input(true, 0, 0, 40, 0, 40, 10, 10, 10, 10, 20, 30, 20, 30, 30, 0, 30);
        }

        // Bien dang nhieu bac kieu profile nhom / ton: chuoi ngang-doc xen ke, co feature
        // nam sau ben trong bao hinh.
        private static DimPlineInput SteppedProfile()
        {
            return Input(
                false,
                0, 296,
                0, 256,
                64, 256,
                64, 168,
                48, 168,
                48, 80,
                88, 80,
                88, 0,
                0, 0);
        }

        private static DimSide[] AllSides()
        {
            return new[] { DimSide.Bottom, DimSide.Top, DimSide.Left, DimSide.Right };
        }

        private static List<DimPlinePlacement> OnSide(DimPlinePlan plan, DimSide side)
        {
            List<DimPlinePlacement> result = new List<DimPlinePlacement>();
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Side == side && p.Kind == DimKind.Linear)
                {
                    result.Add(p);
                }
            }

            return result;
        }

        private static int CountOrientation(DimPlinePlan plan, DimOrientation orientation)
        {
            int n = 0;
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Orientation == orientation) n++;
            }
            return n;
        }

        private static int CountOverall(DimPlinePlan plan)
        {
            int n = 0;
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.IsOverall) n++;
            }
            return n;
        }

        private static int CountKind(DimPlinePlan plan, DimKind kind)
        {
            int n = 0;
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Kind == kind) n++;
            }
            return n;
        }

        private static double FindKindValue(DimPlinePlan plan, DimKind kind)
        {
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Kind == kind) return p.MeasuredValue;
            }
            return double.NaN;
        }

        private static DimPlinePlacement First(DimPlinePlan plan, DimOrientation orientation)
        {
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.Orientation == orientation) return p;
            }
            return null;
        }

        private static double FindValue(DimPlinePlan plan, DimOrientation orientation)
        {
            DimPlinePlacement p = First(plan, orientation);
            return p == null ? double.NaN : p.MeasuredValue;
        }

        private static string FindText(DimPlinePlan plan, DimOrientation orientation)
        {
            DimPlinePlacement p = First(plan, orientation);
            return p == null ? string.Empty : p.DisplayText;
        }

        private static double FindOverall(DimPlinePlan plan, DimOrientation orientation)
        {
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (p.IsOverall && p.Orientation == orientation) return p.MeasuredValue;
            }
            return double.NaN;
        }

        private static DimPlinePlacement FindBySegmentValue(DimPlinePlan plan, DimOrientation orientation, double value)
        {
            foreach (DimPlinePlacement p in plan.Placements)
            {
                if (!p.IsOverall && p.Orientation == orientation && Math.Abs(p.MeasuredValue - value) < 1e-6)
                {
                    return p;
                }
            }

            return null;
        }

        // ==================================================================================
        // KHANG DINH CHAT LUONG BO CUC
        // ==================================================================================

        // Bon tieu chi nay la dinh nghia "bo cuc dat" cua lenh. Moi test hinh hoc that deu
        // phai qua ca bon, khong chi kiem gia tri do.
        private static void AssertLayoutClean(DimPlinePlan plan)
        {
            AssertEqual(0, plan.CountTextOverlaps(0.0), "so cap text dim de len nhau");
            AssertEqual(0, plan.CountOverlappingDimLines(1e-9), "so cap duong kich thuoc de len nhau");
            AssertEqual(0, plan.CountDimLinesCrossingGeometry(), "so duong kich thuoc cat qua net ve");
            AssertEqual(0, plan.CountExtensionLinesCrossingGeometry(true), "so duong giong cua dim canh feature cat qua net ve");
        }

        private static void AssertRowSpacing(DimPlinePlan plan)
        {
            foreach (DimSide side in AllSides())
            {
                Dictionary<int, double> rowCoordinate = new Dictionary<int, double>();

                foreach (DimPlinePlacement p in plan.Placements)
                {
                    if (p.Side != side || p.Kind != DimKind.Linear || p.IsOverall || p.IsNearFeature)
                    {
                        continue;
                    }

                    double coord = (side == DimSide.Bottom || side == DimSide.Top)
                        ? p.DimLinePoint.Y
                        : p.DimLinePoint.X;

                    if (rowCoordinate.ContainsKey(p.Row))
                    {
                        AssertClose(rowCoordinate[p.Row], coord, "moi dim cung hang phai cung mot toa do");
                    }
                    else
                    {
                        rowCoordinate[p.Row] = coord;
                    }
                }

                foreach (KeyValuePair<int, double> entry in rowCoordinate)
                {
                    int nextRow = entry.Key + 1;
                    if (!rowCoordinate.ContainsKey(nextRow))
                    {
                        continue;
                    }

                    double gap = Math.Abs(rowCoordinate[nextRow] - entry.Value);
                    AssertClose(plan.EffectiveSpacing, gap, "buoc giua 2 hang lien tiep");
                }
            }
        }

        private static void AssertOverallOutermost(DimPlinePlan plan)
        {
            foreach (DimPlinePlacement overall in plan.Placements)
            {
                if (!overall.IsOverall)
                {
                    continue;
                }

                foreach (DimPlinePlacement feature in plan.Placements)
                {
                    if (feature.IsOverall || feature.Side != overall.Side ||
                        feature.Kind != DimKind.Linear || feature.Orientation != overall.Orientation)
                    {
                        continue;
                    }

                    switch (overall.Side)
                    {
                        case DimSide.Bottom:
                            Assert(overall.DimLinePoint.Y < feature.DimLinePoint.Y,
                                "dim tong the phia duoi phai nam ngoai dim feature");
                            break;
                        case DimSide.Top:
                            Assert(overall.DimLinePoint.Y > feature.DimLinePoint.Y,
                                "dim tong the phia tren phai nam ngoai dim feature");
                            break;
                        case DimSide.Left:
                            Assert(overall.DimLinePoint.X < feature.DimLinePoint.X,
                                "dim tong the phia trai phai nam ngoai dim feature");
                            break;
                        default:
                            Assert(overall.DimLinePoint.X > feature.DimLinePoint.X,
                                "dim tong the phia phai phai nam ngoai dim feature");
                            break;
                    }
                }
            }
        }

        // ==================================================================================
        // KHUNG TEST
        // ==================================================================================

        private sealed class DimPlineAssertException : Exception
        {
            public DimPlineAssertException(string message) : base(message)
            {
            }
        }

        private static void RunTest(DimPlineTestReport report, string name, Action test)
        {
            try
            {
                test();
                report.Passed++;
                report.Lines.Add("PASS  " + name);
            }
            catch (DimPlineAssertException ex)
            {
                report.Failed++;
                report.Lines.Add("FAIL  " + name + "  ->  " + ex.Message);
            }
            catch (Exception ex)
            {
                report.Failed++;
                report.Lines.Add("FAIL  " + name + "  ->  EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new DimPlineAssertException(message);
            }
        }

        private static void AssertEqual(int expected, int actual, string message)
        {
            if (expected != actual)
            {
                throw new DimPlineAssertException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi {1}, nhan {2}", message, expected, actual));
            }
        }

        private static void AssertEqual(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new DimPlineAssertException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi '{1}', nhan '{2}'", message, expected, actual));
            }
        }

        private static void AssertEqual(object expected, object actual, string message)
        {
            if (!Equals(expected, actual))
            {
                throw new DimPlineAssertException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi {1}, nhan {2}", message, expected, actual));
            }
        }

        private static void AssertClose(double expected, double actual, string message)
        {
            AssertClose(expected, actual, message, 1e-6);
        }

        private static void AssertClose(double expected, double actual, string message, double tolerance)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new DimPlineAssertException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi {1}, nhan {2}", message, expected, actual));
            }
        }
    }
}
