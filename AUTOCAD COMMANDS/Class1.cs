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
                PromptSelectionOptions baseOpt = new PromptSelectionOptions();
                baseOpt.MessageForAdding = "\nChọn Polyline hoặc nhóm đối tượng gốc:";
                PromptSelectionResult baseRes = ed.GetSelection(baseOpt);
                if (baseRes.Status != PromptStatus.OK) return;

                Extents3d baseExt = GetSelectionExtents(baseRes.Value, tr);
                Point3d baseCenter = GetCenter(baseExt);

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

                foreach (SelectedObject sel in boundRes.Value)
                {
                    Entity ent = tr.GetObject(sel.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    Extents3d ext = ent.GeometricExtents;
                    Point3d center = GetCenter(ext);

                    double dx = center.X - baseCenter.X;
                    double dy = center.Y - baseCenter.Y;

                    // Ưu tiên trục lệch nhiều hơn
                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        // ===== TRÁI / PHẢI =====
                        if (dx < 0)
                        {
                            // TRÁI
                            if (leftEntity == null ||
                                center.X < GetCenter(leftEntity.GeometricExtents).X)
                            { leftEntity = ent; }
                        }
                        else
                        {
                            // PHẢI
                            if (rightEntity == null ||
                                center.X > GetCenter(rightEntity.GeometricExtents).X)
                            {
                                rightEntity = ent;
                            }
                        }
                    }



                    else
                    {
                        // TRÊN / DƯỚI
                        if (dy > 0)
                        {
                            if (topEntity == null ||
                                center.Y > GetCenter(topEntity.GeometricExtents).Y)
                                topEntity = ent;
                        }
                        else
                        {
                            if (bottomEntity == null ||
                                center.Y < GetCenter(bottomEntity.GeometricExtents).Y)
                                bottomEntity = ent;
                        }
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

    // END OF A COMMAND
}





