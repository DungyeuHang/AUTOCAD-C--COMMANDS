using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AUTOCAD_COMMANDS
{
    public class AutoCutPiece : IDisposable
    {
        public Curve Curve { get; set; }
        public bool IsRemove { get; set; }
        public double StartParam { get; set; }
        public double EndParam { get; set; }
        public Point3d MidPoint { get; set; }
        public double Length { get; set; }
        public string Reason { get; set; } = string.Empty;

        public void Dispose()
        {
            if (Curve != null && !Curve.IsDisposed)
            {
                Curve.Dispose();
                Curve = null;
            }
        }
    }

    public class CutterSpanInfo
    {
        public Entity Cutter { get; set; }
        public double Param1 { get; set; }
        public double Param2 { get; set; }
    }

    public class AutoCutTargetPlan : IDisposable
    {
        public ObjectId TargetId { get; set; }
        public Curve TargetCurve { get; set; }
        public List<AutoCutPiece> Pieces { get; } = new List<AutoCutPiece>();
        public List<Point3d> IntersectionPoints { get; } = new List<Point3d>();
        public HashSet<ObjectId> ValidCutterIds { get; } = new HashSet<ObjectId>();
        public string Summary { get; set; } = string.Empty;

        public IEnumerable<AutoCutPiece> KeepPieces => Pieces.Where(p => !p.IsRemove);
        public IEnumerable<AutoCutPiece> RemovePieces => Pieces.Where(p => p.IsRemove);

        public void Dispose()
        {
            foreach (var p in Pieces)
            {
                p.Dispose();
            }
            Pieces.Clear();
        }
    }

    public class AutoCutAnalysisResult : IDisposable
    {
        public List<AutoCutTargetPlan> TargetPlans { get; } = new List<AutoCutTargetPlan>();
        public AutoCutDiagnosticReport Report { get; } = new AutoCutDiagnosticReport();

        public void Dispose()
        {
            foreach (var tp in TargetPlans)
            {
                tp.Dispose();
            }
            TargetPlans.Clear();
        }
    }

    public static class AutoCutGeometryEngine
    {
        private const double Epsilon = 1e-6;

        public static AutoCutAnalysisResult Analyze(
            Database db,
            Transaction tr,
            IEnumerable<ObjectId> targetIds,
            AutoCutSettings settings)
        {
            AutoCutAnalysisResult result = new AutoCutAnalysisResult();
            AutoCutDiagnosticReport report = result.Report;

            List<ObjectId> validTargetIds = targetIds?
                .Where(id => id.IsValid && !id.IsErased)
                .Distinct()
                .ToList() ?? new List<ObjectId>();

            report.TargetsCount = validTargetIds.Count;
            if (validTargetIds.Count == 0)
            {
                report.DetailLogs.Add("Không có target hợp lệ nào được chọn.");
                return result;
            }

            // Cache candidate entities per space (OwnerId)
            Dictionary<ObjectId, List<Entity>> spaceCandidatesCache = new Dictionary<ObjectId, List<Entity>>();

            int targetIndex = 1;
            foreach (ObjectId targetId in validTargetIds)
            {
                Entity targetEntity = tr.GetObject(targetId, OpenMode.ForRead) as Entity;
                Curve targetCurve = targetEntity as Curve;

                if (targetCurve == null)
                {
                    report.DetailLogs.Add($"Target #{targetIndex}: Không phải Curve, bỏ qua.");
                    targetIndex++;
                    continue;
                }

                // Verify Target Type filter
                if (!MatchesTargetType(targetCurve, settings))
                {
                    report.RejectedByType++;
                    report.DetailLogs.Add($"Target #{targetIndex}: Bị loại do không khớp Target Type ({settings.TargetTypes}).");
                    targetIndex++;
                    continue;
                }

                // Verify Target Layer filter
                if (!MatchesTargetLayer(targetCurve, settings, db))
                {
                    report.RejectedByLayer++;
                    report.DetailLogs.Add($"Target #{targetIndex}: Bị loại do không khớp Target Layer ({targetCurve.Layer} != {settings.TargetLayerName}).");
                    targetIndex++;
                    continue;
                }

                ObjectId ownerSpaceId = targetCurve.OwnerId;
                if (!spaceCandidatesCache.TryGetValue(ownerSpaceId, out List<Entity> candidatesInSpace))
                {
                    candidatesInSpace = LoadCandidatesInSpace(db, tr, ownerSpaceId, report);
                    spaceCandidatesCache[ownerSpaceId] = candidatesInSpace;
                }

                AutoCutTargetPlan plan = AnalyzeSingleTarget(
                    db,
                    tr,
                    targetCurve,
                    targetIndex,
                    candidatesInSpace,
                    settings,
                    report);

                result.TargetPlans.Add(plan);
                targetIndex++;
            }

            // Finalize summary counts
            RefreshSummaryCounts(result);

            return result;
        }

        public static void RefreshSummaryCounts(AutoCutAnalysisResult result)
        {
            if (result == null) return;
            AutoCutDiagnosticReport report = result.Report;
            report.SegmentsToRemoveCount = result.TargetPlans.Sum(p => p.RemovePieces.Count());
            report.SegmentsToKeepCount = result.TargetPlans.Sum(p => p.KeepPieces.Count());
            report.TargetsWithCutsCount = result.TargetPlans.Count(p => p.RemovePieces.Any());
            report.IntersectionsCount = result.TargetPlans.Sum(p => p.IntersectionPoints.Count);
            HashSet<ObjectId> allCutters = new HashSet<ObjectId>();
            foreach (var p in result.TargetPlans)
            {
                foreach (var cid in p.ValidCutterIds)
                {
                    allCutters.Add(cid);
                }
            }
            report.ValidCuttersCount = allCutters.Count;
        }

        public static void InvertClassifications(AutoCutAnalysisResult analysis)
        {
            if (analysis == null) return;

            foreach (var plan in analysis.TargetPlans)
            {
                foreach (var piece in plan.Pieces)
                {
                    piece.IsRemove = !piece.IsRemove;
                    piece.Reason = piece.IsRemove ? "Đảo ngược (Chọn Cắt Bỏ)" : "Đảo ngược (Chọn Giữ Lại)";
                }
            }

            RefreshSummaryCounts(analysis);
        }

        private static List<Entity> LoadCandidatesInSpace(
            Database db,
            Transaction tr,
            ObjectId spaceId,
            AutoCutDiagnosticReport report)
        {
            List<Entity> list = new List<Entity>();
            BlockTableRecord btr = tr.GetObject(spaceId, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return list;

            foreach (ObjectId entId in btr)
            {
                if (!entId.IsValid || entId.IsErased) continue;
                report.TotalCandidatesInSpace++;

                Entity ent = tr.GetObject(entId, OpenMode.ForRead, false, true) as Entity;
                if (ent == null || ent.IsErased) continue;

                // Fast check if it's a Curve
                if (ent is Curve)
                {
                    list.Add(ent);
                }
                else
                {
                    report.RejectedByType++;
                }
            }

            return list;
        }

        private static AutoCutTargetPlan AnalyzeSingleTarget(
            Database db,
            Transaction tr,
            Curve targetCurve,
            int targetIndex,
            List<Entity> candidatesInSpace,
            AutoCutSettings settings,
            AutoCutDiagnosticReport report)
        {
            AutoCutTargetPlan plan = new AutoCutTargetPlan
            {
                TargetId = targetCurve.ObjectId,
                TargetCurve = targetCurve
            };

            // 1. Get expanded target bounding box
            Extents3d targetExtents;
            try
            {
                targetExtents = targetCurve.GeometricExtents;
            }
            catch
            {
                report.DetailLogs.Add($"Target #{targetIndex}: Không đọc được GeometricExtents.");
                return plan;
            }

            double tMinX = targetExtents.MinPoint.X - settings.SearchTolerance;
            double tMinY = targetExtents.MinPoint.Y - settings.SearchTolerance;
            double tMaxX = targetExtents.MaxPoint.X + settings.SearchTolerance;
            double tMaxY = targetExtents.MaxPoint.Y + settings.SearchTolerance;

            // 2. Filter candidate cutters
            List<Entity> validCutters = new List<Entity>();
            List<Entity> closedCutters = new List<Entity>();

            foreach (Entity candidate in candidatesInSpace)
            {
                if (candidate.ObjectId == targetCurve.ObjectId)
                {
                    report.RejectedByTargetSelf++;
                    continue;
                }

                // Type check
                if (!IsCandidateCutterType(candidate, settings.CutterTypes))
                {
                    continue;
                }

                // Layer check
                if (!MatchesLayerFilter(candidate, targetCurve, settings, db))
                {
                    report.RejectedByLayer++;
                    continue;
                }

                // Color check
                if (!AutoCutColorHelper.MatchesFilter(candidate, targetCurve, settings, tr, db))
                {
                    report.RejectedByColor++;
                    continue;
                }

                // Bounding box AABB check
                Extents3d cExt;
                try
                {
                    cExt = candidate.GeometricExtents;
                }
                catch
                {
                    report.RejectedByBoundingBox++;
                    continue;
                }

                if (cExt.MaxPoint.X < tMinX || cExt.MinPoint.X > tMaxX ||
                    cExt.MaxPoint.Y < tMinY || cExt.MinPoint.Y > tMaxY)
                {
                    report.RejectedByBoundingBox++;
                    continue;
                }

                validCutters.Add(candidate);
                if (IsClosedCurve(candidate))
                {
                    closedCutters.Add(candidate);
                }
            }

            // 3. Find geometric intersections & record per-cutter spans
            List<IntersectionPointInfo> rawIntersections = new List<IntersectionPointInfo>();
            List<CutterSpanInfo> cutterSpans = new List<CutterSpanInfo>();

            foreach (Entity cutter in validCutters)
            {
                report.EvaluatedGeometricIntersection++;
                Point3dCollection pts = new Point3dCollection();

                try
                {
                    targetCurve.IntersectWith(cutter, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                }
                catch
                {
                    // Ignore intersection exception
                }

                // Fallback for 2D XY projection if no points found and elevation is ignored
                if (pts.Count == 0 && settings.IgnoreElevation && IsPlanar(targetCurve) && IsPlanar(cutter))
                {
                    try
                    {
                        Plane xyPlane = new Plane(Point3d.Origin, Vector3d.ZAxis);
                        targetCurve.IntersectWith(cutter, Intersect.OnBothOperands, xyPlane, pts, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch
                    {
                    }
                }

                if (pts.Count == 0)
                {
                    report.RejectedByNoIntersection++;
                    continue;
                }

                plan.ValidCutterIds.Add(cutter.ObjectId);
                List<double> cutterParams = new List<double>();

                foreach (Point3d pt in pts)
                {
                    Point3d closest;
                    try
                    {
                        closest = targetCurve.GetClosestPointTo(pt, false);
                    }
                    catch
                    {
                        continue;
                    }

                    if (closest.DistanceTo(pt) > settings.SearchTolerance * 2.0 && !settings.IgnoreElevation)
                    {
                        continue;
                    }

                    double param;
                    try
                    {
                        param = targetCurve.GetParameterAtPoint(closest);
                    }
                    catch
                    {
                        continue;
                    }

                    cutterParams.Add(param);
                    rawIntersections.Add(new IntersectionPointInfo
                    {
                        Point = closest,
                        Parameter = param,
                        Cutter = cutter
                    });
                }

                // If this cutter intersects target at 2+ points, record the cutter span
                if (cutterParams.Count >= 2)
                {
                    cutterParams.Sort();
                    for (int cp = 0; cp < cutterParams.Count - 1; cp += 2)
                    {
                        cutterSpans.Add(new CutterSpanInfo
                        {
                            Cutter = cutter,
                            Param1 = cutterParams[cp],
                            Param2 = cutterParams[cp + 1]
                        });
                    }
                }
            }

            if (rawIntersections.Count == 0)
            {
                report.DetailLogs.Add($"Target #{targetIndex} ({targetCurve.GetType().Name}, Layer: {targetCurve.Layer}): Không có giao điểm nào với các cutter.");
                return plan;
            }

            // 4. Sort and deduplicate intersections
            rawIntersections.Sort((a, b) => a.Parameter.CompareTo(b.Parameter));

            List<IntersectionPointInfo> uniqueIntersections = new List<IntersectionPointInfo>();
            foreach (var cur in rawIntersections)
            {
                if (uniqueIntersections.Count == 0)
                {
                    uniqueIntersections.Add(cur);
                }
                else
                {
                    var last = uniqueIntersections[uniqueIntersections.Count - 1];
                    bool isDuplicate = Math.Abs(cur.Parameter - last.Parameter) <= Epsilon ||
                                       cur.Point.DistanceTo(last.Point) <= settings.DeduplicationTolerance;

                    if (isDuplicate)
                    {
                        report.DuplicateIntersectionsCount++;
                    }
                    else
                    {
                        uniqueIntersections.Add(cur);
                    }
                }
            }

            foreach (var ui in uniqueIntersections)
            {
                plan.IntersectionPoints.Add(ui.Point);
            }

            // 5. Check curve boundaries and interior parameters
            double startParam = targetCurve.StartParam;
            double endParam = targetCurve.EndParam;
            Point3d startPt = targetCurve.GetPointAtParameter(startParam);
            Point3d endPt = targetCurve.GetPointAtParameter(endParam);

            bool hasIntersectionAtStart = false;
            bool hasIntersectionAtEnd = false;
            List<double> interiorSplitParams = new List<double>();

            foreach (var ui in uniqueIntersections)
            {
                bool atStart = ui.Point.DistanceTo(startPt) <= settings.DeduplicationTolerance ||
                               Math.Abs(ui.Parameter - startParam) <= Epsilon;
                bool atEnd = ui.Point.DistanceTo(endPt) <= settings.DeduplicationTolerance ||
                             Math.Abs(ui.Parameter - endParam) <= Epsilon;

                if (atStart)
                {
                    hasIntersectionAtStart = true;
                }
                else if (atEnd)
                {
                    hasIntersectionAtEnd = true;
                }
                else if (ui.Parameter > startParam + Epsilon && ui.Parameter < endParam - Epsilon)
                {
                    interiorSplitParams.Add(ui.Parameter);
                }
            }

            // Deduplicate interior parameters strictly
            interiorSplitParams = interiorSplitParams.Distinct().OrderBy(p => p).ToList();

            // 6. Handle single intersection rule
            if (uniqueIntersections.Count == 1)
            {
                if (settings.SingleIntersectionRule == AutoCutSingleIntersectionRule.Skip)
                {
                    report.SkippedSingleIntersectionCount++;
                    report.DetailLogs.Add($"Target #{targetIndex}: Bỏ qua vì chỉ có 1 giao điểm và SingleIntersection = Skip.");
                    return plan;
                }
            }

            // 7. Split Curve
            List<Curve> pieces = new List<Curve>();
            if (interiorSplitParams.Count > 0)
            {
                DoubleCollection dc = new DoubleCollection();
                foreach (double p in interiorSplitParams)
                {
                    dc.Add(p);
                }

                try
                {
                    DBObjectCollection dbSplits = targetCurve.GetSplitCurves(dc);
                    if (dbSplits != null && dbSplits.Count > 0)
                    {
                        foreach (DBObject obj in dbSplits)
                        {
                            if (obj is Curve c) pieces.Add(c);
                            else obj.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.DetailLogs.Add($"Target #{targetIndex}: Lỗi GetSplitCurves ({ex.Message}).");
                    return plan;
                }
            }
            else
            {
                // No interior split parameters: curve is unbroken
                try
                {
                    Curve clone = targetCurve.Clone() as Curve;
                    if (clone != null) pieces.Add(clone);
                }
                catch
                {
                }
            }

            if (pieces.Count == 0)
            {
                report.DetailLogs.Add($"Target #{targetIndex}: Không tạo được split curves.");
                return plan;
            }

            // 8. Copy properties to split pieces
            foreach (Curve piece in pieces)
            {
                try
                {
                    piece.LayerId = targetCurve.LayerId;
                    piece.Color = targetCurve.Color;
                    piece.LinetypeId = targetCurve.LinetypeId;
                    piece.LinetypeScale = targetCurve.LinetypeScale;
                    piece.LineWeight = targetCurve.LineWeight;
                }
                catch
                {
                }
            }

            // 9. Classify Pieces (KEEP / REMOVE) based on Cut Mode & Cutter Spans
            ClassifyPieces(
                plan,
                pieces,
                targetCurve,
                uniqueIntersections,
                closedCutters,
                cutterSpans,
                hasIntersectionAtStart,
                hasIntersectionAtEnd,
                settings);

            int remCount = plan.RemovePieces.Count();
            int keepCount = plan.KeepPieces.Count();
            report.DetailLogs.Add($"Target #{targetIndex} ({targetCurve.GetType().Name}): {uniqueIntersections.Count} giao điểm, {plan.ValidCutterIds.Count} cutter(s) -> {remCount} đoạn REMOVE, {keepCount} đoạn KEEP.");

            return plan;
        }

        private static void ClassifyPieces(
            AutoCutTargetPlan plan,
            List<Curve> splitPieces,
            Curve originalCurve,
            List<IntersectionPointInfo> intersections,
            List<Entity> closedCutters,
            List<CutterSpanInfo> cutterSpans,
            bool hasIntersectionAtStart,
            bool hasIntersectionAtEnd,
            AutoCutSettings settings)
        {
            int n = splitPieces.Count;
            List<AutoCutPiece> pieceList = new List<AutoCutPiece>();
            bool isClosedTarget = IsClosedCurve(originalCurve);

            for (int i = 0; i < n; i++)
            {
                Curve piece = splitPieces[i];
                double sParam = piece.StartParam;
                double eParam = piece.EndParam;
                double midParam = (sParam + eParam) / 2.0;
                Point3d midPt = piece.GetPointAtParameter(midParam);
                double len = GetCurveLength(piece);

                pieceList.Add(new AutoCutPiece
                {
                    Curve = piece,
                    StartParam = sParam,
                    EndParam = eParam,
                    MidPoint = midPt,
                    Length = len,
                    IsRemove = false
                });
            }

            // Helper to get parameter of midpoint on the original curve
            Func<Point3d, double> getOrigParam = (pt) =>
            {
                try
                {
                    Point3d closest = originalCurve.GetClosestPointTo(pt, false);
                    return originalCurve.GetParameterAtPoint(closest);
                }
                catch
                {
                    return 0.0;
                }
            };

            // Classification by Cut Mode
            switch (settings.CutMode)
            {
                case AutoCutCutMode.ClosedCutterRemoveInside:
                    foreach (var p in pieceList)
                    {
                        bool insideClosed = closedCutters.Any(cc => IsInsideClosedCurve(p.MidPoint, cc, settings.SearchTolerance));
                        p.IsRemove = insideClosed;
                        p.Reason = p.IsRemove ? "Nằm bên trong Closed Cutter" : "Nằm ngoài Cutter";
                    }
                    break;

                case AutoCutCutMode.ClosedCutterRemoveOutside:
                    foreach (var p in pieceList)
                    {
                        bool insideClosed = closedCutters.Any(cc => IsInsideClosedCurve(p.MidPoint, cc, settings.SearchTolerance));
                        p.IsRemove = !insideClosed;
                        p.Reason = p.IsRemove ? "Nằm bên ngoài Cutter" : "Nằm trong Cutter";
                    }
                    break;

                case AutoCutCutMode.RemoveShortest:
                    if (pieceList.Count > 1)
                    {
                        double minLen = pieceList.Min(p => p.Length);
                        double maxLen = pieceList.Max(p => p.Length);

                        if (maxLen - minLen > 1e-4)
                        {
                            // Sort distinct lengths to detect cluster boundary between notches and body
                            var sortedLengths = pieceList.Select(p => p.Length).Distinct().OrderBy(l => l).ToList();

                            double maxGap = -1.0;
                            double splitThreshold = minLen;

                            for (int k = 0; k < sortedLengths.Count - 1; k++)
                            {
                                double gap = sortedLengths[k + 1] - sortedLengths[k];
                                // A clear gap between notch group and workpiece group
                                if (gap > maxGap && (sortedLengths[k + 1] > sortedLengths[k] * 1.25 || gap > 2.0))
                                {
                                    maxGap = gap;
                                    splitThreshold = (sortedLengths[k] + sortedLengths[k + 1]) / 2.0;
                                }
                            }

                            List<AutoCutPiece> shortPieces;
                            if (maxGap > 0)
                            {
                                shortPieces = pieceList.Where(p => p.Length <= splitThreshold).ToList();
                            }
                            else
                            {
                                // Fallback: All pieces within 10% or 0.5 units of minLen
                                double tolerance = Math.Max(0.5, minLen * 0.1);
                                shortPieces = pieceList.Where(p => Math.Abs(p.Length - minLen) <= tolerance).ToList();
                            }

                            // Must keep at least one piece
                            if (shortPieces.Count > 0 && shortPieces.Count < pieceList.Count)
                            {
                                foreach (var sp in shortPieces)
                                {
                                    sp.IsRemove = true;
                                    sp.Reason = $"Đoạn ngắn / Notch ({sp.Length:F2})";
                                }
                            }
                            else
                            {
                                // Ultimate fallback: ALL pieces tied for shortest within 0.1 units (never use FirstOrDefault!)
                                double tol = Math.Max(0.1, minLen * 0.02);
                                foreach (var p in pieceList.Where(p => Math.Abs(p.Length - minLen) <= tol))
                                {
                                    p.IsRemove = true;
                                    p.Reason = $"Đoạn ngắn nhất ({p.Length:F2})";
                                }
                            }
                        }
                    }
                    break;

                case AutoCutCutMode.RemoveLongest:
                    if (pieceList.Count > 1)
                    {
                        double maxLen = pieceList.Max(p => p.Length);
                        double minLen = pieceList.Min(p => p.Length);
                        if (maxLen - minLen > 1e-4)
                        {
                            double tol = Math.Max(0.5, maxLen * 0.05);
                            var longestPieces = pieceList.Where(p => Math.Abs(p.Length - maxLen) <= tol).ToList();
                            if (longestPieces.Count > 0 && longestPieces.Count < pieceList.Count)
                            {
                                foreach (var lp in longestPieces)
                                {
                                    lp.IsRemove = true;
                                    lp.Reason = $"Đoạn dài nhất ({lp.Length:F2})";
                                }
                            }
                        }
                    }
                    break;

                case AutoCutCutMode.SingleIntersectionRule:
                    if (intersections.Count == 1 && pieceList.Count == 2)
                    {
                        if (settings.SingleIntersectionRule == AutoCutSingleIntersectionRule.RemoveBefore)
                        {
                            pieceList[0].IsRemove = true;
                            pieceList[0].Reason = "RemoveBefore 1st intersection";
                        }
                        else if (settings.SingleIntersectionRule == AutoCutSingleIntersectionRule.RemoveAfter)
                        {
                            pieceList[1].IsRemove = true;
                            pieceList[1].Reason = "RemoveAfter 1st intersection";
                        }
                    }
                    break;

                case AutoCutCutMode.BetweenIntersections:
                default:
                    if (intersections.Count == 1)
                    {
                        if (settings.SingleIntersectionRule == AutoCutSingleIntersectionRule.RemoveBefore && pieceList.Count >= 2)
                        {
                            pieceList[0].IsRemove = true;
                            pieceList[0].Reason = "RemoveBefore Single Intersection";
                        }
                        else if (settings.SingleIntersectionRule == AutoCutSingleIntersectionRule.RemoveAfter && pieceList.Count >= 2)
                        {
                            pieceList[1].IsRemove = true;
                            pieceList[1].Reason = "RemoveAfter Single Intersection";
                        }
                    }
                    else if (intersections.Count >= 2)
                    {
                        // Alternating interval logic with Workpiece Preservation Rule
                        double sumEven = pieceList.Where((p, idx) => idx % 2 == 0).Sum(p => p.Length);
                        double sumOdd  = pieceList.Where((p, idx) => idx % 2 == 1).Sum(p => p.Length);

                        // In manufacturing, the cutout notches are ALWAYS the smaller group by total length!
                        // The workpiece body is the larger group!
                        int removeParity;
                        if (Math.Abs(sumEven - sumOdd) > 1e-4)
                        {
                            removeParity = sumEven < sumOdd ? 0 : 1;
                        }
                        else
                        {
                            removeParity = (isClosedTarget || hasIntersectionAtStart) ? 0 : 1;
                        }

                        for (int i = 0; i < pieceList.Count; i++)
                        {
                            if (i % 2 == removeParity)
                            {
                                pieceList[i].IsRemove = true;
                                pieceList[i].Reason = $"Vết cắt / Notch (Nhóm {(removeParity == 0 ? "Chẵn" : "Lẻ")}: {(removeParity == 0 ? sumEven : sumOdd):F1}mm < {(removeParity == 0 ? sumOdd : sumEven):F1}mm)";
                            }
                            else
                            {
                                pieceList[i].IsRemove = false;
                                pieceList[i].Reason = $"Thân phôi giữ lại (Nhóm {(removeParity == 0 ? "Lẻ" : "Chẵn")}: {(removeParity == 0 ? sumOdd : sumEven):F1}mm)";
                            }
                        }
                    }
                    break;
            }

            // Sanity Check: A cut operation must NEVER delete 100% of a target!
            // If every single piece was marked remove, fall back to Workpiece Preservation Rule
            if (pieceList.Count > 1 && pieceList.All(p => p.IsRemove))
            {
                double sumE = pieceList.Where((p, idx) => idx % 2 == 0).Sum(p => p.Length);
                double sumO = pieceList.Where((p, idx) => idx % 2 == 1).Sum(p => p.Length);
                int remParity = sumE <= sumO ? 0 : 1;
                for (int i = 0; i < pieceList.Count; i++)
                {
                    pieceList[i].IsRemove = (i % 2 == remParity);
                    pieceList[i].Reason = pieceList[i].IsRemove ? "Vết cắt / Notch (Bảo tồn phôi)" : "Thân phôi giữ lại (Bảo tồn phôi)";
                }
            }

            plan.Pieces.AddRange(pieceList);
        }

        private static bool IsParamInSpan(double param, CutterSpanInfo span)
        {
            if (span == null) return false;
            double p1 = Math.Min(span.Param1, span.Param2);
            double p2 = Math.Max(span.Param1, span.Param2);
            return param >= p1 - Epsilon && param <= p2 + Epsilon;
        }

        public static bool CommitChanges(
            Transaction tr,
            AutoCutAnalysisResult analysis,
            Database db)
        {
            if (analysis == null || tr == null || db == null) return false;

            foreach (AutoCutTargetPlan plan in analysis.TargetPlans)
            {
                if (!plan.RemovePieces.Any())
                {
                    // No pieces to remove on this target, leave target untouched
                    continue;
                }

                Curve target = tr.GetObject(plan.TargetId, OpenMode.ForWrite) as Curve;
                if (target == null || target.IsErased) continue;

                BlockTableRecord btr = tr.GetObject(target.BlockId, OpenMode.ForWrite) as BlockTableRecord;
                if (btr == null) continue;

                // Erase target
                target.Erase(true);

                // Append keep pieces
                foreach (AutoCutPiece keep in plan.KeepPieces)
                {
                    if (keep.Curve != null && !keep.Curve.IsDisposed)
                    {
                        btr.AppendEntity(keep.Curve);
                        tr.AddNewlyCreatedDBObject(keep.Curve, true);
                    }
                }

                // Dispose removed pieces
                foreach (AutoCutPiece rem in plan.RemovePieces)
                {
                    rem.Dispose();
                }
            }

            return true;
        }

        private static bool MatchesTargetType(Curve curve, AutoCutSettings settings)
        {
            var flags = settings.TargetTypes;
            if (flags.HasFlag(AutoCutTargetTypeFlags.Line) && curve is Line) return true;
            if (flags.HasFlag(AutoCutTargetTypeFlags.Polyline) && (curve is Autodesk.AutoCAD.DatabaseServices.Polyline || curve is Polyline2d)) return true;
            if (flags.HasFlag(AutoCutTargetTypeFlags.Arc) && curve is Arc) return true;
            if (flags.HasFlag(AutoCutTargetTypeFlags.Circle) && curve is Circle) return true;
            if (flags.HasFlag(AutoCutTargetTypeFlags.Ellipse) && curve is Ellipse) return true;
            if (flags.HasFlag(AutoCutTargetTypeFlags.Spline) && curve is Spline) return true;
            return false;
        }

        private static bool MatchesTargetLayer(Curve targetCurve, AutoCutSettings settings, Database db)
        {
            switch (settings.TargetLayerFilterMode)
            {
                case AutoCutTargetLayerFilterMode.AllLayers:
                    return true;
                case AutoCutTargetLayerFilterMode.CurrentLayer:
                    return db != null && targetCurve.LayerId == db.Clayer;
                case AutoCutTargetLayerFilterMode.SpecificLayer:
                    if (string.IsNullOrWhiteSpace(settings.TargetLayerName)) return true;
                    string[] patterns = settings.TargetLayerName.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string pat in patterns)
                    {
                        string p = pat.Trim();
                        if (string.Equals(targetCurve.Layer, p, StringComparison.OrdinalIgnoreCase)) return true;
                        if ((p.Contains("*") || p.Contains("?")) && LikeString(targetCurve.Layer, p)) return true;
                    }
                    return false;
                default:
                    return true;
            }
        }

        private static bool LikeString(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCandidateCutterType(Entity entity, AutoCutCutterTypeFlags flags)
        {
            if (flags.HasFlag(AutoCutCutterTypeFlags.Line) && entity is Line) return true;
            if (flags.HasFlag(AutoCutCutterTypeFlags.Polyline) && (entity is Autodesk.AutoCAD.DatabaseServices.Polyline || entity is Polyline2d)) return true;
            if (flags.HasFlag(AutoCutCutterTypeFlags.Arc) && entity is Arc) return true;
            if (flags.HasFlag(AutoCutCutterTypeFlags.Circle) && entity is Circle) return true;
            if (flags.HasFlag(AutoCutCutterTypeFlags.Ellipse) && entity is Ellipse) return true;
            if (flags.HasFlag(AutoCutCutterTypeFlags.Spline) && entity is Spline) return true;
            return false;
        }

        private static bool MatchesLayerFilter(Entity cutter, Entity target, AutoCutSettings settings, Database db)
        {
            switch (settings.LayerFilterMode)
            {
                case AutoCutLayerFilterMode.AnyLayer:
                    return true;
                case AutoCutLayerFilterMode.CurrentLayer:
                    return cutter.LayerId == db.Clayer;
                case AutoCutLayerFilterMode.CustomLayer:
                    return string.Equals(cutter.Layer, settings.CustomLayerName, StringComparison.OrdinalIgnoreCase);
                case AutoCutLayerFilterMode.SameAsTarget:
                default:
                    return cutter.LayerId == target.LayerId;
            }
        }

        public static bool IsClosedCurve(Entity entity)
        {
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
                return sp.Closed;
            }
            return false;
        }

        public static bool IsInsideClosedCurve(Point3d pt, Entity closedCutter, double tolerance)
        {
            if (closedCutter is Circle circle)
            {
                double dist = pt.DistanceTo(circle.Center);
                return dist <= circle.Radius + tolerance;
            }

            if (closedCutter is Ellipse ellipse)
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

            if (closedCutter is Autodesk.AutoCAD.DatabaseServices.Polyline polyline && IsClosedCurve(polyline))
            {
                return IsPointInPolylineWcs(pt, polyline);
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
                    int steps = 4;
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

            if (polyPoints.Count < 3) return false;

            // Standard Ray Casting (even-odd rule)
            bool inside = false;
            double px = pt.X;
            double py = pt.Y;
            int count = polyPoints.Count;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double xi = polyPoints[i].X, yi = polyPoints[i].Y;
                double xj = polyPoints[j].X, yj = polyPoints[j].Y;

                bool intersect = ((yi > py) != (yj > py)) &&
                                 (px < (xj - xi) * (py - yi) / (yj - yi + Epsilon) + xi);
                if (intersect)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static double GetCurveLength(Curve curve)
        {
            if (curve == null) return 0.0;
            try
            {
                if (curve is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
                {
                    return pl.Length;
                }
                if (curve is Line line)
                {
                    return line.Length;
                }
                if (curve is Arc arc)
                {
                    return arc.Length;
                }
                if (curve is Circle circle)
                {
                    return circle.Circumference;
                }
                if (curve is Polyline2d p2d)
                {
                    return p2d.Length;
                }
                double s = curve.GetDistanceAtParameter(curve.StartParam);
                double e = curve.GetDistanceAtParameter(curve.EndParam);
                return Math.Abs(e - s);
            }
            catch
            {
                try
                {
                    return curve.StartPoint.DistanceTo(curve.EndPoint);
                }
                catch
                {
                    return 0.0;
                }
            }
        }

        private static bool IsPlanar(Entity entity)
        {
            if (entity is Line || entity is Autodesk.AutoCAD.DatabaseServices.Polyline ||
                entity is Polyline2d || entity is Arc || entity is Circle || entity is Ellipse)
            {
                return true;
            }
            return false;
        }

        private class IntersectionPointInfo
        {
            public Point3d Point { get; set; }
            public double Parameter { get; set; }
            public Entity Cutter { get; set; }
        }
    }
}
