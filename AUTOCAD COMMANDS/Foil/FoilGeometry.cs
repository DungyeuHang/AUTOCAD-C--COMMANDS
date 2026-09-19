using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - HINH HOC 2D THUAN (KHONG PHU THUOC AUTOCAD)
    // ------------------------------------------------------------------------------------------
    // Toan bo calculation engine cua DX_FOIL dung cac kieu duoi day thay vi Point2d/Vector2d cua
    // Autodesk.AutoCAD.Geometry. Muc dich:
    //   1. Engine tinh toan co the unit-test doc lap, khong can nap acdbmgd / acad.exe.
    //   2. Tang ve (FoilDrawingBuilder) la noi DUY NHAT biet den AutoCAD API.
    // ==========================================================================================

    public struct FoilVector2d
    {
        public readonly double X;
        public readonly double Y;

        public FoilVector2d(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double LengthSquared { get { return X * X + Y * Y; } }

        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }

        public FoilVector2d Normalized()
        {
            double len = Length;
            if (len <= FoilMath.LengthTolerance)
            {
                return new FoilVector2d(0.0, 0.0);
            }

            return new FoilVector2d(X / len, Y / len);
        }

        /// <summary>Quay nguoc chieu kim dong ho (CCW) quanh goc toa do.</summary>
        public FoilVector2d Rotate(double angleRad)
        {
            double c = Math.Cos(angleRad);
            double s = Math.Sin(angleRad);
            return new FoilVector2d(X * c - Y * s, X * s + Y * c);
        }

        /// <summary>Phap tuyen trai (quay +90 do). Voi da giac CCW day la phap tuyen huong vao trong.</summary>
        public FoilVector2d Perpendicular()
        {
            return new FoilVector2d(-Y, X);
        }

        public double Dot(FoilVector2d other)
        {
            return X * other.X + Y * other.Y;
        }

        /// <summary>Tich co huong 2D (thanh phan Z). Duong = re trai (CCW).</summary>
        public double Cross(FoilVector2d other)
        {
            return X * other.Y - Y * other.X;
        }

        public double AngleRad { get { return Math.Atan2(Y, X); } }

        public static FoilVector2d operator +(FoilVector2d a, FoilVector2d b)
        {
            return new FoilVector2d(a.X + b.X, a.Y + b.Y);
        }

        public static FoilVector2d operator -(FoilVector2d a, FoilVector2d b)
        {
            return new FoilVector2d(a.X - b.X, a.Y - b.Y);
        }

        public static FoilVector2d operator *(FoilVector2d v, double scalar)
        {
            return new FoilVector2d(v.X * scalar, v.Y * scalar);
        }

        public static FoilVector2d operator -(FoilVector2d v)
        {
            return new FoilVector2d(-v.X, -v.Y);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.######}, {1:0.######})", X, Y);
        }
    }

    public struct FoilPoint2d
    {
        public readonly double X;
        public readonly double Y;

        public FoilPoint2d(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double DistanceTo(FoilPoint2d other)
        {
            double dx = other.X - X;
            double dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static FoilPoint2d operator +(FoilPoint2d p, FoilVector2d v)
        {
            return new FoilPoint2d(p.X + v.X, p.Y + v.Y);
        }

        public static FoilPoint2d operator -(FoilPoint2d p, FoilVector2d v)
        {
            return new FoilPoint2d(p.X - v.X, p.Y - v.Y);
        }

        public static FoilVector2d operator -(FoilPoint2d a, FoilPoint2d b)
        {
            return new FoilVector2d(a.X - b.X, a.Y - b.Y);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####})", X, Y);
        }
    }

    public static class FoilMath
    {
        /// <summary>Sai so chieu dai mac dinh (mm) khi so sanh diem / vector.</summary>
        public const double LengthTolerance = 1e-9;

        /// <summary>Sai so goc mac dinh (radian).</summary>
        public const double AngleTolerance = 1e-7;

        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static bool IsZero(double value, double tolerance)
        {
            return Math.Abs(value) <= tolerance;
        }

        /// <summary>
        /// Goc khong dau giua 2 vector, tra ve [0, PI].
        /// Dung atan2(|cross|, dot) thay cho acos(dot) vi acos mat do chinh xac nghiem trong
        /// khi goc gan 0 hoac gan PI - day chinh la 2 vung quyet dinh "co phai bend hay khong".
        /// </summary>
        public static double AngleBetween(FoilVector2d a, FoilVector2d b)
        {
            FoilVector2d ua = a.Normalized();
            FoilVector2d ub = b.Normalized();
            if (ua.LengthSquared <= 0.0 || ub.LengthSquared <= 0.0)
            {
                return 0.0;
            }

            return Math.Atan2(Math.Abs(ua.Cross(ub)), ua.Dot(ub));
        }

        /// <summary>
        /// Dau cua huong re tai mot dinh: +1 = re trai (CCW), -1 = re phai (CW), 0 = di thang.
        ///
        /// CANH BAO - BAY DA TUNG GAY LOI:
        /// |cross(d1, d2)| = sin(alpha) NHO O CA HAI DAU: alpha gan 0 do VA alpha gan 180 do.
        /// Vi vay KHONG duoc dung nguong "co phai bend khong" (MinBendAngle) lam nguong o day:
        /// voi mot chan rat tu (vi du 178.5 do) sin(alpha) con nho hon sin(MinBendAngle), ham se
        /// tra ve 0 va hai duong chan NGUOC CHIEU nhau bi bao cung mot huong.
        ///
        /// Vi the <paramref name="crossTolerance"/> o day la nguong tren TRI SO CROSS (khong phai
        /// nguong goc), va nen de rat nho. Viec quyet dinh "co phai bend hay khong" da duoc lam
        /// rieng bang cach so sanh alpha voi MinBendAngle trong FoilProfileAnalyzer.
        /// </summary>
        public static double TurnSign(FoilVector2d incoming, FoilVector2d outgoing, double crossTolerance)
        {
            FoilVector2d ua = incoming.Normalized();
            FoilVector2d ub = outgoing.Normalized();
            double cross = ua.Cross(ub);
            if (Math.Abs(cross) <= Math.Abs(crossTolerance))
            {
                return 0.0;
            }

            return cross > 0.0 ? 1.0 : -1.0;
        }

        public static double NormalizeAngle(double angleRad)
        {
            double twoPi = Math.PI * 2.0;
            double a = angleRad % twoPi;
            if (a <= -Math.PI) a += twoPi;
            if (a > Math.PI) a -= twoPi;
            return a;
        }
    }

    /// <summary>
    /// Cat mot DUONG THANG VO HAN bang mot DA GIAC LOI.
    /// Thuat toan Liang-Barsky tong quat hoa cho nua mat phang bat ky.
    ///
    /// Day la ham phuc vu DUONG CHAN XIEN (diagonal bend line): duong chan luon duoc mo ta bang
    /// (diem goc + vector huong) trong he toa do phoi roi cat bang bien phoi.
    /// KHONG bao gio lay rieng toa do X hoac Y.
    /// </summary>
    public static class FoilLineClipper
    {
        /// <summary>
        /// Tra ve doan giao giua duong thang vo han P(t) = origin + t * direction va da giac loi.
        /// Da giac co the CW hoac CCW - ham tu xac dinh huong de lay phap tuyen huong vao trong.
        /// </summary>
        public static bool ClipInfiniteLine(
            FoilPoint2d origin,
            FoilVector2d direction,
            IList<FoilPoint2d> convexPolygon,
            out FoilPoint2d first,
            out FoilPoint2d second)
        {
            first = origin;
            second = origin;

            if (convexPolygon == null || convexPolygon.Count < 3)
            {
                return false;
            }

            FoilVector2d dir = direction.Normalized();
            if (dir.LengthSquared <= 0.0)
            {
                return false;
            }

            double orientation = SignedArea(convexPolygon) >= 0.0 ? 1.0 : -1.0;

            double tMin = double.NegativeInfinity;
            double tMax = double.PositiveInfinity;
            const double eps = 1e-12;

            for (int i = 0; i < convexPolygon.Count; i++)
            {
                FoilPoint2d a = convexPolygon[i];
                FoilPoint2d b = convexPolygon[(i + 1) % convexPolygon.Count];
                FoilVector2d edge = b - a;
                if (edge.LengthSquared <= eps)
                {
                    continue;
                }

                // Phap tuyen huong vao trong da giac.
                FoilVector2d inward = edge.Perpendicular() * orientation;

                // Rang buoc: inward . (P(t) - a) >= 0
                //         => t * (inward . dir) >= inward . (a - origin)
                double denom = inward.Dot(dir);
                double num = inward.Dot(a - origin);

                if (Math.Abs(denom) <= eps)
                {
                    // Duong thang song song canh nay: chi kha thi neu da nam trong nua mat phang.
                    if (num > eps)
                    {
                        return false;
                    }

                    continue;
                }

                double t = num / denom;
                if (denom > 0.0)
                {
                    if (t > tMin) tMin = t;
                }
                else
                {
                    if (t < tMax) tMax = t;
                }
            }

            if (double.IsInfinity(tMin) || double.IsInfinity(tMax) || tMin > tMax)
            {
                return false;
            }

            first = origin + dir * tMin;
            second = origin + dir * tMax;
            return true;
        }

        public static double SignedArea(IList<FoilPoint2d> polygon)
        {
            double area = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                FoilPoint2d p = polygon[i];
                FoilPoint2d q = polygon[(i + 1) % polygon.Count];
                area += p.X * q.Y - q.X * p.Y;
            }

            return area * 0.5;
        }
    }

    /// <summary>
    /// He toa do cuc bo cua PHOI (blank):
    ///   U = doc theo CHIEU DAI phoi  (gia tri nguoi dung nhap)
    ///   V = doc theo CHIEU RONG phoi (chieu rong trien khai tinh tu bien dang)
    ///
    /// Moi hinh hoc cua phoi duoc dung trong (U,V) roi transform MOT LAN duy nhat ve WCS.
    /// Nho do duong chan xien duoc xu ly hoan toan bang vector va van dung khi phoi duoc
    /// dat nghieng trong ban ve.
    /// </summary>
    public class FoilBlankFrame
    {
        public FoilPoint2d Origin { get; private set; }

        public FoilVector2d UAxis { get; private set; }

        public FoilVector2d VAxis { get; private set; }

        public FoilBlankFrame(FoilPoint2d origin, double rotationRad)
        {
            Origin = origin;
            UAxis = new FoilVector2d(Math.Cos(rotationRad), Math.Sin(rotationRad));
            VAxis = UAxis.Perpendicular();
        }

        public FoilPoint2d ToWorld(double u, double v)
        {
            return Origin + UAxis * u + VAxis * v;
        }

        public FoilPoint2d ToWorld(FoilPoint2d local)
        {
            return ToWorld(local.X, local.Y);
        }

        public FoilVector2d DirectionToWorld(FoilVector2d local)
        {
            return UAxis * local.X + VAxis * local.Y;
        }
    }
}
