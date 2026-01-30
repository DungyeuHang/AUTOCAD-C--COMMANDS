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

       
    









    // END OF A COMMAND
}





