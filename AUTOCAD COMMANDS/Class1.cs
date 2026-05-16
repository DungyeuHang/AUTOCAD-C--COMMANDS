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
                    SelectionSet baseSelection = PromptForSelection(
                        ed,
                        "\nChọn Polyline hoặc nhóm đối tượng gốc:");
                    if (baseSelection == null) return;

                    baseExt = GetSelectionExtents(baseSelection, tr);
                    baseCenter = GetCenter(baseExt);
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
            if (sourceIds == null || sourceIds.Length == 0)
            {
                SelectionSet sourceSelection = PromptForSelection(
                    ed,
                    "\nChọn đối tượng gốc hoặc nhóm đối tượng:");
                if (sourceSelection == null)
                {
                    return;
                }

                sourceIds = sourceSelection.GetObjectIds();
            }

            DddTargetFilter savedFilter = DddTargetFilterStore.Load();
            if (!PromptForDddTargetFilter(ed, db, savedFilter, out DddTargetFilter targetFilter))
            {
                return;
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
                        new Point3d(sourceCenter.X, sourceCenter.Y, 0.0));
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
                        new Point3d(sourceCenter.X, sourceCenter.Y, 0.0));
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
    }




    // ======================================================
    // CAA_change_pline
    // Mục đích: chuẩn hóa polyline trước khi dùng cho các workflow tự động.
    // Cách làm:
    // - Chọn 1 lightweight Polyline.
    // - Hỏi có set Closed hay bỏ qua, có lưu lựa chọn lần cuối.
    // - Hỏi cách xác định điểm đầu: tự động hoặc người dùng pick, có lưu lựa chọn lần cuối.
    // - Ép chiều vertex về ngược chiều kim đồng hồ nếu polyline kín.
    // - Đổi điểm đầu theo quy tắc tự động hoặc theo vertex người dùng chọn.
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

            PromptEntityOptions entityOptions =
                new PromptEntityOptions("\nChọn polyline cần chuẩn hóa: ");
            entityOptions.SetRejectMessage("\nChỉ hỗ trợ lightweight Polyline.");
            entityOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);

            PromptEntityResult entityResult = ed.GetEntity(entityOptions);
            if (entityResult.Status != PromptStatus.OK)
            {
                return;
            }

            CaaCloseMode savedMode = CaaPolylineSettingsStore.LoadCloseMode();
            PromptKeywordOptions closeOptions =
                new PromptKeywordOptions(
                    $"\nXử lý closed polyline [Close/Skip] <{savedMode}>: ");
            closeOptions.AllowNone = true;
            closeOptions.Keywords.Add("Close");
            closeOptions.Keywords.Add("Skip");
            closeOptions.Keywords.Default = savedMode.ToString();

            PromptResult closeResult = ed.GetKeywords(closeOptions);
            if (closeResult.Status == PromptStatus.Cancel)
            {
                return;
            }

            CaaCloseMode closeMode = savedMode;
            if (closeResult.Status == PromptStatus.OK &&
                Enum.TryParse(closeResult.StringResult, true, out CaaCloseMode parsedMode))
            {
                closeMode = parsedMode;
            }

            CaaPolylineSettingsStore.SaveCloseMode(closeMode);

            CaaStartMode savedStartMode = CaaPolylineSettingsStore.LoadStartMode();
            PromptKeywordOptions startModeOptions =
                new PromptKeywordOptions(
                    $"\nChọn cách xác định điểm đầu [Auto/Pick] <{savedStartMode}>: ");
            startModeOptions.AllowNone = true;
            startModeOptions.Keywords.Add("Auto");
            startModeOptions.Keywords.Add("Pick");
            startModeOptions.Keywords.Default = savedStartMode.ToString();

            PromptResult startModeResult = ed.GetKeywords(startModeOptions);
            if (startModeResult.Status == PromptStatus.Cancel)
            {
                return;
            }

            CaaStartMode startMode = savedStartMode;
            if (startModeResult.Status == PromptStatus.OK &&
                Enum.TryParse(startModeResult.StringResult, true, out CaaStartMode parsedStartMode))
            {
                startMode = parsedStartMode;
            }

            CaaPolylineSettingsStore.SaveStartMode(startMode);

            Point2d? pickedStartPoint = null;
            if (startMode == CaaStartMode.Pick)
            {
                PromptPointOptions startPointOptions =
                    new PromptPointOptions(
                        "\nChọn điểm đầu mong muốn của polyline (pline hở: chọn gần đầu mút): ");
                PromptPointResult startPointResult = ed.GetPoint(startPointOptions);
                if (startPointResult.Status != PromptStatus.OK)
                {
                    return;
                }

                pickedStartPoint =
                    new Point2d(startPointResult.Value.X, startPointResult.Value.Y);
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                    tr.GetObject(entityResult.ObjectId, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Polyline;
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
                    if (signedArea < -CoordinateTolerance)
                    {
                        polyline.ReverseCurve();
                        reversed = true;
                    }
                }
                else if (!polyline.Closed)
                {
                    int requestedStartIndex = startMode == CaaStartMode.Pick && pickedStartPoint.HasValue
                        ? FindClosestVertex(polyline, pickedStartPoint.Value)
                        : FindPreferredStartVertex(polyline);

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
                            startMode == CaaStartMode.Pick
                                ? "\nCAA_change_pline: polyline đang hở nên chỉ nhận điểm đầu ở 1 trong 2 đầu mút để tránh đổi hình. Chọn Close nếu muốn đổi sang vertex giữa."
                                : "\nCAA_change_pline: polyline đang hở nên không đổi điểm đầu về vertex giữa để tránh đổi hình. Chọn Close nếu muốn chuẩn hóa đầy đủ.");
                    }
                }

                if (polyline.Closed && polyline.NumberOfVertices >= 3)
                {
                    int startIndex = startMode == CaaStartMode.Pick && pickedStartPoint.HasValue
                        ? FindClosestVertex(polyline, pickedStartPoint.Value)
                        : FindPreferredStartVertex(polyline);
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

        private static int FindPreferredStartVertex(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            int bestIndex = 0;
            Point2d bestPoint = polyline.GetPoint2dAt(0);

            for (int i = 1; i < polyline.NumberOfVertices; i++)
            {
                Point2d point = polyline.GetPoint2dAt(i);
                bool smallerX = point.X < bestPoint.X - CoordinateTolerance;
                bool sameXSmallerY =
                    Math.Abs(point.X - bestPoint.X) <= CoordinateTolerance &&
                    point.Y < bestPoint.Y - CoordinateTolerance;

                if (smallerX || sameXSmallerY)
                {
                    bestIndex = i;
                    bestPoint = point;
                }
            }

            return bestIndex;
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

        private enum CaaStartMode
        {
            Auto,
            Pick
        }

        private static class CaaPolylineSettingsStore
        {
            private static readonly string CloseModeFilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "caa_change_pline_settings.txt");

            private static readonly string StartModeFilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "caa_change_pline_start_mode.txt");

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

            public static CaaStartMode LoadStartMode()
            {
                try
                {
                    if (!File.Exists(StartModeFilePath))
                    {
                        return CaaStartMode.Auto;
                    }

                    string raw = File.ReadAllText(StartModeFilePath, Encoding.UTF8).Trim();
                    return Enum.TryParse(raw, true, out CaaStartMode mode)
                        ? mode
                        : CaaStartMode.Auto;
                }
                catch
                {
                    return CaaStartMode.Auto;
                }
            }

            public static void SaveStartMode(CaaStartMode mode)
            {
                try
                {
                    File.WriteAllText(StartModeFilePath, mode.ToString(), Encoding.UTF8);
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
    // - Lệnh tạo 1 MText tổng hợp gồm toàn bộ APoint(...) và create_smart_shape(...).
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
            return $"create_smart_shape(\"{prefix}_p\", 1, {points.Count}, arcs_info={{{arcsInfo}}}, close={closeText})";
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

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            if (!TryPromptAxisDirection(
                ed,
                startRes.Value,
                "\nChọn điểm để xác định hướng dim X/Y: ",
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
                        startRes.Value,
                        dirPoint,
                        direction)
                    : FindNearestPointOnYAxisFromProbe(
                        ed,
                        currentSpace,
                        tr,
                        startRes.Value,
                        dirPoint,
                        direction);

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

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                if (!TryPromptDimPlacementPoint(
                    ed,
                    db,
                    startRes.Value,
                    endPoint,
                    useXAxis,
                    out Point3d dimPlacementPoint))
                {
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dimPlacementPoint,
                    Rotation = useXAxis ? 0.0 : Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        private bool TryPromptDimPlacementPoint(
            Editor ed,
            Database db,
            Point3d startPoint,
            Point3d endPoint,
            bool useXAxis,
            out Point3d dimPlacementPoint)
        {
            using (SmartDimPlacementJig jig =
                new SmartDimPlacementJig(db, startPoint, endPoint, useXAxis))
            {
                PromptResult dragResult = ed.Drag(jig);
                if (dragResult.Status == PromptStatus.OK)
                {
                    dimPlacementPoint = jig.DimLinePoint;
                    return true;
                }
            }

            dimPlacementPoint = Point3d.Origin;
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

                useXAxis = Math.Abs(deltaX) >= Math.Abs(deltaY);
                direction = useXAxis
                    ? (deltaX >= 0.0 ? 1.0 : -1.0)
                    : (deltaY >= 0.0 ? 1.0 : -1.0);
                return true;
            }
        }

        private Point3d? FindNearestPointOnXAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateScanLine(startPoint, direction))
            {
                foreach (ObjectId id in GetScanCandidateIds(
                    ed,
                    currentSpace,
                    startPoint,
                    true,
                    direction))
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    Curve curve = entity as Curve;
                    if (curve == null) continue;
                    if (!IsHorizontalRayCandidate(
                        curve,
                        startPoint.Y,
                        startPoint.X,
                        startPoint.X,
                        direction,
                        bestDistance))
                    {
                        continue;
                    }

                    Point3dCollection intersections =
                        TryGetIntersections(curve, scanLine);
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

        private Point3d? FindNearestPointOnXAxisFromProbe(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction)
        {
            // Quét target theo trục X bắt đầu từ probePoint.
            // bestDistance được tính theo khoảng cách từ probePoint để bắt đối tượng gần điểm click 2 nhất.
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateScanLine(probePoint, direction))
            {
                foreach (ObjectId id in GetScanCandidateIds(
                    ed,
                    currentSpace,
                    probePoint,
                    true,
                    direction))
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    Curve curve = entity as Curve;
                    if (curve == null) continue;
                    if (!IsHorizontalRayCandidate(
                        curve,
                        probePoint.Y,
                        startPoint.X,
                        probePoint.X,
                        direction,
                        bestDistance))
                    {
                        continue;
                    }

                    Point3dCollection intersections =
                        TryGetIntersections(curve, scanLine);
                    if (intersections == null || intersections.Count == 0) continue;

                    foreach (Point3d point in intersections)
                    {
                        double projectedFromStart =
                            (point.X - startPoint.X) * direction;
                        double projectedFromProbe =
                            (point.X - probePoint.X) * direction;

                        if (projectedFromStart <= DirectionTolerance) continue;
                        if (projectedFromProbe < -DirectionTolerance) continue;

                        double rankDistance = Math.Max(0.0, projectedFromProbe);
                        if (rankDistance >= bestDistance) continue;

                        bestDistance = rankDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private Point3d? FindNearestPointOnYAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateVerticalScanLine(startPoint, direction))
            {
                foreach (ObjectId id in GetScanCandidateIds(
                    ed,
                    currentSpace,
                    startPoint,
                    false,
                    direction))
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    Curve curve = entity as Curve;
                    if (curve == null) continue;
                    if (!IsVerticalRayCandidate(
                        curve,
                        startPoint.X,
                        startPoint.Y,
                        startPoint.Y,
                        direction,
                        bestDistance))
                    {
                        continue;
                    }

                    Point3dCollection intersections =
                        TryGetIntersections(curve, scanLine);
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

        private Point3d? FindNearestPointOnYAxisFromProbe(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction)
        {
            // Quét target theo trục Y bắt đầu từ probePoint.
            // Vẫn kiểm tra projectedFromStart để đảm bảo DIM đi đúng hướng từ điểm đầu.
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateVerticalScanLine(probePoint, direction))
            {
                foreach (ObjectId id in GetScanCandidateIds(
                    ed,
                    currentSpace,
                    probePoint,
                    false,
                    direction))
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    Curve curve = entity as Curve;
                    if (curve == null) continue;
                    if (!IsVerticalRayCandidate(
                        curve,
                        probePoint.X,
                        startPoint.Y,
                        probePoint.Y,
                        direction,
                        bestDistance))
                    {
                        continue;
                    }

                    Point3dCollection intersections =
                        TryGetIntersections(curve, scanLine);
                    if (intersections == null || intersections.Count == 0) continue;

                    foreach (Point3d point in intersections)
                    {
                        double projectedFromStart =
                            (point.Y - startPoint.Y) * direction;
                        double projectedFromProbe =
                            (point.Y - probePoint.Y) * direction;

                        if (projectedFromStart <= DirectionTolerance) continue;
                        if (projectedFromProbe < -DirectionTolerance) continue;

                        double rankDistance = Math.Max(0.0, projectedFromProbe);
                        if (rankDistance >= bestDistance) continue;

                        bestDistance = rankDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private bool IsHorizontalRayCandidate(
            Curve curve,
            double scanY,
            double startX,
            double probeX,
            double direction,
            double bestDistance)
        {
            // Lọc nhanh bằng GeometricExtents trước khi gọi IntersectWith.
            // Đây là phần giúp SDXY nhanh hơn khi bản vẽ có nhiều object.
            if (!TryGetCurveExtents(curve, out Extents3d extents))
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
            Curve curve,
            double scanX,
            double startY,
            double probeY,
            double direction,
            double bestDistance)
        {
            if (!TryGetCurveExtents(curve, out Extents3d extents))
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
            double direction)
        {
            ObjectId[] selectionIds = TrySelectFenceCandidates(
                ed,
                scanStartPoint,
                useXAxis,
                direction);
            if (selectionIds != null)
            {
                return selectionIds;
            }

            return EnumerateCurveIds(currentSpace);
        }

        private ObjectId[] TrySelectFenceCandidates(
            Editor ed,
            Point3d scanStartPoint,
            bool useXAxis,
            double direction)
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
                            .Where(IsCurveCandidateId)
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

        private IEnumerable<ObjectId> EnumerateCurveIds(BlockTableRecord currentSpace)
        {
            foreach (ObjectId id in currentSpace)
            {
                if (IsCurveCandidateId(id))
                {
                    yield return id;
                }
            }
        }

        private bool IsCurveCandidateId(ObjectId id)
        {
            RXClass objectClass = id.ObjectClass;
            if (objectClass == null)
            {
                return false;
            }

            if (DimensionRxClass != null && objectClass.IsDerivedFrom(DimensionRxClass))
            {
                return false;
            }

            return CurveRxClass == null || objectClass.IsDerivedFrom(CurveRxClass);
        }

        private bool TryGetCurveExtents(Curve curve, out Extents3d extents)
        {
            try
            {
                extents = curve.GeometricExtents;
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                extents = default;
                return false;
            }
        }

        private Point3dCollection TryGetIntersections(Curve curve, Line scanLine)
        {
            // IntersectWith có thể lỗi với vài entity đặc biệt.
            // Bắt lỗi ở đây để lệnh bỏ qua object đó thay vì văng command.
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

        private sealed class AxisDirectionPreviewJig : DrawJig, IDisposable
        {
            private readonly Point3d _startPoint;
            private readonly string _message;
            private readonly bool? _forceXAxis;
            private Point3d _currentPoint;

            public AxisDirectionPreviewJig(
                Point3d startPoint,
                string message,
                bool? forceXAxis)
            {
                _startPoint = startPoint;
                _message = message;
                _forceXAxis = forceXAxis;
                _currentPoint = startPoint;
            }

            public Point3d CurrentPoint => _currentPoint;

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

                if (_currentPoint.DistanceTo(pointResult.Value) <= PreviewPointTolerance)
                {
                    return SamplerStatus.NoChange;
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
                double deltaX = _currentPoint.X - _startPoint.X;
                double deltaY = _currentPoint.Y - _startPoint.Y;

                bool useXAxis = _forceXAxis ?? (Math.Abs(deltaX) >= Math.Abs(deltaY));
                if (useXAxis)
                {
                    return new Point3d(_currentPoint.X, _startPoint.Y, _startPoint.Z);
                }

                return new Point3d(_startPoint.X, _currentPoint.Y, _startPoint.Z);
            }

            public void Dispose()
            {
            }
        }

        private sealed class SmartDimPlacementJig : DrawJig, IDisposable
        {
            private readonly RotatedDimension _previewDimension;
            private readonly Point3d _defaultPoint;
            private Point3d _currentPoint;

            public SmartDimPlacementJig(
                Database db,
                Point3d startPoint,
                Point3d endPoint,
                bool useXAxis)
            {
                double previewOffset = Math.Max(
                    db.Dimtxt + db.Dimgap + db.Dimexe,
                    10.0);

                _defaultPoint = useXAxis
                    ? new Point3d(
                        (startPoint.X + endPoint.X) * 0.5,
                        startPoint.Y + previewOffset,
                        startPoint.Z)
                    : new Point3d(
                        startPoint.X + previewOffset,
                        (startPoint.Y + endPoint.Y) * 0.5,
                        startPoint.Z);

                _currentPoint = _defaultPoint;

                _previewDimension = new RotatedDimension
                {
                    XLine1Point = startPoint,
                    XLine2Point = endPoint,
                    DimLinePoint = _currentPoint,
                    Rotation = useXAxis ? 0.0 : Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle
                };
                _previewDimension.SetDatabaseDefaults(db);
            }

            public Point3d DimLinePoint => _currentPoint;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions pointOptions =
                    new JigPromptPointOptions("\nChọn điểm đặt dim: ");
                // Không dùng BasePoint ở bước này để preview DIM không bị
                // ORTHOMODE của AutoCAD ép theo ngang/dọc.
                pointOptions.UserInputControls =
                    UserInputControls.Accept3dCoordinates;

                PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
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

                _currentPoint = pointResult.Value;
                _previewDimension.DimLinePoint = _currentPoint;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                return _previewDimension.WorldDraw(draw);
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
            { "SS", "SSD_SMART_STRETCH_BY_DIM", "SSD2_SMART_STRETCH_BY_DIM2" };

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
                ["SSD_SMART_STRETCH_BY_DIM"] = new RibbonCommandStyle(
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
        // - Hỏi L trước.
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

            if (!TryPromptStretchLength(ed, out double length))
            {
                return;
            }

            RunSmartStretchLoopWithLength(ed, db, length, "SS");
        }

        // SSD_SMART_STRETCH_BY_DIM:
        // - Chọn 2 DIM.
        // - L = trị tuyệt đối chênh lệch measurement của 2 DIM.
        // - Sau đó chạy cùng core stretch với SS.
        [CommandMethod("SSD_SMART_STRETCH_BY_DIM")]
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

            RunSmartStretchLoopWithLength(ed, db, length, "SSD_SMART_STRETCH_BY_DIM");
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
            double length,
            string commandLabel,
            int passCount = 1)
        {
            // Core dùng chung cho SS/SSD/SSD2.
            // Sau mỗi lượt stretch sẽ quay lại chọn tiếp.
            // Chỉ dừng khi người dùng nhấn Space/Enter hoặc Esc.
            // Tắt OSMODE tạm thời để điểm click không bị OSNAP kéo lệch.
            // Khi kết thúc/cancel luôn khôi phục OSMODE cũ.
            object previousOsMode = null;

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
                                length,
                                passCount > 1
                                    ? $"{commandLabel} [{pass}/{passCount}]"
                                    : commandLabel);

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
            double length,
            string commandLabel)
        {
            SmartStretchSelectionInput selectionInput =
                GetSmartStretchSelectionInput(ed, out bool stopRequested);
            if (stopRequested)
            {
                return SmartStretchLoopResult.StopRequested;
            }

            if (selectionInput == null)
            {
                return SmartStretchLoopResult.Retry;
            }

            ShowSmartStretchSelection(ed, selectionInput.SelectedObjectIds);

            PromptPointOptions startPointOptions =
                new PromptPointOptions("\nChọn điểm đầu hoặc Space/Enter để kết thúc: ");
            startPointOptions.AllowNone = true;

            PromptPointResult startResult = ed.GetPoint(startPointOptions);
            if (startResult.Status == PromptStatus.None ||
                startResult.Status == PromptStatus.Cancel)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartStretchLoopResult.StopRequested;
            }

            if (startResult.Status != PromptStatus.OK)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartStretchLoopResult.Retry;
            }

            PromptResult directionResult = GetDirectionWithPreview(
                ed,
                selectionInput,
                startResult.Value,
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
            ExecuteNativeStretch(ed, selectionInput, startResult.Value, secondPoint);

            ed.WriteMessage(
                $"\n{commandLabel}: đã gọi STRETCH gốc theo {GetDirectionLabel(direction)} với L = {length.ToString("0.###", CultureInfo.InvariantCulture)}.");
            return SmartStretchLoopResult.Completed;
        }

        private static bool TryPromptStretchLength(Editor ed, out double length)
        {
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
                length = 0.0;
                return false;
            }

            length = lengthResult.Status == PromptStatus.None
                ? savedLength
                : lengthResult.Value;

            if (length <= ComparisonTolerance)
            {
                ed.WriteMessage("\nGiá trị L phải lớn hơn 0.");
                return false;
            }

            return true;
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
            out bool stopRequested)
        {
            // Cho phép quét nhiều crossing window.
            // Mỗi window được lưu để lúc gọi STRETCH gốc truyền đúng vùng crossing.
            // Space/Enter ở ngay window đầu tiên sẽ thoát hẳn command loop.
            stopRequested = false;
            ed.WriteMessage(
                "\nWindow: quet nhieu vung neu can. Nhan Space/Enter o goc dau khi chua quet window nao de thoat, hoac sau khi da quet it nhat 1 window de sang buoc stretch.");

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
                                ? "\nChọn góc đầu crossing window hoặc Space/Enter để thoát: "
                                : "\nChọn góc đầu crossing window tiếp theo hoặc Space/Enter để stretch: ");
                    firstCornerOptions.AllowNone = true;

                    PromptPointResult firstCornerResult = ed.GetPoint(firstCornerOptions);
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





