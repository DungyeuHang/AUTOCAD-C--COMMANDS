using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - PHAN TICH HINH HOC (buoc ANALYZE)
    // ------------------------------------------------------------------------------------------
    // Bien polyline tho thanh mot mo hinh noi bo day du thong tin de buoc PLAN lam viec:
    // phan loai ngang/doc/xien, gop doan thang hang, loai doan suy bien, tinh bao hinh chinh xac
    // (ke ca cung), tinh phap tuyen HUONG RA NGOAI cho bien dang kin.
    // File thuan, khong tham chieu AutoCAD.
    // ==========================================================================================

    public sealed class DimPlineVertex
    {
        public double X;
        public double Y;

        /// <summary>Bulge cua doan BAT DAU tu dinh nay (quy uoc AutoCAD).</summary>
        public double Bulge;

        public DimPlineVertex()
        {
        }

        public DimPlineVertex(double x, double y, double bulge)
        {
            X = x;
            Y = y;
            Bulge = bulge;
        }

        public DimPoint ToPoint()
        {
            return new DimPoint(X, Y);
        }
    }

    public sealed class DimPlineInput
    {
        public List<DimPlineVertex> Vertices { get; private set; }

        public bool Closed { get; set; }

        public DimPlineInput()
        {
            Vertices = new List<DimPlineVertex>();
        }

        public void Add(double x, double y)
        {
            Vertices.Add(new DimPlineVertex(x, y, 0.0));
        }

        public void Add(double x, double y, double bulge)
        {
            Vertices.Add(new DimPlineVertex(x, y, bulge));
        }
    }

    public sealed class DimPlineSegment
    {
        public int Index;

        /// <summary>Chi so dinh bat dau tren polyline GOC (truoc khi gop).</summary>
        public int StartVertexIndex;

        public DimPoint Start;
        public DimPoint End;

        public bool IsArc;
        public double Bulge;
        public DimPoint ArcCenter;
        public double ArcRadius;
        public double ArcStartAngle;
        public double ArcSweepAngle;

        /// <summary>Chieu dai that: day cung voi doan thang, do dai cung voi doan cung.</summary>
        public double Length;

        public DimOrientation Orientation;
        public DimPoint Mid;
        public DimBox Bounds;

        public int PreviousIndex = -1;
        public int NextIndex = -1;

        /// <summary>Chi co nghia voi bien dang KIN (mo thi khong xac dinh duoc trong/ngoai).</summary>
        public bool HasOutwardNormal;

        public double OutwardX;
        public double OutwardY;

        /// <summary>Goc tai dinh Start la goc LOI cua bien dang (chi co nghia khi kin).</summary>
        public bool IsConvexAtStart;

        public bool IsConcaveAtStart { get { return HasOutwardNormal && !IsConvexAtStart; } }

        public bool IsHorizontal { get { return Orientation == DimOrientation.Horizontal; } }

        public bool IsVertical { get { return Orientation == DimOrientation.Vertical; } }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0} {1} {2} -> {3} len={4:0.###}{5}",
                Index, Orientation, Start, End, Length, IsArc ? " ARC" : string.Empty);
        }
    }

    public sealed class DimPlineAnalysis
    {
        public List<DimPlineSegment> Segments { get; private set; }

        /// <summary>Duong bao da lam phang (cung -> nhieu day cung nho), dung cho phep thu giao cat.</summary>
        public List<DimPoint> FlatPoints { get; private set; }

        public DimBox Bounds { get; private set; }

        public bool Closed { get; set; }

        public double SignedArea { get; set; }

        public int DroppedZeroLengthSegments { get; set; }

        public int MergedCollinearSegments { get; set; }

        public bool IsValid { get; set; }

        public string ErrorMessage { get; set; }

        public DimPlineAnalysis()
        {
            Segments = new List<DimPlineSegment>();
            FlatPoints = new List<DimPoint>();
            Bounds = new DimBox();
            IsValid = false;
            ErrorMessage = string.Empty;
        }

        public int ArcCount
        {
            get
            {
                int n = 0;
                foreach (DimPlineSegment s in Segments)
                {
                    if (s.IsArc) n++;
                }
                return n;
            }
        }

        public int SkewCount
        {
            get
            {
                int n = 0;
                foreach (DimPlineSegment s in Segments)
                {
                    if (!s.IsArc && s.Orientation == DimOrientation.Skew) n++;
                }
                return n;
            }
        }
    }

    public static class DimPlineAnalyzer
    {
        private const int ArcFlattenSteps = 16;

        public static DimPlineAnalysis Analyze(DimPlineInput input, AutoDimPlineSettings settings)
        {
            DimPlineAnalysis analysis = new DimPlineAnalysis();
            AutoDimPlineSettings cfg = settings ?? new AutoDimPlineSettings();

            if (input == null || input.Vertices == null || input.Vertices.Count < 2)
            {
                analysis.ErrorMessage = "Polyline can it nhat 2 dinh.";
                return analysis;
            }

            analysis.Closed = input.Closed;

            List<DimPlineSegment> raw = BuildRawSegments(input, analysis);
            if (raw.Count == 0)
            {
                analysis.ErrorMessage = "Polyline khong co doan nao co chieu dai (hinh hoc suy bien).";
                return analysis;
            }

            List<DimPlineSegment> merged = MergeCollinear(raw, input.Closed, cfg.AngleToleranceDegrees, analysis);

            for (int i = 0; i < merged.Count; i++)
            {
                DimPlineSegment s = merged[i];
                s.Index = i;
                Finalize(s, cfg.AngleToleranceDegrees);
                analysis.Segments.Add(s);
            }

            LinkNeighbours(analysis.Segments, input.Closed);
            BuildFlatPoints(analysis);

            foreach (DimPlineSegment s in analysis.Segments)
            {
                if (s.IsArc)
                {
                    DimMath.AddArcExtents(
                        analysis.Bounds, s.Start, s.End, s.ArcCenter, s.ArcRadius, s.ArcStartAngle, s.ArcSweepAngle);
                }
                else
                {
                    analysis.Bounds.Add(s.Start);
                    analysis.Bounds.Add(s.End);
                }
            }

            analysis.SignedArea = DimMath.SignedArea(analysis.FlatPoints);
            ComputeOutwardNormals(analysis);

            analysis.IsValid = true;
            return analysis;
        }

        // ------------------------------------------------------------------------------------
        // Doan tho: lam viec truc tiep tren DOAN chu khong tren DINH, nho vay dinh trung nhau
        // chi don gian tao ra mot doan dai 0 va bi loai - khong phai xu ly lech chi so bulge.
        // ------------------------------------------------------------------------------------
        private static List<DimPlineSegment> BuildRawSegments(DimPlineInput input, DimPlineAnalysis analysis)
        {
            List<DimPlineSegment> result = new List<DimPlineSegment>();
            int count = input.Vertices.Count;
            int segmentCount = input.Closed ? count : count - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                DimPlineVertex a = input.Vertices[i];
                DimPlineVertex b = input.Vertices[(i + 1) % count];

                DimPoint start = a.ToPoint();
                DimPoint end = b.ToPoint();
                double chord = DimMath.Distance(start, end);

                if (chord < DimMath.PointTolerance)
                {
                    // Doan dai 0 (dinh trung nhau, hoac cung tron day du) - loai bo an toan.
                    analysis.DroppedZeroLengthSegments++;
                    continue;
                }

                DimPlineSegment segment = new DimPlineSegment
                {
                    StartVertexIndex = i,
                    Start = start,
                    End = end,
                    Bulge = a.Bulge
                };

                if (Math.Abs(a.Bulge) > 1e-12)
                {
                    DimPoint center;
                    double radius, startAngle, sweep;
                    if (DimMath.TryBuildArc(start, end, a.Bulge, out center, out radius, out startAngle, out sweep))
                    {
                        segment.IsArc = true;
                        segment.ArcCenter = center;
                        segment.ArcRadius = radius;
                        segment.ArcStartAngle = startAngle;
                        segment.ArcSweepAngle = sweep;
                    }
                }

                result.Add(segment);
            }

            return result;
        }

        // ------------------------------------------------------------------------------------
        // Gop cac doan THANG lien tiep cung phuong lam mot, ke ca qua diem noi cua polyline kin.
        // Muc dich: mot canh dai bi chia lam 3 dinh chi sinh ra 1 dim chu khong phai 3 dim vun.
        // ------------------------------------------------------------------------------------
        private static List<DimPlineSegment> MergeCollinear(
            List<DimPlineSegment> raw,
            bool closed,
            double angleToleranceDeg,
            DimPlineAnalysis analysis)
        {
            List<DimPlineSegment> result = new List<DimPlineSegment>();

            foreach (DimPlineSegment segment in raw)
            {
                if (result.Count > 0 && CanMerge(result[result.Count - 1], segment, angleToleranceDeg))
                {
                    result[result.Count - 1].End = segment.End;
                    analysis.MergedCollinearSegments++;
                    continue;
                }

                result.Add(segment);
            }

            // Diem noi cua polyline kin: doan cuoi va doan dau co the cung phuong.
            while (closed && result.Count > 2 &&
                   CanMerge(result[result.Count - 1], result[0], angleToleranceDeg))
            {
                DimPlineSegment last = result[result.Count - 1];
                result[0].Start = last.Start;
                result[0].StartVertexIndex = last.StartVertexIndex;
                result.RemoveAt(result.Count - 1);
                analysis.MergedCollinearSegments++;
            }

            return result;
        }

        private static bool CanMerge(DimPlineSegment a, DimPlineSegment b, double angleToleranceDeg)
        {
            if (a.IsArc || b.IsArc)
            {
                return false;
            }

            if (!DimMath.PointsEqual(a.End, b.Start, DimMath.PointTolerance * 10.0))
            {
                return false;
            }

            double angleA = Math.Atan2(a.End.Y - a.Start.Y, a.End.X - a.Start.X);
            double angleB = Math.Atan2(b.End.Y - b.Start.Y, b.End.X - b.Start.X);
            double delta = Math.Abs(DimMath.NormalizeAngle(angleA - angleB + Math.PI) - Math.PI);

            return delta * 180.0 / Math.PI <= angleToleranceDeg;
        }

        private static void Finalize(DimPlineSegment s, double angleToleranceDeg)
        {
            s.Mid = DimMath.Mid(s.Start, s.End);
            s.Bounds = new DimBox();

            if (s.IsArc)
            {
                DimMath.AddArcExtents(
                    s.Bounds, s.Start, s.End, s.ArcCenter, s.ArcRadius, s.ArcStartAngle, s.ArcSweepAngle);
                s.Length = Math.Abs(s.ArcRadius * s.ArcSweepAngle);
                s.Orientation = DimOrientation.Skew;
                return;
            }

            s.Bounds.Add(s.Start);
            s.Bounds.Add(s.End);
            s.Length = DimMath.Distance(s.Start, s.End);

            double horizontalDeviation, verticalDeviation;
            DimMath.AxisDeviationDegrees(s.Start, s.End, out horizontalDeviation, out verticalDeviation);

            if (horizontalDeviation <= angleToleranceDeg)
            {
                s.Orientation = DimOrientation.Horizontal;
            }
            else if (verticalDeviation <= angleToleranceDeg)
            {
                s.Orientation = DimOrientation.Vertical;
            }
            else
            {
                s.Orientation = DimOrientation.Skew;
            }
        }

        private static void LinkNeighbours(List<DimPlineSegment> segments, bool closed)
        {
            int n = segments.Count;
            for (int i = 0; i < n; i++)
            {
                segments[i].PreviousIndex = i > 0 ? i - 1 : (closed ? n - 1 : -1);
                segments[i].NextIndex = i < n - 1 ? i + 1 : (closed ? 0 : -1);
            }
        }

        private static void BuildFlatPoints(DimPlineAnalysis analysis)
        {
            foreach (DimPlineSegment s in analysis.Segments)
            {
                analysis.FlatPoints.Add(s.Start);
                if (s.IsArc)
                {
                    DimMath.FlattenArc(
                        analysis.FlatPoints, s.ArcCenter, s.ArcRadius,
                        s.ArcStartAngle, s.ArcSweepAngle, ArcFlattenSteps);
                    // Diem cuoi cua cung trung voi Start cua doan sau nen bo di de khong lap.
                    if (analysis.FlatPoints.Count > 0)
                    {
                        analysis.FlatPoints.RemoveAt(analysis.FlatPoints.Count - 1);
                    }
                }
            }

            if (!analysis.Closed && analysis.Segments.Count > 0)
            {
                analysis.FlatPoints.Add(analysis.Segments[analysis.Segments.Count - 1].End);
            }
        }

        // ------------------------------------------------------------------------------------
        // Phap tuyen HUONG RA NGOAI: chi xac dinh duoc voi bien dang KIN. Voi CCW (dien tich > 0)
        // phap tuyen ngoai la (dy, -dx); voi CW la (-dy, dx).
        // ------------------------------------------------------------------------------------
        private static void ComputeOutwardNormals(DimPlineAnalysis analysis)
        {
            if (!analysis.Closed || analysis.Segments.Count < 3 || Math.Abs(analysis.SignedArea) < 1e-12)
            {
                return;
            }

            double windingSign = analysis.SignedArea > 0.0 ? 1.0 : -1.0;

            foreach (DimPlineSegment s in analysis.Segments)
            {
                double dx = s.End.X - s.Start.X;
                double dy = s.End.Y - s.Start.Y;
                double len = DimMath.Hypot(dx, dy);
                if (len < DimMath.PointTolerance)
                {
                    continue;
                }

                s.OutwardX = windingSign * dy / len;
                s.OutwardY = -windingSign * dx / len;
                s.HasOutwardNormal = true;
            }

            foreach (DimPlineSegment s in analysis.Segments)
            {
                if (s.PreviousIndex < 0)
                {
                    s.IsConvexAtStart = true;
                    continue;
                }

                DimPlineSegment prev = analysis.Segments[s.PreviousIndex];
                double px = prev.End.X - prev.Start.X;
                double py = prev.End.Y - prev.Start.Y;
                double cx = s.End.X - s.Start.X;
                double cy = s.End.Y - s.Start.Y;
                double cross = px * cy - py * cx;

                s.IsConvexAtStart = cross * windingSign > 0.0;
            }
        }
    }
}
