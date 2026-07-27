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
                if (!TryShowDpaSettingsDialog(settings, out DpaDimAutoPlineSettings editedSettings))
                {
                    return;
                }

                settings = editedSettings;
                settings.SaveToStore();

                if (!string.Equals(settings.Orientation, "Keep current", StringComparison.OrdinalIgnoreCase))
                {
                    double initialSignedArea = GetPolylineSignedArea(polyline);
                    bool shouldReverse = false;

                    if (string.Equals(settings.Orientation, "Counterclockwise", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldReverse = initialSignedArea < 0.0;
                    }
                    else if (string.Equals(settings.Orientation, "Clockwise", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldReverse = initialSignedArea >= 0.0;
                    }

                    if (shouldReverse)
                    {
                        polyline.ReverseCurve();
                    }
                }

                int startIndex = 1;
                int endIndex = vertexCount;

                double scaleFactor = settings.ScaleFactor;
                double offsetMul = settings.OffsetMul;
                double dimOffsetMul = settings.DimOffsetMul;

                int start0 = NormalizeVertexIndex(startIndex, vertexCount);
                int end0 = NormalizeVertexIndex(endIndex, vertexCount);

                if (start0 < 0 || end0 < 0 || start0 >= end0 || end0 >= vertexCount)
                {
                    ed.WriteMessage("\nDPA_DimAutoPline: phạm vi đỉnh không hợp lệ.");
                    return;
                }

                List<Point2d> vertices = new List<Point2d>();
                for (int i = 0; i < vertexCount; i++)
                {
                    vertices.Add(polyline.GetPoint2dAt(i));
                }

                double signedArea = GetPolylineSignedArea(vertices);
                double baseOffset = 70.0;
                double sf = scaleFactor > 0.0 ? scaleFactor : 1.0;
                double dimOffset = Math.Max(15.0, baseOffset / sf);
                double effectiveOffset = dimOffset * offsetMul * dimOffsetMul;

                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                ObjectId layerId = GetCurrentLayerId(tr, db);
                int createdCount = 0;
                for (int n = start0; n < end0; n++)
                {
                    Point2d p1 = vertices[n];
                    Point2d p2 = vertices[n + 1];

                    bool isXEqual = Math.Abs(p1.X - p2.X) < 1e-6;
                    bool isYEqual = Math.Abs(p1.Y - p2.Y) < 1e-6;

                    if (isXEqual && !isYEqual)
                    {
                        double midX = (p1.X + p2.X) / 2.0;
                        double midY = (p1.Y + p2.Y) / 2.0;
                        double sign = signedArea > 0.0 ? -1.0 : 1.0;
                        Point3d dimPoint = new Point3d(midX + sign * effectiveOffset, midY, 0.0);
                        CreateRotatedDimWithLayer(
                            ms,
                            tr,
                            db,
                            layerId,
                            Math.PI / 2.0,
                            new Point3d(p1.X, p1.Y, 0.0),
                            new Point3d(p2.X, p2.Y, 0.0),
                            dimPoint,
                            scaleFactor);
                        createdCount++;
                    }
                    else if (isYEqual && !isXEqual)
                    {
                        double midX = (p1.X + p2.X) / 2.0;
                        double midY = (p1.Y + p2.Y) / 2.0;
                        double sign = signedArea > 0.0 ? -1.0 : 1.0;
                        Point3d dimPoint = new Point3d(midX, midY + sign * effectiveOffset, 0.0);
                        CreateRotatedDimWithLayer(
                            ms,
                            tr,
                            db,
                            layerId,
                            0.0,
                            new Point3d(p1.X, p1.Y, 0.0),
                            new Point3d(p2.X, p2.Y, 0.0),
                            dimPoint,
                            scaleFactor);
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
                        double sign = signedArea > 0.0 ? -1.0 : 1.0;
                        double midX = (p1.X + p2.X) / 2.0;
                        double midY = (p1.Y + p2.Y) / 2.0;
                        Point3d dimPoint = new Point3d(midX + nx * sign * effectiveOffset, midY + ny * sign * effectiveOffset, 0.0);
                        CreateAlignedDimWithLayer(
                            ms,
                            tr,
                            db,
                            layerId,
                            new Point3d(p1.X, p1.Y, 0.0),
                            new Point3d(p2.X, p2.Y, 0.0),
                            dimPoint,
                            scaleFactor);
                        createdCount++;

                        if (settings.CreateAngular)
                        {
                            Point3d center = new Point3d(p1.X, p1.Y, 0.0);
                            Point3d firstRay = new Point3d(p2.X, p2.Y, 0.0);
                            Point3d secondRay = n > 0
                                ? new Point3d(vertices[n - 1].X, vertices[n - 1].Y, 0.0)
                                : new Point3d(p1.X - 20.0, p1.Y, 0.0);
                            Point3d angularPoint = new Point3d(
                                center.X + nx * sign * effectiveOffset + 30.0,
                                center.Y + ny * sign * effectiveOffset + 30.0,
                                0.0);
                            CreateAngularDimWithLayer(
                                ms,
                                tr,
                                db,
                                layerId,
                                center,
                                firstRay,
                                secondRay,
                                angularPoint,
                                scaleFactor);
                        }
                    }
                }

                tr.Commit();
                ed.Regen();
                ed.WriteMessage($"\nDPA_DimAutoPline: đã tạo {createdCount} dim.");
            }
        }

        private static int NormalizeVertexIndex(int index, int vertexCount)
        {
            if (index < 0)
            {
                return -1;
            }

            if (index == 0)
            {
                return 0;
            }

            if (index >= 1 && index <= vertexCount)
            {
                return index - 1;
            }

            if (index >= 0 && index < vertexCount)
            {
                return index;
            }

            return -1;
        }

        private static ObjectId GetCurrentLayerId(Transaction tr, Database db)
        {
            try
            {
                if (db == null || tr == null)
                {
                    return ObjectId.Null;
                }

                return db.Clayer;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static bool TryShowDpaSettingsDialog(DpaDimAutoPlineSettings settings, out DpaDimAutoPlineSettings result)
        {
            result = settings ?? new DpaDimAutoPlineSettings();

            WF.Form dialog = new WF.Form
            {
                Text = "DPA Dim Auto Pline",
                Width = 420,
                Height = 340,
                StartPosition = WF.FormStartPosition.CenterScreen,
                FormBorderStyle = WF.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            WF.Label scaleLabel = new WF.Label { Text = "Scale factor:", AutoSize = true, Top = 12, Left = 12 };
            WF.TextBox scaleBox = new WF.TextBox { Text = result.ScaleFactor.ToString("0.######", CultureInfo.InvariantCulture), Top = 8, Left = 180, Width = 120 };

            WF.Label offsetLabel = new WF.Label { Text = "Offset mul:", AutoSize = true, Top = 42, Left = 12 };
            WF.TextBox offsetBox = new WF.TextBox { Text = result.OffsetMul.ToString("0.######", CultureInfo.InvariantCulture), Top = 38, Left = 180, Width = 120 };

            WF.Label dimOffsetLabel = new WF.Label { Text = "Dim offset mul:", AutoSize = true, Top = 72, Left = 12 };
            WF.TextBox dimOffsetBox = new WF.TextBox { Text = result.DimOffsetMul.ToString("0.######", CultureInfo.InvariantCulture), Top = 68, Left = 180, Width = 120 };

            WF.Label orientLabel = new WF.Label { Text = "Polyline orientation:", AutoSize = true, Top = 102, Left = 12 };
            WF.ComboBox orientBox = new WF.ComboBox { DropDownStyle = WF.ComboBoxStyle.DropDownList, Top = 98, Left = 180, Width = 180 };
            orientBox.Items.Add("Keep current");
            orientBox.Items.Add("Counterclockwise");
            orientBox.Items.Add("Clockwise");
            orientBox.SelectedItem = string.IsNullOrWhiteSpace(result.Orientation) ? "Keep current" : result.Orientation;

            WF.CheckBox angularBox = new WF.CheckBox { Text = "Create angular dim", Checked = result.CreateAngular, Top = 132, Left = 180, AutoSize = true };

            WF.Button okButton = new WF.Button { Text = "OK", DialogResult = WF.DialogResult.OK, Top = 168, Left = 180, Width = 80 };
            WF.Button cancelButton = new WF.Button { Text = "Cancel", DialogResult = WF.DialogResult.Cancel, Top = 168, Left = 270, Width = 80 };
            dialog.Controls.Add(scaleLabel);
            dialog.Controls.Add(scaleBox);
            dialog.Controls.Add(offsetLabel);
            dialog.Controls.Add(offsetBox);
            dialog.Controls.Add(dimOffsetLabel);
            dialog.Controls.Add(dimOffsetBox);
            dialog.Controls.Add(orientLabel);
            dialog.Controls.Add(orientBox);
            dialog.Controls.Add(angularBox);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);

            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            WF.DialogResult dialogResult = dialog.ShowDialog();
            if (dialogResult != WF.DialogResult.OK)
            {
                return false;
            }

            try
            {
                result.ScaleFactor = double.Parse(scaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                result.OffsetMul = double.Parse(offsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                result.DimOffsetMul = double.Parse(dimOffsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                result.Orientation = orientBox.SelectedItem?.ToString() ?? "Keep current";
                result.CreateAngular = angularBox.Checked;
            }
            catch
            {
                result = settings ?? new DpaDimAutoPlineSettings();
            }

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
            public double ScaleFactor { get; set; } = 1.0;
            public double OffsetMul { get; set; } = 1.0;
            public double DimOffsetMul { get; set; } = 1.0;
            public string Orientation { get; set; } = "Keep current";
            public bool CreateAngular { get; set; } = false;

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
                    if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
                    {
                        settings.ScaleFactor = scale;
                    }

                    if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double offset))
                    {
                        settings.OffsetMul = offset;
                    }

                    if (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double dimOffset))
                    {
                        settings.DimOffsetMul = dimOffset;
                    }

                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                    {
                        settings.Orientation = parts[3];
                    }

                    if (parts.Length >= 5 && bool.TryParse(parts[4], out bool createAngular))
                    {
                        settings.CreateAngular = createAngular;
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
                        ScaleFactor.ToString("0.######", CultureInfo.InvariantCulture),
                        OffsetMul.ToString("0.######", CultureInfo.InvariantCulture),
                        DimOffsetMul.ToString("0.######", CultureInfo.InvariantCulture),
                        Orientation ?? "Keep current",
                        CreateAngular.ToString()
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
            ConfigureDimension(dim, layerId, db, 1.0);

            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        private void CreateRotatedDimWithLayer(
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
        }

        private void CreateAlignedDimWithLayer(
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
        }

        private void CreateAngularDimWithLayer(
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
                Type angularType = Type.GetType("Autodesk.AutoCAD.DatabaseServices.AngularDimension, acdbmgd", false);
                if (angularType == null)
                {
                    return;
                }

                ConstructorInfo constructor = angularType.GetConstructor(
                    new[]
                    {
                        typeof(Point3d),
                        typeof(Point3d),
                        typeof(Point3d),
                        typeof(Point3d),
                        typeof(string),
                        typeof(ObjectId)
                    });

                if (constructor == null)
                {
                    return;
                }

                object dimObject = constructor.Invoke(
                    new object[]
                    {
                        center,
                        firstLine,
                        secondLine,
                        dimPoint,
                        string.Empty,
                        db.Dimstyle
                    });

                if (dimObject is Dimension dimension)
                {
                    ConfigureDimension(dimension, layerId, db, scaleFactor);
                    ms.AppendEntity(dimension);
                    tr.AddNewlyCreatedDBObject(dimension, true);
                }
            }
            catch (System.Exception)
            {
                // Nếu API không có AngularDimension trên máy build hiện tại thì bỏ qua để giữ lệnh còn chạy.
            }
        }

        private void ConfigureDimension(Dimension dim, ObjectId layerId)
        {
            ConfigureDimension(dim, layerId, null, 1.0);
        }

        private void ConfigureDimension(Dimension dim, ObjectId layerId, Database db, double scaleFactor)
        {
            if (!layerId.IsNull)
            {
                dim.LayerId = layerId;
            }
            else
            {
                dim.Layer = "_mss.kichthuoc";
            }

            if (scaleFactor > 0.0)
            {
                dim.Dimscale = scaleFactor;
            }

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
                    double textHeight = db.Dimasz;
                    if (textHeight > 1e-9)
                    {
                        PropertyInfo textHeightProperty = dim.GetType().GetProperty("TextHeight");
                        if (textHeightProperty != null && textHeightProperty.CanWrite)
                        {
                            textHeightProperty.SetValue(dim, textHeight);
                        }

                        PropertyInfo dimensionTextHeightProperty = dim.GetType().GetProperty("DimensionTextHeight");
                        if (dimensionTextHeightProperty != null && dimensionTextHeightProperty.CanWrite)
                        {
                            dimensionTextHeightProperty.SetValue(dim, textHeight);
                        }

                        PropertyInfo textHeightOverrideProperty = dim.GetType().GetProperty("TextHeightOverride");
                        if (textHeightOverrideProperty != null && textHeightOverrideProperty.CanWrite)
                        {
                            textHeightOverrideProperty.SetValue(dim, textHeight);
                        }
                    }

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
    }
}
