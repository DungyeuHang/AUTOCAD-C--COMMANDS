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
            object previousOsMode = null;
            object previousSnapMode = null;

            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");
                previousSnapMode = Application.GetSystemVariable("SNAPMODE");
                Application.SetSystemVariable("OSMODE", 0);
                Application.SetSystemVariable("SNAPMODE", 0);

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
                ObjectId layerId = EnsurePhantomLayer(db);
                if (layerId == ObjectId.Null)
                {
                    ed.WriteMessage("\nVVD: không tạo được layer phantom.");
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

                    AddPointMarkerAndText(db, layerId, point, counter, currentDefinition);
                    ed.Regen();

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
                            db,
                            layerId,
                            summaryPointResult.Value,
                            string.Join("\n", definitionLines));
                        ed.Regen();
                    }
                }

                ed.WriteMessage(
                    definitionLines.Count > 0
                        ? $"\nĐã xử lý xong {definitionLines.Count} điểm."
                        : "\nKhông có điểm nào được tạo.");
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

        private static ObjectId EnsurePhantomLayer(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, PhantomLayerName);
                tr.Commit();
                return layerId;
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
            Database db,
            ObjectId layerId,
            Point3d point,
            int counter,
            string definitionLine)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return;
                }

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

                tr.Commit();
            }
        }

        private static void AddSummaryText(
            Database db,
            ObjectId layerId,
            Point3d location,
            string contents)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return;
                }

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

                tr.Commit();
            }
        }
    }
}
