using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - KET QUA BUOC PLAN
    // ------------------------------------------------------------------------------------------
    // Mot DimPlinePlacement la mot dim DA DUOC DINH VI XONG nhung CHUA he cham vao database.
    // Toan bo va cham / xep chong duoc giai quyet o day, truoc khi tao bat ky entity nao.
    // ==========================================================================================

    public sealed class DimPlinePlacement
    {
        public DimKind Kind { get; set; }

        public DimOrientation Orientation { get; set; }

        /// <summary>Diem goc duong giong thu nhat (nam tren hinh hoc).</summary>
        public DimPoint DefPoint1 { get; set; }

        /// <summary>Diem goc duong giong thu hai (nam tren hinh hoc).</summary>
        public DimPoint DefPoint2 { get; set; }

        /// <summary>Diem dat duong kich thuoc.</summary>
        public DimPoint DimLinePoint { get; set; }

        /// <summary>Goc quay cua dim thang: 0 cho ngang, PI/2 cho doc.</summary>
        public double Rotation { get; set; }

        /// <summary>Gia tri do bang DRAWING UNIT THAT (chua nhan DIMLFAC).</summary>
        public double MeasuredValue { get; set; }

        /// <summary>Chuoi se hien thi = MeasuredValue * LinearScale. Chi dung de uoc luong be rong text.</summary>
        public string DisplayText { get; set; }

        public DimSide Side { get; set; }

        public int Row { get; set; }

        public bool IsOverall { get; set; }

        /// <summary>Dim nay duoc dat ngay canh feature (cuc bo) thay vi o bang ngoai bao hinh.</summary>
        public bool IsNearFeature { get; set; }

        /// <summary>Chi so doan nguon, -1 neu la dim bao tong the.</summary>
        public int SegmentIndex { get; set; }

        // Rieng cho dim ban kinh.
        public DimPoint ArcCenter { get; set; }
        public DimPoint ChordPoint { get; set; }
        public double LeaderLength { get; set; }

        // Phuc vu debug bo cuc.
        public double ChosenSideScore { get; set; }
        public double OtherSideScore { get; set; }
        public int GeometryCrossings { get; set; }

        public DimTextBox TextBox { get; set; }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} val={2:0.###} side={3} row={4}{5}",
                Kind, Orientation, MeasuredValue, Side, Row, IsOverall ? " OVERALL" : string.Empty);
        }
    }

    public sealed class DimPlinePlan
    {
        public bool IsValid { get; set; }

        public string ErrorMessage { get; set; }

        public DimPlineAnalysis Analysis { get; set; }

        public List<DimPlinePlacement> Placements { get; private set; }

        /// <summary>Khoang cach thuc te da dung (co the lon hon cai hinh neu DIMSTYLE doi hoi).</summary>
        public double EffectiveDistance { get; set; }

        public double EffectiveSpacing { get; set; }

        public int SkippedTooShort { get; set; }

        public int SkippedDuplicate { get; set; }

        public int SkippedArcs { get; set; }

        public int SkippedSkew { get; set; }

        public int DroppedZeroLength { get; set; }

        public List<string> Report { get; private set; }

        public DimPlinePlan()
        {
            Placements = new List<DimPlinePlacement>();
            Report = new List<string>();
            ErrorMessage = string.Empty;
        }

        public int RowCount(DimSide side)
        {
            int max = -1;
            foreach (DimPlinePlacement p in Placements)
            {
                if (p.Side == side && p.Row > max)
                {
                    max = p.Row;
                }
            }

            return max + 1;
        }

        public int CountOnSide(DimSide side)
        {
            int n = 0;
            foreach (DimPlinePlacement p in Placements)
            {
                if (p.Side == side) n++;
            }
            return n;
        }

        /// <summary>
        /// Kiem tra tu dong: co cap text dim nao de len nhau khong.
        /// Dung trong bo test lam tieu chi PASS/FAIL ve bo cuc.
        /// </summary>
        public int CountTextOverlaps(double gap)
        {
            int overlaps = 0;
            for (int i = 0; i < Placements.Count; i++)
            {
                for (int j = i + 1; j < Placements.Count; j++)
                {
                    DimTextBox a = Placements[i].TextBox;
                    DimTextBox b = Placements[j].TextBox;
                    if (a != null && b != null && a.Overlaps(b, gap))
                    {
                        overlaps++;
                    }
                }
            }

            return overlaps;
        }

        /// <summary>
        /// Kiem tra tu dong: co DUONG KICH THUOC nao chay xuyen qua hinh hoc polyline khong.
        /// Day la tieu chi that - dim dat canh feature nam trong bao hinh la binh thuong,
        /// cai khong chap nhan duoc la duong kich thuoc CAT QUA net ve.
        /// </summary>
        public int CountDimLinesCrossingGeometry()
        {
            if (Analysis == null || Analysis.FlatPoints.Count < 2)
            {
                return 0;
            }

            double eps = Math.Max(1e-9, Analysis.Bounds.Diagonal * 1e-9);
            int crossings = 0;

            foreach (DimPlinePlacement p in Placements)
            {
                if (p.Kind != DimKind.Linear)
                {
                    continue;
                }

                bool horizontal = p.Orientation == DimOrientation.Horizontal;
                double fixedCoord = horizontal ? p.DimLinePoint.Y : p.DimLinePoint.X;
                double a = horizontal ? p.DefPoint1.X : p.DefPoint1.Y;
                double b = horizontal ? p.DefPoint2.X : p.DefPoint2.Y;

                crossings += DimMath.CountSegmentCrossings(
                    Analysis.FlatPoints, Analysis.Closed, horizontal, fixedCoord,
                    Math.Min(a, b), Math.Max(a, b), eps);
            }

            return crossings;
        }

        /// <summary>
        /// Kiem tra tu dong: co hai duong kich thuoc cung phuong nao vua trung toa do vua
        /// chong khoang do len nhau khong. Day la loi "dim de len dim" nang nhat.
        /// </summary>
        public int CountOverlappingDimLines(double gap)
        {
            int overlaps = 0;

            for (int i = 0; i < Placements.Count; i++)
            {
                for (int j = i + 1; j < Placements.Count; j++)
                {
                    DimPlinePlacement a = Placements[i];
                    DimPlinePlacement b = Placements[j];

                    if (a.Kind != DimKind.Linear || b.Kind != DimKind.Linear ||
                        a.Orientation != b.Orientation)
                    {
                        continue;
                    }

                    bool horizontal = a.Orientation == DimOrientation.Horizontal;
                    double ca = horizontal ? a.DimLinePoint.Y : a.DimLinePoint.X;
                    double cb = horizontal ? b.DimLinePoint.Y : b.DimLinePoint.X;

                    if (Math.Abs(ca - cb) > gap)
                    {
                        continue;
                    }

                    double aLow = horizontal
                        ? Math.Min(a.DefPoint1.X, a.DefPoint2.X)
                        : Math.Min(a.DefPoint1.Y, a.DefPoint2.Y);
                    double aHigh = horizontal
                        ? Math.Max(a.DefPoint1.X, a.DefPoint2.X)
                        : Math.Max(a.DefPoint1.Y, a.DefPoint2.Y);
                    double bLow = horizontal
                        ? Math.Min(b.DefPoint1.X, b.DefPoint2.X)
                        : Math.Min(b.DefPoint1.Y, b.DefPoint2.Y);
                    double bHigh = horizontal
                        ? Math.Max(b.DefPoint1.X, b.DefPoint2.X)
                        : Math.Max(b.DefPoint1.Y, b.DefPoint2.Y);

                    if (DimMath.IntervalsOverlap(aLow, aHigh, bLow, bHigh))
                    {
                        overlaps++;
                    }
                }
            }

            return overlaps;
        }

        /// <summary>
        /// Kiem tra tu dong: co DUONG GIONG nao cat qua hinh hoc khong.
        /// Duong giong xuat phat tu chinh doan duoc do nen doan do luon duoc bo qua.
        /// Dim o BANG ngoai thi viec duong giong bang qua net ve la binh thuong trong ban ve
        /// ky thuat, nen mac dinh chi kiem cac dim dat CANH FEATURE - nhung dim ma engine da
        /// hua la sach.
        /// </summary>
        public int CountExtensionLinesCrossingGeometry(bool nearFeatureOnly)
        {
            if (Analysis == null || Analysis.FlatPoints.Count < 2)
            {
                return 0;
            }

            double skip = Math.Max(1e-9, Analysis.Bounds.Diagonal * 1e-9);
            int crossings = 0;

            foreach (DimPlinePlacement p in Placements)
            {
                if (p.Kind != DimKind.Linear || (nearFeatureOnly && !p.IsNearFeature))
                {
                    continue;
                }

                bool vertical = p.Orientation == DimOrientation.Horizontal;
                double target = vertical ? p.DimLinePoint.Y : p.DimLinePoint.X;

                crossings += DimMath.CountRayCrossings(
                    Analysis.FlatPoints, Analysis.Closed, p.DefPoint1, vertical, target, skip);
                crossings += DimMath.CountRayCrossings(
                    Analysis.FlatPoints, Analysis.Closed, p.DefPoint2, vertical, target, skip);
            }

            return crossings;
        }

        /// <summary>So dim duoc dat ngay canh feature thay vi day ra bang ngoai.</summary>
        public int NearFeatureCount
        {
            get
            {
                int n = 0;
                foreach (DimPlinePlacement p in Placements)
                {
                    if (p.IsNearFeature) n++;
                }
                return n;
            }
        }
    }
}
