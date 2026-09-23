using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - HINH DANG DUNG CU VA PHEP KIEM VA CHAM
    // ------------------------------------------------------------------------------------------
    // MOI dung cu (chay dao, coi, ban may) deu la MOT DA GIAC KIN duy nhat. Da giac do vua duoc
    // dung de KIEM VA CHAM, vua duoc dung de VE ra ban ve.
    //
    // Day la diem quan trong: cai ma tho NHIN THAY tren ban ve chinh la cai ma chuong trinh DA
    // KIEM. Truoc day dung cu duoc "ta" bang vai cong thuc be rong theo chieu cao, con hinh ve
    // lai dung mot cong thuc khac - hai cai co the lech nhau ma khong ai biet.
    // ==========================================================================================

    /// <summary>Mot mon dung cu: ten de hien thi + duong bao kin trong HE TOA DO MAY.</summary>
    public class FoilToolShape
    {
        public FoilToolShape(string name, List<FoilPoint2d> outline)
            : this(name, outline, 0.0)
        {
        }

        public FoilToolShape(string name, List<FoilPoint2d> outline, double includedAngleDeg)
        {
            Name = name ?? string.Empty;
            Outline = outline ?? new List<FoilPoint2d>();
            IncludedAngleDeg = includedAngleDeg;

            MinX = double.MaxValue; MinY = double.MaxValue;
            MaxX = double.MinValue; MaxY = double.MinValue;

            foreach (FoilPoint2d q in Outline)
            {
                if (q.X < MinX) MinX = q.X;
                if (q.Y < MinY) MinY = q.Y;
                if (q.X > MaxX) MaxX = q.X;
                if (q.Y > MaxY) MaxY = q.Y;
            }
        }

        // Hop bao - de loai nhanh nhung doan o xa truoc khi lam phep cat canh.
        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }

        /// <summary>Doan thang co the cham mon nay khong (phep loai nhanh bang hop bao).</summary>
        public bool MayTouch(FoilPoint2d a, FoilPoint2d b)
        {
            if (Outline.Count < 3) return false;
            if (Math.Max(a.X, b.X) < MinX || Math.Min(a.X, b.X) > MaxX) return false;
            if (Math.Max(a.Y, b.Y) < MinY || Math.Min(a.Y, b.Y) > MaxY) return false;
            return true;
        }

        /// <summary>
        /// Goc mui dao (do). Canh ngoc len phai thoat khoi ma dao, nen dao chi lam duoc goc chan
        /// nho hon 180 - goc mui. 0 = khong phai chay dao (coi, dam duoi).
        /// </summary>
        public double IncludedAngleDeg { get; private set; }

        /// <summary>Goc chan LON NHAT ma dao nay voi toi (do).</summary>
        public double MaxBendAngleDeg
        {
            get { return IncludedAngleDeg > 0.0 ? 180.0 - IncludedAngleDeg : 180.0; }
        }

        /// <summary>Ten hien thi, vi du "dao thang" / "dao co ngong".</summary>
        public string Name { get; private set; }

        /// <summary>Duong bao KIN (diem cuoi noi ve diem dau) trong he toa do may.</summary>
        public List<FoilPoint2d> Outline { get; private set; }

        /// <summary>Anh guong qua truc dao - dung de doi ben duoc mien cua dao co ngong.</summary>
        public FoilToolShape Mirrored(string name)
        {
            List<FoilPoint2d> flipped = new List<FoilPoint2d>(Outline.Count);
            for (int i = Outline.Count - 1; i >= 0; i--)
            {
                flipped.Add(new FoilPoint2d(-Outline[i].X, Outline[i].Y));
            }

            return new FoilToolShape(name ?? Name, flipped, IncludedAngleDeg);
        }
    }

    /// <summary>Phep kiem hinh hoc giua duong gap khuc chi tiet va da giac dung cu.</summary>
    public static class FoilToolGeometry
    {
        /// <summary>
        /// Doan thang co CHAM vao da giac khong (cat qua canh, hoac nam gon ben trong).
        /// </summary>
        public static bool SegmentHitsPolygon(
            FoilPoint2d a, FoilPoint2d b, List<FoilPoint2d> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                FoilPoint2d p = polygon[i];
                FoilPoint2d q = polygon[(i + 1) % polygon.Count];

                if (SegmentsIntersect(a, b, p, q))
                {
                    return true;
                }
            }

            // Khong cat canh nao thi hoac nam han ben trong, hoac han ben ngoai.
            return PointInPolygon(a, polygon);
        }

        /// <summary>
        /// Khoang cach nho nhat tu doan thang den da giac. Tra ve 0 neu cham nhau.
        /// Dung de xep hang cac phuong an, khong dung de ket luan cham hay khong.
        /// </summary>
        public static double SegmentDistanceToPolygon(
            FoilPoint2d a, FoilPoint2d b, List<FoilPoint2d> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return double.MaxValue;
            }

            if (SegmentHitsPolygon(a, b, polygon))
            {
                return 0.0;
            }

            double best = double.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                FoilPoint2d p = polygon[i];
                FoilPoint2d q = polygon[(i + 1) % polygon.Count];

                double d = SegmentDistance(a, b, p, q);
                if (d < best) best = d;
            }

            return best;
        }

        /// <summary>Diem co nam trong da giac khong (thuat toan ban tia).</summary>
        public static bool PointInPolygon(FoilPoint2d point, List<FoilPoint2d> polygon)
        {
            bool inside = false;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double yi = polygon[i].Y;
                double yj = polygon[j].Y;

                if ((yi > point.Y) == (yj > point.Y)) continue;

                double t = (point.Y - yi) / (yj - yi);
                double x = polygon[i].X + t * (polygon[j].X - polygon[i].X);

                if (point.X < x) inside = !inside;
            }

            return inside;
        }

        /// <summary>Hai doan thang co cat nhau khong (ke ca cham dau mut / trung phuong).</summary>
        public static bool SegmentsIntersect(
            FoilPoint2d p1, FoilPoint2d p2, FoilPoint2d q1, FoilPoint2d q2)
        {
            double d1 = Cross(q1, q2, p1);
            double d2 = Cross(q1, q2, p2);
            double d3 = Cross(p1, p2, q1);
            double d4 = Cross(p1, p2, q2);

            if (((d1 > 0.0 && d2 < 0.0) || (d1 < 0.0 && d2 > 0.0)) &&
                ((d3 > 0.0 && d4 < 0.0) || (d3 < 0.0 && d4 > 0.0)))
            {
                return true;
            }

            const double eps = 1e-12;
            if (Math.Abs(d1) <= eps && OnSegment(q1, q2, p1)) return true;
            if (Math.Abs(d2) <= eps && OnSegment(q1, q2, p2)) return true;
            if (Math.Abs(d3) <= eps && OnSegment(p1, p2, q1)) return true;
            if (Math.Abs(d4) <= eps && OnSegment(p1, p2, q2)) return true;

            return false;
        }

        private static double Cross(FoilPoint2d a, FoilPoint2d b, FoilPoint2d c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static bool OnSegment(FoilPoint2d a, FoilPoint2d b, FoilPoint2d p)
        {
            return p.X >= Math.Min(a.X, b.X) - 1e-12 && p.X <= Math.Max(a.X, b.X) + 1e-12
                && p.Y >= Math.Min(a.Y, b.Y) - 1e-12 && p.Y <= Math.Max(a.Y, b.Y) + 1e-12;
        }

        /// <summary>Khoang cach nho nhat giua hai doan thang.</summary>
        public static double SegmentDistance(
            FoilPoint2d p1, FoilPoint2d p2, FoilPoint2d q1, FoilPoint2d q2)
        {
            if (SegmentsIntersect(p1, p2, q1, q2))
            {
                return 0.0;
            }

            double d = PointToSegment(p1, q1, q2);
            d = Math.Min(d, PointToSegment(p2, q1, q2));
            d = Math.Min(d, PointToSegment(q1, p1, p2));
            d = Math.Min(d, PointToSegment(q2, p1, p2));
            return d;
        }

        /// <summary>Khoang cach tu mot diem den mot doan thang.</summary>
        public static double PointToSegment(FoilPoint2d p, FoilPoint2d a, FoilPoint2d b)
        {
            FoilVector2d ab = b - a;
            double lengthSquared = ab.LengthSquared;

            if (lengthSquared <= 1e-18)
            {
                return p.DistanceTo(a);
            }

            double t = ((p - a).Dot(ab)) / lengthSquared;
            t = FoilMath.Clamp(t, 0.0, 1.0);

            return p.DistanceTo(new FoilPoint2d(a.X + ab.X * t, a.Y + ab.Y * t));
        }
    }
}
