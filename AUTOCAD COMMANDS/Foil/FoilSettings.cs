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

        /// <summary>
        /// Thu tu chan. Mac dinh AutoFeasible: tu tim thu tu chan duoc tren may, thay vi ap dat
        /// thu tu ve cua bien dang (thu tu ve hau nhu khong bao gio la thu tu chan that).
        /// </summary>
        public FoilBendSequenceOrder BendSequenceOrder { get; set; } = FoilBendSequenceOrder.AutoFeasible;

        public string StepLayerName { get; set; } = "_mss.buocchan";

        /// <summary>Chieu cao chu cho nhan cac buoc. 0 = tu dong theo kich thuoc bien dang.</summary>
        public double StepTextHeight { get; set; } = 0.0;

        /// <summary>Khoang ho giua cac buoc, tinh theo ty le be rong o lon nhat.</summary>
        public double StepGapFactor { get; set; } = 0.25;

        /// <summary>So cot cua luoi hinh buoc chan. 0 = tu dong xep vua be ngang phoi.</summary>
        public int StepColumns { get; set; } = 0;

        /// <summary>Co ve hinh coi va dao trong tung o buoc chan hay khong.</summary>
        public bool DrawTooling { get; set; } = true;

        public string ToolingLayerName { get; set; } = "_mss.dungcu";

        // ---------- May chan (dung cu) ----------

        /// <summary>V - khau do coi (mm). 0 = tu dong = DieOpeningFactor * T.</summary>
        public double DieOpening { get; set; } = 0.0;

        /// <summary>He so chon khau do coi khi DieOpening = 0. Quy tac nghe: 6T..10T.</summary>
        public double DieOpeningFactor { get; set; } = 8.0;

        /// <summary>Goc long coi (do).</summary>
        public double DieIncludedAngleDeg { get; set; } = 88.0;

        /// <summary>Nua be rong than coi (mm). 0 = tu dong theo khau do.</summary>
        public double DieBodyHalfWidth { get; set; } = 0.0;

        /// <summary>
        /// Chieu cao than coi (mm), tu mat coi xuong dam may. 0 = tu dong.
        /// Quyet dinh chi tiet chu U / chu MU co cuoi om duoc len than coi hay khong.
        /// </summary>
        public double DieHeight { get; set; } = 0.0;

        /// <summary>Goc chem cua chay dao (do). Quyet dinh goc chan lon nhat lam duoc.</summary>
        public double PunchIncludedAngleDeg { get; set; } = 85.0;

        /// <summary>Ban kinh mui dao (mm). 0 = lay bang ban kinh chan trong R.</summary>
        public double PunchTipRadius { get; set; } = 0.0;

        /// <summary>Chieu cao lam viec cua dao (mm). 0 = tu dong.</summary>
        public double PunchHeight { get; set; } = 0.0;

        /// <summary>
        /// Nua be day LUOI dao (mm). 0 = tu dong. Dao that chi nhon o mui roi thanh luoi song
        /// song; day la kich thuoc quyet dinh chi tiet co lot vua giua hai ma dao hay khong.
        /// </summary>
        public double PunchBladeHalfWidth { get; set; } = 0.0;

        /// <summary>Chieu cao mo toi da cua may (mm). 0 = khong kiem tra.</summary>
        public double MaxPartHeight { get; set; } = 0.0;

        /// <summary>Chuan ga cu hau ngan nhat con dung duoc (mm). 0 = tu dong = khau do coi.</summary>
        public double MinGaugeLength { get; set; } = 0.0;

        /// <summary>
        /// Cho phep TU DOI SANG DAO KHAC khi dao thang bi vuong (dao co ngong, dao nhon).
        /// Con dao duoc chon se duoc ve dung hinh trong tung buoc.
        /// </summary>
        public bool UseToolLibrary { get; set; } = true;

        /// <summary>Nua be rong DAM DUOI (ban may) (mm). 0 = tu dong.</summary>
        public double BedHalfWidth { get; set; } = 0.0;

        /// <summary>Chieu sau dam duoi (mm). 0 = tu dong.</summary>
        public double BedDepth { get; set; } = 0.0;

        /// <summary>Nua be rong HAM KEP COI (mm). 0 = tu dong = 1.6 lan nua be rong than coi.</summary>
        public double DieHolderHalfWidth { get; set; } = 0.0;

        /// <summary>Chieu cao ham kep coi (mm). 0 = tu dong.</summary>
        public double DieHolderHeight { get; set; } = 0.0;

        /// <summary>Nua be rong DAM TREN (mm). 0 = tu dong.</summary>
        public double RamHalfWidth { get; set; } = 0.0;

        /// <summary>Chieu cao mat chan NGON CU HAU (mm). 0 = tu dong.</summary>
        public double BackGaugeHeight { get; set; } = 0.0;

        /// <summary>Khoang vuon toi da cua cu hau (mm). 0 = tu dong (500).</summary>
        public double BackGaugeTravel { get; set; } = 0.0;

        /// <summary>Do lech than cua dao co ngong (mm). 0 = tu dong.</summary>
        public double GooseneckOffset { get; set; } = 0.0;

        /// <summary>Chieu cao bat dau lech cua dao co ngong (mm). 0 = tu dong.</summary>
        public double GooseneckHeight { get; set; } = 0.0;

        /// <summary>Nua be day luoi dao co ngong (mm). 0 = tu dong.</summary>
        public double GooseneckBladeHalfWidth { get; set; } = 0.0;

        /// <summary>Goc mui cua dao nhon (do). 0 = tu dong (30 do).</summary>
        public double AcuteIncludedAngleDeg { get; set; } = 0.0;



        /// <summary>Gia phat cho moi lan phai LAT TON khi tim thu tu chan.</summary>
        public double FlipPenalty { get; set; } = 25.0;

        /// <summary>
        /// Gia phat cho moi lan phai THAY CHAY DAO. Dat cao vi thay dao la thao tac ton cong
        /// nhat trong ca quy trinh: phai dung may, thao ke, can chinh lai.
        /// </summary>
        public double ToolChangePenalty { get; set; } = 120.0;

        /// <summary>Gia phat cho moi lan phai DOI DAU chi tiet (xoay 180 do trong mat phang).</summary>
        public double TurnPenalty { get; set; } = 12.0;

        /// <summary>
        /// Ghi SO THU TU BUOC CHAN bang bong tron o ngoai mep phai phoi, ngang tung duong chan.
        /// Chi co tac dung khi DrawBendSteps = true.
        /// </summary>
        public bool ShowStepNumbers { get; set; } = true;

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
                StepColumns = this.StepColumns,
                DrawTooling = this.DrawTooling,
                ToolingLayerName = this.ToolingLayerName,
                DieOpening = this.DieOpening,
                DieOpeningFactor = this.DieOpeningFactor,
                DieIncludedAngleDeg = this.DieIncludedAngleDeg,
                DieBodyHalfWidth = this.DieBodyHalfWidth,
                DieHeight = this.DieHeight,
                PunchIncludedAngleDeg = this.PunchIncludedAngleDeg,
                PunchTipRadius = this.PunchTipRadius,
                PunchHeight = this.PunchHeight,
                PunchBladeHalfWidth = this.PunchBladeHalfWidth,
                MaxPartHeight = this.MaxPartHeight,
                MinGaugeLength = this.MinGaugeLength,
                UseToolLibrary = this.UseToolLibrary,
                BedHalfWidth = this.BedHalfWidth,
                BedDepth = this.BedDepth,
                DieHolderHalfWidth = this.DieHolderHalfWidth,
                DieHolderHeight = this.DieHolderHeight,
                RamHalfWidth = this.RamHalfWidth,
                BackGaugeHeight = this.BackGaugeHeight,
                BackGaugeTravel = this.BackGaugeTravel,
                GooseneckOffset = this.GooseneckOffset,
                GooseneckHeight = this.GooseneckHeight,
                GooseneckBladeHalfWidth = this.GooseneckBladeHalfWidth,
                AcuteIncludedAngleDeg = this.AcuteIncludedAngleDeg,
                FlipPenalty = this.FlipPenalty,
                ToolChangePenalty = this.ToolChangePenalty,
                TurnPenalty = this.TurnPenalty,
                ShowStepNumbers = this.ShowStepNumbers,
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
