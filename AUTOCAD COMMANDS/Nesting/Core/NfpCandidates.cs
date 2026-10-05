using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Sinh VI TRI DAT tu NFP that: vung dat duoc cua chi tiet = vung trong to (IFP - hinh chu
    /// nhat cac tinh tien giu chi tiet trong le mep) TRU hop moi NFP voi chi tiet da dat. Cho
    /// tot nhat theo bat ky tieu chi "trai / duoi / ngan" nao luon nam tren BIEN vung do, tai mot
    /// DINH cua no - tuc la mot trong:
    ///   - goc IFP,
    ///   - dinh cua mot manh NFP,
    ///   - giao diem canh NFP voi bien IFP,
    ///   - giao diem canh NFP cua HAI chi tiet khac nhau (khe giua hai chi tiet).
    /// Diem nao nam HAN trong mot manh NFP thi bi loai. Diem con lai chi la UNG VIEN - bo giai ma
    /// van kiem va cham chinh xac truoc khi dung.
    /// </summary>
    internal static class NfpCandidates
    {
        private const int GridCells = 24;

        /// <summary>Tran so phep thu giao canh moi lan goi (chan truong hop to cuc day).</summary>
        private const int MaxEdgeTests = 400000;

        /// <param name="keep">
        /// Can bao nhieu diem TOT NHAT. Moi tieu chi xep hang deu uu tien X nho truoc (mep trai
        /// hoac mep phai cua chi tiet, ca hai tang theo X), nen khi da co du <paramref name="keep"/>
        /// diem hop le thi moi diem / cap manh nam han ben phai nguong X do khong the lot vao
        /// danh sach - bo qua, khoi tinh giao canh (phan ton thoi gian nhat).
        /// </param>
        public static List<IntPoint> Generate(
            PreparedShape s, List<PlacedItem> items, long[] clearance, long inset, long sheetL, long sheetW, NfpCache cache, int keep)
        {
            List<IntPoint> result = new List<IntPoint>();
            long xLo = inset - s.Bounds.MinX, xHi = sheetL - inset - s.Bounds.MaxX;
            long yLo = inset - s.Bounds.MinY, yHi = sheetW - inset - s.Bounds.MaxY;
            if (xHi < xLo || yHi < yLo) return result;

            // ---- manh NFP da tinh tien theo vi tri chi tiet da dat ----
            List<NfpPiece> pieces = new List<NfpPiece>();
            List<int> owner = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                PlacedItem it = items[i];
                long fx = it.TranslationX, fy = it.TranslationY;
                foreach (NfpPiece p in cache.Pair(it.Shape, s, clearance[i]))
                {
                    if (p.Box.MaxX + fx < xLo || p.Box.MinX + fx > xHi || p.Box.MaxY + fy < yLo || p.Box.MinY + fy > yHi) continue;
                    IntPoint[] pts = new IntPoint[p.Points.Length];
                    for (int k = 0; k < pts.Length; k++) pts[k] = new IntPoint(p.Points[k].X + fx, p.Points[k].Y + fy);
                    pieces.Add(new NfpPiece(pts));
                    owner.Add(i);
                }
            }

            // ---- luoi de tra nhanh manh nao phu mot diem ----
            long cw = Math.Max(1, (xHi - xLo) / GridCells + 1), ch = Math.Max(1, (yHi - yLo) / GridCells + 1);
            List<int>[] cells = new List<int>[GridCells * GridCells];
            for (int p = 0; p < pieces.Count; p++)
            {
                LongRect b = pieces[p].Box;
                int c0 = Cell(b.MinX, xLo, cw), c1 = Cell(b.MaxX, xLo, cw), r0 = Cell(b.MinY, yLo, ch), r1 = Cell(b.MaxY, yLo, ch);
                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        int k = r * GridCells + c;
                        if (cells[k] == null) cells[k] = new List<int>();
                        cells[k].Add(p);
                    }
                }
            }

            HashSet<long> seen = new HashSet<long>();
            Action<long, long> offer = (x, y) =>
            {
                if (x < xLo || x > xHi || y < yLo || y > yHi) return;
                long key = (x - xLo) * 4000003L + (y - yLo);
                if (!seen.Add(key)) return;
                List<int> cell = cells[Cell(y, yLo, ch) * GridCells + Cell(x, xLo, cw)];
                if (cell != null)
                {
                    foreach (int p in cell)
                    {
                        if (NfpGeometry.StrictlyInside(pieces[p], x, y)) return;
                    }
                }

                result.Add(new IntPoint(x, y));
            };

            // Giao diem nam HAN trong manh THU BA thi khong bao gio la vi tri hop le - loai ngay,
            // truoc khi thu 9 diem lan can (phan lon giao diem roi vao truong hop nay).
            Func<long, long, int, int, bool> blocked = (x, y, pa, pb) =>
            {
                if (x < xLo || x > xHi || y < yLo || y > yHi) return true;
                List<int> cell = cells[Cell(y, yLo, ch) * GridCells + Cell(x, xLo, cw)];
                if (cell == null) return false;
                foreach (int p in cell)
                {
                    if (p != pa && p != pb && NfpGeometry.StrictlyInside(pieces[p], x, y)) return true;
                }

                return false;
            };

            // 1. goc IFP
            offer(xLo, yLo);
            offer(xLo, yHi);
            offer(xHi, yLo);
            offer(xHi, yHi);

            // 2. dinh manh NFP, 3. giao canh NFP voi bien IFP
            foreach (NfpPiece p in pieces)
            {
                IntPoint[] pts = p.Points;
                for (int k = 0, j = pts.Length - 1; k < pts.Length; j = k++)
                {
                    offer(pts[k].X, pts[k].Y);
                    CrossVertical(pts[j], pts[k], xLo, offer);
                    CrossVertical(pts[j], pts[k], xHi, offer);
                    CrossHorizontal(pts[j], pts[k], yLo, offer);
                    CrossHorizontal(pts[j], pts[k], yHi, offer);
                }
            }

            // Nguong X: diem thu "keep" theo X (+ 1 mm luong tu cua diem xep hang).
            long limitX = KthX(result, keep);

            // 4. giao canh NFP cua hai chi tiet KHAC nhau, chi o cap manh co hop bao chong nhau.
            //    Duyet theo X TANG DAN cua phan chong: khi phan chong da nam sau nguong thi moi cap
            //    con lai cung vay - dung luon. Nguong duoc siet lai moi khi co them diem.
            List<long> pairs = new List<long>();
            List<long> pairX = new List<long>();
            for (int pa = 0; pa < pieces.Count; pa++)
            {
                LongRect a = pieces[pa].Box;
                for (int pb = pa + 1; pb < pieces.Count; pb++)
                {
                    if (owner[pa] == owner[pb]) continue;
                    LongRect b = pieces[pb].Box;
                    if (!a.Overlaps(b, 0)) continue;
                    long ix = Math.Max(a.MinX, b.MinX);
                    if (ix > limitX) continue;
                    pairs.Add(((long)pa << 32) | (uint)pb);
                    pairX.Add(ix);
                }
            }

            long[] order = pairs.ToArray();
            long[] keys = pairX.ToArray();
            Array.Sort(keys, order);
            int tests = 0, lastCount = result.Count;
            for (int k = 0; k < order.Length && tests < MaxEdgeTests; k++)
            {
                if (keys[k] > limitX) break;
                int pa = (int)(order[k] >> 32), pb = (int)(order[k] & 0xFFFFFFFF);
                tests += Intersections(pieces[pa], pieces[pb], pa, pb, blocked, offer);
                if (result.Count - lastCount >= 16)
                {
                    lastCount = result.Count;
                    limitX = Math.Min(limitX, KthX(result, keep));
                }
            }

            if (limitX != long.MaxValue) result.RemoveAll(p => p.X > limitX);
            return result;
        }

        /// <summary>X cua diem thu <paramref name="keep"/> (theo X tang dan) + 1 mm; chua du diem = vo cuc.</summary>
        private static long KthX(List<IntPoint> points, int keep)
        {
            if (keep <= 0 || points.Count < keep) return long.MaxValue;
            long[] xs = new long[points.Count];
            for (int i = 0; i < xs.Length; i++) xs[i] = points[i].X;
            Array.Sort(xs);
            return xs[keep - 1] + 1000;
        }

        private static int Cell(long v, long origin, long size)
        {
            long c = (v - origin) / size;
            if (c < 0) return 0;
            if (c >= GridCells) return GridCells - 1;
            return (int)c;
        }

        private static void CrossVertical(IntPoint a, IntPoint b, long x, Action<long, long> offer)
        {
            if ((a.X < x) == (b.X < x) || a.X == b.X) return;
            double t = (double)(x - a.X) / (b.X - a.X);
            offer(x, (long)Math.Round(a.Y + t * (b.Y - a.Y)));
        }

        private static void CrossHorizontal(IntPoint a, IntPoint b, long y, Action<long, long> offer)
        {
            if ((a.Y < y) == (b.Y < y) || a.Y == b.Y) return;
            double t = (double)(y - a.Y) / (b.Y - a.Y);
            offer((long)Math.Round(a.X + t * (b.X - a.X)), y);
        }

        private static int Intersections(NfpPiece p, NfpPiece q, int pi, int qi, Func<long, long, int, int, bool> blocked, Action<long, long> offer)
        {
            // Chi cac canh cat vao vung CHONG cua hai hop bao moi co the giao nhau.
            LongRect zone = new LongRect(
                Math.Max(p.Box.MinX, q.Box.MinX), Math.Max(p.Box.MinY, q.Box.MinY),
                Math.Min(p.Box.MaxX, q.Box.MaxX), Math.Min(p.Box.MaxY, q.Box.MaxY));
            List<int> ea = EdgesIn(p.Points, zone), eb = EdgesIn(q.Points, zone);
            IntPoint[] a = p.Points, b = q.Points;
            int tests = 0;
            foreach (int i in ea)
            {
                IntPoint a0 = a[i == 0 ? a.Length - 1 : i - 1], a1 = a[i];
                long aMinX = Math.Min(a0.X, a1.X), aMaxX = Math.Max(a0.X, a1.X), aMinY = Math.Min(a0.Y, a1.Y), aMaxY = Math.Max(a0.Y, a1.Y);
                foreach (int j in eb)
                {
                    IntPoint b0 = b[j == 0 ? b.Length - 1 : j - 1], b1 = b[j];
                    tests++;
                    if (Math.Max(b0.X, b1.X) < aMinX || Math.Min(b0.X, b1.X) > aMaxX ||
                        Math.Max(b0.Y, b1.Y) < aMinY || Math.Min(b0.Y, b1.Y) > aMaxY) continue;

                    double rx = a1.X - a0.X, ry = a1.Y - a0.Y, sx = b1.X - b0.X, sy = b1.Y - b0.Y;
                    double den = rx * sy - ry * sx;
                    if (Math.Abs(den) < 1e-9) continue;
                    double qx = b0.X - a0.X, qy = b0.Y - a0.Y;
                    double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
                    if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                    long x = (long)Math.Round(a0.X + t * rx), y = (long)Math.Round(a0.Y + t * ry);
                    if (blocked(x, y, pi, qi)) continue;

                    // Lam tron co the roi vao trong mot trong hai manh: thu ca cac diem lan can.
                    for (int ddx = -1; ddx <= 1; ddx++)
                    {
                        for (int ddy = -1; ddy <= 1; ddy++) offer(x + ddx, y + ddy);
                    }
                }
            }

            return tests;
        }

        /// <summary>Chi so canh (k-1, k) co hop bao cham vung <paramref name="zone"/>.</summary>
        private static List<int> EdgesIn(IntPoint[] pts, LongRect zone)
        {
            List<int> r = new List<int>(4);
            for (int k = 0, j = pts.Length - 1; k < pts.Length; j = k++)
            {
                if (Math.Max(pts[j].X, pts[k].X) < zone.MinX || Math.Min(pts[j].X, pts[k].X) > zone.MaxX ||
                    Math.Max(pts[j].Y, pts[k].Y) < zone.MinY || Math.Min(pts[j].Y, pts[k].Y) > zone.MaxY) continue;
                r.Add(k);
            }

            return r;
        }
    }
}
