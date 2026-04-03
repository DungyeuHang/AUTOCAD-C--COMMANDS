using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace AUTOCAD_COMMANDS
{
    public class ChiaDimCommands
    {
        [CommandMethod("CDD2_CHIADIM")]
        public void ChiaDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // ===== CHỌN DIM =====
            PromptEntityOptions peo =
                new PromptEntityOptions("\nChọn DIM cần chia: ");
            peo.SetRejectMessage("\nChỉ hỗ trợ DIM ngang / dọc.");
            peo.AddAllowedClass(typeof(RotatedDimension), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RotatedDimension dim =
                    tr.GetObject(per.ObjectId, OpenMode.ForWrite) as RotatedDimension;
                if (dim == null) return;

                Point3d pt1 = dim.XLine1Point;
                Point3d pt2 = dim.XLine2Point;
                Point3d ptDim = dim.DimLinePoint;

                Vector3d axis = (pt2 - pt1).GetNormal();

                // ===== CHỌN ĐIỂM CHIA =====
                List<double> parameters = new List<double> { 0.0, pt1.DistanceTo(pt2) };

                while (true)
                {
                    PromptPointOptions ppo =
                        new PromptPointOptions("\nChọn điểm chia (Enter / Space để kết thúc): ");
                    ppo.AllowNone = true;
                    ppo.AllowArbitraryInput = true;

                    PromptPointResult ppr = ed.GetPoint(ppo);

                    if (ppr.Status == PromptStatus.None)
                        break;

                    if (ppr.Status == PromptStatus.Cancel)
                        return;

                    // ===== CHIẾU ĐIỂM LÊN TRỤC DIM =====
                    Vector3d v = ppr.Value - pt1;
                    double t = v.DotProduct(axis);

                    // chỉ nhận điểm nằm trong đoạn DIM
                    if (t > 1e-6 && t < pt1.DistanceTo(pt2) - 1e-6)
                        parameters.Add(t);
                }

                // sort đúng theo trục
                parameters = parameters.Distinct().OrderBy(x => x).ToList();

                // ===== XOÁ DIM CŨ =====
                dim.Erase();

                // ===== ĐẢM BẢO LAYER =====
                const string dimLayerName = "_mss.kichthuoc";
                LayerTable lt =
                    tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                ObjectId dimLayerId;
                if (!lt.Has(dimLayerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord
                    {
                        Name = dimLayerName
                    };
                    dimLayerId = lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                else
                {
                    dimLayerId = lt[dimLayerName];
                }

                // ===== TẠO DIM MỚI =====
                BlockTable bt =
                    tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord btr =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite)
                    as BlockTableRecord;

                for (int i = 0; i < parameters.Count - 1; i++)
                {
                    Point3d pA = pt1 + axis * parameters[i];
                    Point3d pB = pt1 + axis * parameters[i + 1];

                    RotatedDimension newDim = new RotatedDimension
                    {
                        XLine1Point = pA,
                        XLine2Point = pB,
                        DimLinePoint = ptDim,
                        Rotation = dim.Rotation,
                        DimensionStyle = db.Dimstyle,
                        LayerId = dimLayerId
                    };

                    btr.AppendEntity(newDim);
                    tr.AddNewlyCreatedDBObject(newDim, true);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nChia DIM xong – logic chuẩn trục DIM 👍");
        }
    }

    // ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> END OF CDD <<<<<<<<<<<<<<<<<<<<<<<<<<< ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

    // START OF SAA
    public class AutoDimCommand
    {
        private const double AutoDimTolerance = 1e-6;

        [CommandMethod("DAA_Dim_auto")]
        public void AutoDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // =============================
                // 1. CHỌN ĐỐI TƯỢNG GỐC
                // =============================
                PromptKeywordOptions baseModeOptions =
                    new PromptKeywordOptions("\nChọn mốc gốc [Object/Point] <Object>: ");
                baseModeOptions.AllowNone = true;
                baseModeOptions.Keywords.Add("Object");
                baseModeOptions.Keywords.Add("Point");
                baseModeOptions.Keywords.Default = "Object";

                PromptResult baseModeResult = ed.GetKeywords(baseModeOptions);
                if (baseModeResult.Status == PromptStatus.Cancel) return;

                string baseMode = baseModeResult.Status == PromptStatus.None
                    ? "Object"
                    : (baseModeResult.StringResult ?? "Object");

                Extents3d baseExt;
                Point3d baseCenter;

                if (string.Equals(baseMode, "Point", StringComparison.OrdinalIgnoreCase))
                {
                    PromptPointResult basePointResult = ed.GetPoint("\nChọn điểm gốc: ");
                    if (basePointResult.Status != PromptStatus.OK) return;

                    baseCenter = basePointResult.Value;
                    baseExt = new Extents3d(baseCenter, baseCenter);
                }
                else
                {
                    PromptSelectionOptions baseOpt = new PromptSelectionOptions();
                    baseOpt.MessageForAdding = "\nChọn Polyline hoặc nhóm đối tượng gốc:";
                    PromptSelectionResult baseRes = ed.GetSelection(baseOpt);
                    if (baseRes.Status != PromptStatus.OK) return;

                    baseExt = GetSelectionExtents(baseRes.Value, tr);
                    baseCenter = GetCenter(baseExt);
                }

                // =============================
                // 2. CHỌN ĐƯỜNG BAO (LINE / PLINE)
                // =============================
                PromptSelectionOptions boundOpt = new PromptSelectionOptions();
                boundOpt.MessageForAdding = "\nChọn các đường bao (Line / Polyline):";
                PromptSelectionResult boundRes = ed.GetSelection(boundOpt);
                if (boundRes.Status != PromptStatus.OK) return;

                Entity leftEntity = null;
                Entity rightEntity = null;
                Entity topEntity = null;
                Entity bottomEntity = null;
                double leftDistance = double.MaxValue;
                double rightDistance = double.MaxValue;
                double topDistance = double.MaxValue;
                double bottomDistance = double.MaxValue;

                foreach (SelectedObject sel in boundRes.Value)
                {
                    Entity ent = tr.GetObject(sel.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    Extents3d ext;
                    try
                    {
                        ext = ent.GeometricExtents;
                    }
                    catch
                    {
                        continue;
                    }

                    double currentLeftDistance = baseCenter.X - ext.MaxPoint.X;
                    if (currentLeftDistance >= -AutoDimTolerance &&
                        currentLeftDistance < leftDistance)
                    {
                        leftDistance = Math.Max(0.0, currentLeftDistance);
                        leftEntity = ent;
                    }

                    double currentRightDistance = ext.MinPoint.X - baseCenter.X;
                    if (currentRightDistance >= -AutoDimTolerance &&
                        currentRightDistance < rightDistance)
                    {
                        rightDistance = Math.Max(0.0, currentRightDistance);
                        rightEntity = ent;
                    }

                    double currentTopDistance = ext.MinPoint.Y - baseCenter.Y;
                    if (currentTopDistance >= -AutoDimTolerance &&
                        currentTopDistance < topDistance)
                    {
                        topDistance = Math.Max(0.0, currentTopDistance);
                        topEntity = ent;
                    }

                    double currentBottomDistance = baseCenter.Y - ext.MaxPoint.Y;
                    if (currentBottomDistance >= -AutoDimTolerance &&
                        currentBottomDistance < bottomDistance)
                    {
                        bottomDistance = Math.Max(0.0, currentBottomDistance);
                        bottomEntity = ent;
                    }
                }

                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // =============================
                // 3. OFFSET THEO DIMSTYLE
                // =============================
                double baseOffset =
                    db.Dimtxt + db.Dimexe + db.Dimgap;

                double offsetH = baseOffset * 6;
                double offsetV = baseOffset * 6;

                // =============================
                // 4. DIM TRÁI
                // =============================
                if (leftEntity != null)
                {
                    Extents3d ext = leftEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        0,
                        new Point3d(ext.MaxPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(0, baseExt.MinPoint.Y - offsetH * -1.5, 0)
                    );
                }

                // =============================
                // 5. DIM PHẢI
                // =============================
                if (rightEntity != null)
                {
                    Extents3d ext = rightEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        0,
                        new Point3d(baseExt.MaxPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(ext.MinPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(0, baseExt.MinPoint.Y - offsetH * -1.5, 0)
                    );
                }

                // =============================
                // 6. DIM TRÊN
                // =============================
                if (topEntity != null)
                {
                    Extents3d ext = topEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        Math.PI / 2,
                        new Point3d(baseExt.MinPoint.X, baseExt.MaxPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X, ext.MinPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X - offsetV * -1.5, 0, 0)
                    );
                }

                // =============================
                // 7. DIM DƯỚI
                // =============================
                if (bottomEntity != null)
                {
                    Extents3d ext = bottomEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        Math.PI / 2,
                        new Point3d(baseExt.MinPoint.X, ext.MaxPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X - offsetV * -1.5, 0, 0)
                    );
                }

                tr.Commit();
            }
        }

        // ======================================================
        // HÀM TẠO DIM
        // ======================================================
        private void CreateDim(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            double angle,
            Point3d p1,
            Point3d p2,
            Point3d dimPoint)
        {
            RotatedDimension dim = new RotatedDimension(
                angle, p1, p2, dimPoint, "", db.Dimstyle);
            // 👉 SET LAYER Ở ĐÂY
            dim.Layer = "_mss.kichthuoc";
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        // ======================================================
        // LẤY EXTENTS CỦA SELECTION
        // ======================================================
        private Extents3d GetSelectionExtents(SelectionSet ss, Transaction tr)
        {
            Extents3d? ext = null;

            foreach (SelectedObject sel in ss)
            {
                Entity ent = tr.GetObject(sel.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ext == null)
                    ext = ent.GeometricExtents;
                else
                {
                    Extents3d e = ent.GeometricExtents;
                    ext = new Extents3d(
                        new Point3d(
                            Math.Min(ext.Value.MinPoint.X, e.MinPoint.X),
                            Math.Min(ext.Value.MinPoint.Y, e.MinPoint.Y),
                            0),
                        new Point3d(
                            Math.Max(ext.Value.MaxPoint.X, e.MaxPoint.X),
                            Math.Max(ext.Value.MaxPoint.Y, e.MaxPoint.Y),
                            0)
                    );
                }
            }
            return ext.Value;
        }

        // ======================================================
        // LẤY TÂM EXTENTS
        // ======================================================
        private Point3d GetCenter(Extents3d ext)
        {
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                0
            );
        }
    }





    public class SmartDimXCommand
    {
        private const string DimLayerName = "_mss.kichthuoc";
        private const double DirectionTolerance = 1e-6;
        private const double SearchDistance = 1000000.0;

        [CommandMethod("SDX")]
        public void SmartDimX()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            PromptPointOptions dirOpt =
                new PromptPointOptions("\nChọn điểm để xác định hướng X (+/-): ");
            dirOpt.BasePoint = startRes.Value;
            dirOpt.UseBasePoint = true;

            PromptPointResult dirRes = ed.GetPoint(dirOpt);
            if (dirRes.Status != PromptStatus.OK) return;

            double deltaX = dirRes.Value.X - startRes.Value.X;
            if (Math.Abs(deltaX) < DirectionTolerance)
            {
                ed.WriteMessage("\nĐiểm hướng phải lệch theo trục X.");
                return;
            }

            double direction = deltaX > 0 ? 1.0 : -1.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnXAxis(currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng X đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    targetPoint.Value.X,
                    startRes.Value.Y,
                    startRes.Value.Z);

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dirRes.Value,
                    Rotation = 0.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        [CommandMethod("SDY")]
        public void SmartDimY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            PromptPointOptions dirOpt =
                new PromptPointOptions("\nChọn điểm để xác định hướng Y (+/-): ");
            dirOpt.BasePoint = startRes.Value;
            dirOpt.UseBasePoint = true;

            PromptPointResult dirRes = ed.GetPoint(dirOpt);
            if (dirRes.Status != PromptStatus.OK) return;

            double deltaY = dirRes.Value.Y - startRes.Value.Y;
            if (Math.Abs(deltaY) < DirectionTolerance)
            {
                ed.WriteMessage("\nĐiểm hướng phải lệch theo trục Y.");
                return;
            }

            double direction = deltaY > 0 ? 1.0 : -1.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnYAxis(currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng Y đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    startRes.Value.X,
                    targetPoint.Value.Y,
                    startRes.Value.Z);

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dirRes.Value,
                    Rotation = Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        private Point3d? FindNearestPointOnXAxis(
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateScanLine(startPoint, direction))
            {
                foreach (ObjectId id in currentSpace)
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    if (entity is Dimension) continue;
                    if (!(entity is Curve)) continue;

                    Point3dCollection intersections =
                        TryGetIntersections(entity, scanLine);
                    if (intersections == null || intersections.Count == 0) continue;

                    foreach (Point3d point in intersections)
                    {
                        double projectedDistance =
                            (point.X - startPoint.X) * direction;

                        if (projectedDistance <= DirectionTolerance) continue;
                        if (projectedDistance >= bestDistance) continue;

                        bestDistance = projectedDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private Point3d? FindNearestPointOnYAxis(
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateVerticalScanLine(startPoint, direction))
            {
                foreach (ObjectId id in currentSpace)
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    if (entity is Dimension) continue;
                    if (!(entity is Curve)) continue;

                    Point3dCollection intersections =
                        TryGetIntersections(entity, scanLine);
                    if (intersections == null || intersections.Count == 0) continue;

                    foreach (Point3d point in intersections)
                    {
                        double projectedDistance =
                            (point.Y - startPoint.Y) * direction;

                        if (projectedDistance <= DirectionTolerance) continue;
                        if (projectedDistance >= bestDistance) continue;

                        bestDistance = projectedDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private Point3dCollection TryGetIntersections(Entity entity, Line scanLine)
        {
            try
            {
                Point3dCollection intersections = new Point3dCollection();
                entity.IntersectWith(
                    scanLine,
                    Intersect.OnBothOperands,
                    intersections,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return intersections;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
        }

        private Line CreateScanLine(Point3d startPoint, double direction)
        {
            Point3d endPoint = new Point3d(
                startPoint.X + SearchDistance * direction,
                startPoint.Y,
                startPoint.Z);

            return new Line(startPoint, endPoint);
        }

        private Line CreateVerticalScanLine(Point3d startPoint, double direction)
        {
            Point3d endPoint = new Point3d(
                startPoint.X,
                startPoint.Y + SearchDistance * direction,
                startPoint.Z);

            return new Line(startPoint, endPoint);
        }

        private ObjectId EnsureDimLayer(Database db, Transaction tr)
        {
            LayerTable layerTable =
                tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

            if (layerTable == null) return ObjectId.Null;

            if (layerTable.Has(DimLayerName))
                return layerTable[DimLayerName];

            layerTable.UpgradeOpen();

            LayerTableRecord layer = new LayerTableRecord
            {
                Name = DimLayerName
            };

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }
    }

    public class TextSyncCommands
    {
        private const double TargetTextHeight = 5.0;
        private const double TextHeightTolerance = 1e-6;

        [CommandMethod("TT_TEXT_CHANGE_5", CommandFlags.UsePickSet)]
        public void SyncTextHeightFiveContent()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            ObjectId[] targetIds = TryConsumePickFirst(ed);

            if (targetIds != null && targetIds.Length > 0)
            {
                ed.WriteMessage(
                    $"\nTT_TEXT_CHANGE_5: dùng {targetIds.Length} đối tượng PickFirst đã chọn sẵn.");
            }

            PromptEntityOptions sourceOptions =
                new PromptEntityOptions("\nChọn text gốc: ");
            sourceOptions.SetRejectMessage("\nChỉ hỗ trợ DBText hoặc MText.");
            sourceOptions.AddAllowedClass(typeof(DBText), true);
            sourceOptions.AddAllowedClass(typeof(MText), true);

            PromptEntityResult sourceResult = ed.GetEntity(sourceOptions);
            if (sourceResult.Status != PromptStatus.OK)
            {
                return;
            }

            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nQuét chọn vùng có text cần đổi nội dung: "
                };

                if (targetIds == null || targetIds.Length == 0)
                {
                    PromptSelectionResult selectionResult = ed.GetSelection(selectionOptions);
                    if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
                    {
                        return;
                    }

                    targetIds = selectionResult.Value.GetObjectIds();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity sourceEntity =
                        tr.GetObject(sourceResult.ObjectId, OpenMode.ForRead) as Entity;
                    if (sourceEntity == null)
                    {
                        return;
                    }

                    TextSyncPayload payload = GetTextSyncPayload(sourceEntity);
                    if (!payload.IsValid)
                    {
                        ed.WriteMessage("\nKhông đọc được nội dung text gốc.");
                        return;
                    }

                    int replacedCount = 0;
                    int matchedCount = 0;

                    foreach (ObjectId objectId in targetIds)
                    {
                        if (objectId.IsNull)
                        {
                            continue;
                        }

                        Entity entity =
                            tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                        if (entity == null)
                        {
                            continue;
                        }

                        if (!TryGetTextHeight(entity, out double textHeight) ||
                            Math.Abs(textHeight - TargetTextHeight) > TextHeightTolerance)
                        {
                            continue;
                        }

                        matchedCount++;

                        if (objectId == sourceResult.ObjectId)
                        {
                            continue;
                        }

                        entity.UpgradeOpen();

                        if (entity is DBText dbText)
                        {
                            if (!string.Equals(dbText.TextString, payload.PlainText, StringComparison.Ordinal))
                            {
                                dbText.TextString = payload.PlainText;
                                replacedCount++;
                            }
                        }
                        else if (entity is MText mText)
                        {
                            string desiredContent = payload.MTextContents ?? payload.PlainText;
                            if (!string.Equals(mText.Contents, desiredContent, StringComparison.Ordinal))
                            {
                                mText.Contents = desiredContent;
                                replacedCount++;
                            }
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nTT_TEXT_CHANGE_5: đã đổi nội dung {replacedCount} text (lọc được {matchedCount} text có height = {TargetTextHeight.ToString("0.###", CultureInfo.InvariantCulture)}).");
                }
            }
            finally
            {
                if (previousSelectionOffscreen != null)
                {
                    Application.SetSystemVariable("SELECTIONOFFSCREEN", previousSelectionOffscreen);
                }
            }
        }

        private static ObjectId[] TryConsumePickFirst(Editor ed)
        {
            PromptSelectionResult impliedResult = ed.SelectImplied();
            if (impliedResult.Status != PromptStatus.OK || impliedResult.Value == null)
            {
                return null;
            }

            ObjectId[] objectIds = impliedResult.Value.GetObjectIds();
            if (objectIds == null || objectIds.Length == 0)
            {
                return null;
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            return objectIds;
        }

        private static TextSyncPayload GetTextSyncPayload(Entity entity)
        {
            if (entity is DBText dbText)
            {
                return new TextSyncPayload(dbText.TextString, dbText.TextString);
            }

            if (entity is MText mText)
            {
                return new TextSyncPayload(mText.Text, mText.Contents);
            }

            return TextSyncPayload.Invalid;
        }

        private static bool TryGetTextHeight(Entity entity, out double textHeight)
        {
            if (entity is DBText dbText)
            {
                textHeight = dbText.Height;
                return true;
            }

            if (entity is MText mText)
            {
                textHeight = mText.TextHeight;
                return true;
            }

            textHeight = 0.0;
            return false;
        }

        private readonly struct TextSyncPayload
        {
            public static readonly TextSyncPayload Invalid =
                new TextSyncPayload(string.Empty, null);

            public TextSyncPayload(string plainText, string mTextContents)
            {
                PlainText = plainText ?? string.Empty;
                MTextContents = mTextContents;
            }

            public string PlainText { get; }

            public string MTextContents { get; }

            public bool IsValid => !string.IsNullOrEmpty(PlainText) || MTextContents != null;
        }
    }

    public class SmartCopyToCenterCommands
    {
        [CommandMethod("CCC_SMART_COPY_TO_CENTER", CommandFlags.UsePickSet)]
        public void SmartCopyToCenter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            ObjectId[] sourceIds = TryConsumePickFirst(ed);

            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                PromptSelectionOptions sourceSelectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nQuét chọn đối tượng nguồn: "
                };

                if (sourceIds == null || sourceIds.Length == 0)
                {
                    PromptSelectionResult sourceSelectionResult = ed.GetSelection(sourceSelectionOptions);
                    if (sourceSelectionResult.Status != PromptStatus.OK || sourceSelectionResult.Value == null)
                    {
                        return;
                    }

                    sourceIds = sourceSelectionResult.Value.GetObjectIds();
                }
                else
                {
                    ed.WriteMessage(
                        $"\nCCC_SMART_COPY_TO_CENTER: dùng {sourceIds.Length} đối tượng PickFirst đã chọn sẵn.");
                }

                PromptPointResult seedPointResult =
                    ed.GetPoint("\nChọn điểm nằm trong vùng đích: ");
                if (seedPointResult.Status != PromptStatus.OK)
                {
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Extents3d sourceExtents = GetSelectionExtents(sourceIds, tr);
                    Point3d sourceCenter = GetCenter(sourceExtents);

                    DBObjectCollection boundaries = ed.TraceBoundary(seedPointResult.Value, false);
                    if (boundaries == null || boundaries.Count == 0)
                    {
                        ed.WriteMessage("\nCCC_SMART_COPY_TO_CENTER: không tìm được vùng bao quanh điểm đã chọn.");
                        return;
                    }

                    using (boundaries)
                    {
                        Curve boundaryCurve = FindBestBoundaryCurve(boundaries, seedPointResult.Value);
                        if (boundaryCurve == null)
                        {
                            ed.WriteMessage("\nCCC_SMART_COPY_TO_CENTER: không xác định được đường bao kín hợp lệ.");
                            return;
                        }

                        using (boundaryCurve)
                        {
                            Point3d targetCenter = GetBoundaryCenter(boundaryCurve);
                            Vector3d displacement = targetCenter - sourceCenter;

                            ObjectId currentSpaceId = db.CurrentSpaceId;
                            ObjectIdCollection sourceIdCollection =
                                new ObjectIdCollection(sourceIds);
                            IdMapping idMapping = new IdMapping();
                            db.DeepCloneObjects(sourceIdCollection, currentSpaceId, idMapping, false);

                            int copiedCount = 0;
                            foreach (IdPair pair in idMapping)
                            {
                                if (!pair.IsCloned || pair.Value.IsNull)
                                {
                                    continue;
                                }

                                Entity clonedEntity =
                                    tr.GetObject(pair.Value, OpenMode.ForWrite, false) as Entity;
                                if (clonedEntity == null)
                                {
                                    continue;
                                }

                                clonedEntity.TransformBy(Matrix3d.Displacement(displacement));
                                copiedCount++;
                            }

                            tr.Commit();

                            ed.WriteMessage(
                                $"\nCCC_SMART_COPY_TO_CENTER: đã copy {copiedCount} đối tượng từ tâm nguồn tới tâm vùng đích.");
                        }
                    }
                }
            }
            finally
            {
                if (previousSelectionOffscreen != null)
                {
                    Application.SetSystemVariable("SELECTIONOFFSCREEN", previousSelectionOffscreen);
                }
            }
        }

        private static ObjectId[] TryConsumePickFirst(Editor ed)
        {
            PromptSelectionResult impliedResult = ed.SelectImplied();
            if (impliedResult.Status != PromptStatus.OK || impliedResult.Value == null)
            {
                return null;
            }

            ObjectId[] objectIds = impliedResult.Value.GetObjectIds();
            if (objectIds == null || objectIds.Length == 0)
            {
                return null;
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            return objectIds;
        }

        private static Curve FindBestBoundaryCurve(DBObjectCollection boundaries, Point3d seedPoint)
        {
            Curve bestCurve = null;
            double bestArea = double.MaxValue;

            foreach (DBObject dbObject in boundaries)
            {
                if (!(dbObject is Curve curve) || !curve.Closed)
                {
                    dbObject.Dispose();
                    continue;
                }

                double? area = TryGetBoundaryArea(curve);
                if (!area.HasValue || area.Value <= 1e-6)
                {
                    curve.Dispose();
                    continue;
                }

                if (area.Value < bestArea)
                {
                    bestCurve?.Dispose();
                    bestCurve = curve;
                    bestArea = area.Value;
                }
                else
                {
                    curve.Dispose();
                }
            }

            return bestCurve;
        }

        private static double? TryGetBoundaryArea(Curve curve)
        {
            DBObjectCollection curveCollection = new DBObjectCollection();
            curveCollection.Add(curve.Clone() as DBObject);

            DBObjectCollection regions = null;
            try
            {
                regions = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(curveCollection);
                if (regions == null || regions.Count == 0)
                {
                    return null;
                }

                using (regions)
                {
                    Autodesk.AutoCAD.DatabaseServices.Region region =
                        regions[0] as Autodesk.AutoCAD.DatabaseServices.Region;
                    if (region == null)
                    {
                        return null;
                    }

                    using (region)
                    {
                        return region.Area;
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                foreach (DBObject dbObject in curveCollection)
                {
                    dbObject?.Dispose();
                }
            }
        }

        private static Point3d GetBoundaryCenter(Curve curve)
        {
            DBObjectCollection curveCollection = new DBObjectCollection();
            curveCollection.Add(curve.Clone() as DBObject);

            try
            {
                using (DBObjectCollection regions =
                    Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(curveCollection))
                {
                    if (regions != null &&
                        regions.Count > 0 &&
                        regions[0] is Autodesk.AutoCAD.DatabaseServices.Region region)
                    {
                        using (region)
                        {
                            Point3d origin = Point3d.Origin;
                            Vector3d xAxis = Vector3d.XAxis;
                            Vector3d yAxis = Vector3d.YAxis;
                            RegionAreaProperties props = region.AreaProperties(ref origin, ref xAxis, ref yAxis);
                            return new Point3d(props.Centroid.X, props.Centroid.Y, 0.0);
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                foreach (DBObject dbObject in curveCollection)
                {
                    dbObject?.Dispose();
                }
            }

            Extents3d extents = curve.GeometricExtents;
            return GetCenter(extents);
        }

        private static Extents3d GetSelectionExtents(IEnumerable<ObjectId> objectIds, Transaction tr)
        {
            Extents3d? extents = null;

            foreach (ObjectId objectId in objectIds)
            {
                if (objectId.IsNull)
                {
                    continue;
                }

                Entity entity = tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                if (entity == null)
                {
                    continue;
                }

                Extents3d currentExtents;
                try
                {
                    currentExtents = entity.GeometricExtents;
                }
                catch
                {
                    continue;
                }

                if (extents == null)
                {
                    extents = currentExtents;
                    continue;
                }

                extents = new Extents3d(
                    new Point3d(
                        Math.Min(extents.Value.MinPoint.X, currentExtents.MinPoint.X),
                        Math.Min(extents.Value.MinPoint.Y, currentExtents.MinPoint.Y),
                        Math.Min(extents.Value.MinPoint.Z, currentExtents.MinPoint.Z)),
                    new Point3d(
                        Math.Max(extents.Value.MaxPoint.X, currentExtents.MaxPoint.X),
                        Math.Max(extents.Value.MaxPoint.Y, currentExtents.MaxPoint.Y),
                        Math.Max(extents.Value.MaxPoint.Z, currentExtents.MaxPoint.Z)));
            }

            if (extents == null)
            {
                throw new InvalidOperationException("Selection has no valid extents.");
            }

            return extents.Value;
        }

        private static Point3d GetCenter(Extents3d extents)
        {
            return new Point3d(
                (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
        }
    }

    public class DungXPaletteEntry : IExtensionApplication
    {
        [CommandMethod("DXPALETTE")]
        public void ShowPalette()
        {
            DungXPaletteHost.ShowPalette();
        }

        [CommandMethod("DXPALETTERELOAD")]
        public void ReloadPalette()
        {
            DungXPaletteHost.ReloadPaletteData(true);
        }

        [CommandMethod("DXPALETTESETFOLDER")]
        public void SetLispFolder()
        {
            DungXPaletteHost.ChooseLispFolder(true);
        }

        [CommandMethod("DXRIBBON")]
        public void ShowRibbon()
        {
            DungXRibbonHost.ShowRibbon();
        }

        [CommandMethod("DXRIBBONRELOAD")]
        public void ReloadRibbon()
        {
            DungXRibbonHost.ReloadRibbon(true);
        }

        public void Initialize()
        {
            DungXPaletteHost.Initialize();
            DungXRibbonHost.Initialize();
        }

        public void Terminate()
        {
            DungXRibbonHost.Terminate();
        }
    }

    internal static class DungXRibbonHost
    {
        private const string TabId = "DUNGX_RIBBON_TAB";
        private static readonly string[] DimensionCommands =
            { "DAA_Dim_auto", "SDX", "SDY", "CDD2_CHIADIM" };

        private static readonly string[] StretchCommands = { "SS" };

        private static readonly string[] ToolCommands =
            { "DXPALETTE", "DXPALETTERELOAD", "DXPALETTESETFOLDER", "DXRIBBONRELOAD" };

        private static readonly string[] HiddenCommands =
            { "DXRIBBON" };

        private static readonly HashSet<string> KnownCommands =
            new HashSet<string>(
                DimensionCommands
                    .Concat(StretchCommands)
                    .Concat(ToolCommands)
                    .Concat(HiddenCommands),
                StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, RibbonCommandStyle> RibbonStyles =
            BuildRibbonStyles();
        private static readonly Dictionary<string, Media.ImageSource> LargeImageCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Media.ImageSource> SmallImageCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static bool _idleHooked;

        public static void Initialize()
        {
            EnsureRibbonCreated(false);
        }

        public static void Terminate()
        {
            if (_idleHooked)
            {
                Application.Idle -= OnApplicationIdle;
                _idleHooked = false;
            }
        }

        public static void ShowRibbon()
        {
            EnsureRibbonCreated(false);

            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                return;
            }

            RibbonTab tab = FindRibbonTab(ribbon);
            if (tab == null)
            {
                return;
            }

            tab.IsVisible = true;
            ribbon.ActiveTab = tab;
        }

        public static void ReloadRibbon(bool showMessage)
        {
            bool created = EnsureRibbonCreated(true);
            if (created)
            {
                ShowRibbon();
            }

            if (!showMessage)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage(
                created
                    ? "\nDX Ribbon da duoc reload."
                    : "\nDX Ribbon se duoc tao khi AutoCAD san sang giao dien Ribbon.");
        }

        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            if (!EnsureRibbonCreated(false))
            {
                return;
            }

            Application.Idle -= OnApplicationIdle;
            _idleHooked = false;
        }

        private static void EnsureIdleHook()
        {
            if (_idleHooked)
            {
                return;
            }

            Application.Idle += OnApplicationIdle;
            _idleHooked = true;
        }

        private static bool EnsureRibbonCreated(bool forceReload)
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                EnsureIdleHook();
                return false;
            }

            RibbonTab existing = FindRibbonTab(ribbon);
            if (existing != null)
            {
                if (!forceReload)
                {
                    return true;
                }

                ribbon.Tabs.Remove(existing);
            }

            RibbonTab tab = new RibbonTab
            {
                Title = "DUNGX",
                Id = TabId,
                Name = TabId
            };

            foreach (RibbonPanel panel in BuildPanels())
            {
                tab.Panels.Add(panel);
            }

            ribbon.Tabs.Add(tab);
            return true;
        }

        private static RibbonTab FindRibbonTab(RibbonControl ribbon)
        {
            if (ribbon == null)
            {
                return null;
            }

            return ribbon.Tabs
                .OfType<RibbonTab>()
                .FirstOrDefault(tab => string.Equals(tab.Id, TabId, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<RibbonPanel> BuildPanels()
        {
            List<PaletteCommandItem> builtInItems = PaletteCommandCatalog.BuildItems()
                .Where(item => item.SourceKind == PaletteSourceKind.BuiltInDll)
                .Where(item => !HiddenCommands.Contains(item.CommandName, StringComparer.OrdinalIgnoreCase))
                .OrderBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<PaletteCommandItem> dimensions = PickCommands(builtInItems, DimensionCommands);
            List<PaletteCommandItem> stretches = PickCommands(builtInItems, StretchCommands);
            List<PaletteCommandItem> tools = PickCommands(builtInItems, ToolCommands);
            List<PaletteCommandItem> more = builtInItems
                .Where(item => !KnownCommands.Contains(item.CommandName))
                .OrderBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dimensions.Count > 0)
            {
                yield return CreatePanel(
                    "Dimension",
                    "Smart dim and split-dimension tools.",
                    dimensions.First(),
                    dimensions.Skip(1));
            }

            if (stretches.Count > 0)
            {
                yield return CreatePanel(
                    "Stretch",
                    "Native-like smart stretch workflow.",
                    stretches.First(),
                    stretches.Skip(1));
            }

            if (tools.Count > 0)
            {
                yield return CreatePanel(
                    "Workspace",
                    "Palette and ribbon management.",
                    tools.First(),
                    tools.Skip(1));
            }

            if (more.Count > 0)
            {
                yield return CreatePanel(
                    "More",
                    "Other commands discovered from this DLL.",
                    more.First(),
                    more.Skip(1));
            }
        }

        private static List<PaletteCommandItem> PickCommands(
            IEnumerable<PaletteCommandItem> items,
            IEnumerable<string> orderedNames)
        {
            Dictionary<string, PaletteCommandItem> map = items
                .GroupBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            List<PaletteCommandItem> result = new List<PaletteCommandItem>();
            foreach (string commandName in orderedNames)
            {
                if (map.TryGetValue(commandName, out PaletteCommandItem item))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        private static RibbonPanel CreatePanel(
            string title,
            string description,
            PaletteCommandItem featuredItem,
            IEnumerable<PaletteCommandItem> secondaryItems)
        {
            RibbonPanelSource source = new RibbonPanelSource
            {
                Title = title,
                Name = "DUNGX_" + title.ToUpperInvariant(),
                Description = description
            };

            if (featuredItem != null)
            {
                source.Items.Add(CreateButton(featuredItem, true));
            }

            List<PaletteCommandItem> secondaryList = secondaryItems
                .Where(item => item != null)
                .ToList();

            if (secondaryList.Count > 0)
            {
                RibbonRowPanel row = new RibbonRowPanel
                {
                    Text = title + " Quick",
                    ShowText = false,
                    IsTopJustified = true
                };

                foreach (PaletteCommandItem item in secondaryList)
                {
                    row.Items.Add(CreateButton(item, false));
                }

                source.Items.Add(row);
            }

            return new RibbonPanel
            {
                Source = source
            };
        }

        private static RibbonButton CreateButton(PaletteCommandItem item, bool large)
        {
            RibbonCommandStyle style = GetStyle(item.CommandName);
            string description = string.IsNullOrWhiteSpace(style.Description)
                ? item.CommandName
                : style.Description;
            RibbonToolTip toolTip = new RibbonToolTip
            {
                Title = style.Title,
                Content = description,
                Command = item.CommandName
            };

            return new RibbonButton
            {
                Id = "DUNGX_BTN_" + item.CommandName.ToUpperInvariant(),
                Name = item.CommandName,
                Text = large ? style.LargeText : style.SmallText,
                ShowText = true,
                ShowImage = true,
                Image = GetIcon(item.CommandName, false),
                LargeImage = GetIcon(item.CommandName, true),
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Description = description,
                ToolTip = toolTip,
                CommandHandler = new DungXRibbonCommandHandler(item),
                CommandParameter = item,
                Tag = item,
                KeyTip = style.KeyTip
            };
        }

        private static RibbonCommandStyle GetStyle(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return RibbonCommandStyle.CreateDefault(string.Empty);
            }

            if (RibbonStyles.TryGetValue(commandName, out RibbonCommandStyle style))
            {
                return style;
            }

            return RibbonCommandStyle.CreateDefault(commandName);
        }

        private static Media.ImageSource GetIcon(string commandName, bool large)
        {
            Dictionary<string, Media.ImageSource> cache = large ? LargeImageCache : SmallImageCache;
            if (cache.TryGetValue(commandName, out Media.ImageSource cached))
            {
                return cached;
            }

            RibbonCommandStyle style = GetStyle(commandName);
            Media.ImageSource created = CreateIcon(style, large ? 32 : 16);
            cache[commandName] = created;
            return created;
        }

        private static Media.ImageSource CreateIcon(RibbonCommandStyle style, int size)
        {
            using (Bitmap bitmap = new Bitmap(size, size))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                Rectangle bounds = new Rectangle(0, 0, size - 1, size - 1);
                int radius = Math.Max(4, size / 5);

                using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
                using (SolidBrush backgroundBrush = new SolidBrush(style.BackColor))
                using (SolidBrush accentBrush = new SolidBrush(style.AccentColor))
                using (Pen borderPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                {
                    graphics.FillPath(backgroundBrush, path);

                    Rectangle accentRect = new Rectangle(0, 0, size, Math.Max(3, size / 5));
                    graphics.FillRectangle(accentBrush, accentRect);
                    graphics.DrawPath(borderPen, path);
                }

                float fontSize = size >= 32 ? 12f : 7f;
                FontStyle fontStyle = style.IconText.Length >= 3 ? FontStyle.Bold : FontStyle.Regular;
                using (System.Drawing.Font font = new System.Drawing.Font(
                    "Segoe UI",
                    fontSize,
                    fontStyle,
                    GraphicsUnit.Pixel))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                using (StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    RectangleF textRect = new RectangleF(1, size * 0.18f, size - 2, size * 0.72f);
                    graphics.DrawString(style.IconText, font, textBrush, textRect, format);
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    stream.Position = 0;

                    Imaging.BitmapImage image = new Imaging.BitmapImage();
                    image.BeginInit();
                    image.CacheOption = Imaging.BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();

            if (diameter > bounds.Width)
            {
                diameter = bounds.Width;
            }

            if (diameter > bounds.Height)
            {
                diameter = bounds.Height;
            }

            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Dictionary<string, RibbonCommandStyle> BuildRibbonStyles()
        {
            return new Dictionary<string, RibbonCommandStyle>(StringComparer.OrdinalIgnoreCase)
            {
                ["DAA_Dim_auto"] = new RibbonCommandStyle(
                    "DAA Auto Dim",
                    "DAA\nAuto",
                    "DAA",
                    "DAA",
                    "Center-based auto dimension workflow.",
                    "DA",
                    Color.FromArgb(33, 45, 74),
                    Color.FromArgb(72, 140, 255)),
                ["SDX"] = new RibbonCommandStyle(
                    "Smart Dim X",
                    "Smart\nDim X",
                    "Dim X",
                    "DX",
                    "Dimension to the nearest object along the X axis.",
                    "SX",
                    Color.FromArgb(28, 62, 78),
                    Color.FromArgb(0, 184, 212)),
                ["SDY"] = new RibbonCommandStyle(
                    "Smart Dim Y",
                    "Smart\nDim Y",
                    "Dim Y",
                    "DY",
                    "Dimension to the nearest object along the Y axis.",
                    "SY",
                    Color.FromArgb(21, 74, 70),
                    Color.FromArgb(0, 200, 150)),
                ["CDD2_CHIADIM"] = new RibbonCommandStyle(
                    "Split Dimension",
                    "Split\nDim",
                    "Split",
                    "CD",
                    "Split an existing dimension into multiple segments.",
                    "CD",
                    Color.FromArgb(63, 45, 86),
                    Color.FromArgb(176, 112, 255)),
                ["SS"] = new RibbonCommandStyle(
                    "Smart Stretch",
                    "Smart\nStretch",
                    "Stretch",
                    "SS",
                    "Window-based smart stretch with preview.",
                    "SS",
                    Color.FromArgb(90, 48, 32),
                    Color.FromArgb(255, 144, 64)),
                ["DXPALETTE"] = new RibbonCommandStyle(
                    "DX Palette",
                    "DX\nPalette",
                    "Palette",
                    "PL",
                    "Open the DungX command palette.",
                    "DP",
                    Color.FromArgb(46, 62, 49),
                    Color.FromArgb(110, 201, 124)),
                ["DXPALETTERELOAD"] = new RibbonCommandStyle(
                    "Refresh Palette",
                    "Refresh",
                    "Refresh",
                    "RF",
                    "Reload palette commands and sources.",
                    "RP",
                    Color.FromArgb(52, 52, 57),
                    Color.FromArgb(173, 181, 189)),
                ["DXPALETTESETFOLDER"] = new RibbonCommandStyle(
                    "Set Lisp Folder",
                    "Set Lisp\nFolder",
                    "Lisp",
                    "LS",
                    "Choose the root folder for DungX Lisp files.",
                    "LF",
                    Color.FromArgb(55, 58, 41),
                    Color.FromArgb(209, 174, 79)),
                ["DXRIBBONRELOAD"] = new RibbonCommandStyle(
                    "Refresh Ribbon",
                    "Refresh\nRibbon",
                    "Ribbon",
                    "RB",
                    "Reload the DUNGX ribbon layout.",
                    "RR",
                    Color.FromArgb(53, 46, 58),
                    Color.FromArgb(220, 120, 255))
            };
        }
    }

    internal sealed class RibbonCommandStyle
    {
        public RibbonCommandStyle(
            string title,
            string largeText,
            string smallText,
            string iconText,
            string description,
            string keyTip,
            Color backColor,
            Color accentColor)
        {
            Title = title;
            LargeText = largeText;
            SmallText = smallText;
            IconText = iconText;
            Description = description;
            KeyTip = keyTip;
            BackColor = backColor;
            AccentColor = accentColor;
        }

        public string Title { get; }

        public string LargeText { get; }

        public string SmallText { get; }

        public string IconText { get; }

        public string Description { get; }

        public string KeyTip { get; }

        public Color BackColor { get; }

        public Color AccentColor { get; }

        public static RibbonCommandStyle CreateDefault(string commandName)
        {
            string cleaned = string.IsNullOrWhiteSpace(commandName)
                ? "CMD"
                : commandName.Replace("_", " ").Trim();

            string[] words = cleaned
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string title = words.Length == 0 ? "Command" : string.Join(" ", words);
            string smallText = words.Length >= 2 ? words[0] : title;
            string largeText = words.Length >= 2
                ? words[0] + "\n" + words[1]
                : title;
            string icon = words.Length >= 2
                ? (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant()
                : title.Substring(0, Math.Min(2, title.Length)).ToUpperInvariant();

            return new RibbonCommandStyle(
                title,
                largeText,
                smallText,
                icon,
                title,
                icon,
                Color.FromArgb(58, 62, 70),
                Color.FromArgb(120, 170, 255));
        }
    }

    internal sealed class DungXRibbonCommandHandler : System.Windows.Input.ICommand
    {
        private readonly PaletteCommandItem _item;

        public DungXRibbonCommandHandler(PaletteCommandItem item)
        {
            _item = item;
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            PaletteCommandItem item = _item;

            if (item == null && parameter is PaletteCommandItem directItem)
            {
                item = directItem;
            }

            if (item == null && parameter is RibbonButton ribbonButton)
            {
                item = ribbonButton.CommandParameter as PaletteCommandItem
                    ?? ribbonButton.Tag as PaletteCommandItem;
            }

            if (item != null)
            {
                DungXPaletteHost.RunCommand(item);
            }
        }
    }

    internal static class DungXPaletteHost
    {
        private static readonly Guid PaletteGuid =
            new Guid("2E5D6E63-70A5-4D41-B72B-50BFC66F37D1");

        private static PaletteSet _paletteSet;
        private static DungXPaletteControl _paletteControl;

        public static void Initialize()
        {
            EnsurePalette();
            if (PaletteStartupStore.LoadAutoShow())
            {
                ReloadPaletteData(false);
                _paletteSet.Visible = true;
            }
        }

        public static void ShowPalette()
        {
            EnsurePalette();
            ReloadPaletteData(false);
            _paletteSet.Visible = true;
        }

        public static bool IsAutoShowEnabled()
        {
            return PaletteStartupStore.LoadAutoShow();
        }

        public static void SetAutoShowEnabled(bool enabled)
        {
            PaletteStartupStore.SaveAutoShow(enabled);
        }

        public static void ReloadPaletteData(bool showMessage)
        {
            EnsurePalette();
            _paletteControl.ReloadData(showMessage);
        }

        public static bool ChooseLispFolder(bool showMessage)
        {
            EnsurePalette();
            bool selected = DungXLispResolver.PickLispRoot(showMessage);
            if (selected)
            {
                _paletteControl.ReloadData(showMessage);
            }
            return selected;
        }

        public static void RunCommand(PaletteCommandItem item)
        {
            if (item == null)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WF.MessageBox.Show(
                    "Khong co ban ve dang active.",
                    "DungX Palette",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return;
            }

            if (!EnsureSourceLoaded(doc, item))
            {
                _paletteControl.ReloadData(true);
                return;
            }

            doc.SendStringToExecute(item.CommandName + " ", true, false, false);
            _paletteControl.SetStatus(
                $"Dang chay {item.CommandName} | {item.SourceLabel}");
        }

        private static bool EnsureSourceLoaded(Document doc, PaletteCommandItem item)
        {
            if (item.SourceKind == PaletteSourceKind.BuiltInDll ||
                item.SourceKind == PaletteSourceKind.ActionMacro ||
                item.SourceKind == PaletteSourceKind.ManualAlias)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                WF.MessageBox.Show(
                    "Khong tim thay file nguon:\n" + item.SourcePath,
                    "DungX Palette",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return false;
            }

            if (item.SourceKind == PaletteSourceKind.ManagedDll)
            {
                string netloadExpr =
                    "_.NETLOAD \"" + item.SourcePath.Replace("\"", "\"\"") + "\" ";
                doc.SendStringToExecute(netloadExpr, true, false, false);
                return true;
            }

            string loadExpr = $"(load \"{EscapeForLisp(item.SourcePath)}\") ";
            doc.SendStringToExecute(loadExpr, true, false, false);
            return true;
        }

        private static string EscapeForLisp(string path)
        {
            return path
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static void EnsurePalette()
        {
            if (_paletteSet != null && _paletteControl != null)
            {
                return;
            }

            _paletteControl = new DungXPaletteControl();
            _paletteSet = new PaletteSet("DungX Commands", PaletteGuid)
            {
                Style = PaletteSetStyles.ShowAutoHideButton
                      | PaletteSetStyles.ShowCloseButton
                      | PaletteSetStyles.Snappable,
                MinimumSize = new Size(110, 220),
                Size = new Size(560, 700),
                DockEnabled = DockSides.Left | DockSides.Right,
                KeepFocus = false
            };

            _paletteSet.Add("Command List", _paletteControl);
        }
    }

    internal sealed class DungXPaletteControl : WF.UserControl
    {
        private static readonly Color BackgroundColor = Color.FromArgb(45, 45, 48);
        private static readonly Color PanelColor = Color.FromArgb(37, 37, 38);
        private static readonly Color BorderColor = Color.FromArgb(63, 63, 70);
        private static readonly Color ForegroundColor = Color.FromArgb(241, 241, 241);
        private static readonly Color AccentColor = Color.FromArgb(0, 122, 204);
        private static readonly Color SelectionColor = Color.FromArgb(62, 62, 64);

        private readonly WF.TextBox _searchBox;
        private readonly WF.TableLayoutPanel _filterPanel;
        private readonly WF.FlowLayoutPanel _buttonPanel;
        private readonly WF.Label _sourceLabel;
        private readonly WF.Label _typeLabel;
        private readonly WF.Label _sortLabel;
        private readonly WF.Label _searchLabel;
        private readonly WF.ComboBox _sourceFilter;
        private readonly WF.ComboBox _typeFilter;
        private readonly WF.ComboBox _sortModeFilter;
        private readonly WF.DataGridView _commandGrid;
        private readonly WF.Button _runButton;
        private readonly WF.Button _reloadButton;
        private readonly WF.Button _folderButton;
        private readonly WF.Button _refreshButton;
        private readonly WF.Button _addSourceButton;
        private readonly WF.Button _addManualButton;
        private readonly WF.Button _removeSourceButton;
        private readonly WF.Label _statusLabel;
        private readonly WF.CheckBox _autoShowCheckBox;
        private List<PaletteCommandItem> _items;
        private Point _dragStartPoint;
        private int _dragRowIndex = -1;

        public DungXPaletteControl()
        {
            Dock = WF.DockStyle.Fill;
            BackColor = BackgroundColor;
            ForeColor = ForegroundColor;
            Font = new System.Drawing.Font(
                "Segoe UI",
                9F,
                FontStyle.Regular,
                GraphicsUnit.Point);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WF.Padding(8),
                BackColor = BackgroundColor
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            Controls.Add(layout);

            _filterPanel = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                ColumnCount = 8,
                AutoSize = true,
                BackColor = BackgroundColor
            };
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 140f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            layout.Controls.Add(_filterPanel, 0, 0);

            _sourceLabel = CreateLabel("Source");
            _sourceLabel.Margin = new WF.Padding(0, 6, 8, 0);
            _filterPanel.Controls.Add(_sourceLabel, 0, 0);

            _sourceFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _sourceFilter.Items.AddRange(new object[] { "All", "DUNGX Custom", "DUNGX 2" });
            _sourceFilter.SelectedIndex = 0;
            _sourceFilter.SelectedIndexChanged += (_, __) => BindGrid();
            _filterPanel.Controls.Add(_sourceFilter, 1, 0);

            _typeLabel = CreateLabel("Type");
            _typeLabel.Margin = new WF.Padding(0, 6, 8, 0);
            _filterPanel.Controls.Add(_typeLabel, 2, 0);

            _typeFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _typeFilter.Items.AddRange(new object[]
            {
                "All",
                "LISP",
                "DLL",
                "VLX",
                "Action",
                "Manual"
            });
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectedIndexChanged += (_, __) => BindGrid();
            _filterPanel.Controls.Add(_typeFilter, 3, 0);

            _sortLabel = CreateLabel("Sort");
            _sortLabel.Margin = new WF.Padding(8, 6, 8, 0);
            _filterPanel.Controls.Add(_sortLabel, 4, 0);

            _sortModeFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _sortModeFilter.Items.AddRange(new object[]
            {
                "Custom",
                "A-Z"
            });
            _sortModeFilter.SelectedIndexChanged += SortModeFilter_SelectedIndexChanged;
            _filterPanel.Controls.Add(_sortModeFilter, 5, 0);

            _searchLabel = CreateLabel("Search");
            _searchLabel.Margin = new WF.Padding(8, 6, 8, 0);
            _filterPanel.Controls.Add(_searchLabel, 6, 0);

            _searchBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 0, 0, 0),
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                BorderStyle = WF.BorderStyle.FixedSingle
            };
            _searchBox.TextChanged += (_, __) => BindGrid();
            _filterPanel.Controls.Add(_searchBox, 7, 0);

            _buttonPanel = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new WF.Padding(0, 8, 0, 8),
                BackColor = BackgroundColor
            };
            layout.Controls.Add(_buttonPanel, 0, 1);

            _runButton = CreateButton("Run", (_, __) => RunSelected());
            _reloadButton = CreateButton("Reload LISP", (_, __) => ReloadLisps());
            _folderButton = CreateButton("LISP Folder", (_, __) => PickFolder());
            _addSourceButton = CreateButton("Add Source", (_, __) => AddSource());
            _addManualButton = CreateButton("Add Manual", (_, __) => AddManualAlias());
            _removeSourceButton = CreateButton("Remove Source", (_, __) => RemoveSelectedSource());
            _refreshButton = CreateButton("Refresh List", (_, __) => ReloadData(true));
            _autoShowCheckBox = CreateCheckBox("Auto Open", AutoShowCheckBox_CheckedChanged);

            _buttonPanel.Controls.Add(_runButton);
            _buttonPanel.Controls.Add(_reloadButton);
            _buttonPanel.Controls.Add(_folderButton);
            _buttonPanel.Controls.Add(_addSourceButton);
            _buttonPanel.Controls.Add(_addManualButton);
            _buttonPanel.Controls.Add(_removeSourceButton);
            _buttonPanel.Controls.Add(_refreshButton);
            _buttonPanel.Controls.Add(_autoShowCheckBox);

            _commandGrid = CreateGrid();
            _commandGrid.AllowDrop = true;
            _commandGrid.CellClick += CommandGrid_CellClick;
            _commandGrid.CellDoubleClick += CommandGrid_CellDoubleClick;
            _commandGrid.KeyDown += CommandGrid_KeyDown;
            _commandGrid.CellEndEdit += CommandGrid_CellEndEdit;
            _commandGrid.MouseDown += CommandGrid_MouseDown;
            _commandGrid.MouseMove += CommandGrid_MouseMove;
            _commandGrid.DragOver += CommandGrid_DragOver;
            _commandGrid.DragDrop += CommandGrid_DragDrop;
            layout.Controls.Add(_commandGrid, 0, 2);

            _statusLabel = CreateLabel("San sang");
            _statusLabel.Dock = WF.DockStyle.Fill;
            _statusLabel.Padding = new WF.Padding(0, 8, 0, 0);
            layout.Controls.Add(_statusLabel, 0, 3);

            _items = new List<PaletteCommandItem>();
            Resize += (_, __) => ApplyResponsiveLayout();
            ApplyResponsiveLayout();
            SetSortMode(PaletteLayoutStore.LoadSortMode());
            _autoShowCheckBox.Checked = DungXPaletteHost.IsAutoShowEnabled();
            ReloadData(false);
        }

        public void ReloadData(bool showMessage)
        {
            string currentFilter = Convert.ToString(_sourceFilter.SelectedItem) ?? "All";
            string selectedCommand = GetSelectedCommandName();
            _items = PaletteCommandCatalog.BuildItems();
            PaletteLayoutStore.ApplyLayout(_items);
            PaletteLayoutStore.SaveLayout(_items);
            RefreshSourceFilter(currentFilter);
            BindGrid(selectedCommand);

            string root = DungXLispResolver.GetDisplayRoot();
            bool ready = DungXLispResolver.TryResolveAllLispFiles(out _, out _);
            string status = ready
                ? $"Ready | LISP root: {root} | Tu dong quet command OK"
                : $"Chua thay du file LISP | Root hien tai: {root}";

            SetStatus(status);

            if (showMessage)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n" + status);
            }
        }

        public void SetStatus(string message)
        {
            _statusLabel.Text = message;
        }

        private void ApplyResponsiveLayout()
        {
            bool compact = Width <= 260;
            bool ultraCompact = Width <= 150;

            _filterPanel.SuspendLayout();
            _buttonPanel.SuspendLayout();

            _filterPanel.Controls.Clear();
            _filterPanel.ColumnStyles.Clear();
            _filterPanel.RowStyles.Clear();

            if (ultraCompact)
            {
                _sourceLabel.Visible = false;
                _typeLabel.Visible = false;
                _sortLabel.Visible = false;
                _searchLabel.Visible = false;

                _filterPanel.ColumnCount = 1;
                _filterPanel.RowCount = 4;
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));

                _sourceFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _typeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _sortModeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _searchBox.Margin = new WF.Padding(0);

                _filterPanel.Controls.Add(_sourceFilter, 0, 0);
                _filterPanel.Controls.Add(_typeFilter, 0, 1);
                _filterPanel.Controls.Add(_sortModeFilter, 0, 2);
                _filterPanel.Controls.Add(_searchBox, 0, 3);
            }
            else if (compact)
            {
                _sourceLabel.Visible = true;
                _typeLabel.Visible = true;
                _sortLabel.Visible = true;
                _searchLabel.Visible = true;

                _filterPanel.ColumnCount = 2;
                _filterPanel.RowCount = 4;
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));

                _sourceFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _typeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _sortModeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _searchBox.Margin = new WF.Padding(0);

                _filterPanel.Controls.Add(_sourceLabel, 0, 0);
                _filterPanel.Controls.Add(_sourceFilter, 1, 0);
                _filterPanel.Controls.Add(_typeLabel, 0, 1);
                _filterPanel.Controls.Add(_typeFilter, 1, 1);
                _filterPanel.Controls.Add(_sortLabel, 0, 2);
                _filterPanel.Controls.Add(_sortModeFilter, 1, 2);
                _filterPanel.Controls.Add(_searchLabel, 0, 3);
                _filterPanel.Controls.Add(_searchBox, 1, 3);
            }
            else
            {
                _sourceLabel.Visible = true;
                _typeLabel.Visible = true;
                _sortLabel.Visible = true;
                _searchLabel.Visible = true;

                _filterPanel.ColumnCount = 8;
                _filterPanel.RowCount = 1;
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 140f));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));

                _sourceFilter.Margin = new WF.Padding(0);
                _typeFilter.Margin = new WF.Padding(0);
                _sortModeFilter.Margin = new WF.Padding(0);
                _searchBox.Margin = new WF.Padding(0);

                _filterPanel.Controls.Add(_sourceLabel, 0, 0);
                _filterPanel.Controls.Add(_sourceFilter, 1, 0);
                _filterPanel.Controls.Add(_typeLabel, 2, 0);
                _filterPanel.Controls.Add(_typeFilter, 3, 0);
                _filterPanel.Controls.Add(_sortLabel, 4, 0);
                _filterPanel.Controls.Add(_sortModeFilter, 5, 0);
                _filterPanel.Controls.Add(_searchLabel, 6, 0);
                _filterPanel.Controls.Add(_searchBox, 7, 0);
            }

            _buttonPanel.FlowDirection = compact
                ? WF.FlowDirection.TopDown
                : WF.FlowDirection.LeftToRight;
            _buttonPanel.WrapContents = compact;
            _buttonPanel.Visible = !compact;

            _runButton.Text = compact ? "Run" : "Run";
            _reloadButton.Text = compact ? "LISP" : "Reload LISP";
            _folderButton.Text = compact ? "Dir" : "LISP Folder";
            _addSourceButton.Text = compact ? "+Src" : "Add Source";
            _addManualButton.Text = compact ? "+Cmd" : "Add Manual";
            _removeSourceButton.Text = compact ? "-Src" : "Remove Source";
            _refreshButton.Text = compact ? "Ref" : "Refresh List";

            _commandGrid.Columns["Favorite"].Visible = true;
            _commandGrid.Columns["Description"].Visible = !compact;
            _commandGrid.Columns["Source"].Visible = !compact;
            _commandGrid.Columns["Command"].AutoSizeMode = compact
                ? WF.DataGridViewAutoSizeColumnMode.Fill
                : WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Command"].Width = compact ? 80 : 140;

            _statusLabel.Visible = !ultraCompact;

            _filterPanel.ResumeLayout();
            _buttonPanel.ResumeLayout();
        }

        private void BindGrid(string preferredCommandName = null)
        {
            preferredCommandName = preferredCommandName ?? GetSelectedCommandName();
            string search = (_searchBox.Text ?? string.Empty).Trim();
            string source = Convert.ToString(_sourceFilter.SelectedItem) ?? "All";
            string type = Convert.ToString(_typeFilter.SelectedItem) ?? "All";

            IEnumerable<PaletteCommandItem> filtered = _items;

            if (!string.Equals(source, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(
                    item => string.Equals(item.SourceLabel, source, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(type, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => MatchesTypeFilter(item.SourceKind, type));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(item =>
                    item.CommandName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.SourceLabel.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            filtered = ApplySortMode(filtered);

            _commandGrid.Rows.Clear();

            foreach (PaletteCommandItem item in filtered)
            {
                int rowIndex =
                    _commandGrid.Rows.Add(
                        item.IsFavorite ? "★" : "☆",
                        item.CommandName,
                        item.Description,
                        item.SourceLabel);
                _commandGrid.Rows[rowIndex].Tag = item;
            }

            if (_commandGrid.Rows.Count > 0)
            {
                _commandGrid.ClearSelection();
                WF.DataGridViewRow selectedRow =
                    _commandGrid.Rows
                        .Cast<WF.DataGridViewRow>()
                        .FirstOrDefault(row =>
                            string.Equals(
                                (row.Tag as PaletteCommandItem)?.CommandName,
                                preferredCommandName,
                                StringComparison.OrdinalIgnoreCase))
                    ?? _commandGrid.Rows[0];

                selectedRow.Selected = true;
                _commandGrid.CurrentCell = selectedRow.Cells["Command"];
            }
        }

        private static WF.DataGridView CreateGrid()
        {
            WF.DataGridView grid = new WF.DataGridView
            {
                Dock = WF.DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = WF.DataGridViewSelectionMode.FullRowSelect,
                EditMode = WF.DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = PanelColor,
                BorderStyle = WF.BorderStyle.FixedSingle,
                GridColor = BorderColor,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false
            };

            grid.ColumnHeadersBorderStyle = WF.DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ForegroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = ForegroundColor;

            grid.DefaultCellStyle.BackColor = PanelColor;
            grid.DefaultCellStyle.ForeColor = ForegroundColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.DefaultCellStyle.SelectionForeColor = ForegroundColor;
            grid.DefaultCellStyle.Padding = new WF.Padding(4, 2, 4, 2);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(42, 42, 44);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = ForegroundColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = ForegroundColor;

            WF.DataGridViewTextBoxColumn favoriteColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Favorite",
                HeaderText = "★",
                Width = 36,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            favoriteColumn.DefaultCellStyle.Alignment = WF.DataGridViewContentAlignment.MiddleCenter;

            WF.DataGridViewTextBoxColumn commandColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Command",
                HeaderText = "Command",
                Width = 140,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn descriptionColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "Description",
                AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.Fill,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn sourceColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Source",
                HeaderText = "Source",
                Width = 120,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };

            grid.Columns.AddRange(favoriteColumn, commandColumn, descriptionColumn, sourceColumn);
            return grid;
        }

        private static WF.Label CreateLabel(string text)
        {
            return new WF.Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = ForegroundColor,
                BackColor = BackgroundColor,
                Anchor = WF.AnchorStyles.Left
            };
        }

        private static WF.Button CreateButton(string text, EventHandler onClick)
        {
            WF.Button button = new WF.Button
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 8, 0),
                Padding = new WF.Padding(10, 4, 10, 4),
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseDownBackColor = SelectionColor;
            button.FlatAppearance.MouseOverBackColor = AccentColor;
            button.Click += onClick;
            return button;
        }

        private static WF.CheckBox CreateCheckBox(string text, EventHandler onCheckedChanged)
        {
            WF.CheckBox checkBox = new WF.CheckBox
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(4, 7, 0, 0),
                BackColor = BackgroundColor,
                ForeColor = ForegroundColor
            };
            checkBox.CheckedChanged += onCheckedChanged;
            return checkBox;
        }

        private void AutoShowCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = _autoShowCheckBox.Checked;
            DungXPaletteHost.SetAutoShowEnabled(enabled);
            SetStatus(enabled
                ? "Da bat tu dong mo DXPALETTE khi khoi dong AutoCAD."
                : "Da tat tu dong mo DXPALETTE khi khoi dong AutoCAD.");
        }

        private void SortModeFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            PaletteSortMode mode = GetCurrentSortMode();
            PaletteLayoutStore.SaveSortMode(mode);
            BindGrid();
            SetStatus(mode == PaletteSortMode.Custom
                ? "Dang sap xep theo yeu thich + thu tu tuy chinh."
                : "Dang sap xep theo ABC.");
        }

        private IEnumerable<PaletteCommandItem> ApplySortMode(IEnumerable<PaletteCommandItem> items)
        {
            switch (GetCurrentSortMode())
            {
                case PaletteSortMode.Alphabetical:
                    return items
                        .OrderByDescending(item => item.IsFavorite)
                        .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.SourceLabel, StringComparer.OrdinalIgnoreCase);
                default:
                    return items
                        .OrderByDescending(item => item.IsFavorite)
                        .ThenBy(item => item.ManualOrder)
                        .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase);
            }
        }

        private PaletteSortMode GetCurrentSortMode()
        {
            string selected = Convert.ToString(_sortModeFilter.SelectedItem) ?? "Custom";
            return string.Equals(selected, "A-Z", StringComparison.OrdinalIgnoreCase)
                ? PaletteSortMode.Alphabetical
                : PaletteSortMode.Custom;
        }

        private void SetSortMode(PaletteSortMode mode)
        {
            string label = mode == PaletteSortMode.Alphabetical ? "A-Z" : "Custom";
            int index = _sortModeFilter.FindStringExact(label);
            _sortModeFilter.SelectedIndex = index >= 0 ? index : 0;
        }

        private string GetSelectedCommandName()
        {
            return GetSelectedItem()?.CommandName;
        }

        private void CommandGrid_CellClick(object sender, WF.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (!string.Equals(
                _commandGrid.Columns[e.ColumnIndex].Name,
                "Favorite",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[e.RowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            item.IsFavorite = !item.IsFavorite;
            PaletteLayoutStore.SaveLayout(_items);
            BindGrid(item.CommandName);
            SetStatus(item.IsFavorite
                ? $"Da danh dau yeu thich: {item.CommandName}"
                : $"Da bo danh dau yeu thich: {item.CommandName}");
        }

        private void CommandGrid_CellDoubleClick(object sender, WF.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            if (!string.Equals(
                _commandGrid.Columns[e.ColumnIndex].Name,
                "Description",
                StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    _commandGrid.Columns[e.ColumnIndex].Name,
                    "Favorite",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunSelected();
            }
        }

        private void CommandGrid_KeyDown(object sender, WF.KeyEventArgs e)
        {
            if (e.KeyCode == WF.Keys.Enter && _commandGrid.CurrentCell != null)
            {
                if (string.Equals(
                        _commandGrid.Columns[_commandGrid.CurrentCell.ColumnIndex].Name,
                        "Description",
                        StringComparison.OrdinalIgnoreCase) &&
                    _commandGrid.IsCurrentCellInEditMode)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                RunSelected();
            }
        }

        private void CommandGrid_CellEndEdit(object sender, WF.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 ||
                !string.Equals(
                    _commandGrid.Columns[e.ColumnIndex].Name,
                    "Description",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WF.DataGridViewRow row = _commandGrid.Rows[e.RowIndex];
            PaletteCommandItem item = row.Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            string description = Convert.ToString(row.Cells["Description"].Value) ?? string.Empty;
            item.Description = description.Trim();
            PaletteDescriptionStore.SaveDescription(item.CommandName, item.Description);
            SetStatus($"Da luu mo ta cho {item.CommandName}");
        }

        private void CommandGrid_MouseDown(object sender, WF.MouseEventArgs e)
        {
            _dragStartPoint = e.Location;
            WF.DataGridView.HitTestInfo hit = _commandGrid.HitTest(e.X, e.Y);
            _dragRowIndex = hit.RowIndex;
        }

        private void CommandGrid_MouseMove(object sender, WF.MouseEventArgs e)
        {
            if (e.Button != WF.MouseButtons.Left)
            {
                return;
            }

            if (GetCurrentSortMode() != PaletteSortMode.Custom)
            {
                return;
            }

            if (_dragRowIndex < 0 || _dragRowIndex >= _commandGrid.Rows.Count)
            {
                return;
            }

            Size dragSize = WF.SystemInformation.DragSize;
            Rectangle dragRect = new Rectangle(
                _dragStartPoint.X - dragSize.Width / 2,
                _dragStartPoint.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);

            if (dragRect.Contains(e.Location))
            {
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[_dragRowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            _commandGrid.DoDragDrop(item, WF.DragDropEffects.Move);
        }

        private void CommandGrid_DragOver(object sender, WF.DragEventArgs e)
        {
            if (GetCurrentSortMode() != PaletteSortMode.Custom ||
                !e.Data.GetDataPresent(typeof(PaletteCommandItem)))
            {
                e.Effect = WF.DragDropEffects.None;
                return;
            }

            e.Effect = WF.DragDropEffects.Move;
        }

        private void CommandGrid_DragDrop(object sender, WF.DragEventArgs e)
        {
            if (GetCurrentSortMode() != PaletteSortMode.Custom ||
                !e.Data.GetDataPresent(typeof(PaletteCommandItem)))
            {
                return;
            }

            PaletteCommandItem draggedItem =
                e.Data.GetData(typeof(PaletteCommandItem)) as PaletteCommandItem;
            if (draggedItem == null)
            {
                return;
            }

            Point clientPoint = _commandGrid.PointToClient(new Point(e.X, e.Y));
            WF.DataGridView.HitTestInfo hit = _commandGrid.HitTest(clientPoint.X, clientPoint.Y);
            int targetIndex = hit.RowIndex;

            List<PaletteCommandItem> visibleItems = _commandGrid.Rows
                .Cast<WF.DataGridViewRow>()
                .Select(row => row.Tag as PaletteCommandItem)
                .Where(item => item != null)
                .ToList();

            int currentIndex = visibleItems.FindIndex(item =>
                string.Equals(item.CommandName, draggedItem.CommandName, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                return;
            }

            if (targetIndex < 0 || targetIndex >= visibleItems.Count)
            {
                targetIndex = visibleItems.Count - 1;
            }

            PaletteCommandItem movingItem = visibleItems[currentIndex];
            visibleItems.RemoveAt(currentIndex);
            if (targetIndex > currentIndex)
            {
                targetIndex--;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, visibleItems.Count));
            visibleItems.Insert(targetIndex, movingItem);

            HashSet<PaletteCommandItem> visibleSet = new HashSet<PaletteCommandItem>(visibleItems);
            List<PaletteCommandItem> fullOrder = _items
                .OrderBy(item => item.ManualOrder)
                .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int visibleIndex = 0;
            for (int i = 0; i < fullOrder.Count; i++)
            {
                if (!visibleSet.Contains(fullOrder[i]))
                {
                    continue;
                }

                fullOrder[i] = visibleItems[visibleIndex++];
            }

            for (int i = 0; i < fullOrder.Count; i++)
            {
                fullOrder[i].ManualOrder = i;
            }

            PaletteLayoutStore.SaveLayout(_items);
            BindGrid(draggedItem.CommandName);
            SetStatus($"Da cap nhat thu tu: {draggedItem.CommandName}");
        }

        private void RunSelected()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon command.");
                return;
            }

            DungXPaletteHost.RunCommand(item);
        }

        private void ReloadLisps()
        {
            bool ok = DungXLispResolver.TryEnsureAllLispFiles(showPrompt: true, out List<string> missing);
            if (ok)
            {
                SetStatus("Da kiem tra xong 2 file LISP, san sang chay.");
            }
            else
            {
                SetStatus("Thieu file LISP: " + string.Join(", ", missing.Select(Path.GetFileName)));
            }

            ReloadData(false);
        }

        private void PickFolder()
        {
            bool selected = DungXPaletteHost.ChooseLispFolder(false);
            SetStatus(selected
                ? "Da cap nhat thu muc LISP."
                : "Khong thay doi thu muc LISP.");
        }

        private void AddSource()
        {
            using (WF.OpenFileDialog dialog = new WF.OpenFileDialog())
            {
                dialog.Title = "Chon file .dll, .lsp hoac .vlx de them vao palette";
                dialog.Filter = "Supported files|*.dll;*.lsp;*.vlx|DLL|*.dll|LISP|*.lsp|VLX|*.vlx";
                dialog.Multiselect = true;

                if (dialog.ShowDialog() != WF.DialogResult.OK)
                {
                    return;
                }

                int added = PaletteSourceStore.AddSources(dialog.FileNames);
                ReloadData(false);
                SetStatus($"Da them {added} source moi.");
            }
        }

        private void AddManualAlias()
        {
            string commandName = PaletteUiHelpers.ShowTextPrompt(
                "Them manual alias",
                "Nhap ten lenh / alias:");
            if (string.IsNullOrWhiteSpace(commandName))
            {
                SetStatus("Khong them manual alias.");
                return;
            }

            PaletteManualCommandStore.Save(commandName.Trim(), string.Empty);
            ReloadData(false);
            SetStatus($"Da them manual alias: {commandName.Trim()}");
        }

        private void RemoveSelectedSource()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon dong nao de xoa source.");
                return;
            }

            if (item.SourceKind == PaletteSourceKind.ManualAlias)
            {
                PaletteManualCommandStore.Remove(item.CommandName);
                ReloadData(false);
                SetStatus("Da xoa manual alias.");
                return;
            }

            if (string.IsNullOrWhiteSpace(item.SourcePath) ||
                !PaletteSourceStore.Contains(item.SourcePath))
            {
                SetStatus("Dong dang chon khong phai source ngoai de xoa.");
                return;
            }

            PaletteSourceStore.RemoveSource(item.SourcePath);
            ReloadData(false);
            SetStatus("Da xoa source khoi palette.");
        }

        private void RefreshSourceFilter(string preferredSelection)
        {
            List<string> sources = _items
                .Select(item => item.SourceLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _sourceFilter.Items.Clear();
            _sourceFilter.Items.Add("All");
            foreach (string source in sources)
            {
                _sourceFilter.Items.Add(source);
            }

            int selectedIndex = 0;
            for (int i = 0; i < _sourceFilter.Items.Count; i++)
            {
                if (string.Equals(
                    Convert.ToString(_sourceFilter.Items[i]),
                    preferredSelection,
                    StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            _sourceFilter.SelectedIndex = selectedIndex;
        }

        private static bool MatchesTypeFilter(PaletteSourceKind sourceKind, string selectedType)
        {
            switch (selectedType)
            {
                case "LISP":
                    return sourceKind == PaletteSourceKind.Lisp;
                case "DLL":
                    return sourceKind == PaletteSourceKind.ManagedDll ||
                           sourceKind == PaletteSourceKind.BuiltInDll;
                case "VLX":
                    return sourceKind == PaletteSourceKind.Vlx;
                case "Action":
                    return sourceKind == PaletteSourceKind.ActionMacro;
                case "Manual":
                    return sourceKind == PaletteSourceKind.ManualAlias;
                default:
                    return true;
            }
        }

        private PaletteCommandItem GetSelectedItem()
        {
            if (_commandGrid.SelectedRows.Count == 0)
            {
                return null;
            }

            return _commandGrid.SelectedRows[0].Tag as PaletteCommandItem;
        }
    }

    public class SmartStretchCommands
    {
        private const double ComparisonTolerance = 1e-6;

        [CommandMethod("SS")]
        public void SmartStretch()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            double savedLength = SmartStretchSettingsStore.LoadLength();
            PromptDoubleOptions lengthOptions =
                new PromptDoubleOptions(
                    $"\nNhập L cho smart stretch <{savedLength.ToString("0.###", CultureInfo.InvariantCulture)}>:");
            lengthOptions.AllowNegative = false;
            lengthOptions.AllowZero = false;
            lengthOptions.AllowNone = true;
            lengthOptions.DefaultValue = savedLength;
            lengthOptions.UseDefaultValue = true;

            PromptDoubleResult lengthResult = ed.GetDouble(lengthOptions);
            if (lengthResult.Status == PromptStatus.Cancel)
            {
                return;
            }

            double length = lengthResult.Status == PromptStatus.None
                ? savedLength
                : lengthResult.Value;

            if (length <= ComparisonTolerance)
            {
                ed.WriteMessage("\nGiá trị L phải lớn hơn 0.");
                return;
            }

            SmartStretchSettingsStore.SaveLength(length);

            SmartStretchSelectionInput selectionInput = GetSmartStretchSelectionInput(ed);
            if (selectionInput == null)
            {
                return;
            }

            ShowSmartStretchSelection(ed, selectionInput.SelectedObjectIds);

            PromptPointResult startResult = ed.GetPoint("\nChọn điểm đầu: ");
            if (startResult.Status != PromptStatus.OK)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return;
            }

            PromptResult directionResult = GetDirectionWithPreview(
                ed,
                selectionInput,
                startResult.Value,
                length,
                out SmartStretchDirection direction,
                out Point3d secondPoint);
            if (directionResult.Status != PromptStatus.OK)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return;
            }
            if (direction == SmartStretchDirection.None)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                ed.WriteMessage("\nKhông xác định được hướng stretch.");
                return;
            }

            ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
            ExecuteNativeStretch(ed, selectionInput, startResult.Value, secondPoint);

            ed.WriteMessage(
                $"\nSS: đã gọi STRETCH gốc theo {GetDirectionLabel(direction)} với L = {length.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }

        private static SmartStretchSelectionInput GetSmartStretchSelectionInput(Editor ed)
        {
            ed.WriteMessage(
                "\nWindow: quet nhieu vung neu can, nhan Space/Enter o buoc chon goc dau de ket thuc chon.");

            List<SmartStretchWindowSelection> windows = new List<SmartStretchWindowSelection>();
            HashSet<ObjectId> selectedIds = new HashSet<ObjectId>();
            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                while (true)
                {
                    PromptPointOptions firstCornerOptions =
                        new PromptPointOptions(
                            windows.Count == 0
                                ? "\nChọn góc đầu crossing window: "
                                : "\nChọn góc đầu crossing window tiếp theo hoặc Space để xong: ");
                    firstCornerOptions.AllowNone = windows.Count > 0;

                    PromptPointResult firstCornerResult = ed.GetPoint(firstCornerOptions);
                    if (firstCornerResult.Status == PromptStatus.None)
                    {
                        break;
                    }

                    if (firstCornerResult.Status != PromptStatus.OK)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        return null;
                    }

                    PromptCornerOptions secondCornerOptions =
                        new PromptCornerOptions(
                            "\nChọn góc đối diện crossing window: ",
                            firstCornerResult.Value);
                    PromptPointResult secondCornerResult = ed.GetCorner(secondCornerOptions);
                    if (secondCornerResult.Status != PromptStatus.OK)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        return null;
                    }

                    PromptSelectionResult crossingResult = ed.SelectCrossingWindow(
                        firstCornerResult.Value,
                        secondCornerResult.Value);
                    if (crossingResult.Status != PromptStatus.OK || crossingResult.Value == null)
                    {
                        ed.WriteMessage("\nWindow này chưa bắt được đối tượng nào.");
                        continue;
                    }

                    windows.Add(
                        new SmartStretchWindowSelection(
                            firstCornerResult.Value,
                            secondCornerResult.Value));

                    foreach (ObjectId objectId in crossingResult.Value.GetObjectIds())
                    {
                        selectedIds.Add(objectId);
                    }

                    ShowSmartStretchSelection(ed, selectedIds.ToArray());
                    ed.WriteMessage($"\nĐã gom {selectedIds.Count} đối tượng. Có thể quét thêm hoặc nhấn Space/Enter để tiếp tục.");
                }
            }
            finally
            {
                if (previousSelectionOffscreen != null)
                {
                    Application.SetSystemVariable("SELECTIONOFFSCREEN", previousSelectionOffscreen);
                }
            }

            if (windows.Count == 0 || selectedIds.Count == 0)
            {
                ed.WriteMessage("\nChưa có đối tượng nào được chọn.");
                return null;
            }

            return SmartStretchSelectionInput.CreateSelection(
                windows,
                selectedIds);
        }

        private static void ExecuteNativeStretch(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d basePoint,
            Point3d secondPoint)
        {
            ViewTableRecord originalView = null;

            try
            {
                originalView = ed.GetCurrentView();
                Extents3d stretchBounds =
                    GetStretchOperationBounds(selectionInput, basePoint, secondPoint);
                ZoomToStretchBounds(ed, stretchBounds);

                List<object> args = new List<object> { "_.STRETCH" };

                foreach (SmartStretchWindowSelection window in selectionInput.Windows)
                {
                    args.Add("_C");
                    args.Add(window.FirstPoint);
                    args.Add(window.SecondPoint);
                }

                args.Add(string.Empty);
                args.Add(basePoint);
                args.Add(secondPoint);

                ed.Command(args.ToArray());
            }
            finally
            {
                if (originalView != null)
                {
                    ed.SetCurrentView(originalView);
                    originalView.Dispose();
                }
            }
        }

        private static Extents3d GetStretchOperationBounds(
            SmartStretchSelectionInput selectionInput,
            Point3d basePoint,
            Point3d secondPoint)
        {
            List<Point3d> points = new List<Point3d> { basePoint, secondPoint };

            if (selectionInput?.Windows != null)
            {
                foreach (SmartStretchWindowSelection window in selectionInput.Windows)
                {
                    points.Add(window.FirstPoint);
                    points.Add(window.SecondPoint);
                }
            }

            double minX = points.Min(point => point.X);
            double minY = points.Min(point => point.Y);
            double minZ = points.Min(point => point.Z);
            double maxX = points.Max(point => point.X);
            double maxY = points.Max(point => point.Y);
            double maxZ = points.Max(point => point.Z);

            double width = Math.Max(maxX - minX, 1.0);
            double height = Math.Max(maxY - minY, 1.0);
            double paddingX = Math.Max(width * 0.15, 10.0);
            double paddingY = Math.Max(height * 0.15, 10.0);

            return new Extents3d(
                new Point3d(minX - paddingX, minY - paddingY, minZ),
                new Point3d(maxX + paddingX, maxY + paddingY, maxZ));
        }

        private static void ZoomToStretchBounds(Editor ed, Extents3d bounds)
        {
            Point3d minPoint = bounds.MinPoint;
            Point3d maxPoint = bounds.MaxPoint;
            ed.Command("_.ZOOM", "_W", minPoint, maxPoint);
        }

        private static PromptResult GetDirectionWithPreview(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d startPoint,
            double length,
            out SmartStretchDirection direction,
            out Point3d secondPoint)
        {
            using (SmartStretchPreviewJig jig =
                new SmartStretchPreviewJig(ed, selectionInput, startPoint, length))
            {
                PromptResult result = ed.Drag(jig);
                direction = jig.Direction;
                secondPoint = jig.SecondPoint;
                return result;
            }
        }

        private static void ShowSmartStretchSelection(Editor ed, ObjectId[] objectIds)
        {
            if (ed == null || objectIds == null || objectIds.Length == 0)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc?.Database;
            if (db == null)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objectId in objectIds)
                {
                    Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                    entity?.Highlight();
                }

                tr.Commit();
            }

            Application.UpdateScreen();
        }

        private static List<int> FindStretchIndicesInsideWindow(
            Entity entity,
            SmartStretchSelectionInput selectionInput,
            Matrix3d ucsInverse)
        {
            List<Point3d> stretchPoints = GetStretchPoints(entity);
            if (stretchPoints.Count == 0)
            {
                return new List<int>();
            }

            return stretchPoints
                .Select((point, index) => new
                {
                    Index = index,
                    Point = point.TransformBy(ucsInverse)
                })
                .Where(item => selectionInput.Windows.Any(window =>
                {
                    Point3d firstCornerUcs = window.FirstPoint.TransformBy(ucsInverse);
                    Point3d secondCornerUcs = window.SecondPoint.TransformBy(ucsInverse);

                    double minX = Math.Min(firstCornerUcs.X, secondCornerUcs.X) - ComparisonTolerance;
                    double maxX = Math.Max(firstCornerUcs.X, secondCornerUcs.X) + ComparisonTolerance;
                    double minY = Math.Min(firstCornerUcs.Y, secondCornerUcs.Y) - ComparisonTolerance;
                    double maxY = Math.Max(firstCornerUcs.Y, secondCornerUcs.Y) + ComparisonTolerance;

                    return item.Point.X >= minX &&
                           item.Point.X <= maxX &&
                           item.Point.Y >= minY &&
                           item.Point.Y <= maxY;
                }))
                .Select(item => item.Index)
                .ToList();
        }

        private static void ClearSmartStretchSelection(ObjectId[] objectIds)
        {
            if (objectIds == null || objectIds.Length == 0)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc?.Database;
            if (db == null)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objectId in objectIds)
                {
                    Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                    entity?.Unhighlight();
                }

                tr.Commit();
            }

            Application.UpdateScreen();
        }

        private static SmartStretchDirection ResolveDirection(Point3d startUcs, Point3d nextUcs)
        {
            double dx = nextUcs.X - startUcs.X;
            double dy = nextUcs.Y - startUcs.Y;

            if (Math.Abs(dx) < ComparisonTolerance && Math.Abs(dy) < ComparisonTolerance)
            {
                return SmartStretchDirection.None;
            }

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                return dx >= 0.0
                    ? SmartStretchDirection.PositiveX
                    : SmartStretchDirection.NegativeX;
            }

            return dy >= 0.0
                ? SmartStretchDirection.PositiveY
                : SmartStretchDirection.NegativeY;
        }

        private static Vector3d GetDisplacementVector(
            SmartStretchDirection direction,
            double length,
            Matrix3d ucs)
        {
            Vector3d ucsVector;

            switch (direction)
            {
                case SmartStretchDirection.PositiveX:
                    ucsVector = new Vector3d(length, 0.0, 0.0);
                    break;
                case SmartStretchDirection.NegativeX:
                    ucsVector = new Vector3d(-length, 0.0, 0.0);
                    break;
                case SmartStretchDirection.PositiveY:
                    ucsVector = new Vector3d(0.0, length, 0.0);
                    break;
                case SmartStretchDirection.NegativeY:
                    ucsVector = new Vector3d(0.0, -length, 0.0);
                    break;
                default:
                    return new Vector3d(0.0, 0.0, 0.0);
            }

            return ucsVector.TransformBy(ucs);
        }

        private static List<SmartStretchEntityInfo> CollectStretchInfos(
            SelectionSet selectionSet,
            Transaction tr,
            Matrix3d ucs)
        {
            List<SmartStretchEntityInfo> infos = new List<SmartStretchEntityInfo>();

            foreach (SelectedObject selectedObject in selectionSet)
            {
                Entity entity = tr.GetObject(selectedObject.ObjectId, OpenMode.ForWrite) as Entity;
                if (entity == null || entity.IsErased)
                {
                    continue;
                }

                List<Point3d> stretchPoints = GetStretchPoints(entity);
                List<Point3d> stretchPointsUcs = stretchPoints
                    .Select(point => point.TransformBy(ucs.Inverse()))
                    .ToList();

                if (stretchPointsUcs.Count == 0)
                {
                    continue;
                }

                infos.Add(new SmartStretchEntityInfo(entity, stretchPointsUcs));
            }

            return infos;
        }

        private static List<Point3d> GetStretchPoints(Entity entity)
        {
            try
            {
                Point3dCollection points = new Point3dCollection();
                entity.GetStretchPoints(points);
                return points.Cast<Point3d>().ToList();
            }
            catch
            {
                return new List<Point3d>();
            }
        }

        private static Dictionary<ObjectId, List<int>> FindNearestStretchIndices(
            IEnumerable<SmartStretchEntityInfo> infos,
            Point3d startUcs,
            SmartStretchDirection direction)
        {
            Dictionary<ObjectId, List<int>> result =
                new Dictionary<ObjectId, List<int>>();

            foreach (SmartStretchEntityInfo info in infos)
            {
                if (info.StretchPointsUcs.Count == 0)
                {
                    continue;
                }

                List<double> distances = info.StretchPointsUcs
                    .Select(point => point.DistanceTo(startUcs))
                    .ToList();

                double minDistance = distances.Min();
                double tolerance = Math.Max(ComparisonTolerance, minDistance * 0.1);

                List<int> indices = distances
                    .Select((distance, index) => new { distance, index })
                    .Where(item => item.distance <= minDistance + tolerance)
                    .Select(item => item.index)
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList();

                if (indices.Count > 0)
                {
                    result[info.Entity.ObjectId] = indices;
                }
            }

            return result;
        }

        private static bool TryStretchEntity(
            Entity entity,
            IEnumerable<int> pointIndices,
            Vector3d displacement)
        {
            try
            {
                IntegerCollection indices = new IntegerCollection();
                foreach (int index in pointIndices.Distinct().OrderBy(value => value))
                {
                    indices.Add(index);
                }

                if (indices.Count == 0)
                {
                    return false;
                }

                entity.MoveStretchPointsAt(indices, displacement);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class SmartStretchPreviewJig : DrawJig, IDisposable
        {
            private readonly Editor _editor;
            private readonly SmartStretchSelectionInput _selectionInput;
            private readonly Point3d _startPoint;
            private readonly double _length;
            private readonly Matrix3d _ucs;
            private readonly Matrix3d _ucsInverse;
            private readonly List<Entity> _previewEntities = new List<Entity>();
            private Point3d _cursorPoint;
            private SmartStretchDirection _direction;
            private Point3d _secondPoint;

            public SmartStretchPreviewJig(
                Editor editor,
                SmartStretchSelectionInput selectionInput,
                Point3d startPoint,
                double length)
            {
                _editor = editor;
                _selectionInput = selectionInput;
                _startPoint = startPoint;
                _length = length;
                _ucs = editor.CurrentUserCoordinateSystem;
                _ucsInverse = _ucs.Inverse();
                _cursorPoint = startPoint;
                _direction = SmartStretchDirection.None;
                _secondPoint = startPoint;
            }

            public SmartStretchDirection Direction => _direction;

            public Point3d SecondPoint => _secondPoint;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions pointOptions =
                    new JigPromptPointOptions("\nChọn điểm sau để xem trước hướng stretch: ");
                pointOptions.BasePoint = _startPoint;
                pointOptions.UseBasePoint = true;

                PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
                if (pointResult.Status == PromptStatus.Cancel)
                {
                    return SamplerStatus.Cancel;
                }

                if (pointResult.Status != PromptStatus.OK)
                {
                    return SamplerStatus.NoChange;
                }

                if (_cursorPoint.DistanceTo(pointResult.Value) <= ComparisonTolerance)
                {
                    return SamplerStatus.NoChange;
                }

                _cursorPoint = pointResult.Value;

                Point3d startUcs = _startPoint.TransformBy(_ucsInverse);
                Point3d nextUcs = _cursorPoint.TransformBy(_ucsInverse);
                SmartStretchDirection newDirection = ResolveDirection(startUcs, nextUcs);

                if (newDirection != _direction)
                {
                    _direction = newDirection;
                    RebuildPreviewEntities();
                }

                if (_direction != SmartStretchDirection.None)
                {
                    Vector3d displacement = GetDisplacementVector(_direction, _length, _ucs);
                    _secondPoint = _startPoint + displacement;
                }
                else
                {
                    _secondPoint = _startPoint;
                }

                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                foreach (Entity previewEntity in _previewEntities)
                {
                    previewEntity.WorldDraw(draw);
                }

                if (_direction != SmartStretchDirection.None)
                {
                    draw.Geometry.WorldLine(_startPoint, _secondPoint);
                }

                return true;
            }

            private void RebuildPreviewEntities()
            {
                DisposePreviewEntities();

                if (_direction == SmartStretchDirection.None)
                {
                    return;
                }

                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc?.Database;
                if (db == null)
                {
                    return;
                }

                Vector3d displacement = GetDisplacementVector(_direction, _length, _ucs);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId objectId in _selectionInput.SelectedObjectIds)
                    {
                        Entity sourceEntity =
                            tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                        if (sourceEntity == null || sourceEntity.IsErased)
                        {
                            continue;
                        }

                        Entity previewEntity = sourceEntity.Clone() as Entity;
                        if (previewEntity == null)
                        {
                            continue;
                        }

                        List<int> indices = FindStretchIndicesInsideWindow(
                            previewEntity,
                            _selectionInput,
                            _ucsInverse);
                        if (indices.Count == 0 ||
                            !TryStretchEntity(previewEntity, indices, displacement))
                        {
                            previewEntity.Dispose();
                            continue;
                        }

                        _previewEntities.Add(previewEntity);
                    }

                    tr.Commit();
                }
            }

            private void DisposePreviewEntities()
            {
                foreach (Entity previewEntity in _previewEntities)
                {
                    previewEntity.Dispose();
                }

                _previewEntities.Clear();
            }

            public void Dispose()
            {
                DisposePreviewEntities();
            }
        }

        private static string GetDirectionLabel(SmartStretchDirection direction)
        {
            switch (direction)
            {
                case SmartStretchDirection.PositiveX:
                    return "SX+";
                case SmartStretchDirection.NegativeX:
                    return "SX-";
                case SmartStretchDirection.PositiveY:
                    return "SY+";
                case SmartStretchDirection.NegativeY:
                    return "SY-";
                default:
                    return "?";
            }
        }
    }

    internal enum SmartStretchDirection
    {
        None,
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY
    }

    internal sealed class SmartStretchSelectionInput
    {
        private SmartStretchSelectionInput()
        {
        }

        public List<SmartStretchWindowSelection> Windows { get; private set; }

        public ObjectId[] SelectedObjectIds { get; private set; }

        public static SmartStretchSelectionInput CreateSelection(
            IEnumerable<SmartStretchWindowSelection> windows,
            IEnumerable<ObjectId> selectedObjectIds)
        {
            return new SmartStretchSelectionInput
            {
                Windows = windows?.ToList() ?? new List<SmartStretchWindowSelection>(),
                SelectedObjectIds = selectedObjectIds?.ToArray() ?? new ObjectId[0]
            };
        }
    }

    internal sealed class SmartStretchWindowSelection
    {
        public SmartStretchWindowSelection(Point3d firstPoint, Point3d secondPoint)
        {
            FirstPoint = firstPoint;
            SecondPoint = secondPoint;
        }

        public Point3d FirstPoint { get; }

        public Point3d SecondPoint { get; }
    }

    internal sealed class SmartStretchEntityInfo
    {
        public SmartStretchEntityInfo(
            Entity entity,
            List<Point3d> stretchPointsUcs)
        {
            Entity = entity;
            StretchPointsUcs = stretchPointsUcs ?? new List<Point3d>();
        }

        public Entity Entity { get; }

        public List<Point3d> StretchPointsUcs { get; }
    }

    internal static class PaletteUiHelpers
    {
        public static string ShowTextPrompt(string title, string label)
        {
            Color backgroundColor = Color.FromArgb(45, 45, 48);
            Color panelColor = Color.FromArgb(37, 37, 38);
            Color foregroundColor = Color.FromArgb(241, 241, 241);

            using (WF.Form form = new WF.Form())
            using (WF.TextBox textBox = new WF.TextBox())
            using (WF.Label textLabel = new WF.Label())
            using (WF.Button okButton = new WF.Button())
            using (WF.Button cancelButton = new WF.Button())
            {
                form.Text = title;
                form.StartPosition = WF.FormStartPosition.CenterParent;
                form.FormBorderStyle = WF.FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 120);
                form.BackColor = backgroundColor;
                form.ForeColor = foregroundColor;

                textLabel.Text = label;
                textLabel.Left = 16;
                textLabel.Top = 16;
                textLabel.Width = 380;
                textLabel.ForeColor = foregroundColor;

                textBox.Left = 16;
                textBox.Top = 42;
                textBox.Width = 380;
                textBox.BackColor = panelColor;
                textBox.ForeColor = foregroundColor;

                okButton.Text = "OK";
                okButton.DialogResult = WF.DialogResult.OK;
                okButton.Left = 240;
                okButton.Top = 80;

                cancelButton.Text = "Cancel";
                cancelButton.DialogResult = WF.DialogResult.Cancel;
                cancelButton.Left = 322;
                cancelButton.Top = 80;

                form.Controls.Add(textLabel);
                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog() == WF.DialogResult.OK
                    ? textBox.Text
                    : string.Empty;
            }
        }
    }

    internal sealed class PaletteCommandItem
    {
        public PaletteCommandItem(
            string commandName,
            string description,
            string sourceLabel,
            PaletteSourceKind sourceKind,
            string sourcePath)
        {
            CommandName = commandName;
            Description = description ?? string.Empty;
            SourceLabel = sourceLabel;
            SourceKind = sourceKind;
            SourcePath = sourcePath ?? string.Empty;
        }

        public string CommandName { get; }

        public string Description { get; set; }

        public string SourceLabel { get; }

        public PaletteSourceKind SourceKind { get; }

        public string SourcePath { get; }

        public bool IsFavorite { get; set; }

        public int ManualOrder { get; set; }
    }

    internal enum PaletteSortMode
    {
        Custom,
        Alphabetical
    }

    internal enum PaletteSourceKind
    {
        BuiltInDll,
        Lisp,
        ManagedDll,
        Vlx,
        ActionMacro,
        ManualAlias
    }

    internal sealed class PaletteSourceFile
    {
        public PaletteSourceFile(string filePath, string displayName, PaletteSourceKind sourceKind)
        {
            FilePath = filePath;
            DisplayName = displayName;
            SourceKind = sourceKind;
        }

        public string FilePath { get; }

        public string DisplayName { get; }

        public PaletteSourceKind SourceKind { get; }
    }

    internal static class DungXLispResolver
    {
        private static readonly string[] RequiredLispFiles =
        {
            "DUNGX Custom Command.LSP",
            "DUNGX 2.LSP"
        };

        private static readonly string ConfigFilePath =
            Path.Combine(GetAssemblyDirectory(), "dungx_lisp_root.txt");

        public static string GetDisplayRoot()
        {
            return GetCurrentRoot() ?? "<chua set>";
        }

        public static bool TryEnsureAllLispFiles(bool showPrompt, out List<string> missing)
        {
            if (TryResolveAllLispFiles(out _, out missing))
            {
                return true;
            }

            if (!showPrompt)
            {
                return false;
            }

            bool selected = PickLispRoot(true);
            if (!selected)
            {
                return false;
            }

            return TryResolveAllLispFiles(out _, out missing);
        }

        public static bool TryResolveAllLispFiles(out List<string> paths, out List<string> missing)
        {
            paths = new List<string>();
            missing = new List<string>();

            string root = GetCurrentRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                missing.AddRange(RequiredLispFiles);
                return false;
            }

            foreach (string fileName in RequiredLispFiles)
            {
                string fullPath = Path.Combine(root, fileName);
                if (File.Exists(fullPath))
                {
                    paths.Add(fullPath);
                }
                else
                {
                    missing.Add(fullPath);
                }
            }

            return missing.Count == 0;
        }

        public static IReadOnlyList<string> GetResolvedLispFiles()
        {
            if (TryResolveAllLispFiles(out List<string> paths, out _))
            {
                return paths;
            }

            return new List<string>();
        }

        public static bool PickLispRoot(bool showMessage)
        {
            using (WF.FolderBrowserDialog dialog = new WF.FolderBrowserDialog())
            {
                dialog.Description = "Chon thu muc chua DUNGX Custom Command.LSP va DUNGX 2.LSP";
                dialog.SelectedPath = GetCurrentRoot() ?? GetAssemblyDirectory();

                if (dialog.ShowDialog() != WF.DialogResult.OK)
                {
                    return false;
                }

                File.WriteAllText(ConfigFilePath, dialog.SelectedPath);

                if (showMessage)
                {
                    WF.MessageBox.Show(
                        "Da luu thu muc LISP:\n" + dialog.SelectedPath,
                        "DungX Palette",
                        WF.MessageBoxButtons.OK,
                        WF.MessageBoxIcon.Information);
                }

                return true;
            }
        }

        private static string GetCurrentRoot()
        {
            string[] candidates =
            {
                TryReadConfigFile(),
                GetAssemblyDirectory(),
                Path.Combine(GetAssemblyDirectory(), "LISP"),
                Path.GetDirectoryName(GetAssemblyDirectory())
            };

            foreach (string candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                bool hasAllFiles = RequiredLispFiles.All(file => File.Exists(Path.Combine(candidate, file)));
                if (hasAllFiles)
                {
                    return candidate;
                }
            }

            return TryReadConfigFile() ?? GetAssemblyDirectory();
        }

        private static string TryReadConfigFile()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return null;
            }

            string path = File.ReadAllText(ConfigFilePath).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        private static string GetAssemblyDirectory()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(assemblyPath) ?? string.Empty;
        }
    }

    internal static class PaletteDescriptionStore
    {
        private static readonly string DescriptionFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_descriptions.tsv");

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(DescriptionFilePath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(DescriptionFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length == 0)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                string description = parts.Length > 1 ? parts[1] : string.Empty;
                if (!string.IsNullOrWhiteSpace(commandName))
                {
                    map[commandName] = description;
                }
            }

            return map;
        }

        public static void SaveDescription(string commandName, string description)
        {
            Dictionary<string, string> map = Load();
            map[commandName] = description ?? string.Empty;

            List<string> lines = map
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + (kvp.Value ?? string.Empty))
                .ToList();

            File.WriteAllLines(DescriptionFilePath, lines, Encoding.UTF8);
        }
    }

    internal static class SmartStretchSettingsStore
    {
        private static readonly string LengthFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_smart_stretch_length.txt");

        public static double LoadLength()
        {
            try
            {
                if (!File.Exists(LengthFilePath))
                {
                    return 100.0;
                }

                string text = (File.ReadAllText(LengthFilePath, Encoding.UTF8) ?? string.Empty).Trim();
                if (double.TryParse(
                    text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out double value) &&
                    value > 0.0)
                {
                    return value;
                }
            }
            catch
            {
            }

            return 100.0;
        }

        public static void SaveLength(double value)
        {
            if (value <= 0.0)
            {
                return;
            }

            File.WriteAllText(
                LengthFilePath,
                value.ToString("0.###", CultureInfo.InvariantCulture),
                Encoding.UTF8);
        }
    }

    internal static class PaletteStartupStore
    {
        private static readonly string AutoShowFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_autoshow.txt");

        public static bool LoadAutoShow()
        {
            try
            {
                if (!File.Exists(AutoShowFilePath))
                {
                    return false;
                }

                string text = (File.ReadAllText(AutoShowFilePath, Encoding.UTF8) ?? string.Empty).Trim();
                return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void SaveAutoShow(bool enabled)
        {
            File.WriteAllText(AutoShowFilePath, enabled ? "1" : "0", Encoding.UTF8);
        }
    }

    internal static class PaletteSourceStore
    {
        private static readonly string SourceFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_sources.tsv");

        public static List<PaletteSourceFile> LoadSources()
        {
            List<PaletteSourceFile> result = new List<PaletteSourceFile>();
            if (!File.Exists(SourceFilePath))
            {
                return result;
            }

            foreach (string rawLine in File.ReadAllLines(SourceFilePath, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string filePath = line.Split('\t')[0].Trim();
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                if (!TryCreateSource(filePath, out PaletteSourceFile source))
                {
                    continue;
                }

                result.Add(source);
            }

            return result
                .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public static int AddSources(IEnumerable<string> filePaths)
        {
            List<string> existing = LoadRawPaths();
            int added = 0;

            foreach (string filePath in filePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string normalized = Path.GetFullPath(filePath);
                if (existing.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!TryCreateSource(normalized, out _))
                {
                    continue;
                }

                existing.Add(normalized);
                added++;
            }

            SaveRawPaths(existing);
            return added;
        }

        public static void RemoveSource(string filePath)
        {
            List<string> existing = LoadRawPaths()
                .Where(path => !string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveRawPaths(existing);
        }

        public static bool Contains(string filePath)
        {
            return LoadRawPaths().Any(
                path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryCreateSource(string filePath, out PaletteSourceFile source)
        {
            source = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            string displayName = Path.GetFileName(filePath);

            switch (extension)
            {
                case ".lsp":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.Lisp);
                    return true;
                case ".vlx":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.Vlx);
                    return true;
                case ".dll":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.ManagedDll);
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> LoadRawPaths()
        {
            if (!File.Exists(SourceFilePath))
            {
                return new List<string>();
            }

            return File.ReadAllLines(SourceFilePath, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void SaveRawPaths(IEnumerable<string> filePaths)
        {
            List<string> lines = filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            File.WriteAllLines(SourceFilePath, lines, Encoding.UTF8);
        }
    }

    internal static class PaletteManualCommandStore
    {
        private static readonly string ManualFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_manual.tsv");

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(ManualFilePath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(ManualFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                string commandName = parts[0].Trim();
                string description = parts.Length > 1 ? parts[1] : string.Empty;

                if (!string.IsNullOrWhiteSpace(commandName))
                {
                    map[commandName] = description;
                }
            }

            return map;
        }

        public static void Save(string commandName, string description)
        {
            Dictionary<string, string> map = Load();
            map[commandName] = description ?? string.Empty;
            SaveAll(map);
        }

        public static void Remove(string commandName)
        {
            Dictionary<string, string> map = Load();
            map.Remove(commandName);
            SaveAll(map);
        }

        private static void SaveAll(Dictionary<string, string> map)
        {
            List<string> lines = map
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + (kvp.Value ?? string.Empty))
                .ToList();

            File.WriteAllLines(ManualFilePath, lines, Encoding.UTF8);
        }
    }

    internal static class PaletteLayoutStore
    {
        private static readonly string LayoutFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_layout.tsv");

        private static readonly string SortModeFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_sort.txt");

        public static void ApplyLayout(List<PaletteCommandItem> items)
        {
            Dictionary<string, Tuple<bool, int>> saved = LoadLayout();
            int nextOrder = saved.Count == 0 ? 0 : saved.Max(kvp => kvp.Value.Item2) + 1;

            foreach (PaletteCommandItem item in items.OrderBy(
                current => current.CommandName,
                StringComparer.OrdinalIgnoreCase))
            {
                if (saved.TryGetValue(item.CommandName, out Tuple<bool, int> state))
                {
                    item.IsFavorite = state.Item1;
                    item.ManualOrder = state.Item2;
                }
                else
                {
                    item.IsFavorite = false;
                    item.ManualOrder = nextOrder++;
                }
            }

            NormalizeManualOrder(items);
        }

        public static void SaveLayout(IEnumerable<PaletteCommandItem> items)
        {
            List<PaletteCommandItem> ordered = items
                .OrderBy(item => item.ManualOrder)
                .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            NormalizeManualOrder(ordered);

            List<string> lines = ordered
                .Select(item =>
                    item.CommandName + "\t" +
                    (item.IsFavorite ? "1" : "0") + "\t" +
                    item.ManualOrder.ToString())
                .ToList();

            File.WriteAllLines(LayoutFilePath, lines, Encoding.UTF8);
        }

        public static PaletteSortMode LoadSortMode()
        {
            if (!File.Exists(SortModeFilePath))
            {
                return PaletteSortMode.Custom;
            }

            string mode = (File.ReadAllText(SortModeFilePath, Encoding.UTF8) ?? string.Empty).Trim();
            return string.Equals(mode, "A-Z", StringComparison.OrdinalIgnoreCase)
                ? PaletteSortMode.Alphabetical
                : PaletteSortMode.Custom;
        }

        public static void SaveSortMode(PaletteSortMode mode)
        {
            string value = mode == PaletteSortMode.Alphabetical ? "A-Z" : "Custom";
            File.WriteAllText(SortModeFilePath, value, Encoding.UTF8);
        }

        private static Dictionary<string, Tuple<bool, int>> LoadLayout()
        {
            Dictionary<string, Tuple<bool, int>> map =
                new Dictionary<string, Tuple<bool, int>>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(LayoutFilePath))
            {
                return map;
            }

            foreach (string rawLine in File.ReadAllLines(LayoutFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string[] parts = rawLine.Split('\t');
                if (parts.Length < 3)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                bool isFavorite = string.Equals(parts[1].Trim(), "1", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(commandName) ||
                    !int.TryParse(parts[2].Trim(), out int manualOrder))
                {
                    continue;
                }

                map[commandName] = Tuple.Create(isFavorite, manualOrder);
            }

            return map;
        }

        private static void NormalizeManualOrder(IEnumerable<PaletteCommandItem> items)
        {
            int index = 0;
            foreach (PaletteCommandItem item in items
                .OrderBy(current => current.ManualOrder)
                .ThenBy(current => current.CommandName, StringComparer.OrdinalIgnoreCase))
            {
                item.ManualOrder = index++;
            }
        }
    }

    internal static class ActionMacroCatalog
    {
        public static IEnumerable<PaletteCommandItem> BuildItems(
            Dictionary<string, string> savedDescriptions)
        {
            string actionFolder = GetDefaultActionsFolder();
            if (string.IsNullOrWhiteSpace(actionFolder) || !Directory.Exists(actionFolder))
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }

            List<PaletteCommandItem> items = new List<PaletteCommandItem>();
            foreach (string filePath in Directory.GetFiles(actionFolder, "*.actm"))
            {
                string commandName = Path.GetFileNameWithoutExtension(filePath);
                string description = savedDescriptions.TryGetValue(commandName, out string saved)
                    ? saved
                    : "Action Recorder macro";

                items.Add(new PaletteCommandItem(
                    commandName,
                    description,
                    "Action Macro",
                    PaletteSourceKind.ActionMacro,
                    filePath));
            }

            return items;
        }

        private static string GetDefaultActionsFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(
                appData,
                "Autodesk",
                "AutoCAD 2022",
                "R24.1",
                "enu",
                "Support",
                "Actions");
        }
    }

    internal static class PaletteCommandCatalog
    {
        private static readonly Regex LispCommandRegex =
            new Regex(@"\(\s*defun\s+[cC]:(?<name>[^\s\(/]+)", RegexOptions.Compiled);
        private static readonly Regex BinaryCommandRegex =
            new Regex(@"(?i)(?:\(\s*defun\s+c:|c:)(?<name>[a-z0-9_\-$]+)", RegexOptions.Compiled);

        public static List<PaletteCommandItem> BuildItems()
        {
            Dictionary<string, string> savedDescriptions = PaletteDescriptionStore.Load();
            List<PaletteCommandItem> result = new List<PaletteCommandItem>();
            Dictionary<string, PaletteCommandItem> unique =
                new Dictionary<string, PaletteCommandItem>(StringComparer.OrdinalIgnoreCase);

            foreach (PaletteCommandItem item in ParseManagedDll(
                Assembly.GetExecutingAssembly(),
                "This DLL",
                Assembly.GetExecutingAssembly().Location,
                PaletteSourceKind.BuiltInDll))
            {
                AddOrReplace(result, unique, item, savedDescriptions);
            }

            if (DungXLispResolver.TryResolveAllLispFiles(out List<string> coreLispFiles, out _))
            {
                foreach (string filePath in coreLispFiles)
                {
                    string sourceLabel = filePath.EndsWith("DUNGX 2.LSP", StringComparison.OrdinalIgnoreCase)
                        ? "DUNGX 2"
                        : "DUNGX Custom";

                    foreach (PaletteCommandItem item in ParseLispFile(
                        filePath,
                        sourceLabel,
                        PaletteSourceKind.Lisp))
                    {
                        AddOrReplace(result, unique, item, savedDescriptions);
                    }
                }
            }

            foreach (KeyValuePair<string, string> manual in PaletteManualCommandStore.Load())
            {
                string description = savedDescriptions.TryGetValue(manual.Key, out string saved)
                    ? saved
                    : manual.Value;

                AddOrReplace(
                    result,
                    unique,
                    new PaletteCommandItem(
                        manual.Key,
                        description,
                        "Manual Alias",
                        PaletteSourceKind.ManualAlias,
                        manual.Key),
                    savedDescriptions);
            }

            foreach (PaletteCommandItem item in ActionMacroCatalog.BuildItems(savedDescriptions))
            {
                AddOrReplace(result, unique, item, savedDescriptions);
            }

            foreach (PaletteSourceFile source in PaletteSourceStore.LoadSources())
            {
                IEnumerable<PaletteCommandItem> items = Enumerable.Empty<PaletteCommandItem>();

                switch (source.SourceKind)
                {
                    case PaletteSourceKind.Lisp:
                        items = ParseLispFile(source.FilePath, source.DisplayName, source.SourceKind);
                        break;
                    case PaletteSourceKind.ManagedDll:
                        items = ParseManagedDll(source.FilePath, source.DisplayName, source.SourceKind);
                        break;
                    case PaletteSourceKind.Vlx:
                        items = ParseVlxFile(source.FilePath, source.DisplayName);
                        break;
                }

                foreach (PaletteCommandItem item in items)
                {
                    AddOrReplace(result, unique, item, savedDescriptions);
                }
            }

            return result;
        }

        private static void AddOrReplace(
            List<PaletteCommandItem> result,
            Dictionary<string, PaletteCommandItem> unique,
            PaletteCommandItem item,
            Dictionary<string, string> savedDescriptions)
        {
            if (savedDescriptions.TryGetValue(item.CommandName, out string savedDescription))
            {
                item.Description = savedDescription;
            }

            if (unique.TryGetValue(item.CommandName, out PaletteCommandItem existing))
            {
                result.Remove(existing);
            }

            unique[item.CommandName] = item;
            result.Add(item);
        }

        private static IEnumerable<PaletteCommandItem> ParseLispFile(
            string filePath,
            string sourceLabel,
            PaletteSourceKind sourceKind)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();
            string pendingComment = string.Empty;

            foreach (string rawLine in File.ReadAllLines(filePath, Encoding.Default))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith(";", StringComparison.Ordinal))
                {
                    string cleaned = CleanComment(line);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        pendingComment = cleaned;
                    }
                    continue;
                }

                Match match = LispCommandRegex.Match(line);
                if (!match.Success)
                {
                    pendingComment = string.Empty;
                    continue;
                }

                string commandName = match.Groups["name"].Value.Trim();
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    pendingComment = string.Empty;
                    continue;
                }

                items.Add(new PaletteCommandItem(
                    commandName,
                    pendingComment,
                    sourceLabel,
                    sourceKind,
                    filePath));
                pendingComment = string.Empty;
            }

            return items;
        }

        private static IEnumerable<PaletteCommandItem> ParseManagedDll(
            string assemblyPath,
            string sourceLabel,
            PaletteSourceKind sourceKind)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                return ParseManagedDll(assembly, sourceLabel, assemblyPath, sourceKind);
            }
            catch
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }
        }

        private static IEnumerable<PaletteCommandItem> ParseManagedDll(
            Assembly assembly,
            string sourceLabel,
            string assemblyPath,
            PaletteSourceKind sourceKind)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();

            foreach (Type type in GetLoadableTypes(assembly))
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static))
                {
                    object[] attrs = method.GetCustomAttributes(typeof(CommandMethodAttribute), false);
                    foreach (CommandMethodAttribute attr in attrs.OfType<CommandMethodAttribute>())
                    {
                        string commandName = attr.GlobalName;
                        if (string.IsNullOrWhiteSpace(commandName))
                        {
                            continue;
                        }

                        items.Add(new PaletteCommandItem(
                            commandName,
                            type.Name + "." + method.Name,
                            sourceLabel,
                            sourceKind,
                            assemblyPath));
                    }
                }
            }

            return items;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private static IEnumerable<PaletteCommandItem> ParseVlxFile(string filePath, string sourceLabel)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();

            try
            {
                string text = Encoding.Default.GetString(File.ReadAllBytes(filePath));
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match match in BinaryCommandRegex.Matches(text))
                {
                    string name = match.Groups["name"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(name) || names.Contains(name))
                    {
                        continue;
                    }

                    names.Add(name);
                    items.Add(new PaletteCommandItem(
                        name,
                        "VLX scan (best effort)",
                        sourceLabel,
                        PaletteSourceKind.Vlx,
                        filePath));
                }
            }
            catch
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }

            return items;
        }

        private static string CleanComment(string line)
        {
            string cleaned = Regex.Replace(line, @"^\s*;+", string.Empty).Trim();
            cleaned = cleaned.Trim('-', '=', '<', '>', '*', ':', ';', ' ');

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (cleaned.Length > 80)
            {
                cleaned = cleaned.Substring(0, 80).Trim();
            }

            return cleaned;
        }
    }

    // END OF A COMMAND
}





