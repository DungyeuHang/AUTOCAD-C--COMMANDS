using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    // ==========================================================================================
    // GHOPHOI - NESTING CORE: integer geometry
    // ------------------------------------------------------------------------------------------
    // Everything in Nesting/Core is plain C#. It MUST NOT reference Autodesk assemblies so that
    // it can be unit-tested outside AutoCAD and can later run on a worker thread.
    //
    // Coordinates are stored as long integers with a resolution of 0.001 mm (NestUnits.PerMm).
    // Products of two coordinate differences stay far below long.MaxValue for any realistic
    // sheet (6 m sheet -> 6e6 units -> products ~1e14), so orientation tests are exact.
    // ==========================================================================================

    public static class NestUnits
    {
        public const double PerMm = 1000.0;

        public static long ToUnits(double mm)
        {
            return (long)Math.Round(mm * PerMm, MidpointRounding.AwayFromZero);
        }

        public static double ToMm(long units)
        {
            return units / PerMm;
        }

        public static double ToMm(double units)
        {
            return units / PerMm;
        }
    }

    public struct IntPoint : IEquatable<IntPoint>
    {
        public readonly long X;
        public readonly long Y;

        public IntPoint(long x, long y)
        {
            X = x;
            Y = y;
        }

        public static IntPoint FromMm(double x, double y)
        {
            return new IntPoint(NestUnits.ToUnits(x), NestUnits.ToUnits(y));
        }

        public bool Equals(IntPoint other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is IntPoint && Equals((IntPoint)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.###}, {1:0.###})", NestUnits.ToMm(X), NestUnits.ToMm(Y));
        }
    }

    public struct LongRect
    {
        public readonly long MinX;
        public readonly long MinY;
        public readonly long MaxX;
        public readonly long MaxY;

        public LongRect(long minX, long minY, long maxX, long maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public long Width { get { return MaxX - MinX; } }

        public long Height { get { return MaxY - MinY; } }

        public LongRect Offset(long dx, long dy)
        {
            return new LongRect(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);
        }

        /// <summary>True when the two rectangles are closer than <paramref name="clearance"/>.</summary>
        public bool Overlaps(LongRect other, long clearance)
        {
            return MinX < other.MaxX + clearance && other.MinX < MaxX + clearance &&
                   MinY < other.MaxY + clearance && other.MinY < MaxY + clearance;
        }

        public static LongRect FromPoints(IList<IntPoint> points)
        {
            long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                IntPoint p = points[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            return new LongRect(minX, minY, maxX, maxY);
        }
    }

    /// <summary>
    /// A part polygon: one outer ring plus zero or more hole rings. Rings are implicitly closed
    /// (last vertex connects to the first). Outer is stored CCW, holes CW.
    /// </summary>
    public sealed class PolyShape
    {
        private PolyShape(IntPoint[] outer, IntPoint[][] holes)
        {
            Outer = outer;
            Holes = holes;
            Bounds = LongRect.FromPoints(outer);

            double area = Math.Abs(GeometryMath.SignedArea(outer));
            foreach (IntPoint[] hole in holes)
            {
                area -= Math.Abs(GeometryMath.SignedArea(hole));
            }

            NetArea = area;
            OuterArea = Math.Abs(GeometryMath.SignedArea(outer));
        }

        public IntPoint[] Outer { get; private set; }

        public IntPoint[][] Holes { get; private set; }

        public LongRect Bounds { get; private set; }

        /// <summary>Outer area minus holes, in square units (0.001 mm)^2.</summary>
        public double NetArea { get; private set; }

        public double OuterArea { get; private set; }

        public int VertexCount
        {
            get
            {
                int n = Outer.Length;
                foreach (IntPoint[] h in Holes) n += h.Length;
                return n;
            }
        }

        public static PolyShape Create(IList<IntPoint> outer, IEnumerable<IList<IntPoint>> holes)
        {
            IntPoint[] o = GeometryMath.CleanRing(outer);
            if (GeometryMath.SignedArea(o) < 0) Array.Reverse(o);

            List<IntPoint[]> hs = new List<IntPoint[]>();
            if (holes != null)
            {
                foreach (IList<IntPoint> hole in holes)
                {
                    IntPoint[] h = GeometryMath.CleanRing(hole);
                    if (h.Length < 3) continue;
                    if (GeometryMath.SignedArea(h) > 0) Array.Reverse(h);
                    hs.Add(h);
                }
            }

            return new PolyShape(o, hs.ToArray());
        }

        public static PolyShape Rectangle(double widthMm, double heightMm)
        {
            long w = NestUnits.ToUnits(widthMm), h = NestUnits.ToUnits(heightMm);
            return Create(new[] { new IntPoint(0, 0), new IntPoint(w, 0), new IntPoint(w, h), new IntPoint(0, h) }, null);
        }

        /// <summary>Applies the orientation (mirror, then rotation) and a translation.</summary>
        public PolyShape Transform(OrientationTransform orientation, long tx, long ty)
        {
            IntPoint[] o = new IntPoint[Outer.Length];
            for (int i = 0; i < o.Length; i++) o[i] = orientation.Apply(Outer[i], tx, ty);

            IntPoint[][] hs = new IntPoint[Holes.Length][];
            for (int k = 0; k < Holes.Length; k++)
            {
                IntPoint[] src = Holes[k];
                IntPoint[] dst = new IntPoint[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = orientation.Apply(src[i], tx, ty);
                hs[k] = dst;
            }

            // Mirroring flips winding; Create() re-normalises it.
            return Create(o, hs);
        }

        public PolyShape Translate(long tx, long ty)
        {
            return Transform(OrientationTransform.Identity, tx, ty);
        }
    }

    /// <summary>
    /// Canonical part orientation: first mirror about the local Y axis (x -> -x) when
    /// <see cref="Mirror"/> is set, then rotate CCW by <see cref="RotationDeg"/>.
    /// This is THE definition shared by the decoder, the validator and the DWG output
    /// (BlockReference: ScaleFactors.X = -1 when mirrored, Rotation = RotationDeg).
    /// Quarter turns are computed exactly in integers.
    /// </summary>
    public struct OrientationTransform : IEquatable<OrientationTransform>
    {
        public static readonly OrientationTransform Identity = new OrientationTransform(0.0, false);

        public readonly double RotationDeg;
        public readonly bool Mirror;
        private readonly int _quarter;     // 0..3 for exact quarter turns, -1 otherwise
        private readonly double _cos;
        private readonly double _sin;

        public OrientationTransform(double rotationDeg, bool mirror)
        {
            double r = rotationDeg % 360.0;
            if (r < 0) r += 360.0;
            if (Math.Abs(r - 360.0) < 1e-9) r = 0.0;

            RotationDeg = r;
            Mirror = mirror;

            _quarter = -1;
            for (int q = 0; q < 4; q++)
            {
                if (Math.Abs(r - q * 90.0) < 1e-9)
                {
                    _quarter = q;
                    RotationDeg = q * 90.0;
                }
            }

            double rad = RotationDeg * Math.PI / 180.0;
            _cos = Math.Cos(rad);
            _sin = Math.Sin(rad);
        }

        public bool IsQuarterTurn { get { return _quarter >= 0; } }

        public IntPoint Apply(IntPoint p, long tx, long ty)
        {
            long x = Mirror ? -p.X : p.X;
            long y = p.Y;

            switch (_quarter)
            {
                case 0: return new IntPoint(x + tx, y + ty);
                case 1: return new IntPoint(-y + tx, x + ty);
                case 2: return new IntPoint(-x + tx, -y + ty);
                case 3: return new IntPoint(y + tx, -x + ty);
                default:
                    return new IntPoint(
                        (long)Math.Round(x * _cos - y * _sin) + tx,
                        (long)Math.Round(x * _sin + y * _cos) + ty);
            }
        }

        public bool Equals(OrientationTransform other)
        {
            return Math.Abs(RotationDeg - other.RotationDeg) < 1e-9 && Mirror == other.Mirror;
        }

        public override bool Equals(object obj)
        {
            return obj is OrientationTransform && Equals((OrientationTransform)obj);
        }

        public override int GetHashCode()
        {
            return RotationDeg.GetHashCode() ^ (Mirror ? 1 : 0);
        }

        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.###} deg{1}", RotationDeg, Mirror ? " mirrored" : string.Empty);
        }
    }

    public static class GeometryMath
    {
        /// <summary>Signed area (CCW positive) in square units.</summary>
        public static double SignedArea(IList<IntPoint> ring)
        {
            int n = ring.Count;
            if (n < 3) return 0.0;

            // Accumulate relative to the first vertex to keep magnitudes small.
            long ox = ring[0].X, oy = ring[0].Y;
            double sum = 0.0;
            for (int i = 1; i < n - 1; i++)
            {
                long ax = ring[i].X - ox, ay = ring[i].Y - oy;
                long bx = ring[i + 1].X - ox, by = ring[i + 1].Y - oy;
                sum += (double)ax * by - (double)ay * bx;
            }

            return sum * 0.5;
        }

        /// <summary>Removes consecutive duplicates (including closing duplicate) and collinear spikes.</summary>
        public static IntPoint[] CleanRing(IList<IntPoint> ring)
        {
            List<IntPoint> pts = new List<IntPoint>(ring.Count);
            for (int i = 0; i < ring.Count; i++)
            {
                if (pts.Count > 0 && pts[pts.Count - 1].Equals(ring[i])) continue;
                pts.Add(ring[i]);
            }

            while (pts.Count > 1 && pts[0].Equals(pts[pts.Count - 1]))
            {
                pts.RemoveAt(pts.Count - 1);
            }

            // Remove exactly collinear vertices (keeps the shape identical, fewer edges).
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count > 3; i++)
                {
                    IntPoint a = pts[(i + pts.Count - 1) % pts.Count];
                    IntPoint b = pts[i];
                    IntPoint c = pts[(i + 1) % pts.Count];
                    if (Cross(a, b, c) == 0 && Dot(b, a, c) <= 0)
                    {
                        // b lies on segment a-c
                        pts.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }
            }

            return pts.ToArray();
        }

        /// <summary>Cross product (b - a) x (c - a). Exact for realistic coordinates.</summary>
        public static long Cross(IntPoint a, IntPoint b, IntPoint c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        /// <summary>Dot product (a - p) . (c - p).</summary>
        private static long Dot(IntPoint p, IntPoint a, IntPoint c)
        {
            return (a.X - p.X) * (c.X - p.X) + (a.Y - p.Y) * (c.Y - p.Y);
        }

        private static int Sign(long v)
        {
            return v > 0 ? 1 : (v < 0 ? -1 : 0);
        }

        private static bool OnSegment(IntPoint a, IntPoint b, IntPoint p)
        {
            return Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) &&
                   Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);
        }

        /// <summary>Exact test: do closed segments ab and cd share at least one point?</summary>
        public static bool SegmentsIntersect(IntPoint a, IntPoint b, IntPoint c, IntPoint d)
        {
            int d1 = Sign(Cross(c, d, a));
            int d2 = Sign(Cross(c, d, b));
            int d3 = Sign(Cross(a, b, c));
            int d4 = Sign(Cross(a, b, d));

            if (d1 * d2 < 0 && d3 * d4 < 0) return true;
            if (d1 == 0 && OnSegment(c, d, a)) return true;
            if (d2 == 0 && OnSegment(c, d, b)) return true;
            if (d3 == 0 && OnSegment(a, b, c)) return true;
            if (d4 == 0 && OnSegment(a, b, d)) return true;
            return false;
        }

        /// <summary>
        /// Diem giao cua hai doan DA BIET la co cham nhau.
        ///
        /// Phai la diem giao THAT chu khong phai dinh dau cua doan: tren mot chi tiet dai
        /// 2446 mm, dinh dau co the cach cho cham hang tram mm, chi ra sai cho con te hon
        /// la khong chi. Hai doan song song / trung nhau thi lay dau mut nam tren doan kia.
        /// </summary>
        public static IntPoint SegmentCrossPoint(IntPoint a, IntPoint b, IntPoint c, IntPoint d)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double cdx = d.X - c.X, cdy = d.Y - c.Y;
            double denom = abx * cdy - aby * cdx;

            if (Math.Abs(denom) > 1e-9)
            {
                double t = ((c.X - a.X) * cdy - (c.Y - a.Y) * cdx) / denom;
                if (t < 0.0) t = 0.0;
                else if (t > 1.0) t = 1.0;

                return new IntPoint(
                    (long)Math.Round(a.X + t * abx, MidpointRounding.AwayFromZero),
                    (long)Math.Round(a.Y + t * aby, MidpointRounding.AwayFromZero));
            }

            // Song song hoac trung nhau: lay dau mut nao thuc su nam tren doan kia.
            if (OnSegment(c, d, a)) return a;
            if (OnSegment(c, d, b)) return b;
            if (OnSegment(a, b, c)) return c;
            return d;
        }

        public static double PointSegmentDistanceSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0.0;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double qx = ax + t * dx - px, qy = ay + t * dy - py;
            return qx * qx + qy * qy;
        }

        /// <summary>Squared minimum distance between closed segments ab and cd (0 when they touch).</summary>
        public static double SegmentDistanceSquared(IntPoint a, IntPoint b, IntPoint c, IntPoint d)
        {
            if (SegmentsIntersect(a, b, c, d)) return 0.0;

            double m = PointSegmentDistanceSquared(a.X, a.Y, c.X, c.Y, d.X, d.Y);
            double t = PointSegmentDistanceSquared(b.X, b.Y, c.X, c.Y, d.X, d.Y);
            if (t < m) m = t;
            t = PointSegmentDistanceSquared(c.X, c.Y, a.X, a.Y, b.X, b.Y);
            if (t < m) m = t;
            t = PointSegmentDistanceSquared(d.X, d.Y, a.X, a.Y, b.X, b.Y);
            if (t < m) m = t;
            return m;
        }

        /// <summary>
        /// Point in ring test (crossing number). Returns 1 inside, 0 on boundary, -1 outside.
        /// </summary>
        public static int PointInRing(IntPoint p, IList<IntPoint> ring)
        {
            int n = ring.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                IntPoint a = ring[j], b = ring[i];
                if (Cross(a, b, p) == 0 && OnSegment(a, b, p)) return 0;

                if ((b.Y > p.Y) != (a.Y > p.Y))
                {
                    // x coordinate of intersection compared exactly: p.X < a.X + (p.Y-a.Y)*(b.X-a.X)/(b.Y-a.Y)
                    long lhs = (p.X - a.X) * (b.Y - a.Y);
                    long rhs = (p.Y - a.Y) * (b.X - a.X);
                    bool left = (b.Y - a.Y) > 0 ? lhs < rhs : lhs > rhs;
                    if (left) inside = !inside;
                }
            }

            return inside ? 1 : -1;
        }

        /// <summary>True when p is strictly in the material of the shape (inside outer, outside holes).</summary>
        public static bool PointInMaterial(IntPoint p, PolyShape shape)
        {
            if (PointInRing(p, shape.Outer) <= 0) return false;
            foreach (IntPoint[] hole in shape.Holes)
            {
                if (PointInRing(p, hole) >= 0) return false;
            }

            return true;
        }

        /// <summary>
        /// O(n^2) simplicity check with bounding-box rejection. Adjacent edges may share their
        /// common vertex; any other contact is reported.
        /// </summary>
        public static bool IsSimpleRing(IList<IntPoint> ring, out int edgeA, out int edgeB)
        {
            edgeA = edgeB = -1;
            int n = ring.Count;
            if (n < 3) return false;

            for (int i = 0; i < n; i++)
            {
                IntPoint a = ring[i], b = ring[(i + 1) % n];
                long minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
                long minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);

                for (int j = i + 1; j < n; j++)
                {
                    bool adjacent = j == i + 1 || (i == 0 && j == n - 1);
                    IntPoint c = ring[j], d = ring[(j + 1) % n];
                    if (Math.Max(c.X, d.X) < minX || Math.Min(c.X, d.X) > maxX ||
                        Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY)
                    {
                        continue;
                    }

                    if (adjacent)
                    {
                        // Adjacent edges only share one vertex; overlapping back-tracking is invalid.
                        IntPoint shared = j == i + 1 ? b : a;
                        IntPoint other1 = j == i + 1 ? a : b;
                        IntPoint other2 = j == i + 1 ? d : c;
                        if (Cross(shared, other1, other2) == 0 && Dot(shared, other1, other2) > 0)
                        {
                            edgeA = i;
                            edgeB = j;
                            return false;
                        }

                        continue;
                    }

                    if (SegmentsIntersect(a, b, c, d))
                    {
                        edgeA = i;
                        edgeB = j;
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>True when any edge of ring r1 touches or crosses any edge of ring r2.</summary>
        public static bool RingsTouch(IList<IntPoint> r1, IList<IntPoint> r2)
        {
            IntPoint ignored;
            return RingsTouch(r1, r2, out ignored);
        }

        /// <summary>
        /// Nhu tren, nhung noi luon CHO cham (dau mut doan dau tien cua r1 co va cham).
        ///
        /// Biet cho cham moi sua duoc ban ve: mot chi tiet dai 2446 mm voi 27 lo ma chi bao
        /// "co cho cham" thi nguoi dung khong biet tim o dau.
        /// </summary>
        public static bool RingsTouch(IList<IntPoint> r1, IList<IntPoint> r2, out IntPoint at)
        {
            at = default(IntPoint);

            LongRect b1 = LongRect.FromPoints(r1), b2 = LongRect.FromPoints(r2);
            if (!b1.Overlaps(b2, 1)) return false;

            for (int i = 0; i < r1.Count; i++)
            {
                IntPoint a = r1[i], b = r1[(i + 1) % r1.Count];
                if (Math.Max(a.X, b.X) < b2.MinX || Math.Min(a.X, b.X) > b2.MaxX ||
                    Math.Max(a.Y, b.Y) < b2.MinY || Math.Min(a.Y, b.Y) > b2.MaxY)
                {
                    continue;
                }

                for (int j = 0; j < r2.Count; j++)
                {
                    IntPoint c = r2[j], d = r2[(j + 1) % r2.Count];
                    if (SegmentsIntersect(a, b, c, d))
                    {
                        at = SegmentCrossPoint(a, b, c, d);
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
