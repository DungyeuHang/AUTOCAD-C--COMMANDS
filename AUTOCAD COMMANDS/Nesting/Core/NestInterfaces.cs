using System;
using System.Collections.Generic;
using System.Threading;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    // ==========================================================================================
    // GHOPHOI - NESTING CORE: replaceable components
    // ------------------------------------------------------------------------------------------
    // V1 implementations:  SimpleNestingEngine, PolygonCollisionModel, CandidatePointDecoder,
    //                      MultiOrderOptimizer, LexicographicSolutionEvaluator,
    //                      BasicRotationCandidateProvider, NestingValidator.
    // A future NfpNestingEngine only has to implement INestingEngine (and can reuse the
    // validator, evaluator and models unchanged).
    // ==========================================================================================

    public interface INestingEngine
    {
        /// <param name="progress">
        /// Co the null. LUU Y: duoc goi TU NHIEU LUONG cung luc (cac luot ghep chay song song),
        /// va thu tu goi KHONG dam bao tang dan - luot thu 10 co the bao truoc luot thu 9. Ben
        /// nhan phai tu lo an toan luong, va neu ve thanh tien trinh thi lay gia tri lon nhat.
        /// </param>
        NestingResult Nest(NestingRequest request, CancellationToken cancellation, Action<NestingProgress> progress);
    }

    public interface IRotationCandidateProvider
    {
        /// <summary>Distinct orientations to try for a part (duplicates by symmetry removed).</summary>
        IList<OrientationTransform> GetOrientations(PartGroup group, NestingSettings settings);
    }

    public interface ICollisionModel
    {
        ClearanceRules Rules { get; }

        PreparedShape Prepare(PartGroup group, OrientationTransform orientation);

        bool FitsInsideSheet(PreparedShape shape, long tx, long ty, SheetSpec sheet);

        bool Collides(PreparedShape moving, long tx, long ty, PlacedShape placed);

        PlacedShape Place(PreparedShape shape, long tx, long ty);
    }

    public interface IPlacementDecoder
    {
        DecodedLayout Decode(IList<PartInstance> order, MaterialJob job, PlacementPolicy policy, CancellationToken cancellation);
    }

    public interface ISolutionEvaluator
    {
        /// <summary>Negative when <paramref name="a"/> is better than <paramref name="b"/>.</summary>
        int Compare(DecodedLayout a, DecodedLayout b);
    }

    public interface IOptimizer
    {
        OptimizationOutcome Optimize(MaterialJob job, CancellationToken cancellation, Action<NestingProgress> progress);
    }

    public interface INestingValidator
    {
        ValidationResult Validate(NestingRequest request, NestingResult result);
    }

    /// <summary>How the decoder ranks valid candidate positions.</summary>
    public enum PlacementPolicy
    {
        /// <summary>Leftmost first, then lowest, then smallest top edge.</summary>
        LeftBottom,

        /// <summary>Smallest right edge (sheet length consumed) first, then lowest top edge.</summary>
        MinLength
    }

    /// <summary>
    /// Clearance semantics (both are REAL distances on the actual part geometry, independent):
    ///   EdgeMargin = minimum distance from a part to the sheet edge,
    ///   Gap        = minimum distance between two parts.
    /// Equivalent inflation view: inflate every part by Gap/2 (round joins) -> inflated parts
    /// must not overlap. The sheet boundary is NOT derived from Gap: the raw part must stay
    /// EdgeMargin inside the sheet (so an inflated part may reach EdgeMargin - Gap/2).
    /// A part's approximation tolerance (arc chords) is added on top of both, per part.
    /// </summary>
    public sealed class ClearanceRules
    {
        public ClearanceRules(NestingSettings settings)
        {
            Gap = NestUnits.ToUnits(Math.Max(0.0, settings.GapMm));
            EdgeMargin = NestUnits.ToUnits(Math.Max(0.0, settings.EdgeMarginMm));
            AllowPartInsideHole = settings.AllowPartInsideHole;
        }

        /// <summary>Required part-to-part distance (units).</summary>
        public long Gap { get; private set; }

        /// <summary>Required part-to-sheet-edge distance (units).</summary>
        public long EdgeMargin { get; private set; }

        public bool AllowPartInsideHole { get; private set; }

        /// <summary>
        /// Minimum polygon distance between two parts. At least 1 unit so that "touching" is
        /// never accepted as non-overlapping (Gap = 0 still forbids overlap).
        /// </summary>
        public long PartClearance(long toleranceA, long toleranceB)
        {
            return Math.Max(1L, Gap + toleranceA + toleranceB);
        }

        public long PartClearance(PartShape a, PartShape b)
        {
            return PartClearance(a.ToleranceUnits, b.ToleranceUnits);
        }

        /// <summary>Minimum polygon distance from a part to every sheet edge.</summary>
        public long BoundaryInset(PartShape part)
        {
            return EdgeMargin + part.ToleranceUnits;
        }
    }

    /// <summary>All inputs for one material (parts of different materials are never mixed).</summary>
    public sealed class MaterialJob
    {
        public MaterialJob(string material, SheetSpec sheet, NestingSettings settings, ICollisionModel collision)
        {
            Material = material;
            Sheet = sheet;
            Settings = settings;
            Collision = collision;
            Groups = new List<PartGroup>();
            Instances = new List<PartInstance>();
            Orientations = new Dictionary<string, List<PreparedShape>>();
            UnfitReasons = new Dictionary<string, string>();
        }

        public string Material { get; private set; }

        public SheetSpec Sheet { get; private set; }

        public NestingSettings Settings { get; private set; }

        public ICollisionModel Collision { get; private set; }

        /// <summary>
        /// CA LENH nay co tu hai don hang tro len hay khong.
        ///
        /// Tinh theo toan bo yeu cau chu khong theo rieng vat lieu nay, de moi vat lieu chay
        /// dung cung mot so luot - co thanh tien trinh moi bao dung.
        /// </summary>
        public bool OrderAware { get; set; }

        public List<PartGroup> Groups { get; private set; }

        public List<PartInstance> Instances { get; private set; }

        /// <summary>Orientations that fit an empty sheet, per part group id.</summary>
        public Dictionary<string, List<PreparedShape>> Orientations { get; private set; }

        /// <summary>Reason per group id when no orientation fits an empty sheet.</summary>
        public Dictionary<string, string> UnfitReasons { get; private set; }
    }

    public sealed class PlacedItem
    {
        public PlacedItem(PartInstance instance, PreparedShape shape, long tx, long ty, PlacedShape placed)
        {
            Instance = instance;
            Shape = shape;
            TranslationX = tx;
            TranslationY = ty;
            Placed = placed;
            ComputeAnchors();
        }

        /// <summary>Reflex (concave) vertices of the outer ring - where other parts can tuck in.</summary>
        internal IntPoint[] ReflexVertices { get; private set; }

        /// <summary>
        /// Diem tren duong bao ngoai (toa do to phoi) de sinh ung vien kieu NFP: dat mot dinh
        /// cua chi tiet dang xep vao day, sau khi day ra ngoai mot khe ho, la dat no CHAM vao
        /// chi tiet nay. Moi vi tri long khit vao nhau deu la mot vi tri cham nhau.
        /// </summary>
        internal IntPoint[] ContactPoints { get; private set; }

        /// <summary>Phap tuyen don vi huong ra ngoai tai moi <see cref="ContactPoints"/>.</summary>
        internal double[] ContactNormalX { get; private set; }

        internal double[] ContactNormalY { get; private set; }

        /// <summary>
        /// He so bu tai moi <see cref="ContactPoints"/>: day theo phap tuyen mot doan
        /// khe_ho x he_so thi diem moi cach CA HAI canh ke dung bang khe ho.
        /// </summary>
        internal double[] ContactPush { get; private set; }

        internal LongRect[] HoleBounds { get; private set; }

        private void ComputeAnchors()
        {
            // Tren mot cung tron, hai dinh lom canh nhau cho ra hai neo gan nhu trung nhau -
            // lay day dac chi lam phep kiem va cham tang vot ma khong them lua chon nao. Lay
            // thua ra roi rut gon DEU: neo van trai deu ca long hoc, ma so luong thi co han.
            const int maxAnchors = 4;
            IntPoint[] outer = Placed.Shape.Outer;
            int n = outer.Length;
            bool[] isReflex = new bool[n];
            for (int i = 0; i < n; i++)
            {
                IntPoint prev = outer[(i + n - 1) % n];
                IntPoint next = outer[(i + 1) % n];
                isReflex[i] = GeometryMath.Cross(prev, outer[i], next) < 0;   // outer is CCW
            }

            // Moi dinh LOM deu la mot cho co the nhet hinh khac vao, nen lay HET roi thua
            // thi rut gon deu.
            //
            // Truoc day chi lay HAI DAU cua moi doan lom, voi ly do "buoc nen se truot not vao
            // trong hoc". Voi hoc VUONG thi dung, nhung mot cung LOM tron (luoi liem, long chu
            // C) la MOT doan lom dai - lay hai dau tuc la ca long cung khong sinh ra mot neo
            // nao, trong khi buoc nen chi truot duoc sang trai va xuong duoi nen khong bao gio
            // bo vao duoc. Do la ly do hinh cong bi xep roi rac.
            List<IntPoint> reflex = new List<IntPoint>();
            for (int i = 0; i < n; i++)
            {
                if (isReflex[i]) reflex.Add(outer[i]);
            }

            if (reflex.Count > maxAnchors)
            {
                List<IntPoint> sampled = new List<IntPoint>(maxAnchors);
                for (int k = 0; k < maxAnchors; k++) sampled.Add(reflex[k * reflex.Count / maxAnchors]);
                reflex = sampled;
            }

            ReflexVertices = reflex.ToArray();

            IntPoint[] contact;
            double[] nx, ny, push;
            ContactSampling.Sample(outer, ContactSampling.DefaultSamples, out contact, out nx, out ny, out push);
            ContactPoints = contact;
            ContactNormalX = nx;
            ContactNormalY = ny;
            ContactPush = push;

            HoleBounds = new LongRect[Placed.Shape.Holes.Length];
            for (int k = 0; k < HoleBounds.Length; k++) HoleBounds[k] = LongRect.FromPoints(Placed.Shape.Holes[k]);
        }

        public PartInstance Instance { get; private set; }

        public PreparedShape Shape { get; private set; }

        public long TranslationX { get; private set; }

        public long TranslationY { get; private set; }

        public PlacedShape Placed { get; private set; }
    }

    public sealed class DecodedSheet
    {
        public DecodedSheet()
        {
            Items = new List<PlacedItem>();
        }

        public List<PlacedItem> Items { get; private set; }

        public long MaxX { get; set; }

        public long MaxY { get; set; }
    }

    public sealed class DecodedLayout
    {
        public DecodedLayout()
        {
            Sheets = new List<DecodedSheet>();
            Unplaced = new List<UnplacedPart>();
        }

        public string OrderingName { get; set; }

        public List<DecodedSheet> Sheets { get; private set; }

        public List<UnplacedPart> Unplaced { get; private set; }

        public bool Cancelled { get; set; }
    }

    public sealed class OptimizationOutcome
    {
        public DecodedLayout Best { get; set; }

        /// <summary>So luot xep da chay xong.</summary>
        public int OrderingsTried { get; set; }

        /// <summary>So luot xep DA DINH chay - "khoi luong viec" cua lan ghep nay.</summary>
        public int RunsPlanned { get; set; }

        /// <summary>Thoi gian thuc te da chay (giay).</summary>
        public double ElapsedSeconds { get; set; }

        /// <summary>Co luot bi BO vi het gio. O che do tat dinh thi khong bao gio.</summary>
        public bool TimeBudgetHit { get; set; }

        public bool Cancelled { get; set; }
    }
}
