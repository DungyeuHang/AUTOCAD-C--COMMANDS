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

        /// <summary>So bong tron danh so thu tu buoc chan da ve o mep phoi.</summary>
        public int StepMarkCount { get; set; }

        public int StepCount { get; set; }

        /// <summary>So duong chan duoc cong be day (goc lom). Chi de bao cao, khong ve danh dau.</summary>
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

                // Duong chan duoc CONG BE DAY (goc lom) chi duoc DEM de bao cao ra dong lenh,
                // KHONG ve them entity danh dau nao tren phoi.
                if (bendLine.Bend.IsThicknessCompensated && !bendLine.IsTangentLine)
                {
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
        /// Ve luoi hinh trinh tu chan. Moi o gom:
        ///   * hinh COI (die) va CHAY DAO - cho tho biet dat phoi theo chieu nao tren may,
        ///   * mat cat chi tiet SAU lan chan do, ve trong HE TOA DO MAY,
        ///   * dau cham tai dinh goc vua chan,
        ///   * hai dong chu: ten buoc va thong so ga dat.
        ///
        /// Hinh coi / dao nam tren LAYER RIENG de tat di cho de nhin khi can.
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

            ObjectId toolLayerId = stepLayerId;
            if (settings.DrawTooling)
            {
                string toolLayerName = string.IsNullOrWhiteSpace(settings.ToolingLayerName)
                    ? "_mss.dungcu"
                    : settings.ToolingLayerName.Trim();

                ObjectId id = CadLayerHelper.EnsureLayer(db, tr, toolLayerName);
                if (!id.IsNull)
                {
                    toolLayerId = id;
                }
            }

            double textHeight = geometry.StepTextHeight > 0.0 ? geometry.StepTextHeight : 1.0;

            foreach (FoilBendStepGeometry step in geometry.Steps)
            {
                // ---- Dung cu truoc, de nam duoi hinh chi tiet ----
                // Duong bao dung cu la duong KIN: day dung la da giac ma phep kiem va cham dung.
                foreach (List<FoilPoint2d> tool in step.ToolOutlines)
                {
                    AddPolyline(db, tr, space, tool, toolLayerId, elevation, true, drawResult);
                }

                // ---- Mat cat chi tiet ----
                AddPolyline(db, tr, space, step.Points, stepLayerId, elevation, false, drawResult);

                // ---- Dau cham tai dinh goc vua chan ----
                if (step.HasMarker && step.Step != null && step.Step.FormedBend != null)
                {
                    Circle marker = new Circle(
                        new Point3d(step.Marker.X, step.Marker.Y, elevation),
                        Vector3d.ZAxis,
                        textHeight * 0.3);
                    marker.SetDatabaseDefaults(db);
                    marker.LayerId = stepLayerId;
                    ApplyBendColor(marker, step.Step.FormedBend.Direction, settings);

                    space.AppendEntity(marker);
                    tr.AddNewlyCreatedDBObject(marker, true);
                    drawResult.StepIds.Add(marker.ObjectId);
                }

                AddText(db, tr, space, step.Label, step.LabelPosition, stepLayerId,
                    textHeight, elevation, drawResult);
                AddText(db, tr, space, step.Detail, step.DetailPosition, stepLayerId,
                    textHeight * 0.8, elevation, drawResult);

                drawResult.StepCount++;
            }

            // ---- So thu tu buoc chan o mep phai phoi ----
            foreach (FoilStepMarkGeometry mark in geometry.StepMarks)
            {
                Circle balloon = new Circle(
                    new Point3d(mark.Center.X, mark.Center.Y, elevation),
                    Vector3d.ZAxis,
                    mark.Radius);
                balloon.SetDatabaseDefaults(db);
                balloon.LayerId = stepLayerId;

                space.AppendEntity(balloon);
                tr.AddNewlyCreatedDBObject(balloon, true);
                drawResult.StepIds.Add(balloon.ObjectId);

                AddCenteredText(db, tr, space, mark.Text, mark.Center, stepLayerId,
                    geometry.StepMarkTextHeight, elevation, drawResult);

                drawResult.StepMarkCount++;
            }
        }

        /// <summary>
        /// Chu CAN GIUA theo ca hai phuong quanh mot diem. Khi HorizontalMode / VerticalMode
        /// khac mac dinh thi AutoCAD lay AlignmentPoint chu khong lay Position, nen phai gan
        /// AlignmentPoint SAU khi dat hai che do - dat truoc se bi ghi de.
        /// </summary>
        private static void AddCenteredText(
            Database db,
            Transaction tr,
            BlockTableRecord space,
            string content,
            FoilPoint2d center,
            ObjectId layerId,
            double height,
            double elevation,
            FoilDrawResult drawResult)
        {
            if (string.IsNullOrEmpty(content) || height <= 0.0)
            {
                return;
            }

            DBText text = new DBText
            {
                Position = new Point3d(center.X, center.Y, elevation),
                Height = height,
                TextString = content
            };
            text.SetDatabaseDefaults(db);
            text.LayerId = layerId;
            text.HorizontalMode = TextHorizontalMode.TextCenter;
            text.VerticalMode = TextVerticalMode.TextVerticalMid;
            text.AlignmentPoint = new Point3d(center.X, center.Y, elevation);

            space.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
            drawResult.StepIds.Add(text.ObjectId);
        }

        private static void AddPolyline(
            Database db,
            Transaction tr,
            BlockTableRecord space,
            List<FoilPoint2d> points,
            ObjectId layerId,
            double elevation,
            bool closed,
            FoilDrawResult drawResult)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            Polyline pl = new Polyline(points.Count);
            pl.SetDatabaseDefaults(db);
            for (int i = 0; i < points.Count; i++)
            {
                pl.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0.0, 0.0, 0.0);
            }

            pl.Closed = closed;
            pl.Elevation = elevation;
            pl.LayerId = layerId;

            space.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            drawResult.StepIds.Add(pl.ObjectId);
        }

        private static void AddText(
            Database db,
            Transaction tr,
            BlockTableRecord space,
            string content,
            FoilPoint2d position,
            ObjectId layerId,
            double height,
            double elevation,
            FoilDrawResult drawResult)
        {
            if (string.IsNullOrEmpty(content) || height <= 0.0)
            {
                return;
            }

            DBText text = new DBText
            {
                Position = new Point3d(position.X, position.Y, elevation),
                Height = height,
                TextString = content
            };
            text.SetDatabaseDefaults(db);
            text.LayerId = layerId;

            space.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
            drawResult.StepIds.Add(text.ObjectId);
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
