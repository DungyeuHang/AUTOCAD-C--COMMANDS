using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WF = System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // DPA - AUTO DIM PLINE
    // ------------------------------------------------------------------------------------------
    // Luong chay: BANG CAI DAT -> CHON PLINE -> PHAN TICH -> BO TRI -> TAO DIM.
    //
    // Nguyen tac ANALYZE -> PLAN -> CREATE: toan bo va cham va xep chong duoc giai quyet trong
    // bo nho TRUOC khi tao entity dau tien. Khong co chuyen tao ra roi nhin roi va lai.
    //
    // Lenh KHONG BAO GIO sua polyline goc (khong dao chieu, khong doi dinh) va cung khong sua
    // DIMSTYLE cua ban ve - chi DIMLFAC duoc ghi de rieng tren tung dim moi tao.
    // ==========================================================================================
    public class AutoDimPlineCommand
    {
        [CommandMethod("DPA_DimAutoPline")]
        public void DimAutoPline()
        {
            Run();
        }

        private void Run()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            const string commandName = "DPA_DimAutoPline";

            AutoDimPlineSettings settings = AutoDimPlineSettingsStore.Load();

            // 1. Bang cai dat truoc, de con chon layer / scale roi moi pick.
            using (AutoDimPlineSettingsForm form = new AutoDimPlineSettingsForm(settings, db))
            {
                if (Application.ShowModalDialog(form) != WF.DialogResult.OK)
                {
                    ed.WriteMessage("\n" + commandName + ": da huy.");
                    return;
                }

                settings = form.GetSettings();
                AutoDimPlineSettingsStore.Save(settings);
            }

            // 2. Chon polyline.
            PromptEntityOptions options = new PromptEntityOptions("\nChon Polyline de tao dim tu dong: ");
            options.SetRejectMessage("\nChi ho tro Polyline (LWPOLYLINE).");
            options.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult picked = ed.GetEntity(options);
            if (picked.Status != PromptStatus.OK)
            {
                return;
            }

            // 3. Mot transaction duy nhat. Bat ky loi nao truoc Commit -> khong commit ->
            //    ban ve khong con lai dim rac nao.
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Polyline mo ForRead: lenh khong sua hinh hoc goc.
                    Polyline polyline = tr.GetObject(picked.ObjectId, OpenMode.ForRead) as Polyline;
                    if (polyline == null)
                    {
                        ed.WriteMessage("\n" + commandName + ": khong doc duoc Polyline.");
                        return;
                    }

                    DimPlineReadResult read = DimPlineCadReader.Read(polyline);
                    if (!read.Success)
                    {
                        ed.WriteMessage("\n" + commandName + ": " + read.ErrorMessage);
                        return;
                    }

                    DimPlineStyle style = DimPlineCadReader.ReadStyle(db);
                    DimPlinePlan plan = DimPlinePlanner.Plan(read.Input, settings, style);

                    if (!plan.IsValid)
                    {
                        ed.WriteMessage("\n" + commandName + ": " + plan.ErrorMessage);
                        return;
                    }

                    if (plan.Placements.Count == 0)
                    {
                        ed.WriteMessage("\n" + commandName + ": khong co kich thuoc nao can tao. " +
                                        BuildSkipSummary(plan, settings));
                        return;
                    }

                    ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, settings.DimensionLayer);

                    BlockTableRecord space =
                        tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    if (space == null)
                    {
                        ed.WriteMessage("\n" + commandName + ": khong mo duoc khong gian ve hien hanh.");
                        return;
                    }

                    DimPlineCreateResult created = DimPlineCadCreator.Create(
                        tr, db, space, layerId, plan, settings, read.Elevation);

                    if (!created.Success)
                    {
                        // Khong Commit -> Dispose se Abort -> khong dim nao con lai tren ban ve.
                        ed.WriteMessage("\n" + commandName + ": loi khi tao dim (" + created.ErrorMessage +
                                        "). Da huy toan bo, ban ve khong bi thay doi.");
                        return;
                    }

                    tr.Commit();

                    // Dat Verbose = true trong file cai dat (autodimpline_settings.tsv)
                    // de in them ban ke hoach bo tri ra dong lenh.
                    if (settings.Verbose)
                    {
                        foreach (string line in plan.Report)
                        {
                            ed.WriteMessage("\n" + line);
                        }
                    }

                    ed.WriteMessage("\n" + BuildSummary(plan, settings, created.CreatedIds.Count));
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\n" + commandName + ": loi khong mong doi (" + ex.Message +
                                    "). Da huy toan bo, ban ve khong bi thay doi.");
                }
            }

            try
            {
                ed.Regen();
            }
            catch (System.Exception)
            {
                // Regen that bai khong anh huong den ket qua da tao.
            }
        }

        private static string BuildSummary(
            DimPlinePlan plan,
            AutoDimPlineSettings settings,
            int createdCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("DPA: ");
            sb.Append("da tao ").Append(createdCount).Append(" dim tren layer '")
              .Append(settings.DimensionLayer).Append("'");

            sb.Append(" (").Append(plan.NearFeatureCount).Append(" dat sat feature, ")
              .Append(plan.Placements.Count - plan.NearFeatureCount).Append(" o bang ngoai)");

            sb.Append(". Linear scale ")
              .Append(settings.LinearScale.ToString("0.######", CultureInfo.InvariantCulture))
              .Append(".");

            string skipped = BuildSkipSummary(plan, settings);
            if (skipped.Length > 0)
            {
                sb.Append(" ").Append(skipped);
            }

            return sb.ToString();
        }

        private static string BuildSkipSummary(DimPlinePlan plan, AutoDimPlineSettings settings)
        {
            List<string> parts = new List<string>();

            if (plan.SkippedArcs > 0)
            {
                parts.Add(plan.SkippedArcs + " doan cung bi bo qua (bat 'Arc segments: Radius dimension' neu muon dim)");
            }

            if (plan.SkippedSkew > 0)
            {
                parts.Add(plan.SkippedSkew + " doan xien bi bo qua (bat 'Skew segments: Aligned dimension' neu muon dim)");
            }

            if (plan.SkippedTooShort > 0)
            {
                parts.Add(plan.SkippedTooShort + " doan ngan hon " +
                          settings.MinSegmentLength.ToString("0.######", CultureInfo.InvariantCulture));
            }

            if (plan.SkippedDuplicate > 0)
            {
                parts.Add(plan.SkippedDuplicate + " kich thuoc trung lap");
            }

            if (plan.DroppedZeroLength > 0)
            {
                parts.Add(plan.DroppedZeroLength + " doan dai 0");
            }

            return parts.Count == 0 ? string.Empty : "Bo qua: " + string.Join(", ", parts.ToArray()) + ".";
        }
    }
}
