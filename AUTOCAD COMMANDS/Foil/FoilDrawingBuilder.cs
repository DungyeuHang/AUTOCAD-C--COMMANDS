using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - TANG VE (AUTOCAD ENTITY BUILDER)
    // ------------------------------------------------------------------------------------------
    // Nhan hinh hoc da tinh san (FoilFlatPatternGeometry - thuan toan hoc) va sinh entity.
    //
    // AN TOAN TRANSACTION: ham nay CHI them entity vao transaction do caller mo. Caller quyet
    // dinh Commit hay Abort. Neu co bat ky loi nao, caller Abort => ban ve khong con rac.
    //
    // LAYER: dung lai CadLayerHelper.EnsureLayer co san cua project - layer da ton tai thi
    // dung lai, chua co thi tao. Khong bao gio tao layer trung.
    // ==========================================================================================

    public class FoilDrawResult
    {
        public ObjectId OutlineId { get; set; }

        public List<ObjectId> BendLineIds { get; private set; } = new List<ObjectId>();

        /// <summary>Toan bo entity cua day hinh trinh tu cac buoc chan.</summary>
        public List<ObjectId> StepIds { get; private set; } = new List<ObjectId>();

        public int StepCount { get; set; }

        /// <summary>So duong chan duoc cong be day (goc lom) - moi duong duoc khoanh 1 vong tron.</summary>
        public int CompensatedCount { get; set; }

        public List<string> Warnings { get; private set; } = new List<string>();

        public int UpCount { get; set; }

        public int DownCount { get; set; }

        public bool LayerCreated { get; set; }

        public List<ObjectId> AllIds
        {
            get
            {
                List<ObjectId> ids = new List<ObjectId>();
                if (!OutlineId.IsNull)
                {
                    ids.Add(OutlineId);
                }

                ids.AddRange(BendLineIds);
                ids.AddRange(StepIds);
                return ids;
            }
        }
    }

    public static class FoilDrawingBuilder
    {
        /// <summary>
        /// Ve phoi (hinh chu nhat) va toan bo duong chan.
        /// Nem exception neu that bai - caller phai Abort transaction.
        /// </summary>
        public static FoilDrawResult Draw(
            Database db,
            Transaction tr,
            FoilFlatPatternResult result,
            FoilFlatPatternGeometry geometry,
            double elevation)
        {
            if (db == null) throw new ArgumentNullException("db");
            if (tr == null) throw new ArgumentNullException("tr");
            if (result == null) throw new ArgumentNullException("result");
            if (geometry == null) throw new ArgumentNullException("geometry");

            FoilSettings settings = result.Settings ?? new FoilSettings();
            FoilDrawResult drawResult = new FoilDrawResult();
            drawResult.Warnings.AddRange(geometry.Warnings);

            BlockTableRecord space = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
            if (space == null)
            {
                throw new InvalidOperationException("Khong mo duoc khong gian ve hien hanh.");
            }

            // ---- Layer cho duong chan ----
            bool layerExisted = LayerExists(db, tr, settings.BendLayerName);
            ObjectId bendLayerId = CadLayerHelper.EnsureLayer(db, tr, settings.BendLayerName);
            if (bendLayerId.IsNull)
            {
                throw new InvalidOperationException(
                    "Khong tao/tim duoc layer \"" + settings.BendLayerName + "\" cho duong chan.");
            }

            drawResult.LayerCreated = !layerExisted;

            // ---- Layer cho bien phoi (rong = giu layer hien hanh) ----
            ObjectId outlineLayerId = ObjectId.Null;
            if (!string.IsNullOrWhiteSpace(settings.OutlineLayerName))
            {
                outlineLayerId = CadLayerHelper.EnsureLayer(db, tr, settings.OutlineLayerName.Trim());
                if (outlineLayerId.IsNull)
                {
                    throw new InvalidOperationException(
                        "Khong tao/tim duoc layer \"" + settings.OutlineLayerName + "\" cho bien phoi.");
                }
            }

            // ---- 1. Hinh chu nhat phoi ----
            Polyline outline = new Polyline(geometry.Outline.Count);
            outline.SetDatabaseDefaults(db);
            for (int i = 0; i < geometry.Outline.Count; i++)
            {
                FoilPoint2d p = geometry.Outline[i];
                outline.AddVertexAt(i, new Point2d(p.X, p.Y), 0.0, 0.0, 0.0);
            }

            outline.Closed = true;
            outline.Elevation = elevation;
            if (!outlineLayerId.IsNull)
            {
                outline.LayerId = outlineLayerId;
            }

            space.AppendEntity(outline);
            tr.AddNewlyCreatedDBObject(outline, true);
            drawResult.OutlineId = outline.ObjectId;

            // ---- 2. Duong chan ----
            foreach (FoilBendLineGeometry bendLine in geometry.BendLines)
            {
                Line line = new Line(
                    new Point3d(bendLine.Start.X, bendLine.Start.Y, elevation),
                    new Point3d(bendLine.End.X, bendLine.End.Y, elevation));
                line.SetDatabaseDefaults(db);
                line.LayerId = bendLayerId;

                ApplyBendColor(line, bendLine.Bend.Direction, settings);

                space.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                drawResult.BendLineIds.Add(line.ObjectId);

                if (bendLine.Bend.Direction == FoilBendDirection.Up)
                {
                    drawResult.UpCount++;
                }
                else
                {
                    drawResult.DownCount++;
                }

                // Duong chan duoc CONG BE DAY (goc lom) duoc khoanh mot vong tron ngay tren
                // phoi - dung quy uoc danh dau tay cua xuong, de kiem tra bang mat.
                if (bendLine.Bend.IsThicknessCompensated && !bendLine.IsTangentLine)
                {
                    Point3d center = new Point3d(
                        (bendLine.Start.X + bendLine.End.X) * 0.5,
                        (bendLine.Start.Y + bendLine.End.Y) * 0.5,
                        elevation);

                    Circle mark = new Circle(center, Vector3d.ZAxis, MarkRadius(result, settings));
                    mark.SetDatabaseDefaults(db);
                    mark.LayerId = bendLayerId;
                    ApplyBendColor(mark, bendLine.Bend.Direction, settings);

                    space.AppendEntity(mark);
                    tr.AddNewlyCreatedDBObject(mark, true);
                    drawResult.BendLineIds.Add(mark.ObjectId);
                    drawResult.CompensatedCount++;
                }
            }

            // ---- 3. Day hinh trinh tu cac buoc chan ----
            if (geometry.Steps.Count > 0)
            {
                DrawBendSteps(db, tr, space, geometry, settings, elevation, drawResult);
            }

            return drawResult;
        }

        /// <summary>
        /// Ve day hinh trinh tu chan: moi buoc la mot polyline ho mo ta mat cat sau lan chan do,
        /// kem mot vong tron danh dau dung duong chan vua thuc hien (mau theo huong UP/DOWN)
        /// va mot dong chu mo ta buoc.
        /// </summary>
        private static void DrawBendSteps(
            Database db,
            Transaction tr,
            BlockTableRecord space,
            FoilFlatPatternGeometry geometry,
            FoilSettings settings,
            double elevation,
            FoilDrawResult drawResult)
        {
            string layerName = string.IsNullOrWhiteSpace(settings.StepLayerName)
                ? "_mss.buocchan"
                : settings.StepLayerName.Trim();

            ObjectId stepLayerId = CadLayerHelper.EnsureLayer(db, tr, layerName);
            if (stepLayerId.IsNull)
            {
                throw new InvalidOperationException(
                    "Khong tao/tim duoc layer \"" + layerName + "\" cho hinh cac buoc chan.");
            }

            double textHeight = geometry.StepTextHeight > 0.0 ? geometry.StepTextHeight : 1.0;

            foreach (FoilBendStepGeometry step in geometry.Steps)
            {
                if (step.Points.Count >= 2)
                {
                    Polyline outline = new Polyline(step.Points.Count);
                    outline.SetDatabaseDefaults(db);
                    for (int i = 0; i < step.Points.Count; i++)
                    {
                        outline.AddVertexAt(i, new Point2d(step.Points[i].X, step.Points[i].Y), 0.0, 0.0, 0.0);
                    }

                    outline.Closed = false;
                    outline.Elevation = elevation;
                    outline.LayerId = stepLayerId;

                    space.AppendEntity(outline);
                    tr.AddNewlyCreatedDBObject(outline, true);
                    drawResult.StepIds.Add(outline.ObjectId);
                }

                // Danh dau duong chan vua thuc hien, mau theo huong chan.
                if (step.HasMarker && step.Step.FormedBend != null)
                {
                    Circle marker = new Circle(
                        new Point3d(step.Marker.X, step.Marker.Y, elevation),
                        Vector3d.ZAxis,
                        textHeight * 0.45);
                    marker.SetDatabaseDefaults(db);
                    marker.LayerId = stepLayerId;
                    ApplyBendColor(marker, step.Step.FormedBend.Direction, settings);

                    space.AppendEntity(marker);
                    tr.AddNewlyCreatedDBObject(marker, true);
                    drawResult.StepIds.Add(marker.ObjectId);
                }

                if (!string.IsNullOrEmpty(step.Label))
                {
                    DBText label = new DBText
                    {
                        Position = new Point3d(step.LabelPosition.X, step.LabelPosition.Y, elevation),
                        Height = textHeight,
                        TextString = step.Label
                    };
                    label.SetDatabaseDefaults(db);
                    label.LayerId = stepLayerId;

                    space.AppendEntity(label);
                    tr.AddNewlyCreatedDBObject(label, true);
                    drawResult.StepIds.Add(label.ObjectId);
                }

                drawResult.StepCount++;
            }
        }

        /// <summary>
        /// Ap mau theo huong chan. Mac dinh: UP = ByLayer, DOWN = ACI 8 (xam chuan cua AutoCAD).
        /// Dung ACI chu khong dung RGB de dong bo voi cach lam mau hien tai cua project.
        /// </summary>
        private static void ApplyBendColor(Entity entity, FoilBendDirection direction, FoilSettings settings)
        {
            FoilColorMode mode = direction == FoilBendDirection.Up
                ? settings.BendUpColorMode
                : settings.BendDownColorMode;

            if (mode == FoilColorMode.ByLayer)
            {
                entity.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                return;
            }

            int index = direction == FoilBendDirection.Up
                ? settings.BendUpColorIndex
                : settings.BendDownColorIndex;

            if (index < 0) index = 0;
            if (index > 256) index = 256;

            entity.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)index);
        }

        /// <summary>
        /// Ban kinh vong tron danh dau duong chan duoc cong be day.
        /// Lay theo chieu rong phoi de luon nhin thay duoc o moi co chi tiet.
        /// </summary>
        private static double MarkRadius(FoilFlatPatternResult result, FoilSettings settings)
        {
            double radius = result.BlankWidth * 0.02;
            double minimum = Math.Max(settings.Thickness * 1.5, 1e-3);
            return radius < minimum ? minimum : radius;
        }

        /// <summary>
        /// Lay danh sach ten layer cua ban ve. Dat o tang ve de tang UI khong phai cham
        /// vao Database (nho vay form nhap lieu la WinForms thuan, test duoc ngoai AutoCAD).
        /// </summary>
        public static List<string> GetLayerNames(Database db)
        {
            List<string> names = new List<string>();
            if (db == null)
            {
                return names;
            }

            try
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
                    if (lt != null)
                    {
                        foreach (ObjectId id in lt)
                        {
                            LayerTableRecord ltr = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                            if (ltr != null && !string.IsNullOrWhiteSpace(ltr.Name))
                            {
                                names.Add(ltr.Name);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Khong lay duoc danh sach layer thi de trong - nguoi dung van go tay duoc.
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static bool LayerExists(Database db, Transaction tr, string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return false;
            }

            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            return lt != null && lt.Has(layerName);
        }
    }
}
