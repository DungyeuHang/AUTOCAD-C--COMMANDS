using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - MO HINH BIEN DANG (PROFILE)
    // ------------------------------------------------------------------------------------------
    // FoilProfile la ban sao "thuan toan hoc" cua polyline bien dang, doc lap voi AutoCAD.
    // Quy uoc bulge giong het LWPolyline cua AutoCAD:
    //     bulge = tan(sweep / 4), duong = cung CCW, am = cung CW, 0 = doan thang.
    // ==========================================================================================

    public enum FoilSegmentKind
    {
        Line = 0,
        Arc = 1
    }

    /// <summary>Dinh tho doc tu polyline: toa do + bulge cua doan bat dau tai dinh do.</summary>
    public struct FoilRawVertex
    {
        public readonly FoilPoint2d Point;
        public readonly double Bulge;

        public FoilRawVertex(FoilPoint2d point, double bulge)
        {
            Point = point;
            Bulge = bulge;
        }

        public FoilRawVertex(double x, double y, double bulge)
        {
            Point = new FoilPoint2d(x, y);
            Bulge = bulge;
        }
    }

    /// <summary>
    /// Mot doan cua bien dang. Moi dai luong dan xuat (ban kinh, goc quet, tiep tuyen...) duoc
    /// tinh mot lan trong constructor de phan con lai cua engine chi doc.
    /// </summary>
    public class FoilProfileSegment
    {
        private const double BulgeTolerance = 1e-12;

        public FoilPoint2d Start { get; private set; }

        public FoilPoint2d End { get; private set; }

        public double Bulge { get; private set; }

        public FoilSegmentKind Kind { get; private set; }

        /// <summary>Khoang cach thang Start -> End.</summary>
        public double ChordLength { get; private set; }

        /// <summary>Chieu dai thuc cua doan: chord voi Line, do dai cung voi Arc.</summary>
        public double Length { get; private set; }

        /// <summary>Goc quet co dau (radian). Duong = CCW. Chi co nghia voi Arc.</summary>
        public double SweepAngle { get; private set; }

        /// <summary>Ban kinh cung ve trong ban ve. Chi co nghia voi Arc.</summary>
        public double Radius { get; private set; }

        /// <summary>Tam cung. Chi co nghia voi Arc.</summary>
        public FoilPoint2d Center { get; private set; }

        /// <summary>Tiep tuyen don vi tai Start, theo chieu di chuyen.</summary>
        public FoilVector2d StartDirection { get; private set; }

        /// <summary>Tiep tuyen don vi tai End, theo chieu di chuyen.</summary>
        public FoilVector2d EndDirection { get; private set; }

        public bool IsArc { get { return Kind == FoilSegmentKind.Arc; } }

        public FoilProfileSegment(FoilPoint2d start, FoilPoint2d end, double bulge)
        {
            Start = start;
            End = end;
            Bulge = bulge;

            FoilVector2d chord = end - start;
            ChordLength = chord.Length;
            FoilVector2d chordDir = chord.Normalized();

            if (Math.Abs(bulge) <= BulgeTolerance || ChordLength <= FoilMath.LengthTolerance)
            {
                Kind = FoilSegmentKind.Line;
                SweepAngle = 0.0;
                Radius = 0.0;
                Center = start;
                Length = ChordLength;
                StartDirection = chordDir;
                EndDirection = chordDir;
                return;
            }

            Kind = FoilSegmentKind.Arc;

            // sweep = 4 * atan(bulge)  (quy uoc AutoCAD)
            SweepAngle = 4.0 * Math.Atan(bulge);
            double halfSweep = SweepAngle * 0.5;

            Radius = Math.Abs(ChordLength / (2.0 * Math.Sin(halfSweep)));
            Length = Math.Abs(SweepAngle) * Radius;

            // Khoang cach co dau tu trung diem day cung den tam: h = (c/2) / tan(sweep/2)
            double h = (ChordLength * 0.5) / Math.Tan(halfSweep);
            FoilPoint2d mid = new FoilPoint2d(
                (start.X + end.X) * 0.5,
                (start.Y + end.Y) * 0.5);
            Center = mid + chordDir.Perpendicular() * h;

            // Tiep tuyen: quay day cung -sweep/2 tai dau, +sweep/2 tai cuoi.
            StartDirection = chordDir.Rotate(-halfSweep);
            EndDirection = chordDir.Rotate(halfSweep);
        }

        /// <summary>Diem giua cung / giua doan thang.</summary>
        public FoilPoint2d MidPoint()
        {
            if (!IsArc)
            {
                return new FoilPoint2d((Start.X + End.X) * 0.5, (Start.Y + End.Y) * 0.5);
            }

            FoilVector2d toStart = Start - Center;
            double startAngle = toStart.AngleRad;
            double midAngle = startAngle + SweepAngle * 0.5;
            return new FoilPoint2d(
                Center.X + Radius * Math.Cos(midAngle),
                Center.Y + Radius * Math.Sin(midAngle));
        }

        /// <summary>
        /// Dien tich "circular segment" co dau giua day cung va cung tron.
        /// Dung de tinh dien tich co dau chinh xac cho bien dang co cung.
        /// </summary>
        public double CircularSegmentArea()
        {
            if (!IsArc)
            {
                return 0.0;
            }

            return 0.5 * Radius * Radius * (SweepAngle - Math.Sin(SweepAngle));
        }

        public FoilProfileSegment Reversed()
        {
            return new FoilProfileSegment(End, Start, -Bulge);
        }

        /// <summary>Tach doan lam doi tai diem giua. Dung khi mo vong kin de trien khai.</summary>
        public void SplitAtMiddle(out FoilProfileSegment firstHalf, out FoilProfileSegment secondHalf)
        {
            FoilPoint2d mid = MidPoint();
            if (!IsArc)
            {
                firstHalf = new FoilProfileSegment(Start, mid, 0.0);
                secondHalf = new FoilProfileSegment(mid, End, 0.0);
                return;
            }

            // Nua cung co goc quet sweep/2 => bulge moi = tan(sweep / 8).
            double halfBulge = Math.Tan(SweepAngle / 8.0);
            firstHalf = new FoilProfileSegment(Start, mid, halfBulge);
            secondHalf = new FoilProfileSegment(mid, End, halfBulge);
        }
    }

    public class FoilProfile
    {
        public List<FoilProfileSegment> Segments { get; private set; }

        public bool Closed { get; private set; }

        public FoilProfile(IEnumerable<FoilProfileSegment> segments, bool closed)
        {
            Segments = new List<FoilProfileSegment>(segments);
            Closed = closed;
        }

        public int SegmentCount { get { return Segments.Count; } }

        public FoilPoint2d StartPoint { get { return Segments[0].Start; } }

        public FoilPoint2d EndPoint { get { return Segments[Segments.Count - 1].End; } }

        public double TotalLength
        {
            get
            {
                double sum = 0.0;
                for (int i = 0; i < Segments.Count; i++)
                {
                    sum += Segments[i].Length;
                }

                return sum;
            }
        }

        /// <summary>
        /// Dien tich co dau (duong = CCW). Tinh bang cong thuc shoelace tren cac dinh
        /// cong them dien tich circular segment cua tung cung.
        /// </summary>
        public double SignedArea()
        {
            double area = 0.0;
            for (int i = 0; i < Segments.Count; i++)
            {
                FoilProfileSegment s = Segments[i];
                area += s.Start.X * s.End.Y - s.End.X * s.Start.Y;
                area += 2.0 * s.CircularSegmentArea();
            }

            return area * 0.5;
        }

        public bool IsCounterClockwise { get { return SignedArea() >= 0.0; } }

        public FoilProfile Reversed()
        {
            List<FoilProfileSegment> reversed = new List<FoilProfileSegment>(Segments.Count);
            for (int i = Segments.Count - 1; i >= 0; i--)
            {
                reversed.Add(Segments[i].Reversed());
            }

            return new FoilProfile(reversed, Closed);
        }

        public List<FoilPoint2d> GetVertices()
        {
            List<FoilPoint2d> points = new List<FoilPoint2d>(Segments.Count + 1);
            points.Add(Segments[0].Start);
            for (int i = 0; i < Segments.Count; i++)
            {
                points.Add(Segments[i].End);
            }

            if (Closed && points.Count > 1)
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }
    }

    public static class FoilProfileBuilder
    {
        /// <summary>
        /// Dung FoilProfile tu danh sach dinh tho. Bo cac doan co chieu dai 0 (ghi chu lai),
        /// tra ve null neu khong du du lieu.
        /// </summary>
        public static FoilProfile Build(
            IList<FoilRawVertex> vertices,
            bool closed,
            double duplicateTolerance,
            List<string> notes)
        {
            if (vertices == null || vertices.Count < 2)
            {
                if (notes != null)
                {
                    notes.Add("Bien dang phai co it nhat 2 dinh.");
                }

                return null;
            }

            int segmentCount = closed ? vertices.Count : vertices.Count - 1;
            List<FoilProfileSegment> segments = new List<FoilProfileSegment>(segmentCount);
            int skipped = 0;

            for (int i = 0; i < segmentCount; i++)
            {
                FoilRawVertex a = vertices[i];
                FoilRawVertex b = vertices[(i + 1) % vertices.Count];

                if (a.Point.DistanceTo(b.Point) <= duplicateTolerance)
                {
                    // Doan dai 0: bo qua nhung KHONG bo qua im lang.
                    skipped++;
                    continue;
                }

                segments.Add(new FoilProfileSegment(a.Point, b.Point, a.Bulge));
            }

            if (skipped > 0 && notes != null)
            {
                notes.Add(string.Format(
                    "Da bo qua {0} doan co chieu dai bang 0 (dinh trung nhau).", skipped));
            }

            if (segments.Count == 0)
            {
                if (notes != null)
                {
                    notes.Add("Bien dang khong con doan nao hop le sau khi loai cac doan dai 0.");
                }

                return null;
            }

            return new FoilProfile(segments, closed && segments.Count >= 3);
        }

        /// <summary>
        /// Chuan hoa huong duyet de ket qua KHONG phu thuoc vao chieu ve polyline cua nguoi dung.
        ///
        ///  - Vong KIN : chuan hoa ve CCW (dien tich co dau duong).
        ///  - Vong HO  : chon chieu sao cho diem cuoi "lon hon" diem dau theo thu tu tu dien
        ///               (X truoc, roi Y). Quy tac nay xac dinh va khong phu thuoc thu tu ve.
        ///
        /// Nho vay dau cua goc re tai moi dinh (=> huong chan UP/DOWN) luon nhat quan.
        /// </summary>
        public static FoilProfile NormalizeOrientation(FoilProfile profile, double tolerance, List<string> notes)
        {
            if (profile == null)
            {
                return null;
            }

            bool reverse;
            if (profile.Closed)
            {
                reverse = profile.SignedArea() < 0.0;
            }
            else
            {
                FoilPoint2d s = profile.StartPoint;
                FoilPoint2d e = profile.EndPoint;
                if (Math.Abs(e.X - s.X) > tolerance)
                {
                    reverse = e.X < s.X;
                }
                else if (Math.Abs(e.Y - s.Y) > tolerance)
                {
                    reverse = e.Y < s.Y;
                }
                else
                {
                    reverse = false;
                }
            }

            if (!reverse)
            {
                return profile;
            }

            if (notes != null)
            {
                notes.Add("Da dao chieu duyet bien dang de chuan hoa (khong doi hinh dang).");
            }

            return profile.Reversed();
        }

        /// <summary>
        /// Mo mot bien dang KIN thanh chuoi HO de trien khai.
        ///
        /// Cach lam: cat tai DIEM GIUA cua doan dai nhat. Nho vay moi dinh goc cua vong kin deu
        /// tro thanh mot bend noi bo binh thuong, khong sinh ra "bend tai duong noi" dac biet.
        /// Ve mat cong nghe day cung la cach cat phoi hop ly nhat cho tiet dien kin.
        /// </summary>
        public static FoilProfile OpenClosedLoop(FoilProfile profile, List<string> notes)
        {
            if (profile == null || !profile.Closed)
            {
                return profile;
            }

            int longestIndex = 0;
            double longest = -1.0;
            for (int i = 0; i < profile.SegmentCount; i++)
            {
                if (profile.Segments[i].Length > longest)
                {
                    longest = profile.Segments[i].Length;
                    longestIndex = i;
                }
            }

            FoilProfileSegment firstHalf;
            FoilProfileSegment secondHalf;
            profile.Segments[longestIndex].SplitAtMiddle(out firstHalf, out secondHalf);

            List<FoilProfileSegment> opened = new List<FoilProfileSegment>(profile.SegmentCount + 1);

            // Bat dau tu nua sau cua doan bi cat.
            opened.Add(secondHalf);
            for (int k = 1; k < profile.SegmentCount; k++)
            {
                opened.Add(profile.Segments[(longestIndex + k) % profile.SegmentCount]);
            }

            // Ket thuc bang nua dau cua doan bi cat.
            opened.Add(firstHalf);

            if (notes != null)
            {
                notes.Add(string.Format(
                    "Bien dang KIN da duoc mo tai diem giua doan dai nhat (doan #{0}) de trien khai.",
                    longestIndex + 1));
            }

            return new FoilProfile(opened, false);
        }

        /// <summary>
        /// Kiem tra xem vong kin co phai la DUONG BAO CO BE DAY hay khong
        /// (tuc nguoi dung ve ca 2 mat cua ton thay vi 1 duong bien dang).
        ///
        /// Voi mot dai ton be day T va chieu dai duong tam L:
        ///     Area ~= L * T ;  Perimeter ~= 2L + 2T   =>   2 * Area / Perimeter ~= T
        /// </summary>
        public static bool LooksLikeThicknessOutline(FoilProfile profile, double thickness, out double impliedThickness)
        {
            impliedThickness = 0.0;
            if (profile == null || !profile.Closed || thickness <= 0.0)
            {
                return false;
            }

            double perimeter = profile.TotalLength;
            if (perimeter <= FoilMath.LengthTolerance)
            {
                return false;
            }

            double area = Math.Abs(profile.SignedArea());
            impliedThickness = 2.0 * area / perimeter;

            return Math.Abs(impliedThickness - thickness) <= 0.35 * thickness;
        }

        /// <summary>
        /// Phat hien bien dang tu cat (self-intersection) giua cac doan khong ke nhau.
        /// Chi kiem tra tren day cung nen day la phep kiem tra XAP XI - dung de CANH BAO,
        /// khong dung de chan.
        /// </summary>
        public static int CountSelfIntersections(FoilProfile profile, double tolerance)
        {
            if (profile == null)
            {
                return 0;
            }

            int count = 0;
            int n = profile.SegmentCount;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 2; j < n; j++)
                {
                    // Voi vong kin, doan dau va doan cuoi la ke nhau.
                    if (profile.Closed && i == 0 && j == n - 1)
                    {
                        continue;
                    }

                    if (SegmentsProperlyIntersect(
                        profile.Segments[i].Start, profile.Segments[i].End,
                        profile.Segments[j].Start, profile.Segments[j].End,
                        tolerance))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool SegmentsProperlyIntersect(
            FoilPoint2d p1, FoilPoint2d p2,
            FoilPoint2d q1, FoilPoint2d q2,
            double tolerance)
        {
            FoilVector2d r = p2 - p1;
            FoilVector2d s = q2 - q1;
            double denom = r.Cross(s);
            if (Math.Abs(denom) <= 1e-12)
            {
                return false;
            }

            FoilVector2d qp = q1 - p1;
            double t = qp.Cross(s) / denom;
            double u = qp.Cross(r) / denom;

            double tEps = r.Length > tolerance ? tolerance / r.Length : 0.0;
            double uEps = s.Length > tolerance ? tolerance / s.Length : 0.0;

            return t > tEps && t < 1.0 - tEps && u > uEps && u < 1.0 - uEps;
        }
    }
}
