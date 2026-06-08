using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;


namespace AUTOCAD_COMMANDS
{



    // ======================================================
    // SDXY - SMART DIM THEO TRỤC X/Y
    // Mục đích: click điểm đầu, click điểm hướng, tự dim tới đối tượng gần điểm hướng nhất.
    // Ghi chú: SmartDimX/SmartDimY cũ vẫn còn trong class nhưng command chính đang dùng là SDXY.
    // ======================================================
    public class SmartDimXCommand
    {
        private const string DimLayerName = "_mss.kichthuoc";
        private const double DirectionTolerance = 1e-6;
        private const double PreviewPointTolerance = 1e-4;
        private const double SearchDistance = 1000000.0;
        private static readonly RXClass CurveRxClass = RXObject.GetClass(typeof(Curve));
        private static readonly RXClass DimensionRxClass = RXObject.GetClass(typeof(Dimension));
        private static readonly RXClass EntityRxClass = RXObject.GetClass(typeof(Entity));
        private static List<SdxyEntityTypeChoice> _cachedSdxyEntityTypeChoices;
        private static ViewTableRecord _pendingSdxyViewRestore;
        private static bool _sdxyViewRestoreScheduled;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public void SmartDimX()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            if (!TryPromptAxisDirection(
                ed,
                startRes.Value,
                "\nChọn điểm để xác định hướng X (+/-): ",
                true,
                out Point3d dirPoint,
                out double direction))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnXAxis(ed, currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng X đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    targetPoint.Value.X,
                    dirPoint.Y,
                    dirPoint.Z);

                if (startRes.Value.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }

                ObjectId dimLayerId = EnsureDimLayer(db, tr);
                currentSpace.UpgradeOpen();

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startRes.Value,
                    XLine2Point = endPoint,
                    DimLinePoint = dirPoint,
                    Rotation = 0.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        public void SmartDimY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptPointResult startRes =
                ed.GetPoint("\nChọn điểm đầu dim: ");
            if (startRes.Status != PromptStatus.OK) return;

            if (!TryPromptAxisDirection(
                ed,
                startRes.Value,
                "\nChọn điểm để xác định hướng Y (+/-): ",
                false,
                out Point3d dirPoint,
                out double direction))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint =
                    FindNearestPointOnYAxis(ed, currentSpace, tr, startRes.Value, direction);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng Y đã chọn.");
                    return;
                }

                Point3d endPoint = new Point3d(
                    dirPoint.X,
                    targetPoint.Value.Y,
                    dirPoint.Z);

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
                    DimLinePoint = dirPoint,
                    Rotation = Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        // SDXY:
        // - Tự chọn trục X/Y theo hướng click.
        // - Điểm click thứ 2 chỉ dùng để xác định hướng và dò target.
        // - Sau khi tìm được điểm cuối, người dùng tự chọn điểm đặt DIM.
        // - Nhờ vậy có thể dim vượt qua các đối tượng trung gian gần điểm đầu.
        [CommandMethod("SDXY")]
        public void SmartDimXY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            SdxyTargetSettings settings = SdxyTargetSettingsStore.Load();
            if (!TryPromptSdxyStartPoint(ed, db, ref settings, out Point3d startPoint))
            {
                return;
            }

            if (!TryPromptAxisDirection(
                ed,
                startPoint,
                "\nChọn điểm để xác định hướng dim X/Y (nhấn Shift để đổi X/Y): ",
                null,
                out Point3d dirPoint,
                out double direction,
                out bool useXAxis))
            {
                return;
            }

            Point3d endPoint;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;

                if (currentSpace == null) return;

                Point3d? targetPoint = useXAxis
                    ? FindNearestPointOnXAxisFromProbe(
                        ed,
                        currentSpace,
                        tr,
                        startPoint,
                        dirPoint,
                        direction,
                        settings)
                    : FindNearestPointOnYAxisFromProbe(
                        ed,
                        currentSpace,
                        tr,
                        startPoint,
                        dirPoint,
                        direction,
                        settings);

                if (!targetPoint.HasValue)
                {
                    ed.WriteMessage(
                        useXAxis
                            ? "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng X đã chọn."
                            : "\nKhông tìm thấy đối tượng nào gần nhất theo đúng hướng Y đã chọn.");
                    return;
                }

                endPoint = useXAxis
                    ? new Point3d(targetPoint.Value.X, dirPoint.Y, dirPoint.Z)
                    : new Point3d(dirPoint.X, targetPoint.Value.Y, dirPoint.Z);

                if (startPoint.DistanceTo(endPoint) < DirectionTolerance)
                {
                    ed.WriteMessage("\nKhoảng dim quá nhỏ hoặc trùng điểm đầu.");
                    return;
                }
            }

            if (!TryPromptDimPlacementPoint(
                ed,
                db,
                startPoint,
                endPoint,
                useXAxis,
                out Point3d dimPlacementPoint,
                out bool finalUseXAxis))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                if (currentSpace == null) return;

                ObjectId dimLayerId = EnsureDimLayer(db, tr);

                RotatedDimension dim = new RotatedDimension
                {
                    XLine1Point = startPoint,
                    XLine2Point = endPoint,
                    DimLinePoint = dimPlacementPoint,
                    Rotation = finalUseXAxis ? 0.0 : Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle,
                    LayerId = dimLayerId
                };

                currentSpace.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);
                tr.Commit();
            }
        }

        [CommandMethod("SDXYSETTINGS")]
        public void ConfigureSmartDimXY()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            SdxyTargetSettings settings = SdxyTargetSettingsStore.Load();

            if (PromptForSdxySettings(ed, db, ref settings))
            {
                ed.WriteMessage($"\nSDXY: đã lưu setting target. {BuildSdxySettingsSummary(settings)}");
            }
        }

        private bool TryPromptDimPlacementPoint(
            Editor ed,
            Database db,
            Point3d startPoint,
            Point3d endPoint,
            bool useXAxis,
            out Point3d dimPlacementPoint,
            out bool finalUseXAxis)
        {
            using (SmartDimPlacementPrompt prompt =
                new SmartDimPlacementPrompt(ed, db, startPoint, endPoint, useXAxis))
            {
                if (prompt.Prompt(out dimPlacementPoint, out finalUseXAxis))
                {
                    return true;
                }
            }

            dimPlacementPoint = Point3d.Origin;
            finalUseXAxis = useXAxis;
            return false;
        }

        private static void ScheduleSdxyViewRestore(ViewTableRecord view)
        {
            if (view == null)
            {
                return;
            }

            _pendingSdxyViewRestore?.Dispose();
            _pendingSdxyViewRestore = view;

            if (_sdxyViewRestoreScheduled)
            {
                return;
            }

            try
            {
                Application.Idle += RestoreSdxyViewOnIdle;
                _sdxyViewRestoreScheduled = true;
            }
            catch
            {
                _pendingSdxyViewRestore?.Dispose();
                _pendingSdxyViewRestore = null;
                _sdxyViewRestoreScheduled = false;
            }
        }

        private static void RestoreSdxyViewOnIdle(object sender, EventArgs e)
        {
            try
            {
                Application.Idle -= RestoreSdxyViewOnIdle;
            }
            catch
            {
            }

            _sdxyViewRestoreScheduled = false;

            ViewTableRecord view = _pendingSdxyViewRestore;
            _pendingSdxyViewRestore = null;
            if (view == null)
            {
                return;
            }

            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor?.SetCurrentView(view);
            }
            catch
            {
            }
            finally
            {
                view.Dispose();
            }
        }

        private bool TryPromptAxisDirection(
            Editor ed,
            Point3d startPoint,
            string message,
            bool? forceXAxis,
            out Point3d pointResult,
            out double direction)
        {
            return TryPromptAxisDirection(
                ed,
                startPoint,
                message,
                forceXAxis,
                out pointResult,
                out direction,
                out _);
        }

        private bool TryPromptAxisDirection(
            Editor ed,
            Point3d startPoint,
            string message,
            bool? forceXAxis,
            out Point3d pointResult,
            out double direction,
            out bool useXAxis)
        {
            // Nếu forceXAxis có giá trị thì chỉ chấp nhận hướng theo đúng trục đó.
            // Nếu forceXAxis = null thì chọn trục có độ lệch lớn hơn giữa X và Y.
            while (true)
            {
                PromptStatus promptStatus;

                using (AxisDirectionPreviewJig jig =
                    new AxisDirectionPreviewJig(startPoint, message, forceXAxis))
                {
                    PromptResult dragResult = ed.Drag(jig);
                    promptStatus = dragResult.Status;
                    pointResult = jig.CurrentPoint;
                    useXAxis = jig.UseXAxis;
                }

                if (promptStatus != PromptStatus.OK)
                {
                    pointResult = startPoint;
                    direction = 0.0;
                    useXAxis = forceXAxis ?? true;
                    return false;
                }

                double deltaX = pointResult.X - startPoint.X;
                double deltaY = pointResult.Y - startPoint.Y;

                if (forceXAxis.HasValue)
                {
                    useXAxis = forceXAxis.Value;
                    double axisDelta = useXAxis ? deltaX : deltaY;
                    if (Math.Abs(axisDelta) < DirectionTolerance)
                    {
                        ed.WriteMessage(
                            useXAxis
                                ? "\nĐiểm hướng phải lệch theo trục X. Hãy chọn lại."
                                : "\nĐiểm hướng phải lệch theo trục Y. Hãy chọn lại.");
                        continue;
                    }

                    direction = axisDelta > 0.0 ? 1.0 : -1.0;
                    return true;
                }

                if (Math.Abs(deltaX) < DirectionTolerance &&
                    Math.Abs(deltaY) < DirectionTolerance)
                {
                    ed.WriteMessage("\nĐiểm hướng phải lệch theo X hoặc Y. Hãy chọn lại.");
                    continue;
                }

                direction = useXAxis
                    ? (deltaX >= 0.0 ? 1.0 : -1.0)
                    : (deltaY >= 0.0 ? 1.0 : -1.0);
                return true;
            }
        }

        private bool TryPromptSdxyStartPoint(
            Editor ed,
            Database db,
            ref SdxyTargetSettings settings,
            out Point3d startPoint)
        {
            startPoint = Point3d.Origin;

            while (true)
            {
                PromptPointOptions options =
                    new PromptPointOptions(
                        $"\nChọn điểm đầu dim hoặc [Settings] <{BuildSdxySettingsSummary(settings)}>:");
                options.AppendKeywordsToMessage = false;
                options.Keywords.Add("Settings");

                PromptPointResult result = ed.GetPoint(options);
                if (result.Status == PromptStatus.OK)
                {
                    startPoint = result.Value;
                    return true;
                }

                if (result.Status == PromptStatus.Keyword &&
                    string.Equals(result.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PromptForSdxySettings(ed, db, ref settings))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }

        private bool PromptForSdxySettings(
            Editor ed,
            Database db,
            ref SdxyTargetSettings settings)
        {
            if (ed == null || db == null)
            {
                return false;
            }

            List<SdxyEntityTypeChoice> availableTypes = GetAvailableSdxyEntityTypeChoices();
            while (true)
            {
                List<string> availableLayers = LoadSdxyLayerNames(db, settings);
                using (SdxySettingsForm form =
                    new SdxySettingsForm(availableTypes, availableLayers, settings))
                {
                    WF.DialogResult result = Application.ShowModalDialog(form);
                    if (form.PendingAction == SdxySettingsFormAction.PickSample)
                    {
                        settings = form.ResultSettings;
                    }
                    else if (result == WF.DialogResult.OK)
                    {
                        settings = form.ResultSettings;
                        SdxyTargetSettingsStore.Save(settings);
                        SdxyNamedFilterStore.SaveCurrentName(form.SelectedNamedFilterName);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (TryPromptSdxySampleDescriptor(ed, db, out SdxySampleDescriptor sampleDescriptor))
                {
                    settings.SampleDescriptors.Add(sampleDescriptor);
                    if (!settings.UseSampleType &&
                        !settings.UseSampleLayer &&
                        !settings.UseSampleLinetype &&
                        !settings.UseSampleColor &&
                        !settings.UseSampleBlockName)
                    {
                        settings.UseSampleType = true;
                        settings.UseSampleLayer = true;
                    }
                }
            }
        }

        private bool TryPromptSdxySampleDescriptor(
            Editor ed,
            Database db,
            out SdxySampleDescriptor sampleDescriptor)
        {
            sampleDescriptor = null;

            PromptEntityOptions options =
                new PromptEntityOptions("\nChọn đối tượng mẫu cho SDXY filter: ");
            PromptEntityResult result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                return false;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity entity = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Entity;
                if (entity == null)
                {
                    return false;
                }

                sampleDescriptor = BuildSdxySampleDescriptor(entity, tr);
                return sampleDescriptor != null;
            }
        }

        private SdxySampleDescriptor BuildSdxySampleDescriptor(Entity entity, Transaction tr)
        {
            if (entity == null)
            {
                return null;
            }

            string typeName = entity.GetType().FullName ?? entity.GetType().Name;
            string typeDisplayName = GetSdxyEntityDisplayName(entity.GetType());
            string layerName = entity.Layer ?? string.Empty;
            string linetypeName = entity.Linetype ?? string.Empty;
            string colorKey = BuildSdxyColorKey(entity.Color);
            string colorDisplayName = BuildSdxyColorDisplayName(entity.Color);
            string blockName = entity is BlockReference blockReference
                ? GetSdxyBlockName(blockReference, tr)
                : string.Empty;

            return new SdxySampleDescriptor(
                typeName,
                typeDisplayName,
                layerName,
                linetypeName,
                colorKey,
                colorDisplayName,
                blockName);
        }

        private List<string> LoadSdxyLayerNames(Database db, SdxyTargetSettings settings)
        {
            SortedSet<string> names =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable =
                    tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
                if (layerTable != null)
                {
                    foreach (ObjectId id in layerTable)
                    {
                        LayerTableRecord layer =
                            tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                        if (layer != null && !string.IsNullOrWhiteSpace(layer.Name))
                        {
                            names.Add(layer.Name);
                        }
                    }
                }
            }

            foreach (string layerName in settings?.AllowedLayers ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    names.Add(layerName);
                }
            }

            foreach (SdxySampleDescriptor sample in settings?.SampleDescriptors ?? Enumerable.Empty<SdxySampleDescriptor>())
            {
                if (!string.IsNullOrWhiteSpace(sample?.LayerName))
                {
                    names.Add(sample.LayerName);
                }
            }

            return names.ToList();
        }

        private List<SdxyEntityTypeChoice> GetAvailableSdxyEntityTypeChoices()
        {
            if (_cachedSdxyEntityTypeChoices != null)
            {
                return _cachedSdxyEntityTypeChoices;
            }

            HashSet<string> commonTypes = new HashSet<string>(
                new[]
                {
                    typeof(Line).FullName,
                    typeof(Autodesk.AutoCAD.DatabaseServices.Polyline).FullName,
                    typeof(Polyline2d).FullName,
                    typeof(Polyline3d).FullName,
                    typeof(Arc).FullName,
                    typeof(Circle).FullName,
                    typeof(Ellipse).FullName,
                    typeof(Spline).FullName,
                    typeof(BlockReference).FullName,
                    typeof(Dimension).FullName,
                    typeof(DBText).FullName,
                    typeof(MText).FullName,
                    typeof(Hatch).FullName,
                    typeof(Xline).FullName,
                    typeof(Ray).FullName,
                    typeof(Autodesk.AutoCAD.DatabaseServices.Region).FullName
                }
                .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);

            List<SdxyEntityTypeChoice> result = typeof(Entity).Assembly
                .GetTypes()
                .Where(type =>
                    type.IsClass &&
                    type.IsPublic &&
                    !type.IsGenericTypeDefinition &&
                    type != typeof(Entity) &&
                    typeof(Entity).IsAssignableFrom(type))
                .Select(type =>
                    new SdxyEntityTypeChoice(
                        type,
                        GetSdxyEntityDisplayName(type),
                        commonTypes.Contains(type.FullName ?? string.Empty)))
                .OrderByDescending(choice => choice.IsCommon)
                .ThenBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cachedSdxyEntityTypeChoices = result;
            return result;
        }

        private static string GetSdxyEntityDisplayName(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            string displayName = type.Name;
            if (type.IsAbstract)
            {
                displayName += " (family)";
            }

            return displayName;
        }

        private string BuildSdxySettingsSummary(SdxyTargetSettings settings)
        {
            if (settings == null)
            {
                return "All types | All layers | No sample";
            }

            int typeCount = settings.AllowedTypeNames.Count;
            int layerCount = settings.AllowedLayers.Count;
            string sampleSummary = "No sample";
            int sampleCount = settings.SampleDescriptors.Count;
            if (sampleCount > 0 &&
                (settings.UseSampleType ||
                 settings.UseSampleLayer ||
                 settings.UseSampleLinetype ||
                 settings.UseSampleColor ||
                 settings.UseSampleBlockName))
            {
                List<string> parts = new List<string>();
                if (settings.UseSampleType) parts.Add("Type");
                if (settings.UseSampleLayer) parts.Add("Layer");
                if (settings.UseSampleLinetype) parts.Add("Linetype");
                if (settings.UseSampleColor) parts.Add("Color");
                if (settings.UseSampleBlockName) parts.Add("Block");
                sampleSummary = $"Sample={sampleCount} obj ({string.Join("+", parts)})";
            }

            return
                $"Type={(typeCount == 0 ? "All" : typeCount.ToString())} | " +
                $"Layer={(layerCount == 0 ? "All" : layerCount.ToString())} | " +
                sampleSummary;
        }

        private bool IsSdxyTargetCandidate(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings)
        {
            if (entity == null || entity.IsErased)
            {
                return false;
            }

            if (!IsSdxyEntityVisible(entity, tr))
            {
                return false;
            }

            if (settings == null)
            {
                return entity is Curve && !(entity is Dimension);
            }

            if (!MatchesSdxyTypeFilters(entity, settings))
            {
                return false;
            }

            if (settings.AllowedLayers.Count > 0 &&
                !settings.AllowedLayers.Contains(entity.Layer ?? string.Empty))
            {
                return false;
            }

            return MatchesSdxySampleFilters(entity, tr, settings);
        }

        private bool MatchesSdxyTypeFilters(Entity entity, SdxyTargetSettings settings)
        {
            if (settings == null || settings.AllowedTypeNames.Count == 0)
            {
                return true;
            }

            Type entityType = entity.GetType();
            foreach (string typeName in settings.AllowedTypeNames)
            {
                Type targetType = ResolveSdxyEntityType(typeName);
                if (targetType != null && targetType.IsAssignableFrom(entityType))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesSdxySampleFilters(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings)
        {
            List<SdxySampleDescriptor> samples = settings?.SampleDescriptors
                ?.Where(sample => sample != null)
                .ToList()
                ?? new List<SdxySampleDescriptor>();
            if (samples.Count == 0)
            {
                return true;
            }

            return samples.Any(sample => MatchesSingleSdxySampleFilter(entity, tr, settings, sample));
        }

        private bool MatchesSingleSdxySampleFilter(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            SdxySampleDescriptor sample)
        {
            if (sample == null)
            {
                return true;
            }

            if (settings.UseSampleType)
            {
                Type sampleType = ResolveSdxyEntityType(sample.TypeName);
                if (sampleType == null || !sampleType.IsAssignableFrom(entity.GetType()))
                {
                    return false;
                }
            }

            if (settings.UseSampleLayer &&
                !string.Equals(entity.Layer, sample.LayerName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleLinetype &&
                !string.Equals(entity.Linetype, sample.LinetypeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleColor &&
                !string.Equals(
                    BuildSdxyColorKey(entity.Color),
                    sample.ColorKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleBlockName)
            {
                if (!(entity is BlockReference blockReference))
                {
                    return false;
                }

                string blockName = GetSdxyBlockName(blockReference, tr);
                if (!string.Equals(blockName, sample.BlockName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSdxyEntityVisible(Entity entity, Transaction tr)
        {
            try
            {
                if (!entity.Visible)
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                LayerTableRecord layer =
                    tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer != null && (layer.IsOff || layer.IsFrozen))
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private Type ResolveSdxyEntityType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            return typeof(Entity).Assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
        }

        private string BuildSdxyColorKey(Autodesk.AutoCAD.Colors.Color color)
        {
            if (color == null)
            {
                return string.Empty;
            }

            switch (color.ColorMethod)
            {
                case Autodesk.AutoCAD.Colors.ColorMethod.ByLayer:
                    return "ByLayer";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByBlock:
                    return "ByBlock";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByAci:
                    return "ACI:" + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
                case Autodesk.AutoCAD.Colors.ColorMethod.ByColor:
                    return "RGB:" +
                        color.Red.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Green.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Blue.ToString(CultureInfo.InvariantCulture);
                default:
                    return color.ColorMethod + ":" + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
            }
        }

        private string BuildSdxyColorDisplayName(Autodesk.AutoCAD.Colors.Color color)
        {
            if (color == null)
            {
                return string.Empty;
            }

            switch (color.ColorMethod)
            {
                case Autodesk.AutoCAD.Colors.ColorMethod.ByLayer:
                    return "ByLayer";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByBlock:
                    return "ByBlock";
                case Autodesk.AutoCAD.Colors.ColorMethod.ByAci:
                    return "ACI " + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
                case Autodesk.AutoCAD.Colors.ColorMethod.ByColor:
                    return "RGB " +
                        color.Red.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Green.ToString(CultureInfo.InvariantCulture) + "," +
                        color.Blue.ToString(CultureInfo.InvariantCulture);
                default:
                    return color.ColorMethod.ToString();
            }
        }

        private string GetSdxyBlockName(BlockReference blockReference, Transaction tr)
        {
            if (blockReference == null)
            {
                return string.Empty;
            }

            try
            {
                ObjectId blockId = blockReference.DynamicBlockTableRecord;
                if (blockId.IsNull)
                {
                    blockId = blockReference.BlockTableRecord;
                }

                BlockTableRecord block =
                    tr.GetObject(blockId, OpenMode.ForRead) as BlockTableRecord;
                return block?.Name ?? string.Empty;
            }
            catch
            {
                try
                {
                    return blockReference.Name ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private Point3d? FindNearestPointOnXAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                startPoint,
                direction,
                useXAxis: true,
                settings: null);
        }

        private Point3d? FindNearestPointOnXAxisFromProbe(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction,
            SdxyTargetSettings settings)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                probePoint,
                direction,
                useXAxis: true,
                settings: settings);
        }

        private Point3d? FindNearestPointOnYAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            double direction)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                startPoint,
                direction,
                useXAxis: false,
                settings: null);
        }

        private Point3d? FindNearestPointOnYAxisFromProbe(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction,
            SdxyTargetSettings settings)
        {
            return FindNearestPointOnAxis(
                ed,
                currentSpace,
                tr,
                startPoint,
                probePoint,
                direction,
                useXAxis: false,
                settings: settings);
        }

        private Point3d? FindNearestPointOnAxis(
            Editor ed,
            BlockTableRecord currentSpace,
            Transaction tr,
            Point3d startPoint,
            Point3d probePoint,
            double direction,
            bool useXAxis,
            SdxyTargetSettings settings)
        {
            Point3d? bestPoint = null;
            double bestDistance = double.MaxValue;

            using (Line scanLine = useXAxis
                ? CreateScanLine(probePoint, direction)
                : CreateVerticalScanLine(probePoint, direction))
            {
                foreach (ObjectId id in GetScanCandidateIds(
                    ed,
                    currentSpace,
                    probePoint,
                    useXAxis,
                    direction,
                    settings))
                {
                    Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    BlockReference blockReference = entity as BlockReference;
                    bool useBlockContentMode =
                        blockReference != null &&
                        ShouldUseSdxyBlockContentMode(settings);

                    if (useBlockContentMode)
                    {
                        if (!IsSdxyBlockContainerCandidate(blockReference, tr, settings))
                        {
                            continue;
                        }
                    }
                    else if (!IsSdxyTargetCandidate(entity, tr, settings))
                    {
                        continue;
                    }

                    bool fastPass = useXAxis
                        ? IsHorizontalRayCandidate(
                            entity,
                            tr,
                            settings,
                            useBlockContentMode,
                            probePoint.Y,
                            startPoint.X,
                            probePoint.X,
                            direction,
                            bestDistance)
                        : IsVerticalRayCandidate(
                            entity,
                            tr,
                            settings,
                            useBlockContentMode,
                            probePoint.X,
                            startPoint.Y,
                            probePoint.Y,
                            direction,
                            bestDistance);
                    if (!fastPass)
                    {
                        continue;
                    }

                    Point3dCollection intersections =
                        useBlockContentMode
                            ? TryGetSdxyBlockContentIntersections(
                                blockReference,
                                tr,
                                settings,
                                scanLine,
                                useXAxis,
                                new HashSet<ObjectId>())
                            : TryGetIntersections(entity, scanLine, useXAxis);
                    if (intersections == null || intersections.Count == 0)
                    {
                        continue;
                    }

                    foreach (Point3d point in intersections)
                    {
                        double projectedFromStart = useXAxis
                            ? (point.X - startPoint.X) * direction
                            : (point.Y - startPoint.Y) * direction;
                        double projectedFromProbe = useXAxis
                            ? (point.X - probePoint.X) * direction
                            : (point.Y - probePoint.Y) * direction;

                        if (projectedFromStart <= DirectionTolerance)
                        {
                            continue;
                        }

                        if (projectedFromProbe < -DirectionTolerance)
                        {
                            continue;
                        }

                        double rankDistance = Math.Max(0.0, projectedFromProbe);
                        if (rankDistance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = rankDistance;
                        bestPoint = point;
                    }
                }
            }

            return bestPoint;
        }

        private bool IsHorizontalRayCandidate(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            bool useBlockContentMode,
            double scanY,
            double startX,
            double probeX,
            double direction,
            double bestDistance)
        {
            // Lọc nhanh bằng GeometricExtents trước khi gọi IntersectWith.
            // Đây là phần giúp SDXY nhanh hơn khi bản vẽ có nhiều object.
            if (!TryGetSdxyScanExtents(
                entity,
                tr,
                settings,
                useBlockContentMode,
                out Extents3d extents))
            {
                return true;
            }

            if (scanY < extents.MinPoint.Y - DirectionTolerance ||
                scanY > extents.MaxPoint.Y + DirectionTolerance)
            {
                return false;
            }

            if (direction > 0.0)
            {
                if (extents.MaxPoint.X <= startX + DirectionTolerance ||
                    extents.MaxPoint.X < probeX - DirectionTolerance)
                {
                    return false;
                }

                double minRankDistance = extents.MinPoint.X > probeX
                    ? extents.MinPoint.X - probeX
                    : 0.0;
                return minRankDistance < bestDistance;
            }

            if (extents.MinPoint.X >= startX - DirectionTolerance ||
                extents.MinPoint.X > probeX + DirectionTolerance)
            {
                return false;
            }

            double minNegativeRankDistance = extents.MaxPoint.X < probeX
                ? probeX - extents.MaxPoint.X
                : 0.0;
            return minNegativeRankDistance < bestDistance;
        }

        private bool IsVerticalRayCandidate(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            bool useBlockContentMode,
            double scanX,
            double startY,
            double probeY,
            double direction,
            double bestDistance)
        {
            if (!TryGetSdxyScanExtents(
                entity,
                tr,
                settings,
                useBlockContentMode,
                out Extents3d extents))
            {
                return true;
            }

            if (scanX < extents.MinPoint.X - DirectionTolerance ||
                scanX > extents.MaxPoint.X + DirectionTolerance)
            {
                return false;
            }

            if (direction > 0.0)
            {
                if (extents.MaxPoint.Y <= startY + DirectionTolerance ||
                    extents.MaxPoint.Y < probeY - DirectionTolerance)
                {
                    return false;
                }

                double minRankDistance = extents.MinPoint.Y > probeY
                    ? extents.MinPoint.Y - probeY
                    : 0.0;
                return minRankDistance < bestDistance;
            }

            if (extents.MinPoint.Y >= startY - DirectionTolerance ||
                extents.MinPoint.Y > probeY + DirectionTolerance)
            {
                return false;
            }

            double minNegativeRankDistance = extents.MaxPoint.Y < probeY
                ? probeY - extents.MaxPoint.Y
                : 0.0;
            return minNegativeRankDistance < bestDistance;
        }

        private IEnumerable<ObjectId> GetScanCandidateIds(
            Editor ed,
            BlockTableRecord currentSpace,
            Point3d scanStartPoint,
            bool useXAxis,
            double direction,
            SdxyTargetSettings settings)
        {
            ObjectId[] selectionIds = TrySelectFenceCandidates(
                ed,
                scanStartPoint,
                useXAxis,
                direction,
                settings);
            if (selectionIds != null)
            {
                return selectionIds;
            }

            return EnumerateEntityIds(currentSpace, settings);
        }

        private ObjectId[] TrySelectFenceCandidates(
            Editor ed,
            Point3d scanStartPoint,
            bool useXAxis,
            double direction,
            SdxyTargetSettings settings)
        {
            if (ed == null)
            {
                return null;
            }

            Point3d fenceEnd = useXAxis
                ? new Point3d(
                    scanStartPoint.X + SearchDistance * direction,
                    scanStartPoint.Y,
                    scanStartPoint.Z)
                : new Point3d(
                    scanStartPoint.X,
                    scanStartPoint.Y + SearchDistance * direction,
                    scanStartPoint.Z);

            try
            {
                using (Point3dCollection fencePoints = new Point3dCollection())
                {
                    fencePoints.Add(scanStartPoint);
                    fencePoints.Add(fenceEnd);

                    PromptSelectionResult result = ed.SelectFence(fencePoints);
                    if (result.Status == PromptStatus.OK && result.Value != null)
                    {
                        return result.Value
                            .GetObjectIds()
                            .Where(id => IsScanCandidateId(id, settings))
                            .ToArray();
                    }

                    if (result.Status == PromptStatus.None)
                    {
                        return Array.Empty<ObjectId>();
                    }
                }
            }
            catch
            {
                // Nếu engine selection không trả được kết quả ổn định trong một số
                // bản vẽ đặc biệt thì fallback về cách quét hẹp.
            }

            return TrySelectScanWindowCandidates(
                ed,
                scanStartPoint,
                fenceEnd,
                useXAxis,
                settings);
        }

        private ObjectId[] TrySelectScanWindowCandidates(
            Editor ed,
            Point3d scanStartPoint,
            Point3d fenceEnd,
            bool useXAxis,
            SdxyTargetSettings settings)
        {
            if (ed == null)
            {
                return null;
            }

            const double windowHalfWidth = 10.0;
            Point3dCollection windowPoints = new Point3dCollection();
            if (useXAxis)
            {
                windowPoints.Add(new Point3d(
                    scanStartPoint.X,
                    scanStartPoint.Y - windowHalfWidth,
                    scanStartPoint.Z));
                windowPoints.Add(new Point3d(
                    fenceEnd.X,
                    fenceEnd.Y + windowHalfWidth,
                    fenceEnd.Z));
            }
            else
            {
                windowPoints.Add(new Point3d(
                    scanStartPoint.X - windowHalfWidth,
                    scanStartPoint.Y,
                    scanStartPoint.Z));
                windowPoints.Add(new Point3d(
                    fenceEnd.X + windowHalfWidth,
                    fenceEnd.Y,
                    fenceEnd.Z));
            }

            try
            {
                PromptSelectionResult result = ed.SelectCrossingWindow(
                    windowPoints[0],
                    windowPoints[1]);
                if (result.Status == PromptStatus.OK && result.Value != null)
                {
                    return result.Value
                        .GetObjectIds()
                        .Where(id => IsScanCandidateId(id, settings))
                        .ToArray();
                }

                if (result.Status == PromptStatus.None)
                {
                    return Array.Empty<ObjectId>();
                }
            }
            catch
            {
            }

            return null;
        }

        private IEnumerable<ObjectId> EnumerateEntityIds(
            BlockTableRecord currentSpace,
            SdxyTargetSettings settings)
        {
            foreach (ObjectId id in currentSpace)
            {
                if (IsScanCandidateId(id, settings))
                {
                    yield return id;
                }
            }
        }

        private bool IsScanCandidateId(ObjectId id, SdxyTargetSettings settings)
        {
            RXClass objectClass = id.ObjectClass;
            if (objectClass == null)
            {
                return false;
            }

            if (settings == null)
            {
                if (DimensionRxClass != null && objectClass.IsDerivedFrom(DimensionRxClass))
                {
                    return false;
                }

                return CurveRxClass == null || objectClass.IsDerivedFrom(CurveRxClass);
            }

            return EntityRxClass == null || objectClass.IsDerivedFrom(EntityRxClass);
        }

        private bool TryGetEntityExtents(Entity entity, out Extents3d extents)
        {
            try
            {
                if (entity == null || entity.IsErased)
                {
                    extents = default;
                    return false;
                }

                extents = entity.GeometricExtents;
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                extents = default;
                return false;
            }
        }

        private bool ShouldUseSdxyBlockContentMode(SdxyTargetSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            if (settings.AllowedTypeNames.Count == 0)
            {
                return true;
            }

            foreach (string typeName in settings.AllowedTypeNames)
            {
                Type targetType = ResolveSdxyEntityType(typeName);
                if (targetType != null &&
                    !typeof(BlockReference).IsAssignableFrom(targetType))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSdxyBlockContainerCandidate(
            BlockReference blockReference,
            Transaction tr,
            SdxyTargetSettings settings)
        {
            if (blockReference == null || blockReference.IsErased)
            {
                return false;
            }

            if (!IsSdxyEntityVisible(blockReference, tr))
            {
                return false;
            }

            if (settings == null || settings.AllowedTypeNames.Count == 0)
            {
                return true;
            }

            Type blockReferenceType = typeof(BlockReference);
            foreach (string typeName in settings.AllowedTypeNames)
            {
                Type targetType = ResolveSdxyEntityType(typeName);
                if (targetType != null && targetType.IsAssignableFrom(blockReferenceType))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetSdxyScanExtents(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            bool useBlockContentMode,
            out Extents3d extents)
        {
            if (useBlockContentMode && entity is BlockReference blockReference)
            {
                return TryGetSdxyBlockContentExtents(
                    blockReference,
                    tr,
                    settings,
                    out extents,
                    new HashSet<ObjectId>());
            }

            return TryGetEntityExtents(entity, out extents);
        }

        private bool TryGetSdxyBlockContentExtents(
            BlockReference blockReference,
            Transaction tr,
            SdxyTargetSettings settings,
            out Extents3d extents,
            HashSet<ObjectId> visitedBlockDefinitions)
        {
            extents = default;
            if (blockReference == null || tr == null)
            {
                return false;
            }

            ObjectId blockDefinitionId = blockReference.BlockTableRecord;
            if (blockDefinitionId.IsNull ||
                visitedBlockDefinitions.Contains(blockDefinitionId))
            {
                return false;
            }

            visitedBlockDefinitions.Add(blockDefinitionId);

            try
            {
                Extents3d? combinedExtents = null;
                BlockTableRecord definition =
                    tr.GetObject(blockDefinitionId, OpenMode.ForRead) as BlockTableRecord;
                if (definition != null)
                {
                    foreach (ObjectId childId in definition)
                    {
                        Entity childEntity =
                            tr.GetObject(childId, OpenMode.ForRead) as Entity;
                        if (childEntity == null)
                        {
                            continue;
                        }

                        if (childEntity is BlockReference nestedBlockReference &&
                            IsSdxyBlockContainerCandidate(nestedBlockReference, tr, settings))
                        {
                            using (Entity transformedNestedEntity =
                                TryGetTransformedEntity(nestedBlockReference, blockReference.BlockTransform))
                            {
                                if (transformedNestedEntity is BlockReference transformedNestedBlock &&
                                    TryGetSdxyBlockContentExtents(
                                        transformedNestedBlock,
                                        tr,
                                        settings,
                                        out Extents3d nestedExtents,
                                        visitedBlockDefinitions))
                                {
                                    combinedExtents = combinedExtents == null
                                        ? nestedExtents
                                        : UnionExtents(combinedExtents.Value, nestedExtents);
                                }
                                else if (TryGetTransformedExtents(
                                    nestedBlockReference,
                                    blockReference.BlockTransform,
                                    out Extents3d fallbackNestedExtents))
                                {
                                    combinedExtents = combinedExtents == null
                                        ? fallbackNestedExtents
                                        : UnionExtents(combinedExtents.Value, fallbackNestedExtents);
                                }
                            }

                            continue;
                        }

                        if (!IsSdxyBlockContentCandidate(
                            childEntity,
                            tr,
                            settings,
                            blockReference))
                        {
                            continue;
                        }

                        try
                        {
                            Extents3d transformedExtents =
                                TransformExtents(
                                    childEntity.GeometricExtents,
                                    blockReference.BlockTransform);
                            combinedExtents = combinedExtents == null
                                ? transformedExtents
                                : UnionExtents(combinedExtents.Value, transformedExtents);
                        }
                        catch
                        {
                        }
                    }
                }

                foreach (ObjectId attributeId in blockReference.AttributeCollection)
                {
                    AttributeReference attribute =
                        tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (!IsSdxyBlockContentCandidate(attribute, tr, settings, blockReference))
                    {
                        continue;
                    }

                    try
                    {
                        Extents3d attributeExtents = attribute.GeometricExtents;
                        combinedExtents = combinedExtents == null
                            ? attributeExtents
                            : UnionExtents(combinedExtents.Value, attributeExtents);
                    }
                    catch
                    {
                    }
                }

                if (combinedExtents == null)
                {
                    return false;
                }

                extents = combinedExtents.Value;
                return true;
            }
            finally
            {
                visitedBlockDefinitions.Remove(blockDefinitionId);
            }
        }

        private Point3dCollection TryGetSdxyBlockContentIntersections(
            BlockReference blockReference,
            Transaction tr,
            SdxyTargetSettings settings,
            Line scanLine,
            bool useXAxis,
            HashSet<ObjectId> visitedBlockDefinitions)
        {
            Point3dCollection result = new Point3dCollection();
            if (blockReference == null || tr == null || scanLine == null)
            {
                return result;
            }

            ObjectId blockDefinitionId = blockReference.BlockTableRecord;
            if (blockDefinitionId.IsNull ||
                visitedBlockDefinitions.Contains(blockDefinitionId))
            {
                return result;
            }

            visitedBlockDefinitions.Add(blockDefinitionId);

            try
            {
                BlockTableRecord definition =
                    tr.GetObject(blockDefinitionId, OpenMode.ForRead) as BlockTableRecord;
                if (definition != null)
                {
                    foreach (ObjectId childId in definition)
                    {
                        Entity childEntity =
                            tr.GetObject(childId, OpenMode.ForRead) as Entity;
                        if (childEntity == null)
                        {
                            continue;
                        }

                        if (childEntity is BlockReference nestedBlockReference &&
                            IsSdxyBlockContainerCandidate(nestedBlockReference, tr, settings))
                        {
                            using (Entity transformedNestedEntity =
                                TryGetTransformedEntity(nestedBlockReference, blockReference.BlockTransform))
                            {
                                if (transformedNestedEntity is BlockReference transformedNestedBlock)
                                {
                                    Point3dCollection nestedPoints =
                                        TryGetSdxyBlockContentIntersections(
                                            transformedNestedBlock,
                                            tr,
                                            settings,
                                            scanLine,
                                            useXAxis,
                                            visitedBlockDefinitions);
                                    AddIntersectionPoints(result, nestedPoints);
                                }
                                else
                                {
                                    AddIntersectionPoints(
                                        result,
                                        BuildTransformedExtentsFallbackIntersections(
                                            nestedBlockReference,
                                            blockReference.BlockTransform,
                                            scanLine,
                                            useXAxis));
                                }
                            }

                            continue;
                        }

                        if (!IsSdxyBlockContentCandidate(
                            childEntity,
                            tr,
                            settings,
                            blockReference))
                        {
                            continue;
                        }

                        using (Entity transformedEntity =
                            TryGetTransformedEntity(childEntity, blockReference.BlockTransform))
                        {
                            Point3dCollection intersections = transformedEntity != null
                                ? TryGetIntersections(transformedEntity, scanLine, useXAxis)
                                : BuildTransformedExtentsFallbackIntersections(
                                    childEntity,
                                    blockReference.BlockTransform,
                                    scanLine,
                                    useXAxis);

                            AddIntersectionPoints(result, intersections);
                        }
                    }
                }

                foreach (ObjectId attributeId in blockReference.AttributeCollection)
                {
                    AttributeReference attribute =
                        tr.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (!IsSdxyBlockContentCandidate(attribute, tr, settings, blockReference))
                    {
                        continue;
                    }

                    AddIntersectionPoints(
                        result,
                        TryGetIntersections(attribute, scanLine, useXAxis));
                }
            }
            finally
            {
                visitedBlockDefinitions.Remove(blockDefinitionId);
            }

            return result;
        }

        private bool IsSdxyBlockContentCandidate(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            BlockReference containerBlockReference)
        {
            if (entity == null || entity.IsErased)
            {
                return false;
            }

            if (!IsSdxyEntityVisible(entity, tr))
            {
                return false;
            }

            if (!MatchesSdxyContentTypeFilters(entity, settings))
            {
                return false;
            }

            if (!MatchesSdxyContentLayerFilters(entity, settings, containerBlockReference))
            {
                return false;
            }

            return MatchesSdxyContentSampleFilters(entity, tr, settings, containerBlockReference);
        }

        private bool MatchesSdxyContentTypeFilters(Entity entity, SdxyTargetSettings settings)
        {
            if (entity == null)
            {
                return false;
            }

            if (settings == null || settings.AllowedTypeNames.Count == 0)
            {
                return true;
            }

            Type entityType = entity.GetType();
            foreach (string typeName in settings.AllowedTypeNames)
            {
                Type targetType = ResolveSdxyEntityType(typeName);
                if (targetType == null ||
                    typeof(BlockReference).IsAssignableFrom(targetType))
                {
                    continue;
                }

                if (targetType.IsAssignableFrom(entityType))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesSdxyContentLayerFilters(
            Entity entity,
            SdxyTargetSettings settings,
            BlockReference containerBlockReference)
        {
            if (settings == null || settings.AllowedLayers.Count == 0)
            {
                return true;
            }

            string entityLayer = entity?.Layer ?? string.Empty;
            if (settings.AllowedLayers.Contains(entityLayer))
            {
                return true;
            }

            string containerLayer = containerBlockReference?.Layer ?? string.Empty;
            return !string.IsNullOrEmpty(containerLayer) &&
                   settings.AllowedLayers.Contains(containerLayer);
        }

        private bool MatchesSdxyContentSampleFilters(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            BlockReference containerBlockReference)
        {
            List<SdxySampleDescriptor> samples = settings?.SampleDescriptors
                ?.Where(sample => sample != null)
                .ToList()
                ?? new List<SdxySampleDescriptor>();
            if (samples.Count == 0)
            {
                return true;
            }

            return samples.Any(sample =>
                MatchesSingleSdxyContentSampleFilter(
                    entity,
                    tr,
                    settings,
                    sample,
                    containerBlockReference));
        }

        private bool MatchesSingleSdxyContentSampleFilter(
            Entity entity,
            Transaction tr,
            SdxyTargetSettings settings,
            SdxySampleDescriptor sample,
            BlockReference containerBlockReference)
        {
            if (sample == null)
            {
                return true;
            }

            if (settings.UseSampleType)
            {
                Type sampleType = ResolveSdxyEntityType(sample.TypeName);
                if (sampleType == null || !sampleType.IsAssignableFrom(entity.GetType()))
                {
                    return false;
                }
            }

            if (settings.UseSampleLayer)
            {
                string entityLayer = entity.Layer ?? string.Empty;
                string containerLayer = containerBlockReference?.Layer ?? string.Empty;
                if (!string.Equals(entityLayer, sample.LayerName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(containerLayer, sample.LayerName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (settings.UseSampleLinetype &&
                !string.Equals(entity.Linetype, sample.LinetypeName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleColor &&
                !string.Equals(
                    BuildSdxyColorKey(entity.Color),
                    sample.ColorKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (settings.UseSampleBlockName)
            {
                string blockName = entity is BlockReference childBlockReference
                    ? GetSdxyBlockName(childBlockReference, tr)
                    : GetSdxyBlockName(containerBlockReference, tr);
                if (!string.Equals(blockName, sample.BlockName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void AddIntersectionPoints(
            Point3dCollection target,
            Point3dCollection source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (Point3d point in source)
            {
                AddIntersectionPoint(target, point);
            }
        }

        private Extents3d TransformExtents(Extents3d extents, Matrix3d transform)
        {
            Point3d[] corners =
            {
                new Point3d(extents.MinPoint.X, extents.MinPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MinPoint.X, extents.MinPoint.Y, extents.MaxPoint.Z),
                new Point3d(extents.MinPoint.X, extents.MaxPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MinPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MinPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MinPoint.Y, extents.MaxPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MaxPoint.Y, extents.MinPoint.Z),
                new Point3d(extents.MaxPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z)
            };

            Point3d firstPoint = corners[0].TransformBy(transform);
            Extents3d transformed = new Extents3d(firstPoint, firstPoint);
            for (int i = 1; i < corners.Length; i++)
            {
                transformed.AddPoint(corners[i].TransformBy(transform));
            }

            return transformed;
        }

        private Extents3d UnionExtents(Extents3d left, Extents3d right)
        {
            return new Extents3d(
                new Point3d(
                    Math.Min(left.MinPoint.X, right.MinPoint.X),
                    Math.Min(left.MinPoint.Y, right.MinPoint.Y),
                    Math.Min(left.MinPoint.Z, right.MinPoint.Z)),
                new Point3d(
                    Math.Max(left.MaxPoint.X, right.MaxPoint.X),
                    Math.Max(left.MaxPoint.Y, right.MaxPoint.Y),
                    Math.Max(left.MaxPoint.Z, right.MaxPoint.Z)));
        }

        private Entity TryGetTransformedEntity(Entity entity, Matrix3d transform)
        {
            if (entity == null || entity.IsErased)
            {
                return null;
            }

            try
            {
                return entity.GetTransformedCopy(transform) as Entity;
            }
            catch
            {
                return null;
            }
        }

        private Point3dCollection BuildTransformedExtentsFallbackIntersections(
            Entity entity,
            Matrix3d transform,
            Line scanLine,
            bool useXAxis)
        {
            if (scanLine == null ||
                !TryGetTransformedExtents(entity, transform, out Extents3d transformedExtents))
            {
                return null;
            }

            return BuildExtentsFallbackIntersections(
                transformedExtents,
                scanLine,
                useXAxis);
        }

        private bool TryGetTransformedExtents(
            Entity entity,
            Matrix3d transform,
            out Extents3d extents)
        {
            extents = default;
            if (entity == null || entity.IsErased)
            {
                return false;
            }

            try
            {
                extents = TransformExtents(entity.GeometricExtents, transform);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Point3dCollection TryGetIntersections(
            Entity entity,
            Line scanLine,
            bool useXAxis)
        {
            if (entity == null || scanLine == null)
            {
                return null;
            }

            // IntersectWith có thể lỗi với vài entity đặc biệt.
            // Bắt lỗi ở đây để lệnh bỏ qua object đó thay vì văng command.
            try
            {
                Point3dCollection intersections = new Point3dCollection();
                entity.IntersectWith(
                    scanLine,
                    Intersect.OnBothOperands,
                    intersections,
                    IntPtr.Zero,
                    IntPtr.Zero);
                if (intersections.Count > 0)
                {
                    return intersections;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }

            return BuildExtentsFallbackIntersections(entity, scanLine, useXAxis);
        }

        private Point3dCollection BuildExtentsFallbackIntersections(
            Entity entity,
            Line scanLine,
            bool useXAxis)
        {
            if (!TryGetEntityExtents(entity, out Extents3d extents))
            {
                return null;
            }

            return BuildExtentsFallbackIntersections(extents, scanLine, useXAxis);
        }

        private Point3dCollection BuildExtentsFallbackIntersections(
            Extents3d extents,
            Line scanLine,
            bool useXAxis)
        {
            if (scanLine == null)
            {
                return null;
            }

            Point3dCollection points = new Point3dCollection();
            if (useXAxis)
            {
                double scanY = scanLine.StartPoint.Y;
                if (scanY < extents.MinPoint.Y - DirectionTolerance ||
                    scanY > extents.MaxPoint.Y + DirectionTolerance)
                {
                    return points;
                }

                AddIntersectionPoint(points, new Point3d(extents.MinPoint.X, scanY, scanLine.StartPoint.Z));
                AddIntersectionPoint(points, new Point3d(extents.MaxPoint.X, scanY, scanLine.StartPoint.Z));
                return points;
            }

            double scanX = scanLine.StartPoint.X;
            if (scanX < extents.MinPoint.X - DirectionTolerance ||
                scanX > extents.MaxPoint.X + DirectionTolerance)
            {
                return points;
            }

            AddIntersectionPoint(points, new Point3d(scanX, extents.MinPoint.Y, scanLine.StartPoint.Z));
            AddIntersectionPoint(points, new Point3d(scanX, extents.MaxPoint.Y, scanLine.StartPoint.Z));
            return points;
        }

        private void AddIntersectionPoint(Point3dCollection points, Point3d candidate)
        {
            foreach (Point3d existing in points)
            {
                if (existing.DistanceTo(candidate) <= DirectionTolerance)
                {
                    return;
                }
            }

            points.Add(candidate);
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

        private sealed class AxisDirectionPreviewJig : DrawJig, IDisposable
        {
            private readonly Point3d _startPoint;
            private readonly string _message;
            private readonly bool? _forceXAxis;
            private Point3d _currentPoint;
            private bool _invertAxisChoice;
            private bool _shiftWasPressed;

            public AxisDirectionPreviewJig(
                Point3d startPoint,
                string message,
                bool? forceXAxis)
            {
                _startPoint = startPoint;
                _message = message;
                _forceXAxis = forceXAxis;
                _currentPoint = startPoint;
                _invertAxisChoice = false;
                _shiftWasPressed = false;
            }

            public Point3d CurrentPoint => _currentPoint;

            public bool UseXAxis => ResolveUseXAxis(_currentPoint);

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions pointOptions =
                    new JigPromptPointOptions(_message);
                // Không dùng BasePoint để điểm thứ 2 của SDXY / SmartDimX / SmartDimY
                // không bị ORTHOMODE ép theo ngang/dọc.
                pointOptions.UserInputControls =
                    UserInputControls.Accept3dCoordinates |
                    UserInputControls.NoZDirectionOrtho;

                PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
                if (pointResult.Status == PromptStatus.Cancel)
                {
                    return SamplerStatus.Cancel;
                }

                if (pointResult.Status != PromptStatus.OK)
                {
                    return SamplerStatus.NoChange;
                }

                bool shiftPressed = IsShiftPressedForDimJig();
                bool axisToggled =
                    !_forceXAxis.HasValue &&
                    shiftPressed &&
                    !_shiftWasPressed;
                _shiftWasPressed = shiftPressed;

                if (_currentPoint.DistanceTo(pointResult.Value) <= PreviewPointTolerance &&
                    !axisToggled)
                {
                    return SamplerStatus.NoChange;
                }

                if (axisToggled)
                {
                    _invertAxisChoice = !_invertAxisChoice;
                }

                _currentPoint = pointResult.Value;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                Point3d previewPoint = GetPreviewPoint();
                if (_startPoint.DistanceTo(previewPoint) <= DirectionTolerance)
                {
                    return true;
                }

                draw.Geometry.WorldLine(_startPoint, previewPoint);
                return true;
            }

            private Point3d GetPreviewPoint()
            {
                bool useXAxis = ResolveUseXAxis(_currentPoint);
                if (useXAxis)
                {
                    return new Point3d(_currentPoint.X, _startPoint.Y, _startPoint.Z);
                }

                return new Point3d(_startPoint.X, _currentPoint.Y, _startPoint.Z);
            }

            private bool ResolveUseXAxis(Point3d point)
            {
                if (_forceXAxis.HasValue)
                {
                    return _forceXAxis.Value;
                }

                double deltaX = point.X - _startPoint.X;
                double deltaY = point.Y - _startPoint.Y;
                bool useXAxis = Math.Abs(deltaX) >= Math.Abs(deltaY);
                return _invertAxisChoice ? !useXAxis : useXAxis;
            }

            public void Dispose()
            {
            }
        }

        private static bool IsShiftPressedForDimJig()
        {
            const int ShiftVirtualKey = 0x10;

            if ((GetAsyncKeyState(ShiftVirtualKey) & 0x8000) != 0)
            {
                return true;
            }

            return (WF.Control.ModifierKeys & WF.Keys.Shift) == WF.Keys.Shift;
        }

        private sealed class SmartDimPlacementPrompt : IDisposable
        {
            private readonly Editor _editor;
            private readonly RotatedDimension _previewDimension;
            private readonly IntegerCollection _viewportNumbers;
            private readonly Point3d _defaultPoint;
            private readonly bool _originalUseXAxis;
            private readonly double _minX;
            private readonly double _maxX;
            private readonly double _minY;
            private readonly double _maxY;
            private readonly double _switchMargin;
            private Point3d _currentPoint;
            private bool _useXAxis;
            private bool _previewAdded;
            private ViewTableRecord _initialView;
            private ViewTableRecord _latestChangedView;

            public SmartDimPlacementPrompt(
                Editor editor,
                Database db,
                Point3d startPoint,
                Point3d endPoint,
                bool useXAxis)
            {
                _editor = editor;
                _viewportNumbers = new IntegerCollection();
                _initialView = TryGetCurrentView(editor);

                double previewOffset = Math.Max(
                    db.Dimtxt + db.Dimgap + db.Dimexe,
                    10.0);

                _originalUseXAxis = useXAxis;
                _minX = Math.Min(startPoint.X, endPoint.X);
                _maxX = Math.Max(startPoint.X, endPoint.X);
                _minY = Math.Min(startPoint.Y, endPoint.Y);
                _maxY = Math.Max(startPoint.Y, endPoint.Y);
                _switchMargin = previewOffset;

                _defaultPoint = useXAxis
                    ? new Point3d(
                        (startPoint.X + endPoint.X) * 0.5,
                        startPoint.Y + previewOffset,
                        startPoint.Z)
                    : new Point3d(
                        startPoint.X + previewOffset,
                        (startPoint.Y + endPoint.Y) * 0.5,
                        startPoint.Z);

                _useXAxis = useXAxis;
                _currentPoint = _defaultPoint;

                _previewDimension = new RotatedDimension
                {
                    XLine1Point = startPoint,
                    XLine2Point = endPoint,
                    DimLinePoint = _currentPoint,
                    Rotation = _useXAxis ? 0.0 : Math.PI / 2.0,
                    DimensionStyle = db.Dimstyle
                };
                _previewDimension.SetDatabaseDefaults(db);
            }

            public bool Prompt(out Point3d dimLinePoint, out bool useXAxis)
            {
                AddPreview();
                _editor.PointMonitor += EditorPointMonitor;

                PromptPointOptions pointOptions =
                    new PromptPointOptions(
                        "\nChọn điểm đặt dim (Enter/Space = mặc định, kéo ra ngoài 2 đầu để đổi hướng): ");
                pointOptions.AllowNone = true;

                PromptPointResult pointResult = _editor.GetPoint(pointOptions);
                if (pointResult.Status == PromptStatus.None)
                {
                    dimLinePoint = _currentPoint;
                    useXAxis = _useXAxis;
                    return true;
                }

                if (pointResult.Status != PromptStatus.OK)
                {
                    ScheduleLatestViewRestore();
                    dimLinePoint = Point3d.Origin;
                    useXAxis = _originalUseXAxis;
                    return false;
                }

                UpdatePreview(pointResult.Value);
                dimLinePoint = _currentPoint;
                useXAxis = _useXAxis;
                return true;
            }

            private void EditorPointMonitor(object sender, PointMonitorEventArgs e)
            {
                try
                {
                    UpdatePreview(e.Context.ComputedPoint);
                }
                catch
                {
                }
            }

            private bool ResolveUseXAxis(Point3d point)
            {
                if (_originalUseXAxis)
                {
                    bool switchedToVertical =
                        point.X < _minX - _switchMargin ||
                        point.X > _maxX + _switchMargin;
                    return !switchedToVertical;
                }

                bool switchedToHorizontal =
                    point.Y < _minY - _switchMargin ||
                    point.Y > _maxY + _switchMargin;
                return switchedToHorizontal;
            }

            private void UpdatePreview(Point3d point)
            {
                if (_currentPoint.DistanceTo(point) <= PreviewPointTolerance)
                {
                    return;
                }

                _useXAxis = ResolveUseXAxis(point);
                _currentPoint = point;
                _previewDimension.DimLinePoint = _currentPoint;
                _previewDimension.Rotation = _useXAxis ? 0.0 : Math.PI / 2.0;
                UpdateTransient();
            }

            private void AddPreview()
            {
                try
                {
                    TransientManager.CurrentTransientManager.AddTransient(
                        _previewDimension,
                        TransientDrawingMode.DirectShortTerm,
                        128,
                        _viewportNumbers);
                    _previewAdded = true;
                }
                catch
                {
                }
            }

            private void UpdateTransient()
            {
                if (!_previewAdded)
                {
                    return;
                }

                try
                {
                    TransientManager.CurrentTransientManager.UpdateTransient(
                        _previewDimension,
                        _viewportNumbers);
                }
                catch
                {
                }
            }

            private void ErasePreview()
            {
                if (!_previewAdded)
                {
                    return;
                }

                try
                {
                    TransientManager.CurrentTransientManager.EraseTransient(
                        _previewDimension,
                        _viewportNumbers);
                }
                catch
                {
                }

                _previewAdded = false;
            }

            private static ViewTableRecord TryGetCurrentView(Editor editor)
            {
                if (editor == null)
                {
                    return null;
                }

                try
                {
                    return editor.GetCurrentView();
                }
                catch
                {
                    return null;
                }
            }

            private void ScheduleLatestViewRestore()
            {
                if (_latestChangedView != null)
                {
                    ViewTableRecord view = _latestChangedView;
                    _latestChangedView = null;
                    ScheduleSdxyViewRestore(view);
                    return;
                }

                if (_initialView != null)
                {
                    ViewTableRecord view = _initialView;
                    _initialView = null;
                    ScheduleSdxyViewRestore(view);
                }
            }

            public void Dispose()
            {
                if (_editor != null)
                {
                    _editor.PointMonitor -= EditorPointMonitor;
                }

                ErasePreview();
                _initialView?.Dispose();
                _latestChangedView?.Dispose();
                _previewDimension?.Dispose();
            }
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
}
