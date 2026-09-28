using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>A part polygon in one orientation, positioned at translation (0, 0).</summary>
    public sealed class PreparedShape
    {
        internal PreparedShape(PartGroup group, OrientationTransform orientation, PolyShape shape)
        {
            Group = group;
            Orientation = orientation;
            Shape = shape;
            Bounds = shape.Bounds;
            PolygonEdges.Collect(shape, out EdgeA, out EdgeB);
            RingBounds = RingPointTests.RingBoundsOf(shape);

            double[] ignore;
            ContactSampling.Sample(
                shape.Outer, ContactSampling.DefaultSamples,
                out ContactPoints, out ContactNormalX, out ContactNormalY, out ignore);
        }

        /// <summary>
        /// Diem tren duong bao ngoai (toa do rieng cua hinh, chua tinh tinh tien) de sinh ung
        /// vien kieu NFP.
        /// </summary>
        internal readonly IntPoint[] ContactPoints;

        /// <summary>
        /// Phap tuyen don vi huong ra ngoai tai moi <see cref="ContactPoints"/>. Hai diem chi
        /// co the AP vao nhau khi hai phap tuyen huong nguoc nhau - do la bo loc chinh de
        /// khong phai thu moi cap diem.
        /// </summary>
        internal readonly double[] ContactNormalX;

        internal readonly double[] ContactNormalY;

        public PartGroup Group { get; private set; }

        public OrientationTransform Orientation { get; private set; }

        public PolyShape Shape { get; private set; }

        public LongRect Bounds { get; private set; }

        internal readonly IntPoint[] EdgeA;
        internal readonly IntPoint[] EdgeB;

        /// <summary>Hop bao cua tung vong: [0] = vong ngoai, [1..] = cac lo. Bat bien.</summary>
        internal readonly LongRect[] RingBounds;
    }

    /// <summary>A placed polygon in sheet coordinates with a uniform-grid edge index.</summary>
    public sealed class PlacedShape
    {
        private const int GridThreshold = 24;
        private const int MaxCells = 32;

        private readonly int[][] _cells;
        private readonly int _cols;
        private readonly int _rows;
        private readonly long _cellSize;
        private readonly int[] _stamp;
        private int _stampCounter;

        internal PlacedShape(PolyShape shape, long toleranceUnits)
        {
            Shape = shape;
            Bounds = shape.Bounds;
            PolygonEdges.Collect(shape, out EdgeA, out EdgeB);
            _stamp = new int[EdgeA.Length];
            ToleranceUnits = toleranceUnits;
            RingBounds = RingPointTests.RingBoundsOf(shape);

            if (EdgeA.Length <= GridThreshold) return;

            long size = Math.Max(Bounds.Width, Bounds.Height);
            _cellSize = Math.Max(1000L, size / MaxCells + 1);
            _cols = (int)(Bounds.Width / _cellSize) + 1;
            _rows = (int)(Bounds.Height / _cellSize) + 1;

            List<int>[] cells = new List<int>[_cols * _rows];
            for (int e = 0; e < EdgeA.Length; e++)
            {
                int c0, r0, c1, r1;
                CellRange(
                    Math.Min(EdgeA[e].X, EdgeB[e].X), Math.Min(EdgeA[e].Y, EdgeB[e].Y),
                    Math.Max(EdgeA[e].X, EdgeB[e].X), Math.Max(EdgeA[e].Y, EdgeB[e].Y),
                    out c0, out r0, out c1, out r1);

                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        int k = r * _cols + c;
                        if (cells[k] == null) cells[k] = new List<int>(4);
                        cells[k].Add(e);
                    }
                }
            }

            _cells = new int[cells.Length][];
            for (int k = 0; k < cells.Length; k++)
            {
                _cells[k] = cells[k] != null ? cells[k].ToArray() : null;
            }
        }

        public PolyShape Shape { get; private set; }

        /// <summary>Approximation tolerance of the placed part (units).</summary>
        public long ToleranceUnits { get; private set; }

        public LongRect Bounds { get; private set; }

        internal readonly IntPoint[] EdgeA;
        internal readonly IntPoint[] EdgeB;

        /// <summary>Hop bao cua tung vong: [0] = vong ngoai, [1..] = cac lo. Bat bien (an toan giua cac luong).</summary>
        internal readonly LongRect[] RingBounds;

        private void CellRange(long minX, long minY, long maxX, long maxY, out int c0, out int r0, out int c1, out int r1)
        {
            c0 = Clamp((minX - Bounds.MinX) / _cellSize, _cols);
            c1 = Clamp((maxX - Bounds.MinX) / _cellSize, _cols);
            r0 = Clamp((minY - Bounds.MinY) / _cellSize, _rows);
            r1 = Clamp((maxY - Bounds.MinY) / _cellSize, _rows);
        }

        private static int Clamp(long v, int count)
        {
            if (v < 0) return 0;
            if (v >= count) return count - 1;
            return (int)v;
        }

        /// <summary>True when segment ab comes closer than sqrt(limit2) to any edge of this shape.</summary>
        internal bool AnyEdgeCloserThan(IntPoint a, IntPoint b, long clearance, double limit2)
        {
            long minX = Math.Min(a.X, b.X) - clearance, maxX = Math.Max(a.X, b.X) + clearance;
            long minY = Math.Min(a.Y, b.Y) - clearance, maxY = Math.Max(a.Y, b.Y) + clearance;
            if (maxX < Bounds.MinX || minX > Bounds.MaxX || maxY < Bounds.MinY || minY > Bounds.MaxY) return false;

            if (_cells == null)
            {
                for (int e = 0; e < EdgeA.Length; e++)
                {
                    if (EdgeCloser(e, a, b, minX, minY, maxX, maxY, limit2)) return true;
                }

                return false;
            }

            if (++_stampCounter == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _stampCounter = 1;
            }

            int c0, r0, c1, r1;
            CellRange(minX, minY, maxX, maxY, out c0, out r0, out c1, out r1);
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    int[] cell = _cells[r * _cols + c];
                    if (cell == null) continue;
                    foreach (int e in cell)
                    {
                        if (_stamp[e] == _stampCounter) continue;
                        _stamp[e] = _stampCounter;
                        if (EdgeCloser(e, a, b, minX, minY, maxX, maxY, limit2)) return true;
                    }
                }
            }

            return false;
        }

        private bool EdgeCloser(int e, IntPoint a, IntPoint b, long minX, long minY, long maxX, long maxY, double limit2)
        {
            IntPoint c = EdgeA[e], d = EdgeB[e];
            if (Math.Max(c.X, d.X) < minX || Math.Min(c.X, d.X) > maxX ||
                Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY)
            {
                return false;
            }

            return GeometryMath.SegmentDistanceSquared(a, b, c, d) < limit2;
        }
    }

    /// <summary>
    /// Diem-trong-da-giac cho phep kiem va cham - CHO RA DUNG CUNG KET QUA voi
    /// <see cref="GeometryMath.PointInRing"/> / <see cref="GeometryMath.PointInMaterial"/>, chi
    /// nhanh hon. GeometryMath (va validator dung no) giu nguyen.
    ///
    /// Da do tren ban ve that: ~50% thoi gian ghep nam o phep "dinh 0 nam trong vat lieu" cua
    /// Collides - vi ban cu tinh Cross tren MOI canh de kiem "nam tren bien", di qua IList, va
    /// duyet MOI lo (556 lo tren 47 chi tiet).
    ///
    /// Vi sao ket qua y het:
    ///   - Diem nam NGOAI hop bao dong cua vong: khong the nam tren bien (moi diem bien deu
    ///     trong hop bao dong), va so canh cat duong ngang qua diem luon chan (vong kin) hoac
    ///     bang 0 -> ban cu cung tra -1.
    ///   - "Nam tren canh" = Cross == 0 VA nam trong hop bao cua canh; doi thu tu hai dieu
    ///     kien (khong co tac dung phu) khong doi ket qua, chi bo duoc phep nhan khi diem o xa.
    ///   - Phep dem giao cat giu NGUYEN so hoc long cua ban cu.
    /// </summary>
    internal static class RingPointTests
    {
        public static LongRect[] RingBoundsOf(PolyShape shape)
        {
            LongRect[] r = new LongRect[1 + shape.Holes.Length];
            r[0] = LongRect.FromPoints(shape.Outer);
            for (int k = 0; k < shape.Holes.Length; k++) r[k + 1] = LongRect.FromPoints(shape.Holes[k]);
            return r;
        }

        /// <summary>1 trong, 0 tren bien, -1 ngoai - nhu <see cref="GeometryMath.PointInRing"/>.</summary>
        public static int PointInRing(IntPoint p, IntPoint[] ring, LongRect bounds)
        {
            if (p.X < bounds.MinX || p.X > bounds.MaxX || p.Y < bounds.MinY || p.Y > bounds.MaxY) return -1;

            int n = ring.Length;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                IntPoint a = ring[j], b = ring[i];
                if (Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) &&
                    Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y) &&
                    GeometryMath.Cross(a, b, p) == 0)
                {
                    return 0;
                }

                if ((b.Y > p.Y) != (a.Y > p.Y))
                {
                    long lhs = (p.X - a.X) * (b.Y - a.Y);
                    long rhs = (p.Y - a.Y) * (b.X - a.X);
                    bool left = (b.Y - a.Y) > 0 ? lhs < rhs : lhs > rhs;
                    if (left) inside = !inside;
                }
            }

            return inside ? 1 : -1;
        }

        /// <summary>
        /// Nhu <see cref="GeometryMath.PointInMaterial"/>; <paramref name="outer"/> tra ve luon
        /// ket qua cua vong ngoai (luon duoc tinh) de khoi tinh lai khi kiem lo kin.
        /// </summary>
        public static bool PointInMaterial(IntPoint p, PolyShape shape, LongRect[] ringBounds, out int outer)
        {
            outer = PointInRing(p, shape.Outer, ringBounds[0]);
            if (outer <= 0) return false;
            IntPoint[][] holes = shape.Holes;
            for (int k = 0; k < holes.Length; k++)
            {
                if (PointInRing(p, holes[k], ringBounds[k + 1]) >= 0) return false;
            }

            return true;
        }
    }

    internal static class PolygonEdges
    {
        public static void Collect(PolyShape shape, out IntPoint[] a, out IntPoint[] b)
        {
            int n = shape.VertexCount;
            a = new IntPoint[n];
            b = new IntPoint[n];
            int k = 0;
            AddRing(shape.Outer, a, b, ref k);
            foreach (IntPoint[] h in shape.Holes) AddRing(h, a, b, ref k);
        }

        private static void AddRing(IntPoint[] ring, IntPoint[] a, IntPoint[] b, ref int k)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                a[k] = ring[i];
                b[k] = ring[(i + 1) % ring.Length];
                k++;
            }
        }
    }

    /// <summary>
    /// Real polygon collision (not bounding boxes): two parts are compatible when
    ///   - no edge of one comes closer than PartClearance (Gap + both tolerances) to an edge
    ///     of the other, and
    ///   - neither lies inside the material of the other, and
    ///   - unless AllowPartInsideHole, neither lies inside a CLOSED hole of the other.
    /// A concave notch / pocket is outside the outer ring, so parts may always nest there.
    /// Bounding boxes are only used as a fast pre-filter.
    /// </summary>
    public sealed class PolygonCollisionModel : ICollisionModel
    {
        public PolygonCollisionModel(NestingSettings settings)
        {
            Rules = new ClearanceRules(settings);
        }

        public ClearanceRules Rules { get; private set; }

        /// <summary>
        /// CHI de phep thu / benchmark A-B: true = dung phep diem-trong-da-giac cu
        /// (<see cref="GeometryMath"/>). Hai cach cho ra CUNG ket qua; mac dinh false (nhanh).
        /// </summary>
        internal bool LegacyPointTests { get; set; }

        public PreparedShape Prepare(PartGroup group, OrientationTransform orientation)
        {
            return new PreparedShape(group, orientation, group.Shape.Polygon.Transform(orientation, 0, 0));
        }

        public bool FitsInsideSheet(PreparedShape shape, long tx, long ty, SheetSpec sheet)
        {
            long inset = Rules.BoundaryInset(shape.Group.Shape);
            LongRect b = shape.Bounds;
            return b.MinX + tx >= inset &&
                   b.MinY + ty >= inset &&
                   b.MaxX + tx <= sheet.LengthUnits - inset &&
                   b.MaxY + ty <= sheet.WidthUnits - inset;
        }

        public bool Collides(PreparedShape moving, long tx, long ty, PlacedShape placed)
        {
            long clearance = Rules.PartClearance(moving.Group.Shape.ToleranceUnits, placed.ToleranceUnits);
            LongRect mb = moving.Bounds.Offset(tx, ty);
            if (!mb.Overlaps(placed.Bounds, clearance)) return false;

            // Containment (cheap, catches deep overlaps before the edge loop).
            IntPoint m0 = moving.Shape.Outer[0];
            IntPoint m0World = new IntPoint(m0.X + tx, m0.Y + ty);
            IntPoint p0 = placed.Shape.Outer[0];
            IntPoint p0Local = new IntPoint(p0.X - tx, p0.Y - ty);

            if (LegacyPointTests)
            {
                if (GeometryMath.PointInMaterial(m0World, placed.Shape)) return true;
                if (GeometryMath.PointInMaterial(p0Local, moving.Shape)) return true;

                // Inside the outer ring but not in material = inside a closed hole. Either the
                // whole part is in that hole (part-in-part) or it crosses the hole boundary.
                if (!Rules.AllowPartInsideHole)
                {
                    if (placed.Shape.Holes.Length > 0 && GeometryMath.PointInRing(m0World, placed.Shape.Outer) >= 0) return true;
                    if (moving.Shape.Holes.Length > 0 && GeometryMath.PointInRing(p0Local, moving.Shape.Outer) >= 0) return true;
                }
            }
            else
            {
                // Cung cac phep kiem tren, cung thu tu, cung ket qua - xem RingPointTests. Ket qua
                // vong ngoai lay lai tu phep kiem vat lieu thay vi tinh lai.
                int m0Outer, p0Outer;
                if (RingPointTests.PointInMaterial(m0World, placed.Shape, placed.RingBounds, out m0Outer)) return true;
                if (RingPointTests.PointInMaterial(p0Local, moving.Shape, moving.RingBounds, out p0Outer)) return true;

                if (!Rules.AllowPartInsideHole)
                {
                    if (placed.Shape.Holes.Length > 0 && m0Outer >= 0) return true;
                    if (moving.Shape.Holes.Length > 0 && p0Outer >= 0) return true;
                }
            }

            double limit2 = (double)clearance * clearance;
            IntPoint[] ea = moving.EdgeA, eb = moving.EdgeB;
            for (int i = 0; i < ea.Length; i++)
            {
                IntPoint a = new IntPoint(ea[i].X + tx, ea[i].Y + ty);
                IntPoint b = new IntPoint(eb[i].X + tx, eb[i].Y + ty);
                if (placed.AnyEdgeCloserThan(a, b, clearance, limit2)) return true;
            }

            return false;
        }

        public PlacedShape Place(PreparedShape shape, long tx, long ty)
        {
            return new PlacedShape(shape.Shape.Translate(tx, ty), shape.Group.Shape.ToleranceUnits);
        }
    }
}
