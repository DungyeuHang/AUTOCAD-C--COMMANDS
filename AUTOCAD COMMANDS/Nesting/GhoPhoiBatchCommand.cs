using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;
// accoreconsole chi co accoremgd (KHONG co acmgd), nen phai dung Core.Application -
// lop nay co trong CA hai host, AutoCAD day du lan accoreconsole.
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace AUTOCAD_COMMANDS.Nesting
{
    // ==========================================================================================
    // GHOPHOI_BATCH - CHAY GHOPHOI KHONG CAN NGUOI BAM (dung cho kiem thu ban ve THAT)
    // ------------------------------------------------------------------------------------------
    // Lenh GHOPHOI that co 4 hop thoai, nen khong the chay hang loat tren ban ve san xuat that.
    // Lenh nay chay DUNG NHUNG HAM DO - doc, nhan dang, ghep, kiem tra, ghi ban ve - chi bo
    // phan hop thoai, de co the goi bang accoreconsole tren hang tram ban ve that:
    //
    //     accoreconsole /i <ban_ve.dwg> /s <script.scr>
    //
    // KHONG phai ban sao cua thuat toan. Moi buoc goi thang vao code san xuat; neu code san
    // xuat sai thi o day cung sai y het - do chinh la muc dich.
    //
    // AN TOAN: chi DOC ban ve dang mo, khong bao gio SAVE. Moi ket qua ghi sang thu muc rieng
    // lay tu bien moi truong GHOPHOI_BATCH_OUT.
    //
    // Bien moi truong (deu khong bat buoc tru OUT):
    //     GHOPHOI_BATCH_OUT      thu muc ket qua (bat buoc)
    //     GHOPHOI_BATCH_GAP      khe cat mm            (mac dinh 5)
    //     GHOPHOI_BATCH_MARGIN   le mep mm             (mac dinh 5)
    //     GHOPHOI_BATCH_ROT      quarter | half | none (mac dinh quarter)
    //     GHOPHOI_BATCH_MIRROR   0 | 1                 (mac dinh 0)
    //     GHOPHOI_BATCH_INHOLE   0 | 1                 (mac dinh 0)
    //     GHOPHOI_BATCH_BUDGET   giay                  (mac dinh 30)
    //     GHOPHOI_BATCH_SEED     so nguyen             (mac dinh 1)
    //     GHOPHOI_BATCH_SHEET    <dai>x<rong>          (mac dinh 2500x1250)
    //     GHOPHOI_BATCH_DWG      0 = khong ghi ban ve moi (mac dinh 1)
    // ==========================================================================================
    public class GhoPhoiBatchCommand
    {
        [CommandMethod("GHOPHOI_BATCH", CommandFlags.Modal)]
        public void GhoPhoiBatch()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            string outFolder = Environment.GetEnvironmentVariable("GHOPHOI_BATCH_OUT");
            if (string.IsNullOrWhiteSpace(outFolder))
            {
                doc.Editor.WriteMessage("\nGHOPHOI_BATCH: thieu bien moi truong GHOPHOI_BATCH_OUT.");
                return;
            }

            string name = Path.GetFileNameWithoutExtension(
                string.IsNullOrEmpty(doc.Database.Filename) ? "Drawing" : doc.Database.Filename);

            StringBuilder log = new StringBuilder();
            try
            {
                Directory.CreateDirectory(outFolder);
                RunOne(doc, outFolder, name, log);
            }
            catch (Exception ex)
            {
                log.AppendLine("FATAL " + ex.GetType().Name + ": " + ex.Message);
                log.AppendLine(ex.StackTrace ?? string.Empty);
            }

            try
            {
                File.WriteAllText(Path.Combine(outFolder, name + ".report.txt"), log.ToString(), Encoding.UTF8);
            }
            catch
            {
                // best effort - the console output below is the fallback
            }

            doc.Editor.WriteMessage("\n" + log);
        }

        private static void RunOne(Document doc, string outFolder, string name, StringBuilder log)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            Database db = doc.Database;

            GhoPhoiSettings settings = BuildSettings();

            log.AppendLine("=== GHOPHOI_BATCH ===");
            log.AppendLine("DRAWING\t" + (db.Filename ?? "?"));
            log.AppendLine(string.Format(ci, "SETTINGS\tgap={0} margin={1} rot={2} mirror={3} inhole={4} budget={5} seed={6} tol={7}",
                settings.GapMm, settings.EdgeMarginMm, settings.RotationMode, settings.AllowMirror ? 1 : 0,
                settings.AllowPartInsideHole ? 1 : 0, settings.TimeBudgetSeconds, settings.Seed, settings.ArcToleranceMm));

            // ---- 1. "selection" = every entity in model space (worst case for recognition) ----
            Stopwatch swAll = Stopwatch.StartNew();
            Stopwatch sw = Stopwatch.StartNew();
            List<ObjectId> ids = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms) ids.Add(id);
            }

            log.AppendLine(string.Format(ci, "SELECTED\t{0}\t{1} ms", ids.Count, sw.ElapsedMilliseconds));
            if (ids.Count == 0)
            {
                log.AppendLine("RESULT\tEMPTY MODEL SPACE");
                return;
            }

            // ---- 2. read + recognise: EXACTLY the production calls ----
            sw.Restart();
            NestReadResult read;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                read = NestingSelectionReader.Read(tr, ids, settings);
            }

            RecognitionResult recognition =
                new PartRecognizer(settings.ToRecognitionSettings()).Recognize(read.Chains, read.Texts);
            recognition.GlobalWarnings.InsertRange(0, read.Warnings);
            recognition.IgnoredEntityCount = read.IgnoredCount;

            int unattached = 0;
            foreach (KeyValuePair<int, List<Pt>> marking in read.Markings)
            {
                if (!PartRecognizer.AttachMarking(recognition.Parts, marking.Key, marking.Value)) unattached++;
            }

            int unengraved = GhoPhoiPipeline.AttachEngravings(read, recognition, settings);

            long recogniseMs = sw.ElapsedMilliseconds;
            log.AppendLine(string.Format(ci, "READ\tsources={0}\tchains={1}\ttexts={2}\tmarkings={3}\tignored={4}\t{5} ms",
                read.Sources.Count, read.Chains.Count, read.Texts.Count, read.Markings.Count,
                read.IgnoredCount, recogniseMs));
            log.AppendLine(string.Format(ci,
                "RECOGNISED\tparts={0}\tunattachedMarkings={1}\tengravings={2}\tunattachedEngravings={3}",
                recognition.Parts.Count, unattached, read.Engravings.Count, unengraved));

            foreach (string w in recognition.GlobalWarnings) log.AppendLine("WARN\t" + w);

            // ---- 3. forensic dump of every recognised record ----
            int invalid = 0, ambiguous = 0, warning = 0, ok = 0;
            foreach (RecognizedPart r in recognition.Parts)
            {
                switch (r.Status)
                {
                    case PartStatus.InvalidGeometry: invalid++; break;
                    case PartStatus.Ambiguous: ambiguous++; break;
                    case PartStatus.Warning: warning++; break;
                    default: ok++; break;
                }

                log.AppendLine(string.Format(ci,
                    "PART\t{0}\tname={1}\tstatus={2}\tqty={3}{4}\tmat={5}{6}\touter={7}\tholes={8}\tmark={9}\tengrave={16}\ttext={10}\tbbox={11:0.###}x{12:0.###}\tat={13:0.###},{14:0.###}\tsrc={15}",
                    r.Index, r.Name, r.Status, r.Quantity, r.QuantityFromText ? "(text)" : "(default)",
                    r.Material, r.MaterialFromText ? "(text)" : "(default)",
                    r.Outer != null ? r.Outer.Points.Count : 0,
                    r.Holes.Count, r.MarkingSources.Count, r.TextSources.Count,
                    r.Width, r.Height, r.MinX, r.MinY, r.GeometrySources.Count,
                    r.EngravingSources.Count));

                foreach (string note in r.Notes) log.AppendLine("\tNOTE\t" + note);
            }

            log.AppendLine(string.Format(ci, "STATUS\tok={0}\twarning={1}\tambiguous={2}\tinvalid={3}",
                ok, warning, ambiguous, invalid));

            if (recognition.Parts.Count == 0)
            {
                log.AppendLine("RESULT\tNO PARTS RECOGNISED");
                return;
            }

            // ---- 4. what a careful user would do at the review table ----
            // Invalid geometry is never nested; ambiguous records are confirmed so the run can
            // proceed, and the count above keeps that visible.
            foreach (RecognizedPart r in recognition.Parts)
            {
                r.Include = r.IsNestable;
                if (r.Status == PartStatus.Ambiguous) r.Confirmed = true;
            }

            List<PartGroup> groups = PartRecognizer.ToPartGroups(recognition.Parts, settings.ArcToleranceMm);
            if (groups.Count == 0)
            {
                log.AppendLine("RESULT\tNO NESTABLE PARTS");
                return;
            }

            // ---- 5. request: one sheet size for every material ----
            NestingRequest request = new NestingRequest { Settings = settings.ToNestingSettings() };
            request.Groups.AddRange(groups);

            SheetSpec sheet = BuildSheet();
            SortedDictionary<string, int> materials = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (PartGroup g in groups)
            {
                int n;
                materials.TryGetValue(g.Material, out n);
                materials[g.Material] = n + g.Quantity;
                request.SheetByMaterial[g.Material] = new SheetSpec(sheet.Name, sheet.LengthMm, sheet.WidthMm);
            }

            foreach (KeyValuePair<string, int> m in materials)
            {
                log.AppendLine(string.Format(ci, "MATERIAL\t{0}\tqty={1}", m.Key, m.Value));
            }

            // Che do chi NHAN DANG: quet duoc nhieu ban ve that trong thoi gian ngan de san
            // loi nhan dang - phan ma fixture tong hop khong the phu duoc.
            if (string.Equals(Environment.GetEnvironmentVariable("GHOPHOI_BATCH_NEST"), "0", StringComparison.Ordinal))
            {
                log.AppendLine(string.Format(ci, "TOTAL\t{0} ms", swAll.ElapsedMilliseconds));
                log.AppendLine("RESULT\tRECOGNITION-ONLY");
                return;
            }

            // ---- 6. nest + validate (same engine, same validator) ----
            sw.Restart();
            NestingResult result = new SimpleNestingEngine().Nest(request, CancellationToken.None, null);
            long nestMs = sw.ElapsedMilliseconds;

            NestingStatistics st = result.Statistics;
            log.AppendLine(string.Format(ci,
                "NEST\tgroups={0}\trequested={1}\tplaced={2}\tunplaced={3}\tsheets={4}\t{5} ms",
                st.PartGroupCount, st.RequestedQuantity, st.PlacedQuantity, st.UnplacedQuantity,
                st.SheetCount, nestMs));

            ValidationResult v = result.Validation;
            log.AppendLine(string.Format(ci, "VALIDATOR\t{0}\tissues={1}\tminGap={2:0.####}\tminEdge={3:0.####}",
                v != null && v.IsValid ? "PASS" : "FAIL",
                v != null ? v.Issues.Count : -1,
                v != null ? v.MinPartDistanceMm : double.NaN,
                v != null ? v.MinEdgeDistanceMm : double.NaN));

            if (v != null)
            {
                foreach (ValidationIssue issue in v.Issues) log.AppendLine("\tISSUE\t" + issue.Kind + "\t" + issue.Message);
            }

            foreach (UnplacedPart u in result.Unplaced) log.AppendLine("\tUNPLACED\t" + u.PartGroupId + "\t" + u.Reason);

            // ---- 7. fixture (same writer as the "Luu fixture test" tick) ----
            try
            {
                string fixturePath = Path.Combine(outFolder, name + ".nest");
                NestingFixture.Save(fixturePath, request, "GHOPHOI_BATCH from " + (db.Filename ?? "?"));
                log.AppendLine("FIXTURE\t" + fixturePath);
            }
            catch (Exception ex)
            {
                log.AppendLine("FIXTURE-ERROR\t" + ex.Message);
            }

            File.WriteAllText(Path.Combine(outFolder, name + ".result.txt"),
                GhoPhoiPipeline.BuildReport(request, result), Encoding.UTF8);

            // ---- 8. output DWG (only when the validator passed, exactly like the real command) ----
            bool wantDwg = !string.Equals(Environment.GetEnvironmentVariable("GHOPHOI_BATCH_DWG"), "0", StringComparison.Ordinal);
            if (wantDwg && v != null && v.IsValid && st.PlacedQuantity > 0)
            {
                sw.Restart();
                List<OutputPart> outputs = GhoPhoiPipeline.BuildOutputParts(groups, read);
                string dwgPath = Path.Combine(outFolder, name + "_GHOPHOI.dwg");
                NestingDwgWriteResult written = NestingDwgWriter.Write(
                    db, dwgPath, request, result, outputs, settings, name);
                log.AppendLine(string.Format(ci, "DWG\t{0}\tblockrefs={1}\t{2} ms",
                    written.Path, written.BlockReferenceCount, sw.ElapsedMilliseconds));
                foreach (string w in written.Warnings) log.AppendLine("\tDWG-WARN\t" + w);
            }
            else if (wantDwg)
            {
                log.AppendLine("DWG\tSKIPPED (validator failed or nothing placed)");
            }

            log.AppendLine(string.Format(ci, "TOTAL\t{0} ms", swAll.ElapsedMilliseconds));
            log.AppendLine("RESULT\tOK");
        }

        /// <summary>
        /// Chay DUNG hai phan cua GHOPHOI_TEST (loi + nhan dang, roi tang AutoCAD) nhung ghi
        /// ket qua ra file thay vi ra dong lenh, de chay duoc trong accoreconsole.
        /// </summary>
        [CommandMethod("GHOPHOI_CADTEST", CommandFlags.Modal)]
        public void GhoPhoiCadTest()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            StringBuilder sb = new StringBuilder();
            int failed = 0;

            try
            {
                SelfTests.NestingTestReport core =
                    SelfTests.NestingSelfTests.Run(GhoPhoiSettingsStore.FixtureFolder);
                foreach (string line in core.Lines) sb.AppendLine(line);
                if (!core.AllPassed) failed++;

                sb.AppendLine();
                SelfTests.NestingTestReport cad = GhoPhoiCadTests.Run();
                foreach (string line in cad.Lines) sb.AppendLine(line);
                if (!cad.AllPassed) failed++;
            }
            catch (Exception ex)
            {
                failed++;
                sb.AppendLine("FATAL " + ex.GetType().Name + ": " + ex.Message);
                sb.AppendLine(ex.StackTrace ?? string.Empty);
            }

            sb.AppendLine(failed == 0 ? "CADTEST-RESULT ALL-PASSED" : "CADTEST-RESULT FAILED");

            string outFolder = Environment.GetEnvironmentVariable("GHOPHOI_BATCH_OUT");
            if (!string.IsNullOrWhiteSpace(outFolder))
            {
                try
                {
                    Directory.CreateDirectory(outFolder);
                    File.WriteAllText(Path.Combine(outFolder, "cadtest.txt"), sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // fall through to the console copy below
                }
            }

            doc.Editor.WriteMessage(Environment.NewLine + sb);
        }

        private static GhoPhoiSettings BuildSettings()
        {
            GhoPhoiSettings s = new GhoPhoiSettings
            {
                GapMm = Num("GHOPHOI_BATCH_GAP", 5.0),
                EdgeMarginMm = Num("GHOPHOI_BATCH_MARGIN", 5.0),
                AllowMirror = Flag("GHOPHOI_BATCH_MIRROR", false),
                AllowPartInsideHole = Flag("GHOPHOI_BATCH_INHOLE", false),
                TimeBudgetSeconds = Num("GHOPHOI_BATCH_BUDGET", 30.0),
                Seed = (int)Num("GHOPHOI_BATCH_SEED", 1.0),
                SaveFixture = true,
                OpenOutputDrawing = false
            };

            string rot = Environment.GetEnvironmentVariable("GHOPHOI_BATCH_ROT");
            if (string.Equals(rot, "half", StringComparison.OrdinalIgnoreCase)) s.RotationMode = GhoPhoiRotationMode.HalfTurns;
            else if (string.Equals(rot, "none", StringComparison.OrdinalIgnoreCase)) s.RotationMode = GhoPhoiRotationMode.None;
            else s.RotationMode = GhoPhoiRotationMode.QuarterTurns;

            return s;
        }

        private static SheetSpec BuildSheet()
        {
            string spec = Environment.GetEnvironmentVariable("GHOPHOI_BATCH_SHEET");
            double length = 2500.0, width = 1250.0;

            if (!string.IsNullOrWhiteSpace(spec))
            {
                string[] parts = spec.Split('x', 'X');
                double a, b;
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                {
                    length = a;
                    width = b;
                }
            }

            string name = string.Format(CultureInfo.InvariantCulture, "{0:0.##}x{1:0.##}", width, length);
            return new SheetSpec(name, length, width);
        }

        private static double Num(string variable, double fallback)
        {
            double value;
            string text = Environment.GetEnvironmentVariable(variable);
            return !string.IsNullOrWhiteSpace(text) &&
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static bool Flag(string variable, bool fallback)
        {
            string text = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            return text.Trim() == "1" || string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
