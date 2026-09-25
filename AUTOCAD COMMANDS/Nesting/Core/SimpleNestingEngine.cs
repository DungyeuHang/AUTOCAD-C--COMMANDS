using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// V1 engine: groups parts by material, expands quantities into instances, runs the
    /// optimizer per material on its own sheets, builds statistics and ALWAYS runs the
    /// independent validator on the final result.
    /// </summary>
    public sealed class SimpleNestingEngine : INestingEngine
    {
        private readonly IRotationCandidateProvider _rotations;
        private readonly Func<NestingSettings, ICollisionModel> _collisionFactory;
        private readonly IOptimizer _optimizer;
        private readonly INestingValidator _validator;

        public SimpleNestingEngine()
            : this(
                new BasicRotationCandidateProvider(),
                s => new PolygonCollisionModel(s),
                new MultiOrderOptimizer(new CandidatePointDecoder(), new LexicographicSolutionEvaluator()),
                new NestingValidator())
        {
        }

        public SimpleNestingEngine(
            IRotationCandidateProvider rotations,
            Func<NestingSettings, ICollisionModel> collisionFactory,
            IOptimizer optimizer,
            INestingValidator validator)
        {
            _rotations = rotations;
            _collisionFactory = collisionFactory;
            _optimizer = optimizer;
            _validator = validator;
        }

        public static string NormalizeMaterial(string material)
        {
            return (material ?? string.Empty).Trim().ToUpperInvariant();
        }

        public NestingResult Nest(NestingRequest request, CancellationToken cancellation, Action<NestingProgress> progress)
        {
            if (request == null) throw new ArgumentNullException("request");

            Stopwatch watch = Stopwatch.StartNew();
            NestingResult result = new NestingResult();
            NestingSettings settings = request.Settings ?? new NestingSettings();
            ICollisionModel collision = _collisionFactory(settings);
            CultureInfo ci = CultureInfo.InvariantCulture;

            // ---- group by material (never mix materials) ----
            SortedDictionary<string, List<PartGroup>> byMaterial =
                new SortedDictionary<string, List<PartGroup>>(StringComparer.Ordinal);
            foreach (PartGroup g in request.Groups)
            {
                string key = NormalizeMaterial(g.Material);
                List<PartGroup> list;
                if (!byMaterial.TryGetValue(key, out list))
                {
                    list = new List<PartGroup>();
                    byMaterial[key] = list;
                }

                list.Add(g);
            }

            result.Statistics.PartGroupCount = request.Groups.Count;

            // Tong so luot ghep cua CA LENH, biet truoc de ve duoc thanh tien trinh that:
            // moi vat lieu chay cung mot so luot (moi thu tu xep x hai chinh sach dat).
            int runsPerMaterial = MultiOrderOptimizer.RunCount(settings);
            int totalRuns = Math.Max(1, byMaterial.Count * runsPerMaterial);
            int materialIndex = 0;

            foreach (KeyValuePair<string, List<PartGroup>> entry in byMaterial)
            {
                string material = entry.Key;
                SheetSpec sheet = request.ResolveSheet(material);
                MaterialStatistics ms = new MaterialStatistics { Material = material };
                result.Statistics.Materials.Add(ms);

                if (sheet == null)
                {
                    foreach (PartGroup g in entry.Value)
                    {
                        ms.Requested += Math.Max(0, g.Quantity);
                        for (int k = 0; k < g.Quantity; k++)
                        {
                            result.Unplaced.Add(new UnplacedPart(InstanceId(g, k), g.Id,
                                "Khong co kho phoi nao duoc chon cho vat lieu " + material + "."));
                        }
                    }

                    ms.Unplaced = ms.Requested;
                    continue;
                }

                ms.SheetName = sheet.Name;
                if (!sheet.IsCompatibleWith(material))
                {
                    result.Warnings.Add(string.Format(ci,
                        "Kho phoi '{0}' khong khai bao tuong thich voi vat lieu {1}.", sheet.Name, material));
                }

                // The time budget is for the whole request: split what is left over the
                // remaining materials (every material still gets at least one full ordering).
                int materialsLeft = byMaterial.Count - result.Statistics.Materials.Count + 1;
                NestingSettings jobSettings = settings.Clone();
                jobSettings.TimeBudgetSeconds = Math.Max(0.0, settings.TimeBudgetSeconds - watch.Elapsed.TotalSeconds) / materialsLeft;

                MaterialJob job = new MaterialJob(material, sheet, jobSettings, collision);
                foreach (PartGroup g in entry.Value)
                {
                    job.Groups.Add(g);
                    ms.Requested += Math.Max(0, g.Quantity);
                    for (int k = 0; k < g.Quantity; k++)
                    {
                        job.Instances.Add(new PartInstance(InstanceId(g, k), g, k));
                    }

                    PrepareOrientations(job, g, sheet, collision);
                }

                // Bo toi uu chi biet luot chay cua RIENG vat lieu nay, nen cong them phan cac
                // vat lieu truoc do de con so bao ra la tien do cua ca lenh.
                int done = materialIndex * runsPerMaterial;
                Action<NestingProgress> relay = progress == null
                    ? (Action<NestingProgress>)null
                    : p => progress(new NestingProgress(p.Message, done + p.Done, totalRuns));

                OptimizationOutcome outcome = _optimizer.Optimize(job, cancellation, relay);
                materialIndex++;

                // Vat lieu nay xong: day thanh tien trinh len dung moc, ke ca khi co luot bi
                // bo qua vi het gio hay nguoi dung bam Dung - neu khong thanh se dung lung chung.
                if (progress != null)
                {
                    progress(new NestingProgress(material + ": xong", materialIndex * runsPerMaterial, totalRuns));
                }

                ms.OrderingsTried = outcome.OrderingsTried;
                ms.TimeBudgetHit = outcome.TimeBudgetHit;
                if (outcome.Cancelled) result.Cancelled = true;

                DecodedLayout best = outcome.Best ?? new DecodedLayout();
                ms.BestOrdering = best.OrderingName;
                AppendMaterialResult(result, ms, job, best, settings);
            }

            foreach (MaterialStatistics ms in result.Statistics.Materials)
            {
                result.Statistics.RequestedQuantity += ms.Requested;
                result.Statistics.PlacedQuantity += ms.Placed;
                result.Statistics.UnplacedQuantity += ms.Unplaced;
                result.Statistics.SheetCount += ms.SheetCount;
            }

            result.Validation = _validator.Validate(request, result);
            result.Statistics.ElapsedSeconds = watch.Elapsed.TotalSeconds;
            return result;
        }

        private static string InstanceId(PartGroup g, int copy)
        {
            return g.Id + "#" + (copy + 1).ToString(CultureInfo.InvariantCulture);
        }

        private void PrepareOrientations(MaterialJob job, PartGroup g, SheetSpec sheet, ICollisionModel collision)
        {
            List<PreparedShape> fitting = new List<PreparedShape>();
            long inset = collision.Rules.BoundaryInset(g.Shape);
            long usableL = sheet.LengthUnits - 2 * inset;
            long usableW = sheet.WidthUnits - 2 * inset;

            if (g.Shape.Polygon.Outer.Length < 3 || g.Shape.Polygon.NetArea <= 0)
            {
                job.Orientations[g.Id] = fitting;
                job.UnfitReasons[g.Id] = "Hinh hoc chi tiet khong hop le (it hon 3 dinh hoac dien tich = 0).";
                return;
            }

            foreach (OrientationTransform o in _rotations.GetOrientations(g, job.Settings))
            {
                PreparedShape shape = collision.Prepare(g, o);
                if (shape.Bounds.Width <= usableL && shape.Bounds.Height <= usableW)
                {
                    fitting.Add(shape);
                }
            }

            job.Orientations[g.Id] = fitting;
            if (fitting.Count == 0)
            {
                job.UnfitReasons[g.Id] = string.Format(CultureInfo.InvariantCulture,
                    "Chi tiet {0:0.##} x {1:0.##} mm khong vua vung dat duoc {2:0.##} x {3:0.##} mm cua kho {4} " +
                    "o bat ky huong xoay cho phep nao (da tru le mep {5:0.##} mm moi phia{6}).",
                    g.Shape.WidthMm, g.Shape.HeightMm,
                    NestUnits.ToMm(Math.Max(0, usableL)), NestUnits.ToMm(Math.Max(0, usableW)),
                    sheet.Name, job.Settings.EdgeMarginMm,
                    g.Shape.ToleranceMm > 0 ? string.Format(CultureInfo.InvariantCulture, " + dung sai cung {0:0.###} mm", g.Shape.ToleranceMm) : string.Empty);
            }
        }

        private static void AppendMaterialResult(
            NestingResult result, MaterialStatistics ms, MaterialJob job, DecodedLayout layout, NestingSettings settings)
        {
            SheetSpec spec = job.Sheet;
            double sheetW = spec.WidthMm, sheetL = spec.LengthMm;
            double totalPartArea = 0, totalUsedArea = 0;

            int number = 0;
            foreach (DecodedSheet ds in layout.Sheets)
            {
                if (ds.Items.Count == 0) continue;

                SheetResult sr = new SheetResult(result.Sheets.Count, job.Material, spec);
                sr.NumberInMaterial = ++number;

                foreach (PlacedItem item in ds.Items)
                {
                    sr.Placements.Add(new Placement
                    {
                        InstanceId = item.Instance.Id,
                        PartGroupId = item.Instance.PartGroupId,
                        SheetIndex = sr.Index,
                        RotationDeg = item.Shape.Orientation.RotationDeg,
                        Mirror = item.Shape.Orientation.Mirror,
                        TranslationX = item.TranslationX,
                        TranslationY = item.TranslationY
                    });
                    sr.PartAreaMm2 += item.Instance.Group.Shape.NetAreaMm2;
                }

                sr.UsedLengthMm = Math.Min(sheetL, NestUnits.ToMm(ds.MaxX) + settings.EdgeMarginMm);
                double usedArea = sr.UsedLengthMm * sheetW;
                sr.Utilization = usedArea > 0 ? sr.PartAreaMm2 / usedArea : 0;
                sr.SheetUtilization = sheetL * sheetW > 0 ? sr.PartAreaMm2 / (sheetL * sheetW) : 0;
                sr.WasteAreaMm2 = Math.Max(0, usedArea - sr.PartAreaMm2);
                sr.RemnantLengthMm = Math.Max(0, sheetL - sr.UsedLengthMm);
                sr.RemnantAreaMm2 = sr.RemnantLengthMm * sheetW;

                result.Sheets.Add(sr);
                ms.SheetCount++;
                ms.Placed += sr.Placements.Count;
                ms.UsedLengthMm += sr.UsedLengthMm;
                ms.WasteAreaMm2 += sr.WasteAreaMm2;
                ms.RemnantAreaMm2 += sr.RemnantAreaMm2;
                totalPartArea += sr.PartAreaMm2;
                totalUsedArea += usedArea;
            }

            ms.Utilization = totalUsedArea > 0 ? totalPartArea / totalUsedArea : 0;
            result.Unplaced.AddRange(layout.Unplaced);
            ms.Unplaced = layout.Unplaced.Count;
        }
    }
}
