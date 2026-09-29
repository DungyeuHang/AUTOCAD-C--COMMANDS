using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;

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

        /// <summary>True khi ket qua duoc ve thang vao ban ve dang mo thay vi ra file moi.</summary>
        public bool DrawnInPlace;

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
            return Write(source, outputPath, request, result, parts, settings, sourceName, null);
        }

        /// <param name="reservedBlockNames">
        /// Ten block KHONG duoc dung (ten da co trong ban ve se nhan ket qua). Chen vao ban ve
        /// co san block trung ten thi AutoCAD giu dinh nghia CU - chi tiet se mang hinh cua lan
        /// ghep truoc.
        /// </param>
        private static NestingDwgWriteResult Write(
            Database source,
            string outputPath,
            NestingRequest request,
            NestingResult result,
            IList<OutputPart> parts,
            GhoPhoiSettings settings,
            string sourceName,
            ICollection<string> reservedBlockNames)
        {
            // Bat bien cuoi cung: ket qua DA BIET la khong qua validator thi khong bao gio duoc
            // ghi, ke ca khi ben goi quen chan. (Validation = null: ket qua dung tay o muc thap,
            // vd. phep thu 8 huong - van cho ghi; duong san xuat luon co Validation.)
            if (result.Validation != null && !result.Validation.IsValid)
            {
                throw new InvalidOperationException("Ket qua KHONG qua validator - khong tao ban ve san xuat.");
            }

            NestingDwgWriteResult write = new NestingDwgWriteResult { Path = outputPath };
            Dictionary<string, OutputPart> byGroup = new Dictionary<string, OutputPart>(StringComparer.Ordinal);
            foreach (OutputPart p in parts) byGroup[p.Group.Id] = p;

            // Moi chi tiet DA XEP phai co hinh nguon de ve. Truoc day placement thieu dinh nghia
            // bi BO QUA IM LANG (continue) - ban ve ra thieu chi tiet trong khi validator bao DAT.
            // Kiem truoc khi tao bat cu thu gi: loi thi khong co file nao.
            foreach (Placement pl in result.Placements)
            {
                OutputPart op;
                if (!byGroup.TryGetValue(pl.PartGroupId, out op) || op.SourceIds.Count == 0)
                {
                    throw new InvalidOperationException("Chi tiet " + pl.PartGroupId + " (" + pl.InstanceId +
                        ") da xep nhung khong co hinh nguon de ve - KHONG tao ban ve thieu chi tiet.");
                }
            }

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
                            Name = UniqueBlockName(bt, p.Group, reservedBlockNames),
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

                        int partNumber = 0;
                        foreach (Placement pl in sheet.Placements)
                        {
                            partNumber++;

                            ObjectId blockId;
                            if (!blockIds.TryGetValue(pl.PartGroupId, out blockId)) continue;

                            Point3d position = new Point3d(corner.X + pl.TranslationXMm, corner.Y + pl.TranslationYMm, 0);
                            AddPlacement(ms, tr, blockId, position, pl, settings.OutputAsBlocks, byGroup[pl.PartGroupId].Group);
                            write.BlockReferenceCount++;

                            // Ma P + STT chi de doi chieu khi ra xuong; chi tiet nao DA CO chu
                            // cat cua chinh nguoi dung thi khong ve them - ve them se thanh hai
                            // dong chu chong len nhau tren cung mot chi tiet.
                            if (settings.LabelParts && !HasOwnText(byGroup[pl.PartGroupId]))
                            {
                                OutputPart op = byGroup[pl.PartGroupId];
                                AddPartLabel(ms, tr, labelLayer, op.Group, pl, corner, partNumber, settings.LabelPartNameAndOrder);
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
                            TagPart(tr, br, op.Group, kv.Value);
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

                // ---- 5. khong giu block: explode HET (ca block long trong chi tiet) + purge ----
                if (!settings.OutputAsBlocks)
                {
                    ExplodeAllBlocks(target);
                    PurgeUnusedBlocks(target, null);
                }

                target.SaveAs(outputPath, DwgVersion.Current);
            }

            return write;
        }

        /// <summary>
        /// Ve ket qua ghep vao CHINH ban ve dang mo, goc dat tai <paramref name="at"/>.
        ///
        /// Cach lam: dung lai y nguyen <see cref="Write"/> de dung bo cuc ra mot file tam (toan
        /// bo phep bien hinh da duoc kiem ky o day), doc no vao bo nho roi CHEN vao ban ve hien
        /// tai va PHA KHOI ngay. Khong viet lai phep dung hinh lan thu hai - neu viet lai thi
        /// som muon hai duong se lech nhau.
        ///
        /// Sau khi pha khoi, moi entity nam trong khong gian mo hinh va GIU NGUYEN LAYER cua no
        /// (duong bao o layer duong bao, chu khac o layer khac), nen khi xuat di cat co the
        /// chon dung layer can cat.
        /// </summary>
        public static NestingDwgWriteResult DrawIntoCurrent(
            Database db,
            Point3d at,
            NestingRequest request,
            NestingResult result,
            IList<OutputPart> parts,
            GhoPhoiSettings settings,
            string sourceName)
        {
            string temp = Path.Combine(Path.GetTempPath(),
                "ghophoi_" + Guid.NewGuid().ToString("N") + ".dwg");

            NestingDwgWriteResult write;
            try
            {
                HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (Transaction rt = db.TransactionManager.StartOpenCloseTransaction())
                {
                    BlockTable existing = (BlockTable)rt.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in existing) taken.Add(((BlockTableRecord)rt.GetObject(id, OpenMode.ForRead)).Name);
                }

                write = Write(db, temp, request, result, parts, settings, sourceName, taken);

                using (Database layout = new Database(false, true))
                {
                    layout.ReadDwgFile(temp, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

                    HashSet<ObjectId> blocksBefore = BlockIds(db);
                    ObjectId blockId = db.Insert(UniqueName(db, "GHOPHOI_KETQUA"), layout, false);

                    try
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                            BlockReference br = new BlockReference(at, blockId);
                            ms.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);

                            // Pha khoi ngay: de nguyen block thi may CNC doc ca khoi va cat tat ca
                            // moi thu ben trong, ke ca chu khong dinh cat.
                            br.ExplodeToOwnerSpace();
                            br.Erase();

                            tr.Commit();
                        }
                    }
                    catch
                    {
                        // db.Insert da them dinh nghia block vao ban ve TRUOC transaction: loi o day
                        // thi transaction huy phan chen, con dinh nghia phai tu don de ban ve that su
                        // "giu nguyen" nhu thong bao loi noi.
                        PurgeBlock(db, blockId);
                        throw;
                    }

                    // Dinh nghia block chi la phuong tien de chen - bo di cho ban ve sach. Khong
                    // giu block thi purge ca moi dinh nghia block VUA mang vao ma khong con ai
                    // dung (block cua nguoi dung co san tu truoc thi khong dung vao).
                    PurgeBlock(db, blockId);
                    if (!settings.OutputAsBlocks)
                    {
                        HashSet<ObjectId> added = BlockIds(db);
                        added.ExceptWith(blocksBefore);
                        PurgeUnusedBlocks(db, added);
                    }
                }

                write.Path = string.Empty;
                write.DrawnInPlace = true;
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // temp file only
                }
            }

            return write;
        }

        private static string UniqueName(Database db, string baseName)
        {
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(baseName)) return baseName;

                for (int i = 2; i < 10000; i++)
                {
                    string candidate = baseName + "_" + i.ToString(CultureInfo.InvariantCulture);
                    if (!bt.Has(candidate)) return candidate;
                }
            }

            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static void PurgeBlock(Database db, ObjectId blockId)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = tr.GetObject(blockId, OpenMode.ForWrite) as BlockTableRecord;
                    if (btr != null && btr.GetBlockReferenceIds(true, true).Count == 0) btr.Erase();
                    tr.Commit();
                }
            }
            catch
            {
                // leaving an unused block definition behind is harmless
            }
        }

        private static HashSet<ObjectId> BlockIds(Database db)
        {
            HashSet<ObjectId> ids = new HashSet<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt) ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Explode moi BlockReference trong model space, lap lai cho den khi khong con cai nao
        /// (block long nhau). Chi dung tren ban ve KET QUA tam, khong bao gio tren ban ve goc.
        /// Block khong explode duoc (xref, block cam explode) thi de nguyen.
        /// </summary>
        private static void ExplodeAllBlocks(Database db)
        {
            HashSet<ObjectId> stuck = new HashSet<ObjectId>();
            for (int pass = 0; pass < 32; pass++)
            {
                bool exploded = false;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    List<ObjectId> refs = new List<ObjectId>();
                    foreach (ObjectId id in ms)
                    {
                        if (!stuck.Contains(id) && id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference)))) refs.Add(id);
                    }

                    foreach (ObjectId id in refs)
                    {
                        BlockReference br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                        BlockTableRecord def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        if (def.IsFromExternalReference || def.IsLayout || !def.Explodable)
                        {
                            stuck.Add(id);
                            continue;
                        }

                        try
                        {
                            br.ExplodeToOwnerSpace();
                            br.Erase();
                            exploded = true;
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception)
                        {
                            stuck.Add(id);      // vd. ty le khong deu voi cung tron - de nguyen
                        }
                    }

                    tr.Commit();
                }

                if (!exploded) break;
            }
        }

        /// <summary>
        /// Purge dinh nghia block khong con ai dung, lap lai cho den het (block long nhau).
        /// <paramref name="only"/> = null: moi block trong ban ve; khac null: chi trong tap nay.
        /// </summary>
        private static void PurgeUnusedBlocks(Database db, ICollection<ObjectId> only)
        {
            for (int pass = 0; pass < 32; pass++)
            {
                ObjectIdCollection candidates = new ObjectIdCollection();
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in bt)
                    {
                        if (id.IsErased || (only != null && !only.Contains(id))) continue;
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (btr.IsLayout || btr.IsFromExternalReference || btr.IsDependent) continue;
                        candidates.Add(id);
                    }
                }

                if (candidates.Count == 0) return;
                db.Purge(candidates);
                if (candidates.Count == 0) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in candidates) tr.GetObject(id, OpenMode.ForWrite).Erase();
                    tr.Commit();
                }
            }
        }

        private static void AddPlacement(
            BlockTableRecord ms, Transaction tr, ObjectId blockId, Point3d position, Placement pl, bool asBlock, PartGroup g)
        {
            BlockReference br = new BlockReference(position, blockId)
            {
                Rotation = pl.RotationDeg * Math.PI / 180.0,
                ScaleFactors = new Scale3d(pl.Mirror ? -1.0 : 1.0, 1.0, 1.0)
            };
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            if (asBlock) TagPart(tr, br, g, 1);

            if (!asBlock)
            {
                // Original entities transformed individually (no block in the cut file).
                br.ExplodeToOwnerSpace();
                br.Erase();
            }
        }

        /// <summary>
        /// Gan SL / vat lieu / don / ten len block chi tiet (XData "GHOPHOI_PART"). Chon lai ket
        /// qua (giu block) de GHEP LAI thi GHOPHOI doc lai dung thong tin nay, khong phai quet
        /// lai ban ve goc: chu SL cua nguoi dung khong nam trong block nen khong co cach nao khac.
        /// Xem <see cref="NestingSelectionReader"/> va GhoPhoiPipeline.ApplyPresets.
        /// </summary>
        private static void TagPart(Transaction tr, BlockReference br, PartGroup g, int quantity)
        {
            Database db = br.Database;
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(PartPreset.XDataApp))
            {
                rat.UpgradeOpen();
                RegAppTableRecord app = new RegAppTableRecord { Name = PartPreset.XDataApp };
                rat.Add(app);
                tr.AddNewlyCreatedDBObject(app, true);
            }

            br.XData = PartPreset.ToXData(new PartPreset
            {
                Quantity = Math.Max(1, quantity),
                Material = g.Material ?? string.Empty,
                Order = g.Order ?? string.Empty,
                Name = g.Name ?? string.Empty
            });
        }

        /// <summary>Chi tiet nay da mang san chu khac cua nguoi dung chua.</summary>
        private static bool HasOwnText(OutputPart part)
        {
            if (part == null || part.Group == null) return false;
            RecognizedPart r = part.Group.SourceReference as RecognizedPart;
            return r != null && r.EngravingSources.Count > 0;
        }

        /// <summary>
        /// Ghi ma P + so thu tu vao giua mot chi tiet. So thu tu dem TRONG TUNG TO, vi nguoi
        /// dung dung no de doi chieu tren chinh to dang cam.
        ///
        /// <paramref name="withNameAndOrder"/>: them dong TEN PHOI va dong TEN DON HANG ngay
        /// duoi ma P (khong co don thi bo dong don). Day chi la nhan doi chieu tren layer khong
        /// in - chu cat cua nguoi dung van nam nguyen trong chi tiet, khong bi dung vao.
        /// </summary>
        private static void AddPartLabel(
            BlockTableRecord ms, Transaction tr, ObjectId layer, PartGroup g, Placement pl, Point3d corner, int number,
            bool withNameAndOrder)
        {
            PolyShape world = g.Shape.Polygon.Transform(pl.Orientation, pl.TranslationX, pl.TranslationY);
            double cx = corner.X + NestUnits.ToMm((world.Bounds.MinX + world.Bounds.MaxX) / 2);
            double cy = corner.Y + NestUnits.ToMm((world.Bounds.MinY + world.Bounds.MaxY) / 2);
            double bw = NestUnits.ToMm(world.Bounds.Width), bh = NestUnits.ToMm(world.Bounds.Height);

            List<string> lines = new List<string> { "P" + number.ToString("00", CultureInfo.InvariantCulture) };
            if (withNameAndOrder)
            {
                if (!string.IsNullOrWhiteSpace(g.Name)) lines.Add(g.Name.Trim());
                if (!string.IsNullOrEmpty(g.Order)) lines.Add("Don: " + g.Order);
            }

            double h = Math.Min(bw, bh) / 6.0;
            if (lines.Count > 1)
            {
                // Nhieu dong: chia chieu cao cho so dong, va dong dai nhat phai lot vua chieu ngang.
                int longest = 1;
                foreach (string line in lines) longest = Math.Max(longest, line.Length);
                h = Math.Min(h, bh / (lines.Count * 1.6 + 4.4));
                h = Math.Min(h, bw * 0.9 / (longest * 0.8));
            }

            h = Math.Max(2.0, Math.Min(15.0, h));

            StringBuilder contents = new StringBuilder();
            foreach (string line in lines)
            {
                if (contents.Length > 0) contents.Append("\\P");
                contents.Append(line.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}"));
            }

            MText label = new MText
            {
                Location = new Point3d(cx, cy, 0),
                Attachment = AttachmentPoint.MiddleCenter,
                TextHeight = h,
                Contents = contents.ToString(),
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

        private static string UniqueBlockName(BlockTable bt, PartGroup g, ICollection<string> reserved)
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
            while (bt.Has(name) || (reserved != null && reserved.Contains(name))) name = baseName + "_" + (k++).ToString(CultureInfo.InvariantCulture);
            return name;
        }

        private static string SheetLabel(SheetResult s, NestingResult result)
        {
            int ofMaterial = 0;
            foreach (SheetResult o in result.Sheets)
            {
                if (o.Material == s.Material) ofMaterial++;
            }

            // Nhan don hang la DONG RIENG o tren cung, khong chen vao giua cac so lieu san co:
            // ten don co the dai bao nhieu cung duoc ma khong day cai gi sang mot ben. Ca khoi
            // chu nay duoc dat trong mot MText rong bang dung chieu dai to nen no tu xuong dong.
            string orders = s.OrderLabel(2);
            string head = orders.Length > 0 ? "Don: " + orders + "\n" : string.Empty;

            return head + string.Format(CultureInfo.InvariantCulture,
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
