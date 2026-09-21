using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - LOP HINH HOC THUAN
    // ------------------------------------------------------------------------------------------
    // File nay (va cac file DimPline*.cs khac tru DimPlineCad*.cs) KHONG tham chieu AutoCAD.
    // Nho vay toan bo engine phan tich + bo tri dim chay duoc o 2 noi:
    //   1. Trong AutoCAD qua lenh DPA_TEST.
    //   2. Ngoai AutoCAD, bien thanh console exe de chay test khi phat trien.
    // Quy uoc quan trong: moi don vi o day deu la DRAWING UNIT THAT. DIMLFAC (linear scale) chi
    // anh huong den CHUOI TEXT hien thi, khong bao gio duoc dung de tinh toa do.
    // ==========================================================================================

    public enum DimOrientation
    {
        Horizontal,
        Vertical,
        Skew
    }

    public enum DimSide
    {
        Bottom,
        Top,
        Left,
        Right
    }

    public enum DimKind
    {
        Linear,
        Aligned,
        Radial
    }

    public struct DimPoint
    {
        public double X;
        public double Y;

        public DimPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
        }
    }

    public sealed class DimBox
    {
        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }
        public bool IsValid { get; private set; }

        public DimBox()
        {
            MinX = double.MaxValue;
            MinY = double.MaxValue;
            MaxX = double.MinValue;
            MaxY = double.MinValue;
            IsValid = false;
        }

        public void Add(double x, double y)
        {
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
            IsValid = true;
        }

        public void Add(DimPoint p)
        {
            Add(p.X, p.Y);
        }

        public double Width { get { return IsValid ? MaxX - MinX : 0.0; } }

        public double Height { get { return IsValid ? MaxY - MinY : 0.0; } }

        public double CenterX { get { return IsValid ? (MinX + MaxX) * 0.5 : 0.0; } }

        public double CenterY { get { return IsValid ? (MinY + MaxY) * 0.5 : 0.0; } }

        public double Diagonal { get { return IsValid ? DimMath.Hypot(Width, Height) : 0.0; } }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0:0.###} {1:0.###}]..[{2:0.###} {3:0.###}]", MinX, MinY, MaxX, MaxY);
        }
    }

    // Hop chu nhat cua text dim, dung de phat hien va cham text-text o buoc bo tri.
    public sealed class DimTextBox
    {
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;

        public DimTextBox(double centerX, double centerY, double width, double height)
        {
            MinX = centerX - width * 0.5;
            MaxX = centerX + width * 0.5;
            MinY = centerY - height * 0.5;
            MaxY = centerY + height * 0.5;
        }

        public bool Overlaps(DimTextBox other, double gap)
        {
            if (other == null)
            {
                return false;
            }

            return MinX - gap < other.MaxX &&
                   MaxX + gap > other.MinX &&
                   MinY - gap < other.MaxY &&
                   MaxY + gap > other.MinY;
        }
    }

    internal static class DimMath
    {
        // Sai so tuyet doi cho so sanh diem.
        public const double PointTolerance = 1e-7;

        public static double Hypot(double dx, double dy)
        {
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double Distance(DimPoint a, DimPoint b)
        {
            return Hypot(b.X - a.X, b.Y - a.Y);
        }

        public static bool PointsEqual(DimPoint a, DimPoint b, double tol)
        {
            return Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;
        }

        public static DimPoint Mid(DimPoint a, DimPoint b)
        {
            return new DimPoint((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
        }

        public static double NormalizeAngle(double a)
        {
            double twoPi = Math.PI * 2.0;
            while (a < 0.0) a += twoPi;
            while (a >= twoPi) a -= twoPi;
            return a;
        }

        // Do lech goc cua doan so voi truc ngang / truc doc, tinh bang do.
        // Phan loai Horizontal / Vertical theo NGUONG GOC chu khong theo nguong toa do,
        // nho vay nguong khong phu thuoc chieu dai doan (ban cu dung 1e-6 tuyet doi nen mot
        // doan dai 200 lech 0.0001 da bi coi la xien).
        public static void AxisDeviationDegrees(
            DimPoint start,
            DimPoint end,
            out double horizontalDeviation,
            out double verticalDeviation)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double len = Hypot(dx, dy);

            if (len < PointTolerance)
            {
                horizontalDeviation = 90.0;
                verticalDeviation = 90.0;
                return;
            }

            double sinFromHorizontal = Math.Min(1.0, Math.Abs(dy) / len);
            double sinFromVertical = Math.Min(1.0, Math.Abs(dx) / len);

            horizontalDeviation = Math.Asin(sinFromHorizontal) * 180.0 / Math.PI;
            verticalDeviation = Math.Asin(sinFromVertical) * 180.0 / Math.PI;
        }

        // Bulge -> cung tron. Quy uoc AutoCAD: bulge = tan(goc_om / 4), duong = nguoc kim dong ho.
        public static bool TryBuildArc(
            DimPoint start,
            DimPoint end,
            double bulge,
            out DimPoint center,
            out double radius,
            out double startAngle,
            out double sweepAngle)
        {
            center = new DimPoint(0.0, 0.0);
            radius = 0.0;
            startAngle = 0.0;
            sweepAngle = 0.0;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double chord = Hypot(dx, dy);

            if (chord < PointTolerance || Math.Abs(bulge) < 1e-12)
            {
                return false;
            }

            double theta = 4.0 * Math.Atan(bulge);
            double halfTheta = theta * 0.5;
            double sinHalf = Math.Sin(halfTheta);
            if (Math.Abs(sinHalf) < 1e-12)
            {
                return false;
            }

            double signedRadius = chord / (2.0 * sinHalf);
            double apothem = signedRadius * Math.Cos(halfTheta);

            // Phap tuyen cua day cung: huong day cung quay +90 do.
            double perpX = -dy / chord;
            double perpY = dx / chord;

            double midX = (start.X + end.X) * 0.5;
            double midY = (start.Y + end.Y) * 0.5;

            center = new DimPoint(midX + perpX * apothem, midY + perpY * apothem);
            radius = Math.Abs(signedRadius);
            startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            sweepAngle = theta;
            return true;
        }

        public static bool AngleInSweep(double angle, double startAngle, double sweepAngle)
        {
            double twoPi = Math.PI * 2.0;

            if (sweepAngle >= 0.0)
            {
                double delta = NormalizeAngle(angle - startAngle);
                return delta <= Math.Min(sweepAngle, twoPi) + 1e-12;
            }

            double deltaCw = NormalizeAngle(startAngle - angle);
            return deltaCw <= Math.Min(-sweepAngle, twoPi) + 1e-12;
        }

        // Bao hinh CHINH XAC cua cung: phai xet ca 4 diem cuc tri 0/90/180/270 do neu chung
        // nam trong cung quet, khong chi 2 dau mut.
        public static void AddArcExtents(
            DimBox box,
            DimPoint start,
            DimPoint end,
            DimPoint center,
            double radius,
            double startAngle,
            double sweepAngle)
        {
            box.Add(start);
            box.Add(end);

            for (int q = 0; q < 4; q++)
            {
                double angle = q * Math.PI * 0.5;
                if (AngleInSweep(angle, startAngle, sweepAngle))
                {
                    box.Add(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
                }
            }
        }

        // Chia cung thanh day cung nho, chi phuc vu phep thu giao cat khi cham diem.
        public static void FlattenArc(
            List<DimPoint> target,
            DimPoint center,
            double radius,
            double startAngle,
            double sweepAngle,
            int steps)
        {
            if (steps < 2)
            {
                steps = 2;
            }

            for (int i = 1; i <= steps; i++)
            {
                double a = startAngle + sweepAngle * i / steps;
                target.Add(new DimPoint(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
            }
        }

        /// <summary>
        /// Dem so lan mot tia thang (ngang hoac doc) cat qua duong bao.
        /// Dung lam diem phat "duong giong cat qua hinh" khi cham diem cho tung phia dat dim.
        /// Quy uoc nua mo [0,1) tren tham so doan de khong dem trung tai dinh chung.
        /// </summary>
        public static int CountRayCrossings(
            IList<DimPoint> polyPoints,
            bool closed,
            DimPoint origin,
            bool vertical,
            double target,
            double skipTolerance)
        {
            if (polyPoints == null || polyPoints.Count < 2)
            {
                return 0;
            }

            int crossings = 0;
            int last = closed ? polyPoints.Count : polyPoints.Count - 1;

            double from = vertical ? origin.Y : origin.X;
            double lo = Math.Min(from, target);
            double hi = Math.Max(from, target);

            for (int i = 0; i < last; i++)
            {
                DimPoint a = polyPoints[i];
                DimPoint b = polyPoints[(i + 1) % polyPoints.Count];

                // Bo qua doan chua chinh diem goc cua tia (doan dang duoc do).
                if (PointsEqual(a, origin, skipTolerance) || PointsEqual(b, origin, skipTolerance))
                {
                    continue;
                }

                double a0 = vertical ? a.X : a.Y;
                double b0 = vertical ? b.X : b.Y;
                double a1 = vertical ? a.Y : a.X;
                double b1 = vertical ? b.Y : b.X;
                double cut = vertical ? origin.X : origin.Y;

                double denom = b0 - a0;
                if (Math.Abs(denom) < PointTolerance)
                {
                    continue; // song song voi tia
                }

                double t = (cut - a0) / denom;
                if (t < 0.0 || t >= 1.0)
                {
                    continue;
                }

                double hit = a1 + (b1 - a1) * t;
                if (hit > lo + skipTolerance && hit < hi - skipTolerance)
                {
                    crossings++;
                }
            }

            return crossings;
        }

        /// <summary>
        /// Dem so lan mot DOAN THANG huu han (ngang hoac doc) cat NGANG qua duong bao.
        /// Chi dem cat that su o phan trong cua doan: doan ket thuc dung tren mot canh cua bien
        /// dang (rat pho bien khi dim mot bac) KHONG bi tinh la loi. Canh nam trung len duong
        /// kich thuoc thi bi tinh (vi se ve de len hinh).
        /// </summary>
        public static int CountSegmentCrossings(
            IList<DimPoint> polyPoints,
            bool closed,
            bool horizontalLine,
            double fixedCoord,
            double lo,
            double hi,
            double eps)
        {
            if (polyPoints == null || polyPoints.Count < 2 || hi - lo <= eps * 2.0)
            {
                return 0;
            }

            int crossings = 0;
            int last = closed ? polyPoints.Count : polyPoints.Count - 1;

            for (int i = 0; i < last; i++)
            {
                DimPoint a = polyPoints[i];
                DimPoint b = polyPoints[(i + 1) % polyPoints.Count];

                double aAcross = horizontalLine ? a.Y : a.X;
                double bAcross = horizontalLine ? b.Y : b.X;
                double aAlong = horizontalLine ? a.X : a.Y;
                double bAlong = horizontalLine ? b.X : b.Y;

                double denom = bAcross - aAcross;

                if (Math.Abs(denom) < PointTolerance)
                {
                    // Canh song song voi duong kich thuoc: chi la loi khi nam dung tren no.
                    if (Math.Abs(aAcross - fixedCoord) < PointTolerance &&
                        IntervalsOverlap(Math.Min(aAlong, bAlong), Math.Max(aAlong, bAlong), lo + eps, hi - eps))
                    {
                        crossings++;
                    }

                    continue;
                }

                double t = (fixedCoord - aAcross) / denom;
                if (t < 0.0 || t >= 1.0)
                {
                    continue;
                }

                double hit = aAlong + (bAlong - aAlong) * t;
                if (hit > lo + eps && hit < hi - eps)
                {
                    crossings++;
                }
            }

            return crossings;
        }

        public static double SignedArea(IList<DimPoint> points)
        {
            if (points == null || points.Count < 3)
            {
                return 0.0;
            }

            double area = 0.0;
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                DimPoint current = points[i];
                DimPoint next = points[(i + 1) % count];
                area += current.X * next.Y - next.X * current.Y;
            }

            return area * 0.5;
        }

        public static bool IntervalsOverlap(double aLow, double aHigh, double bLow, double bHigh)
        {
            return aLow < bHigh && aHigh > bLow;
        }
    }
}
