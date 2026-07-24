using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    internal static class DimensionPrompt
    {
        private const double ComparisonTolerance = 1e-6;

        public static bool TryPromptDimensionMeasurement(
            Editor ed,
            Database db,
            string message,
            out double measurement,
            bool allowZero = false)
        {
            measurement = 0.0;

            while (true)
            {
                PromptEntityOptions options = new PromptEntityOptions(message);
                options.SetRejectMessage("\nChỉ hỗ trợ các loại DIM hợp lệ.");
                options.AddAllowedClass(typeof(Dimension), false);

                PromptEntityResult result = ed.GetEntity(options);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Dimension dimension =
                        tr.GetObject(result.ObjectId, OpenMode.ForRead) as Dimension;
                    if (dimension == null)
                    {
                        ed.WriteMessage("\nKhông đọc được dim. Hãy chọn lại.");
                        continue;
                    }

                    measurement = Math.Abs(dimension.Measurement);
                    if (double.IsNaN(measurement) ||
                        double.IsInfinity(measurement) ||
                        (!allowZero && measurement <= ComparisonTolerance))
                    {
                        ed.WriteMessage(
                            allowZero
                                ? "\nDim có measurement không hợp lệ. Hãy chọn lại."
                                : "\nDim có giá trị không hợp lệ (phải > 0). Hãy chọn lại.");
                        continue;
                    }

                    return true;
                }
            }
        }

        public static bool TryPromptStretchLengthFromDimensions(
            Editor ed,
            Database db,
            bool halfDifference,
            out double length)
        {
            length = 0.0;

            while (true)
            {
                if (!TryPromptDimensionMeasurement(
                    ed,
                    db,
                    "\nChọn dim gốc: ",
                    out double baseMeasurement,
                    allowZero: false))
                {
                    return false;
                }

                if (!TryPromptDimensionMeasurement(
                    ed,
                    db,
                    "\nChọn dim hiện hành: ",
                    out double currentMeasurement,
                    allowZero: false))
                {
                    return false;
                }

                double difference = Math.Abs(baseMeasurement - currentMeasurement);
                length = halfDifference ? difference / 2.0 : difference;
                if (length <= ComparisonTolerance)
                {
                    ed.WriteMessage("\nHai dim đang cho chênh lệch bằng 0. Hãy chọn lại.");
                    continue;
                }

                if (halfDifference)
                {
                    ed.WriteMessage(
                        $"\nL = (|{baseMeasurement.ToString("0.###", CultureInfo.InvariantCulture)} - {currentMeasurement.ToString("0.###", CultureInfo.InvariantCulture)}|) / 2 = {length.ToString("0.###", CultureInfo.InvariantCulture)}");
                }
                else
                {
                    ed.WriteMessage(
                        $"\nL = |{baseMeasurement.ToString("0.###", CultureInfo.InvariantCulture)} - {currentMeasurement.ToString("0.###", CultureInfo.InvariantCulture)}| = {length.ToString("0.###", CultureInfo.InvariantCulture)}");
                }
                return true;
            }
        }
    }
}
