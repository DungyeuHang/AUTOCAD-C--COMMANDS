using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // SX/SY là phiên bản C# tương đương hai Lisp cũ:
    // - Nhóm 1 và nhóm 2 được stretch ngược chiều nhau.
    // - L là tổng độ dịch chuyển; mỗi nhóm dịch L/2.
    // - Tăng/Giảm quyết định chiều dịch chuyển.
    public class SmartStretchAxisCommands
    {
        private const double ComparisonTolerance = 1e-6;

        [CommandMethod("SX")]
        public void SmartStretchX()
        {
            RunSymmetricAxisStretch(SmartStretchAxis.X, "SX");
        }

        [CommandMethod("SY")]
        public void SmartStretchY()
        {
            RunSymmetricAxisStretch(SmartStretchAxis.Y, "SY");
        }

        private static void RunSymmetricAxisStretch(
            SmartStretchAxis axis,
            string commandLabel)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;

            if (doc == null || ed == null)
            {
                return;
            }

            double length = SmartStretchSettingsStore.LoadLength();
            if (!TryPromptLengthSource(ed, commandLabel, ref length))
            {
                return;
            }

            int directionSign = 1; // Mặc định theo trường hợp tăng hết

            object previousOsMode = null;
            object previousSelectionOffscreen = null;
            try
            {
                previousOsMode = Application.GetSystemVariable("OSMODE");
                previousSelectionOffscreen =
                    Application.GetSystemVariable("SELECTIONOFFSCREEN");

                Application.SetSystemVariable("OSMODE", 0);
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                while (true)
                {
                    SelectionSet firstSelection = PromptSelection(
                        ed,
                        $"\n{commandLabel}: chọn nhóm đối tượng 1 (Enter/Esc để kết thúc): ");
                    if (firstSelection == null)
                    {
                        break;
                    }

                    SelectionSet secondSelection = PromptSelection(
                        ed,
                        $"\n{commandLabel}: chọn nhóm đối tượng 2 (Enter/Esc để kết thúc): ");
                    if (secondSelection == null)
                    {
                        break;
                    }

                    double halfLength = length / 2.0;
                    Point3d basePoint;
                    Point3d firstTarget;
                    Point3d secondTarget;
                    BuildAxisTargets(
                        ed,
                        axis,
                        directionSign,
                        halfLength,
                        out basePoint,
                        out firstTarget,
                        out secondTarget);

                    ExecuteNativeStretch(ed, firstSelection, basePoint, firstTarget);
                    ExecuteNativeStretch(ed, secondSelection, basePoint, secondTarget);

                    ed.WriteMessage(
                        $"\n{commandLabel}: đã stretch 2 nhóm với L = {FormatLength(length)} " +
                        $"({FormatLength(halfLength)} mỗi nhóm). Có thể chọn tiếp.");
                }
            }
            finally
            {
                if (previousSelectionOffscreen != null)
                {
                    Application.SetSystemVariable(
                        "SELECTIONOFFSCREEN",
                        previousSelectionOffscreen);
                }

                if (previousOsMode != null)
                {
                    Application.SetSystemVariable("OSMODE", previousOsMode);
                }
            }

            ed.WriteMessage($"\n{commandLabel}: đã kết thúc.");
        }

        private static bool TryPromptLengthSource(
            Editor ed,
            string commandLabel,
            ref double length)
        {
            while (true)
            {
                PromptKeywordOptions sourceOptions =
                    new PromptKeywordOptions(
                        $"\n{commandLabel}: chọn nguồn L [L/C] <{FormatLength(length)}> " +
                        "(Enter dùng giá trị đã lưu): ");
                sourceOptions.AllowNone = true;
                sourceOptions.AppendKeywordsToMessage = false;
                sourceOptions.Keywords.Add("L");
                sourceOptions.Keywords.Add("C");

                PromptResult sourceResult = ed.GetKeywords(sourceOptions);
                if (sourceResult.Status == PromptStatus.Cancel)
                {
                    return false;
                }

                if (sourceResult.Status == PromptStatus.None)
                {
                    return IsValidLength(length);
                }

                if (sourceResult.Status != PromptStatus.Keyword &&
                    sourceResult.Status != PromptStatus.OK)
                {
                    return false;
                }

                if (string.Equals(
                    sourceResult.StringResult,
                    "L",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (TryPromptManualLength(ed, length, out double manualLength))
                    {
                        length = manualLength;
                        SmartStretchSettingsStore.SaveLength(length);
                        ed.WriteMessage(
                            $"\n{commandLabel}: cập nhật L = {FormatLength(length)}.");
                        return true;
                    }

                    return false;
                }

                if (string.Equals(
                    sourceResult.StringResult,
                    "C",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (QuickCalculatorState.TryGetCurrentDisplayValue(out double calculatorLength) &&
                        IsValidLength(calculatorLength))
                    {
                        length = calculatorLength;
                        SmartStretchSettingsStore.SaveLength(length);
                        ed.WriteMessage(
                            $"\n{commandLabel}: lấy L = {FormatLength(length)} từ ô nhập calculator.");
                        return true;
                    }

                    ed.WriteMessage(
                        "\nÔ nhập calculator chưa có giá trị L hợp lệ (khác 0). " +
                        "Hãy nhập số/phép tính hoặc chọn lại history.");
                }
            }
        }

        private static bool TryPromptManualLength(
            Editor ed,
            double defaultLength,
            out double length)
        {
            PromptDoubleOptions options =
                new PromptDoubleOptions(
                    $"\nNhập L cho smart stretch <{FormatLength(defaultLength)}>: ");
            options.AllowNone = true;
            options.AllowNegative = true;
            options.AllowZero = false;
            options.DefaultValue = defaultLength;
            options.UseDefaultValue = true;

            PromptDoubleResult result = ed.GetDouble(options);
            if (result.Status == PromptStatus.Cancel)
            {
                length = 0.0;
                return false;
            }

            length = result.Status == PromptStatus.None
                ? defaultLength
                : result.Value;

            if (!IsValidLength(length))
            {
                ed.WriteMessage("\nL phải khác 0.");
                return false;
            }

            return true;
        }


        private static SelectionSet PromptSelection(
            Editor ed,
            string message)
        {
            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = message
            };

            PromptSelectionResult result = ed.GetSelection(options);
            return result.Status == PromptStatus.OK
                ? result.Value
                : null;
        }

        private static void BuildAxisTargets(
            Editor ed,
            SmartStretchAxis axis,
            int directionSign,
            double halfLength,
            out Point3d basePoint,
            out Point3d firstTarget,
            out Point3d secondTarget)
        {
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            basePoint = Point3d.Origin.TransformBy(ucs);

            double firstDistance = axis == SmartStretchAxis.X
                ? -directionSign * halfLength
                : directionSign * halfLength;
            double secondDistance = axis == SmartStretchAxis.X
                ? directionSign * halfLength
                : -directionSign * halfLength;

            Point3d firstUcsPoint = axis == SmartStretchAxis.X
                ? new Point3d(firstDistance, 0.0, 0.0)
                : new Point3d(0.0, firstDistance, 0.0);
            Point3d secondUcsPoint = axis == SmartStretchAxis.X
                ? new Point3d(secondDistance, 0.0, 0.0)
                : new Point3d(0.0, secondDistance, 0.0);

            firstTarget = firstUcsPoint.TransformBy(ucs);
            secondTarget = secondUcsPoint.TransformBy(ucs);
        }

        private static void ExecuteNativeStretch(
            Editor ed,
            SelectionSet selection,
            Point3d basePoint,
            Point3d targetPoint)
        {
            List<object> args = new List<object>
            {
                "_.STRETCH",
                selection,
                string.Empty,
                basePoint,
                targetPoint,
                string.Empty
            };

            ed.Command(args.ToArray());
        }

        private static bool IsValidLength(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   Math.Abs(value) > ComparisonTolerance;
        }

        private static string FormatLength(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private enum SmartStretchAxis
        {
            X,
            Y
        }
    }
}
