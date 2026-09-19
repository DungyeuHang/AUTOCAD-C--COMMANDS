using System;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - HIEU CHUAN THEO MAY CHAN THAT (CALIBRATION)
    // ------------------------------------------------------------------------------------------
    // Quy trinh: cat mot mieng phoi thu co chieu dai phang DA BIET, chan mot lan, do lai
    // kich thuoc 2 canh theo MOLD LINE (den dinh goc nhon), roi giai NGUOC:
    //
    //     BD   = L1 + L2 - FlatLength
    //     OSSB = (R + T) * tan(alpha / 2)          (thuan tuy hinh hoc, khong phu thuoc K)
    //     BA   = 2 * OSSB - BD
    //     K    = (BA / alpha - R) / T
    //
    // Gia tri K thu duoc phan anh dung may chan, vat lieu va dao cu cua xuong. Ket qua co the
    // ghi thang vao bang chan (FoilBendTable) de dung lau dai.
    // ==========================================================================================

    public class FoilCalibrationInput
    {
        public double Thickness { get; set; }

        public double InsideRadius { get; set; }

        /// <summary>alpha - BEND ANGLE (do). Chan vuong goc = 90.</summary>
        public double BendAngleDeg { get; set; }

        /// <summary>Chieu dai phoi phang truoc khi chan (da do that).</summary>
        public double FlatLength { get; set; }

        /// <summary>Canh 1 sau khi chan, do theo mold line (den dinh goc nhon).</summary>
        public double Flange1 { get; set; }

        /// <summary>Canh 2 sau khi chan, do theo mold line (den dinh goc nhon).</summary>
        public double Flange2 { get; set; }
    }

    public class FoilCalibrationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public double BendDeduction { get; set; }

        public double BendAllowance { get; set; }

        public double OutsideSetback { get; set; }

        public double KFactor { get; set; }

        public string FormatReport(int precision)
        {
            string f = precision <= 0 ? "0" : "0." + new string('0', Math.Min(precision, 6));
            CultureInfo ci = CultureInfo.InvariantCulture;

            if (!Success)
            {
                return "KHONG HIEU CHUAN DUOC:" + Environment.NewLine + Message;
            }

            return
                "KET QUA HIEU CHUAN" + Environment.NewLine +
                "------------------" + Environment.NewLine +
                "Bend Deduction  BD : " + BendDeduction.ToString(f, ci) + " mm" + Environment.NewLine +
                "Outside Setback    : " + OutsideSetback.ToString(f, ci) + " mm" + Environment.NewLine +
                "Bend Allowance  BA : " + BendAllowance.ToString(f, ci) + " mm" + Environment.NewLine +
                "K-Factor           : " + KFactor.ToString("0.#####", ci) + Environment.NewLine +
                (string.IsNullOrEmpty(Message) ? string.Empty : Environment.NewLine + Message);
        }
    }

    public static class FoilCalibration
    {
        public static FoilCalibrationResult Solve(FoilCalibrationInput input)
        {
            FoilCalibrationResult result = new FoilCalibrationResult();
            CultureInfo ci = CultureInfo.InvariantCulture;

            if (input == null)
            {
                result.Message = "Thieu du lieu hieu chuan.";
                return result;
            }

            if (input.Thickness <= 0.0 || !FoilValidation.IsFinite(input.Thickness))
            {
                result.Message = "Chieu day T phai lon hon 0.";
                return result;
            }

            if (input.InsideRadius < 0.0 || !FoilValidation.IsFinite(input.InsideRadius))
            {
                result.Message = "Ban kinh trong R phai >= 0.";
                return result;
            }

            if (input.BendAngleDeg <= 0.0 || input.BendAngleDeg >= 180.0)
            {
                result.Message = "Goc chan alpha phai nam trong khoang 0 .. 180 do (khong bao gom hai dau).";
                return result;
            }

            if (input.FlatLength <= 0.0 || input.Flange1 <= 0.0 || input.Flange2 <= 0.0)
            {
                result.Message = "Chieu dai phoi phang va hai canh deu phai lon hon 0.";
                return result;
            }

            double alpha = input.BendAngleDeg * FoilMath.DegToRad;
            double moldLineSum = input.Flange1 + input.Flange2;

            double bd = moldLineSum - input.FlatLength;
            double ossb = (input.InsideRadius + input.Thickness) * Math.Tan(alpha * 0.5);
            double ba = 2.0 * ossb - bd;
            double k = (ba / alpha - input.InsideRadius) / input.Thickness;

            result.BendDeduction = bd;
            result.OutsideSetback = ossb;
            result.BendAllowance = ba;
            result.KFactor = k;
            result.Success = true;

            if (bd <= 0.0)
            {
                result.Message = string.Format(
                    ci,
                    "Canh bao: BD = {0:0.####} <= 0. Thong thuong tong mold line phai LON HON chieu dai " +
                    "phoi phang. Kiem tra lai cach do (co dang do theo mat trong thay vi mold line?).",
                    bd);
            }
            else if (k < 0.0 || k > 1.0)
            {
                result.Message = string.Format(
                    ci,
                    "Canh bao: K = {0:0.#####} nam ngoai khoang vat ly 0 .. 1. Kiem tra lai R, T hoac " +
                    "cach do canh. Gia tri BD = {1:0.####} van dung duoc truc tiep trong bang chan.",
                    k,
                    bd);
            }

            return result;
        }
    }
}
