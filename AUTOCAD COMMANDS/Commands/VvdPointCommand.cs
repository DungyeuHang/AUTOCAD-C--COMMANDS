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
    // VVD / VE_DIEM
    // Mục đích: chuyển lệnh Lisp chọn điểm tuần tự sang C#.
    // Cách dùng:
    // - Nhập prefix.
    // - Click lần lượt các điểm, Enter để dừng.
    // - Chọn vị trí đặt MText tổng hợp.
    // Lưu ý: pick point dùng đúng snap/osnap hiện tại của người dùng.
    // ======================================================
    public class VvdPointCommand
    {
        private const string PhantomLayerName = "_mss.phantom";
        private const double MarkerRadius = 0.3;
        private const double TextHeightFallback = 0.1;
        private const double LabelOffsetY = 0.1;
        private const double TextWidth = 10.0;
        private const double NumericTolerance = 1e-6;

        [CommandMethod("VVD")]
        [CommandMethod("VE_DIEM")]
        public void CreateVariableDefinitionPoints()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            ed.WriteMessage("\nĐang xử lý...");

            PromptStringOptions prefixOptions =
                new PromptStringOptions("\nNhập tiền tố (có thể bỏ trống): ")
                {
                    AllowSpaces = false
                };
            PromptResult prefixResult = ed.GetString(prefixOptions);
            if (prefixResult.Status != PromptStatus.OK)
            {
                return;
            }

            string prefix = (prefixResult.StringResult ?? string.Empty).Trim();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, PhantomLayerName);
                if (layerId == ObjectId.Null)
                {
                    ed.WriteMessage("\nVVD: không tạo được layer phantom.");
                    return;
                }

                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return;
                }

                List<string> definitionLines = new List<string>();
                Point3d? lastPoint = null;
                int counter = 1;

                while (true)
                {
                    PromptPointOptions pointOptions =
                        new PromptPointOptions(
                            $"\nChọn điểm {counter} (Enter để dừng): ")
                        {
                            AllowNone = true
                        };

                    PromptPointResult pointResult = ed.GetPoint(pointOptions);
                    if (pointResult.Status == PromptStatus.None)
                    {
                        break;
                    }

                    if (pointResult.Status != PromptStatus.OK)
                    {
                        return;
                    }

                    Point3d point = pointResult.Value;
                    string currentDefinition = BuildDefinitionLine(prefix, counter, point, lastPoint);

                    AddPointMarkerAndText(currentSpace, tr, db, layerId, point, counter, currentDefinition);
                    db.TransactionManager.QueueForGraphicsFlush();
                    Application.UpdateScreen();

                    definitionLines.Add(currentDefinition);
                    lastPoint = point;
                    counter++;
                }

                if (definitionLines.Count > 0)
                {
                    PromptPointOptions summaryPointOptions =
                        new PromptPointOptions("\nChọn vị trí để đặt text tổng hợp: ");
                    PromptPointResult summaryPointResult = ed.GetPoint(summaryPointOptions);
                    if (summaryPointResult.Status == PromptStatus.OK)
                    {
                        AddSummaryText(
                            currentSpace,
                            tr,
                            db,
                            layerId,
                            summaryPointResult.Value,
                            string.Join("\n", definitionLines));
                    }
                }

                tr.Commit();
                ed.WriteMessage(
                    definitionLines.Count > 0
                        ? $"\nĐã xử lý xong {definitionLines.Count} điểm."
                        : "\nKhông có điểm nào được tạo.");
            }
        }

        private static string BuildDefinitionLine(
            string prefix,
            int counter,
            Point3d point,
            Point3d? lastPoint)
        {
            string pointName = BuildPointName(prefix, counter);
            if (!lastPoint.HasValue)
            {
                return
                    $"{pointName} = APoint({CadFormatHelper.FormatNumber(point.X, NumericTolerance)}, {CadFormatHelper.FormatNumber(point.Y, NumericTolerance)})";
            }

            double dx = point.X - lastPoint.Value.X;
            double dy = point.Y - lastPoint.Value.Y;
            string previousPointName = BuildPointName(prefix, counter - 1);
            return
                $"{pointName} = APoint({previousPointName}.x{CadFormatHelper.FormatOffset(dx, NumericTolerance)}, {previousPointName}.y{CadFormatHelper.FormatOffset(dy, NumericTolerance)})";
        }

        private static string BuildPointName(string prefix, int counter)
        {
            return string.IsNullOrWhiteSpace(prefix)
                ? $"_p{counter}"
                : $"{prefix}_p{counter}";
        }

        private static void AddPointMarkerAndText(
            BlockTableRecord currentSpace,
            Transaction tr,
            Database db,
            ObjectId layerId,
            Point3d point,
            int counter,
            string definitionLine)
        {
            Circle marker = new Circle(point, Vector3d.ZAxis, MarkerRadius)
            {
                LayerId = layerId
            };
            currentSpace.AppendEntity(marker);
            tr.AddNewlyCreatedDBObject(marker, true);

            Point3d labelPoint =
                new Point3d(
                    point.X,
                    point.Y + (counter % 2 == 1 ? LabelOffsetY : -LabelOffsetY),
                    point.Z);

            CadMTextHelper.AddMText(
                currentSpace,
                tr,
                layerId,
                labelPoint,
                TextWidth,
                definitionLine,
                counter % 2 == 1
                    ? AttachmentPoint.BottomLeft
                    : AttachmentPoint.TopLeft,
                db.Textsize > NumericTolerance
                    ? db.Textsize
                    : TextHeightFallback);
        }

        private static void AddSummaryText(
            BlockTableRecord currentSpace,
            Transaction tr,
            Database db,
            ObjectId layerId,
            Point3d location,
            string contents)
        {
            CadMTextHelper.AddMText(
                currentSpace,
                tr,
                layerId,
                location,
                TextWidth,
                contents,
                AttachmentPoint.BottomLeft,
                db.Textsize > NumericTolerance
                    ? db.Textsize
                    : TextHeightFallback);
        }
    }
}
