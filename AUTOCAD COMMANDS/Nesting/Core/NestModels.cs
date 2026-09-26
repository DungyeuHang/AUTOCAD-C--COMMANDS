using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    // ==========================================================================================
    // GHOPHOI - NESTING CORE: data model (plain C#, no AutoCAD)
    // ==========================================================================================

    /// <summary>Computational part geometry (approximation of the original CAD geometry).</summary>
    public sealed class PartShape
    {
        public PartShape(PolyShape polygon)
            : this(polygon, 0.0)
        {
        }

        /// <param name="toleranceMm">
        /// Maximum distance by which the REAL part boundary can lie outside this polygon
        /// (arc chord sagitta). 0 for parts made only of straight segments (exact polygon).
        /// Added to the part's clearances so approximation never eats into Gap / EdgeMargin.
        /// </param>
        public PartShape(PolyShape polygon, double toleranceMm)
        {
            if (polygon == null) throw new ArgumentNullException("polygon");
            if (double.IsNaN(toleranceMm) || toleranceMm < 0) throw new ArgumentOutOfRangeException("toleranceMm");
            Polygon = polygon;
            ToleranceMm = toleranceMm;
            ToleranceUnits = (long)Math.Ceiling(toleranceMm * NestUnits.PerMm - 1e-9);
        }

        /// <summary>Polygon in the part's local frame (units = 0.001 mm).</summary>
        public PolyShape Polygon { get; private set; }

        public double ToleranceMm { get; private set; }

        /// <summary>Tolerance rounded UP to whole units (conservative).</summary>
        public long ToleranceUnits { get; private set; }

        public double WidthMm { get { return NestUnits.ToMm(Polygon.Bounds.Width); } }

        public double HeightMm { get { return NestUnits.ToMm(Polygon.Bounds.Height); } }

        public double NetAreaMm2 { get { return Polygon.NetArea / (NestUnits.PerMm * NestUnits.PerMm); } }
    }

    /// <summary>One distinct part with a requested quantity.</summary>
    public sealed class PartGroup
    {
        public PartGroup(string id, PartShape shape, int quantity, string material)
            : this(id, shape, quantity, material, null)
        {
        }

        public PartGroup(string id, PartShape shape, int quantity, string material, string order)
        {
            Id = id;
            Shape = shape;
            Quantity = quantity;
            Material = material;
            Name = id;
            Order = string.IsNullOrWhiteSpace(order) ? string.Empty : order.Trim();
        }

        public string Id { get; private set; }

        public string Name { get; set; }

        public PartShape Shape { get; private set; }

        public int Quantity { get; private set; }

        public string Material { get; private set; }

        /// <summary>
        /// Ten don hang. Rong = khong thuoc don nao (chay mot don, nhu truoc day).
        ///
        /// Day THUAN TUY la nhan di kem: no khong dung vao hinh hoc, khong dung vao phep kiem
        /// va cham, va khi xep hang thi chi la khoa PHU - dung sau ca so to lan chieu dai da
        /// dung. Nho vay gom don khong bao gio lam ton them mot mili vat lieu nao.
        /// </summary>
        public string Order { get; private set; }

        /// <summary>Opaque reference back to the source (e.g. the recognition record index).</summary>
        public object SourceReference { get; set; }
    }

    public sealed class PartInstance
    {
        public PartInstance(string id, PartGroup group, int copyIndex)
        {
            Id = id;
            Group = group;
            CopyIndex = copyIndex;
        }

        public string Id { get; private set; }

        public PartGroup Group { get; private set; }

        public string PartGroupId { get { return Group.Id; } }

        /// <summary>Ten don hang, lay theo nhom. Xem <see cref="PartGroup.Order"/>.</summary>
        public string Order { get { return Group.Order; } }

        public int CopyIndex { get; private set; }
    }

    /// <summary>A company-approved sheet size. Length runs along X, Width along Y.</summary>
    public sealed class SheetSpec
    {
        public SheetSpec(string name, double lengthMm, double widthMm)
        {
            Name = name;
            LengthMm = lengthMm;
            WidthMm = widthMm;
            Materials = new List<string>();
        }

        public string Name { get; private set; }

        public double LengthMm { get; private set; }

        public double WidthMm { get; private set; }

        /// <summary>Compatible materials. Empty = any material.</summary>
        public List<string> Materials { get; private set; }

        public long LengthUnits { get { return NestUnits.ToUnits(LengthMm); } }

        public long WidthUnits { get { return NestUnits.ToUnits(WidthMm); } }

        public bool IsCompatibleWith(string material)
        {
            if (Materials.Count == 0) return true;
            foreach (string m in Materials)
            {
                if (string.Equals(m, material, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public string SizeText
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} x {1:0.##}", WidthMm, LengthMm);
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class NestingSettings
    {
        public double GapMm { get; set; } = 5.0;

        public double EdgeMarginMm { get; set; } = 5.0;

        /// <summary>Mirror changes handedness of bent parts - OFF unless the user enables it.</summary>
        public bool AllowMirror { get; set; } = false;

        /// <summary>Allowed rotations in degrees (V1: quarter turns).</summary>
        public List<double> AllowedRotations { get; set; } = new List<double> { 0.0, 90.0, 180.0, 270.0 };

        /// <summary>Wall-clock budget for trying additional orderings. At least one ordering always runs.</summary>
        public double TimeBudgetSeconds { get; set; } = 30.0;

        /// <summary>
        /// Tim DU so luot xep, khong cat bot theo dong ho. MAC DINH BAT.
        ///
        /// Vi sao can: cach cu do theo <see cref="TimeBudgetSeconds"/>, nen tap cac luot chay
        /// duoc phu thuoc may nhanh hay cham va so nhan. Da do tren ban ve that (47 nhom, SL
        /// 85, han muc 30 giay): chay song song duoc 16+16 luot ra 12779 mm, chay tuan tu chi
        /// duoc 7+4 luot ra 13050 mm - te hon 2% chi vi may cham hon.
        ///
        /// Bat len thi khoi luong viec bang dung so luot xep, ma con so do chi phu thuoc cai
        /// dat. May cham chay lau hon nhung ra DUNG cung mot ket qua.
        ///
        /// Tat di thi quay ve dung hanh vi cu: co tran thoi gian, doi lai ket qua co the khac
        /// nhau giua cac may.
        /// </summary>
        public bool DeterministicSearch { get; set; } = true;

        /// <summary>Number of extra seeded (jittered) orderings on top of the deterministic ones.</summary>
        public int ExtraSeededOrderings { get; set; } = 3;

        public int Seed { get; set; } = 1;

        /// <summary>
        /// Allow a part to be placed completely inside a CLOSED hole of another part
        /// (part-in-part). Default OFF. Concave notches / pockets (open to the outside) are
        /// always allowed - they are not holes.
        /// </summary>
        public bool AllowPartInsideHole { get; set; } = false;

        /// <summary>Parallel decoder runs (0 = processor count, 1 = sequential). Does not change the result.</summary>
        public int MaxParallelism { get; set; } = 0;

        public NestingSettings Clone()
        {
            NestingSettings c = (NestingSettings)MemberwiseClone();
            c.AllowedRotations = new List<double>(AllowedRotations);
            return c;
        }
    }

    public sealed class NestingRequest
    {
        public NestingRequest()
        {
            Groups = new List<PartGroup>();
            SheetByMaterial = new Dictionary<string, SheetSpec>(StringComparer.OrdinalIgnoreCase);
            Settings = new NestingSettings();
        }

        public List<PartGroup> Groups { get; private set; }

        /// <summary>Sheet used for each material. Missing materials fall back to <see cref="DefaultSheet"/>.</summary>
        public Dictionary<string, SheetSpec> SheetByMaterial { get; private set; }

        public SheetSpec DefaultSheet { get; set; }

        public NestingSettings Settings { get; set; }

        public SheetSpec ResolveSheet(string material)
        {
            SheetSpec sheet;
            if (material != null && SheetByMaterial.TryGetValue(material, out sheet) && sheet != null) return sheet;
            return DefaultSheet;
        }
    }

    public sealed class Placement
    {
        public string InstanceId { get; set; }

        public string PartGroupId { get; set; }

        /// <summary>Global sheet index in <see cref="NestingResult.Sheets"/>.</summary>
        public int SheetIndex { get; set; }

        public double RotationDeg { get; set; }

        public bool Mirror { get; set; }

        /// <summary>
        /// Ten don hang cua chi tiet nay. Ghi thang vao day - khong bat ben dung phai tra
        /// nguoc ve nhom de biet - de "don hang song sot qua buoc xep dat" la thu kiem duoc.
        /// </summary>
        public string OrderName { get; set; }

        /// <summary>Translation in units (0.001 mm) relative to the sheet's lower-left corner.</summary>
        public long TranslationX { get; set; }

        public long TranslationY { get; set; }

        public double TranslationXMm { get { return NestUnits.ToMm(TranslationX); } }

        public double TranslationYMm { get { return NestUnits.ToMm(TranslationY); } }

        public OrientationTransform Orientation { get { return new OrientationTransform(RotationDeg, Mirror); } }
    }

    public sealed class SheetResult
    {
        public SheetResult(int index, string material, SheetSpec sheet)
        {
            Index = index;
            Material = material;
            Sheet = sheet;
            Placements = new List<Placement>();
            Orders = new List<string>();
        }

        public int Index { get; set; }

        /// <summary>1-based number within its material.</summary>
        public int NumberInMaterial { get; set; }

        public string Material { get; private set; }

        public SheetSpec Sheet { get; private set; }

        public List<Placement> Placements { get; private set; }

        /// <summary>
        /// Ten cac don hang co mat tren to nay, khong trung, theo thu tu bang chu cai.
        ///
        /// Day la DANH SACH DAY DU. Nhan ve tren to co the phai rut gon khi qua dai, nhung
        /// thanh phan day du thi luon co o day va trong ban bao cao - khong bao gio im lang
        /// bo bot ten don nao.
        /// </summary>
        public List<string> Orders { get; private set; }

        /// <summary>
        /// Nhan don hang de ve len to: rut gon khi qua dai. Rong khi khong chay theo don.
        ///
        /// <paramref name="maxNames"/> ten dau duoc viet ra, phan con lai gom thanh
        /// "+ N don khac" - deu dan va khong phu thuoc thu tu xep.
        /// </summary>
        public string OrderLabel(int maxNames)
        {
            if (Orders.Count == 0) return string.Empty;
            if (maxNames < 1) maxNames = 1;
            if (Orders.Count <= maxNames) return string.Join(" + ", Orders.ToArray());

            string[] head = new string[maxNames];
            Orders.CopyTo(0, head, 0, maxNames);
            return string.Join(" + ", head) + " + " + (Orders.Count - maxNames).ToString(CultureInfo.InvariantCulture) + " don khac";
        }

        public double PartAreaMm2 { get; set; }

        /// <summary>Length along X actually consumed (rightmost part edge + edge margin).</summary>
        public double UsedLengthMm { get; set; }

        /// <summary>Part area / used area (used length x sheet width).</summary>
        public double Utilization { get; set; }

        /// <summary>Part area / full sheet area.</summary>
        public double SheetUtilization { get; set; }

        /// <summary>Unused area inside the used length (true scrap estimate).</summary>
        public double WasteAreaMm2 { get; set; }

        /// <summary>Full-width strip after the used length (potentially reusable remnant).</summary>
        public double RemnantLengthMm { get; set; }

        public double RemnantAreaMm2 { get; set; }
    }

    public sealed class UnplacedPart
    {
        public UnplacedPart(string instanceId, string partGroupId, string reason, string order = null)
        {
            InstanceId = instanceId;
            PartGroupId = partGroupId;
            Reason = reason;
            OrderName = string.IsNullOrEmpty(order) ? string.Empty : order;
        }

        public string InstanceId { get; private set; }

        public string PartGroupId { get; private set; }

        public string Reason { get; private set; }

        /// <summary>
        /// Ten don hang cua chi tiet khong xep duoc. Phai giu: nguoi dung can biet NGAY la
        /// don nao con thieu hang, chu khong phai tra nguoc tung ma chi tiet.
        /// </summary>
        public string OrderName { get; private set; }
    }

    public sealed class MaterialStatistics
    {
        public string Material { get; set; }

        public string SheetName { get; set; }

        public int SheetCount { get; set; }

        public int Requested { get; set; }

        public int Placed { get; set; }

        public int Unplaced { get; set; }

        public double UsedLengthMm { get; set; }

        public double Utilization { get; set; }

        public double WasteAreaMm2 { get; set; }

        public double RemnantAreaMm2 { get; set; }

        /// <summary>So luot xep DA CHAY xong.</summary>
        public int OrderingsTried { get; set; }

        /// <summary>
        /// So luot xep DA DINH chay - day la "khoi luong viec" cua lan ghep nay.
        ///
        /// O che do tat dinh thi hai con so nay luon bang nhau; lech nhau tuc la co luot bi
        /// bo (het gio, hoac nguoi dung bam dung).
        /// </summary>
        public int OrderingsPlanned { get; set; }

        /// <summary>Thoi gian THUC TE da chay cho vat lieu nay (giay).</summary>
        public double ElapsedSeconds { get; set; }

        public string BestOrdering { get; set; }

        public bool TimeBudgetHit { get; set; }
    }

    public sealed class NestingStatistics
    {
        public NestingStatistics()
        {
            Materials = new List<MaterialStatistics>();
        }

        public int PartGroupCount { get; set; }

        public int RequestedQuantity { get; set; }

        public int PlacedQuantity { get; set; }

        public int UnplacedQuantity { get; set; }

        public int SheetCount { get; set; }

        public double ElapsedSeconds { get; set; }

        public List<MaterialStatistics> Materials { get; private set; }
    }

    public sealed class NestingResult
    {
        public NestingResult()
        {
            Sheets = new List<SheetResult>();
            Unplaced = new List<UnplacedPart>();
            Statistics = new NestingStatistics();
            Warnings = new List<string>();
        }

        public List<SheetResult> Sheets { get; private set; }

        public IEnumerable<Placement> Placements
        {
            get
            {
                foreach (SheetResult s in Sheets)
                {
                    foreach (Placement p in s.Placements) yield return p;
                }
            }
        }

        public List<UnplacedPart> Unplaced { get; private set; }

        public NestingStatistics Statistics { get; private set; }

        public ValidationResult Validation { get; set; }

        public List<string> Warnings { get; private set; }

        public bool Cancelled { get; set; }
    }

    public enum ValidationIssueKind
    {
        OutsideSheet,
        EdgeMargin,
        Overlap,
        InsufficientGap,
        InvalidTransform,
        QuantityMismatch,
        DuplicatePlacement,
        UnknownPart,
        ExtraPlacement,
        PartInsideHole,
        MaterialMismatch
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationIssueKind kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        public ValidationIssueKind Kind { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return Kind + ": " + Message;
        }
    }

    /// <summary>
    /// Mot lan bao tien do. Co CON SO chu khong chi co chu, de thanh tien trinh chay tu 0 den
    /// 100% - nguoi dung nhin la biet con bao lau, thay vi mot vach chay qua chay lai.
    /// </summary>
    public sealed class NestingProgress
    {
        public NestingProgress(string message, int done, int total)
        {
            Message = message ?? string.Empty;
            Done = done;
            Total = total;
        }

        public string Message { get; private set; }

        /// <summary>So luot ghep da chay xong.</summary>
        public int Done { get; private set; }

        /// <summary>Tong so luot ghep cua ca lenh (moi vat lieu x moi thu tu x moi chinh sach).</summary>
        public int Total { get; private set; }

        public int Percent
        {
            get
            {
                if (Total <= 0) return 0;
                int p = (int)Math.Round(Done * 100.0 / Total);
                return p < 0 ? 0 : (p > 100 ? 100 : p);
            }
        }
    }

    public sealed class ValidationResult
    {
        public ValidationResult()
        {
            Issues = new List<ValidationIssue>();
        }

        public List<ValidationIssue> Issues { get; private set; }

        public bool IsValid { get { return Issues.Count == 0; } }

        /// <summary>Smallest measured part-to-part distance (mm), or NaN when fewer than 2 parts share a sheet.</summary>
        public double MinPartDistanceMm { get; set; } = double.NaN;

        /// <summary>Smallest measured part-to-sheet-edge distance (mm).</summary>
        public double MinEdgeDistanceMm { get; set; } = double.NaN;

        public bool Has(ValidationIssueKind kind)
        {
            foreach (ValidationIssue i in Issues)
            {
                if (i.Kind == kind) return true;
            }

            return false;
        }
    }
}
