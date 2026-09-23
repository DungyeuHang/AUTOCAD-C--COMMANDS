using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - BO KIEM THU CALCULATION ENGINE
    // ------------------------------------------------------------------------------------------
    // File nay KHONG tham chieu AutoCAD, nen bo test chay duoc o 2 noi:
    //   1. Trong AutoCAD qua lenh DX_FOIL_TEST.
    //   2. Ngoai AutoCAD, bien cung cac file Foil*.cs thuan thanh console exe (dung khi phat trien).
    // ==========================================================================================

    public class FoilTestReport
    {
        public int Passed { get; set; }

        public int Failed { get; set; }

        public List<string> Lines { get; private set; } = new List<string>();

        public int Total { get { return Passed + Failed; } }

        public bool AllPassed { get { return Failed == 0; } }
    }

    public static class FoilSelfTests
    {
        private const double Tol = 1e-6;

        public static FoilTestReport Run()
        {
            FoilTestReport report = new FoilTestReport();

            RunTest(report, "01. Mot chan 90 do - quy tac xuong (N * T)", Test01_SingleBendCustomRule);
            RunTest(report, "02. Mot chan 90 do - K-factor (golden numbers)", Test02_SingleBendKFactorGolden);
            RunTest(report, "03. Hai chan - bien dang U", Test03_TwoBendsUProfile);
            RunTest(report, "04. Ba chan - bien dang nhieu bac", Test04_ThreeBends);
            RunTest(report, "05. Doi chieu day T", Test05_DifferentThickness);
            RunTest(report, "06. Doi ban kinh trong R", Test06_DifferentRadius);
            RunTest(report, "07. Doi K-factor", Test07_DifferentKFactor);
            RunTest(report, "08. Chan goc nhon (45 do)", Test08_AcuteBend);
            RunTest(report, "09. Chan goc tu (135 do)", Test09_ObtuseBend);
            RunTest(report, "10. Quy tac xuong: canh co 2 duong chan", Test10_ShopRuleTwoBendsOnOneFlange);
            RunTest(report, "11. Duong chan XIEN - hinh hoc vector", Test11_DiagonalBendGeometry);
            RunTest(report, "12. Polyline CW cho ket qua giong CCW", Test12_CwVsCcwConsistency);
            RunTest(report, "13. Dao chieu ve polyline khong doi ket qua", Test13_ReversedDrawOrder);
            RunTest(report, "14. Polyline khong hop le", Test14_InvalidPolyline);
            RunTest(report, "15. Chieu day = 0 bi tu choi", Test15_ZeroThickness);
            RunTest(report, "16. Chieu dai phoi = 0 bi tu choi", Test16_ZeroLength);
            RunTest(report, "17. K-factor khong hop le bi tu choi", Test17_InvalidKFactor);
            RunTest(report, "18. Cung ve san tro thanh bend that", Test18_ArcBend);
            RunTest(report, "19. Bien dang KIN duoc mo va trien khai", Test19_ClosedProfile);
            RunTest(report, "20. Nhan dien duong bao CO BE DAY", Test20_ThicknessOutlineDetection);
            RunTest(report, "21. Vi tri duong chan KHONG chia deu", Test21_BendPositionsNotUniform);
            RunTest(report, "22. Bend Allowance va Bend Deduction cho cung ket qua", Test22_BaEqualsBdMethod);
            RunTest(report, "23. Canh qua ngan so voi setback bi tu choi", Test23_FlangeTooShort);
            RunTest(report, "24. Clip duong thang vo han bang bien phoi", Test24_LineClipper);
            RunTest(report, "25. Hieu chuan giai nguoc ra K-factor", Test25_CalibrationRoundTrip);
            RunTest(report, "26. Bang chan: tra cuu va noi suy", Test26_BendTableLookup);
            RunTest(report, "27. Bien dang Z: mot chan UP, mot chan DOWN", Test27_ZProfileDirections);
            RunTest(report, "28. Doan dai 0 bi loai bo", Test28_ZeroLengthSegmentRemoved);
            RunTest(report, "29. Diem noi tiep tuyen khong bi coi la bend", Test29_CollinearNotBend);
            RunTest(report, "30. Phoi xoay: hinh hoc transform dung", Test30_RotatedBlank);
            RunTest(report, "31. Huong chan dung ca khi goc chan gan 180 do", Test31_NearFlatBendDirection);
            RunTest(report, "32. Tach doi cung - toan hoc bulge dung", Test32_ArcSplit);
            RunTest(report, "33. Bien dang KIN co cung (mo tai doan cung)", Test33_ClosedProfileWithArcs);
            RunTest(report, "34. Buoc chan: B0 dung bang phoi phang", Test34_SequenceStepZeroIsFlat);
            RunTest(report, "35. Buoc chan: buoc cuoi trung bien dang goc", Test35_SequenceLastStepMatchesProfile);
            RunTest(report, "36. Buoc chan: thu tu chan", Test36_SequenceOrders);
            RunTest(report, "37. Buoc chan: hinh dang trung gian dung", Test37_SequenceIntermediateShape);
            RunTest(report, "38. Bu be day CHI tai goc lom (duong bao ngoai)", Test38_ThicknessOnConcaveOnly);
            RunTest(report, "39. Bu be day voi goc chan khac 90 do", Test39_ThicknessNonRightAngle);
            RunTest(report, "40. Doi huong bu be day", Test40_CompensationModeSwitch);
            RunTest(report, "41. Doi nhan UP/DOWN KHONG lam doi kich thuoc phoi", Test41_DisplayFlipDoesNotChangeSize);
            RunTest(report, "42. Bien dang lech so: dao chieu cho phoi KHAC nhau", Test42_UnbalancedProfileReverse);
            RunTest(report, "43. Dao: mui nhon roi luoi song song", Test43_PunchProfile);
            RunTest(report, "44. He toa do may: goc chan luon MO LEN", Test44_MachineFrameOpensUp);
            RunTest(report, "45. Chu Z bat buoc lat ton dung mot lan", Test45_ZProfileNeedsOneFlip);
            RunTest(report, "46. Mu doi xung: thu tu ve bi ket, tu dong go duoc", Test46_AutoOrderBeatsProfileOrder);
            RunTest(report, "47. Doi thu tu chan KHONG lam doi kich thuoc phoi", Test47_OrderDoesNotChangeBlank);
            RunTest(report, "48. Long hep sau bi bao va dao", Test48_NarrowChannelHitsPunch);
            RunTest(report, "49. Canh ngan hon canh nho nhat cua coi bi canh bao", Test49_ShortFlangeWarning);
            RunTest(report, "50. Tim kiem chum dat ket qua nhu vet can", Test50_BeamSearchMatchesBruteForce);
            RunTest(report, "51. Hinh cac buoc KHONG chong len nhau", Test51_StepCellsDoNotOverlap);
            RunTest(report, "52. Hinh he toa do may DONG DANG hinh bien dang", Test52_MachineShapeIsCongruent);
            RunTest(report, "53. Khau chan luon MO LEN TREN trong he toa do may", Test53_BendAlwaysOpensUpward);

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

        /// <summary>
        /// Bien dang L: canh 20 va 30, mot chan 90 do.
        /// Quy tac xuong Factor = 1, T = 1.2  =>  moi ben mat 1.2
        ///   canh 20 -> 18.8 ; canh 30 -> 28.8 ; W = 47.6 = 50 - 2 * 1.2
        /// </summary>
        private static void Test01_SingleBendCustomRule()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, LProfile());

            AssertNoErrors(r);
            AssertEqual(1, r.BendCount, "so duong chan");
            AssertClose(50.0, r.MoldLineTotalLength, "tong mold line");
            AssertClose(47.6, r.BlankWidth, "chieu rong phoi");
            AssertClose(2.4, r.TotalDeduction, "tong bu chan");
            AssertClose(1.2, r.Bends[0].OutsideSetback, "OSSB");
            AssertClose(0.0, r.Bends[0].BendAllowance, "BA");
            AssertClose(2.4, r.Bends[0].BendDeduction, "BD");
            AssertClose(18.8, r.Bends[0].FlatPosition, "vi tri duong chan");
            AssertClose(90.0, r.Bends[0].BendAngleDeg, "bend angle");
            AssertClose(90.0, r.Bends[0].OpeningAngleDeg, "opening angle");
        }

        /// <summary>
        /// GOLDEN TEST - cac con so nay duoc tinh tay va phai khong bao gio thay doi:
        ///   T = 1.2 ; R = 1.2 ; alpha = 90 ; K = 0.42
        ///   R_neutral = 1.2 + 0.42 * 1.2 = 1.704
        ///   BA   = (PI/2) * 1.704            = 2.676636940...
        ///   OSSB = (1.2 + 1.2) * tan(45 do)  = 2.4
        ///   BD   = 2 * 2.4 - BA              = 2.123363059...
        /// </summary>
        private static void Test02_SingleBendKFactorGolden()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, LProfile());

            AssertNoErrors(r);
            FoilBendInfo b = r.Bends[0];

            AssertClose(1.704, b.InsideRadius + b.KFactor * b.Thickness, "ban kinh truc trung hoa");
            AssertClose(2.676636940415, b.BendAllowance, "BA", 1e-9);
            AssertClose(2.4, b.OutsideSetback, "OSSB", 1e-9);
            AssertClose(2.123363059585, b.BendDeduction, "BD", 1e-9);
            AssertClose(47.876636940415, r.BlankWidth, "chieu rong phoi", 1e-9);

            // Vung chan: canh 20 bi cat 2.4 -> 17.6 ; vung chan rong BA.
            AssertClose(17.6, b.FlatZoneStart, "dau vung chan", 1e-9);
            AssertClose(17.6 + 2.676636940415, b.FlatZoneEnd, "cuoi vung chan", 1e-9);
            AssertClose(17.6 + 2.676636940415 / 2.0, b.FlatPosition, "tam vung chan", 1e-9);
        }

        private static void Test03_TwoBendsUProfile()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, UProfile());

            AssertNoErrors(r);
            AssertEqual(2, r.BendCount, "so duong chan");
            AssertClose(70.0, r.MoldLineTotalLength, "tong mold line");

            // 18.8 + 27.6 + 18.8
            AssertClose(65.2, r.BlankWidth, "chieu rong phoi");
            AssertClose(18.8, r.Bends[0].FlatPosition, "vi tri chan #1");
            AssertClose(46.4, r.Bends[1].FlatPosition, "vi tri chan #2");

            // Ca hai goc cua chu U deu re cung mot phia.
            AssertEqual(
                r.Bends[0].Direction.ToString(),
                r.Bends[1].Direction.ToString(),
                "hai chan cua chu U phai cung huong");
        }

        private static void Test04_ThreeBends()
        {
            // Bien dang bac thang: 10 len, 20 phai, 15 len, 25 phai.
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 10, 0),
                new FoilRawVertex(20, 10, 0),
                new FoilRawVertex(20, 25, 0),
                new FoilRawVertex(45, 25, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertNoErrors(r);
            AssertEqual(3, r.BendCount, "so duong chan");
            AssertClose(70.0, r.MoldLineTotalLength, "tong mold line");

            // 10-1.2 + (20-2.4) + (15-2.4) + (25-1.2) = 8.8 + 17.6 + 12.6 + 23.8
            AssertClose(62.8, r.BlankWidth, "chieu rong phoi");
            AssertClose(8.8, r.Bends[0].FlatPosition, "vi tri chan #1");
            AssertClose(26.4, r.Bends[1].FlatPosition, "vi tri chan #2");
            AssertClose(39.0, r.Bends[2].FlatPosition, "vi tri chan #3");
        }

        private static void Test05_DifferentThickness()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 2.0;
            FoilFlatPatternResult r = Compute(s, LProfile());

            AssertNoErrors(r);
            AssertClose(46.0, r.BlankWidth, "W voi T = 2.0");    // 50 - 2*2.0
            AssertClose(18.0, r.Bends[0].FlatPosition, "vi tri chan");
        }

        private static void Test06_DifferentRadius()
        {
            FoilSettings a = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            a.InsideRadius = 1.2;

            FoilSettings b = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            b.InsideRadius = 3.0;

            FoilFlatPatternResult ra = Compute(a, LProfile());
            FoilFlatPatternResult rb = Compute(b, LProfile());

            AssertNoErrors(ra);
            AssertNoErrors(rb);

            // R lon hon => OSSB lon hon va BD lon hon => phoi hep hon.
            AssertTrue(rb.Bends[0].OutsideSetback > ra.Bends[0].OutsideSetback, "OSSB phai tang theo R");
            AssertTrue(rb.Bends[0].BendDeduction > ra.Bends[0].BendDeduction, "BD phai tang theo R");
            AssertTrue(rb.BlankWidth < ra.BlankWidth, "phoi phai hep hon khi R lon hon");

            // Kiem tra truc tiep cong thuc: OSSB = (R + T) * tan(45 do) = R + T
            AssertClose(4.2, rb.Bends[0].OutsideSetback, "OSSB voi R = 3.0", 1e-9);
        }

        private static void Test07_DifferentKFactor()
        {
            FoilSettings a = BaseSettings(FoilBendMethod.BendAllowanceKFactor);
            a.KFactor = 0.33;

            FoilSettings b = BaseSettings(FoilBendMethod.BendAllowanceKFactor);
            b.KFactor = 0.50;

            FoilFlatPatternResult ra = Compute(a, LProfile());
            FoilFlatPatternResult rb = Compute(b, LProfile());

            AssertNoErrors(ra);
            AssertNoErrors(rb);

            // K lon hon => truc trung hoa xa mat trong hon => BA lon hon => BD nho hon => phoi rong hon.
            AssertTrue(rb.Bends[0].BendAllowance > ra.Bends[0].BendAllowance, "BA phai tang theo K");
            AssertTrue(rb.Bends[0].BendDeduction < ra.Bends[0].BendDeduction, "BD phai giam khi K tang");
            AssertTrue(rb.BlankWidth > ra.BlankWidth, "phoi phai rong hon khi K lon hon");

            AssertClose(
                (Math.PI / 2.0) * (1.2 + 0.33 * 1.2),
                ra.Bends[0].BendAllowance,
                "BA voi K = 0.33",
                1e-9);
        }

        private static void Test08_AcuteBend()
        {
            // Goc re 45 do: di len roi re sang phai 45 do.
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(20 * Math.Sin(45 * FoilMath.DegToRad),
                                  20 + 20 * Math.Cos(45 * FoilMath.DegToRad), 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertNoErrors(r);
            AssertEqual(1, r.BendCount, "so duong chan");
            AssertClose(45.0, r.Bends[0].BendAngleDeg, "bend angle alpha", 1e-6);
            AssertClose(135.0, r.Bends[0].OpeningAngleDeg, "opening angle beta", 1e-6);

            double alpha = 45.0 * FoilMath.DegToRad;
            AssertClose(alpha * 1.704, r.Bends[0].BendAllowance, "BA goc nhon", 1e-9);
            AssertClose(2.4 * Math.Tan(alpha / 2.0), r.Bends[0].OutsideSetback, "OSSB goc nhon", 1e-9);
        }

        private static void Test09_ObtuseBend()
        {
            // Goc re 135 do.
            double a = 135.0 * FoilMath.DegToRad;
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(20 * Math.Sin(a), 20 + 20 * Math.Cos(a), 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertNoErrors(r);
            AssertClose(135.0, r.Bends[0].BendAngleDeg, "bend angle alpha", 1e-6);
            AssertClose(45.0, r.Bends[0].OpeningAngleDeg, "opening angle beta", 1e-6);
            AssertClose(a * 1.704, r.Bends[0].BendAllowance, "BA goc tu", 1e-9);
            AssertClose(2.4 * Math.Tan(a / 2.0), r.Bends[0].OutsideSetback, "OSSB goc tu", 1e-9);

            // Goc tu => OSSB rat lon => BD lon => phoi hep di nhieu.
            AssertTrue(r.Bends[0].BendDeduction > 7.0, "BD goc 135 do phai lon hon 7");
        }

        /// <summary>
        /// Kiem tra DUNG yeu cau cua xuong:
        ///   canh co 1 duong chan -> L - 1 * T
        ///   canh co 2 duong chan -> L - 2 * T
        /// </summary>
        private static void Test10_ShopRuleTwoBendsOnOneFlange()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, UProfile());

            AssertNoErrors(r);

            List<FoilProfileElement> flanges = new List<FoilProfileElement>();
            foreach (FoilProfileElement e in r.Elements)
            {
                if (e.Kind == FoilElementKind.Flange)
                {
                    flanges.Add(e);
                }
            }

            AssertEqual(3, flanges.Count, "so canh");

            // Canh dau: 20 mm, 1 duong chan -> 20 - 1 * 1.2
            AssertClose(20.0 - 1 * 1.2, flanges[0].FlatLength, "canh 1 duong chan");

            // Canh giua: 30 mm, 2 duong chan -> 30 - 2 * 1.2
            AssertClose(30.0 - 2 * 1.2, flanges[1].FlatLength, "canh 2 duong chan");

            // Canh cuoi: 20 mm, 1 duong chan
            AssertClose(20.0 - 1 * 1.2, flanges[2].FlatLength, "canh 1 duong chan (cuoi)");
        }

        /// <summary>
        /// Duong chan xien phai duoc dung bang vector va cat dung bang bien phoi,
        /// KHONG duoc rut gon thanh "lay toa do X" hay "lay toa do Y".
        /// </summary>
        private static void Test11_DiagonalBendGeometry()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.BlankLength = 100.0;
            FoilFlatPatternResult r = Compute(s, LProfile());
            AssertNoErrors(r);

            double skew = 30.0 * FoilMath.DegToRad;
            r.Bends[0].SkewAngleRad = skew;
            AssertTrue(r.Bends[0].IsDiagonal, "bend phai duoc danh dau la xien");

            FoilFlatPatternGeometry g = FoilFlatPatternGeometry.Build(
                r, new FoilPoint2d(0, 0), 0.0);

            AssertEqual(1, g.BendLines.Count, "so duong chan sinh ra");
            FoilBendLineGeometry line = g.BendLines[0];

            // 1. Goc cua duong chan phai dung 30 do.
            FoilVector2d dir = (line.End - line.Start).Normalized();
            double actualAngle = Math.Abs(Math.Atan2(dir.Y, dir.X));
            AssertClose(skew, actualAngle, "goc nghieng duong chan", 1e-9);

            // 2. Hai dau phai nam TREN BIEN phoi (100 x W).
            double w = r.BlankWidth;   // 47.6
            AssertTrue(OnRectangleBoundary(line.Start, 100.0, w), "dau duong chan phai nam tren bien phoi");
            AssertTrue(OnRectangleBoundary(line.End, 100.0, w), "cuoi duong chan phai nam tren bien phoi");

            // 3. Duong chan phai di qua diem neo (L/2, v).
            double v = r.Bends[0].FlatPosition;   // 18.8
            FoilPoint2d anchor = new FoilPoint2d(50.0, v);
            FoilVector2d toAnchor = anchor - line.Start;
            AssertClose(0.0, toAnchor.Cross(dir), "duong chan phai di qua diem neo", 1e-9);

            // 4. Kiem tra CHIEU DAI GIAI TICH.
            //    Voi skew = 30 do tai v = 18.8 tren phoi 100 x 47.6:
            //      di len   : dy = 47.6 - 18.8 = 28.8  => dx = 28.8 / tan(30) = 49.88 <= 50  (thoat canh TREN)
            //      di xuong : dy = -18.8                => dx = -32.56        >= -50 (thoat canh DUOI)
            //    => doan cat trai het chieu rong, do dai = W / sin(30 do) = 95.2
            AssertClose(w / Math.Sin(skew), line.Length, "chieu dai doan chan xien (thoat canh tren/duoi)", 1e-9);
            AssertClose(0.0, Math.Min(line.Start.Y, line.End.Y), "dau duoi phai nam tren canh V = 0", 1e-9);
            AssertClose(w, Math.Max(line.Start.Y, line.End.Y), "dau tren phai nam tren canh V = W", 1e-9);

            // 5. Voi goc nghieng THOAI (5 do) duong chan thoat qua hai canh DAU phoi,
            //    khi do do dai = L / cos(5 do) va PHAI dai hon chieu dai phoi.
            double shallow = 5.0 * FoilMath.DegToRad;
            r.Bends[0].SkewAngleRad = shallow;
            FoilFlatPatternGeometry g2 = FoilFlatPatternGeometry.Build(r, new FoilPoint2d(0, 0), 0.0);
            FoilBendLineGeometry line2 = g2.BendLines[0];

            AssertClose(100.0 / Math.Cos(shallow), line2.Length,
                "chieu dai doan chan xien thoai (thoat hai canh dau)", 1e-9);
            AssertTrue(line2.Length > 100.0, "duong chan xien thoai phai dai hon chieu dai phoi");
            AssertClose(0.0, Math.Min(line2.Start.X, line2.End.X), "dau trai phai nam tren canh U = 0", 1e-9);
            AssertClose(100.0, Math.Max(line2.Start.X, line2.End.X), "dau phai phai nam tren canh U = L", 1e-9);
        }

        private static void Test12_CwVsCcwConsistency()
        {
            // Hinh chu nhat kin ve theo CCW va theo CW phai cho ket qua giong het nhau.
            FoilRawVertex[] ccw =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(40, 0, 0),
                new FoilRawVertex(40, 20, 0),
                new FoilRawVertex(0, 20, 0)
            };

            FoilRawVertex[] cw =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(40, 20, 0),
                new FoilRawVertex(40, 0, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult a = Compute(s, ccw, true);
            FoilFlatPatternResult b = Compute(s, cw, true);

            AssertNoErrors(a);
            AssertNoErrors(b);
            AssertEqual(a.BendCount, b.BendCount, "so duong chan CW vs CCW");
            AssertClose(a.BlankWidth, b.BlankWidth, "chieu rong CW vs CCW");

            for (int i = 0; i < a.BendCount; i++)
            {
                AssertClose(a.Bends[i].FlatPosition, b.Bends[i].FlatPosition,
                    "vi tri chan #" + (i + 1) + " CW vs CCW");
                AssertEqual(a.Bends[i].Direction.ToString(), b.Bends[i].Direction.ToString(),
                    "huong chan #" + (i + 1) + " CW vs CCW");
            }
        }

        private static void Test13_ReversedDrawOrder()
        {
            FoilRawVertex[] forward = UProfile();
            FoilRawVertex[] backward = new FoilRawVertex[forward.Length];
            for (int i = 0; i < forward.Length; i++)
            {
                backward[i] = forward[forward.Length - 1 - i];
            }

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult a = Compute(s, forward, false);
            FoilFlatPatternResult b = Compute(s, backward, false);

            AssertNoErrors(a);
            AssertNoErrors(b);
            AssertClose(a.BlankWidth, b.BlankWidth, "chieu rong khi dao chieu ve");
            AssertEqual(a.BendCount, b.BendCount, "so duong chan khi dao chieu ve");

            for (int i = 0; i < a.BendCount; i++)
            {
                AssertClose(a.Bends[i].FlatPosition, b.Bends[i].FlatPosition,
                    "vi tri chan #" + (i + 1) + " khi dao chieu ve");
                AssertEqual(a.Bends[i].Direction.ToString(), b.Bends[i].Direction.ToString(),
                    "huong chan #" + (i + 1) + " khi dao chieu ve");
            }
        }

        private static void Test14_InvalidPolyline()
        {
            List<string> notes = new List<string>();
            FoilProfile p = FoilProfileBuilder.Build(
                new[] { new FoilRawVertex(0, 0, 0) }, false, 1e-6, notes);

            AssertTrue(p == null, "polyline 1 dinh phai bi tu choi");
            AssertTrue(notes.Count > 0, "phai co thong bao ro rang");
        }

        private static void Test15_ZeroThickness()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 0.0;

            List<string> errors = FoilValidation.ValidateSettings(s);
            AssertTrue(errors.Count > 0, "chieu day 0 phai bi tu choi");
        }

        private static void Test16_ZeroLength()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.BlankLength = 0.0;

            List<string> errors = FoilValidation.ValidateSettings(s);
            AssertTrue(errors.Count > 0, "chieu dai phoi 0 phai bi tu choi");

            // Va engine cung phai chan o buoc tinh.
            FoilFlatPatternResult r = Compute(s, LProfile());
            AssertTrue(r.HasErrors, "engine phai bao loi khi chieu dai phoi = 0");
        }

        private static void Test17_InvalidKFactor()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            s.KFactor = 1.8;
            AssertTrue(FoilValidation.ValidateSettings(s).Count > 0, "K = 1.8 phai bi tu choi");

            s.KFactor = -0.1;
            AssertTrue(FoilValidation.ValidateSettings(s).Count > 0, "K am phai bi tu choi");

            s.KFactor = 0.42;
            AssertEqual(0, FoilValidation.ValidateSettings(s).Count, "K = 0.42 phai hop le");
        }

        /// <summary>
        /// Polyline co bulge: cung 90 do ban kinh 10 giua hai doan thang.
        /// Cung DA la vung chan => khong ap setback, chieu dai trien khai = BA cua chinh cung do.
        /// </summary>
        private static void Test18_ArcBend()
        {
            double bulge = Math.Tan(-Math.PI / 8.0);   // cung CW 90 do
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, bulge),
                new FoilRawVertex(10, 30, 0),
                new FoilRawVertex(40, 30, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertNoErrors(r);
            AssertEqual(1, r.BendCount, "cung phai tao dung 1 bend (khong sinh them bend gia tai tiep diem)");

            FoilBendInfo b = r.Bends[0];
            AssertTrue(b.Kind == FoilBendKind.Arc, "bend phai la loai Arc");
            AssertClose(90.0, b.BendAngleDeg, "goc quet cung", 1e-6);
            AssertClose(10.0, b.InsideRadius, "ban kinh trong lay tu cung ve", 1e-6);

            double expectedBa = (Math.PI / 2.0) * (10.0 + 0.42 * 1.2);
            AssertClose(expectedBa, b.BendAllowance, "BA cua cung", 1e-9);
            AssertClose(0.0, b.AppliedSetbackPrev, "cung khong ap setback truoc");
            AssertClose(0.0, b.AppliedSetbackNext, "cung khong ap setback sau");

            // W = 20 (canh thang) + BA + 30 (canh thang)
            AssertClose(50.0 + expectedBa, r.BlankWidth, "chieu rong phoi co cung", 1e-9);
            AssertClose(20.0, b.FlatZoneStart, "dau vung chan cung", 1e-9);
            AssertClose(20.0 + expectedBa, b.FlatZoneEnd, "cuoi vung chan cung", 1e-9);
        }

        private static void Test19_ClosedProfile()
        {
            FoilRawVertex[] rect =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(40, 0, 0),
                new FoilRawVertex(40, 20, 0),
                new FoilRawVertex(0, 20, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, rect, true);

            AssertNoErrors(r);

            // Vong kin duoc mo tai giua doan dai nhat => 5 canh, 4 bend.
            AssertEqual(4, r.BendCount, "so duong chan cua tiet dien kin");
            AssertClose(120.0, r.MoldLineTotalLength, "chu vi mold line");
            AssertClose(120.0 - 4 * 2.4, r.BlankWidth, "chieu rong phoi tiet dien kin");
        }

        private static void Test20_ThicknessOutlineDetection()
        {
            // Dai ton 100 x 1.2 ve kin: day la DUONG BAO CO BE DAY, phai bi tu choi.
            FoilRawVertex[] strip =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(100, 0, 0),
                new FoilRawVertex(100, 1.2, 0),
                new FoilRawVertex(0, 1.2, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, strip, true);

            AssertTrue(r.HasErrors, "duong bao co be day phai bi tu choi khi chua bat tuy chon");
            AssertTrue(JoinAll(r.Errors).IndexOf("BE DAY", StringComparison.OrdinalIgnoreCase) >= 0,
                "loi phai neu ro ly do la duong bao co be day");

            // Bat tuy chon: chan doan "duong bao co be day" phai chuyen tu LOI thanh CANH BAO.
            s.AllowThicknessOutlineInput = true;
            FoilFlatPatternResult r2 = Compute(s, strip, true);

            AssertTrue(JoinAll(r2.Warnings).IndexOf("BE DAY", StringComparison.OrdinalIgnoreCase) >= 0,
                "khi bat tuy chon thi chan doan phai ha xuong thanh canh bao");
            AssertTrue(JoinAll(r2.Errors).IndexOf("BE DAY", StringComparison.OrdinalIgnoreCase) < 0,
                "khi bat tuy chon thi khong con bao LOI ve be day nua");

            // Luu y ky thuat: hinh dang nay VAN khong trien khai duoc, vi hai canh dau dung bang
            // T nen sau khi tru setback hai dau se am. Engine phai bao LOI HINH HOC ro rang
            // thay vi im lang tao ra phoi sai.
            AssertTrue(r2.HasErrors, "dai ton be day van phai bao loi hinh hoc");
            AssertTrue(JoinAll(r2.Errors).IndexOf("am", StringComparison.OrdinalIgnoreCase) >= 0,
                "loi hinh hoc phai noi ro chieu dai trien khai bi am");
        }

        /// <summary>
        /// Vi tri duong chan phai tich luy tu chieu dai that, KHONG duoc chia deu.
        /// </summary>
        private static void Test21_BendPositionsNotUniform()
        {
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 5, 0),
                new FoilRawVertex(60, 5, 0),
                new FoilRawVertex(60, 12, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertNoErrors(r);
            AssertEqual(2, r.BendCount, "so duong chan");

            // 5-1.2 = 3.8 ; roi 60-2.4 = 57.6 ; roi 7-1.2 = 5.8  => W = 67.2
            AssertClose(67.2, r.BlankWidth, "chieu rong");
            AssertClose(3.8, r.Bends[0].FlatPosition, "vi tri chan #1");
            AssertClose(61.4, r.Bends[1].FlatPosition, "vi tri chan #2");

            // Neu chia deu thi hai duong chan se o 22.4 va 44.8 - phai KHAC.
            AssertTrue(Math.Abs(r.Bends[0].FlatPosition - r.BlankWidth / 3.0) > 1.0,
                "vi tri duong chan khong duoc chia deu");
        }

        private static void Test22_BaEqualsBdMethod()
        {
            FoilSettings a = BaseSettings(FoilBendMethod.BendAllowanceKFactor);
            FoilSettings b = BaseSettings(FoilBendMethod.BendDeductionKFactor);

            FoilFlatPatternResult ra = Compute(a, UProfile());
            FoilFlatPatternResult rb = Compute(b, UProfile());

            AssertNoErrors(ra);
            AssertNoErrors(rb);
            AssertClose(ra.BlankWidth, rb.BlankWidth, "BA-method va BD-method phai trung ket qua", 1e-12);

            for (int i = 0; i < ra.BendCount; i++)
            {
                AssertClose(ra.Bends[i].FlatPosition, rb.Bends[i].FlatPosition,
                    "vi tri chan #" + (i + 1), 1e-12);

                // Dong nhat thuc BD = 2 * OSSB - BA
                AssertClose(
                    2.0 * ra.Bends[i].OutsideSetback - ra.Bends[i].BendAllowance,
                    ra.Bends[i].BendDeduction,
                    "dong nhat thuc BD = 2*OSSB - BA",
                    1e-12);
            }
        }

        private static void Test23_FlangeTooShort()
        {
            // Canh giua chi 1 mm nhung setback hai dau la 2 * 2.4 = 4.8 mm.
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(1, 20, 0),
                new FoilRawVertex(1, 40, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertTrue(r.HasErrors, "canh qua ngan so voi setback phai bao loi");
            AssertTrue(r.Errors[0].IndexOf("am", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       r.Errors[0].IndexOf("ngan", StringComparison.OrdinalIgnoreCase) >= 0,
                       "thong bao loi phai noi ro nguyen nhan");
        }

        private static void Test24_LineClipper()
        {
            List<FoilPoint2d> rect = new List<FoilPoint2d>
            {
                new FoilPoint2d(0, 0),
                new FoilPoint2d(100, 0),
                new FoilPoint2d(100, 50),
                new FoilPoint2d(0, 50)
            };

            FoilPoint2d a, b;

            // Duong ngang qua giua.
            AssertTrue(FoilLineClipper.ClipInfiniteLine(
                new FoilPoint2d(50, 25), new FoilVector2d(1, 0), rect, out a, out b),
                "duong ngang phai cat duoc");
            AssertClose(100.0, a.DistanceTo(b), "chieu dai doan cat ngang");

            // Duong cheo 45 do.
            AssertTrue(FoilLineClipper.ClipInfiniteLine(
                new FoilPoint2d(50, 25), new FoilVector2d(1, 1), rect, out a, out b),
                "duong cheo phai cat duoc");
            AssertClose(50.0 * Math.Sqrt(2.0), a.DistanceTo(b), "chieu dai doan cat cheo", 1e-9);

            // Duong nam ngoai hoan toan.
            AssertTrue(!FoilLineClipper.ClipInfiniteLine(
                new FoilPoint2d(50, 200), new FoilVector2d(1, 0), rect, out a, out b),
                "duong ngoai phoi khong duoc cat");

            // Da giac CW cung phai cho ket qua giong CCW.
            List<FoilPoint2d> rectCw = new List<FoilPoint2d>
            {
                new FoilPoint2d(0, 0),
                new FoilPoint2d(0, 50),
                new FoilPoint2d(100, 50),
                new FoilPoint2d(100, 0)
            };

            AssertTrue(FoilLineClipper.ClipInfiniteLine(
                new FoilPoint2d(50, 25), new FoilVector2d(1, 0), rectCw, out a, out b),
                "da giac CW phai cat duoc");
            AssertClose(100.0, a.DistanceTo(b), "chieu dai doan cat tren da giac CW");
        }

        /// <summary>
        /// Lay K = 0.42 -> tinh xuoi ra BD -> dua BD vao hieu chuan -> phai giai nguoc lai
        /// dung K = 0.42.
        /// </summary>
        private static void Test25_CalibrationRoundTrip()
        {
            const double t = 1.2;
            const double r = 1.2;
            const double k = 0.42;
            double alpha = Math.PI / 2.0;

            double ba = alpha * (r + k * t);
            double ossb = (r + t) * Math.Tan(alpha / 2.0);
            double bd = 2.0 * ossb - ba;

            double flange1 = 20.0;
            double flange2 = 30.0;
            double flat = flange1 + flange2 - bd;

            FoilCalibrationResult cal = FoilCalibration.Solve(new FoilCalibrationInput
            {
                Thickness = t,
                InsideRadius = r,
                BendAngleDeg = 90.0,
                FlatLength = flat,
                Flange1 = flange1,
                Flange2 = flange2
            });

            AssertTrue(cal.Success, "hieu chuan phai thanh cong");
            AssertClose(bd, cal.BendDeduction, "BD giai nguoc", 1e-9);
            AssertClose(ba, cal.BendAllowance, "BA giai nguoc", 1e-9);
            AssertClose(k, cal.KFactor, "K-factor giai nguoc", 1e-9);
        }

        private static void Test26_BendTableLookup()
        {
            FoilBendTable table = new FoilBendTable();
            table.Add(new FoilBendTableEntry { Thickness = 1.2, Radius = 1.2, AngleDeg = 60, BendDeduction = 1.0 });
            table.Add(new FoilBendTableEntry { Thickness = 1.2, Radius = 1.2, AngleDeg = 90, BendDeduction = 2.0 });
            table.Add(new FoilBendTableEntry { Thickness = 2.0, Radius = 2.0, AngleDeg = 90, BendDeduction = 3.5 });

            AssertClose(2.0, table.Lookup(1.2, 1.2, 90).BendDeduction, "tra cuu khop chinh xac");
            AssertClose(1.5, table.Lookup(1.2, 1.2, 75).BendDeduction, "noi suy tuyen tinh giua 60 va 90", 1e-9);
            AssertClose(2.0, table.Lookup(1.2, 1.2, 120).BendDeduction, "ngoai khoang phai kep ve hang bien");
            AssertTrue(table.Lookup(5.0, 5.0, 90) == null, "khong co du lieu thi phai tra ve null");

            // Chay qua ca engine.
            FoilSettings s = BaseSettings(FoilBendMethod.BendTable);
            FoilFlatPatternResult r = Compute(s, LProfile(), false, table);

            AssertNoErrors(r);
            AssertClose(2.0, r.Bends[0].BendDeduction, "BD lay tu bang chan", 1e-9);
            AssertClose(1.0, r.Bends[0].OutsideSetback, "OSSB = (BD + BA) / 2", 1e-9);
            AssertClose(48.0, r.BlankWidth, "chieu rong theo bang chan", 1e-9);

            // Thieu bang chan phai bao loi ro rang chu khong crash.
            FoilFlatPatternResult r2 = Compute(s, LProfile(), false, null);
            AssertTrue(r2.HasErrors, "thieu bang chan phai bao loi");
        }

        private static void Test27_ZProfileDirections()
        {
            FoilRawVertex[] z =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(30, 20, 0),
                new FoilRawVertex(30, 40, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, z, false);

            AssertNoErrors(r);
            AssertEqual(2, r.BendCount, "bien dang Z co 2 duong chan");
            AssertTrue(r.Bends[0].Direction != r.Bends[1].Direction,
                "hai duong chan cua bien dang Z phai NGUOC huong nhau");

            // Dao tuy chon thi ca hai phai lat.
            FoilSettings inv = BaseSettings(FoilBendMethod.CustomShopRule);
            inv.InvertBendDirection = true;
            FoilFlatPatternResult ri = Compute(inv, z, false);

            AssertTrue(ri.Bends[0].Direction != r.Bends[0].Direction, "tuy chon dao huong phai co tac dung");
            AssertTrue(ri.Bends[1].Direction != r.Bends[1].Direction, "tuy chon dao huong phai co tac dung");
        }

        private static void Test28_ZeroLengthSegmentRemoved()
        {
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(0, 20, 0),      // dinh trung
                new FoilRawVertex(30, 20, 0)
            };

            List<string> notes = new List<string>();
            FoilProfile p = FoilProfileBuilder.Build(v, false, 1e-6, notes);

            AssertTrue(p != null, "profile van phai dung duoc");
            AssertEqual(2, p.SegmentCount, "doan dai 0 phai bi loai");
            AssertTrue(notes.Count > 0, "phai ghi chu lai viec loai doan dai 0");

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, v, false);
            AssertNoErrors(r);
            AssertEqual(1, r.BendCount, "dinh trung khong duoc tao bend gia");
        }

        private static void Test29_CollinearNotBend()
        {
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(10, 0, 0),
                new FoilRawVertex(25, 0, 0),      // thang hang
                new FoilRawVertex(40, 0, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, v, false);

            AssertNoErrors(r);
            AssertEqual(0, r.BendCount, "diem thang hang khong phai bend");
            AssertClose(40.0, r.BlankWidth, "phoi phang giu nguyen chieu dai");
        }

        private static void Test30_RotatedBlank()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.BlankLength = 100.0;
            FoilFlatPatternResult r = Compute(s, LProfile());
            AssertNoErrors(r);

            FoilPoint2d origin = new FoilPoint2d(500, 300);
            double rot = 90.0 * FoilMath.DegToRad;

            FoilFlatPatternGeometry g = FoilFlatPatternGeometry.Build(r, origin, rot);

            AssertEqual(4, g.Outline.Count, "phoi phai co 4 dinh");

            // Goc 0 van o diem chen.
            AssertClose(500.0, g.Outline[0].X, "goc phoi X", 1e-9);
            AssertClose(300.0, g.Outline[0].Y, "goc phoi Y", 1e-9);

            // Xoay 90 do: truc U (chieu dai) huong theo +Y.
            AssertClose(500.0, g.Outline[1].X, "goc thu hai X sau khi xoay", 1e-9);
            AssertClose(400.0, g.Outline[1].Y, "goc thu hai Y sau khi xoay", 1e-9);

            // Kich thuoc phai giu nguyen.
            AssertClose(100.0, g.Outline[0].DistanceTo(g.Outline[1]), "chieu dai phoi sau khi xoay", 1e-9);
            AssertClose(r.BlankWidth, g.Outline[1].DistanceTo(g.Outline[2]), "chieu rong phoi sau khi xoay", 1e-9);

            // Duong chan chay DOC theo truc U (chieu dai phoi). Sau khi xoay 90 do, truc U
            // huong theo +Y, nen duong chan phai song song truc Y.
            AssertEqual(1, g.BendLines.Count, "so duong chan");
            FoilVector2d dir = (g.BendLines[0].End - g.BendLines[0].Start).Normalized();
            AssertClose(0.0, Math.Abs(dir.X), "duong chan phai song song truc Y sau khi xoay 90 do", 1e-9);
            AssertClose(100.0, g.BendLines[0].Length, "duong chan phai trai het chieu dai phoi", 1e-9);

            // Va khi KHONG xoay, duong chan phai song song truc X (giong ban ve mau).
            FoilFlatPatternGeometry g0 = FoilFlatPatternGeometry.Build(r, origin, 0.0);
            FoilVector2d dir0 = (g0.BendLines[0].End - g0.BendLines[0].Start).Normalized();
            AssertClose(0.0, Math.Abs(dir0.Y), "duong chan phai song song truc X khi khong xoay", 1e-9);
        }

        /// <summary>
        /// Bay: khi goc chan alpha tien gan 180 do thi |cross(d1, d2)| = sin(alpha) lai NHO DAN.
        /// Neu lay nguong "co phai bend khong" (MinBendAngle) de xac dinh luon DAU cua goc re
        /// thi hai chan nguoc chieu nhau se bi bao cung mot huong.
        /// Test dung dung vung nguy hiem do: alpha = 178.5 do voi MinBendAngle = 2 do
        /// (sin(178.5) = 0.0262 < sin(2) = 0.0349).
        /// </summary>
        private static void Test31_NearFlatBendDirection()
        {
            const double alphaDeg = 178.5;
            double alpha = alphaDeg * FoilMath.DegToRad;

            // Re TRAI 178.5 do.
            FoilVector2d leftDir = new FoilVector2d(1, 0).Rotate(alpha);
            FoilRawVertex[] left =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(20, 0, 0),
                new FoilRawVertex(20 + 20 * leftDir.X, 20 * leftDir.Y, 0)
            };

            // Re PHAI 178.5 do (anh guong qua truc X).
            FoilVector2d rightDir = new FoilVector2d(1, 0).Rotate(-alpha);
            FoilRawVertex[] right =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(20, 0, 0),
                new FoilRawVertex(20 + 20 * rightDir.X, 20 * rightDir.Y, 0)
            };

            // Dung quy tac xuong: setback khong phu thuoc goc (= Factor * T) nen hinh hoc van hop le.
            // Neu dung K-factor thi OSSB = (R+T)*tan(89.25 do) = 183 mm > canh 20 mm va engine se
            // (dung dan) tu choi - khi do khong con kiem tra duoc rieng phan huong chan nua.
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.MinBendAngleDeg = 2.0;
            s.MaxBendAngleDeg = 179.0;

            FoilFlatPatternResult rl = Compute(s, left, false);
            FoilFlatPatternResult rr = Compute(s, right, false);

            AssertNoErrors(rl);
            AssertNoErrors(rr);
            AssertEqual(1, rl.BendCount, "so duong chan (re trai)");
            AssertEqual(1, rr.BendCount, "so duong chan (re phai)");
            AssertClose(alphaDeg, rl.Bends[0].BendAngleDeg, "goc chan re trai", 1e-6);
            AssertClose(alphaDeg, rr.Bends[0].BendAngleDeg, "goc chan re phai", 1e-6);

            AssertClose(1.0, rl.Bends[0].TurnSign, "re trai phai cho TurnSign = +1");
            AssertClose(-1.0, rr.Bends[0].TurnSign, "re phai phai cho TurnSign = -1");
            AssertTrue(rl.Bends[0].Direction != rr.Bends[0].Direction,
                "hai chan nguoc chieu nhau phai cho huong chan KHAC nhau");
        }

        /// <summary>
        /// Tach doi cung dung khi mo vong kin. Bulge cua nua cung = tan(sweep / 8).
        /// </summary>
        private static void Test32_ArcSplit()
        {
            // Cung CCW 90 do, ban kinh 10, tu (10,0) den (0,10) quanh goc toa do.
            double bulge = Math.Tan(Math.PI / 8.0);
            FoilProfileSegment arc = new FoilProfileSegment(
                new FoilPoint2d(10, 0), new FoilPoint2d(0, 10), bulge);

            AssertTrue(arc.IsArc, "phai nhan dien la cung");
            AssertClose(10.0, arc.Radius, "ban kinh cung", 1e-9);
            AssertClose(Math.PI / 2.0, arc.SweepAngle, "goc quet", 1e-9);
            AssertClose(0.0, arc.Center.DistanceTo(new FoilPoint2d(0, 0)), "tam cung", 1e-9);

            // Tiep tuyen tai (10,0) cua chuyen dong CCW quanh goc toa do phai la (0,1).
            AssertClose(0.0, arc.StartDirection.X, "tiep tuyen dau - thanh phan X", 1e-9);
            AssertClose(1.0, arc.StartDirection.Y, "tiep tuyen dau - thanh phan Y", 1e-9);
            AssertClose(-1.0, arc.EndDirection.X, "tiep tuyen cuoi - thanh phan X", 1e-9);
            AssertClose(0.0, arc.EndDirection.Y, "tiep tuyen cuoi - thanh phan Y", 1e-9);

            FoilProfileSegment first;
            FoilProfileSegment second;
            arc.SplitAtMiddle(out first, out second);

            AssertClose(Math.PI / 4.0, first.SweepAngle, "nua dau quet 45 do", 1e-9);
            AssertClose(Math.PI / 4.0, second.SweepAngle, "nua sau quet 45 do", 1e-9);
            AssertClose(10.0, first.Radius, "nua dau giu ban kinh", 1e-9);
            AssertClose(10.0, second.Radius, "nua sau giu ban kinh", 1e-9);
            AssertClose(arc.Length, first.Length + second.Length, "tong chieu dai hai nua", 1e-9);
            AssertClose(0.0, first.End.DistanceTo(second.Start), "hai nua phai noi nhau", 1e-9);
            AssertClose(0.0, first.Start.DistanceTo(arc.Start), "diem dau giu nguyen", 1e-9);
            AssertClose(0.0, second.End.DistanceTo(arc.End), "diem cuoi giu nguyen", 1e-9);
        }

        /// <summary>
        /// Tiet dien kin co goc bo tron: khi mo vong, doan dai nhat co the la CUNG.
        /// Kiem tra duong di nay khong lam sai tong chieu dai trien khai.
        /// </summary>
        private static void Test33_ClosedProfileWithArcs()
        {
            // Hinh chu nhat 40 x 20 nhung 4 goc la cung 90 do ban kinh 5 (kieu "stadium" bo tron).
            double b = Math.Tan(Math.PI / 8.0);   // cung CCW 90 do
            FoilRawVertex[] v =
            {
                new FoilRawVertex(5, 0, 0),
                new FoilRawVertex(35, 0, b),
                new FoilRawVertex(40, 5, 0),
                new FoilRawVertex(40, 15, b),
                new FoilRawVertex(35, 20, 0),
                new FoilRawVertex(5, 20, b),
                new FoilRawVertex(0, 15, 0),
                new FoilRawVertex(0, 5, b)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, v, true);

            AssertNoErrors(r);

            // 4 goc bo tron = 4 bend kieu Arc, khong co dinh goc nhon nao.
            AssertEqual(4, r.BendCount, "so duong chan");
            foreach (FoilBendInfo bend in r.Bends)
            {
                AssertTrue(bend.Kind == FoilBendKind.Arc, "moi bend phai la loai Arc");
                AssertClose(90.0, bend.BendAngleDeg, "goc quet moi cung", 1e-6);
                AssertClose(5.0, bend.InsideRadius, "ban kinh trong moi cung", 1e-6);
                AssertClose(0.0, bend.AppliedSetbackPrev, "cung khong ap setback truoc");
                AssertClose(0.0, bend.AppliedSetbackNext, "cung khong ap setback sau");
            }

            // W = tong canh thang (30 + 10 + 30 + 10 = 80) + 4 * BA
            double ba = (Math.PI / 2.0) * (5.0 + 0.42 * 1.2);
            AssertClose(80.0, r.MoldLineTotalLength, "tong canh thang", 1e-9);
            AssertClose(80.0 + 4.0 * ba, r.BlankWidth, "chieu rong phoi", 1e-9);

            // Vi tri duong chan phai tang dan va nam trong pham vi phoi.
            double previous = -1.0;
            foreach (FoilBendInfo bend in r.Bends)
            {
                AssertTrue(bend.FlatPosition > previous, "vi tri duong chan phai tang dan");
                AssertTrue(bend.FlatPosition >= 0.0 && bend.FlatPosition <= r.BlankWidth,
                    "duong chan phai nam trong pham vi phoi");
                previous = bend.FlatPosition;
            }
        }

        /// <summary>
        /// Buoc 0 (chua chan gi) phai la mot doan THANG dai dung bang CHIEU RONG PHOI.
        /// Neu ai do dung tong mold line de ve buoc 0 thi test nay se bat duoc ngay.
        /// </summary>
        private static void Test34_SequenceStepZeroIsFlat()
        {
            foreach (FoilBendMethod method in new[]
                { FoilBendMethod.CustomShopRule, FoilBendMethod.BendDeductionKFactor })
            {
                FoilSettings s = BaseSettings(method);
                FoilFlatPatternResult r = Compute(s, HatProfile());
                AssertNoErrors(r);

                List<FoilBendStep> steps = FoilBendSequenceBuilder.Build(
                    r, FoilBendSequenceOrder.ProfileOrder);

                AssertEqual(r.BendCount + 1, steps.Count, "so buoc = so duong chan + 1 (" + method + ")");

                FoilBendStep flat = steps[0];
                AssertEqual(0, flat.StepNumber, "buoc dau tien phai la B0");
                AssertTrue(flat.FormedBend == null, "B0 khong chan duong nao");

                AssertClose(r.BlankWidth, flat.OutlineLength,
                    "chieu dai B0 phai bang chieu rong phoi (" + method + ")", 1e-9);
                AssertClose(0.0, flat.Height, "B0 phai hoan toan phang (" + method + ")", 1e-9);
                AssertClose(r.BlankWidth, flat.Width, "be rong bao cua B0 (" + method + ")", 1e-9);
            }
        }

        /// <summary>
        /// Buoc cuoi (chan het) phai co cac canh dung bang chieu dai MOLD LINE cua bien dang goc,
        /// va cac goc re dung bang goc chan goc.
        /// </summary>
        private static void Test35_SequenceLastStepMatchesProfile()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilRawVertex[] v = HatProfile();
            FoilFlatPatternResult r = Compute(s, v);
            AssertNoErrors(r);

            List<FoilBendStep> steps = FoilBendSequenceBuilder.Build(
                r, FoilBendSequenceOrder.ProfileOrder);
            FoilBendStep last = steps[steps.Count - 1];

            AssertEqual(r.BendCount, last.StepNumber, "buoc cuoi phai la buoc thu N");

            // So canh cua buoc cuoi phai bang so canh cua bien dang goc.
            AssertEqual(v.Length, last.Points.Count, "so dinh cua buoc cuoi");

            // Tung canh phai dung bang chieu dai mold line tuong ung.
            for (int i = 0; i < last.Points.Count - 1; i++)
            {
                double expected = v[i].Point.DistanceTo(v[i + 1].Point);
                double actual = last.Points[i].DistanceTo(last.Points[i + 1]);
                AssertClose(expected, actual,
                    "chieu dai canh #" + (i + 1) + " o buoc cuoi", 1e-9);
            }

            // Tong chieu dai buoc cuoi = tong mold line.
            AssertClose(r.MoldLineTotalLength, last.OutlineLength,
                "tong chieu dai buoc cuoi phai bang tong mold line", 1e-9);

            // Goc re tai tung dinh phai trung goc chan.
            for (int i = 1; i < last.Points.Count - 1; i++)
            {
                FoilVector2d incoming = last.Points[i] - last.Points[i - 1];
                FoilVector2d outgoing = last.Points[i + 1] - last.Points[i];
                double alpha = FoilMath.AngleBetween(incoming, outgoing) * FoilMath.RadToDeg;
                AssertClose(r.Bends[i - 1].BendAngleDeg, alpha,
                    "goc re tai dinh #" + i + " o buoc cuoi", 1e-6);
            }
        }

        private static void Test36_SequenceOrders()
        {
            // Combo "Thu tu chan" tren form nhap anh xa THANG tu SelectedIndex sang enum nay.
            // Neu ai do doi thu tu khai bao enum thi combo se chon nham, nen khoa lai o day.
            AssertEqual(0, (int)FoilBendSequenceOrder.ProfileOrder, "gia tri enum ProfileOrder");
            AssertEqual(1, (int)FoilBendSequenceOrder.Reverse, "gia tri enum Reverse");
            AssertEqual(2, (int)FoilBendSequenceOrder.OutsideIn, "gia tri enum OutsideIn");
            AssertEqual(3, (int)FoilBendSequenceOrder.AutoFeasible, "gia tri enum AutoFeasible");

            AssertEqual("1,2,3,4,5",
                Join(FoilBendSequenceBuilder.BuildOrder(5, FoilBendSequenceOrder.ProfileOrder)),
                "thu tu ProfileOrder");

            AssertEqual("5,4,3,2,1",
                Join(FoilBendSequenceBuilder.BuildOrder(5, FoilBendSequenceOrder.Reverse)),
                "thu tu Reverse");

            // Xen ke hai dau vao giua, khong duoc lap hay bo sot dinh giua.
            AssertEqual("1,5,2,4,3",
                Join(FoilBendSequenceBuilder.BuildOrder(5, FoilBendSequenceOrder.OutsideIn)),
                "thu tu OutsideIn (le)");

            AssertEqual("1,4,2,3",
                Join(FoilBendSequenceBuilder.BuildOrder(4, FoilBendSequenceOrder.OutsideIn)),
                "thu tu OutsideIn (chan)");

            AssertEqual("1", Join(FoilBendSequenceBuilder.BuildOrder(1, FoilBendSequenceOrder.OutsideIn)),
                "thu tu OutsideIn voi 1 duong chan");

            // Du thu tu nao, buoc cuoi cung cung phai ra cung mot hinh dang.
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, HatProfile());

            FoilBendStep a = Last(FoilBendSequenceBuilder.Build(r, FoilBendSequenceOrder.ProfileOrder));
            FoilBendStep b = Last(FoilBendSequenceBuilder.Build(r, FoilBendSequenceOrder.Reverse));
            FoilBendStep c = Last(FoilBendSequenceBuilder.Build(r, FoilBendSequenceOrder.OutsideIn));

            AssertClose(a.OutlineLength, b.OutlineLength, "buoc cuoi phai giong nhau (Reverse)", 1e-9);
            AssertClose(a.OutlineLength, c.OutlineLength, "buoc cuoi phai giong nhau (OutsideIn)", 1e-9);
            AssertEqual(a.Points.Count, b.Points.Count, "so dinh buoc cuoi (Reverse)");
            AssertEqual(a.Points.Count, c.Points.Count, "so dinh buoc cuoi (OutsideIn)");

            for (int i = 0; i < a.Points.Count; i++)
            {
                AssertClose(0.0, a.Points[i].DistanceTo(b.Points[i]),
                    "dinh #" + i + " buoc cuoi (Reverse)", 1e-9);
                AssertClose(0.0, a.Points[i].DistanceTo(c.Points[i]),
                    "dinh #" + i + " buoc cuoi (OutsideIn)", 1e-9);
            }
        }

        /// <summary>
        /// Kiem tra mot buoc TRUNG GIAN cu the tren bien dang L, tinh tay:
        ///   canh 20 -> FlatLength 18.8 ; canh 30 -> FlatLength 28.8 ; BA = 0 (quy tac xuong)
        ///   B0: mot doan thang 47.6
        ///   B1: canh (18.8 + 1.2) = 20 roi re 90 do roi canh (28.8 + 1.2) = 30
        /// </summary>
        private static void Test37_SequenceIntermediateShape()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            FoilFlatPatternResult r = Compute(s, LProfile());
            AssertNoErrors(r);

            List<FoilBendStep> steps = FoilBendSequenceBuilder.Build(
                r, FoilBendSequenceOrder.ProfileOrder);

            AssertEqual(2, steps.Count, "bien dang L co 2 buoc (B0 va B1)");

            // B0: doan thang dai 47.6
            AssertEqual(2, steps[0].Points.Count, "B0 chi co 2 dinh");
            AssertClose(47.6, steps[0].OutlineLength, "chieu dai B0", 1e-9);

            // B1: hai canh 20 va 30 vuong goc nhau.
            FoilBendStep b1 = steps[1];
            AssertEqual(3, b1.Points.Count, "B1 co 3 dinh");
            AssertClose(20.0, b1.Points[0].DistanceTo(b1.Points[1]), "canh 1 cua B1", 1e-9);
            AssertClose(30.0, b1.Points[1].DistanceTo(b1.Points[2]), "canh 2 cua B1", 1e-9);
            AssertClose(50.0, b1.OutlineLength, "tong chieu dai B1 = tong mold line", 1e-9);

            // Diem danh dau phai nam dung tai dinh vua chan.
            AssertClose(0.0, b1.MarkerPoint.DistanceTo(b1.Points[1]), "vi tri danh dau duong chan", 1e-9);
            AssertTrue(b1.FormedBend != null && b1.FormedBend.Index == 1, "B1 phai chan duong so 1");
        }


        /// <summary>
        /// DUNG VI DU THAT CUA NGUOI DUNG.
        /// Nguoi dung chi chon DUONG BAO NGOAI. Chi cac goc LOM (hem gap vao trong, cho xuong
        /// danh dau bang vong tron) moi duoc cong be day; cac goc LOI giu nguyen nhu cu.
        ///
        /// Bien dang thu: mat tren co mot HEM gap xuong.
        ///     mat tren 20 - vach hem 15 - day hem 34 - vach hem 15 - mat tren 20
        /// Day hem  ve 34 co 2 goc lom  ->  34 + 2 * 1.2 = 36.4
        /// Vach hem ve 15 co 1 goc lom  ->  15 + 1 * 1.2 = 16.2
        /// Mat tren ve 20, hai goc deu LOI  ->  giu nguyen 20
        /// </summary>
        private static void Test38_ThicknessOnConcaveOnly()
        {
            FoilRawVertex[] v =
            {
                new FoilRawVertex(-20, 15, 0),   // mat tren trai
                new FoilRawVertex(0, 15, 0),     // goc LOI  (vao hem)
                new FoilRawVertex(0, 0, 0),      // goc LOM  - vong tron
                new FoilRawVertex(34, 0, 0),     // goc LOM  - vong tron
                new FoilRawVertex(34, 15, 0),    // goc LOI  (ra khoi hem)
                new FoilRawVertex(54, 15, 0)     // mat tren phai
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 1.2;
            s.CustomFactor = 1.0;

            // Xac dinh huong chan cua 2 goc day hem, roi chon dung huong do de bu be day.
            FoilSettings probe = s.Clone();
            probe.ThicknessCompensation = FoilThicknessCompensationMode.None;
            FoilFlatPatternResult p = Compute(probe, v, false);

            AssertNoErrors(p);
            AssertEqual(4, p.BendCount, "bien dang hem phai co 4 duong chan");

            double concaveTurn = p.Bends[1].TurnSign;
            AssertTrue(p.Bends[2].TurnSign == concaveTurn, "hai goc day hem phai cung phia re");
            AssertTrue(p.Bends[0].TurnSign != concaveTurn, "goc loi phai nguoc phia voi goc lom");
            AssertTrue(p.Bends[3].TurnSign != concaveTurn, "goc loi con lai cung phai nguoc phia");

            s.ThicknessCompensation = concaveTurn >= 0.0
                ? FoilThicknessCompensationMode.TurnLeft
                : FoilThicknessCompensationMode.TurnRight;

            FoilFlatPatternResult r = Compute(s, v, false);
            AssertNoErrors(r);

            // CHI 2 goc day hem duoc cong be day.
            AssertTrue(!r.Bends[0].IsThicknessCompensated, "goc loi #1 KHONG duoc cong be day");
            AssertTrue(r.Bends[1].IsThicknessCompensated, "goc lom #2 phai duoc cong be day");
            AssertTrue(r.Bends[2].IsThicknessCompensated, "goc lom #3 phai duoc cong be day");
            AssertTrue(!r.Bends[3].IsThicknessCompensated, "goc loi #4 KHONG duoc cong be day");

            AssertClose(1.2, r.Bends[1].MoldLineOffset, "offset tai goc lom = T", 1e-9);
            AssertClose(0.0, r.Bends[0].MoldLineOffset, "offset tai goc loi = 0", 1e-9);

            List<FoilProfileElement> flanges = Flanges(r);
            AssertEqual(5, flanges.Count, "so canh");

            // Day la yeu cau cot loi cua nguoi dung:
            AssertClose(16.2, flanges[1].EffectiveMoldLineLength,
                "vach hem ve 15 co 1 vong tron -> 16.2", 1e-9);
            AssertClose(36.4, flanges[2].EffectiveMoldLineLength,
                "day hem ve 34 co 2 vong tron -> 36.4", 1e-9);
            AssertClose(16.2, flanges[3].EffectiveMoldLineLength,
                "vach hem ve 15 ben kia -> 16.2", 1e-9);

            // Mat tren hai dau chi co goc LOI nen KHONG duoc doi.
            AssertClose(20.0, flanges[0].EffectiveMoldLineLength, "mat tren trai giu nguyen 20", 1e-9);
            AssertClose(20.0, flanges[4].EffectiveMoldLineLength, "mat tren phai giu nguyen 20", 1e-9);

            // Tong bu be day dung bang 4 lan T (2 goc lom, moi goc an sang 2 canh ke).
            FoilFlatPatternResult noComp = Compute(probe, v, false);
            AssertClose(4 * 1.2, r.MoldLineTotalLength - noComp.MoldLineTotalLength,
                "tong bu be day = 4 * T", 1e-9);
        }

        /// <summary>
        /// Voi goc chan khac 90 do, luong bu KHONG con bang T ma la T * tan(alpha/2).
        /// Test nay bat truong hop ai do hard-code "cong dung 1 lan T".
        /// </summary>
        private static void Test39_ThicknessNonRightAngle()
        {
            double alpha = 60.0 * FoilMath.DegToRad;
            FoilRawVertex[] v =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(20 * Math.Sin(alpha), 20 + 20 * Math.Cos(alpha), 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 1.2;
            s.ThicknessCompensation = FoilThicknessCompensationMode.AllBends;

            FoilFlatPatternResult r = Compute(s, v, false);
            AssertNoErrors(r);
            AssertClose(60.0, r.Bends[0].BendAngleDeg, "goc chan", 1e-6);

            double expected = 1.2 * Math.Tan(alpha / 2.0);
            AssertClose(expected, r.Bends[0].MoldLineOffset, "offset = T * tan(alpha/2)", 1e-9);
            AssertTrue(Math.Abs(expected - 1.2) > 0.4,
                "voi 60 do offset phai KHAC han T (chung to khong bi hard-code)");

            List<FoilProfileElement> flanges = Flanges(r);
            AssertClose(20.0 + expected, flanges[0].EffectiveMoldLineLength, "canh 1", 1e-9);
            AssertClose(20.0 + expected, flanges[1].EffectiveMoldLineLength, "canh 2", 1e-9);
        }

        /// <summary>
        /// Neu tool doan nguoc phia vat lieu, doi mot lua chon la dao duoc tap duong chan
        /// duoc bu. Muc bu cua UP va DOWN phai cong don dung bang che do "tat ca".
        /// </summary>
        private static void Test40_CompensationModeSwitch()
        {
            FoilRawVertex[] z =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(30, 20, 0),
                new FoilRawVertex(30, 40, 0)
            };

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 1.2;

            s.ThicknessCompensation = FoilThicknessCompensationMode.None;
            FoilFlatPatternResult none = Compute(s, z, false);

            s.ThicknessCompensation = FoilThicknessCompensationMode.TurnLeft;
            FoilFlatPatternResult up = Compute(s, z, false);

            s.ThicknessCompensation = FoilThicknessCompensationMode.TurnRight;
            FoilFlatPatternResult down = Compute(s, z, false);

            s.ThicknessCompensation = FoilThicknessCompensationMode.AllBends;
            FoilFlatPatternResult all = Compute(s, z, false);

            AssertNoErrors(none);
            AssertNoErrors(all);

            // Bien dang Z co dung 1 chan UP va 1 chan DOWN.
            AssertEqual(0, CountCompensated(none), "che do None khong bu duong nao");
            AssertEqual(1, CountCompensated(up), "che do UP chi bu 1 duong chan");
            AssertEqual(1, CountCompensated(down), "che do DOWN chi bu 1 duong chan");
            AssertEqual(2, CountCompensated(all), "che do AllBends bu ca 2");

            // UP va DOWN phai bu HAI duong chan khac nhau.
            AssertTrue(up.Bends[0].IsThicknessCompensated != down.Bends[0].IsThicknessCompensated,
                "UP va DOWN phai bu hai duong chan khac nhau");

            // Cong don: (UP - None) + (DOWN - None) = (All - None)
            double deltaUp = up.BlankWidth - none.BlankWidth;
            double deltaDown = down.BlankWidth - none.BlankWidth;
            double deltaAll = all.BlankWidth - none.BlankWidth;

            AssertTrue(deltaUp > 0.0, "bu be day phai lam phoi rong ra");
            AssertClose(deltaAll, deltaUp + deltaDown, "muc bu phai cong don dung", 1e-9);
        }

        /// <summary>
        /// HOI QUY - LOI DA TUNG XAY RA:
        /// Tuy chon "Dao huong chan UP/DOWN" chi de doi NHAN va MAU cho hop quy uoc xuong.
        /// Truoc day phep bu be day bam vao nhan do, nen bat/tat mot tuy chon HIEN THI lam
        /// KICH THUOC PHOI nhay 2.4 mm.
        ///
        /// Phai dung bien dang LECH SO (bac thang: 2 re phai + 1 re trai) moi lo ra loi nay -
        /// bien dang can bang se che lap vi hai phia bu bang nhau.
        /// </summary>
        private static void Test41_DisplayFlipDoesNotChangeSize()
        {
            FoilRawVertex[] stair = StairProfile();

            foreach (FoilThicknessCompensationMode mode in new[]
            {
                FoilThicknessCompensationMode.None,
                FoilThicknessCompensationMode.TurnLeft,
                FoilThicknessCompensationMode.TurnRight,
                FoilThicknessCompensationMode.AllBends
            })
            {
                FoilSettings normal = BaseSettings(FoilBendMethod.CustomShopRule);
                normal.ThicknessCompensation = mode;
                normal.InvertBendDirection = false;

                FoilSettings flipped = BaseSettings(FoilBendMethod.CustomShopRule);
                flipped.ThicknessCompensation = mode;
                flipped.InvertBendDirection = true;

                FoilFlatPatternResult a = Compute(normal, stair, false);
                FoilFlatPatternResult b = Compute(flipped, stair, false);

                AssertNoErrors(a);
                AssertNoErrors(b);

                AssertClose(a.BlankWidth, b.BlankWidth,
                    "doi nhan UP/DOWN khong duoc lam doi chieu rong phoi (" + mode + ")", 1e-12);
                AssertEqual(CountCompensated(a), CountCompensated(b),
                    "doi nhan UP/DOWN khong duoc lam doi so duong duoc bu (" + mode + ")");

                // Tung duong chan phai giu nguyen trang thai bu.
                for (int i = 0; i < a.Bends.Count; i++)
                {
                    AssertTrue(
                        a.Bends[i].IsThicknessCompensated == b.Bends[i].IsThicknessCompensated,
                        "duong chan #" + (i + 1) + " phai giu nguyen trang thai bu (" + mode + ")");
                }

                // Nhung NHAN thi dung la phai dao.
                if (a.BendCount > 0)
                {
                    AssertTrue(a.Bends[0].Direction != b.Bends[0].Direction,
                        "nhan UP/DOWN van phai duoc dao that su (" + mode + ")");
                }
            }
        }

        /// <summary>
        /// Voi bien dang LECH SO (so goc re trai khac so goc re phai), hai chieu bu phai cho
        /// PHOI KHAC NHAU - day chinh la luc nguoi dung phai nhin vong tron de chon dung phia.
        /// Bac thang 10-20-15-25: 1 re trai + 2 re phai.
        /// </summary>
        private static void Test42_UnbalancedProfileReverse()
        {
            FoilRawVertex[] stair = StairProfile();

            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 1.2;

            s.ThicknessCompensation = FoilThicknessCompensationMode.None;
            FoilFlatPatternResult none = Compute(s, stair, false);

            s.ThicknessCompensation = FoilThicknessCompensationMode.TurnLeft;
            FoilFlatPatternResult left = Compute(s, stair, false);

            s.ThicknessCompensation = FoilThicknessCompensationMode.TurnRight;
            FoilFlatPatternResult right = Compute(s, stair, false);

            AssertNoErrors(none);
            AssertNoErrors(left);
            AssertNoErrors(right);

            AssertEqual(3, none.BendCount, "bac thang co 3 duong chan");
            AssertEqual(1, CountCompensated(left), "chi 1 goc re trai");
            AssertEqual(2, CountCompensated(right), "co 2 goc re phai");

            // 62.8 goc; moi goc duoc bu lam phoi rong them 2 * T = 2.4
            AssertClose(62.8, none.BlankWidth, "phoi khi khong bu", 1e-9);
            AssertClose(62.8 + 1 * 2.4, left.BlankWidth, "phoi khi bu phia re trai", 1e-9);
            AssertClose(62.8 + 2 * 2.4, right.BlankWidth, "phoi khi bu phia re phai", 1e-9);

            AssertTrue(Math.Abs(left.BlankWidth - right.BlankWidth) > 1e-9,
                "bien dang lech so PHAI cho hai ket qua khac nhau khi dao chieu");

            // Moi duong chan duoc bu dung 1 trong 2 chieu (phan hoach dung).
            for (int i = 0; i < none.BendCount; i++)
            {
                AssertTrue(
                    left.Bends[i].IsThicknessCompensated != right.Bends[i].IsThicknessCompensated,
                    "duong chan #" + (i + 1) + " phai duoc bu o dung mot trong hai chieu");
            }
        }

        // ==================================================================================
        // HA TANG TEST
        // ==================================================================================

        /// <summary>Bac thang 10-20-15-25: 3 duong chan, LECH SO (1 re trai, 2 re phai).</summary>
        private static FoilRawVertex[] StairProfile()
        {
            return new[]
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 10, 0),
                new FoilRawVertex(20, 10, 0),
                new FoilRawVertex(20, 25, 0),
                new FoilRawVertex(45, 25, 0)
            };
        }

        private static int CountCompensated(FoilFlatPatternResult result)
        {
            int count = 0;
            foreach (FoilBendInfo b in result.Bends)
            {
                if (b.IsThicknessCompensated)
                {
                    count++;
                }
            }

            return count;
        }

        // ==================================================================================
        // MO HINH MAY CHAN VA TRINH TU CHAN
        // ==================================================================================

        /// <summary>
        /// Dao that KHONG phai hinh chem suot chieu cao: chi nhon o mui roi thanh luoi song
        /// song. Neu mo hinh coi ca dao la hinh chem thi moi canh da chan deu bi bao "va dao"
        /// (day dung la loi da tung mac phai) - test nay khoa lai hinh dang dung.
        /// </summary>
        private static void Test43_PunchProfile()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.DieOpening = 10.0;
            s.PunchIncludedAngleDeg = 85.0;
            s.PunchTipRadius = 1.0;
            s.PunchBladeHalfWidth = 6.0;

            FoilToolingGeometry t = FoilToolingGeometry.Resolve(s);

            AssertClose(10.0, t.DieOpening, "khau do coi lay dung gia tri nguoi dung", 1e-12);
            AssertClose(6.0, t.PunchBladeHalfWidth, "nua be day luoi dao", 1e-12);

            // Mui nhon: tu 0 den PunchNoseHeight be rong tang dan theo goc dao.
            double nose = t.PunchNoseHeight;
            AssertTrue(nose > 0.0, "phai co mot doan mui nhon");
            AssertClose(1.0, t.PunchHalfWidthAt(0.0), "tai mui dao be rong = ban kinh mui", 1e-12);
            AssertClose(6.0, t.PunchHalfWidthAt(nose), "het doan nhon thi bang be day luoi", 1e-9);

            // Tren doan nhon: LUOI SONG SONG - be rong khong doi.
            AssertClose(6.0, t.PunchHalfWidthAt(nose * 2.0), "tren mui la luoi song song", 1e-12);
            AssertClose(6.0, t.PunchHalfWidthAt(t.PunchHeight * 0.99), "van la luoi song song", 1e-12);

            // Tren chieu cao lam viec la do ga - rong han.
            AssertTrue(t.PunchHalfWidthAt(t.PunchHeight + 1.0) > 6.0,
                "tren chieu cao lam viec la do ga, rong hon luoi dao");

            // Goc chan lon nhat: 180 - goc dao.
            AssertClose(95.0, t.MaxBendAngleRad * FoilMath.RadToDeg,
                "goc chan lon nhat cua dao 85 do", 1e-9);
        }

        /// <summary>
        /// Tren may chan, goc chan LUON mo len tren (dao tu tren an xuong long coi). Vi vay
        /// trong he toa do may, sau moi lan chan hai canh ke duong chan deu phai NGOC LEN,
        /// va dinh goc chan phai nam dung tai goc toa do.
        /// </summary>
        private static void Test44_MachineFrameOpensUp()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;

            FoilFlatPatternResult r = Compute(s, HatProfile());
            AssertNoErrors(r);

            FoilBendPlan plan = FoilBendSequenceBuilder.Plan(r, s);
            AssertEqual(r.BendCount + 1, plan.Steps.Count, "so buoc = so duong chan + 1");

            for (int i = 1; i < plan.Steps.Count; i++)
            {
                FoilBendStep step = plan.Steps[i];
                List<FoilPoint2d> pts = step.MachinePoints;
                AssertTrue(pts.Count >= 2, "buoc B" + i + " phai co hinh trong he toa do may");

                // Dinh goc chan nam tai goc toa do => phai co dung mot diem trung goc.
                int atOrigin = 0;
                foreach (FoilPoint2d q in pts)
                {
                    if (Math.Abs(q.X) < 1e-9 && Math.Abs(q.Y) < 1e-9) atOrigin++;
                }

                AssertTrue(atOrigin >= 1,
                    "B" + i + ": dinh goc chan phai nam tai goc he toa do may");

                // Hai diem ke goc phai NGOC LEN (y > 0) - day la dac trung cua chan tren may.
                int originIndex = -1;
                for (int k = 0; k < pts.Count; k++)
                {
                    if (Math.Abs(pts[k].X) < 1e-9 && Math.Abs(pts[k].Y) < 1e-9) { originIndex = k; break; }
                }

                if (originIndex > 0)
                {
                    AssertTrue(pts[originIndex - 1].Y > 1e-9,
                        "B" + i + ": canh truoc goc chan phai ngoc len");
                }

                if (originIndex >= 0 && originIndex < pts.Count - 1)
                {
                    AssertTrue(pts[originIndex + 1].Y > 1e-9,
                        "B" + i + ": canh sau goc chan phai ngoc len");
                }
            }
        }

        /// <summary>
        /// Chu Z co hai goc re NGUOC chieu nhau. May chi chan duoc mot chieu, nen bat buoc
        /// phai LAT TON dung mot lan - khong the it hon.
        /// </summary>
        private static void Test45_ZProfileNeedsOneFlip()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;

            FoilRawVertex[] z =
            {
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(40, 0, 0),
                new FoilRawVertex(40, -20, 0)
            };

            FoilFlatPatternResult r = Compute(s, z);
            AssertNoErrors(r);
            AssertEqual(2, r.BendCount, "chu Z co 2 duong chan");

            FoilBendPlan plan = FoilBendSequenceBuilder.Plan(r, s);
            AssertTrue(plan.AllFeasible, "chu Z phai chan duoc");
            AssertEqual(1, plan.FlipCount, "chu Z bat buoc lat ton dung 1 lan");

            // Hai lan chan phai nam o hai mat khac nhau.
            AssertTrue(plan.Steps[1].Flipped != plan.Steps[2].Flipped,
                "hai goc nguoc chieu phai chan o hai mat khac nhau");

            // Chu U (hai goc CUNG chieu) thi khong phai lat lan nao.
            FoilRawVertex[] u =
            {
                new FoilRawVertex(0, 15, 0),
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(60, 0, 0),
                new FoilRawVertex(60, 15, 0)
            };

            FoilBendPlan uPlan = FoilBendSequenceBuilder.Plan(Compute(s, u), s);
            AssertTrue(uPlan.AllFeasible, "chu U rong phai chan duoc");
            AssertEqual(0, uPlan.FlipCount, "chu U khong phai lat ton lan nao");
        }

        /// <summary>
        /// BAI TOAN CHINH cua tinh nang nay: voi mu doi xung, chan lan luot theo thu tu VE
        /// (1,2,3,4) se ket o buoc cuoi - canh da chan chuc xuong duoi mat coi. Che do tu dong
        /// phai tim ra mot thu tu chan duoc het.
        /// </summary>
        private static void Test46_AutoOrderBeatsProfileOrder()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = 1.5;
            s.InsideRadius = 1.5;

            FoilRawVertex[] hat =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(30, 0, 0),
                new FoilRawVertex(30, 40, 0),
                new FoilRawVertex(90, 40, 0),
                new FoilRawVertex(90, 0, 0),
                new FoilRawVertex(120, 0, 0)
            };

            FoilFlatPatternResult r = Compute(s, hat);
            AssertNoErrors(r);
            AssertEqual(4, r.BendCount, "mu doi xung co 4 duong chan");

            FoilSettings profileOrder = s.Clone();
            profileOrder.BendSequenceOrder = FoilBendSequenceOrder.ProfileOrder;
            FoilBendPlan naive = FoilBendSequenceBuilder.Plan(r, profileOrder);

            FoilSettings autoOrder = s.Clone();
            autoOrder.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;
            FoilBendPlan smart = FoilBendSequenceBuilder.Plan(r, autoOrder);

            AssertTrue(!naive.AllFeasible,
                "chan lan luot theo thu tu VE phai bi ket (day la ly do can thuat toan)");
            AssertTrue(smart.AllFeasible,
                "che do tu dong phai tim duoc thu tu chan het");
            AssertEqual(4, smart.Order.Count, "thu tu phai du 4 duong chan");

            // Thu tu phai la mot HOAN VI day du, khong lap, khong sot.
            HashSet<int> seen = new HashSet<int>();
            foreach (int index in smart.Order)
            {
                AssertTrue(seen.Add(index), "thu tu chan khong duoc lap duong chan #" + index);
                AssertTrue(index >= 1 && index <= 4, "chi so duong chan phai trong 1..4");
            }
        }

        /// <summary>
        /// Thu tu chan la chuyen CONG NGHE, khong duoc dung vao con so phoi. Du chon thu tu
        /// nao, chieu rong phoi va vi tri moi duong chan phai y het nhau.
        /// </summary>
        private static void Test47_OrderDoesNotChangeBlank()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.BendDeductionKFactor);
            FoilFlatPatternResult r = Compute(s, HatProfile());
            AssertNoErrors(r);

            double width = r.BlankWidth;
            FoilBendStep reference = null;

            foreach (FoilBendSequenceOrder order in new[]
            {
                FoilBendSequenceOrder.ProfileOrder,
                FoilBendSequenceOrder.Reverse,
                FoilBendSequenceOrder.OutsideIn,
                FoilBendSequenceOrder.AutoFeasible
            })
            {
                FoilSettings variant = s.Clone();
                variant.BendSequenceOrder = order;

                FoilBendPlan plan = FoilBendSequenceBuilder.Plan(r, variant);
                AssertEqual(r.BendCount + 1, plan.Steps.Count, "so buoc (" + order + ")");

                AssertClose(width, r.BlankWidth, "chieu rong phoi khong doi (" + order + ")", 1e-12);
                AssertClose(r.BlankWidth, plan.Steps[0].OutlineLength,
                    "B0 luon bang chieu rong phoi (" + order + ")", 1e-9);

                FoilBendStep last = plan.Steps[plan.Steps.Count - 1];
                AssertClose(r.MoldLineTotalLength, last.OutlineLength,
                    "buoc cuoi luon bang tong mold line (" + order + ")", 1e-9);

                if (reference == null)
                {
                    reference = last;
                    continue;
                }

                AssertEqual(reference.Points.Count, last.Points.Count,
                    "so dinh buoc cuoi (" + order + ")");

                for (int i = 0; i < reference.Points.Count; i++)
                {
                    AssertClose(0.0, reference.Points[i].DistanceTo(last.Points[i]),
                        "dinh #" + i + " cua buoc cuoi (" + order + ")", 1e-9);
                }
            }
        }

        /// <summary>
        /// Long chu U hep va sau: canh da chan nga vao long dao khi chan canh con lai.
        /// Voi dao 85 do va luoi day, truong hop nay phai bi bao (ho dao am hoac rat nho).
        /// Cung bien dang do nhung LONG RONG thi phai chan duoc thoai mai.
        /// </summary>
        private static void Test48_NarrowChannelHitsPunch()
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;
            s.DieOpening = 10.0;
            s.PunchBladeHalfWidth = 10.0;   // luoi day 20 mm

            FoilRawVertex[] narrow =
            {
                new FoilRawVertex(0, 40, 0),
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(16, 0, 0),
                new FoilRawVertex(16, 40, 0)
            };

            FoilBendPlan tight = FoilBendSequenceBuilder.Plan(Compute(s, narrow), s);
            AssertTrue(!tight.AllFeasible,
                "long 16 mm voi canh 40 mm va luoi dao 20 mm phai bi bao va dao");

            FoilRawVertex[] wide =
            {
                new FoilRawVertex(0, 40, 0),
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(120, 0, 0),
                new FoilRawVertex(120, 40, 0)
            };

            FoilBendPlan roomy = FoilBendSequenceBuilder.Plan(Compute(s, wide), s);
            AssertTrue(roomy.AllFeasible, "long 120 mm thi phai chan duoc");
            AssertTrue(roomy.Steps[2].PunchClearance > 0.0, "phai con ho dao duong");
        }

        /// <summary>
        /// Canh ngan hon V/2 + R + T thi khong gac duoc len vai coi. Phai duoc canh bao,
        /// va canh bao phai BIEN MAT khi doi sang coi nho hon.
        /// </summary>
        private static void Test49_ShortFlangeWarning()
        {
            FoilRawVertex[] l =
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 8, 0),
                new FoilRawVertex(60, 8, 0)
            };

            FoilSettings big = BaseSettings(FoilBendMethod.CustomShopRule);
            big.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;
            big.DieOpening = 40.0;          // canh nho nhat = 20 + 1.2 + 1.2 = 22.4 > 8

            FoilBendPlan bigPlan = FoilBendSequenceBuilder.Plan(Compute(big, l), big);
            AssertTrue(HasIssue(bigPlan, "canh nho nhat"),
                "canh 8 mm tren coi V40 phai bi canh bao qua ngan");

            FoilSettings small = big.Clone();
            small.DieOpening = 6.0;         // canh nho nhat = 3 + 1.2 + 1.2 = 5.4 < 8

            FoilBendPlan smallPlan = FoilBendSequenceBuilder.Plan(Compute(small, l), small);
            AssertTrue(!HasIssue(smallPlan, "canh nho nhat"),
                "doi sang coi V6 thi canh 8 mm khong con bi canh bao");
        }

        /// <summary>
        /// Tim thu tu chan la bai toan hoan vi. Ban cai dat dung TIM KIEM CHUM (beam search)
        /// cho nhanh; test nay VET CAN toan bo hoan vi tren nhieu bien dang de chac chan chum
        /// khong bo sot phuong an tot hon.
        /// </summary>
        private static void Test50_BeamSearchMatchesBruteForce()
        {
            FoilRawVertex[][] profiles =
            {
                // Mu doi xung.
                new[]
                {
                    new FoilRawVertex(0, 0, 0), new FoilRawVertex(30, 0, 0),
                    new FoilRawVertex(30, 40, 0), new FoilRawVertex(90, 40, 0),
                    new FoilRawVertex(90, 0, 0), new FoilRawVertex(120, 0, 0)
                },

                // Bac thang - moi goc cung mot chieu.
                new[]
                {
                    new FoilRawVertex(0, 0, 0), new FoilRawVertex(0, 25, 0),
                    new FoilRawVertex(40, 25, 0), new FoilRawVertex(40, 50, 0),
                    new FoilRawVertex(80, 50, 0), new FoilRawVertex(80, 75, 0)
                },

                // Chu Z co them mot canh.
                new[]
                {
                    new FoilRawVertex(0, 30, 0), new FoilRawVertex(0, 0, 0),
                    new FoilRawVertex(50, 0, 0), new FoilRawVertex(50, -30, 0),
                    new FoilRawVertex(90, -30, 0)
                }
            };

            foreach (FoilRawVertex[] profile in profiles)
            {
                FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
                s.Thickness = 1.5;
                s.InsideRadius = 1.5;
                s.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;

                FoilFlatPatternResult r = Compute(s, profile);
                AssertNoErrors(r);
                AssertTrue(r.BendCount >= 3 && r.BendCount <= 7, "so duong chan hop ly de vet can");

                FoilToolingGeometry tooling = FoilToolingGeometry.Resolve(s);
                Dictionary<int, FoilBendInfo> byIndex = new Dictionary<int, FoilBendInfo>();
                List<int> all = new List<int>();
                foreach (FoilBendInfo b in r.Bends) { byIndex[b.Index] = b; all.Add(b.Index); }

                int bestBlocked = int.MaxValue;
                List<List<int>> permutations = new List<List<int>>();
                Permute(all, 0, permutations);

                foreach (List<int> order in permutations)
                {
                    int blocked = CountBlocked(r, byIndex, tooling, order);
                    if (blocked < bestBlocked) bestBlocked = blocked;
                }

                FoilBendPlan plan = FoilBendSequenceBuilder.Plan(r, s);
                int planBlocked = CountBlocked(r, byIndex, tooling, plan.Order);

                AssertEqual(bestBlocked, planBlocked,
                    "tim kiem chum phai dat so buoc bi chan nho nhat (" + r.BendCount + " duong chan)");

                AssertEqual(bestBlocked == 0, plan.AllFeasible,
                    "co bao 'chan duoc het' dung voi ket qua vet can");
            }
        }

        private static void AssertEqual(bool expected, bool actual, string what)
        {
            AssertTrue(expected == actual,
                what + " (mong doi " + expected + ", nhan duoc " + actual + ")");
        }

        private static int CountBlocked(
            FoilFlatPatternResult result,
            Dictionary<int, FoilBendInfo> byIndex,
            FoilToolingGeometry tooling,
            List<int> order)
        {
            HashSet<int> formed = new HashSet<int>();
            int blocked = 0;

            foreach (int index in order)
            {
                FoilFormedShape shape = FoilFormedShape.Build(result, formed);
                FoilBendMounting mount = FoilPressBrakeModel.Evaluate(shape, byIndex[index], tooling);
                if (!mount.Feasible) blocked++;
                formed.Add(index);
            }

            return blocked;
        }

        private static void Permute(List<int> items, int start, List<List<int>> output)
        {
            if (start >= items.Count)
            {
                output.Add(new List<int>(items));
                return;
            }

            for (int i = start; i < items.Count; i++)
            {
                int tmp = items[start]; items[start] = items[i]; items[i] = tmp;
                Permute(items, start + 1, output);
                tmp = items[start]; items[start] = items[i]; items[i] = tmp;
            }
        }

        /// <summary>
        /// Hai o hinh buoc chan KHONG duoc de len nhau, va ca day phai nam HOAN TOAN duoi phoi.
        ///
        /// Loi cu: be rong o chi tinh theo hinh chi tiet, con chieu cao chu lai suy tu chieu cao
        /// hinh - voi bien dang cao thi dong chu dai gap may lan be rong o nen cac nhan de chong
        /// len nhau. Test nay tinh hop bao cua tung o DA GOM CA DONG CHU va bat loi do.
        /// </summary>
        private static void Test51_StepCellsDoNotOverlap()
        {
            // Hai truong hop bay:
            //   * chi tiet NHO tren phoi DAI  -> xep duoc nhieu cot, nhan de dam vao cot ben canh
            //   * chi tiet CAO tren phoi ngan -> mot cot, cac hang de dam vao nhau
            CheckStepLayout(2500.0, 1.2, new[]
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(20, 0, 0),
                new FoilRawVertex(20, 30, 0),
                new FoilRawVertex(60, 30, 0),
                new FoilRawVertex(60, 0, 0),
                new FoilRawVertex(80, 0, 0)
            });

            CheckStepLayout(800.0, 2.0, new[]
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 120, 0),
                new FoilRawVertex(45, 120, 0),
                new FoilRawVertex(45, 0, 0),
                new FoilRawVertex(90, 0, 0),
                new FoilRawVertex(90, 110, 0)
            });
        }

        private static void CheckStepLayout(
            double blankLength, double thickness, FoilRawVertex[] profile)
        {
            FoilSettings s = BaseSettings(FoilBendMethod.CustomShopRule);
            s.Thickness = thickness;
            s.InsideRadius = thickness;
            s.BlankLength = blankLength;
            s.DrawBendSteps = true;
            s.DrawTooling = true;
            s.BendSequenceOrder = FoilBendSequenceOrder.AutoFeasible;

            FoilFlatPatternResult r = Compute(s, profile);
            AssertNoErrors(r);

            FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                r, new FoilPoint2d(0, 0), 0.0);

            AssertTrue(geometry.Steps.Count >= 2, "phai sinh duoc nhieu buoc de kiem tra");

            double h = geometry.StepTextHeight;
            AssertTrue(h > 0.0, "phai co chieu cao chu");

            List<double[]> boxes = new List<double[]>();
            foreach (FoilBendStepGeometry step in geometry.Steps)
            {
                double[] box = { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
                Grow(box, step.Points);
                Grow(box, step.DiePoints);
                Grow(box, step.PunchPoints);

                // Hai dong chu: uoc luong be rong nhu tang ve dang dung.
                GrowText(box, step.LabelPosition, step.Label, h);
                GrowText(box, step.DetailPosition, step.Detail, h * 0.8);

                boxes.Add(box);

                // Ca day phai nam duoi canh duoi cua phoi (V = 0).
                AssertTrue(box[3] < 1e-9,
                    "hinh buoc chan phai nam duoi phoi, khong de len phoi");
            }

            const double tolerance = -1e-9;
            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    double overlapX = Math.Min(boxes[i][2], boxes[j][2]) - Math.Max(boxes[i][0], boxes[j][0]);
                    double overlapY = Math.Min(boxes[i][3], boxes[j][3]) - Math.Max(boxes[i][1], boxes[j][1]);

                    AssertTrue(overlapX <= tolerance || overlapY <= tolerance,
                        "o buoc " + i + " va " + j + " de len nhau");
                }
            }
        }

        private static void Grow(double[] box, List<FoilPoint2d> points)
        {
            foreach (FoilPoint2d p in points)
            {
                if (p.X < box[0]) box[0] = p.X;
                if (p.Y < box[1]) box[1] = p.Y;
                if (p.X > box[2]) box[2] = p.X;
                if (p.Y > box[3]) box[3] = p.Y;
            }
        }

        private static void GrowText(double[] box, FoilPoint2d at, string text, double height)
        {
            if (string.IsNullOrEmpty(text)) return;

            double width = text.Length * height * 0.62;
            Grow(box, new List<FoilPoint2d>
            {
                at,
                new FoilPoint2d(at.X + width, at.Y + height)
            });
        }

        /// <summary>
        /// Hinh mot buoc duoc giu o HAI he toa do: he bien dang (de kiem chieu dai vat lieu) va
        /// HE TOA DO MAY (de ve cho tho). Hai cai phai la CUNG MOT HINH, chi khac phep dat.
        ///
        /// Loi da tung mac: hinh o he toa do may duoc dung bang cach xoay hinh TRUOC khi chan,
        /// nen vung chan dai BA van con nguyen - goc chan bi ve CUT va hai canh ke ngan mat
        /// dung bang setback. Voi quy tac xuong BA = 0 nen khong lo ra; chi cac phuong phap
        /// K-factor (BA > 0) moi thay. Test nay chay CA HAI nhom phuong phap.
        /// </summary>
        private static void Test52_MachineShapeIsCongruent()
        {
            foreach (FoilBendMethod method in new[]
            {
                FoilBendMethod.CustomShopRule,
                FoilBendMethod.BendDeductionKFactor,
                FoilBendMethod.BendAllowanceKFactor
            })
            {
                foreach (FoilBendSequenceOrder order in new[]
                {
                    FoilBendSequenceOrder.ProfileOrder,
                    FoilBendSequenceOrder.AutoFeasible
                })
                {
                    FoilSettings s = BaseSettings(method);
                    s.BendSequenceOrder = order;

                    FoilFlatPatternResult r = Compute(s, HatProfile());
                    AssertNoErrors(r);

                    // Voi K-factor thi BA phai KHAC 0, neu khong test nay khong co y nghia.
                    if (method != FoilBendMethod.CustomShopRule)
                    {
                        AssertTrue(r.Bends[0].BendAllowance > 1e-6,
                            "phuong phap " + method + " phai co BA > 0 thi phep thu moi co y nghia");
                    }

                    FoilBendPlan plan = FoilBendSequenceBuilder.Plan(r, s);

                    foreach (FoilBendStep step in plan.Steps)
                    {
                        if (step.FormedBend == null) continue;

                        List<FoilPoint2d> profile = FoilBendSequenceBuilder.Simplify(step.Points);
                        List<FoilPoint2d> machine = FoilBendSequenceBuilder.Simplify(step.MachinePoints);

                        string tag = method + "/" + order + " B" + step.StepNumber;

                        AssertEqual(profile.Count, machine.Count, "so dinh hai he toa do (" + tag + ")");

                        double profileLength = 0.0;
                        double machineLength = 0.0;

                        for (int i = 0; i < profile.Count - 1 && i < machine.Count - 1; i++)
                        {
                            double a = profile[i].DistanceTo(profile[i + 1]);
                            double b = machine[i].DistanceTo(machine[i + 1]);
                            profileLength += a;
                            machineLength += b;

                            AssertClose(a, b, "canh #" + (i + 1) + " (" + tag + ")", 1e-9);
                        }

                        AssertClose(profileLength, machineLength,
                            "tong chieu dai (" + tag + ")", 1e-9);

                        // Dinh goc chan vua thuc hien phai nam dung tai goc he toa do may.
                        bool atOrigin = false;
                        foreach (FoilPoint2d q in machine)
                        {
                            if (Math.Abs(q.X) < 1e-9 && Math.Abs(q.Y) < 1e-9) { atOrigin = true; break; }
                        }

                        AssertTrue(atOrigin, "dinh goc chan phai o goc toa do may (" + tag + ")");
                    }
                }
            }
        }

        /// <summary>
        /// BAT BIEN VAT LY CUA MAY CHAN: dao di tu tren xuong long coi, nen trong he toa do may
        /// khau chan LUON MO LEN TREN - hai canh ke dinh goc deu ngoc len, va dinh goc la diem
        /// THAP NHAT cua cum quanh no.
        ///
        /// Day chinh la rang buoc sinh ra toan bo bai toan thu tu: goc re nguoc chieu thi khong
        /// con cach nao khac ngoai LAT TON. Neu phep dat bi sai dau o dau do thi test nay bat
        /// duoc, bat ke phuong phap tinh hay che do thu tu nao.
        /// </summary>
        private static void Test53_BendAlwaysOpensUpward()
        {
            int checkedSteps = 0;

            foreach (FoilBendMethod method in new[]
            {
                FoilBendMethod.CustomShopRule,
                FoilBendMethod.BendDeductionKFactor,
                FoilBendMethod.BendAllowanceKFactor
            })
            {
                foreach (FoilBendSequenceOrder order in new[]
                {
                    FoilBendSequenceOrder.ProfileOrder,
                    FoilBendSequenceOrder.Reverse,
                    FoilBendSequenceOrder.OutsideIn,
                    FoilBendSequenceOrder.AutoFeasible
                })
                {
                    FoilSettings s = BaseSettings(method);
                    s.BendSequenceOrder = order;

                    FoilFlatPatternResult r = Compute(s, HatProfile());
                    AssertNoErrors(r);

                    FoilBendPlan plan = FoilBendSequenceBuilder.Plan(r, s);

                    foreach (FoilBendStep step in plan.Steps)
                    {
                        if (step.FormedBend == null) continue;

                        List<FoilPoint2d> pts = FoilBendSequenceBuilder.Simplify(step.MachinePoints);
                        string tag = method + "/" + order + " B" + step.StepNumber;

                        int apex = -1;
                        for (int i = 0; i < pts.Count; i++)
                        {
                            if (Math.Abs(pts[i].X) < 1e-9 && Math.Abs(pts[i].Y) < 1e-9) { apex = i; break; }
                        }

                        AssertTrue(apex >= 0, "phai co dinh goc chan tai goc toa do (" + tag + ")");

                        // Hai canh ke dinh goc phai NGOC LEN.
                        if (apex > 0)
                        {
                            AssertTrue(pts[apex - 1].Y > 1e-9,
                                "canh truoc dinh goc phai ngoc len (" + tag + ")");
                        }

                        if (apex >= 0 && apex < pts.Count - 1)
                        {
                            AssertTrue(pts[apex + 1].Y > 1e-9,
                                "canh sau dinh goc phai ngoc len (" + tag + ")");
                        }

                        // Goc mo giua hai canh phai dung bang goc mo cua duong chan.
                        if (apex > 0 && apex < pts.Count - 1)
                        {
                            FoilVector2d left = (pts[apex - 1] - pts[apex]).Normalized();
                            FoilVector2d right = (pts[apex + 1] - pts[apex]).Normalized();

                            double opening = FoilMath.AngleBetween(left, right) * FoilMath.RadToDeg;
                            AssertClose(step.FormedBend.OpeningAngleDeg, opening,
                                "goc mo tai dinh (" + tag + ")", 1e-6);

                            // Duong phan giac phai huong THANG LEN (truc dao).
                            FoilVector2d bisector = (left + right).Normalized();
                            AssertClose(0.0, bisector.X, "phan giac phai trung truc dao (" + tag + ")", 1e-9);
                            AssertTrue(bisector.Y > 0.0, "phan giac phai huong len (" + tag + ")");
                        }

                        checkedSteps++;
                    }
                }
            }

            AssertTrue(checkedSteps >= 90, "phai kiem duoc nhieu buoc (" + checkedSteps + ")");
        }

        private static bool HasIssue(FoilBendPlan plan, string fragment)
        {
            foreach (FoilBendStep step in plan.Steps)
            {
                foreach (FoilBendIssue issue in step.Issues)
                {
                    if (issue.Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static FoilSettings BaseSettings(FoilBendMethod method)
        {
            return new FoilSettings
            {
                BlankLength = 2500.0,
                Thickness = 1.2,
                InsideRadius = 1.2,
                KFactor = 0.42,
                CustomFactor = 1.0,
                Method = method,
                Precision = 2,

                // Cac test co dinh coi polyline da la duong chan that o MOI goc.
                // Che do bu be day tai goc lom duoc kiem tra rieng o test 38/39/40.
                ThicknessCompensation = FoilThicknessCompensationMode.None
            };
        }

        /// <summary>Bien dang L: len 20, roi sang phai 30. Mot chan 90 do.</summary>
        private static FoilRawVertex[] LProfile()
        {
            return new[]
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(30, 20, 0)
            };
        }

        /// <summary>Bien dang U: xuong 20, sang phai 30, len 20. Hai chan 90 do.</summary>
        private static FoilRawVertex[] UProfile()
        {
            return new[]
            {
                new FoilRawVertex(0, 20, 0),
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(30, 0, 0),
                new FoilRawVertex(30, 20, 0)
            };
        }

        private static FoilFlatPatternResult Compute(FoilSettings settings, FoilRawVertex[] vertices)
        {
            return Compute(settings, vertices, false, null);
        }

        private static FoilFlatPatternResult Compute(
            FoilSettings settings, FoilRawVertex[] vertices, bool closed)
        {
            return Compute(settings, vertices, closed, null);
        }

        private static FoilFlatPatternResult Compute(
            FoilSettings settings, FoilRawVertex[] vertices, bool closed, FoilBendTable table)
        {
            List<string> notes = new List<string>();
            FoilProfile profile = FoilProfileBuilder.Build(
                vertices, closed, settings.DuplicatePointTolerance, notes);

            FoilProfileAnalysis analysis = FoilProfileAnalyzer.Analyze(profile, settings);
            analysis.Notes.InsertRange(0, notes);

            IFoilBendStrategy strategy = FoilStrategyFactory.Create(settings, table);
            return FoilFlatPatternCalculator.Calculate(analysis, settings, strategy);
        }

        private static bool OnRectangleBoundary(FoilPoint2d p, double length, double width)
        {
            const double e = 1e-7;
            bool inside = p.X >= -e && p.X <= length + e && p.Y >= -e && p.Y <= width + e;
            bool onEdge = Math.Abs(p.X) <= e || Math.Abs(p.X - length) <= e ||
                          Math.Abs(p.Y) <= e || Math.Abs(p.Y - width) <= e;
            return inside && onEdge;
        }

        private static void RunTest(FoilTestReport report, string name, Action test)
        {
            try
            {
                test();
                report.Passed++;
                report.Lines.Add("[PASS] " + name);
            }
            catch (Exception ex)
            {
                report.Failed++;
                report.Lines.Add("[FAIL] " + name + "  ->  " + ex.Message);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual(int expected, int actual, string what)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: mong doi {1}, nhan duoc {2}", what, expected, actual));
            }
        }

        private static void AssertEqual(string expected, string actual, string what)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: mong doi '{1}', nhan duoc '{2}'", what, expected, actual));
            }
        }

        private static void AssertClose(double expected, double actual, string what)
        {
            AssertClose(expected, actual, what, Tol);
        }

        private static void AssertClose(double expected, double actual, string what, double tolerance)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: mong doi {1:0.##########}, nhan duoc {2:0.##########} (lech {3:0.##########})",
                    what, expected, actual, Math.Abs(expected - actual)));
            }
        }

        /// <summary>Bien dang mu (hat / C) co hem - giong ban ve mau cua nguoi dung.</summary>
        private static FoilRawVertex[] HatProfile()
        {
            return new[]
            {
                new FoilRawVertex(0, 0, 0),
                new FoilRawVertex(10, 0, 0),
                new FoilRawVertex(10, 47.5, 0),
                new FoilRawVertex(46.4, 47.5, 0),
                new FoilRawVertex(46.4, 32.3, 0),
                new FoilRawVertex(91.4, 32.3, 0),
                new FoilRawVertex(91.4, 47.5, 0),
                new FoilRawVertex(262.4, 47.5, 0),
                new FoilRawVertex(262.4, 0, 0),
                new FoilRawVertex(272.4, 0, 0)
            };
        }

        private static List<FoilProfileElement> Flanges(FoilFlatPatternResult result)
        {
            List<FoilProfileElement> flanges = new List<FoilProfileElement>();
            foreach (FoilProfileElement e in result.Elements)
            {
                if (e.Kind == FoilElementKind.Flange)
                {
                    flanges.Add(e);
                }
            }

            return flanges;
        }

        private static FoilBendStep Last(List<FoilBendStep> steps)
        {
            return steps[steps.Count - 1];
        }

        private static string Join(List<int> values)
        {
            string[] parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(",", parts);
        }

        private static string JoinAll(List<string> lines)
        {
            return string.Join(" | ", lines.ToArray());
        }

        private static void AssertNoErrors(FoilFlatPatternResult result)
        {
            if (result.HasErrors)
            {
                throw new InvalidOperationException("Co loi: " + string.Join(" | ", result.Errors.ToArray()));
            }
        }
    }
}
