using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Globalization;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Bo kiem thu cua DPA, theo dung quy uoc test cua project (giong DX_FOIL_TEST / ACC_AUTO_CUT_TEST).
    ///   PHAN A: engine phan tich + bo tri thuan (DimPlineSelfTests) - khong dung AutoCAD.
    ///   PHAN B: tang AutoCAD - doc polyline, doc DIMSTYLE, tao dim, layer, DIMLFAC, an toan du lieu.
    /// Tat ca PHAN B chay tren Database trong bo nho nen khong dung den ban ve dang mo.
    /// </summary>
    public class AutoDimPlineTestCommand
    {
        [CommandMethod("DPA_TEST")]
        public void RunTests()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null)
            {
                return;
            }

            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n DPA - PHAN A: ENGINE PHAN TICH + BO TRI");
            ed.WriteMessage("\n==================================================");

            DimPlineTestReport engineReport = DimPlineSelfTests.Run();
            foreach (string line in engineReport.Lines)
            {
                ed.WriteMessage("\n" + line);
            }

            ed.WriteMessage("\n");
            ed.WriteMessage("\n==================================================");
            ed.WriteMessage("\n DPA - PHAN B: TANG AUTOCAD");
            ed.WriteMessage("\n==================================================");

            int passed = 0;
            int failed = 0;

            RunTest("B1: Doc LWPolyline kin thanh mo hinh thuan", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(200, 0), new Point2d(200, 100), new Point2d(0, 100));

                    DimPlineReadResult read = DimPlineCadReader.Read(pl);
                    Assert(read.Success, "phai doc duoc polyline");
                    AssertEqual(4, read.Input.Vertices.Count, "so dinh");
                    Assert(read.Input.Closed, "phai nhan biet la polyline kin");
                    AssertClose(200.0, read.Input.Vertices[1].X, "toa do dinh 2");
                    tr.Commit();
                }
            });

            RunTest("B2: Doc bulge cua doan cung", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(100, 0), new Point2d(100, 50));
                    pl.SetBulgeAt(1, Math.Tan(Math.PI / 8.0));

                    DimPlineReadResult read = DimPlineCadReader.Read(pl);
                    Assert(read.Success, "phai doc duoc polyline");
                    AssertClose(Math.Tan(Math.PI / 8.0), read.Input.Vertices[1].Bulge, "bulge phai duoc giu nguyen");

                    DimPlineAnalysis analysis = DimPlineAnalyzer.Analyze(read.Input, new AutoDimPlineSettings());
                    AssertEqual(1, analysis.ArcCount, "phai nhan ra 1 doan cung");
                    tr.Commit();
                }
            });

            RunTest("B3: Polyline kin bang mat duoc coi la kin that", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Closed = false nhung dinh cuoi trung dinh dau (rat pho bien khi import).
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 40),
                        new Point2d(0, 40), new Point2d(0, 0));

                    DimPlineReadResult read = DimPlineCadReader.Read(pl);
                    Assert(read.Success, "phai doc duoc polyline");
                    Assert(read.Input.Closed, "phai duoc coi la kin");
                    AssertEqual(4, read.Input.Vertices.Count, "dinh trung phai bi bo");
                    tr.Commit();
                }
            });

            RunTest("B4: Dim duoc tao dung tren layer da chon", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(200, 0), new Point2d(200, 100), new Point2d(0, 100));

                    AutoDimPlineSettings settings = DefaultSettings();
                    settings.DimensionLayer = "DPA_TEST_LAYER";

                    int created = CreateDimensions(db, tr, pl, settings);
                    Assert(created > 0, "phai tao duoc dim");

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    Assert(lt.Has("DPA_TEST_LAYER"), "layer phai duoc tao tu dong");

                    foreach (ObjectId id in GetDimensionIds(db, tr))
                    {
                        Dimension dim = (Dimension)tr.GetObject(id, OpenMode.ForRead);
                        AssertEqualText("DPA_TEST_LAYER", dim.Layer, "layer cua dim");
                    }

                    tr.Commit();
                }
            });

            RunTest("B5: DIMLFAC 0.25 khong lam sai hinh hoc do", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(200, 0), new Point2d(200, 100), new Point2d(0, 100));

                    AutoDimPlineSettings settings = DefaultSettings();
                    settings.LinearScale = 0.25;

                    CreateDimensions(db, tr, pl, settings);

                    bool sawWidth = false;
                    foreach (ObjectId id in GetDimensionIds(db, tr))
                    {
                        RotatedDimension dim = tr.GetObject(id, OpenMode.ForRead) as RotatedDimension;
                        Assert(dim != null, "dim ngang/doc phai la RotatedDimension");

                        AssertClose(0.25, dim.Dimlfac, "DIMLFAC phai duoc ghi de tren tung dim");

                        double measured = dim.XLine1Point.DistanceTo(dim.XLine2Point);
                        if (Math.Abs(measured - 200.0) < 1e-6)
                        {
                            sawWidth = true;

                            // Diem dat duong kich thuoc phai tinh theo DRAWING UNIT, khong bi
                            // nhan 0.25. Voi hinh chu nhat, dim ngang nam duoi day.
                            Assert(dim.DimLinePoint.Y < -1.0,
                                "duong kich thuoc phai cach hinh hoc theo drawing unit, thuc te " + dim.DimLinePoint.Y);
                        }
                    }

                    Assert(sawWidth, "phai co dim do dung 200 drawing unit");
                    tr.Commit();
                }
            });

            RunTest("B6: Khong dung toi dim da co san trong ban ve", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord ms = GetModelSpace(db, tr);

                    // Mot dim co san, khong lien quan gi toi polyline sap dim.
                    RotatedDimension existing = new RotatedDimension(
                        0.0,
                        new Point3d(500, 500, 0),
                        new Point3d(600, 500, 0),
                        new Point3d(550, 480, 0),
                        string.Empty,
                        db.Dimstyle);
                    ms.AppendEntity(existing);
                    tr.AddNewlyCreatedDBObject(existing, true);
                    ObjectId existingId = existing.ObjectId;
                    Point3d existingDimLine = existing.DimLinePoint;

                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 40), new Point2d(0, 40));

                    int created = CreateDimensions(db, tr, pl, DefaultSettings());
                    Assert(created > 0, "phai tao duoc dim moi");

                    RotatedDimension after = tr.GetObject(existingId, OpenMode.ForRead) as RotatedDimension;
                    Assert(after != null && !after.IsErased, "dim co san khong duoc bi xoa");
                    AssertClose(existingDimLine.X, after.DimLinePoint.X, "dim co san khong duoc bi di chuyen (X)");
                    AssertClose(existingDimLine.Y, after.DimLinePoint.Y, "dim co san khong duoc bi di chuyen (Y)");

                    AssertEqual(created + 1, GetDimensionIds(db, tr).Count, "tong so dim");
                    tr.Commit();
                }
            });

            RunTest("B7: Polyline goc khong bi sua", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 40),
                        new Point2d(20, 40), new Point2d(20, 70), new Point2d(0, 70));

                    int vertexCount = pl.NumberOfVertices;
                    bool closed = pl.Closed;
                    Point2d[] before = new Point2d[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        before[i] = pl.GetPoint2dAt(i);
                    }

                    CreateDimensions(db, tr, pl, DefaultSettings());

                    AssertEqual(vertexCount, pl.NumberOfVertices, "so dinh polyline khong duoc doi");
                    Assert(closed == pl.Closed, "trang thai kin/mo khong duoc doi");
                    for (int i = 0; i < vertexCount; i++)
                    {
                        AssertClose(before[i].X, pl.GetPoint2dAt(i).X, "toa do X dinh " + i);
                        AssertClose(before[i].Y, pl.GetPoint2dAt(i).Y, "toa do Y dinh " + i);
                    }

                    tr.Commit();
                }
            });

            RunTest("B8: Huy transaction thi khong con dim rac", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                {
                    ObjectId plineId;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Polyline pl = CreatePolyline(db, tr, true,
                            new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 40), new Point2d(0, 40));
                        plineId = pl.ObjectId;
                        tr.Commit();
                    }

                    // Tao dim roi KHONG Commit - dung y het duong thoat loi cua lenh that.
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Polyline pl = (Polyline)tr.GetObject(plineId, OpenMode.ForRead);
                        int created = CreateDimensions(db, tr, pl, DefaultSettings());
                        Assert(created > 0, "phai tao duoc dim trong transaction");
                        // Khong goi tr.Commit() -> Dispose se Abort.
                    }

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        AssertEqual(0, GetDimensionIds(db, tr).Count, "ban ve phai sach sau khi huy");
                        tr.Commit();
                    }
                }
            });

            RunTest("B9: Doc dung co chu / mui ten tu DIMSTYLE", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                {
                    db.Dimscale = 2.0;
                    db.Dimtxt = 3.0;
                    db.Dimasz = 4.0;
                    db.Dimdec = 1;

                    DimPlineStyle style = DimPlineCadReader.ReadStyle(db);
                    AssertClose(6.0, style.TextHeight, "DIMTXT * DIMSCALE");
                    AssertClose(8.0, style.ArrowSize, "DIMASZ * DIMSCALE");
                    AssertEqual(1, style.DecimalPlaces, "DIMDEC");

                    // Khoang cach dat dim khong bao gio duoc nho hon co chu that.
                    AutoDimPlineSettings settings = DefaultSettings();
                    settings.DistanceFromPline = 0.5;
                    settings.DimensionSpacing = 0.5;

                    DimPlineInput input = new DimPlineInput();
                    input.Closed = true;
                    input.Add(0, 0); input.Add(60, 0); input.Add(60, 40); input.Add(0, 40);

                    DimPlinePlan plan = DimPlinePlanner.Plan(input, settings, style);
                    Assert(plan.EffectiveDistance >= style.TextHeight,
                        "khoang cach phai duoc nang len theo co chu that");
                    Assert(plan.EffectiveSpacing >= style.TextHeight,
                        "buoc xep hang phai duoc nang len theo co chu that");
                }
            });

            RunTest("B10: Polyline khong nam trong mat phang XY bi tu choi ro rang", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, true,
                        new Point2d(0, 0), new Point2d(60, 0), new Point2d(60, 40), new Point2d(0, 40));
                    // Polyline vua tao da o trang thai ForWrite nen khong goi UpgradeOpen.
                    pl.Normal = new Vector3d(1, 0, 0);

                    DimPlineReadResult read = DimPlineCadReader.Read(pl);
                    Assert(!read.Success, "phai bi tu choi");
                    Assert(read.ErrorMessage.Length > 0, "phai co thong bao ly do");
                    tr.Commit();
                }
            });

            RunTest("B11: Bien dang nhieu bac tao dim that, khong chong nhau", ref passed, ref failed, ed, () =>
            {
                using (Database db = new Database(true, true))
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = CreatePolyline(db, tr, false,
                        new Point2d(0, 296), new Point2d(0, 256), new Point2d(64, 256),
                        new Point2d(64, 168), new Point2d(48, 168), new Point2d(48, 80),
                        new Point2d(88, 80), new Point2d(88, 0), new Point2d(0, 0));

                    DimPlineReadResult read = DimPlineCadReader.Read(pl);
                    Assert(read.Success, "phai doc duoc polyline");

                    DimPlineStyle style = DimPlineCadReader.ReadStyle(db);
                    DimPlinePlan plan = DimPlinePlanner.Plan(read.Input, DefaultSettings(), style);

                    Assert(plan.IsValid, "plan phai hop le");
                    AssertEqual(0, plan.CountTextOverlaps(0.0), "text dim de len nhau");
                    AssertEqual(0, plan.CountOverlappingDimLines(1e-9), "duong kich thuoc de len nhau");
                    AssertEqual(0, plan.CountDimLinesCrossingGeometry(), "duong kich thuoc cat qua net ve");

                    int created = CreateDimensions(db, tr, pl, DefaultSettings());
                    AssertEqual(plan.Placements.Count, created, "so dim tao ra phai dung bang ban ke hoach");
                    tr.Commit();
                }
            });

            ed.WriteMessage("\n");
            ed.WriteMessage("\n==================================================");
            ed.WriteMessage(string.Format(
                CultureInfo.InvariantCulture,
                "\n PHAN B: {0} PASS / {1} FAIL  (tong {2})", passed, failed, passed + failed));
            ed.WriteMessage(string.Format(
                CultureInfo.InvariantCulture,
                "\n TONG: {0} PASS / {1} FAIL",
                engineReport.Passed + passed, engineReport.Failed + failed));
            ed.WriteMessage("\n==================================================");
        }

        // ==================================================================================
        // TIEN ICH
        // ==================================================================================

        private static AutoDimPlineSettings DefaultSettings()
        {
            AutoDimPlineSettings s = new AutoDimPlineSettings();
            s.DistanceFromPline = 20.0;
            s.DimensionSpacing = 15.0;
            s.MinSegmentLength = 1.0;
            return s;
        }

        private static BlockTableRecord GetModelSpace(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }

        private static Polyline CreatePolyline(Database db, Transaction tr, bool closed, params Point2d[] points)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < points.Length; i++)
            {
                pl.AddVertexAt(i, points[i], 0.0, 0.0, 0.0);
            }

            pl.Closed = closed;

            BlockTableRecord ms = GetModelSpace(db, tr);
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return pl;
        }

        private static int CreateDimensions(Database db, Transaction tr, Polyline pl, AutoDimPlineSettings settings)
        {
            DimPlineReadResult read = DimPlineCadReader.Read(pl);
            Assert(read.Success, "doc polyline: " + read.ErrorMessage);

            DimPlineStyle style = DimPlineCadReader.ReadStyle(db);
            DimPlinePlan plan = DimPlinePlanner.Plan(read.Input, settings, style);
            Assert(plan.IsValid, "lap ke hoach: " + plan.ErrorMessage);

            ObjectId layerId = CadLayerHelper.EnsureLayer(db, tr, settings.DimensionLayer);
            BlockTableRecord ms = GetModelSpace(db, tr);

            DimPlineCreateResult created = DimPlineCadCreator.Create(
                tr, db, ms, layerId, plan, settings, read.Elevation);
            Assert(created.Success, "tao dim: " + created.ErrorMessage);

            return created.CreatedIds.Count;
        }

        private static System.Collections.Generic.List<ObjectId> GetDimensionIds(Database db, Transaction tr)
        {
            System.Collections.Generic.List<ObjectId> result = new System.Collections.Generic.List<ObjectId>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (id.IsErased)
                {
                    continue;
                }

                Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity is Dimension)
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private sealed class DpaTestException : Exception
        {
            public DpaTestException(string message) : base(message)
            {
            }
        }

        private static void RunTest(string name, ref int passed, ref int failed, Editor ed, Action test)
        {
            try
            {
                test();
                passed++;
                ed.WriteMessage("\nPASS  " + name);
            }
            catch (DpaTestException ex)
            {
                failed++;
                ed.WriteMessage("\nFAIL  " + name + "  ->  " + ex.Message);
            }
            catch (Exception ex)
            {
                failed++;
                ed.WriteMessage("\nFAIL  " + name + "  ->  EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new DpaTestException(message);
            }
        }

        private static void AssertEqual(int expected, int actual, string message)
        {
            if (expected != actual)
            {
                throw new DpaTestException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi {1}, nhan {2}", message, expected, actual));
            }
        }

        private static void AssertEqualText(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new DpaTestException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi '{1}', nhan '{2}'", message, expected, actual));
            }
        }

        private static void AssertClose(double expected, double actual, string message)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > 1e-6)
            {
                throw new DpaTestException(string.Format(
                    CultureInfo.InvariantCulture, "{0}: cho doi {1}, nhan {2}", message, expected, actual));
            }
        }
    }
}
