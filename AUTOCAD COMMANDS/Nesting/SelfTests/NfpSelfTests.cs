using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using AUTOCAD_COMMANDS.Nesting.Core;
using static AUTOCAD_COMMANDS.Nesting.SelfTests.NestingTestHarness;

namespace AUTOCAD_COMMANDS.Nesting.SelfTests
{
    /// <summary>
    /// NFP da giac that (tuy chon "NFP" / "So sanh"): hinh hoc (chia manh loi, tong Minkowski,
    /// dia khe ho), an toan (moi ket qua qua validator, du SL, tai lap) va so sanh hai thuat toan.
    /// </summary>
    public static class NfpSelfTests
    {
        public static void Run(NestingTestReport report, string fixtureFolder)
        {
            NestingTestHarness.Run(report, "N01. Chia manh loi: chu U -> 3 manh loi, tong dien tich dung bang", N01_ConvexPiecesOfU);
            NestingTestHarness.Run(report, "N02. Ranh nho (khong gi lot vao) bi dien; hoc lon giu nguyen", N02_PocketFilling);
            NestingTestHarness.Run(report, "N03. Tong Minkowski hai hinh vuong = hinh vuong tong canh", N03_MinkowskiSquares);
            NestingTestHarness.Run(report, "N04. Da giac khe ho BAO NGOAI hinh tron, khit tuyet doi theo truc", N04_DiscCoversCircle);
            NestingTestHarness.Run(report, "N05. NFP tren fixture kho (khit tuyet doi, hoc lom, lo kin): hop le + dung SL", () => N05_NfpFixtures(fixtureFolder));
            NestingTestHarness.Run(report, "N06. NFP fuzz co seed (40 bai L / U / T / tam giac): validator + du SL", N06_NfpFuzz);
            NestingTestHarness.Run(report, "N07. NFP tai lap (song song, cache dung chung)", N07_NfpDeterministic);
            NestingTestHarness.Run(report, "N08. So sanh ket qua: validator > chua xep > so to > chieu dai", N08_ResultComparer);
        }

        private static IntPoint[] Ring(params double[] xy)
        {
            IntPoint[] r = new IntPoint[xy.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = IntPoint.FromMm(xy[2 * i], xy[2 * i + 1]);
            return r;
        }

        private static void N01_ConvexPiecesOfU()
        {
            IntPoint[] u = Ring(0, 0, 310, 0, 310, 300, 210, 300, 210, 100, 100, 100, 100, 300, 0, 300);
            long dev;
            List<IntPoint[]> pieces = NfpGeometry.ConvexPieces(u, 5000, out dev);
            Equal(3, pieces.Count, "chu U = 3 manh");
            Equal(0L, dev, "canh thang: khong sai lech");
            double area = 0;
            foreach (IntPoint[] p in pieces)
            {
                area += GeometryMath.SignedArea(p);
                for (int i = 0; i < p.Length; i++)
                {
                    True(GeometryMath.Cross(p[i], p[(i + 1) % p.Length], p[(i + 2) % p.Length]) > 0, "manh loi, CCW");
                }
            }

            Close(GeometryMath.SignedArea(u), area, 1.0, "tong dien tich cac manh = dien tich chu U");
        }

        private static void N02_PocketFilling()
        {
            long dev;
            // Ranh 6 x 4 mm (khe 5 mm: khong gi lot vao) -> dien day -> 1 manh.
            IntPoint[] notch = Ring(0, 0, 200, 0, 200, 100, 103, 100, 103, 96, 97, 96, 97, 100, 0, 100);
            Equal(1, NfpGeometry.ConvexPieces(notch, 5000, out dev).Count, "ranh nho bi dien");

            // Hoc 80 x 60 mm -> giu nguyen -> nhieu manh.
            IntPoint[] pocket = Ring(0, 0, 200, 0, 200, 100, 140, 100, 140, 40, 60, 40, 60, 100, 0, 100);
            True(NfpGeometry.ConvexPieces(pocket, 5000, out dev).Count > 1, "hoc lon giu nguyen");
        }

        private static void N03_MinkowskiSquares()
        {
            IntPoint[] a = Ring(0, 0, 10, 0, 10, 10, 0, 10), b = Ring(0, 0, 20, 0, 20, 20, 0, 20);
            IntPoint[] s = NfpGeometry.MinkowskiConvex(a, b);
            Equal(4, s.Length, "4 dinh");
            Close(900.0 * 1e6, GeometryMath.SignedArea(s), 1.0, "dien tich 30 x 30");
            LongRect box = LongRect.FromPoints(s);
            Equal(0L, box.MinX, "minX");
            Equal(30000L, box.MaxX, "maxX");
        }

        private static void N04_DiscCoversCircle()
        {
            long r = 5000;
            IntPoint[] d = NfpGeometry.Disc(r);
            NfpPiece piece = new NfpPiece(d);
            for (int k = 0; k < 720; k++)
            {
                double ang = Math.PI * k / 360.0;
                long x = (long)Math.Round((r - 1) * Math.Cos(ang)), y = (long)Math.Round((r - 1) * Math.Sin(ang));
                True(NfpGeometry.StrictlyInside(piece, x, y), "diem tren hinh tron nam trong da giac (goc " + k + ")");
            }

            LongRect box = LongRect.FromPoints(d);
            True(box.MaxX <= r + 1 && box.MinX >= -r - 1 && box.MaxY <= r + 1 && box.MinY >= -r - 1, "theo truc: dung ban kinh (khit tuyet doi)");
        }

        private static readonly string[] HardFixtures =
        {
            "05_rectangle_exact_edge_margin.nest", "07_l_notch_forced.nest", "08_u_pocket_forced.nest",
            "09_c_mouth_forced_no_rotation.nest", "13_closed_hole_disallowed.nest", "14_closed_hole_allowed.nest",
            "15_rotation_required.nest", "18_exact_gap_and_margin_grid.nest", "19_gap_plus_one_unit_too_big.nest",
            "quality_u_pair_01.nest", "quality_c_pair_01.nest", "quality_zigzag_pair_01.nest",
            "quality_arc_u_01.nest", "quality_stair_pair_01.nest"
        };

        private static void N05_NfpFixtures(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) throw new NestingAssertException("khong co thu muc fixture");
            foreach (string name in HardFixtures)
            {
                NestingFixture.Fixture fx = NestingFixture.Load(Path.Combine(folder, name));
                fx.Request.Settings.Algorithm = NestingAlgorithm.Nfp;
                NestingResult res = new SimpleNestingEngine().Nest(fx.Request, CancellationToken.None, null);
                True(res.Validation.IsValid, name + ": validator");

                foreach (KeyValuePair<string, string> e in fx.Expectations)
                {
                    double v = double.Parse(e.Value, CultureInfo.InvariantCulture);
                    switch (e.Key.ToLowerInvariant())
                    {
                        case "placed=": Equal((int)v, res.Statistics.PlacedQuantity, name + ": placed"); break;
                        case "unplaced=": Equal((int)v, res.Statistics.UnplacedQuantity, name + ": unplaced"); break;
                        case "sheets=": Equal((int)v, res.Sheets.Count, name + ": sheets"); break;
                        case "sheets<=": True(res.Sheets.Count <= v, name + ": sheets " + res.Sheets.Count); break;
                        case "mingap>=": True(res.Validation.MinPartDistanceMm >= v - 1e-9, name + ": mingap"); break;
                        case "minedge>=": True(res.Validation.MinEdgeDistanceMm >= v - 1e-9, name + ": minedge"); break;
                    }
                }

                // Diem manh cua NFP: chu U + luoi liem long vao nhau (diem ung vien: 652 mm).
                if (name == "quality_arc_u_01.nest")
                {
                    double len = NestingResultComparer.TotalUsedLengthMm(res);
                    True(len <= 595, "NFP long khit hinh cong: " + len.ToString("0.0", CultureInfo.InvariantCulture) + " mm > 595");
                }
            }
        }

        private static NestingRequest RandomRequest(int seed)
        {
            Random r = new Random(seed);
            NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("K", 1200, 600), Settings = new NestingSettings() };
            req.Settings.Algorithm = NestingAlgorithm.Nfp;
            req.Settings.ExtraSeededOrderings = 0;
            req.Settings.GapMm = r.Next(2) == 0 ? 5.0 : 3.0;
            req.Settings.AllowPartInsideHole = r.Next(3) == 0;
            int groups = 3 + r.Next(5);
            for (int i = 0; i < groups; i++)
            {
                double w = 40 + r.Next(300), h = 30 + r.Next(250);
                double a = 0.3 + 0.4 * r.NextDouble(), b = 0.3 + 0.4 * r.NextDouble();
                IntPoint[] o;
                switch (r.Next(4))
                {
                    case 0: o = Ring(0, 0, w, 0, w, h * a, w * b, h * a, w * b, h, 0, h); break;
                    case 1: o = Ring(0, 0, w, 0, w, h, w * (1 - b * 0.5), h, w * (1 - b * 0.5), h * a, w * b * 0.5, h * a, w * b * 0.5, h, 0, h); break;
                    case 2: o = Ring(0, 0, w, 0, w * b, h); break;
                    default: o = Ring(0, 0, w, 0, w, h, 0, h); break;
                }

                List<IList<IntPoint>> holes = new List<IList<IntPoint>>();
                if (r.Next(4) == 0 && w > 120 && h > 120) holes.Add(Ring(20, 20, 20, h * 0.25, w * 0.25, h * 0.25, w * 0.25, 20));
                PartShape shape = new PartShape(PolyShape.Create(o, holes), r.Next(3) == 0 ? 0.05 : 0.0);
                req.Groups.Add(new PartGroup("G" + i.ToString(CultureInfo.InvariantCulture), shape, 1 + r.Next(4), "1.2MM"));
            }

            return req;
        }

        private static void N06_NfpFuzz()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                NestingRequest req = RandomRequest(seed);
                NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                True(res.Validation.IsValid, "seed " + seed + ": validator " + (res.Validation.IsValid ? string.Empty : res.Validation.Issues[0].ToString()));
                int requested = 0;
                foreach (PartGroup g in req.Groups) requested += g.Quantity;
                Equal(requested, res.Statistics.PlacedQuantity + res.Statistics.UnplacedQuantity, "seed " + seed + ": du SL");
            }
        }

        private static void N07_NfpDeterministic()
        {
            for (int seed = 41; seed <= 45; seed++)
            {
                List<Placement> a = new List<Placement>(new SimpleNestingEngine().Nest(RandomRequest(seed), CancellationToken.None, null).Placements);
                List<Placement> b = new List<Placement>(new SimpleNestingEngine().Nest(RandomRequest(seed), CancellationToken.None, null).Placements);
                Equal(a.Count, b.Count, "seed " + seed + ": so chi tiet");
                for (int i = 0; i < a.Count; i++)
                {
                    True(a[i].TranslationX == b[i].TranslationX && a[i].TranslationY == b[i].TranslationY &&
                         a[i].RotationDeg == b[i].RotationDeg, "seed " + seed + ": cung vi tri #" + i);
                }
            }
        }

        private static void N08_ResultComparer()
        {
            NestingRequest req = RandomRequest(7);
            req.Settings.Algorithm = NestingAlgorithm.CandidatePoints;
            NestingRequest copy = NestingResultComparer.WithAlgorithm(req, NestingAlgorithm.Nfp);
            Equal(NestingAlgorithm.CandidatePoints, req.Settings.Algorithm, "ban goc khong bi doi");
            Equal(NestingAlgorithm.Nfp, copy.Settings.Algorithm, "ban sao dung thuat toan");
            Equal(req.Groups.Count, copy.Groups.Count, "cung nhom chi tiet");

            NestingResult a = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
            Equal(0, NestingResultComparer.Compare(a, a), "chinh no = ngang nhau");

            // Bot mot to (gia lap) -> it to hon thi thang.
            NestingResult fewer = new NestingResult();
            fewer.Validation = a.Validation;
            fewer.Statistics.SheetCount = Math.Max(0, a.Statistics.SheetCount - 1);
            fewer.Statistics.UnplacedQuantity = a.Statistics.UnplacedQuantity;
            True(NestingResultComparer.Compare(fewer, a) < 0, "it to hon thang");

            NestingResult unplaced = new NestingResult();
            unplaced.Validation = a.Validation;
            unplaced.Statistics.UnplacedQuantity = a.Statistics.UnplacedQuantity + 1;
            True(NestingResultComparer.Compare(a, unplaced) < 0, "it chua xep hon thang (du nhieu to hon)");

            NestingResult invalid = new NestingResult();
            invalid.Validation = new ValidationResult();
            invalid.Validation.Issues.Add(new ValidationIssue(ValidationIssueKind.QuantityMismatch, "gia lap"));
            True(NestingResultComparer.Compare(a, invalid) < 0, "qua validator luon thang");
        }
    }
}
