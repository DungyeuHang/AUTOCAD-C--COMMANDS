﻿﻿﻿using Autodesk.AutoCAD.ApplicationServices;
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
        private const double DddSearchDistance = 1000000.0;
        private const string DaaBaseObjectKeyword = "Object";
        private const string DaaBasePointKeyword = "Point";

        // Đối tượng gốc DDD được chọn gần nhất nhất, để lần chạy sau có thể
        // nhấn Enter/Space dùng lại thay vì phải pick lại từ đầu.
        private static ObjectId[] _lastDddSourceIds;

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
                Enum.TryParse(WorkspaceUiStateStore.GetValue("daa.baseMode"), true, out DaaBaseMode baseMode);
                if (!Enum.IsDefined(typeof(DaaBaseMode), baseMode))
                    baseMode = DaaBaseMode.Object;

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

                ObjectId dimLayerId = EnsureAutoDimLayer(db, tr);

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

                    CreateDimWithLayer(
                        ms, tr, db, dimLayerId,
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

                    CreateDimWithLayer(
                        ms, tr, db, dimLayerId,
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

                    CreateDimWithLayer(
                        ms, tr, db, dimLayerId,
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

                    CreateDimWithLayer(
                        ms, tr, db, dimLayerId,
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
            else
            {
                if (!PromptForDddFilterMode(ed, db, ref targetFilter))
                {
                    return;
                }
            }

            _lastDddSourceIds = sourceIds;

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

                Point3d? leftPoint = null;
                Point3d? rightPoint = null;
                Point3d? topPoint = null;
                Point3d? bottomPoint = null;
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

                    if (TryGetDddDirectionalPoint(
                        entity,
                        extents,
                        sourceCenter,
                        useXAxis: true,
                        direction: -1.0,
                        out Point3d currentLeftPoint))
                    {
                        double currentLeftDistance =
                            sourceExtents.Value.MinPoint.X - currentLeftPoint.X;
                        if (currentLeftDistance >= -AutoDimTolerance &&
                            currentLeftDistance < leftDistance)
                        {
                            leftDistance = Math.Max(0.0, currentLeftDistance);
                            leftPoint = currentLeftPoint;
                        }
                    }

                    if (TryGetDddDirectionalPoint(
                        entity,
                        extents,
                        sourceCenter,
                        useXAxis: true,
                        direction: 1.0,
                        out Point3d currentRightPoint))
                    {
                        double currentRightDistance =
                            currentRightPoint.X - sourceExtents.Value.MaxPoint.X;
                        if (currentRightDistance >= -AutoDimTolerance &&
                            currentRightDistance < rightDistance)
                        {
                            rightDistance = Math.Max(0.0, currentRightDistance);
                            rightPoint = currentRightPoint;
                        }
                    }

                    if (TryGetDddDirectionalPoint(
                        entity,
                        extents,
                        sourceCenter,
                        useXAxis: false,
                        direction: 1.0,
                        out Point3d currentTopPoint))
                    {
                        double currentTopDistance =
                            currentTopPoint.Y - sourceExtents.Value.MaxPoint.Y;
                        if (currentTopDistance >= -AutoDimTolerance &&
                            currentTopDistance < topDistance)
                        {
                            topDistance = Math.Max(0.0, currentTopDistance);
                            topPoint = currentTopPoint;
                        }
                    }

                    if (TryGetDddDirectionalPoint(
                        entity,
                        extents,
                        sourceCenter,
                        useXAxis: false,
                        direction: -1.0,
                        out Point3d currentBottomPoint))
                    {
                        double currentBottomDistance =
                            sourceExtents.Value.MinPoint.Y - currentBottomPoint.Y;
                        if (currentBottomDistance >= -AutoDimTolerance &&
                            currentBottomDistance < bottomDistance)
                        {
                            bottomDistance = Math.Max(0.0, currentBottomDistance);
                            bottomPoint = currentBottomPoint;
                        }
                    }
                }

                if (!leftPoint.HasValue &&
                    !rightPoint.HasValue &&
                    !topPoint.HasValue &&
                    !bottomPoint.HasValue)
                {
                    WriteDddNoSurroundingTargetMessage(ed, targetFilter);
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

                if (leftPoint.HasValue && leftDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        0.0,
                        new Point3d(leftPoint.Value.X, sourceCenter.Y, 0.0),
                        new Point3d(sourceExtents.Value.MinPoint.X, sourceCenter.Y, 0.0),
                        new Point3d(sourceCenter.X, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (rightPoint.HasValue && rightDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        0.0,
                        new Point3d(sourceExtents.Value.MaxPoint.X, sourceCenter.Y, 0.0),
                        new Point3d(rightPoint.Value.X, sourceCenter.Y, 0.0),
                        new Point3d(sourceCenter.X, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (topPoint.HasValue && topDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        Math.PI / 2.0,
                        new Point3d(sourceCenter.X, sourceExtents.Value.MaxPoint.Y, 0.0),
                        new Point3d(sourceCenter.X, topPoint.Value.Y, 0.0),
                        new Point3d(verticalDimPlacementX, sourceCenter.Y, 0.0));
                    createdCount++;
                }

                if (bottomPoint.HasValue && bottomDistance > AutoDimTolerance)
                {
                    CreateDimWithLayer(
                        ms,
                        tr,
                        db,
                        dimLayerId,
                        Math.PI / 2.0,
                        new Point3d(sourceCenter.X, bottomPoint.Value.Y, 0.0),
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
                    leftPoint.HasValue ? (double?)leftDistance : null,
                    rightPoint.HasValue ? (double?)rightDistance : null,
                    topPoint.HasValue ? (double?)topDistance : null,
                    bottomPoint.HasValue ? (double?)bottomDistance : null);
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

        [CommandMethod("DPA_DimAutoPline")]
        public void DimAutoPline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions entityOptions =
                new PromptEntityOptions("\nChọn Polyline để tạo DIM tự động: ");
            entityOptions.SetRejectMessage("\nChỉ hỗ trợ Polyline.");
            entityOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);

            PromptEntityResult entityResult = ed.GetEntity(entityOptions);
            if (entityResult.Status != PromptStatus.OK)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                    tr.GetObject(entityResult.ObjectId, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                if (polyline == null)
                {
                    ed.WriteMessage("\nDPA_DimAutoPline: không đọc được Polyline.");
                    return;
                }

                int vertexCount = polyline.NumberOfVertices;
                if (vertexCount < 2)
                {
                    ed.WriteMessage("\nDPA_DimAutoPline: Polyline cần ít nhất 2 đỉnh.");
                    return;
                }

                DpaDimAutoPlineSettings settings = DpaDimAutoPlineSettings.LoadFromStore();
                bool isClosed = polyline.Closed;

                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                ObjectId layerId = EnsureAutoDimLayer(db, tr);

                // Tất cả thông số (hướng, phạm vi đỉnh, scale/offset, các tuỳ chọn) đều nằm
                // chung 1 dialog - không hỏi rời rạc từng cái trên dòng lệnh nữa.
                if (!TryShowDpaSettingsDialog(
                        settings,
                        tr,
                        db,
                        ms,
                        layerId,
                        polyline,
                        vertexCount,
                        isClosed,
                        out DpaDimAutoPlineSettings editedSettings,
                        out int createdCount,
                        out int angularCreated,
                        out int angularSkipped))
                {
                    return;
                }

                settings = editedSettings;
                settings.SaveToStore();

                tr.Commit();
                ed.Regen();

                string message = $"\nDPA_DimAutoPline: đã tạo {createdCount} dim.";
                if (settings.CreateAngular)
                {
                    message += $" Góc: {angularCreated} tạo được";
                    if (angularSkipped > 0)
                    {
                        message += $", {angularSkipped} bị bỏ qua (2 cạnh gần như thẳng hàng/suy biến)";
                    }

                    message += ".";
                }

                ed.WriteMessage(message);
            }
        }

        // Chạy toàn bộ logic tạo dim cho 1 bộ settings - dùng chung cho cả nút Preview
        // và khi bấm OK, để "xem trước" trong dialog luôn khớp 100% với kết quả cuối cùng.
        private List<ObjectId> CreateDpaDimensions(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            ObjectId layerId,
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            List<DpaSegment> segments,
            double signedArea,
            bool isClosed,
            DpaDimAutoPlineSettings settings,
            double dimLinearScale,
            out int createdCount,
            out int angularCreated,
            out int angularSkipped)
        {
            List<ObjectId> createdIds = new List<ObjectId>();
            createdCount = 0;
            angularCreated = 0;
            angularSkipped = 0;

            // Không có "Scale factor" tự chỉnh cỡ dim ở đây nữa - khoảng cách đặt dim lấy
            // thẳng từ DIMTXT/DIMEXE/DIMGAP * DIMSCALE hiện hành của bản vẽ (giống cách DAA
            // tính baseOffset), rồi mới nhân thêm 2 hệ số Offset mul / Dim offset mul mà
            // người dùng tự chỉnh. Cỡ chữ/mũi tên của dim không bị đụng vào ở bất kỳ đâu.
            double dimScale = db.Dimscale > 1e-9 ? db.Dimscale : 1.0;
            double baseOffset = (db.Dimtxt + db.Dimexe + db.Dimgap) * dimScale;
            double dimOffset = Math.Max(15.0 * dimScale, baseOffset);
            double effectiveOffset = dimOffset * settings.OffsetMul * settings.DimOffsetMul;
            double sign = signedArea > 0.0 ? -1.0 : 1.0;

            // scaleFactor được truyền xuống các hàm Create*WithLayer bên dưới thực chất là
            // DIMLFAC (hệ số nhân giá trị đo hiển thị trong text dim) - KHÔNG liên quan gì
            // đến offset/kích thước phía trên. <= 0 nghĩa là để dim tự thừa hưởng DIMLFAC
            // hiện hành của bản vẽ thay vì ép cứng.
            double scaleFactor = dimLinearScale;

            double globalMinX = double.MaxValue, globalMaxX = double.MinValue;
            double globalMinY = double.MaxValue, globalMaxY = double.MinValue;
            foreach (DpaSegment seg in segments)
            {
                globalMinX = Math.Min(globalMinX, Math.Min(seg.Start.X, seg.End.X));
                globalMaxX = Math.Max(globalMaxX, Math.Max(seg.Start.X, seg.End.X));
                globalMinY = Math.Min(globalMinY, Math.Min(seg.Start.Y, seg.End.Y));
                globalMaxY = Math.Max(globalMaxY, Math.Max(seg.Start.Y, seg.End.Y));
            }

            double unifiedBaselineY = sign > 0.0 ? globalMaxY + effectiveOffset : globalMinY - effectiveOffset;
            double unifiedBaselineX = sign > 0.0 ? globalMaxX + effectiveOffset : globalMinX - effectiveOffset;

            foreach (DpaSegment seg in segments)
            {
                if (seg.IsArc)
                {
                    try
                    {
                        CircularArc3d arc = polyline.GetArcSegmentAt(seg.ArcVertexIndex);
                        Point3d center2d = arc.Center;
                        Point2d chordMid = new Point2d(
                            (seg.Start.X + seg.End.X) / 2.0,
                            (seg.Start.Y + seg.End.Y) / 2.0);

                        double dirX = chordMid.X - center2d.X;
                        double dirY = chordMid.Y - center2d.Y;
                        double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
                        if (dirLen < 1e-9)
                        {
                            dirX = seg.End.Y - seg.Start.Y;
                            dirY = seg.Start.X - seg.End.X;
                            dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
                        }

                        if (dirLen > 1e-9)
                        {
                            double radius = arc.Radius;
                            Point3d centerPoint = center2d;
                            Point3d chordPoint = new Point3d(
                                center2d.X + dirX / dirLen * radius,
                                center2d.Y + dirY / dirLen * radius,
                                0.0);

                            ObjectId radialId = CreateRadialDimWithLayer(
                                ms, tr, db, layerId, centerPoint, chordPoint, effectiveOffset, scaleFactor);
                            createdIds.Add(radialId);
                            createdCount++;
                        }
                    }
                    catch
                    {
                        // Bỏ qua nếu không đọc được hình học cung (polyline lỗi/degenerate).
                    }

                    continue;
                }

                Point2d p1 = seg.Start;
                Point2d p2 = seg.End;

                bool isXEqual = Math.Abs(p1.X - p2.X) < 1e-6;
                bool isYEqual = Math.Abs(p1.Y - p2.Y) < 1e-6;

                if (isXEqual && !isYEqual)
                {
                    double midX = (p1.X + p2.X) / 2.0;
                    double midY = (p1.Y + p2.Y) / 2.0;
                    double dimX = settings.UnifiedBaseline ? unifiedBaselineX : midX + sign * effectiveOffset;
                    Point3d dimPoint = new Point3d(dimX, midY, 0.0);
                    ObjectId id = CreateRotatedDimWithLayer(
                        ms, tr, db, layerId, Math.PI / 2.0,
                        new Point3d(p1.X, p1.Y, 0.0),
                        new Point3d(p2.X, p2.Y, 0.0),
                        dimPoint,
                        scaleFactor);
                    createdIds.Add(id);
                    createdCount++;
                }
                else if (isYEqual && !isXEqual)
                {
                    double midX = (p1.X + p2.X) / 2.0;
                    double midY = (p1.Y + p2.Y) / 2.0;
                    double dimY = settings.UnifiedBaseline ? unifiedBaselineY : midY + sign * effectiveOffset;
                    Point3d dimPoint = new Point3d(midX, dimY, 0.0);
                    ObjectId id = CreateRotatedDimWithLayer(
                        ms, tr, db, layerId, 0.0,
                        new Point3d(p1.X, p1.Y, 0.0),
                        new Point3d(p2.X, p2.Y, 0.0),
                        dimPoint,
                        scaleFactor);
                    createdIds.Add(id);
                    createdCount++;
                }
                else
                {
                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 1e-9)
                    {
                        continue;
                    }

                    double nx = -dy / length;
                    double ny = dx / length;
                    double midX = (p1.X + p2.X) / 2.0;
                    double midY = (p1.Y + p2.Y) / 2.0;
                    Point3d dimPoint = new Point3d(
                        midX + nx * sign * effectiveOffset,
                        midY + ny * sign * effectiveOffset,
                        0.0);
                    ObjectId alignedId = CreateAlignedDimWithLayer(
                        ms, tr, db, layerId,
                        new Point3d(p1.X, p1.Y, 0.0),
                        new Point3d(p2.X, p2.Y, 0.0),
                        dimPoint,
                        scaleFactor);
                    createdIds.Add(alignedId);
                    createdCount++;

                    if (settings.CreateAngular)
                    {
                        Point3d center = new Point3d(p1.X, p1.Y, 0.0);
                        Point3d firstRay = new Point3d(p2.X, p2.Y, 0.0);
                        int previousVertexIndex = seg.StartVertexIndex - 1;
                        Point3d secondRay = previousVertexIndex >= 0
                            ? new Point3d(
                                polyline.GetPoint2dAt(previousVertexIndex).X,
                                polyline.GetPoint2dAt(previousVertexIndex).Y,
                                0.0)
                            : new Point3d(p1.X - 20.0, p1.Y, 0.0);

                        if (TryGetAngularArcPoint(center, firstRay, secondRay, effectiveOffset * 1.5, out Point3d angularPoint))
                        {
                            ObjectId angularId = CreateAngularDimWithLayer(
                                ms, tr, db, layerId, center, firstRay, secondRay, angularPoint, scaleFactor);
                            if (!angularId.IsNull)
                            {
                                createdIds.Add(angularId);
                                angularCreated++;
                            }
                            else
                            {
                                angularSkipped++;
                            }
                        }
                        else
                        {
                            angularSkipped++;
                        }
                    }
                }
            }

            if (settings.AddEnvelope && isClosed &&
                globalMaxX > globalMinX + 1e-6 && globalMaxY > globalMinY + 1e-6)
            {
                double envelopeOffset = effectiveOffset * 3.0;

                ObjectId widthId = CreateRotatedDimWithLayer(
                    ms, tr, db, layerId, 0.0,
                    new Point3d(globalMinX, globalMinY, 0.0),
                    new Point3d(globalMaxX, globalMinY, 0.0),
                    new Point3d(0.0, globalMinY - envelopeOffset, 0.0),
                    scaleFactor);
                createdIds.Add(widthId);
                createdCount++;

                ObjectId heightId = CreateRotatedDimWithLayer(
                    ms, tr, db, layerId, Math.PI / 2.0,
                    new Point3d(globalMinX, globalMinY, 0.0),
                    new Point3d(globalMinX, globalMaxY, 0.0),
                    new Point3d(globalMinX - envelopeOffset, 0.0, 0.0),
                    scaleFactor);
                createdIds.Add(heightId);
                createdCount++;
            }

            return createdIds;
        }

        // Một "đoạn" để dim: có thể là 1 cạnh thẳng gốc, hoặc nhiều cạnh thẳng liên
        // tiếp cùng phương đã được gộp lại (collinear merge), hoặc 1 cạnh cung (bulge).
        private sealed class DpaSegment
        {
            public bool IsArc;
            public int ArcVertexIndex;
            public Point2d Start;
            public Point2d End;
            public int StartVertexIndex;
        }

        private static bool IsCollinear(Point2d a, Point2d b, Point2d c)
        {
            double dx1 = b.X - a.X, dy1 = b.Y - a.Y;
            double dx2 = c.X - b.X, dy2 = c.Y - b.Y;
            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
            if (len1 < 1e-9 || len2 < 1e-9)
            {
                return false;
            }

            double cross = dx1 * dy2 - dy1 * dx2;
            return Math.Abs(cross / (len1 * len2)) < 1e-6;
        }

        // Gộp các cạnh thẳng liên tiếp cùng phương thành 1 đoạn để không tạo nhiều
        // dim vụn trên cùng 1 đường thẳng dài. Cạnh cung (bulge) luôn tách riêng.
        private static List<DpaSegment> BuildDpaSegments(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            List<Point2d> vertices,
            int start0,
            int end0)
        {
            List<DpaSegment> segments = new List<DpaSegment>();
            int n = start0;

            while (n < end0)
            {
                SegmentType segType;
                try
                {
                    segType = polyline.GetSegmentType(n);
                }
                catch
                {
                    segType = SegmentType.Line;
                }

                if (segType == SegmentType.Arc)
                {
                    segments.Add(new DpaSegment
                    {
                        IsArc = true,
                        ArcVertexIndex = n,
                        Start = vertices[n],
                        End = vertices[n + 1],
                        StartVertexIndex = n
                    });
                    n++;
                    continue;
                }

                Point2d segStart = vertices[n];
                Point2d segEnd = vertices[n + 1];
                int startVertexIndex = n;
                int m = n + 1;

                while (m < end0)
                {
                    SegmentType nextType;
                    try
                    {
                        nextType = polyline.GetSegmentType(m);
                    }
                    catch
                    {
                        nextType = SegmentType.Line;
                    }

                    if (nextType == SegmentType.Arc)
                    {
                        break;
                    }

                    Point2d candidateEnd = vertices[m + 1];
                    if (!IsCollinear(segStart, segEnd, candidateEnd))
                    {
                        break;
                    }

                    segEnd = candidateEnd;
                    m++;
                }

                segments.Add(new DpaSegment
                {
                    IsArc = false,
                    Start = segStart,
                    End = segEnd,
                    StartVertexIndex = startVertexIndex
                });
                n = m;
            }

            return segments;
        }



        // Dialog này chạy trực tiếp phép tạo dim thật (không phải hình vẽ giả) mỗi khi
        // bấm Preview, xoá lô cũ rồi tạo lô mới ngay trên bản vẽ để người dùng thấy kết
        // quả thật trước khi OK - tránh phải bấm lệnh lại nhiều lần để dò thông số.
        // Lưu ý orientation (Keep/CCW/CW) đã áp dụng trước khi mở dialog nên không có ở đây.
        // Toàn bộ thông số DPA (hướng polyline, phạm vi đỉnh, scale/offset, các tuỳ chọn)
        // nằm chung trong 1 dialog thay vì hỏi rời rạc từng cái trên dòng lệnh. Preview
        // chạy đúng logic tạo dim thật (xoá lô cũ trước khi tạo lô mới) nên luôn khớp
        // 100% với kết quả cuối cùng khi bấm OK.
        private bool TryShowDpaSettingsDialog(
            DpaDimAutoPlineSettings settings,
            Transaction tr,
            Database db,
            BlockTableRecord ms,
            ObjectId layerId,
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            int vertexCount,
            bool isClosed,
            out DpaDimAutoPlineSettings result,
            out int createdCount,
            out int angularCreated,
            out int angularSkipped)
        {
            DpaDimAutoPlineSettings currentSettings = settings ?? new DpaDimAutoPlineSettings();
            int finalCreatedCount = 0;
            int finalAngularCreated = 0;
            int finalAngularSkipped = 0;
            List<ObjectId> lastBatchIds = new List<ObjectId>();

            // originalSignedArea chụp lại hướng THẬT của polyline trước khi dialog đụng vào
            // gì cả, để "Keep current" luôn quay đúng về hướng gốc dù trước đó đã preview
            // thử Counterclockwise/Clockwise (mỗi lần preview có thể ReverseCurve polyline
            // thật - isCurrentlyReversed theo dõi để không bị đảo lặp sai hướng).
            double originalSignedArea = GetPolylineSignedArea(polyline);
            bool isCurrentlyReversed = false;

            WF.Form dialog = new WF.Form
            {
                Text = "DPA Dim Auto Pline",
                Width = 420,
                Height = 490,
                StartPosition = WF.FormStartPosition.CenterScreen,
                FormBorderStyle = WF.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            WF.Label orientLabel = new WF.Label { Text = "Polyline orientation:", AutoSize = true, Top = 14, Left = 12 };
            WF.ComboBox orientBox = new WF.ComboBox { DropDownStyle = WF.ComboBoxStyle.DropDownList, Top = 10, Left = 180, Width = 180 };
            orientBox.Items.Add("Keep current");
            orientBox.Items.Add("Counterclockwise");
            orientBox.Items.Add("Clockwise");
            orientBox.SelectedItem = string.IsNullOrWhiteSpace(currentSettings.Orientation) ? "Keep current" : currentSettings.Orientation;

            WF.Label rangeLabel = new WF.Label { Text = "Phạm vi đỉnh:", AutoSize = true, Top = 46, Left = 12 };
            WF.RadioButton allRadio = new WF.RadioButton { Text = "Toàn bộ", Checked = true, Top = 44, Left = 180, AutoSize = true };
            WF.RadioButton rangeRadio = new WF.RadioButton { Text = "Đoạn", Top = 44, Left = 280, AutoSize = true };

            WF.Label startVertexLabel = new WF.Label { Text = $"Từ đỉnh (1-{vertexCount}):", AutoSize = true, Top = 76, Left = 32 };
            WF.NumericUpDown startVertexUpDown = new WF.NumericUpDown
            {
                Top = 72, Left = 180, Width = 80, Minimum = 1, Maximum = vertexCount, Value = 1, Enabled = false
            };

            WF.Label endVertexLabel = new WF.Label { Text = $"Đến đỉnh (1-{vertexCount}):", AutoSize = true, Top = 106, Left = 32 };
            WF.NumericUpDown endVertexUpDown = new WF.NumericUpDown
            {
                Top = 102, Left = 180, Width = 80, Minimum = 1, Maximum = vertexCount, Value = vertexCount, Enabled = false
            };

            allRadio.CheckedChanged += (s, e) =>
            {
                startVertexUpDown.Enabled = !allRadio.Checked;
                endVertexUpDown.Enabled = !allRadio.Checked;
            };

            WF.Label offsetLabel = new WF.Label { Text = "Offset mul:", AutoSize = true, Top = 136, Left = 12 };
            WF.TextBox offsetBox = new WF.TextBox { Text = currentSettings.OffsetMul.ToString("0.######", CultureInfo.InvariantCulture), Top = 132, Left = 180, Width = 120 };

            WF.Label dimOffsetLabel = new WF.Label { Text = "Dim offset mul:", AutoSize = true, Top = 166, Left = 12 };
            WF.TextBox dimOffsetBox = new WF.TextBox { Text = currentSettings.DimOffsetMul.ToString("0.######", CultureInfo.InvariantCulture), Top = 162, Left = 180, Width = 120 };

            // DIMLFAC (Dimension Linear Scale Factor) - nhân vào GIÁ TRỊ ĐO hiển thị trong
            // text dim, khác hoàn toàn DIMSCALE (cỡ chữ/mũi tên - không có nút chỉnh riêng ở
            // đây, luôn theo DIMSTYLE hiện hành). Mặc định lấy đúng DIMLFAC hiện tại của bản
            // vẽ để không vô tình ép sai giá trị đo nếu người dùng không đổi gì.
            double currentDimLfac = db.Dimlfac > 1e-9 ? db.Dimlfac : 1.0;
            WF.Label dimLfacLabel = new WF.Label { Text = "Dim linear scale (DIMLFAC):", AutoSize = true, Top = 196, Left = 12 };
            WF.TextBox dimLfacBox = new WF.TextBox { Text = currentDimLfac.ToString("0.######", CultureInfo.InvariantCulture), Top = 192, Left = 180, Width = 120 };

            WF.CheckBox angularBox = new WF.CheckBox { Text = "Create angular dim", Checked = currentSettings.CreateAngular, Top = 226, Left = 180, AutoSize = true };

            WF.CheckBox baselineBox = new WF.CheckBox { Text = "Same baseline per direction", Checked = currentSettings.UnifiedBaseline, Top = 256, Left = 180, AutoSize = true };

            WF.CheckBox envelopeBox = new WF.CheckBox { Text = "Add overall envelope dim (closed pline)", Checked = currentSettings.AddEnvelope, Top = 286, Left = 180, AutoSize = true };

            WF.Label statusLabel = new WF.Label
            {
                Text = "Bấm Preview để xem thử trên bản vẽ.",
                AutoSize = false,
                Top = 320,
                Left = 12,
                Width = 380,
                Height = 50,
                ForeColor = System.Drawing.Color.DimGray
            };

            WF.Button previewButton = new WF.Button { Text = "Preview", Top = 376, Left = 90, Width = 80 };
            WF.Button okButton = new WF.Button { Text = "OK", DialogResult = WF.DialogResult.OK, Top = 376, Left = 180, Width = 80 };
            WF.Button cancelButton = new WF.Button { Text = "Cancel", DialogResult = WF.DialogResult.Cancel, Top = 376, Left = 270, Width = 80 };

            dialog.Controls.Add(orientLabel);
            dialog.Controls.Add(orientBox);
            dialog.Controls.Add(rangeLabel);
            dialog.Controls.Add(allRadio);
            dialog.Controls.Add(rangeRadio);
            dialog.Controls.Add(startVertexLabel);
            dialog.Controls.Add(startVertexUpDown);
            dialog.Controls.Add(endVertexLabel);
            dialog.Controls.Add(endVertexUpDown);
            dialog.Controls.Add(offsetLabel);
            dialog.Controls.Add(offsetBox);
            dialog.Controls.Add(dimOffsetLabel);
            dialog.Controls.Add(dimOffsetBox);
            dialog.Controls.Add(dimLfacLabel);
            dialog.Controls.Add(dimLfacBox);
            dialog.Controls.Add(angularBox);
            dialog.Controls.Add(baselineBox);
            dialog.Controls.Add(envelopeBox);
            dialog.Controls.Add(statusLabel);
            dialog.Controls.Add(previewButton);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);

            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            bool TryReadSettings(out DpaDimAutoPlineSettings parsed, out double dimLinearScale)
            {
                parsed = new DpaDimAutoPlineSettings
                {
                    Orientation = orientBox.SelectedItem?.ToString() ?? "Keep current",
                    CreateAngular = angularBox.Checked,
                    UnifiedBaseline = baselineBox.Checked,
                    AddEnvelope = envelopeBox.Checked
                };
                dimLinearScale = 0.0;

                try
                {
                    parsed.OffsetMul = double.Parse(offsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    parsed.DimOffsetMul = double.Parse(dimOffsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    dimLinearScale = double.Parse(dimLfacBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            void EraseLastBatch()
            {
                foreach (ObjectId id in lastBatchIds)
                {
                    if (id.IsNull || id.IsErased)
                    {
                        continue;
                    }

                    try
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
                        obj?.Erase();
                    }
                    catch
                    {
                    }
                }

                lastBatchIds.Clear();
            }

            void RunPreview()
            {
                EraseLastBatch();

                if (!TryReadSettings(out DpaDimAutoPlineSettings parsed, out double dimLinearScale))
                {
                    statusLabel.ForeColor = System.Drawing.Color.Firebrick;
                    statusLabel.Text = "Giá trị không hợp lệ (offset/DIMLFAC phải là số).";
                    finalCreatedCount = 0;
                    finalAngularCreated = 0;
                    finalAngularSkipped = 0;
                    return;
                }

                // Đảo polyline thật cho khớp hướng vừa chọn (so với hướng GỐC, không phải so
                // với trạng thái sau lần preview trước) - toggle idempotent, không bị lặp sai.
                bool targetReversed = false;
                if (string.Equals(parsed.Orientation, "Counterclockwise", StringComparison.OrdinalIgnoreCase))
                {
                    targetReversed = originalSignedArea < 0.0;
                }
                else if (string.Equals(parsed.Orientation, "Clockwise", StringComparison.OrdinalIgnoreCase))
                {
                    targetReversed = originalSignedArea >= 0.0;
                }

                if (targetReversed != isCurrentlyReversed)
                {
                    polyline.ReverseCurve();
                    isCurrentlyReversed = targetReversed;
                }

                List<Point2d> vertices = new List<Point2d>();
                for (int i = 0; i < vertexCount; i++)
                {
                    vertices.Add(polyline.GetPoint2dAt(i));
                }

                int start0 = allRadio.Checked ? 0 : (int)startVertexUpDown.Value - 1;
                int end0 = allRadio.Checked ? vertexCount - 1 : (int)endVertexUpDown.Value - 1;
                if (start0 > end0)
                {
                    int swap = start0;
                    start0 = end0;
                    end0 = swap;
                }

                if (start0 < 0 || end0 < 0 || start0 >= end0 || end0 >= vertexCount)
                {
                    statusLabel.ForeColor = System.Drawing.Color.Firebrick;
                    statusLabel.Text = "Phạm vi đỉnh không hợp lệ (Từ đỉnh phải nhỏ hơn Đến đỉnh).";
                    finalCreatedCount = 0;
                    finalAngularCreated = 0;
                    finalAngularSkipped = 0;
                    return;
                }

                double signedArea = GetPolylineSignedArea(vertices);
                List<DpaSegment> segments = BuildDpaSegments(polyline, vertices, start0, end0);

                currentSettings = parsed;

                lastBatchIds = CreateDpaDimensions(
                    ms, tr, db, layerId, polyline, segments, signedArea, isClosed, parsed, dimLinearScale,
                    out finalCreatedCount, out finalAngularCreated, out finalAngularSkipped);

                try
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.Regen();
                }
                catch
                {
                }

                statusLabel.ForeColor = System.Drawing.Color.DimGray;
                string statusText = $"Đã tạo {finalCreatedCount} dim.";
                if (parsed.CreateAngular)
                {
                    statusText += $" Góc: {finalAngularCreated} tạo được";
                    if (finalAngularSkipped > 0)
                    {
                        statusText += $", {finalAngularSkipped} bị bỏ qua";
                    }

                    statusText += ".";
                }

                statusLabel.Text = statusText;
            }

            previewButton.Click += (s, e) => RunPreview();
            okButton.Click += (s, e) => RunPreview();

            WF.DialogResult dialogResult = dialog.ShowDialog();

            if (dialogResult != WF.DialogResult.OK)
            {
                EraseLastBatch();

                // Nếu Cancel sau khi đã preview đảo hướng, trả polyline về đúng hướng gốc.
                if (isCurrentlyReversed)
                {
                    polyline.ReverseCurve();
                }

                result = currentSettings;
                createdCount = 0;
                angularCreated = 0;
                angularSkipped = 0;
                return false;
            }

            result = currentSettings;
            createdCount = finalCreatedCount;
            angularCreated = finalAngularCreated;
            angularSkipped = finalAngularSkipped;
            return true;
        }

        private static double GetPolylineSignedArea(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            if (polyline == null || polyline.NumberOfVertices < 3)
            {
                return 0.0;
            }

            double area = 0.0;
            int count = polyline.NumberOfVertices;
            for (int i = 0; i < count; i++)
            {
                Point2d current = polyline.GetPoint2dAt(i);
                Point2d next = polyline.GetPoint2dAt((i + 1) % count);
                area += current.X * next.Y - next.X * current.Y;
            }

            return area / 2.0;
        }

        private static double GetPolylineSignedArea(IList<Point2d> vertices)
        {
            if (vertices == null || vertices.Count < 3)
            {
                return 0.0;
            }

            double area = 0.0;
            int count = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Point2d current = vertices[i];
                Point2d next = vertices[(i + 1) % count];
                area += current.X * next.Y - next.X * current.Y;
            }

            return area / 2.0;
        }

        private sealed class DpaDimAutoPlineSettings
        {
            public double OffsetMul { get; set; } = 1.0;
            public double DimOffsetMul { get; set; } = 1.0;
            public string Orientation { get; set; } = "Keep current";
            public bool CreateAngular { get; set; } = false;
            public bool UnifiedBaseline { get; set; } = false;
            public bool AddEnvelope { get; set; } = true;

            public static DpaDimAutoPlineSettings LoadFromStore()
            {
                return LoadFromString(WorkspaceUiStateStore.GetValue("dpa.settings"));
            }

            private static DpaDimAutoPlineSettings LoadFromString(string data)
            {
                DpaDimAutoPlineSettings settings = new DpaDimAutoPlineSettings();
                if (string.IsNullOrWhiteSpace(data))
                {
                    return settings;
                }

                try
                {
                    string[] parts = data.Split('\t');
                    if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double offset))
                    {
                        settings.OffsetMul = offset;
                    }

                    if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dimOffset))
                    {
                        settings.DimOffsetMul = dimOffset;
                    }

                    if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        settings.Orientation = parts[2];
                    }

                    if (parts.Length >= 4 && bool.TryParse(parts[3], out bool createAngular))
                    {
                        settings.CreateAngular = createAngular;
                    }

                    if (parts.Length >= 5 && bool.TryParse(parts[4], out bool unifiedBaseline))
                    {
                        settings.UnifiedBaseline = unifiedBaseline;
                    }

                    if (parts.Length >= 6 && bool.TryParse(parts[5], out bool addEnvelope))
                    {
                        settings.AddEnvelope = addEnvelope;
                    }
                }
                catch
                {
                    return new DpaDimAutoPlineSettings();
                }

                return settings;
            }

            public void SaveToStore()
            {
                WorkspaceUiStateStore.SaveValue("dpa.settings", ToSaveString());
            }

            private string ToSaveString()
            {
                return string.Join("\t",
                    new[]
                    {
                        OffsetMul.ToString("0.######", CultureInfo.InvariantCulture),
                        DimOffsetMul.ToString("0.######", CultureInfo.InvariantCulture),
                        Orientation ?? "Keep current",
                        CreateAngular.ToString(),
                        UnifiedBaseline.ToString(),
                        AddEnvelope.ToString()
                    });
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
            ConfigureDimension(dim, layerId, db, 0.0);

            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        private ObjectId CreateRotatedDimWithLayer(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            ObjectId layerId,
            double angle,
            Point3d p1,
            Point3d p2,
            Point3d dimPoint,
            double scaleFactor)
        {
            RotatedDimension dim = new RotatedDimension(
                angle,
                p1,
                p2,
                dimPoint,
                string.Empty,
                db.Dimstyle);
            ConfigureDimension(dim, layerId, db, scaleFactor);

            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
            return dim.ObjectId;
        }

        private ObjectId CreateAlignedDimWithLayer(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            ObjectId layerId,
            Point3d p1,
            Point3d p2,
            Point3d dimPoint,
            double scaleFactor)
        {
            AlignedDimension dim = new AlignedDimension(
                p1,
                p2,
                dimPoint,
                string.Empty,
                db.Dimstyle);
            ConfigureDimension(dim, layerId, db, scaleFactor);

            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
            return dim.ObjectId;
        }

        // Trả về ObjectId.Null nếu không dựng được (ví dụ 2 tia suy biến) để caller có
        // thể đếm số góc bị bỏ qua và báo cho người dùng biết, thay vì im lặng như trước.
        private ObjectId CreateAngularDimWithLayer(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            ObjectId layerId,
            Point3d center,
            Point3d firstLine,
            Point3d secondLine,
            Point3d dimPoint,
            double scaleFactor)
        {
            try
            {
                // AutoCAD .NET không có lớp "AngularDimension" chung - lớp đúng cho góc đo
                // qua 1 đỉnh + 2 tia là Point3AngularDimension (đây chính là lý do bản cũ
                // dùng reflection dò "AngularDimension" luôn trả về null và im lặng bỏ qua).
                Point3AngularDimension dim = new Point3AngularDimension(
                    center,
                    firstLine,
                    secondLine,
                    dimPoint,
                    string.Empty,
                    db.Dimstyle);
                ConfigureDimension(dim, layerId, db, scaleFactor);
                ms.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                return dim.ObjectId;
            }
            catch (System.Exception)
            {
                return ObjectId.Null;
            }
        }

        // Điểm đặt cung đo góc phải nằm trên đường phân giác của góc thật giữa 2 tia
        // (không phải một offset cố định (+30,+30) như trước), nếu không AutoCAD có thể
        // đo nhầm sang góc phản (VD 350° thay vì 10°).
        private static bool TryGetAngularArcPoint(
            Point3d center,
            Point3d firstRay,
            Point3d secondRay,
            double offset,
            out Point3d arcPoint)
        {
            arcPoint = center;

            double v1x = firstRay.X - center.X;
            double v1y = firstRay.Y - center.Y;
            double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);

            double v2x = secondRay.X - center.X;
            double v2y = secondRay.Y - center.Y;
            double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (len1 < 1e-9 || len2 < 1e-9)
            {
                return false;
            }

            double u1x = v1x / len1;
            double u1y = v1y / len1;
            double u2x = v2x / len2;
            double u2y = v2y / len2;

            double bisectorX = u1x + u2x;
            double bisectorY = u1y + u2y;
            double bisectorLen = Math.Sqrt(bisectorX * bisectorX + bisectorY * bisectorY);

            if (bisectorLen < 1e-9)
            {
                // 2 tia gần như ngược chiều nhau (góc ~180°): dùng pháp tuyến của tia 1 làm hướng dự phòng.
                bisectorX = -u1y;
                bisectorY = u1x;
                bisectorLen = 1.0;
            }

            double scale = Math.Max(offset, 1e-6) / bisectorLen;
            arcPoint = new Point3d(
                center.X + bisectorX * scale,
                center.Y + bisectorY * scale,
                0.0);
            return true;
        }

        private ObjectId CreateRadialDimWithLayer(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            ObjectId layerId,
            Point3d centerPoint,
            Point3d chordPoint,
            double leaderLength,
            double scaleFactor)
        {
            RadialDimension dim = new RadialDimension(
                centerPoint,
                chordPoint,
                leaderLength,
                string.Empty,
                db.Dimstyle);
            ConfigureDimension(dim, layerId, db, scaleFactor);

            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
            return dim.ObjectId;
        }

        private void ConfigureDimension(Dimension dim, ObjectId layerId)
        {
            ConfigureDimension(dim, layerId, null, 0.0);
        }

        // dimLinearScale ở đây là DIMLFAC (Dimension Linear Scale Factor - hệ số nhân vào
        // GIÁ TRỊ ĐO được hiển thị trong text của dim), KHÔNG phải DIMSCALE (hệ số phóng to
        // chữ/mũi tên - đã bỏ khỏi command này, xem ghi chú bên dưới). <= 0 nghĩa là "không
        // đụng vào", để dim tự thừa hưởng DIMLFAC hiện hành của bản vẽ.
        private void ConfigureDimension(Dimension dim, ObjectId layerId, Database db, double dimLinearScale)
        {
            if (!layerId.IsNull)
            {
                dim.LayerId = layerId;
            }
            else
            {
                dim.Layer = "_mss.kichthuoc";
            }

            if (dimLinearScale > 0.0)
            {
                dim.Dimlfac = dimLinearScale;
            }

            // KHÔNG override Dimscale/TextHeight ở đây: dim mới tạo (đã gán db.Dimstyle qua
            // constructor) tự động thừa hưởng đúng DIMSCALE + DIMTXT hiện hành của bản vẽ, y
            // hệt như khi người dùng tự dim tay - không cần 1 nút "resize" riêng cho việc đó.
            ApplyCurrentDimStyleTextSettings(dim, db);
        }

        private static void ApplyCurrentDimStyleTextSettings(Dimension dim, Database db)
        {
            if (dim == null)
            {
                return;
            }

            try
            {
                if (db != null)
                {
                    if (!db.Textstyle.IsNull)
                    {
                        PropertyInfo textStyleProperty = dim.GetType().GetProperty("TextStyleId");
                        if (textStyleProperty != null && textStyleProperty.CanWrite)
                        {
                            textStyleProperty.SetValue(dim, db.Textstyle);
                        }

                        PropertyInfo textStylePropertyAlt = dim.GetType().GetProperty("TextStyle");
                        if (textStylePropertyAlt != null && textStylePropertyAlt.CanWrite)
                        {
                            textStylePropertyAlt.SetValue(dim, db.Textstyle);
                        }
                    }
                }
            }
            catch
            {
            }
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

        private bool TryGetDddDirectionalPoint(
            Entity entity,
            Extents3d extents,
            Point3d origin,
            bool useXAxis,
            double direction,
            out Point3d point)
        {
            point = default;

            if (!IsDddRayCandidate(extents, origin, useXAxis, direction))
            {
                return false;
            }

            using (Line scanLine = useXAxis
                ? CreateDddHorizontalScanLine(origin, direction)
                : CreateDddVerticalScanLine(origin, direction))
            {
                Point3dCollection intersections =
                    TryGetDddIntersections(entity, scanLine, extents, useXAxis);
                if (intersections == null || intersections.Count == 0)
                {
                    return false;
                }

                bool found = false;
                double bestProjectedDistance = double.MaxValue;
                foreach (Point3d candidate in intersections)
                {
                    double projectedDistance = useXAxis
                        ? (candidate.X - origin.X) * direction
                        : (candidate.Y - origin.Y) * direction;

                    if (projectedDistance < -AutoDimTolerance ||
                        projectedDistance >= bestProjectedDistance)
                    {
                        continue;
                    }

                    bestProjectedDistance = projectedDistance;
                    point = candidate;
                    found = true;
                }

                return found;
            }
        }

        private bool IsDddRayCandidate(
            Extents3d extents,
            Point3d origin,
            bool useXAxis,
            double direction)
        {
            if (useXAxis)
            {
                if (origin.Y < extents.MinPoint.Y - AutoDimTolerance ||
                    origin.Y > extents.MaxPoint.Y + AutoDimTolerance)
                {
                    return false;
                }

                return direction > 0.0
                    ? extents.MaxPoint.X > origin.X + AutoDimTolerance
                    : extents.MinPoint.X < origin.X - AutoDimTolerance;
            }

            if (origin.X < extents.MinPoint.X - AutoDimTolerance ||
                origin.X > extents.MaxPoint.X + AutoDimTolerance)
            {
                return false;
            }

            return direction > 0.0
                ? extents.MaxPoint.Y > origin.Y + AutoDimTolerance
                : extents.MinPoint.Y < origin.Y - AutoDimTolerance;
        }

        private Point3dCollection TryGetDddIntersections(
            Entity entity,
            Line scanLine,
            Extents3d extents,
            bool useXAxis)
        {
            if (entity == null || scanLine == null)
            {
                return null;
            }

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

            return entity is BlockReference
                ? BuildDddExtentsFallbackIntersections(extents, scanLine, useXAxis)
                : null;
        }

        private Point3dCollection BuildDddExtentsFallbackIntersections(
            Extents3d extents,
            Line scanLine,
            bool useXAxis)
        {
            Point3dCollection points = new Point3dCollection();
            if (useXAxis)
            {
                double scanY = scanLine.StartPoint.Y;
                AddDddIntersectionPoint(points, new Point3d(extents.MinPoint.X, scanY, 0.0));
                AddDddIntersectionPoint(points, new Point3d(extents.MaxPoint.X, scanY, 0.0));
                return points;
            }

            double scanX = scanLine.StartPoint.X;
            AddDddIntersectionPoint(points, new Point3d(scanX, extents.MinPoint.Y, 0.0));
            AddDddIntersectionPoint(points, new Point3d(scanX, extents.MaxPoint.Y, 0.0));
            return points;
        }

        private void AddDddIntersectionPoint(Point3dCollection points, Point3d candidate)
        {
            foreach (Point3d existing in points)
            {
                if (existing.DistanceTo(candidate) <= AutoDimTolerance)
                {
                    return;
                }
            }

            points.Add(candidate);
        }

        private Line CreateDddHorizontalScanLine(Point3d origin, double direction)
        {
            return new Line(
                origin,
                new Point3d(
                    origin.X + DddSearchDistance * direction,
                    origin.Y,
                    origin.Z));
        }

        private Line CreateDddVerticalScanLine(Point3d origin, double direction)
        {
            return new Line(
                origin,
                new Point3d(
                    origin.X,
                    origin.Y + DddSearchDistance * direction,
                    origin.Z));
        }

        private static void WriteDddNoSurroundingTargetMessage(
            Editor ed,
            DddTargetFilter targetFilter)
        {
            if (targetFilter == null)
            {
                ed.WriteMessage("\nDDD_Dim_4_direction: không tìm thấy đối tượng bao quanh phù hợp.");
                return;
            }

            ed.WriteMessage(
                "\nDDD_Dim_4_direction: không tìm thấy đối tượng bao quanh phù hợp " +
                $"với filter hiện tại ({targetFilter.ToDisplayText()}). Hãy thử Pick lại target hoặc chọn None.");
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

            WorkspaceUiStateStore.SaveValue("daa.baseMode", baseMode.ToString());
            return true;
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

                if (PromptForDddTargetSample(ed, db, out DddTargetFilter updatedFilter))
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

            if (!TryGetDddObjectKind(entity, out DddObjectKind kind) || kind != targetFilter.Kind)
            {
                return false;
            }

            if (targetFilter.IsClosed.HasValue && kind == DddObjectKind.Polyline)
            {
                bool? entityIsClosed = null;
                if (entity is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
                {
                    entityIsClosed = pl.Closed;
                }
                else if (entity is Polyline2d p2d)
                {
                    entityIsClosed = p2d.Closed;
                }
                else if (entity is Polyline3d p3d)
                {
                    entityIsClosed = p3d.Closed;
                }

                if (entityIsClosed != targetFilter.IsClosed)
                {
                    return false;
                }
            }

            return true;
        }

        // Được gọi khi PromptForDddFilterMode đã nhận keyword "Pick" - đi thẳng
        // vào chọn đối tượng mẫu, không hỏi lại Pick/None một lần nữa (thừa và
        // vô nghĩa vì người dùng vừa chọn Pick xong).
        private bool PromptForDddTargetSample(
            Editor ed,
            Database db,
            out DddTargetFilter targetFilter)
        {
            targetFilter = null;

            while (true)
            {
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

            if (!TryGetDddObjectKind(entity, out DddObjectKind kind))
            {
                return false;
            }

            targetFilter = new DddTargetFilter()
            {
                Kind = kind,
                LayerName = entity.Layer
            };

            if (entity is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
            {
                targetFilter.IsClosed = pl.Closed;
            }
            else if (entity is Polyline2d p2d)
            {
                targetFilter.IsClosed = p2d.Closed;
            }
            else if (entity is Polyline3d p3d)
            {
                targetFilter.IsClosed = p3d.Closed;
            }

            return true;
        }

        private bool TryGetDddObjectKind(Entity entity, out DddObjectKind kind)
        {
            if (entity is Line)
            {
                kind = DddObjectKind.Line;
                return true;
            }

            if (entity is Autodesk.AutoCAD.DatabaseServices.Polyline ||
                entity is Polyline2d ||
                entity is Polyline3d)
            {
                kind = DddObjectKind.Polyline;
                return true;
            }

            if (entity is BlockReference)
            {
                kind = DddObjectKind.Block;
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

        private enum DddObjectKind
        {
            Line,
            Polyline,
            Block
        }

        private sealed class DddTargetFilter
        {
            public DddObjectKind Kind { get; set; }
            public string LayerName { get; set; }
            public bool? IsClosed { get; set; }

            public string ToDisplayText()
            {
                string text = $"{Kind} | {LayerName}";
                if (IsClosed.HasValue)
                {
                    text += $" | Closed={IsClosed.Value}";
                }

                return text;
            }
        }

        private static class DddTargetFilterStore
        {
            // Save() ghi vào WorkspaceUiStateStore (key "ddd.targetFilter") -
            // Load() phải đọc từ đúng chỗ đó. Trước đây Load() đọc từ một file
            // .tsv rời rạc, không liên quan gì tới Save(), nên đối tượng mẫu
            // vừa Pick không bao giờ thực sự được nhớ lại cho lần sau.
            public static DddTargetFilter Load()
            {
                try
                {
                    string raw = WorkspaceUiStateStore.GetValue("ddd.targetFilter");
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return null;
                    }

                    string[] parts = raw.Split('\t');
                    if (parts.Length < 2)
                    {
                        return null;
                    }

                    if (!Enum.TryParse(parts[0].Trim(), true, out DddObjectKind kind))
                    {
                        return null;
                    }

                    string layerName = parts[1].Trim();
                    if (string.IsNullOrWhiteSpace(layerName))
                    {
                        return null;
                    }

                    var filter = new DddTargetFilter
                    {
                        Kind = kind,
                        LayerName = layerName
                    };

                    if (parts.Length >= 3 && bool.TryParse(parts[2].Trim(), out bool isClosed))
                    {
                        filter.IsClosed = isClosed;
                    }

                    return filter;
                }
                catch
                {
                    return null;
                }
            }

            public static void Save(DddTargetFilter filter)
            {
                if (filter == null)
                {
                    WorkspaceUiStateStore.SaveValue("ddd.targetFilter", null);
                    return;
                }

                string isClosedString = filter.IsClosed.HasValue ? $"\t{filter.IsClosed.Value}" : string.Empty;
                string valueToSave = filter.Kind + "\t" + (filter.LayerName ?? string.Empty) + isClosedString;

                WorkspaceUiStateStore.SaveValue("ddd.targetFilter", valueToSave);
            }
        }

        private enum DaaBaseMode
        {
            Object,
            Point
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

                ObjectId[] savedIds = GetValidLastDddSourceIds(db);
                string message = savedIds != null
                    ? "\nChọn đối tượng gốc hoặc nhóm đối tượng (Enter = dùng lựa chọn đã lưu):"
                    : "\nChọn đối tượng gốc hoặc nhóm đối tượng:";

                SelectionSet sourceSelection = PromptForSelectionOrSaved(
                    ed,
                    message,
                    savedIds,
                    out bool useSaved,
                    out bool cancelled);

                if (cancelled)
                {
                    return false;
                }

                sourceIds = useSaved ? savedIds : sourceSelection.GetObjectIds();
                if (sourceIds != null && sourceIds.Length > 0)
                {
                    return true;
                }
            }
        }

        // Cho phép nhấn Enter/Space để dùng lại lựa chọn gốc đã lưu từ lần
        // chạy trước, hoặc pick đối tượng mới như bình thường.
        private SelectionSet PromptForSelectionOrSaved(
            Editor ed,
            string message,
            ObjectId[] savedIds,
            out bool useSaved,
            out bool cancelled)
        {
            useSaved = false;
            cancelled = false;

            while (true)
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = message
                };

                PromptSelectionResult result = ed.GetSelection(options);
                if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
                {
                    return result.Value;
                }

                if (result.Status == PromptStatus.Cancel)
                {
                    cancelled = true;
                    return null;
                }

                if (savedIds != null && savedIds.Length > 0)
                {
                    useSaved = true;
                    return null;
                }

                ed.WriteMessage("\nChưa chọn được đối tượng hợp lệ, hãy chọn lại.");
            }
        }

        private static ObjectId[] GetValidLastDddSourceIds(Database db)
        {
            if (_lastDddSourceIds == null || _lastDddSourceIds.Length == 0)
            {
                return null;
            }

            List<ObjectId> valid = new List<ObjectId>();
            foreach (ObjectId id in _lastDddSourceIds)
            {
                if (id.IsNull || !id.IsValid || id.IsErased)
                {
                    continue;
                }

                if (db != null && id.Database != db)
                {
                    continue;
                }

                valid.Add(id);
            }

            return valid.Count > 0 ? valid.ToArray() : null;
        }
    }
}
