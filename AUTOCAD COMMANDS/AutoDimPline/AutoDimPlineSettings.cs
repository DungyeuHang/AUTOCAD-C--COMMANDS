using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - THONG SO
    // File thuan, khong tham chieu AutoCAD (de bo test chay duoc ngoai AutoCAD).
    // ==========================================================================================

    /// <summary>
    /// Cach xu ly doan CUNG (bulge) cua polyline.
    /// Mac dinh Skip: bien dang cong KHONG bao gio bi bien thanh dim thang sai nghia;
    /// so luong bi bo qua luon duoc bao lai cho nguoi dung.
    /// </summary>
    public enum DimPlineArcMode
    {
        Skip,
        Radius
    }

    /// <summary>Cach xu ly doan XIEN (khong ngang, khong doc).</summary>
    public enum DimPlineSkewMode
    {
        Skip,
        Aligned
    }

    /// <summary>
    /// Huong uu tien khi chon phia dat dim. Day la "phuong an reverse" de nguoi dung co
    /// nhieu lua chon bo cuc tu cung mot hinh hoc ma khong phai sua tay tung dim.
    /// </summary>
    public enum DimPlineSideBias
    {
        /// <summary>Thuat toan tu cham diem va quyet dinh.</summary>
        Auto,

        /// <summary>Lay ket qua cua Auto roi LAT NGUOC lai toan bo (duoi &lt;-&gt; tren, trai &lt;-&gt; phai).</summary>
        Flip,

        /// <summary>Ep tat ca dim ngang xuong duoi, dim doc sang trai.</summary>
        BottomLeft,

        /// <summary>Ep tat ca dim ngang len tren, dim doc sang phai.</summary>
        TopRight
    }

    public sealed class AutoDimPlineSettings
    {
        public const string DefaultLayerName = "_mss.kichthuoc";

        /// <summary>
        /// DIMLFAC - he so nhan vao GIA TRI DO HIEN THI trong text cua dim.
        /// KHONG phai DIMSCALE (co chu / mui ten). Hinh hoc 200 voi LinearScale 0.25 -> text "50",
        /// nhung MOI phep tinh vi tri van dung 200 (drawing unit that).
        /// </summary>
        public double LinearScale { get; set; }

        /// <summary>Layer se chua toan bo dim tao ra. Tu tao neu chua co.</summary>
        public string DimensionLayer { get; set; }

        /// <summary>Khoang cach hinh hoc that tu bao hinh polyline toi hang dim dau tien.</summary>
        public double DistanceFromPline { get; set; }

        /// <summary>Khoang cach giua 2 hang dim lien tiep cung mot phia.</summary>
        public double DimensionSpacing { get; set; }

        /// <summary>Doan ngan hon nguong nay khong duoc tao dim.</summary>
        public double MinSegmentLength { get; set; }

        /// <summary>Co tao dim bao tong the theo X / Y hay khong.</summary>
        public bool CreateOverall { get; set; }

        /// <summary>
        /// ON: thuat toan tu chon phia (tren/duoi, trai/phai) cho tung doan.
        /// OFF: dim ngang xuong duoi, dim doc sang trai (van xep hang chong chong len nhau).
        /// </summary>
        public bool AutoLayout { get; set; }

        /// <summary>
        /// ON: feature nam sau ben trong duoc dim NGAY CANH no thay vi keo duong giong that dai
        /// ra bang ngoai - giong cach dim tay. Vi tri cuc bo chi duoc chap nhan khi da kiem tra
        /// duong kich thuoc va duong giong khong cat qua hinh hoc va text khong cham dim khac;
        /// neu khong dat, dim tu dong quay ve bang ngoai.
        /// OFF: moi dim deu nam o bang ngoai bao hinh.
        /// </summary>
        public bool PlaceNearFeature { get; set; }

        public DimPlineSideBias SideBias { get; set; }

        public DimPlineArcMode ArcMode { get; set; }

        public DimPlineSkewMode SkewMode { get; set; }

        /// <summary>
        /// Nguong goc (DO) de coi mot doan la ngang hoac doc. Theo goc chu khong theo toa do
        /// nen khong phu thuoc chieu dai doan. Cung dung cho phep gop doan thang hang.
        /// </summary>
        public double AngleToleranceDegrees { get; set; }

        /// <summary>In ban ke hoach bo tri ra dong lenh (phuc vu debug, khong tao dim).</summary>
        public bool Verbose { get; set; }

        public AutoDimPlineSettings()
        {
            LinearScale = 1.0;
            DimensionLayer = DefaultLayerName;
            DistanceFromPline = 20.0;
            DimensionSpacing = 15.0;
            MinSegmentLength = 1.0;
            CreateOverall = true;
            AutoLayout = true;
            PlaceNearFeature = true;
            SideBias = DimPlineSideBias.Auto;
            ArcMode = DimPlineArcMode.Skip;
            SkewMode = DimPlineSkewMode.Aligned;
            AngleToleranceDegrees = 0.5;
            Verbose = false;
        }

        public AutoDimPlineSettings Clone()
        {
            return new AutoDimPlineSettings
            {
                LinearScale = LinearScale,
                DimensionLayer = DimensionLayer,
                DistanceFromPline = DistanceFromPline,
                DimensionSpacing = DimensionSpacing,
                MinSegmentLength = MinSegmentLength,
                CreateOverall = CreateOverall,
                AutoLayout = AutoLayout,
                PlaceNearFeature = PlaceNearFeature,
                SideBias = SideBias,
                ArcMode = ArcMode,
                SkewMode = SkewMode,
                AngleToleranceDegrees = AngleToleranceDegrees,
                Verbose = Verbose
            };
        }

        /// <summary>
        /// Kiem tra gia tri. Tra ve danh sach loi rong neu hop le.
        /// Khong bao gio nem exception - UI va command deu dua vao day de bao loi ro rang.
        /// </summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();

            if (!IsFinite(LinearScale) || LinearScale <= 0.0)
            {
                errors.Add("Linear scale (DIMLFAC) phai la so duong.");
            }

            if (!IsFinite(DistanceFromPline) || DistanceFromPline < 0.0)
            {
                errors.Add("Distance from PL phai >= 0.");
            }

            if (!IsFinite(DimensionSpacing) || DimensionSpacing <= 0.0)
            {
                errors.Add("Dim spacing phai la so duong.");
            }

            if (!IsFinite(MinSegmentLength) || MinSegmentLength < 0.0)
            {
                errors.Add("Min segment phai >= 0.");
            }

            if (!IsFinite(AngleToleranceDegrees) || AngleToleranceDegrees <= 0.0 || AngleToleranceDegrees >= 45.0)
            {
                errors.Add("Angle tolerance phai trong khoang (0, 45) do.");
            }

            if (string.IsNullOrWhiteSpace(DimensionLayer))
            {
                errors.Add("Dim layer khong duoc de trong.");
            }

            return errors;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "scale={0} layer={1} dist={2} spacing={3} minSeg={4} overall={5} auto={6} bias={7} arc={8} skew={9}",
                LinearScale, DimensionLayer, DistanceFromPline, DimensionSpacing,
                MinSegmentLength, CreateOverall, AutoLayout, SideBias, ArcMode, SkewMode);
        }
    }

    /// <summary>
    /// Cac thong so hinh thuc cua DIMSTYLE hien hanh ma engine bo tri CAN de tinh cho:
    /// co chu quyet dinh be rong text, co mui ten quyet dinh khoang ho toi thieu.
    /// Command doc chung tu ban ve (DIMTXT * DIMSCALE, DIMASZ * DIMSCALE, DIMDEC);
    /// engine khong bao gio tu che ra so cung.
    /// </summary>
    public sealed class DimPlineStyle
    {
        public double TextHeight { get; set; }

        public double ArrowSize { get; set; }

        public int DecimalPlaces { get; set; }

        public DimPlineStyle()
        {
            TextHeight = 2.5;
            ArrowSize = 2.5;
            DecimalPlaces = 2;
        }

        public DimPlineStyle(double textHeight, double arrowSize, int decimalPlaces)
        {
            TextHeight = textHeight > 1e-9 ? textHeight : 2.5;
            ArrowSize = arrowSize > 1e-9 ? arrowSize : 2.5;
            DecimalPlaces = decimalPlaces < 0 ? 0 : (decimalPlaces > 8 ? 8 : decimalPlaces);
        }

        /// <summary>
        /// Uoc luong be rong text dim. Khong can chinh xac tuyet doi - chi can du an toan de
        /// engine khong xep 2 text sat nhau. He so 0.72 la be rong trung binh cua chu so so voi
        /// chieu cao chu trong cac font SHX/TTF thong dung, cong them le hai ben.
        /// </summary>
        public double EstimateTextWidth(string text)
        {
            int length = string.IsNullOrEmpty(text) ? 1 : text.Length;
            return length * TextHeight * 0.72 + TextHeight * 0.5;
        }
    }
}
