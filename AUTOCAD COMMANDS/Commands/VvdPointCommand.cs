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
    // Lưu ý: toàn bộ pick point trong lệnh đều tắt snap/osnap tạm thời.
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
                new PromptStringOptions("\nNhập tiền tố: ")
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
                ed.WriteMessage("\nVVD: prefix không được rỗng.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, PhantomLayerName);
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

                    Circle marker = new Circle(point, Vector3d.ZAxis, MarkerRadius)
                    {
                        LayerId = layerId
                    };
                    currentSpace.AppendEntity(marker);
                    tr.AddNewlyCreatedDBObject(marker, true);

                    string currentDefinition;
                    if (!lastPoint.HasValue)
                    {
                        currentDefinition =
                            $"{prefix}_p{counter} = APoint({prefix}_p{counter}.x, {prefix}_p{counter}.y)";
                    }
                    else
                    {
                        double dx = point.X - lastPoint.Value.X;
                        double dy = point.Y - lastPoint.Value.Y;
                        currentDefinition =
                            $"{prefix}_p{counter} = APoint({prefix}_p{counter - 1}.x{CadFormatHelper.FormatOffset(dx, NumericTolerance)}, {prefix}_p{counter - 1}.y{CadFormatHelper.FormatOffset(dy, NumericTolerance)})";
                    }

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
                        currentDefinition,
                        counter % 2 == 1
                            ? AttachmentPoint.BottomLeft
                            : AttachmentPoint.TopLeft,
                        db.Textsize > NumericTolerance
                            ? db.Textsize
                            : TextHeightFallback);

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
                        CadMTextHelper.AddMText(
                            currentSpace,
                            tr,
                            layerId,
                            summaryPointResult.Value,
                            TextWidth,
                            string.Join("\n", definitionLines),
                            AttachmentPoint.BottomLeft,
                            db.Textsize > NumericTolerance
                                ? db.Textsize
                                : TextHeightFallback);
                    }
                }

                tr.Commit();
                ed.WriteMessage(
                    definitionLines.Count > 0
                        ? $"\nĐã xử lý xong {definitionLines.Count} điểm."
                        : "\nKhông có điểm nào được tạo.");
            }
        }

    }
}
