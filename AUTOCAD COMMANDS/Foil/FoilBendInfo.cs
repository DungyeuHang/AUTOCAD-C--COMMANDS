using System;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - QUY UOC THUAT NGU (KHONG DUOC TRON LAN)
    // ------------------------------------------------------------------------------------------
    //  T      Thickness          Chieu day ton.
    //  R      Inside Radius      Ban kinh chan TRONG (mat trong cua goc chan).
    //  alpha  BEND ANGLE         Goc VAT LIEU BI UON QUA. Chan vuong goc => alpha = 90 do.
    //                            Tai mot dinh polyline, alpha = goc GIUA 2 VECTOR HUONG
    //                            (di thang => alpha = 0).
    //  beta   OPENING ANGLE      Goc mo giua 2 canh, do phia trong: beta = 180 - alpha.
    //                            Chan vuong goc => beta = 90 do. (KHAC NGHIA voi alpha du
    //                            voi 90 do hai gia tri trung nhau - day la cai bay pho bien.)
    //  K      K-factor           Vi tri truc trung hoa: R_neutral = R + K * T.
    //  BA     Bend Allowance     Chieu dai TRIEN KHAI cua cung chan, do tren truc trung hoa:
    //                                BA = alpha(rad) * (R + K * T)
    //  OSSB   Outside Setback    Khoang cach tu duong tiep tuyen chan den dinh mold-line,
    //                            do doc theo canh:
    //                                OSSB = (R + T) * tan(alpha / 2)
    //  BD     Bend Deduction     Luong phai TRU khoi tong kich thuoc mold-line:
    //                                BD = 2 * OSSB - BA
    //                            => Flat = L1 + L2 - BD  (L1, L2 do den dinh goc nhon)
    //
    // Nguon cong thuc: quy uoc sheet-metal tieu chuan (Autodesk Inventor / Fusion sheet-metal
    // unfold rules; SolidWorks bend allowance/deduction; Machinery's Handbook - chuong sheet
    // metal bending). Xem them FoilBendStrategies.cs.
    //
    // CANH BAO: OSSB phan ky khi alpha -> 180 do. Vi vay FoilSettings.MaxBendAngleDeg chan
    // gia tri qua lon (mac dinh 179 do).
    // ==========================================================================================

    public enum FoilBendKind
    {
        /// <summary>Bend tai mot dinh goc nhon (2 doan thang gap nhau).</summary>
        SharpVertex = 0,

        /// <summary>Bend da duoc ve san bang mot cung tren polyline (co bulge).</summary>
        Arc = 1
    }

    public enum FoilBendDirection
    {
        Up = 0,
        Down = 1
    }

    /// <summary>
    /// Toan bo thong tin cua mot duong chan. Khong co gia tri nao o day duoc lam tron -
    /// viec lam tron chi xay ra khi hien thi.
    /// </summary>
    public class FoilBendInfo
    {
        /// <summary>So thu tu duong chan (1-based) theo chieu duyet da chuan hoa.</summary>
        public int Index { get; set; }

        public FoilBendKind Kind { get; set; }

        /// <summary>
        /// Diem dac trung cua bend tren BIEN DANG GOC:
        ///  - SharpVertex: dinh mold-line.
        ///  - Arc        : diem giua cung.
        /// </summary>
        public FoilPoint2d Vertex { get; set; }

        /// <summary>Chi so doan truoc bend trong FoilProfile (-1 neu khong co).</summary>
        public int PreviousSegmentIndex { get; set; } = -1;

        /// <summary>Chi so doan sau bend trong FoilProfile (-1 neu khong co).</summary>
        public int NextSegmentIndex { get; set; } = -1;

        /// <summary>Tiep tuyen don vi di vao bend.</summary>
        public FoilVector2d IncomingDirection { get; set; }

        /// <summary>Tiep tuyen don vi di ra khoi bend.</summary>
        public FoilVector2d OutgoingDirection { get; set; }

        /// <summary>alpha - BEND ANGLE (radian). Chan vuong goc = PI/2.</summary>
        public double BendAngleRad { get; set; }

        /// <summary>beta - OPENING ANGLE (radian) = PI - alpha.</summary>
        public double OpeningAngleRad { get { return Math.PI - BendAngleRad; } }

        public double BendAngleDeg { get { return BendAngleRad * FoilMath.RadToDeg; } }

        public double OpeningAngleDeg { get { return OpeningAngleRad * FoilMath.RadToDeg; } }

        /// <summary>+1 = re trai (CCW), -1 = re phai (CW) theo chieu duyet da chuan hoa.</summary>
        public double TurnSign { get; set; }

        public FoilBendDirection Direction { get; set; }

        /// <summary>Phap tuyen cuc bo tai bend tren bien dang (huong ve phia tam cong).</summary>
        public FoilVector2d LocalNormal { get; set; }

        public double InsideRadius { get; set; }

        public double Thickness { get; set; }

        public double KFactor { get; set; }

        /// <summary>BA - chieu dai trien khai cua cung chan.</summary>
        public double BendAllowance { get; set; }

        /// <summary>OSSB ly thuyet cho MOT ben.</summary>
        public double OutsideSetback { get; set; }

        /// <summary>
        /// Luong phai CONG THEM vao moi canh ke tai GOC LOM de di tu duong bao ngoai ra
        /// duong chan that: offset = T * tan(alpha/2). Bang 0 tai goc LOI.
        /// Day chinh la "1 vong tron = 1 be day" ma xuong danh dau tay.
        /// </summary>
        public double MoldLineOffset { get; set; }

        /// <summary>True neu duong chan nay duoc cong be day (goc lom - co "vong tron").</summary>
        public bool IsThicknessCompensated { get { return MoldLineOffset > 1e-12; } }

        /// <summary>BD ly thuyet = 2 * OSSB - BA.</summary>
        public double BendDeduction { get; set; }

        /// <summary>
        /// Co ap setback vao canh TRUOC hay khong.
        /// False khi canh truoc la mot CUNG - cung do da mang chieu dai trien khai chinh xac
        /// cua rieng no nen khong duoc tru them lan nua.
        /// </summary>
        public bool SetbackAppliesPrev { get; set; } = true;

        /// <summary>Co ap setback vao canh SAU hay khong. Xem <see cref="SetbackAppliesPrev"/>.</summary>
        public bool SetbackAppliesNext { get; set; } = true;

        /// <summary>Setback THUC SU tru vao canh truoc (0 neu canh truoc la cung).</summary>
        public double AppliedSetbackPrev { get; set; }

        /// <summary>Setback THUC SU tru vao canh sau (0 neu canh sau la cung).</summary>
        public double AppliedSetbackNext { get; set; }

        /// <summary>Luong thuc su bi tru khoi tong = AppliedSetbackPrev + AppliedSetbackNext - BA.</summary>
        public double EffectiveDeduction
        {
            get { return AppliedSetbackPrev + AppliedSetbackNext - BendAllowance; }
        }

        // ---------- Toa do tren PHOI (he U/V) ----------

        /// <summary>Vi tri V cua duong chan duoc ve (tam vung chan).</summary>
        public double FlatPosition { get; set; }

        /// <summary>Bien dau vung chan theo V.</summary>
        public double FlatZoneStart { get; set; }

        /// <summary>Bien cuoi vung chan theo V.</summary>
        public double FlatZoneEnd { get; set; }

        /// <summary>
        /// Goc nghieng cua DUONG CHAN tren phoi so voi truc U (doc chieu dai phoi).
        /// 0 = duong chan song song canh dai cua phoi (truong hop mat cat ngang thong thuong).
        /// Khac 0 = DUONG CHAN XIEN - duoc xu ly hoan toan bang vector, xem FoilDrawingBuilder.
        /// </summary>
        public double SkewAngleRad { get; set; }

        /// <summary>Vector don vi DOC theo duong chan, trong he (U,V) cua phoi.</summary>
        public FoilVector2d BendLineDirection
        {
            get { return new FoilVector2d(Math.Cos(SkewAngleRad), Math.Sin(SkewAngleRad)); }
        }

        /// <summary>Vector don vi VUONG GOC voi duong chan (chieu trien khai), trong he (U,V).</summary>
        public FoilVector2d BendLineNormal
        {
            get { return BendLineDirection.Perpendicular(); }
        }

        public bool IsDiagonal
        {
            get { return Math.Abs(SkewAngleRad) > 1e-9; }
        }

        /// <summary>Mo ta nguon hinh hoc sinh ra bend nay (phuc vu chan doan).</summary>
        public string SourceGeometry { get; set; } = string.Empty;

        /// <summary>Ghi chu tu chien luoc tinh (vi du: dong bang chan da dung).</summary>
        public string CalculationNote { get; set; } = string.Empty;
    }
}
