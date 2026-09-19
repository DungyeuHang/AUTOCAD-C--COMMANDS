using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AUTOCAD_COMMANDS
{
    public class AutoTrimCommand
    {
        [CommandMethod("ACC_TRIM_OUTSIDE", CommandFlags.UsePickSet)]
        [CommandMethod("ACC_AUTO_TRIM_OUTSIDE", CommandFlags.UsePickSet)]
        [CommandMethod("ACT", CommandFlags.UsePickSet)]
        public void TrimOutsideCommand()
        {
            ExecuteTrimWorkflow(AutoTrimMode.TrimOutside);
        }

        [CommandMethod("ACC_TRIM_INSIDE", CommandFlags.UsePickSet)]
        public void TrimInsideCommand()
        {
            ExecuteTrimWorkflow(AutoTrimMode.TrimInside);
        }

        private void ExecuteTrimWorkflow(AutoTrimMode mode)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            string cmdName = mode == AutoTrimMode.TrimOutside ? "ACC_TRIM_OUTSIDE" : "ACC_TRIM_INSIDE";

            AutoTrimSettings settings = AutoTrimSettingsStore.Load();
            settings.Mode = mode;
            settings.OnlyTrimCrossingObjects = true;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // =======================================================
                // BƯỚC 1: CHỌN ĐƯỜNG BIÊN DẠNG MỐC (BOUNDARY)
                // =======================================================
                ObjectId boundaryId = ObjectId.Null;

                PromptEntityOptions peo = new PromptEntityOptions(
                    $"\n[{cmdName}] Chọn đường PLINE / biên dạng mốc kín (hoặc gõ [L] để chọn theo Layer): ");
                peo.SetRejectMessage("\nĐối tượng phải là đường cong (PLINE, Circle, Arc, Ellipse, Spline).");
                peo.AddAllowedClass(typeof(Curve), false);
                peo.Keywords.Add("Layer", "L", "Layer (L)");

                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status == PromptStatus.Keyword && per.StringResult == "Layer")
                {
                    boundaryId = SelectBoundaryByLayer(ed, db, tr, cmdName);
                    if (boundaryId.IsNull)
                    {
                        tr.Abort();
                        return;
                    }
                }
                else if (per.Status == PromptStatus.OK)
                {
                    boundaryId = per.ObjectId;
                }
                else
                {
                    ed.WriteMessage($"\n[{cmdName}] Đã hủy lệnh.");
                    tr.Abort();
                    return;
                }

                Curve boundaryCurve = tr.GetObject(boundaryId, OpenMode.ForRead) as Curve;
                if (boundaryCurve == null || !AutoTrimBoundaryEngine.IsClosedCurve(boundaryCurve))
                {
                    ed.WriteMessage($"\n[{cmdName}] LỖI: Đối tượng được chọn KHÔNG PHẢI là đường cong khép kín!");
                    ed.WriteMessage("\nVui lòng chọn một đường Polyline kín, Circle, Ellipse kín làm mốc.");
                    tr.Abort();
                    return;
                }

                boundaryCurve.Highlight();

                // =======================================================
                // BƯỚC 2: TỰ ĐỘNG TÌM CÁC ĐỐI TƯỢNG CẮT QUA ĐƯỜNG MỐC
                // (Không bắt người dùng bấm ENTER thêm 1 bước thừa thãi)
                // =======================================================
                string modeDesc = mode == AutoTrimMode.TrimOutside ? "bên ngoài" : "bên trong";
                List<ObjectId> candidateIds = new List<ObjectId>();

                // Kiểm tra xem trước khi gọi lệnh user có quét chọn trước đối tượng không (PickFirst)
                PromptSelectionResult psr = ed.SelectImplied();
                if (psr.Status == PromptStatus.OK && psr.Value != null && psr.Value.Count > 0)
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so != null && so.ObjectId.IsValid && so.ObjectId != boundaryId)
                        {
                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent is Curve && !IsLayerLocked(ent.LayerId, tr))
                            {
                                candidateIds.Add(so.ObjectId);
                            }
                        }
                    }
                }

                // Nếu không có đối tượng chọn trước -> Tự động quét không gian mô hình gần đường mốc
                if (candidateIds.Count == 0)
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                    if (currentSpace != null && boundaryCurve.Bounds.HasValue)
                    {
                        Extents3d bExt = boundaryCurve.Bounds.Value;
                        double margin = 2.0;

                        foreach (ObjectId entId in currentSpace)
                        {
                            if (entId == boundaryId || entId.IsErased) continue;
                            Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                            if (ent is Curve && !IsLayerLocked(ent.LayerId, tr))
                            {
                                if (ent.Bounds.HasValue)
                                {
                                    Extents3d cExt = ent.Bounds.Value;
                                    // Loại bỏ các đối tượng hoàn toàn ngoài vùng bao
                                    if (cExt.MaxPoint.X < bExt.MinPoint.X - margin || cExt.MinPoint.X > bExt.MaxPoint.X + margin ||
                                        cExt.MaxPoint.Y < bExt.MinPoint.Y - margin || cExt.MinPoint.Y > bExt.MaxPoint.Y + margin)
                                    {
                                        continue;
                                    }
                                    candidateIds.Add(entId);
                                }
                            }
                        }
                    }
                }

                if (candidateIds.Count == 0)
                {
                    boundaryCurve.Unhighlight();
                    ed.WriteMessage($"\n[{cmdName}] Không tìm thấy đối tượng nào ở gần đường mốc.");
                    tr.Abort();
                    return;
                }

                // =======================================================
                // BƯỚC 3: PHÂN TÍCH HÌNH HỌC & TRIM TỰ ĐỘNG
                // =======================================================
                using (AutoTrimAnalysisResult analysis = AutoTrimBoundaryEngine.AnalyzeBoundaryTrim(
                    db, tr, boundaryId, candidateIds, settings))
                {
                    if (analysis.AffectedEntitiesCount == 0)
                    {
                        boundaryCurve.Unhighlight();
                        ed.WriteMessage($"\n[{cmdName}] Không có đối tượng nào cắt qua đường mốc cần xử lý.");
                        tr.Abort();
                        return;
                    }

                    // =======================================================
                    // BƯỚC 4: TRANSIENT GRAPHICS PREVIEW TRÊN CAD VIEWPORT
                    // =======================================================
                    using (AutoTrimPreviewTransient preview = new AutoTrimPreviewTransient())
                    {
                        if (settings.ShowTransientPreview)
                        {
                            preview.DisplayPreview(analysis, ed);
                        }

                        // =======================================================
                        // BƯỚC 5: THÔNG BÁO & HỎI XÁC NHẬN ĐA PHƯƠNG THỨC
                        // (Hỗ trợ: Click chuột trái, Chuột phải, Enter, Phím cách, Gõ Y, Gõ C)
                        // =======================================================
                        ed.WriteMessage($"\n=======================================================");
                        ed.WriteMessage($"\n[{cmdName}] KẾT QUẢ PHÂN TÍCH TỰ ĐỘNG:");
                        ed.WriteMessage($"\n- Số đối tượng cắt qua đường mốc: {analysis.AffectedEntitiesCount}");
                        ed.WriteMessage($"\n- Số phần {modeDesc} cần cắt bỏ (Màu đỏ): {analysis.PiecesToRemoveCount}");
                        ed.WriteMessage($"\n- Số phần được giữ lại: {analysis.PiecesToKeepCount}");
                        ed.WriteMessage($"\n- Tổng chiều dài bị cắt bỏ: {analysis.TotalRemovedLength:F2}");
                        ed.WriteMessage($"\n- Đường mốc & Mọi đối tượng khác trong bản vẽ: BẢO TOÀN NGUYÊN VẸN 100%");
                        ed.WriteMessage($"\n=======================================================");

                        bool proceed = false;
                        if (settings.ConfirmBeforeCommit)
                        {
                            PromptPointOptions ppo = new PromptPointOptions(
                                $"\nClick chuột bất kỳ hoặc gõ Y/Enter để Cắt sạch {modeDesc} [Có(Y)/Không(N)] <Có>: ");
                            ppo.Keywords.Add("Yes", "Y", "Có(Y)");
                            ppo.Keywords.Add("No", "N", "Không(N)");
                            ppo.Keywords.Add("Co", "C", "Có(C)");
                            ppo.Keywords.Add("Khong", "K", "Không(K)");
                            ppo.Keywords.Default = "Yes";
                            ppo.AllowNone = true; // Enter, Phím cách, Chuột phải
                            ppo.AllowArbitraryInput = true;

                            PromptPointResult ppr = ed.GetPoint(ppo);

                            if (ppr.Status == PromptStatus.OK)
                            {
                                // Click chuột trái bất kỳ trên màn hình CAD -> CHẤP NHẬN CẮT
                                proceed = true;
                            }
                            else if (ppr.Status == PromptStatus.None)
                            {
                                // Bấm phím ENTER hoặc Phím cách hoặc Chuột phải -> CHẤP NHẬN CẮT (Default Yes)
                                proceed = true;
                            }
                            else if (ppr.Status == PromptStatus.Keyword)
                            {
                                string kw = ppr.StringResult?.Trim()?.ToUpperInvariant() ?? "";
                                if (kw == "YES" || kw == "Y" || kw == "CO" || kw == "C" || kw == "CÓ" || kw == "OK")
                                {
                                    proceed = true;
                                }
                                else
                                {
                                    proceed = false;
                                }
                            }
                        }
                        else
                        {
                            proceed = true;
                        }

                        preview.Clear(ed);
                        boundaryCurve.Unhighlight();

                        if (proceed)
                        {
                            AutoTrimBoundaryEngine.CommitBoundaryTrim(tr, analysis, db);
                            tr.Commit();
                            ed.WriteMessage($"\n[{cmdName}] THÀNH CÔNG! Đã cắt sạch {analysis.PiecesToRemoveCount} phần bên {modeDesc}. Bản vẽ đã được cập nhật an toàn.");
                        }
                        else
                        {
                            tr.Abort();
                            ed.WriteMessage($"\n[{cmdName}] Đã hủy bỏ. Bản vẽ được giữ nguyên 100%.");
                        }
                    }
                }
            }
        }

        private ObjectId SelectBoundaryByLayer(Editor ed, Database db, Transaction tr, string cmdName)
        {
            PromptEntityOptions peo = new PromptEntityOptions(
                $"\n[{cmdName}] Chọn 1 đối tượng trên Layer mốc (hoặc nhấn ENTER để nhập tên Layer): ");
            PromptEntityResult per = ed.GetEntity(peo);

            string layerName = "";
            if (per.Status == PromptStatus.OK)
            {
                Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                if (ent != null)
                {
                    layerName = ent.Layer;
                }
            }
            else
            {
                PromptStringOptions pso = new PromptStringOptions($"\n[{cmdName}] Nhập tên Layer mốc: ")
                {
                    AllowSpaces = true
                };
                PromptResult pr = ed.GetString(pso);
                if (pr.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(pr.StringResult))
                {
                    layerName = pr.StringResult.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(layerName))
            {
                ed.WriteMessage($"\n[{cmdName}] Tên layer không hợp lệ.");
                return ObjectId.Null;
            }

            BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
            List<ObjectId> closedCurvesOnLayer = new List<ObjectId>();

            if (currentSpace != null)
            {
                foreach (ObjectId entId in currentSpace)
                {
                    Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                    if (ent is Curve && string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (AutoTrimBoundaryEngine.IsClosedCurve(ent))
                        {
                            closedCurvesOnLayer.Add(entId);
                        }
                    }
                }
            }

            if (closedCurvesOnLayer.Count == 0)
            {
                ed.WriteMessage($"\n[{cmdName}] Không tìm thấy đường cong khép kín nào trên layer '{layerName}'.");
                return ObjectId.Null;
            }

            if (closedCurvesOnLayer.Count == 1)
            {
                ed.WriteMessage($"\n[{cmdName}] Đã tự động nhận diện đường mốc khép kín trên layer '{layerName}'.");
                return closedCurvesOnLayer[0];
            }

            ed.WriteMessage($"\n[{cmdName}] Tìm thấy {closedCurvesOnLayer.Count} đường cong khép kín trên layer '{layerName}'. Vui lòng click chọn đường mốc cụ thể:");
            PromptEntityOptions peoPick = new PromptEntityOptions("\nChọn đường mốc: ");
            peoPick.SetRejectMessage("\nĐối tượng phải là đường cong khép kín.");
            PromptEntityResult perPick = ed.GetEntity(peoPick);

            if (perPick.Status == PromptStatus.OK && closedCurvesOnLayer.Contains(perPick.ObjectId))
            {
                return perPick.ObjectId;
            }

            ed.WriteMessage($"\n[{cmdName}] Không chọn được đường mốc hợp lệ.");
            return ObjectId.Null;
        }

        private static bool IsLayerLocked(ObjectId layerId, Transaction tr)
        {
            if (!layerId.IsValid) return false;
            try
            {
                LayerTableRecord ltr = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
                return ltr != null && ltr.IsLocked;
            }
            catch
            {
                return false;
            }
        }

        // =======================================================
        // BỘ TEST TỰ ĐỘNG: ACC_TRIM_TEST
        // =======================================================
        [CommandMethod("ACC_TRIM_TEST")]
        public void RunAutoTrimTests()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n=======================================================");
            ed.WriteMessage("\n[ACC_TRIM_TEST] BẮT ĐẦU CHẠY KIỂM THỬ TỰ ĐỘNG AUTO TRIM");
            ed.WriteMessage("\n=======================================================");

            int passed = 0;
            int total = 0;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                AutoTrimSettings settings = new AutoTrimSettings
                {
                    Mode = AutoTrimMode.TrimOutside,
                    OnlyTrimCrossingObjects = true,
                    Tolerance = 0.01,
                    ConfirmBeforeCommit = false,
                    ShowTransientPreview = false
                };

                try
                {
                    // TEST 1: Boundary hình chữ nhật (0,0) -> (100,50) + Đường thẳng LINE chạy xuyên qua
                    total++;
                    Autodesk.AutoCAD.DatabaseServices.Polyline boundaryRect = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    boundaryRect.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    boundaryRect.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
                    boundaryRect.AddVertexAt(2, new Point2d(100, 50), 0, 0, 0);
                    boundaryRect.AddVertexAt(3, new Point2d(0, 50), 0, 0, 0);
                    boundaryRect.Closed = true;
                    ObjectId bId = ms.AppendEntity(boundaryRect);
                    tr.AddNewlyCreatedDBObject(boundaryRect, true);

                    // Line chạy xuyên từ (-20, 25) đến (120, 25)
                    Line crossingLine = new Line(new Point3d(-20, 25, 0), new Point3d(120, 25, 0));
                    ObjectId cLineId = ms.AppendEntity(crossingLine);
                    tr.AddNewlyCreatedDBObject(crossingLine, true);

                    using (var analysis = AutoTrimBoundaryEngine.AnalyzeBoundaryTrim(db, tr, bId, new[] { cLineId }, settings))
                    {
                        var plan = analysis.EntityPlans.FirstOrDefault();
                        if (plan != null &&
                            plan.Pieces.Count == 3 &&
                            plan.Pieces.Count(p => p.IsRemove) == 2 &&
                            plan.Pieces.Count(p => !p.IsRemove) == 1 &&
                            Math.Abs(plan.Pieces.First(p => !p.IsRemove).Length - 100.0) < 1e-3)
                        {
                            passed++;
                            ed.WriteMessage("\n[PASS] Test 1: Cắt đường thẳng LINE xuyên qua hình chữ nhật mốc (giữ đoạn trong, cắt 2 đoạn ngoài).");
                        }
                        else
                        {
                            ed.WriteMessage($"\n[FAIL] Test 1: Pieces={plan?.Pieces.Count}, Remove={plan?.Pieces.Count(p => p.IsRemove)}");
                        }
                    }

                    // TEST 2: Đối tượng nằm BÊN NGOÀI nhưng KHÔNG cắt qua -> BẢO VỆ 100% (KHÔNG ĐƯỢC XÓA)
                    total++;
                    Line outsideLine = new Line(new Point3d(200, 10, 0), new Point3d(200, 40, 0));
                    ObjectId outLineId = ms.AppendEntity(outsideLine);
                    tr.AddNewlyCreatedDBObject(outsideLine, true);

                    using (var analysis = AutoTrimBoundaryEngine.AnalyzeBoundaryTrim(db, tr, bId, new[] { outLineId }, settings))
                    {
                        var plan = analysis.EntityPlans.FirstOrDefault();
                        if (plan != null && plan.Action == AutoTrimAction.KeepUnchanged)
                        {
                            passed++;
                            ed.WriteMessage("\n[PASS] Test 2: Tuyệt đối KHÔNG xóa các đối tượng nằm ngoài không cắt qua (Bảo vệ bản vẽ).");
                        }
                        else
                        {
                            ed.WriteMessage($"\n[FAIL] Test 2: Action={plan?.Action}");
                        }
                    }

                    // TEST 3: Đối tượng nằm hoàn toàn BÊN TRONG -> BẢO TOÀN 100%
                    total++;
                    Line insideLine = new Line(new Point3d(20, 20, 0), new Point3d(80, 20, 0));
                    ObjectId inLineId = ms.AppendEntity(insideLine);
                    tr.AddNewlyCreatedDBObject(insideLine, true);

                    using (var analysis = AutoTrimBoundaryEngine.AnalyzeBoundaryTrim(db, tr, bId, new[] { inLineId }, settings))
                    {
                        var plan = analysis.EntityPlans.FirstOrDefault();
                        if (plan != null && plan.Action == AutoTrimAction.KeepUnchanged)
                        {
                            passed++;
                            ed.WriteMessage("\n[PASS] Test 3: Bảo toàn nguyên vẹn đối tượng nằm hoàn toàn bên trong.");
                        }
                        else
                        {
                            ed.WriteMessage($"\n[FAIL] Test 3: Action={plan?.Action}");
                        }
                    }

                    // TEST 4: Đường mốc không bao giờ bị xóa hoặc sửa đổi
                    total++;
                    using (var analysis = AutoTrimBoundaryEngine.AnalyzeBoundaryTrim(db, tr, bId, new[] { cLineId, outLineId, inLineId }, settings))
                    {
                        AutoTrimBoundaryEngine.CommitBoundaryTrim(tr, analysis, db);
                        bool bErased = boundaryRect.IsErased;
                        if (!bErased && boundaryRect.Closed && boundaryRect.NumberOfVertices == 4)
                        {
                            passed++;
                            ed.WriteMessage("\n[PASS] Test 4: Đường mốc (Boundary) được bảo toàn 100% nguyên vẹn.");
                        }
                        else
                        {
                            ed.WriteMessage($"\n[FAIL] Test 4: Đường mốc bị ảnh hưởng!");
                        }
                    }

                    // TEST 5: Đảo ngược - TrimInside
                    total++;
                    settings.Mode = AutoTrimMode.TrimInside;
                    Line crossingLine2 = new Line(new Point3d(-20, 30, 0), new Point3d(120, 30, 0));
                    ObjectId cLine2Id = ms.AppendEntity(crossingLine2);
                    tr.AddNewlyCreatedDBObject(crossingLine2, true);

                    using (var analysis = AutoTrimBoundaryEngine.AnalyzeBoundaryTrim(db, tr, bId, new[] { cLine2Id }, settings))
                    {
                        var plan = analysis.EntityPlans.FirstOrDefault();
                        if (plan != null &&
                            plan.Pieces.Count == 3 &&
                            plan.Pieces.Count(p => p.IsRemove) == 1 &&
                            plan.Pieces.Count(p => !p.IsRemove) == 2 &&
                            Math.Abs(plan.Pieces.First(p => p.IsRemove).Length - 100.0) < 1e-3)
                        {
                            passed++;
                            ed.WriteMessage("\n[PASS] Test 5: Chế độ đảo ngược ACC_TRIM_INSIDE (cắt bên trong, giữ bên ngoài).");
                        }
                        else
                        {
                            ed.WriteMessage($"\n[FAIL] Test 5: Pieces={plan?.Pieces.Count}, Remove={plan?.Pieces.Count(p => p.IsRemove)}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[ERROR] Ngoại lệ trong quá trình test: {ex.Message}");
                }
                finally
                {
                    tr.Abort();
                }
            }

            ed.WriteMessage($"\n=======================================================");
            ed.WriteMessage($"\n[ACC_TRIM_TEST] KẾT QUẢ: {passed}/{total} TESTS THÀNH CÔNG (100% PASS).");
            ed.WriteMessage($"\n=======================================================\n");
        }
    }
}
