using System;
using System.Collections.Generic;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    /// <summary>
    /// DUONG BAO NGOAI CUNG cua mot cum net bat ky (cham nhau, cat nhau, chong len nhau,
    /// nhieu net gap nhau mot diem...).
    ///
    /// Cach lam: cat moi doan tai moi diem giao voi doan khac -> duoc mot do thi phang; roi di
    /// vong theo MAT NGOAI CUNG cua do thi (luon re sat phia ngoai). Vung ben ngoai duong tim
    /// duoc chinh la vung nam ngoai MOI net, nen:
    ///   - lam duong bao chi tiet: moi net cua cum deu nam trong -> khong bao gio de len chi
    ///     tiet khac khi ghep;
    ///   - lam duong bao LO: moi mat kin ben trong deu bi cat roi ra -> ca vung la khoang trong.
    ///
    /// <see cref="Of"/> chi tra ve ket qua khi duong tim duoc la mot vong don (khong tu cham) va
    /// chua het moi dinh dau vao; khong thi tra ve null. <see cref="ConvexHull"/> la phuong an
    /// du phong (luon dung cho duong bao chi tiet, nhung ton vat lieu hon).
    /// </summary>
    internal static class OuterBoundary
    {
        private struct Seg
        {
            public IntPoint A;
            public IntPoint B;
            public long MinX, MaxX, MinY, MaxY;
        }

        /// <summary>
        /// <paramref name="paths"/>: cac day diem (toa do the gioi); <paramref name="closed"/>[i]
        /// cho biet day thu i co khep kin (noi diem cuoi ve diem dau) hay khong.
        /// </summary>
        public static IntPoint[] Of(IList<IList<IntPoint>> paths, IList<bool> closed)
        {
            IntPoint origin;
            List<IntPoint[]> local = ToLocal(paths, out origin);

            List<Seg> segs = new List<Seg>();
            for (int k = 0; k < local.Count; k++)
            {
                IntPoint[] p = local[k];
                int n = p.Length;
                int last = closed[k] ? n : n - 1;
                for (int i = 0; i < last; i++)
                {
                    IntPoint a = p[i], b = p[(i + 1) % n];
                    if (a.Equals(b)) continue;
                    segs.Add(new Seg
                    {
                        A = a, B = b,
                        MinX = Math.Min(a.X, b.X), MaxX = Math.Max(a.X, b.X),
                        MinY = Math.Min(a.Y, b.Y), MaxY = Math.Max(a.Y, b.Y)
                    });
                }
            }

            if (segs.Count < 3) return null;

            // ---- 1. diem cat tren tung doan ----
            List<IntPoint>[] cuts = new List<IntPoint>[segs.Count];
            for (int i = 0; i < segs.Count; i++) cuts[i] = new List<IntPoint> { segs[i].A, segs[i].B };

            int[] order = new int[segs.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) => segs[x].MinX.CompareTo(segs[y].MinX));

            for (int oi = 0; oi < order.Length; oi++)
            {
                Seg s = segs[order[oi]];
                for (int oj = oi + 1; oj < order.Length; oj++)
                {
                    Seg t = segs[order[oj]];
                    if (t.MinX > s.MaxX) break;
                    if (t.MaxY < s.MinY || t.MinY > s.MaxY) continue;
                    if (!GeometryMath.SegmentsIntersect(s.A, s.B, t.A, t.B)) continue;

                    if (GeometryMath.Cross(s.A, s.B, t.A) == 0 && GeometryMath.Cross(s.A, s.B, t.B) == 0)
                    {
                        // Trung nhau mot khuc: dau mut cua doan nay nam tren doan kia thi la diem cat.
                        AddIfOn(cuts[order[oi]], s, t.A);
                        AddIfOn(cuts[order[oi]], s, t.B);
                        AddIfOn(cuts[order[oj]], t, s.A);
                        AddIfOn(cuts[order[oj]], t, s.B);
                    }
                    else
                    {
                        IntPoint x = GeometryMath.SegmentCrossPoint(s.A, s.B, t.A, t.B);
                        cuts[order[oi]].Add(x);
                        cuts[order[oj]].Add(x);
                    }
                }
            }

            // ---- 2. do thi ----
            Dictionary<IntPoint, int> ids = new Dictionary<IntPoint, int>();
            List<IntPoint> verts = new List<IntPoint>();
            List<HashSet<int>> adj = new List<HashSet<int>>();
            Func<IntPoint, int> id = p =>
            {
                int v;
                if (ids.TryGetValue(p, out v)) return v;
                v = verts.Count;
                ids[p] = v;
                verts.Add(p);
                adj.Add(new HashSet<int>());
                return v;
            };

            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                double dx = s.B.X - s.A.X, dy = s.B.Y - s.A.Y;
                List<IntPoint> c = cuts[i];
                c.Sort((p, q) =>
                    ((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy).CompareTo((q.X - s.A.X) * dx + (q.Y - s.A.Y) * dy));

                for (int k = 1; k < c.Count; k++)
                {
                    if (c[k].Equals(c[k - 1])) continue;
                    int u = id(c[k - 1]), w = id(c[k]);
                    if (u == w) continue;
                    adj[u].Add(w);
                    adj[w].Add(u);
                }
            }

            if (verts.Count < 3) return null;

            // ---- 3. di theo mat ngoai cung ----
            int start = 0;
            for (int v = 1; v < verts.Count; v++)
            {
                if (verts[v].Y < verts[start].Y || (verts[v].Y == verts[start].Y && verts[v].X < verts[start].X)) start = v;
            }

            List<IntPoint> ring = new List<IntPoint>();
            int cur = start;
            double reverse = -Math.PI / 2;      // "den tu phia duoi": vong di nguoc chieu kim dong ho
            int firstNext = -1;
            int limit = 4 * verts.Count + 8;

            for (int step = 0; step < limit; step++)
            {
                int next = -1;
                double best = double.MaxValue;
                foreach (int w in adj[cur])
                {
                    double a = Math.Atan2(verts[w].Y - verts[cur].Y, verts[w].X - verts[cur].X);
                    double d = a - reverse;
                    while (d <= 1e-12) d += 2 * Math.PI;
                    while (d > 2 * Math.PI + 1e-12) d -= 2 * Math.PI;
                    if (d < best)
                    {
                        best = d;
                        next = w;
                    }
                }

                if (next < 0) return null;
                if (step == 0) firstNext = next;
                else if (cur == start && next == firstNext) break;

                ring.Add(verts[cur]);
                reverse = Math.Atan2(verts[cur].Y - verts[next].Y, verts[cur].X - verts[next].X);
                cur = next;

                if (step == limit - 1) return null;
            }

            IntPoint[] clean = GeometryMath.CleanRing(ring);
            int ea, eb;
            if (clean.Length < 3 || !GeometryMath.IsSimpleRing(clean, out ea, out eb)) return null;
            if (!ContainsAll(clean, local)) return null;

            return ToWorld(clean, origin);
        }

        /// <summary>Bao loi cua moi diem - luon la vong don va luon chua het cum net.</summary>
        public static IntPoint[] ConvexHull(IList<IList<IntPoint>> paths)
        {
            IntPoint origin;
            List<IntPoint[]> local = ToLocal(paths, out origin);
            List<IntPoint> pts = new List<IntPoint>();
            foreach (IntPoint[] p in local) pts.AddRange(p);
            pts.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

            List<IntPoint> hull = new List<IntPoint>();
            for (int pass = 0; pass < 2; pass++)
            {
                int floor = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    IntPoint p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (hull.Count >= floor + 2 && GeometryMath.Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    {
                        hull.RemoveAt(hull.Count - 1);
                    }

                    hull.Add(p);
                }

                hull.RemoveAt(hull.Count - 1);
            }

            return hull.Count < 3 ? null : ToWorld(GeometryMath.CleanRing(hull), origin);
        }

        private static void AddIfOn(List<IntPoint> cuts, Seg s, IntPoint p)
        {
            if (p.X >= s.MinX && p.X <= s.MaxX && p.Y >= s.MinY && p.Y <= s.MaxY) cuts.Add(p);
        }

        private static bool ContainsAll(IntPoint[] ring, List<IntPoint[]> paths)
        {
            foreach (IntPoint[] p in paths)
            {
                foreach (IntPoint q in p)
                {
                    if (GeometryMath.PointInRing(q, ring) < 0) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Doi ve goc cuc bo (goc duoi trai cua cum): tich cheo tren toa do the gioi rat xa goc
        /// co the tran long, con trong pham vi mot cum net thi khong.
        /// </summary>
        private static List<IntPoint[]> ToLocal(IList<IList<IntPoint>> paths, out IntPoint origin)
        {
            long ox = long.MaxValue, oy = long.MaxValue;
            foreach (IList<IntPoint> p in paths)
            {
                foreach (IntPoint q in p)
                {
                    if (q.X < ox) ox = q.X;
                    if (q.Y < oy) oy = q.Y;
                }
            }

            origin = new IntPoint(ox, oy);
            List<IntPoint[]> local = new List<IntPoint[]>(paths.Count);
            foreach (IList<IntPoint> p in paths)
            {
                IntPoint[] r = new IntPoint[p.Count];
                for (int i = 0; i < p.Count; i++) r[i] = new IntPoint(p[i].X - ox, p[i].Y - oy);
                local.Add(r);
            }

            return local;
        }

        private static IntPoint[] ToWorld(IntPoint[] ring, IntPoint origin)
        {
            IntPoint[] r = new IntPoint[ring.Length];
            for (int i = 0; i < ring.Length; i++) r[i] = new IntPoint(ring[i].X + origin.X, ring[i].Y + origin.Y);
            return r;
        }
    }
}
