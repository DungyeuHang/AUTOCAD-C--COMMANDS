using System;
using System.Collections.Generic;
using System.Threading;
using AUTOCAD_COMMANDS.Nesting.Core;
using static AUTOCAD_COMMANDS.Nesting.SelfTests.NestingTestHarness;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    /// <summary>
    /// DON HANG - phan lam viec o tang loi (khong cham AutoCAD).
    ///
    /// Dieu quan trong nhat duoc khoa lai o day: ten don la SIEU DU LIEU, no chi duoc phep len
    /// tieng khi moi thu khac da hoa. Khong bao gio duoc lam tang so to hay ton them vat lieu
    /// de doi lay viec cac don nam gon gang hon.
    /// </summary>
    public static class OrderSelfTests
    {
        public static void Run(NestingTestReport report)
        {
            NestingTestHarness.Run(report, "O01. Mot don: ten di het den ket qua", O01_SingleOrder);
            NestingTestHarness.Run(report, "O02. Hai don tren cung mot to", O02_TwoOrders);
            NestingTestHarness.Run(report, "O03. Ba don: khong don nao bi that lac", O03_ThreeOrders);
            NestingTestHarness.Run(report, "O04. Chi tiet van dung don cua no khi co hinh giong het", O04_SameShapeDifferentOrders);
            NestingTestHarness.Run(report, "O05. To tron don: danh sach don day du va dung", O05_MixedSheetOrders);
            NestingTestHarness.Run(report, "O06. Gom don KHONG duoc lam tang so to", O06_OrderNeverCostsASheet);
            NestingTestHarness.Run(report, "O07. Phai tron don khi tron moi du mot to", O07_MixWhenItSavesASheet);
            NestingTestHarness.Run(report, "O08. Khong tron thua khi so to nhu nhau", O08_NoNeedlessMixing);
            NestingTestHarness.Run(report, "O09. Ten don song sot qua phep XOAY", O09_SurvivesRotation);
            NestingTestHarness.Run(report, "O10. Ten don song sot qua phep LAT GUONG", O10_SurvivesMirror);
            NestingTestHarness.Run(report, "O11. Ten don song sot qua buoc dat (Placement)", O11_SurvivesPlacement);
            NestingTestHarness.Run(report, "O23. Chi tiet CHUA XEP van giu ten don", O23_UnplacedKeepsOrder);
            NestingTestHarness.Run(report, "O24. Nhan don tren to: day du, va rut gon khi qua dai", O24_SheetOrderLabel);
            NestingTestHarness.Run(report, "O25. Fixture .nest ghi/doc lai ten don nguyen ven", O25_FixtureRoundTrip);
            NestingTestHarness.Run(report, "O26. Chay mot don: xep hang giong het khi khong co don", O26_SingleOrderRanksIdentically);
        }

        // ==================================================================================
        // helpers
        // ==================================================================================

        private static PartGroup Rect(string id, double w, double h, int qty, string order)
        {
            return new PartGroup(id, new PartShape(PolyShape.Rectangle(w, h)), qty, "1.2MM", order);
        }

        private static void AssertValid(NestingResult res)
        {
            if (!res.Validation.IsValid)
            {
                throw new NestingAssertException("validator: " + res.Validation.Issues[0]);
            }
        }

        private static NestingResult Nest(NestingRequest r)
        {
            return new SimpleNestingEngine().Nest(r, CancellationToken.None, null);
        }

        /// <summary>Tong so don PHAI tron them tren moi to (mot to mot don = 0).</summary>
        private static int Mixing(NestingResult res)
        {
            int total = 0;
            foreach (SheetResult s in res.Sheets)
            {
                if (s.Orders.Count > 1) total += s.Orders.Count - 1;
            }

            return total;
        }

        private static int CountByOrder(NestingResult res, string order)
        {
            int n = 0;
            foreach (SheetResult s in res.Sheets)
            {
                foreach (Placement p in s.Placements)
                {
                    if (string.Equals(p.OrderName, order, StringComparison.Ordinal)) n++;
                }
            }

            return n;
        }

        // ==================================================================================
        // tests
        // ==================================================================================

        private static void O01_SingleOrder()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 300, 200, 4, "DON-A"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(4, res.Statistics.PlacedQuantity, "xep het");
            Equal(4, CountByOrder(res, "DON-A"), "moi placement deu mang ten don");

            foreach (SheetResult s in res.Sheets)
            {
                Equal(1, s.Orders.Count, "to chi co mot don");
                Equal("DON-A", s.Orders[0], "dung ten don");
            }
        }

        private static void O02_TwoOrders()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 300, 200, 3, "DON-A"));
            r.Groups.Add(Rect("B", 300, 200, 2, "DON-B"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(5, res.Statistics.PlacedQuantity, "xep het");
            Equal(3, CountByOrder(res, "DON-A"), "du so luong don A");
            Equal(2, CountByOrder(res, "DON-B"), "du so luong don B");
        }

        private static void O03_ThreeOrders()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 300, 200, 3, "DON-A"));
            r.Groups.Add(Rect("B", 250, 180, 4, "DON-B"));
            r.Groups.Add(Rect("C", 200, 150, 5, "DON-C"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(12, res.Statistics.PlacedQuantity, "xep het");
            Equal(3, CountByOrder(res, "DON-A"), "don A du");
            Equal(4, CountByOrder(res, "DON-B"), "don B du");
            Equal(5, CountByOrder(res, "DON-C"), "don C du");

            List<string> seen = new List<string>();
            foreach (SheetResult s in res.Sheets)
            {
                foreach (string o in s.Orders)
                {
                    if (!seen.Contains(o)) seen.Add(o);
                }
            }

            Equal(3, seen.Count, "ca ba don deu xuat hien tren ket qua");
        }

        private static void O04_SameShapeDifferentOrders()
        {
            // Hai nhom hinh GIONG HET nhau, chi khac don. De lan nhau thi khong con biet duong.
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("S1", 300, 200, 2, "DON-A"));
            r.Groups.Add(Rect("S2", 300, 200, 2, "DON-B"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(2, CountByOrder(res, "DON-A"), "dung 2 cai cua don A");
            Equal(2, CountByOrder(res, "DON-B"), "dung 2 cai cua don B");

            foreach (SheetResult s in res.Sheets)
            {
                foreach (Placement p in s.Placements)
                {
                    string expected = p.PartGroupId == "S1" ? "DON-A" : "DON-B";
                    Equal(expected, p.OrderName, "chi tiet " + p.PartGroupId + " phai thuoc " + expected);
                }
            }
        }

        private static void O05_MixedSheetOrders()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 600, 600, 6, "DON-A"));
            r.Groups.Add(Rect("B", 600, 600, 2, "DON-B"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(1, res.Sheets.Count, "tam 600x600: 8 cai vua dung mot to");

            SheetResult sheet = res.Sheets[0];
            Equal(2, sheet.Orders.Count, "to nay co dung hai don");
            Equal("DON-A", sheet.Orders[0], "danh sach don xep theo bang chu cai");
            Equal("DON-B", sheet.Orders[1], "danh sach don xep theo bang chu cai");
        }

        private static void O06_OrderNeverCostsASheet()
        {
            // Cung mot bo chi tiet, chay hai lan: mot lan CO ten don, mot lan KHONG.
            // So to va tong chieu dai phai y het nhau - gom don khong duoc lam ton them gi.
            NestingRequest withOrders = NestingCoreSelfTests.Request(2500, 1250);
            withOrders.Groups.Add(Rect("A", 600, 600, 6, "DON-A"));
            withOrders.Groups.Add(Rect("B", 600, 600, 5, "DON-B"));
            withOrders.Groups.Add(Rect("C", 400, 300, 7, "DON-C"));

            NestingRequest without = NestingCoreSelfTests.Request(2500, 1250);
            without.Groups.Add(Rect("A", 600, 600, 6, null));
            without.Groups.Add(Rect("B", 600, 600, 5, null));
            without.Groups.Add(Rect("C", 400, 300, 7, null));

            NestingResult a = Nest(withOrders), b = Nest(without);
            AssertValid(a);
            AssertValid(b);

            Equal(b.Statistics.PlacedQuantity, a.Statistics.PlacedQuantity, "so chi tiet da xep khong doi");
            True(a.Sheets.Count <= b.Sheets.Count, "co ten don khong duoc ton them to: "
                + a.Sheets.Count + " > " + b.Sheets.Count);

            double lenA = 0, lenB = 0;
            foreach (SheetResult s in a.Sheets) lenA += s.UsedLengthMm;
            foreach (SheetResult s in b.Sheets) lenB += s.UsedLengthMm;
            True(lenA <= lenB + 1e-6, "co ten don khong duoc ton them chieu dai: " + lenA + " > " + lenB);
        }

        private static void O07_MixWhenItSavesASheet()
        {
            // Don A 6 tam, don B 2 tam, moi to chua dung 8 tam. Tach ra la 2 to, tron lai 1 to.
            // Bat buoc phai tron - gon gang khong bao gio duoc phep dat hon vat lieu.
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 600, 600, 6, "DON-A"));
            r.Groups.Add(Rect("B", 600, 600, 2, "DON-B"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(1, res.Sheets.Count, "phai tron hai don de chi ton mot to");
            Equal(1, Mixing(res), "dung mot lan tron");
        }

        private static void O08_NoNeedlessMixing()
        {
            // Moi don vua dung mot to: khong co ly do gi de tron.
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 600, 600, 8, "DON-A"));
            r.Groups.Add(Rect("B", 600, 600, 8, "DON-B"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(2, res.Sheets.Count, "hai to");
            Equal(0, Mixing(res), "moi to chi mot don");
        }

        private static void O09_SurvivesRotation()
        {
            // Chi tiet 1200x200 tren to rong 1250: bat buoc phai xoay moi vua chieu dai to.
            NestingRequest r = NestingCoreSelfTests.Request(300, 1250);
            r.Groups.Add(Rect("A", 1200, 200, 1, "DON-XOAY"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(1, res.Statistics.PlacedQuantity, "xep duoc");

            Placement p = res.Sheets[0].Placements[0];
            True(p.RotationDeg != 0, "phai co xoay moi vua (goc = " + p.RotationDeg + ")");
            Equal("DON-XOAY", p.OrderName, "xoay xong van dung ten don");
        }

        private static void O10_SurvivesMirror()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Settings.AllowMirror = true;
            r.Groups.Add(new PartGroup("L", new PartShape(PolyShape.Create(
                new List<IntPoint>
                {
                    IntPoint.FromMm(0, 0), IntPoint.FromMm(300, 0), IntPoint.FromMm(300, 100),
                    IntPoint.FromMm(100, 100), IntPoint.FromMm(100, 300), IntPoint.FromMm(0, 300)
                },
                new List<IList<IntPoint>>())), 6, "1.2MM", "DON-GUONG"));

            NestingResult res = Nest(r);
            AssertValid(res);
            Equal(6, res.Statistics.PlacedQuantity, "xep het");

            foreach (SheetResult s in res.Sheets)
            {
                foreach (Placement p in s.Placements)
                {
                    Equal("DON-GUONG", p.OrderName, "lat guong xong van dung ten don");
                }
            }
        }

        private static void O11_SurvivesPlacement()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 300, 200, 5, "DON-A"));
            r.Groups.Add(Rect("B", 200, 150, 5, "DON-B"));

            NestingResult res = Nest(r);
            AssertValid(res);

            // Ten don tren TUNG placement phai khop voi ten don cua nhom - khong duoc lech.
            Dictionary<string, string> byGroup = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PartGroup g in r.Groups) byGroup[g.Id] = g.Order;

            int checkedCount = 0;
            foreach (SheetResult s in res.Sheets)
            {
                foreach (Placement p in s.Placements)
                {
                    Equal(byGroup[p.PartGroupId], p.OrderName, "placement " + p.InstanceId + " sai ten don");
                    checkedCount++;
                }
            }

            Equal(10, checkedCount, "phai kiem du 10 placement");
        }

        private static void O23_UnplacedKeepsOrder()
        {
            // Chi tiet to hon ca to phoi: khong bao gio xep duoc, nhung van phai biet la cua don nao.
            NestingRequest r = NestingCoreSelfTests.Request(500, 400);
            r.Groups.Add(Rect("OK", 200, 200, 1, "DON-A"));
            r.Groups.Add(Rect("QUALON", 900, 300, 2, "DON-B"));

            NestingResult res = Nest(r);
            True(res.Statistics.UnplacedQuantity >= 2, "hai cai qua kho phai bi tu choi");

            int seen = 0;
            foreach (UnplacedPart u in res.Unplaced)
            {
                if (u.PartGroupId != "QUALON") continue;
                Equal("DON-B", u.OrderName, "chi tiet chua xep phai giu ten don");
                seen++;
            }

            Equal(2, seen, "ca hai cai chua xep deu duoc bao kem ten don");
        }

        private static void O24_SheetOrderLabel()
        {
            SheetResult s = new SheetResult(0, "1.2MM", new SheetSpec("T", 2500, 1250));
            Equal(string.Empty, s.OrderLabel(2), "khong co don thi nhan rong");

            s.Orders.Add("DON-A");
            Equal("DON-A", s.OrderLabel(2), "mot don");

            s.Orders.Add("DON-B");
            Equal("DON-A + DON-B", s.OrderLabel(2), "hai don viet du");

            s.Orders.Add("DON-C");
            Equal("DON-A + DON-B + 1 don khac", s.OrderLabel(2), "qua dai thi rut gon");

            s.Orders.Add("DON-D");
            Equal("DON-A + DON-B + 2 don khac", s.OrderLabel(2), "dem dung so don con lai");

            // Rut gon la chuyen cua CAI NHAN; danh sach day du van con nguyen o day.
            Equal(4, s.Orders.Count, "danh sach day du khong bi cat bot");

            // Ten don dai khong lam hong gi - nhan chi la chuoi, khong co do rong cung.
            SheetResult longNames = new SheetResult(1, "1.2MM", new SheetSpec("T", 2500, 1250));
            string big = new string('X', 120);
            longNames.Orders.Add(big);
            Equal(big, longNames.OrderLabel(2), "ten dai van ra nguyen ven khi chi co mot don");
        }

        private static void O25_FixtureRoundTrip()
        {
            NestingRequest r = NestingCoreSelfTests.Request(2500, 1250);
            r.Groups.Add(Rect("A", 300, 200, 2, "DON-A"));
            r.Groups.Add(Rect("B", 200, 150, 3, "DON B CO KHOANG TRANG"));
            r.Groups.Add(Rect("C", 100, 100, 1, null));

            NestingFixture.Fixture back = NestingFixture.Parse(NestingFixture.Write(r, "test"));
            Equal(3, back.Request.Groups.Count, "du so nhom");
            Equal("DON-A", back.Request.Groups[0].Order, "ten don don gian");
            Equal("DON B CO KHOANG TRANG", back.Request.Groups[1].Order, "ten don co khoang trang");
            Equal(string.Empty, back.Request.Groups[2].Order, "khong co don thi van rong");
        }

        private static void O26_SingleOrderRanksIdentically()
        {
            // Chay mot don (hoac khong co don) thi hai khoa phu ve don phai IM LANG han:
            // bo xep hang tra ve 0 nhu truoc khi co tinh nang nay.
            LexicographicSolutionEvaluator e = new LexicographicSolutionEvaluator();

            DecodedLayout a = Layout("DON-A", 1000000, 500000);
            DecodedLayout b = Layout("DON-A", 1000000, 500000);
            Equal(0, e.Compare(a, b), "mot don: hoa thi van hoa");

            DecodedLayout none1 = Layout(null, 1000000, 500000);
            DecodedLayout none2 = Layout(null, 1000000, 500000);
            Equal(0, e.Compare(none1, none2), "khong don: hoa thi van hoa");
        }

        /// <summary>Mot to gia voi dung mot chi tiet, de thu bo xep hang.</summary>
        private static DecodedLayout Layout(string order, long maxX, long maxY)
        {
            PartGroup g = new PartGroup("G", new PartShape(PolyShape.Rectangle(100, 100)), 1, "1.2MM", order);
            PolygonCollisionModel collision = new PolygonCollisionModel(new NestingSettings());
            PreparedShape shape = collision.Prepare(g, new OrientationTransform(0, false));

            DecodedLayout layout = new DecodedLayout();
            DecodedSheet sheet = new DecodedSheet { MaxX = maxX, MaxY = maxY };
            sheet.Items.Add(new PlacedItem(
                new PartInstance("G#0", g, 0), shape, 0, 0, collision.Place(shape, 0, 0)));
            layout.Sheets.Add(sheet);
            return layout;
        }
    }
}
