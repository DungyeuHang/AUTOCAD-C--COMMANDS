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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace AUTOCAD_COMMANDS
{
    // ======================================================
    // CDD2_CHIADIM
    // Mục đích: chia một DIM thẳng/ngang/dọc thành nhiều đoạn nhỏ.
    // Cách dùng: chọn DIM gốc, click các điểm chia nằm trên trục DIM, Enter để kết thúc.
    // Lưu ý khi sửa: lệnh này chỉ xử lý RotatedDimension, không áp dụng cho mọi loại DIM.
    // ======================================================
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

    // ======================================================
    // NHÓM DIM TỰ ĐỘNG
    // DAA: dim từ mốc gốc tới 4 đối tượng bao do người dùng chọn.
    // DDD: dim từ đối tượng/nhóm đối tượng tới 4 phía gần nhất, có bộ lọc đối tượng đích.
    // BD : đổi vị trí đặt DIM, tức điểm cuối cùng khi đặt DIM bằng lệnh AutoCAD.
    // ======================================================
    public class AutoDimCommand
    {
        private const double AutoDimTolerance = 1e-6;
        private const double DddMismatchTolerance = 1e-4;
        private const string DaaBaseObjectKeyword = "Object";
        private const string DaaBasePointKeyword = "Point";

        // DAA_Dim_auto:
        // - Chọn mốc gốc là Object hoặc Point.
        // - Sau đó chọn các đường bao đích.
        // - Lệnh tự tìm trái/phải/trên/dưới gần nhất trong selection đích rồi tạo DIM.
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
                Extents3d baseExt;
                Point3d baseCenter;
                DaaBaseMode baseMode = DaaBaseModeStore.Load();
                if (!TryPromptDaaBaseReference(
                    ed,
                    tr,
                    ref baseMode,
                    out baseExt,
                    out baseCenter))
                {
                    return;
                }

                // =============================
                // 2. CHỌN ĐƯỜNG BAO (LINE / PLINE)
                // =============================
                SelectionSet boundSelection = PromptForSelection(
                    ed,
                    "\nChọn các đường bao (Line / Polyline):");
                if (boundSelection == null) return;

                Entity leftEntity = null;
                Entity rightEntity = null;
                Entity topEntity = null;
                Entity bottomEntity = null;
                double leftDistance = double.MaxValue;
                double rightDistance = double.MaxValue;
                double topDistance = double.MaxValue;
                double bottomDistance = double.MaxValue;

                foreach (SelectedObject sel in boundSelection)
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

        // DDD_Dim_4_direction:
        // - Có hỗ trợ PickFirst để chọn sẵn đối tượng gốc trước khi gọi lệnh.
        // - Tự quét 4 hướng quanh extents của đối tượng gốc.
        // - Có bộ lọc target theo loại Line/Polyline/Block + Layer, lưu lại cho lần sau.
        [CommandMethod("DDD_Dim_4_direction", CommandFlags.UsePickSet)]
        public void AutoDimFourDirections()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId[] sourceIds = TryConsumePickFirst(ed);
            DddTargetFilter targetFilter = DddTargetFilterStore.Load();
            if (sourceIds == null || sourceIds.Length == 0)
            {
                if (!TryPromptDddSourceSelection(ed, db, ref targetFilter, out sourceIds))
                {
                    return;
                }
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Extents3d? sourceExtents = TryGetSelectionExtentsSafe(sourceIds, tr);
                if (!sourceExtents.HasValue)
                {
                    ed.WriteMessage("\nDDD_Dim_4_direction: không lấy được extents của đối tượng gốc.");
                    return;
                }

                Point3d sourceCenter = GetCenter(sourceExtents.Value);
                HashSet<ObjectId> sourceSet = new HashSet<ObjectId>(sourceIds);

                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return;
                }

                Extents3d? leftExtents = null;
                Extents3d? rightExtents = null;
                Extents3d? topExtents = null;
                Extents3d? bottomExtents = null;
                double leftDistance = double.MaxValue;
                double rightDistance = double.MaxValue;
                double topDistance = double.MaxValue;
                double bottomDistance = double.MaxValue;

                foreach (ObjectId id in currentSpace)
                {
                    if (sourceSet.Contains(id))
                    {
                        continue;
                    }

                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!IsAutoDimTargetCandidate(entity, tr, targetFilter))
                    {
                        continue;
                    }

                    if (!TryGetEntityExtentsSafe(entity, out Extents3d extents))
                    {
                        continue;
                    }

                    if (HasVerticalOverlap(sourceExtents.Value, extents))
                    {
                        double currentLeftDistance = sourceExtents.Value.MinPoint.X - extents.MaxPoint.X;
                        if (currentLeftDistance >= -AutoDimTolerance &&
                            currentLeftDistance < leftDistance)
                        {
                            leftDistance = Math.Max(0.0, currentLeftDistance);
                            leftExtents = extents;
                        }

                        double currentRightDistance = extents.MinPoint.X - sourceExtents.Value.MaxPoint.X;
                        if (currentRightDistance >= -AutoDimTolerance &&
                            currentRightDistance < rightDistance)
                        {
                            rightDistance = Math.Max(0.0, currentRightDistance);
                            rightExtents = extents;
                        }
                    }

                    if (HasHorizontalOverlap(sourceExtents.Value, extents))
                    {
                        double currentTopDistance = extents.MinPoint.Y - sourceExtents.Value.MaxPoint.Y;
                        if (currentTopDistance >= -AutoDimTolerance &&
                            currentTopDistance < topDistance)
                        {
                            topDistance = Math.Max(0.0, currentTopDistance);
                            topExtents = extents;
                        }

                        double currentBottomDistance = sourceExtents.Value.MinPoint.Y - extents.MaxPoint.Y;
                        if (currentBottomDistance >= -AutoDimTolerance &&
                            currentBottomDistance < bottomDistance)
                        {
                            bottomDistance = Math.Max(0.0, currentBottomDistance);
                            bottomExtents = extents;
                        }
                    }
                }

                if (!leftExtents.HasValue &&
                    !rightExtents.HasValue &&
                    !topExtents.HasValue &&
                    !bottomExtents.HasValue)
                {
                    ed.WriteMessage("\nDDD_Dim_4_direction: không tìm thấy đối tượng bao quanh phù hợp.");
                    return;
                }

                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                if (ms == null)
                {
                    return;
                }

                ObjectId dimLayerId = EnsureAutoDimLayer(db, tr);
                int createdCount = 0;
                double verticalDimPlacementX = sourceCenter.X - 200.0;

                if (leftExtents.HasValue && leftDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        0.0,
                        new Point3d(leftExtents.Value.MaxPoint.X, sourceCenter.Y, 0.0),
                        new Point3d(sourceExtents.Value.MinPoint.X, sourceCenter.Y, 0.0),
                        new Point3d(sourceCenter.X, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (rightExtents.HasValue && rightDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        0.0,
                        new Point3d(sourceExtents.Value.MaxPoint.X, sourceCenter.Y, 0.0),
                        new Point3d(rightExtents.Value.MinPoint.X, sourceCenter.Y, 0.0),
                        new Point3d(sourceCenter.X, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (topExtents.HasValue && topDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        Math.PI / 2.0,
                        new Point3d(sourceCenter.X, sourceExtents.Value.MaxPoint.Y, 0.0),
                        new Point3d(sourceCenter.X, topExtents.Value.MinPoint.Y, 0.0),
                        new Point3d(verticalDimPlacementX, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (bottomExtents.HasValue && bottomDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        Math.PI / 2.0,
                        new Point3d(sourceCenter.X, bottomExtents.Value.MaxPoint.Y, 0.0),
                        new Point3d(sourceCenter.X, sourceExtents.Value.MinPoint.Y, 0.0),
                        new Point3d(verticalDimPlacementX, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (createdCount == 0)
                {
                    ed.WriteMessage("\nDDD_Dim_4_direction: không có khoảng hở hợp lệ để dim.");
                    return;
                }

                tr.Commit();
                ed.WriteMessage($"\nDDD_Dim_4_direction: đã tạo {createdCount} dim.");

                string mismatchWarning = BuildDddMismatchWarning(
                    leftExtents.HasValue ? (double?)leftDistance : null,
                    rightExtents.HasValue ? (double?)rightDistance : null,
                    topExtents.HasValue ? (double?)topDistance : null,
                    bottomExtents.HasValue ? (double?)bottomDistance : null);
                if (!string.IsNullOrWhiteSpace(mismatchWarning))
                {
                    WF.MessageBox.Show(
                        mismatchWarning,
                        "DDD_Dim_4_direction",
                        WF.MessageBoxButtons.OK,
                        WF.MessageBoxIcon.Warning);
                }
            }
        }

        // BD:
        // - Đổi điểm đặt DIM line/text placement của nhiều DIM về cùng một điểm click.
        // - Dùng reflection để hỗ trợ nhiều subtype Dimension khác nhau.
        // - Với DIM thường, property quan trọng nhất là DimLinePoint.
        [CommandMethod("BD", CommandFlags.UsePickSet)]
        public void ChangeDimensionPlacementPoint()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ObjectId[] dimensionIds = null;
            ObjectId[] pickFirstIds = TryConsumePickFirst(ed);
            if (pickFirstIds != null && pickFirstIds.Length > 0)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    dimensionIds = FilterDimensionIds(pickFirstIds, tr);
                    tr.Commit();
                }

                if (dimensionIds == null || dimensionIds.Length == 0)
                {
                    ed.WriteMessage("\nPickFirst không có DIM hợp lệ, hãy quét chọn DIM.");
                }
            }

            if (dimensionIds == null || dimensionIds.Length == 0)
            {
                SelectionSet selection = PromptForDimensionSelection(ed);
                if (selection == null)
                {
                    return;
                }

                dimensionIds = selection.GetObjectIds();
            }

            PromptPointOptions pointOptions =
                new PromptPointOptions("\nChọn vị trí đặt mới cho DIM: ");
            PromptPointResult pointResult = ed.GetPoint(pointOptions);
            if (pointResult.Status != PromptStatus.OK)
            {
                return;
            }

            int changedCount = 0;
            int unsupportedCount = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objectId in dimensionIds)
                {
                    if (objectId.IsNull)
                    {
                        continue;
                    }

                    Dimension dimension = tr.GetObject(objectId, OpenMode.ForWrite, false) as Dimension;
                    if (dimension == null)
                    {
                        continue;
                    }

                    if (TrySetDimensionPlacementPoint(dimension, pointResult.Value))
                    {
                        changedCount++;
                    }
                    else
                    {
                        unsupportedCount++;
                    }
                }

                tr.Commit();
            }

            ed.Regen();

            ed.WriteMessage(
                unsupportedCount > 0
                    ? $"\nBD_CHANGE_POSITION_DIM: đã đổi {changedCount} DIM, bỏ qua {unsupportedCount} DIM không hỗ trợ điểm đặt."
                    : $"\nBD_CHANGE_POSITION_DIM: đã đổi vị trí đặt cho {changedCount} DIM.");
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

        private void CreateDimWithLayer(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            ObjectId layerId,
            double angle,
            Point3d p1,
            Point3d p2,
            Point3d dimPoint)
        {
            RotatedDimension dim = new RotatedDimension(
                angle,
                p1,
                p2,
                dimPoint,
                string.Empty,
                db.Dimstyle);
            if (!layerId.IsNull)
            {
                dim.LayerId = layerId;
            }
            else
            {
                dim.Layer = "_mss.kichthuoc";
            }

            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        private static string BuildDddMismatchWarning(
            double? leftDistance,
            double? rightDistance,
            double? topDistance,
            double? bottomDistance)
        {
            List<string> lines = new List<string>();

            if (leftDistance.HasValue &&
                rightDistance.HasValue &&
                Math.Abs(leftDistance.Value - rightDistance.Value) > DddMismatchTolerance)
            {
                lines.Add(
                    $"Dim ngang không bằng nhau: Trái = {FormatDddDistance(leftDistance.Value)}, Phải = {FormatDddDistance(rightDistance.Value)}");
            }

            if (topDistance.HasValue &&
                bottomDistance.HasValue &&
                Math.Abs(topDistance.Value - bottomDistance.Value) > DddMismatchTolerance)
            {
                lines.Add(
                    $"Dim dọc không bằng nhau: Trên = {FormatDddDistance(topDistance.Value)}, Dưới = {FormatDddDistance(bottomDistance.Value)}");
            }

            if (lines.Count == 0)
            {
                return null;
            }

            return "Kết quả DDD có dim đối xứng không bằng nhau.\n\n" + string.Join("\n", lines);
        }

        private static string FormatDddDistance(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
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

        private SelectionSet PromptForSelection(Editor ed, string message)
        {
            while (true)
            {
                PromptSelectionOptions options = new PromptSelectionOptions();
                options.MessageForAdding = message;

                PromptSelectionResult result = ed.GetSelection(options);
                if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
                {
                    return result.Value;
                }

                if (result.Status == PromptStatus.Cancel)
                {
                    return null;
                }

                ed.WriteMessage("\nChưa chọn được đối tượng hợp lệ, hãy chọn lại.");
            }
        }

        private bool TryPromptDaaBaseReference(
            Editor ed,
            Transaction tr,
            ref DaaBaseMode baseMode,
            out Extents3d baseExt,
            out Point3d baseCenter)
        {
            baseExt = default;
            baseCenter = Point3d.Origin;

            while (true)
            {
                if (!PromptForDaaBaseMode(ed, ref baseMode))
                {
                    return false;
                }

                if (baseMode == DaaBaseMode.Point)
                {
                    PromptPointResult pointResult = ed.GetPoint("\nChọn điểm gốc: ");
                    if (pointResult.Status == PromptStatus.OK)
                    {
                        baseCenter = pointResult.Value;
                        baseExt = new Extents3d(baseCenter, baseCenter);
                        return true;
                    }

                    return false;
                }

                SelectionSet baseSelection = PromptForSelection(
                    ed,
                    "\nChọn Polyline hoặc nhóm đối tượng gốc:");
                if (baseSelection == null)
                {
                    return false;
                }

                baseExt = GetSelectionExtents(baseSelection, tr);
                baseCenter = GetCenter(baseExt);
                return true;
            }
        }

        private bool PromptForDaaBaseMode(Editor ed, ref DaaBaseMode baseMode)
        {
            PromptKeywordOptions options =
                new PromptKeywordOptions(
                    $"\nChọn mốc gốc [Object/Point] <{baseMode}>: ");
            options.AllowNone = true;
            options.Keywords.Add(DaaBaseObjectKeyword);
            options.Keywords.Add(DaaBasePointKeyword);
            options.Keywords.Default = baseMode.ToString();

            PromptResult result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (result.Status == PromptStatus.OK &&
                Enum.TryParse(result.StringResult, true, out DaaBaseMode parsedMode))
            {
                baseMode = parsedMode;
            }

            DaaBaseModeStore.Save(baseMode);
            return true;
        }

        private bool TryPromptDddSourceSelection(
            Editor ed,
            Database db,
            ref DddTargetFilter targetFilter,
            out ObjectId[] sourceIds)
        {
            sourceIds = null;

            while (true)
            {
                if (!PromptForDddFilterMode(ed, db, ref targetFilter))
                {
                    return false;
                }

                SelectionSet sourceSelection = PromptForSelection(
                    ed,
                    "\nChọn đối tượng gốc hoặc nhóm đối tượng:");
                if (sourceSelection == null)
                {
                    return false;
                }

                sourceIds = sourceSelection.GetObjectIds();
                return sourceIds != null && sourceIds.Length > 0;
            }
        }

        private bool PromptForDddFilterMode(
            Editor ed,
            Database db,
            ref DddTargetFilter targetFilter)
        {
            while (true)
            {
                string defaultLabel = targetFilter?.ToDisplayText() ?? "None";
                PromptKeywordOptions options =
                    new PromptKeywordOptions(
                        $"\nFilter đích DDD [UseCurrent/Pick/None] <UseCurrent: {defaultLabel}>: ");
                options.AllowNone = true;
                options.Keywords.Add("UseCurrent");
                options.Keywords.Add("Pick");
                options.Keywords.Add("None");
                options.Keywords.Default = "UseCurrent";

                PromptResult result = ed.GetKeywords(options);
                if (result.Status == PromptStatus.Cancel)
                {
                    return false;
                }

                string action = result.Status == PromptStatus.None
                    ? "UseCurrent"
                    : (result.StringResult ?? "UseCurrent");

                if (string.Equals(action, "UseCurrent", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(action, "None", StringComparison.OrdinalIgnoreCase))
                {
                    targetFilter = null;
                    DddTargetFilterStore.Save(null);
                    return true;
                }

                if (PromptForDddTargetFilter(ed, db, targetFilter, out DddTargetFilter updatedFilter))
                {
                    targetFilter = updatedFilter;
                    try
                    {
                        ed.SetImpliedSelection(Array.Empty<ObjectId>());
                    }
                    catch
                    {
                    }

                    return true;
                }

                return false;
            }
        }

        private SelectionSet PromptForDimensionSelection(Editor ed)
        {
            // Chỉ cho phép quét DIMENSION để tránh người dùng click nhầm sang line/text/block.
            // Bật SELECTIONOFFSCREEN tạm thời để vùng quét ngoài màn hình vẫn bắt DIM ổn hơn.
            SelectionFilter filter = new SelectionFilter(
                new[]
                {
                    new TypedValue((int)DxfCode.Start, "DIMENSION")
                });

            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                while (true)
                {
                    PromptSelectionOptions options = new PromptSelectionOptions
                    {
                        MessageForAdding = "\nQuét chọn DIM cần đổi điểm đặt: "
                    };

                    PromptSelectionResult result = ed.GetSelection(options, filter);
                    if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
                    {
                        return result.Value;
                    }

                    if (result.Status == PromptStatus.Cancel)
                    {
                        return null;
                    }

                    ed.WriteMessage("\nChưa chọn được DIM hợp lệ, hãy chọn lại.");
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

        private static ObjectId[] FilterDimensionIds(IEnumerable<ObjectId> objectIds, Transaction tr)
        {
            List<ObjectId> dimensionIds = new List<ObjectId>();
            foreach (ObjectId objectId in objectIds ?? Enumerable.Empty<ObjectId>())
            {
                if (objectId.IsNull)
                {
                    continue;
                }

                if (tr.GetObject(objectId, OpenMode.ForRead, false) is Dimension)
                {
                    dimensionIds.Add(objectId);
                }
            }

            return dimensionIds.ToArray();
        }

        private static bool TrySetDimensionPlacementPoint(Dimension dimension, Point3d point)
        {
            // Mỗi loại Dimension của AutoCAD có thể đặt tên property khác nhau.
            // Thứ tự ưu tiên ở đây:
            // - DimLinePoint: đúng điểm đặt của dim thẳng/aligned/rotated.
            // - ArcPoint/LeaderEndPoint: cho một số dim đặc biệt.
            // - TextPosition: fallback cuối cùng nếu dim chỉ cho đổi vị trí text.
            if (dimension == null)
            {
                return false;
            }

            // Có những DIM đã bị kéo text thủ công trước đó.
            // Nếu không trả text về vị trí default trước khi đổi DimLinePoint,
            // có thể nhìn như DIM không nhúc nhích dù setter vẫn chạy.
            TrySetUsingDefaultTextPosition(dimension, true);

            string[] placementProperties =
            {
                "DimLinePoint",
                "ArcPoint",
                "LeaderEndPoint",
                "TextPosition"
            };

            Type dimensionType = dimension.GetType();
            foreach (string propertyName in placementProperties)
            {
                PropertyInfo property = dimensionType.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public);

                if (property == null ||
                    !property.CanWrite ||
                    property.PropertyType != typeof(Point3d))
                {
                    continue;
                }

                try
                {
                    if (string.Equals(propertyName, "TextPosition", StringComparison.OrdinalIgnoreCase))
                    {
                        TrySetUsingDefaultTextPosition(dimension, false);
                    }

                    property.SetValue(dimension, point, null);
                    TryRecomputeDimensionBlock(dimension);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static void TrySetUsingDefaultTextPosition(Dimension dimension, bool value)
        {
            try
            {
                PropertyInfo property = dimension.GetType().GetProperty(
                    "UsingDefaultTextPosition",
                    BindingFlags.Instance | BindingFlags.Public);

                if (property != null &&
                    property.CanWrite &&
                    property.PropertyType == typeof(bool))
                {
                    property.SetValue(dimension, value, null);
                }
            }
            catch
            {
            }
        }

        private static void TryRecomputeDimensionBlock(Dimension dimension)
        {
            try
            {
                MethodInfo method = dimension.GetType().GetMethod(
                    "RecomputeDimensionBlock",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(bool) },
                    null);

                method?.Invoke(dimension, new object[] { true });
            }
            catch
            {
            }
        }

        private Extents3d? TryGetSelectionExtentsSafe(IEnumerable<ObjectId> objectIds, Transaction tr)
        {
            Extents3d? extents = null;
            foreach (ObjectId objectId in objectIds ?? Enumerable.Empty<ObjectId>())
            {
                if (objectId.IsNull)
                {
                    continue;
                }

                Entity entity = tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                if (!TryGetEntityExtentsSafe(entity, out Extents3d currentExtents))
                {
                    continue;
                }

                extents = extents.HasValue
                    ? MergeExtents(extents.Value, currentExtents)
                    : currentExtents;
            }

            return extents;
        }

        private bool TryGetEntityExtentsSafe(Entity entity, out Extents3d extents)
        {
            try
            {
                if (entity == null || entity.IsErased)
                {
                    extents = default;
                    return false;
                }

                extents = entity.GeometricExtents;
                return true;
            }
            catch
            {
                extents = default;
                return false;
            }
        }

        private static Extents3d MergeExtents(Extents3d left, Extents3d right)
        {
            return new Extents3d(
                new Point3d(
                    Math.Min(left.MinPoint.X, right.MinPoint.X),
                    Math.Min(left.MinPoint.Y, right.MinPoint.Y),
                    Math.Min(left.MinPoint.Z, right.MinPoint.Z)),
                new Point3d(
                    Math.Max(left.MaxPoint.X, right.MaxPoint.X),
                    Math.Max(left.MaxPoint.Y, right.MaxPoint.Y),
                    Math.Max(left.MaxPoint.Z, right.MaxPoint.Z)));
        }

        private bool IsAutoDimTargetCandidate(
            Entity entity,
            Transaction tr,
            DddTargetFilter targetFilter)
        {
            if (entity == null || entity.IsErased)
            {
                return false;
            }

            if (entity is Dimension ||
                entity is DBText ||
                entity is MText ||
                entity is AttributeDefinition ||
                entity is AttributeReference)
            {
                return false;
            }

            try
            {
                if (!entity.Visible)
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                LayerTableRecord layer =
                    tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer != null && (layer.IsOff || layer.IsFrozen))
                {
                    return false;
                }
            }
            catch
            {
            }

            if (targetFilter == null)
            {
                return true;
            }

            if (!string.Equals(entity.Layer, targetFilter.LayerName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryGetDddTargetKind(entity, out DddTargetKind kind) && kind == targetFilter.Kind;
        }

        private bool PromptForDddTargetFilter(
            Editor ed,
            Database db,
            DddTargetFilter savedFilter,
            out DddTargetFilter targetFilter)
        {
            // Filter đích của DDD:
            // - Enter/Space: dùng lại filter đã lưu.
            // - Pick: click đối tượng mẫu để lấy loại + layer.
            // - None: bỏ filter, chạy tự do như bản đầu.
            while (true)
            {
                string defaultLabel = savedFilter?.ToDisplayText() ?? "None";
                PromptKeywordOptions options =
                    new PromptKeywordOptions(
                        $"\nChọn đối tượng đích [Pick/None] <{defaultLabel}>: ");
                options.AllowNone = true;
                options.Keywords.Add("Pick");
                options.Keywords.Add("None");

                PromptResult result = ed.GetKeywords(options);
                if (result.Status == PromptStatus.Cancel)
                {
                    targetFilter = null;
                    return false;
                }

                if (result.Status == PromptStatus.None)
                {
                    targetFilter = savedFilter;
                    return true;
                }

                if (string.Equals(result.StringResult, "None", StringComparison.OrdinalIgnoreCase))
                {
                    targetFilter = null;
                    DddTargetFilterStore.Save(null);
                    return true;
                }

                PromptEntityOptions entityOptions =
                    new PromptEntityOptions("\nChọn Line / Polyline / Block làm mẫu đích: ");
                entityOptions.SetRejectMessage("\nChỉ hỗ trợ Line, Polyline hoặc BlockReference.");
                entityOptions.AddAllowedClass(typeof(Line), true);
                entityOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
                entityOptions.AddAllowedClass(typeof(Polyline2d), true);
                entityOptions.AddAllowedClass(typeof(Polyline3d), true);
                entityOptions.AddAllowedClass(typeof(BlockReference), true);

                PromptEntityResult entityResult = ed.GetEntity(entityOptions);
                if (entityResult.Status == PromptStatus.Cancel)
                {
                    targetFilter = null;
                    return false;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity entity = tr.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Entity;
                    if (TryCreateDddTargetFilter(entity, out DddTargetFilter pickedFilter))
                    {
                        targetFilter = pickedFilter;
                        DddTargetFilterStore.Save(targetFilter);
                        try
                        {
                            ed.SetImpliedSelection(Array.Empty<ObjectId>());
                        }
                        catch
                        {
                        }

                        return true;
                    }
                }

                ed.WriteMessage("\nKhông đọc được filter từ đối tượng vừa chọn, hãy chọn lại.");
            }
        }

        private bool TryCreateDddTargetFilter(Entity entity, out DddTargetFilter targetFilter)
        {
            targetFilter = null;
            if (entity == null || string.IsNullOrWhiteSpace(entity.Layer))
            {
                return false;
            }

            if (!TryGetDddTargetKind(entity, out DddTargetKind kind))
            {
                return false;
            }

            targetFilter = new DddTargetFilter
            {
                Kind = kind,
                LayerName = entity.Layer
            };
            return true;
        }

        private bool TryGetDddTargetKind(Entity entity, out DddTargetKind kind)
        {
            if (entity is Line)
            {
                kind = DddTargetKind.Line;
                return true;
            }

            if (entity is Autodesk.AutoCAD.DatabaseServices.Polyline ||
                entity is Polyline2d ||
                entity is Polyline3d)
            {
                kind = DddTargetKind.Polyline;
                return true;
            }

            if (entity is BlockReference)
            {
                kind = DddTargetKind.Block;
                return true;
            }

            kind = default;
            return false;
        }

        private bool HasVerticalOverlap(Extents3d sourceExtents, Extents3d targetExtents)
        {
            return targetExtents.MaxPoint.Y >= sourceExtents.MinPoint.Y - AutoDimTolerance &&
                   targetExtents.MinPoint.Y <= sourceExtents.MaxPoint.Y + AutoDimTolerance;
        }

        private bool HasHorizontalOverlap(Extents3d sourceExtents, Extents3d targetExtents)
        {
            return targetExtents.MaxPoint.X >= sourceExtents.MinPoint.X - AutoDimTolerance &&
                   targetExtents.MinPoint.X <= sourceExtents.MaxPoint.X + AutoDimTolerance;
        }

        private ObjectId EnsureAutoDimLayer(Database db, Transaction tr)
        {
            const string dimLayerName = "_mss.kichthuoc";
            LayerTable layerTable =
                tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

            if (layerTable == null)
            {
                return ObjectId.Null;
            }

            if (layerTable.Has(dimLayerName))
            {
                return layerTable[dimLayerName];
            }

            layerTable.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord
            {
                Name = dimLayerName
            };

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }

        private enum DddTargetKind
        {
            Line,
            Polyline,
            Block
        }

        private enum DaaBaseMode
        {
            Object,
            Point
        }

        private sealed class DddTargetFilter
        {
            public DddTargetKind Kind { get; set; }
            public string LayerName { get; set; }

            public string ToDisplayText()
            {
                return $"{Kind} | {LayerName}";
            }
        }

        private static class DddTargetFilterStore
        {
            private static readonly string FilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "ddd_dim_target_filter.tsv");

            public static DddTargetFilter Load()
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                try
                {
                    string raw = File.ReadAllText(FilePath, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return null;
                    }

                    string[] parts = raw.Split('\t');
                    if (parts.Length < 2)
                    {
                        return null;
                    }

                    if (!Enum.TryParse(parts[0].Trim(), true, out DddTargetKind kind))
                    {
                        return null;
                    }

                    string layerName = parts[1].Trim();
                    if (string.IsNullOrWhiteSpace(layerName))
                    {
                        return null;
                    }

                    return new DddTargetFilter
                    {
                        Kind = kind,
                        LayerName = layerName
                    };
                }
                catch
                {
                    return null;
                }
            }

            public static void Save(DddTargetFilter filter)
            {
                try
                {
                    if (filter == null)
                    {
                        if (File.Exists(FilePath))
                        {
                            File.Delete(FilePath);
                        }

                        return;
                    }

                    File.WriteAllText(
                        FilePath,
                        filter.Kind + "\t" + (filter.LayerName ?? string.Empty),
                        Encoding.UTF8);
                }
                catch
                {
                }
            }
        }

        private static class DaaBaseModeStore
        {
            private static readonly string FilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "daa_dim_base_mode.txt");

            public static DaaBaseMode Load()
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        return DaaBaseMode.Object;
                    }

                    string raw = (File.ReadAllText(FilePath, Encoding.UTF8) ?? string.Empty).Trim();
                    return Enum.TryParse(raw, true, out DaaBaseMode mode)
                        ? mode
                        : DaaBaseMode.Object;
                }
                catch
                {
                    return DaaBaseMode.Object;
                }
            }

            public static void Save(DaaBaseMode mode)
            {
                try
                {
                    File.WriteAllText(FilePath, mode.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }




    // ======================================================
    // CAA_change_pline
    // Mục đích: chuẩn hóa polyline trước khi dùng cho các workflow tự động.
    // Cách làm:
    // - Chọn 1 lightweight Polyline.
    // - Hỏi có set Closed hay bỏ qua, có lưu lựa chọn lần cuối.
    // - Hỏi hướng polyline, hiển thị rõ CCW/CW theo tiếng Việt, có lưu lựa chọn lần cuối.
    // - Luôn yêu cầu người dùng pick điểm đầu mong muốn.
    // - Ép chiều vertex theo hướng người dùng chọn nếu polyline kín.
    // - Đổi điểm đầu theo vertex người dùng chọn.
    // Lưu ý: với polyline hở, chỉ cho đổi điểm đầu giữa 2 đầu mút để tránh đổi hình.
    // ======================================================
    public class ChangePolylineCommand
    {
        private const double CoordinateTolerance = 1e-8;

        [CommandMethod("CAA_change_pline")]
        public void ChangePolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            CaaCloseMode closeMode = CaaPolylineSettingsStore.LoadCloseMode();
            CaaDirectionMode directionMode = CaaPolylineSettingsStore.LoadDirectionMode();

            if (!TryPromptCaaEntity(
                ed,
                ref closeMode,
                ref directionMode,
                out ObjectId polylineId))
            {
                return;
            }

            if (!TryPromptCaaStartPoint(
                ed,
                ref closeMode,
                ref directionMode,
                out Point2d pickedStartPoint))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                    tr.GetObject(polylineId, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                if (polyline == null)
                {
                    ed.WriteMessage("\nCAA_change_pline: không đọc được polyline.");
                    return;
                }

                if (polyline.NumberOfVertices < 2)
                {
                    ed.WriteMessage("\nCAA_change_pline: polyline phải có ít nhất 2 vertex.");
                    return;
                }

                if (closeMode == CaaCloseMode.Close)
                {
                    polyline.Closed = true;
                }

                bool reversed = false;
                bool startChanged = false;

                if (polyline.Closed && polyline.NumberOfVertices >= 3)
                {
                    double signedArea = GetPolylineSignedArea(polyline);
                    bool isCounterClockwise = signedArea >= -CoordinateTolerance;
                    bool shouldBeCounterClockwise = directionMode == CaaDirectionMode.CCW;
                    if (isCounterClockwise != shouldBeCounterClockwise)
                    {
                        polyline.ReverseCurve();
                        reversed = true;
                    }
                }
                else if (!polyline.Closed)
                {
                    int requestedStartIndex = FindClosestVertex(polyline, pickedStartPoint);

                    // Với polyline hở, chỉ được đổi giữa 2 đầu mút.
                    // Không xoay vertex giữa lên đầu vì như vậy sẽ làm đổi path.
                    if (requestedStartIndex == polyline.NumberOfVertices - 1)
                    {
                        polyline.ReverseCurve();
                        reversed = true;
                        startChanged = true;
                    }
                    else if (requestedStartIndex != 0)
                    {
                        ed.WriteMessage(
                            "\nCAA_change_pline: polyline đang hở nên chỉ nhận điểm đầu ở 1 trong 2 đầu mút để tránh đổi hình. Chọn Close nếu muốn đổi sang vertex giữa.");
                    }
                }

                if (polyline.Closed && polyline.NumberOfVertices >= 3)
                {
                    int startIndex = FindClosestVertex(polyline, pickedStartPoint);
                    if (startIndex > 0)
                    {
                        RotateClosedPolylineStart(polyline, startIndex);
                        startChanged = true;
                    }
                }

                tr.Commit();

                ed.WriteMessage(
                    $"\nCAA_change_pline: xong. Closed={(polyline.Closed ? "Yes" : "No")}, Reverse={(reversed ? "Yes" : "No")}, đổi điểm đầu={(startChanged ? "Yes" : "No")}.");
            }
        }

        private static int FindClosestVertex(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            Point2d targetPoint)
        {
            int bestIndex = 0;
            double bestDistanceSquared = double.MaxValue;

            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                Point2d point = polyline.GetPoint2dAt(i);
                double dx = point.X - targetPoint.X;
                double dy = point.Y - targetPoint.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestIndex = i;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return bestIndex;
        }

        private static double GetPolylineSignedArea(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            double area = 0.0;
            int vertexCount = polyline.NumberOfVertices;
            for (int i = 0; i < vertexCount; i++)
            {
                Point2d current = polyline.GetPoint2dAt(i);
                Point2d next = polyline.GetPoint2dAt((i + 1) % vertexCount);
                area += current.X * next.Y - next.X * current.Y;
            }

            return area / 2.0;
        }

        private static void RotateClosedPolylineStart(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            int startIndex)
        {
            if (startIndex <= 0 || startIndex >= polyline.NumberOfVertices)
            {
                return;
            }

            List<CaaPolylineVertex> vertices = ReadVertices(polyline);
            List<CaaPolylineVertex> reordered = vertices
                .Skip(startIndex)
                .Concat(vertices.Take(startIndex))
                .ToList();

            for (int i = 0; i < reordered.Count; i++)
            {
                CaaPolylineVertex vertex = reordered[i];
                polyline.SetPointAt(i, vertex.Point);
                polyline.SetBulgeAt(i, vertex.Bulge);
                polyline.SetStartWidthAt(i, vertex.StartWidth);
                polyline.SetEndWidthAt(i, vertex.EndWidth);
            }
        }

        private static List<CaaPolylineVertex> ReadVertices(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            List<CaaPolylineVertex> vertices = new List<CaaPolylineVertex>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                vertices.Add(
                    new CaaPolylineVertex(
                        polyline.GetPoint2dAt(i),
                        polyline.GetBulgeAt(i),
                        polyline.GetStartWidthAt(i),
                        polyline.GetEndWidthAt(i)));
            }

            return vertices;
        }

        private static bool TryPromptCaaStartPoint(
            Editor ed,
            ref CaaCloseMode closeMode,
            ref CaaDirectionMode directionMode,
            out Point2d pickedStartPoint)
        {
            pickedStartPoint = Point2d.Origin;

            while (true)
            {
                PromptPointOptions startPointOptions =
                    new PromptPointOptions(
                        $"\nChọn điểm đầu mong muốn (pline hở: chọn gần đầu mút) hoặc [Settings] <Close={closeMode}, Dir={directionMode}>: ");
                startPointOptions.AppendKeywordsToMessage = false;
                startPointOptions.Keywords.Add("Settings");

                PromptPointResult startPointResult = ed.GetPoint(startPointOptions);
                if (startPointResult.Status == PromptStatus.OK)
                {
                    pickedStartPoint =
                        new Point2d(startPointResult.Value.X, startPointResult.Value.Y);
                    return true;
                }

                if (startPointResult.Status == PromptStatus.Keyword &&
                    string.Equals(startPointResult.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PromptForCaaSettings(ed, ref closeMode, ref directionMode))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }

        private static bool TryPromptCaaEntity(
            Editor ed,
            ref CaaCloseMode closeMode,
            ref CaaDirectionMode directionMode,
            out ObjectId polylineId)
        {
            polylineId = ObjectId.Null;

            while (true)
            {
                PromptEntityOptions entityOptions =
                    new PromptEntityOptions(
                        $"\nChọn polyline cần chuẩn hóa hoặc [Settings] <Close={closeMode}, Dir={directionMode}>: ");
                entityOptions.AppendKeywordsToMessage = false;
                entityOptions.SetRejectMessage("\nChỉ hỗ trợ lightweight Polyline.");
                entityOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
                entityOptions.Keywords.Add("Settings");

                PromptEntityResult entityResult = ed.GetEntity(entityOptions);
                if (entityResult.Status == PromptStatus.OK)
                {
                    polylineId = entityResult.ObjectId;
                    return true;
                }

                if (entityResult.Status == PromptStatus.Keyword &&
                    string.Equals(entityResult.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PromptForCaaSettings(ed, ref closeMode, ref directionMode))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }

        private static bool PromptForCaaSettings(
            Editor ed,
            ref CaaCloseMode closeMode,
            ref CaaDirectionMode directionMode)
        {
            PromptKeywordOptions closeOptions =
                new PromptKeywordOptions(
                    $"\nXử lý closed polyline [Close/Skip] <{closeMode}>: ");
            closeOptions.AllowNone = true;
            closeOptions.Keywords.Add("Close");
            closeOptions.Keywords.Add("Skip");
            closeOptions.Keywords.Default = closeMode.ToString();

            PromptResult closeResult = ed.GetKeywords(closeOptions);
            if (closeResult.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (closeResult.Status == PromptStatus.OK &&
                Enum.TryParse(closeResult.StringResult, true, out CaaCloseMode parsedCloseMode))
            {
                closeMode = parsedCloseMode;
            }

            CaaPolylineSettingsStore.SaveCloseMode(closeMode);

            PromptKeywordOptions directionOptions =
                new PromptKeywordOptions(
                    $"\nChọn hướng polyline [CCW=Nguoc chieu kim dong ho/CW=Cung chieu kim dong ho] <{directionMode}>: ");
            directionOptions.AllowNone = true;
            directionOptions.Keywords.Add("CCW");
            directionOptions.Keywords.Add("CW");
            directionOptions.Keywords.Default = directionMode.ToString();

            PromptResult directionResult = ed.GetKeywords(directionOptions);
            if (directionResult.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (directionResult.Status == PromptStatus.OK &&
                Enum.TryParse(directionResult.StringResult, true, out CaaDirectionMode parsedDirectionMode))
            {
                directionMode = parsedDirectionMode;
            }

            CaaPolylineSettingsStore.SaveDirectionMode(directionMode);
            return true;
        }

        private readonly struct CaaPolylineVertex
        {
            public CaaPolylineVertex(Point2d point, double bulge, double startWidth, double endWidth)
            {
                Point = point;
                Bulge = bulge;
                StartWidth = startWidth;
                EndWidth = endWidth;
            }

            public Point2d Point { get; }

            public double Bulge { get; }

            public double StartWidth { get; }

            public double EndWidth { get; }
        }

        private enum CaaCloseMode
        {
            Close,
            Skip
        }

        private enum CaaDirectionMode
        {
            CCW,
            CW
        }

        private static class CaaPolylineSettingsStore
        {
            private static readonly string CloseModeFilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "caa_change_pline_settings.txt");

            private static readonly string DirectionModeFilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "caa_change_pline_direction_mode.txt");

            public static CaaCloseMode LoadCloseMode()
            {
                try
                {
                    if (!File.Exists(CloseModeFilePath))
                    {
                        return CaaCloseMode.Close;
                    }

                    string raw = File.ReadAllText(CloseModeFilePath, Encoding.UTF8).Trim();
                    return Enum.TryParse(raw, true, out CaaCloseMode mode)
                        ? mode
                        : CaaCloseMode.Close;
                }
                catch
                {
                    return CaaCloseMode.Close;
                }
            }

            public static void SaveCloseMode(CaaCloseMode mode)
            {
                try
                {
                    File.WriteAllText(CloseModeFilePath, mode.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }

            public static CaaDirectionMode LoadDirectionMode()
            {
                try
                {
                    if (!File.Exists(DirectionModeFilePath))
                    {
                        return CaaDirectionMode.CCW;
                    }

                    string raw = File.ReadAllText(DirectionModeFilePath, Encoding.UTF8).Trim();
                    return Enum.TryParse(raw, true, out CaaDirectionMode mode)
                        ? mode
                        : CaaDirectionMode.CCW;
                }
                catch
                {
                    return CaaDirectionMode.CCW;
                }
            }

            public static void SaveDirectionMode(CaaDirectionMode mode)
            {
                try
                {
                    File.WriteAllText(DirectionModeFilePath, mode.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }



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

                ObjectId layerId = EnsureLayer(db, tr, PhantomLayerName);
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

        private static ObjectId EnsureLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable layerTable =
                tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (layerTable == null)
            {
                return ObjectId.Null;
            }

            if (layerTable.Has(layerName))
            {
                return layerTable[layerName];
            }

            layerTable.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord
            {
                Name = layerName
            };

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }
    }



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
                    ObjectId layerId = EnsureLayer(db, tr, PhantomLayerName);
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
                                $"{prefix}_p{count} = APoint({FormatNumber(point.X)}, {FormatNumber(point.Y)})";
                        }
                        else
                        {
                            double dx = point.X - previousPoint.Value.X;
                            double dy = point.Y - previousPoint.Value.Y;
                            definitionLine =
                                $"{prefix}_p{count} = APoint({prefix}_p{count - 1}.x{FormatOffset(dx)}, {prefix}_p{count - 1}.y{FormatOffset(dy)})";
                        }

                        definitionLines.Add(definitionLine);

                        double labelYOffset = count % 2 == 1 ? 0.1 : -0.1;
                        Point3d labelPoint = new Point3d(point.X, point.Y + labelYOffset, point.Z);
                        AddMText(
                            currentSpace,
                            tr,
                            layerId,
                            labelPoint,
                            PointLabelWidth,
                            definitionLine);

                        previousPoint = point;
                    }

                    string smartShapeText = BuildSmartShapeText(polyline, points, prefix);
                    string summaryText = string.Join("\n", definitionLines.Concat(new[] { smartShapeText }));
                    Point3d firstPoint = points[0];
                    Point3d summaryPoint = new Point3d(firstPoint.X, firstPoint.Y - 3.0, firstPoint.Z);
                    AddMText(
                        currentSpace,
                        tr,
                        layerId,
                        summaryPoint,
                        SummaryTextWidth,
                        summaryText);

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

        private static void AddMText(
            BlockTableRecord owner,
            Transaction tr,
            ObjectId layerId,
            Point3d location,
            double width,
            string contents)
        {
            MText text = new MText
            {
                Location = location,
                Width = width,
                TextHeight = TextHeight,
                Contents = ToMTextContents(contents),
                LayerId = layerId
            };

            owner.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static string ToMTextContents(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("\n", "\\P");
        }

        private static string FormatNumber(double value)
        {
            if (Math.Abs(value - Math.Round(value)) <= NumericTolerance)
            {
                return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string FormatOffset(double value)
        {
            if (Math.Abs(value) <= NumericTolerance)
            {
                return string.Empty;
            }

            return value > 0.0
                ? " + " + FormatNumber(value)
                : " - " + FormatNumber(Math.Abs(value));
        }

        private static string FormatRadius(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static ObjectId EnsureLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable layerTable =
                tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (layerTable == null)
            {
                return ObjectId.Null;
            }

            if (layerTable.Has(layerName))
            {
                return layerTable[layerName];
            }

            layerTable.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord
            {
                Name = layerName
            };

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }
    }



    // ======================================================
    // SDXY - SMART DIM THEO TRỤC X/Y
    // Mục đích: click điểm đầu, click điểm hướng, tự dim tới đối tượng gần điểm hướng nhất.
    // Ghi chú: SmartDimX/SmartDimY cũ vẫn còn trong class nhưng command chính đang dùng là SDXY.
    // ======================================================
    public class SmartDimXCommand
    {
        private const string DimLayerName = "_mss.kichthuoc";
        private const double DirectionTolerance = 1e-6;
        private const double PreviewPointTolerance = 1e-4;
        private const double SearchDistance = 1000000.0;
        private static readonly RXClass CurveRxClass = RXObject.GetClass(typeof(Curve));
        private static readonly RXClass DimensionRxClass = RXObject.GetClass(typeof(Dimension));
        private static readonly RXClass EntityRxClass = RXObject.GetClass(typeof(Entity));
        private static List<SdxyEntityTypeChoice> _cachedSdxyEntityTypeChoices;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public void SmartDimX()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            if (!TryPromptAxisDirection(
                ed,
                startRes.Value,
                "\nChọn điểm để xác định hướng X (+/-): ",
                true,
                out Point3d dirPoint,
                out double direction))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnXAxis(ed, currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng X đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    targetPoint.Value.X,
                    dirPoint.Y,
                    dirPoint.Z);

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);
                currentSpace.UpgradeOpen();

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dirPoint,
                    Rotation = 0.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        public void SmartDimY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            if (!TryPromptAxisDirection(
                ed,
                startRes.Value,
                "\nChọn điểm để xác định hướng Y (+/-): ",
                false,
                out Point3d dirPoint,
                out double direction))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnYAxis(ed, currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng Y đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    dirPoint.X,
                    targetPoint.Value.Y,
                    dirPoint.Z);

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
                    DimLinePoint = dirPoint,
                    Rotation = Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        // SDXY:
        // - Tự chọn trục X/Y theo hướng click.
        // - Điểm click thứ 2 chỉ dùng để xác định hướng và dò target.
        // - Sau khi tìm được điểm cuối, người dùng tự chọn điểm đặt DIM.
        // - Nhờ vậy có thể dim vượt qua các đối tượng trung gian gần điểm đầu.
        [CommandMethod("SDXY")]
        public void SmartDimXY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            SdxyTargetSettings settings = SdxyTargetSettingsStore.Load();
            if (!TryPromptSdxyStartPoint(ed, db, ref settings, out Point3d startPoint))
            {
                return;
            }

            if (!TryPromptAxisDirection(
                ed,
                startPoint,
                "\nChọn điểm để xác định hướng dim X/Y (nhấn Shift để đổi X/Y): ",
                null,
                out Point3d dirPoint,
                out double direction,
                out bool useXAxis))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint = useXAxis
                    ? FindNearestPointOnXAxisFromProbe(
                        ed,
                        currentSpace,
                        tr,
                        startPoint,
                        dirPoint,
                        direction,
                        settings)
                    : FindNearestPointOnYAxisFromProbe(
                        ed,
                        currentSpace,
                        tr,
                        startPoint,
                        dirPoint,
                        direction,
                        settings);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        useXAxis
                            ? "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng X đã chọn."
                            : "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng Y đã chọn.");
                    return;
                }

                Point3d endPoint = useXAxis
                    ? new Point3d(targetPoint.Value.X, dirPoint.Y, dirPoint.Z)
                    : new Point3d(dirPoint.X, targetPoint.Value.Y, dirPoint.Z);

                if (startPoint.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                if (!TryPromptDimPlacementPoint(
                    ed,
                    db,
                    startPoint,
                    endPoint,
                    useXAxis,
                    out Point3d dimPlacementPoint,
                    out bool finalUseXAxis))
                {
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startPoint,
                    XLine2Point = endPoint,
                    DimLinePoint = dimPlacementPoint,
                    Rotation = finalUseXAxis ? 0.0 : Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        [CommandMethod("SDXYSETTINGS")]
        public void ConfigureSmartDimXY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            SdxyTargetSettings settings = SdxyTargetSettingsStore.Load();

            if (PromptForSdxySettings(ed, db, ref settings))
            {
                ed.WriteMessage($"\nSDXY: đã lưu setting target. {BuildSdxySettingsSummary(settings)}");
            }
        }

        private bool TryPromptDimPlacementPoint(
            Editor ed,
            Database db,
            Point3d startPoint,
            Point3d endPoint,
            bool useXAxis,
            out Point3d dimPlacementPoint,
            out bool finalUseXAxis)
        {
            using (SmartDimPlacementJig jig =
                new SmartDimPlacementJig(db, startPoint, endPoint, useXAxis))
            {
                PromptResult dragResult = ed.Drag(jig);
                if (dragResult.Status == PromptStatus.OK || jig.AcceptedByShortcut)
                {
                    dimPlacementPoint = jig.DimLinePoint;
                    finalUseXAxis = jig.UseXAxis;
                    return true;
                }
            }

            dimPlacementPoint = Point3d.Origin;
            finalUseXAxis = useXAxis;
            return false;
        }

        private bool TryPromptAxisDirection(
            Editor ed,
            Point3d startPoint,
            string message,
            bool? forceXAxis,
            out Point3d pointResult,
            out double direction)
        {
            return TryPromptAxisDirection(
                ed,
                startPoint,
                message,
                forceXAxis,
                out pointResult,
                out direction,
                out _);
        }

        private bool TryPromptAxisDirection(
            Editor ed,
            Point3d startPoint,
            string message,
            bool? forceXAxis,
            out Point3d pointResult,
            out double direction,
            out bool useXAxis)
        {
            // Nếu forceXAxis có giá trị thì chỉ chấp nhận hướng theo đúng trục đó.
            // Nếu forceXAxis = null thì chọn trục có độ lệch lớn hơn giữa X và Y.
            while (true)
            {
                PromptStatus promptStatus;

                using (AxisDirectionPreviewJig jig =
                    new AxisDirectionPreviewJig(startPoint, message, forceXAxis))
                {
                    PromptResult dragResult = ed.Drag(jig);
                    promptStatus = dragResult.Status;
                    pointResult = jig.CurrentPoint;
                    useXAxis = jig.UseXAxis;
                }

                if (promptStatus != PromptStatus.OK)
                {
                    pointResult = startPoint;
                    direction = 0.0;
                    useXAxis = forceXAxis ?? true;
                    return false;
                }

                double deltaX = pointResult.X - startPoint.X;
                double deltaY = pointResult.Y - startPoint.Y;

                if (forceXAxis.HasValue)
                {
                    useXAxis = forceXAxis.Value;
                    double axisDelta = useXAxis ? deltaX : deltaY;
                    if (Math.Abs(axisDelta) < DirectionTolerance)
                    {
                        ed.WriteMessage(
                            useXAxis
                                ? "\nĐiểm hướng phải lệch theo trục X. Hãy chọn lại."
                                : "\nĐiểm hướng phải lệch theo trục Y. Hãy chọn lại.");
                        continue;
                    }

                    direction = axisDelta > 0.0 ? 1.0 : -1.0;
                    return true;
                }

                if (Math.Abs(deltaX) < DirectionTolerance &&
                    Math.Abs(deltaY) < DirectionTolerance)
                {
                    ed.WriteMessage("\nĐiểm hướng phải lệch theo X hoặc Y. Hãy chọn lại.");
                    continue;
                }

                direction = useXAxis
                    ? (deltaX >= 0.0 ? 1.0 : -1.0)
                    : (deltaY >= 0.0 ? 1.0 : -1.0);
                return true;
            }
        }

        private bool TryPromptSdxyStartPoint(
            Editor ed,
            Database db,
            ref SdxyTargetSettings settings,
            out Point3d startPoint)
        {
            startPoint = Point3d.Origin;

            while (true)
            {
                PromptPointOptions options =
                    new PromptPointOptions(
                        $"\nChọn điểm đầu dim hoặc [Settings] <{BuildSdxySettingsSummary(settings)}>:");
                options.AppendKeywordsToMessage = false;
                options.Keywords.Add("Settings");

                PromptPointResult result = ed.GetPoint(options);
                if (result.Status == PromptStatus.OK)
                {
                    startPoint = result.Value;
                    return true;
                }

                if (result.Status == PromptStatus.Keyword &&
                    string.Equals(result.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PromptForSdxySettings(ed, db, ref settings))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }

        private bool PromptForSdxySettings(
            Editor ed,
            Database db,
            ref SdxyTargetSettings settings)
        {
            if (ed == null || db == null)
            {
                return false;
            }

            List<SdxyEntityTypeChoice> availableTypes = GetAvailableSdxyEntityTypeChoices();
            while (true)
            {
                List<string> availableLayers = LoadSdxyLayerNames(db, settings);
                using (SdxySettingsForm form =
                    new SdxySettingsForm(availableTypes, availableLayers, settings))
                {
                    WF.DialogResult result = Application.ShowModalDialog(form);
                    if (form.PendingAction == SdxySettingsFormAction.PickSample)
                    {
                        settings = form.ResultSettings;
                    }
                    else if (result == WF.DialogResult.OK)
                    {
                        settings = form.ResultSettings;
                        SdxyTargetSettingsStore.Save(settings);
                        SdxyNamedFilterStore.SaveCurrentName(form.SelectedNamedFilterName);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (TryPromptSdxySampleDescriptor(ed, db, out SdxySampleDescriptor sampleDescriptor))
                {
                    settings.SampleDescriptors.Add(sampleDescriptor);
                    if (!settings.UseSampleType &&
                        !settings.UseSampleLayer &&
                        !settings.UseSampleLinetype &&
                        !settings.UseSampleColor &&
                        !settings.UseSampleBlockName)
                    {
                        settings.UseSampleType = true;
                        settings.UseSampleLayer = true;
                    }
                }
            }
        }

        private bool TryPromptSdxySampleDescriptor(
            Editor ed,
            Database db,
            out SdxySampleDescriptor sampleDescriptor)
        {
            sampleDescriptor = null;

            PromptEntityOptions options =
                new PromptEntityOptions("\nChọn đối tượng mẫu cho SDXY filter: ");
            PromptEntityResult result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                return false;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity entity = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Entity;
                if (entity == null)
                {
                    return false;
                }

                sampleDescriptor = BuildSdxySampleDescriptor(entity, tr);
                return sampleDescriptor != null;
            }
        }

        private SdxySampleDescriptor BuildSdxySampleDescriptor(Entity entity, Transaction tr)
        {
            if (entity == null)
            {
                return null;
            }

            string typeName = entity.GetType().FullName ?? entity.GetType().Name;
            string typeDisplayName = GetSdxyEntityDisplayName(entity.GetType());
            string layerName = entity.Layer ?? string.Empty;
            string linetypeName = entity.Linetype ?? string.Empty;
            string colorKey = BuildSdxyColorKey(entity.Color);
            string colorDisplayName = BuildSdxyColorDisplayName(entity.Color);
            string blockName = entity is BlockReference blockReference
                ? GetSdxyBlockName(blockReference, tr)
                : string.Empty;

            return new SdxySampleDescriptor(
                typeName,
                typeDisplayName,
                layerName,
                linetypeName,
                colorKey,
                colorDisplayName,
                blockName);
        }

        private List<string> LoadSdxyLayerNames(Database db, SdxyTargetSettings settings)
        {
            SortedSet<string> names =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable =
                    tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
                if (layerTable != null)
                {
                    foreach (ObjectId id in layerTable)
                    {
                        LayerTableRecord layer =
                            tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                        if (layer != null && !string.IsNullOrWhiteSpace(layer.Name))
                        {
                            names.Add(layer.Name);
                        }
                    }
                }
            }

            foreach (string layerName in settings?.AllowedLayers ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    names.Add(layerName);
                }
            }

            foreach (SdxySampleDescriptor sample in settings?.SampleDescriptors ?? Enumerable.Empty<SdxySampleDescriptor>())
            {
                if (!string.IsNullOrWhiteSpace(sample?.LayerName))
                {
                    names.Add(sample.LayerName);
                }
            }

            return names.ToList();
        }

        private List<SdxyEntityTypeChoice> GetAvailableSdxyEntityTypeChoices()
        {
            if (_cachedSdxyEntityTypeChoices != null)
            {
                return _cachedSdxyEntityTypeChoices;
            }

            HashSet<string> commonTypes = new HashSet<string>(
                new[]
                {
                    typeof(Line).FullName,
                    typeof(Autodesk.AutoCAD.DatabaseServices.Polyline).FullName,
                    typeof(Polyline2d).FullName,
                    typeof(Polyline3d).FullName,
                    typeof(Arc).FullName,
                    typeof(Circle).FullName,
                    typeof(Ellipse).FullName,
                    typeof(Spline).FullName,
                    typeof(BlockReference).FullName,
                    typeof(Dimension).FullName,
                    typeof(DBText).FullName,
                    typeof(MText).FullName,
                    typeof(Hatch).FullName,
                    typeof(Xline).FullName,
                    typeof(Ray).FullName,
                    typeof(Autodesk.AutoCAD.DatabaseServices.Region).FullName
                }
                .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);

            List<SdxyEntityTypeChoice> result = typeof(Entity).Assembly
                .GetTypes()
                .Where(type =>
                    type.IsClass &&
                    type.IsPublic &&
                    !type.IsGenericTypeDefinition &&
                    type != typeof(Entity) &&
                    typeof(Entity).IsAssignableFrom(type))
                .Select(type =>
                    new SdxyEntityTypeChoice(
                        type,
                        GetSdxyEntityDisplayName(type),
                        commonTypes.Contains(type.FullName ?? string.Empty)))
                .OrderByDescending(choice => choice.IsCommon)
                .ThenBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cachedSdxyEntityTypeChoices = result;
            return result;
        }

        private static string GetSdxyEntityDisplayName(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            string displayName = type.Name;
            if (type.IsAbstract)
            {
                displayName += " (family)";
            }

            return displayName;
        }

        private string BuildSdxySettingsSummary(SdxyTargetSettings settings)
        {
            if (settings == null)
            {
                return "All types | All layers | No sample";
            }

            int typeCount = settings.AllowedTypeNames.Count;
            int layerCount = settings.AllowedLayers.Count;
            string sampleSummary = "No sample";
            int sampleCount = settings.SampleDescriptors.Count;
            if (sampleCount > 0 &&
                (settings.UseSampleType ||
                 settings.UseSampleLayer ||
                 settings.UseSampleLinetype ||
                 settings.UseSampleColor ||
                 settings.UseSampleBlockName))
            {
                List<string> parts = new List<string>();
                if (settings.UseSampleType) parts.Add("Type");
                if (settings.UseSampleLayer) parts.Add("Layer");
                if (settings.UseSampleLinetype) parts.Add("Linetype");
                if (settings.UseSampleColor) parts.Add("Color");
                if (settings.UseSampleBlockName) parts.Add("Block");
                sampleSummary = $"Sample={sampleCount} obj ({string.Join("+", parts)})";
            }

            return
                $"Type={(typeCount == 0 ? "All" : typeCount.ToString())} | " +
                $"Layer={(layerCount == 0 ? "All" : layerCount.ToString())} | " +
                sampleSummary;
        }

        private bool IsSdxyTargetCandidate(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings)
        {
            if (entity == null || entity.IsErased)
            {
                return false;
            }

            if (!IsSdxyEntityVisible(entity, tr))
            {
                return false;
            }

            if (settings == null)
            {
                return entity is Curve && !(entity is Dimension);
            }

            if (!MatchesSdxyTypeFilters(entity, settings))
            {
                return false;
            }

            if (settings.AllowedLayers.Count > 0 &&
                !settings.AllowedLayers.Contains(entity.Layer ?? string.Empty))
            {
                return false;
            }

            return MatchesSdxySampleFilters(entity, tr, settings);
        }

        private bool MatchesSdxyTypeFilters(Entity entity, SdxyTargetSettings settings)
        {
            if (settings == null || settings.AllowedTypeNames.Count == 0)
            {
                return true;
            }

            Type entityType = entity.GetType();
            foreach (string typeName in settings.AllowedTypeNames)
            {
                Type targetType = ResolveSdxyEntityType(typeName);
                if (targetType != null && targetType.IsAssignableFrom(entityType))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesSdxySampleFilters(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings)
        {
            List<SdxySampleDescriptor> samples = settings?.SampleDescriptors
                ?.Where(sample => sample != null)
                .ToList()
                ?? new List<SdxySampleDescriptor>();
            if (samples.Count == 0)
            {
                return true;
            }

            return samples.Any(sample => MatchesSingleSdxySampleFilter(entity, tr, settings, sample));
        }

        private bool MatchesSingleSdxySampleFilter(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            SdxySampleDescriptor sample)
        {
            if (sample == null)
            {
                return true;
            }

            if (settings.UseSampleType)
            {
                Type sampleType = ResolveSdxyEntityType(sample.TypeName);
                if (sampleType == null || !sampleType.IsAssignableFrom(entity.GetType()))
                {
                    return false;
                }
            }

            if (settings.UseSampleLayer &&
                !string.Equals(entity.Layer, sample.LayerName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleLinetype &&
                !string.Equals(entity.Linetype, sample.LinetypeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleColor &&
                !string.Equals(
                    BuildSdxyColorKey(entity.Color),
                    sample.ColorKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleBlockName)
            {
                if (!(entity is BlockReference blockReference))
                {
                    return false;
                }

                string blockName = GetSdxyBlockName(blockReference, tr);
                if (!string.Equals(blockName, sample.BlockName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSdxyEntityVisible(Entity entity, Transaction tr)
        {
            try
            {
                if (!entity.Visible)
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                LayerTableRecord layer =
                    tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer != null && (layer.IsOff || layer.IsFrozen))
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private Type ResolveSdxyEntityType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return typeof(Entity).Assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
        }

        private string BuildSdxyColorKey(Autodesk.AutoCAD.Colors.Color color)
        {
            if (color == null)
            {
                return string.Empty;
            }

            switch (color.ColorMethod)
            {
                case Autodesk.AutoCAD.Colors.ColorMethod.ByLayer:
                    return "ByLayer";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByBlock:
                    return "ByBlock";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByAci:
                    return "ACI:" + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
                case Autodesk.AutoCAD.Colors.ColorMethod.ByColor:
                    return "RGB:" +
                        color.Red.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Green.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Blue.ToString(CultureInfo.InvariantCulture);
                default:
                    return color.ColorMethod + ":" + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
            }
        }

        private string BuildSdxyColorDisplayName(Autodesk.AutoCAD.Colors.Color color)
        {
            if (color == null)
            {
                return string.Empty;
            }

            switch (color.ColorMethod)
            {
                case Autodesk.AutoCAD.Colors.ColorMethod.ByLayer:
                    return "ByLayer";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByBlock:
                    return "ByBlock";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByAci:
                    return "ACI " + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
                case Autodesk.AutoCAD.Colors.ColorMethod.ByColor:
                    return "RGB " +
                        color.Red.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Green.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Blue.ToString(CultureInfo.InvariantCulture);
                default:
                    return color.ColorMethod.ToString();
            }
        }

        private string GetSdxyBlockName(BlockReference blockReference, Transaction tr)
        {
            if (blockReference == null)
            {
                return string.Empty;
            }

            try
            {
                ObjectId blockId = blockReference.DynamicBlockTableRecord;
                if (blockId.IsNull)
                {
                    blockId = blockReference.BlockTableRecord;
                }

                BlockTableRecord block =
                    tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;
                return block?.Name ?? string.Empty;
            }
            catch
            {
                try
                {
                    return blockReference.Name ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private Point3d? FindNearestPointOnXAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                startPoint,
                direction,
                useXAxis: true,
                settings: null);
        }

        private Point3d? FindNearestPointOnXAxisFromProbe(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction,
            SdxyTargetSettings settings)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                probePoint,
                direction,
                useXAxis: true,
                settings: settings);
        }

        private Point3d? FindNearestPointOnYAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                startPoint,
                direction,
                useXAxis: false,
                settings: null);
        }

        private Point3d? FindNearestPointOnYAxisFromProbe(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction,
            SdxyTargetSettings settings)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                probePoint,
                direction,
                useXAxis: false,
                settings: settings);
        }

        private Point3d? FindNearestPointOnAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction,
            bool useXAxis,
            SdxyTargetSettings settings)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = useXAxis
                ? CreateScanLine(probePoint, direction)
                : CreateVerticalScanLine(probePoint, direction))
            {
                foreach (ObjectId id in GetScanCandidateIds(
                    ed,
                    currentSpace,
                    probePoint,
                    useXAxis,
                    direction,
                    settings))
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!IsSdxyTargetCandidate(entity, tr, settings))
                    {
                        continue;
                    }

                    bool fastPass = useXAxis
                        ? IsHorizontalRayCandidate(
                            entity,
                            probePoint.Y,
                            startPoint.X,
                            probePoint.X,
                            direction,
                            bestDistance)
                        : IsVerticalRayCandidate(
                            entity,
                            probePoint.X,
                            startPoint.Y,
                            probePoint.Y,
                            direction,
                            bestDistance);
                    if (!fastPass)
                    {
                        continue;
                    }

                    Point3dCollection intersections =
                        TryGetIntersections(entity, scanLine, useXAxis);
                    if (intersections == null || intersections.Count == 0)
                    {
                        continue;
                    }

                    foreach (Point3d point in intersections)
                    {
                        double projectedFromStart = useXAxis
                            ? (point.X - startPoint.X) * direction
                            : (point.Y - startPoint.Y) * direction;
                        double projectedFromProbe = useXAxis
                            ? (point.X - probePoint.X) * direction
                            : (point.Y - probePoint.Y) * direction;

                        if (projectedFromStart <= DirectionTolerance)
                        {
                            continue;
                        }

                        if (projectedFromProbe < -DirectionTolerance)
                        {
                            continue;
                        }

                        double rankDistance = Math.Max(0.0, projectedFromProbe);
                        if (rankDistance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = rankDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private bool IsHorizontalRayCandidate(
            Entity entity,
            double scanY,
            double startX,
            double probeX,
            double direction,
            double bestDistance)
        {
            // Lọc nhanh bằng GeometricExtents trước khi gọi IntersectWith.
            // Đây là phần giúp SDXY nhanh hơn khi bản vẽ có nhiều object.
            if (!TryGetEntityExtents(entity, out Extents3d extents))
            {
                return true;
            }

            if (scanY < extents.MinPoint.Y - DirectionTolerance ||
                scanY > extents.MaxPoint.Y + DirectionTolerance)
            {
                return false;
            }

            if (direction > 0.0)
            {
                if (extents.MaxPoint.X <= startX + DirectionTolerance ||
                    extents.MaxPoint.X < probeX - DirectionTolerance)
                {
                    return false;
                }

                double minRankDistance = extents.MinPoint.X > probeX
                    ? extents.MinPoint.X - probeX
                    : 0.0;
                return minRankDistance < bestDistance;
            }

            if (extents.MinPoint.X >= startX - DirectionTolerance ||
                extents.MinPoint.X > probeX + DirectionTolerance)
            {
                return false;
            }

            double minNegativeRankDistance = extents.MaxPoint.X < probeX
                ? probeX - extents.MaxPoint.X
                : 0.0;
            return minNegativeRankDistance < bestDistance;
        }

        private bool IsVerticalRayCandidate(
            Entity entity,
            double scanX,
            double startY,
            double probeY,
            double direction,
            double bestDistance)
        {
            if (!TryGetEntityExtents(entity, out Extents3d extents))
            {
                return true;
            }

            if (scanX < extents.MinPoint.X - DirectionTolerance ||
                scanX > extents.MaxPoint.X + DirectionTolerance)
            {
                return false;
            }

            if (direction > 0.0)
            {
                if (extents.MaxPoint.Y <= startY + DirectionTolerance ||
                    extents.MaxPoint.Y < probeY - DirectionTolerance)
                {
                    return false;
                }

                double minRankDistance = extents.MinPoint.Y > probeY
                    ? extents.MinPoint.Y - probeY
                    : 0.0;
                return minRankDistance < bestDistance;
            }

            if (extents.MinPoint.Y >= startY - DirectionTolerance ||
                extents.MinPoint.Y > probeY + DirectionTolerance)
            {
                return false;
            }

            double minNegativeRankDistance = extents.MaxPoint.Y < probeY
                ? probeY - extents.MaxPoint.Y
                : 0.0;
            return minNegativeRankDistance < bestDistance;
        }

        private IEnumerable<ObjectId> GetScanCandidateIds(
            Editor ed,
            BlockTableRecord currentSpace,
            Point3d scanStartPoint,
            bool useXAxis,
            double direction,
            SdxyTargetSettings settings)
        {
            ObjectId[] selectionIds = TrySelectFenceCandidates(
                ed,
                scanStartPoint,
                useXAxis,
                direction,
                settings);
            if (selectionIds != null)
            {
                return selectionIds;
            }

            return EnumerateEntityIds(currentSpace, settings);
        }

        private ObjectId[] TrySelectFenceCandidates(
            Editor ed,
            Point3d scanStartPoint,
            bool useXAxis,
            double direction,
            SdxyTargetSettings settings)
        {
            if (ed == null)
            {
                return null;
            }

            Point3d fenceEnd = useXAxis
                ? new Point3d(
                    scanStartPoint.X + SearchDistance * direction,
                    scanStartPoint.Y,
                    scanStartPoint.Z)
                : new Point3d(
                    scanStartPoint.X,
                    scanStartPoint.Y + SearchDistance * direction,
                    scanStartPoint.Z);

            try
            {
                using (Point3dCollection fencePoints = new Point3dCollection())
                {
                    fencePoints.Add(scanStartPoint);
                    fencePoints.Add(fenceEnd);

                    PromptSelectionResult result = ed.SelectFence(fencePoints);
                    if (result.Status == PromptStatus.OK && result.Value != null)
                    {
                        return result.Value
                            .GetObjectIds()
                            .Where(id => IsScanCandidateId(id, settings))
                            .ToArray();
                    }

                    if (result.Status == PromptStatus.None)
                    {
                        return Array.Empty<ObjectId>();
                    }
                }
            }
            catch
            {
                // Nếu engine selection không trả được kết quả ổn định trong một số
                // bản vẽ đặc biệt thì fallback về cách duyệt cũ.
            }

            return null;
        }

        private IEnumerable<ObjectId> EnumerateEntityIds(
            BlockTableRecord currentSpace,
            SdxyTargetSettings settings)
        {
            foreach (ObjectId id in currentSpace)
            {
                if (IsScanCandidateId(id, settings))
                {
                    yield return id;
                }
            }
        }

        private bool IsScanCandidateId(ObjectId id, SdxyTargetSettings settings)
        {
            RXClass objectClass = id.ObjectClass;
            if (objectClass == null)
            {
                return false;
            }

            if (settings == null)
            {
                if (DimensionRxClass != null && objectClass.IsDerivedFrom(DimensionRxClass))
                {
                    return false;
                }

                return CurveRxClass == null || objectClass.IsDerivedFrom(CurveRxClass);
            }

            return EntityRxClass == null || objectClass.IsDerivedFrom(EntityRxClass);
        }

        private bool TryGetEntityExtents(Entity entity, out Extents3d extents)
        {
            try
            {
                if (entity == null || entity.IsErased)
                {
                    extents = default;
                    return false;
                }

                extents = entity.GeometricExtents;
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                extents = default;
                return false;
            }
        }

        private Point3dCollection TryGetIntersections(
            Entity entity,
            Line scanLine,
            bool useXAxis)
        {
            // IntersectWith có thể lỗi với vài entity đặc biệt.
            // Bắt lỗi ở đây để lệnh bỏ qua object đó thay vì văng command.
            try
            {
                Point3dCollection intersections = new Point3dCollection();
                entity.IntersectWith(
                    scanLine,
                    Intersect.OnBothOperands,
                    intersections,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (intersections.Count > 0)
                {
                    return intersections;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }

            return BuildExtentsFallbackIntersections(entity, scanLine, useXAxis);
        }

        private Point3dCollection BuildExtentsFallbackIntersections(
            Entity entity,
            Line scanLine,
            bool useXAxis)
        {
            if (!TryGetEntityExtents(entity, out Extents3d extents))
            {
                return null;
            }

            Point3dCollection points = new Point3dCollection();
            if (useXAxis)
            {
                double scanY = scanLine.StartPoint.Y;
                if (scanY < extents.MinPoint.Y - DirectionTolerance ||
                    scanY > extents.MaxPoint.Y + DirectionTolerance)
                {
                    return points;
                }

                AddIntersectionPoint(points, new Point3d(extents.MinPoint.X, scanY, scanLine.StartPoint.Z));
                AddIntersectionPoint(points, new Point3d(extents.MaxPoint.X, scanY, scanLine.StartPoint.Z));
                return points;
            }

            double scanX = scanLine.StartPoint.X;
            if (scanX < extents.MinPoint.X - DirectionTolerance ||
                scanX > extents.MaxPoint.X + DirectionTolerance)
            {
                return points;
            }

            AddIntersectionPoint(points, new Point3d(scanX, extents.MinPoint.Y, scanLine.StartPoint.Z));
            AddIntersectionPoint(points, new Point3d(scanX, extents.MaxPoint.Y, scanLine.StartPoint.Z));
            return points;
        }

        private void AddIntersectionPoint(Point3dCollection points, Point3d candidate)
        {
            foreach (Point3d existing in points)
            {
                if (existing.DistanceTo(candidate) <= DirectionTolerance)
                {
                    return;
                }
            }

            points.Add(candidate);
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

        private sealed class AxisDirectionPreviewJig : DrawJig, IDisposable
        {
            private readonly Point3d _startPoint;
            private readonly string _message;
            private readonly bool? _forceXAxis;
            private Point3d _currentPoint;
            private bool _invertAxisChoice;
            private bool _shiftWasPressed;

            public AxisDirectionPreviewJig(
                Point3d startPoint,
                string message,
                bool? forceXAxis)
            {
                _startPoint = startPoint;
                _message = message;
                _forceXAxis = forceXAxis;
                _currentPoint = startPoint;
                _invertAxisChoice = false;
                _shiftWasPressed = false;
            }

            public Point3d CurrentPoint => _currentPoint;

            public bool UseXAxis => ResolveUseXAxis(_currentPoint);

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions pointOptions =
                    new JigPromptPointOptions(_message);
                // Không dùng BasePoint để điểm thứ 2 của SDXY / SmartDimX / SmartDimY
                // không bị ORTHOMODE ép theo ngang/dọc.
                pointOptions.UserInputControls =
                    UserInputControls.Accept3dCoordinates |
                    UserInputControls.NoZDirectionOrtho;

                PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
                if (pointResult.Status == PromptStatus.Cancel)
                {
                    return SamplerStatus.Cancel;
                }

                if (pointResult.Status != PromptStatus.OK)
                {
                    return SamplerStatus.NoChange;
                }

                bool shiftPressed = IsShiftPressedForDimJig();
                bool axisToggled =
                    !_forceXAxis.HasValue &&
                    shiftPressed &&
                    !_shiftWasPressed;
                _shiftWasPressed = shiftPressed;

                if (_currentPoint.DistanceTo(pointResult.Value) <= PreviewPointTolerance &&
                    !axisToggled)
                {
                    return SamplerStatus.NoChange;
                }

                if (axisToggled)
                {
                    _invertAxisChoice = !_invertAxisChoice;
                }

                _currentPoint = pointResult.Value;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                Point3d previewPoint = GetPreviewPoint();
                if (_startPoint.DistanceTo(previewPoint) <= DirectionTolerance)
                {
                    return true;
                }

                draw.Geometry.WorldLine(_startPoint, previewPoint);
                return true;
            }

            private Point3d GetPreviewPoint()
            {
                bool useXAxis = ResolveUseXAxis(_currentPoint);
                if (useXAxis)
                {
                    return new Point3d(_currentPoint.X, _startPoint.Y, _startPoint.Z);
                }

                return new Point3d(_startPoint.X, _currentPoint.Y, _startPoint.Z);
            }

            private bool ResolveUseXAxis(Point3d point)
            {
                if (_forceXAxis.HasValue)
                {
                    return _forceXAxis.Value;
                }

                double deltaX = point.X - _startPoint.X;
                double deltaY = point.Y - _startPoint.Y;
                bool useXAxis = Math.Abs(deltaX) >= Math.Abs(deltaY);
                return _invertAxisChoice ? !useXAxis : useXAxis;
            }

            public void Dispose()
            {
            }
        }

        private static bool IsShiftPressedForDimJig()
        {
            const int ShiftVirtualKey = 0x10;

            if ((GetAsyncKeyState(ShiftVirtualKey) & 0x8000) != 0)
            {
                return true;
            }

            return (WF.Control.ModifierKeys & WF.Keys.Shift) == WF.Keys.Shift;
        }

        private sealed class SmartDimPlacementJig : DrawJig, IDisposable
        {
            private readonly RotatedDimension _previewDimension;
            private readonly Point3d _defaultPoint;
            private readonly bool _originalUseXAxis;
            private readonly double _minX;
            private readonly double _maxX;
            private readonly double _minY;
            private readonly double _maxY;
            private readonly double _switchMargin;
            private Point3d _currentPoint;
            private bool _useXAxis;
            private bool _acceptedByShortcut;

            public SmartDimPlacementJig(
                Database db,
                Point3d startPoint,
                Point3d endPoint,
                bool useXAxis)
            {
                double previewOffset = Math.Max(
                    db.Dimtxt + db.Dimgap + db.Dimexe,
                    10.0);

                _originalUseXAxis = useXAxis;
                _minX = Math.Min(startPoint.X, endPoint.X);
                _maxX = Math.Max(startPoint.X, endPoint.X);
                _minY = Math.Min(startPoint.Y, endPoint.Y);
                _maxY = Math.Max(startPoint.Y, endPoint.Y);
                _switchMargin = previewOffset;

                _defaultPoint = useXAxis
                    ? new Point3d(
                        (startPoint.X + endPoint.X) * 0.5,
                        startPoint.Y + previewOffset,
                        startPoint.Z)
                    : new Point3d(
                        startPoint.X + previewOffset,
                        (startPoint.Y + endPoint.Y) * 0.5,
                        startPoint.Z);

                _useXAxis = useXAxis;
                _currentPoint = _defaultPoint;

                _previewDimension = new RotatedDimension
                {
                    XLine1Point = startPoint,
                    XLine2Point = endPoint,
                    DimLinePoint = _currentPoint,
                    Rotation = _useXAxis ? 0.0 : Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle
                };
                _previewDimension.SetDatabaseDefaults(db);
            }

            public Point3d DimLinePoint => _currentPoint;

            public bool UseXAxis => _useXAxis;

            public bool AcceptedByShortcut => _acceptedByShortcut;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions pointOptions =
                    new JigPromptPointOptions(
                        "\nChọn điểm đặt dim (mặc định như cũ, kéo ra ngoài 2 đầu để đổi hướng): ");
                // Không dùng BasePoint ở bước này để preview DIM không bị
                // ORTHOMODE của AutoCAD ép theo ngang/dọc.
                pointOptions.UserInputControls =
                    UserInputControls.Accept3dCoordinates |
                    UserInputControls.NullResponseAccepted;

                PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
                if (pointResult.Status == PromptStatus.None)
                {
                    // Space/Enter: chốt luôn tại điểm preview hiện tại.
                    // Nếu người dùng chưa rê chuột thì _currentPoint vẫn là điểm auto cũ.
                    _acceptedByShortcut = true;
                    return SamplerStatus.Cancel;
                }

                if (pointResult.Status == PromptStatus.Cancel)
                {
                    return SamplerStatus.Cancel;
                }

                if (pointResult.Status != PromptStatus.OK)
                {
                    return SamplerStatus.NoChange;
                }

                if (_currentPoint.DistanceTo(pointResult.Value) <= PreviewPointTolerance)
                {
                    return SamplerStatus.NoChange;
                }

                _useXAxis = ResolveUseXAxis(pointResult.Value);
                _currentPoint = pointResult.Value;
                _previewDimension.DimLinePoint = _currentPoint;
                _previewDimension.Rotation = _useXAxis ? 0.0 : Math.PI / 2.0;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                return _previewDimension.WorldDraw(draw);
            }

            private bool ResolveUseXAxis(Point3d point)
            {
                if (_originalUseXAxis)
                {
                    bool switchedToVertical =
                        point.X < _minX - _switchMargin ||
                        point.X > _maxX + _switchMargin;
                    return !switchedToVertical;
                }

                bool switchedToHorizontal =
                    point.Y < _minY - _switchMargin ||
                    point.Y > _maxY + _switchMargin;
                return switchedToHorizontal;
            }

            public void Dispose()
            {
                _previewDimension?.Dispose();
            }
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

    // ======================================================
    // TT_TEXT_CHANGE_5
    // Mục đích: lấy nội dung text gốc và thay nội dung cho các text height = 5 trong vùng chọn.
    // Lưu ý: chỉ đổi nội dung, không đổi layer/style/height/rotation.
    // Có hỗ trợ PickFirst để dùng FILTER trước rồi gọi lệnh.
    // ======================================================
    public class TextSyncCommands
    {
        private const double TargetTextHeight = 5.0;
        private const double TextHeightTolerance = 1e-6;

        // Flow:
        // 1. Chọn text gốc.
        // 2. Quét vùng text đích hoặc dùng PickFirst.
        // 3. Lọc DBText/MText có height = 5 rồi thay nội dung.
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
                    PromptSelectionResult selectionResult =
                        PromptForSelection(ed, selectionOptions.MessageForAdding);
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

        private static PromptSelectionResult PromptForSelection(Editor ed, string message)
        {
            while (true)
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = message
                };

                PromptSelectionResult result = ed.GetSelection(options);
                if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
                {
                    return result;
                }

                if (result.Status == PromptStatus.Cancel)
                {
                    return result;
                }

                ed.WriteMessage("\nChưa chọn được đối tượng hợp lệ, hãy chọn lại.");
            }
        }

        private static TextSyncPayload GetTextSyncPayload(Entity entity)
        {
            // DBText dùng TextString, MText dùng Contents.
            // Payload giữ cả plain text và MText content để hạn chế mất format MText.
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
            // Chỉ xử lý text chọn trực tiếp.
            // Text nằm trong block không được bóc ra ở lệnh này.
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

    // ======================================================
    // CCC / BBB - COPY VÀO TÂM VÙNG
    // CCC: chọn object/nhóm object nguồn, click nhiều vùng kín, copy nguồn vào tâm từng vùng.
    // BBB: chọn block definition từ bảng block, click nhiều vùng kín, insert block vào tâm từng vùng.
    // Lưu ý quan trọng:
    // - Khi tính tâm nguồn của CCC, có lọc bỏ Dimension/Text/Attribute khỏi extents.
    // - Khi copy vẫn copy nguyên selection, không xóa text/dim khỏi kết quả.
    // ======================================================
    public class SmartCopyToCenterCommands
    {
        private const double BoundarySearchDistance = 1000000.0;
        private const double BoundaryTolerance = 1e-6;

        // CCC_SMART_COPY_TO_CENTER:
        // - Hỗ trợ PickFirst để chọn nguồn trước.
        // - Click nhiều vùng đích liên tiếp, Enter để kết thúc.
        // - Tâm vùng ưu tiên nhánh quét nhanh 4 hướng, fallback TraceBoundary khi cần.
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
                    PromptSelectionResult sourceSelectionResult =
                        PromptForSelection(ed, sourceSelectionOptions.MessageForAdding);
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

                Point3d sourceCenter;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        Extents3d sourceExtents = GetSelectionExtents(
                            sourceIds,
                            tr,
                            ignoreDimensions: true,
                            ignoreTextEntities: true);
                        sourceCenter = GetCenter(sourceExtents);
                    }
                    catch (InvalidOperationException)
                    {
                        ed.WriteMessage("\nCCC_SMART_COPY_TO_CENTER: không lấy được tâm hợp lệ từ selection nguồn.");
                        return;
                    }
                }

                int copiedZoneCount = 0;
                int totalCopiedEntities = 0;

                while (true)
                {
                    PromptPointOptions seedPointOptions =
                        new PromptPointOptions(
                            copiedZoneCount == 0
                                ? "\nChọn điểm nằm trong vùng đích: "
                                : "\nChọn điểm nằm trong vùng đích tiếp theo hoặc Enter để kết thúc: ");
                    seedPointOptions.AllowNone = copiedZoneCount > 0;

                    PromptPointResult seedPointResult = ed.GetPoint(seedPointOptions);
                    if (seedPointResult.Status == PromptStatus.None)
                    {
                        break;
                    }

                    if (seedPointResult.Status != PromptStatus.OK)
                    {
                        return;
                    }

                    int copiedCount = CopySourceToBoundaryCenter(
                        db,
                        ed,
                        sourceIds,
                        sourceCenter,
                        seedPointResult.Value);

                    if (copiedCount <= 0)
                    {
                        continue;
                    }

                    copiedZoneCount++;
                    totalCopiedEntities += copiedCount;

                    ed.WriteMessage(
                        $"\nCCC_SMART_COPY_TO_CENTER: đã copy {copiedCount} đối tượng vào vùng thứ {copiedZoneCount}.");
                }

                if (copiedZoneCount > 1)
                {
                    ed.WriteMessage(
                        $"\nCCC_SMART_COPY_TO_CENTER: hoàn tất {copiedZoneCount} vùng, tổng cộng {totalCopiedEntities} đối tượng đã được copy.");
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

        // BBB_BLOCK_TO_CENTER:
        // - Không cần có sẵn block reference trong bản vẽ.
        // - Mở form chọn block definition hiện có trong drawing.
        // - Insert block vào tâm từng vùng người dùng click.
        [CommandMethod("BBB_BLOCK_TO_CENTER")]
        public void BlockToCenter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            ObjectId blockDefinitionId = PromptForBlockDefinition(db);
            if (blockDefinitionId.IsNull)
            {
                return;
            }

            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                Point3d blockCenterInDefinition;
                Point3d blockOriginInDefinition;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        GetBlockDefinitionPlacementData(
                            blockDefinitionId,
                            tr,
                            out blockCenterInDefinition,
                            out blockOriginInDefinition);
                    }
                    catch (InvalidOperationException)
                    {
                        ed.WriteMessage("\nBBB_BLOCK_TO_CENTER: không lấy được dữ liệu hợp lệ từ block đã chọn.");
                        return;
                    }
                }

                int copiedZoneCount = 0;
                int insertedBlockCount = 0;
                while (true)
                {
                    PromptPointOptions seedPointOptions =
                        new PromptPointOptions(
                            copiedZoneCount == 0
                                ? "\nChọn điểm nằm trong vùng đích: "
                                : "\nChọn điểm nằm trong vùng đích tiếp theo hoặc Enter để kết thúc: ");
                    seedPointOptions.AllowNone = copiedZoneCount > 0;

                    PromptPointResult seedPointResult = ed.GetPoint(seedPointOptions);
                    if (seedPointResult.Status == PromptStatus.None)
                    {
                        break;
                    }

                    if (seedPointResult.Status != PromptStatus.OK)
                    {
                        return;
                    }

                    int insertedCount = InsertBlockDefinitionToBoundaryCenter(
                        db,
                        ed,
                        blockDefinitionId,
                        blockCenterInDefinition,
                        blockOriginInDefinition,
                        seedPointResult.Value);

                    if (insertedCount <= 0)
                    {
                        continue;
                    }

                    copiedZoneCount++;
                    insertedBlockCount += insertedCount;
                    ed.WriteMessage(
                        $"\nBBB_BLOCK_TO_CENTER: đã chèn block vào vùng thứ {copiedZoneCount}.");
                }

                if (copiedZoneCount > 1)
                {
                    ed.WriteMessage(
                        $"\nBBB_BLOCK_TO_CENTER: hoàn tất {copiedZoneCount} vùng, tổng cộng {insertedBlockCount} block đã được chèn.");
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

        private static int InsertBlockDefinitionToBoundaryCenter(
            Database db,
            Editor ed,
            ObjectId blockDefinitionId,
            Point3d blockCenterInDefinition,
            Point3d blockOriginInDefinition,
            Point3d seedPoint)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!TryResolveBoundaryCenter(
                    db,
                    ed,
                    tr,
                    seedPoint,
                    "BBB_BLOCK_TO_CENTER",
                    out Point3d targetCenter))
                {
                    return 0;
                }

                Vector3d centerOffset = blockCenterInDefinition - blockOriginInDefinition;
                Point3d insertionPoint = targetCenter - centerOffset;

                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return 0;
                }

                BlockReference blockReference =
                    new BlockReference(insertionPoint, blockDefinitionId);
                currentSpace.AppendEntity(blockReference);
                tr.AddNewlyCreatedDBObject(blockReference, true);

                AppendBlockAttributes(blockReference, tr);

                tr.Commit();
                return 1;
            }
        }

        private static int CopySourceToBoundaryCenter(
            Database db,
            Editor ed,
            ObjectId[] sourceIds,
            Point3d sourceCenter,
            Point3d seedPoint)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!TryResolveBoundaryCenter(
                    db,
                    ed,
                    tr,
                    seedPoint,
                    "CCC_SMART_COPY_TO_CENTER",
                    out Point3d targetCenter))
                {
                    return 0;
                }

                Vector3d displacement = targetCenter - sourceCenter;

                ObjectId currentSpaceId = db.CurrentSpaceId;
                ObjectIdCollection sourceIdCollection = new ObjectIdCollection(sourceIds);
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
                return copiedCount;
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

        private static PromptSelectionResult PromptForSelection(Editor ed, string message)
        {
            while (true)
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = message
                };

                PromptSelectionResult result = ed.GetSelection(options);
                if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
                {
                    return result;
                }

                if (result.Status == PromptStatus.Cancel)
                {
                    return result;
                }

                ed.WriteMessage("\nChưa chọn được đối tượng hợp lệ, hãy chọn lại.");
            }
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

        private static bool TryResolveBoundaryCenter(
            Database db,
            Editor ed,
            Transaction tr,
            Point3d seedPoint,
            string commandLabel,
            out Point3d targetCenter)
        {
            // Lõi tìm tâm vùng cho CCC/BBB.
            // Nhánh fast dùng 4 tia gần nhất để tránh TraceBoundary quá nặng ở bản vẽ phức tạp.
            // Nếu nhánh fast không đủ dữ liệu thì fallback TraceBoundary của AutoCAD.
            targetCenter = Point3d.Origin;

            if (TryEstimateBoundaryCenterFast(
                db,
                tr,
                seedPoint,
                out targetCenter))
            {
                return true;
            }

            if (TryTraceBoundaryCenter(ed, seedPoint, out targetCenter, out string failureMessage))
            {
                return true;
            }

            ed.WriteMessage($"\n{commandLabel}: {failureMessage}");
            return false;
        }

        private static bool TryTraceBoundaryCenter(
            Editor ed,
            Point3d seedPoint,
            out Point3d targetCenter,
            out string failureMessage)
        {
            targetCenter = Point3d.Origin;
            failureMessage = "không tìm được vùng bao quanh điểm đã chọn.";

            DBObjectCollection boundaries = ed.TraceBoundary(seedPoint, false);
            if (boundaries == null || boundaries.Count == 0)
            {
                return false;
            }

            using (boundaries)
            {
                Curve boundaryCurve = FindBestBoundaryCurve(boundaries, seedPoint);
                if (boundaryCurve == null)
                {
                    failureMessage = "không xác định được đường bao kín hợp lệ.";
                    return false;
                }

                using (boundaryCurve)
                {
                    targetCenter = GetBoundaryCenter(boundaryCurve);
                    return true;
                }
            }
        }

        private static bool TryEstimateBoundaryCenterFast(
            Database db,
            Transaction tr,
            Point3d seedPoint,
            out Point3d targetCenter)
        {
            // Cách nhanh để lấy tâm vùng:
            // - Bắn 4 tia trái/phải/trên/dưới từ điểm click.
            // - Chỉ xét object đang hiển thị, bỏ layer Off/Frozen.
            // - Lấy 4 biên gần nhất rồi tính tâm từ chúng.
            targetCenter = Point3d.Origin;

            BlockTableRecord currentSpace =
                tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
            if (currentSpace == null)
            {
                return false;
            }

            Point3d? leftPoint = FindNearestPointOnXAxis(currentSpace, tr, seedPoint, -1.0);
            Point3d? rightPoint = FindNearestPointOnXAxis(currentSpace, tr, seedPoint, 1.0);
            Point3d? bottomPoint = FindNearestPointOnYAxis(currentSpace, tr, seedPoint, -1.0);
            Point3d? topPoint = FindNearestPointOnYAxis(currentSpace, tr, seedPoint, 1.0);

            if (!leftPoint.HasValue ||
                !rightPoint.HasValue ||
                !bottomPoint.HasValue ||
                !topPoint.HasValue)
            {
                return false;
            }

            double minX = leftPoint.Value.X;
            double maxX = rightPoint.Value.X;
            double minY = bottomPoint.Value.Y;
            double maxY = topPoint.Value.Y;

            if (maxX - minX <= BoundaryTolerance || maxY - minY <= BoundaryTolerance)
            {
                return false;
            }

            if (seedPoint.X <= minX + BoundaryTolerance ||
                seedPoint.X >= maxX - BoundaryTolerance ||
                seedPoint.Y <= minY + BoundaryTolerance ||
                seedPoint.Y >= maxY - BoundaryTolerance)
            {
                return false;
            }

            targetCenter = new Point3d(
                (minX + maxX) * 0.5,
                (minY + maxY) * 0.5,
                seedPoint.Z);
            return true;
        }

        private static Point3d? FindNearestPointOnXAxis(
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
                    if (!IsAxisScanCandidate(entity, tr, startPoint, useXAxis: true, direction))
                    {
                        continue;
                    }

                    Point3dCollection intersections = TryGetIntersections(entity as Curve, scanLine);
                    if (intersections == null || intersections.Count == 0)
                    {
                        continue;
                    }

                    foreach (Point3d point in intersections)
                    {
                        double projectedDistance = (point.X - startPoint.X) * direction;
                        if (projectedDistance <= BoundaryTolerance || projectedDistance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = projectedDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private static Point3d? FindNearestPointOnYAxis(
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
                    if (!IsAxisScanCandidate(entity, tr, startPoint, useXAxis: false, direction))
                    {
                        continue;
                    }

                    Point3dCollection intersections = TryGetIntersections(entity as Curve, scanLine);
                    if (intersections == null || intersections.Count == 0)
                    {
                        continue;
                    }

                    foreach (Point3d point in intersections)
                    {
                        double projectedDistance = (point.Y - startPoint.Y) * direction;
                        if (projectedDistance <= BoundaryTolerance || projectedDistance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = projectedDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private static bool IsAxisScanCandidate(
            Entity entity,
            Transaction tr,
            Point3d startPoint,
            bool useXAxis,
            double direction)
        {
            if (entity == null ||
                entity.IsErased ||
                entity is Dimension ||
                !(entity is Curve) ||
                !IsEntityDisplayed(entity, tr))
            {
                return false;
            }

            try
            {
                Extents3d extents = entity.GeometricExtents;
                if (useXAxis)
                {
                    if (extents.MinPoint.Y > startPoint.Y + BoundaryTolerance ||
                        extents.MaxPoint.Y < startPoint.Y - BoundaryTolerance)
                    {
                        return false;
                    }

                    return direction > 0.0
                        ? extents.MaxPoint.X > startPoint.X + BoundaryTolerance
                        : extents.MinPoint.X < startPoint.X - BoundaryTolerance;
                }

                if (extents.MinPoint.X > startPoint.X + BoundaryTolerance ||
                    extents.MaxPoint.X < startPoint.X - BoundaryTolerance)
                {
                    return false;
                }

                return direction > 0.0
                    ? extents.MaxPoint.Y > startPoint.Y + BoundaryTolerance
                    : extents.MinPoint.Y < startPoint.Y - BoundaryTolerance;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEntityDisplayed(Entity entity, Transaction tr)
        {
            try
            {
                if (!entity.Visible)
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                LayerTableRecord layer =
                    tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer == null)
                {
                    return true;
                }

                return !layer.IsOff && !layer.IsFrozen;
            }
            catch
            {
                return true;
            }
        }

        private static Point3dCollection TryGetIntersections(Curve curve, Line scanLine)
        {
            if (curve == null)
            {
                return null;
            }

            try
            {
                Point3dCollection intersections = new Point3dCollection();
                curve.IntersectWith(
                    scanLine,
                    Intersect.OnBothOperands,
                    intersections,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return intersections;
            }
            catch
            {
                return null;
            }
        }

        private static Line CreateScanLine(Point3d startPoint, double direction)
        {
            return new Line(
                startPoint,
                new Point3d(
                    startPoint.X + BoundarySearchDistance * direction,
                    startPoint.Y,
                    startPoint.Z));
        }

        private static Line CreateVerticalScanLine(Point3d startPoint, double direction)
        {
            return new Line(
                startPoint,
                new Point3d(
                    startPoint.X,
                    startPoint.Y + BoundarySearchDistance * direction,
                    startPoint.Z));
        }

        private static ObjectId PromptForBlockDefinition(Database db)
        {
            List<BlockDefinitionChoice> blocks = LoadInsertableBlocks(db);
            if (blocks.Count == 0)
            {
                WF.MessageBox.Show(
                    "Khong tim thay block nao co the chen trong ban ve hien tai.",
                    "BBB_BLOCK_TO_CENTER",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Information);
                return ObjectId.Null;
            }

            using (BlockDefinitionPickerForm form = new BlockDefinitionPickerForm(blocks))
            {
                return Application.ShowModalDialog(form) == WF.DialogResult.OK
                    ? form.SelectedBlockId
                    : ObjectId.Null;
            }
        }

        private static List<BlockDefinitionChoice> LoadInsertableBlocks(Database db)
        {
            List<BlockDefinitionChoice> result = new List<BlockDefinitionChoice>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable =
                    tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                if (blockTable == null)
                {
                    return result;
                }

                foreach (ObjectId blockId in blockTable)
                {
                    BlockTableRecord record =
                        tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;
                    if (record == null)
                    {
                        continue;
                    }

                    if (record.IsLayout ||
                        record.IsAnonymous ||
                        record.IsDependent ||
                        record.IsFromExternalReference ||
                        record.IsFromOverlayReference ||
                        string.IsNullOrWhiteSpace(record.Name))
                    {
                        continue;
                    }

                    result.Add(new BlockDefinitionChoice(blockId, record.Name));
                }
            }

            return result
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void GetBlockDefinitionPlacementData(
            ObjectId blockDefinitionId,
            Transaction tr,
            out Point3d blockCenterInDefinition,
            out Point3d blockOriginInDefinition)
        {
            BlockTableRecord record =
                tr.GetObject(blockDefinitionId, OpenMode.ForRead) as BlockTableRecord;
            if (record == null)
            {
                throw new InvalidOperationException("Invalid block definition.");
            }

            blockOriginInDefinition = record.Origin;
            Extents3d? extents = null;

            foreach (ObjectId entityId in record)
            {
                Entity entity = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                if (entity == null || entity.IsErased)
                {
                    continue;
                }

                if (entity is AttributeDefinition attributeDefinition && attributeDefinition.Invisible)
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

            blockCenterInDefinition = extents.HasValue
                ? GetCenter(extents.Value)
                : blockOriginInDefinition;
        }

        private static void AppendBlockAttributes(BlockReference blockReference, Transaction tr)
        {
            BlockTableRecord definition =
                tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (definition == null || !definition.HasAttributeDefinitions)
            {
                return;
            }

            foreach (ObjectId entityId in definition)
            {
                AttributeDefinition attributeDefinition =
                    tr.GetObject(entityId, OpenMode.ForRead) as AttributeDefinition;
                if (attributeDefinition == null || attributeDefinition.Constant)
                {
                    continue;
                }

                AttributeReference attributeReference = new AttributeReference();
                attributeReference.SetAttributeFromBlock(
                    attributeDefinition,
                    blockReference.BlockTransform);
                attributeReference.Position =
                    attributeDefinition.Position.TransformBy(blockReference.BlockTransform);

                if (attributeReference.IsMTextAttribute)
                {
                    attributeReference.UpdateMTextAttribute();
                }

                blockReference.AttributeCollection.AppendAttribute(attributeReference);
                tr.AddNewlyCreatedDBObject(attributeReference, true);
            }
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
            try
            {
                // Với bài toán đặt block/đối tượng vào giữa ô, tâm extents của đường bao
                // thường khớp trực quan hơn centroid hình học khi vùng có hốc/rỗng.
                Extents3d boundaryExtents = curve.GeometricExtents;
                return GetCenter(boundaryExtents);
            }
            catch
            {
            }

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

            Extents3d fallbackExtents = curve.GeometricExtents;
            return GetCenter(fallbackExtents);
        }

        private static Extents3d GetSelectionExtents(
            IEnumerable<ObjectId> objectIds,
            Transaction tr,
            bool ignoreDimensions = false,
            bool ignoreTextEntities = false)
        {
            // Dùng để lấy tâm nguồn CCC.
            // ignoreDimensions/ignoreTextEntities chỉ ảnh hưởng việc tính tâm,
            // không ảnh hưởng danh sách object được copy.
            Extents3d? extents = null;

            foreach (ObjectId objectId in objectIds)
            {
                if (objectId.IsNull)
                {
                    continue;
                }

                Entity entity = tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                if (!TryGetFilteredEntityExtents(
                    entity,
                    tr,
                    ignoreDimensions,
                    ignoreTextEntities,
                    out Extents3d currentExtents))
                {
                    continue;
                }

                if (extents == null)
                {
                    extents = currentExtents;
                    continue;
                }

                extents = UnionExtents(extents.Value, currentExtents);
            }

            if (extents == null)
            {
                throw new InvalidOperationException("Selection has no valid extents.");
            }

            return extents.Value;
        }

        private static bool TryGetFilteredEntityExtents(
            Entity entity,
            Transaction tr,
            bool ignoreDimensions,
            bool ignoreTextEntities,
            out Extents3d extents,
            HashSet<ObjectId> visitedBlockDefinitions = null)
        {
            // Tính extents có lọc cho từng entity.
            // Với block: nếu block không chứa Text/Dim/Attribute thì dùng GeometricExtents nhanh.
            // Nếu có các object cần loại khỏi tâm thì duyệt sâu vào block definition.
            extents = default;

            if (entity == null)
            {
                return false;
            }

            if (ignoreDimensions && entity is Dimension)
            {
                return false;
            }

            if (ignoreTextEntities &&
                (entity is DBText ||
                 entity is MText ||
                 entity is AttributeDefinition ||
                 entity is AttributeReference))
            {
                return false;
            }

            if (entity is BlockReference blockReference)
            {
                return TryGetFilteredBlockReferenceExtents(
                    blockReference,
                    tr,
                    ignoreDimensions,
                    ignoreTextEntities,
                    out extents,
                    visitedBlockDefinitions ?? new HashSet<ObjectId>());
            }

            try
            {
                extents = entity.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetFilteredBlockReferenceExtents(
            BlockReference blockReference,
            Transaction tr,
            bool ignoreDimensions,
            bool ignoreTextEntities,
            out Extents3d extents,
            HashSet<ObjectId> visitedBlockDefinitions)
        {
            extents = default;
            if (blockReference == null)
            {
                return false;
            }

            ObjectId blockDefinitionId = blockReference.BlockTableRecord;
            if (blockDefinitionId.IsNull || visitedBlockDefinitions.Contains(blockDefinitionId))
            {
                return false;
            }

            if (CanUseDirectBlockReferenceExtents(
                blockReference,
                tr,
                ignoreDimensions,
                ignoreTextEntities,
                visitedBlockDefinitions))
            {
                try
                {
                    extents = blockReference.GeometricExtents;
                    return true;
                }
                catch
                {
                }
            }

            visitedBlockDefinitions.Add(blockDefinitionId);

            try
            {
                Extents3d? combinedExtents = null;
                BlockTableRecord definition =
                    tr.GetObject(blockDefinitionId, OpenMode.ForRead) as BlockTableRecord;
                if (definition != null)
                {
                    foreach (ObjectId childId in definition)
                    {
                        Entity childEntity =
                            tr.GetObject(childId, OpenMode.ForRead) as Entity;
                        if (!TryGetFilteredEntityExtents(
                            childEntity,
                            tr,
                            ignoreDimensions,
                            ignoreTextEntities,
                            out Extents3d childExtents,
                            visitedBlockDefinitions))
                        {
                            continue;
                        }

                        Extents3d transformedExtents =
                            TransformExtents(childExtents, blockReference.BlockTransform);
                        combinedExtents = combinedExtents == null
                            ? transformedExtents
                            : UnionExtents(combinedExtents.Value, transformedExtents);
                    }
                }

                if (!ignoreTextEntities)
                {
                    foreach (ObjectId attributeId in blockReference.AttributeCollection)
                    {
                        AttributeReference attribute =
                            tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                        if (attribute == null)
                        {
                            continue;
                        }

                        try
                        {
                            Extents3d attributeExtents = attribute.GeometricExtents;
                            combinedExtents = combinedExtents == null
                                ? attributeExtents
                                : UnionExtents(combinedExtents.Value, attributeExtents);
                        }
                        catch
                        {
                        }
                    }
                }

                if (combinedExtents == null)
                {
                    return false;
                }

                extents = combinedExtents.Value;
                return true;
            }
            finally
            {
                visitedBlockDefinitions.Remove(blockDefinitionId);
            }
        }

        private static bool CanUseDirectBlockReferenceExtents(
            BlockReference blockReference,
            Transaction tr,
            bool ignoreDimensions,
            bool ignoreTextEntities,
            HashSet<ObjectId> visitedBlockDefinitions)
        {
            if ((!ignoreDimensions && !ignoreTextEntities) || blockReference == null)
            {
                return true;
            }

            if (ignoreTextEntities && blockReference.AttributeCollection.Count > 0)
            {
                return false;
            }

            ObjectId blockDefinitionId = blockReference.BlockTableRecord;
            if (blockDefinitionId.IsNull || visitedBlockDefinitions.Contains(blockDefinitionId))
            {
                return true;
            }

            visitedBlockDefinitions.Add(blockDefinitionId);

            try
            {
                BlockTableRecord definition =
                    tr.GetObject(blockDefinitionId, OpenMode.ForRead) as BlockTableRecord;
                if (definition == null)
                {
                    return true;
                }

                foreach (ObjectId childId in definition)
                {
                    Entity childEntity = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (childEntity == null)
                    {
                        continue;
                    }

                    if (ShouldExcludeEntityFromCenter(childEntity, ignoreDimensions, ignoreTextEntities))
                    {
                        return false;
                    }

                    if (childEntity is BlockReference nestedBlockReference &&
                        !CanUseDirectBlockReferenceExtents(
                            nestedBlockReference,
                            tr,
                            ignoreDimensions,
                            ignoreTextEntities,
                            visitedBlockDefinitions))
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                visitedBlockDefinitions.Remove(blockDefinitionId);
            }
        }

        private static bool ShouldExcludeEntityFromCenter(
            Entity entity,
            bool ignoreDimensions,
            bool ignoreTextEntities)
        {
            if (entity == null)
            {
                return false;
            }

            if (ignoreDimensions && entity is Dimension)
            {
                return true;
            }

            return ignoreTextEntities &&
                   (entity is DBText ||
                    entity is MText ||
                    entity is AttributeDefinition ||
                    entity is AttributeReference);
        }

        private static Extents3d TransformExtents(Extents3d extents, Matrix3d transform)
        {
            Point3d[] corners =
            {
                new Point3d(extents.MinPoint.X, extents.MinPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MinPoint.X, extents.MinPoint.Y, extents.MaxPoint.Z),
                new Point3d(extents.MinPoint.X, extents.MaxPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MinPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MinPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MinPoint.Y, extents.MaxPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MaxPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z)
            };

            Point3d firstPoint = corners[0].TransformBy(transform);
            Extents3d transformed = new Extents3d(firstPoint, firstPoint);
            for (int i = 1; i < corners.Length; i++)
            {
                transformed.AddPoint(corners[i].TransformBy(transform));
            }

            return transformed;
        }

        private static Extents3d UnionExtents(Extents3d left, Extents3d right)
        {
            return new Extents3d(
                new Point3d(
                    Math.Min(left.MinPoint.X, right.MinPoint.X),
                    Math.Min(left.MinPoint.Y, right.MinPoint.Y),
                    Math.Min(left.MinPoint.Z, right.MinPoint.Z)),
                new Point3d(
                    Math.Max(left.MaxPoint.X, right.MaxPoint.X),
                    Math.Max(left.MaxPoint.Y, right.MaxPoint.Y),
                    Math.Max(left.MaxPoint.Z, right.MaxPoint.Z)));
        }

        private static Point3d GetCenter(Extents3d extents)
        {
            return new Point3d(
                (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
        }
    }

    // ======================================================
    // UI CHỌN BLOCK CHO BBB
    // Form nhỏ để lọc/chọn block definition trong bản vẽ hiện tại.
    // ======================================================
    internal sealed class BlockDefinitionChoice
    {
        public BlockDefinitionChoice(ObjectId id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }

        public ObjectId Id { get; }

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class BlockDefinitionPickerForm : WF.Form
    {
        private readonly List<BlockDefinitionChoice> _allBlocks;
        private readonly WF.TextBox _searchBox;
        private readonly WF.ListBox _listBox;
        private readonly WF.Label _countLabel;
        private readonly WF.Button _okButton;

        public BlockDefinitionPickerForm(IEnumerable<BlockDefinitionChoice> blocks)
        {
            _allBlocks = blocks?
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<BlockDefinitionChoice>();

            Text = "Chon Block Nguon";
            StartPosition = WF.FormStartPosition.CenterParent;
            MinimumSize = new Size(420, 520);
            Size = new Size(460, 580);
            FormBorderStyle = WF.FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WF.Padding(10)
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            Controls.Add(layout);

            WF.Label searchLabel = new WF.Label
            {
                Text = "Tim block:",
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 6)
            };
            layout.Controls.Add(searchLabel, 0, 0);

            _searchBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            _searchBox.TextChanged += (_, __) => ApplyFilter();
            layout.Controls.Add(_searchBox, 0, 1);

            _listBox = new WF.ListBox
            {
                Dock = WF.DockStyle.Fill,
                IntegralHeight = false
            };
            _listBox.SelectedIndexChanged += (_, __) => UpdateSelectionState();
            _listBox.DoubleClick += (_, __) => ConfirmSelection();
            layout.Controls.Add(_listBox, 0, 2);

            WF.FlowLayoutPanel footer = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                FlowDirection = WF.FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(footer, 0, 3);

            _okButton = new WF.Button
            {
                Text = "OK",
                AutoSize = true,
                Enabled = false
            };
            _okButton.Click += (_, __) => ConfirmSelection();
            footer.Controls.Add(_okButton);

            WF.Button cancelButton = new WF.Button
            {
                Text = "Cancel",
                AutoSize = true,
                DialogResult = WF.DialogResult.Cancel
            };
            footer.Controls.Add(cancelButton);

            _countLabel = new WF.Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new WF.Padding(0, 8, 12, 0)
            };
            footer.Controls.Add(_countLabel);

            AcceptButton = _okButton;
            CancelButton = cancelButton;

            ApplyFilter();
        }

        public ObjectId SelectedBlockId =>
            _listBox.SelectedItem is BlockDefinitionChoice choice
                ? choice.Id
                : ObjectId.Null;

        private void ApplyFilter()
        {
            string keyword = (_searchBox.Text ?? string.Empty).Trim();
            IEnumerable<BlockDefinitionChoice> filtered = _allBlocks;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                filtered = filtered.Where(item =>
                    item.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            List<BlockDefinitionChoice> items = filtered.ToList();
            _listBox.BeginUpdate();
            _listBox.Items.Clear();
            foreach (BlockDefinitionChoice item in items)
            {
                _listBox.Items.Add(item);
            }
            _listBox.EndUpdate();

            if (_listBox.Items.Count > 0)
            {
                _listBox.SelectedIndex = 0;
            }

            _countLabel.Text = $"{items.Count} block";
            UpdateSelectionState();
        }

        private void UpdateSelectionState()
        {
            _okButton.Enabled = _listBox.SelectedItem is BlockDefinitionChoice;
        }

        private void ConfirmSelection()
        {
            if (!(_listBox.SelectedItem is BlockDefinitionChoice))
            {
                return;
            }

            DialogResult = WF.DialogResult.OK;
            Close();
        }
    }

    internal sealed class SdxyEntityTypeChoice
    {
        public SdxyEntityTypeChoice(Type managedType, string displayName, bool isCommon)
        {
            ManagedType = managedType;
            DisplayName = displayName ?? string.Empty;
            IsCommon = isCommon;
        }

        public Type ManagedType { get; }

        public string DisplayName { get; }

        public bool IsCommon { get; }

        public string TypeName => ManagedType?.FullName ?? string.Empty;

        public override string ToString()
        {
            return DisplayName;
        }
    }

    internal sealed class SdxySampleDescriptor
    {
        public SdxySampleDescriptor(
            string typeName,
            string typeDisplayName,
            string layerName,
            string linetypeName,
            string colorKey,
            string colorDisplayName,
            string blockName)
        {
            TypeName = typeName ?? string.Empty;
            TypeDisplayName = typeDisplayName ?? string.Empty;
            LayerName = layerName ?? string.Empty;
            LinetypeName = linetypeName ?? string.Empty;
            ColorKey = colorKey ?? string.Empty;
            ColorDisplayName = colorDisplayName ?? string.Empty;
            BlockName = blockName ?? string.Empty;
        }

        public string TypeName { get; }

        public string TypeDisplayName { get; }

        public string LayerName { get; }

        public string LinetypeName { get; }

        public string ColorKey { get; }

        public string ColorDisplayName { get; }

        public string BlockName { get; }

        public SdxySampleDescriptor Clone()
        {
            return new SdxySampleDescriptor(
                TypeName,
                TypeDisplayName,
                LayerName,
                LinetypeName,
                ColorKey,
                ColorDisplayName,
                BlockName);
        }

        public string BuildSummary()
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TypeDisplayName))
            {
                parts.Add("Type: " + TypeDisplayName);
            }

            if (!string.IsNullOrWhiteSpace(LayerName))
            {
                parts.Add("Layer: " + LayerName);
            }

            if (!string.IsNullOrWhiteSpace(LinetypeName))
            {
                parts.Add("Linetype: " + LinetypeName);
            }

            if (!string.IsNullOrWhiteSpace(ColorDisplayName))
            {
                parts.Add("Color: " + ColorDisplayName);
            }

            if (!string.IsNullOrWhiteSpace(BlockName))
            {
                parts.Add("Block: " + BlockName);
            }

            return parts.Count == 0
                ? "Chua co sample object."
                : string.Join(" | ", parts);
        }
    }

    internal sealed class SdxyTargetSettings
    {
        public SdxyTargetSettings()
        {
            AllowedTypeNames = new HashSet<string>(StringComparer.Ordinal);
            AllowedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SampleDescriptors = new List<SdxySampleDescriptor>();
        }

        public HashSet<string> AllowedTypeNames { get; }

        public HashSet<string> AllowedLayers { get; }

        public bool UseSampleType { get; set; }

        public bool UseSampleLayer { get; set; }

        public bool UseSampleLinetype { get; set; }

        public bool UseSampleColor { get; set; }

        public bool UseSampleBlockName { get; set; }

        public List<SdxySampleDescriptor> SampleDescriptors { get; }

        public SdxySampleDescriptor SampleDescriptor
        {
            get
            {
                return SampleDescriptors.Count == 0 ? null : SampleDescriptors[0];
            }
            set
            {
                SampleDescriptors.Clear();
                if (value != null)
                {
                    SampleDescriptors.Add(value.Clone());
                }
            }
        }

        public SdxyTargetSettings Clone()
        {
            SdxyTargetSettings clone = new SdxyTargetSettings
            {
                UseSampleType = UseSampleType,
                UseSampleLayer = UseSampleLayer,
                UseSampleLinetype = UseSampleLinetype,
                UseSampleColor = UseSampleColor,
                UseSampleBlockName = UseSampleBlockName
            };

            foreach (string typeName in AllowedTypeNames)
            {
                clone.AllowedTypeNames.Add(typeName);
            }

            foreach (string layerName in AllowedLayers)
            {
                clone.AllowedLayers.Add(layerName);
            }

            foreach (SdxySampleDescriptor sample in SampleDescriptors)
            {
                if (sample != null)
                {
                    clone.SampleDescriptors.Add(sample.Clone());
                }
            }

            return clone;
        }
    }

    internal enum SdxySettingsFormAction
    {
        None,
        PickSample
    }

    internal sealed class SdxySettingsForm : WF.Form
    {
        private readonly List<SdxyEntityTypeChoice> _availableTypes;
        private readonly List<string> _availableLayers;
        private readonly WF.ComboBox _namedFilterCombo;
        private readonly WF.CheckedListBox _typeList;
        private readonly WF.CheckedListBox _layerList;
        private readonly WF.Label _filterPreviewLabel;
        private readonly WF.Label _typeCountLabel;
        private readonly WF.Label _layerCountLabel;
        private readonly WF.Label _sampleSummaryLabel;
        private readonly WF.ListBox _sampleListBox;
        private readonly WF.Label _sampleListCountLabel;
        private readonly WF.CheckBox _sampleTypeCheckBox;
        private readonly WF.CheckBox _sampleLayerCheckBox;
        private readonly WF.CheckBox _sampleLinetypeCheckBox;
        private readonly WF.CheckBox _sampleColorCheckBox;
        private readonly WF.CheckBox _sampleBlockNameCheckBox;
        private readonly WF.ComboBox _sampleTypeValueCombo;
        private readonly WF.ComboBox _sampleLayerValueCombo;
        private readonly WF.TextBox _sampleLinetypeValueTextBox;
        private readonly WF.TextBox _sampleColorValueTextBox;
        private readonly WF.TextBox _sampleBlockNameValueTextBox;
        private readonly Dictionary<string, SdxyTargetSettings> _namedFilters;
        private SdxyTargetSettings _draftSettings;
        private string _selectedNamedFilterName;
        private int _selectedSampleIndex;
        private bool _suppressSampleEditorEvents;

        public SdxySettingsForm(
            IEnumerable<SdxyEntityTypeChoice> availableTypes,
            IEnumerable<string> availableLayers,
            SdxyTargetSettings currentSettings)
        {
            _availableTypes = availableTypes?.ToList() ?? new List<SdxyEntityTypeChoice>();
            _availableLayers = availableLayers?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
            _namedFilters = SdxyNamedFilterStore.LoadAll();
            _draftSettings = currentSettings?.Clone() ?? new SdxyTargetSettings();
            _selectedNamedFilterName = SdxyNamedFilterStore.LoadCurrentName();
            _selectedSampleIndex = _draftSettings.SampleDescriptors.Count - 1;

            Text = "SDXY Target Settings";
            StartPosition = WF.FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 620);
            Size = new Size(760, 680);
            FormBorderStyle = WF.FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new WF.Padding(10)
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            Controls.Add(layout);

            WF.GroupBox namedFiltersGroup = new WF.GroupBox
            {
                Text = "Named Filters",
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Padding = new WF.Padding(10, 20, 10, 10),
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(namedFiltersGroup, 0, 0);

            WF.TableLayoutPanel namedFiltersLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            namedFiltersLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            namedFiltersLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            namedFiltersLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            namedFiltersLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            namedFiltersGroup.Controls.Add(namedFiltersLayout);

            WF.Label currentPresetLabel = new WF.Label
            {
                Text = "Current:",
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 4, 8, 0)
            };
            namedFiltersLayout.Controls.Add(currentPresetLabel, 0, 0);

            _namedFilterCombo = new WF.ComboBox
            {
                Dock = WF.DockStyle.Top,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            _namedFilterCombo.SelectedIndexChanged += (_, __) => UpdateNamedFilterButtons();
            namedFiltersLayout.Controls.Add(_namedFilterCombo, 1, 0);

            WF.FlowLayoutPanel namedButtons = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new WF.Padding(0)
            };
            namedFiltersLayout.Controls.Add(namedButtons, 1, 1);

            WF.Button saveAsNamedButton = CreateSmallButton("Save As...");
            saveAsNamedButton.Click += (_, __) => SaveCurrentAsNamedFilter();
            namedButtons.Controls.Add(saveAsNamedButton);

            WF.Button loadNamedButton = CreateSmallButton("Load");
            loadNamedButton.Click += (_, __) => LoadSelectedNamedFilter();
            namedButtons.Controls.Add(loadNamedButton);

            WF.Button deleteNamedButton = CreateSmallButton("Delete");
            deleteNamedButton.Click += (_, __) => DeleteSelectedNamedFilter();
            namedButtons.Controls.Add(deleteNamedButton);

            WF.Label introLabel = new WF.Label
            {
                Text =
                    "Chon cac doi tuong ma SDXY duoc phep dim toi. " +
                    "Neu bo trong hoac check het thi xem nhu khong loc. " +
                    "Sample object co the dung de loc them theo type/layer/linetype/color/block.",
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(introLabel, 0, 1);

            _filterPreviewLabel = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                BorderStyle = WF.BorderStyle.FixedSingle,
                Padding = new WF.Padding(10),
                Margin = new WF.Padding(0, 0, 0, 8),
                Font = new System.Drawing.Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            layout.Controls.Add(_filterPreviewLabel, 0, 2);

            WF.TabControl tabs = new WF.TabControl
            {
                Dock = WF.DockStyle.Fill
            };
            layout.Controls.Add(tabs, 0, 3);

            WF.TabPage typesPage = new WF.TabPage("Types");
            tabs.TabPages.Add(typesPage);
            WF.TableLayoutPanel typesLayout = CreateTabLayout(typesPage);
            WF.FlowLayoutPanel typeButtons = CreateButtonPanel();
            typesLayout.Controls.Add(typeButtons, 0, 0);

            WF.Button allTypesButton = CreateSmallButton("All");
            allTypesButton.Click += (_, __) => SetAllChecked(_typeList, true);
            typeButtons.Controls.Add(allTypesButton);

            WF.Button noneTypesButton = CreateSmallButton("None");
            noneTypesButton.Click += (_, __) => SetAllChecked(_typeList, false);
            typeButtons.Controls.Add(noneTypesButton);

            WF.Button commonTypesButton = CreateSmallButton("Common");
            commonTypesButton.Click += (_, __) => ApplyCommonTypesSelection();
            typeButtons.Controls.Add(commonTypesButton);

            _typeList = new WF.CheckedListBox
            {
                Dock = WF.DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _typeList.ItemCheck += TypeList_ItemCheck;
            typesLayout.Controls.Add(_typeList, 0, 1);

            _typeCountLabel = new WF.Label
            {
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            typesLayout.Controls.Add(_typeCountLabel, 0, 2);

            WF.TabPage layersPage = new WF.TabPage("Layers");
            tabs.TabPages.Add(layersPage);
            WF.TableLayoutPanel layersLayout = CreateTabLayout(layersPage);
            WF.FlowLayoutPanel layerButtons = CreateButtonPanel();
            layersLayout.Controls.Add(layerButtons, 0, 0);

            WF.Button allLayersButton = CreateSmallButton("All");
            allLayersButton.Click += (_, __) => SetAllChecked(_layerList, true);
            layerButtons.Controls.Add(allLayersButton);

            WF.Button noneLayersButton = CreateSmallButton("None");
            noneLayersButton.Click += (_, __) => SetAllChecked(_layerList, false);
            layerButtons.Controls.Add(noneLayersButton);

            _layerList = new WF.CheckedListBox
            {
                Dock = WF.DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _layerList.ItemCheck += LayerList_ItemCheck;
            layersLayout.Controls.Add(_layerList, 0, 1);

            _layerCountLabel = new WF.Label
            {
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            layersLayout.Controls.Add(_layerCountLabel, 0, 2);

            WF.TabPage samplePage = new WF.TabPage("Sample");
            tabs.TabPages.Add(samplePage);
            WF.TableLayoutPanel sampleLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new WF.Padding(8)
            };
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            samplePage.Controls.Add(sampleLayout);

            _sampleSummaryLabel = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 10)
            };
            sampleLayout.Controls.Add(_sampleSummaryLabel, 0, 0);

            WF.FlowLayoutPanel sampleButtons = CreateButtonPanel();
            sampleLayout.Controls.Add(sampleButtons, 0, 1);

            WF.Button pickSampleButton = CreateSmallButton("Pick sample...");
            pickSampleButton.Click += (_, __) => RequestPickSample();
            sampleButtons.Controls.Add(pickSampleButton);

            WF.Button addCurrentSampleButton = CreateSmallButton("Add current");
            addCurrentSampleButton.Click += (_, __) => AddCurrentSampleFromEditor();
            sampleButtons.Controls.Add(addCurrentSampleButton);

            WF.Button removeSampleButton = CreateSmallButton("Remove selected");
            removeSampleButton.Click += (_, __) => RemoveSelectedSample();
            sampleButtons.Controls.Add(removeSampleButton);

            WF.Button clearSampleButton = CreateSmallButton("Clear editor");
            clearSampleButton.Click += (_, __) =>
            {
                ClearSampleEditor();
            };
            sampleButtons.Controls.Add(clearSampleButton);

            WF.GroupBox sampleListGroup = new WF.GroupBox
            {
                Text = "Saved sample objects (OR)",
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                Padding = new WF.Padding(12, 24, 12, 12),
                Margin = new WF.Padding(0, 0, 0, 10)
            };
            sampleLayout.Controls.Add(sampleListGroup, 0, 2);

            WF.TableLayoutPanel sampleListLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true
            };
            sampleListLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            sampleListLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleListGroup.Controls.Add(sampleListLayout);

            _sampleListBox = new WF.ListBox
            {
                Dock = WF.DockStyle.Fill,
                Height = 120
            };
            _sampleListBox.SelectedIndexChanged += (_, __) => LoadSelectedSampleIntoEditors();
            sampleListLayout.Controls.Add(_sampleListBox, 0, 0);

            _sampleListCountLabel = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            sampleListLayout.Controls.Add(_sampleListCountLabel, 0, 1);

            WF.GroupBox sampleValuesGroup = new WF.GroupBox
            {
                Text = "Sample values",
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                Padding = new WF.Padding(12, 24, 12, 12),
                Margin = new WF.Padding(0, 0, 0, 10)
            };
            sampleLayout.Controls.Add(sampleValuesGroup, 0, 3);

            WF.TableLayoutPanel sampleValuesLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                AutoSize = true
            };
            sampleValuesLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            sampleValuesLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            sampleValuesGroup.Controls.Add(sampleValuesLayout);

            _sampleTypeValueCombo = new WF.ComboBox
            {
                Dock = WF.DockStyle.Top,
                DropDownStyle = WF.ComboBoxStyle.DropDown
            };
            foreach (SdxyEntityTypeChoice choice in _availableTypes)
            {
                _sampleTypeValueCombo.Items.Add(choice);
            }

            _sampleLayerValueCombo = new WF.ComboBox
            {
                Dock = WF.DockStyle.Top,
                DropDownStyle = WF.ComboBoxStyle.DropDown
            };
            foreach (string layerName in _availableLayers)
            {
                _sampleLayerValueCombo.Items.Add(layerName);
            }

            _sampleLinetypeValueTextBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top
            };

            _sampleColorValueTextBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top
            };

            _sampleBlockNameValueTextBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top
            };

            AddSampleValueRow(sampleValuesLayout, 0, "Type:", _sampleTypeValueCombo);
            AddSampleValueRow(sampleValuesLayout, 1, "Layer:", _sampleLayerValueCombo);
            AddSampleValueRow(sampleValuesLayout, 2, "Linetype:", _sampleLinetypeValueTextBox);
            AddSampleValueRow(sampleValuesLayout, 3, "Color key:", _sampleColorValueTextBox);
            AddSampleValueRow(sampleValuesLayout, 4, "Block name:", _sampleBlockNameValueTextBox);

            WF.Label sampleHintLabel = new WF.Label
            {
                Text = "Pick sample de lay nhanh attribute, sau do co the sua tay va luu thanh preset. Color key ho tro: ByLayer, ByBlock, ACI:1, RGB:255,0,0.",
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            sampleValuesLayout.Controls.Add(sampleHintLabel, 0, 5);
            sampleValuesLayout.SetColumnSpan(sampleHintLabel, 2);

            WF.GroupBox sampleGroup = new WF.GroupBox
            {
                Text = "Match sample attributes",
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                Padding = new WF.Padding(12, 24, 12, 12)
            };
            sampleLayout.Controls.Add(sampleGroup, 0, 4);

            WF.FlowLayoutPanel sampleCheckPanel = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.TopDown,
                WrapContents = false
            };
            sampleGroup.Controls.Add(sampleCheckPanel);

            _sampleTypeCheckBox = CreateSampleCheckBox("Match type");
            _sampleLayerCheckBox = CreateSampleCheckBox("Match layer");
            _sampleLinetypeCheckBox = CreateSampleCheckBox("Match linetype");
            _sampleColorCheckBox = CreateSampleCheckBox("Match color");
            _sampleBlockNameCheckBox = CreateSampleCheckBox("Match block name");
            sampleCheckPanel.Controls.Add(_sampleTypeCheckBox);
            sampleCheckPanel.Controls.Add(_sampleLayerCheckBox);
            sampleCheckPanel.Controls.Add(_sampleLinetypeCheckBox);
            sampleCheckPanel.Controls.Add(_sampleColorCheckBox);
            sampleCheckPanel.Controls.Add(_sampleBlockNameCheckBox);

            _sampleTypeValueCombo.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleTypeValueCombo.SelectedIndexChanged += (_, __) => RefreshSampleEditorState();
            _sampleLayerValueCombo.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleLayerValueCombo.SelectedIndexChanged += (_, __) => RefreshSampleEditorState();
            _sampleLinetypeValueTextBox.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleColorValueTextBox.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleBlockNameValueTextBox.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleTypeCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleLayerCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleLinetypeCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleColorCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleBlockNameCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();

            WF.FlowLayoutPanel footer = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                FlowDirection = WF.FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(footer, 0, 4);

            WF.Button okButton = new WF.Button
            {
                Text = "OK",
                AutoSize = true,
                DialogResult = WF.DialogResult.OK
            };
            footer.Controls.Add(okButton);

            WF.Button cancelButton = new WF.Button
            {
                Text = "Cancel",
                AutoSize = true,
                DialogResult = WF.DialogResult.Cancel
            };
            footer.Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            LoadNamedFilterItems();
            LoadTypeItems();
            LoadLayerItems();
            LoadSampleState();
            RefreshFilterPreview();
        }

        public SdxySettingsFormAction PendingAction { get; private set; }

        public SdxyTargetSettings ResultSettings => BuildSettings();

        public string SelectedNamedFilterName => _selectedNamedFilterName ?? string.Empty;

        private static WF.TableLayoutPanel CreateTabLayout(WF.Control parent)
        {
            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new WF.Padding(8)
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            parent.Controls.Add(layout);
            return layout;
        }

        private static WF.FlowLayoutPanel CreateButtonPanel()
        {
            return new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                FlowDirection = WF.FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
        }

        private static WF.Button CreateSmallButton(string text)
        {
            return new WF.Button
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 8, 0)
            };
        }

        private static WF.CheckBox CreateSampleCheckBox(string text)
        {
            return new WF.CheckBox
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 6)
            };
        }

        private static void AddSampleValueRow(
            WF.TableLayoutPanel layout,
            int rowIndex,
            string labelText,
            WF.Control control)
        {
            WF.Label label = new WF.Label
            {
                Text = labelText,
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 4, 8, 0)
            };

            control.Margin = new WF.Padding(0, 0, 0, 6);
            layout.Controls.Add(label, 0, rowIndex);
            layout.Controls.Add(control, 1, rowIndex);
        }

        private void LoadTypeItems()
        {
            _typeList.Items.Clear();
            HashSet<string> selected =
                _draftSettings.AllowedTypeNames.Count == 0
                    ? new HashSet<string>(_availableTypes.Select(item => item.TypeName), StringComparer.Ordinal)
                    : new HashSet<string>(_draftSettings.AllowedTypeNames, StringComparer.Ordinal);

            foreach (SdxyEntityTypeChoice choice in _availableTypes)
            {
                int index = _typeList.Items.Add(choice);
                _typeList.SetItemChecked(index, selected.Contains(choice.TypeName));
            }

            UpdateTypeCountLabel();
        }

        private void LoadLayerItems()
        {
            _layerList.Items.Clear();
            HashSet<string> selected =
                _draftSettings.AllowedLayers.Count == 0
                    ? new HashSet<string>(_availableLayers, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(_draftSettings.AllowedLayers, StringComparer.OrdinalIgnoreCase);

            foreach (string layerName in _availableLayers)
            {
                int index = _layerList.Items.Add(layerName);
                _layerList.SetItemChecked(index, selected.Contains(layerName));
            }

            UpdateLayerCountLabel();
        }

        private void ApplyCommonTypesSelection()
        {
            for (int i = 0; i < _typeList.Items.Count; i++)
            {
                bool isCommon =
                    _typeList.Items[i] is SdxyEntityTypeChoice choice &&
                    choice.IsCommon;
                _typeList.SetItemChecked(i, isCommon);
            }

            UpdateTypeCountLabel();
        }

        private void SetAllChecked(WF.CheckedListBox list, bool isChecked)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, isChecked);
            }

            UpdateTypeCountLabel();
            UpdateLayerCountLabel();
        }

        private void UpdateTypeCountLabel()
        {
            _typeCountLabel.Text =
                $"Dang chon {_typeList.CheckedItems.Count}/{_typeList.Items.Count} type.";
        }

        private void UpdateLayerCountLabel()
        {
            _layerCountLabel.Text =
                $"Dang chon {_layerList.CheckedItems.Count}/{_layerList.Items.Count} layer.";
        }

        private void TypeList_ItemCheck(object sender, WF.ItemCheckEventArgs e)
        {
            UpdateCheckedCountLabel(_typeList, _typeCountLabel, "type", e);
            QueueRefreshFilterPreview();
        }

        private void LayerList_ItemCheck(object sender, WF.ItemCheckEventArgs e)
        {
            UpdateCheckedCountLabel(_layerList, _layerCountLabel, "layer", e);
            QueueRefreshFilterPreview();
        }

        private static void UpdateCheckedCountLabel(
            WF.CheckedListBox list,
            WF.Label label,
            string noun,
            WF.ItemCheckEventArgs e)
        {
            if (list == null || label == null)
            {
                return;
            }

            int checkedCount = list.CheckedItems.Count;
            if (e != null)
            {
                if (e.CurrentValue != WF.CheckState.Checked &&
                    e.NewValue == WF.CheckState.Checked)
                {
                    checkedCount++;
                }
                else if (e.CurrentValue == WF.CheckState.Checked &&
                    e.NewValue != WF.CheckState.Checked)
                {
                    checkedCount--;
                }
            }

            label.Text = $"Dang chon {checkedCount}/{list.Items.Count} {noun}.";
        }

        private void LoadSampleState()
        {
            LoadSampleListItems();
            SdxySampleDescriptor sample = GetSelectedOrCurrentSample();

            _suppressSampleEditorEvents = true;
            try
            {
                SetSampleTypeEditorValue(sample);
                _sampleLayerValueCombo.Text = sample?.LayerName ?? string.Empty;
                _sampleLinetypeValueTextBox.Text = sample?.LinetypeName ?? string.Empty;
                _sampleColorValueTextBox.Text = sample?.ColorKey ?? string.Empty;
                _sampleBlockNameValueTextBox.Text = sample?.BlockName ?? string.Empty;
            }
            finally
            {
                _suppressSampleEditorEvents = false;
            }

            _sampleTypeCheckBox.Checked = _draftSettings.UseSampleType;
            _sampleLayerCheckBox.Checked = _draftSettings.UseSampleLayer;
            _sampleLinetypeCheckBox.Checked = _draftSettings.UseSampleLinetype;
            _sampleColorCheckBox.Checked = _draftSettings.UseSampleColor;
            _sampleBlockNameCheckBox.Checked = _draftSettings.UseSampleBlockName;
            RefreshSampleEditorState();
        }

        private void LoadNamedFilterItems()
        {
            _namedFilterCombo.Items.Clear();
            foreach (string name in _namedFilters.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                _namedFilterCombo.Items.Add(name);
            }

            if (!string.IsNullOrWhiteSpace(_selectedNamedFilterName) &&
                _namedFilters.ContainsKey(_selectedNamedFilterName))
            {
                _namedFilterCombo.SelectedItem = _selectedNamedFilterName;
            }
            else if (_namedFilterCombo.Items.Count > 0)
            {
                _namedFilterCombo.SelectedIndex = 0;
            }

            UpdateNamedFilterButtons();
        }

        private void UpdateNamedFilterButtons()
        {
            if (_namedFilterCombo.SelectedItem is string selectedName &&
                !string.IsNullOrWhiteSpace(selectedName))
            {
                _selectedNamedFilterName = selectedName;
            }
        }

        private void SaveCurrentAsNamedFilter()
        {
            string inputName = PaletteUiHelpers.ShowTextPrompt(
                "SDXY Named Filter",
                "Nhap ten filter de luu:");
            string filterName = NormalizeNamedFilterName(inputName);
            if (string.IsNullOrWhiteSpace(filterName))
            {
                return;
            }

            if (_namedFilters.ContainsKey(filterName))
            {
                WF.DialogResult overwriteResult = WF.MessageBox.Show(
                    $"Filter '{filterName}' da ton tai. Ghi de?",
                    "SDXY Named Filter",
                    WF.MessageBoxButtons.YesNo,
                    WF.MessageBoxIcon.Question);
                if (overwriteResult != WF.DialogResult.Yes)
                {
                    return;
                }
            }

            _namedFilters[filterName] = BuildSettings();
            _selectedNamedFilterName = filterName;
            SdxyNamedFilterStore.SaveAll(_namedFilters);
            LoadNamedFilterItems();
            _namedFilterCombo.SelectedItem = filterName;
        }

        private void LoadSelectedNamedFilter()
        {
            if (!(_namedFilterCombo.SelectedItem is string filterName) ||
                string.IsNullOrWhiteSpace(filterName) ||
                !_namedFilters.TryGetValue(filterName, out SdxyTargetSettings settings))
            {
                return;
            }

            _draftSettings = settings.Clone();
            _selectedNamedFilterName = filterName;
            _selectedSampleIndex = _draftSettings.SampleDescriptors.Count > 0 ? 0 : -1;
            LoadTypeItems();
            LoadLayerItems();
            LoadSampleState();
            RefreshFilterPreview();
        }

        private void DeleteSelectedNamedFilter()
        {
            if (!(_namedFilterCombo.SelectedItem is string filterName) ||
                string.IsNullOrWhiteSpace(filterName))
            {
                return;
            }

            WF.DialogResult deleteResult = WF.MessageBox.Show(
                $"Xoa filter '{filterName}'?",
                "SDXY Named Filter",
                WF.MessageBoxButtons.YesNo,
                WF.MessageBoxIcon.Question);
            if (deleteResult != WF.DialogResult.Yes)
            {
                return;
            }

            _namedFilters.Remove(filterName);
            if (string.Equals(_selectedNamedFilterName, filterName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedNamedFilterName = string.Empty;
            }

            SdxyNamedFilterStore.SaveAll(_namedFilters);
            LoadNamedFilterItems();
        }

        private static string NormalizeNamedFilterName(string name)
        {
            string normalized = (name ?? string.Empty).Trim();
            normalized = normalized.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized;
        }

        private void RequestPickSample()
        {
            _draftSettings = BuildSettings();
            PendingAction = SdxySettingsFormAction.PickSample;
            Close();
        }

        private SdxyTargetSettings BuildSettings()
        {
            SdxyTargetSettings settings = new SdxyTargetSettings
            {
                UseSampleType = _sampleTypeCheckBox.Checked,
                UseSampleLayer = _sampleLayerCheckBox.Checked,
                UseSampleLinetype = _sampleLinetypeCheckBox.Checked,
                UseSampleColor = _sampleColorCheckBox.Checked,
                UseSampleBlockName = _sampleBlockNameCheckBox.Checked
            };

            HashSet<string> selectedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in _typeList.CheckedItems)
            {
                if (item is SdxyEntityTypeChoice choice &&
                    !string.IsNullOrWhiteSpace(choice.TypeName))
                {
                    selectedTypes.Add(choice.TypeName);
                }
            }

            if (selectedTypes.Count > 0 && selectedTypes.Count < _availableTypes.Count)
            {
                foreach (string typeName in selectedTypes)
                {
                    settings.AllowedTypeNames.Add(typeName);
                }
            }

            HashSet<string> selectedLayers =
                new HashSet<string>(_layerList.CheckedItems.Cast<string>(), StringComparer.OrdinalIgnoreCase);
            if (selectedLayers.Count > 0 && selectedLayers.Count < _availableLayers.Count)
            {
                foreach (string layerName in selectedLayers)
                {
                    settings.AllowedLayers.Add(layerName);
                }
            }

            foreach (SdxySampleDescriptor sample in BuildSampleDescriptorListFromUi())
            {
                settings.SampleDescriptors.Add(sample);
            }

            if (settings.SampleDescriptors.Count == 0)
            {
                settings.UseSampleType = false;
                settings.UseSampleLayer = false;
                settings.UseSampleLinetype = false;
                settings.UseSampleColor = false;
                settings.UseSampleBlockName = false;
            }
            else
            {
                bool hasType = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.TypeName));
                bool hasLayer = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.LayerName));
                bool hasLinetype = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.LinetypeName));
                bool hasColor = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.ColorKey));
                bool hasBlock = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.BlockName));

                if (!hasType)
                {
                    settings.UseSampleType = false;
                }

                if (!hasLayer)
                {
                    settings.UseSampleLayer = false;
                }

                if (!hasLinetype)
                {
                    settings.UseSampleLinetype = false;
                }

                if (!hasColor)
                {
                    settings.UseSampleColor = false;
                }

                if (!hasBlock)
                {
                    settings.UseSampleBlockName = false;
                }
            }

            return settings;
        }

        private void QueueRefreshFilterPreview()
        {
            if (IsHandleCreated)
            {
                BeginInvoke((Action)RefreshFilterPreview);
            }
        }

        private void RefreshFilterPreview()
        {
            if (_filterPreviewLabel == null)
            {
                return;
            }

            SdxyTargetSettings settings = BuildSettings();
            string typeText = settings.AllowedTypeNames.Count == 0
                ? "All types"
                : $"{settings.AllowedTypeNames.Count} type";
            string layerText = settings.AllowedLayers.Count == 0
                ? "All layers"
                : $"{settings.AllowedLayers.Count} layer";

            List<string> sampleModes = GetEnabledSampleModeLabels(settings);
            string sampleText = settings.SampleDescriptors.Count == 0
                ? "No sample objects"
                : $"{settings.SampleDescriptors.Count} sample object(s)" +
                  (sampleModes.Count == 0 ? string.Empty : $" match {string.Join("+", sampleModes)}");

            _filterPreviewLabel.Text =
                "Current filter = " + typeText + " AND " + layerText + " AND " + sampleText;
        }

        private List<string> GetEnabledSampleModeLabels(SdxyTargetSettings settings)
        {
            List<string> modes = new List<string>();
            if (settings.UseSampleType) modes.Add("Type");
            if (settings.UseSampleLayer) modes.Add("Layer");
            if (settings.UseSampleLinetype) modes.Add("Linetype");
            if (settings.UseSampleColor) modes.Add("Color");
            if (settings.UseSampleBlockName) modes.Add("Block");
            return modes;
        }

        private void SetSampleTypeEditorValue(SdxySampleDescriptor sample)
        {
            string sampleTypeName = sample?.TypeName ?? string.Empty;
            SdxyEntityTypeChoice matchedChoice = _availableTypes.FirstOrDefault(choice =>
                string.Equals(choice.TypeName, sampleTypeName, StringComparison.OrdinalIgnoreCase));
            if (matchedChoice != null)
            {
                _sampleTypeValueCombo.SelectedItem = matchedChoice;
                return;
            }

            _sampleTypeValueCombo.SelectedItem = null;
            _sampleTypeValueCombo.Text = !string.IsNullOrWhiteSpace(sample?.TypeDisplayName)
                ? sample.TypeDisplayName
                : sampleTypeName;
        }

        private SdxySampleDescriptor BuildSampleDescriptorFromEditors()
        {
            string typeName = string.Empty;
            string typeDisplayName = string.Empty;

            if (_sampleTypeValueCombo.SelectedItem is SdxyEntityTypeChoice selectedChoice)
            {
                typeName = selectedChoice.TypeName ?? string.Empty;
                typeDisplayName = selectedChoice.DisplayName ?? string.Empty;
            }
            else
            {
                string sampleTypeText = (_sampleTypeValueCombo.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(sampleTypeText))
                {
                    SdxyEntityTypeChoice matchedChoice = _availableTypes.FirstOrDefault(choice =>
                        string.Equals(choice.DisplayName, sampleTypeText, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(choice.TypeName, sampleTypeText, StringComparison.OrdinalIgnoreCase));
                    if (matchedChoice != null)
                    {
                        typeName = matchedChoice.TypeName ?? string.Empty;
                        typeDisplayName = matchedChoice.DisplayName ?? string.Empty;
                    }
                    else
                    {
                        typeName = sampleTypeText;
                        typeDisplayName = sampleTypeText;
                    }
                }
            }

            string layerName = (_sampleLayerValueCombo.Text ?? string.Empty).Trim();
            string linetypeName = (_sampleLinetypeValueTextBox.Text ?? string.Empty).Trim();
            string colorKey = (_sampleColorValueTextBox.Text ?? string.Empty).Trim();
            string blockName = (_sampleBlockNameValueTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(typeName) &&
                string.IsNullOrWhiteSpace(layerName) &&
                string.IsNullOrWhiteSpace(linetypeName) &&
                string.IsNullOrWhiteSpace(colorKey) &&
                string.IsNullOrWhiteSpace(blockName))
            {
                return null;
            }

            string colorDisplayName = colorKey;
            if (_draftSettings.SampleDescriptor != null &&
                string.Equals(_draftSettings.SampleDescriptor.ColorKey, colorKey, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_draftSettings.SampleDescriptor.ColorDisplayName))
            {
                colorDisplayName = _draftSettings.SampleDescriptor.ColorDisplayName;
            }

            return new SdxySampleDescriptor(
                typeName,
                typeDisplayName,
                layerName,
                linetypeName,
                colorKey,
                colorDisplayName,
                blockName);
        }

        private List<SdxySampleDescriptor> BuildSampleDescriptorListFromUi()
        {
            List<SdxySampleDescriptor> samples = _draftSettings.SampleDescriptors
                .Where(sample => sample != null)
                .Select(sample => sample.Clone())
                .ToList();

            SdxySampleDescriptor currentEditorSample = BuildSampleDescriptorFromEditors();
            if (_selectedSampleIndex >= 0 && _selectedSampleIndex < samples.Count)
            {
                if (currentEditorSample == null)
                {
                    samples.RemoveAt(_selectedSampleIndex);
                }
                else
                {
                    samples[_selectedSampleIndex] = currentEditorSample;
                }
            }
            else if (currentEditorSample != null)
            {
                samples.Add(currentEditorSample);
            }

            return samples;
        }

        private void LoadSampleListItems()
        {
            List<SdxySampleDescriptor> samples = _draftSettings.SampleDescriptors
                .Where(sample => sample != null)
                .ToList();

            _sampleListBox.Items.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                string summary = samples[i].BuildSummary();
                _sampleListBox.Items.Add($"{i + 1}. {summary}");
            }

            _sampleListCountLabel.Text = $"Dang luu {samples.Count} sample object.";
            if (samples.Count == 0)
            {
                _selectedSampleIndex = -1;
                _sampleListBox.ClearSelected();
                return;
            }

            if (_selectedSampleIndex < 0 || _selectedSampleIndex >= samples.Count)
            {
                _selectedSampleIndex = samples.Count - 1;
            }

            if (_sampleListBox.Items.Count > 0)
            {
                _suppressSampleEditorEvents = true;
                try
                {
                    _sampleListBox.SelectedIndex = _selectedSampleIndex;
                }
                finally
                {
                    _suppressSampleEditorEvents = false;
                }
            }
        }

        private SdxySampleDescriptor GetSelectedOrCurrentSample()
        {
            if (_selectedSampleIndex >= 0 &&
                _selectedSampleIndex < _draftSettings.SampleDescriptors.Count)
            {
                return _draftSettings.SampleDescriptors[_selectedSampleIndex];
            }

            return null;
        }

        private void LoadSelectedSampleIntoEditors()
        {
            if (_suppressSampleEditorEvents)
            {
                return;
            }

            _selectedSampleIndex = _sampleListBox.SelectedIndex;
            SdxySampleDescriptor sample = GetSelectedOrCurrentSample();

            _suppressSampleEditorEvents = true;
            try
            {
                SetSampleTypeEditorValue(sample);
                _sampleLayerValueCombo.Text = sample?.LayerName ?? string.Empty;
                _sampleLinetypeValueTextBox.Text = sample?.LinetypeName ?? string.Empty;
                _sampleColorValueTextBox.Text = sample?.ColorKey ?? string.Empty;
                _sampleBlockNameValueTextBox.Text = sample?.BlockName ?? string.Empty;
            }
            finally
            {
                _suppressSampleEditorEvents = false;
            }

            RefreshSampleEditorState();
        }

        private void AddCurrentSampleFromEditor()
        {
            SdxySampleDescriptor sample = BuildSampleDescriptorFromEditors();
            if (sample == null)
            {
                return;
            }

            _draftSettings.SampleDescriptors.Add(sample);
            _selectedSampleIndex = _draftSettings.SampleDescriptors.Count - 1;
            LoadSampleListItems();
            RefreshSampleEditorState();
        }

        private void RemoveSelectedSample()
        {
            if (_selectedSampleIndex < 0 || _selectedSampleIndex >= _draftSettings.SampleDescriptors.Count)
            {
                return;
            }

            _draftSettings.SampleDescriptors.RemoveAt(_selectedSampleIndex);
            if (_selectedSampleIndex >= _draftSettings.SampleDescriptors.Count)
            {
                _selectedSampleIndex = _draftSettings.SampleDescriptors.Count - 1;
            }

            LoadSampleListItems();
            LoadSelectedSampleIntoEditors();
            RefreshSampleEditorState();
        }

        private void ClearSampleEditor()
        {
            _selectedSampleIndex = -1;
            _suppressSampleEditorEvents = true;
            try
            {
                _sampleListBox.ClearSelected();
                _sampleTypeValueCombo.SelectedItem = null;
                _sampleTypeValueCombo.Text = string.Empty;
                _sampleLayerValueCombo.Text = string.Empty;
                _sampleLinetypeValueTextBox.Text = string.Empty;
                _sampleColorValueTextBox.Text = string.Empty;
                _sampleBlockNameValueTextBox.Text = string.Empty;
            }
            finally
            {
                _suppressSampleEditorEvents = false;
            }

            RefreshSampleEditorState();
        }

        private void RefreshSampleEditorState()
        {
            if (_suppressSampleEditorEvents)
            {
                return;
            }

            if (_selectedSampleIndex >= 0 && _selectedSampleIndex < _draftSettings.SampleDescriptors.Count)
            {
                SdxySampleDescriptor selectedSample = BuildSampleDescriptorFromEditors();
                if (selectedSample != null)
                {
                    _draftSettings.SampleDescriptors[_selectedSampleIndex] = selectedSample;
                    LoadSampleListItems();
                }
            }

            SdxySampleDescriptor sample = BuildSampleDescriptorFromEditors();
            List<string> activeConditions = new List<string>();

            if (_sampleTypeCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.TypeName))
            {
                activeConditions.Add("Type");
            }

            if (_sampleLayerCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.LayerName))
            {
                activeConditions.Add("Layer");
            }

            if (_sampleLinetypeCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.LinetypeName))
            {
                activeConditions.Add("Linetype");
            }

            if (_sampleColorCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.ColorKey))
            {
                activeConditions.Add("Color");
            }

            if (_sampleBlockNameCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.BlockName))
            {
                activeConditions.Add("Block");
            }

            _sampleSummaryLabel.Text = sample == null
                ? "Chua co sample/filter value. Bam Pick sample de lay nhanh, hoac nhap tay cac attribute ben duoi."
                : sample.BuildSummary() + Environment.NewLine +
                  "Dang match: " +
                  (activeConditions.Count == 0 ? "chua chon attribute nao." : string.Join(" + ", activeConditions));

            UpdateSampleCheckBoxState(_sampleTypeCheckBox, !string.IsNullOrWhiteSpace(sample?.TypeName));
            UpdateSampleCheckBoxState(_sampleLayerCheckBox, !string.IsNullOrWhiteSpace(sample?.LayerName));
            UpdateSampleCheckBoxState(_sampleLinetypeCheckBox, !string.IsNullOrWhiteSpace(sample?.LinetypeName));
            UpdateSampleCheckBoxState(_sampleColorCheckBox, !string.IsNullOrWhiteSpace(sample?.ColorKey));
            UpdateSampleCheckBoxState(_sampleBlockNameCheckBox, !string.IsNullOrWhiteSpace(sample?.BlockName));
            RefreshFilterPreview();
        }

        private static void UpdateSampleCheckBoxState(WF.CheckBox checkBox, bool isEnabled)
        {
            if (checkBox == null)
            {
                return;
            }

            checkBox.Enabled = isEnabled;
            if (!isEnabled)
            {
                checkBox.Checked = false;
            }
        }
    }

    // ======================================================
    // ENTRY POINT CỦA PLUGIN
    // Initialize/Terminate được AutoCAD gọi khi NETLOAD hoặc bundle autoload.
    // Đây cũng là nơi khởi tạo tracker DXPALETTE và Ribbon.
    // ======================================================
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
            DungXPaletteHost.Terminate();
            DungXRibbonHost.Terminate();
        }
    }

    // ======================================================
    // RIBBON DUNGX
    // Tạo tab/panel/nút ribbon từ danh sách command trong project.
    // Nếu đổi tên CommandMethod thủ công, nhớ cập nhật các mảng command dưới đây nếu muốn Ribbon chạy đúng.
    // ======================================================
    internal static class DungXRibbonHost
    {
        private const string TabId = "DUNGX_RIBBON_TAB";
        // Các command hiện lên panel Dimension.
        private static readonly string[] DimensionCommands =
            { "DAA_Dim_auto", "DDD_Dim_4_direction", "SDXY", "BD_CHANGE_POSITION_DIM", "CDD2_CHIADIM" };

        // Các command hiện lên panel Stretch.
        private static readonly string[] StretchCommands =
            { "SS", "SSD", "SSD2_SMART_STRETCH_BY_DIM2" };

        // Các command tiện ích workspace/palette/ribbon.
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
                ["SDXY"] = new RibbonCommandStyle(
                    "Smart Dim XY",
                    "Smart\nDim XY",
                    "Dim XY",
                    "DXY",
                    "Dimension to the nearest object along X or Y based on click direction.",
                    "SX",
                    Color.FromArgb(25, 67, 75),
                    Color.FromArgb(0, 196, 176)),
                ["CDD2_CHIADIM"] = new RibbonCommandStyle(
                    "Split Dimension",
                    "Split\nDim",
                    "Split",
                    "CD",
                    "Split an existing dimension into multiple segments.",
                    "CD",
                    Color.FromArgb(63, 45, 86),
                    Color.FromArgb(176, 112, 255)),
                ["BD_CHANGE_POSITION_DIM"] = new RibbonCommandStyle(
                    "Move Dim Placement",
                    "Move\nDim Pos",
                    "Dim Pos",
                    "BD",
                    "Move the placement point of selected dimensions to a clicked point.",
                    "BD",
                    Color.FromArgb(48, 62, 82),
                    Color.FromArgb(116, 172, 255)),
                ["SS"] = new RibbonCommandStyle(
                    "Smart Stretch",
                    "Smart\nStretch",
                    "Stretch",
                    "SS",
                    "Window-based smart stretch with preview.",
                    "SS",
                    Color.FromArgb(90, 48, 32),
                    Color.FromArgb(255, 144, 64)),
                ["SSD"] = new RibbonCommandStyle(
                    "Stretch By Dim",
                    "Stretch\nBy Dim",
                    "By Dim",
                    "SD",
                    "Smart stretch with L derived from two dimensions.",
                    "SB",
                    Color.FromArgb(96, 58, 28),
                    Color.FromArgb(255, 172, 82)),
                ["SSD2_SMART_STRETCH_BY_DIM2"] = new RibbonCommandStyle(
                    "Stretch By Dim2",
                    "Stretch\nBy Dim2",
                    "By Dim2",
                    "S2",
                    "Smart stretch with L = |dim1 - dim2| / 2 and two stretch passes.",
                    "S2",
                    Color.FromArgb(110, 70, 34),
                    Color.FromArgb(255, 196, 106)),
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

    // ======================================================
    // DXPALETTE HOST
    // Quản lý vòng đời PaletteSet: tạo palette, reload data, auto-open khi mở AutoCAD.
    // Phần UI thật nằm trong DungXPaletteControl bên dưới.
    // ======================================================
    internal static class DungXPaletteHost
    {
        private static readonly Guid PaletteGuid =
            new Guid("2E5D6E63-70A5-4D41-B72B-50BFC66F37D1");

        private static PaletteSet _paletteSet;
        private static DungXPaletteControl _paletteControl;

        public static void Initialize()
        {
            EnsurePalette();
            PaletteCommandUsageTracker.Initialize();
            if (PaletteStartupStore.LoadAutoShow())
            {
                ReloadPaletteData(false);
                _paletteSet.Visible = true;
            }
        }

        public static void Terminate()
        {
            PaletteCommandUsageTracker.Terminate();
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
            _paletteControl?.SetStatus(
                $"Dang chay {item.CommandName} | {item.SourceLabel}");
        }

        public static void NotifyCommandUsage(string commandName, int usageCount)
        {
            _paletteControl?.RecordUsage(commandName, usageCount);
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

    // ======================================================
    // DXPALETTE UI
    // Bảng command chính:
    // - Filter theo Source/Type/Search.
    // - Favorite, sort Custom/A-Z/Used.
    // - Đếm số lần dùng command.
    // - Lưu width cột, layout, favorite, usage.
    // Lưu ý khi sửa UI: cố gắng chỉ sửa render/event UI, tránh đụng store nếu không cần.
    // ======================================================
    internal sealed class DungXPaletteControl : WF.UserControl
    {
        private static readonly Color BackgroundColor = Color.FromArgb(12, 12, 12);
        private static readonly Color PanelColor = Color.FromArgb(18, 18, 18);
        private static readonly Color BorderColor = Color.FromArgb(42, 42, 42);
        private static readonly Color ForegroundColor = Color.FromArgb(241, 241, 241);
        private static readonly Color AccentColor = Color.FromArgb(94, 94, 94);
        private static readonly Color SelectionColor = Color.FromArgb(28, 28, 28);
        private static readonly Color CardColor = Color.FromArgb(20, 20, 20);
        private static readonly Color CardBorderColor = Color.FromArgb(58, 58, 58);
        private static readonly Color CardShadowColor = Color.FromArgb(8, 8, 8);
        private static readonly Color HeaderAccentColor = Color.FromArgb(72, 72, 72);
        private static readonly Color MutedBadgeColor = Color.FromArgb(40, 40, 40);
        private static readonly Color FavoriteOnColor = Color.FromArgb(255, 204, 64);
        private static readonly Color FavoriteOffColor = Color.FromArgb(112, 112, 112);

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
        private readonly WF.Button _reloadButton;
        private readonly WF.Button _folderButton;
        private readonly WF.Button _refreshButton;
        private readonly WF.Button _addSourceButton;
        private readonly WF.Button _addManualButton;
        private readonly WF.Button _removeSourceButton;
        private readonly WF.Button _resetUsageButton;
        private readonly WF.Label _summaryLabel;
        private readonly WF.Label _usageSummaryLabel;
        private readonly WF.Label _statusLabel;
        private readonly WF.CheckBox _autoShowCheckBox;
        private List<PaletteCommandItem> _items;
        private Point _dragStartPoint;
        private int _dragRowIndex = -1;
        private bool _isApplyingColumnWidths;
        private int _hoveredCommandRowIndex = -1;
        private int _pressedCommandRowIndex = -1;

        public DungXPaletteControl()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            Dock = WF.DockStyle.Fill;
            BackColor = BackgroundColor;
            ForeColor = ForegroundColor;
            Font = new System.Drawing.Font(
                "Segoe UI",
                9F,
                FontStyle.Regular,
                GraphicsUnit.Point);

            PaletteChromePanel chromePanel = new PaletteChromePanel
            {
                Dock = WF.DockStyle.Fill,
                Padding = new WF.Padding(12),
                Margin = new WF.Padding(0),
                BackColor = BackgroundColor
            };
            Controls.Add(chromePanel);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new WF.Padding(4),
                BackColor = PanelColor
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            chromePanel.Controls.Add(layout);

            PaletteTitlePanel titlePanel = new PaletteTitlePanel
            {
                Dock = WF.DockStyle.Top,
                Margin = new WF.Padding(0, 0, 0, 6)
            };
            layout.Controls.Add(titlePanel, 0, 0);

            _filterPanel = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                ColumnCount = 8,
                AutoSize = true,
                BackColor = PanelColor
            };
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 140f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            layout.Controls.Add(_filterPanel, 0, 1);

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
                "A-Z",
                "Used"
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
                Margin = new WF.Padding(0, 4, 0, 4),
                BackColor = PanelColor
            };
            layout.Controls.Add(_buttonPanel, 0, 2);

            _reloadButton = CreateButton("Reload LISP", (_, __) => ReloadLisps());
            _folderButton = CreateButton("LISP Folder", (_, __) => PickFolder());
            _addSourceButton = CreateButton("Add Source", (_, __) => AddSource());
            _addManualButton = CreateButton("Add Manual", (_, __) => AddManualAlias());
            _removeSourceButton = CreateButton("Remove Source", (_, __) => RemoveSelectedSource());
            _refreshButton = CreateButton("Refresh List", (_, __) => ReloadData(true));
            _resetUsageButton = CreateButton("Reset Stats", (_, __) => ResetUsageStats());
            _autoShowCheckBox = CreateCheckBox("Auto Open", AutoShowCheckBox_CheckedChanged);

            _buttonPanel.Controls.Add(_reloadButton);
            _buttonPanel.Controls.Add(_folderButton);
            _buttonPanel.Controls.Add(_addSourceButton);
            _buttonPanel.Controls.Add(_addManualButton);
            _buttonPanel.Controls.Add(_removeSourceButton);
            _buttonPanel.Controls.Add(_refreshButton);
            _buttonPanel.Controls.Add(_resetUsageButton);
            _buttonPanel.Controls.Add(_autoShowCheckBox);

            _summaryLabel = CreateLabel("Tong lenh: 0");
            _summaryLabel.Dock = WF.DockStyle.Fill;
            _summaryLabel.Padding = new WF.Padding(0, 2, 0, 6);
            _summaryLabel.Margin = new WF.Padding(0, 0, 0, 2);
            _summaryLabel.AutoEllipsis = true;
            _summaryLabel.ForeColor = Color.FromArgb(196, 196, 196);
            _summaryLabel.Font = new System.Drawing.Font(
                "Segoe UI",
                8.25F,
                FontStyle.Bold,
                GraphicsUnit.Point);
            layout.Controls.Add(_summaryLabel, 0, 3);

            _usageSummaryLabel = CreateLabel("Thong ke dung: chua co du lieu");
            _usageSummaryLabel.Dock = WF.DockStyle.Fill;
            _usageSummaryLabel.Padding = new WF.Padding(0, 0, 0, 6);
            _usageSummaryLabel.Margin = new WF.Padding(0, 0, 0, 2);
            _usageSummaryLabel.AutoEllipsis = true;
            _usageSummaryLabel.ForeColor = Color.FromArgb(156, 156, 156);
            _usageSummaryLabel.Font = new System.Drawing.Font(
                "Segoe UI",
                8.25F,
                FontStyle.Bold,
                GraphicsUnit.Point);
            layout.Controls.Add(_usageSummaryLabel, 0, 4);

            _commandGrid = CreateGrid();
            _commandGrid.AllowDrop = true;
            _commandGrid.CellClick += CommandGrid_CellClick;
            _commandGrid.KeyDown += CommandGrid_KeyDown;
            _commandGrid.CellEndEdit += CommandGrid_CellEndEdit;
            _commandGrid.MouseDown += CommandGrid_MouseDown;
            _commandGrid.MouseMove += CommandGrid_MouseMove;
            _commandGrid.MouseUp += CommandGrid_MouseUp;
            _commandGrid.MouseLeave += CommandGrid_MouseLeave;
            _commandGrid.DragOver += CommandGrid_DragOver;
            _commandGrid.DragDrop += CommandGrid_DragDrop;
            _commandGrid.ColumnWidthChanged += CommandGrid_ColumnWidthChanged;
            _commandGrid.CellPainting += CommandGrid_CellPainting;
            layout.Controls.Add(_commandGrid, 0, 5);
            EnableDoubleBuffer(_commandGrid);
            ApplySavedColumnWidths();

            _statusLabel = CreateLabel("San sang");
            _statusLabel.Dock = WF.DockStyle.Fill;
            _statusLabel.Padding = new WF.Padding(0, 8, 0, 0);
            _statusLabel.ForeColor = Color.FromArgb(186, 190, 198);
            layout.Controls.Add(_statusLabel, 0, 6);

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
            PaletteCommandUsageTracker.SetKnownCommands(_items.Select(item => item.CommandName));
            PaletteUsageStore.ApplyUsage(_items);
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

            _reloadButton.Text = compact ? "LISP" : "Reload LISP";
            _folderButton.Text = compact ? "Dir" : "LISP Folder";
            _addSourceButton.Text = compact ? "+Src" : "Add Source";
            _addManualButton.Text = compact ? "+Cmd" : "Add Manual";
            _removeSourceButton.Text = compact ? "-Src" : "Remove Source";
            _refreshButton.Text = compact ? "Ref" : "Refresh List";
            _resetUsageButton.Text = compact ? "Reset" : "Reset Stats";

            _commandGrid.Columns["Favorite"].Visible = true;
            _commandGrid.Columns["Used"].Visible = true;
            _commandGrid.Columns["Description"].Visible = true;
            _commandGrid.Columns["Source"].Visible = true;
            _commandGrid.Columns["Favorite"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Command"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Used"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Description"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Source"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;

            _statusLabel.Visible = !ultraCompact;

            _filterPanel.ResumeLayout();
            _buttonPanel.ResumeLayout();
        }

        private void BindGrid(string preferredCommandName = null)
        {
            // Rebind toàn bộ grid khi filter/sort/layout thay đổi.
            // Với usage count sau khi command chạy, code ưu tiên cập nhật nhẹ để tránh palette bị lag.
            preferredCommandName = preferredCommandName ?? GetSelectedCommandName();
            List<PaletteCommandItem> filteredItems = GetFilteredItems();
            UpdateSummary(filteredItems);

            _commandGrid.Rows.Clear();

            foreach (PaletteCommandItem item in filteredItems)
            {
                int rowIndex =
                    _commandGrid.Rows.Add(
                        item.IsFavorite ? "★" : "☆",
                        item.CommandName,
                        item.UsageCount,
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

        private void UpdateSummary(IReadOnlyCollection<PaletteCommandItem> filteredItems)
        {
            IReadOnlyCollection<PaletteCommandItem> allItems =
                _items ?? (IReadOnlyCollection<PaletteCommandItem>)Array.Empty<PaletteCommandItem>();
            IReadOnlyCollection<PaletteCommandItem> visibleItems =
                filteredItems ?? (IReadOnlyCollection<PaletteCommandItem>)Array.Empty<PaletteCommandItem>();
            int totalUsage = allItems
                .GroupBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .Sum(group => group.Max(item => item.UsageCount));
            int usedCommandCount = allItems
                .GroupBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .Count(group => group.Max(item => item.UsageCount) > 0);

            List<string> sourceParts = allItems
                .GroupBy(item => item.SourceLabel ?? string.Empty)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}: {group.Count()}")
                .ToList();

            string sourceSummary = sourceParts.Count == 0
                ? "Chua co source nao"
                : string.Join(" | ", sourceParts);

            _summaryLabel.Text =
                $"Tong lenh: {allItems.Count} | Dang hien: {visibleItems.Count} | Theo nguon: {sourceSummary}";
            _usageSummaryLabel.Text = totalUsage > 0
                ? $"Tong luot dung: {totalUsage} | So lenh da dung: {usedCommandCount}"
                : "Tong luot dung: 0 | So lenh da dung: 0";
        }

        private static WF.DataGridView CreateGrid()
        {
            WF.DataGridView grid = new WF.DataGridView
            {
                Dock = WF.DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = true,
                MultiSelect = false,
                SelectionMode = WF.DataGridViewSelectionMode.FullRowSelect,
                EditMode = WF.DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = PanelColor,
                BorderStyle = WF.BorderStyle.FixedSingle,
                GridColor = BorderColor,
                CellBorderStyle = WF.DataGridViewCellBorderStyle.SingleHorizontal,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ScrollBars = WF.ScrollBars.Both,
                AutoSizeColumnsMode = WF.DataGridViewAutoSizeColumnsMode.None,
                RowTemplate = { Height = 30 }
            };

            grid.ColumnHeadersHeight = 30;
            grid.ColumnHeadersHeightSizeMode = WF.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            grid.ColumnHeadersBorderStyle = WF.DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ForegroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = ForegroundColor;
            grid.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font(
                "Segoe UI",
                8.75F,
                FontStyle.Bold,
                GraphicsUnit.Point);

            grid.DefaultCellStyle.BackColor = PanelColor;
            grid.DefaultCellStyle.ForeColor = ForegroundColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.DefaultCellStyle.SelectionForeColor = ForegroundColor;
            grid.DefaultCellStyle.Padding = new WF.Padding(4, 2, 4, 2);

            grid.AlternatingRowsDefaultCellStyle.BackColor = PanelColor;
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
                Width = 150,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn usedColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Used",
                HeaderText = "Used",
                Width = 54,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            usedColumn.DefaultCellStyle.Alignment = WF.DataGridViewContentAlignment.MiddleCenter;
            WF.DataGridViewTextBoxColumn descriptionColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "Description",
                Width = 210,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn sourceColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Source",
                HeaderText = "Source",
                Width = 110,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };

            grid.Columns.AddRange(
                favoriteColumn,
                commandColumn,
                usedColumn,
                descriptionColumn,
                sourceColumn);
            return grid;
        }

        private static WF.Label CreateLabel(string text)
        {
            return new WF.Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = ForegroundColor,
                BackColor = PanelColor,
                Anchor = WF.AnchorStyles.Left
            };
        }

        private static WF.Button CreateButton(string text, EventHandler onClick, bool primary = true)
        {
            PaletteToolbarButton button = new PaletteToolbarButton
            {
                Text = text,
                IsPrimary = primary,
                Font = new System.Drawing.Font(
                    "Segoe UI",
                    8.5F,
                    FontStyle.Bold,
                    GraphicsUnit.Point)
            };
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
                BackColor = PanelColor,
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
            SetStatus(
                mode == PaletteSortMode.Custom
                    ? "Dang sap xep theo yeu thich + thu tu tuy chinh."
                    : mode == PaletteSortMode.Used
                        ? "Dang sap xep theo so lan su dung."
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
                case PaletteSortMode.Used:
                    return items
                        .OrderByDescending(item => item.UsageCount)
                        .ThenByDescending(item => item.IsFavorite)
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
            if (string.Equals(selected, "A-Z", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Alphabetical;
            }

            if (string.Equals(selected, "Used", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Used;
            }

            return PaletteSortMode.Custom;
        }

        private void SetSortMode(PaletteSortMode mode)
        {
            string label =
                mode == PaletteSortMode.Alphabetical
                    ? "A-Z"
                    : mode == PaletteSortMode.Used
                        ? "Used"
                        : "Custom";
            int index = _sortModeFilter.FindStringExact(label);
            _sortModeFilter.SelectedIndex = index >= 0 ? index : 0;
        }

        private string GetSelectedCommandName()
        {
            return GetSelectedItem()?.CommandName;
        }

        private void CommandGrid_CellClick(object sender, WF.DataGridViewCellEventArgs e)
        {
            // Click 1 lần vào cột Command là chạy lệnh.
            // Click vào cột Favorite chỉ bật/tắt sao, không chạy lệnh.
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[e.RowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            string columnName = _commandGrid.Columns[e.ColumnIndex].Name;
            if (string.Equals(columnName, "Command", StringComparison.OrdinalIgnoreCase))
            {
                RunItem(item);
                return;
            }

            if (!string.Equals(
                columnName,
                "Favorite",
                StringComparison.OrdinalIgnoreCase))
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
            if (e.Button == WF.MouseButtons.Left &&
                hit.RowIndex >= 0 &&
                hit.ColumnIndex >= 0 &&
                string.Equals(_commandGrid.Columns[hit.ColumnIndex].Name, "Command", StringComparison.OrdinalIgnoreCase))
            {
                _pressedCommandRowIndex = hit.RowIndex;
                _commandGrid.InvalidateRow(hit.RowIndex);
            }
        }

        private void CommandGrid_MouseMove(object sender, WF.MouseEventArgs e)
        {
            UpdateHoveredCommandRow(e.Location);

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

        private void CommandGrid_MouseUp(object sender, WF.MouseEventArgs e)
        {
            if (_pressedCommandRowIndex >= 0)
            {
                int previousPressed = _pressedCommandRowIndex;
                _pressedCommandRowIndex = -1;
                _commandGrid.InvalidateRow(previousPressed);
            }
        }

        private void CommandGrid_MouseLeave(object sender, EventArgs e)
        {
            if (_hoveredCommandRowIndex >= 0)
            {
                int previousHovered = _hoveredCommandRowIndex;
                _hoveredCommandRowIndex = -1;
                _commandGrid.InvalidateRow(previousHovered);
            }

            if (_pressedCommandRowIndex >= 0)
            {
                int previousPressed = _pressedCommandRowIndex;
                _pressedCommandRowIndex = -1;
                _commandGrid.InvalidateRow(previousPressed);
            }

            _commandGrid.Cursor = WF.Cursors.Default;
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

        private void UpdateHoveredCommandRow(Point location)
        {
            WF.DataGridView.HitTestInfo hit = _commandGrid.HitTest(location.X, location.Y);
            int hoveredRow = -1;
            bool isCommandCell = hit.RowIndex >= 0 &&
                hit.ColumnIndex >= 0 &&
                string.Equals(_commandGrid.Columns[hit.ColumnIndex].Name, "Command", StringComparison.OrdinalIgnoreCase);

            if (isCommandCell)
            {
                hoveredRow = hit.RowIndex;
            }

            if (_hoveredCommandRowIndex != hoveredRow)
            {
                int previousHovered = _hoveredCommandRowIndex;
                _hoveredCommandRowIndex = hoveredRow;

                if (previousHovered >= 0 && previousHovered < _commandGrid.Rows.Count)
                {
                    _commandGrid.InvalidateRow(previousHovered);
                }

                if (_hoveredCommandRowIndex >= 0 && _hoveredCommandRowIndex < _commandGrid.Rows.Count)
                {
                    _commandGrid.InvalidateRow(_hoveredCommandRowIndex);
                }
            }

            _commandGrid.Cursor = isCommandCell ? WF.Cursors.Hand : WF.Cursors.Default;
        }

        private void CommandGrid_ColumnWidthChanged(object sender, WF.DataGridViewColumnEventArgs e)
        {
            if (_isApplyingColumnWidths || e?.Column == null || e.Column.Width <= 0)
            {
                return;
            }

            PaletteLayoutStore.SaveColumnWidths(GetCurrentColumnWidths());
        }

        private Dictionary<string, int> GetCurrentColumnWidths()
        {
            Dictionary<string, int> widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (WF.DataGridViewColumn column in _commandGrid.Columns)
            {
                if (column == null || string.IsNullOrWhiteSpace(column.Name) || column.Width <= 0)
                {
                    continue;
                }

                widths[column.Name] = column.Width;
            }

            return widths;
        }

        private void ApplySavedColumnWidths()
        {
            if (_commandGrid.Columns.Count == 0)
            {
                return;
            }

            Dictionary<string, int> widths = PaletteLayoutStore.LoadColumnWidths();
            if (widths.Count == 0)
            {
                return;
            }

            _isApplyingColumnWidths = true;
            try
            {
                foreach (KeyValuePair<string, int> entry in widths)
                {
                    if (!_commandGrid.Columns.Contains(entry.Key))
                    {
                        continue;
                    }

                    _commandGrid.Columns[entry.Key].Width = Math.Max(24, entry.Value);
                }
            }
            finally
            {
                _isApplyingColumnWidths = false;
            }
        }

        private void CommandGrid_CellPainting(object sender, WF.DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = _commandGrid.Columns[e.ColumnIndex].Name;
            if (e.RowIndex < 0)
            {
                PaintPaletteHeaderCell(e);
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[e.RowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            if (string.Equals(columnName, "Command", StringComparison.OrdinalIgnoreCase))
            {
                PaintCommandButtonCell(e, item);
                return;
            }

            if (string.Equals(columnName, "Favorite", StringComparison.OrdinalIgnoreCase))
            {
                PaintFavoriteCell(e, item);
            }
        }

        private void PaintPaletteHeaderCell(WF.DataGridViewCellPaintingEventArgs e)
        {
            e.Handled = true;
            e.PaintBackground(e.CellBounds, false);

            using (SolidBrush backBrush = new SolidBrush(BackgroundColor))
            {
                e.Graphics.FillRectangle(backBrush, e.CellBounds);
            }

            Rectangle accentRect = new Rectangle(
                e.CellBounds.X,
                e.CellBounds.Bottom - 3,
                e.CellBounds.Width,
                3);
            using (SolidBrush accentBrush = new SolidBrush(HeaderAccentColor))
            {
                e.Graphics.FillRectangle(accentBrush, accentRect);
            }

            Rectangle textBounds = Rectangle.Inflate(e.CellBounds, -8, -4);
            WF.TextRenderer.DrawText(
                e.Graphics,
                Convert.ToString(e.FormattedValue) ?? string.Empty,
                _commandGrid.ColumnHeadersDefaultCellStyle.Font ?? _commandGrid.Font,
                textBounds,
                ForegroundColor,
                WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);
        }

        private void PaintCommandButtonCell(WF.DataGridViewCellPaintingEventArgs e, PaletteCommandItem item)
        {
            e.Handled = true;
            PaintRowBackground(e);

            bool selected = e.State.HasFlag(WF.DataGridViewElementStates.Selected);
            bool hovered = e.RowIndex == _hoveredCommandRowIndex;
            bool pressed = e.RowIndex == _pressedCommandRowIndex;
            (Color topColor, Color bottomColor, Color borderColor) = GetCommandButtonColors(
                item,
                selected,
                hovered,
                pressed);

            Rectangle buttonBounds = Rectangle.Inflate(e.CellBounds, -4, -4);
            Rectangle contentBounds = buttonBounds;
            if (pressed)
            {
                contentBounds.Offset(0, 1);
            }

            if (!pressed)
            {
                Rectangle shadowBounds = buttonBounds;
                shadowBounds.Offset(0, 1);
                using (GraphicsPath shadowPath = CreatePaletteRoundedRectangle(shadowBounds, 4))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                {
                    e.Graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            using (GraphicsPath buttonPath = CreatePaletteRoundedRectangle(contentBounds, 4))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                contentBounds,
                topColor,
                bottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(borderColor))
            using (Pen innerBorderPen = new Pen(Color.FromArgb(56, 255, 255, 255)))
            {
                e.Graphics.FillPath(fillBrush, buttonPath);

                Rectangle glossBounds = new Rectangle(
                    contentBounds.X + 1,
                    contentBounds.Y + 1,
                    Math.Max(1, contentBounds.Width - 2),
                    Math.Max(5, (contentBounds.Height / 2) - 1));
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(buttonPath);
                using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                    glossBounds,
                    Color.FromArgb(hovered ? 44 : 34, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(glossBrush, glossBounds);
                }
                e.Graphics.Restore(state);

                e.Graphics.DrawPath(borderPen, buttonPath);
                e.Graphics.DrawPath(innerBorderPen, buttonPath);
            }

            if (hovered && !pressed)
            {
                using (GraphicsPath hoverPath = CreatePaletteRoundedRectangle(contentBounds, 4))
                using (Pen hoverPen = new Pen(Color.FromArgb(112, 150, 196)))
                {
                    e.Graphics.DrawPath(hoverPen, hoverPath);
                }
            }

            Rectangle textBounds = Rectangle.Inflate(contentBounds, -8, -1);
            using (System.Drawing.Font buttonFont = new System.Drawing.Font(
                "Segoe UI",
                8.75F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    item.CommandName,
                    buttonFont,
                    textBounds,
                    ForegroundColor,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);
            }
        }

        private void PaintUsageBadgeCell(WF.DataGridViewCellPaintingEventArgs e, PaletteCommandItem item)
        {
            e.Handled = true;
            PaintRowBackground(e);

            bool selected = e.State.HasFlag(WF.DataGridViewElementStates.Selected);
            bool hasUsage = item.UsageCount > 0;

            Rectangle badgeBounds = new Rectangle(
                e.CellBounds.X + 10,
                e.CellBounds.Y + 8,
                Math.Max(24, e.CellBounds.Width - 20),
                Math.Max(18, e.CellBounds.Height - 16));

            Color badgeTop = hasUsage
                ? (selected ? Color.FromArgb(112, 126, 148) : Color.FromArgb(86, 98, 118))
                : (selected ? Color.FromArgb(124, 128, 138) : MutedBadgeColor);
            Color badgeBottom = hasUsage
                ? (selected ? Color.FromArgb(88, 100, 118) : Color.FromArgb(66, 74, 90))
                : (selected ? Color.FromArgb(96, 100, 108) : Color.FromArgb(60, 64, 72));

            using (LinearGradientBrush badgeBrush = new LinearGradientBrush(
                badgeBounds,
                badgeTop,
                badgeBottom,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(Color.FromArgb(92, 92, 92)))
            {
                e.Graphics.FillRectangle(badgeBrush, badgeBounds);
                e.Graphics.DrawRectangle(borderPen, badgeBounds);
            }

            using (System.Drawing.Font badgeFont = new System.Drawing.Font(
                "Segoe UI",
                8.5F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    item.UsageCount.ToString(CultureInfo.InvariantCulture),
                    badgeFont,
                    badgeBounds,
                    ForegroundColor,
                    WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.NoPadding);
            }
        }

        private void PaintFavoriteCell(WF.DataGridViewCellPaintingEventArgs e, PaletteCommandItem item)
        {
            e.Handled = true;
            PaintRowBackground(e);

            string starText = item.IsFavorite ? "★" : "☆";
            Color starColor = item.IsFavorite ? FavoriteOnColor : FavoriteOffColor;
            using (System.Drawing.Font starFont = new System.Drawing.Font(
                "Segoe UI Symbol",
                item.IsFavorite ? 12F : 11F,
                item.IsFavorite ? FontStyle.Bold : FontStyle.Regular,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    starText,
                    starFont,
                    e.CellBounds,
                    starColor,
                    WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.NoPadding);
            }
        }

        private void PaintRowBackground(WF.DataGridViewCellPaintingEventArgs e)
        {
            Color backColor = e.State.HasFlag(WF.DataGridViewElementStates.Selected)
                ? SelectionColor
                : PanelColor;

            using (SolidBrush backBrush = new SolidBrush(backColor))
            using (Pen separatorPen = new Pen(BorderColor))
            {
                e.Graphics.FillRectangle(backBrush, e.CellBounds);
                e.Graphics.DrawLine(
                    separatorPen,
                    e.CellBounds.Left,
                    e.CellBounds.Bottom - 1,
                    e.CellBounds.Right,
                    e.CellBounds.Bottom - 1);
            }
        }

        private void PaintCommandGlyph(Graphics graphics, Rectangle bounds, PaletteCommandItem item, bool selected)
        {
            Color chipTop = selected ? Color.FromArgb(255, 255, 255) : Color.FromArgb(240, 244, 250);
            Color chipBottom = selected ? Color.FromArgb(215, 223, 235) : Color.FromArgb(206, 214, 228);
            Color stroke = selected ? Color.FromArgb(34, 42, 58) : Color.FromArgb(48, 56, 74);

            using (GraphicsPath chipPath = CreatePaletteRoundedRectangle(bounds, 5))
            using (LinearGradientBrush chipBrush = new LinearGradientBrush(
                bounds,
                chipTop,
                chipBottom,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(Color.FromArgb(160, 172, 192)))
            using (Pen iconPen = new Pen(stroke, 1.6f))
            using (SolidBrush iconBrush = new SolidBrush(stroke))
            {
                iconPen.StartCap = LineCap.Round;
                iconPen.EndCap = LineCap.Round;
                iconPen.LineJoin = LineJoin.Round;

                graphics.FillPath(chipBrush, chipPath);
                graphics.DrawPath(borderPen, chipPath);

                Rectangle iconBounds = Rectangle.Inflate(bounds, -3, -3);
                switch (GetCommandGlyphKind(item))
                {
                    case "dim":
                        DrawDimGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                    case "stretch":
                        DrawStretchGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                    case "text":
                        DrawTextGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                    case "block":
                        DrawBlockGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                    case "ui":
                        DrawUiGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                    case "reload":
                        DrawRefreshGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                    default:
                        DrawCommandGlyph(graphics, iconPen, iconBrush, iconBounds);
                        break;
                }
            }
        }

        private static string GetCommandGlyphKind(PaletteCommandItem item)
        {
            string commandName = item?.CommandName?.ToUpperInvariant() ?? string.Empty;
            if (commandName.Contains("DIM") || commandName.StartsWith("DAA") || commandName.StartsWith("DDD") || commandName.StartsWith("CDD"))
            {
                return "dim";
            }

            if (commandName.Contains("STRETCH") || commandName.StartsWith("SS"))
            {
                return "stretch";
            }

            if (commandName.Contains("TEXT") || commandName.StartsWith("TT"))
            {
                return "text";
            }

            if (commandName.Contains("BLOCK") || commandName.StartsWith("BBB") || commandName.StartsWith("CCC"))
            {
                return "block";
            }

            if (commandName.Contains("PALETTE") || commandName.Contains("RIBBON"))
            {
                return "ui";
            }

            if (commandName.Contains("RELOAD") || item?.SourceKind == PaletteSourceKind.ActionMacro)
            {
                return "reload";
            }

            return "generic";
        }

        private static System.Drawing.Bitmap CreatePaletteToolbarIcon(string buttonText, Color iconColor)
        {
            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(18, 18, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(iconColor, 1.8f))
            using (SolidBrush brush = new SolidBrush(iconColor))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                Rectangle bounds = new Rectangle(1, 1, 16, 16);
                string key = GetToolbarGlyphKind(buttonText);
                switch (key)
                {
                    case "run":
                        DrawRunGlyph(graphics, pen, brush, bounds);
                        break;
                    case "folder":
                        DrawFolderGlyph(graphics, pen, brush, bounds);
                        break;
                    case "add":
                        DrawAddGlyph(graphics, pen, brush, bounds);
                        break;
                    case "remove":
                        DrawRemoveGlyph(graphics, pen, brush, bounds);
                        break;
                    case "refresh":
                        DrawRefreshGlyph(graphics, pen, brush, bounds);
                        break;
                    case "reset":
                        DrawResetGlyph(graphics, pen, brush, bounds);
                        break;
                    default:
                        DrawUiGlyph(graphics, pen, brush, bounds);
                        break;
                }
            }

            return bitmap;
        }

        private static string GetToolbarGlyphKind(string buttonText)
        {
            string text = (buttonText ?? string.Empty).ToUpperInvariant();
            if (text.Contains("RUN"))
            {
                return "run";
            }

            if (text.Contains("FOLDER"))
            {
                return "folder";
            }

            if (text.Contains("ADD"))
            {
                return "add";
            }

            if (text.Contains("REMOVE"))
            {
                return "remove";
            }

            if (text.Contains("RESET"))
            {
                return "reset";
            }

            if (text.Contains("REFRESH") || text.Contains("RELOAD"))
            {
                return "refresh";
            }

            return "ui";
        }

        private static void DrawDimGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            int midY = bounds.Top + bounds.Height / 2;
            int left = bounds.Left + 2;
            int right = bounds.Right - 2;
            graphics.DrawLine(pen, left, bounds.Top + 2, left, bounds.Bottom - 2);
            graphics.DrawLine(pen, right, bounds.Top + 2, right, bounds.Bottom - 2);
            graphics.DrawLine(pen, left + 1, midY, right - 1, midY);
            graphics.FillPolygon(brush, new[]
            {
                new Point(left + 1, midY),
                new Point(left + 5, midY - 3),
                new Point(left + 5, midY + 3)
            });
            graphics.FillPolygon(brush, new[]
            {
                new Point(right - 1, midY),
                new Point(right - 5, midY - 3),
                new Point(right - 5, midY + 3)
            });
        }

        private static void DrawStretchGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            Rectangle rect = new Rectangle(bounds.Left + 1, bounds.Top + 4, bounds.Width - 7, bounds.Height - 8);
            graphics.DrawRectangle(pen, rect);
            int arrowX = rect.Right + 1;
            int arrowY = bounds.Top + bounds.Height / 2;
            graphics.DrawLine(pen, rect.Right - 1, arrowY, arrowX + 2, arrowY);
            graphics.FillPolygon(brush, new[]
            {
                new Point(arrowX + 2, arrowY),
                new Point(arrowX - 1, arrowY - 3),
                new Point(arrowX - 1, arrowY + 3)
            });
        }

        private static void DrawTextGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            int top = bounds.Top + 2;
            int centerX = bounds.Left + bounds.Width / 2;
            graphics.DrawLine(pen, bounds.Left + 2, top, bounds.Right - 2, top);
            graphics.DrawLine(pen, centerX, top, centerX, bounds.Bottom - 2);
            graphics.DrawLine(pen, bounds.Left + 4, bounds.Bottom - 3, bounds.Right - 4, bounds.Bottom - 3);
        }

        private static void DrawBlockGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            Rectangle back = new Rectangle(bounds.Left + 2, bounds.Top + 2, bounds.Width - 7, bounds.Height - 7);
            Rectangle front = new Rectangle(bounds.Left + 5, bounds.Top + 5, bounds.Width - 7, bounds.Height - 7);
            graphics.DrawRectangle(pen, back);
            graphics.DrawRectangle(pen, front);
            graphics.FillRectangle(brush, front.Left + 3, front.Top + 3, 3, 3);
        }

        private static void DrawUiGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            Rectangle panel = new Rectangle(bounds.Left + 1, bounds.Top + 2, bounds.Width - 2, bounds.Height - 4);
            graphics.DrawRectangle(pen, panel);
            graphics.DrawLine(pen, panel.Left + 4, panel.Top + 1, panel.Left + 4, panel.Bottom - 1);
            graphics.FillRectangle(brush, panel.Left + 6, panel.Top + 3, panel.Width - 9, 2);
            graphics.FillRectangle(brush, panel.Left + 6, panel.Top + 7, panel.Width - 12, 2);
            graphics.FillRectangle(brush, panel.Left + 6, panel.Top + 11, panel.Width - 10, 2);
        }

        private static void DrawCommandGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            int midY = bounds.Top + bounds.Height / 2;
            graphics.DrawLine(pen, bounds.Left + 3, midY, bounds.Left + 7, midY);
            graphics.DrawLine(pen, bounds.Left + 3, midY, bounds.Left + 6, midY - 3);
            graphics.DrawLine(pen, bounds.Left + 3, midY, bounds.Left + 6, midY + 3);
            graphics.DrawLine(pen, bounds.Left + 9, bounds.Bottom - 4, bounds.Right - 3, bounds.Bottom - 4);
        }

        private static void DrawRefreshGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            Rectangle arc = new Rectangle(bounds.Left + 3, bounds.Top + 3, bounds.Width - 7, bounds.Height - 7);
            graphics.DrawArc(pen, arc, 30, 260);
            Point tip = new Point(bounds.Right - 2, bounds.Top + 6);
            graphics.FillPolygon(brush, new[]
            {
                tip,
                new Point(tip.X - 5, tip.Y - 1),
                new Point(tip.X - 2, tip.Y + 4)
            });
        }

        private static void DrawRunGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            graphics.FillPolygon(brush, new[]
            {
                new Point(bounds.Left + 4, bounds.Top + 3),
                new Point(bounds.Right - 3, bounds.Top + bounds.Height / 2),
                new Point(bounds.Left + 4, bounds.Bottom - 3)
            });
        }

        private static void DrawFolderGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddLines(new[]
            {
                new Point(bounds.Left + 2, bounds.Top + 6),
                new Point(bounds.Left + 5, bounds.Top + 3),
                new Point(bounds.Left + 9, bounds.Top + 3),
                new Point(bounds.Left + 11, bounds.Top + 5),
                new Point(bounds.Right - 2, bounds.Top + 5),
                new Point(bounds.Right - 3, bounds.Bottom - 3),
                new Point(bounds.Left + 2, bounds.Bottom - 3),
                new Point(bounds.Left + 2, bounds.Top + 6)
            });
            graphics.DrawPath(pen, path);
            path.Dispose();
        }

        private static void DrawAddGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            DrawFolderGlyph(graphics, pen, brush, bounds);
            graphics.DrawLine(
                pen,
                bounds.Left + bounds.Width / 2,
                bounds.Top + 6,
                bounds.Left + bounds.Width / 2,
                bounds.Bottom - 4);
            graphics.DrawLine(
                pen,
                bounds.Left + 4,
                bounds.Top + bounds.Height / 2,
                bounds.Right - 4,
                bounds.Top + bounds.Height / 2);
        }

        private static void DrawRemoveGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            graphics.DrawEllipse(pen, bounds.Left + 2, bounds.Top + 2, bounds.Width - 5, bounds.Height - 5);
            graphics.DrawLine(
                pen,
                bounds.Left + 4,
                bounds.Top + bounds.Height / 2,
                bounds.Right - 4,
                bounds.Top + bounds.Height / 2);
        }

        private static void DrawResetGlyph(Graphics graphics, Pen pen, Brush brush, Rectangle bounds)
        {
            DrawRefreshGlyph(graphics, pen, brush, bounds);
            graphics.DrawLine(pen, bounds.Left + 5, bounds.Bottom - 4, bounds.Right - 5, bounds.Bottom - 4);
        }

        private static (Color topColor, Color bottomColor, Color borderColor) GetCommandButtonColors(
            PaletteCommandItem item,
            bool selected,
            bool hovered,
            bool pressed)
        {
            if (pressed)
            {
                return (
                    Color.FromArgb(24, 26, 30),
                    Color.FromArgb(12, 12, 14),
                    Color.FromArgb(86, 90, 96));
            }

            if (hovered)
            {
                return (
                    Color.FromArgb(46, 50, 58),
                    Color.FromArgb(22, 24, 28),
                    Color.FromArgb(116, 126, 142));
            }

            if (selected)
            {
                return (
                    Color.FromArgb(38, 40, 46),
                    Color.FromArgb(18, 18, 20),
                    Color.FromArgb(96, 102, 114));
            }

            return (
                Color.FromArgb(32, 34, 38),
                Color.FromArgb(16, 16, 18),
                Color.FromArgb(72, 74, 78));
        }

        private static GraphicsPath CreatePaletteRoundedRectangle(Rectangle bounds, int radius)
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

        private static void EnableDoubleBuffer(WF.Control control)
        {
            if (control == null)
            {
                return;
            }

            try
            {
                PropertyInfo property = typeof(WF.Control).GetProperty(
                    "DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                property?.SetValue(control, true, null);
            }
            catch
            {
            }
        }

        private List<PaletteCommandItem> GetFilteredItems()
        {
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

            return ApplySortMode(filtered).ToList();
        }

        private void RunSelected()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon command.");
                return;
            }

            RunItem(item);
        }

        private void RunItem(PaletteCommandItem item)
        {
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

        private void ResetUsageStats()
        {
            WF.DialogResult result = WF.MessageBox.Show(
                "Ban co chac muon reset toan bo thong ke su dung command?",
                "Reset DungX Stats",
                WF.MessageBoxButtons.YesNo,
                WF.MessageBoxIcon.Question);
            if (result != WF.DialogResult.Yes)
            {
                return;
            }

            PaletteUsageStore.Reset();
            foreach (PaletteCommandItem item in _items)
            {
                item.UsageCount = 0;
            }

            BindGrid(GetSelectedCommandName());
            SetStatus("Da reset thong ke su dung command.");
        }

        public void RecordUsage(string commandName, int usageCount)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            string selectedCommandName = GetSelectedCommandName();
            foreach (PaletteCommandItem item in _items.Where(current =>
                string.Equals(current.CommandName, commandName, StringComparison.OrdinalIgnoreCase)))
            {
                item.UsageCount = usageCount;
            }

            if (GetCurrentSortMode() == PaletteSortMode.Used)
            {
                BindGrid(selectedCommandName);
                return;
            }

            UpdateSummary(GetFilteredItems());

            foreach (WF.DataGridViewRow row in _commandGrid.Rows)
            {
                PaletteCommandItem item = row.Tag as PaletteCommandItem;
                if (item == null)
                {
                    continue;
                }

                row.Cells["Used"].Value = item.UsageCount;
            }
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

    // ======================================================
    // SS / SSD / SSD2 - SMART STRETCH
    // SS  : nhập L thủ công rồi stretch theo hướng click.
    // SSD : lấy L = |DIM1 - DIM2|.
    // SSD2: lấy L = |DIM1 - DIM2| / 2 và chạy 2 lượt stretch.
    // Lưu ý: phần stretch cuối gọi STRETCH gốc của AutoCAD để giữ hành vi gần chuẩn nhất.
    // ======================================================
    public class SmartStretchCommands
    {
        private const double ComparisonTolerance = 1e-6;

        // SS:
        // - Dùng ngay L đã lưu từ lần trước.
        // - Nếu cần đổi L thì gõ keyword Length ngay tại prompt chọn điểm đầu.
        // - Quét một hoặc nhiều crossing window.
        // - Chọn điểm đầu + điểm hướng để quyết định SX+/SX-/SY+/SY-.
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

            RunSmartStretchLoopWithLength(
                ed,
                db,
                SmartStretchSettingsStore.LoadLength(),
                "SS",
                allowInteractiveLengthOverride: true);
        }

        // SSD:
        // - Chọn 2 DIM.
        // - L = trị tuyệt đối chênh lệch measurement của 2 DIM.
        // - Sau đó chạy cùng core stretch với SS.
        [CommandMethod("SSD")]
        public void SmartStretchByDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            if (!TryPromptStretchLengthFromDimensions(
                ed,
                db,
                halfDifference: false,
                out double length))
            {
                return;
            }

            RunSmartStretchLoopWithLength(ed, db, length, "SSD");
        }

        // SSD2_SMART_STRETCH_BY_DIM2:
        // - Chọn 2 DIM.
        // - L = |DIM1 - DIM2| / 2.
        // - Chạy 2 pass để xử lý các đối tượng đối xứng.
        [CommandMethod("SSD2_SMART_STRETCH_BY_DIM2")]
        public void SmartStretchByHalfDimDifference()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            if (!TryPromptStretchLengthFromDimensions(
                ed,
                db,
                halfDifference: true,
                out double length))
            {
                return;
            }

            RunSmartStretchLoopWithLength(
                ed,
                db,
                length,
                "SSD2_SMART_STRETCH_BY_DIM2",
                passCount: 2);
        }

        private static void RunSmartStretchLoopWithLength(
            Editor ed,
            Database db,
            double initialLength,
            string commandLabel,
            int passCount = 1,
            bool allowInteractiveLengthOverride = false)
        {
            // Core dùng chung cho SS/SSD/SSD2.
            // Sau mỗi lượt stretch sẽ quay lại chọn tiếp.
            // Chỉ dừng khi người dùng nhấn Space/Enter hoặc Esc.
            // Tắt OSMODE tạm thời để điểm click không bị OSNAP kéo lệch.
            // Khi kết thúc/cancel luôn khôi phục OSMODE cũ.
            object previousOsMode = null;
            double length = initialLength;

            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");
                Application.SetSystemVariable("OSMODE", 0);

                SmartStretchSettingsStore.SaveLength(length);

                while (true)
                {
                    for (int pass = 1; pass <= passCount; pass++)
                    {
                        while (true)
                        {
                            if (passCount > 1)
                            {
                                ed.WriteMessage(
                                    $"\n{commandLabel}: thực hiện stretch lần {pass}/{passCount} với L = {length.ToString("0.###", CultureInfo.InvariantCulture)}.");
                            }

                            SmartStretchLoopResult result = RunSingleSmartStretchWithLength(
                                ed,
                                db,
                                ref length,
                                passCount > 1
                                    ? $"{commandLabel} [{pass}/{passCount}]"
                                    : commandLabel,
                                allowInteractiveLengthOverride);

                            if (result == SmartStretchLoopResult.Completed)
                            {
                                break;
                            }

                            if (result == SmartStretchLoopResult.StopRequested)
                            {
                                return;
                            }
                        }
                    }
                }
            }
            finally
            {
                if (previousOsMode != null)
                {
                    Application.SetSystemVariable("OSMODE", previousOsMode);
                }
            }
        }

        private static SmartStretchLoopResult RunSingleSmartStretchWithLength(
            Editor ed,
            Database db,
            ref double length,
            string commandLabel,
            bool allowInteractiveLengthOverride)
        {
            SmartStretchSelectionInput selectionInput =
                GetSmartStretchSelectionInput(ed, ref length, out bool stopRequested);
            if (stopRequested)
            {
                return SmartStretchLoopResult.StopRequested;
            }

            if (selectionInput == null)
            {
                return SmartStretchLoopResult.Retry;
            }

            ShowSmartStretchSelection(ed, selectionInput.SelectedObjectIds);

            if (!TryPromptSmartStretchStartPoint(
                ed,
                ref length,
                allowInteractiveLengthOverride,
                commandLabel,
                out Point3d startPoint,
                out bool stopAtStartPointPrompt))
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return stopAtStartPointPrompt
                    ? SmartStretchLoopResult.StopRequested
                    : SmartStretchLoopResult.Retry;
            }

            PromptResult directionResult = GetDirectionWithPreview(
                ed,
                selectionInput,
                startPoint,
                length,
                out SmartStretchDirection direction,
                out Point3d secondPoint);
            if (directionResult.Status == PromptStatus.Cancel)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartStretchLoopResult.StopRequested;
            }

            if (directionResult.Status != PromptStatus.OK)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartStretchLoopResult.Retry;
            }

            if (direction == SmartStretchDirection.None)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                ed.WriteMessage("\nKhông xác định được hướng stretch. Hãy làm lại.");
                return SmartStretchLoopResult.Retry;
            }

            ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
            ExecuteNativeStretch(ed, selectionInput, startPoint, secondPoint);

            ed.WriteMessage(
                $"\n{commandLabel}: đã gọi STRETCH gốc theo {GetDirectionLabel(direction)} với L = {FormatLength(length)}.");
            return SmartStretchLoopResult.Completed;
        }

        private static bool TryPromptSmartStretchStartPoint(
            Editor ed,
            ref double length,
            bool allowInteractiveLengthOverride,
            string commandLabel,
            out Point3d startPoint,
            out bool stopRequested)
        {
            startPoint = Point3d.Origin;
            stopRequested = false;

            while (true)
            {
                string message = allowInteractiveLengthOverride
                    ? $"\nChọn điểm đầu hoặc [Length] <{FormatLength(length)}> để đổi L, Space/Enter để kết thúc: "
                    : "\nChọn điểm đầu hoặc Space/Enter để kết thúc: ";

                PromptPointOptions startPointOptions = new PromptPointOptions(message);
                startPointOptions.AllowNone = true;

                if (allowInteractiveLengthOverride)
                {
                    startPointOptions.AppendKeywordsToMessage = false;
                    startPointOptions.Keywords.Add("Length");
                }

                PromptPointResult startResult = ed.GetPoint(startPointOptions);
                if (startResult.Status == PromptStatus.None ||
                    startResult.Status == PromptStatus.Cancel)
                {
                    stopRequested = true;
                    return false;
                }

                if (startResult.Status == PromptStatus.Keyword &&
                    string.Equals(startResult.StringResult, "Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryPromptStretchLength(ed, length, out double updatedLength))
                    {
                        length = updatedLength;
                        SmartStretchSettingsStore.SaveLength(length);
                        ed.WriteMessage($"\n{commandLabel}: cập nhật L = {FormatLength(length)}.");
                    }

                    continue;
                }

                if (startResult.Status != PromptStatus.OK)
                {
                    return false;
                }

                startPoint = startResult.Value;
                return true;
            }
        }

        private static bool TryPromptStretchLength(Editor ed, double defaultLength, out double length)
        {
            PromptDoubleOptions lengthOptions =
                new PromptDoubleOptions(
                    $"\nNhập L cho smart stretch <{defaultLength.ToString("0.###", CultureInfo.InvariantCulture)}>:");
            lengthOptions.AllowNegative = false;
            lengthOptions.AllowZero = false;
            lengthOptions.AllowNone = true;
            lengthOptions.DefaultValue = defaultLength;
            lengthOptions.UseDefaultValue = true;

            PromptDoubleResult lengthResult = ed.GetDouble(lengthOptions);
            if (lengthResult.Status == PromptStatus.Cancel)
            {
                length = 0.0;
                return false;
            }

            length = lengthResult.Status == PromptStatus.None
                ? defaultLength
                : lengthResult.Value;

            if (length <= ComparisonTolerance)
            {
                ed.WriteMessage("\nGiá trị L phải lớn hơn 0.");
                return false;
            }

            return true;
        }

        private static string FormatLength(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryPromptStretchLengthFromDimensions(
            Editor ed,
            Database db,
            bool halfDifference,
            out double length)
        {
            length = 0.0;

            while (true)
            {
                if (!TryPromptDimensionMeasurement(
                    ed,
                    db,
                    "\nChọn dim gốc: ",
                    out double baseMeasurement))
                {
                    return false;
                }

                if (!TryPromptDimensionMeasurement(
                    ed,
                    db,
                    "\nChọn dim hiện hành: ",
                    out double currentMeasurement))
                {
                    return false;
                }

                double difference = Math.Abs(baseMeasurement - currentMeasurement);
                length = halfDifference ? difference / 2.0 : difference;
                if (length <= ComparisonTolerance)
                {
                    ed.WriteMessage("\nHai dim đang cho chênh lệch bằng 0. Hãy chọn lại.");
                    continue;
                }

                if (halfDifference)
                {
                    ed.WriteMessage(
                        $"\nL = (|{baseMeasurement.ToString("0.###", CultureInfo.InvariantCulture)} - {currentMeasurement.ToString("0.###", CultureInfo.InvariantCulture)}|) / 2 = {length.ToString("0.###", CultureInfo.InvariantCulture)}");
                }
                else
                {
                    ed.WriteMessage(
                        $"\nL = |{baseMeasurement.ToString("0.###", CultureInfo.InvariantCulture)} - {currentMeasurement.ToString("0.###", CultureInfo.InvariantCulture)}| = {length.ToString("0.###", CultureInfo.InvariantCulture)}");
                }
                return true;
            }
        }

        private static bool TryPromptDimensionMeasurement(
            Editor ed,
            Database db,
            string message,
            out double measurement)
        {
            measurement = 0.0;

            while (true)
            {
                PromptEntityOptions options = new PromptEntityOptions(message);
                options.SetRejectMessage("\nChỉ hỗ trợ các loại DIM hợp lệ.");
                options.AddAllowedClass(typeof(Dimension), false);

                PromptEntityResult result = ed.GetEntity(options);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Dimension dimension =
                        tr.GetObject(result.ObjectId, OpenMode.ForRead) as Dimension;
                    if (dimension == null)
                    {
                        ed.WriteMessage("\nKhông đọc được dim. Hãy chọn lại.");
                        continue;
                    }

                    measurement = Math.Abs(dimension.Measurement);
                    if (measurement <= ComparisonTolerance)
                    {
                        ed.WriteMessage("\nDim có giá trị không hợp lệ. Hãy chọn lại.");
                        continue;
                    }

                    return true;
                }
            }
        }

        private static SmartStretchSelectionInput GetSmartStretchSelectionInput(
            Editor ed,
            ref double length,
            out bool stopRequested)
        {
            // Cho phép quét nhiều crossing window.
            // Mỗi window được lưu để lúc gọi STRETCH gốc truyền đúng vùng crossing.
            // Space/Enter ở ngay window đầu tiên sẽ thoát hẳn command loop.
            stopRequested = false;
            ed.WriteMessage(
                "\nWindow: quet nhieu vung neu can. Giu Shift khi quet de loai bot doi tuong dang bi overlap. Nhan Space/Enter o goc dau khi chua quet window nao de thoat, hoac sau khi da quet it nhat 1 window de sang buoc stretch.");

            List<SmartStretchWindowSelection> windows = new List<SmartStretchWindowSelection>();
            HashSet<ObjectId> selectedIds = new HashSet<ObjectId>();
            Dictionary<ObjectId, List<SmartStretchWindowSelection>> effectiveWindowsByObject =
                new Dictionary<ObjectId, List<SmartStretchWindowSelection>>();
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
                                ? $"\nChọn góc đầu crossing window hoặc [Length] <{FormatLength(length)}> để đổi L, Space/Enter để thoát: "
                                : $"\nChọn góc đầu crossing window tiếp theo hoặc [Length] <{FormatLength(length)}> để đổi L, Space/Enter để stretch: ");
                    firstCornerOptions.AllowNone = true;
                    firstCornerOptions.AppendKeywordsToMessage = false;
                    firstCornerOptions.Keywords.Add("Length");

                    PromptPointResult firstCornerResult = ed.GetPoint(firstCornerOptions);
                    if (firstCornerResult.Status == PromptStatus.Keyword &&
                        string.Equals(firstCornerResult.StringResult, "Length", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryPromptStretchLength(ed, length, out double updatedLength))
                        {
                            length = updatedLength;
                            SmartStretchSettingsStore.SaveLength(length);
                            ed.WriteMessage(
                                $"\nSS: cập nhật L hiện tại = {FormatLength(length)}.");
                        }

                        continue;
                    }

                    if (firstCornerResult.Status == PromptStatus.None)
                    {
                        if (windows.Count == 0)
                        {
                            stopRequested = true;
                            ClearSmartStretchSelection(selectedIds.ToArray());
                            return null;
                        }

                        break;
                    }

                    if (firstCornerResult.Status == PromptStatus.Cancel)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        stopRequested = true;
                        return null;
                    }

                    if (firstCornerResult.Status != PromptStatus.OK)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        return null;
                    }

                    bool removeSelection = IsShiftPressed();

                    PromptCornerOptions secondCornerOptions =
                        new PromptCornerOptions(
                            "\nChọn góc đối diện crossing window: ",
                            firstCornerResult.Value);
                    PromptPointResult secondCornerResult = ed.GetCorner(secondCornerOptions);
                    if (secondCornerResult.Status == PromptStatus.Cancel)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        stopRequested = true;
                        return null;
                    }

                    if (secondCornerResult.Status != PromptStatus.OK)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        return null;
                    }

                    removeSelection = removeSelection || IsShiftPressed();

                    PromptSelectionResult crossingResult = ed.SelectCrossingWindow(
                        firstCornerResult.Value,
                        secondCornerResult.Value);
                    if (crossingResult.Status != PromptStatus.OK || crossingResult.Value == null)
                    {
                        ed.WriteMessage("\nWindow này chưa bắt được đối tượng nào.");
                        continue;
                    }

                    ObjectId[] crossingIds = crossingResult.Value.GetObjectIds();
                    SmartStretchSelectionMode mode = removeSelection
                        ? SmartStretchSelectionMode.Remove
                        : SmartStretchSelectionMode.Add;
                    SmartStretchWindowSelection windowSelection =
                        new SmartStretchWindowSelection(
                            firstCornerResult.Value,
                            secondCornerResult.Value,
                            mode);

                    if (mode == SmartStretchSelectionMode.Remove)
                    {
                        ObjectId[] previouslySelectedIds = selectedIds.ToArray();
                        int removedCount = 0;

                        foreach (ObjectId objectId in crossingIds)
                        {
                            if (selectedIds.Remove(objectId))
                            {
                                removedCount++;
                                effectiveWindowsByObject.Remove(objectId);
                            }
                        }

                        if (removedCount == 0)
                        {
                            ed.WriteMessage(
                                "\nShift window này không loại được đối tượng nào trong tập chọn hiện tại.");
                            continue;
                        }

                        windows.Add(windowSelection);
                        RefreshSmartStretchSelection(previouslySelectedIds, selectedIds.ToArray());
                        ed.WriteMessage(
                            $"\nĐã loại {removedCount} đối tượng. Còn lại {selectedIds.Count} đối tượng.");
                    }
                    else
                    {
                        windows.Add(windowSelection);

                        foreach (ObjectId objectId in crossingIds)
                        {
                            selectedIds.Add(objectId);

                            if (!effectiveWindowsByObject.TryGetValue(
                                objectId,
                                out List<SmartStretchWindowSelection> objectWindows))
                            {
                                objectWindows = new List<SmartStretchWindowSelection>();
                                effectiveWindowsByObject[objectId] = objectWindows;
                            }

                            objectWindows.Add(windowSelection);
                        }

                        ShowSmartStretchSelection(ed, selectedIds.ToArray());
                        ed.WriteMessage(
                            $"\nĐã gom {selectedIds.Count} đối tượng. Có thể quét thêm hoặc giữ Shift để loại bớt rồi nhấn Space/Enter để tiếp tục.");
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

            if (windows.Count == 0 || selectedIds.Count == 0)
            {
                ed.WriteMessage("\nChưa có đối tượng nào được chọn.");
                return null;
            }

            return SmartStretchSelectionInput.CreateSelection(
                windows,
                selectedIds,
                effectiveWindowsByObject);
        }

        private enum SmartStretchLoopResult
        {
            Completed,
            StopRequested,
            Retry
        }

        private static void ExecuteNativeStretch(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d basePoint,
            Point3d secondPoint)
        {
            // Gọi STRETCH gốc bằng các crossing window đã lưu.
            // Zoom tạm vào vùng stretch để giảm lỗi AutoCAD bỏ sót object ngoài màn hình.
            ViewTableRecord originalView = null;

            try
            {
                originalView = ed.GetCurrentView();
                Extents3d stretchBounds =
                    GetStretchOperationBounds(selectionInput, basePoint, secondPoint);
                ZoomToStretchBounds(ed, stretchBounds);

                List<object> args = new List<object> { "_.STRETCH" };
                SmartStretchSelectionMode currentMode = SmartStretchSelectionMode.Add;

                foreach (SmartStretchWindowSelection window in selectionInput.Windows)
                {
                    if (window.Mode != currentMode)
                    {
                        args.Add(
                            window.Mode == SmartStretchSelectionMode.Remove
                                ? "_R"
                                : "_A");
                        currentMode = window.Mode;
                    }

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
            // Jig preview: rê chuột để xem hướng stretch trước khi click.
            // Preview là mô phỏng, kết quả cuối vẫn do STRETCH gốc xử lý.
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

        private static void RefreshSmartStretchSelection(
            ObjectId[] previousObjectIds,
            ObjectId[] currentObjectIds)
        {
            if (previousObjectIds != null && previousObjectIds.Length > 0)
            {
                ClearSmartStretchSelection(previousObjectIds);
            }

            if (currentObjectIds != null && currentObjectIds.Length > 0)
            {
                ShowSmartStretchSelection(
                    Application.DocumentManager.MdiActiveDocument?.Editor,
                    currentObjectIds);
            }
        }

        private static List<int> FindStretchIndicesInsideWindow(
            ObjectId sourceObjectId,
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
                .Where(item => selectionInput.GetEffectiveWindowsForObject(sourceObjectId).Any(window =>
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsShiftPressed()
        {
            const int ShiftVirtualKey = 0x10;

            if ((GetAsyncKeyState(ShiftVirtualKey) & 0x8000) != 0)
            {
                return true;
            }

            return (WF.Control.ModifierKeys & WF.Keys.Shift) == WF.Keys.Shift;
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
                            sourceEntity.ObjectId,
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

    // ======================================================
    // IPP / IPS - INSERT PHAO PG / PGS
    // - Chọn 2 DIM để lấy RỘNG và CAO.
    // - Nhập số ôm tường để suy ra tên block: PG-xx hoặc PGS-xx.
    // - Chèn block và cập nhật dynamic properties CAO / RONG.
    // ======================================================
    public class InsertPhaoCommands
    {
        private const double InsertPhaoTolerance = 1e-6;
        private const int DefaultWrapOnWall = 60;

        [CommandMethod("IPP_INSERT_PG")]
        public void InsertPg()
        {
            RunInsertPhaoCommand("PG", "IPP_INSERT_PG");
        }

        [CommandMethod("IPS_INSERT_PGS")]
        public void InsertPgs()
        {
            RunInsertPhaoCommand("PGS", "IPS_INSERT_PGS");
        }

        private static void RunInsertPhaoCommand(string blockPrefix, string commandLabel)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            if (!TryPromptWrapOnWall(ed, out int wrapOnWall))
            {
                return;
            }

            if (!TryPromptDimensionMeasurement(
                ed,
                db,
                "\nChọn DIM chiều RỘNG: ",
                out double valueWidth))
            {
                return;
            }

            if (!TryPromptDimensionMeasurement(
                ed,
                db,
                "\nChọn DIM chiều CAO: ",
                out double valueHeight))
            {
                return;
            }

            string blockName = blockPrefix + "-" + wrapOnWall.ToString(CultureInfo.InvariantCulture);
            if (!TryPromptInsertionPoint(ed, out Point3d insertionPoint))
            {
                return;
            }

            if (!TryGetOrLoadBlockDefinition(db, blockName, out ObjectId blockDefinitionId, out string errorMessage))
            {
                ed.WriteMessage($"\n{commandLabel}: {errorMessage}");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    ed.WriteMessage($"\n{commandLabel}: không mở được không gian hiện tại để chèn block.");
                    return;
                }

                BlockReference blockReference =
                    new BlockReference(insertionPoint, blockDefinitionId)
                    {
                        ScaleFactors = new Scale3d(1.0),
                        Rotation = 0.0
                    };

                currentSpace.AppendEntity(blockReference);
                tr.AddNewlyCreatedDBObject(blockReference, true);

                AppendBlockAttributes(blockReference, tr);
                UpdateDynamicProperties(blockReference, valueWidth, valueHeight);
                blockReference.RecordGraphicsModified(true);

                tr.Commit();
            }

            ed.WriteMessage(
                $"\n{commandLabel}: đã chèn block {blockName} và cập nhật tham số động.");
        }

        private static bool TryPromptWrapOnWall(Editor ed, out int wrapOnWall)
        {
            PromptIntegerOptions options =
                new PromptIntegerOptions(
                    $"\nNhập phào ôm tường <{DefaultWrapOnWall.ToString(CultureInfo.InvariantCulture)}>: ");
            options.AllowNegative = false;
            options.AllowZero = false;
            options.AllowNone = true;
            options.DefaultValue = DefaultWrapOnWall;
            options.UseDefaultValue = true;

            PromptIntegerResult result = ed.GetInteger(options);
            if (result.Status == PromptStatus.Cancel)
            {
                wrapOnWall = DefaultWrapOnWall;
                return false;
            }

            wrapOnWall = result.Status == PromptStatus.None
                ? DefaultWrapOnWall
                : result.Value;
            return true;
        }

        private static bool TryPromptDimensionMeasurement(
            Editor ed,
            Database db,
            string message,
            out double measurement)
        {
            measurement = 0.0;

            while (true)
            {
                PromptEntityOptions options = new PromptEntityOptions(message);
                options.SetRejectMessage("\nHãy chọn đúng đối tượng DIMENSION.");
                options.AddAllowedClass(typeof(Dimension), false);

                PromptEntityResult result = ed.GetEntity(options);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Dimension dimension =
                        tr.GetObject(result.ObjectId, OpenMode.ForRead) as Dimension;
                    if (dimension == null)
                    {
                        ed.WriteMessage("\nKhông đọc được DIM. Hãy chọn lại.");
                        continue;
                    }

                    measurement = Math.Abs(dimension.Measurement);
                    if (measurement <= InsertPhaoTolerance)
                    {
                        ed.WriteMessage("\nDIM có measurement không hợp lệ. Hãy chọn lại.");
                        continue;
                    }

                    return true;
                }
            }
        }

        private static bool TryPromptInsertionPoint(Editor ed, out Point3d insertionPoint)
        {
            PromptPointOptions options =
                new PromptPointOptions("\nChọn điểm chèn block: ");
            options.AllowNone = true;

            PromptPointResult result = ed.GetPoint(options);
            if (result.Status == PromptStatus.OK)
            {
                insertionPoint = result.Value;
                return true;
            }

            if (result.Status == PromptStatus.None)
            {
                object lastPoint = Application.GetSystemVariable("LASTPOINT");
                if (lastPoint is Point3d point)
                {
                    insertionPoint = point;
                    return true;
                }
            }

            insertionPoint = Point3d.Origin;
            return false;
        }

        private static bool TryGetOrLoadBlockDefinition(
            Database db,
            string blockName,
            out ObjectId blockDefinitionId,
            out string errorMessage)
        {
            blockDefinitionId = ObjectId.Null;
            errorMessage = string.Empty;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable =
                    tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                if (blockTable != null && blockTable.Has(blockName))
                {
                    blockDefinitionId = blockTable[blockName];
                    return true;
                }
            }

            string blockFilePath;
            try
            {
                blockFilePath = HostApplicationServices.Current.FindFile(
                    blockName + ".dwg",
                    db,
                    FindFileHint.Default);
            }
            catch
            {
                errorMessage =
                    $"không tìm thấy block '{blockName}' trong bản vẽ hoặc Support File Search Path.";
                return false;
            }

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(
                        blockFilePath,
                        FileOpenMode.OpenForReadAndAllShare,
                        false,
                        null);
                    blockDefinitionId = db.Insert(blockName, sourceDb, false);
                }
            }
            catch (System.Exception ex)
            {
                errorMessage = $"không nạp được block '{blockName}': {ex.Message}";
                return false;
            }

            return !blockDefinitionId.IsNull;
        }

        private static void UpdateDynamicProperties(
            BlockReference blockReference,
            double valueWidth,
            double valueHeight)
        {
            if (blockReference == null || !blockReference.IsDynamicBlock)
            {
                return;
            }

            foreach (DynamicBlockReferenceProperty property in blockReference.DynamicBlockReferencePropertyCollection)
            {
                if (property == null || property.ReadOnly)
                {
                    continue;
                }

                string propertyName = property.PropertyName ?? string.Empty;
                if (propertyName.Equals("CAO", StringComparison.OrdinalIgnoreCase))
                {
                    property.Value = valueHeight;
                }
                else if (propertyName.Equals("RONG", StringComparison.OrdinalIgnoreCase))
                {
                    property.Value = valueWidth;
                }
            }
        }

        private static void AppendBlockAttributes(BlockReference blockReference, Transaction tr)
        {
            BlockTableRecord definition =
                tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (definition == null || !definition.HasAttributeDefinitions)
            {
                return;
            }

            foreach (ObjectId entityId in definition)
            {
                AttributeDefinition attributeDefinition =
                    tr.GetObject(entityId, OpenMode.ForRead) as AttributeDefinition;
                if (attributeDefinition == null || attributeDefinition.Constant)
                {
                    continue;
                }

                AttributeReference attributeReference = new AttributeReference();
                attributeReference.SetAttributeFromBlock(
                    attributeDefinition,
                    blockReference.BlockTransform);
                attributeReference.Position =
                    attributeDefinition.Position.TransformBy(blockReference.BlockTransform);

                if (attributeReference.IsMTextAttribute)
                {
                    attributeReference.UpdateMTextAttribute();
                }

                blockReference.AttributeCollection.AppendAttribute(attributeReference);
                tr.AddNewlyCreatedDBObject(attributeReference, true);
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

        public Dictionary<ObjectId, List<SmartStretchWindowSelection>> EffectiveWindowsByObject
        {
            get;
            private set;
        }

        public static SmartStretchSelectionInput CreateSelection(
            IEnumerable<SmartStretchWindowSelection> windows,
            IEnumerable<ObjectId> selectedObjectIds,
            IDictionary<ObjectId, List<SmartStretchWindowSelection>> effectiveWindowsByObject)
        {
            return new SmartStretchSelectionInput
            {
                Windows = windows?.ToList() ?? new List<SmartStretchWindowSelection>(),
                SelectedObjectIds = selectedObjectIds?.ToArray() ?? new ObjectId[0],
                EffectiveWindowsByObject =
                    effectiveWindowsByObject?.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value?.ToList() ?? new List<SmartStretchWindowSelection>())
                    ?? new Dictionary<ObjectId, List<SmartStretchWindowSelection>>()
            };
        }

        public IEnumerable<SmartStretchWindowSelection> GetEffectiveWindowsForObject(
            ObjectId objectId)
        {
            if (EffectiveWindowsByObject != null &&
                EffectiveWindowsByObject.TryGetValue(
                    objectId,
                    out List<SmartStretchWindowSelection> windows))
            {
                return windows;
            }

            return Enumerable.Empty<SmartStretchWindowSelection>();
        }
    }

    internal enum SmartStretchSelectionMode
    {
        Add,
        Remove
    }

    internal sealed class SmartStretchWindowSelection
    {
        public SmartStretchWindowSelection(
            Point3d firstPoint,
            Point3d secondPoint,
            SmartStretchSelectionMode mode)
        {
            FirstPoint = firstPoint;
            SecondPoint = secondPoint;
            Mode = mode;
        }

        public Point3d FirstPoint { get; }

        public Point3d SecondPoint { get; }

        public SmartStretchSelectionMode Mode { get; }
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

    // Khung ngoài của DXPALETTE: vẽ nền tối, viền, bóng và accent line.
    // Chỉ phục vụ giao diện, không chứa logic command.
    internal sealed class PaletteChromePanel : WF.Panel
    {
        private static readonly Color OuterFrameColor = Color.FromArgb(14, 14, 14);
        private static readonly Color SurfaceTopColor = Color.FromArgb(38, 38, 40);
        private static readonly Color SurfaceBottomColor = Color.FromArgb(18, 18, 20);
        private static readonly Color BorderColor = Color.FromArgb(68, 68, 72);
        public PaletteChromePanel()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            DoubleBuffered = true;
        }

        protected override void OnPaintBackground(WF.PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(12, 12, 12));
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadowBounds = new Rectangle(2, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 7));
            using (GraphicsPath shadowPath = CreateRoundedPath(shadowBounds, 12))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(95, 0, 0, 0)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            Rectangle surfaceBounds = new Rectangle(0, 0, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            using (GraphicsPath surfacePath = CreateRoundedPath(surfaceBounds, 12))
            using (LinearGradientBrush surfaceBrush = new LinearGradientBrush(
                surfaceBounds,
                SurfaceTopColor,
                SurfaceBottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(BorderColor))
            using (Pen innerPen = new Pen(Color.FromArgb(46, 255, 255, 255)))
            using (Pen outerPen = new Pen(OuterFrameColor))
            {
                e.Graphics.FillPath(surfaceBrush, surfacePath);
                e.Graphics.DrawPath(outerPen, surfacePath);
                e.Graphics.DrawPath(borderPen, surfacePath);

                Rectangle innerBounds = Rectangle.Inflate(surfaceBounds, -1, -1);
                using (GraphicsPath innerPath = CreateRoundedPath(innerBounds, 10))
                {
                    e.Graphics.DrawPath(innerPen, innerPath);
                }
            }

            Rectangle highlightBounds = new Rectangle(2, 2, Math.Max(1, Width - 9), Math.Max(6, (Height / 5)));
            using (GraphicsPath highlightPath = CreateRoundedPath(highlightBounds, 10))
            using (LinearGradientBrush highlightBrush = new LinearGradientBrush(
                highlightBounds,
                Color.FromArgb(48, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                GraphicsState state = e.Graphics.Save();
                Rectangle clipBounds = new Rectangle(1, 1, Math.Max(1, Width - 6), Math.Max(1, Height - 6));
                using (GraphicsPath clipPath = CreateRoundedPath(clipBounds, 12))
                {
                    e.Graphics.SetClip(clipPath);
                    e.Graphics.FillPath(highlightBrush, highlightPath);
                }

                e.Graphics.Restore(state);
            }

            Rectangle accentBounds = new Rectangle(12, Math.Max(10, Height - 16), Math.Max(10, Width - 28), 2);
            using (LinearGradientBrush accentBrush = new LinearGradientBrush(
                accentBounds,
                Color.FromArgb(0, 82, 152, 218),
                Color.FromArgb(180, 82, 152, 218),
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(accentBrush, accentBounds);
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
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
    }

    // Header "DUNGX PROJECT" ở đầu DXPALETTE.
    // Nếu muốn đổi logo/tên thương hiệu thì sửa control này.
    internal sealed class PaletteTitlePanel : WF.Panel
    {
        private static readonly Color TitleTopColor = Color.FromArgb(42, 42, 46);
        private static readonly Color TitleBottomColor = Color.FromArgb(24, 24, 28);
        private static readonly Color TitleBorderColor = Color.FromArgb(84, 84, 90);
        private static readonly Color TitleTextColor = Color.FromArgb(236, 236, 238);
        private static readonly Color TitleSubtleColor = Color.FromArgb(164, 164, 170);
        private static readonly Color LogoBlue = Color.FromArgb(64, 164, 255);

        public PaletteTitlePanel()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            DoubleBuffered = true;
            Height = 40;
            MinimumSize = new Size(0, 40);
            Margin = new WF.Padding(0, 0, 0, 6);
        }

        protected override void OnPaintBackground(WF.PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(18, 18, 18));
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            using (GraphicsPath path = CreateRoundedPath(bounds, 8))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                bounds,
                TitleTopColor,
                TitleBottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(TitleBorderColor))
            using (Pen innerPen = new Pen(Color.FromArgb(42, 255, 255, 255)))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);

                Rectangle innerBounds = Rectangle.Inflate(bounds, -1, -1);
                using (GraphicsPath innerPath = CreateRoundedPath(innerBounds, 7))
                {
                    e.Graphics.DrawPath(innerPen, innerPath);
                }
            }

            Rectangle glossBounds = new Rectangle(1, 1, Math.Max(1, Width - 4), Math.Max(8, Height / 2));
            using (GraphicsPath clipPath = CreateRoundedPath(bounds, 8))
            using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                glossBounds,
                Color.FromArgb(36, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(clipPath);
                e.Graphics.FillRectangle(glossBrush, glossBounds);
                e.Graphics.Restore(state);
            }

            Rectangle logoBounds = new Rectangle(10, 8, 20, 20);
            DrawLogo(e.Graphics, logoBounds);

            Rectangle titleBounds = new Rectangle(36, 6, Math.Max(60, Width - 110), 24);
            using (System.Drawing.Font titleFont = new System.Drawing.Font(
                "Segoe UI",
                11.25F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (System.Drawing.Font subFont = new System.Drawing.Font(
                "Segoe UI",
                7.75F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    "DUNGX PROJECT",
                    titleFont,
                    titleBounds,
                    TitleTextColor,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);

                Rectangle subBounds = new Rectangle(36, 24, Math.Max(60, Width - 110), 12);
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    "Custom Command Manager",
                    subFont,
                    subBounds,
                    TitleSubtleColor,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);
            }

            DrawHeaderGlyphs(e.Graphics, Width - 52, 10);
        }

        private static void DrawLogo(Graphics graphics, Rectangle bounds)
        {
            using (SolidBrush blueBrush = new SolidBrush(LogoBlue))
            using (SolidBrush darkBrush = new SolidBrush(Color.FromArgb(34, 98, 176)))
            using (SolidBrush lightBrush = new SolidBrush(Color.FromArgb(120, 198, 255)))
            using (Pen outlinePen = new Pen(Color.FromArgb(180, 10, 24, 42)))
            {
                Point[] top = {
                    new Point(bounds.Left + 6, bounds.Top),
                    new Point(bounds.Left + 14, bounds.Top + 4),
                    new Point(bounds.Left + 8, bounds.Top + 8),
                    new Point(bounds.Left, bounds.Top + 4)
                };
                Point[] left = {
                    new Point(bounds.Left, bounds.Top + 4),
                    new Point(bounds.Left + 8, bounds.Top + 8),
                    new Point(bounds.Left + 8, bounds.Top + 16),
                    new Point(bounds.Left, bounds.Top + 12)
                };
                Point[] right = {
                    new Point(bounds.Left + 8, bounds.Top + 8),
                    new Point(bounds.Left + 14, bounds.Top + 4),
                    new Point(bounds.Left + 14, bounds.Top + 12),
                    new Point(bounds.Left + 8, bounds.Top + 16)
                };

                graphics.FillPolygon(lightBrush, top);
                graphics.FillPolygon(blueBrush, left);
                graphics.FillPolygon(darkBrush, right);
                graphics.DrawPolygon(outlinePen, top);
                graphics.DrawPolygon(outlinePen, left);
                graphics.DrawPolygon(outlinePen, right);
            }
        }

        private static void DrawHeaderGlyphs(Graphics graphics, int x, int y)
        {
            using (Pen linePen = new Pen(Color.FromArgb(198, 198, 202), 1.6f))
            {
                linePen.StartCap = LineCap.Round;
                linePen.EndCap = LineCap.Round;

                graphics.DrawLine(linePen, x, y + 2, x + 10, y + 2);
                graphics.DrawLine(linePen, x, y + 6, x + 10, y + 6);
                graphics.DrawLine(linePen, x + 20, y, x + 28, y + 8);
                graphics.DrawLine(linePen, x + 28, y, x + 20, y + 8);
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
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
    }

    // Nút toolbar custom kiểu glossy.
    // Dùng cho Reload LISP, Add Source, Reset Stats...
    internal sealed class PaletteToolbarButton : WF.Button
    {
        private static readonly Color ForeColorNormal = Color.FromArgb(242, 242, 244);
        private static readonly Color DefaultTopColor = Color.FromArgb(50, 52, 58);
        private static readonly Color DefaultBottomColor = Color.FromArgb(22, 24, 28);
        private static readonly Color DefaultBorderColor = Color.FromArgb(84, 86, 92);
        private static readonly Color PrimaryTopColor = Color.FromArgb(88, 144, 212);
        private static readonly Color PrimaryBottomColor = Color.FromArgb(28, 72, 124);
        private static readonly Color PrimaryBorderColor = Color.FromArgb(102, 164, 232);
        private bool _hovered;
        private bool _pressed;

        public PaletteToolbarButton()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            DoubleBuffered = true;
            AutoSize = true;
            AutoSizeMode = WF.AutoSizeMode.GrowAndShrink;
            Margin = new WF.Padding(0, 0, 8, 0);
            Padding = new WF.Padding(14, 5, 14, 5);
            MinimumSize = new Size(76, 30);
            Cursor = WF.Cursors.Hand;
            FlatStyle = WF.FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = ForeColorNormal;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
        }

        public bool IsPrimary { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(WF.MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == WF.MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(WF.MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaintBackground(WF.PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(18, 18, 18));
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadowBounds = new Rectangle(1, 2, Math.Max(1, Width - 3), Math.Max(1, Height - 4));
            if (!_pressed)
            {
                using (GraphicsPath shadowPath = CreateRoundedPath(shadowBounds, 6))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
                {
                    e.Graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            Rectangle buttonBounds = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 3));
            if (_pressed)
            {
                buttonBounds.Offset(0, 1);
            }

            (Color topColor, Color bottomColor, Color borderColor) = GetColors();
            using (GraphicsPath buttonPath = CreateRoundedPath(buttonBounds, 6))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                buttonBounds,
                topColor,
                bottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(borderColor))
            using (Pen innerPen = new Pen(Color.FromArgb(60, 255, 255, 255)))
            {
                e.Graphics.FillPath(fillBrush, buttonPath);

                Rectangle glossBounds = new Rectangle(
                    buttonBounds.X + 1,
                    buttonBounds.Y + 1,
                    Math.Max(1, buttonBounds.Width - 2),
                    Math.Max(8, (buttonBounds.Height / 2) - 1));
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(buttonPath);
                using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                    glossBounds,
                    Color.FromArgb(IsPrimary ? 64 : 46, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(glossBrush, glossBounds);
                }

                e.Graphics.Restore(state);
                e.Graphics.DrawPath(borderPen, buttonPath);
                e.Graphics.DrawPath(innerPen, buttonPath);
            }

            if (_hovered && !_pressed)
            {
                using (GraphicsPath hoverPath = CreateRoundedPath(buttonBounds, 6))
                using (Pen hoverPen = new Pen(IsPrimary
                    ? Color.FromArgb(176, 214, 255)
                    : Color.FromArgb(132, 146, 168)))
                {
                    e.Graphics.DrawPath(hoverPen, hoverPath);
                }
            }

            Rectangle textBounds = Rectangle.Inflate(buttonBounds, -12, -2);
            if (_pressed)
            {
                textBounds.Offset(0, 1);
            }

            WF.TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                Enabled ? ForeColorNormal : Color.FromArgb(132, 132, 136),
                WF.TextFormatFlags.HorizontalCenter |
                WF.TextFormatFlags.VerticalCenter |
                WF.TextFormatFlags.EndEllipsis |
                WF.TextFormatFlags.NoPadding);
        }

        private (Color topColor, Color bottomColor, Color borderColor) GetColors()
        {
            if (!Enabled)
            {
                return (
                    Color.FromArgb(34, 34, 36),
                    Color.FromArgb(20, 20, 22),
                    Color.FromArgb(64, 64, 68));
            }

            if (IsPrimary)
            {
                if (_pressed)
                {
                    return (
                        Color.FromArgb(52, 102, 166),
                        Color.FromArgb(20, 58, 104),
                        Color.FromArgb(126, 186, 248));
                }

                if (_hovered)
                {
                    return (
                        Color.FromArgb(112, 168, 232),
                        Color.FromArgb(36, 86, 142),
                        Color.FromArgb(146, 208, 255));
                }

                return (PrimaryTopColor, PrimaryBottomColor, PrimaryBorderColor);
            }

            if (_pressed)
            {
                return (
                    Color.FromArgb(36, 38, 42),
                    Color.FromArgb(18, 18, 20),
                    Color.FromArgb(94, 96, 104));
            }

            if (_hovered)
            {
                return (
                    Color.FromArgb(66, 70, 78),
                    Color.FromArgb(28, 30, 34),
                    Color.FromArgb(118, 124, 136));
            }

            return (DefaultTopColor, DefaultBottomColor, DefaultBorderColor);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
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

        public int UsageCount { get; set; }
    }

    internal enum PaletteSortMode
    {
        Custom,
        Alphabetical,
        Used
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

    // ======================================================
    // PALETTE COMMAND USAGE TRACKER
    // Đếm số lần dùng command trong DXPALETTE.
    // Theo dõi cả command .NET/DLL và một số command LISP thông qua event AutoCAD.
    // ======================================================
    internal static class PaletteCommandUsageTracker
    {
        private static readonly HashSet<string> KnownCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<IntPtr> HookedDocumentPointers =
            new HashSet<IntPtr>();
        private static readonly Dictionary<IntPtr, string> PendingLispCommands =
            new Dictionary<IntPtr, string>();

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            DocumentCollection documentManager = Application.DocumentManager;
            documentManager.DocumentCreated += OnDocumentCreated;
            documentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

            foreach (Document document in documentManager)
            {
                HookDocument(document);
            }

            _initialized = true;
        }

        public static void Terminate()
        {
            if (!_initialized)
            {
                return;
            }

            DocumentCollection documentManager = Application.DocumentManager;
            documentManager.DocumentCreated -= OnDocumentCreated;
            documentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;

            foreach (Document document in documentManager)
            {
                UnhookDocument(document);
            }

            HookedDocumentPointers.Clear();
            PendingLispCommands.Clear();
            _initialized = false;
        }

        public static void SetKnownCommands(IEnumerable<string> commandNames)
        {
            KnownCommands.Clear();
            foreach (string commandName in commandNames ?? Enumerable.Empty<string>())
            {
                string normalized = NormalizeCommandName(commandName);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    KnownCommands.Add(normalized);
                }
            }
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            HookDocument(e.Document);
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            UnhookDocument(e.Document);
        }

        private static void HookDocument(Document document)
        {
            if (document == null)
            {
                return;
            }

            IntPtr pointer = document.UnmanagedObject;
            if (pointer == IntPtr.Zero || !HookedDocumentPointers.Add(pointer))
            {
                return;
            }

            document.CommandEnded += OnDocumentCommandEnded;
            document.LispWillStart += OnDocumentLispWillStart;
            document.LispEnded += OnDocumentLispEnded;
            document.LispCancelled += OnDocumentLispCancelled;
        }

        private static void UnhookDocument(Document document)
        {
            if (document == null)
            {
                return;
            }

            IntPtr pointer = document.UnmanagedObject;
            if (pointer != IntPtr.Zero && HookedDocumentPointers.Remove(pointer))
            {
                document.CommandEnded -= OnDocumentCommandEnded;
                document.LispWillStart -= OnDocumentLispWillStart;
                document.LispEnded -= OnDocumentLispEnded;
                document.LispCancelled -= OnDocumentLispCancelled;
                PendingLispCommands.Remove(pointer);
            }
        }

        private static void OnDocumentCommandEnded(object sender, CommandEventArgs e)
        {
            string commandName = NormalizeCommandName(e?.GlobalCommandName);
            if (string.IsNullOrWhiteSpace(commandName) || !KnownCommands.Contains(commandName))
            {
                return;
            }

            int usageCount = PaletteUsageStore.Increment(commandName);
            DungXPaletteHost.NotifyCommandUsage(commandName, usageCount);
        }

        private static void OnDocumentLispWillStart(object sender, LispWillStartEventArgs e)
        {
            Document document = sender as Document;
            if (document == null)
            {
                return;
            }

            string commandName = TryResolveKnownLispCommandName(e?.FirstLine);
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            PendingLispCommands[document.UnmanagedObject] = commandName;
        }

        private static void OnDocumentLispEnded(object sender, EventArgs e)
        {
            CompletePendingLispCommand(sender as Document);
        }

        private static void OnDocumentLispCancelled(object sender, EventArgs e)
        {
            Document document = sender as Document;
            if (document == null)
            {
                return;
            }

            PendingLispCommands.Remove(document.UnmanagedObject);
        }

        private static void CompletePendingLispCommand(Document document)
        {
            if (document == null)
            {
                return;
            }

            IntPtr pointer = document.UnmanagedObject;
            if (pointer == IntPtr.Zero ||
                !PendingLispCommands.TryGetValue(pointer, out string commandName) ||
                string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            PendingLispCommands.Remove(pointer);
            int usageCount = PaletteUsageStore.Increment(commandName);
            DungXPaletteHost.NotifyCommandUsage(commandName, usageCount);
        }

        private static string NormalizeCommandName(string commandName)
        {
            string normalized = (commandName ?? string.Empty).Trim();
            while (normalized.StartsWith(".", StringComparison.Ordinal) ||
                   normalized.StartsWith("_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized;
        }

        private static string TryResolveKnownLispCommandName(string firstLine)
        {
            string normalizedLine = NormalizeCommandName(firstLine);
            if (!string.IsNullOrWhiteSpace(normalizedLine) && KnownCommands.Contains(normalizedLine))
            {
                return normalizedLine;
            }

            string line = (firstLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            Match cCommandMatch = Regex.Match(
                line,
                @"(?i)(?:\(\s*defun\s+[cC]:|[cC]:)(?<name>[a-z0-9_\-$]+)");
            if (cCommandMatch.Success)
            {
                string candidate = NormalizeCommandName(cCommandMatch.Groups["name"].Value);
                if (KnownCommands.Contains(candidate))
                {
                    return candidate;
                }
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("(", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1).TrimStart();
            }

            string leadingToken = new string(
                trimmed.TakeWhile(ch =>
                    !char.IsWhiteSpace(ch) &&
                    ch != '(' &&
                    ch != ')' &&
                    ch != '"' &&
                    ch != '\'')
                .ToArray());

            string normalizedToken = NormalizeCommandName(leadingToken);
            return KnownCommands.Contains(normalizedToken)
                ? normalizedToken
                : null;
        }
    }

    // ======================================================
    // LISP RESOLVER
    // Quản lý thư mục LISP và tìm các file LISP cần load/chạy từ DXPALETTE.
    // ======================================================
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

    // Lưu mô tả command người dùng nhập trong DXPALETTE.
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

    // Lưu số lần dùng command để giữ thống kê qua các lần mở AutoCAD.
    internal static class PaletteUsageStore
    {
        private static readonly string UsageFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_usage.tsv");

        public static void ApplyUsage(IEnumerable<PaletteCommandItem> items)
        {
            Dictionary<string, int> usageMap = Load();
            foreach (PaletteCommandItem item in items ?? Enumerable.Empty<PaletteCommandItem>())
            {
                item.UsageCount = usageMap.TryGetValue(item.CommandName, out int count)
                    ? count
                    : 0;
            }
        }

        public static int Increment(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return 0;
            }

            Dictionary<string, int> usageMap = Load();
            usageMap[commandName] = usageMap.TryGetValue(commandName, out int count)
                ? count + 1
                : 1;
            SaveAll(usageMap);
            return usageMap[commandName];
        }

        public static void Reset()
        {
            if (File.Exists(UsageFilePath))
            {
                File.Delete(UsageFilePath);
            }
        }

        private static Dictionary<string, int> Load()
        {
            Dictionary<string, int> map =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(UsageFilePath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(UsageFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length < 2)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(commandName) ||
                    !int.TryParse(parts[1].Trim(), out int usageCount))
                {
                    continue;
                }

                map[commandName] = Math.Max(0, usageCount);
            }

            return map;
        }

        private static void SaveAll(Dictionary<string, int> usageMap)
        {
            List<string> lines = usageMap
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + Math.Max(0, kvp.Value).ToString(CultureInfo.InvariantCulture))
                .ToList();

            File.WriteAllLines(UsageFilePath, lines, Encoding.UTF8);
        }
    }

    // Lưu giá trị L gần nhất của SS để Enter lần sau dùng lại nhanh.
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

    internal static class SdxyTargetSettingsStore
    {
        private static readonly string FilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "sdxy_target_settings.tsv");

        public static SdxyTargetSettings Load()
        {
            if (!File.Exists(FilePath))
            {
                return new SdxyTargetSettings();
            }

            try
            {
                return ParseLines(File.ReadAllLines(FilePath, Encoding.UTF8));
            }
            catch
            {
                return new SdxyTargetSettings();
            }
        }

        public static void Save(SdxyTargetSettings settings)
        {
            settings = settings ?? new SdxyTargetSettings();
            File.WriteAllLines(FilePath, BuildLines(settings), Encoding.UTF8);
        }

        internal static SdxyTargetSettings ParseLines(IEnumerable<string> rawLines)
        {
            SdxyTargetSettings settings = new SdxyTargetSettings();
            string sampleTypeName = string.Empty;
            string sampleTypeDisplay = string.Empty;
            string sampleLayer = string.Empty;
            string sampleLinetype = string.Empty;
            string sampleColorKey = string.Empty;
            string sampleColorDisplay = string.Empty;
            string sampleBlock = string.Empty;
            Dictionary<int, Dictionary<string, string>> indexedSamples =
                new Dictionary<int, Dictionary<string, string>>();

            foreach (string rawLine in rawLines ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string[] parts = rawLine.Split(new[] { '\t' }, 2);
                string key = parts[0].Trim();
                string value = parts.Length > 1 ? parts[1] : string.Empty;

                if (TryParseIndexedSampleKey(key, out int sampleIndex, out string sampleField))
                {
                    if (!indexedSamples.TryGetValue(sampleIndex, out Dictionary<string, string> sampleValues))
                    {
                        sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        indexedSamples[sampleIndex] = sampleValues;
                    }

                    sampleValues[sampleField] = value.Trim();
                    continue;
                }

                switch (key)
                {
                    case "Type":
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            settings.AllowedTypeNames.Add(value.Trim());
                        }
                        break;
                    case "Layer":
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            settings.AllowedLayers.Add(value.Trim());
                        }
                        break;
                    case "UseSampleType":
                        settings.UseSampleType = ParseBoolean(value);
                        break;
                    case "UseSampleLayer":
                        settings.UseSampleLayer = ParseBoolean(value);
                        break;
                    case "UseSampleLinetype":
                        settings.UseSampleLinetype = ParseBoolean(value);
                        break;
                    case "UseSampleColor":
                        settings.UseSampleColor = ParseBoolean(value);
                        break;
                    case "UseSampleBlockName":
                        settings.UseSampleBlockName = ParseBoolean(value);
                        break;
                    case "SampleType":
                        sampleTypeName = value.Trim();
                        break;
                    case "SampleTypeDisplay":
                        sampleTypeDisplay = value.Trim();
                        break;
                    case "SampleLayer":
                        sampleLayer = value.Trim();
                        break;
                    case "SampleLinetype":
                        sampleLinetype = value.Trim();
                        break;
                    case "SampleColorKey":
                        sampleColorKey = value.Trim();
                        break;
                    case "SampleColorDisplay":
                        sampleColorDisplay = value.Trim();
                        break;
                    case "SampleBlock":
                        sampleBlock = value.Trim();
                        break;
                }
            }

            foreach (KeyValuePair<int, Dictionary<string, string>> pair in indexedSamples
                .OrderBy(item => item.Key))
            {
                SdxySampleDescriptor indexedSample = BuildSampleDescriptor(pair.Value);
                if (indexedSample != null)
                {
                    settings.SampleDescriptors.Add(indexedSample);
                }
            }

            if (settings.SampleDescriptors.Count == 0 &&
                (!string.IsNullOrWhiteSpace(sampleTypeName) ||
                 !string.IsNullOrWhiteSpace(sampleLayer) ||
                 !string.IsNullOrWhiteSpace(sampleLinetype) ||
                 !string.IsNullOrWhiteSpace(sampleColorKey) ||
                 !string.IsNullOrWhiteSpace(sampleBlock)))
            {
                settings.SampleDescriptors.Add(new SdxySampleDescriptor(
                    sampleTypeName,
                    sampleTypeDisplay,
                    sampleLayer,
                    sampleLinetype,
                    sampleColorKey,
                    sampleColorDisplay,
                    sampleBlock));
            }

            if (settings.SampleDescriptors.Count == 0)
            {
                settings.UseSampleType = false;
                settings.UseSampleLayer = false;
                settings.UseSampleLinetype = false;
                settings.UseSampleColor = false;
                settings.UseSampleBlockName = false;
            }

            return settings;
        }

        internal static List<string> BuildLines(SdxyTargetSettings settings)
        {
            settings = settings ?? new SdxyTargetSettings();

            List<string> lines = new List<string>();
            foreach (string typeName in settings.AllowedTypeNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add("Type\t" + typeName);
            }

            foreach (string layerName in settings.AllowedLayers.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add("Layer\t" + layerName);
            }

            lines.Add("UseSampleType\t" + (settings.UseSampleType ? "1" : "0"));
            lines.Add("UseSampleLayer\t" + (settings.UseSampleLayer ? "1" : "0"));
            lines.Add("UseSampleLinetype\t" + (settings.UseSampleLinetype ? "1" : "0"));
            lines.Add("UseSampleColor\t" + (settings.UseSampleColor ? "1" : "0"));
            lines.Add("UseSampleBlockName\t" + (settings.UseSampleBlockName ? "1" : "0"));

            List<SdxySampleDescriptor> samples = settings.SampleDescriptors
                .Where(sample => sample != null)
                .ToList();
            for (int i = 0; i < samples.Count; i++)
            {
                SdxySampleDescriptor sample = samples[i];
                lines.Add("Sample" + i + "Type\t" + (sample.TypeName ?? string.Empty));
                lines.Add("Sample" + i + "TypeDisplay\t" + (sample.TypeDisplayName ?? string.Empty));
                lines.Add("Sample" + i + "Layer\t" + (sample.LayerName ?? string.Empty));
                lines.Add("Sample" + i + "Linetype\t" + (sample.LinetypeName ?? string.Empty));
                lines.Add("Sample" + i + "ColorKey\t" + (sample.ColorKey ?? string.Empty));
                lines.Add("Sample" + i + "ColorDisplay\t" + (sample.ColorDisplayName ?? string.Empty));
                lines.Add("Sample" + i + "Block\t" + (sample.BlockName ?? string.Empty));
            }

            return lines;
        }

        private static SdxySampleDescriptor BuildSampleDescriptor(IReadOnlyDictionary<string, string> values)
        {
            if (values == null)
            {
                return null;
            }

            values.TryGetValue("Type", out string typeName);
            values.TryGetValue("TypeDisplay", out string typeDisplay);
            values.TryGetValue("Layer", out string layerName);
            values.TryGetValue("Linetype", out string linetypeName);
            values.TryGetValue("ColorKey", out string colorKey);
            values.TryGetValue("ColorDisplay", out string colorDisplay);
            values.TryGetValue("Block", out string blockName);

            if (string.IsNullOrWhiteSpace(typeName) &&
                string.IsNullOrWhiteSpace(layerName) &&
                string.IsNullOrWhiteSpace(linetypeName) &&
                string.IsNullOrWhiteSpace(colorKey) &&
                string.IsNullOrWhiteSpace(blockName))
            {
                return null;
            }

            return new SdxySampleDescriptor(
                typeName,
                typeDisplay,
                layerName,
                linetypeName,
                colorKey,
                colorDisplay,
                blockName);
        }

        private static bool TryParseIndexedSampleKey(string key, out int sampleIndex, out string sampleField)
        {
            sampleIndex = -1;
            sampleField = string.Empty;

            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith("Sample", StringComparison.Ordinal) ||
                key.Length <= "Sample".Length ||
                !char.IsDigit(key["Sample".Length]))
            {
                return false;
            }

            int index = "Sample".Length;
            while (index < key.Length && char.IsDigit(key[index]))
            {
                index++;
            }

            if (index <= "Sample".Length || index >= key.Length)
            {
                return false;
            }

            if (!int.TryParse(key.Substring("Sample".Length, index - "Sample".Length), out sampleIndex))
            {
                sampleIndex = -1;
                return false;
            }

            sampleField = key.Substring(index);
            return !string.IsNullOrWhiteSpace(sampleField);
        }

        private static bool ParseBoolean(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class SdxyNamedFilterStore
    {
        private static readonly string FilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "sdxy_named_filters.tsv");

        private static readonly string CurrentNameFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "sdxy_named_filter_current.txt");

        public static Dictionary<string, SdxyTargetSettings> LoadAll()
        {
            Dictionary<string, SdxyTargetSettings> result =
                new Dictionary<string, SdxyTargetSettings>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(FilePath))
            {
                return result;
            }

            try
            {
                string currentName = null;
                List<string> blockLines = new List<string>();
                foreach (string rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (rawLine.StartsWith("[Filter]\t", StringComparison.Ordinal))
                    {
                        SaveCurrentBlock(result, currentName, blockLines);
                        currentName = NormalizeName(rawLine.Substring("[Filter]\t".Length));
                        blockLines = new List<string>();
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        SaveCurrentBlock(result, currentName, blockLines);
                        currentName = null;
                        blockLines = new List<string>();
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(currentName))
                    {
                        blockLines.Add(rawLine);
                    }
                }

                SaveCurrentBlock(result, currentName, blockLines);
            }
            catch
            {
                return new Dictionary<string, SdxyTargetSettings>(StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }

        public static void SaveAll(IReadOnlyDictionary<string, SdxyTargetSettings> namedFilters)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, SdxyTargetSettings> pair in (namedFilters ?? new Dictionary<string, SdxyTargetSettings>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name = NormalizeName(pair.Key);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                lines.Add("[Filter]\t" + name);
                lines.AddRange(SdxyTargetSettingsStore.BuildLines(pair.Value));
                lines.Add(string.Empty);
            }

            File.WriteAllLines(FilePath, lines, Encoding.UTF8);
        }

        public static string LoadCurrentName()
        {
            try
            {
                if (!File.Exists(CurrentNameFilePath))
                {
                    return string.Empty;
                }

                return NormalizeName(File.ReadAllText(CurrentNameFilePath, Encoding.UTF8));
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void SaveCurrentName(string name)
        {
            try
            {
                string normalized = NormalizeName(name);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    if (File.Exists(CurrentNameFilePath))
                    {
                        File.Delete(CurrentNameFilePath);
                    }

                    return;
                }

                File.WriteAllText(CurrentNameFilePath, normalized, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void SaveCurrentBlock(
            Dictionary<string, SdxyTargetSettings> result,
            string currentName,
            List<string> blockLines)
        {
            if (string.IsNullOrWhiteSpace(currentName))
            {
                return;
            }

            result[currentName] = SdxyTargetSettingsStore.ParseLines(blockLines ?? Enumerable.Empty<string>());
        }

        private static string NormalizeName(string name)
        {
            string normalized = (name ?? string.Empty).Trim();
            normalized = normalized.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized;
        }
    }

    // Lưu trạng thái Auto Open của DXPALETTE.
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

    // Lưu danh sách source ngoài do người dùng thêm vào DXPALETTE.
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

    // Lưu các command/alias thủ công do người dùng tự thêm.
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

    // Lưu layout DXPALETTE: favorite, thứ tự custom, sort mode, width cột.
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

        private static readonly string ColumnWidthsFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_columns.tsv");

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
            if (string.Equals(mode, "A-Z", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Alphabetical;
            }

            if (string.Equals(mode, "Used", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Used;
            }

            return PaletteSortMode.Custom;
        }

        public static void SaveSortMode(PaletteSortMode mode)
        {
            string value =
                mode == PaletteSortMode.Alphabetical
                    ? "A-Z"
                    : mode == PaletteSortMode.Used
                        ? "Used"
                        : "Custom";
            File.WriteAllText(SortModeFilePath, value, Encoding.UTF8);
        }

        public static Dictionary<string, int> LoadColumnWidths()
        {
            Dictionary<string, int> widths =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(ColumnWidthsFilePath))
            {
                return widths;
            }

            foreach (string rawLine in File.ReadAllLines(ColumnWidthsFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string[] parts = rawLine.Split('\t');
                if (parts.Length < 2)
                {
                    continue;
                }

                string columnName = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(columnName) ||
                    !int.TryParse(parts[1].Trim(), out int width) ||
                    width <= 0)
                {
                    continue;
                }

                widths[columnName] = width;
            }

            return widths;
        }

        public static void SaveColumnWidths(IReadOnlyDictionary<string, int> widths)
        {
            if (widths == null || widths.Count == 0)
            {
                return;
            }

            List<string> lines = widths
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > 0)
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + kvp.Value.ToString())
                .ToList();

            if (lines.Count == 0)
            {
                return;
            }

            File.WriteAllLines(ColumnWidthsFilePath, lines, Encoding.UTF8);
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

    // Quét Action Macro của AutoCAD để đưa vào DXPALETTE.
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

    // Tập hợp command từ nhiều nguồn: DLL hiện tại, LISP, VLX, Action Macro, manual alias.
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





