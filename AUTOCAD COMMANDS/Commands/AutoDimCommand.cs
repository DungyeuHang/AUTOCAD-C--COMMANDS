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
}
