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
            NestingTestHarness.Run(report, "B14. Round-trip DWG that: doc lai hinh -> khe/le/chong/lo/hinh tung chi tiet (+6 dot bien)", B14_RoundTripNewDrawing);
            NestingTestHarness.Run(report, "B15. Round-trip ve thang vao ban ve (mac dinh), goc am le", B15_RoundTripDrawIntoCurrent);
            NestingTestHarness.Run(report, "B16. Chi tiet da xep ma thieu hinh nguon -> tu choi, KHONG co file / khong dung ban ve", B16_WriterRefusesMissingSource);
            NestingTestHarness.Run(report, "B17. File cai dat: NaN / vo cuc / enum la bi bo qua, gia tri dung van doc", B17_SettingsRejectNonsense);
            NestingTestHarness.Run(report, "B18. Metadata A-D qua duong san xuat: SL sai / kich thuoc KHONG vao block cat; chu trong lo", B18_MetadataThroughProductionPath);
            NestingTestHarness.Run(report, "B19. Writer tu chan ket qua KHONG qua validator (ca ve thang)", B19_WriterRefusesInvalidResult);
            GhoPhoiOrderCadTests.Run(report);
            NestingTestHarness.Summary(report);
            return report;
        }

        private static GhoPhoiSettings Settings()
        {
            return new GhoPhoiSettings { DefaultMaterialType = string.Empty, OutputAsBlocks = true };
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

        // ==================================================================================
        // B14/B15: ROUND-TRIP THAT - ghi DWG, doc lai bang CHINH duong nhan dang, do lai hinh hoc
        // ==================================================================================

        /// <summary>
        /// Dung sai lam tron khi doc lai: toa do DWG (double, mm) -> don vi 0.001 mm. Moi phep so
        /// sanh duoi day dung DUNG hai nguon sai so co ly do: dung sai cung cua tung chi tiet
        /// (<see cref="GhoPhoiSettings.ArcToleranceMm"/>, chi cho vong ngoai co cung - dung nhu loi
        /// dung) va sai so lam tron nay. Khong co "slack" nao khac.
        /// </summary>
        private const double RoundTripEpsMm = 0.002;

        /// <summary>Mot chi tiet doc lai tu DWG: da giac trong toa do the gioi (don vi 0.001 mm).</summary>
        private sealed class OutputShape
        {
            public PolyShape Shape;
            public double TolMm;
            public int SheetIndex = -1;
        }

        /// <summary>
        /// Ban ve nguon cho round-trip: tam bo tron co 2 lo tron (cung + lo), chu L bang LINE +
        /// ARC, chu U lom (thang), hinh chiral co canh cung (bat lat guong se lat that), mot
        /// chi tiet vat lieu 1.5MM (to thu hai, hang thu hai).
        /// </summary>
        private static void BuildRoundTripSource(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Append(db, tr, RoundedRect(0, 0, 300, 200, 20));
                Append(db, tr, new Circle(new Point3d(80, 100, 0), Vector3d.ZAxis, 30));
                Append(db, tr, new Circle(new Point3d(200, 100, 0), Vector3d.ZAxis, 20));
                Txt(db, tr, "SL: 2", 120, 150);

                Append(db, tr, new Line(new Point3d(1000, 0, 0), new Point3d(1400, 0, 0)));
                Append(db, tr, new Line(new Point3d(1400, 0, 0), new Point3d(1400, 100, 0)));
                Append(db, tr, new Line(new Point3d(1400, 100, 0), new Point3d(1120, 100, 0)));
                Append(db, tr, new Arc(new Point3d(1120, 120, 0), 20, Math.PI, Math.PI * 1.5));
                Append(db, tr, new Line(new Point3d(1100, 120, 0), new Point3d(1100, 300, 0)));
                Append(db, tr, new Line(new Point3d(1100, 300, 0), new Point3d(1000, 300, 0)));
                Append(db, tr, new Line(new Point3d(1000, 300, 0), new Point3d(1000, 0, 0)));
                Txt(db, tr, "SL: 3", 1020, 20);

                Polyline u = new Polyline();
                double[] uxy = { 2000, 0, 2250, 0, 2250, 140, 2180, 140, 2180, 50, 2070, 50, 2070, 140, 2000, 140 };
                for (int i = 0; i < uxy.Length; i += 2) u.AddVertexAt(i / 2, new Point2d(uxy[i], uxy[i + 1]), 0, 0, 0);
                u.Closed = true;
                Append(db, tr, u);
                Txt(db, tr, "SL: 2", 2010, 10);

                Polyline c = new Polyline();
                double[] cxy = { 3000, 500, 3300, 500, 3300, 560, 3100, 560, 3100, 700, 3000, 700 };
                for (int i = 0; i < cxy.Length; i += 2) c.AddVertexAt(i / 2, new Point2d(cxy[i], cxy[i + 1]), i == 2 ? 0.5 : 0.0, 0, 0);
                c.Closed = true;
                Append(db, tr, c);
                Txt(db, tr, "SL: 3", 3010, 510);

                Append(db, tr, RoundedRect(4000, 0, 260, 180, 15));
                Txt(db, tr, "SL: 2", 4030, 60);
                Append(db, tr, new MText { Location = new Point3d(4030, 150, 0), TextHeight = 10, Contents = "1.5MM" });
                tr.Commit();
            }
        }

        /// <summary>Snapshot cua ban ve nguon: moi entity + hop bao, de chung minh no khong doi.</summary>
        private static string SourceSnapshot(Database db, ICollection<ObjectId> only)
        {
            List<string> rows = new List<string>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ModelSpaceIds(db, tr))
                {
                    if (only != null && !only.Contains(id)) continue;
                    Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    Extents3d x = e.GeometricExtents;
                    rows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2:R},{3:R},{4:R},{5:R}",
                        e.GetType().Name, e.Layer, x.MinPoint.X, x.MinPoint.Y, x.MaxPoint.X, x.MaxPoint.Y));
                }
            }

            rows.Sort(StringComparer.Ordinal);
            return string.Join("\n", rows.ToArray());
        }

        /// <summary>
        /// Doc hinh hoc tu cac entity <paramref name="ids"/> cua ban ve DAU RA bang CHINH duong nhan
        /// dang san xuat (bo qua layer GHOPHOI_*), cung cac khung to tren layer GHOPHOI_TO.
        /// </summary>
        private static void ReadBack(Database db, List<ObjectId> ids, GhoPhoiSettings s, out List<OutputShape> shapes, out List<Extents3d> frames)
        {
            shapes = new List<OutputShape>();
            frames = new List<Extents3d>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                List<ObjectId> geometry = new List<ObjectId>();
                foreach (ObjectId id in ids)
                {
                    Entity e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (string.Equals(e.Layer, NestingDwgWriter.SheetLayer, StringComparison.OrdinalIgnoreCase) && e is Polyline)
                    {
                        frames.Add(e.GeometricExtents);
                        continue;
                    }

                    if (e.Layer.StartsWith("GHOPHOI_", StringComparison.OrdinalIgnoreCase)) continue;
                    geometry.Add(id);
                }

                NestReadResult read = NestingSelectionReader.Read(tr, geometry, s);
                RecognitionResult rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                foreach (RecognizedPart p in rec.Parts)
                {
                    if (p.Outer == null) continue;
                    List<IList<IntPoint>> holes = new List<IList<IntPoint>>();
                    foreach (RecognizedLoop h in p.Holes) holes.Add(ToUnits(h.Points));
                    shapes.Add(new OutputShape
                    {
                        Shape = PolyShape.Create(ToUnits(p.Outer.Points), holes),
                        TolMm = p.Outer.Approximated ? s.ArcToleranceMm : 0.0
                    });
                }
            }

            frames.Sort((a, b) =>
            {
                int c = b.MaxPoint.Y.CompareTo(a.MaxPoint.Y);        // hang tren truoc (vat lieu dau)
                return c != 0 ? c : a.MinPoint.X.CompareTo(b.MinPoint.X);
            });
        }

        private static List<IntPoint> ToUnits(List<Pt> pts)
        {
            List<IntPoint> r = new List<IntPoint>(pts.Count);
            foreach (Pt p in pts) r.Add(IntPoint.FromMm(p.X, p.Y));
            return r;
        }

        private static double MinDistanceMm(PolyShape a, PolyShape b)
        {
            double best = double.MaxValue;
            List<IntPoint[]> ra = new List<IntPoint[]> { a.Outer }, rb = new List<IntPoint[]> { b.Outer };
            ra.AddRange(a.Holes);
            rb.AddRange(b.Holes);
            foreach (IntPoint[] x in ra)
            {
                foreach (IntPoint[] y in rb)
                {
                    for (int i = 0; i < x.Length; i++)
                    {
                        for (int j = 0; j < y.Length; j++)
                        {
                            double d = GeometryMath.SegmentDistanceSquared(x[i], x[(i + 1) % x.Length], y[j], y[(j + 1) % y.Length]);
                            if (d < best) best = d;
                        }
                    }
                }
            }

            return Math.Sqrt(best) / NestUnits.PerMm;
        }

        /// <summary>Khoang cach lon nhat tu moi dinh cua <paramref name="a"/> toi bien cua <paramref name="b"/> (mm).</summary>
        private static double VertexToBoundaryMm(PolyShape a, PolyShape b)
        {
            List<IntPoint[]> ra = new List<IntPoint[]> { a.Outer }, rb = new List<IntPoint[]> { b.Outer };
            ra.AddRange(a.Holes);
            rb.AddRange(b.Holes);
            double worst = 0;
            foreach (IntPoint[] x in ra)
            {
                foreach (IntPoint v in x)
                {
                    double best = double.MaxValue;
                    foreach (IntPoint[] y in rb)
                    {
                        for (int j = 0; j < y.Length; j++)
                        {
                            IntPoint c = y[j], d = y[(j + 1) % y.Length];
                            double t = GeometryMath.PointSegmentDistanceSquared(v.X, v.Y, c.X, c.Y, d.X, d.Y);
                            if (t < best) best = t;
                        }
                    }

                    worst = Math.Max(worst, Math.Sqrt(best) / NestUnits.PerMm);
                }
            }

            return worst;
        }

        /// <summary>
        /// Kiem DOC LAP tren hinh hoc doc tu DWG, so voi ket qua loi <paramref name="expected"/>:
        ///   1. moi placement co DUNG mot chi tiet doc lai trung hinh (moi dinh cua ben nay cach
        ///      bien ben kia &lt;= dung sai cung + eps, hai chieu) - bat ca sai xoay / lat / tinh tien;
        ///   2. cung so lo;
        ///   3. nam trong DUNG khung to cua no, cach mep &gt;= EdgeMargin (+ dung sai cung) - eps;
        ///   4. moi cap tren cung to: khong chong, khong nam trong lo (khi cam), khe &gt;= Gap +
        ///      dung sai hai ben - eps;
        ///   5. khong co chi tiet la trong khung to; so khung = so to.
        /// Tra ve danh sach loi (rong = dat).
        /// </summary>
        private static List<string> CheckRoundTrip(
            NestingRequest req, NestingResult expected, List<OutputShape> shapes, List<Extents3d> frames)
        {
            List<string> issues = new List<string>();
            NestingSettings ns = req.Settings;
            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>(StringComparer.Ordinal);
            foreach (PartGroup g in req.Groups) groups[g.Id] = g;

            if (frames.Count != expected.Sheets.Count) issues.Add("so khung to " + frames.Count + " != so to " + expected.Sheets.Count);
            int n = Math.Min(frames.Count, expected.Sheets.Count);

            HashSet<OutputShape> used = new HashSet<OutputShape>();
            for (int si = 0; si < n; si++)
            {
                SheetResult sheet = expected.Sheets[si];
                Extents3d fe = frames[si];
                if (Math.Abs(fe.MaxPoint.X - fe.MinPoint.X - sheet.Sheet.LengthMm) > RoundTripEpsMm ||
                    Math.Abs(fe.MaxPoint.Y - fe.MinPoint.Y - sheet.Sheet.WidthMm) > RoundTripEpsMm)
                {
                    issues.Add("to " + si + ": khung sai kich thuoc");
                }

                long cx = NestUnits.ToUnits(fe.MinPoint.X), cy = NestUnits.ToUnits(fe.MinPoint.Y);
                foreach (Placement pl in sheet.Placements)
                {
                    PartGroup g = groups[pl.PartGroupId];
                    PolyShape want = g.Shape.Polygon.Transform(pl.Orientation, pl.TranslationX + cx, pl.TranslationY + cy);
                    PolyShape wantOuter = PolyShape.Create(want.Outer, null);
                    double tol = g.Shape.ToleranceMm + RoundTripEpsMm;
                    OutputShape hit = null;
                    foreach (OutputShape o in shapes)
                    {
                        if (used.Contains(o)) continue;
                        if (!o.Shape.Bounds.Overlaps(want.Bounds, NestUnits.ToUnits(tol))) continue;
                        PolyShape outer = PolyShape.Create(o.Shape.Outer, null);
                        if (VertexToBoundaryMm(wantOuter, outer) <= tol && VertexToBoundaryMm(outer, wantOuter) <= tol)
                        {
                            hit = o;
                            break;
                        }
                    }

                    if (hit == null)
                    {
                        issues.Add(pl.InstanceId + ": KHONG co hinh trung khop trong DWG (xoay/lat/tinh tien/hinh sai?)");
                        continue;
                    }

                    used.Add(hit);
                    hit.SheetIndex = si;
                    if (hit.Shape.Holes.Length != g.Shape.Polygon.Holes.Length)
                    {
                        issues.Add(pl.InstanceId + ": so lo " + hit.Shape.Holes.Length + " != " + g.Shape.Polygon.Holes.Length);
                    }
                    else if (VertexToBoundaryMm(want, hit.Shape) > tol || VertexToBoundaryMm(hit.Shape, want) > tol)
                    {
                        issues.Add(pl.InstanceId + ": lo SAI vi tri / hinh dang");
                    }
                }

                // Lề mép: khung la hinh chu nhat loi nen khoang cach bien chi tiet -> mep = min qua cac dinh.
                foreach (OutputShape o in shapes)
                {
                    LongRect b = o.Shape.Bounds;
                    bool inFrame = NestUnits.ToMm(b.MinX) >= fe.MinPoint.X - 1 && NestUnits.ToMm(b.MaxX) <= fe.MaxPoint.X + 1 &&
                                   NestUnits.ToMm(b.MinY) >= fe.MinPoint.Y - 1 && NestUnits.ToMm(b.MaxY) <= fe.MaxPoint.Y + 1;
                    if (!inFrame) continue;
                    if (o.SheetIndex != si) issues.Add("to " + si + ": co chi tiet LA trong khung (khong khop placement nao)");

                    double edge = double.MaxValue;
                    foreach (IntPoint v in o.Shape.Outer)
                    {
                        double x = NestUnits.ToMm(v.X), y = NestUnits.ToMm(v.Y);
                        edge = Math.Min(edge, Math.Min(Math.Min(x - fe.MinPoint.X, fe.MaxPoint.X - x), Math.Min(y - fe.MinPoint.Y, fe.MaxPoint.Y - y)));
                    }

                    if (edge < ns.EdgeMarginMm + o.TolMm - RoundTripEpsMm)
                    {
                        issues.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "to {0}: cach mep {1:0.###} mm < {2:0.###}", si, edge, ns.EdgeMarginMm));
                    }
                }
            }

            List<OutputShape> placed = shapes.FindAll(o => o.SheetIndex >= 0);
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    OutputShape a = placed[i], b = placed[j];
                    if (a.SheetIndex != b.SheetIndex) continue;
                    if (GeometryMath.PointInMaterial(a.Shape.Outer[0], b.Shape) || GeometryMath.PointInMaterial(b.Shape.Outer[0], a.Shape))
                    {
                        issues.Add("to " + a.SheetIndex + ": hai chi tiet CHONG len nhau");
                        continue;
                    }

                    if (!ns.AllowPartInsideHole &&
                        ((b.Shape.Holes.Length > 0 && GeometryMath.PointInRing(a.Shape.Outer[0], b.Shape.Outer) > 0) ||
                         (a.Shape.Holes.Length > 0 && GeometryMath.PointInRing(b.Shape.Outer[0], a.Shape.Outer) > 0)))
                    {
                        issues.Add("to " + a.SheetIndex + ": chi tiet nam trong LO KIN cua chi tiet khac");
                        continue;
                    }

                    double d = MinDistanceMm(a.Shape, b.Shape);
                    double need = ns.GapMm + a.TolMm + b.TolMm - RoundTripEpsMm;
                    if (d < need)
                    {
                        issues.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "to {0}: khe {1:0.###} mm < {2:0.###}", a.SheetIndex, d, ns.GapMm));
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// HOI QUY: placement thieu dinh nghia hinh truoc day bi BO QUA IM LANG -> ban ve ra thieu
        /// chi tiet trong khi validator bao DAT. Gio phai nem loi TRUOC khi tao bat cu thu gi.
        /// </summary>
        private static void B16_WriterRefusesMissingSource()
        {
            using (Database db = new Database(true, true))
            {
                RoundTripCase c = PrepareRoundTrip(db);
                string placedGroup = c.Result.Sheets[0].Placements[0].PartGroupId;

                List<List<OutputPart>> broken = new List<List<OutputPart>>
                {
                    c.Outputs.FindAll(o => o.Group.Id != placedGroup),                          // thieu han
                    c.Outputs.ConvertAll(o => o.Group.Id == placedGroup
                        ? new OutputPart { Group = o.Group, Origin = o.Origin }                 // co nhung rong
                        : o)
                };

                foreach (List<OutputPart> outputs in broken)
                {
                    string path = Path.Combine(Path.GetTempPath(), "ghophoi_b16_" + Guid.NewGuid().ToString("N") + ".dwg");
                    bool threw = false;
                    try
                    {
                        NestingDwgWriter.Write(db, path, c.Request, c.Result, outputs, Settings(), "b16");
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                    finally
                    {
                        bool exists = File.Exists(path);
                        TryDelete(path);
                        True(!exists, "khong duoc co file DWG thieu chi tiet");
                    }

                    True(threw, "ghi ban ve thieu hinh nguon phai nem loi");

                    int before;
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction()) before = ModelSpaceIds(db, tr).Count;
                    threw = false;
                    try
                    {
                        NestingDwgWriter.DrawIntoCurrent(db, Point3d.Origin, c.Request, c.Result, outputs, Settings(), "b16");
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }

                    True(threw, "ve thang thieu hinh nguon phai nem loi");
                    Equal(c.SourceBefore, SourceSnapshot(db, null), "ban ve dang mo KHONG doi");
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        Equal(before, ModelSpaceIds(db, tr).Count, "khong them entity nao");
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        True(!bt.Has("GHOPHOI_KETQUA"), "khong de lai dinh nghia block");
                    }
                }
            }
        }

        /// <summary>
        /// HOI QUY: "Infinity" trong file cai dat lot qua kiem "&gt;= 0" va lam bang cai dat nem
        /// loi moi lan mo (roi con bi ghi lai); RotationMode "5" cung vay. Gia tri la phai bi bo
        /// qua (giu mac dinh), gia tri dung van doc duoc.
        /// </summary>
        private static void B17_SettingsRejectNonsense()
        {
            GhoPhoiSettings d = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
            string[] doubles = { "GapMm", "EdgeMarginMm", "TimeBudgetSeconds", "ArcToleranceMm", "JoinToleranceMm", "MaxTextDistanceMm", "SheetSpacingMm" };
            foreach (string bad in new[] { "Infinity", "-Infinity", "NaN", "1e400", "abc", "" })
            {
                GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
                foreach (string key in doubles) GhoPhoiSettingsStore.Apply(s, key, bad);
                Equal(d.GapMm, s.GapMm, "GapMm '" + bad + "'");
                Equal(d.EdgeMarginMm, s.EdgeMarginMm, "EdgeMarginMm '" + bad + "'");
                Equal(d.TimeBudgetSeconds, s.TimeBudgetSeconds, "TimeBudgetSeconds '" + bad + "'");
                Equal(d.ArcToleranceMm, s.ArcToleranceMm, "ArcToleranceMm '" + bad + "'");
                Equal(d.JoinToleranceMm, s.JoinToleranceMm, "JoinToleranceMm '" + bad + "'");
                Equal(d.MaxTextDistanceMm, s.MaxTextDistanceMm, "MaxTextDistanceMm '" + bad + "'");
                Equal(d.SheetSpacingMm, s.SheetSpacingMm, "SheetSpacingMm '" + bad + "'");
            }

            foreach (string bad in new[] { "5", "-1", "3", "Bogus" })
            {
                GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, RotationMode = GhoPhoiRotationMode.HalfTurns };
                GhoPhoiSettingsStore.Apply(s, "RotationMode", bad);
                Equal(GhoPhoiRotationMode.HalfTurns, s.RotationMode, "RotationMode '" + bad + "' phai bi bo qua");
            }

            GhoPhoiSettings good = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
            GhoPhoiSettingsStore.Apply(good, "GapMm", "6.5");
            GhoPhoiSettingsStore.Apply(good, "EdgeMarginMm", "0");
            GhoPhoiSettingsStore.Apply(good, "RotationMode", "None");
            GhoPhoiSettingsStore.Apply(good, "SearchEffort", "Balanced");
            GhoPhoiSettingsStore.Apply(good, "AllowPartInsideHole", "True");
            Equal(6.5, good.GapMm, "gia tri dung van doc");
            Equal(0.0, good.EdgeMarginMm, "le 0 hop le");
            Equal(GhoPhoiRotationMode.None, good.RotationMode, "enum dung van doc");
            Equal(SearchEffort.Balanced, good.SearchEffort, "muc tim kiem");

            GhoPhoiSettings alg = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
            GhoPhoiSettingsStore.Apply(alg, "Algorithm", "Compare");
            Equal(GhoPhoiAlgorithmMode.Compare, alg.Algorithm, "thuat toan: so sanh");
            GhoPhoiSettingsStore.Apply(alg, "Algorithm", "7");
            Equal(GhoPhoiAlgorithmMode.Compare, alg.Algorithm, "thuat toan: so la bi bo qua");
            GhoPhoiSettingsStore.Apply(alg, "Algorithm", "Nfp");
            Equal(NestingAlgorithm.Nfp, alg.ToNestingSettings().Algorithm, "NFP di xuong loi");
            True(good.AllowPartInsideHole, "cho phep lo kin");

            double v;
            True(!GhoPhoiSettingsStore.TryD("Infinity", out v) && !GhoPhoiSettingsStore.TryD("NaN", out v), "TryD tu choi vo cuc / NaN (ca danh muc kho phoi)");
            True(GhoPhoiSettingsStore.TryD(" 1250 ", out v) && v == 1250, "TryD so thuong");
        }

        private static Polyline Rect(double x, double y, double w, double h)
        {
            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x + w, y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x + w, y + h), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x, y + h), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        /// <summary>
        /// A-D tren entity CAD THAT (DBText / MText / Polyline) qua NestingSelectionReader ->
        /// nhan dang -> ghep -> ghi DWG -> doc lai block: chu SL sai va chu kich thuoc KHONG duoc
        /// sao vao block cat; chu cat that van di theo; chu cua chi tiet nho trong lo cua khung
        /// thuoc chi tiet nho.
        /// </summary>
        private static void B18_MetadataThroughProductionPath()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_b18_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Append(db, tr, Rect(0, 0, 200, 100));                                    // P1: SL sai
                        Txt(db, tr, "SL: abc", 20, 60);
                        Append(db, tr, new MText { Location = new Point3d(20, 40, 0), TextHeight = 10, Contents = "1.2MM" });

                        Append(db, tr, Rect(500, 0, 200, 100));                                  // P2: dau phay + kich thuoc + chu cat
                        Txt(db, tr, "SL: 2, 1.5MM", 520, 70);
                        Txt(db, tr, "R12.5MM", 520, 40);
                        Txt(db, tr, "KH-9", 620, 20);

                        Append(db, tr, Rect(1000, 0, 400, 400));                                 // khung F
                        Append(db, tr, Rect(1050, 50, 300, 300));                                // lo cua F
                        Append(db, tr, Rect(1120, 120, 100, 100));                               // S trong lo
                        Txt(db, tr, "SL: 3", 1010, 200);                                         // tren vat lieu F
                        Txt(db, tr, "SL: 4", 1150, 160);                                         // trong S
                        Txt(db, tr, "1.2MM", 1010, 20);
                        Txt(db, tr, "1.2MM", 1150, 140);
                        tr.Commit();
                    }

                    NestReadResult read;
                    RecognitionResult rec;
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        rec = ReadAndRecognize(db, tr, Settings(), out read);
                    }

                    List<RecognizedPart> parts = rec.Parts.FindAll(p => p.IsNestable);
                    Equal(4, parts.Count, "4 chi tiet: " + string.Join(" | ", rec.Parts.ConvertAll(p => p.Name + " " + p.Status + " " + p.NotesText).ToArray()));
                    RecognizedPart p1 = parts.Find(p => Math.Abs(p.MinX) < 1e-6);
                    RecognizedPart p2 = parts.Find(p => Math.Abs(p.MinX - 500) < 1e-6);
                    RecognizedPart frame = parts.Find(p => Math.Abs(p.MinX - 1000) < 1e-6);
                    RecognizedPart small = parts.Find(p => Math.Abs(p.MinX - 1120) < 1e-6);

                    Equal(PartStatus.Ambiguous, p1.Status, "P1: SL sai -> phai xac nhan");
                    True(p1.NotesText.Contains("SL: abc"), "P1: ghi chu chi ra chu sai: " + p1.NotesText);
                    Equal(0, p1.EngravingSources.Count, "P1: 'SL: abc' KHONG la chu cat");

                    Equal(2, p2.Quantity, "P2: 'SL: 2, 1.5MM' -> SL 2");
                    Equal("1.5MM", p2.Material, "P2: vat lieu 1.5MM");
                    Equal(PartStatus.Ok, p2.Status, "P2: ro rang");
                    Equal(1, p2.EngravingSources.Count, "P2: CHI 'KH-9' la chu cat (khong phai R12.5MM, khong phai SL)");

                    Equal(3, frame.Quantity, "F: SL tren vat lieu khung");
                    Equal(4, small.Quantity, "S: SL cua chi tiet nho trong lo");
                    Equal(1, frame.Holes.Count, "F co lo");

                    // Nguoi dung sua P1 trong bang kiem tra roi xac nhan -> di tiep.
                    p1.Quantity = 1;
                    p1.Confirmed = true;
                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                    req.SheetByMaterial["1.5MM"] = new SheetSpec("T15", 1000, 500);
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm));
                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(1 + 2 + 3 + 4, res.Statistics.PlacedQuantity, "xep du");

                    GhoPhoiSettings s = Settings();
                    s.LabelParts = false;
                    NestingDwgWriter.Write(db, path, req, res, GhoPhoiPipeline.BuildOutputParts(req.Groups, read), s, "b18");

                    using (Database check = new Database(false, true))
                    {
                        check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                        using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction())
                        {
                            List<string> texts = new List<string>();
                            BlockTable bt = (BlockTable)tr.GetObject(check.BlockTableId, OpenMode.ForRead);
                            foreach (ObjectId id in bt)
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                                if (!btr.Name.StartsWith("GHOPHOI_", StringComparison.Ordinal)) continue;
                                foreach (ObjectId eid in btr)
                                {
                                    DBObject o = tr.GetObject(eid, OpenMode.ForRead);
                                    if (o is DBText) texts.Add(((DBText)o).TextString);
                                    if (o is MText) texts.Add(((MText)o).Text);
                                }
                            }

                            True(texts.Contains("KH-9"), "chu cat that van nam trong block");
                            foreach (string bad in new[] { "SL: abc", "R12.5MM", "SL: 2, 1.5MM", "SL: 3", "SL: 4", "1.2MM" })
                            {
                                True(!texts.Contains(bad), "'" + bad + "' KHONG duoc nam trong block cat");
                            }
                        }
                    }
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void B19_WriterRefusesInvalidResult()
        {
            using (Database db = new Database(true, true))
            {
                RoundTripCase c = PrepareRoundTrip(db);
                NestingResult invalid = CopyResult(c.Result);
                invalid.Validation = new ValidationResult();
                invalid.Validation.Issues.Add(new ValidationIssue(ValidationIssueKind.Overlap, "gia lap"));

                string path = Path.Combine(Path.GetTempPath(), "ghophoi_b19_" + Guid.NewGuid().ToString("N") + ".dwg");
                bool threw = false;
                try
                {
                    NestingDwgWriter.Write(db, path, c.Request, invalid, c.Outputs, Settings(), "b19");
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                finally
                {
                    bool exists = File.Exists(path);
                    TryDelete(path);
                    True(!exists, "khong duoc co file");
                }

                True(threw, "ghi ket qua KHONG DAT phai bi tu choi");

                threw = false;
                try
                {
                    NestingDwgWriter.DrawIntoCurrent(db, Point3d.Origin, c.Request, invalid, c.Outputs, Settings(), "b19");
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                True(threw, "ve thang ket qua KHONG DAT phai bi tu choi");
                Equal(c.SourceBefore, SourceSnapshot(db, null), "ban ve KHONG doi");

                // Ket qua DAT van ghi binh thuong.
                string ok = Path.Combine(Path.GetTempPath(), "ghophoi_b19ok_" + Guid.NewGuid().ToString("N") + ".dwg");
                try
                {
                    NestingDwgWriter.Write(db, ok, c.Request, c.Result, c.Outputs, Settings(), "b19");
                    True(File.Exists(ok), "ket qua DAT van ghi duoc");
                }
                finally
                {
                    TryDelete(ok);
                }
            }
        }

        private static void ExpectIssue(List<string> issues, string fragment, string what)
        {
            True(issues.Exists(s => s.IndexOf(fragment, StringComparison.Ordinal) >= 0),
                what + " phai bi bat voi loi '" + fragment + "', thuc te: " + (issues.Count == 0 ? "(khong loi nao)" : string.Join(" | ", issues.ToArray())));
        }

        private static NestingResult CopyResult(NestingResult r)
        {
            NestingResult c = new NestingResult();
            foreach (SheetResult s in r.Sheets)
            {
                SheetResult cs = new SheetResult(s.Index, s.Material, s.Sheet)
                {
                    NumberInMaterial = s.NumberInMaterial,
                    UsedLengthMm = s.UsedLengthMm,
                    RemnantLengthMm = s.RemnantLengthMm,
                    PartAreaMm2 = s.PartAreaMm2,
                    Utilization = s.Utilization,
                    SheetUtilization = s.SheetUtilization,
                    WasteAreaMm2 = s.WasteAreaMm2,
                    RemnantAreaMm2 = s.RemnantAreaMm2
                };
                foreach (Placement p in s.Placements)
                {
                    cs.Placements.Add(new Placement
                    {
                        InstanceId = p.InstanceId,
                        PartGroupId = p.PartGroupId,
                        SheetIndex = p.SheetIndex,
                        RotationDeg = p.RotationDeg,
                        Mirror = p.Mirror,
                        OrderName = p.OrderName,
                        TranslationX = p.TranslationX,
                        TranslationY = p.TranslationY
                    });
                }

                c.Sheets.Add(cs);
            }

            c.Unplaced.AddRange(r.Unplaced);
            c.Validation = r.Validation;
            return c;
        }

        private sealed class RoundTripCase
        {
            public Database Db;
            public NestReadResult Read;
            public NestingRequest Request;
            public NestingResult Result;
            public List<OutputPart> Outputs;
            public string SourceBefore;
        }

        private static RoundTripCase PrepareRoundTrip(Database db)
        {
            BuildRoundTripSource(db);
            RoundTripCase c = new RoundTripCase { Db = db };
            RecognitionResult rec;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                rec = ReadAndRecognize(db, tr, Settings(), out c.Read);
            }

            Equal(5, rec.Parts.FindAll(p => p.IsNestable).Count, "5 chi tiet nhan dang");
            c.Request = new NestingRequest { DefaultSheet = new SheetSpec("T", 1500, 600) };
            c.Request.SheetByMaterial["1.5MM"] = new SheetSpec("T15", 900, 500);
            c.Request.Settings.AllowMirror = true;
            c.Request.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, Settings().ArcToleranceMm));
            c.Result = new SimpleNestingEngine().Nest(c.Request, CancellationToken.None, null);
            True(c.Result.Validation.IsValid, "validator loi");
            Equal(12, c.Result.Statistics.PlacedQuantity, "xep du 2 + 3 + 2 + 3 + 2");
            True(c.Result.Sheets.Count >= 2, "hai vat lieu -> it nhat 2 to");
            bool anyRotated = false, anyHoles = false;
            foreach (Placement p in c.Result.Placements) if (p.RotationDeg != 0) anyRotated = true;

            foreach (PartGroup g in c.Request.Groups) if (g.Shape.Polygon.Holes.Length > 0) anyHoles = true;
            True(anyRotated, "phep thu can it nhat mot chi tiet XOAY");
            True(anyHoles, "phep thu can chi tiet co LO");

            c.Outputs = GhoPhoiPipeline.BuildOutputParts(c.Request.Groups, c.Read);
            c.SourceBefore = SourceSnapshot(db, null);
            return c;
        }

        /// <summary>Ghi ra FILE DWG moi (ca dang block va dang pha khoi), doc lai tu file, kiem doc lap.</summary>
        private static List<string> RoundTripNewDrawing(RoundTripCase c, NestingResult toWrite, List<OutputPart> outputs, bool asBlocks)
        {
            return RoundTripNewDrawing(c, toWrite, outputs, asBlocks, c.Request);
        }

        /// <param name="checkAgainst">Yeu cau dung de KIEM (mac dinh = yeu cau da ghep).</param>
        private static List<string> RoundTripNewDrawing(RoundTripCase c, NestingResult toWrite, List<OutputPart> outputs, bool asBlocks, NestingRequest checkAgainst)
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_rt_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                GhoPhoiSettings s = Settings();
                s.LabelParts = false;
                s.OutputAsBlocks = asBlocks;
                NestingDwgWriter.Write(c.Db, path, c.Request, toWrite, outputs, s, "roundtrip");
                Equal(c.SourceBefore, SourceSnapshot(c.Db, null), "ban ve nguon KHONG doi");

                using (Database check = new Database(false, true))
                {
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    List<ObjectId> ids;
                    using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction()) ids = ModelSpaceIds(check, tr);
                    List<OutputShape> shapes;
                    List<Extents3d> frames;
                    ReadBack(check, ids, s, out shapes, out frames);
                    return CheckRoundTrip(checkAgainst, c.Result, shapes, frames);
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// B14. Round-trip THAT tren file DWG: ghi -> doc file -> nhan dang lai -> do lai tu hinh
        /// hoc DWG: hinh tung chi tiet (ca xoay / lat), so lo, lề mép, khe, chong, lo kin.
        /// Ca dang block lan dang pha khoi. Kem 4 dot bien co chu dich (phai bi bat).
        /// </summary>
        private static void B14_RoundTripNewDrawing()
        {
            using (Database db = new Database(true, true))
            {
                RoundTripCase c = PrepareRoundTrip(db);

                foreach (bool asBlocks in new[] { true, false })
                {
                    List<string> issues = RoundTripNewDrawing(c, c.Result, c.Outputs, asBlocks);
                    True(issues.Count == 0, (asBlocks ? "block" : "pha khoi") + ": " + string.Join(" | ", issues.ToArray()));
                }

                // ---- dot bien: bo kiem phai BAT duoc ----
                Placement first = c.Result.Sheets[0].Placements[0];
                Placement second = c.Result.Sheets[0].Placements[1];

                NestingResult overlap = CopyResult(c.Result);
                overlap.Sheets[0].Placements[1].TranslationX = first.TranslationX;
                overlap.Sheets[0].Placements[1].TranslationY = first.TranslationY;
                overlap.Sheets[0].Placements[1].RotationDeg = first.RotationDeg;
                overlap.Sheets[0].Placements[1].Mirror = first.Mirror;
                // Hai duong bao TRUNG KHIT nhau: bo nhan dang khong tach duoc thanh hai chi tiet nen
                // ca hai "bien mat" khoi ket qua doc lai - van la phat hien dung, chi khac cau bao.
                List<string> overlapIssues = RoundTripNewDrawing(c, overlap, c.Outputs, false);
                True(overlapIssues.Exists(s => s.Contains("CHONG") || s.Contains("KHONG co hinh trung khop")),
                    "dot bien CHONG HINH phai bi bat, thuc te: " + string.Join(" | ", overlapIssues.ToArray()));

                // Khe: tren CHINH DWG dung, doi Gap len 6 mm (thuc te chi ~5 mm) -> phep do khe phai bao.
                NestingRequest stricter = new NestingRequest { DefaultSheet = c.Request.DefaultSheet, Settings = c.Request.Settings.Clone() };
                foreach (KeyValuePair<string, SheetSpec> kv in c.Request.SheetByMaterial) stricter.SheetByMaterial[kv.Key] = kv.Value;
                stricter.Groups.AddRange(c.Request.Groups);
                stricter.Settings.GapMm = c.Request.Settings.GapMm + 1.0;
                ExpectIssue(RoundTripNewDrawing(c, c.Result, c.Outputs, false, stricter), "khe", "phep do KHE (Gap + 1 mm)");

                NestingResult rotated = CopyResult(c.Result);
                rotated.Sheets[0].Placements[0].RotationDeg = (first.RotationDeg + 90) % 360;
                ExpectIssue(RoundTripNewDrawing(c, rotated, c.Outputs, true), "KHONG co hinh trung khop", "dot bien SAI GOC XOAY");

                NestingResult edge = CopyResult(c.Result);
                PartGroup g0 = c.Request.Groups.Find(g => g.Id == first.PartGroupId);
                LongRect b0 = g0.Shape.Polygon.Transform(first.Orientation, 0, 0).Bounds;
                edge.Sheets[0].Placements[0].TranslationX = NestUnits.ToUnits(1.0) - b0.MinX;   // cach mep 1 mm < 5 mm
                ExpectIssue(RoundTripNewDrawing(c, edge, c.Outputs, false), "cach mep", "dot bien LE MEP");

                // Bo lo: bo cac CIRCLE khoi nguon sao chep cua tam co lo.
                List<OutputPart> noHole = new List<OutputPart>();
                foreach (OutputPart op in c.Outputs)
                {
                    OutputPart copy = new OutputPart { Group = op.Group, Origin = op.Origin };
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in op.SourceIds)
                        {
                            if (!(tr.GetObject(id, OpenMode.ForRead) is Circle)) copy.SourceIds.Add(id);
                        }
                    }

                    noHole.Add(copy);
                }

                ExpectIssue(RoundTripNewDrawing(c, c.Result, noHole, true), "so lo", "dot bien MAT LO");

                // Lat guong sai tren chi tiet chiral (300 + cung phong ~15 x 200, khong lo): CUNG hop
                // bao, KHAC hinh - bo kiem bang hop bao (B5 / B7) khong bat duoc loi nay.
                PartGroup chiralGroup = c.Request.Groups.Find(g =>
                    g.Shape.Polygon.Holes.Length == 0 && g.Shape.ToleranceMm > 0 &&
                    g.Shape.WidthMm > 300 && g.Shape.WidthMm < 330 && Math.Abs(g.Shape.HeightMm - 200) < 1);
                True(chiralGroup != null, "phai tim thay chi tiet chiral");
                int chiralSheet = c.Result.Sheets.FindIndex(sh => sh.Placements.Exists(p => p.PartGroupId == chiralGroup.Id));
                True(chiralSheet >= 0, "chi tiet chiral phai duoc xep");

                NestingResult mirrored = CopyResult(c.Result);
                Placement m = mirrored.Sheets[chiralSheet].Placements.Find(p => p.PartGroupId == chiralGroup.Id);
                LongRect before = chiralGroup.Shape.Polygon.Transform(m.Orientation, m.TranslationX, m.TranslationY).Bounds;
                m.Mirror = !m.Mirror;
                LongRect after = chiralGroup.Shape.Polygon.Transform(m.Orientation, 0, 0).Bounds;
                m.TranslationX = before.MinX - after.MinX;       // giu nguyen goc hop bao
                m.TranslationY = before.MinY - after.MinY;
                LongRect moved = chiralGroup.Shape.Polygon.Transform(m.Orientation, m.TranslationX, m.TranslationY).Bounds;
                True(moved.MinX == before.MinX && moved.MaxX == before.MaxX && moved.MinY == before.MinY && moved.MaxY == before.MaxY,
                    "dot bien lat guong phai giu NGUYEN hop bao");
                ExpectIssue(RoundTripNewDrawing(c, mirrored, c.Outputs, true), "KHONG co hinh trung khop", "dot bien LAT GUONG SAI (cung hop bao)");
            }
        }

        /// <summary>
        /// B15. Round-trip cua cach xuat MAC DINH: ve thang vao ban ve dang mo (pha khoi). Chi
        /// doc cac entity MOI; ban ve goc phai nguyen tung entity.
        /// </summary>
        private static void B15_RoundTripDrawIntoCurrent()
        {
            using (Database db = new Database(true, true))
            {
                RoundTripCase c = PrepareRoundTrip(db);
                HashSet<ObjectId> before;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction()) before = new HashSet<ObjectId>(ModelSpaceIds(db, tr));

                GhoPhoiSettings s = Settings();
                s.LabelParts = false;
                s.OutputAsBlocks = false;
                Point3d at = new Point3d(-12345.5, 67890.25, 0);      // goc am / lon, khong nguyen
                NestingDwgWriter.DrawIntoCurrent(db, at, c.Request, c.Result, c.Outputs, s, "roundtrip");

                Equal(c.SourceBefore, SourceSnapshot(db, before), "entity goc KHONG doi");
                List<ObjectId> added;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction()) added = ModelSpaceIds(db, tr).FindAll(id => !before.Contains(id));
                True(added.Count > 0, "co entity moi");

                List<OutputShape> shapes;
                List<Extents3d> frames;
                ReadBack(db, added, s, out shapes, out frames);
                List<string> issues = CheckRoundTrip(c.Request, c.Result, shapes, frames);
                True(issues.Count == 0, string.Join(" | ", issues.ToArray()));
            }
        }
    }
}
