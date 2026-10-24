using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{

    // ======================================================
    // SCC_SMART_COPY - SMART COPY
    // Cơ chế y hệt SS (Smart Stretch): quét nhiều crossing window,
    // chọn điểm đầu + điểm hướng để quyết định SX+/SX-/SY+/SY-,
    // lặp lại vô hạn cho tới khi Space/Enter/Esc.
    // Khác biệt duy nhất: thay vì gọi STRETCH gốc, lệnh này gọi COPY gốc
    // (copy nguyên khối đối tượng theo hướng/khoảng cách đã chọn).
    // L dùng chung với SS/SSD/SSD2/SX/SY qua SmartStretchSettingsStore,
    // hỗ trợ lấy L trực tiếp từ DXCalculator (keyword C/C2).
    // ======================================================
    public class SmartCopyCommands
    {
        private const double ComparisonTolerance = 1e-6;

        // SCC_SMART_COPY:
        // - Dùng ngay L đã lưu từ lần trước (chung với SS/SX/SY).
        // - Nếu cần đổi L thì gõ số trực tiếp, hoặc keyword L/X/C/C2 ngay tại prompt.
        // - Quét một hoặc nhiều crossing window.
        // - Chọn điểm đầu + điểm hướng để quyết định hướng copy.
        [CommandMethod("SCC_SMART_COPY")]
        public void SmartCopy()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            double length = SmartStretchSettingsStore.LoadLength();

            if (!SmartStretchCommands.TryPromptSmartStretchLengthSource(ed, "SCC", ref length))
            {
                return;
            }

            RunSmartCopyLoopWithLength(ed, db, length, "SCC");
        }

        private static void RunSmartCopyLoopWithLength(
            Editor ed,
            Database db,
            double initialLength,
            string commandLabel)
        {
            // Core lặp lại nhiều lần theo các hướng, y như SS.
            // Chỉ dừng khi người dùng nhấn Space/Enter hoặc Esc.
            // Tắt OSMODE tạm thời để điểm click không bị OSNAP kéo lệch.
            object previousOsMode = null;
            double length = initialLength;

            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");
                Application.SetSystemVariable("OSMODE", 0);

                SmartStretchSettingsStore.SaveLength(length);

                while (true)
                {
                    SmartCopyLoopResult result = RunSingleSmartCopyWithLength(ed, db, ref length, commandLabel);

                    if (result == SmartCopyLoopResult.StopRequested)
                    {
                        return;
                    }
                }
            }
            finally
            {
                if (previousOsMode != null)
                {
                    Application.SetSystemVariable("OSMODE", previousOsMode);
                }
            }
        }

        private static SmartCopyLoopResult RunSingleSmartCopyWithLength(
            Editor ed,
            Database db,
            ref double length,
            string commandLabel)
        {
            SmartStretchSelectionInput selectionInput =
                SmartStretchCommands.GetSmartStretchSelectionInput(
                    ed,
                    ref length,
                    commandLabel,
                    out bool stopRequested);
            if (stopRequested)
            {
                return SmartCopyLoopResult.StopRequested;
            }

            if (selectionInput == null)
            {
                return SmartCopyLoopResult.Retry;
            }

            SmartStretchCommands.ShowSmartStretchSelection(ed, selectionInput.SelectedObjectIds);
            if (!SmartStretchCommands.TryPromptSmartStretchStartPoint(
                ed,
                ref length,
                allowInteractiveLengthOverride: false,
                commandLabel: commandLabel,
                startPoint: out Point3d startPoint,
                stopRequested: out bool stopAtStartPointPrompt))
            {
                SmartStretchCommands.ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return stopAtStartPointPrompt
                    ? SmartCopyLoopResult.StopRequested
                    : SmartCopyLoopResult.Retry;
            }

            PromptResult directionResult = GetDirectionWithCopyPreview(
                ed,
                selectionInput,
                startPoint,
                length,
                out SmartStretchDirection direction,
                out Point3d secondPoint);
            if (directionResult.Status == PromptStatus.Cancel)
            {
                SmartStretchCommands.ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartCopyLoopResult.StopRequested;
            }

            if (directionResult.Status != PromptStatus.OK)
            {
                SmartStretchCommands.ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartCopyLoopResult.Retry;
            }

            if (direction == SmartStretchDirection.None)
            {
                SmartStretchCommands.ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                ed.WriteMessage("\nKhông xác định được hướng copy. Hãy làm lại.");
                return SmartCopyLoopResult.Retry;
            }

            SmartStretchCommands.ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
            ExecuteNativeCopy(ed, selectionInput, startPoint, secondPoint);

            SmartStretchDirection actualDirection = length < 0
                ? SmartStretchCommands.FlipDirection(direction)
                : direction;
            ed.WriteMessage(
                $"\n{commandLabel}: đã gọi COPY gốc theo {SmartStretchCommands.GetDirectionLabel(actualDirection)} với L = {SmartStretchCommands.FormatLength(length)}.");
            return SmartCopyLoopResult.Completed;
        }

        private enum SmartCopyLoopResult
        {
            Completed,
            StopRequested,
            Retry
        }

        private static void ExecuteNativeCopy(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d basePoint,
            Point3d secondPoint)
        {
            // Gọi COPY gốc bằng các crossing window đã lưu.
            // Zoom tạm vào vùng copy để giảm lỗi AutoCAD bỏ sót object ngoài màn hình.
            ViewTableRecord originalView = null;

            try
            {
                originalView = ed.GetCurrentView();
                Extents3d copyBounds =
                    SmartStretchCommands.GetStretchOperationBounds(selectionInput, basePoint, secondPoint);
                SmartStretchCommands.ZoomToStretchBounds(ed, copyBounds);

                List<object> args = new List<object> { "_.COPY" };
                SmartStretchSelectionMode currentMode = SmartStretchSelectionMode.Add;

                foreach (SmartStretchWindowSelection window in selectionInput.Windows)
                {
                    if (window.Mode != currentMode)
                    {
                        args.Add(
                            window.Mode == SmartStretchSelectionMode.Remove
                                ? "_R"
                                : "_A");
                        currentMode = window.Mode;
                    }

                    // Phải replay đúng kiểu Window/Crossing đã quét lúc bôi sáng,
                    // nếu không AutoCAD sẽ luôn dùng Crossing và copy thêm đối tượng
                    // chỉ giao với vùng (không được bôi sáng ở bước xem trước).
                    args.Add(window.IsWindowSelection ? "_W" : "_C");
                    args.Add(window.FirstPoint);
                    args.Add(window.SecondPoint);
                }

                args.Add(string.Empty);
                args.Add(basePoint);
                args.Add(secondPoint);
                // Thoát chế độ copy nhiều bản sau khi đã tạo đúng 1 bản copy.
                args.Add(string.Empty);

                ed.Command(args.ToArray());
            }
            finally
            {
                if (originalView != null)
                {
                    ed.SetCurrentView(originalView);
                    originalView.Dispose();
                }
            }
        }

        private static PromptResult GetDirectionWithCopyPreview(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d startPoint,
            double length,
            out SmartStretchDirection direction,
            out Point3d secondPoint)
        {
            // Jig preview: rê chuột để xem trước bản copy theo hướng trước khi click.
            using (SmartCopyPreviewJig jig =
                new SmartCopyPreviewJig(ed, selectionInput, startPoint, length))
            {
                PromptResult result = ed.Drag(jig);
                direction = jig.Direction;
                secondPoint = jig.SecondPoint;
                return result;
            }
        }

        private sealed class SmartCopyPreviewJig : DrawJig, IDisposable
        {
            private readonly Editor _editor;
            private readonly SmartStretchSelectionInput _selectionInput;
            private readonly Point3d _startPoint;
            private readonly double _length;
            private readonly Matrix3d _ucs;
            private readonly Matrix3d _ucsInverse;
            private readonly List<Entity> _previewEntities = new List<Entity>();
            private Point3d _cursorPoint;
            private SmartStretchDirection _direction;
            private Point3d _secondPoint;

            public SmartCopyPreviewJig(
                Editor editor,
                SmartStretchSelectionInput selectionInput,
                Point3d startPoint,
                double length)
            {
                _editor = editor;
                _selectionInput = selectionInput;
                _startPoint = startPoint;
                _length = length;
                _ucs = editor.CurrentUserCoordinateSystem;
                _ucsInverse = _ucs.Inverse();
                _cursorPoint = startPoint;
                _direction = SmartStretchDirection.None;
                _secondPoint = startPoint;
            }

            public SmartStretchDirection Direction => _direction;

            public Point3d SecondPoint => _secondPoint;

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                JigPromptPointOptions pointOptions =
                    new JigPromptPointOptions("\nChọn điểm sau để xem trước hướng copy: ");
                pointOptions.BasePoint = _startPoint;
                pointOptions.UseBasePoint = true;

                PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
                if (pointResult.Status == PromptStatus.Cancel)
                {
                    return SamplerStatus.Cancel;
                }

                if (pointResult.Status != PromptStatus.OK)
                {
                    return SamplerStatus.NoChange;
                }

                if (_cursorPoint.DistanceTo(pointResult.Value) <= ComparisonTolerance)
                {
                    return SamplerStatus.NoChange;
                }

                _cursorPoint = pointResult.Value;

                Point3d startUcs = _startPoint.TransformBy(_ucsInverse);
                Point3d nextUcs = _cursorPoint.TransformBy(_ucsInverse);
                SmartStretchDirection newDirection =
                    SmartStretchCommands.ResolveDirection(startUcs, nextUcs);

                if (newDirection != _direction)
                {
                    _direction = newDirection;
                    RebuildPreviewEntities();
                }

                if (_direction != SmartStretchDirection.None)
                {
                    Vector3d displacement =
                        SmartStretchCommands.GetDisplacementVector(_direction, _length, _ucs);
                    _secondPoint = _startPoint + displacement;
                }
                else
                {
                    _secondPoint = _startPoint;
                }

                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                foreach (Entity previewEntity in _previewEntities)
                {
                    previewEntity.WorldDraw(draw);
                }

                if (_direction != SmartStretchDirection.None)
                {
                    draw.Geometry.WorldLine(_startPoint, _secondPoint);
                }

                return true;
            }

            private void RebuildPreviewEntities()
            {
                DisposePreviewEntities();

                if (_direction == SmartStretchDirection.None)
                {
                    return;
                }

                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc?.Database;
                if (db == null)
                {
                    return;
                }

                Vector3d displacement =
                    SmartStretchCommands.GetDisplacementVector(_direction, _length, _ucs);
                Matrix3d displacementMatrix = Matrix3d.Displacement(displacement);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId objectId in _selectionInput.SelectedObjectIds)
                    {
                        Entity sourceEntity =
                            tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                        if (sourceEntity == null || sourceEntity.IsErased)
                        {
                            continue;
                        }

                        Entity previewEntity = sourceEntity.Clone() as Entity;
                        if (previewEntity == null)
                        {
                            continue;
                        }

                        try
                        {
                            previewEntity.TransformBy(displacementMatrix);
                        }
                        catch
                        {
                            previewEntity.Dispose();
                            continue;
                        }

                        _previewEntities.Add(previewEntity);
                    }

                    tr.Commit();
                }
            }

            private void DisposePreviewEntities()
            {
                foreach (Entity previewEntity in _previewEntities)
                {
                    previewEntity.Dispose();
                }

                _previewEntities.Clear();
            }

            public void Dispose()
            {
                DisposePreviewEntities();
            }
        }
    }
}
