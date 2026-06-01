using System;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    internal static class CadFormatHelper
    {
        public static string FormatNumber(
            double value,
            double numericTolerance,
            string decimalFormat = "0.####")
        {
            if (Math.Abs(value - Math.Round(value)) <= numericTolerance)
            {
                return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString(decimalFormat, CultureInfo.InvariantCulture);
        }

        public static string FormatOffset(
            double value,
            double numericTolerance,
            string decimalFormat = "0.####")
        {
            if (Math.Abs(value) <= numericTolerance)
            {
                return string.Empty;
            }

            return value > 0.0
                ? " + " + FormatNumber(value, numericTolerance, decimalFormat)
                : " - " + FormatNumber(Math.Abs(value), numericTolerance, decimalFormat);
        }
    }
}
