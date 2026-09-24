using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using WF = System.Windows.Forms;

namespace AUTOCAD_COMMANDS.Nesting
{
    // ==========================================================================================
    // GHOPHOI - GHEP PHOI TU DONG (2D sheet-metal nesting, V1)
    // ------------------------------------------------------------------------------------------
    // Quy trinh:
    //    GHOPHOI -> chon chi tiet (hinh + text SL / vat lieu)
    //            -> nhan dang duong bao / lo / text   (NestingSelectionReader + Recognition/*)
    //            -> bang KIEM TRA                      (NestingReviewForm)
    //            -> bang CAI DAT                       (NestingSettingsForm)
    //            -> ghep phoi tren luong phu           (Core/SimpleNestingEngine, khong goi AutoCAD)
    //            -> VALIDATOR doc lap                  (Core/NestingValidator)
    //            -> ban ve MOI                         (NestingDwgWriter)
    //
    // AN TOAN: ban ve goc chi duoc DOC. Ket qua chi duoc xuat khi validator DAT.
    // V1 = candidate-point + va cham da giac that. KHONG co NFP / GA / SA (xem V2).
    // ==========================================================================================
    public class GhoPhoiCommand
    {
        private static readonly List<ObjectId> Highlighted = new List<ObjectId>();

        [CommandMethod("GHOPHOI", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void GhoPhoi()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                Run(doc, ed, db);
            }
            catch (Exception ex)
            {
                ClearHighlight(db);
                ed.WriteMessage("\nGHOPHOI: LOI - da huy, ban ve goc giu nguyen.\n  (X) " + ex.Message);
#if DEBUG
                ed.WriteMessage("\n" + ex);
#endif
                WF.MessageBox.Show("GHOPHOI gap loi, da huy. Ban ve goc khong bi thay doi.\n\n" + ex.Message,
                    "GHOPHOI", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
            }
        }

        private static void Run(Document doc, Editor ed, Database db)
        {
            GhoPhoiSettings settings = GhoPhoiSettingsStore.Load();

            // ---- 1. selection ----
            ObjectId[] ids = SelectEntities(ed);
            if (ids == null || ids.Length == 0)
            {
                ed.WriteMessage("\nGHOPHOI: Da huy - chua chon doi tuong.");
                return;
            }

            // ---- 2. read + recognise (read only) ----
            NestReadResult read;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                read = NestingSelectionReader.Read(tr, ids, settings);
            }

            RecognitionResult recognition = new PartRecognizer(settings.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
            recognition.GlobalWarnings.InsertRange(0, read.Warnings);
            recognition.IgnoredEntityCount = read.IgnoredCount;

            int unattached = 0;
            foreach (KeyValuePair<int, List<Pt>> marking in read.Markings)
            {
                if (!PartRecognizer.AttachMarking(recognition.Parts, marking.Key, marking.Value)) unattached++;
            }

            if (unattached > 0)
            {
                recognition.GlobalWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} doi tuong tren layer danh dau ({1}) nam ngoai moi chi tiet - khong xuat.",
                    unattached, string.Join(", ", settings.MarkingLayers.ToArray())));
            }

            if (recognition.Parts.Count == 0)
            {
                ed.WriteMessage("\nGHOPHOI: Khong nhan dang duoc chi tiet nao (can duong bao kin).");
                foreach (string w in recognition.GlobalWarnings) ed.WriteMessage("\n  (!) " + w);
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nGHOPHOI: Nhan dang {0} ban ghi tu {1} doi tuong ({2} text).",
                recognition.Parts.Count, read.Sources.Count, read.Texts.Count));

            // ---- 3. review ----
            using (NestingReviewForm review = new NestingReviewForm(
                recognition, r => ZoomTo(ed, db, read, r), settings.AutoZoomInReview))
            {
                WF.DialogResult answer = Application.ShowModalDialog(review);
                settings.AutoZoomInReview = review.AutoZoom;
                ClearHighlight(db);
                if (answer != WF.DialogResult.OK)
                {
                    GhoPhoiSettingsStore.Save(settings);
                    ed.WriteMessage("\nGHOPHOI: Da huy o bang kiem tra - ban ve giu nguyen.");
                    return;
                }
            }

            List<PartGroup> groups = PartRecognizer.ToPartGroups(recognition.Parts, settings.ArcToleranceMm);

            // ---- 4. settings ----
            SortedDictionary<string, int> materials = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (PartGroup g in groups)
            {
                int n;
                materials.TryGetValue(g.Material, out n);
                materials[g.Material] = n + g.Quantity;
            }

            string catalogError;
            List<SheetSpec> catalog = GhoPhoiSettingsStore.LoadCatalog(out catalogError);
            if (catalogError != null) ed.WriteMessage("\nGHOPHOI: (!) " + catalogError);

            NestingRequest request = new NestingRequest();
            using (NestingSettingsForm form = new NestingSettingsForm(settings, catalog, materials))
            {
                if (Application.ShowModalDialog(form) != WF.DialogResult.OK)
                {
                    ed.WriteMessage("\nGHOPHOI: Da huy o bang cai dat - ban ve giu nguyen.");
                    return;
                }

                settings = form.Settings;
                GhoPhoiSettingsStore.Save(settings);
                if (form.CatalogChanged) GhoPhoiSettingsStore.SaveCatalog(form.Catalog);
                foreach (KeyValuePair<string, SheetSpec> kv in form.SheetByMaterial) request.SheetByMaterial[kv.Key] = kv.Value;
            }

            request.Settings = settings.ToNestingSettings();
            request.Groups.AddRange(groups);

            if (settings.SaveFixture) SaveFixture(ed, request, db);

            // ---- 5. nest (worker thread, core only) + validate ----
            NestingResult result;
            using (NestingProgressForm progress = new NestingProgressForm(new SimpleNestingEngine(), request))
            {
                Application.ShowModalDialog(progress);
                if (progress.Error != null) throw progress.Error;
                result = progress.Result;
            }

            if (result == null)
            {
                ed.WriteMessage("\nGHOPHOI: Khong co ket qua.");
                return;
            }

            string report = BuildReport(request, result);
            foreach (string line in report.Split('\n')) ed.WriteMessage("\n" + line.TrimEnd('\r'));

            bool valid = result.Validation != null && result.Validation.IsValid;
            bool hasPlacements = result.Statistics.PlacedQuantity > 0;
            string blockReason = !valid ? "VALIDATOR KHONG DAT - khong tao ban ve san xuat."
                : (!hasPlacements ? "Khong co chi tiet nao duoc xep." : string.Empty);

            using (NestingResultForm resultForm = new NestingResultForm(report, valid && hasPlacements, blockReason))
            {
                if (Application.ShowModalDialog(resultForm) != WF.DialogResult.OK)
                {
                    ed.WriteMessage("\nGHOPHOI: Khong tao ban ve.");
                    return;
                }
            }

            // ---- 6. output: NEW drawing ----
            List<OutputPart> outputs = BuildOutputParts(groups, read);
            string path = NestingDwgWriter.DefaultOutputPath(db);
            string sourceName = string.IsNullOrEmpty(db.Filename) ? doc.Name : Path.GetFileName(db.Filename);
            NestingDwgWriteResult written;
            try
            {
                written = NestingDwgWriter.Write(db, path, request, result, outputs, settings, sourceName);
            }
            catch (Exception ex)
            {
                // Never leave a half-written production file behind.
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // best effort
                }

                ed.WriteMessage("\nGHOPHOI: KHONG ghi duoc ban ve moi - ban ve goc khong bi thay doi.\n  (X) " + ex.Message + "\n  File: " + path);
                WF.MessageBox.Show(
                    "Khong ghi duoc ban ve moi:\n" + path + "\n\n" + ex.Message + "\n\nBan ve goc khong bi thay doi.",
                    "GHOPHOI", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
                return;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nGHOPHOI: Da tao ban ve moi ({0} chi tiet tren {1} to):\n  {2}",
                written.BlockReferenceCount, result.Sheets.Count, written.Path));
            foreach (string w in written.Warnings) ed.WriteMessage("\n  (!) " + w);

            if (settings.OpenOutputDrawing) OpenWhenIdle(written.Path);
        }

        private static ObjectId[] SelectEntities(Editor ed)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                ed.SetImpliedSelection(new ObjectId[0]);
                return implied.Value.GetObjectIds();
            }

            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = "\nGHOPHOI - chon cac chi tiet can ghep (hinh bao + text SL / vat lieu): "
            };
            PromptSelectionResult sel = ed.GetSelection(options);
            return sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds() : null;
        }

        private static List<OutputPart> BuildOutputParts(List<PartGroup> groups, NestReadResult read)
        {
            List<OutputPart> outputs = new List<OutputPart>();
            foreach (PartGroup g in groups)
            {
                RecognizedPart r = (RecognizedPart)g.SourceReference;
                Pt o = PartRecognizer.LocalOrigin(r);
                OutputPart op = new OutputPart { Group = g, Origin = new Point3d(o.X, o.Y, 0) };
                HashSet<ObjectId> seen = new HashSet<ObjectId>();
                foreach (int s in r.GeometrySources)
                {
                    ObjectId id = read.Sources[s].Id;
                    if (seen.Add(id)) op.SourceIds.Add(id);
                }

                outputs.Add(op);
            }

            return outputs;
        }

        internal static string BuildReport(NestingRequest request, NestingResult result)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            NestingStatistics st = result.Statistics;
            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>(StringComparer.Ordinal);
            foreach (PartGroup g in request.Groups) groups[g.Id] = g;

            sb.AppendLine("==================================================");
            sb.AppendLine(" GHOPHOI - KET QUA GHEP PHOI (V1)");
            sb.AppendLine("==================================================");
            sb.AppendLine(string.Format(ci, " Nhom chi tiet     : {0}", st.PartGroupCount));
            sb.AppendLine(string.Format(ci, " Tong SL yeu cau   : {0}", st.RequestedQuantity));
            sb.AppendLine(string.Format(ci, " Da xep            : {0}", st.PlacedQuantity));
            sb.AppendLine(string.Format(ci, " Chua xep          : {0}", st.UnplacedQuantity));
            sb.AppendLine(string.Format(ci, " So to phoi        : {0}", st.SheetCount));
            sb.AppendLine(string.Format(ci, " Khe cat / le mep  : {0:0.##} / {1:0.##} mm   (lat guong: {2})",
                request.Settings.GapMm, request.Settings.EdgeMarginMm, request.Settings.AllowMirror ? "CO" : "KHONG"));
            sb.AppendLine(" Chi tiet trong lo kin: " + (request.Settings.AllowPartInsideHole ? "CHO PHEP" : "KHONG (hoc lom ho van duoc phep)"));
            sb.AppendLine(string.Format(ci, " Thoi gian         : {0:0.0} s{1}", st.ElapsedSeconds, result.Cancelled ? "   (NGUOI DUNG DA DUNG SOM)" : string.Empty));

            foreach (MaterialStatistics m in st.Materials)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(ci, " [{0}]  kho {1}", m.Material, m.SheetName ?? "-"));
                sb.AppendLine(string.Format(ci, "   So to {0} | xep {1}/{2} | dai da dung {3:0} mm | su dung {4:0.0}%",
                    m.SheetCount, m.Placed, m.Requested, m.UsedLengthMm, m.Utilization * 100));
                sb.AppendLine(string.Format(ci, "   Phe lieu uoc tinh {0:0.###} m2 | phan du (dai cuoi to) {1:0.###} m2",
                    m.WasteAreaMm2 / 1e6, m.RemnantAreaMm2 / 1e6));
                sb.AppendLine(string.Format(ci, "   Da thu {0} thu tu, tot nhat: {1}{2}",
                    m.OrderingsTried, m.BestOrdering ?? "-", m.TimeBudgetHit ? "  (het thoi gian cho phep)" : string.Empty));

                foreach (SheetResult s in result.Sheets)
                {
                    if (s.Material != m.Material) continue;
                    sb.AppendLine(string.Format(ci, "   - To {0}: {1} chi tiet, dai dung {2:0} mm, su dung {3:0.0}% ({4:0.0}% ca to), phan du {5:0} mm",
                        s.NumberInMaterial, s.Placements.Count, s.UsedLengthMm, s.Utilization * 100, s.SheetUtilization * 100, s.RemnantLengthMm));
                }
            }

            sb.AppendLine();
            sb.AppendLine(" SO LUONG THEO CHI TIET (yeu cau = da xep + chua xep):");
            Dictionary<string, int> placed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Placement p in result.Placements)
            {
                int n;
                placed.TryGetValue(p.PartGroupId, out n);
                placed[p.PartGroupId] = n + 1;
            }

            Dictionary<string, List<string>> reasons = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, int> unplaced = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (UnplacedPart u in result.Unplaced)
            {
                int n;
                unplaced.TryGetValue(u.PartGroupId, out n);
                unplaced[u.PartGroupId] = n + 1;
                List<string> list;
                if (!reasons.TryGetValue(u.PartGroupId, out list))
                {
                    list = new List<string>();
                    reasons[u.PartGroupId] = list;
                }

                if (!list.Contains(u.Reason)) list.Add(u.Reason);
            }

            foreach (PartGroup g in request.Groups)
            {
                int p, u;
                placed.TryGetValue(g.Id, out p);
                unplaced.TryGetValue(g.Id, out u);
                sb.AppendLine(string.Format(ci, "   {0,-20} {1,-8} yeu cau {2,4} | xep {3,4} | chua xep {4,4}",
                    g.Name, g.Material, g.Quantity, p, u));
                List<string> why;
                if (reasons.TryGetValue(g.Id, out why))
                {
                    foreach (string w in why) sb.AppendLine("        ly do: " + w);
                }
            }

            sb.AppendLine();
            ValidationResult v = result.Validation;
            if (v != null && v.IsValid)
            {
                sb.AppendLine(" VALIDATOR: DAT");
                if (!double.IsNaN(v.MinPartDistanceMm)) sb.AppendLine(string.Format(ci, "   Khe nho nhat do duoc : {0:0.###} mm", v.MinPartDistanceMm));
                if (!double.IsNaN(v.MinEdgeDistanceMm)) sb.AppendLine(string.Format(ci, "   Cach mep nho nhat    : {0:0.###} mm", v.MinEdgeDistanceMm));
            }
            else
            {
                sb.AppendLine(" VALIDATOR: KHONG DAT - KHONG DUOC DUNG KET QUA NAY DE CAT!");
                if (v != null)
                {
                    int shown = 0;
                    foreach (ValidationIssue i in v.Issues)
                    {
                        if (shown++ >= 30)
                        {
                            sb.AppendLine("   ... va " + (v.Issues.Count - 30).ToString(ci) + " loi khac");
                            break;
                        }

                        sb.AppendLine("   (X) " + i);
                    }
                }
            }

            foreach (string w in result.Warnings) sb.AppendLine(" (!) " + w);
            sb.AppendLine("==================================================");
            return sb.ToString();
        }

        private static void SaveFixture(Editor ed, NestingRequest request, Database db)
        {
            try
            {
                Directory.CreateDirectory(GhoPhoiSettingsStore.FixtureFolder);
                string name = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(db.Filename) ? "Drawing" : db.Filename);
                string path = Path.Combine(GhoPhoiSettingsStore.FixtureFolder,
                    name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".nest");
                NestingFixture.Save(path, request, "exported from " + (db.Filename ?? "unsaved drawing"));
                ed.WriteMessage("\nGHOPHOI: Da luu fixture test: " + path);
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nGHOPHOI: (!) Khong luu duoc fixture: " + ex.Message);
            }
        }

        private static void ZoomTo(Editor ed, Database db, NestReadResult read, RecognizedPart r)
        {
            ClearHighlight(db);

            List<int> sources = new List<int>(r.GeometrySources);
            sources.AddRange(r.TextSources);
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (int s in sources)
                {
                    if (s < 0 || s >= read.Sources.Count) continue;
                    ObjectId id = read.Sources[s].Id;
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    ent.Highlight();
                    Highlighted.Add(id);
                }

                tr.Commit();
            }

            double w = Math.Max(r.Width, 1.0), h = Math.Max(r.Height, 1.0);
            using (ViewTableRecord view = ed.GetCurrentView())
            {
                view.CenterPoint = new Point2d((r.MinX + r.MaxX) * 0.5, (r.MinY + r.MaxY) * 0.5);
                double aspect = view.Width / Math.Max(view.Height, 1e-9);
                double height = Math.Max(h, w / Math.Max(aspect, 1e-9)) * 1.6;
                view.Height = height;
                view.Width = height * aspect;
                ed.SetCurrentView(view);
            }

            Application.UpdateScreen();
        }

        private static void ClearHighlight(Database db)
        {
            if (Highlighted.Count == 0) return;
            try
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (ObjectId id in Highlighted)
                    {
                        if (id.IsErased || !id.IsValid) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null) ent.Unhighlight();
                    }

                    tr.Commit();
                }
            }
            catch
            {
                // Highlight is cosmetic only.
            }

            Highlighted.Clear();
        }

        /// <summary>Opens the output after the command ended (document switching needs application context).</summary>
        private static void OpenWhenIdle(string path)
        {
            EventHandler handler = null;
            handler = (s, e) =>
            {
                Application.Idle -= handler;
                try
                {
                    Application.DocumentManager.Open(path, false);
                }
                catch (Exception ex)
                {
                    Document active = Application.DocumentManager.MdiActiveDocument;
                    if (active != null) active.Editor.WriteMessage("\nGHOPHOI: Khong tu mo duoc ban ve (" + ex.Message + "). Mo thu cong: " + path);
                }
            };
            Application.Idle += handler;
        }

        // ==================================================================================
        // GHOPHOI_TEST - same convention as DX_FOIL_TEST / ACC_AUTO_CUT_TEST
        // ==================================================================================
        [CommandMethod("GHOPHOI_TEST")]
        public void RunTests()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n GHOPHOI - PHAN A: CORE + NHAN DANG (khong AutoCAD)");
            ed.WriteMessage("\n==================================================");
            SelfTests.NestingTestReport core = SelfTests.NestingSelfTests.Run(GhoPhoiSettingsStore.FixtureFolder);
            foreach (string line in core.Lines) ed.WriteMessage("\n" + line);

            ed.WriteMessage("\n");
            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n GHOPHOI - PHAN B: TANG AUTOCAD (Database trong bo nho)");
            ed.WriteMessage("\n==================================================");
            SelfTests.NestingTestReport cad = GhoPhoiCadTests.Run();
            foreach (string line in cad.Lines) ed.WriteMessage("\n" + line);
        }
    }
}
