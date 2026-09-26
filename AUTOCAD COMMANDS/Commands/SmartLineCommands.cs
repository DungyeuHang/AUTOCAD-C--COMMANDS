using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;


namespace AUTOCAD_COMMANDS
{

    // ======================================================
    // 1S_SMART_LINE - VẼ LINE TỰ KÉO DÀI TỚI ĐỐI TƯỢNG GẦN NHẤT
    // Mục đích: click điểm đầu, click điểm hướng (hướng bất kỳ, theo Ortho/Polar nếu bật),
    // line tự kéo dài tới đối tượng gần điểm hướng nhất theo hướng đó.
    // Lưu ý:
    // - Giống SDXY: đối tượng nằm giữa điểm đầu và điểm hướng sẽ được bỏ qua,
    //   nên có thể click điểm hướng vượt qua đối tượng không muốn dừng lại.
    // - Bỏ qua Dimension, Text/MText/Attribute, Leader/MLeader, Table, Hatch.
    // - Quét cả nội dung bên trong Block (kể cả block lồng nhau).
    // - Sau mỗi đoạn, điểm cuối thành điểm đầu mới để vẽ tiếp; Enter/Esc để kết thúc.
    // ======================================================
    public class SmartLineCommands
    {
        private const double Tolerance = 1e-6;
        private const int MaxBlockNestingDepth = 16;

        private static readonly RXClass[] ExcludedRxClasses =
        {
            RXObject.GetClass(typeof(Dimension)),
            RXObject.GetClass(typeof(DBText)),
            RXObject.GetClass(typeof(MText)),
            RXObject.GetClass(typeof(Leader)),
            RXObject.GetClass(typeof(MLeader)),
            RXObject.GetClass(typeof(Table)),
            RXObject.GetClass(typeof(Hatch))
        };

        private static readonly RXClass EntityRxClass = RXObject.GetClass(typeof(Entity));

        [CommandMethod("1S_SMART_LINE")]
        public void SmartLine()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            Matrix3d ucsToWcs = ed.CurrentUserCoordinateSystem;

            PromptPointResult startRes = ed.GetPoint("\nChọn điểm đầu line: ");
            if (startRes.Status != PromptStatus.OK)
            {
                return;
            }

            Point3d startUcs = startRes.Value;
            List<ObjectId> createdLineIds = new List<ObjectId>();
            List<Point3d> startHistoryUcs = new List<Point3d>();

            while (true)
            {
                PromptPointOptions dirOptions = new PromptPointOptions(
                    createdLineIds.Count == 0
                        ? "\nChọn điểm xác định hướng line: "
                        : "\nChọn điểm hướng tiếp theo hoặc [Undo] <kết thúc>: ")
                {
                    BasePoint = startUcs,
                    UseBasePoint = true,
                    UseDashedLine = true,
                    AllowNone = true
                };
                if (createdLineIds.Count > 0)
                {
                    dirOptions.Keywords.Add("Undo");
                }

                PromptPointResult dirRes = ed.GetPoint(dirOptions);
                if (dirRes.Status == PromptStatus.Keyword)
                {
                    if (createdLineIds.Count > 0)
                    {
                        EraseLine(db, createdLineIds[createdLineIds.Count - 1]);
                        createdLineIds.RemoveAt(createdLineIds.Count - 1);
                        startUcs = startHistoryUcs[startHistoryUcs.Count - 1];
                        startHistoryUcs.RemoveAt(startHistoryUcs.Count - 1);
                    }

                    continue;
                }

                if (dirRes.Status != PromptStatus.OK)
                {
                    return;
                }

                Point3d startWcs = startUcs.TransformBy(ucsToWcs);
                Point3d probeWcs = dirRes.Value.TransformBy(ucsToWcs);
                Vector3d direction = new Vector3d(
                    probeWcs.X - startWcs.X,
                    probeWcs.Y - startWcs.Y,
                    0.0);
                double probeDistance = direction.Length;
                if (probeDistance < Tolerance)
                {
                    ed.WriteMessage("\nĐiểm hướng trùng điểm đầu, chọn lại.");
                    continue;
                }

                direction = direction / probeDistance;

                Point3d endWcs;
                Point3d? hit = FindNearestHit(db, startWcs, direction, probeDistance);
                if (hit.HasValue)
                {
                    endWcs = new Point3d(hit.Value.X, hit.Value.Y, startWcs.Z);
                }
                else
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào theo hướng đã chọn, vẽ line tới điểm đã click.");
                    endWcs = new Point3d(probeWcs.X, probeWcs.Y, startWcs.Z);
                }

                ObjectId lineId = AppendLine(db, startWcs, endWcs);
                if (lineId.IsNull)
                {
                    ed.WriteMessage("\nLine quá ngắn, chọn lại.");
                    continue;
                }

                createdLineIds.Add(lineId);
                startHistoryUcs.Add(startUcs);
                startUcs = endWcs.TransformBy(ucsToWcs.Inverse());
            }
        }

        private Point3d? FindNearestHit(
            Database db,
            Point3d startWcs,
            Vector3d direction,
            double probeDistance)
        {
            // Điểm được chấp nhận phải nằm sau điểm đầu và không nằm trước điểm hướng.
            // Vì t tăng dần theo hướng, điểm có t nhỏ nhất chính là điểm gần điểm hướng nhất.
            double minT = Math.Max(Tolerance, probeDistance - Tolerance);
            double bestT = double.MaxValue;
            Point3d? bestPoint = null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            using (Ray ray = new Ray { BasePoint = startWcs, UnitDir = direction })
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return null;
                }

                Dictionary<ObjectId, bool> layerVisibility = new Dictionary<ObjectId, bool>();

                foreach (ObjectId id in currentSpace)
                {
                    if (!IsCandidateClass(id.ObjectClass))
                    {
                        continue;
                    }

                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!IsEntityVisible(entity, tr, layerVisibility))
                    {
                        continue;
                    }

                    ScanEntity(
                        entity,
                        Matrix3d.Identity,
                        false,
                        tr,
                        ray,
                        startWcs,
                        direction,
                        minT,
                        layerVisibility,
                        0,
                        ref bestT,
                        ref bestPoint);
                }

                tr.Commit();
            }

            return bestPoint;
        }

        private void ScanEntity(
            Entity entity,
            Matrix3d transform,
            bool isNested,
            Transaction tr,
            Ray ray,
            Point3d startWcs,
            Vector3d direction,
            double minT,
            Dictionary<ObjectId, bool> layerVisibility,
            int depth,
            ref double bestT,
            ref Point3d? bestPoint)
        {
            // Lọc nhanh bằng extents trước khi gọi IntersectWith / explode block.
            if (TryGetExtents(entity, transform, isNested, out Extents3d extents) &&
                !RayMayHitBox(extents, startWcs, direction, minT, bestT))
            {
                return;
            }

            if (entity is BlockReference blockReference)
            {
                if (depth >= MaxBlockNestingDepth)
                {
                    return;
                }

                BlockTableRecord definition =
                    tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (definition == null)
                {
                    return;
                }

                Matrix3d childTransform = transform * blockReference.BlockTransform;
                foreach (ObjectId childId in definition)
                {
                    if (!IsCandidateClass(childId.ObjectClass))
                    {
                        continue;
                    }

                    Entity child = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (!IsEntityVisible(child, tr, layerVisibility))
                    {
                        continue;
                    }

                    ScanEntity(
                        child,
                        childTransform,
                        true,
                        tr,
                        ray,
                        startWcs,
                        direction,
                        minT,
                        layerVisibility,
                        depth + 1,
                        ref bestT,
                        ref bestPoint);
                }

                return;
            }

            Entity target = entity;
            Entity transformedCopy = null;
            if (isNested)
            {
                try
                {
                    transformedCopy = entity.GetTransformedCopy(transform) as Entity;
                }
                catch
                {
                    transformedCopy = null;
                }

                if (transformedCopy == null)
                {
                    return;
                }

                target = transformedCopy;
            }

            try
            {
                Point3dCollection intersections = new Point3dCollection();
                try
                {
                    target.IntersectWith(
                        ray,
                        Intersect.OnBothOperands,
                        intersections,
                        IntPtr.Zero,
                        IntPtr.Zero);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Một số entity đặc biệt không hỗ trợ IntersectWith: bỏ qua.
                    return;
                }

                foreach (Point3d point in intersections)
                {
                    double t = (point.X - startWcs.X) * direction.X +
                               (point.Y - startWcs.Y) * direction.Y;
                    if (t < minT || t >= bestT)
                    {
                        continue;
                    }

                    bestT = t;
                    bestPoint = point;
                }
            }
            finally
            {
                transformedCopy?.Dispose();
            }
        }

        private bool IsCandidateClass(RXClass objectClass)
        {
            if (objectClass == null ||
                (EntityRxClass != null && !objectClass.IsDerivedFrom(EntityRxClass)))
            {
                return false;
            }

            foreach (RXClass excluded in ExcludedRxClasses)
            {
                if (excluded != null && objectClass.IsDerivedFrom(excluded))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsEntityVisible(
            Entity entity,
            Transaction tr,
            Dictionary<ObjectId, bool> layerVisibility)
        {
            if (entity == null || entity.IsErased || !entity.Visible)
            {
                return false;
            }

            ObjectId layerId = entity.LayerId;
            if (layerVisibility.TryGetValue(layerId, out bool visible))
            {
                return visible;
            }

            LayerTableRecord layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
            visible = layer == null || (!layer.IsOff && !layer.IsFrozen);
            layerVisibility[layerId] = visible;
            return visible;
        }

        private bool TryGetExtents(
            Entity entity,
            Matrix3d transform,
            bool applyTransform,
            out Extents3d extents)
        {
            extents = default;
            try
            {
                Extents3d raw = entity.GeometricExtents;
                if (!applyTransform)
                {
                    extents = raw;
                    return true;
                }

                Point3d min = raw.MinPoint;
                Point3d max = raw.MaxPoint;
                Extents3d result = new Extents3d();
                bool first = true;
                for (int i = 0; i < 8; i++)
                {
                    Point3d corner = new Point3d(
                        (i & 1) == 0 ? min.X : max.X,
                        (i & 2) == 0 ? min.Y : max.Y,
                        (i & 4) == 0 ? min.Z : max.Z).TransformBy(transform);
                    if (first)
                    {
                        result = new Extents3d(corner, corner);
                        first = false;
                    }
                    else
                    {
                        result.AddPoint(corner);
                    }
                }

                extents = result;
                return true;
            }
            catch
            {
                // Entity vô hạn (XLine/Ray) hoặc không có extents: để IntersectWith quyết định.
                return false;
            }
        }

        private bool RayMayHitBox(
            Extents3d extents,
            Point3d origin,
            Vector3d direction,
            double minT,
            double bestT)
        {
            // Slab test 2D: tìm đoạn [tEnter, tExit] mà tia nằm trong hộp extents.
            double tEnter = double.NegativeInfinity;
            double tExit = double.PositiveInfinity;

            if (!ClipSlab(origin.X, direction.X, extents.MinPoint.X, extents.MaxPoint.X, ref tEnter, ref tExit) ||
                !ClipSlab(origin.Y, direction.Y, extents.MinPoint.Y, extents.MaxPoint.Y, ref tEnter, ref tExit))
            {
                return false;
            }

            return tExit >= minT - Tolerance && tEnter < bestT;
        }

        private static bool ClipSlab(
            double origin,
            double direction,
            double min,
            double max,
            ref double tEnter,
            ref double tExit)
        {
            if (Math.Abs(direction) < 1e-12)
            {
                return origin >= min - Tolerance && origin <= max + Tolerance;
            }

            double t1 = (min - Tolerance - origin) / direction;
            double t2 = (max + Tolerance - origin) / direction;
            if (t1 > t2)
            {
                double swap = t1;
                t1 = t2;
                t2 = swap;
            }

            tEnter = Math.Max(tEnter, t1);
            tExit = Math.Min(tExit, t2);
            return tEnter <= tExit;
        }

        private ObjectId AppendLine(Database db, Point3d startWcs, Point3d endWcs)
        {
            if (startWcs.DistanceTo(endWcs) < Tolerance)
            {
                return ObjectId.Null;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    return ObjectId.Null;
                }

                Line line = new Line(startWcs, endWcs);
                line.SetDatabaseDefaults(db);
                ObjectId id = currentSpace.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
                return id;
            }
        }

        private void EraseLine(Database db, ObjectId lineId)
        {
            if (lineId.IsNull || lineId.IsErased)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity line = tr.GetObject(lineId, OpenMode.ForWrite) as Entity;
                line?.Erase();
                tr.Commit();
            }
        }
    }
}
