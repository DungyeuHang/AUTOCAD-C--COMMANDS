using System;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - CAU HINH
    // ------------------------------------------------------------------------------------------
    // Khong hard-code gia tri quan trong o nhieu noi. Moi thu nam o day va duoc luu lai qua
    // FoilSettingsStore (TSV trong %AppData%\DUNGX\AUTOCAD_COMMANDS, giong AutoCut).
    // ==========================================================================================

    /// <summary>Phuong phap tinh bu chan.</summary>
    public enum FoilBendMethod
    {
        /// <summary>Quy tac xuong: moi bend an vao moi canh ke mot luong Factor * T.</summary>
        CustomShopRule = 0,

        /// <summary>Tieu chuan: Bend Deduction suy ra tu K-factor.</summary>
        BendDeductionKFactor = 1,

        /// <summary>Tieu chuan: Bend Allowance suy ra tu K-factor.</summary>
        BendAllowanceKFactor = 2,

        /// <summary>Tra bang chan do thuc te cua xuong.</summary>
        BendTable = 3
    }

    /// <summary>
    /// Cach hieu ban kinh cua CUNG da ve san tren polyline bien dang.
    /// Chi anh huong khi polyline co bulge (goc da bo tron).
    /// </summary>
    public enum FoilArcRadiusInterpretation
    {
        /// <summary>Ban kinh cung ve = ban kinh chan TRONG (mac dinh cho bien dang 1 net).</summary>
        InsideRadius = 0,

        /// <summary>Ban kinh cung ve = ban kinh chan NGOAI => R_trong = R_ve - T.</summary>
        OutsideRadius = 1,

        /// <summary>Ban kinh cung ve = ban kinh duong tam ton => R_trong = R_ve - T/2.</summary>
        MidRadius = 2
    }

    /// <summary>
    /// BU BE DAY TAI GOC LOM ("may duong chan dac biet").
    ///
    /// Nguoi dung chi chon DUONG BAO NGOAI cua chi tiet. Voi phan lon goc (goc LOI), duong ve
    /// da chinh la duong chan that - tinh nhu cu, khong cong gi.
    ///
    /// Nhung tai goc LOM (hem / nep gap vao trong), duong bao ngoai lai di theo MAT TRONG cua
    /// cho gap. Duong chan that (duong nhap vao may chan, dung de tinh phu bi) nam xa hon mot
    /// doan do DOC THEO CANH:
    ///
    ///     offset = T * tan(alpha / 2)          (chinh la OSSB voi R = 0)
    ///
    /// Chan vuong goc (alpha = 90) => tan(45) = 1 => offset = T. Vi vay, dung nhu cach xuong
    /// danh dau bang vong tron:
    ///
    ///     duong chan that = duong ngoai + (so vong tron) * be day
    ///     canh ve 15 co 1 goc lom  ->  15 + 1 * 1.2 = 16.2
    ///     canh ve 34 co 2 goc lom  ->  34 + 2 * 1.2 = 36.4
    ///
    /// LUU Y QUAN TRONG: tu MOT polyline HO khong the suy chac chan vat lieu nam phia nao,
    /// nen khong the tu dong biet goc nao la lom. Tuy nhien cac goc lom luon roi tron ve MOT
    /// PHIA RE. Vi the nguoi dung chon phia nao duoc cong, roi doi chieu ket qua tren cac
    /// duong do de kiem tra bang mat.
    ///
    /// PHAI PHAN BIET VOI FoilSettings.InvertBendDirection:
    ///   - InvertBendDirection chi doi NHAN va MAU hien thi UP/DOWN theo quy uoc xuong.
    ///   - Che do duoi day bam vao DAU GOC RE (su that hinh hoc), KHONG bam vao nhan UP/DOWN.
    /// Nho vay doi quy uoc hien thi se KHONG BAO GIO lam doi kich thuoc phoi.
    /// (Truoc day bam vao nhan UP/DOWN nen doi nhan lam phoi nhay 2.4 mm - da sua.)
    /// </summary>
    public enum FoilThicknessCompensationMode
    {
        /// <summary>Khong cong - coi polyline da la duong chan that o moi goc.</summary>
        None = 0,

        /// <summary>Cong be day tai cac goc RE TRAI (cross > 0) theo chieu duyet da chuan hoa.</summary>
        TurnLeft = 1,

        /// <summary>Cong be day tai cac goc RE PHAI (cross &lt; 0) theo chieu duyet da chuan hoa.</summary>
        TurnRight = 2,

        /// <summary>Cong tai MOI goc - khi polyline ve hoan toan theo mat trong.</summary>
        AllBends = 3
    }

    public enum FoilColorMode
    {
        ByLayer = 0,
        AciIndex = 1
    }

    /// <summary>Cach the hien vung chan tren phoi.</summary>
    public enum FoilBendLineMode
    {
        /// <summary>Mot duong duy nhat tai TAM vung chan (mac dinh - dung cho may chan).</summary>
        SingleCenterLine = 0,

        /// <summary>Hai duong tiep tuyen o 2 bien vung chan.</summary>
        TangentPair = 1
    }

    public class FoilSettings
    {
        // ---------- Thong tin phoi ----------

        /// <summary>Chieu dai phoi (mm) - nguoi dung nhap, giu nguyen khong tinh lai.</summary>
        public double BlankLength { get; set; } = 2500.0;

        /// <summary>Chieu day ton T (mm).</summary>
        public double Thickness { get; set; } = 1.2;

        /// <summary>Ban kinh chan TRONG R (mm).</summary>
        public double InsideRadius { get; set; } = 1.2;

        /// <summary>
        /// Cong be day tai nhung duong chan nao (goc lom). Xem FoilThicknessCompensationMode.
        /// </summary>
        public FoilThicknessCompensationMode ThicknessCompensation { get; set; }
            = FoilThicknessCompensationMode.TurnLeft;

        // ---------- Phuong phap tinh ----------

        public FoilBendMethod Method { get; set; } = FoilBendMethod.CustomShopRule;

        /// <summary>K-factor (vi tri truc trung hoa). Neutral radius = R + K * T.</summary>
        public double KFactor { get; set; } = 0.42;

        /// <summary>He so cho quy tac xuong: tru Factor * T tai moi dau canh co chan.</summary>
        public double CustomFactor { get; set; } = 1.0;

        /// <summary>
        /// Neu bat: quy tac xuong duoc nhan them tan(alpha/2) de ty le theo goc chan.
        /// Mac dinh TAT => giu dung cong thuc hien tai cua xuong: N * T * Factor.
        /// </summary>
        public bool CustomRuleScaleByAngle { get; set; } = false;

        /// <summary>Duong dan file bang chan (TSV). Rong = dung file mac dinh trong AppData.</summary>
        public string BendTablePath { get; set; } = string.Empty;

        // ---------- Hinh hoc / dung sai ----------

        public FoilArcRadiusInterpretation ArcRadiusInterpretation { get; set; }
            = FoilArcRadiusInterpretation.InsideRadius;

        /// <summary>Goc re nho hon nguong nay coi la di thang, khong phai bend (do).</summary>
        public double MinBendAngleDeg { get; set; } = 0.5;

        /// <summary>Goc chan lon hon nguong nay bi tu choi (do). tan(alpha/2) phan ky khi -> 180.</summary>
        public double MaxBendAngleDeg { get; set; } = 179.0;

        /// <summary>Hai dinh cach nhau duoi nguong nay coi la trung nhau (mm).</summary>
        public double DuplicatePointTolerance { get; set; } = 1e-6;

        /// <summary>Cho phep nhan polyline kin trong co ve la duong bao CO BE DAY.</summary>
        public bool AllowThicknessOutlineInput { get; set; } = false;

        /// <summary>Dao toan bo huong chan UP/DOWN neu quy uoc cua xuong nguoc lai.</summary>
        public bool InvertBendDirection { get; set; } = false;

        // ---------- Dau ra ----------

        public string BendLayerName { get; set; } = "_mss.dut";

        /// <summary>Layer cho hinh chu nhat phoi. Rong = dung layer hien hanh.</summary>
        public string OutlineLayerName { get; set; } = string.Empty;

        public FoilColorMode BendUpColorMode { get; set; } = FoilColorMode.ByLayer;

        public int BendUpColorIndex { get; set; } = 7;

        /// <summary>Mac dinh chan XUONG dung mau xam ACI 8 (mau xam chuan cua AutoCAD).</summary>
        public FoilColorMode BendDownColorMode { get; set; } = FoilColorMode.AciIndex;

        public int BendDownColorIndex { get; set; } = 8;

        public FoilBendLineMode BendLineMode { get; set; } = FoilBendLineMode.SingleCenterLine;

        // ---------- Hinh cac buoc chan ----------

        /// <summary>Co ve day hinh trinh tu cac buoc chan ben duoi phoi hay khong.</summary>
        public bool DrawBendSteps { get; set; } = true;

        public FoilBendSequenceOrder BendSequenceOrder { get; set; } = FoilBendSequenceOrder.ProfileOrder;

        public string StepLayerName { get; set; } = "_mss.buocchan";

        /// <summary>Chieu cao chu cho nhan cac buoc. 0 = tu dong theo kich thuoc bien dang.</summary>
        public double StepTextHeight { get; set; } = 0.0;

        /// <summary>Khoang ho giua cac buoc, tinh theo ty le be rong buoc lon nhat.</summary>
        public double StepGapFactor { get; set; } = 0.25;

        /// <summary>Goc xoay phoi trong WCS (do). 0 = chieu dai phoi nam ngang.</summary>
        public double BlankRotationDeg { get; set; } = 0.0;

        /// <summary>So chu so thap phan khi HIEN THI. Tinh toan luon dung double day du.</summary>
        public int Precision { get; set; } = 2;

        public bool ZoomToResult { get; set; } = true;

        public double MinBendAngleRad { get { return MinBendAngleDeg * FoilMath.DegToRad; } }

        public double MaxBendAngleRad { get { return MaxBendAngleDeg * FoilMath.DegToRad; } }

        public string DisplayFormat
        {
            get
            {
                int p = Precision < 0 ? 0 : (Precision > 6 ? 6 : Precision);
                if (p == 0)
                {
                    return "0";
                }

                return "0." + new string('0', p);
            }
        }

        public FoilSettings Clone()
        {
            return new FoilSettings
            {
                BlankLength = this.BlankLength,
                Thickness = this.Thickness,
                InsideRadius = this.InsideRadius,
                ThicknessCompensation = this.ThicknessCompensation,
                Method = this.Method,
                KFactor = this.KFactor,
                CustomFactor = this.CustomFactor,
                CustomRuleScaleByAngle = this.CustomRuleScaleByAngle,
                BendTablePath = this.BendTablePath,
                ArcRadiusInterpretation = this.ArcRadiusInterpretation,
                MinBendAngleDeg = this.MinBendAngleDeg,
                MaxBendAngleDeg = this.MaxBendAngleDeg,
                DuplicatePointTolerance = this.DuplicatePointTolerance,
                AllowThicknessOutlineInput = this.AllowThicknessOutlineInput,
                InvertBendDirection = this.InvertBendDirection,
                BendLayerName = this.BendLayerName,
                OutlineLayerName = this.OutlineLayerName,
                BendUpColorMode = this.BendUpColorMode,
                BendUpColorIndex = this.BendUpColorIndex,
                BendDownColorMode = this.BendDownColorMode,
                BendDownColorIndex = this.BendDownColorIndex,
                BendLineMode = this.BendLineMode,
                DrawBendSteps = this.DrawBendSteps,
                BendSequenceOrder = this.BendSequenceOrder,
                StepLayerName = this.StepLayerName,
                StepTextHeight = this.StepTextHeight,
                StepGapFactor = this.StepGapFactor,
                BlankRotationDeg = this.BlankRotationDeg,
                Precision = this.Precision,
                ZoomToResult = this.ZoomToResult
            };
        }

        public static string DescribeMethod(FoilBendMethod method)
        {
            switch (method)
            {
                case FoilBendMethod.CustomShopRule:
                    return "Custom / Shop Rule";
                case FoilBendMethod.BendDeductionKFactor:
                    return "Bend Deduction + K-Factor";
                case FoilBendMethod.BendAllowanceKFactor:
                    return "Bend Allowance + K-Factor";
                case FoilBendMethod.BendTable:
                    return "Custom Bend Table";
                default:
                    return method.ToString();
            }
        }
    }
}
