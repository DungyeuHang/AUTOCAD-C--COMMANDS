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


    public class StretchByDimCommands
    {
        // ===============================
        // LỆNH CHÍNH
        // ===============================
        [CommandMethod("SAA_STRETCH_BY_DIM")]
        public void StretchByDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // ===== 1. CHỌN DIM CẦN THÀNH =====
            PromptSelectionOptions dimOpt = new PromptSelectionOptions();
            dimOpt.MessageForAdding = "\nQuét DIM kích thước CẦN THÀNH: ";

            PromptSelectionResult dimRes = ed.GetSelection(
                dimOpt,
                new SelectionFilter(new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Start, "DIMENSION")
                })
            );

            if (dimRes.Status != PromptStatus.OK) return;

            double targetWidth = 0.0;
            double targetHeight = 0.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in dimRes.Value)
                {
                    Dimension dim = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Dimension;
                    if (dim == null) continue;

                    if (IsHorizontal(dim))
                        targetWidth = dim.Measurement;
                    else
                        targetHeight = dim.Measurement;
                }

                if (targetWidth <= 0 || targetHeight <= 0)
                {
                    ed.WriteMessage("\n❌ Cần đủ 1 DIM ngang và 1 DIM dọc.");
                    return;
                }

                tr.Commit();
            }

            // ===== 2. CHỌN KHUNG BAN ĐẦU =====
            PromptSelectionOptions frameOpt = new PromptSelectionOptions();
            frameOpt.MessageForAdding = "\nQuét KHUNG BAN ĐẦU: ";

            PromptSelectionResult frameRes = ed.GetSelection(frameOpt);
            if (frameRes.Status != PromptStatus.OK) return;

            Extents3d ext;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ext = GetExtents(frameRes.Value, tr);
                tr.Commit();
            }

            double currentWidth = ext.MaxPoint.X - ext.MinPoint.X;
            double currentHeight = ext.MaxPoint.Y - ext.MinPoint.Y;

            double deltaW = targetWidth - currentWidth;
            double deltaH = targetHeight - currentHeight;

            ed.WriteMessage($"\nΔRộng = {deltaW}, ΔCao = {deltaH}");

            // ===== 3. STRETCH (BẢN ĐƠN GIẢN – MOVE TOÀN KHUNG) =====
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in frameRes.Value)
                {
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    ent.TransformBy(
                        Matrix3d.Displacement(
                            new Vector3d(deltaW / 2.0, deltaH, 0)
                        )
                    );
                }
                tr.Commit();
            }

            ed.WriteMessage("\n✔ STRETCH HOÀN TẤT.");
        }

        // ===============================
        // PHÂN BIỆT DIM NGANG / DỌC
        // DIMLINEAR + DIMROTATED → RotatedDimension
        // ===============================
        private bool IsHorizontal(Dimension dim)
        {
            if (dim is RotatedDimension rd)
            {
                double rot = rd.Rotation; // radian
                return Math.Abs(Math.Sin(rot)) < 0.01;
            }

            // các loại DIM khác không xử lý → coi là ngang
            return true;
        }

        // ===============================
        // LẤY BOUNDING BOX KHUNG
        // ===============================
        private Extents3d GetExtents(SelectionSet ss, Transaction tr)
        {
            bool first = true;
            Extents3d ext = new Extents3d();

            foreach (SelectedObject so in ss)
            {
                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (first)
                {
                    ext = ent.GeometricExtents;
                    first = false;
                }
                else
                {
                    ext.AddExtents(ent.GeometricExtents);
                }
            }

            return ext;
        }
    }

}





