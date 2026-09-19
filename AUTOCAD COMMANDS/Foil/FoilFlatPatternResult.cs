using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Ket qua trien khai phoi. Moi gia tri o day la double DAY DU - khong lam tron.
    /// Viec lam tron chi xay ra trong <see cref="FormatReport"/> va tren UI.
    /// </summary>
    public class FoilFlatPatternResult
    {
        /// <summary>Chieu dai phoi - GIU NGUYEN gia tri nguoi dung nhap.</summary>
        public double BlankLength { get; set; }

        /// <summary>Chieu rong phoi = chieu dai trien khai cua bien dang.</summary>
        public double BlankWidth { get; set; }

        /// <summary>Tong chieu dai bien dang theo mold line (truoc khi bu chan).</summary>
        public double MoldLineTotalLength { get; set; }

        /// <summary>Tong luong bi tru = MoldLineTotalLength - BlankWidth.</summary>
        public double TotalDeduction { get { return MoldLineTotalLength - BlankWidth; } }

        public List<FoilBendInfo> Bends { get; private set; } = new List<FoilBendInfo>();

        public List<FoilProfileElement> Elements { get; private set; }
            = new List<FoilProfileElement>();

        public List<string> Notes { get; private set; } = new List<string>();

        public List<string> Warnings { get; private set; } = new List<string>();

        public List<string> Errors { get; private set; } = new List<string>();

        public bool HasErrors { get { return Errors.Count > 0; } }

        public bool IsUsable { get { return !HasErrors && BlankWidth > 0.0; } }

        public FoilSettings Settings { get; set; }

        public string MethodName { get; set; } = string.Empty;

        public string FormulaDescription { get; set; } = string.Empty;

        public int BendCount { get { return Bends.Count; } }

        /// <summary>
        /// Bang ket qua dang van ban, dung cho form ket qua va cho dong lenh.
        /// </summary>
        public string FormatReport()
        {
            FoilSettings s = Settings ?? new FoilSettings();
            string f = s.DisplayFormat;
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("--------------------------------------");
            sb.AppendLine("KET QUA TRIEN KHAI PHOI (FLAT PATTERN)");
            sb.AppendLine("--------------------------------------");
            sb.AppendLine();
            sb.AppendLine("Chieu dai phoi   : " + BlankLength.ToString(f, ci) + " mm");
            sb.AppendLine("Chieu rong phoi  : " + BlankWidth.ToString(f, ci) + " mm");
            sb.AppendLine();
            sb.AppendLine("Tong mold line   : " + MoldLineTotalLength.ToString(f, ci) + " mm");
            sb.AppendLine("Tong bu chan     : " + TotalDeduction.ToString(f, ci) + " mm");
            sb.AppendLine("So duong chan    : " + BendCount.ToString(ci));
            sb.AppendLine();
            sb.AppendLine("Phuong phap      : " + MethodName);
            sb.AppendLine("Chieu day T      : " + s.Thickness.ToString(f, ci) + " mm");
            sb.AppendLine("Ban kinh trong R : " + s.InsideRadius.ToString(f, ci) + " mm");

            if (s.Method == FoilBendMethod.BendDeductionKFactor ||
                s.Method == FoilBendMethod.BendAllowanceKFactor)
            {
                sb.AppendLine("K-Factor         : " + s.KFactor.ToString("0.####", ci));
            }
            else if (s.Method == FoilBendMethod.CustomShopRule)
            {
                sb.AppendLine("He so bu (Factor): " + s.CustomFactor.ToString("0.####", ci));
            }

            sb.AppendLine();
            sb.AppendLine("Cong thuc su dung:");
            foreach (string line in (FormulaDescription ?? string.Empty)
                .Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                sb.AppendLine("  " + line);
            }

            sb.AppendLine();
            sb.AppendLine("--------------------------------------");
            sb.AppendLine("CHI TIET TUNG DUONG CHAN");
            sb.AppendLine("--------------------------------------");

            if (Bends.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("  (Khong co duong chan - phoi phang)");
            }

            foreach (FoilBendInfo b in Bends)
            {
                sb.AppendLine();
                sb.AppendLine("#" + b.Index.ToString(ci) +
                              (b.Kind == FoilBendKind.Arc ? "  [cung ve san]" : "  [dinh goc nhon]"));
                sb.AppendLine("  Bend angle  (alpha) : " + b.BendAngleDeg.ToString(f, ci) + " do");
                sb.AppendLine("  Opening angle (beta): " + b.OpeningAngleDeg.ToString(f, ci) + " do");
                sb.AppendLine("  Huong chan          : " +
                              (b.Direction == FoilBendDirection.Up ? "UP (len)" : "DOWN (xuong)"));
                sb.AppendLine("  Inside radius R     : " + b.InsideRadius.ToString(f, ci) + " mm");
                sb.AppendLine("  BA (bend allowance) : " + b.BendAllowance.ToString(f, ci) + " mm");
                sb.AppendLine("  OSSB (setback/ben)  : " + b.OutsideSetback.ToString(f, ci) + " mm");
                sb.AppendLine("  BD (bend deduction) : " + b.BendDeduction.ToString(f, ci) + " mm");
                sb.AppendLine("  Tru thuc te         : " + b.EffectiveDeduction.ToString(f, ci) + " mm");
                sb.AppendLine("  Vi tri tren phoi    : " + b.FlatPosition.ToString(f, ci) + " mm");

                if (b.BendAllowance > 0.0)
                {
                    sb.AppendLine("  Vung chan           : " +
                                  b.FlatZoneStart.ToString(f, ci) + " .. " +
                                  b.FlatZoneEnd.ToString(f, ci) + " mm");
                }

                if (b.IsDiagonal)
                {
                    sb.AppendLine("  Duong chan XIEN     : " +
                                  (b.SkewAngleRad * FoilMath.RadToDeg).ToString(f, ci) + " do");
                }

                if (!string.IsNullOrEmpty(b.CalculationNote))
                {
                    sb.AppendLine("  Ghi chu             : " + b.CalculationNote);
                }
            }

            if (Notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--------------------------------------");
                sb.AppendLine("GHI CHU");
                sb.AppendLine("--------------------------------------");
                foreach (string note in Notes)
                {
                    sb.AppendLine("  - " + note);
                }
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--------------------------------------");
                sb.AppendLine("CANH BAO");
                sb.AppendLine("--------------------------------------");
                foreach (string warning in Warnings)
                {
                    sb.AppendLine("  (!) " + warning);
                }
            }

            if (Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--------------------------------------");
                sb.AppendLine("LOI");
                sb.AppendLine("--------------------------------------");
                foreach (string error in Errors)
                {
                    sb.AppendLine("  (X) " + error);
                }
            }

            return sb.ToString();
        }
    }
}
