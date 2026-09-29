using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Lexicographic comparison (not a weighted utilisation score):
    ///   1. fewer unplaced parts,
    ///   2. fewer sheets,
    ///   3. smaller total used sheet length (the unused tail of a sheet is a remnant, not waste),
    ///   3b. NHIEU vat lieu hon nam trong LO KIN cua chi tiet khac, roi DUNG IT LO hon (manh cat roi trong lo la
    ///      vun, kho tan dung; lap no truoc de phan to lien con lai to nhat),
    ///   4. tighter packing: smaller total used bounding area (used length x used height),
    ///   5. it don hang bi tron chung tren mot to,
    ///   6. chi tiet cung don nam gan nhau hon.
    /// Lengths are compared with a 1 mm tolerance so that noise cannot flip a decision.
    ///
    /// Hai khoa 5 va 6 la de CHIA BAI KHI DA HOA: chung chi duoc xet khi so to, chieu dai va
    /// dien tich deu ngang nhau. Nho vay viec gom don KHONG BAO GIO lam tang so to hay ton
    /// them vat lieu - dung nhu yeu cau. Va khi ca don hang chi co mot (hoac khong chay theo
    /// don) thi hai khoa nay duoc bo qua han, nen ket qua giong het truoc khi co tinh nang nay.
    /// </summary>
    public sealed class LexicographicSolutionEvaluator : ISolutionEvaluator
    {
        /// <summary>1 mm. Bo giai ma dung chung con so nay de cat som (xem CandidatePointDecoder).</summary>
        internal const long LengthTolerance = 1000;

        public int Compare(DecodedLayout a, DecodedLayout b)
        {
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;

            int c = a.Unplaced.Count.CompareTo(b.Unplaced.Count);
            if (c != 0) return c;

            c = a.Sheets.Count.CompareTo(b.Sheets.Count);
            if (c != 0) return c;

            long lenA = TotalUsedLength(a), lenB = TotalUsedLength(b);
            if (Math.Abs(lenA - lenB) > LengthTolerance) return lenA.CompareTo(lenB);

            // Cung chieu dai: phuong an nhet NHIEU vat lieu hon vao LO KIN cua chi tiet khac thi
            // thang. Phan ben trong lo bi cat roi ra thanh manh vun, kho tan dung - xep chi tiet
            // vao do de phan to LIEN con lai to nhat. Khong cho phep ghep vao lo thi ca hai deu
            // bang 0 va khoa nay khong doi gi.
            double holeA = AreaInsideHoles(a), holeB = AreaInsideHoles(b);
            if (Math.Abs(holeA - holeB) > (double)LengthTolerance * LengthTolerance) return holeB.CompareTo(holeA);

            // Cung luong vat lieu trong lo: DON vao IT lo hon thi thang - cac lo con lai trong
            // nguyen, lan sau tan dung duoc; rai moi lo mot manh thi lo nao cung do dang.
            c = HolesUsed(a).CompareTo(HolesUsed(b));
            if (c != 0) return c;

            double areaA = TotalUsedArea(a), areaB = TotalUsedArea(b);
            if (Math.Abs(areaA - areaB) > (double)LengthTolerance * LengthTolerance) return areaA.CompareTo(areaB);

            // Chi chia bai theo don khi THUC SU co nhieu don. Mot don (hoac khong co don) thi
            // dung o day, y het khi chua co tinh nang nay.
            if (!HasSeveralOrders(a) && !HasSeveralOrders(b)) return 0;

            c = OrderMixing(a).CompareTo(OrderMixing(b));
            if (c != 0) return c;

            long spreadA = OrderSpread(a), spreadB = OrderSpread(b);
            if (Math.Abs(spreadA - spreadB) > LengthTolerance) return spreadA.CompareTo(spreadB);

            return 0;
        }

        private static bool HasSeveralOrders(DecodedLayout layout)
        {
            string first = null;
            foreach (DecodedSheet s in layout.Sheets)
            {
                foreach (PlacedItem i in s.Items)
                {
                    string order = i.Instance.Order;
                    if (string.IsNullOrEmpty(order)) continue;
                    if (first == null) first = order;
                    else if (!string.Equals(first, order, StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tong so don PHAI tron them tren moi to: mot to chi co mot don thi khong ton gi,
        /// hai don thi ton 1, ba don thi ton 2... Cang nho cang de theo doi khi ra xuong.
        /// </summary>
        public static int OrderMixing(DecodedLayout layout)
        {
            int total = 0;
            List<string> seen = new List<string>();
            foreach (DecodedSheet s in layout.Sheets)
            {
                seen.Clear();
                foreach (PlacedItem i in s.Items)
                {
                    string order = i.Instance.Order;
                    if (string.IsNullOrEmpty(order) || seen.Contains(order)) continue;
                    seen.Add(order);
                }

                if (seen.Count > 1) total += seen.Count - 1;
            }

            return total;
        }

        /// <summary>
        /// Tong BE RONG THEO X ma moi don chiem tren moi to. Chi tiet cung don nam sat nhau
        /// thi be rong nho; rai rac khap to thi be rong lon. Do theo X vi to phoi dai theo X.
        /// </summary>
        public static long OrderSpread(DecodedLayout layout)
        {
            long total = 0;
            Dictionary<string, long[]> span = new Dictionary<string, long[]>(StringComparer.Ordinal);
            foreach (DecodedSheet s in layout.Sheets)
            {
                span.Clear();
                foreach (PlacedItem i in s.Items)
                {
                    string order = i.Instance.Order;
                    if (string.IsNullOrEmpty(order)) continue;

                    long lo = i.Placed.Bounds.MinX, hi = i.Placed.Bounds.MaxX;
                    long[] cur;
                    if (!span.TryGetValue(order, out cur)) span[order] = new[] { lo, hi };
                    else
                    {
                        if (lo < cur[0]) cur[0] = lo;
                        if (hi > cur[1]) cur[1] = hi;
                    }
                }

                foreach (KeyValuePair<string, long[]> kv in span) total += kv.Value[1] - kv.Value[0];
            }

            return total;
        }

        public static long TotalUsedLength(DecodedLayout layout)
        {
            long sum = 0;
            foreach (DecodedSheet s in layout.Sheets) sum += s.MaxX;
            return sum;
        }

        /// <summary>
        /// Tong dien tich (thuc, tru lo) cac chi tiet nam TRON trong hop bao mot lo kin cua chi
        /// tiet khac tren cung to. Bo cuc da qua kiem va cham nen nam trong hop bao lo = nam
        /// trong lo (phan hop bao ngoai lo la vat lieu cua chi tiet chua no).
        /// </summary>
        public static double AreaInsideHoles(DecodedLayout layout)
        {
            double sum = 0;
            foreach (DecodedSheet s in layout.Sheets)
            {
                foreach (PlacedItem holder in s.Items)
                {
                    LongRect[] holes = holder.HoleBounds;
                    if (holes == null || holes.Length == 0) continue;

                    foreach (PlacedItem i in s.Items)
                    {
                        if (ReferenceEquals(i, holder)) continue;
                        LongRect b = i.Placed.Bounds;
                        foreach (LongRect h in holes)
                        {
                            if (b.MinX >= h.MinX && b.MinY >= h.MinY && b.MaxX <= h.MaxX && b.MaxY <= h.MaxY)
                            {
                                sum += i.Instance.Group.Shape.Polygon.NetArea;
                                break;
                            }
                        }
                    }
                }
            }

            return sum;
        }

        /// <summary>So lo kin (cua moi chi tiet tren moi to) co it nhat mot chi tiet nam trong.</summary>
        public static int HolesUsed(DecodedLayout layout)
        {
            int used = 0;
            foreach (DecodedSheet s in layout.Sheets)
            {
                foreach (PlacedItem holder in s.Items)
                {
                    LongRect[] holes = holder.HoleBounds;
                    if (holes == null) continue;

                    foreach (LongRect h in holes)
                    {
                        foreach (PlacedItem i in s.Items)
                        {
                            if (ReferenceEquals(i, holder)) continue;
                            LongRect b = i.Placed.Bounds;
                            if (b.MinX >= h.MinX && b.MinY >= h.MinY && b.MaxX <= h.MaxX && b.MaxY <= h.MaxY)
                            {
                                used++;
                                break;
                            }
                        }
                    }
                }
            }

            return used;
        }

        private static double TotalUsedArea(DecodedLayout layout)
        {
            double sum = 0;
            foreach (DecodedSheet s in layout.Sheets) sum += (double)s.MaxX * s.MaxY;
            return sum;
        }
    }
}
