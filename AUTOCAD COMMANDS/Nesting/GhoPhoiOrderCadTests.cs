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
            NestingTestHarness.Run(report, "D3. Duong bao ghep tu hai don -> MO HO, khong tu chon bua", D3_ConflictIsAmbiguous);
            NestingTestHarness.Run(report, "D4. Nhan P + STT: bat thi co, tat thi khong", D4_PartLabelOption);
            NestingTestHarness.Run(report, "D5. Ten don tren to xuat ra; ban ve goc khong doi", D5_OrderOnSheetLabel);
            NestingTestHarness.Run(report, "D6. Ba don di het duong: xep -> nhan -> ban ve xuat ra", D6_ThreeOrdersEndToEnd);
            NestingTestHarness.Run(report, "D7. Ma P KHONG de len chu cat san co cua chi tiet", D7_PartLabelNeverReplacesOwnText);
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

                // Chot voi DUNG MOT don -> ten bi xoa trang (mot don khong phai la chay theo don).
                list.SetName(0, "DON-DUY-NHAT");
                list.ApplyScan(0, new[] { a }, false);
                Equal(null, list.Finalize(), "chot duoc");
                Equal(1, list.Count, "chi con dong da quet");
                Equal(string.Empty, list[0].Name, "mot don thi de ten rong");

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

                GhoPhoiSettings s = new GhoPhoiSettings();
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
        /// Mot duong bao ghep tu hai luot quet KHAC don: khong duoc tu y chon mot don roi di
        /// tiep - phai danh dau MO HO de nguoi dung quyet.
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

                GhoPhoiSettings s = new GhoPhoiSettings();
                NestReadResult read = NestingSelectionReader.Read(tr, new[] { l1, l2, l3, l4 }, s, byEntity);
                RecognitionResult rec = new PartRecognizer(s.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);

                Equal(1, GhoPhoiPipeline.AssignOrders(read, rec), "phai bao dung mot xung dot");
                Equal(1, rec.Parts.Count, "mot ban ghi");
                Equal(PartStatus.Ambiguous, rec.Parts[0].Status, "phai la MO HO");
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

                    GhoPhoiSettings s = new GhoPhoiSettings { LabelParts = labelParts };
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

                            string text = t.Contents ?? string.Empty;
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
                    GhoPhoiSettings s = new GhoPhoiSettings { LabelParts = false };
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

                    GhoPhoiSettings s = new GhoPhoiSettings { LabelParts = true };
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

                            string t = mt.Contents ?? string.Empty;
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

                    GhoPhoiSettings s = new GhoPhoiSettings { LabelParts = false };
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
