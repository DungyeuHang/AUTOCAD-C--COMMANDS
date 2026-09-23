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
    // APOINT2 - MAKE POINTS BY POLYLINE (SMART LINEAR REFERENCE)
    // Khác APOINT ở chỗ: mỗi trục x/y được tham chiếu tới điểm "tuyến tính" thật sự,
    // không phải lúc nào cũng bám vào điểm ngay trước.
    // Quy tắc cho điểm p(n+1), xét riêng từng trục x và y:
    // 1. Nếu toạ độ trùng với điểm liền trước pn -> ưu tiên lấy luôn pn (đã tuyến tính).
    // 2. Nếu không, dò trong các điểm phía trước, lấy điểm có toạ độ bằng theo option:
    //    First = chỉ số n nhỏ nhất (mặc định), Last = chỉ số n lớn nhất (phương án dự phòng).
    // 3. Nếu không điểm nào phía trước trùng -> giữ logic cũ: pn.x + dx / pn.y + dy.
    // Ví dụ: p5 có y = p4.y, x = p2.x = p3.x  =>  p5 = APoint(p2.x, p4.y)
    // Mục đích: khi sửa tham số của pline thì các điểm bám đúng gốc, không chạy sai.
    // Cách dùng:
    // - Chọn lightweight Polyline.
    // - Nhập prefix điểm, ví dụ bl_fr.
    // - Lệnh tạo circle + text cho từng vertex.
    // - Lệnh tạo 1 MText tổng hợp gồm toàn bộ APoint(...) và smart_pl(...).
    // ======================================================
    public class APoint2Command
    {
        private const string PhantomLayerName = "_mss.phantom";
        private const double MarkerRadius = 0.3;
        private const double TextHeight = 0.1;
        private const double PointLabelWidth = 10.0;
        private const double SummaryTextWidth = 120.0;
        private const double NumericTolerance = 1e-6;

        // Nhớ lựa chọn của lần chạy trước trong cùng phiên AutoCAD.
        private static bool useLastMatchDefault;

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
                    ed.WriteMessage("\nAPOINT2: prefix không được rỗng.");
                    return;
                }

                // Chọn kiểu dò điểm trùng toạ độ:
                // First = lấy điểm có chỉ số nhỏ nhất (mặc định), Last = lấy điểm có chỉ số lớn nhất.
                PromptKeywordOptions modeOptions =
                    new PromptKeywordOptions("\nDò điểm trùng toạ độ theo chỉ số [First=nhỏ nhất/Last=lớn nhất]")
                    {
                        AllowNone = true
                    };
                modeOptions.Keywords.Add("First");
                modeOptions.Keywords.Add("Last");
                modeOptions.Keywords.Default = useLastMatchDefault ? "Last" : "First";

                PromptResult modeResult = ed.GetKeywords(modeOptions);
                if (modeResult.Status != PromptStatus.OK)
                {
                    return;
                }

                bool useLastMatch = string.Equals(modeResult.StringResult, "Last", StringComparison.OrdinalIgnoreCase);
                useLastMatchDefault = useLastMatch;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                        tr.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                    if (polyline == null)
                    {
                        ed.WriteMessage("\nAPOINT2: không đọc được polyline.");
                        return;
                    }

                    int vertexCount = polyline.NumberOfVertices;
                    if (vertexCount == 0)
                    {
                        ed.WriteMessage("\nAPOINT2: polyline không có vertex.");
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
                        if (i == 0)
                        {
                            definitionLine =
                                $"{prefix}_p{count} = APoint({CadFormatHelper.FormatNumber(point.X, NumericTolerance)}, {CadFormatHelper.FormatNumber(point.Y, NumericTolerance)})";
                        }
                        else
                        {
                            string xExpression = BuildAxisExpression(points, i, prefix, true, useLastMatch);
                            string yExpression = BuildAxisExpression(points, i, prefix, false, useLastMatch);
                            definitionLine =
                                $"{prefix}_p{count} = APoint({xExpression}, {yExpression})";
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
                    ed.WriteMessage(
                        $"\nAPOINT2: đã tạo {points.Count} điểm và text tổng hợp (chế độ {(useLastMatch ? "Last" : "First")}).");
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

        // Dựng biểu thức cho 1 trục (x hoặc y) của điểm points[index].
        // Ưu tiên: điểm liền trước -> điểm phía trước trùng toạ độ -> offset theo điểm liền trước.
        // useLastMatch = false: lấy điểm trùng có chỉ số nhỏ nhất; true: lấy chỉ số lớn nhất.
        private static string BuildAxisExpression(
            IReadOnlyList<Point3d> points,
            int index,
            string prefix,
            bool isXAxis,
            bool useLastMatch)
        {
            string axisName = isXAxis ? "x" : "y";
            double currentValue = GetAxisValue(points[index], isXAxis);
            double previousValue = GetAxisValue(points[index - 1], isXAxis);

            // 1. Trùng với điểm liền trước -> bám luôn điểm liền trước.
            if (Math.Abs(currentValue - previousValue) <= NumericTolerance)
            {
                return $"{prefix}_p{index}.{axisName}";
            }

            // 2. Dò trong các điểm phía trước (p1..p(n-1)), lấy điểm trùng toạ độ
            //    theo chỉ số nhỏ nhất (mặc định) hoặc lớn nhất (tuỳ option).
            if (useLastMatch)
            {
                for (int j = index - 2; j >= 0; j--)
                {
                    if (Math.Abs(currentValue - GetAxisValue(points[j], isXAxis)) <= NumericTolerance)
                    {
                        return $"{prefix}_p{j + 1}.{axisName}";
                    }
                }
            }
            else
            {
                for (int j = 0; j < index - 1; j++)
                {
                    if (Math.Abs(currentValue - GetAxisValue(points[j], isXAxis)) <= NumericTolerance)
                    {
                        return $"{prefix}_p{j + 1}.{axisName}";
                    }
                }
            }

            // 3. Không có điểm nào phía trước trùng -> giữ logic cũ: lệch so với điểm liền trước.
            return $"{prefix}_p{index}.{axisName}{CadFormatHelper.FormatOffset(currentValue - previousValue, NumericTolerance)}";
        }

        private static double GetAxisValue(Point3d point, bool isXAxis)
        {
            return isXAxis ? point.X : point.Y;
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
