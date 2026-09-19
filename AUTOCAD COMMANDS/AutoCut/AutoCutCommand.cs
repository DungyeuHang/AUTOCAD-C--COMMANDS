using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using WF = System.Windows.Forms;

namespace AUTOCAD_COMMANDS
{
    public class AutoCutCommand
    {
        [CommandMethod("ACC_AUTO_CUT", CommandFlags.UsePickSet)]
        [CommandMethod("ACC", CommandFlags.UsePickSet)]
        public void AutoCut()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            AutoCutSettings settings = AutoCutSettingsStore.Load();

            // Bước 1: Hiển thị Settings Form (nếu được bật trong cấu hình)
            if (settings.ShowSettingsBeforeSelection)
            {
                using (AutoCutSettingsForm settingsForm = new AutoCutSettingsForm(settings, db))
                {
                    WF.DialogResult res = Application.ShowModalDialog(settingsForm);
                    if (res != WF.DialogResult.OK)
                    {
                        ed.WriteMessage("\nACC_AUTO_CUT: Đã thoát cài đặt.");
                        return;
                    }

                    settings = settingsForm.GetSettings();
                    AutoCutSettingsStore.Save(settings);
                }
            }

            // Bước 2: Chọn TARGET (User CHỈ CHỌN TARGET, KHÔNG chọn cutter)
            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nChọn các đối tượng TARGET cần cắt (LINE, PLINE, ARC, CIRCLE, ELLIPSE...): ",
                RejectObjectsFromNonCurrentSpace = true,
                AllowDuplicates = false
            };
            pso.Keywords.Add("Settings", "S", "Cài đặt (S)");
            pso.KeywordInput += (s, e) =>
            {
                using (AutoCutSettingsForm form = new AutoCutSettingsForm(settings, db))
                {
                    if (Application.ShowModalDialog(form) == WF.DialogResult.OK)
                    {
                        settings = form.GetSettings();
                        AutoCutSettingsStore.Save(settings);
                        ed.WriteMessage("\nACC_AUTO_CUT: Đã cập nhật cài đặt.");
                    }
                }
            };

            SelectionFilter filter = BuildTargetFilter(settings, db);
            PromptSelectionResult selRes = ed.GetSelection(pso, filter);

            if (selRes.Status != PromptStatus.OK || selRes.Value == null || selRes.Value.Count == 0)
            {
                ed.WriteMessage("\nACC_AUTO_CUT: Chưa chọn target nào.");
                return;
            }

            List<ObjectId> targetIds = selRes.Value.GetObjectIds().ToList();
            ed.WriteMessage($"\nACC_AUTO_CUT: Đã chọn {targetIds.Count} target(s). Đang tự động tìm Cutters...");

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            using (AutoCutPreviewTransient transientPreview = new AutoCutPreviewTransient())
            {
                // Bước 3: Tự động tìm Cutters & phân tích cắt
                AutoCutAnalysisResult analysis = AutoCutGeometryEngine.Analyze(db, tr, targetIds, settings);

                // Nếu không có đoạn nào cắt được, thông báo chẩn đoán ngay
                if (analysis.Report.SegmentsToRemoveCount == 0)
                {
                    ed.WriteMessage("\n[ACC_AUTO_CUT] Không tìm thấy đoạn nào để cắt. Chi tiết chẩn đoán:");
                    ed.WriteMessage("\n" + analysis.Report.FormatDiagnosticLog());
                    analysis.Dispose();
                    tr.Abort();
                    return;
                }

                // Bước 4: Hiển thị Transient Graphics Preview trên CAD Viewport
                transientPreview.DisplayPreview(analysis, ed);

                // Bước 5: Hiển thị Form Thống kê / Xem trước / Xác nhận
                using (AutoCutPreviewForm previewForm = new AutoCutPreviewForm(db, tr, ed, settings, analysis, transientPreview))
                {
                    WF.DialogResult dialogRes = Application.ShowModalDialog(previewForm);

                    // Xóa transient graphics ngay khi đóng form
                    transientPreview.Clear(ed);

                    if (dialogRes == WF.DialogResult.OK)
                    {
                        // User bấm APPLY: Thực hiện sửa database trong Transaction duy nhất
                        AutoCutAnalysisResult finalAnalysis = previewForm.ResultAnalysis;
                        AutoCutGeometryEngine.CommitChanges(tr, finalAnalysis, db);
                        tr.Commit();

                        ed.WriteMessage($"\n[ACC_AUTO_CUT] Thành công! Đã cắt bỏ {finalAnalysis.Report.SegmentsToRemoveCount} đoạn trên {finalAnalysis.Report.TargetsWithCutsCount} target(s). Tạo mới {finalAnalysis.Report.ObjectsToCreateCount} đối tượng.");
                    }
                    else
                    {
                        // User bấm CANCEL: Giữ nguyên bản vẽ 100%
                        tr.Abort();
                        ed.WriteMessage("\n[ACC_AUTO_CUT] Đã hủy bỏ. Bản vẽ được giữ nguyên 100%.");
                    }
                }
            }
        }

        [CommandMethod("ACC_AUTO_CUT_SETTINGS")]
        public void AutoCutSettingsCommand()
        {
            AutoCutSettings settings = AutoCutSettingsStore.Load();
            Database db = Application.DocumentManager.MdiActiveDocument?.Database;
            using (AutoCutSettingsForm form = new AutoCutSettingsForm(settings, db))
            {
                if (Application.ShowModalDialog(form) == WF.DialogResult.OK)
                {
                    AutoCutSettingsStore.Save(form.GetSettings());
                    Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                    ed?.WriteMessage("\nACC_AUTO_CUT: Đã lưu cài đặt.");
                }
            }
        }

        [CommandMethod("ACC_AUTO_CUT_TEST")]
        public void RunAutomatedTests()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n BẮT ĐẦU CHẠY BỘ KIỂM THỬ ACC_AUTO_CUT (17 TESTS)");
            ed.WriteMessage("\n==================================================");

            int passed = 0;
            int failed = 0;

            RunTest("TEST 1: LINE target + 2 LINE cutters => remove segment between", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Target: Vertical line from (10, 0) to (10, 100)
                    Line target = new Line(new Point3d(10, 0, 0), new Point3d(10, 100, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    // Cutter A: Horizontal at Y=30
                    Line cA = new Line(new Point3d(0, 30, 0), new Point3d(20, 30, 0));
                    btr.AppendEntity(cA);
                    tr.AddNewlyCreatedDBObject(cA, true);

                    // Cutter B: Horizontal at Y=70
                    Line cB = new Line(new Point3d(0, 70, 0), new Point3d(20, 70, 0));
                    btr.AppendEntity(cB);
                    tr.AddNewlyCreatedDBObject(cB, true);

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.BetweenIntersections };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 2, "IntersectionsCount must be 2");
                        Assert(result.Report.SegmentsToRemoveCount == 1, "SegmentsToRemoveCount must be 1");
                        Assert(result.Report.SegmentsToKeepCount == 2, "SegmentsToKeepCount must be 2");

                        var plan = result.TargetPlans[0];
                        var remPiece = plan.RemovePieces.First();
                        Assert(Math.Abs(remPiece.Length - 40.0) < 1e-4, "Removed segment length must be 40 (between 30 and 70)");
                    }
                }
            });

            RunTest("TEST 2: PLINE target + 2 LINE cutters", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Target Polyline: (0, 0) -> (100, 0)
                    Autodesk.AutoCAD.DatabaseServices.Polyline pl = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
                    ObjectId tId = btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    // Cutters at X=25 and X=75
                    Line c1 = new Line(new Point3d(25, -10, 0), new Point3d(25, 10, 0));
                    Line c2 = new Line(new Point3d(75, -10, 0), new Point3d(75, 10, 0));
                    btr.AppendEntity(c1); tr.AddNewlyCreatedDBObject(c1, true);
                    btr.AppendEntity(c2); tr.AddNewlyCreatedDBObject(c2, true);

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.BetweenIntersections };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.SegmentsToRemoveCount == 1, "Must remove 1 piece");
                        Assert(result.Report.SegmentsToKeepCount == 2, "Must keep 2 pieces");
                    }
                }
            });

            RunTest("TEST 3: PLINE có Bulge + cutters (Bảo toàn Bulge)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Polyline with arc segment (bulge = 1.0 -> semi-circle)
                    Autodesk.AutoCAD.DatabaseServices.Polyline pl = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    pl.AddVertexAt(0, new Point2d(0, 0), 1.0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(100, 0), 0.0, 0, 0);
                    ObjectId tId = btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    // Cutters crossing the arc
                    Line c1 = new Line(new Point3d(20, -50, 0), new Point3d(20, 100, 0));
                    Line c2 = new Line(new Point3d(80, -50, 0), new Point3d(80, 100, 0));
                    btr.AppendEntity(c1); tr.AddNewlyCreatedDBObject(c1, true);
                    btr.AppendEntity(c2); tr.AddNewlyCreatedDBObject(c2, true);

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.BetweenIntersections };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.SegmentsToRemoveCount == 1, "SegmentsToRemoveCount must be 1");
                        var keepPieces = result.TargetPlans[0].KeepPieces.ToList();
                        Assert(keepPieces.Count == 2, "KeepPieces must be 2");
                        // Verify split pieces remain Polylines with bulge preserved
                        foreach (var kp in keepPieces)
                        {
                            Assert(kp.Curve is Autodesk.AutoCAD.DatabaseServices.Polyline, "Split piece must be Polyline");
                        }
                    }
                }
            });

            RunTest("TEST 4: 4 Cutters => Multiple Intervals", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    // 4 Cutters at X=20, 40, 60, 80
                    for (int x = 20; x <= 80; x += 20)
                    {
                        Line c = new Line(new Point3d(x, -10, 0), new Point3d(x, 10, 0));
                        btr.AppendEntity(c); tr.AddNewlyCreatedDBObject(c, true);
                    }

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.BetweenIntersections };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 4, "IntersectionsCount must be 4");
                        Assert(result.Report.SegmentsToRemoveCount == 2, "SegmentsToRemoveCount must be 2 (intervals [20,40] and [60,80])");
                        Assert(result.Report.SegmentsToKeepCount == 3, "SegmentsToKeepCount must be 3");
                    }
                }
            });

            RunTest("TEST 5: Circle Cutter + Target => RemoveInside", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    // Circle center (50, 0), radius 20 -> intersects line at X=30 and X=70
                    Circle circle = new Circle(new Point3d(50, 0, 0), Vector3d.ZAxis, 20.0);
                    btr.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.ClosedCutterRemoveInside };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.SegmentsToRemoveCount == 1, "Must remove inside segment");
                        var rem = result.TargetPlans[0].RemovePieces.First();
                        Assert(Math.Abs(rem.MidPoint.X - 50.0) < 1e-4, "Removed midpoint must be around X=50");
                    }
                }
            });

            RunTest("TEST 6: Circle Cutter + Target => RemoveOutside", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    Circle circle = new Circle(new Point3d(50, 0, 0), Vector3d.ZAxis, 20.0);
                    btr.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.ClosedCutterRemoveOutside };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.SegmentsToRemoveCount == 2, "Must remove outside segments (before 30 and after 70)");
                        Assert(result.Report.SegmentsToKeepCount == 1, "Must keep interior segment [30, 70]");
                    }
                }
            });

            RunTest("TEST 7: Only 1 Intersection => Skip by Default", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    // 1 single cutter at X=50
                    Line c = new Line(new Point3d(50, -10, 0), new Point3d(50, 10, 0));
                    btr.AppendEntity(c); tr.AddNewlyCreatedDBObject(c, true);

                    AutoCutSettings s = new AutoCutSettings
                    {
                        CutMode = AutoCutCutMode.BetweenIntersections,
                        SingleIntersectionRule = AutoCutSingleIntersectionRule.Skip
                    };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.SegmentsToRemoveCount == 0, "SegmentsToRemoveCount must be 0 when SingleIntersection = Skip");
                        Assert(result.Report.SkippedSingleIntersectionCount == 1, "SkippedSingleIntersectionCount must be 1");
                    }
                }
            });

            RunTest("TEST 8: Same Layer/Color nhưng Object không Intersect", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    // Non-intersecting parallel line
                    Line nonIntersecting = new Line(new Point3d(0, 50, 0), new Point3d(100, 50, 0));
                    btr.AppendEntity(nonIntersecting); tr.AddNewlyCreatedDBObject(nonIntersecting, true);

                    AutoCutSettings s = new AutoCutSettings();
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 0, "IntersectionsCount must be 0");
                        Assert(result.Report.SegmentsToRemoveCount == 0, "Target must not be modified");
                    }
                }
            });

            RunTest("TEST 9: Different Layer Filtered According to Settings", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    CadLayerHelper.EnsureLayer(db, tr, "TargetLayer");
                    CadLayerHelper.EnsureLayer(db, tr, "OtherLayer");

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0)) { Layer = "TargetLayer" };
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    Line c1 = new Line(new Point3d(30, -10, 0), new Point3d(30, 10, 0)) { Layer = "OtherLayer" };
                    Line c2 = new Line(new Point3d(70, -10, 0), new Point3d(70, 10, 0)) { Layer = "OtherLayer" };
                    btr.AppendEntity(c1); tr.AddNewlyCreatedDBObject(c1, true);
                    btr.AppendEntity(c2); tr.AddNewlyCreatedDBObject(c2, true);

                    // Settings: SameAsTarget -> cutters on OtherLayer must be rejected
                    AutoCutSettings s = new AutoCutSettings { LayerFilterMode = AutoCutLayerFilterMode.SameAsTarget };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.RejectedByLayer == 2, "Both cutters must be rejected by layer filter");
                        Assert(result.Report.SegmentsToRemoveCount == 0, "No cuts made");
                    }
                }
            });

            RunTest("TEST 10: Multiple Targets Processed Independently", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line targetA = new Line(new Point3d(10, 0, 0), new Point3d(10, 100, 0));
                    Line targetB = new Line(new Point3d(50, 0, 0), new Point3d(50, 100, 0));
                    ObjectId tAId = btr.AppendEntity(targetA); tr.AddNewlyCreatedDBObject(targetA, true);
                    ObjectId tBId = btr.AppendEntity(targetB); tr.AddNewlyCreatedDBObject(targetB, true);

                    // Cutters crossing both targets
                    Line c1 = new Line(new Point3d(0, 30, 0), new Point3d(60, 30, 0));
                    Line c2 = new Line(new Point3d(0, 70, 0), new Point3d(60, 70, 0));
                    btr.AppendEntity(c1); tr.AddNewlyCreatedDBObject(c1, true);
                    btr.AppendEntity(c2); tr.AddNewlyCreatedDBObject(c2, true);

                    AutoCutSettings s = new AutoCutSettings { CutMode = AutoCutCutMode.BetweenIntersections };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tAId, tBId }, s))
                    {
                        Assert(result.TargetPlans.Count == 2, "Must analyze 2 targets");
                        Assert(result.Report.SegmentsToRemoveCount == 2, "Must remove 1 segment per target (total 2)");
                    }
                }
            });

            RunTest("TEST 11: Duplicate/Nearly Identical Intersections Deduplicated", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ObjectId tId = btr.AppendEntity(target);
                    tr.AddNewlyCreatedDBObject(target, true);

                    // Two cutters meeting at the exact same point (50, 0)
                    Line c1 = new Line(new Point3d(50, -10, 0), new Point3d(50, 10, 0));
                    Line c2 = new Line(new Point3d(40, -10, 0), new Point3d(60, 10, 0)); // Passes through (50,0)
                    btr.AppendEntity(c1); tr.AddNewlyCreatedDBObject(c1, true);
                    btr.AppendEntity(c2); tr.AddNewlyCreatedDBObject(c2, true);

                    AutoCutSettings s = new AutoCutSettings { SingleIntersectionRule = AutoCutSingleIntersectionRule.Skip };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.DuplicateIntersectionsCount == 1, "Must detect 1 duplicate intersection");
                        Assert(result.Report.IntersectionsCount == 1, "Must have 1 unique intersection");
                    }
                }
            });

            RunTest("TEST 12: Cancel Preview => Drawing Unchanged (Database Rollback)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                {
                    ObjectId tId;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        Line target = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                        tId = btr.AppendEntity(target);
                        tr.AddNewlyCreatedDBObject(target, true);

                        Line c1 = new Line(new Point3d(30, -10, 0), new Point3d(30, 10, 0));
                        Line c2 = new Line(new Point3d(70, -10, 0), new Point3d(70, 10, 0));
                        btr.AppendEntity(c1); tr.AddNewlyCreatedDBObject(c1, true);
                        btr.AppendEntity(c2); tr.AddNewlyCreatedDBObject(c2, true);
                        tr.Commit();
                    }

                    // Simulate user canceling in preview
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        AutoCutSettings s = new AutoCutSettings();
                        using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                        {
                            // User clicked CANCEL:
                            tr.Abort(); // Database rolled back
                        }
                    }

                    // Verify target is still intact in database
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        DBObject obj = tr.GetObject(tId, OpenMode.ForRead);
                        Assert(obj != null && !obj.IsErased, "Target must remain intact and not erased after Cancel");
                    }
                }
            });

            RunTest("TEST 13: CIRCLE target + Overlapping CIRCLE cutter (Venn diagram cut)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Target Circle: center (0,0), radius 50
                    Circle targetCircle = new Circle(Point3d.Origin, Vector3d.ZAxis, 50.0);
                    ObjectId tId = btr.AppendEntity(targetCircle);
                    tr.AddNewlyCreatedDBObject(targetCircle, true);

                    // Cutter Circle: center (50, 0), radius 50
                    Circle cutterCircle = new Circle(new Point3d(50, 0, 0), Vector3d.ZAxis, 50.0);
                    btr.AppendEntity(cutterCircle);
                    tr.AddNewlyCreatedDBObject(cutterCircle, true);

                    AutoCutSettings s = new AutoCutSettings
                    {
                        TargetTypes = AutoCutTargetTypeFlags.AllCurves,
                        CutMode = AutoCutCutMode.ClosedCutterRemoveInside
                    };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 2, "2 circles must intersect at 2 points");
                        Assert(result.Report.SegmentsToRemoveCount == 1, "Must remove the inside arc");
                        Assert(result.Report.SegmentsToKeepCount == 1, "Must keep the outside arc");

                        var plan = result.TargetPlans[0];
                        var remArc = plan.RemovePieces.First().Curve as Arc;
                        Assert(remArc != null, "Resulting pieces of split circle must be Arc");
                    }
                }
            });

            RunTest("TEST 14: Closed Polyline target + U-shaped notch cutters", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Closed rectangular Polyline (0,0) to (100,200)
                    Autodesk.AutoCAD.DatabaseServices.Polyline rect = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    rect.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    rect.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
                    rect.AddVertexAt(2, new Point2d(100, 200), 0, 0, 0);
                    rect.AddVertexAt(3, new Point2d(0, 200), 0, 0, 0);
                    rect.Closed = true;
                    ObjectId tId = btr.AppendEntity(rect);
                    tr.AddNewlyCreatedDBObject(rect, true);

                    // U-shaped cutter at top edge (crossing top edge at X=40 and X=60)
                    Autodesk.AutoCAD.DatabaseServices.Polyline notchTop = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    notchTop.AddVertexAt(0, new Point2d(40, 210), 0, 0, 0);
                    notchTop.AddVertexAt(1, new Point2d(40, 190), 0, 0, 0);
                    notchTop.AddVertexAt(2, new Point2d(60, 190), 0, 0, 0);
                    notchTop.AddVertexAt(3, new Point2d(60, 210), 0, 0, 0);
                    notchTop.Closed = false;
                    btr.AppendEntity(notchTop);
                    tr.AddNewlyCreatedDBObject(notchTop, true);

                    AutoCutSettings s = new AutoCutSettings
                    {
                        TargetTypes = AutoCutTargetTypeFlags.AllCurves,
                        CutMode = AutoCutCutMode.BetweenIntersections
                    };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 2, "Notch must intersect top edge at 2 points");
                        Assert(result.Report.SegmentsToRemoveCount == 1, "Must remove notch segment");
                        var remPiece = result.TargetPlans[0].RemovePieces.First();
                        Assert(Math.Abs(remPiece.Length - 20.0) < 1e-3, "Removed notch segment length must be 20");
                    }
                }
            });

            RunTest("TEST 15: Closed Polyline target + 4 EQUAL notch cutters with RemoveShortest (must remove all 4 notches)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Sheet rectangle: (0,0) to (1000, 500)
                    Autodesk.AutoCAD.DatabaseServices.Polyline sheet = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    sheet.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    sheet.AddVertexAt(1, new Point2d(1000, 0), 0, 0, 0);
                    sheet.AddVertexAt(2, new Point2d(1000, 500), 0, 0, 0);
                    sheet.AddVertexAt(3, new Point2d(0, 500), 0, 0, 0);
                    sheet.Closed = true;
                    ObjectId tId = btr.AppendEntity(sheet);
                    tr.AddNewlyCreatedDBObject(sheet, true);

                    // 4 identical notch cutters (20mm wide each)
                    // Notch 1: bottom edge at X=200..220
                    Autodesk.AutoCAD.DatabaseServices.Polyline n1 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n1.AddVertexAt(0, new Point2d(200, -10), 0, 0, 0);
                    n1.AddVertexAt(1, new Point2d(200, 10), 0, 0, 0);
                    n1.AddVertexAt(2, new Point2d(220, 10), 0, 0, 0);
                    n1.AddVertexAt(3, new Point2d(220, -10), 0, 0, 0);
                    btr.AppendEntity(n1);
                    tr.AddNewlyCreatedDBObject(n1, true);

                    // Notch 2: bottom edge at X=800..820
                    Autodesk.AutoCAD.DatabaseServices.Polyline n2 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n2.AddVertexAt(0, new Point2d(800, -10), 0, 0, 0);
                    n2.AddVertexAt(1, new Point2d(800, 10), 0, 0, 0);
                    n2.AddVertexAt(2, new Point2d(820, 10), 0, 0, 0);
                    n2.AddVertexAt(3, new Point2d(820, -10), 0, 0, 0);
                    btr.AppendEntity(n2);
                    tr.AddNewlyCreatedDBObject(n2, true);

                    // Notch 3: top edge at X=200..220
                    Autodesk.AutoCAD.DatabaseServices.Polyline n3 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n3.AddVertexAt(0, new Point2d(200, 510), 0, 0, 0);
                    n3.AddVertexAt(1, new Point2d(200, 490), 0, 0, 0);
                    n3.AddVertexAt(2, new Point2d(220, 490), 0, 0, 0);
                    n3.AddVertexAt(3, new Point2d(220, 510), 0, 0, 0);
                    btr.AppendEntity(n3);
                    tr.AddNewlyCreatedDBObject(n3, true);

                    // Notch 4: top edge at X=800..820
                    Autodesk.AutoCAD.DatabaseServices.Polyline n4 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n4.AddVertexAt(0, new Point2d(800, 510), 0, 0, 0);
                    n4.AddVertexAt(1, new Point2d(800, 490), 0, 0, 0);
                    n4.AddVertexAt(2, new Point2d(820, 490), 0, 0, 0);
                    n4.AddVertexAt(3, new Point2d(820, 510), 0, 0, 0);
                    btr.AppendEntity(n4);
                    tr.AddNewlyCreatedDBObject(n4, true);

                    AutoCutSettings s = new AutoCutSettings
                    {
                        TargetTypes = AutoCutTargetTypeFlags.AllCurves,
                        CutMode = AutoCutCutMode.RemoveShortest
                    };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 8, "Must find 8 intersections");
                        Assert(result.Report.SegmentsToRemoveCount == 4, $"RemoveShortest must remove ALL 4 equal notches, but got {result.Report.SegmentsToRemoveCount}");
                        Assert(result.Report.SegmentsToKeepCount == 4, "Must keep 4 workpiece perimeter segments");
                        foreach (var rem in result.TargetPlans[0].RemovePieces)
                        {
                            Assert(Math.Abs(rem.Length - 20.0) < 1e-2, $"Each removed notch length must be 20, got {rem.Length}");
                        }
                    }
                }
            });

            RunTest("TEST 16: Closed Polyline target + 4 notch cutters with BetweenIntersections (Workpiece Preservation Rule)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Sheet rectangle: (0,0) to (1000, 500)
                    Autodesk.AutoCAD.DatabaseServices.Polyline sheet = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    sheet.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    sheet.AddVertexAt(1, new Point2d(1000, 0), 0, 0, 0);
                    sheet.AddVertexAt(2, new Point2d(1000, 500), 0, 0, 0);
                    sheet.AddVertexAt(3, new Point2d(0, 500), 0, 0, 0);
                    sheet.Closed = true;
                    ObjectId tId = btr.AppendEntity(sheet);
                    tr.AddNewlyCreatedDBObject(sheet, true);

                    // 4 identical notch cutters (20mm wide each)
                    Autodesk.AutoCAD.DatabaseServices.Polyline n1 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n1.AddVertexAt(0, new Point2d(200, -10), 0, 0, 0);
                    n1.AddVertexAt(1, new Point2d(200, 10), 0, 0, 0);
                    n1.AddVertexAt(2, new Point2d(220, 10), 0, 0, 0);
                    n1.AddVertexAt(3, new Point2d(220, -10), 0, 0, 0);
                    btr.AppendEntity(n1);
                    tr.AddNewlyCreatedDBObject(n1, true);

                    Autodesk.AutoCAD.DatabaseServices.Polyline n2 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n2.AddVertexAt(0, new Point2d(800, -10), 0, 0, 0);
                    n2.AddVertexAt(1, new Point2d(800, 10), 0, 0, 0);
                    n2.AddVertexAt(2, new Point2d(820, 10), 0, 0, 0);
                    n2.AddVertexAt(3, new Point2d(820, -10), 0, 0, 0);
                    btr.AppendEntity(n2);
                    tr.AddNewlyCreatedDBObject(n2, true);

                    Autodesk.AutoCAD.DatabaseServices.Polyline n3 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n3.AddVertexAt(0, new Point2d(200, 510), 0, 0, 0);
                    n3.AddVertexAt(1, new Point2d(200, 490), 0, 0, 0);
                    n3.AddVertexAt(2, new Point2d(220, 490), 0, 0, 0);
                    n3.AddVertexAt(3, new Point2d(220, 510), 0, 0, 0);
                    btr.AppendEntity(n3);
                    tr.AddNewlyCreatedDBObject(n3, true);

                    Autodesk.AutoCAD.DatabaseServices.Polyline n4 = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    n4.AddVertexAt(0, new Point2d(800, 510), 0, 0, 0);
                    n4.AddVertexAt(1, new Point2d(800, 490), 0, 0, 0);
                    n4.AddVertexAt(2, new Point2d(820, 490), 0, 0, 0);
                    n4.AddVertexAt(3, new Point2d(820, 510), 0, 0, 0);
                    btr.AppendEntity(n4);
                    tr.AddNewlyCreatedDBObject(n4, true);

                    AutoCutSettings s = new AutoCutSettings
                    {
                        TargetTypes = AutoCutTargetTypeFlags.AllCurves,
                        CutMode = AutoCutCutMode.BetweenIntersections
                    };
                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { tId }, s))
                    {
                        Assert(result.Report.IntersectionsCount == 8, "Must find 8 intersections");
                        Assert(result.Report.SegmentsToRemoveCount == 4, $"Workpiece Preservation Rule must remove ALL 4 notches, got {result.Report.SegmentsToRemoveCount}");
                        Assert(result.Report.SegmentsToKeepCount == 4, "Must keep 4 workpiece perimeter segments");
                        double removedLen = result.TargetPlans[0].RemovePieces.Sum(p => p.Length);
                        double keepLen = result.TargetPlans[0].KeepPieces.Sum(p => p.Length);
                        Assert(Math.Abs(removedLen - 80.0) < 1e-1, $"Total removed notches length must be 80, got {removedLen}");
                        Assert(Math.Abs(keepLen - 2920.0) < 1e-1, $"Total kept workpiece length must be 2920, got {keepLen}");
                    }
                }
            });

            RunTest("TEST 17: Target Layer Filtering (Specific Layer match & reject other layers)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

                    // Create custom layers
                    LayerTableRecord ltrTarget = new LayerTableRecord { Name = "BIEN_DANG" };
                    lt.Add(ltrTarget);
                    tr.AddNewlyCreatedDBObject(ltrTarget, true);

                    LayerTableRecord ltrOther = new LayerTableRecord { Name = "NET_KHUAT" };
                    lt.Add(ltrOther);
                    tr.AddNewlyCreatedDBObject(ltrOther, true);

                    // Target 1 on "BIEN_DANG"
                    Line target1 = new Line(new Point3d(0, 50, 0), new Point3d(100, 50, 0)) { Layer = "BIEN_DANG" };
                    ObjectId t1Id = btr.AppendEntity(target1);
                    tr.AddNewlyCreatedDBObject(target1, true);

                    // Target 2 on "NET_KHUAT"
                    Line target2 = new Line(new Point3d(0, 150, 0), new Point3d(100, 150, 0)) { Layer = "NET_KHUAT" };
                    ObjectId t2Id = btr.AppendEntity(target2);
                    tr.AddNewlyCreatedDBObject(target2, true);

                    // Cutter intersecting both lines at X=30 and X=70
                    Line c1 = new Line(new Point3d(30, 0, 0), new Point3d(30, 200, 0)) { Layer = "BIEN_DANG" };
                    btr.AppendEntity(c1);
                    tr.AddNewlyCreatedDBObject(c1, true);

                    Line c2 = new Line(new Point3d(70, 0, 0), new Point3d(70, 200, 0)) { Layer = "BIEN_DANG" };
                    btr.AppendEntity(c2);
                    tr.AddNewlyCreatedDBObject(c2, true);

                    AutoCutSettings s = new AutoCutSettings
                    {
                        TargetTypes = AutoCutTargetTypeFlags.AllCurves,
                        TargetLayerFilterMode = AutoCutTargetLayerFilterMode.SpecificLayer,
                        TargetLayerName = "BIEN_DANG",
                        LayerFilterMode = AutoCutLayerFilterMode.AnyLayer,
                        CutMode = AutoCutCutMode.BetweenIntersections
                    };

                    // Verify SelectionFilter generation
                    SelectionFilter filter = BuildTargetFilter(s, db);
                    Assert(filter != null && filter.GetFilter().Length >= 2, "Selection filter must contain Start and LayerName");

                    using (var result = AutoCutGeometryEngine.Analyze(db, tr, new[] { t1Id, t2Id }, s))
                    {
                        Assert(result.Report.TargetsCount == 2, "2 targets provided");
                        Assert(result.Report.TargetsWithCutsCount == 1, "Only target on BIEN_DANG must be cut");
                        Assert(result.Report.RejectedByLayer >= 1, "Target on NET_KHUAT must be rejected by layer");
                        Assert(result.TargetPlans.Count == 1, "TargetPlans must contain only the valid target");
                        Assert(result.TargetPlans[0].TargetId == t1Id, "Target on BIEN_DANG must be the one processed");
                        Assert(result.Report.SegmentsToRemoveCount == 1, "Must cut 1 segment between X=30 and X=70 on target 1");
                    }
                }
            });

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage($"\n KẾT QUẢ KIỂM THỬ: {passed} PASS / {failed} FAIL (Tổng cộng: {passed + failed})");
            ed.WriteMessage("\n==================================================");
        }

        private static void RunTest(string testName, ref int passed, ref int failed, Editor ed, Action testAction)
        {
            try
            {
                testAction();
                passed++;
                ed.WriteMessage($"\n[PASS] {testName}");
            }
            catch (System.Exception ex)
            {
                failed++;
                ed.WriteMessage($"\n[FAIL] {testName} -> {ex.Message}");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + message);
            }
        }

        private static SelectionFilter BuildTargetFilter(AutoCutSettings settings, Database db)
        {
            List<TypedValue> values = new List<TypedValue>();

            List<string> types = new List<string>();
            var targetTypes = settings.TargetTypes;
            if (targetTypes.HasFlag(AutoCutTargetTypeFlags.Line)) types.Add("LINE");
            if (targetTypes.HasFlag(AutoCutTargetTypeFlags.Polyline)) { types.Add("LWPOLYLINE"); types.Add("POLYLINE"); }
            if (targetTypes.HasFlag(AutoCutTargetTypeFlags.Arc)) types.Add("ARC");
            if (targetTypes.HasFlag(AutoCutTargetTypeFlags.Circle)) types.Add("CIRCLE");
            if (targetTypes.HasFlag(AutoCutTargetTypeFlags.Ellipse)) types.Add("ELLIPSE");
            if (targetTypes.HasFlag(AutoCutTargetTypeFlags.Spline)) types.Add("SPLINE");

            if (types.Count == 0)
            {
                types.Add("LINE");
                types.Add("LWPOLYLINE");
                types.Add("POLYLINE");
                types.Add("ARC");
                types.Add("CIRCLE");
                types.Add("ELLIPSE");
                types.Add("SPLINE");
            }

            string filterStr = string.Join(",", types);
            values.Add(new TypedValue((int)DxfCode.Start, filterStr));

            if (settings.TargetLayerFilterMode == AutoCutTargetLayerFilterMode.CurrentLayer && db != null)
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    LayerTableRecord ltr = tr.GetObject(db.Clayer, OpenMode.ForRead) as LayerTableRecord;
                    if (ltr != null)
                    {
                        values.Add(new TypedValue((int)DxfCode.LayerName, ltr.Name));
                    }
                }
            }
            else if (settings.TargetLayerFilterMode == AutoCutTargetLayerFilterMode.SpecificLayer && !string.IsNullOrWhiteSpace(settings.TargetLayerName))
            {
                values.Add(new TypedValue((int)DxfCode.LayerName, settings.TargetLayerName.Trim()));
            }

            return new SelectionFilter(values.ToArray());
        }
    }
}
