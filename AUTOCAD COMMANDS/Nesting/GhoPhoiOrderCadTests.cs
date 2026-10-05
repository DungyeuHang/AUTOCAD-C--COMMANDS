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
    /// <summary>
    /// DON HANG - phan can den AutoCAD: bang don, ten don di tu luot quet vao ban ghi nhan
    /// dang, nhan P + STT, va ten don tren ban ve xuat ra.
    ///
    /// Chay tren database trong bo nho, khong bao gio dung den ban ve dang mo.
    /// </summary>
    internal static class GhoPhoiOrderCadTests
    {
        public static void Run(NestingTestReport report)
        {
            NestingTestHarness.Run(report, "D1. Bang don: ten trong / trung / xoa / quet lai", D1_OrderListRules);
            NestingTestHarness.Run(report, "D2. Ten don di tu luot quet vao ban ghi nhan dang", D2_OrderReachesRecords);
            NestingTestHarness.Run(report, "D3. Duong bao ghep tu hai don -> tu chon don nhieu net nhat, CANH BAO", D3_ConflictIsAmbiguous);
            NestingTestHarness.Run(report, "D4. Nhan P + STT: bat thi co, tat thi khong", D4_PartLabelOption);
            NestingTestHarness.Run(report, "D5. Ten don tren to xuat ra; ban ve goc khong doi", D5_OrderOnSheetLabel);
            NestingTestHarness.Run(report, "D6. Ba don di het duong: xep -> nhan -> ban ve xuat ra", D6_ThreeOrdersEndToEnd);
            NestingTestHarness.Run(report, "D7. Ma P KHONG de len chu cat san co cua chi tiet", D7_PartLabelNeverReplacesOwnText);
            NestingTestHarness.Run(report, "D8. Nhan chi tiet ghi them ten phoi + ten don (bat / tat)", D8_PartLabelNameAndOrder);
            NestingTestHarness.Run(report, "D9. Mac dinh: ve xong EXPLODE het + PURGE sach block GHOPHOI", D9_ExplodeAndPurgeByDefault);
            NestingTestHarness.Run(report, "D10. Nho bang don + phoi da quet lan truoc; RESET xoa het", D10_OrderMemory);
            NestingTestHarness.Run(report, "D11. Giu block -> quet lai ket qua de GHEP LAI: giu SL / vat lieu / don, khong nham block", D11_RenestFromKeptBlocks);
            NestingTestHarness.Run(report, "D12. Nho chinh sua o bang KIEM TRA; chay lai ap dung cai; RESET xoa", D12_ReviewEditsRemembered);
        }

        private static void D12_ReviewEditsRemembered()
        {
            foreach (string path in new[] { null, Path.Combine(Path.GetTempPath(), "ghophoi_rev_" + Guid.NewGuid().ToString("N") + ".dwg") })
            {
                string tag = path == null ? "chua luu" : "co ten";
                using (Database db = new Database(true, true))
                {
                    List<ObjectId> ids = new List<ObjectId>();
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        ids.Add(Rect(db, tr, 0, 0, 200, 100));
                        ids.Add(Rect(db, tr, 300, 0, 150, 80));
                        tr.Commit();
                    }

                    GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
                    try
                    {
                        // ---- lan 1: nguoi dung sua o bang KIEM TRA ----
                        NestReadResult read;
                        RecognitionResult rec = ReadLikeCommand(db, ids, s, null, out read);
                        Dictionary<RecognizedPart, ReviewEdit> snap = GhoPhoiPipeline.SnapshotForReview(read, rec);
                        RecognizedPart big = rec.Parts.Find(p => p.MinX < 1), small = rec.Parts.Find(p => p.MinX > 1);
                        big.Quantity = 7;
                        big.Material = "1.5MM";
                        small.Include = false;

                        List<string> seen = new List<string>();
                        foreach (ReviewEdit o in snap.Values) seen.Add(o.Key);
                        List<ReviewEdit> diff = GhoPhoiPipeline.DiffAfterReview(rec, snap);
                        Equal(2, diff.Count, tag + ": hai chi tiet bi sua");
                        NestingReviewMemory.Merge(db, path, seen, diff);

                        // Ban ve co ten: bo ban trong bo nho de ep doc lai tu FILE (nhu tat AutoCAD mo lai).
                        if (path != null) NestingReviewMemory.ForgetSession(db);

                        // ---- lan 2: chay lai, phai ap dung cai ----
                        NestReadResult read2;
                        RecognitionResult rec2 = ReadLikeCommand(db, ids, s, null, out read2);
                        Dictionary<RecognizedPart, ReviewEdit> snap2 = GhoPhoiPipeline.SnapshotForReview(read2, rec2);
                        Equal(2, GhoPhoiPipeline.ApplyReviewEdits(rec2, snap2, NestingReviewMemory.Load(db, path)), tag + ": ap lai 2 chi tiet");
                        RecognizedPart big2 = rec2.Parts.Find(p => p.MinX < 1), small2 = rec2.Parts.Find(p => p.MinX > 1);
                        Equal(7, big2.Quantity, tag + ": SL da sua");
                        Equal("1.5MM", big2.Material, tag + ": vat lieu da sua");
                        True(big2.Include, tag + ": ghep khong doi");
                        True(!small2.Include, tag + ": bo tick Ghep duoc nho");
                        Equal(1, small2.Quantity, tag + ": SL cai khong sua van nhu nhan dang");

                        // ---- lan 2 chi quet cai nho, sua lai cho no: chinh sua cua cai lon PHAI con ----
                        NestReadResult read3;
                        RecognitionResult rec3 = ReadLikeCommand(db, new List<ObjectId> { ids[1] }, s, null, out read3);
                        Dictionary<RecognizedPart, ReviewEdit> snap3 = GhoPhoiPipeline.SnapshotForReview(read3, rec3);
                        GhoPhoiPipeline.ApplyReviewEdits(rec3, snap3, NestingReviewMemory.Load(db, path));
                        rec3.Parts[0].Include = true;      // tick lai = tro ve nhu nhan dang -> het chinh sua
                        List<string> seen3 = new List<string>();
                        foreach (ReviewEdit o in snap3.Values) seen3.Add(o.Key);
                        NestingReviewMemory.Merge(db, path, seen3, GhoPhoiPipeline.DiffAfterReview(rec3, snap3));
                        Equal(1, NestingReviewMemory.Load(db, path).Count, tag + ": chi con chinh sua cua cai lon");

                        // ---- RESET ----
                        NestingOrderMemory.Clear(db, path);
                        Equal(0, NestingReviewMemory.Load(db, path).Count, tag + ": RESET xoa chinh sua");
                    }
                    finally
                    {
                        NestingOrderMemory.Clear(db, path);
                    }
                }
            }
        }

        /// <summary>Doc + nhan dang + gan don + lay lai thong tin tu block, nhu lenh that.</summary>
        private static RecognitionResult ReadLikeCommand(Database db, IList<ObjectId> ids, GhoPhoiSettings s,
            IDictionary<ObjectId, string> byEntity, out NestReadResult read)
        {
            RecognitionResult rec;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                read = NestingSelectionReader.Read(tr, ids, s, byEntity);
                rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
            }

            GhoPhoiPipeline.AssignOrders(read, rec);
            GhoPhoiPipeline.ApplyPresets(read, rec);
            return rec;
        }

        private static void D11_RenestFromKeptBlocks()
        {
            using (Database db = new Database(true, true))
            {
                List<ObjectId> source = new List<ObjectId>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    source.Add(Rect(db, tr, 0, 0, 200, 100));
                    source.Add(Append(db, tr, new MText { Location = new Point3d(20, 80, 0), TextHeight = 10, Contents = "SL: 3\\P1.5MM" }));
                    source.Add(Rect(db, tr, 300, 0, 150, 80));
                    source.Add(Append(db, tr, new MText { Location = new Point3d(320, 60, 0), TextHeight = 10, Contents = "SL: 2\\P1.5MM" }));
                    tr.Commit();
                }

                Dictionary<ObjectId, string> byEntity = new Dictionary<ObjectId, string>();
                foreach (ObjectId id in source) byEntity[id] = "DON-X";

                GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, OutputAsBlocks = true };
                NestReadResult read;
                RecognitionResult rec = ReadLikeCommand(db, source, s, byEntity, out read);
                Equal(2, rec.Parts.Count, "hai chi tiet goc");

                NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                req.SheetByMaterial["1.5MM"] = new SheetSpec("T15", 2000, 1000);
                req.Settings.TimeBudgetSeconds = 60;
                req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));
                NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                True(res.Validation.IsValid, "validator");
                Equal(5, res.Statistics.PlacedQuantity, "3 + 2");

                HashSet<ObjectId> before;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction()) before = new HashSet<ObjectId>(ModelSpaceIds(db, tr));
                NestingDwgWriter.DrawIntoCurrent(db, new Point3d(0, -5000, 0), req, res, Outputs(req, read), s, "test");
                List<ObjectId> firstResult;
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    firstResult = ModelSpaceIds(db, tr).FindAll(id => !before.Contains(id));

                // ---- quet CA vung ket qua cu (khung, nhan, chu... deu nam trong) ----
                GhoPhoiSettings s2 = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
                NestReadResult read2;
                RecognitionResult again = ReadLikeCommand(db, firstResult, s2, null, out read2);
                List<RecognizedPart> nestable = again.Parts.FindAll(p => p.IsNestable);
                Equal(5, nestable.Count, "5 block = 5 chi tiet, khung to / nhan bi bo qua: " +
                    string.Join(" | ", again.Parts.ConvertAll(p => p.Name + " " + p.Status + " " + p.NotesText).ToArray()));
                Equal(5, read2.Presets.Count, "moi block mang thong tin");
                foreach (RecognizedPart p in nestable)
                {
                    Equal(1, p.Quantity, "moi block la 1 cai");
                    Equal("1.5MM", SimpleNestingEngine.NormalizeMaterial(p.Material), "vat lieu giu nguyen: " + p.NotesText);
                    Equal("DON-X", p.Order, "don giu nguyen");
                    True(p.Status != PartStatus.Ambiguous, "khong mo ho: " + p.NotesText);
                }

                // ---- ghep lai lan 2 (van giu block): block moi KHONG duoc dung nham dinh nghia cu ----
                NestingRequest req2 = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                req2.SheetByMaterial["1.5MM"] = new SheetSpec("T15", 2000, 1000);
                req2.Settings.TimeBudgetSeconds = 60;
                req2.Groups.AddRange(PartRecognizer.ToPartGroups(again.Parts, s.ArcToleranceMm));
                NestingResult res2 = new SimpleNestingEngine().Nest(req2, CancellationToken.None, null);
                True(res2.Validation.IsValid, "validator lan 2");

                // Mot chi tiet MOI hinh khac nhung se mang ten P1 giong lan truoc.
                ObjectId odd;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    odd = Rect(db, tr, 10000, 0, 400, 50);
                    tr.Commit();
                }

                NestReadResult read3;
                RecognitionResult rec3 = ReadLikeCommand(db, new List<ObjectId> { odd }, s, null, out read3);
                NestingRequest req3 = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                req3.Settings.TimeBudgetSeconds = 30;
                req3.Groups.AddRange(PartRecognizer.ToPartGroups(rec3.Parts, s.ArcToleranceMm));
                NestingResult res3 = new SimpleNestingEngine().Nest(req3, CancellationToken.None, null);

                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction()) before = new HashSet<ObjectId>(ModelSpaceIds(db, tr));
                NestingDwgWriter.DrawIntoCurrent(db, new Point3d(0, -20000, 0), req3, res3, Outputs(req3, read3), s, "test");
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    int refs = 0;
                    foreach (ObjectId id in ModelSpaceIds(db, tr))
                    {
                        if (before.Contains(id)) continue;
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        refs++;
                        Extents3d e = br.GeometricExtents;
                        double longest = Math.Max(e.MaxPoint.X - e.MinPoint.X, e.MaxPoint.Y - e.MinPoint.Y);
                        True(Math.Abs(longest - 400) < 1e-3, "block moi phai la hinh MOI (400), dang la " + longest);
                    }

                    Equal(1, refs, "mot block moi");
                }
            }
        }

        private static void D10_OrderMemory()
        {
            // ---- luat cua bang ----
            NestingOrderList list = new NestingOrderList();
            list.Restore(null);
            Equal(1, list.Count, "nap bang rong -> mot dong trong");
            True(!list.AnyScanned, "chua quet gi");

            using (Database db = new Database(true, true))
            {
                ObjectId a, b, c;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    a = Rect(db, tr, 0, 0, 100, 100);
                    b = Rect(db, tr, 200, 0, 100, 100);
                    c = Rect(db, tr, 400, 0, 100, 100);
                    tr.Commit();
                }

                NestingOrderEntry e1 = new NestingOrderEntry { Name = "DH-A" };
                e1.Ids.AddRange(new[] { a, b });
                NestingOrderEntry e2 = new NestingOrderEntry { Name = "DH-B" };
                e2.Ids.Add(c);
                NestingOrderEntry empty = new NestingOrderEntry { Name = "DH-TRONG" };   // chua quet -> khong luu

                foreach (string path in new[] { null, Path.Combine(Path.GetTempPath(), "ghophoi_mem_" + Guid.NewGuid().ToString("N") + ".dwg") })
                {
                    string tag = path == null ? "ban ve chua luu" : "ban ve co ten";
                    try
                    {
                        NestingOrderMemory.Save(db, path, new[] { e1, e2, empty });

                        int missing;
                        List<NestingOrderEntry> back = NestingOrderMemory.Load(db, path, out missing);
                        Equal(2, back.Count, tag + ": hai don da quet");
                        Equal("DH-A", back[0].Name, tag + ": dung ten, dung thu tu");
                        Equal(2, back[0].Ids.Count, tag + ": don A hai doi tuong");
                        True(back[0].Ids.Contains(a) && back[0].Ids.Contains(b), tag + ": dung doi tuong");
                        Equal(0, missing, tag + ": khong thieu");

                        list.Restore(back);
                        Equal(2, list.Count, tag + ": bang nap lai hai dong");
                        True(list.AnyScanned, tag + ": da co quet");

                        list.Reset();
                        Equal(1, list.Count, tag + ": RESET con mot dong");
                        True(!list.AnyScanned, tag + ": RESET xoa het quet");

                        NestingOrderMemory.Clear(db, path);
                        Equal(0, NestingOrderMemory.Load(db, path, out missing).Count, tag + ": RESET xoa ca ban luu");
                    }
                    finally
                    {
                        NestingOrderMemory.Clear(db, path);
                    }
                }

                // ---- doc tu FILE (phien sau): doi tuong da bi xoa thi bo qua va dem ----
                string file = Path.Combine(Path.GetTempPath(), "ghophoi_mem_" + Guid.NewGuid().ToString("N") + ".dwg");
                try
                {
                    NestingOrderMemory.Save(db, file, new[] { e1, e2 });
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        tr.GetObject(b, OpenMode.ForWrite).Erase();
                        tr.Commit();
                    }

                    using (Database other = new Database(true, true))
                    {
                        // Database khac = khong co ban trong bo nho -> phai doc tu file. Handle cua
                        // ban ve moi khong trung voi ban ve kia, nen chi kiem la doc duoc hai dong.
                        int ignored;
                        Equal(2, NestingOrderMemory.Load(other, file, out ignored).Count, "doc lai tu file: hai don");
                    }

                    int missing;
                    List<NestingOrderEntry> back = NestingOrderMemory.Load(db, file, out missing);
                    Equal(1, back[0].Ids.Count, "doi tuong da xoa bi bo qua");
                    Equal(1, missing, "va duoc dem de bao nguoi dung");
                }
                finally
                {
                    NestingOrderMemory.Clear(db, file);
                }
            }
        }

        private static void D9_ExplodeAndPurgeByDefault()
        {
            using (Database db = new Database(true, true))
            {
                NestReadResult read;
                RecognitionResult rec;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Rect(db, tr, 0, 0, 200, 100);
                    Rect(db, tr, 300, 0, 150, 80);
                    Rect(db, tr, 320, 10, 150, 40);     // chong len cai tren -> gop thanh 1 chi tiet
                    tr.Commit();
                }

                GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
                True(!s.OutputAsBlocks, "mac dinh KHONG giu block");
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    read = NestingSelectionReader.Read(tr, ModelSpaceIds(db, tr), s);
                    rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                }

                Equal(2, rec.Parts.Count, "hai chi tiet (cai chong nhau da gop): " +
                    string.Join(" | ", rec.Parts.ConvertAll(p => p.Name + " " + p.Status + " " + p.NotesText).ToArray()));
                True(rec.Parts.TrueForAll(p => p.IsNestable), "ca hai ghep duoc");

                NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                req.Settings.TimeBudgetSeconds = 60;
                req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));
                NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                True(res.Validation.IsValid, "validator");

                NestingDwgWriter.DrawIntoCurrent(db, new Point3d(0, -5000, 0), req, res, Outputs(req, read), s, "test");

                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    int inserts = 0;
                    foreach (ObjectId id in ModelSpaceIds(db, tr))
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference) inserts++;
                    }

                    Equal(0, inserts, "khong con BlockReference nao");

                    List<string> left = new List<string>();
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in bt)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (btr.Name.StartsWith("GHOPHOI", StringComparison.OrdinalIgnoreCase)) left.Add(btr.Name);
                    }

                    Equal(0, left.Count, "khong con dinh nghia block GHOPHOI_*: " + string.Join(", ", left.ToArray()));
                }
            }
        }

        // ==================================================================================
        // helpers
        // ==================================================================================

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

        /// <summary>Mot chu nhat kin bang LWPolyline, tra ve ObjectId cua no.</summary>
        private static ObjectId Rect(Database db, Transaction tr, double x, double y, double w, double h)
        {
            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x + w, y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x + w, y + h), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x, y + h), 0, 0, 0);
            pl.Closed = true;
            return Append(db, tr, pl);
        }

        private static List<OutputPart> Outputs(NestingRequest req, NestReadResult read)
        {
            List<OutputPart> outputs = new List<OutputPart>();
            foreach (PartGroup g in req.Groups)
            {
                RecognizedPart r = (RecognizedPart)g.SourceReference;
                Pt o = PartRecognizer.LocalOrigin(r);
                OutputPart op = new OutputPart { Group = g, Origin = new Point3d(o.X, o.Y, 0) };
                foreach (int s in r.GeometrySources) op.SourceIds.Add(read.Sources[s].Id);
                outputs.Add(op);
            }

            return outputs;
        }

        /// <summary>Dong dau cua noi dung MText (nhan chi tiet: ma P nam o dong dau).</summary>
        private static string FirstLine(string contents)
        {
            string text = contents ?? string.Empty;
            int cut = text.IndexOf("\\P", StringComparison.Ordinal);
            return cut < 0 ? text : text.Substring(0, cut);
        }

        // ==================================================================================
        // tests
        // ==================================================================================

        /// <summary>
        /// Moi luat cua bang don hang. Day la cho de sai nhat trong ca tinh nang: mat mot luot
        /// quet cu vi bam nham, hoac hai dong cung ten roi khong biet chi tiet thuoc dong nao.
        /// </summary>
        private static void D1_OrderListRules()
        {
            NestingOrderList list = new NestingOrderList();
            Equal(1, list.Count, "bang luon bat dau bang mot dong");

            // Ten de trong thi khong cho quet.
            list.SetName(0, "   ");
            Equal(string.Empty, list[0].Name, "khoang trang hai dau bi cat");
            True(list.WhyCannotScan(0) != null, "ten trong thi phai chan lai");

            // Ten trung thi canh bao, khong im lang de len nhau.
            list.SetName(0, "DON-A");
            list.Add();
            list.SetName(1, "don-a");
            Equal(1, list.DuplicateRowOf(1), "trung ten (khong phan biet hoa thuong) voi dong 1");
            True(list.WhyCannotScan(1) != null, "trung ten thi phai chan lai");

            list.SetName(1, "DON-B");
            Equal(0, list.DuplicateRowOf(1), "doi ten xong thi het trung");
            True(list.WhyCannotScan(1) == null, "ten rieng thi quet duoc");

            // Nhieu dong cung ton tai, khong dong nao de len dong nao.
            list.Add();
            list.SetName(2, "DON-C");
            Equal(3, list.Count, "ba dong");
            Equal("DON-A", list[0].Name, "dong 1 giu nguyen ten");
            Equal("DON-B", list[1].Name, "dong 2 giu nguyen ten");

            // Ten dai khong bi cat.
            string big = new string('X', 150);
            list.SetName(2, big);
            Equal(big, list[2].Name, "ten dai giu nguyen ven");
            list.SetName(2, "DON-C");

            // Quet lan hai: THEM vao hay THAY THE deu phai lam dung.
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId a = Rect(db, tr, 0, 0, 100, 100);
                ObjectId b = Rect(db, tr, 200, 0, 100, 100);
                ObjectId c = Rect(db, tr, 400, 0, 100, 100);

                list.ApplyScan(0, new[] { a, b }, false);
                Equal(2, list[0].Ids.Count, "luot quet dau: 2 doi tuong");
                True(list[0].Scanned, "dong 1 da quet");

                list.ApplyScan(0, new[] { c }, true);
                Equal(3, list[0].Ids.Count, "quet THEM thi cong don lai");

                list.ApplyScan(0, new[] { a, b }, true);
                Equal(3, list[0].Ids.Count, "doi tuong trung chi tinh mot lan");

                list.ApplyScan(0, new[] { c }, false);
                Equal(1, list[0].Ids.Count, "quet LAI thi bo het cai cu");

                // Xoa mot dong CHUA quet.
                list.Remove(2);
                Equal(2, list.Count, "xoa dong chua quet");
                Equal("DON-B", list[1].Name, "dong con lai khong bi xao tron");

                // Xoa mot dong DA quet.
                list.ApplyScan(1, new[] { a, b }, false);
                Equal(2, list[1].Ids.Count, "dong 2 da quet");
                list.Remove(1);
                Equal(1, list.Count, "xoa duoc ca dong da quet");
                Equal("DON-A", list[0].Name, "con lai dung dong 1");

                // Xoa het thi tu tao lai mot dong trong - bang khong bao gio duoc rong.
                list.Remove(0);
                Equal(1, list.Count, "xoa dong cuoi thi tu tao lai mot dong");
                True(!list[0].Scanned, "dong moi chua quet gi");

                // Chot khi chua quet gi -> phai bao loi.
                True(list.Finalize() != null, "chua quet gi thi khong chot duoc");

                // Chot voi DUNG MOT don -> ten nguoi dung dat duoc GIU (de ghi "P + ten don" len phoi).
                list.SetName(0, "DON-DUY-NHAT");
                list.ApplyScan(0, new[] { a }, false);
                Equal(null, list.Finalize(), "chot duoc");
                Equal(1, list.Count, "chi con dong da quet");
                Equal("DON-DUY-NHAT", list[0].Name, "mot don van giu ten nguoi dung dat");

                // ... chi ten TU SINH ("DON-01") moi bi xoa trang.
                NestingOrderList auto = new NestingOrderList();
                auto.ApplyScan(0, new[] { a }, false);
                Equal(null, auto.Finalize(), "chot duoc voi ten tu sinh");
                Equal(string.Empty, auto[0].Name, "ten tu sinh DON-01 khong mang di ghi");
                True(NestingOrderList.IsAutoName("DON-12") && !NestingOrderList.IsAutoName("DON-A1") && !NestingOrderList.IsAutoName("DH-01"), "nhan ra ten tu sinh");

                // Quet mot doi tuong da thuoc don khac -> CHUYEN sang don moi (luot quet sau thang).
                NestingOrderList move = new NestingOrderList();
                move.SetName(0, "CU");
                move.ApplyScan(0, new[] { a, b }, false);
                move.Add();
                move.SetName(1, "MOI");
                Equal(1, move.ApplyScan(1, new[] { b, c }, false), "bao so doi tuong da chuyen");
                True(move[0].Ids.Count == 1 && move[0].Ids[0] == a, "don cu mat doi tuong vua chuyen");
                True(move[1].Ids.Contains(b) && move[1].Ids.Contains(c), "don moi co doi tuong vua quet");

                // Chot voi HAI don -> giu nguyen ten ca hai.
                NestingOrderList two = new NestingOrderList();
                two.SetName(0, "DON-X");
                two.ApplyScan(0, new[] { a }, false);
                two.Add();
                two.SetName(1, "DON-Y");
                two.ApplyScan(1, new[] { b }, false);
                two.Add();   // dong thua, chua quet - phai bi bo di chu khong bao loi

                Equal(null, two.Finalize(), "chot duoc voi hai don");
                Equal(2, two.Count, "dong thua bi bo, con dung hai don");
                Equal("DON-X", two[0].Name, "ten don 1 giu nguyen");
                Equal("DON-Y", two[1].Name, "ten don 2 giu nguyen");

                // Chot khi da quet ma chua co ten -> phai bao loi.
                NestingOrderList blank = new NestingOrderList();
                blank.SetName(0, "DON-P");
                blank.ApplyScan(0, new[] { a }, false);
                blank.Add();
                blank.SetName(1, string.Empty);
                blank.ApplyScan(1, new[] { b }, false);
                True(blank.Finalize() != null, "da quet ma chua co ten thi phai bao");
            }
        }

        /// <summary>Ten don di tu luot quet -> doi tuong -> ban ghi nhan dang -> nhom ghep.</summary>
        private static void D2_OrderReachesRecords()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId a = Rect(db, tr, 0, 0, 200, 100);
                ObjectId b = Rect(db, tr, 400, 0, 150, 120);

                Dictionary<ObjectId, string> byEntity = new Dictionary<ObjectId, string>
                {
                    { a, "DON-A" },
                    { b, "DON-B" }
                };

                GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
                NestReadResult read = NestingSelectionReader.Read(tr, new[] { a, b }, s, byEntity);
                RecognitionResult rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);

                Equal(0, GhoPhoiPipeline.AssignOrders(read, rec), "khong co xung dot");
                Equal(2, rec.Parts.Count, "hai ban ghi");

                foreach (RecognizedPart p in rec.Parts)
                {
                    True(p.Order == "DON-A" || p.Order == "DON-B", "ban ghi phai co ten don, dang co: " + p.Order);
                }

                // ... va di tiep sang nhom ghep.
                List<PartGroup> groups = PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm);
                Equal(2, groups.Count, "hai nhom");
                foreach (PartGroup g in groups)
                {
                    RecognizedPart r = (RecognizedPart)g.SourceReference;
                    Equal(r.Order, g.Order, "nhom ghep phai giu dung ten don cua ban ghi");
                }
            }
        }

        /// <summary>
        /// Mot duong bao ghep tu hai luot quet KHAC don: tu chon don chiem nhieu net nhat (bang
        /// nhau thi theo ten), chi CANH BAO - khong chan nguoi dung nua (yeu cau cua nguoi dung).
        /// Ghi chu van phai neu du ten ca hai don.
        /// </summary>
        private static void D3_ConflictIsAmbiguous()
        {
            using (Database db = new Database(true, true))
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Mot hinh vuong ghep tu BON doan thang roi - hai doan thuoc don A, hai doan don B.
                ObjectId l1 = Append(db, tr, new Line(new Point3d(0, 0, 0), new Point3d(200, 0, 0)));
                ObjectId l2 = Append(db, tr, new Line(new Point3d(200, 0, 0), new Point3d(200, 200, 0)));
                ObjectId l3 = Append(db, tr, new Line(new Point3d(200, 200, 0), new Point3d(0, 200, 0)));
                ObjectId l4 = Append(db, tr, new Line(new Point3d(0, 200, 0), new Point3d(0, 0, 0)));

                Dictionary<ObjectId, string> byEntity = new Dictionary<ObjectId, string>
                {
                    { l1, "DON-A" }, { l2, "DON-A" }, { l3, "DON-B" }, { l4, "DON-B" }
                };

                GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty };
                NestReadResult read = NestingSelectionReader.Read(tr, new[] { l1, l2, l3, l4 }, s, byEntity);
                RecognitionResult rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);

                Equal(1, GhoPhoiPipeline.AssignOrders(read, rec), "phai bao dung mot xung dot");
                Equal(1, rec.Parts.Count, "mot ban ghi");
                Equal(PartStatus.Warning, rec.Parts[0].Status, "chi CANH BAO, khong chan");
                Equal("DON-A", rec.Parts[0].Order, "bang nhau 2-2 thi chon theo ten");
                True(rec.Parts[0].NotesText.Contains("DON-A") && rec.Parts[0].NotesText.Contains("DON-B"),
                    "ghi chu phai neu ten ca hai don, dang co: " + rec.Parts[0].NotesText);
            }
        }

        /// <summary>Nhan P + STT: bat thi moi chi tiet mot nhan "Pnn"; tat thi khong co cai nao.</summary>
        private static void D4_PartLabelOption()
        {
            Equal(3, LabelCount(true), "bat thi ba chi tiet ba nhan");
            Equal(0, LabelCount(false), "tat thi khong con nhan nao");
        }

        /// <summary>So nhan "Pnn" tren layer nhan trong ban ve xuat ra.</summary>
        private static int LabelCount(bool labelParts)
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_order_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Rect(db, tr, 0, 0, 200, 100);
                        Rect(db, tr, 300, 0, 200, 100);
                        Rect(db, tr, 600, 0, 200, 100);
                        tr.Commit();
                    }

                    GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, LabelParts = labelParts };
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        read = NestingSelectionReader.Read(tr, ModelSpaceIds(db, tr), s);
                        rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                    }

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                    req.Settings.TimeBudgetSeconds = 60;
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));

                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(3, res.Statistics.PlacedQuantity, "xep het ba cai");

                    NestingDwgWriter.Write(db, path, req, res, Outputs(req, read), s, "test");
                }

                int labels = 0;
                using (Database check = new Database(false, true))
                {
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in ModelSpaceIds(check, tr))
                        {
                            MText t = tr.GetObject(id, OpenMode.ForRead) as MText;
                            if (t == null || t.Layer != NestingDwgWriter.LabelLayer) continue;

                            string text = FirstLine(t.Contents);
                            if (text.Length == 3 && text[0] == 'P' && char.IsDigit(text[1]) && char.IsDigit(text[2]))
                            {
                                labels++;
                            }
                        }
                    }
                }

                return labels;
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // chi la file tam
                }
            }
        }

        /// <summary>
        /// BA don di het duong: nhan dang -> xep -> nhan tren to -> ban ve xuat ra.
        ///
        /// Kiem nhung thu chi lo ra khi chay ca duong: ten don dai bi rut gon tren nhan nhung
        /// van con DU trong danh sach cua to, chi tiet chua xep van giu ten don, va ban ve
        /// xuat ra mang dung ten don.
        /// </summary>
        private static void D6_ThreeOrdersEndToEnd()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_order_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Rect(db, tr, 0, 0, 200, 100);
                        Rect(db, tr, 300, 0, 200, 100);
                        Rect(db, tr, 600, 0, 200, 100);
                        Rect(db, tr, 900, 0, 3000, 100);   // dai hon ca to -> khong bao gio xep duoc
                        tr.Commit();
                    }

                    // Ten don co ca loai DAI de thu phan rut gon nhan.
                    string longName = "DON-HANG-CUC-KY-DAI-DE-THU-PHAN-RUT-GON-NHAN-TREN-TO-PHOI";
                    GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, LabelParts = false };
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        List<ObjectId> ids = ModelSpaceIds(db, tr);
                        Dictionary<ObjectId, string> byEntity = new Dictionary<ObjectId, string>
                        {
                            { ids[0], "DON-A" },
                            { ids[1], "DON-B" },
                            { ids[2], longName },
                            { ids[3], "DON-QUALON" }
                        };

                        read = NestingSelectionReader.Read(tr, ids, s, byEntity);
                        rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                        Equal(0, GhoPhoiPipeline.AssignOrders(read, rec), "khong co xung dot don");
                    }

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                    req.Settings.TimeBudgetSeconds = 60;
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));

                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(3, res.Statistics.PlacedQuantity, "ba cai vua xep duoc");
                    Equal(1, res.Statistics.UnplacedQuantity, "cai dai 3000 mm khong xep duoc");

                    // 34. chi tiet chua xep van giu ten don.
                    Equal(1, res.Unplaced.Count, "mot chi tiet chua xep");
                    Equal("DON-QUALON", res.Unplaced[0].OrderName, "chi tiet chua xep phai giu ten don");

                    // 29. ba don chung mot to khi can.
                    Equal(1, res.Sheets.Count, "ba cai nho vao chung mot to");
                    SheetResult sheet = res.Sheets[0];
                    Equal(3, sheet.Orders.Count, "to co ba don");

                    // 33. nhan rut gon, nhung danh sach day du thi khong duoc thieu.
                    string label = sheet.OrderLabel(2);
                    True(label.Contains("don khac"), "ba don thi nhan phai rut gon, dang co: " + label);
                    True(sheet.Orders.Contains(longName), "danh sach day du van phai co ten don dai");
                    True(sheet.Orders.Contains("DON-A") && sheet.Orders.Contains("DON-B"), "va ca hai don kia");

                    // 33b. ban bao cao ghi DU ten, khong rut gon.
                    string report = GhoPhoiPipeline.BuildReport(req, res);
                    True(report.Contains(longName), "ban bao cao phai ghi DU ten don dai");
                    True(report.Contains("DON-QUALON"), "ban bao cao phai neu ten don cua chi tiet chua xep");

                    NestingDwgWriter.Write(db, path, req, res, Outputs(req, read), s, "test");
                }

                // 35. ban ve xuat ra mang dung ten don.
                bool found = false;
                using (Database check = new Database(false, true))
                {
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in ModelSpaceIds(check, tr))
                        {
                            MText t = tr.GetObject(id, OpenMode.ForRead) as MText;
                            if (t == null) continue;
                            if ((t.Contents ?? string.Empty).Contains("DON-A")) found = true;
                        }
                    }
                }

                True(found, "ban ve xuat ra phai mang ten don");
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // chi la file tam
                }
            }
        }

        /// <summary>
        /// Chi tiet DA CO chu cat cua chinh nguoi dung thi khong ve them ma P len no - neu
        /// khong se thanh hai dong chu chong nhau giua chi tiet.
        /// </summary>
        private static void D7_PartLabelNeverReplacesOwnText()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_order_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Rect(db, tr, 0, 0, 300, 200);
                        // Chu CAT nam trong chi tiet (khong doc ra SL / vat lieu).
                        Append(db, tr, new DBText { Position = new Point3d(60, 90, 0), Height = 20, TextString = "MA-XYZ" });
                        Rect(db, tr, 500, 0, 300, 200);   // cai nay khong co chu
                        tr.Commit();
                    }

                    GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, LabelParts = true };
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        read = NestingSelectionReader.Read(tr, ModelSpaceIds(db, tr), s);
                        rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                    }

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                    req.Settings.TimeBudgetSeconds = 60;
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));

                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(2, res.Statistics.PlacedQuantity, "hai chi tiet");

                    NestingDwgWriter.Write(db, path, req, res, Outputs(req, read), s, "test");
                }

                int pLabels = 0;
                bool ownTextKept = false;
                using (Database check = new Database(false, true))
                {
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in ModelSpaceIds(check, tr))
                        {
                            MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                            if (mt == null || mt.Layer != NestingDwgWriter.LabelLayer) continue;

                            string t = FirstLine(mt.Contents);
                            if (t.Length == 3 && t[0] == 'P') pLabels++;
                        }

                        // Chi tiet duoc xuat ra duoi dang KHOI, nen chu cat cua nguoi dung nam
                        // trong DINH NGHIA KHOI chu khong o model space - phai tim ca trong do.
                        BlockTable bt = (BlockTable)tr.GetObject(check.BlockTableId, OpenMode.ForRead);
                        foreach (ObjectId btrId in bt)
                        {
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                            foreach (ObjectId id in btr)
                            {
                                DBText dt = tr.GetObject(id, OpenMode.ForRead) as DBText;
                                if (dt != null && (dt.TextString ?? string.Empty).Contains("MA-XYZ")) ownTextKept = true;
                            }
                        }
                    }
                }

                True(ownTextKept, "chu cat cua nguoi dung phai duoc giu nguyen van");
                Equal(1, pLabels, "chi cai KHONG co chu san moi duoc ve ma P (dang co " + pLabels + ")");
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // chi la file tam
                }
            }
        }

        /// <summary>
        /// Bat "ghi them ten phoi + ten don": nhan cua moi chi tiet = ma P, ten phoi, ten don
        /// (ca ba trong cung MOT MText). Tat thi chi con ma P nhu cu.
        /// </summary>
        private static void D8_PartLabelNameAndOrder()
        {
            List<string> on = PartLabels(true);
            Equal(2, on.Count, "hai chi tiet hai nhan");
            foreach (string t in on)
            {
                string[] lines = t.Split(new[] { "\\P" }, StringSplitOptions.None);
                Equal(3, lines.Length, "ma P + ten phoi + ten don, dang co: " + t);
                True(lines[0].Length == 3 && lines[0][0] == 'P', "dong dau van la ma P: " + t);
                True(lines[1].StartsWith("P", StringComparison.Ordinal), "dong hai la ten phoi: " + t);
                True(lines[2] == "Don: DON-A" || lines[2] == "Don: DON-B", "dong ba la ten don: " + t);
            }

            True(on.Exists(t => t.EndsWith("DON-A", StringComparison.Ordinal)) &&
                 on.Exists(t => t.EndsWith("DON-B", StringComparison.Ordinal)), "moi chi tiet dung don cua no");

            List<string> off = PartLabels(false);
            Equal(2, off.Count, "tat thi van hai nhan ma P");
            True(off.TrueForAll(t => t.Length == 3 && t[0] == 'P'), "tat thi chi con ma P");
        }

        /// <summary>Noi dung cac nhan chi tiet (layer nhan) trong ban ve xuat ra.</summary>
        private static List<string> PartLabels(bool nameAndOrder)
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_order_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Rect(db, tr, 0, 0, 300, 200);
                        Rect(db, tr, 500, 0, 300, 200);
                        tr.Commit();
                    }

                    GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, LabelParts = true, LabelPartNameAndOrder = nameAndOrder };
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        List<ObjectId> ids = ModelSpaceIds(db, tr);
                        Dictionary<ObjectId, string> byEntity = new Dictionary<ObjectId, string>
                        {
                            { ids[0], "DON-A" },
                            { ids[1], "DON-B" }
                        };

                        read = NestingSelectionReader.Read(tr, ids, s, byEntity);
                        rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                        Equal(0, GhoPhoiPipeline.AssignOrders(read, rec), "khong co xung dot don");
                    }

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                    req.Settings.TimeBudgetSeconds = 60;
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));

                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(2, res.Statistics.PlacedQuantity, "hai chi tiet");

                    NestingDwgWriter.Write(db, path, req, res, Outputs(req, read), s, "test");
                }

                List<string> labels = new List<string>();
                using (Database check = new Database(false, true))
                {
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in ModelSpaceIds(check, tr))
                        {
                            MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                            if (mt != null && mt.Layer == NestingDwgWriter.LabelLayer) labels.Add(mt.Contents ?? string.Empty);
                        }
                    }
                }

                return labels;
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // chi la file tam
                }
            }
        }

        /// <summary>
        /// To co hai don thi nhan tren to phai neu ca hai, va ban ve GOC khong duoc dong vao.
        /// </summary>
        private static void D5_OrderOnSheetLabel()
        {
            string path = Path.Combine(Path.GetTempPath(), "ghophoi_order_" + Guid.NewGuid().ToString("N") + ".dwg");
            try
            {
                using (Database db = new Database(true, true))
                {
                    NestReadResult read;
                    RecognitionResult rec;
                    List<ObjectId> before;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Rect(db, tr, 0, 0, 200, 100);
                        Rect(db, tr, 300, 0, 200, 100);
                        tr.Commit();
                    }

                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        before = ModelSpaceIds(db, tr);
                    }

                    GhoPhoiSettings s = new GhoPhoiSettings { DefaultMaterialType = string.Empty, LabelParts = false };
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        List<ObjectId> ids = ModelSpaceIds(db, tr);
                        Dictionary<ObjectId, string> byEntity = new Dictionary<ObjectId, string>
                        {
                            { ids[0], "DON-MOT" },
                            { ids[1], "DON-HAI" }
                        };

                        read = NestingSelectionReader.Read(tr, ids, s, byEntity);
                        rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
                        GhoPhoiPipeline.AssignOrders(read, rec);
                    }

                    NestingRequest req = new NestingRequest { DefaultSheet = new SheetSpec("T", 2000, 1000) };
                    req.Settings.TimeBudgetSeconds = 60;
                    req.Groups.AddRange(PartRecognizer.ToPartGroups(rec.Parts, s.ArcToleranceMm));

                    NestingResult res = new SimpleNestingEngine().Nest(req, CancellationToken.None, null);
                    True(res.Validation.IsValid, "validator");
                    Equal(1, res.Sheets.Count, "ca hai vao mot to");
                    Equal(2, res.Sheets[0].Orders.Count, "to co hai don");

                    NestingDwgWriter.Write(db, path, req, res, Outputs(req, read), s, "test");

                    // Ban ve GOC khong duoc them bot gi.
                    using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        Equal(before.Count, ModelSpaceIds(db, tr).Count, "ban ve goc phai giu nguyen so doi tuong");
                    }
                }

                bool found = false;
                using (Database check = new Database(false, true))
                {
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    using (Transaction tr = check.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in ModelSpaceIds(check, tr))
                        {
                            MText t = tr.GetObject(id, OpenMode.ForRead) as MText;
                            if (t == null) continue;

                            string text = t.Contents ?? string.Empty;
                            if (text.Contains("DON-MOT") && text.Contains("DON-HAI")) found = true;
                        }
                    }
                }

                True(found, "nhan tren to phai neu ca hai ten don");
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // chi la file tam
                }
            }
        }
    }
}
