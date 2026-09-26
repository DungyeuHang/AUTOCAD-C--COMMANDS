using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;
using AUTOCAD_COMMANDS.Nesting.SelfTests;
using static AUTOCAD_COMMANDS.Nesting.SelfTests.NestingTestHarness;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>AutoCAD-layer tests on in-memory databases (never touch the open drawing).</summary>
    internal static class GhoPhoiCadTests
    {
        public static NestingTestReport Run()
        {
            NestingTestReport report = new NestingTestReport();
            NestingTestHarness.Run(report, "B1. LWPolyline bo tron (bulge) + lo tron", B1_RoundedPlateWithHole);
            NestingTestHarness.Run(report, "B2. LINE roi + TEXT SL ngoai + MTEXT vat lieu trong", B2_LinesAndTexts);
            NestingTestHarness.Run(report, "B3. Chi tiet la BLOCK xoay", B3_BlockPart);
            NestingTestHarness.Run(report, "B4. Duong chan tren layer danh dau", B4_MarkingLayer);
            NestingTestHarness.Run(report, "B5. Xuat ban ve moi: hinh goc, transform khop core, ban ve nguon khong doi", B5_WriteDrawing);
            NestingTestHarness.Run(report, "B6. 8 huong (xoay x lat): DWG == core == validator, ARC dung chieu", B6_AllOrientationsTransform);
            NestingTestHarness.Run(report, "B7. Forensic DWG: nguon khong doi, ARC/LINE giu nguyen, SL, vat lieu, khung, nhan, khong rac", B7_OutputForensic);
            NestingTestHarness.Run(report, "B8. File da ton tai -> _2; ghi loi -> exception ro rang, khong file rac", B8_OutputPathAndWriteFailure);
            NestingTestHarness.Run(report, "B9. TEXT can giua chua adjust nam giua 2 chi tiet -> AMBIGUOUS", B9_UnadjustedJustifiedTextAmbiguous);
            NestingTestHarness.Run(report, "B10. Layer khac: TEXT/MTEXT la hinh khac, layer bao van la thong tin", B10_EngravingLayerRouting);
            NestingTestHarness.Run(report, "B11. Chu khac theo DUNG phep bien hinh cua chi tiet o ca 8 huong", B11_EngravingTransform);
            NestingTestHarness.Run(report, "B12. Chi tiet CHUA XEP van mang theo hinh khac", B12_EngravingOnUnplacedPart);
            NestingTestHarness.Run(report, "B13. Ve thang vao ban ve dang mo, da pha khoi, giu layer", B13_DrawIntoCurrentDrawing);
            GhoPhoiOrderCadTests.Run(report);
            NestingTestHarness.Summary(report);
            return report;
        }

        private static GhoPhoiSettings Settings()
        {
            return new GhoPhoiSettings();
        }

        private static ObjectId Append(Database db, Transaction tr, Entity e)
        {
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ObjectId id = ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            return id;
        }

        private static List<ObjectId> ModelSpaceIds(Database db, Transaction tr)
        {
            List<ObjectId> ids = new List<ObjectId>();
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms) ids.Add(id);
            return ids;
        }

        private static Polyline RoundedRect(double x, double y, double w, double h, double r)
        {
            double b = Math.Tan(Math.PI / 8);   // 90 deg CCW corner
            Polyline pl = new Polyline();
            int i = 0;
            pl.AddVertexAt(i++, new Point2d(x + r, y), 0, 0, 0);
            pl.AddVertexAt(i++, new Point2d(x + w - r, y), b, 0, 0);
            pl.AddVertexAt(i++, new Point2d(x + w, y + r), 0, 0, 0);
            pl.AddVertexAt(i++, new Point2d(x + w, y + h - r), b, 0, 0);
            pl.AddVertexAt(i++, new Point2d(x + w - r, y + h), 0, 0, 0);
            pl.AddVertexAt(i++, new Point2d(x + r, y + h), b, 0, 0);
            pl.AddVertexAt(i++, new Point2d(x, y + h - r), 0, 0, 0);
            pl.AddVertexAt(i, new Point2d(x, y + r), b, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private static RecognitionResult ReadAndRecognize(Database db, Transaction tr, GhoPhoiSettings s, out NestReadResult read)
        {
            read = NestingSelectionReader.Read(tr, ModelSpaceIds(db, tr), s);
            RecognitionResult r = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
            foreach (KeyValuePair<int, List<Pt>> m in read.Markings) PartRecognizer.AttachMarking(r.Parts, m.Key, m.Value);
            GhoPhoiPipeline.AttachEngravings(read, r, s);
            return r;
        }

        private static void B1_RoundedPlateWithHole()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Append(db, tr, RoundedRect(0, 0, 200, 100, 20));
                Append(db, tr, new Circle(new Point3d(100, 50, 0), Vector3d.ZAxis, 25));

                NestReadResult read;
                RecognitionResult r = ReadAndRecognize(db, tr, Settings(), out read);
                Equal(1, r.Parts.Count, "one record");
                RecognizedPart p = r.Parts[0];
                True(p.IsNestable, "valid: " + p.NotesText);
                Equal(1, p.Holes.Count, "circle is a hole");

                double exact = 200 * 100 - (4 - Math.PI) * 20 * 20;
                True(p.Outer.Area <= exact + 1e-6 && exact - p.Outer.Area < 200 * 0.05 * 2, "chord approximation inside and within tol");
                Close(200, p.Width, 1e-6, "width");
                tr.Abort();
            }
        }

        private static void B2_LinesAndTexts()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Append(db, tr, new Line(new Point3d(0, 0, 0), new Point3d(300, 0, 0)));
                Append(db, tr, new Line(new Point3d(300, 0, 0), new Point3d(300, 150, 0)));
                Append(db, tr, new Line(new Point3d(300, 150, 0), new Point3d(0, 150, 0)));
                Append(db, tr, new Line(new Point3d(0, 150, 0), new Point3d(0, 0, 0)));
                Append(db, tr, new DBText { Position = new Point3d(100, -30, 0), Height = 10, TextString = "SL: 3" });
                Append(db, tr, new MText { Location = new Point3d(100, 80, 0), TextHeight = 10, Contents = "1,5 mm" });

                NestReadResult read;
                RecognitionResult r = ReadAndRecognize(db, tr, Settings(), out read);
                List<RecognizedPart> parts = r.Parts.FindAll(x => x.IsNestable);
                Equal(1, parts.Count, "one part");
                Equal(3, parts[0].Quantity, "SL from DBText outside");
                Equal("1.5MM", parts[0].Material, "material from MText inside");
                Equal(4, parts[0].GeometrySources.Count, "4 lines");
                tr.Abort();
            }
        }

        private static void B3_BlockPart()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                BlockTableRecord def = new BlockTableRecord { Name = "PART_A" };
                ObjectId defId = bt.Add(def);
                tr.AddNewlyCreatedDBObject(def, true);
                Polyline pl = RoundedRect(0, 0, 120, 60, 5);
                def.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);

                Append(db, tr, new BlockReference(new Point3d(500, 500, 0), defId) { Rotation = Math.PI / 2 });

                NestReadResult read;
                RecognitionResult r = ReadAndRecognize(db, tr, Settings(), out read);
                Equal(1, r.Parts.Count, "one record");
                True(r.Parts[0].IsNestable, "valid");
                Close(60, r.Parts[0].Width, 1e-6, "rotated block width");
                Close(120, r.Parts[0].Height, 1e-6, "rotated block height");
                Equal(1, r.Parts[0].GeometrySources.Count, "the block reference is the single source");
                tr.Abort();
            }
        }

        private static void B4_MarkingLayer()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layer = CadLayerHelper.EnsureLayer(db, tr, "_mss.dut");
                Append(db, tr, RoundedRect(0, 0, 200, 100, 0.0001));
                Append(db, tr, new Line(new Point3d(50, 0, 0), new Point3d(50, 100, 0)) { LayerId = layer });
                Append(db, tr, new Circle(new Point3d(150, 50, 0), Vector3d.ZAxis, 10) { LayerId = layer });

                NestReadResult read;
                RecognitionResult r = ReadAndRecognize(db, tr, Settings(), out read);
                Equal(1, r.Parts.Count, "circle on marking layer is NOT a hole / part");
                Equal(0, r.Parts[0].Holes.Count, "no hole");
                Equal(2, r.Parts[0].MarkingSources.Count, "line + circle carried as marking");
                tr.Abort();
            }
        }

        /// <summary>
        /// Chiral part (LWPOLYLINE with one bulge arc) placed in ALL 8 orientations (4 rotations x
        /// mirror). For every BlockReference in the written DWG:
        ///   - every original vertex, transformed by BlockTransform, is a vertex of the core
        ///     (= validator) polygon for that placement,
        ///   - the midpoint of the ARC segment, transformed, lies on the core outline (within the
        ///     chord tolerance), so the arc bulges the right way after mirroring.
        /// </summary>
        private static void B6_AllOrientationsTransform()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_test_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // 300 x 200 chiral L; the (1300,500)->(1300,560) edge is an arc (bulge 0.5).
                        Polyline pl = new Polyline();
                        double[] xy = { 1000, 500, 1300, 500, 1300, 560, 1100, 560, 1100, 700, 1000, 700 };
                        for (int i = 0; i < xy.Length; i += 2) pl.AddVertexAt(i / 2, new Point2d(xy[i], xy[i + 1]), i == 2 ? 0.5 : 0.0, 0, 0);
                        pl.Closed = true;
                        Append(db, tr, pl);
                        tr.Commit();
                    }

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        rec = ReadAndRecognize(db, tr, Settings(), out read);
                    }

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 3000, 1000) };
                    req.Settings.AllowMirror = true;
                    PartGroup g0 = PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm)[0];
                    PartGroup g = new PartGroup(g0.Id, g0.Shape, 8, g0.Material) { Name = g0.Name, SourceReference = g0.SourceReference };
                    req.Groups.Add(g);
                    True(g.Shape.ToleranceMm > 0, "part with an arc carries a tolerance");

                    NestingResult res = new NestingResult();
                    SheetResult sheet = new SheetResult(0, g.Material, req.DefaultSheet) { NumberInMaterial = 1 };
                    int k = 0;
                    foreach (bool mirror in new[] { false, true })
                    {
                        foreach (double rot in new[] { 0.0, 90.0, 180.0, 270.0 })
                        {
                            // 350 mm pitch, each placement centred in its cell.
                            OrientationTransform o = new OrientationTransform(rot, mirror);
                            LongRect b = g.Shape.Polygon.Transform(o, 0, 0).Bounds;
                            long cx = NestUnits.ToUnits(200 + 350 * k), cy = NestUnits.ToUnits(500);
                            sheet.Placements.Add(new Placement
                            {
                                InstanceId = g.Id + "#" + (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                                PartGroupId = g.Id,
                                SheetIndex = 0,
                                RotationDeg = rot,
                                Mirror = mirror,
                                TranslationX = cx - (b.MinX + b.MaxX) / 2,
                                TranslationY = cy - (b.MinY + b.MaxY) / 2
                            });
                            k++;
                        }
                    }

                    res.Sheets.Add(sheet);
                    res.Validation = new NestingValidator().Validate(req, res);
                    True(res.Validation.IsValid, "8 orientations valid: " + (res.Validation.IsValid ? "" : res.Validation.Issues[0].ToString()));

                    RecognizedPart r = (RecognizedPart)g.SourceReference;
                    Pt o0 = PartRecognizer.LocalOrigin(r);
                    OutputPart op = new OutputPart { Group = g, Origin = new Point3d(o0.X, o0.Y, 0) };
                    foreach (int s in r.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);

                    GhoPhoiSettings settings = Settings();
                    settings.LabelParts = false;
                    NestingDwgWriter.Write(db, path, req, res, new List<OutputPart> { op }, settings, "test");
                    double cornerX = 0, cornerY = -req.DefaultSheet.WidthMm;

                    using (Database check = new Database(false, true))
                    {
                        check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                        using (Transaction tr = check.TransactionManager.StartTransaction())
                        {
                            List<BlockReference> refs = new List<BlockReference>();
                            foreach (ObjectId id in ModelSpaceIds(check, tr))
                            {
                                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (br != null) refs.Add(br);
                            }

                            Equal(8, refs.Count, "8 inserts");
                            BlockTableRecord def = (BlockTableRecord)tr.GetObject(refs[0].BlockTableRecord, OpenMode.ForRead);
                            Polyline src = null;
                            foreach (ObjectId id in def) src = tr.GetObject(id, OpenMode.ForRead) as Polyline ?? src;
                            True(src != null, "original LWPOLYLINE inside block");
                            True(Math.Abs(src.GetBulgeAt(1) - 0.5) < 1e-12, "bulge preserved, not flattened");

                            foreach (Placement pl in sheet.Placements)
                            {
                                PolyShape world = g.Shape.Polygon.Transform(pl.Orientation, pl.TranslationX, pl.TranslationY);
                                Point3d expectedPos = new Point3d(cornerX + pl.TranslationXMm, cornerY + pl.TranslationYMm, 0);
                                BlockReference br = refs.Find(x => x.Position.DistanceTo(expectedPos) < 1e-6);
                                True(br != null, pl.InstanceId + ": insert at the core translation");
                                string tag = pl.InstanceId + " (" + pl.Orientation + ")";

                                for (int i = 0; i < src.NumberOfVertices; i++)
                                {
                                    Point3d p = src.GetPoint3dAt(i).TransformBy(br.BlockTransform);
                                    bool hit = false;
                                    foreach (IntPoint q in world.Outer)
                                    {
                                        if (Math.Abs(p.X - (cornerX + NestUnits.ToMm(q.X))) < 1e-3 &&
                                            Math.Abs(p.Y - (cornerY + NestUnits.ToMm(q.Y))) < 1e-3)
                                        {
                                            hit = true;
                                        }
                                    }

                                    True(hit, tag + ": output vertex " + i + " matches a core vertex");
                                }

                                Point3d arcMid = src.GetPointAtParameter(1.5).TransformBy(br.BlockTransform);
                                double best = double.MaxValue;
                                IntPoint[] ring = world.Outer;
                                for (int i = 0; i < ring.Length; i++)
                                {
                                    IntPoint a = ring[i], c = ring[(i + 1) % ring.Length];
                                    double d = Math.Sqrt(GeometryMath.PointSegmentDistanceSquared(
                                        (arcMid.X - cornerX) * NestUnits.PerMm, (arcMid.Y - cornerY) * NestUnits.PerMm, a.X, a.Y, c.X, c.Y)) / NestUnits.PerMm;
                                    best = Math.Min(best, d);
                                }

                                True(best <= g.Shape.ToleranceMm + 1e-3, tag + ": arc midpoint on the core outline (" + best + " mm)");
                            }

                            tr.Commit();
                        }
                    }
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// Phan loai chu theo LAYER, khong doan theo noi dung:
        ///   _mss.khac  -> hinh khac len chi tiet, di theo chi tiet khi xuat
        ///   _mss.bao   -> thong tin (SL / vat lieu), KHONG bao gio bi khac
        /// Chu khac nam ngoai moi chi tiet thi khong bi nuot im lang ma duoc bao ra.
        /// </summary>
        private static void B10_EngravingLayerRouting()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId engraveLayer = CadLayerHelper.EnsureLayer(db, tr, "_mss.khac");
                ObjectId infoLayer = CadLayerHelper.EnsureLayer(db, tr, "_mss.bao");

                Append(db, tr, RoundedRect(0, 0, 200, 100, 0.0001));        // chi tiet A
                Append(db, tr, RoundedRect(400, 0, 200, 100, 0.0001));      // chi tiet B

                // hinh khac
                Append(db, tr, new DBText
                {
                    TextString = "H",
                    Position = new Point3d(60, 40, 0),
                    Height = 10,
                    LayerId = engraveLayer
                });
                Append(db, tr, new MText
                {
                    Contents = "ABC-123",
                    Location = new Point3d(500, 50, 0),
                    TextHeight = 8,
                    LayerId = engraveLayer
                });

                // thong tin - phai duoc doc chu KHONG duoc khac
                Append(db, tr, new DBText
                {
                    TextString = "SL: 3",
                    Position = new Point3d(20, 20, 0),
                    Height = 10,
                    LayerId = infoLayer
                });
                Append(db, tr, new MText
                {
                    Contents = "1.5MM",
                    Location = new Point3d(430, 20, 0),
                    TextHeight = 8,
                    LayerId = infoLayer
                });

                // chu khac lac ngoai moi chi tiet
                Append(db, tr, new DBText
                {
                    TextString = "LAC",
                    Position = new Point3d(1500, 1500, 0),
                    Height = 10,
                    LayerId = engraveLayer
                });

                NestReadResult read;
                RecognitionResult r = ReadAndRecognize(db, tr, Settings(), out read);

                Equal(3, read.Engravings.Count, "3 chu tren layer khac");
                Equal(2, read.Texts.Count, "2 chu thong tin - chu khac KHONG lot vao day");

                RecognizedPart a = null, b = null;
                foreach (RecognizedPart p in r.Parts)
                {
                    if (p.Outer == null) continue;
                    if (p.MinX < 1) a = p;
                    else b = p;
                }

                True(a != null && b != null, "nhan dang duoc 2 chi tiet");

                Equal(1, a.EngravingSources.Count, "A co 1 hinh khac");
                Equal(1, b.EngravingSources.Count, "B co 1 hinh khac");
                Equal(0, a.MarkingSources.Count, "hinh khac KHONG duoc dem la duong ho");
                Equal(0, b.MarkingSources.Count, "hinh khac KHONG duoc dem la duong ho");

                Equal(3, a.Quantity, "SL van doc duoc tu layer bao");
                Equal("1.5MM", b.Material, "vat lieu van doc duoc tu layer bao");

                bool warned = false;
                foreach (string w in r.GlobalWarnings)
                {
                    if (w.IndexOf("_mss.khac", StringComparison.OrdinalIgnoreCase) >= 0) warned = true;
                }

                True(warned, "chu khac lac ngoai chi tiet phai duoc bao, khong duoc nuot im lang");
                tr.Abort();
            }
        }

        /// <summary>
        /// BAT BIEN CHINH cua tinh nang nay: hinh khac phai nhan DUNG phep bien hinh ma chi
        /// tiet nhan - khong phai "cong them do doi cho".
        ///
        /// Cach kiem: voi ca 8 huong (4 goc xoay x lat guong), lay diem neo cua chu trong
        /// block roi bien hinh bang BlockTransform cua AutoCAD, va so voi diem tinh DOC LAP
        /// bang chinh phep bien hinh cua LOI (OrientationTransform.Apply). Hai con duong
        /// hoan toan khac nhau phai ra cung mot diem.
        ///
        /// Dong thoi kiem noi dung block: dung 1 duong bao + 1 TEXT + 1 MTEXT khac, va
        /// TUYET DOI khong co chu thong tin cua layer bao.
        /// </summary>
        private static void B11_EngravingTransform()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_engrave_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;
                    Point3d textPos = new Point3d(1060, 540, 0);
                    Point3d mtextPos = new Point3d(1180, 520, 0);
                    int sourceCountBefore;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        ObjectId engraveLayer = CadLayerHelper.EnsureLayer(db, tr, "_mss.khac");
                        ObjectId infoLayer = CadLayerHelper.EnsureLayer(db, tr, "_mss.bao");

                        // Chi tiet KHONG doi xung (co mot canh cung) de lat guong nhin ra ngay.
                        Polyline pl = new Polyline();
                        double[] xy = { 1000, 500, 1300, 500, 1300, 560, 1100, 560, 1100, 700, 1000, 700 };
                        for (int i = 0; i < xy.Length; i += 2)
                        {
                            pl.AddVertexAt(i / 2, new Point2d(xy[i], xy[i + 1]), i == 2 ? 0.5 : 0.0, 0, 0);
                        }

                        pl.Closed = true;
                        Append(db, tr, pl);

                        // Chu khac: co goc xoay va can giua - hai thu de sai nhat.
                        DBText t = new DBText
                        {
                            TextString = "H7",
                            Height = 12,
                            Rotation = 30.0 * Math.PI / 180.0,
                            LayerId = engraveLayer
                        };
                        t.Position = textPos;
                        t.HorizontalMode = TextHorizontalMode.TextCenter;
                        t.VerticalMode = TextVerticalMode.TextVerticalMid;
                        t.AlignmentPoint = textPos;
                        Append(db, tr, t);

                        Append(db, tr, new MText
                        {
                            Contents = "MA-99",
                            Location = mtextPos,
                            TextHeight = 9,
                            LayerId = engraveLayer
                        });

                        // Thong tin - phai KHONG bao gio xuat hien trong block.
                        Append(db, tr, new DBText
                        {
                            TextString = "SL: 8",
                            Position = new Point3d(1020, 680, 0),
                            Height = 10,
                            LayerId = infoLayer
                        });

                        tr.Commit();
                    }

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        rec = ReadAndRecognize(db, tr, Settings(), out read);
                        sourceCountBefore = ModelSpaceIds(db, tr).Count;
                    }

                    Equal(1, rec.Parts.Count, "mot chi tiet");
                    Equal(2, rec.Parts[0].EngravingSources.Count, "TEXT + MTEXT deu la hinh khac");
                    Equal(8, rec.Parts[0].Quantity, "SL van doc duoc tu layer bao");

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 3000, 1000) };
                    req.Settings.AllowMirror = true;
                    PartGroup g0 = PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm)[0];
                    PartGroup g = new PartGroup(g0.Id, g0.Shape, 8, g0.Material)
                    {
                        Name = g0.Name,
                        SourceReference = g0.SourceReference
                    };

                    req.Groups.Add(g);

                    NestingResult res = new NestingResult();
                    SheetResult sheet = new SheetResult(0, g.Material, req.DefaultSheet) { NumberInMaterial = 1 };
                    int k = 0;
                    foreach (bool mirror in new[] { false, true })
                    {
                        foreach (double rot in new[] { 0.0, 90.0, 180.0, 270.0 })
                        {
                            OrientationTransform o = new OrientationTransform(rot, mirror);
                            LongRect bb = g.Shape.Polygon.Transform(o, 0, 0).Bounds;
                            long cx = NestUnits.ToUnits(200 + 350 * k), cy = NestUnits.ToUnits(500);
                            sheet.Placements.Add(new Placement
                            {
                                InstanceId = g.Id + "#" + (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                                PartGroupId = g.Id,
                                SheetIndex = 0,
                                RotationDeg = rot,
                                Mirror = mirror,
                                TranslationX = cx - (bb.MinX + bb.MaxX) / 2,
                                TranslationY = cy - (bb.MinY + bb.MaxY) / 2
                            });

                            k++;
                        }
                    }

                    res.Sheets.Add(sheet);
                    res.Validation = new NestingValidator().Validate(req, res);
                    True(res.Validation.IsValid, "8 huong hop le");

                    RecognizedPart rp = (RecognizedPart)g.SourceReference;
                    Pt origin = PartRecognizer.LocalOrigin(rp);
                    OutputPart op = new OutputPart { Group = g, Origin = new Point3d(origin.X, origin.Y, 0) };
                    foreach (int s in rp.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);

                    GhoPhoiSettings settings = Settings();

                    // BAT nhan ten chi tiet len: chi tiet da mang chu khac cua nguoi dung thi
                    // KHONG duoc ve them nhan tu sinh - neu khong se thanh hai chu khac nhau,
                    // va cai khong phai cua ho lai nam chinh giua chi tiet.
                    settings.LabelParts = true;
                    NestingDwgWriter.Write(db, path, req, res, new List<OutputPart> { op }, settings, "test");

                    double cornerX = 0, cornerY = -req.DefaultSheet.WidthMm;

                    using (Database check = new Database(false, true))
                    {
                        check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                        using (Transaction tr = check.TransactionManager.StartTransaction())
                        {
                            List<BlockReference> refs = new List<BlockReference>();
                            foreach (ObjectId id in ModelSpaceIds(check, tr))
                            {
                                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (br != null) refs.Add(br);
                            }

                            Equal(8, refs.Count, "8 insert");

                            int generatedLabels = 0;
                            foreach (ObjectId id in ModelSpaceIds(check, tr))
                            {
                                Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                                if (e is MText && string.Equals(e.Layer, NestingDwgWriter.LabelLayer, StringComparison.Ordinal))
                                {
                                    generatedLabels++;
                                }
                            }

                            Equal(0, generatedLabels,
                                "chi tiet da co chu khac thi khong duoc ve them nhan ten tu sinh");

                            // ---- noi dung block: dung nhung gi can co ----
                            BlockTableRecord def = (BlockTableRecord)tr.GetObject(refs[0].BlockTableRecord, OpenMode.ForRead);
                            int polylines = 0, texts = 0, mtexts = 0;
                            DBText engravedText = null;
                            MText engravedMText = null;

                            foreach (ObjectId id in def)
                            {
                                Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                                if (e is Polyline) polylines++;
                                else if (e is MText)
                                {
                                    mtexts++;
                                    engravedMText = (MText)e;
                                }
                                else if (e is DBText)
                                {
                                    texts++;
                                    engravedText = (DBText)e;
                                }
                            }

                            Equal(1, polylines, "1 duong bao trong block");
                            Equal(1, texts, "dung 1 TEXT khac trong block");
                            Equal(1, mtexts, "dung 1 MTEXT khac trong block");

                            True(engravedText.TextString == "H7", "noi dung TEXT giu nguyen");
                            True(engravedMText.Contents.IndexOf("MA-99", StringComparison.Ordinal) >= 0,
                                "noi dung MTEXT giu nguyen");
                            True(Math.Abs(engravedText.Height - 12) < 1e-9, "chieu cao TEXT giu nguyen");
                            True(Math.Abs(engravedMText.TextHeight - 9) < 1e-9, "chieu cao MTEXT giu nguyen");
                            Equal("_mss.khac", engravedText.Layer, "TEXT giu nguyen layer");
                            Equal("_mss.khac", engravedMText.Layer, "MTEXT giu nguyen layer");
                            True(engravedText.TextString.IndexOf("SL", StringComparison.Ordinal) < 0,
                                "chu thong tin KHONG duoc lot vao block");

                            // ---- bien hinh: hai duong tinh doc lap phai trung nhau ----
                            foreach (Placement pl in sheet.Placements)
                            {
                                Point3d expectedInsert = new Point3d(cornerX + pl.TranslationXMm, cornerY + pl.TranslationYMm, 0);
                                BlockReference br = refs.Find(x => x.Position.DistanceTo(expectedInsert) < 1e-6);
                                True(br != null, pl.InstanceId + ": co insert dung cho");

                                string tag = pl.InstanceId + " (" + pl.Orientation + ")";

                                CheckEngravedPoint(tag + " TEXT", engravedText.Position, textPos,
                                    origin, pl, br, cornerX, cornerY);
                                CheckEngravedPoint(tag + " TEXT-align", engravedText.AlignmentPoint, textPos,
                                    origin, pl, br, cornerX, cornerY);
                                CheckEngravedPoint(tag + " MTEXT", engravedMText.Location, mtextPos,
                                    origin, pl, br, cornerX, cornerY);
                            }

                            tr.Commit();
                        }
                    }

                    // ---- ban ve nguon khong duoc doi ----
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        Equal(sourceCountBefore, ModelSpaceIds(db, tr).Count, "so doi tuong nguon khong doi");

                        int stillThere = 0;
                        foreach (ObjectId id in ModelSpaceIds(db, tr))
                        {
                            DBText t = tr.GetObject(id, OpenMode.ForRead) as DBText;
                            if (t != null && t.TextString == "H7")
                            {
                                stillThere++;
                                True(t.Position.DistanceTo(textPos) < 1e-9, "TEXT nguon khong bi di chuyen");
                                Equal("_mss.khac", t.Layer, "TEXT nguon khong bi doi layer");
                            }
                        }

                        Equal(1, stillThere, "TEXT khac nguon van con nguyen");
                        tr.Commit();
                    }
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// So diem neo cua chu sau khi bien hinh theo HAI duong doc lap:
        ///   (1) AutoCAD: diem trong block x BlockTransform cua BlockReference,
        ///   (2) LOI    : OrientationTransform.Apply tren toa do dia phuong cua chi tiet.
        /// Chung phai ra cung mot cho - do la dinh nghia cua "hinh khac nhan dung phep bien
        /// hinh ma chi tiet nhan".
        /// </summary>
        /// <summary>
        /// Chi tiet khong vua to nao van duoc ve rieng o duoi ban ve. No dung CHUNG dinh nghia
        /// block voi chi tiet da xep, nen hinh khac phai di theo - khong duoc roi mat chi vi
        /// chi tiet chua xep duoc.
        /// </summary>
        private static void B12_EngravingOnUnplacedPart()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_unplaced_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        ObjectId engraveLayer = CadLayerHelper.EnsureLayer(db, tr, "_mss.khac");

                        // 900 x 400: to thu nghiem chi co 500 x 300 nen khong the vua.
                        Append(db, tr, RoundedRect(0, 0, 900, 400, 0.0001));
                        Append(db, tr, new DBText
                        {
                            TextString = "KHAC-U",
                            Position = new Point3d(450, 200, 0),
                            Height = 20,
                            LayerId = engraveLayer
                        });

                        tr.Commit();
                    }

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        rec = ReadAndRecognize(db, tr, Settings(), out read);
                    }

                    Equal(1, rec.Parts[0].EngravingSources.Count, "chi tiet co 1 hinh khac");

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 500, 300) };
                    PartGroup g = PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm)[0];
                    req.Groups.Add(g);

                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    Equal(0, res.Statistics.PlacedQuantity, "khong the xep duoc");
                    Equal(1, res.Statistics.UnplacedQuantity, "dung 1 chi tiet chua xep");

                    RecognizedPart rp = (RecognizedPart)g.SourceReference;
                    Pt origin = PartRecognizer.LocalOrigin(rp);
                    OutputPart op = new OutputPart { Group = g, Origin = new Point3d(origin.X, origin.Y, 0) };
                    foreach (int s in rp.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);

                    GhoPhoiSettings settings = Settings();
                    settings.LabelParts = false;
                    NestingDwgWriter.Write(db, path, req, res, new List<OutputPart> { op }, settings, "test");

                    using (Database check = new Database(false, true))
                    {
                        check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                        using (Transaction tr = check.TransactionManager.StartTransaction())
                        {
                            BlockReference br = null;
                            foreach (ObjectId id in ModelSpaceIds(check, tr))
                            {
                                BlockReference b = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (b != null) br = b;
                            }

                            True(br != null, "chi tiet chua xep van duoc ve ra ban ve");

                            BlockTableRecord def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            int texts = 0;
                            foreach (ObjectId id in def)
                            {
                                DBText t = tr.GetObject(id, OpenMode.ForRead) as DBText;
                                if (t != null && t.TextString == "KHAC-U") texts++;
                            }

                            Equal(1, texts, "hinh khac van nam trong block cua chi tiet chua xep");
                            tr.Commit();
                        }
                    }
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// Ve ket qua THANG vao ban ve dang mo tai diem chon, o che do DA PHA KHOI.
        ///
        /// Ba dieu phai dung:
        ///   * khong con BlockReference nao - de nguyen khoi thi may CNC cat ca chu ben trong,
        ///   * moi doi tuong GIU NGUYEN layer cua no, de xuat di cat con chon duoc layer,
        ///   * hinh goc van nam nguyen cho cu, khong bi don di hay sua.
        /// </summary>
        private static void B13_DrawIntoCurrentDrawing()
        {
            using (Database db = new Database(true, true))
            {
                NestReadResult read;
                RecognitionResult rec;
                Point3d textAt = new Point3d(100, 50, 0);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId engrave = CadLayerHelper.EnsureLayer(db, tr, "_mss.khac");
                    Append(db, tr, RoundedRect(0, 0, 200, 100, 0.0001));
                    Append(db, tr, new DBText
                    {
                        TextString = "KH1",
                        Position = textAt,
                        Height = 10,
                        LayerId = engrave
                    });

                    tr.Commit();
                }

                int sourceCount;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    rec = ReadAndRecognize(db, tr, Settings(), out read);
                    sourceCount = ModelSpaceIds(db, tr).Count;
                }

                Equal(1, rec.Parts[0].EngravingSources.Count, "co hinh khac");

                NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 1000, 600) };
                PartGroup g = PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm)[0];
                req.Groups.Add(g);

                NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                True(res.Statistics.PlacedQuantity == 1, "xep duoc 1 chi tiet");

                RecognizedPart rp = (RecognizedPart)g.SourceReference;
                Pt origin = PartRecognizer.LocalOrigin(rp);
                OutputPart op = new OutputPart { Group = g, Origin = new Point3d(origin.X, origin.Y, 0) };
                foreach (int s in rp.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);

                GhoPhoiSettings settings = Settings();
                settings.LabelParts = false;
                settings.OutputAsBlocks = false;        // pha khoi

                Point3d at = new Point3d(5000, 7000, 0);
                NestingDwgWriter.DrawIntoCurrent(db, at, req, res, new List<OutputPart> { op }, settings, "test");

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    List<ObjectId> ids = ModelSpaceIds(db, tr);
                    True(ids.Count > sourceCount, "ban ve phai co them doi tuong moi");

                    int inserts = 0, engravedText = 0, sourceTextStill = 0;
                    double maxX = double.MinValue;

                    foreach (ObjectId id in ids)
                    {
                        Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (e is BlockReference) inserts++;

                        DBText t = e as DBText;
                        if (t != null && t.TextString == "KH1")
                        {
                            if (t.Position.DistanceTo(textAt) < 1e-9) sourceTextStill++;
                            else
                            {
                                engravedText++;
                                Equal("_mss.khac", t.Layer, "chu khac giu nguyen layer");
                            }
                        }

                        maxX = Math.Max(maxX, e.GeometricExtents.MaxPoint.X);
                    }

                    Equal(0, inserts, "da PHA KHOI: khong duoc con BlockReference nao");
                    Equal(1, engravedText, "chu khac phai duoc ve ra dung 1 lan");
                    Equal(1, sourceTextStill, "chu goc van nam nguyen cho cu");
                    True(maxX > at.X, "ban ghep phai nam quanh diem da chon");

                    tr.Commit();
                }
            }
        }

        private static void CheckEngravedPoint(
            string tag, Point3d inBlock, Point3d original, Pt origin,
            Placement placement, BlockReference reference, double cornerX, double cornerY)
        {
            Point3d actual = inBlock.TransformBy(reference.BlockTransform);

            IntPoint local = IntPoint.FromMm(original.X - origin.X, original.Y - origin.Y);
            IntPoint moved = placement.Orientation.Apply(local, placement.TranslationX, placement.TranslationY);
            double expectedX = cornerX + NestUnits.ToMm(moved.X);
            double expectedY = cornerY + NestUnits.ToMm(moved.Y);

            True(Math.Abs(actual.X - expectedX) < 1e-3 && Math.Abs(actual.Y - expectedY) < 1e-3,
                tag + ": hinh khac phai theo dung phep bien hinh cua chi tiet - mong doi ("
                + expectedX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + expectedY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "), nhan duoc ("
                + actual.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + actual.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // temp file cleanup only
            }
        }

        private static void B5_WriteDrawing()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_test_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    int sourceCount;
                    NestReadResult read;
                    RecognitionResult rec;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // L-shaped part made of LINEs and one ARC corner: output must contain the ARC itself.
                        Append(db, tr, new Line(new Point3d(0, 0, 0), new Point3d(400, 0, 0)));
                        Append(db, tr, new Line(new Point3d(400, 0, 0), new Point3d(400, 100, 0)));
                        Append(db, tr, new Line(new Point3d(400, 100, 0), new Point3d(120, 100, 0)));
                        Append(db, tr, new Arc(new Point3d(120, 120, 0), 20, Math.PI, Math.PI * 1.5));
                        Append(db, tr, new Line(new Point3d(100, 120, 0), new Point3d(100, 300, 0)));
                        Append(db, tr, new Line(new Point3d(100, 300, 0), new Point3d(0, 300, 0)));
                        Append(db, tr, new Line(new Point3d(0, 300, 0), new Point3d(0, 0, 0)));
                        Append(db, tr, new DBText { Position = new Point3d(20, 20, 0), Height = 8, TextString = "SL: 2" });
                        tr.Commit();
                    }

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        sourceCount = ModelSpaceIds(db, tr).Count;
                        rec = ReadAndRecognize(db, tr, Settings(), out read);
                    }

                    List<RecognizedPart> parts = rec.Parts.FindAll(p => p.IsNestable);
                    Equal(1, parts.Count, "L recognised: " + (rec.Parts.Count > 0 ? rec.Parts[0].NotesText : ""));
                    Equal(2, parts[0].Quantity, "SL 2");

                    // Narrow sheet (Y = 350): the 400 x 300 L must rotate to fit two copies.
                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 800, 450) };
                    req.Settings.TimeBudgetSeconds = 60;
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm));
                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(2, res.Statistics.PlacedQuantity, "placed");

                    List<OutputPart> outputs = new List<OutputPart>();
                    foreach (PartGroup g in req.Groups)
                    {
                        RecognizedPart r = (RecognizedPart)g.SourceReference;
                        Pt o = PartRecognizer.LocalOrigin(r);
                        OutputPart op = new OutputPart { Group = g, Origin = new Point3d(o.X, o.Y, 0) };
                        foreach (int s in r.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);
                        outputs.Add(op);
                    }

                    GhoPhoiSettings settings = Settings();
                    settings.LabelParts = false;
                    NestingDwgWriteResult written = NestingDwgWriter.Write(db, path, req, res, outputs, settings, "test");
                    Equal(2, written.BlockReferenceCount, "2 block references");
                    True(File.Exists(path), "file written");

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        Equal(sourceCount, ModelSpaceIds(db, tr).Count, "source drawing unchanged");
                    }

                    using (Database check = new Database(false, true))
                    {
                        check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                        using (Transaction tr = check.TransactionManager.StartTransaction())
                        {
                            List<BlockReference> refs = new List<BlockReference>();
                            foreach (ObjectId id in ModelSpaceIds(check, tr))
                            {
                                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (br != null) refs.Add(br);
                            }

                            Equal(2, refs.Count, "placements in output");

                            BlockTableRecord def = (BlockTableRecord)tr.GetObject(refs[0].BlockTableRecord, OpenMode.ForRead);
                            int arcs = 0, lines = 0;
                            foreach (ObjectId id in def)
                            {
                                DBObject o = tr.GetObject(id, OpenMode.ForRead);
                                if (o is Arc) arcs++;
                                if (o is Line) lines++;
                                True(!(o is DBText), "metadata text is not copied into the part");
                            }

                            Equal(1, arcs, "original ARC preserved (not a polygon)");
                            Equal(6, lines, "original LINEs preserved");

                            // Transform check: block ref extents == core polygon bounds (+ sheet corner).
                            SheetResult sheet = res.Sheets[0];
                            double cornerX = 0, cornerY = -sheet.Sheet.WidthMm;
                            foreach (Placement pl in sheet.Placements)
                            {
                                PolyShape world = req.Groups[0].Shape.Polygon.Transform(pl.Orientation, pl.TranslationX, pl.TranslationY);
                                bool matched = false;
                                foreach (BlockReference br in refs)
                                {
                                    Extents3d e = br.GeometricExtents;
                                    if (Math.Abs(e.MinPoint.X - (cornerX + NestUnits.ToMm(world.Bounds.MinX))) < 0.1 &&
                                        Math.Abs(e.MinPoint.Y - (cornerY + NestUnits.ToMm(world.Bounds.MinY))) < 0.1 &&
                                        Math.Abs(e.MaxPoint.X - (cornerX + NestUnits.ToMm(world.Bounds.MaxX))) < 0.1 &&
                                        Math.Abs(e.MaxPoint.Y - (cornerY + NestUnits.ToMm(world.Bounds.MaxY))) < 0.1)
                                    {
                                        matched = true;
                                    }
                                }

                                True(matched, "block reference geometry matches core placement " + pl.InstanceId);
                            }

                            tr.Commit();
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // temp file cleanup only
                }
            }
        }

        private static Polyline RoundedPlate(double x, double y, double w, double h, double r)
        {
            return RoundedRect(x, y, w, h, r);
        }

        private static void Txt(Database db, Transaction tr, string s, double x, double y)
        {
            Append(db, tr, new DBText { Position = new Point3d(x, y, 0), Height = 12, TextString = s });
        }

        /// <summary>
        /// Realistic drawing through the whole non-UI pipeline, then a forensic audit of the DWG:
        /// source untouched, LINE/ARC/LWPOLYLINE/CIRCLE preserved per block, INSERT count and
        /// per-part quantity, every INSERT's geometry == core/validator polygon, inside its own
        /// sheet frame with EdgeMargin, material of the sheet, sheet frame sizes, labels
        /// (material / utilization / remnant), remnant line position, unplaced parts shown
        /// separately, and no stray entities.
        /// </summary>
        private static void B7_OutputForensic()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_test_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // A: rounded plate, 2 round holes, bend line on _mss.dut; SL outside below.
                        Append(db, tr, RoundedPlate(0, 0, 400, 250, 25));
                        Append(db, tr, new Circle(new Point3d(100, 125, 0), Vector3d.ZAxis, 30));
                        Append(db, tr, new Circle(new Point3d(300, 125, 0), Vector3d.ZAxis, 30));
                        ObjectId dut = CadLayerHelper.EnsureLayer(db, tr, "_mss.dut");
                        Append(db, tr, new Line(new Point3d(200, 0, 0), new Point3d(200, 250, 0)) { LayerId = dut });
                        Txt(db, tr, "SL: 3", 150, -40);
                        Txt(db, tr, "1.2MM", 150, -60);
                        // B: L bracket of LINEs + concave ARC fillet; MTEXT inside with SL + 1.5MM.
                        double bx = 700;
                        Append(db, tr, new Line(new Point3d(bx, 0, 0), new Point3d(bx + 500, 0, 0)));
                        Append(db, tr, new Line(new Point3d(bx + 500, 0, 0), new Point3d(bx + 500, 150, 0)));
                        Append(db, tr, new Line(new Point3d(bx + 500, 150, 0), new Point3d(bx + 180, 150, 0)));
                        Append(db, tr, new Arc(new Point3d(bx + 180, 180, 0), 30, Math.PI, Math.PI * 1.5));
                        Append(db, tr, new Line(new Point3d(bx + 150, 180, 0), new Point3d(bx + 150, 450, 0)));
                        Append(db, tr, new Line(new Point3d(bx + 150, 450, 0), new Point3d(bx, 450, 0)));
                        Append(db, tr, new Line(new Point3d(bx, 450, 0), new Point3d(bx, 0, 0)));
                        Append(db, tr, new MText { Location = new Point3d(bx + 250, 100, 0), TextHeight = 12, Contents = "SL: 4\\P1.5 MM" });
                        // C: small tab, SL 6, default material.
                        Append(db, tr, RoundedRect(1400, 0, 90, 60, 8));
                        Txt(db, tr, "SL:6", 1400, 80);
                        // D: too long for any sheet -> unplaced, 1.5MM.
                        Append(db, tr, RoundedRect(0, 1000, 3200, 100, 0.001));
                        Txt(db, tr, "SL 2", 100, 1150);
                        Txt(db, tr, "1,5mm", 100, 1170);
                        tr.Commit();
                    }

                    List<string> before = Fingerprint(db);

                    GhoPhoiSettings settings = Settings();

                    // Phep thu nay dem CA nhan ten chi tiet, nen phai tu BAT len chu khong dua
                    // vao gia tri mac dinh - mac dinh gio la TAT (nhan tu sinh lam nguoi dung
                    // tuong chuong trinh sua chu cua ho).
                    settings.LabelParts = true;

                    NestReadResult read;
                    RecognitionResult rec;
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        rec = ReadAndRecognize(db, tr, settings, out read);
                    }

                    List<RecognizedPart> nestable = rec.Parts.FindAll(p => p.IsNestable);
                    Equal(4, nestable.Count, "4 parts recognised: " + string.Join(" | ", rec.Parts.ConvertAll(p => p.Name + " " + p.Status + " " + p.NotesText).ToArray()));

                    NestingRequest req = new NestingRequest { Settings = settings.ToNestingSettings() };
                    req.Settings.TimeBudgetSeconds = 120;
                    req.SheetByMaterial["1.2MM"] = new SheetSpec("1250x2500", 2500, 1250);
                    req.SheetByMaterial["1.5MM"] = new SheetSpec("1500x3000", 3000, 1500);
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, settings.ArcToleranceMm));
                    NestingResult res = new SimpleNestingEngine().Nest(req, System.Threading.CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(3 + 4 + 6, res.Statistics.PlacedQuantity, "placed A3 + B4 + C6");
                    Equal(2, res.Statistics.UnplacedQuantity, "D x2 unplaced");

                    List<OutputPart> outputs = new List<OutputPart>();
                    Dictionary<string, Dictionary<string, int>> sourceKinds = new Dictionary<string, Dictionary<string, int>>();
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (PartGroup g in req.Groups)
                        {
                            RecognizedPart r = (RecognizedPart)g.SourceReference;
                            Pt o = PartRecognizer.LocalOrigin(r);
                            OutputPart op = new OutputPart { Group = g, Origin = new Point3d(o.X, o.Y, 0) };
                            Dictionary<string, int> kinds = new Dictionary<string, int>();
                            foreach (int s in r.GeometrySources)
                            {
                                ObjectId id = read.Sources[s].Id;
                                if (op.SourceIds.Contains(id)) continue;
                                op.SourceIds.Add(id);
                                string dxf = id.ObjectClass.DxfName;
                                int n;
                                kinds.TryGetValue(dxf, out n);
                                kinds[dxf] = n + 1;
                            }

                            sourceKinds[g.Id] = kinds;
                            outputs.Add(op);
                        }
                    }

                    NestingDwgWriter.Write(db, path, req, res, outputs, settings, "forensic");
                    List<string> after = Fingerprint(db);
                    Equal(string.Join("\n", before.ToArray()), string.Join("\n", after.ToArray()), "source drawing byte-for-byte same entities");

                    using (Database check = new Database(false, true))
                    {
                        check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                        using (Transaction tr = check.TransactionManager.StartTransaction())
                        {
                            List<Polyline> frames = new List<Polyline>();
                            List<Line> remnants = new List<Line>();
                            List<BlockReference> inserts = new List<BlockReference>();
                            List<MText> texts = new List<MText>();
                            int other = 0;
                            foreach (ObjectId id in ModelSpaceIds(check, tr))
                            {
                                Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                                if (e is BlockReference) inserts.Add((BlockReference)e);
                                else if (e is Polyline && e.Layer == NestingDwgWriter.SheetLayer) frames.Add((Polyline)e);
                                else if (e is Line && e.Layer == NestingDwgWriter.RemnantLayer) remnants.Add((Line)e);
                                else if (e is MText) texts.Add((MText)e);
                                else other++;
                            }

                            Equal(0, other, "no stray entities");
                            Equal(res.Sheets.Count, frames.Count, "one frame per sheet");
                            int remnantSheets = res.Sheets.FindAll(s => s.RemnantLengthMm > 1.0).Count;
                            Equal(remnantSheets, remnants.Count, "one remnant line per sheet with remnant");
                            int unplacedGroups = 1;
                            Equal(res.Statistics.PlacedQuantity + unplacedGroups, inserts.Count, "INSERT = placed + 1 per unplaced group");
                            Equal(res.Statistics.PlacedQuantity + res.Sheets.Count + 2 + 1, texts.Count, "MTEXT = labels + sheet labels + unplaced title/label + summary");

                            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>();
                            foreach (PartGroup g in req.Groups) groups[g.Id] = g;
                            HashSet<ObjectId> matched = new HashSet<ObjectId>();

                            for (int si = 0; si < res.Sheets.Count; si++)
                            {
                                SheetResult sheet = res.Sheets[si];
                                Extents3d fe = frames[si].GeometricExtents;
                                Close(sheet.Sheet.LengthMm, fe.MaxPoint.X - fe.MinPoint.X, 1e-6, "frame length sheet " + si);
                                Close(sheet.Sheet.WidthMm, fe.MaxPoint.Y - fe.MinPoint.Y, 1e-6, "frame width sheet " + si);
                                double cx = fe.MinPoint.X, cy = fe.MinPoint.Y;

                                string label = null;
                                foreach (MText t in texts)
                                {
                                    string plain = t.Text;
                                    if (plain.StartsWith("TO " + sheet.NumberInMaterial + "/", StringComparison.Ordinal) &&
                                        plain.Contains(sheet.Material) && Math.Abs(t.Location.X - cx) < 1e-6)
                                    {
                                        label = plain;
                                    }
                                }

                                True(label != null, "sheet label with number + material for sheet " + si);
                                True(label.Contains(sheet.Sheet.Name), "label shows sheet size");
                                True(label.Contains((sheet.Utilization * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%"), "label shows utilization");
                                True(label.Contains("Phan du " + sheet.RemnantLengthMm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " mm"), "label shows remnant");

                                if (sheet.RemnantLengthMm > 1.0)
                                {
                                    Line rl = remnants.Find(l => Math.Abs(l.StartPoint.X - (cx + sheet.UsedLengthMm)) < 1e-6);
                                    True(rl != null, "remnant line at used length for sheet " + si);
                                }

                                foreach (Placement pl in sheet.Placements)
                                {
                                    PartGroup g = groups[pl.PartGroupId];
                                    Equal(SimpleNestingEngine.NormalizeMaterial(g.Material), sheet.Material, "part material == sheet material");
                                    PolyShape world = g.Shape.Polygon.Transform(pl.Orientation, pl.TranslationX, pl.TranslationY);
                                    Point3d pos = new Point3d(cx + pl.TranslationXMm, cy + pl.TranslationYMm, 0);
                                    BlockReference br = inserts.Find(x => x.Position.DistanceTo(pos) < 1e-6 && !matched.Contains(x.ObjectId));
                                    True(br != null, pl.InstanceId + ": INSERT at core position");
                                    matched.Add(br.ObjectId);
                                    Close(pl.RotationDeg * Math.PI / 180.0, br.Rotation, 1e-9, pl.InstanceId + " rotation");
                                    Close(pl.Mirror ? -1 : 1, br.ScaleFactors.X, 1e-12, pl.InstanceId + " mirror");

                                    // Output geometry (real curves) vs core polygon: extents agree within the
                                    // chord tolerance and stay EdgeMargin inside the frame.
                                    Extents3d e = br.GeometricExtents;
                                    double tol = g.Shape.ToleranceMm + 1e-3;
                                    Close(cx + NestUnits.ToMm(world.Bounds.MinX), e.MinPoint.X, tol, pl.InstanceId + " minX");
                                    Close(cy + NestUnits.ToMm(world.Bounds.MinY), e.MinPoint.Y, tol, pl.InstanceId + " minY");
                                    Close(cx + NestUnits.ToMm(world.Bounds.MaxX), e.MaxPoint.X, tol, pl.InstanceId + " maxX");
                                    Close(cy + NestUnits.ToMm(world.Bounds.MaxY), e.MaxPoint.Y, tol, pl.InstanceId + " maxY");
                                    double margin = req.Settings.EdgeMarginMm - 1e-6;
                                    True(e.MinPoint.X - fe.MinPoint.X >= margin && e.MinPoint.Y - fe.MinPoint.Y >= margin &&
                                         fe.MaxPoint.X - e.MaxPoint.X >= margin && fe.MaxPoint.Y - e.MaxPoint.Y >= margin,
                                         pl.InstanceId + ": real geometry keeps EdgeMargin inside its own frame");

                                    // Block content = the original entity kinds of that part.
                                    BlockTableRecord def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                                    Dictionary<string, int> kinds = new Dictionary<string, int>();
                                    foreach (ObjectId id in def)
                                    {
                                        string dxf = id.ObjectClass.DxfName;
                                        int n;
                                        kinds.TryGetValue(dxf, out n);
                                        kinds[dxf] = n + 1;
                                    }

                                    foreach (KeyValuePair<string, int> kv in sourceKinds[g.Id])
                                    {
                                        int n;
                                        kinds.TryGetValue(kv.Key, out n);
                                        Equal(kv.Value, n, g.Name + ": " + kv.Key + " count preserved in block");
                                    }

                                    Equal(sourceKinds[g.Id].Count, kinds.Count, g.Name + ": no extra entity kinds in block");
                                }
                            }

                            // Per-part quantity in the file.
                            foreach (PartGroup g in req.Groups)
                            {
                                int inFrames = 0;
                                foreach (SheetResult s in res.Sheets) inFrames += s.Placements.FindAll(p => p.PartGroupId == g.Id).Count;
                                int unplaced = res.Unplaced.FindAll(u => u.PartGroupId == g.Id).Count;
                                Equal(g.Quantity, inFrames + unplaced, g.Name + ": quantity accounted");
                            }

                            True(texts.Exists(t => t.Text.Contains("CHUA XEP DUOC")), "unplaced section title");
                            True(texts.Exists(t => t.Text.Contains("x2 CHUA XEP")), "unplaced part labelled with its count");
                            Equal(res.Statistics.PlacedQuantity, matched.Count, "every placement matched exactly one INSERT");
                            tr.Commit();
                        }
                    }
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>Handle + type + layer + extents of every model-space entity.</summary>
        private static List<string> Fingerprint(Database db)
        {
            List<string> list = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ModelSpaceIds(db, tr))
                {
                    Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    string ext;
                    try
                    {
                        Extents3d x = e.GeometricExtents;
                        ext = x.MinPoint.ToString() + x.MaxPoint.ToString();
                    }
                    catch
                    {
                        ext = "-";
                    }

                    list.Add(id.Handle + " " + id.ObjectClass.DxfName + " " + e.Layer + " " + ext);
                }
            }

            return list;
        }

        private static void B8_OutputPathAndWriteFailure()
        {
            string stem = Path.Combine(Path.GetTempPath(), "ghophoi_exist_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(stem + ".dwg", "x");
            try
            {
                Equal(stem + "_2.dwg", NestingDwgWriter.UniquePath(stem), "existing output is never overwritten");
            }
            finally
            {
                TryDelete(stem + ".dwg");
            }

            string bad = Path.Combine(Path.GetTempPath(), "ghophoi_missing_" + Guid.NewGuid().ToString("N"), "sub", "out.dwg");
            using (Database db = new Database(true, true))
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Append(db, tr, RoundedRect(0, 0, 100, 50, 0.0001));
                    tr.Commit();
                }

                NestReadResult read;
                RecognitionResult rec;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    rec = ReadAndRecognize(db, tr, Settings(), out read);
                }

                NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 500, 500) };
                req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, 0.05));
                NestingResult res = new SimpleNestingEngine().Nest(req, System.Threading.CancellationToken.None, null);
                RecognizedPart r = (RecognizedPart)req.Groups[0].SourceReference;
                OutputPart op = new OutputPart { Group = req.Groups[0], Origin = new Point3d(r.Outer.MinX, r.Outer.MinY, 0) };
                foreach (int s in r.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);

                bool threw = false;
                try
                {
                    NestingDwgWriter.Write(db, bad, req, res, new List<OutputPart> { op }, Settings(), "t");
                }
                catch (Exception)
                {
                    threw = true;
                }

                True(threw, "writing into a missing folder fails with an exception the command can catch");
                True(!File.Exists(bad), "no partial file");
            }
        }

        /// <summary>
        /// Regression (found in the interactive run): a centre-justified DBText whose alignment was
        /// never adjusted reported extents starting at its alignment point, which moved its centre
        /// onto the neighbouring part and silently assigned SL to it instead of AMBIGUOUS.
        /// </summary>
        private static void B9_UnadjustedJustifiedTextAmbiguous()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Append(db, tr, RoundedRect(0, 600, 300, 200, 0.0001));
                Append(db, tr, RoundedRect(360, 600, 300, 200, 0.0001));
                DBText t = new DBText
                {
                    Height = 15,
                    TextString = "SL: 4",
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = new Point3d(330, 700, 0)
                };
                Append(db, tr, t);
                Point3d before = t.Position;

                NestReadResult read;
                RecognitionResult r = ReadAndRecognize(db, tr, Settings(), out read);
                Equal(1, read.Texts.Count, "one text");
                Close(330, read.Texts[0].Position.X, 0.5, "text centre X from the adjusted clone");
                List<RecognizedPart> parts = r.Parts.FindAll(p => p.IsNestable);
                Equal(2, parts.Count, "two plates");
                foreach (RecognizedPart p in parts)
                {
                    Equal(PartStatus.Ambiguous, p.Status, p.Name + " flagged AMBIGUOUS");
                    Equal(1, p.Quantity, p.Name + " not silently assigned");
                }

                True(t.Position.IsEqualTo(before), "source text not modified by the reader");

                // The formula must agree with AutoCAD's own adjustment for every justification.
                Database previousWorking = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = db;
                try
                {
                    TextHorizontalMode[] hs = { TextHorizontalMode.TextCenter, TextHorizontalMode.TextRight, TextHorizontalMode.TextMid, TextHorizontalMode.TextLeft };
                    TextVerticalMode[] vs = { TextVerticalMode.TextBase, TextVerticalMode.TextBottom, TextVerticalMode.TextVerticalMid, TextVerticalMode.TextTop };
                    foreach (double rot in new[] { 0.0, Math.PI / 2, 0.7 })
                    {
                        foreach (TextHorizontalMode h in hs)
                        {
                            foreach (TextVerticalMode v in vs)
                            {
                                if (h == TextHorizontalMode.TextLeft && v == TextVerticalMode.TextBase) continue;
                                if (h == TextHorizontalMode.TextMid && v != TextVerticalMode.TextBase) continue;
                                DBText j = new DBText
                                {
                                    Height = 10, TextString = "SL: 12", Rotation = rot,
                                    HorizontalMode = h, VerticalMode = v, AlignmentPoint = new Point3d(500, 900, 0)
                                };
                                Append(db, tr, j);
                                Pt unadjusted = NestingSelectionReader.DbTextCenter(j, db);
                                j.AdjustAlignment(db);
                                Extents3d e = j.GeometricExtents;
                                Pt truth = new Pt((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2);
                                Pt adjusted = NestingSelectionReader.DbTextCenter(j, db);
                                string tag = h + "/" + v + " rot " + rot.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                                True(unadjusted.DistanceTo(truth) < 0.05, tag + ": unadjusted centre " + unadjusted + " vs AutoCAD " + truth);
                                True(adjusted.DistanceTo(truth) < 0.05, tag + ": adjusted centre " + adjusted + " vs AutoCAD " + truth);
                            }
                        }
                    }
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = previousWorking;
                }

                tr.Abort();
            }
        }
    }
}
