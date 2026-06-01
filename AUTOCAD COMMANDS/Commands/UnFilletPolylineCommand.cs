using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
 
namespace AUTOCAD_COMMANDS
{
    // ======================================================
    // UFF - UN-FILLET POLYLINE
    // Mục đích: viết lại Lisp UFF bằng C# để bỏ các đoạn fillet/cung trên polyline nhanh hơn.
    // Cách dùng:
    // - Chọn lightweight Polyline.
    // - Lệnh duyệt từng segment có bulge.
    // - Với segment cung, bỏ điểm đầu/cuối cung và thay bằng 1 điểm giao của 2 đoạn thẳng kề.
    // - Tạo polyline mới trên layer _mss.phantom, không xóa polyline gốc.
    // ======================================================
    public class UnFilletPolylineCommand
    {
        private const string PhantomLayerName = "_mss.phantom";
        private const double GeometryTolerance = 1e-9;

        [CommandMethod("UFF")]
        public void UnFillet()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions options =
                new PromptEntityOptions("\nChọn polyline cần bỏ fillet: ");
            options.SetRejectMessage("\nĐối tượng không phải lightweight Polyline.");
            options.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);

            PromptEntityResult result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Autodesk.AutoCAD.DatabaseServices.Polyline source =
                    tr.GetObject(result.ObjectId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                if (source == null)
                {
                    ed.WriteMessage("\nUFF: đối tượng không phải Polyline.");
                    return;
                }

                if (source.NumberOfVertices < 2)
                {
                    ed.WriteMessage("\nUFF: polyline phải có ít nhất 2 vertex.");
                    return;
                }

                List<Point2d> newVertices = BuildUnfilletedVertices(source);
                RemoveConsecutiveDuplicatePoints(newVertices);

                if (newVertices.Count < 2)
                {
                    ed.WriteMessage("\nUFF: không tạo được polyline mới từ dữ liệu hiện tại.");
                    return;
                }

                ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, PhantomLayerName);
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return;
                }

                Autodesk.AutoCAD.DatabaseServices.Polyline target =
                    new Autodesk.AutoCAD.DatabaseServices.Polyline(newVertices.Count)
                    {
                        LayerId = layerId,
                        Elevation = source.Elevation,
                        Normal = source.Normal,
                        Closed = source.Closed && newVertices.Count >= 3
                    };

                for (int i = 0; i < newVertices.Count; i++)
                {
                    target.AddVertexAt(i, newVertices[i], 0.0, 0.0, 0.0);
                }

                currentSpace.AppendEntity(target);
                tr.AddNewlyCreatedDBObject(target, true);
                tr.Commit();

                ed.WriteMessage(
                    $"\nUFF: đã bỏ fillet và tạo polyline mới với {newVertices.Count} vertex.");
            }
        }

        private static List<Point2d> BuildUnfilletedVertices(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            List<Point2d> vertices = new List<Point2d>();
            int count = polyline.NumberOfVertices;
            bool closed = polyline.Closed;

            int i = 0;
            while (i < count)
            {
                bool hasNext = i + 1 < count || closed;
                double bulge = hasNext ? polyline.GetBulgeAt(i) : 0.0;

                if (Math.Abs(bulge) <= GeometryTolerance || !hasNext)
                {
                    vertices.Add(polyline.GetPoint2dAt(i));
                    i++;
                    continue;
                }

                Point2d p0 = polyline.GetPoint2dAt(i);
                Point2d p3 = polyline.GetPoint2dAt((i + 1) % count);
                Point2d p1 = i > 0
                    ? polyline.GetPoint2dAt(i - 1)
                    : (closed ? polyline.GetPoint2dAt(count - 1) : p0);
                Point2d p2 = i + 2 < count
                    ? polyline.GetPoint2dAt(i + 2)
                    : (closed ? polyline.GetPoint2dAt((i + 2) % count) : p3);

                if (closed && i == count - 1 && vertices.Count > 0 && AreSamePoint(vertices[0], p3))
                {
                    vertices.RemoveAt(0);
                }

                if (TryIntersectInfiniteLines(p0, p0 - p1, p3, p2 - p3, out Point2d intersection))
                {
                    vertices.Add(intersection);
                }
                else
                {
                    // Nếu 2 line kề song song hoặc thiếu line kề thì fallback giữ điểm đầu cung.
                    vertices.Add(p0);
                }

                i += 2;
            }

            return vertices;
        }

        private static bool TryIntersectInfiniteLines(
            Point2d basePoint1,
            Vector2d direction1,
            Point2d basePoint2,
            Vector2d direction2,
            out Point2d intersection)
        {
            intersection = Point2d.Origin;

            double denominator = Cross(direction1, direction2);
            if (Math.Abs(denominator) <= GeometryTolerance ||
                direction1.Length <= GeometryTolerance ||
                direction2.Length <= GeometryTolerance)
            {
                return false;
            }

            Vector2d between = basePoint2 - basePoint1;
            double parameter = Cross(between, direction2) / denominator;
            intersection = basePoint1 + direction1.MultiplyBy(parameter);
            return true;
        }

        private static double Cross(Vector2d first, Vector2d second)
        {
            return first.X * second.Y - first.Y * second.X;
        }

        private static void RemoveConsecutiveDuplicatePoints(List<Point2d> points)
        {
            for (int i = points.Count - 1; i > 0; i--)
            {
                if (AreSamePoint(points[i], points[i - 1]))
                {
                    points.RemoveAt(i);
                }
            }
        }

        private static bool AreSamePoint(Point2d first, Point2d second)
        {
            return first.GetDistanceTo(second) <= GeometryTolerance;
        }
    }
}
