using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using WF = System.Windows.Forms;
using Exception = System.Exception;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // DX_FOIL - DAN PHOI TON CHAN TU POLYLINE BIEN DANG
    // ------------------------------------------------------------------------------------------
    // Quy trinh:
    //    DX_FOIL  ->  bang nhap lieu  ->  chon polyline bien dang  ->  phan tich
    //             ->  bang ket qua / xem truoc  ->  TAO PHOI  ->  chon diem dat
    //             ->  ve hinh chu nhat phoi + toan bo duong chan tren layer _mss.dut
    //
    // Kien truc (xem them tung file):
    //    FoilInputForm            giao dien nhap lieu
    //    FoilPolylineReader       AutoCAD Polyline  ->  FoilProfile (thuan toan hoc)
    //    FoilProfileAnalyzer      FoilProfile       ->  Flange / Bend (hinh hoc that)
    //    FoilFlatPatternCalculator  + IFoilBendStrategy  ->  chieu rong + vi tri duong chan
    //    FoilFlatPatternGeometry  bo cuc (U,V)      ->  toa do WCS (ke ca duong chan xien)
    //    FoilDrawingBuilder       toa do WCS        ->  entity AutoCAD
    //
    // AN TOAN: ban ve chi bi thay doi sau khi nguoi dung bam TAO PHOI. Moi loi deu Abort
    // transaction nen khong bao gio de lai hinh hoc rac.
    // ==========================================================================================
    public class FoilCommand
    {
        [CommandMethod("DX_FOIL")]
        [CommandMethod("DANPHOI")]
        public void DanPhoi()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // ---- BUOC 1: Bang nhap lieu ----
            FoilSettings settings = FoilSettingsStore.Load();
            List<string> layerNames = FoilDrawingBuilder.GetLayerNames(db);
            using (FoilInputForm inputForm = new FoilInputForm(settings, layerNames))
            {
                if (Application.ShowModalDialog(inputForm) != WF.DialogResult.OK)
                {
                    ed.WriteMessage("\nDX_FOIL: Da huy o bang nhap lieu.");
                    return;
                }

                settings = inputForm.GetSettings();
                FoilSettingsStore.Save(settings);
            }

            // ---- BUOC 2: Nap bang chan neu can ----
            FoilBendTable bendTable = null;
            if (settings.Method == FoilBendMethod.BendTable)
            {
                string path = string.IsNullOrWhiteSpace(settings.BendTablePath)
                    ? FoilSettingsStore.GetDefaultBendTablePath()
                    : settings.BendTablePath;

                string tableError;
                bendTable = FoilBendTable.LoadFromFile(path, out tableError);
                if (bendTable == null)
                {
                    ed.WriteMessage("\nDX_FOIL: " + tableError);
                    return;
                }

                ed.WriteMessage(string.Format(
                    CultureInfo.InvariantCulture,
                    "\nDX_FOIL: Da nap bang chan ({0} dong) tu {1}",
                    bendTable.Count, path));
            }

            // ---- BUOC 3: Chon polyline bien dang ----
            PromptEntityOptions entityOptions =
                new PromptEntityOptions("\nChon POLYLINE bien dang mat cat: ");
            entityOptions.SetRejectMessage(
                "\nDoi tuong khong phai POLYLINE. Hay chon mot polyline bien dang.");
            entityOptions.AddAllowedClass(typeof(Polyline), false);
            entityOptions.AddAllowedClass(typeof(Polyline2d), false);
            entityOptions.AddAllowedClass(typeof(Polyline3d), false);
            entityOptions.AllowNone = false;

            PromptEntityResult entityResult = ed.GetEntity(entityOptions);
            if (entityResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nDX_FOIL: Da huy - chua chon bien dang.");
                return;
            }

            // ---- BUOC 4: Doc va phan tich (CHI DOC - chua dong vao ban ve) ----
            FoilPolylineReadResult readResult;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                Entity entity = tr.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Entity;
                readResult = FoilPolylineReader.Read(entity, settings);
            }

            if (!readResult.Success)
            {
                ed.WriteMessage("\nDX_FOIL: Khong doc duoc bien dang:");
                foreach (string error in readResult.Errors)
                {
                    ed.WriteMessage("\n  (X) " + error);
                }

                return;
            }

            FoilProfileAnalysis analysis = FoilProfileAnalyzer.Analyze(readResult.Profile, settings);
            analysis.Notes.InsertRange(0, readResult.Notes);

            IFoilBendStrategy strategy = FoilStrategyFactory.Create(settings, bendTable);
            FoilFlatPatternResult result =
                FoilFlatPatternCalculator.Calculate(analysis, settings, strategy);

            if (result.HasErrors)
            {
                ed.WriteMessage("\nDX_FOIL: Khong trien khai duoc bien dang nay:");
                foreach (string error in result.Errors)
                {
                    ed.WriteMessage("\n  (X) " + error);
                }

                WF.MessageBox.Show(
                    "Khong trien khai duoc bien dang nay:" + Environment.NewLine + Environment.NewLine +
                    "  - " + string.Join(Environment.NewLine + "  - ", result.Errors.ToArray()),
                    "DX_FOIL",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return;
            }

            // ---- BUOC 5: Bang ket qua / xem truoc ----
            // Cho phep doi che do bu be day ngay tren bang ket qua: form goi lai ham nay de
            // tinh lai tu dau voi che do khac, nho vay nguoi dung thay ngay con so cua ca
            // 4 phuong an va chon dung, khong phai chay lai lenh.
            FoilBendTable tableForRecompute = bendTable;
            Func<FoilThicknessCompensationMode, FoilFlatPatternResult> recompute =
                delegate (FoilThicknessCompensationMode mode)
                {
                    FoilSettings variant = settings.Clone();
                    variant.ThicknessCompensation = mode;

                    FoilProfileAnalysis variantAnalysis =
                        FoilProfileAnalyzer.Analyze(readResult.Profile, variant);

                    return FoilFlatPatternCalculator.Calculate(
                        variantAnalysis,
                        variant,
                        FoilStrategyFactory.Create(variant, tableForRecompute));
                };

            using (FoilResultForm resultForm = new FoilResultForm(
                result, analysis.NormalizedProfile ?? readResult.Profile, recompute))
            {
                if (Application.ShowModalDialog(resultForm) != WF.DialogResult.OK)
                {
                    ed.WriteMessage("\nDX_FOIL: Da huy - ban ve giu nguyen 100%.");
                    return;
                }

                // Nguoi dung co the da doi che do bu be day ngay tren bang ket qua.
                result = resultForm.SelectedResult;
                settings = result.Settings ?? settings;
                FoilSettingsStore.Save(settings);
            }

            // ---- BUOC 6: Diem dat phoi ----
            double gap = Math.Max(result.BlankWidth * 0.25, 20.0);
            Point3d defaultPoint = new Point3d(
                readResult.MaxX + gap,
                readResult.MinY,
                readResult.Elevation);

            PromptPointOptions pointOptions = new PromptPointOptions(string.Format(
                CultureInfo.InvariantCulture,
                "\nChon diem dat phoi (goc duoi-trai) <Enter de dat canh bien dang tai {0:0.##},{1:0.##}>: ",
                defaultPoint.X, defaultPoint.Y));
            pointOptions.AllowNone = true;
            pointOptions.UseBasePoint = false;

            PromptPointResult pointResult = ed.GetPoint(pointOptions);
            Point3d insertion;

            if (pointResult.Status == PromptStatus.OK)
            {
                insertion = pointResult.Value;
            }
            else if (pointResult.Status == PromptStatus.None)
            {
                insertion = defaultPoint;
            }
            else
            {
                ed.WriteMessage("\nDX_FOIL: Da huy - ban ve giu nguyen 100%.");
                return;
            }

            // ---- BUOC 7: Ve ----
            FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                result,
                new FoilPoint2d(insertion.X, insertion.Y),
                settings.BlankRotationDeg * FoilMath.DegToRad);

            FoilDrawResult drawResult = null;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    drawResult = FoilDrawingBuilder.Draw(db, tr, result, geometry, insertion.Z);
                    tr.Commit();
                }
                catch (Exception ex)
                {
                    tr.Abort();
                    ed.WriteMessage("\nDX_FOIL: LOI khi tao hinh hoc - da huy toan bo, ban ve giu nguyen.");
                    ed.WriteMessage("\n  (X) " + ex.Message);

                    WF.MessageBox.Show(
                        "Loi khi tao hinh hoc, da huy bo toan bo thay doi:" +
                        Environment.NewLine + Environment.NewLine + ex.Message,
                        "DX_FOIL",
                        WF.MessageBoxButtons.OK,
                        WF.MessageBoxIcon.Error);
                    return;
                }
            }

            // ---- BUOC 8: Bao cao + zoom ----
            ReportSuccess(ed, result, drawResult, settings, geometry);

            if (settings.ZoomToResult && drawResult != null)
            {
                SelectAndZoom(ed, drawResult, geometry);
            }
        }

        [CommandMethod("DX_FOIL_SETTINGS")]
        public void FoilSettingsCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            FoilSettings settings = FoilSettingsStore.Load();
            using (FoilInputForm form = new FoilInputForm(
                settings, FoilDrawingBuilder.GetLayerNames(doc.Database)))
            {
                if (Application.ShowModalDialog(form) == WF.DialogResult.OK)
                {
                    FoilSettingsStore.Save(form.GetSettings());
                    doc.Editor.WriteMessage("\nDX_FOIL: Da luu cai dat.");
                }
            }
        }

        [CommandMethod("DX_FOIL_CALIB")]
        public void FoilCalibrationCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            FoilSettings settings = FoilSettingsStore.Load();
            using (FoilCalibrationForm form = new FoilCalibrationForm(settings))
            {
                if (Application.ShowModalDialog(form) == WF.DialogResult.OK && form.HasResult)
                {
                    settings.KFactor = FoilMath.Clamp(form.Result.KFactor, 0.0, 1.0);
                    settings.Method = FoilBendMethod.BendDeductionKFactor;
                    FoilSettingsStore.Save(settings);

                    doc.Editor.WriteMessage(string.Format(
                        CultureInfo.InvariantCulture,
                        "\nDX_FOIL: Da luu K-Factor hieu chuan = {0:0.#####}", settings.KFactor));
                }
            }
        }

        private static void ReportSuccess(
            Editor ed,
            FoilFlatPatternResult result,
            FoilDrawResult drawResult,
            FoilSettings settings,
            FoilFlatPatternGeometry geometry)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string f = settings.DisplayFormat;

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n DX_FOIL - DA TAO PHOI");
            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n Chieu dai phoi  : " + result.BlankLength.ToString(f, ci) + " mm");
            ed.WriteMessage("\n Chieu rong phoi : " + result.BlankWidth.ToString(f, ci) + " mm");
            ed.WriteMessage("\n Tong bu chan    : " + result.TotalDeduction.ToString(f, ci) + " mm");
            ed.WriteMessage(string.Format(
                ci,
                "\n Duong chan      : {0}  ({1} UP / {2} DOWN) tren layer \"{3}\"{4}",
                drawResult.BendLineIds.Count,
                drawResult.UpCount,
                drawResult.DownCount,
                settings.BendLayerName,
                drawResult.LayerCreated ? "  [layer vua duoc tao]" : string.Empty));
            ed.WriteMessage("\n Phuong phap     : " + result.MethodName);

            if (drawResult.CompensatedCount > 0)
            {
                ed.WriteMessage(string.Format(
                    ci,
                    "\n Bu be day       : {0} duong chan (goc lom) da duoc cong be day." +
                    "\n                   Neu bu nham phia, chay lai va tick \"Dao nguoc phia offset\".",
                    drawResult.CompensatedCount));
            }

            if (drawResult.StepCount > 0)
            {
                ed.WriteMessage(string.Format(
                    ci,
                    "\n Hinh buoc chan  : {0} buoc (B0 = phoi phang) tren layer \"{1}\"",
                    drawResult.StepCount - 1,
                    settings.StepLayerName));

                FoilBendPlan plan = geometry != null ? geometry.Plan : null;
                if (plan != null && plan.Order.Count > 0)
                {
                    List<string> names = new List<string>();
                    foreach (int index in plan.Order)
                    {
                        names.Add("#" + index.ToString(ci));
                    }

                    ed.WriteMessage(string.Format(
                        ci,
                        "\n Thu tu chan     : {0}   ({1})",
                        string.Join(" > ", names.ToArray()),
                        plan.OrderDescription));

                    ed.WriteMessage(string.Format(
                        ci,
                        "\n Lat ton         : {0} lan{1}",
                        plan.FlipCount,
                        plan.AllFeasible
                            ? string.Empty
                            : "   -- CO BUOC MO HINH MAY BAO KHONG CHAN DUOC"));

                    if (plan.Tooling != null)
                    {
                        ed.WriteMessage(string.Format(
                            ci,
                            "\n Dung cu         : coi V{0:0.##}  dao {1:0.#} do  canh nho nhat {2:0.##} mm",
                            plan.Tooling.DieOpening,
                            plan.Tooling.PunchIncludedAngleDeg,
                            plan.Tooling.MinFlangeLength));
                    }

                    foreach (string warning in plan.Warnings)
                    {
                        ed.WriteMessage("\n  (!) " + warning);
                    }
                }
            }

            foreach (string note in result.Notes)
            {
                ed.WriteMessage("\n  - " + note);
            }

            foreach (string warning in result.Warnings)
            {
                ed.WriteMessage("\n  (!) " + warning);
            }

            foreach (string warning in drawResult.Warnings)
            {
                ed.WriteMessage("\n  (!) " + warning);
            }

            ed.WriteMessage("\n==================================================");
        }

        private static void SelectAndZoom(
            Editor ed, FoilDrawResult drawResult, FoilFlatPatternGeometry geometry)
        {
            try
            {
                List<ObjectId> ids = drawResult.AllIds;
                if (ids.Count > 0)
                {
                    ed.SetImpliedSelection(ids.ToArray());
                }

                if (geometry.Outline.Count < 4)
                {
                    return;
                }

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (FoilPoint2d p in geometry.Outline)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }

                double width = Math.Max(maxX - minX, 1e-6) * 1.2;
                double height = Math.Max(maxY - minY, 1e-6) * 1.2;

                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    view.CenterPoint = new Point2d((minX + maxX) * 0.5, (minY + maxY) * 0.5);
                    view.Width = width;
                    view.Height = height;
                    ed.SetCurrentView(view);
                }
            }
            catch
            {
                // Zoom chi la tien ich - khong duoc lam hong ket qua da tao.
            }
        }
    }
}
