﻿﻿using Autodesk.AutoCAD.ApplicationServices;
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
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;


namespace AUTOCAD_COMMANDS
{

    // ======================================================
    // SS / SSD / SSD2 - SMART STRETCH
    // SS  : nhập L thủ công rồi stretch theo hướng click.
    // SSD : lấy L = |DIM1 - DIM2|.
    // SSD2: lấy L = |DIM1 - DIM2| / 2 và chạy 2 lượt stretch.
    // Lưu ý: phần stretch cuối gọi STRETCH gốc của AutoCAD để giữ hành vi gần chuẩn nhất.
    // ======================================================
    public class SmartStretchCommands
    {
        private const double ComparisonTolerance = 1e-6;

        // SS:
        // - Dùng ngay L đã lưu từ lần trước.
        // - Nếu cần đổi L thì gõ keyword L (Length) hoặc C (Calculator) ngay tại prompt chọn điểm đầu.
        // - Quét một hoặc nhiều crossing window.
        // - Chọn điểm đầu + điểm hướng để quyết định SX+/SX-/SY+/SY-.
        [CommandMethod("SS")]
        public void SmartStretch()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            double length = WorkspaceUiStateStore.TryGetDouble("smartstretch.length", out double savedLength)
                ? savedLength
                : 500.0; // Giá trị mặc định nếu chưa có cấu hình

            // Cho phép nhập số trực tiếp để set L mới, hoặc L/C/Enter như cũ
            if (!TryPromptSmartStretchLengthSource(ed, "SS", ref length))
            {
                return;
            }

            RunSmartStretchLoopWithLength(ed, db, length, "SS");
        }

        private static bool TryPromptSmartStretchLengthSource(
            Editor ed,
            string commandLabel,
            ref double length)
        {
            while (true)
            {
                PromptStringOptions sourceOptions =
                    new PromptStringOptions(
                        $"\n{commandLabel}: chọn nguồn L [L/C] <{FormatLength(length)}> " +
                        "(Enter dùng giá trị đã lưu): ")
                    {
                        AllowSpaces = false,
                        UseDefaultValue = false
                    };
                // PromptStringOptions does not support Keywords - parse manually below.

                PromptResult sourceResult = ed.GetString(sourceOptions);
                if (sourceResult.Status == PromptStatus.Cancel)
                {
                    return false;
                }

                if (sourceResult.Status == PromptStatus.None ||
                    string.IsNullOrWhiteSpace(sourceResult.StringResult))
                {
                    return length > ComparisonTolerance;
                }

                string input = sourceResult.StringResult.Trim();

                // If user typed a number directly, treat it as new length
                if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double numericLength))
                {
                    if (numericLength > ComparisonTolerance)
                    {
                        length = numericLength;
                        WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));
                        ed.WriteMessage($"\n{commandLabel}: cập nhật L = {FormatLength(length)}.");
                        return true;
                    }

                    ed.WriteMessage("\nL phải lớn hơn 0.");
                    continue;
                }

                if (string.Equals(input, "L", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryPromptStretchLength(ed, length, out double manualLength))
                    {
                        length = manualLength;
                        WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));
                        ed.WriteMessage($"\n{commandLabel}: cập nhật L = {FormatLength(length)}.");
                        return true;
                    }

                    return false;
                }

                if (string.Equals(input, "C", StringComparison.OrdinalIgnoreCase))
                {
                    if (QuickCalculatorState.TryGetCurrentDisplayValue(out double calculatorLength) &&
                        !double.IsNaN(calculatorLength) &&
                        !double.IsInfinity(calculatorLength) &&
                        calculatorLength > ComparisonTolerance)
                    {
                        length = calculatorLength;
                        WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));
                        ed.WriteMessage($"\n{commandLabel}: lấy L = {FormatLength(length)} từ ô nhập calculator.");
                        return true;
                    }

                    ed.WriteMessage(
                        "\nÔ nhập calculator chưa có giá trị L hợp lệ (> 0). Hãy nhập số/phép tính hoặc chọn lại history.");
                }
            }
        }

        // SSD:
        // - Chọn 2 DIM.
        // - L = trị tuyệt đối chênh lệch measurement của 2 DIM.
        // - Sau đó chạy cùng core stretch với SS.
        [CommandMethod("SSD")]
        public void SmartStretchByDim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            if (!DimensionPrompt.TryPromptStretchLengthFromDimensions(
                ed,
                db,
                halfDifference: false,
                out double length))
            {
                return;
            }

            RunSmartStretchLoopWithLength(ed, db, length, "SSD");
        }

        // SSD2_SMART_STRETCH_BY_DIM2:
        // - Chọn 2 DIM.
        // - L = |DIM1 - DIM2| / 2.
        // - Chạy 2 pass để xử lý các đối tượng đối xứng.
        [CommandMethod("SSD2_SMART_STRETCH_BY_DIM2")]
        public void SmartStretchByHalfDimDifference()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            if (!DimensionPrompt.TryPromptStretchLengthFromDimensions(
                ed,
                db,
                halfDifference: true,
                out double length))
            {
                return;
            }

            RunSmartStretchLoopWithLength(
                ed,
                db,
                length,
                "SSD2_SMART_STRETCH_BY_DIM2",
                passCount: 2);
        }

        private static void RunSmartStretchLoopWithLength(
            Editor ed,
            Database db,
            double initialLength,
            string commandLabel,
            int passCount = 1,
            bool allowInteractiveLengthOverride = false)
        {
            // Core dùng chung cho SS/SSD/SSD2.
            // Sau mỗi lượt stretch sẽ quay lại chọn tiếp.
            // Chỉ dừng khi người dùng nhấn Space/Enter hoặc Esc.
            // Tắt OSMODE tạm thời để điểm click không bị OSNAP kéo lệch.
            // Khi kết thúc/cancel luôn khôi phục OSMODE cũ.
            object previousOsMode = null;
            double length = initialLength;

            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");
                Application.SetSystemVariable("OSMODE", 0);

                WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));

                while (true)
                {
                    for (int pass = 1; pass <= passCount; pass++)
                    {
                        while (true)
                        {
                            if (passCount > 1)
                            {
                                ed.WriteMessage(
                                    $"\n{commandLabel}: thực hiện stretch lần {pass}/{passCount} với L = {length.ToString("0.###", CultureInfo.InvariantCulture)}.");
                            }

                            SmartStretchLoopResult result = RunSingleSmartStretchWithLength(
                                ed,
                                db,
                                ref length,
                                passCount > 1
                                    ? $"{commandLabel} [{pass}/{passCount}]"
                                    : commandLabel,
                                allowInteractiveLengthOverride);

                            if (result == SmartStretchLoopResult.Completed)
                            {
                                break;
                            }

                            if (result == SmartStretchLoopResult.StopRequested)
                            {
                                return;
                            }
                        }
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

        private static SmartStretchLoopResult RunSingleSmartStretchWithLength(
            Editor ed,
            Database db,
            ref double length,
            string commandLabel,
            bool allowInteractiveLengthOverride)
        {
            SmartStretchSelectionInput selectionInput =
                GetSmartStretchSelectionInput(
                    ed,
                    ref length,
                    commandLabel,
                    out bool stopRequested);
            if (stopRequested)
            {
                return SmartStretchLoopResult.StopRequested;
            }

            if (selectionInput == null)
            {
                return SmartStretchLoopResult.Retry;
            }

            ShowSmartStretchSelection(ed, selectionInput.SelectedObjectIds);
            if (!TryPromptSmartStretchStartPoint(
                ed,
                ref length,
                allowInteractiveLengthOverride,
                commandLabel,
                out Point3d startPoint,
                out bool stopAtStartPointPrompt))
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return stopAtStartPointPrompt
                    ? SmartStretchLoopResult.StopRequested
                    : SmartStretchLoopResult.Retry;
            }

            PromptResult directionResult = GetDirectionWithPreview(
                ed,
                selectionInput,
                startPoint,
                length,
                out SmartStretchDirection direction,
                out Point3d secondPoint);
            if (directionResult.Status == PromptStatus.Cancel)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartStretchLoopResult.StopRequested;
            }

            if (directionResult.Status != PromptStatus.OK)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                return SmartStretchLoopResult.Retry;
            }

            if (direction == SmartStretchDirection.None)
            {
                ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
                ed.WriteMessage("\nKhông xác định được hướng stretch. Hãy làm lại.");
                return SmartStretchLoopResult.Retry;
            }

            ClearSmartStretchSelection(selectionInput.SelectedObjectIds);
            ExecuteNativeStretch(ed, selectionInput, startPoint, secondPoint);

            ed.WriteMessage(
                $"\n{commandLabel}: đã gọi STRETCH gốc theo {GetDirectionLabel(direction)} với L = {FormatLength(length)}.");
            return SmartStretchLoopResult.Completed;
        }

        private static bool TryPromptSmartStretchStartPoint(
            Editor ed,
            ref double length,
            bool allowInteractiveLengthOverride,
            string commandLabel,
            out Point3d startPoint,
            out bool stopRequested)
        {
            startPoint = Point3d.Origin;
            stopRequested = false;

            while (true)
            {
                string message;
                if (allowInteractiveLengthOverride)
                {
                    message = $"\nChọn điểm đầu hoặc [L/C] <{FormatLength(length)}>, Space/Enter để kết thúc: ";
                }
                else
                {
                    message = "\nChọn điểm đầu hoặc Space/Enter để kết thúc: ";
                }

                PromptPointOptions startPointOptions = new PromptPointOptions(message) { AllowNone = true };
                if (allowInteractiveLengthOverride)
                {
                    startPointOptions.AppendKeywordsToMessage = false;
                    startPointOptions.Keywords.Add("L");
                    startPointOptions.Keywords.Add("C");
                }

                PromptPointResult startResult = ed.GetPoint(startPointOptions);
                if (startResult.Status == PromptStatus.Keyword)
                {
                    string keyword = startResult.StringResult;

                    if (string.Equals(keyword, "L", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryPromptStretchLength(ed, length, out double updatedLength))
                        {
                            length = updatedLength;
                            WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));
                            ed.WriteMessage($"\n{commandLabel}: cập nhật L = {FormatLength(length)}.");
                        }
                    }
                    else if (string.Equals(keyword, "C", StringComparison.OrdinalIgnoreCase))
                    {
                        TryUseCalculatorLength(ed, ref length, commandLabel);
                    }

                    continue;
                }

                if (startResult.Status == PromptStatus.None || startResult.Status == PromptStatus.Cancel)
                {
                    stopRequested = true;
                    return false;
                }

                if (startResult.Status != PromptStatus.OK)
                {
                    return false;
                }

                startPoint = startResult.Value;
                return true;
            }
        }

        private static bool TryPromptStretchLength(Editor ed, double defaultLength, out double length)
        {
            PromptDoubleOptions lengthOptions =
                new PromptDoubleOptions(
                    $"\nNhập L cho smart stretch <{defaultLength.ToString("0.###", CultureInfo.InvariantCulture)}>:");
            lengthOptions.AllowNegative = false;
            lengthOptions.AllowZero = false;
            lengthOptions.AllowNone = true;
            lengthOptions.DefaultValue = defaultLength;
            lengthOptions.UseDefaultValue = true;

            PromptDoubleResult lengthResult = ed.GetDouble(lengthOptions);
            if (lengthResult.Status == PromptStatus.Cancel)
            {
                length = 0.0;
                return false;
            }

            length = lengthResult.Status == PromptStatus.None
                ? defaultLength
                : lengthResult.Value;

            if (length <= ComparisonTolerance)
            {
                ed.WriteMessage("\nGiá trị L phải lớn hơn 0.");
                return false;
            }

            return true;
        }

        private static string FormatLength(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryUseCalculatorLength(
            Editor ed,
            ref double length,
            string commandLabel)
        {
            // C luôn lấy nội dung hiện tại của ô nhập. Không fallback về kết quả
            // '=' cũ vì người dùng có thể đang khôi phục một phép tính trong history.
            if (QuickCalculatorState.TryGetCurrentDisplayValue(out double displayValue) &&
                !double.IsNaN(displayValue) &&
                !double.IsInfinity(displayValue) &&
                displayValue > ComparisonTolerance)
            {
                length = displayValue;
                WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));
                ed.WriteMessage($"\n{commandLabel}: đã lấy L = {FormatLength(length)} từ ô nhập liệu của calculator.");
                return true;
            }

            ed.WriteMessage(
                "\nÔ nhập calculator chưa có giá trị hợp lệ (> 0). Hãy nhập số/phép tính hoặc chọn lại history.");
            return false;
        }

        private static SmartStretchSelectionInput GetSmartStretchSelectionInput(
            Editor ed,
            ref double length,
            string commandLabel,
            out bool stopRequested)
        {
            // Cho phép quét nhiều crossing window.
            // Mỗi window được lưu để lúc gọi STRETCH gốc truyền đúng vùng crossing.
            // Space/Enter ở ngay window đầu tiên sẽ thoát hẳn command loop.
            stopRequested = false;
            ed.WriteMessage(
                "\nWindow: quet nhieu vung neu can. Giu Shift khi quet de loai bot doi tuong dang bi overlap. Nhan Space/Enter o goc dau khi chua quet window nao de thoat, hoac sau khi da quet it nhat 1 window de sang buoc stretch.");

            List<SmartStretchWindowSelection> windows = new List<SmartStretchWindowSelection>();
            HashSet<ObjectId> selectedIds = new HashSet<ObjectId>();
            Dictionary<ObjectId, List<SmartStretchWindowSelection>> effectiveWindowsByObject =
                new Dictionary<ObjectId, List<SmartStretchWindowSelection>>();
            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                while (true)
                {
                    PromptPointOptions firstCornerOptions =
                        new PromptPointOptions(
                            windows.Count == 0
                                ? $"\nChọn góc đầu crossing window hoặc [L/C] <{FormatLength(length)}>, Space/Enter để thoát: "
                                : $"\nChọn góc đầu crossing window tiếp theo hoặc [L/C] <{FormatLength(length)}>, Space/Enter để stretch: ");
                    firstCornerOptions.AllowNone = true;
                    firstCornerOptions.AppendKeywordsToMessage = false;
                    firstCornerOptions.Keywords.Add("L");
                    firstCornerOptions.Keywords.Add("C");

                    PromptPointResult firstCornerResult = ed.GetPoint(firstCornerOptions);
                    if (firstCornerResult.Status == PromptStatus.Keyword)
                    {
                        string keyword = firstCornerResult.StringResult;

                        if (string.Equals(keyword, "L", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryPromptStretchLength(ed, length, out double updatedLength))
                            {
                                length = updatedLength;
                                WorkspaceUiStateStore.SaveValue("smartstretch.length", length.ToString(CultureInfo.InvariantCulture));
                                ed.WriteMessage(
                                    $"\n{commandLabel}: cập nhật L hiện tại = {FormatLength(length)}.");
                            }
                        }
                        else if (string.Equals(keyword, "C", StringComparison.OrdinalIgnoreCase))
                        {
                            TryUseCalculatorLength(ed, ref length, commandLabel);
                        }

                        continue;
                    }

                    if (firstCornerResult.Status == PromptStatus.None)
                    {
                        if (windows.Count == 0)
                        {
                            stopRequested = true;
                            ClearSmartStretchSelection(selectedIds.ToArray());
                            return null;
                        }

                        break;
                    }

                    if (firstCornerResult.Status == PromptStatus.Cancel)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        stopRequested = true;
                        return null;
                    }

                    if (firstCornerResult.Status != PromptStatus.OK)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        return null;
                    }

                    bool removeSelection = IsShiftPressed();

                    PromptCornerOptions secondCornerOptions =
                        new PromptCornerOptions(
                            "\nChọn góc đối diện crossing window: ",
                            firstCornerResult.Value);
                    PromptPointResult secondCornerResult = ed.GetCorner(secondCornerOptions);
                    if (secondCornerResult.Status == PromptStatus.Cancel)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        stopRequested = true;
                        return null;
                    }

                    if (secondCornerResult.Status != PromptStatus.OK)
                    {
                        ClearSmartStretchSelection(selectedIds.ToArray());
                        return null;
                    }

                    removeSelection = removeSelection || IsShiftPressed();

                    PromptSelectionResult crossingResult = ed.SelectCrossingWindow(
                        firstCornerResult.Value,
                        secondCornerResult.Value);
                    if (crossingResult.Status != PromptStatus.OK || crossingResult.Value == null)
                    {
                        ed.WriteMessage("\nWindow này chưa bắt được đối tượng nào.");
                        continue;
                    }

                    ObjectId[] crossingIds = crossingResult.Value.GetObjectIds();
                    SmartStretchSelectionMode mode = removeSelection
                        ? SmartStretchSelectionMode.Remove
                        : SmartStretchSelectionMode.Add;
                    SmartStretchWindowSelection windowSelection =
                        new SmartStretchWindowSelection(
                            firstCornerResult.Value,
                            secondCornerResult.Value,
                            mode);

                    if (mode == SmartStretchSelectionMode.Remove)
                    {
                        ObjectId[] previouslySelectedIds = selectedIds.ToArray();
                        int removedCount = 0;

                        foreach (ObjectId objectId in crossingIds)
                        {
                            if (selectedIds.Remove(objectId))
                            {
                                removedCount++;
                                effectiveWindowsByObject.Remove(objectId);
                            }
                        }

                        if (removedCount == 0)
                        {
                            ed.WriteMessage(
                                "\nShift window này không loại được đối tượng nào trong tập chọn hiện tại.");
                            continue;
                        }

                        windows.Add(windowSelection);
                        RefreshSmartStretchSelection(previouslySelectedIds, selectedIds.ToArray());
                        ed.WriteMessage(
                            $"\nĐã loại {removedCount} đối tượng. Còn lại {selectedIds.Count} đối tượng.");
                    }
                    else
                    {
                        windows.Add(windowSelection);

                        foreach (ObjectId objectId in crossingIds)
                        {
                            selectedIds.Add(objectId);

                            if (!effectiveWindowsByObject.TryGetValue(
                                objectId,
                                out List<SmartStretchWindowSelection> objectWindows))
                            {
                                objectWindows = new List<SmartStretchWindowSelection>();
                                effectiveWindowsByObject[objectId] = objectWindows;
                            }

                            objectWindows.Add(windowSelection);
                        }

                        ShowSmartStretchSelection(ed, selectedIds.ToArray());
                        ed.WriteMessage(
                            $"\nĐã gom {selectedIds.Count} đối tượng. Có thể quét thêm hoặc giữ Shift để loại bớt rồi nhấn Space/Enter để tiếp tục.");
                    }
                }
            }
            finally
            {
                if (previousSelectionOffscreen != null)
                {
                    Application.SetSystemVariable("SELECTIONOFFSCREEN", previousSelectionOffscreen);
                }
            }

            if (windows.Count == 0 || selectedIds.Count == 0)
            {
                ed.WriteMessage("\nChưa có đối tượng nào được chọn.");
                return null;
            }

            return SmartStretchSelectionInput.CreateSelection(
                windows,
                selectedIds,
                effectiveWindowsByObject);
        }

        private enum SmartStretchLoopResult
        {
            Completed,
            StopRequested,
            Retry
        }

        private static void ExecuteNativeStretch(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d basePoint,
            Point3d secondPoint)
        {
            // Gọi STRETCH gốc bằng các crossing window đã lưu.
            // Zoom tạm vào vùng stretch để giảm lỗi AutoCAD bỏ sót object ngoài màn hình.
            ViewTableRecord originalView = null;

            try
            {
                originalView = ed.GetCurrentView();
                Extents3d stretchBounds =
                    GetStretchOperationBounds(selectionInput, basePoint, secondPoint);
                ZoomToStretchBounds(ed, stretchBounds);

                List<object> args = new List<object> { "_.STRETCH" };
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

                    args.Add("_C");
                    args.Add(window.FirstPoint);
                    args.Add(window.SecondPoint);
                }

                args.Add(string.Empty);
                args.Add(basePoint);
                args.Add(secondPoint);

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

        private static Extents3d GetStretchOperationBounds(
            SmartStretchSelectionInput selectionInput,
            Point3d basePoint,
            Point3d secondPoint)
        {
            List<Point3d> points = new List<Point3d> { basePoint, secondPoint };

            if (selectionInput?.Windows != null)
            {
                foreach (SmartStretchWindowSelection window in selectionInput.Windows)
                {
                    points.Add(window.FirstPoint);
                    points.Add(window.SecondPoint);
                }
            }

            double minX = points.Min(point => point.X);
            double minY = points.Min(point => point.Y);
            double minZ = points.Min(point => point.Z);
            double maxX = points.Max(point => point.X);
            double maxY = points.Max(point => point.Y);
            double maxZ = points.Max(point => point.Z);

            double width = Math.Max(maxX - minX, 1.0);
            double height = Math.Max(maxY - minY, 1.0);
            double paddingX = Math.Max(width * 0.15, 10.0);
            double paddingY = Math.Max(height * 0.15, 10.0);

            return new Extents3d(
                new Point3d(minX - paddingX, minY - paddingY, minZ),
                new Point3d(maxX + paddingX, maxY + paddingY, maxZ));
        }

        private static void ZoomToStretchBounds(Editor ed, Extents3d bounds)
        {
            Point3d minPoint = bounds.MinPoint;
            Point3d maxPoint = bounds.MaxPoint;
            ed.Command("_.ZOOM", "_W", minPoint, maxPoint);
        }

        private static PromptResult GetDirectionWithPreview(
            Editor ed,
            SmartStretchSelectionInput selectionInput,
            Point3d startPoint,
            double length,
            out SmartStretchDirection direction,
            out Point3d secondPoint)
        {
            // Jig preview: rê chuột để xem hướng stretch trước khi click.
            // Preview là mô phỏng, kết quả cuối vẫn do STRETCH gốc xử lý.
            using (SmartStretchPreviewJig jig =
                new SmartStretchPreviewJig(ed, selectionInput, startPoint, length))
            {
                PromptResult result = ed.Drag(jig);
                direction = jig.Direction;
                secondPoint = jig.SecondPoint;
                return result;
            }
        }

        private static void ShowSmartStretchSelection(Editor ed, ObjectId[] objectIds)
        {
            if (ed == null || objectIds == null || objectIds.Length == 0)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc?.Database;
            if (db == null)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objectId in objectIds)
                {
                    Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                    entity?.Highlight();
                }

                tr.Commit();
            }

            Application.UpdateScreen();
        }

        private static void RefreshSmartStretchSelection(
            ObjectId[] previousObjectIds,
            ObjectId[] currentObjectIds)
        {
            if (previousObjectIds != null && previousObjectIds.Length > 0)
            {
                ClearSmartStretchSelection(previousObjectIds);
            }

            if (currentObjectIds != null && currentObjectIds.Length > 0)
            {
                ShowSmartStretchSelection(
                    Application.DocumentManager.MdiActiveDocument?.Editor,
                    currentObjectIds);
            }
        }

        private static List<int> FindStretchIndicesInsideWindow(
            ObjectId sourceObjectId,
            Entity entity,
            SmartStretchSelectionInput selectionInput,
            Matrix3d ucsInverse)
        {
            List<Point3d> stretchPoints = GetStretchPoints(entity);
            if (stretchPoints.Count == 0)
            {
                return new List<int>();
            }

            return stretchPoints
                .Select((point, index) => new
                {
                    Index = index,
                    Point = point.TransformBy(ucsInverse)
                })
                .Where(item => selectionInput.GetEffectiveWindowsForObject(sourceObjectId).Any(window =>
                {
                    Point3d firstCornerUcs = window.FirstPoint.TransformBy(ucsInverse);
                    Point3d secondCornerUcs = window.SecondPoint.TransformBy(ucsInverse);

                    double minX = Math.Min(firstCornerUcs.X, secondCornerUcs.X) - ComparisonTolerance;
                    double maxX = Math.Max(firstCornerUcs.X, secondCornerUcs.X) + ComparisonTolerance;
                    double minY = Math.Min(firstCornerUcs.Y, secondCornerUcs.Y) - ComparisonTolerance;
                    double maxY = Math.Max(firstCornerUcs.Y, secondCornerUcs.Y) + ComparisonTolerance;

                    return item.Point.X >= minX &&
                           item.Point.X <= maxX &&
                           item.Point.Y >= minY &&
                           item.Point.Y <= maxY;
                }))
                .Select(item => item.Index)
                .ToList();
        }

        private static void ClearSmartStretchSelection(ObjectId[] objectIds)
        {
            if (objectIds == null || objectIds.Length == 0)
            {
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc?.Database;
            if (db == null)
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objectId in objectIds)
                {
                    Entity entity = tr.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                    entity?.Unhighlight();
                }

                tr.Commit();
            }

            Application.UpdateScreen();
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsShiftPressed()
        {
            const int ShiftVirtualKey = 0x10;

            if ((GetAsyncKeyState(ShiftVirtualKey) & 0x8000) != 0)
            {
                return true;
            }

            return (WF.Control.ModifierKeys & WF.Keys.Shift) == WF.Keys.Shift;
        }

        private static SmartStretchDirection ResolveDirection(Point3d startUcs, Point3d nextUcs)
        {
            double dx = nextUcs.X - startUcs.X;
            double dy = nextUcs.Y - startUcs.Y;

            if (Math.Abs(dx) < ComparisonTolerance && Math.Abs(dy) < ComparisonTolerance)
            {
                return SmartStretchDirection.None;
            }

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                return dx >= 0.0
                    ? SmartStretchDirection.PositiveX
                    : SmartStretchDirection.NegativeX;
            }

            return dy >= 0.0
                ? SmartStretchDirection.PositiveY
                : SmartStretchDirection.NegativeY;
        }

        private static Vector3d GetDisplacementVector(
            SmartStretchDirection direction,
            double length,
            Matrix3d ucs)
        {
            Vector3d ucsVector;

            switch (direction)
            {
                case SmartStretchDirection.PositiveX:
                    ucsVector = new Vector3d(length, 0.0, 0.0);
                    break;
                case SmartStretchDirection.NegativeX:
                    ucsVector = new Vector3d(-length, 0.0, 0.0);
                    break;
                case SmartStretchDirection.PositiveY:
                    ucsVector = new Vector3d(0.0, length, 0.0);
                    break;
                case SmartStretchDirection.NegativeY:
                    ucsVector = new Vector3d(0.0, -length, 0.0);
                    break;
                default:
                    return new Vector3d(0.0, 0.0, 0.0);
            }

            return ucsVector.TransformBy(ucs);
        }

        private static List<SmartStretchEntityInfo> CollectStretchInfos(
            SelectionSet selectionSet,
            Transaction tr,
            Matrix3d ucs)
        {
            List<SmartStretchEntityInfo> infos = new List<SmartStretchEntityInfo>();

            foreach (SelectedObject selectedObject in selectionSet)
            {
                Entity entity = tr.GetObject(selectedObject.ObjectId, OpenMode.ForWrite) as Entity;
                if (entity == null || entity.IsErased)
                {
                    continue;
                }

                List<Point3d> stretchPoints = GetStretchPoints(entity);
                List<Point3d> stretchPointsUcs = stretchPoints
                    .Select(point => point.TransformBy(ucs.Inverse()))
                    .ToList();

                if (stretchPointsUcs.Count == 0)
                {
                    continue;
                }

                infos.Add(new SmartStretchEntityInfo(entity, stretchPointsUcs));
            }

            return infos;
        }

        private static List<Point3d> GetStretchPoints(Entity entity)
        {
            try
            {
                Point3dCollection points = new Point3dCollection();
                entity.GetStretchPoints(points);
                return points.Cast<Point3d>().ToList();
            }
            catch
            {
                return new List<Point3d>();
            }
        }

        private static Dictionary<ObjectId, List<int>> FindNearestStretchIndices(
            IEnumerable<SmartStretchEntityInfo> infos,
            Point3d startUcs,
            SmartStretchDirection direction)
        {
            Dictionary<ObjectId, List<int>> result =
                new Dictionary<ObjectId, List<int>>();

            foreach (SmartStretchEntityInfo info in infos)
            {
                if (info.StretchPointsUcs.Count == 0)
                {
                    continue;
                }

                List<double> distances = info.StretchPointsUcs
                    .Select(point => point.DistanceTo(startUcs))
                    .ToList();

                double minDistance = distances.Min();
                double tolerance = Math.Max(ComparisonTolerance, minDistance * 0.1);

                List<int> indices = distances
                    .Select((distance, index) => new { distance, index })
                    .Where(item => item.distance <= minDistance + tolerance)
                    .Select(item => item.index)
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList();

                if (indices.Count > 0)
                {
                    result[info.Entity.ObjectId] = indices;
                }
            }

            return result;
        }

        private static bool TryStretchEntity(
            Entity entity,
            IEnumerable<int> pointIndices,
            Vector3d displacement)
        {
            try
            {
                IntegerCollection indices = new IntegerCollection();
                foreach (int index in pointIndices.Distinct().OrderBy(value => value))
                {
                    indices.Add(index);
                }

                if (indices.Count == 0)
                {
                    return false;
                }

                entity.MoveStretchPointsAt(indices, displacement);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class SmartStretchPreviewJig : DrawJig, IDisposable
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

            public SmartStretchPreviewJig(
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
                    new JigPromptPointOptions("\nChọn điểm sau để xem trước hướng stretch: ");
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
                SmartStretchDirection newDirection = ResolveDirection(startUcs, nextUcs);

                if (newDirection != _direction)
                {
                    _direction = newDirection;
                    RebuildPreviewEntities();
                }

                if (_direction != SmartStretchDirection.None)
                {
                    Vector3d displacement = GetDisplacementVector(_direction, _length, _ucs);
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

                Vector3d displacement = GetDisplacementVector(_direction, _length, _ucs);

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

                        List<int> indices = FindStretchIndicesInsideWindow(
                            sourceEntity.ObjectId,
                            previewEntity,
                            _selectionInput,
                            _ucsInverse);
                        if (indices.Count == 0 ||
                            !TryStretchEntity(previewEntity, indices, displacement))
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

        private static string GetDirectionLabel(SmartStretchDirection direction)
        {
            switch (direction)
            {
                case SmartStretchDirection.PositiveX:
                    return "SX+";
                case SmartStretchDirection.NegativeX:
                    return "SX-";
                case SmartStretchDirection.PositiveY:
                    return "SY+";
                case SmartStretchDirection.NegativeY:
                    return "SY-";
                default:
                    return "?";
            }
        }
    }
}
