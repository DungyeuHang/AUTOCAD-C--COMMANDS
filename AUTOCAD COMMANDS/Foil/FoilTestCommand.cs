using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Globalization;
using Exception = System.Exception;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Lenh kiem thu cua DX_FOIL, theo dung quy uoc test cua project (giong ACC_AUTO_CUT_TEST).
    /// Gom 2 phan:
    ///   A. Bo test calculation engine thuan (FoilSelfTests) - khong dung AutoCAD.
    ///   B. Bo test tang AutoCAD: doc polyline, tao layer, ve entity, mau, huong chan.
    /// Tat ca chay tren Database trong bo nho nen khong dung den ban ve dang mo.
    /// </summary>
    public class FoilTestCommand
    {
        [CommandMethod("DX_FOIL_TEST")]
        public void RunTests()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null)
            {
                return;
            }

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n DX_FOIL - PHAN A: CALCULATION ENGINE");
            ed.WriteMessage("\n==================================================");

            FoilTestReport engineReport = FoilSelfTests.Run();
            foreach (string line in engineReport.Lines)
            {
                ed.WriteMessage("\n" + line);
            }

            ed.WriteMessage("\n");
            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n DX_FOIL - PHAN B: TANG AUTOCAD");
            ed.WriteMessage("\n==================================================");

            int passed = 0;
            int failed = 0;

            RunTest("B1: Doc LWPolyline bien dang chu L", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 20), new Point2d(30, 20));

                    FoilSettings s = DefaultSettings();
                    FoilPolylineReadResult read = FoilPolylineReader.Read(pl, s);

                    Assert(read.Success, "phai doc duoc polyline");
                    Assert(read.Profile.SegmentCount == 2, "phai co 2 doan");
                    Assert(!read.Profile.Closed, "polyline ho");
                    Assert(Math.Abs(read.Profile.TotalLength - 50.0) < 1e-9, "tong chieu dai phai la 50");

                    tr.Abort();
                }
            });

            RunTest("B2: Doc bulge -> cung tro thanh bend that", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    double bulge = Math.Tan(-Math.PI / 8.0);      // cung CW 90 do
                    Polyline pl = new Polyline();
                    pl.SetDatabaseDefaults(db);
                    pl.AddVertexAt(0, new Point2d(0, 0), 0.0, 0.0, 0.0);
                    pl.AddVertexAt(1, new Point2d(0, 20), bulge, 0.0, 0.0);
                    pl.AddVertexAt(2, new Point2d(10, 30), 0.0, 0.0, 0.0);
                    pl.AddVertexAt(3, new Point2d(40, 30), 0.0, 0.0, 0.0);
                    AppendToModelSpace(db, tr, pl);

                    FoilSettings s = DefaultSettings();
                    FoilPolylineReadResult read = FoilPolylineReader.Read(pl, s);
                    Assert(read.Success, "phai doc duoc polyline co bulge");

                    FoilProfileAnalysis analysis = FoilProfileAnalyzer.Analyze(read.Profile, s);
                    Assert(!analysis.HasErrors, "phan tich khong duoc loi");
                    Assert(analysis.Bends.Count == 1, "phai co dung 1 bend (tu cung)");
                    Assert(analysis.Bends[0].Kind == FoilBendKind.Arc, "bend phai la loai Arc");
                    Assert(Math.Abs(analysis.Bends[0].BendAngleDeg - 90.0) < 1e-6, "goc quet phai la 90 do");
                    Assert(Math.Abs(analysis.Bends[0].InsideRadius - 10.0) < 1e-6, "ban kinh trong phai la 10");

                    tr.Abort();
                }
            });

            RunTest("B3: Tu choi doi tuong khong phai polyline", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Line line = new Line(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
                    AppendToModelSpace(db, tr, line);

                    FoilPolylineReadResult read = FoilPolylineReader.Read(line, DefaultSettings());
                    Assert(!read.Success, "LINE phai bi tu choi");
                    Assert(read.Errors.Count > 0, "phai co thong bao loi ro rang");

                    tr.Abort();
                }
            });

            RunTest("B4: Tao layer _mss.dut va ve phoi + duong chan", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 20), new Point2d(0, 0),
                        new Point2d(30, 0), new Point2d(30, 20));

                    FoilSettings s = DefaultSettings();
                    s.BlankLength = 1000.0;

                    FoilFlatPatternResult result = Calculate(pl, s);
                    Assert(!result.HasErrors, "tinh toan khong duoc loi");
                    Assert(result.BendCount == 2, "phai co 2 duong chan");

                    FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(100, 100), 0.0);

                    FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);

                    Assert(!draw.OutlineId.IsNull, "phai tao duoc bien phoi");
                    Assert(draw.BendLineIds.Count == 2, "phai tao duoc 2 duong chan");
                    Assert(draw.LayerCreated, "layer _mss.dut phai duoc tao moi");

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    Assert(lt.Has("_mss.dut"), "layer _mss.dut phai ton tai");

                    foreach (ObjectId id in draw.BendLineIds)
                    {
                        Line bendLine = (Line)tr.GetObject(id, OpenMode.ForRead);
                        Assert(bendLine.Layer == "_mss.dut", "duong chan phai nam tren layer _mss.dut");
                        Assert(Math.Abs(bendLine.Length - 1000.0) < 1e-6,
                            "duong chan phai trai het chieu dai phoi");
                    }

                    Polyline outline = (Polyline)tr.GetObject(draw.OutlineId, OpenMode.ForRead);
                    Assert(outline.Closed, "bien phoi phai la polyline kin");
                    Assert(outline.NumberOfVertices == 4, "bien phoi phai co 4 dinh");

                    tr.Abort();
                }
            });

            RunTest("B5: Ve lai lan 2 KHONG tao layer trung", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 20), new Point2d(30, 20));

                    FoilSettings s = DefaultSettings();
                    FoilFlatPatternResult result = Calculate(pl, s);

                    FoilFlatPatternGeometry g1 = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(0, 0), 0.0);
                    FoilDrawResult d1 = FoilDrawingBuilder.Draw(db, tr, result, g1, 0.0);
                    Assert(d1.LayerCreated, "lan 1 phai tao layer");

                    FoilFlatPatternGeometry g2 = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(0, 500), 0.0);
                    FoilDrawResult d2 = FoilDrawingBuilder.Draw(db, tr, result, g2, 0.0);
                    Assert(!d2.LayerCreated, "lan 2 phai dung lai layer cu, khong tao trung");

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    int count = 0;
                    foreach (ObjectId id in lt)
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (string.Equals(ltr.Name, "_mss.dut", StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                        }
                    }

                    Assert(count == 1, "chi duoc co dung 1 layer _mss.dut");

                    tr.Abort();
                }
            });

            RunTest("B6: Mau chan DOWN dung ACI 8 (xam)", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Bien dang Z: chac chan co 1 chan UP va 1 chan DOWN.
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 20),
                        new Point2d(30, 20), new Point2d(30, 40));

                    FoilSettings s = DefaultSettings();
                    FoilFlatPatternResult result = Calculate(pl, s);
                    Assert(result.BendCount == 2, "bien dang Z phai co 2 duong chan");

                    FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(0, 0), 0.0);
                    FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);

                    Assert(draw.UpCount == 1 && draw.DownCount == 1, "phai co 1 UP va 1 DOWN");

                    bool foundGrey = false;
                    bool foundByLayer = false;
                    foreach (ObjectId id in draw.BendLineIds)
                    {
                        Line bendLine = (Line)tr.GetObject(id, OpenMode.ForRead);
                        if (bendLine.Color.IsByLayer)
                        {
                            foundByLayer = true;
                        }
                        else if (bendLine.Color.IsByAci && bendLine.Color.ColorIndex == 8)
                        {
                            foundGrey = true;
                        }
                    }

                    Assert(foundByLayer, "chan UP phai la ByLayer theo mac dinh");
                    Assert(foundGrey, "chan DOWN phai la ACI 8 (xam) theo mac dinh");

                    tr.Abort();
                }
            });

            RunTest("B7: Abort transaction khong de lai hinh hoc rac", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                {
                    ObjectId outlineId;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Polyline pl = CreatePolyline(db, tr, false,
                            new Point2d(0, 0), new Point2d(0, 20), new Point2d(30, 20));

                        FoilSettings s = DefaultSettings();
                        FoilFlatPatternResult result = Calculate(pl, s);
                        FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                            result, new FoilPoint2d(0, 0), 0.0);
                        FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);
                        outlineId = draw.OutlineId;

                        tr.Abort();
                    }

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                            bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        int count = 0;
                        foreach (ObjectId id in ms)
                        {
                            count++;
                        }

                        Assert(count == 0, "sau khi Abort, khong duoc con entity nao");
                        Assert(!outlineId.IsValid || outlineId.IsErased || outlineId.IsNull ||
                               count == 0, "bien phoi khong duoc ton tai sau Abort");
                        tr.Abort();
                    }
                }
            });

            RunTest("B8: Polyline kin chu nhat trien khai dung", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(40, 0),
                        new Point2d(40, 20), new Point2d(0, 20));

                    FoilSettings s = DefaultSettings();
                    FoilFlatPatternResult result = Calculate(pl, s);

                    Assert(!result.HasErrors, "tiet dien kin phai trien khai duoc");
                    Assert(result.BendCount == 4, "phai co 4 duong chan");
                    Assert(Math.Abs(result.BlankWidth - (120.0 - 4 * 2.4)) < 1e-9,
                        "chieu rong phai la 120 - 4 * 2.4");

                    tr.Abort();
                }
            });

            RunTest("B9: Phoi xoay - entity nam dung vi tri", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 20), new Point2d(30, 20));

                    FoilSettings s = DefaultSettings();
                    s.BlankLength = 200.0;
                    s.BlankRotationDeg = 45.0;

                    FoilFlatPatternResult result = Calculate(pl, s);
                    FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                        result,
                        new FoilPoint2d(0, 0),
                        s.BlankRotationDeg * FoilMath.DegToRad);

                    FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);
                    Polyline outline = (Polyline)tr.GetObject(draw.OutlineId, OpenMode.ForRead);

                    Point2d p0 = outline.GetPoint2dAt(0);
                    Point2d p1 = outline.GetPoint2dAt(1);
                    Assert(Math.Abs(p0.GetDistanceTo(p1) - 200.0) < 1e-6,
                        "canh dai phoi phai giu dung 200 sau khi xoay");

                    double expected = 200.0 / Math.Sqrt(2.0);
                    Assert(Math.Abs(p1.X - expected) < 1e-6 && Math.Abs(p1.Y - expected) < 1e-6,
                        "goc thu hai phai nam dung tren huong 45 do");

                    tr.Abort();
                }
            });

            RunTest("B10: Polyline Normal (0,0,-1) khong bi lat guong", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Cung mot bien dang chu L nhung mat phang polyline bi lat.
                    // Toa do OCS (0,0) (0,20) (30,20) tuong ung WCS (0,0) (0,20) (-30,20),
                    // nghia la canh 30 nam TRUOC canh 20 theo chieu duyet chuan hoa.
                    Polyline pl = new Polyline();
                    pl.SetDatabaseDefaults(db);
                    pl.AddVertexAt(0, new Point2d(0, 0), 0.0, 0.0, 0.0);
                    pl.AddVertexAt(1, new Point2d(0, 20), 0.0, 0.0, 0.0);
                    pl.AddVertexAt(2, new Point2d(30, 20), 0.0, 0.0, 0.0);
                    pl.Normal = new Vector3d(0, 0, -1);
                    AppendToModelSpace(db, tr, pl);

                    FoilSettings s = DefaultSettings();
                    FoilPolylineReadResult read = FoilPolylineReader.Read(pl, s);
                    Assert(read.Success, "phai doc duoc polyline lat mat phang");

                    // Doc dung WCS thi dinh cuoi phai o X am.
                    Assert(read.MinX < -1.0,
                        "toa do phai duoc quy doi ve WCS (dinh cuoi nam ben trai goc)");

                    FoilFlatPatternResult result = Calculate(pl, s);
                    Assert(!result.HasErrors, "phai trien khai duoc");
                    Assert(result.BendCount == 1, "phai co 1 duong chan");
                    Assert(Math.Abs(result.BlankWidth - 47.6) < 1e-9, "chieu rong van phai la 47.6");

                    // Neu doc nham bang OCS thi vi tri duong chan se ra 18.8 (canh 20 truoc).
                    Assert(Math.Abs(result.Bends[0].FlatPosition - 28.8) < 1e-9,
                        "vi tri duong chan phai la 28.8 (canh 30 di truoc trong WCS), khong phai 18.8");

                    tr.Abort();
                }
            });

            RunTest("B11: Ve hinh cac buoc chan tren layer rieng", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Bien dang Z: 2 duong chan => 3 buoc (B0, B1, B2).
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 20),
                        new Point2d(30, 20), new Point2d(30, 40));

                    FoilSettings s = DefaultSettings();
                    s.BlankLength = 1000.0;
                    s.DrawBendSteps = true;

                    FoilFlatPatternResult result = Calculate(pl, s);
                    Assert(!result.HasErrors, "tinh toan khong duoc loi");

                    FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(0, 0), 0.0);

                    Assert(geometry.Steps.Count == 3, "phai co 3 buoc (B0 + 2 lan chan)");

                    FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);
                    Assert(draw.StepCount == 3, "phai ve du 3 buoc");
                    Assert(draw.StepIds.Count > 0, "phai tao duoc entity cho cac buoc");

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    Assert(lt.Has("_mss.buocchan"), "layer hinh buoc chan phai duoc tao");

                    Assert(lt.Has("_mss.dungcu"), "layer dung cu (coi + dao) phai duoc tao");

                    int shapes = 0;
                    int toolShapes = 0;
                    int markers = 0;
                    int labels = 0;
                    foreach (ObjectId id in draw.StepIds)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        Assert(ent.Layer == "_mss.buocchan" || ent.Layer == "_mss.dungcu",
                            "entity cua buoc chan phai nam tren layer rieng, khong lan sang _mss.dut");

                        bool isTool = ent.Layer == "_mss.dungcu";
                        if (ent is Polyline)
                        {
                            if (isTool) toolShapes++; else shapes++;
                        }
                        else if (ent is Circle)
                        {
                            markers++;
                        }
                        else if (ent is DBText)
                        {
                            labels++;
                        }
                    }

                    Assert(shapes == 3, "moi buoc phai co 1 duong gap khuc cua chi tiet");

                    // Moi buoc ve DU bo phan may da duoc dung de kiem va cham:
                    // dam duoi, ke coi, than coi, ngon cu hau, chay dao. Chi tiet nay thap hon
                    // chieu cao dao nen khong ve dam tren (no khong the cham toi).
                    int expectedTools = 0;
                    foreach (FoilBendStepGeometry sg in geometry.Steps)
                    {
                        expectedTools += sg.ToolOutlines.Count;
                    }

                    Assert(expectedTools == 15,
                        "3 buoc x 5 bo phan may (dam duoi, ke coi, coi, cu hau, dao)");
                    Assert(toolShapes == expectedTools,
                        "phai ve dung bo phan may da dung de kiem va cham");

                    // Moi duong chan duoc danh mot so thu tu bang bong tron o mep phoi.
                    Assert(draw.StepMarkCount == 2, "2 duong chan => 2 bong tron so thu tu");
                    Assert(markers == 2 + draw.StepMarkCount,
                        "2 dau danh dau dinh goc + 2 bong tron so thu tu");

                    // Buoc co chan co them dong chu phu ve thong so ga dat; moi bong tron co
                    // mot chu so ben trong.
                    Assert(labels == 5 + draw.StepMarkCount,
                        "3 dong chu chinh + 2 dong chu phu + 2 so trong bong tron");

                    // Duong chan van phai nam dung layer cua no.
                    foreach (ObjectId id in draw.BendLineIds)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        Assert(ent.Layer == "_mss.dut", "duong chan van phai o layer _mss.dut");
                    }

                    tr.Abort();
                }
            });

            RunTest("B12: Tat tuy chon thi khong ve buoc chan", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 20), new Point2d(30, 20));

                    FoilSettings s = DefaultSettings();
                    s.DrawBendSteps = false;

                    FoilFlatPatternResult result = Calculate(pl, s);
                    FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(0, 0), 0.0);

                    Assert(geometry.Steps.Count == 0, "khong duoc sinh buoc nao khi da tat tuy chon");

                    FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);
                    Assert(draw.StepCount == 0, "khong duoc ve buoc nao");
                    Assert(draw.StepIds.Count == 0, "khong duoc tao entity buoc nao");

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    Assert(!lt.Has("_mss.buocchan"),
                        "khong duoc tao layer buoc chan khi tuy chon dang tat");
                    Assert(!lt.Has("_mss.dungcu"),
                        "khong duoc tao layer dung cu khi tuy chon dang tat");
                    Assert(geometry.StepMarks.Count == 0,
                        "khong duoc danh so thu tu khi khong ve buoc chan");
                    Assert(draw.StepMarkCount == 0, "khong duoc ve bong tron nao");

                    tr.Abort();
                }
            });

            RunTest("B13: Khong ve entity danh dau tren duong chan", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Bien dang hem: co goc LOM nen chac chan co duong chan duoc cong be day.
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(0, 60), new Point2d(100, 60),
                        new Point2d(100, 40), new Point2d(226.4, 40), new Point2d(226.4, 60),
                        new Point2d(326.4, 60), new Point2d(326.4, 0));

                    FoilSettings s = DefaultSettings();
                    s.ThicknessCompensation = FoilThicknessCompensationMode.TurnLeft;
                    s.DrawBendSteps = false;

                    FoilFlatPatternResult result = Calculate(pl, s);
                    FoilFlatPatternGeometry geometry = FoilFlatPatternGeometry.Build(
                        result, new FoilPoint2d(0, 0), 0.0);

                    FoilDrawResult draw = FoilDrawingBuilder.Draw(db, tr, result, geometry, 0.0);

                    Assert(draw.CompensatedCount > 0,
                        "bien dang nay phai co duong chan duoc cong be day (de phep thu co y nghia)");

                    // Duong chan chi duoc ve bang LINE - khong con vong tron danh dau nao.
                    foreach (ObjectId id in draw.BendLineIds)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        Assert(ent is Line,
                            "chi duoc ve Line cho duong chan, khong ve entity danh dau");
                    }

                    Assert(draw.BendLineIds.Count == result.BendCount,
                        "so entity phai bang dung so duong chan, khong co entity thua");

                    tr.Abort();
                }
            });

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage(string.Format(
                CultureInfo.InvariantCulture,
                "\n PHAN B: {0} PASS / {1} FAIL", passed, failed));
            ed.WriteMessage(string.Format(
                CultureInfo.InvariantCulture,
                "\n TONG CONG: {0} PASS / {1} FAIL",
                engineReport.Passed + passed,
                engineReport.Failed + failed));
            ed.WriteMessage("\n==================================================");
        }

        // ==============================================================================
        // HA TANG
        // ==============================================================================

        private static FoilSettings DefaultSettings()
        {
            return new FoilSettings
            {
                BlankLength = 2500.0,
                Thickness = 1.2,
                InsideRadius = 1.2,
                KFactor = 0.42,
                CustomFactor = 1.0,
                Method = FoilBendMethod.CustomShopRule
            };
        }

        private static FoilFlatPatternResult Calculate(Entity entity, FoilSettings settings)
        {
            FoilPolylineReadResult read = FoilPolylineReader.Read(entity, settings);
            if (!read.Success)
            {
                throw new InvalidOperationException(
                    "Khong doc duoc polyline: " + string.Join(" | ", read.Errors.ToArray()));
            }

            FoilProfileAnalysis analysis = FoilProfileAnalyzer.Analyze(read.Profile, settings);
            IFoilBendStrategy strategy = FoilStrategyFactory.Create(settings, null);
            return FoilFlatPatternCalculator.Calculate(analysis, settings, strategy);
        }

        private static Polyline CreatePolyline(
            Database db, Transaction tr, bool closed, params Point2d[] points)
        {
            Polyline pl = new Polyline(points.Length);
            pl.SetDatabaseDefaults(db);
            for (int i = 0; i < points.Length; i++)
            {
                pl.AddVertexAt(i, points[i], 0.0, 0.0, 0.0);
            }

            pl.Closed = closed;
            AppendToModelSpace(db, tr, pl);
            return pl;
        }

        private static void AppendToModelSpace(Database db, Transaction tr, Entity entity)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ms.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
        }

        private static void RunTest(
            string name, ref int passed, ref int failed, Editor ed, Action action)
        {
            try
            {
                action();
                passed++;
                ed.WriteMessage("\n[PASS] " + name);
            }
            catch (Exception ex)
            {
                failed++;
                ed.WriteMessage("\n[FAIL] " + name + "  ->  " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + message);
            }
        }
    }
}
