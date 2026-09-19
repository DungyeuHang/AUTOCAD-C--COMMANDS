using System;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - CALCULATION ENGINE (STRATEGY)
    // ------------------------------------------------------------------------------------------
    // Moi chien luoc tra ve DUNG 2 dai luong doc lap:
    //
    //   OutsideSetback (OSSB) : luong bi TRU o MOI BEN cua bend, do doc theo canh.
    //   BendAllowance  (BA)   : CHIEU RONG cua vung chan tren phoi da trai phang.
    //
    // Tu do:  BD = 2 * OSSB - BA      va      Flat = sum(L_mold_line) - sum(BD)
    //
    // Mo hinh nay bao duoc CA quy tac xuong lan cong thuc ky thuat:
    //
    //   * Quy tac xuong  : OSSB = Factor * T ,  BA = 0
    //                      => canh co N duong chan mat di N * Factor * T  (dung yeu cau)
    //                      => BD = 2 * Factor * T cho moi bend
    //
    //   * K-factor       : OSSB = (R + T) * tan(alpha/2) ,  BA = alpha * (R + K*T)
    //                      => BD = 2*(R+T)*tan(alpha/2) - alpha*(R + K*T)
    //
    // LUU Y KY THUAT QUAN TRONG:
    // "Bend Deduction + K-Factor" va "Bend Allowance + K-Factor" la HAI CACH VIET cua cung mot
    // phep tinh, lien he boi dong nhat thuc BD = 2*OSSB - BA. Voi cung T, R, alpha, K chung cho
    // ra CUNG chieu rong phoi. Giu ca hai vi ky su quen dung ca hai ngon ngu, va vi ho bao cao
    // gia tri chinh khac nhau (BD hay BA). Muon ket qua KHAC BIET thuc su thi dung Bend Table.
    // ==========================================================================================

    public class FoilBendRequest
    {
        /// <summary>alpha - BEND ANGLE (radian), goc vat lieu bi uon qua.</summary>
        public double BendAngleRad { get; set; }

        /// <summary>R - ban kinh chan TRONG.</summary>
        public double InsideRadius { get; set; }

        /// <summary>T - chieu day ton.</summary>
        public double Thickness { get; set; }

        public FoilSettings Settings { get; set; }
    }

    public class FoilBendCompensation
    {
        public double OutsideSetback { get; set; }

        public double BendAllowance { get; set; }

        public double KFactorUsed { get; set; }

        public string Note { get; set; } = string.Empty;

        public double BendDeduction { get { return 2.0 * OutsideSetback - BendAllowance; } }
    }

    public interface IFoilBendStrategy
    {
        string DisplayName { get; }

        /// <summary>Cong thuc dang van ban, hien thi trong bang ket qua.</summary>
        string FormulaDescription { get; }

        FoilBendCompensation Compute(FoilBendRequest request);
    }

    /// <summary>
    /// QUY TAC XUONG: moi duong chan an vao moi canh ke mot luong Factor * T.
    /// Vi du Factor = 1, T = 1.2:  canh 20 mm co 1 duong chan -> 20 - 1.2 = 18.8
    ///                             canh 20 mm co 2 duong chan -> 20 - 2.4 = 17.6
    /// Day KHONG phai cong thuc ky thuat - day la quy tac kinh nghiem cua xuong.
    /// </summary>
    public class FoilCustomShopRuleStrategy : IFoilBendStrategy
    {
        public string DisplayName { get { return "Custom / Shop Rule"; } }

        public string FormulaDescription
        {
            get
            {
                return "OSSB = Factor * T  (moi ben);  BA = 0;  BD = 2 * Factor * T" +
                       Environment.NewLine +
                       "=> Canh co N duong chan: contribution = L - N * Factor * T";
            }
        }

        public FoilBendCompensation Compute(FoilBendRequest request)
        {
            FoilSettings s = request.Settings;
            double setback = s.CustomFactor * request.Thickness;
            string note = "Shop rule: Factor * T";

            if (s.CustomRuleScaleByAngle)
            {
                // Tuy chon: ty le theo goc chan, chuan hoa ve 1.0 tai alpha = 90 do
                // (vi tan(45) = 1 nen 90 do giu nguyen gia tri goc).
                double halfAngle = request.BendAngleRad * 0.5;
                double factor = Math.Tan(FoilMath.Clamp(halfAngle, 0.0, 89.5 * FoilMath.DegToRad));
                setback *= factor;
                note = "Shop rule: Factor * T * tan(alpha/2)";
            }

            return new FoilBendCompensation
            {
                OutsideSetback = setback,
                BendAllowance = 0.0,
                KFactorUsed = double.NaN,
                Note = note
            };
        }
    }

    /// <summary>
    /// Cong thuc tieu chuan dua tren K-factor. Lop co so dung chung cho ca 2 cach bao cao
    /// (BD hoac BA) vi ve mat toan hoc chung dong nhat.
    /// </summary>
    public abstract class FoilKFactorStrategyBase : IFoilBendStrategy
    {
        public abstract string DisplayName { get; }

        public abstract string FormulaDescription { get; }

        public FoilBendCompensation Compute(FoilBendRequest request)
        {
            FoilSettings s = request.Settings;
            double alpha = request.BendAngleRad;
            double r = request.InsideRadius;
            double t = request.Thickness;
            double k = s.KFactor;

            // Chan tan(alpha/2) khong cho phan ky khi alpha -> 180 do.
            double halfAngle = FoilMath.Clamp(alpha * 0.5, 0.0, s.MaxBendAngleRad * 0.5);

            double ossb = (r + t) * Math.Tan(halfAngle);
            double ba = alpha * (r + k * t);

            return new FoilBendCompensation
            {
                OutsideSetback = ossb,
                BendAllowance = ba,
                KFactorUsed = k,
                Note = string.Format(
                    CultureInfo.InvariantCulture,
                    "R_neutral = R + K*T = {0:0.#####}",
                    r + k * t)
            };
        }
    }

    public class FoilBendDeductionKFactorStrategy : FoilKFactorStrategyBase
    {
        public override string DisplayName { get { return "Bend Deduction + K-Factor"; } }

        public override string FormulaDescription
        {
            get
            {
                return "BA   = alpha(rad) * (R + K * T)" + Environment.NewLine +
                       "OSSB = (R + T) * tan(alpha / 2)" + Environment.NewLine +
                       "BD   = 2 * OSSB - BA" + Environment.NewLine +
                       "Flat = sum(L mold-line) - sum(BD)";
            }
        }
    }

    public class FoilBendAllowanceKFactorStrategy : FoilKFactorStrategyBase
    {
        public override string DisplayName { get { return "Bend Allowance + K-Factor"; } }

        public override string FormulaDescription
        {
            get
            {
                return "BA   = alpha(rad) * (R + K * T)" + Environment.NewLine +
                       "OSSB = (R + T) * tan(alpha / 2)" + Environment.NewLine +
                       "Flat = sum(L - OSSB truoc - OSSB sau) + sum(BA)" + Environment.NewLine +
                       "(Tuong duong BD vi BD = 2*OSSB - BA)";
            }
        }
    }

    /// <summary>
    /// Tra BANG CHAN thuc te cua xuong. Bang cho truc tiep BD (va tuy chon BA) theo
    /// (T, R, goc chan). Day la duong de tool bam sat may chan that thay vi ly thuyet.
    /// </summary>
    public class FoilBendTableStrategy : IFoilBendStrategy
    {
        private readonly FoilBendTable _table;

        public FoilBendTableStrategy(FoilBendTable table)
        {
            _table = table;
        }

        public string DisplayName { get { return "Custom Bend Table"; } }

        public string FormulaDescription
        {
            get
            {
                return "BD lay truc tiep tu bang chan cua xuong theo (T, R, goc)." + Environment.NewLine +
                       "OSSB = (BD + BA) / 2 ;  BA = 0 neu bang khong khai bao cot Allowance.";
            }
        }

        public FoilBendCompensation Compute(FoilBendRequest request)
        {
            if (_table == null)
            {
                throw new FoilCalculationException(
                    "Chua nap duoc bang chan (Bend Table). Hay chon file bang chan trong cai dat.");
            }

            FoilBendTableEntry entry = _table.Lookup(
                request.Thickness,
                request.InsideRadius,
                request.BendAngleRad * FoilMath.RadToDeg);

            if (entry == null)
            {
                throw new FoilCalculationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "Bang chan khong co du lieu cho T = {0:0.###}, R = {1:0.###}, goc = {2:0.##} do.",
                    request.Thickness,
                    request.InsideRadius,
                    request.BendAngleRad * FoilMath.RadToDeg));
            }

            double ba = entry.BendAllowance;
            double bd = entry.BendDeduction;

            // BD = 2*OSSB - BA  =>  OSSB = (BD + BA) / 2
            return new FoilBendCompensation
            {
                OutsideSetback = (bd + ba) * 0.5,
                BendAllowance = ba,
                KFactorUsed = double.NaN,
                Note = entry.SourceNote
            };
        }
    }

    public class FoilCalculationException : Exception
    {
        public FoilCalculationException(string message) : base(message)
        {
        }
    }

    public static class FoilStrategyFactory
    {
        /// <summary>
        /// Tao chien luoc tuong ung. Voi BendTable, <paramref name="table"/> phai duoc nap truoc
        /// (tang AutoCAD/UI chiu trach nhiem doc file).
        /// </summary>
        public static IFoilBendStrategy Create(FoilSettings settings, FoilBendTable table)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            switch (settings.Method)
            {
                case FoilBendMethod.CustomShopRule:
                    return new FoilCustomShopRuleStrategy();
                case FoilBendMethod.BendDeductionKFactor:
                    return new FoilBendDeductionKFactorStrategy();
                case FoilBendMethod.BendAllowanceKFactor:
                    return new FoilBendAllowanceKFactorStrategy();
                case FoilBendMethod.BendTable:
                    return new FoilBendTableStrategy(table);
                default:
                    return new FoilCustomShopRuleStrategy();
            }
        }
    }
}
