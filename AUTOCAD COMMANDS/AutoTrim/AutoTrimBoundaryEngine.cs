using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AUTOCAD_COMMANDS
{
    public enum AutoTrimAction
    {
        KeepUnchanged = 0,
        SplitAndKeepSome = 1,
        EraseCompletely = 2
    }

    public class AutoTrimPiece : IDisposable
    {
        public Curve Curve { get; set; }
        public bool IsRemove { get; set; }
        public Point3d MidPoint { get; set; }
        public double Length { get; set; }
        public double StartParam { get; set; }
        public double EndParam { get; set; }

        public void Dispose()
        {
            if (Curve != null && !Curve.IsDisposed)
            {
                Curve.Dispose();
                Curve = null;
            }
        }
    }

    public class AutoTrimEntityPlan : IDisposable
    {
        public ObjectId EntityId { get; set; }
        public Curve OriginalCurve { get; set; }
        public AutoTrimAction Action { get; set; } = AutoTrimAction.KeepUnchanged;
        public List<AutoTrimPiece> Pieces { get; } = new List<AutoTrimPiece>();
        public List<Point3d> IntersectionPoints { get; } = new List<Point3d>();

        public bool HasIntersections => IntersectionPoints.Count > 0;
        public IEnumerable<AutoTrimPiece> KeepPieces => Pieces.Where(p => !p.IsRemove);
        public IEnumerable<AutoTrimPiece> RemovePieces => Pieces.Where(p => p.IsRemove);

        public void Dispose()
        {
            foreach (var p in Pieces)
            {
                p.Dispose();
            }
            Pieces.Clear();
        }
    }

    public class AutoTrimAnalysisResult : IDisposable
    {
        public ObjectId BoundaryId { get; set; }
        public Curve BoundaryCurve { get; set; }
        public List<AutoTrimEntityPlan> EntityPlans { get; } = new List<AutoTrimEntityPlan>();

        public int TotalProcessedCount => EntityPlans.Count;
        public int AffectedEntitiesCount => EntityPlans.Count(p => p.Action != AutoTrimAction.KeepUnchanged);
        public int ErasedCompletelyCount => EntityPlans.Count(p => p.Action == AutoTrimAction.EraseCompletely);
        public int SplitEntitiesCount => EntityPlans.Count(p => p.Action == AutoTrimAction.SplitAndKeepSome);
        public int PiecesToRemoveCount => EntityPlans.Where(p => p.Action != AutoTrimAction.KeepUnchanged).Sum(p => p.Pieces.Count(pc => pc.IsRemove)) + ErasedCompletelyCount;
        public int PiecesToKeepCount => EntityPlans.Where(p => p.Action != AutoTrimAction.KeepUnchanged).Sum(p => p.Pieces.Count(pc => !pc.IsRemove));

        public double TotalRemovedLength
        {
            get
            {
                double total = 0.0;
                foreach (var plan in EntityPlans)
                {
                    if (plan.Action == AutoTrimAction.KeepUnchanged) continue;

                    if (plan.Action == AutoTrimAction.EraseCompletely && plan.OriginalCurve != null)
                    {
                        total += AutoCutGeometryEngine.GetCurveLength(plan.OriginalCurve);
                    }
                    else
                    {
                        total += plan.Pieces.Where(p => p.IsRemove).Sum(p => p.Length);
                    }
                }
                return total;
            }
        }

        public void Dispose()
        {
            foreach (var plan in EntityPlans)
            {
                plan.Dispose();
            }
            EntityPlans.Clear();
        }
    }

    public static class AutoTrimBoundaryEngine
    {
        private const double Epsilon = 1e-6;

        public static bool IsClosedCurve(Entity entity)
        {
            if (entity == null) return false;
            if (entity is Circle) return true;

            if (entity is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
            {
                if (pl.Closed) return true;
                if (pl.NumberOfVertices >= 3)
                {
                    try
                    {
                        Point3d startPt = pl.GetPoint3dAt(0);
                        Point3d endPt = pl.GetPoint3dAt(pl.NumberOfVertices - 1);
                        if (startPt.DistanceTo(endPt) <= 1e-3) return true;
                    }
                    catch { }
                }
                return false;
            }

            if (entity is Polyline2d p2d)
            {
                if (p2d.Closed) return true;
                try
                {
                    if (p2d.StartPoint.DistanceTo(p2d.EndPoint) <= 1e-3) return true;
                }
                catch { }
                return false;
            }

            if (entity is Ellipse el)
            {
                return el.StartParam == 0.0 && Math.Abs(el.EndParam - (2.0 * Math.PI)) <= 1e-3;
            }

            if (entity is Spline sp)
            {
                if (sp.Closed) return true;
                try
                {
                    if (sp.StartPoint.DistanceTo(sp.EndPoint) <= 1e-3) return true;
                }
                catch { }
                return false;
            }

            return false;
        }

        public static bool IsInsideClosedCurve(Point3d pt, Entity boundary, double tolerance = 0.01)
        {
            if (boundary == null) return false;

            // 1. Fast bounding box check
            try
            {
                if (boundary.Bounds.HasValue)
                {
                    Extents3d ext = boundary.Bounds.Value;
                    if (pt.X < ext.MinPoint.X - tolerance || pt.X > ext.MaxPoint.X + tolerance ||
                        pt.Y < ext.MinPoint.Y - tolerance || pt.Y > ext.MaxPoint.Y + tolerance)
                    {
                        return false;
                    }
                }
            }
            catch { }

            // 2. Circle
            if (boundary is Circle circle)
            {
                double dist = pt.DistanceTo(circle.Center);
                return dist <= circle.Radius + tolerance;
            }

            // 3. Ellipse
            if (boundary is Ellipse ellipse)
            {
                Vector3d v = pt - ellipse.Center;
                Vector3d majorDir = ellipse.MajorAxis.GetNormal();
                Vector3d normal = ellipse.Normal.GetNormal();
                Vector3d minorDir = normal.CrossProduct(majorDir).GetNormal();

                double x = v.DotProduct(majorDir);
                double y = v.DotProduct(minorDir);
                double a = ellipse.MajorRadius;
                double b = ellipse.MinorRadius;

                if (a <= Epsilon || b <= Epsilon) return false;
                return (x * x) / (a * a) + (y * y) / (b * b) <= 1.0 + (tolerance / a);
            }

            // 4. Lightweight Polyline (handles straight & arc bulge segments)
            if (boundary is Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
            {
                return IsPointInPolylineWcs(pt, polyline);
            }

            // 5. Polyline2d or Spline (sample points along curve)
            if (boundary is Curve curve)
            {
                return IsPointInGeneralCurve(pt, curve);
            }

            return false;
        }

        private static bool IsPointInPolylineWcs(Point3d pt, Autodesk.AutoCAD.DatabaseServices.Polyline pl)
        {
            List<Point2d> polyPoints = new List<Point2d>();
            int numVerts = pl.NumberOfVertices;

            for (int i = 0; i < numVerts; i++)
            {
                Point3d wcsPt = pl.GetPoint3dAt(i);
                polyPoints.Add(new Point2d(wcsPt.X, wcsPt.Y));

                double bulge = pl.GetBulgeAt(i);
                if (Math.Abs(bulge) > 1e-5)
                {
                    Point3d p1 = pl.GetPoint3dAt(i);
                    Point3d p2 = pl.GetPoint3dAt((i + 1) % numVerts);
                    double angle = 4.0 * Math.Atan(bulge);
                    int steps = 8;
                    for (int s = 1; s < steps; s++)
                    {
                        double fraction = (double)s / steps;
                        double midBulge = Math.Tan(angle * fraction / 4.0);
                        Point2d sample = new Point2d(
                            p1.X + fraction * (p2.X - p1.X) - midBulge * (p2.Y - p1.Y),
                            p1.Y + fraction * (p2.Y - p1.Y) + midBulge * (p2.X - p1.X));
                        polyPoints.Add(sample);
                    }
                }
            }

            return IsPointInPolygon(new Point2d(pt.X, pt.Y), polyPoints);
        }

        private static bool IsPointInGeneralCurve(Point3d pt, Curve curve)
        {
            try
            {
                double s = curve.StartParam;
                double e = curve.EndParam;
                int steps = 120;
                List<Point2d> samples = new List<Point2d>(steps + 1);

                for (int i = 0; i <= steps; i++)
                {
                    double t = s + (e - s) * i / steps;
                    Point3d p = curve.GetPointAtParameter(t);
                    samples.Add(new Point2d(p.X, p.Y));
                }

                return IsPointInPolygon(new Point2d(pt.X, pt.Y), samples);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPointInPolygon(Point2d pt, List<Point2d> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            double px = pt.X;
            double py = pt.Y;
            int count = polygon.Count;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double xi = polygon[i].X, yi = polygon[i].Y;
                double xj = polygon[j].X, yj = polygon[j].Y;

                bool intersect = ((yi > py) != (yj > py)) &&
                                 (px < (xj - xi) * (py - yi) / (yj - yi + 1e-12) + xi);
                if (intersect)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static Point3d GetCurveMidPoint(Curve c)
        {
            if (c == null) return Point3d.Origin;
            try
            {
                double len = AutoCutGeometryEngine.GetCurveLength(c);
                if (len > 1e-5)
                {
                    return c.GetPointAtDist(len / 2.0);
                }
            }
            catch { }

            try
            {
                double midParam = (c.StartParam + c.EndParam) / 2.0;
                return c.GetPointAtParameter(midParam);
            }
            catch { }

            try
            {
                return new Point3d(
                    (c.StartPoint.X + c.EndPoint.X) / 2.0,
                    (c.StartPoint.Y + c.EndPoint.Y) / 2.0,
                    (c.StartPoint.Z + c.EndPoint.Z) / 2.0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        public static AutoTrimAnalysisResult AnalyzeBoundaryTrim(
            Database db,
            Transaction tr,
            ObjectId boundaryId,
            IEnumerable<ObjectId> candidateIds,
            AutoTrimSettings settings)
        {
            AutoTrimAnalysisResult result = new AutoTrimAnalysisResult
            {
                BoundaryId = boundaryId
            };

            if (!boundaryId.IsValid || boundaryId.IsErased) return result;
            Curve boundary = tr.GetObject(boundaryId, OpenMode.ForRead) as Curve;
            if (boundary == null || !IsClosedCurve(boundary)) return result;

            result.BoundaryCurve = boundary;
            Plane xyPlane = new Plane(Point3d.Origin, Vector3d.ZAxis);

            List<ObjectId> validCandidates = candidateIds?
                .Where(id => id.IsValid && !id.IsErased && id != boundaryId)
                .Distinct()
                .ToList() ?? new List<ObjectId>();

            foreach (ObjectId candId in validCandidates)
            {
                Curve candCurve = tr.GetObject(candId, OpenMode.ForRead) as Curve;
                if (candCurve == null) continue;

                AutoTrimEntityPlan plan = new AutoTrimEntityPlan
                {
                    EntityId = candId,
                    OriginalCurve = candCurve
                };

                // 1. Tìm tất cả giao điểm thực tế với đường mốc
                Point3dCollection rawPts = new Point3dCollection();
                try
                {
                    candCurve.IntersectWith(boundary, Intersect.OnBothOperands, rawPts, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }

                try
                {
                    candCurve.IntersectWith(boundary, Intersect.OnBothOperands, xyPlane, rawPts, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }

                // Deduplicate điểm giao
                List<Point3d> uniquePts = new List<Point3d>();
                foreach (Point3d p in rawPts)
                {
                    if (!uniquePts.Any(u => u.DistanceTo(p) <= 1e-4))
                    {
                        uniquePts.Add(p);
                    }
                }
                plan.IntersectionPoints.AddRange(uniquePts);

                // =========================================================================
                // NGUYÊN TẮC VÀNG: CHỈ CẮT CÁC ĐỐI TƯỢNG THỰC SỰ CẮT QUA ĐƯỜNG MỐC!
                // Nếu đối tượng KHÔNG có giao điểm với đường mốc -> GIỮ NGUYÊN 100%, KHÔNG XÓA!
                // =========================================================================
                if (uniquePts.Count == 0)
                {
                    plan.Action = AutoTrimAction.KeepUnchanged;
                    result.EntityPlans.Add(plan);
                    continue;
                }

                bool isCandClosed = IsClosedCurve(candCurve);

                // Tính tham số chia cắt dọc theo đường cong candidate
                List<double> splitParams = new List<double>();
                foreach (Point3d p in uniquePts)
                {
                    try
                    {
                        Point3d closePt = candCurve.GetClosestPointTo(p, false);
                        double param = candCurve.GetParameterAtPoint(closePt);
                        splitParams.Add(param);
                    }
                    catch { }
                }

                splitParams.Sort();

                double sParam = candCurve.StartParam;
                double eParam = candCurve.EndParam;

                List<double> distinctParams = new List<double>();
                for (int i = 0; i < splitParams.Count; i++)
                {
                    double p = splitParams[i];
                    if (distinctParams.Count > 0 && Math.Abs(p - distinctParams.Last()) < 1e-4) continue;
                    if (!isCandClosed && (Math.Abs(p - sParam) < 1e-4 || Math.Abs(p - eParam) < 1e-4)) continue;
                    distinctParams.Add(p);
                }

                if (distinctParams.Count == 0)
                {
                    // Chỉ chạm nhẹ ở đầu mút chứ không cắt xuyên qua -> GIỮ NGUYÊN 100%!
                    plan.Action = AutoTrimAction.KeepUnchanged;
                    result.EntityPlans.Add(plan);
                    continue;
                }

                // Chia tách đường cong thành các đoạn
                DoubleCollection dc = new DoubleCollection();
                foreach (double p in distinctParams) dc.Add(p);

                DBObjectCollection splitPieces = null;
                try
                {
                    splitPieces = candCurve.GetSplitCurves(dc);
                }
                catch
                {
                    try
                    {
                        Point3dCollection pc = new Point3dCollection();
                        foreach (Point3d pt in uniquePts) pc.Add(pt);
                        splitPieces = candCurve.GetSplitCurves(pc);
                    }
                    catch { }
                }

                if (splitPieces == null || splitPieces.Count == 0)
                {
                    plan.Action = AutoTrimAction.KeepUnchanged;
                    result.EntityPlans.Add(plan);
                    continue;
                }

                // Đánh giá từng đoạn con sau khi cắt
                foreach (DBObject obj in splitPieces)
                {
                    if (obj is Curve pieceCurve)
                    {
                        Point3d mid = GetCurveMidPoint(pieceCurve);
                        bool isInside = IsInsideClosedCurve(mid, boundary, settings.Tolerance);

                        // Ở TrimOutside: Phần bên ngoài (isInside == false) bị cắt bỏ (IsRemove = true)
                        //                Phần bên trong (isInside == true) được giữ lại (IsRemove = false)
                        bool isRemove;
                        if (settings.Mode == AutoTrimMode.TrimOutside)
                        {
                            isRemove = !isInside;
                        }
                        else
                        {
                            isRemove = isInside;
                        }

                        AutoTrimPiece piece = new AutoTrimPiece
                        {
                            Curve = pieceCurve,
                            IsRemove = isRemove,
                            MidPoint = mid,
                            Length = AutoCutGeometryEngine.GetCurveLength(pieceCurve),
                            StartParam = pieceCurve.StartParam,
                            EndParam = pieceCurve.EndParam
                        };
                        plan.Pieces.Add(piece);
                    }
                    else
                    {
                        obj.Dispose();
                    }
                }

                int removeCount = plan.Pieces.Count(p => p.IsRemove);
                int keepCount = plan.Pieces.Count(p => !p.IsRemove);

                if (removeCount == 0)
                {
                    // Toàn bộ các đoạn đều được giữ lại -> Không cần sửa database
                    plan.Action = AutoTrimAction.KeepUnchanged;
                }
                else if (keepCount == 0)
                {
                    // Toàn bộ các đoạn đều bị cắt bỏ
                    plan.Action = AutoTrimAction.EraseCompletely;
                }
                else
                {
                    // Cắt đứt: Giữ lại các đoạn bên trong, xóa các đoạn thò ra ngoài
                    plan.Action = AutoTrimAction.SplitAndKeepSome;
                }

                result.EntityPlans.Add(plan);
            }

            return result;
        }

        public static bool CommitBoundaryTrim(Transaction tr, AutoTrimAnalysisResult analysis, Database db)
        {
            if (analysis == null || tr == null || db == null) return false;

            Dictionary<ObjectId, BlockTableRecord> spaceCache = new Dictionary<ObjectId, BlockTableRecord>();

            foreach (AutoTrimEntityPlan plan in analysis.EntityPlans)
            {
                if (plan.EntityId == analysis.BoundaryId) continue; // Boundary TUYỆT ĐỐI KHÔNG BAO GIỜ BỊ SỬA!
                if (plan.Action == AutoTrimAction.KeepUnchanged) continue;

                Entity origEnt = tr.GetObject(plan.EntityId, OpenMode.ForWrite) as Entity;
                if (origEnt == null || origEnt.IsErased) continue;

                if (!spaceCache.TryGetValue(origEnt.OwnerId, out BlockTableRecord btr))
                {
                    btr = tr.GetObject(origEnt.OwnerId, OpenMode.ForWrite) as BlockTableRecord;
                    spaceCache[origEnt.OwnerId] = btr;
                }

                if (plan.Action == AutoTrimAction.EraseCompletely)
                {
                    origEnt.Erase();
                }
                else if (plan.Action == AutoTrimAction.SplitAndKeepSome)
                {
                    // Thêm các đoạn giữ lại vào Database, bảo tồn nguyên vẹn Layer, Color, Linetype, LineWeight
                    foreach (AutoTrimPiece piece in plan.Pieces)
                    {
                        if (!piece.IsRemove && piece.Curve != null)
                        {
                            Curve newCurve = piece.Curve.Clone() as Curve;
                            if (newCurve != null)
                            {
                                newCurve.SetPropertiesFrom(origEnt);
                                btr.AppendEntity(newCurve);
                                tr.AddNewlyCreatedDBObject(newCurve, true);
                            }
                        }
                    }

                    // Xóa đối tượng gốc chưa cắt
                    origEnt.Erase();
                }
            }

            return true;
        }
    }
}
