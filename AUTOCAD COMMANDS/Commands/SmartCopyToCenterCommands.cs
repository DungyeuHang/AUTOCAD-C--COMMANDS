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
}
