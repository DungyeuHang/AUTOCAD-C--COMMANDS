using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;

namespace AUTOCAD_COMMANDS
{
    public class ChiaDimCommands
    {
        [CommandMethod("CDD2_CHIADIM")]
        public void ChiaDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // ===== CHỌN DIM =====
            PromptEntityOptions peo =
                new PromptEntityOptions("\nChọn DIM cần chia: ");
            peo.SetRejectMessage("\nChỉ hỗ trợ DIM ngang / dọc.");
            peo.AddAllowedClass(typeof(RotatedDimension), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RotatedDimension dim =
                    tr.GetObject(per.ObjectId, OpenMode.ForWrite) as RotatedDimension;
                if (dim == null) return;

                Point3d pt1 = dim.XLine1Point;
                Point3d pt2 = dim.XLine2Point;
                Point3d ptDim = dim.DimLinePoint;

                Vector3d axis = (pt2 - pt1).GetNormal();

                // ===== CHỌN ĐIỂM CHIA =====
                List<double> parameters = new List<double> { 0.0, pt1.DistanceTo(pt2) };

                while (true)
                {
                    PromptPointOptions ppo =
                        new PromptPointOptions("\nChọn điểm chia (Enter / Space để kết thúc): ");
                    ppo.AllowNone = true;
                    ppo.AllowArbitraryInput = true;

                    PromptPointResult ppr = ed.GetPoint(ppo);

                    if (ppr.Status == PromptStatus.None)
                        break;

                    if (ppr.Status == PromptStatus.Cancel)
                        return;

                    // ===== CHIẾU ĐIỂM LÊN TRỤC DIM =====
                    Vector3d v = ppr.Value - pt1;
                    double t = v.DotProduct(axis);

                    // chỉ nhận điểm nằm trong đoạn DIM
                    if (t > 1e-6 && t < pt1.DistanceTo(pt2) - 1e-6)
                        parameters.Add(t);
                }

                // sort đúng theo trục
                parameters = parameters.Distinct().OrderBy(x => x).ToList();

                // ===== XOÁ DIM CŨ =====
                dim.Erase();

                // ===== ĐẢM BẢO LAYER =====
                const string dimLayerName = "_mss.kichthuoc";
                LayerTable lt =
                    tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                ObjectId dimLayerId;
                if (!lt.Has(dimLayerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord
                    {
                        Name = dimLayerName
                    };
                    dimLayerId = lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                else
                {
                    dimLayerId = lt[dimLayerName];
                }

                // ===== TẠO DIM MỚI =====
                BlockTable bt =
                    tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord btr =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite)
                    as BlockTableRecord;

                for (int i = 0; i < parameters.Count - 1; i++)
                {
                    Point3d pA = pt1 + axis * parameters[i];
                    Point3d pB = pt1 + axis * parameters[i + 1];

                    RotatedDimension newDim = new RotatedDimension
                    {
                        XLine1Point = pA,
                        XLine2Point = pB,
                        DimLinePoint = ptDim,
                        Rotation = dim.Rotation,
                        DimensionStyle = db.Dimstyle,
                        LayerId = dimLayerId
                    };

                    btr.AppendEntity(newDim);
                    tr.AddNewlyCreatedDBObject(newDim, true);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nChia DIM xong – logic chuẩn trục DIM 👍");
        }
    }

    // ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;; >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>> END OF CDD <<<<<<<<<<<<<<<<<<<<<<<<<<< ;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

    // START OF SAA
    public class AutoDimCommand
    {
        [CommandMethod("DAA_Dim_auto")]
        public void AutoDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // =============================
                // 1. CHỌN ĐỐI TƯỢNG GỐC
                // =============================
                PromptSelectionOptions baseOpt = new PromptSelectionOptions();
                baseOpt.MessageForAdding = "\nChọn Polyline hoặc nhóm đối tượng gốc:";
                PromptSelectionResult baseRes = ed.GetSelection(baseOpt);
                if (baseRes.Status != PromptStatus.OK) return;

                Extents3d baseExt = GetSelectionExtents(baseRes.Value, tr);
                Point3d baseCenter = GetCenter(baseExt);

                // =============================
                // 2. CHỌN ĐƯỜNG BAO (LINE / PLINE)
                // =============================
                PromptSelectionOptions boundOpt = new PromptSelectionOptions();
                boundOpt.MessageForAdding = "\nChọn các đường bao (Line / Polyline):";
                PromptSelectionResult boundRes = ed.GetSelection(boundOpt);
                if (boundRes.Status != PromptStatus.OK) return;

                Entity leftEntity = null;
                Entity rightEntity = null;
                Entity topEntity = null;
                Entity bottomEntity = null;

                foreach (SelectedObject sel in boundRes.Value)
                {
                    Entity ent = tr.GetObject(sel.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    Extents3d ext = ent.GeometricExtents;
                    Point3d center = GetCenter(ext);

                    double dx = center.X - baseCenter.X;
                    double dy = center.Y - baseCenter.Y;

                    // Ưu tiên trục lệch nhiều hơn
                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        // ===== TRÁI / PHẢI =====
                        if (dx < 0)
                        {
                            // TRÁI
                            if (leftEntity == null ||
                                center.X < GetCenter(leftEntity.GeometricExtents).X)
                            { leftEntity = ent; }
                        }
                        else
                        {
                            // PHẢI
                            if (rightEntity == null ||
                                center.X > GetCenter(rightEntity.GeometricExtents).X)
                            {
                                rightEntity = ent;
                            }
                        }
                    }



                    else
                    {
                        // TRÊN / DƯỚI
                        if (dy > 0)
                        {
                            if (topEntity == null ||
                                center.Y > GetCenter(topEntity.GeometricExtents).Y)
                                topEntity = ent;
                        }
                        else
                        {
                            if (bottomEntity == null ||
                                center.Y < GetCenter(bottomEntity.GeometricExtents).Y)
                                bottomEntity = ent;
                        }
                    }
                }

                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                BlockTableRecord ms =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // =============================
                // 3. OFFSET THEO DIMSTYLE
                // =============================
                double baseOffset =
                    db.Dimtxt + db.Dimexe + db.Dimgap;

                double offsetH = baseOffset * 6;
                double offsetV = baseOffset * 6;

                // =============================
                // 4. DIM TRÁI
                // =============================
                if (leftEntity != null)
                {
                    Extents3d ext = leftEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        0,
                        new Point3d(ext.MaxPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(0, baseExt.MinPoint.Y - offsetH * -1.5, 0)
                    );
                }

                // =============================
                // 5. DIM PHẢI
                // =============================
                if (rightEntity != null)
                {
                    Extents3d ext = rightEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        0,
                        new Point3d(baseExt.MaxPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(ext.MinPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(0, baseExt.MinPoint.Y - offsetH * -1.5, 0)
                    );
                }

                // =============================
                // 6. DIM TRÊN
                // =============================
                if (topEntity != null)
                {
                    Extents3d ext = topEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        Math.PI / 2,
                        new Point3d(baseExt.MinPoint.X, baseExt.MaxPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X, ext.MinPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X - offsetV * -1.5, 0, 0)
                    );
                }

                // =============================
                // 7. DIM DƯỚI
                // =============================
                if (bottomEntity != null)
                {
                    Extents3d ext = bottomEntity.GeometricExtents;

                    CreateDim(
                        ms, tr, db,
                        Math.PI / 2,
                        new Point3d(baseExt.MinPoint.X, ext.MaxPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X, baseExt.MinPoint.Y, 0),
                        new Point3d(baseExt.MinPoint.X - offsetV * -1.5, 0, 0)
                    );
                }

                tr.Commit();
            }
        }

        // ======================================================
        // HÀM TẠO DIM
        // ======================================================
        private void CreateDim(
            BlockTableRecord ms,
            Transaction tr,
            Database db,
            double angle,
            Point3d p1,
            Point3d p2,
            Point3d dimPoint)
        {
            RotatedDimension dim = new RotatedDimension(
                angle, p1, p2, dimPoint, "", db.Dimstyle);
            // 👉 SET LAYER Ở ĐÂY
            dim.Layer = "_mss.kichthuoc";
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        // ======================================================
        // LẤY EXTENTS CỦA SELECTION
        // ======================================================
        private Extents3d GetSelectionExtents(SelectionSet ss, Transaction tr)
        {
            Extents3d? ext = null;

            foreach (SelectedObject sel in ss)
            {
                Entity ent = tr.GetObject(sel.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ext == null)
                    ext = ent.GeometricExtents;
                else
                {
                    Extents3d e = ent.GeometricExtents;
                    ext = new Extents3d(
                        new Point3d(
                            Math.Min(ext.Value.MinPoint.X, e.MinPoint.X),
                            Math.Min(ext.Value.MinPoint.Y, e.MinPoint.Y),
                            0),
                        new Point3d(
                            Math.Max(ext.Value.MaxPoint.X, e.MaxPoint.X),
                            Math.Max(ext.Value.MaxPoint.Y, e.MaxPoint.Y),
                            0)
                    );
                }
            }
            return ext.Value;
        }

        // ======================================================
        // LẤY TÂM EXTENTS
        // ======================================================
        private Point3d GetCenter(Extents3d ext)
        {
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                0
            );
        }
    }





    public class SmartDimXCommand
    {
        private const string DimLayerName = "_mss.kichthuoc";
        private const double DirectionTolerance = 1e-6;
        private const double SearchDistance = 1000000.0;

        [CommandMethod("SDX")]
        public void SmartDimX()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            PromptPointOptions dirOpt =
                new PromptPointOptions("\nChọn điểm để xác định hướng X (+/-): ");
            dirOpt.BasePoint = startRes.Value;
            dirOpt.UseBasePoint = true;

            PromptPointResult dirRes = ed.GetPoint(dirOpt);
            if (dirRes.Status != PromptStatus.OK) return;

            double deltaX = dirRes.Value.X - startRes.Value.X;
            if (Math.Abs(deltaX) < DirectionTolerance)
            {
                ed.WriteMessage("\nĐiểm hướng phải lệch theo trục X.");
                return;
            }

            double direction = deltaX > 0 ? 1.0 : -1.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnXAxis(currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng X đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    targetPoint.Value.X,
                    startRes.Value.Y,
                    startRes.Value.Z);

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dirRes.Value,
                    Rotation = 0.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        [CommandMethod("SDY")]
        public void SmartDimY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            PromptPointOptions dirOpt =
                new PromptPointOptions("\nChọn điểm để xác định hướng Y (+/-): ");
            dirOpt.BasePoint = startRes.Value;
            dirOpt.UseBasePoint = true;

            PromptPointResult dirRes = ed.GetPoint(dirOpt);
            if (dirRes.Status != PromptStatus.OK) return;

            double deltaY = dirRes.Value.Y - startRes.Value.Y;
            if (Math.Abs(deltaY) < DirectionTolerance)
            {
                ed.WriteMessage("\nĐiểm hướng phải lệch theo trục Y.");
                return;
            }

            double direction = deltaY > 0 ? 1.0 : -1.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnYAxis(currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng Y đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    startRes.Value.X,
                    targetPoint.Value.Y,
                    startRes.Value.Z);

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dirRes.Value,
                    Rotation = Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        private Point3d? FindNearestPointOnXAxis(
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateScanLine(startPoint, direction))
            {
                foreach (ObjectId id in currentSpace)
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    if (entity is Dimension) continue;
                    if (!(entity is Curve)) continue;

                    Point3dCollection intersections =
                        TryGetIntersections(entity, scanLine);
                    if (intersections == null || intersections.Count == 0) continue;

                    foreach (Point3d point in intersections)
                    {
                        double projectedDistance =
                            (point.X - startPoint.X) * direction;

                        if (projectedDistance <= DirectionTolerance) continue;
                        if (projectedDistance >= bestDistance) continue;

                        bestDistance = projectedDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private Point3d? FindNearestPointOnYAxis(
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = CreateVerticalScanLine(startPoint, direction))
            {
                foreach (ObjectId id in currentSpace)
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased) continue;
                    if (entity is Dimension) continue;
                    if (!(entity is Curve)) continue;

                    Point3dCollection intersections =
                        TryGetIntersections(entity, scanLine);
                    if (intersections == null || intersections.Count == 0) continue;

                    foreach (Point3d point in intersections)
                    {
                        double projectedDistance =
                            (point.Y - startPoint.Y) * direction;

                        if (projectedDistance <= DirectionTolerance) continue;
                        if (projectedDistance >= bestDistance) continue;

                        bestDistance = projectedDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private Point3dCollection TryGetIntersections(Entity entity, Line scanLine)
        {
            try
            {
                Point3dCollection intersections = new Point3dCollection();
                entity.IntersectWith(
                    scanLine,
                    Intersect.OnBothOperands,
                    intersections,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return intersections;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
        }

        private Line CreateScanLine(Point3d startPoint, double direction)
        {
            Point3d endPoint = new Point3d(
                startPoint.X + SearchDistance * direction,
                startPoint.Y,
                startPoint.Z);

            return new Line(startPoint, endPoint);
        }

        private Line CreateVerticalScanLine(Point3d startPoint, double direction)
        {
            Point3d endPoint = new Point3d(
                startPoint.X,
                startPoint.Y + SearchDistance * direction,
                startPoint.Z);

            return new Line(startPoint, endPoint);
        }

        private ObjectId EnsureDimLayer(Database db, Transaction tr)
        {
            LayerTable layerTable =
                tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

            if (layerTable == null) return ObjectId.Null;

            if (layerTable.Has(DimLayerName))
                return layerTable[DimLayerName];

            layerTable.UpgradeOpen();

            LayerTableRecord layer = new LayerTableRecord
            {
                Name = DimLayerName
            };

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }
    }

    public class DungXPaletteEntry : IExtensionApplication
    {
        [CommandMethod("DXPALETTE")]
        public void ShowPalette()
        {
            DungXPaletteHost.ShowPalette();
        }

        [CommandMethod("DXPALETTERELOAD")]
        public void ReloadPalette()
        {
            DungXPaletteHost.ReloadPaletteData(true);
        }

        [CommandMethod("DXPALETTESETFOLDER")]
        public void SetLispFolder()
        {
            DungXPaletteHost.ChooseLispFolder(true);
        }

        public void Initialize()
        {
            DungXPaletteHost.Initialize();
        }

        public void Terminate()
        {
        }
    }

    internal static class DungXPaletteHost
    {
        private static readonly Guid PaletteGuid =
            new Guid("2E5D6E63-70A5-4D41-B72B-50BFC66F37D1");

        private static PaletteSet _paletteSet;
        private static DungXPaletteControl _paletteControl;

        public static void Initialize()
        {
            EnsurePalette();
        }

        public static void ShowPalette()
        {
            EnsurePalette();
            ReloadPaletteData(false);
            _paletteSet.Visible = true;
        }

        public static void ReloadPaletteData(bool showMessage)
        {
            EnsurePalette();
            _paletteControl.ReloadData(showMessage);
        }

        public static bool ChooseLispFolder(bool showMessage)
        {
            EnsurePalette();
            bool selected = DungXLispResolver.PickLispRoot(showMessage);
            if (selected)
            {
                _paletteControl.ReloadData(showMessage);
            }
            return selected;
        }

        public static void RunCommand(PaletteCommandItem item)
        {
            if (item == null)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                WF.MessageBox.Show(
                    "Khong co ban ve dang active.",
                    "DungX Palette",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return;
            }

            if (!EnsureSourceLoaded(doc, item))
            {
                _paletteControl.ReloadData(true);
                return;
            }

            doc.SendStringToExecute(item.CommandName + " ", true, false, false);
            _paletteControl.SetStatus(
                $"Dang chay {item.CommandName} | {item.SourceLabel}");
        }

        private static bool EnsureSourceLoaded(Document doc, PaletteCommandItem item)
        {
            if (item.SourceKind == PaletteSourceKind.BuiltInDll)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                WF.MessageBox.Show(
                    "Khong tim thay file nguon:\n" + item.SourcePath,
                    "DungX Palette",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning);
                return false;
            }

            if (item.SourceKind == PaletteSourceKind.ManagedDll)
            {
                string netloadExpr =
                    "_.NETLOAD \"" + item.SourcePath.Replace("\"", "\"\"") + "\" ";
                doc.SendStringToExecute(netloadExpr, true, false, false);
                return true;
            }

            string loadExpr = $"(load \"{EscapeForLisp(item.SourcePath)}\") ";
            doc.SendStringToExecute(loadExpr, true, false, false);
            return true;
        }

        private static string EscapeForLisp(string path)
        {
            return path
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static void EnsurePalette()
        {
            if (_paletteSet != null && _paletteControl != null)
            {
                return;
            }

            _paletteControl = new DungXPaletteControl();
            _paletteSet = new PaletteSet("DungX Commands", PaletteGuid)
            {
                Style = PaletteSetStyles.ShowAutoHideButton
                      | PaletteSetStyles.ShowCloseButton
                      | PaletteSetStyles.Snappable,
                MinimumSize = new Size(420, 320),
                Size = new Size(560, 700),
                DockEnabled = DockSides.Left | DockSides.Right,
                KeepFocus = false
            };

            _paletteSet.Add("Command List", _paletteControl);
        }
    }

    internal sealed class DungXPaletteControl : WF.UserControl
    {
        private static readonly Color BackgroundColor = Color.FromArgb(45, 45, 48);
        private static readonly Color PanelColor = Color.FromArgb(37, 37, 38);
        private static readonly Color BorderColor = Color.FromArgb(63, 63, 70);
        private static readonly Color ForegroundColor = Color.FromArgb(241, 241, 241);
        private static readonly Color AccentColor = Color.FromArgb(0, 122, 204);
        private static readonly Color SelectionColor = Color.FromArgb(62, 62, 64);

        private readonly WF.TextBox _searchBox;
        private readonly WF.ComboBox _sourceFilter;
        private readonly WF.DataGridView _commandGrid;
        private readonly WF.Button _runButton;
        private readonly WF.Button _reloadButton;
        private readonly WF.Button _folderButton;
        private readonly WF.Button _refreshButton;
        private readonly WF.Button _addSourceButton;
        private readonly WF.Button _removeSourceButton;
        private readonly WF.Label _statusLabel;
        private List<PaletteCommandItem> _items;

        public DungXPaletteControl()
        {
            Dock = WF.DockStyle.Fill;
            BackColor = BackgroundColor;
            ForeColor = ForegroundColor;
            Font = new System.Drawing.Font(
                "Segoe UI",
                9F,
                FontStyle.Regular,
                GraphicsUnit.Point);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WF.Padding(8),
                BackColor = BackgroundColor
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            Controls.Add(layout);

            WF.TableLayoutPanel filterPanel = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                ColumnCount = 4,
                AutoSize = true,
                BackColor = BackgroundColor
            };
            filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            layout.Controls.Add(filterPanel, 0, 0);

            WF.Label searchLabel = CreateLabel("Search");
            searchLabel.Margin = new WF.Padding(0, 6, 8, 0);
            filterPanel.Controls.Add(searchLabel, 0, 0);

            _searchBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 0, 12, 0),
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                BorderStyle = WF.BorderStyle.FixedSingle
            };
            _searchBox.TextChanged += (_, __) => BindGrid();
            filterPanel.Controls.Add(_searchBox, 1, 0);

            WF.Label sourceLabel = CreateLabel("Source");
            sourceLabel.Margin = new WF.Padding(0, 6, 8, 0);
            filterPanel.Controls.Add(sourceLabel, 2, 0);

            _sourceFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _sourceFilter.Items.AddRange(new object[] { "All", "DUNGX Custom", "DUNGX 2" });
            _sourceFilter.SelectedIndex = 0;
            _sourceFilter.SelectedIndexChanged += (_, __) => BindGrid();
            filterPanel.Controls.Add(_sourceFilter, 3, 0);

            WF.FlowLayoutPanel buttonPanel = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new WF.Padding(0, 8, 0, 8),
                BackColor = BackgroundColor
            };
            layout.Controls.Add(buttonPanel, 0, 1);

            _runButton = CreateButton("Run", (_, __) => RunSelected());
            _reloadButton = CreateButton("Reload LISP", (_, __) => ReloadLisps());
            _folderButton = CreateButton("LISP Folder", (_, __) => PickFolder());
            _addSourceButton = CreateButton("Add Source", (_, __) => AddSource());
            _removeSourceButton = CreateButton("Remove Source", (_, __) => RemoveSelectedSource());
            _refreshButton = CreateButton("Refresh List", (_, __) => ReloadData(true));

            buttonPanel.Controls.Add(_runButton);
            buttonPanel.Controls.Add(_reloadButton);
            buttonPanel.Controls.Add(_folderButton);
            buttonPanel.Controls.Add(_addSourceButton);
            buttonPanel.Controls.Add(_removeSourceButton);
            buttonPanel.Controls.Add(_refreshButton);

            _commandGrid = CreateGrid();
            _commandGrid.CellDoubleClick += CommandGrid_CellDoubleClick;
            _commandGrid.KeyDown += CommandGrid_KeyDown;
            _commandGrid.CellEndEdit += CommandGrid_CellEndEdit;
            layout.Controls.Add(_commandGrid, 0, 2);

            _statusLabel = CreateLabel("San sang");
            _statusLabel.Dock = WF.DockStyle.Fill;
            _statusLabel.Padding = new WF.Padding(0, 8, 0, 0);
            layout.Controls.Add(_statusLabel, 0, 3);

            _items = new List<PaletteCommandItem>();
            ReloadData(false);
        }

        public void ReloadData(bool showMessage)
        {
            string currentFilter = Convert.ToString(_sourceFilter.SelectedItem) ?? "All";
            _items = PaletteCommandCatalog.BuildItems();
            RefreshSourceFilter(currentFilter);
            BindGrid();

            string root = DungXLispResolver.GetDisplayRoot();
            bool ready = DungXLispResolver.TryResolveAllLispFiles(out _, out _);
            string status = ready
                ? $"Ready | LISP root: {root} | Tu dong quet command OK"
                : $"Chua thay du file LISP | Root hien tai: {root}";

            SetStatus(status);

            if (showMessage)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n" + status);
            }
        }

        public void SetStatus(string message)
        {
            _statusLabel.Text = message;
        }

        private void BindGrid()
        {
            string search = (_searchBox.Text ?? string.Empty).Trim();
            string source = Convert.ToString(_sourceFilter.SelectedItem) ?? "All";

            IEnumerable<PaletteCommandItem> filtered = _items;

            if (!string.Equals(source, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(
                    item => string.Equals(item.SourceLabel, source, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(item =>
                    item.CommandName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.SourceLabel.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _commandGrid.Rows.Clear();

            foreach (PaletteCommandItem item in filtered)
            {
                int rowIndex =
                    _commandGrid.Rows.Add(item.CommandName, item.Description, item.SourceLabel);
                _commandGrid.Rows[rowIndex].Tag = item;
            }

            if (_commandGrid.Rows.Count > 0)
            {
                _commandGrid.ClearSelection();
                _commandGrid.Rows[0].Selected = true;
                _commandGrid.CurrentCell = _commandGrid.Rows[0].Cells[0];
            }
        }

        private static WF.DataGridView CreateGrid()
        {
            WF.DataGridView grid = new WF.DataGridView
            {
                Dock = WF.DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = WF.DataGridViewSelectionMode.FullRowSelect,
                EditMode = WF.DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = PanelColor,
                BorderStyle = WF.BorderStyle.FixedSingle,
                GridColor = BorderColor,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false
            };

            grid.ColumnHeadersBorderStyle = WF.DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ForegroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = ForegroundColor;

            grid.DefaultCellStyle.BackColor = PanelColor;
            grid.DefaultCellStyle.ForeColor = ForegroundColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.DefaultCellStyle.SelectionForeColor = ForegroundColor;
            grid.DefaultCellStyle.Padding = new WF.Padding(4, 2, 4, 2);

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(42, 42, 44);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = ForegroundColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = ForegroundColor;

            WF.DataGridViewTextBoxColumn commandColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Command",
                HeaderText = "Command",
                Width = 140,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn descriptionColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "Description",
                AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.Fill,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn sourceColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Source",
                HeaderText = "Source",
                Width = 120,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };

            grid.Columns.AddRange(commandColumn, descriptionColumn, sourceColumn);
            return grid;
        }

        private static WF.Label CreateLabel(string text)
        {
            return new WF.Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = ForegroundColor,
                BackColor = BackgroundColor,
                Anchor = WF.AnchorStyles.Left
            };
        }

        private static WF.Button CreateButton(string text, EventHandler onClick)
        {
            WF.Button button = new WF.Button
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 8, 0),
                Padding = new WF.Padding(10, 4, 10, 4),
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseDownBackColor = SelectionColor;
            button.FlatAppearance.MouseOverBackColor = AccentColor;
            button.Click += onClick;
            return button;
        }

        private void CommandGrid_CellDoubleClick(object sender, WF.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            if (e.ColumnIndex != 1)
            {
                RunSelected();
            }
        }

        private void CommandGrid_KeyDown(object sender, WF.KeyEventArgs e)
        {
            if (e.KeyCode == WF.Keys.Enter && _commandGrid.CurrentCell != null)
            {
                if (_commandGrid.CurrentCell.ColumnIndex == 1 && _commandGrid.IsCurrentCellInEditMode)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                RunSelected();
            }
        }

        private void CommandGrid_CellEndEdit(object sender, WF.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1)
            {
                return;
            }

            WF.DataGridViewRow row = _commandGrid.Rows[e.RowIndex];
            PaletteCommandItem item = row.Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            string description = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
            item.Description = description.Trim();
            PaletteDescriptionStore.SaveDescription(item.CommandName, item.Description);
            SetStatus($"Da luu mo ta cho {item.CommandName}");
        }

        private void RunSelected()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon command.");
                return;
            }

            DungXPaletteHost.RunCommand(item);
        }

        private void ReloadLisps()
        {
            bool ok = DungXLispResolver.TryEnsureAllLispFiles(showPrompt: true, out List<string> missing);
            if (ok)
            {
                SetStatus("Da kiem tra xong 2 file LISP, san sang chay.");
            }
            else
            {
                SetStatus("Thieu file LISP: " + string.Join(", ", missing.Select(Path.GetFileName)));
            }

            ReloadData(false);
        }

        private void PickFolder()
        {
            bool selected = DungXPaletteHost.ChooseLispFolder(false);
            SetStatus(selected
                ? "Da cap nhat thu muc LISP."
                : "Khong thay doi thu muc LISP.");
        }

        private void AddSource()
        {
            using (WF.OpenFileDialog dialog = new WF.OpenFileDialog())
            {
                dialog.Title = "Chon file .dll, .lsp hoac .vlx de them vao palette";
                dialog.Filter = "Supported files|*.dll;*.lsp;*.vlx|DLL|*.dll|LISP|*.lsp|VLX|*.vlx";
                dialog.Multiselect = true;

                if (dialog.ShowDialog() != WF.DialogResult.OK)
                {
                    return;
                }

                int added = PaletteSourceStore.AddSources(dialog.FileNames);
                ReloadData(false);
                SetStatus($"Da them {added} source moi.");
            }
        }

        private void RemoveSelectedSource()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon dong nao de xoa source.");
                return;
            }

            if (string.IsNullOrWhiteSpace(item.SourcePath) ||
                !PaletteSourceStore.Contains(item.SourcePath))
            {
                SetStatus("Dong dang chon khong phai source ngoai de xoa.");
                return;
            }

            PaletteSourceStore.RemoveSource(item.SourcePath);
            ReloadData(false);
            SetStatus("Da xoa source khoi palette.");
        }

        private void RefreshSourceFilter(string preferredSelection)
        {
            List<string> sources = _items
                .Select(item => item.SourceLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _sourceFilter.Items.Clear();
            _sourceFilter.Items.Add("All");
            foreach (string source in sources)
            {
                _sourceFilter.Items.Add(source);
            }

            int selectedIndex = 0;
            for (int i = 0; i < _sourceFilter.Items.Count; i++)
            {
                if (string.Equals(
                    Convert.ToString(_sourceFilter.Items[i]),
                    preferredSelection,
                    StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            _sourceFilter.SelectedIndex = selectedIndex;
        }

        private PaletteCommandItem GetSelectedItem()
        {
            if (_commandGrid.SelectedRows.Count == 0)
            {
                return null;
            }

            return _commandGrid.SelectedRows[0].Tag as PaletteCommandItem;
        }
    }

    internal sealed class PaletteCommandItem
    {
        public PaletteCommandItem(
            string commandName,
            string description,
            string sourceLabel,
            PaletteSourceKind sourceKind,
            string sourcePath)
        {
            CommandName = commandName;
            Description = description ?? string.Empty;
            SourceLabel = sourceLabel;
            SourceKind = sourceKind;
            SourcePath = sourcePath ?? string.Empty;
        }

        public string CommandName { get; }

        public string Description { get; set; }

        public string SourceLabel { get; }

        public PaletteSourceKind SourceKind { get; }

        public string SourcePath { get; }
    }

    internal enum PaletteSourceKind
    {
        BuiltInDll,
        Lisp,
        ManagedDll,
        Vlx
    }

    internal sealed class PaletteSourceFile
    {
        public PaletteSourceFile(string filePath, string displayName, PaletteSourceKind sourceKind)
        {
            FilePath = filePath;
            DisplayName = displayName;
            SourceKind = sourceKind;
        }

        public string FilePath { get; }

        public string DisplayName { get; }

        public PaletteSourceKind SourceKind { get; }
    }

    internal static class DungXLispResolver
    {
        private static readonly string[] RequiredLispFiles =
        {
            "DUNGX Custom Command.LSP",
            "DUNGX 2.LSP"
        };

        private static readonly string ConfigFilePath =
            Path.Combine(GetAssemblyDirectory(), "dungx_lisp_root.txt");

        public static string GetDisplayRoot()
        {
            return GetCurrentRoot() ?? "<chua set>";
        }

        public static bool TryEnsureAllLispFiles(bool showPrompt, out List<string> missing)
        {
            if (TryResolveAllLispFiles(out _, out missing))
            {
                return true;
            }

            if (!showPrompt)
            {
                return false;
            }

            bool selected = PickLispRoot(true);
            if (!selected)
            {
                return false;
            }

            return TryResolveAllLispFiles(out _, out missing);
        }

        public static bool TryResolveAllLispFiles(out List<string> paths, out List<string> missing)
        {
            paths = new List<string>();
            missing = new List<string>();

            string root = GetCurrentRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                missing.AddRange(RequiredLispFiles);
                return false;
            }

            foreach (string fileName in RequiredLispFiles)
            {
                string fullPath = Path.Combine(root, fileName);
                if (File.Exists(fullPath))
                {
                    paths.Add(fullPath);
                }
                else
                {
                    missing.Add(fullPath);
                }
            }

            return missing.Count == 0;
        }

        public static IReadOnlyList<string> GetResolvedLispFiles()
        {
            if (TryResolveAllLispFiles(out List<string> paths, out _))
            {
                return paths;
            }

            return new List<string>();
        }

        public static bool PickLispRoot(bool showMessage)
        {
            using (WF.FolderBrowserDialog dialog = new WF.FolderBrowserDialog())
            {
                dialog.Description = "Chon thu muc chua DUNGX Custom Command.LSP va DUNGX 2.LSP";
                dialog.SelectedPath = GetCurrentRoot() ?? GetAssemblyDirectory();

                if (dialog.ShowDialog() != WF.DialogResult.OK)
                {
                    return false;
                }

                File.WriteAllText(ConfigFilePath, dialog.SelectedPath);

                if (showMessage)
                {
                    WF.MessageBox.Show(
                        "Da luu thu muc LISP:\n" + dialog.SelectedPath,
                        "DungX Palette",
                        WF.MessageBoxButtons.OK,
                        WF.MessageBoxIcon.Information);
                }

                return true;
            }
        }

        private static string GetCurrentRoot()
        {
            string[] candidates =
            {
                TryReadConfigFile(),
                GetAssemblyDirectory(),
                Path.Combine(GetAssemblyDirectory(), "LISP"),
                Path.GetDirectoryName(GetAssemblyDirectory())
            };

            foreach (string candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                bool hasAllFiles = RequiredLispFiles.All(file => File.Exists(Path.Combine(candidate, file)));
                if (hasAllFiles)
                {
                    return candidate;
                }
            }

            return TryReadConfigFile() ?? GetAssemblyDirectory();
        }

        private static string TryReadConfigFile()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return null;
            }

            string path = File.ReadAllText(ConfigFilePath).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        private static string GetAssemblyDirectory()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(assemblyPath) ?? string.Empty;
        }
    }

    internal static class PaletteDescriptionStore
    {
        private static readonly string DescriptionFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_descriptions.tsv");

        public static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(DescriptionFilePath))
            {
                return map;
            }

            foreach (string line in File.ReadAllLines(DescriptionFilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length == 0)
                {
                    continue;
                }

                string commandName = parts[0].Trim();
                string description = parts.Length > 1 ? parts[1] : string.Empty;
                if (!string.IsNullOrWhiteSpace(commandName))
                {
                    map[commandName] = description;
                }
            }

            return map;
        }

        public static void SaveDescription(string commandName, string description)
        {
            Dictionary<string, string> map = Load();
            map[commandName] = description ?? string.Empty;

            List<string> lines = map
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key + "\t" + (kvp.Value ?? string.Empty))
                .ToList();

            File.WriteAllLines(DescriptionFilePath, lines, Encoding.UTF8);
        }
    }

    internal static class PaletteSourceStore
    {
        private static readonly string SourceFilePath =
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dungx_palette_sources.tsv");

        public static List<PaletteSourceFile> LoadSources()
        {
            List<PaletteSourceFile> result = new List<PaletteSourceFile>();
            if (!File.Exists(SourceFilePath))
            {
                return result;
            }

            foreach (string rawLine in File.ReadAllLines(SourceFilePath, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string filePath = line.Split('\t')[0].Trim();
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                if (!TryCreateSource(filePath, out PaletteSourceFile source))
                {
                    continue;
                }

                result.Add(source);
            }

            return result
                .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public static int AddSources(IEnumerable<string> filePaths)
        {
            List<string> existing = LoadRawPaths();
            int added = 0;

            foreach (string filePath in filePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string normalized = Path.GetFullPath(filePath);
                if (existing.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!TryCreateSource(normalized, out _))
                {
                    continue;
                }

                existing.Add(normalized);
                added++;
            }

            SaveRawPaths(existing);
            return added;
        }

        public static void RemoveSource(string filePath)
        {
            List<string> existing = LoadRawPaths()
                .Where(path => !string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveRawPaths(existing);
        }

        public static bool Contains(string filePath)
        {
            return LoadRawPaths().Any(
                path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryCreateSource(string filePath, out PaletteSourceFile source)
        {
            source = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            string displayName = Path.GetFileName(filePath);

            switch (extension)
            {
                case ".lsp":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.Lisp);
                    return true;
                case ".vlx":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.Vlx);
                    return true;
                case ".dll":
                    source = new PaletteSourceFile(filePath, displayName, PaletteSourceKind.ManagedDll);
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> LoadRawPaths()
        {
            if (!File.Exists(SourceFilePath))
            {
                return new List<string>();
            }

            return File.ReadAllLines(SourceFilePath, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void SaveRawPaths(IEnumerable<string> filePaths)
        {
            List<string> lines = filePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            File.WriteAllLines(SourceFilePath, lines, Encoding.UTF8);
        }
    }

    internal static class PaletteCommandCatalog
    {
        private static readonly Regex LispCommandRegex =
            new Regex(@"\(\s*defun\s+[cC]:(?<name>[^\s\(/]+)", RegexOptions.Compiled);
        private static readonly Regex BinaryCommandRegex =
            new Regex(@"(?i)(?:\(\s*defun\s+c:|c:)(?<name>[a-z0-9_\-$]+)", RegexOptions.Compiled);

        public static List<PaletteCommandItem> BuildItems()
        {
            Dictionary<string, string> savedDescriptions = PaletteDescriptionStore.Load();
            List<PaletteCommandItem> result = new List<PaletteCommandItem>();
            Dictionary<string, PaletteCommandItem> unique =
                new Dictionary<string, PaletteCommandItem>(StringComparer.OrdinalIgnoreCase);

            foreach (PaletteCommandItem item in ParseManagedDll(
                Assembly.GetExecutingAssembly(),
                "This DLL",
                Assembly.GetExecutingAssembly().Location,
                PaletteSourceKind.BuiltInDll))
            {
                AddOrReplace(result, unique, item, savedDescriptions);
            }

            if (DungXLispResolver.TryResolveAllLispFiles(out List<string> coreLispFiles, out _))
            {
                foreach (string filePath in coreLispFiles)
                {
                    string sourceLabel = filePath.EndsWith("DUNGX 2.LSP", StringComparison.OrdinalIgnoreCase)
                        ? "DUNGX 2"
                        : "DUNGX Custom";

                    foreach (PaletteCommandItem item in ParseLispFile(
                        filePath,
                        sourceLabel,
                        PaletteSourceKind.Lisp))
                    {
                        AddOrReplace(result, unique, item, savedDescriptions);
                    }
                }
            }

            foreach (PaletteSourceFile source in PaletteSourceStore.LoadSources())
            {
                IEnumerable<PaletteCommandItem> items = Enumerable.Empty<PaletteCommandItem>();

                switch (source.SourceKind)
                {
                    case PaletteSourceKind.Lisp:
                        items = ParseLispFile(source.FilePath, source.DisplayName, source.SourceKind);
                        break;
                    case PaletteSourceKind.ManagedDll:
                        items = ParseManagedDll(source.FilePath, source.DisplayName, source.SourceKind);
                        break;
                    case PaletteSourceKind.Vlx:
                        items = ParseVlxFile(source.FilePath, source.DisplayName);
                        break;
                }

                foreach (PaletteCommandItem item in items)
                {
                    AddOrReplace(result, unique, item, savedDescriptions);
                }
            }

            return result;
        }

        private static void AddOrReplace(
            List<PaletteCommandItem> result,
            Dictionary<string, PaletteCommandItem> unique,
            PaletteCommandItem item,
            Dictionary<string, string> savedDescriptions)
        {
            if (savedDescriptions.TryGetValue(item.CommandName, out string savedDescription))
            {
                item.Description = savedDescription;
            }

            if (unique.TryGetValue(item.CommandName, out PaletteCommandItem existing))
            {
                result.Remove(existing);
            }

            unique[item.CommandName] = item;
            result.Add(item);
        }

        private static IEnumerable<PaletteCommandItem> ParseLispFile(
            string filePath,
            string sourceLabel,
            PaletteSourceKind sourceKind)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();
            string pendingComment = string.Empty;

            foreach (string rawLine in File.ReadAllLines(filePath, Encoding.Default))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith(";", StringComparison.Ordinal))
                {
                    string cleaned = CleanComment(line);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        pendingComment = cleaned;
                    }
                    continue;
                }

                Match match = LispCommandRegex.Match(line);
                if (!match.Success)
                {
                    pendingComment = string.Empty;
                    continue;
                }

                string commandName = match.Groups["name"].Value.Trim();
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    pendingComment = string.Empty;
                    continue;
                }

                items.Add(new PaletteCommandItem(
                    commandName,
                    pendingComment,
                    sourceLabel,
                    sourceKind,
                    filePath));
                pendingComment = string.Empty;
            }

            return items;
        }

        private static IEnumerable<PaletteCommandItem> ParseManagedDll(
            string assemblyPath,
            string sourceLabel,
            PaletteSourceKind sourceKind)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                return ParseManagedDll(assembly, sourceLabel, assemblyPath, sourceKind);
            }
            catch
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }
        }

        private static IEnumerable<PaletteCommandItem> ParseManagedDll(
            Assembly assembly,
            string sourceLabel,
            string assemblyPath,
            PaletteSourceKind sourceKind)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();

            foreach (Type type in GetLoadableTypes(assembly))
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static))
                {
                    object[] attrs = method.GetCustomAttributes(typeof(CommandMethodAttribute), false);
                    foreach (CommandMethodAttribute attr in attrs.OfType<CommandMethodAttribute>())
                    {
                        string commandName = attr.GlobalName;
                        if (string.IsNullOrWhiteSpace(commandName))
                        {
                            continue;
                        }

                        items.Add(new PaletteCommandItem(
                            commandName,
                            type.Name + "." + method.Name,
                            sourceLabel,
                            sourceKind,
                            assemblyPath));
                    }
                }
            }

            return items;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private static IEnumerable<PaletteCommandItem> ParseVlxFile(string filePath, string sourceLabel)
        {
            List<PaletteCommandItem> items = new List<PaletteCommandItem>();

            try
            {
                string text = Encoding.Default.GetString(File.ReadAllBytes(filePath));
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match match in BinaryCommandRegex.Matches(text))
                {
                    string name = match.Groups["name"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(name) || names.Contains(name))
                    {
                        continue;
                    }

                    names.Add(name);
                    items.Add(new PaletteCommandItem(
                        name,
                        "VLX scan (best effort)",
                        sourceLabel,
                        PaletteSourceKind.Vlx,
                        filePath));
                }
            }
            catch
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }

            return items;
        }

        private static string CleanComment(string line)
        {
            string cleaned = Regex.Replace(line, @"^\s*;+", string.Empty).Trim();
            cleaned = cleaned.Trim('-', '=', '<', '>', '*', ':', ';', ' ');

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (cleaned.Length > 80)
            {
                cleaned = cleaned.Substring(0, 80).Trim();
            }

            return cleaned;
        }
    }

    // END OF A COMMAND
}





