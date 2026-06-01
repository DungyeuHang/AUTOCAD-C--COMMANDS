using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
 
namespace AUTOCAD_COMMANDS
{
    // ======================================================
    // APOINT - MAKE POINTS BY POLYLINE
    // Mục đích: viết lại lệnh Lisp APOINT bằng C# cho nhanh và ổn định hơn.
    // Cách dùng:
    // - Chọn lightweight Polyline.
    // - Nhập prefix điểm, ví dụ bl_fr.
    // - Lệnh tạo circle + text cho từng vertex.
    // - Lệnh tạo 1 MText tổng hợp gồm toàn bộ APoint(...) và smart_pl(...).
    // Lưu ý: text tổng hợp tự đặt tại x = p1.x, y = p1.y - 2.
    // ======================================================
    public class APointCommand
    {
        private const string PhantomLayerName = "_mss.phantom";
        private const double MarkerRadius = 0.3;
        private const double TextHeight = 0.1;
        private const double PointLabelWidth = 10.0;
        private const double SummaryTextWidth = 120.0;
        private const double NumericTolerance = 1e-6;

        [CommandMethod("APOINT")]
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

                PromptStringOptions prefixOptions =
                    new PromptStringOptions("\nNhập tiền tố điểm (vd: bl_fr): ")
                    {
                        AllowSpaces = false
                    };
                PromptResult prefixResult = ed.GetString(prefixOptions);
                if (prefixResult.Status != PromptStatus.OK)
                {
                    return;
                }

                string prefix = (prefixResult.StringResult ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    ed.WriteMessage("\nAPOINT: prefix không được rỗng.");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                        tr.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (polyline == null)
                    {
                        ed.WriteMessage("\nAPOINT: không đọc được polyline.");
                        return;
                    }

                    int vertexCount = polyline.NumberOfVertices;
                    if (vertexCount == 0)
                    {
                        ed.WriteMessage("\nAPOINT: polyline không có vertex.");
                        return;
                    }

                    List<Point3d> points = GetPolylineVertices(polyline);
                    ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, PhantomLayerName);
                    BlockTableRecord currentSpace =
                        tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    if (currentSpace == null)
                    {
                        return;
                    }

                    List<string> definitionLines = new List<string>();
                    Point3d? previousPoint = null;

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

                        string definitionLine;
                        if (!previousPoint.HasValue)
                        {
                            definitionLine =
                                $"{prefix}_p{count} = APoint({CadFormatHelper.FormatNumber(point.X, NumericTolerance)}, {CadFormatHelper.FormatNumber(point.Y, NumericTolerance)})";
                        }
                        else
                        {
                            double dx = point.X - previousPoint.Value.X;
                            double dy = point.Y - previousPoint.Value.Y;
                            definitionLine =
                                $"{prefix}_p{count} = APoint({prefix}_p{count - 1}.x{CadFormatHelper.FormatOffset(dx, NumericTolerance)}, {prefix}_p{count - 1}.y{CadFormatHelper.FormatOffset(dy, NumericTolerance)})";
                        }

                        definitionLines.Add(definitionLine);

                        double labelYOffset = count % 2 == 1 ? 0.1 : -0.1;
                        Point3d labelPoint = new Point3d(point.X, point.Y + labelYOffset, point.Z);
                        CadMTextHelper.AddMText(
                            currentSpace,
                            tr,
                            layerId,
                            labelPoint,
                            PointLabelWidth,
                            definitionLine,
                            TextHeight);

                        previousPoint = point;
                    }

                    string smartShapeText = BuildSmartShapeText(polyline, points, prefix);
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
                        TextHeight);

                    tr.Commit();
                    ed.WriteMessage($"\nAPOINT: đã tạo {points.Count} điểm và text tổng hợp.");
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

        private static List<Point3d> GetPolylineVertices(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            List<Point3d> points = new List<Point3d>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                Point2d point2d = polyline.GetPoint2dAt(i);
                Point3d point = new Point3d(point2d.X, point2d.Y, polyline.Elevation)
                    .TransformBy(polyline.Ecs);
                points.Add(point);
            }

            return points;
        }

        private static string BuildSmartShapeText(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            IReadOnlyList<Point3d> points,
            string prefix)
        {
            List<string> arcsInfoItems = new List<string>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                double bulge = polyline.GetBulgeAt(i);
                if (Math.Abs(bulge) <= NumericTolerance)
                {
                    continue;
                }

                double chord = points[i].DistanceTo(points[i + 1]);
                double theta = 4.0 * Math.Atan(bulge);
                double denominator = 2.0 * Math.Sin(theta / 2.0);
                if (Math.Abs(denominator) <= NumericTolerance)
                {
                    continue;
                }

                double radius = chord / denominator;
                int startPointIndex = i + 1;
                int endPointIndex = i + 2;
                if (radius < 0.0)
                {
                    radius = Math.Abs(radius);
                    startPointIndex = i + 2;
                    endPointIndex = i + 1;
                }

                arcsInfoItems.Add(
                    $"({startPointIndex},{endPointIndex}): ({FormatRadius(radius)}, True)");
            }

            string closeText = polyline.Closed ? "True" : "False";
            string arcsInfo = string.Join(", ", arcsInfoItems);
            return $"smart_pl(\"{prefix}_p\", 1, {points.Count}, arcs_info={{{arcsInfo}}}, close={closeText})";
        }

        private static string FormatRadius(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
