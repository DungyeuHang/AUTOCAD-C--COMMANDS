using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AUTOCAD_COMMANDS
{
    // ======================================================
    // APOINT (alias APOINT2) - MAKE POINTS BY POLYLINE
    // Gộp APOINT + APOINT2 làm 1: dùng chung logic "tham chiếu tuyến tính" -
    // mỗi trục x/y được tham chiếu tới điểm trùng toạ độ gần nhất tìm được phía
    // trước (theo chỉ số nhỏ nhất "First" hoặc lớn nhất "Last"); nếu không có
    // điểm nào trùng thì tự rơi về logic cũ: bám điểm liền trước + offset.
    // Vì vậy APOINT2 (cũ) vốn là tập hợp lớn hơn của APOINT (cũ), nay chỉ còn
    // 1 lệnh duy nhất, giữ cả 2 tên lệnh để không phá vỡ thói quen gõ lệnh.
    // Ví dụ: p5 có y = p4.y, x = p2.x = p3.x  =>  p5 = APoint(p2.x, p4.y)
    // Cách dùng:
    // - Chọn lightweight Polyline.
    // - Trong bảng tuỳ chọn: nhập prefix, cỡ chữ, chế độ dò First/Last,
    //   bật/tắt gộp điểm trùng toạ độ liên tiếp, bật/tắt đảo chiều pline,
    //   xem trước kết quả rồi mới "Tạo điểm".
    // - Lệnh tạo circle + text cho từng vertex.
    // - Lệnh tạo 1 MText tổng hợp gồm toàn bộ APoint(...) và smart_pl(...).
    // ======================================================
    public class APointCommand
    {
        private const string PhantomLayerName = "_mss.phantom";
        private const double MarkerRadius = 0.3;
        private const double DefaultTextHeight = 0.1;
        private const double PointLabelWidth = 10.0;
        private const double SummaryTextWidth = 120.0;
        private const double NumericTolerance = 1e-6;

        // Nhớ lựa chọn của lần chạy trước trong cùng phiên AutoCAD.
        private static string lastPrefix = string.Empty;
        private static APointGeometryHelper.MatchMode lastMatchMode = APointGeometryHelper.MatchMode.First;
        private static bool lastMergeDuplicates;
        private static bool lastReverseDirection;
        private static double lastTextHeight = DefaultTextHeight;

        [CommandMethod("APOINT")]
        [CommandMethod("APOINT2")]
        public void MakePointsByPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            object previousOsMode = null;
            object previousSnapMode = null;

            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");
                previousSnapMode = Application.GetSystemVariable("SNAPMODE");
                Application.SetSystemVariable("OSMODE", 0);
                Application.SetSystemVariable("SNAPMODE", 0);

                PromptEntityOptions entityOptions =
                    new PromptEntityOptions("\nChọn polyline: ");
                entityOptions.SetRejectMessage("\nChỉ hỗ trợ lightweight Polyline.");
                entityOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);

                PromptEntityResult entityResult = ed.GetEntity(entityOptions);
                if (entityResult.Status != PromptStatus.OK)
                {
                    return;
                }

                string prefix = lastPrefix;
                APointGeometryHelper.MatchMode matchMode = lastMatchMode;
                bool mergeDuplicates = lastMergeDuplicates;
                bool reverseDirection = lastReverseDirection;
                double textHeight = lastTextHeight;

                while (true)
                {
                    using (Transaction trRead = db.TransactionManager.StartTransaction())
                    {
                        Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                            trRead.GetObject(entityResult.ObjectId, OpenMode.ForRead)
                                as Autodesk.AutoCAD.DatabaseServices.Polyline;
                        if (polyline == null)
                        {
                            ed.WriteMessage("\nAPOINT: không đọc được polyline.");
                            trRead.Commit();
                            return;
                        }

                        if (polyline.NumberOfVertices == 0)
                        {
                            ed.WriteMessage("\nAPOINT: polyline không có vertex.");
                            trRead.Commit();
                            return;
                        }

                        List<Point3d> originalPoints = APointGeometryHelper.GetPolylineVertices(polyline);
                        double[] originalBulges = APointGeometryHelper.GetSegmentBulges(polyline);
                        bool closed = polyline.Closed;

                        string plineInfo = $"Đã chọn: Polyline ({originalPoints.Count} đỉnh gốc)";

                        Func<string, bool, APointGeometryHelper.MatchMode, bool, string> previewBuilder =
                            (p, merge, mode, reverse) =>
                        {
                            List<Point3d> workPoints;
                            double[] workBulges;
                            APointGeometryHelper.ApplyReverse(
                                originalPoints, originalBulges, reverse, out workPoints, out workBulges);

                            APointGeometryHelper.ReducedPoints reduced =
                                APointGeometryHelper.ReduceConsecutiveDuplicates(workPoints, merge, NumericTolerance);

                            List<string> lines = APointGeometryHelper.BuildSmartDefinitionLines(
                                reduced.Points, p, mode, NumericTolerance);
                            string shapeText = APointGeometryHelper.BuildSmartShapeText(
                                workBulges, closed, reduced.Points, reduced.LastOriginalIndex, p, NumericTolerance);

                            return string.Join("\n", lines.Concat(new[] { shapeText }));
                        };

                        using (APointOptionsForm form = new APointOptionsForm(
                            "APOINT - Tuỳ chọn tạo điểm",
                            true,
                            plineInfo,
                            prefix,
                            mergeDuplicates,
                            matchMode,
                            reverseDirection,
                            textHeight,
                            previewBuilder))
                        {
                            Application.ShowModalDialog(form);

                            if (form.Action == APointDialogAction.Cancel)
                            {
                                trRead.Commit();
                                return;
                            }

                            if (form.Action == APointDialogAction.ReselectPline)
                            {
                                trRead.Commit();
                                PromptEntityResult newSelection = ed.GetEntity(entityOptions);
                                if (newSelection.Status != PromptStatus.OK)
                                {
                                    return;
                                }

                                entityResult = newSelection;
                                continue;
                            }

                            prefix = form.Prefix;
                            matchMode = form.MatchMode;
                            mergeDuplicates = form.MergeDuplicates;
                            reverseDirection = form.ReverseDirection;
                            textHeight = form.TextHeight;
                            trRead.Commit();
                        }
                    }

                    break;
                }

                lastPrefix = prefix;
                lastMatchMode = matchMode;
                lastMergeDuplicates = mergeDuplicates;
                lastReverseDirection = reverseDirection;
                lastTextHeight = textHeight;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                        tr.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (polyline == null)
                    {
                        ed.WriteMessage("\nAPOINT: không đọc được polyline.");
                        return;
                    }

                    List<Point3d> originalPoints = APointGeometryHelper.GetPolylineVertices(polyline);
                    double[] originalBulges = APointGeometryHelper.GetSegmentBulges(polyline);
                    bool closed = polyline.Closed;

                    List<Point3d> workPoints;
                    double[] workBulges;
                    APointGeometryHelper.ApplyReverse(
                        originalPoints, originalBulges, reverseDirection, out workPoints, out workBulges);

                    APointGeometryHelper.ReducedPoints reduced =
                        APointGeometryHelper.ReduceConsecutiveDuplicates(workPoints, mergeDuplicates, NumericTolerance);
                    List<Point3d> points = reduced.Points;

                    ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, PhantomLayerName);
                    BlockTableRecord currentSpace =
                        tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    if (currentSpace == null)
                    {
                        return;
                    }

                    List<string> definitionLines = APointGeometryHelper.BuildSmartDefinitionLines(
                        points, prefix, matchMode, NumericTolerance);

                    for (int i = 0; i < points.Count; i++)
                    {
                        Point3d point = points[i];
                        int count = i + 1;

                        Circle marker = new Circle(point, Vector3d.ZAxis, MarkerRadius)
                        {
                            LayerId = layerId
                        };
                        currentSpace.AppendEntity(marker);
                        tr.AddNewlyCreatedDBObject(marker, true);

                        double labelYOffset = count % 2 == 1 ? 0.1 : -0.1;
                        Point3d labelPoint = new Point3d(point.X, point.Y + labelYOffset, point.Z);
                        CadMTextHelper.AddMText(
                            currentSpace,
                            tr,
                            layerId,
                            labelPoint,
                            PointLabelWidth,
                            definitionLines[i],
                            textHeight);
                    }

                    string smartShapeText = APointGeometryHelper.BuildSmartShapeText(
                        workBulges, closed, points, reduced.LastOriginalIndex, prefix, NumericTolerance);
                    string summaryText = string.Join("\n", definitionLines.Concat(new[] { smartShapeText }));
                    Point3d firstPoint = points[0];
                    Point3d summaryPoint = new Point3d(firstPoint.X, firstPoint.Y - 3.0, firstPoint.Z);
                    CadMTextHelper.AddMText(
                        currentSpace,
                        tr,
                        layerId,
                        summaryPoint,
                        SummaryTextWidth,
                        summaryText,
                        textHeight);

                    tr.Commit();
                    ed.WriteMessage(
                        $"\nAPOINT: đã tạo {points.Count} điểm và text tổng hợp (chế độ {matchMode}).");
                }
            }
            finally
            {
                if (previousOsMode != null)
                {
                    Application.SetSystemVariable("OSMODE", previousOsMode);
                }

                if (previousSnapMode != null)
                {
                    Application.SetSystemVariable("SNAPMODE", previousSnapMode);
                }
            }
        }
    }
}
