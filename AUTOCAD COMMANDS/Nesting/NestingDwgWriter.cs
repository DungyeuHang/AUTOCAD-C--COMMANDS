using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>What the writer needs to know about one part group's ORIGINAL geometry.</summary>
    internal sealed class OutputPart
    {
        public PartGroup Group;

        /// <summary>Top-level source entities to clone (contours + markings, no metadata text).</summary>
        public ObjectIdCollection SourceIds = new ObjectIdCollection();

        /// <summary>Local frame origin (bbox lower-left) in source WCS - becomes the block base point.</summary>
        public Point3d Origin;
    }

    internal sealed class NestingDwgWriteResult
    {
        public string Path;
        public int BlockReferenceCount;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Writes the nesting result into a NEW drawing file. The source drawing is only read
    /// (WblockCloneObjects copies the original LINE/ARC/POLYLINE... entities - never polygon
    /// approximations). Each part group becomes a block whose base point is the part's local
    /// origin, each placement a BlockReference with exactly the core transform:
    ///   Position = sheet corner + translation, Rotation = placement rotation,
    ///   ScaleFactors.X = -1 when mirrored (mirror first, then rotate - same as OrientationTransform).
    /// </summary>
    internal static class NestingDwgWriter
    {
        public const string SheetLayer = "GHOPHOI_TO";
        public const string TextLayer = "GHOPHOI_TEXT";
        public const string LabelLayer = "GHOPHOI_NHAN";
        public const string RemnantLayer = "GHOPHOI_PHANDU";

        public static NestingDwgWriteResult Write(
            Database source,
            string outputPath,
            NestingRequest request,
            NestingResult result,
            IList<OutputPart> parts,
            GhoPhoiSettings settings,
            string sourceName)
        {
            NestingDwgWriteResult write = new NestingDwgWriteResult { Path = outputPath };
            Dictionary<string, OutputPart> byGroup = new Dictionary<string, OutputPart>(StringComparer.Ordinal);
            foreach (OutputPart p in parts) byGroup[p.Group.Id] = p;

            using (Database target = new Database(true, true))
            {
                target.Insunits = source.Insunits;
                target.Measurement = source.Measurement;

                // ---- 1. one block definition per part group ----
                Dictionary<string, ObjectId> blockIds = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForWrite);
                    foreach (OutputPart p in parts)
                    {
                        BlockTableRecord btr = new BlockTableRecord
                        {
                            Name = UniqueBlockName(bt, p.Group),
                            Origin = p.Origin
                        };
                        blockIds[p.Group.Id] = bt.Add(btr);
                        tr.AddNewlyCreatedDBObject(btr, true);
                    }

                    tr.Commit();
                }

                foreach (OutputPart p in parts)
                {
                    if (p.SourceIds.Count == 0)
                    {
                        write.Warnings.Add("Chi tiet " + p.Group.Name + " khong co doi tuong nguon de sao chep.");
                        continue;
                    }

                    using (IdMapping map = new IdMapping())
                    {
                        source.WblockCloneObjects(p.SourceIds, blockIds[p.Group.Id], map, DuplicateRecordCloning.Ignore, false);
                    }
                }

                // ---- 2. sheets, placements, labels ----
                using (Transaction tr = target.TransactionManager.StartTransaction())
                {
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        SymbolUtilityServices.GetBlockModelSpaceId(target), OpenMode.ForWrite);

                    ObjectId sheetLayer = EnsureLayer(target, tr, SheetLayer, 4);
                    ObjectId textLayer = EnsureLayer(target, tr, TextLayer, 7);
                    ObjectId labelLayer = EnsureLayer(target, tr, LabelLayer, 2);
                    ObjectId remnantLayer = EnsureLayer(target, tr, RemnantLayer, 3);

                    double spacing = Math.Max(0, settings.SheetSpacingMm);
                    double rowY = 0;
                    double maxRight = 0;
                    string currentMaterial = null;
                    double x = 0, rowHeight = 0;

                    foreach (SheetResult sheet in result.Sheets)
                    {
                        if (!string.Equals(sheet.Material, currentMaterial, StringComparison.Ordinal))
                        {
                            if (currentMaterial != null) rowY -= rowHeight + spacing * 2;
                            currentMaterial = sheet.Material;
                            x = 0;
                            rowHeight = 0;
                        }

                        double L = sheet.Sheet.LengthMm, W = sheet.Sheet.WidthMm;
                        Point3d corner = new Point3d(x, rowY - W, 0);
                        double textH = Math.Max(10.0, Math.Min(60.0, W / 40.0));

                        AddRectangle(ms, tr, sheetLayer, corner, L, W);

                        if (sheet.RemnantLengthMm > 1.0)
                        {
                            Line cut = new Line(
                                new Point3d(corner.X + sheet.UsedLengthMm, corner.Y, 0),
                                new Point3d(corner.X + sheet.UsedLengthMm, corner.Y + W, 0)) { LayerId = remnantLayer };
                            ms.AppendEntity(cut);
                            tr.AddNewlyCreatedDBObject(cut, true);
                        }

                        CadMTextHelper.AddMText(ms, tr, textLayer,
                            new Point3d(corner.X, corner.Y + W + textH * 5.5, 0), L, SheetLabel(sheet, result), textH);

                        foreach (Placement pl in sheet.Placements)
                        {
                            ObjectId blockId;
                            if (!blockIds.TryGetValue(pl.PartGroupId, out blockId)) continue;

                            Point3d position = new Point3d(corner.X + pl.TranslationXMm, corner.Y + pl.TranslationYMm, 0);
                            AddPlacement(ms, tr, blockId, position, pl, settings.OutputAsBlocks);
                            write.BlockReferenceCount++;

                            if (settings.LabelParts)
                            {
                                OutputPart op = byGroup[pl.PartGroupId];
                                AddPartLabel(ms, tr, labelLayer, op.Group, pl, corner);
                            }
                        }

                        x += L + spacing;
                        rowHeight = Math.Max(rowHeight, W + textH * 6);
                        maxRight = Math.Max(maxRight, x);
                    }

                    // ---- 3. unplaced parts shown once per group, clearly labelled ----
                    double bottom = rowY - rowHeight - spacing * 2;
                    if (result.Unplaced.Count > 0)
                    {
                        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (UnplacedPart u in result.Unplaced)
                        {
                            int n;
                            counts.TryGetValue(u.PartGroupId, out n);
                            counts[u.PartGroupId] = n + 1;
                        }

                        CadMTextHelper.AddMText(ms, tr, textLayer, new Point3d(0, bottom, 0), 3000,
                            "CHI TIET CHUA XEP DUOC (KHONG CO TREN TO NAO):", 40);
                        double ux = 0;
                        double uy = bottom - 120;
                        foreach (KeyValuePair<string, int> kv in counts)
                        {
                            OutputPart op;
                            if (!byGroup.TryGetValue(kv.Key, out op)) continue;
                            Point3d pos = new Point3d(ux, uy - op.Group.Shape.HeightMm, 0);
                            BlockReference br = new BlockReference(pos, blockIds[kv.Key]);
                            ms.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);
                            CadMTextHelper.AddMText(ms, tr, labelLayer, new Point3d(ux, uy + 50, 0), 1000,
                                string.Format(CultureInfo.InvariantCulture, "{0} x{1} CHUA XEP", op.Group.Name, kv.Value), 25);
                            ux += op.Group.Shape.WidthMm + spacing;
                        }
                    }

                    // ---- 4. overall summary above the first row ----
                    CadMTextHelper.AddMText(ms, tr, textLayer, new Point3d(0, 900, 0), Math.Max(3000, maxRight),
                        Summary(request, result, sourceName), 30);

                    tr.Commit();
                }

                target.SaveAs(outputPath, DwgVersion.Current);
            }

            return write;
        }

        private static void AddPlacement(BlockTableRecord ms, Transaction tr, ObjectId blockId, Point3d position, Placement pl, bool asBlock)
        {
            BlockReference br = new BlockReference(position, blockId)
            {
                Rotation = pl.RotationDeg * Math.PI / 180.0,
                ScaleFactors = new Scale3d(pl.Mirror ? -1.0 : 1.0, 1.0, 1.0)
            };
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            if (!asBlock)
            {
                // Original entities transformed individually (no block in the cut file).
                br.ExplodeToOwnerSpace();
                br.Erase();
            }
        }

        private static void AddPartLabel(BlockTableRecord ms, Transaction tr, ObjectId layer, PartGroup g, Placement pl, Point3d corner)
        {
            PolyShape world = g.Shape.Polygon.Transform(pl.Orientation, pl.TranslationX, pl.TranslationY);
            double cx = corner.X + NestUnits.ToMm((world.Bounds.MinX + world.Bounds.MaxX) / 2);
            double cy = corner.Y + NestUnits.ToMm((world.Bounds.MinY + world.Bounds.MaxY) / 2);
            double size = Math.Min(NestUnits.ToMm(world.Bounds.Width), NestUnits.ToMm(world.Bounds.Height));
            double h = Math.Max(2.0, Math.Min(15.0, size / 6.0));

            MText label = new MText
            {
                Location = new Point3d(cx, cy, 0),
                Attachment = AttachmentPoint.MiddleCenter,
                TextHeight = h,
                Contents = g.Name.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}"),
                LayerId = layer
            };
            ms.AppendEntity(label);
            tr.AddNewlyCreatedDBObject(label, true);
        }

        private static void AddRectangle(BlockTableRecord ms, Transaction tr, ObjectId layer, Point3d corner, double L, double W)
        {
            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(corner.X, corner.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(corner.X + L, corner.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(corner.X + L, corner.Y + W), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(corner.X, corner.Y + W), 0, 0, 0);
            pl.Closed = true;
            pl.LayerId = layer;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short color)
        {
            ObjectId id = CadLayerHelper.EnsureLayer(db, tr, name);
            LayerTableRecord ltr = tr.GetObject(id, OpenMode.ForWrite) as LayerTableRecord;
            if (ltr != null)
            {
                ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, color);
                ltr.IsPlottable = name != LabelLayer && name != RemnantLayer;
            }

            return id;
        }

        private static string UniqueBlockName(BlockTable bt, PartGroup g)
        {
            StringBuilder sb = new StringBuilder("GHOPHOI_");
            foreach (char c in g.Name ?? g.Id)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            }

            string baseName = sb.ToString();
            if (baseName.Length > 200) baseName = baseName.Substring(0, 200);
            string name = baseName;
            int k = 2;
            while (bt.Has(name)) name = baseName + "_" + (k++).ToString(CultureInfo.InvariantCulture);
            return name;
        }

        private static string SheetLabel(SheetResult s, NestingResult result)
        {
            int ofMaterial = 0;
            foreach (SheetResult o in result.Sheets)
            {
                if (o.Material == s.Material) ofMaterial++;
            }

            return string.Format(CultureInfo.InvariantCulture,
                "TO {0}/{1}  |  {2}  |  KHO {3} ({4})\n" +
                "{5} chi tiet  |  Su dung {6:0.0}% (tren phan da dung)  |  {7:0.0}% ca to\n" +
                "Chieu dai da dung {8:0} mm  |  Phan du {9:0} mm ({10:0.###} m2)  |  Phe lieu uoc tinh {11:0.###} m2",
                s.NumberInMaterial, ofMaterial, s.Material, s.Sheet.Name, s.Sheet.SizeText,
                s.Placements.Count, s.Utilization * 100, s.SheetUtilization * 100,
                s.UsedLengthMm, s.RemnantLengthMm, s.RemnantAreaMm2 / 1e6, s.WasteAreaMm2 / 1e6);
        }

        internal static string Summary(NestingRequest request, NestingResult result, string sourceName)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            NestingStatistics st = result.Statistics;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("GHOPHOI - KET QUA GHEP PHOI (V1)");
            sb.AppendLine("Ban ve nguon: " + sourceName + "   |   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci));
            sb.AppendLine(string.Format(ci,
                "Nhom chi tiet: {0}  |  Yeu cau: {1}  |  Da xep: {2}  |  Chua xep: {3}  |  So to: {4}",
                st.PartGroupCount, st.RequestedQuantity, st.PlacedQuantity, st.UnplacedQuantity, st.SheetCount));
            sb.AppendLine(string.Format(ci, "Khe cat {0:0.##} mm  |  Le mep {1:0.##} mm  |  Lat guong: {2}  |  Chi tiet trong lo kin: {3}",
                request.Settings.GapMm, request.Settings.EdgeMarginMm, request.Settings.AllowMirror ? "CO" : "KHONG", request.Settings.AllowPartInsideHole ? "CO" : "KHONG"));
            foreach (MaterialStatistics m in st.Materials)
            {
                sb.AppendLine(string.Format(ci,
                    "{0}: kho {1}, {2} to, xep {3}/{4}, dai da dung {5:0} mm, su dung {6:0.0}%, phan du {7:0.###} m2, phe lieu {8:0.###} m2",
                    m.Material, m.SheetName ?? "-", m.SheetCount, m.Placed, m.Requested, m.UsedLengthMm,
                    m.Utilization * 100, m.RemnantAreaMm2 / 1e6, m.WasteAreaMm2 / 1e6));
            }

            sb.Append("KIEM TRA HINH HOC (validator): ");
            sb.Append(result.Validation != null && result.Validation.IsValid ? "DAT" : "KHONG DAT");
            if (result.Validation != null && !double.IsNaN(result.Validation.MinPartDistanceMm))
            {
                sb.Append(string.Format(ci, "  |  khe nho nhat do duoc {0:0.###} mm", result.Validation.MinPartDistanceMm));
            }

            if (result.Validation != null && !double.IsNaN(result.Validation.MinEdgeDistanceMm))
            {
                sb.Append(string.Format(ci, "  |  cach mep nho nhat {0:0.###} mm", result.Validation.MinEdgeDistanceMm));
            }

            return sb.ToString();
        }

        public static string DefaultOutputPath(Database source)
        {
            string folder = null, name = "Drawing";
            try
            {
                string file = source.Filename;
                if (!string.IsNullOrEmpty(file) && !file.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    folder = System.IO.Path.GetDirectoryName(file);
                    name = System.IO.Path.GetFileNameWithoutExtension(file);
                }
            }
            catch
            {
                // fall through to Documents
            }

            if (string.IsNullOrEmpty(folder) || !IsWritable(folder))
            {
                folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            string stem = System.IO.Path.Combine(folder,
                name + "_GHOPHOI_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            return UniquePath(stem);
        }

        /// <summary>Never overwrite an existing file: stem.dwg, stem_2.dwg, stem_3.dwg...</summary>
        internal static string UniquePath(string stem)
        {
            string path = stem + ".dwg";
            for (int k = 2; File.Exists(path); k++)
            {
                path = stem + "_" + k.ToString(CultureInfo.InvariantCulture) + ".dwg";
            }

            return path;
        }

        private static bool IsWritable(string folder)
        {
            try
            {
                string probe = System.IO.Path.Combine(folder, ".ghophoi_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
