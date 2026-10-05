using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    // ==========================================================================================
    // NFP - DA GIAC KHONG-VUA THAT (No-Fit Polygon) bang TONG MINKOWSKI
    // ------------------------------------------------------------------------------------------
    // Chi tiet B dat tai tinh tien t va chi tiet A (da dat tai f) cach nhau >= khe c
    //     <=>  t - f  KHONG nam trong  A (+) (-B) (+) Dia(c)       ((+) = tong Minkowski)
    //
    // Tong Minkowski chi de tinh voi DA GIAC LOI, nen moi chi tiet duoc chia thanh vai manh loi
    // (tam giac hoa roi gop - Hertel-Mehlhorn). NFP cua cap chi tiet = HOP cac NFP manh loi;
    // khong can tinh phep hop: mot vi tri hop le khi KHONG nam trong manh nao.
    //
    // Moi phep xap xi o day deu LAM NFP TO RA (an toan): dien cac hoc lom ma khong chi tiet nao
    // chui vao duoc, rut gon duong cong roi bu lai bang dung sai rut gon, dia khe ho thay bang
    // da giac BAO NGOAI hinh tron. Va ca vay, vi tri NFP de xuat VAN phai qua phep kiem va cham
    // chinh xac cua CandidatePointDecoder - NFP chi la bo SINH UNG VIEN, khong bao gio la trong tai.
    //
    // Lo kin cua chi tiet da dat coi nhu DAC (khong sinh ung vien trong lo); khi cho phep ghep
    // vao lo, bo giai ma van them ung vien trong lo theo cach cu.
    // ==========================================================================================
    internal sealed class NfpPiece
    {
        public NfpPiece(IntPoint[] points)
        {
            Points = points;
            Box = LongRect.FromPoints(points);
        }

        /// <summary>Da giac LOI, nguoc chieu kim dong ho, khong co dinh thang hang.</summary>
        public readonly IntPoint[] Points;

        public readonly LongRect Box;
    }

    internal static class NfpGeometry
    {
        /// <summary>Dung sai rut gon duong bao (0.25 mm) - bu lai vao ban kinh dia khe ho.</summary>
        internal const long SimplifyTolerance = 250;

        /// <summary>
        /// Hoc lom chi DANG GIU khi phan lot vao duoc (sau khi tru khe cat hai ben) rong VA sau
        /// hon 12 mm. Rang / ranh nho hon chi cho mot mau nho cua chi tiet khac chui vao - doi
        /// lai moi ranh sinh them vai manh loi va nhan so manh NFP len hang tram (ban ve that:
        /// 15-29 manh / chi tiet). Dien chung lai chi lam NFP TO RA - van an toan.
        /// </summary>
        internal const long UsefulPocket = 12000;

        /// <summary>So canh cua da giac bao ngoai hinh tron khe ho.</summary>
        private const int DiscSides = 12;

        /// <summary>Qua so dinh nay sau khi rut gon thi bo tam giac hoa, dung bao loi (van an toan).</summary>
        private const int MaxDecomposeVertices = 600;

        // ---------------------------------------------------------------- chia manh loi

        /// <summary>
        /// Chia vong NGOAI cua chi tiet thanh cac manh loi.
        ///
        /// <paramref name="gap"/>: khe cat. Hoc lom co MIENG khong qua 2 khe, hoac SAU khong qua
        /// 1 khe (vet giam ung suat, rang cua nho...) thi khong mot diem nao cua chi tiet khac lot
        /// vao duoc ma van giu du khe - hoc do bi DIEN DAY, bot hang chuc manh vun ma khong mat
        /// cho dat nao. Hoc lon hon (chu C, U, L) giu nguyen - do chinh la cho NFP phat huy.
        ///
        /// <paramref name="deviation"/>: sai lech LON NHAT thuc te do rut gon duong cong gay ra
        /// (canh thang = 0). Ben goi cong no vao ban kinh khe ho de NFP khong bao gio hut.
        /// </summary>
        public static List<IntPoint[]> ConvexPieces(IntPoint[] outer, long gap, out long deviation)
        {
            deviation = 0;
            IntPoint[] ring = GeometryMath.CleanRing(outer);
            if (ring.Length < 3) return new List<IntPoint[]>();
            if (GeometryMath.SignedArea(ring) < 0) Array.Reverse(ring);

            IntPoint[] hull = ConvexHull(ring);
            ring = FillNarrowPockets(ring, hull, 2 * gap + UsefulPocket, gap + UsefulPocket);
            ring = Simplify(ring, SimplifyTolerance, out deviation);
            ring = GeometryMath.CleanRing(ring);

            int ea, eb;
            if (ring.Length < 3 || ring.Length > MaxDecomposeVertices || GeometryMath.SignedArea(ring) <= 0 ||
                !GeometryMath.IsSimpleRing(ring, out ea, out eb))
            {
                deviation = 0;
                return new List<IntPoint[]> { hull };
            }

            if (IsConvex(ring)) return new List<IntPoint[]> { ring };

            List<List<int>> tris = Triangulate(ring);
            if (tris == null)
            {
                deviation = 0;
                return new List<IntPoint[]> { hull };
            }

            List<List<int>> merged = MergeConvex(ring, tris);
            List<IntPoint[]> pieces = new List<IntPoint[]>(merged.Count);
            foreach (List<int> p in merged)
            {
                IntPoint[] pts = new IntPoint[p.Count];
                for (int i = 0; i < p.Count; i++) pts[i] = ring[p[i]];
                pts = RemoveCollinear(pts);
                if (pts.Length >= 3) pieces.Add(pts);
            }

            return pieces.Count > 0 ? pieces : new List<IntPoint[]> { hull };
        }

        /// <summary>Andrew monotone chain. Ket qua nguoc chieu kim dong ho, khong dinh thang hang.</summary>
        public static IntPoint[] ConvexHull(IList<IntPoint> points)
        {
            List<IntPoint> p = new List<IntPoint>(points);
            p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            if (p.Count < 3) return p.ToArray();

            IntPoint[] h = new IntPoint[2 * p.Count];
            int k = 0;
            for (int i = 0; i < p.Count; i++)
            {
                while (k >= 2 && GeometryMath.Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
                h[k++] = p[i];
            }

            for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && GeometryMath.Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
                h[k++] = p[i];
            }

            IntPoint[] r = new IntPoint[Math.Max(0, k - 1)];
            Array.Copy(h, r, r.Length);
            return r;
        }

        /// <summary>
        /// Dien cac hoc lom (phan giua bao loi va duong bao) co MIENG hep hon
        /// <paramref name="minOpening"/>. Duong bao chi TO RA - an toan cho NFP.
        /// </summary>
        private static IntPoint[] FillNarrowPockets(IntPoint[] ring, IntPoint[] hull, long minOpening, long minDepth)
        {
            if (hull.Length < 3 || minOpening <= 0) return ring;

            Dictionary<IntPoint, int> index = new Dictionary<IntPoint, int>();
            for (int i = 0; i < ring.Length; i++)
            {
                if (!index.ContainsKey(ring[i])) index[ring[i]] = i;
            }

            List<int> hullIdx = new List<int>();
            foreach (IntPoint h in hull)
            {
                int i;
                if (!index.TryGetValue(h, out i)) return ring;
                hullIdx.Add(i);
            }

            // Bao loi va duong bao cung chieu: thu tu cac dinh bao loi tren duong bao phai tang
            // dan (vong tron). Khong dung thi khong dong vao.
            int start = hullIdx.IndexOf(MinOf(hullIdx));
            List<int> ordered = new List<int>();
            for (int k = 0; k < hullIdx.Count; k++) ordered.Add(hullIdx[(start + k) % hullIdx.Count]);
            for (int k = 1; k < ordered.Count; k++)
            {
                if (ordered[k] <= ordered[k - 1]) return ring;
            }

            double open2 = (double)minOpening * minOpening;
            List<IntPoint> result = new List<IntPoint>(ring.Length);
            for (int k = 0; k < ordered.Count; k++)
            {
                int a = ordered[k], b = k + 1 < ordered.Count ? ordered[k + 1] : ordered[0] + ring.Length;
                result.Add(ring[a]);
                if (b - a <= 1) continue;

                IntPoint pa = ring[a], pb = ring[b % ring.Length];
                double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                if (dx * dx + dy * dy <= open2) continue;          // mieng hep: dien day

                double depth2 = 0;
                for (int i = a + 1; i < b; i++)
                {
                    IntPoint q = ring[i % ring.Length];
                    depth2 = Math.Max(depth2, GeometryMath.PointSegmentDistanceSquared(q.X, q.Y, pa.X, pa.Y, pb.X, pb.Y));
                }

                if (depth2 <= (double)minDepth * minDepth) continue;   // nong: dien day

                for (int i = a + 1; i < b; i++) result.Add(ring[i % ring.Length]);
            }

            return result.Count >= 3 ? result.ToArray() : ring;
        }

        private static int MinOf(List<int> list)
        {
            int m = int.MaxValue;
            foreach (int v in list) m = Math.Min(m, v);
            return m;
        }

        /// <summary>Douglas-Peucker cho vong kin: moi dinh bo di cach doan thay the khong qua <paramref name="tol"/>.</summary>
        private static IntPoint[] Simplify(IntPoint[] ring, long tol, out long deviation)
        {
            deviation = 0;
            int n = ring.Length;
            if (n <= 4 || tol <= 0) return ring;

            int far = 0;
            double best = -1;
            for (int i = 1; i < n; i++)
            {
                double dx = ring[i].X - ring[0].X, dy = ring[i].Y - ring[0].Y;
                double d = dx * dx + dy * dy;
                if (d > best)
                {
                    best = d;
                    far = i;
                }
            }

            bool[] keep = new bool[n];
            keep[0] = keep[far] = true;
            double tol2 = (double)tol * tol;
            Mark(ring, 0, far, tol2, keep);
            Mark(ring, far, n, tol2, keep);

            List<int> kept = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (keep[i]) kept.Add(i);
            }

            if (kept.Count < 3) return ring;

            // Sai lech THUC TE: moi dinh bi bo cach doan thay the no bao xa.
            double worst = 0;
            for (int k = 0; k < kept.Count; k++)
            {
                int a = kept[k], b = k + 1 < kept.Count ? kept[k + 1] : kept[0] + n;
                IntPoint pa = ring[a], pb = ring[b % n];
                for (int i = a + 1; i < b; i++)
                {
                    IntPoint q = ring[i % n];
                    worst = Math.Max(worst, GeometryMath.PointSegmentDistanceSquared(q.X, q.Y, pa.X, pa.Y, pb.X, pb.Y));
                }
            }

            deviation = (long)Math.Ceiling(Math.Sqrt(worst));
            IntPoint[] r = new IntPoint[kept.Count];
            for (int k = 0; k < kept.Count; k++) r[k] = ring[kept[k]];
            return r;
        }

        private static void Mark(IntPoint[] ring, int a, int b, double tol2, bool[] keep)
        {
            int n = ring.Length;
            Stack<int[]> work = new Stack<int[]>();
            work.Push(new[] { a, b });
            while (work.Count > 0)
            {
                int[] s = work.Pop();
                if (s[1] - s[0] < 2) continue;
                IntPoint pa = ring[s[0] % n], pb = ring[s[1] % n];
                int idx = -1;
                double best = tol2;
                for (int i = s[0] + 1; i < s[1]; i++)
                {
                    IntPoint p = ring[i % n];
                    double d = GeometryMath.PointSegmentDistanceSquared(p.X, p.Y, pa.X, pa.Y, pb.X, pb.Y);
                    if (d > best)
                    {
                        best = d;
                        idx = i;
                    }
                }

                if (idx < 0) continue;
                keep[idx % n] = true;
                work.Push(new[] { s[0], idx });
                work.Push(new[] { idx, s[1] });
            }
        }

        private static bool IsConvex(IntPoint[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                if (GeometryMath.Cross(ring[i], ring[(i + 1) % ring.Length], ring[(i + 2) % ring.Length]) < 0) return false;
            }

            return true;
        }

        /// <summary>Tam giac hoa cat tai (ear clipping) tren vong CCW don. Null khi ket (so hoc suy bien).</summary>
        private static List<List<int>> Triangulate(IntPoint[] ring)
        {
            List<int> v = new List<int>(ring.Length);
            for (int i = 0; i < ring.Length; i++) v.Add(i);
            List<List<int>> tris = new List<List<int>>();

            int guard = 0;
            while (v.Count > 3)
            {
                if (++guard > 4 * ring.Length * ring.Length) return null;
                bool found = false;
                for (int k = 0; k < v.Count; k++)
                {
                    int ip = v[(k + v.Count - 1) % v.Count], ic = v[k], inx = v[(k + 1) % v.Count];
                    IntPoint a = ring[ip], b = ring[ic], c = ring[inx];
                    long cr = GeometryMath.Cross(a, b, c);
                    if (cr < 0) continue;
                    if (cr == 0)
                    {
                        // Dinh thang hang: bo di khong doi hinh.
                        v.RemoveAt(k);
                        found = true;
                        break;
                    }

                    bool ear = true;
                    foreach (int iq in v)
                    {
                        if (iq == ip || iq == ic || iq == inx) continue;
                        IntPoint q = ring[iq];
                        if (q.Equals(a) || q.Equals(b) || q.Equals(c)) continue;
                        if (GeometryMath.Cross(a, b, q) >= 0 && GeometryMath.Cross(b, c, q) >= 0 && GeometryMath.Cross(c, a, q) >= 0)
                        {
                            ear = false;
                            break;
                        }
                    }

                    if (!ear) continue;
                    tris.Add(new List<int> { ip, ic, inx });
                    v.RemoveAt(k);
                    found = true;
                    break;
                }

                if (!found) return null;
            }

            if (v.Count == 3 && GeometryMath.Cross(ring[v[0]], ring[v[1]], ring[v[2]]) > 0) tris.Add(new List<int>(v));
            return tris;
        }

        /// <summary>Hertel-Mehlhorn: gop hai manh chung canh neu manh moi van loi.</summary>
        private static List<List<int>> MergeConvex(IntPoint[] ring, List<List<int>> pieces)
        {
            List<List<int>> list = new List<List<int>>(pieces);
            bool merged = true;
            while (merged)
            {
                merged = false;
                Dictionary<long, int> edgeOwner = new Dictionary<long, int>();
                for (int p = 0; p < list.Count && !merged; p++)
                {
                    List<int> poly = list[p];
                    for (int k = 0; k < poly.Count && !merged; k++)
                    {
                        int u = poly[k], w = poly[(k + 1) % poly.Count];
                        int other;
                        if (edgeOwner.TryGetValue(Key(w, u), out other))
                        {
                            List<int> m = Join(list[other], poly, u, w);
                            if (m != null && IsConvexIdx(ring, m))
                            {
                                list[other] = m;
                                list.RemoveAt(p);
                                merged = true;
                            }
                        }

                        edgeOwner[Key(u, w)] = p;
                    }
                }
            }

            return list;
        }

        private static long Key(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }

        /// <summary>X co canh (w, u), Y co canh (u, w): noi lai thanh mot vong (bo canh chung).</summary>
        private static List<int> Join(List<int> x, List<int> y, int u, int w)
        {
            int xi = -1, yi = -1;
            for (int i = 0; i < x.Count; i++)
            {
                if (x[i] == w && x[(i + 1) % x.Count] == u) xi = i;
            }

            for (int i = 0; i < y.Count; i++)
            {
                if (y[i] == u && y[(i + 1) % y.Count] == w) yi = i;
            }

            if (xi < 0 || yi < 0) return null;
            List<int> r = new List<int>(x.Count + y.Count - 2);

            // x: bat dau tu u, di het vong toi w.
            for (int k = 0; k < x.Count; k++) r.Add(x[(xi + 1 + k) % x.Count]);

            // y: tu sau w toi truoc u.
            for (int k = 2; k < y.Count; k++) r.Add(y[(yi + k) % y.Count]);
            return r;
        }

        private static bool IsConvexIdx(IntPoint[] ring, List<int> poly)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                if (GeometryMath.Cross(ring[poly[i]], ring[poly[(i + 1) % n]], ring[poly[(i + 2) % n]]) < 0) return false;
            }

            return true;
        }

        private static IntPoint[] RemoveCollinear(IntPoint[] pts)
        {
            List<IntPoint> r = new List<IntPoint>(pts);
            bool changed = true;
            while (changed && r.Count > 3)
            {
                changed = false;
                for (int i = 0; i < r.Count && r.Count > 3; i++)
                {
                    if (GeometryMath.Cross(r[(i + r.Count - 1) % r.Count], r[i], r[(i + 1) % r.Count]) == 0)
                    {
                        r.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }
            }

            return r.ToArray();
        }

        // ---------------------------------------------------------------- tong Minkowski

        /// <summary>Tong Minkowski hai da giac LOI CCW (thuat toan tron canh, O(n + m)).</summary>
        public static IntPoint[] MinkowskiConvex(IntPoint[] a, IntPoint[] b)
        {
            int na = a.Length, nb = b.Length;
            int ia = Lowest(a), ib = Lowest(b);
            List<IntPoint> r = new List<IntPoint>(na + nb);
            int i = 0, j = 0;
            while (i < na || j < nb)
            {
                IntPoint pa = a[(ia + i) % na], pb = b[(ib + j) % nb];
                r.Add(new IntPoint(pa.X + pb.X, pa.Y + pb.Y));
                if (i == na)
                {
                    j++;
                    continue;
                }

                if (j == nb)
                {
                    i++;
                    continue;
                }

                IntPoint qa = a[(ia + i + 1) % na], qb = b[(ib + j + 1) % nb];
                long ex = qa.X - pa.X, ey = qa.Y - pa.Y, fx = qb.X - pb.X, fy = qb.Y - pb.Y;
                double cross = (double)ex * fy - (double)ey * fx;
                if (cross > 0) i++;
                else if (cross < 0) j++;
                else
                {
                    i++;
                    j++;
                }
            }

            return RemoveCollinear(GeometryMath.CleanRing(r));
        }

        private static int Lowest(IntPoint[] p)
        {
            int best = 0;
            for (int i = 1; i < p.Length; i++)
            {
                if (p[i].Y < p[best].Y || (p[i].Y == p[best].Y && p[i].X < p[best].X)) best = i;
            }

            return best;
        }

        /// <summary>Da giac deu BAO NGOAI hinh tron ban kinh r (moi diem cua hinh tron nam trong).</summary>
        public static IntPoint[] Disc(long r)
        {
            // Canh doi dien 4 huong truc nam DUNG tai r (khit tuyet doi theo truc - luoi chu nhat
            // sat khe, tay chu U vua khit hoc). Lam tron dinh co the hut <= 0.5 don vi o huong xien:
            // phep kiem va cham chinh xac phia sau loai cac diem do.
            double R = r / Math.Cos(Math.PI / DiscSides);
            IntPoint[] d = new IntPoint[DiscSides];
            for (int k = 0; k < DiscSides; k++)
            {
                double ang = 2 * Math.PI * (k + 0.5) / DiscSides;
                d[k] = new IntPoint((long)Math.Round(R * Math.Cos(ang)), (long)Math.Round(R * Math.Sin(ang)));
            }

            return d;
        }

        /// <summary>Diem nam HAN trong da giac loi CCW (tren bien = khong).</summary>
        public static bool StrictlyInside(NfpPiece piece, long x, long y)
        {
            if (x <= piece.Box.MinX || x >= piece.Box.MaxX || y <= piece.Box.MinY || y >= piece.Box.MaxY) return false;
            IntPoint[] p = piece.Points;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            {
                double cross = (double)(p[i].X - p[j].X) * (y - p[j].Y) - (double)(p[i].Y - p[j].Y) * (x - p[j].X);
                if (cross <= 0) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// NFP da tinh, theo cap (hinh da dat, hinh dang xep, khe ho). NFP chi phu thuoc HINH, khong
    /// phu thuoc vi tri, nen moi cap chi tinh MOT lan cho ca lan ghep (moi thu tu xep, moi luong).
    /// </summary>
    internal sealed class NfpCache
    {
        private static readonly ConditionalWeakTable<MaterialJob, NfpCache> PerJob = new ConditionalWeakTable<MaterialJob, NfpCache>();

        private readonly ConcurrentDictionary<Tuple<PreparedShape, PreparedShape, long>, NfpPiece[]> _pairs =
            new ConcurrentDictionary<Tuple<PreparedShape, PreparedShape, long>, NfpPiece[]>();

        private readonly ConditionalWeakTable<PreparedShape, Decomposed> _pieces = new ConditionalWeakTable<PreparedShape, Decomposed>();

        private readonly long _gap;

        private sealed class Decomposed
        {
            public List<IntPoint[]> Pieces;
            public long Deviation;
        }

        private NfpCache(MaterialJob job)
        {
            _gap = job.Collision.Rules.PartClearance(0, 0);
        }

        public static NfpCache For(MaterialJob job)
        {
            return PerJob.GetValue(job, j => new NfpCache(j));
        }

        private Decomposed PiecesOf(PreparedShape shape)
        {
            return _pieces.GetValue(shape, s =>
            {
                long dev;
                List<IntPoint[]> p = NfpGeometry.ConvexPieces(s.Shape.Outer, _gap, out dev);
                return new Decomposed { Pieces = p, Deviation = dev };
            });
        }

        /// <summary>
        /// Cac manh NFP (toa do tuong doi: vi tri chi tiet DANG XEP tru vi tri chi tiet DA DAT).
        /// </summary>
        public NfpPiece[] Pair(PreparedShape placed, PreparedShape moving, long clearance)
        {
            return _pairs.GetOrAdd(Tuple.Create(placed, moving, clearance), k => Build(k.Item1, k.Item2, k.Item3));
        }

        private NfpPiece[] Build(PreparedShape placed, PreparedShape moving, long clearance)
        {
            Decomposed da = PiecesOf(placed), db = PiecesOf(moving);
            List<IntPoint[]> a = da.Pieces, b = db.Pieces;

            // Bu dung sai lech rut gon THUC TE cua hai hinh (canh thang = 0) - vi tri cham khit
            // tuyet doi (luoi chu nhat sat khe) van giu duoc.
            IntPoint[] disc = NfpGeometry.Disc(clearance + da.Deviation + db.Deviation);
            List<NfpPiece> result = new List<NfpPiece>(a.Count * b.Count);
            foreach (IntPoint[] pa in a)
            {
                IntPoint[] ad = NfpGeometry.MinkowskiConvex(pa, disc);
                foreach (IntPoint[] pb in b)
                {
                    IntPoint[] neg = new IntPoint[pb.Length];
                    for (int i = 0; i < pb.Length; i++) neg[i] = new IntPoint(-pb[i].X, -pb[i].Y);
                    IntPoint[] sum = NfpGeometry.MinkowskiConvex(ad, neg);
                    if (sum.Length >= 3) result.Add(new NfpPiece(sum));
                }
            }

            return result.ToArray();
        }
    }
}
