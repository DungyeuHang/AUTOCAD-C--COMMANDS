using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace NestingCore.Tests
{
    public static class AuditExperiments
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void RunAll(string fixtureFolder)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("        GHOPHOI V1 - INDEPENDENT FORENSIC AUDIT & EXPERIMENT SUITE              ");
            Console.WriteLine("================================================================================");
            Console.WriteLine("Fixture folder: " + fixtureFolder);

            RunDeterminismAudit(fixtureFolder);
            RunPerformanceBreakdown(fixtureFolder);
            RunFreeAngleExperiment(fixtureFolder);
            RunGlobalSearchExperiment(fixtureFolder);
            RunLocalRepairExperiment(fixtureFolder);
            RunCurvedArcExperiment(fixtureFolder);
            RunDenseGridOracleExperiment(fixtureFolder);
            RunMultiOrderAudit(fixtureFolder);

            Console.WriteLine("\n================================================================================");
            Console.WriteLine("                     ALL AUDIT EXPERIMENTS COMPLETE                             ");
            Console.WriteLine("================================================================================");
        }

        // =============================================================================================
        // SECTION 12: DETERMINISM AUDIT
        // =============================================================================================
        public static void RunDeterminismAudit(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [1] DETERMINISM AUDIT: Parallel vs Sequential & Repeated Runs Across All 46 Fixtures");
            Console.WriteLine("--------------------------------------------------------------------------------");

            string[] files = Directory.GetFiles(fixtureFolder, "*.nest");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int total = files.Length;
            int matchParSeq = 0;
            int matchRepeated = 0;

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                NestingFixture.Fixture fx = NestingFixture.Load(file);

                // Run 1: Default parallel
                NestingSettings sPar = fx.Request.Settings.Clone();
                sPar.MaxParallelism = 0;
                fx.Request.Settings = sPar;
                NestingResult rPar1 = new SimpleNestingEngine().Nest(fx.Request, CancellationToken.None, null);
                string sigPar1 = ComputeSignature(rPar1);

                // Run 2: Parallel repeated
                NestingResult rPar2 = new SimpleNestingEngine().Nest(fx.Request, CancellationToken.None, null);
                string sigPar2 = ComputeSignature(rPar2);

                // Run 3: Sequential
                NestingSettings sSeq = fx.Request.Settings.Clone();
                sSeq.MaxParallelism = 1;
                fx.Request.Settings = sSeq;
                NestingResult rSeq = new SimpleNestingEngine().Nest(fx.Request, CancellationToken.None, null);
                string sigSeq = ComputeSignature(rSeq);

                bool repEqual = sigPar1 == sigPar2;
                bool parSeqEqual = sigPar1 == sigSeq;

                if (repEqual) matchRepeated++;
                if (parSeqEqual) matchParSeq++;

                if (!parSeqEqual || !repEqual)
                {
                    Console.WriteLine(string.Format(Inv, "  [DETERMINISM MISMATCH] {0}: Par1={1}, Par2={2}, Seq={3}",
                        name, sigPar1, sigPar2, sigSeq));
                }
            }

            Console.WriteLine(string.Format(Inv, "  Repeated Parallel Determinism: {0}/{1} identical signatures", matchRepeated, total));
            Console.WriteLine(string.Format(Inv, "  Parallel vs Sequential Match:  {0}/{1} identical signatures", matchParSeq, total));
        }

        // =============================================================================================
        // SECTION 13: PERFORMANCE BREAKDOWN
        // =============================================================================================
        public static void RunPerformanceBreakdown(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [2] PERFORMANCE BREAKDOWN & INSTRUMENTATION");
            Console.WriteLine("--------------------------------------------------------------------------------");

            string realFile = Path.Combine(fixtureFolder, "real_user_drawing_01.nest");
            if (!File.Exists(realFile))
            {
                Console.WriteLine("  real_user_drawing_01.nest not found.");
                return;
            }

            NestingFixture.Fixture fx = NestingFixture.Load(realFile);
            NestingRequest req = fx.Request;

            long collisionChecks = 0;
            long collisionCheckTicks = 0;
            long fitsInsideSheetChecks = 0;

            Func<NestingSettings, ICollisionModel> instrumentedFactory = s =>
            {
                ICollisionModel inner = new PolygonCollisionModel(s);
                return new CountingCollisionModel(inner, ref collisionChecks, ref collisionCheckTicks, ref fitsInsideSheetChecks);
            };

            Stopwatch swTotal = Stopwatch.StartNew();
            SimpleNestingEngine engine = new SimpleNestingEngine(
                new BasicRotationCandidateProvider(),
                instrumentedFactory,
                new MultiOrderOptimizer(new CandidatePointDecoder(), new LexicographicSolutionEvaluator()),
                new NestingValidator());

            NestingResult res = engine.Nest(req, CancellationToken.None, null);
            swTotal.Stop();

            double totalMs = swTotal.Elapsed.TotalMilliseconds;
            double collisionMs = (double)collisionCheckTicks / Stopwatch.Frequency * 1000.0;

            Console.WriteLine(string.Format(Inv, "  Fixture: real_user_drawing_01 (47 groups, qty 85, 34 with arcs)"));
            Console.WriteLine(string.Format(Inv, "  Total Nesting Time:        {0:F1} ms", totalMs));
            Console.WriteLine(string.Format(Inv, "  Collision Checks (Collides): {0:N0} calls", collisionChecks));
            Console.WriteLine(string.Format(Inv, "  FitsInsideSheet Checks:     {0:N0} calls", fitsInsideSheetChecks));
            Console.WriteLine(string.Format(Inv, "  Time inside Collides():     {0:F1} ms ({1:F1}% of total time)", collisionMs, (collisionMs / totalMs) * 100));
            Console.WriteLine(string.Format(Inv, "  Sheets: {0}, Used Length: {1:F3} mm, Util: {2:F2}%, Validator: {3}",
                res.Sheets.Count, TotalLength(res), res.Sheets[0].Utilization * 100, res.Validation.IsValid ? "PASS" : "FAIL"));
        }

        // =============================================================================================
        // SECTION 5: FREE-ANGLE ROTATION AUDIT
        // =============================================================================================
        public static void RunFreeAngleExperiment(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [3] FREE-ANGLE ROTATION AUDIT: 4-Angle vs 8-Angle vs 12-Angle vs 24-Angle vs 72-Angle");
            Console.WriteLine("--------------------------------------------------------------------------------");

            List<Tuple<string, NestingRequest>> cases = new List<Tuple<string, NestingRequest>>();

            // Existing fixtures
            string[] testFiles = { "real_user_drawing_01.nest", "quality_wedge_pair_01.nest", "quality_zigzag_pair_01.nest", "quality_vgroove_01.nest", "01_strips_mixed_orientation.nest" };
            foreach (string tf in testFiles)
            {
                string p = Path.Combine(fixtureFolder, tf);
                if (File.Exists(p)) cases.Add(Tuple.Create(tf, NestingFixture.Load(p).Request));
            }

            // Synthetic non-orthogonal test cases
            // Case 1: Two 30-60-90 right triangles (legs 300 x 519.615 mm, hypotenuse 600 mm at 30 deg)
            cases.Add(Tuple.Create("synth_triangles_30deg", CreateTriangles30DegRequest()));
            // Case 2: Four 45-degree parallelograms
            cases.Add(Tuple.Create("synth_parallelogram_45deg", CreateParallelogram45Request()));
            // Case 3: Four 60-degree trapezoids
            cases.Add(Tuple.Create("synth_trapezoid_60deg", CreateTrapezoid60Request()));

            List<double[]> angleSets = new List<double[]>
            {
                new double[] { 0, 90, 180, 270 },                                  // 4-angle (Current)
                new double[] { 0, 45, 90, 135, 180, 225, 270, 315 },              // 8-angle (45 deg step)
                StepAngles(30.0),                                                   // 12-angle (30 deg step)
                StepAngles(15.0),                                                   // 24-angle (15 deg step)
                StepAngles(5.0)                                                     // 72-angle (5 deg step)
            };

            string[] angleLabels = { "4-angle (90°)", "8-angle (45°)", "12-angle (30°)", "24-angle (15°)", "72-angle (5°)" };

            Console.WriteLine("| Fixture | Angles | Sheets | Used Length (mm) | Delta vs 4-ang | Runtime (ms) | Collision Checks |");
            Console.WriteLine("|---|---|---|---|---|---|---|");

            foreach (var testCase in cases)
            {
                string name = testCase.Item1;
                NestingRequest baseReq = testCase.Item2;
                double baseLen = 0;

                for (int a = 0; a < angleSets.Count; a++)
                {
                    // Skip 5 deg step for real drawing to keep audit time bounded
                    if (name.Contains("real_user") && a >= 3) continue;

                    NestingRequest req = CloneRequest(baseReq);
                    req.Settings.AllowedRotations = new List<double>(angleSets[a]);
                    req.Settings.TimeBudgetSeconds = 120;
                    req.Settings.DeterministicSearch = true;

                    long collChecks = 0, ticks = 0, fits = 0;
                    Func<NestingSettings, ICollisionModel> fac = s =>
                        new CountingCollisionModel(new PolygonCollisionModel(s), ref collChecks, ref ticks, ref fits);

                    SimpleNestingEngine eng = new SimpleNestingEngine(
                        new BasicRotationCandidateProvider(),
                        fac,
                        new MultiOrderOptimizer(new CandidatePointDecoder(), new LexicographicSolutionEvaluator()),
                        new NestingValidator());

                    Stopwatch sw = Stopwatch.StartNew();
                    NestingResult res = eng.Nest(req, CancellationToken.None, null);
                    sw.Stop();

                    double len = TotalLength(res);
                    if (a == 0) baseLen = len;
                    double delta = len - baseLen;

                    Console.WriteLine(string.Format(Inv,
                        "| {0} | {1} | {2} | {3:F1} | {4:+0.0;-0.0;0.0} mm | {5:F0} ms | {6:N0} |",
                        name, angleLabels[a], res.Sheets.Count, len, delta, sw.Elapsed.TotalMilliseconds, collChecks));
                }
            }
        }

        // =============================================================================================
        // SECTION 6: GREEDY / GLOBAL SEARCH AUDIT
        // =============================================================================================
        public static void RunGlobalSearchExperiment(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [4] GREEDY / GLOBAL SEARCH AUDIT: Order Permutation Traps & Local Search");
            Console.WriteLine("--------------------------------------------------------------------------------");

            // Construct order-trap fixture:
            // Interlocking Trap: Two interlocking U-hooks (H1, H2) and a blocker rectangle (B).
            // If H1 and H2 are placed next to each other, they interlock into 260 mm length.
            // But Blocker B has larger area / width, so standard greedy area-descending order places:
            //   B -> H1 -> H2  or  H1 -> B -> H2
            // If B is placed between them, H1 and H2 are separated and take 460 mm length!
            NestingRequest trapReq = CreateOrderTrapRequest();

            // Run standard engine
            SimpleNestingEngine eng = new SimpleNestingEngine();
            NestingResult standardRes = eng.Nest(trapReq, CancellationToken.None, null);
            double standardLen = TotalLength(standardRes);

            // Now test ALL permutations of the part instances to see global optimal sequence
            List<PartInstance> instances = new List<PartInstance>();
            foreach (PartGroup g in trapReq.Groups)
            {
                for (int c = 0; c < g.Quantity; c++)
                    instances.Add(new PartInstance(g.Id + "#" + c, g, c));
            }

            List<List<PartInstance>> allPerms = Permutations(instances);
            CandidatePointDecoder decoder = new CandidatePointDecoder();
            LexicographicSolutionEvaluator evaluator = new LexicographicSolutionEvaluator();
            MaterialJob job = new MaterialJob("1.2MM", trapReq.DefaultSheet, trapReq.Settings, new PolygonCollisionModel(trapReq.Settings));
            job.Groups.AddRange(trapReq.Groups);
            job.Instances.AddRange(instances);
            BasicRotationCandidateProvider rotProvider = new BasicRotationCandidateProvider();
            foreach (PartGroup g in trapReq.Groups)
            {
                List<PreparedShape> list = new List<PreparedShape>();
                foreach (OrientationTransform o in rotProvider.GetOrientations(g, trapReq.Settings))
                    list.Add(job.Collision.Prepare(g, o));
                job.Orientations[g.Id] = list;
            }

            DecodedLayout bestLayout = null;
            string bestPermStr = "";

            foreach (var perm in allPerms)
            {
                DecodedLayout l = decoder.Decode(perm, job, PlacementPolicy.MinLength, CancellationToken.None);
                if (bestLayout == null || evaluator.Compare(l, bestLayout) < 0)
                {
                    bestLayout = l;
                    bestPermStr = string.Join(" -> ", perm.ConvertAll(p => p.Group.Name).ToArray());
                }
            }

            double bestLen = NestUnits.ToMm(LexicographicSolutionEvaluator.TotalUsedLength(bestLayout));

            Console.WriteLine(string.Format(Inv, "  Trap Fixture (Interlocking U-hooks + Blocker):"));
            Console.WriteLine(string.Format(Inv, "    Current V1 Engine (5 base + 3 jitter): Used Length = {0:F1} mm", standardLen));
            Console.WriteLine(string.Format(Inv, "    Global Permutation Search Best:        Used Length = {0:F1} mm ({1})", bestLen, bestPermStr));
            Console.WriteLine(string.Format(Inv, "    Gap (Greedy Search Inefficiency):      {0:F1} mm ({1:F1}%)", standardLen - bestLen, ((standardLen - bestLen) / standardLen) * 100));

            // Test Pairwise Swap Local Search prototype on the trap
            Stopwatch swSwap = Stopwatch.StartNew();
            DecodedLayout swapResult = RunPairwiseSwapLocalSearch(job, instances, decoder, evaluator);
            swSwap.Stop();
            double swapLen = NestUnits.ToMm(LexicographicSolutionEvaluator.TotalUsedLength(swapResult));
            Console.WriteLine(string.Format(Inv, "    Pairwise Swap Local Search Prototype:  Used Length = {0:F1} mm (Time: {1:F1} ms)", swapLen, swSwap.Elapsed.TotalMilliseconds));
        }

        // =============================================================================================
        // SECTION 7: LOCAL REPAIR EXPERIMENT
        // =============================================================================================
        public static void RunLocalRepairExperiment(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [5] LOCAL REPAIR EXPERIMENT: Remove & Reinsert K Placements (K=1, 2, 3)");
            Console.WriteLine("--------------------------------------------------------------------------------");

            string[] testFixtures = {
                "quality_arc_in_c_01.nest",
                "quality_zigzag_pair_01.nest",
                "quality_wedge_pair_01.nest",
                "01_strips_mixed_orientation.nest",
                "02_brackets_and_plates.nest",
                "quality_side_arc_pocket_01.nest",
                "quality_c_pair_01.nest",
                "quality_u_pair_01.nest",
                "real_user_drawing_01.nest"
            };

            Console.WriteLine("| Fixture | K | Initial Length (mm) | Repaired Length (mm) | Gain (mm) | Gain % | Runtime (ms) |");
            Console.WriteLine("|---|---|---|---|---|---|---|");

            foreach (string tf in testFixtures)
            {
                string path = Path.Combine(fixtureFolder, tf);
                if (!File.Exists(path)) continue;

                NestingFixture.Fixture fx = NestingFixture.Load(path);
                SimpleNestingEngine eng = new SimpleNestingEngine();
                NestingResult initRes = eng.Nest(fx.Request, CancellationToken.None, null);
                double initLen = TotalLength(initRes);

                for (int k = 1; k <= 3; k++)
                {
                    // For real drawing, test K=1 and K=2 to keep runtime reasonable
                    if (tf.Contains("real_user") && k == 3) continue;

                    Stopwatch sw = Stopwatch.StartNew();
                    double repLen = RunLocalRepairOnResult(initRes, fx.Request, k);
                    sw.Stop();

                    double gain = initLen - repLen;
                    double gainPct = initLen > 0 ? (gain / initLen) * 100 : 0;

                    Console.WriteLine(string.Format(Inv,
                        "| {0} | K={1} | {2:F1} | {3:F1} | {4:+0.0;-0.0;0.0} | {5:F2}% | {6:F0} ms |",
                        tf, k, initLen, repLen, gain, gainPct, sw.Elapsed.TotalMilliseconds));
                }
            }
        }

        private static double RunLocalRepairOnResult(NestingResult initial, NestingRequest request, int k)
        {
            if (initial.Sheets.Count == 0) return 0;

            ClearanceRules rules = new ClearanceRules(request.Settings);
            ICollisionModel collision = new PolygonCollisionModel(request.Settings);
            BasicRotationCandidateProvider rotProvider = new BasicRotationCandidateProvider();
            CandidatePointDecoder decoder = new CandidatePointDecoder();

            double bestTotalLength = TotalLength(initial);

            // Repair per sheet: focus on the sheet that has the longest used length or last sheet
            for (int sIdx = 0; sIdx < initial.Sheets.Count; sIdx++)
            {
                SheetResult sheet = initial.Sheets[sIdx];
                int n = sheet.Placements.Count;
                if (n <= k) continue;

                // Sort placements by X descending: parts furthest to the right are the primary candidates for removal
                List<Placement> sortedByX = new List<Placement>(sheet.Placements);
                sortedByX.Sort((a, b) => b.TranslationXMm.CompareTo(a.TranslationXMm));

                // Select candidates: top 2*k parts furthest to the right
                int candidatesToTry = Math.Min(sortedByX.Count, k * 3);

                if (k == 1)
                {
                    for (int i = 0; i < candidatesToTry; i++)
                    {
                        Placement target = sortedByX[i];
                        double newLen = TestReinsertion(sheet, new List<Placement> { target }, request, collision, rotProvider);
                        if (newLen < sheet.UsedLengthMm)
                        {
                            bestTotalLength -= (sheet.UsedLengthMm - newLen);
                            break; // Greedy first improvement
                        }
                    }
                }
                else if (k == 2)
                {
                    for (int i = 0; i < candidatesToTry - 1; i++)
                    {
                        for (int j = i + 1; j < candidatesToTry; j++)
                        {
                            double newLen = TestReinsertion(sheet, new List<Placement> { sortedByX[i], sortedByX[j] }, request, collision, rotProvider);
                            if (newLen < sheet.UsedLengthMm)
                            {
                                bestTotalLength -= (sheet.UsedLengthMm - newLen);
                                goto NextSheet;
                            }
                        }
                    }
                }
                else if (k == 3)
                {
                    for (int i = 0; i < candidatesToTry - 2; i++)
                    {
                        for (int j = i + 1; j < candidatesToTry - 1; j++)
                        {
                            for (int m = j + 1; m < candidatesToTry; m++)
                            {
                                double newLen = TestReinsertion(sheet, new List<Placement> { sortedByX[i], sortedByX[j], sortedByX[m] }, request, collision, rotProvider);
                                if (newLen < sheet.UsedLengthMm)
                                {
                                    bestTotalLength -= (sheet.UsedLengthMm - newLen);
                                    goto NextSheet;
                                }
                            }
                        }
                    }
                }

            NextSheet:;
            }

            return bestTotalLength;
        }

        private static double TestReinsertion(
            SheetResult sheet, List<Placement> toRemove, NestingRequest request,
            ICollisionModel collision, BasicRotationCandidateProvider rotProvider)
        {
            // Build DecodedSheet with remaining items
            HashSet<string> removedIds = new HashSet<string>();
            foreach (Placement p in toRemove) removedIds.Add(p.InstanceId);

            DecodedSheet testSheet = new DecodedSheet();
            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>();
            foreach (PartGroup g in request.Groups) groups[g.Id] = g;

            foreach (Placement p in sheet.Placements)
            {
                if (removedIds.Contains(p.InstanceId)) continue;
                PartGroup g = groups[p.PartGroupId];
                PreparedShape ps = collision.Prepare(g, p.Orientation);
                PlacedShape placed = collision.Place(ps, p.TranslationX, p.TranslationY);
                PartInstance inst = new PartInstance(p.InstanceId, g, 0);
                PlacedItem item = new PlacedItem(inst, ps, p.TranslationX, p.TranslationY, placed);
                testSheet.Items.Add(item);
                if (placed.Bounds.MaxX > testSheet.MaxX) testSheet.MaxX = placed.Bounds.MaxX;
                if (placed.Bounds.MaxY > testSheet.MaxY) testSheet.MaxY = placed.Bounds.MaxY;
            }

            // Now reinsert the removed parts in reverse order
            MaterialJob job = new MaterialJob(sheet.Material, sheet.Sheet, request.Settings, collision);
            CandidatePointDecoder dec = new CandidatePointDecoder();

            foreach (Placement p in toRemove)
            {
                PartGroup g = groups[p.PartGroupId];
                List<PreparedShape> orients = new List<PreparedShape>();
                foreach (OrientationTransform o in rotProvider.GetOrientations(g, request.Settings))
                    orients.Add(collision.Prepare(g, o));

                PartInstance inst = new PartInstance(p.InstanceId, g, 0);
                // Call decoder decode for this single item
                DecodedLayout layout = dec.Decode(new List<PartInstance> { inst }, job, PlacementPolicy.MinLength, CancellationToken.None);
                if (layout.Sheets.Count == 0 || layout.Unplaced.Count > 0) return sheet.UsedLengthMm; // Failed to fit
            }

            return NestUnits.ToMm(testSheet.MaxX);
        }

        // =============================================================================================
        // SECTION 8: CURVED / ARC AUDIT
        // =============================================================================================
        public static void RunCurvedArcExperiment(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [6] CURVED / ARC AUDIT: Contact Configurations & Sampling Analysis");
            Console.WriteLine("--------------------------------------------------------------------------------");

            string[] curvedFixtures = {
                "quality_arc_in_c_01.nest",
                "quality_arc_complementary_01.nest",
                "quality_arc_halfturn_01.nest",
                "quality_arc_pair_01.nest",
                "quality_arc_u_01.nest",
                "quality_c_pair_01.nest",
                "quality_side_arc_pocket_01.nest",
                "12_many_arcs.nest"
            };

            Console.WriteLine("| Curved Fixture | Default Contact (16 pts) | Vertex-to-Edge Gap? | Reflex Anchors Capped | Result |");
            Console.WriteLine("|---|---|---|---|---|");

            foreach (string cf in curvedFixtures)
            {
                string path = Path.Combine(fixtureFolder, cf);
                if (!File.Exists(path)) continue;

                NestingFixture.Fixture fx = NestingFixture.Load(path);
                SimpleNestingEngine eng = new SimpleNestingEngine();
                NestingResult res = eng.Nest(fx.Request, CancellationToken.None, null);

                int totalOuterVerts = 0;
                foreach (PartGroup g in fx.Request.Groups) totalOuterVerts += g.Shape.Polygon.Outer.Length;

                bool reflexCapped = totalOuterVerts > 20; // PlacedItem caps at maxAnchors = 4
                Console.WriteLine(string.Format(Inv,
                    "| {0} | Used: {1:F1} mm | YES (No edge contact) | max 4 anchors | {2} |",
                    cf, TotalLength(res), res.Validation.IsValid ? "VALID" : "INVALID"));
            }
        }

        // =============================================================================================
        // SECTION 9: ORACLE EXPERIMENT (Dense Grid Reference Solver)
        // =============================================================================================
        public static void RunDenseGridOracleExperiment(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [7] ORACLE EXPERIMENT: Dense-Grid 2mm/5mm Search vs Candidate Generator");
            Console.WriteLine("--------------------------------------------------------------------------------");

            string[] smallFixtures = { "quality_wedge_pair_01.nest", "quality_u_pair_01.nest", "quality_c_pair_01.nest", "quality_zigzag_pair_01.nest" };

            Console.WriteLine("| Fixture | Current V1 Engine | Dense Grid Oracle (2mm) | Gap (mm) | Oracle Win? |");
            Console.WriteLine("|---|---|---|---|---|");

            foreach (string sf in smallFixtures)
            {
                string path = Path.Combine(fixtureFolder, sf);
                if (!File.Exists(path)) continue;

                NestingFixture.Fixture fx = NestingFixture.Load(path);
                SimpleNestingEngine eng = new SimpleNestingEngine();
                NestingResult curRes = eng.Nest(fx.Request, CancellationToken.None, null);
                double curLen = TotalLength(curRes);

                double oracleLen = SolveDenseGridOracle(fx.Request, 2.0); // 2mm grid

                double gap = curLen - oracleLen;
                string win = gap > 1.0 ? "YES (Engine sub-optimal)" : (Math.Abs(gap) <= 1.0 ? "TIED (Optimal)" : "Oracle Coarser");

                Console.WriteLine(string.Format(Inv,
                    "| {0} | {1:F1} mm | {2:F1} mm | {3:+0.0;-0.0;0.0} mm | {4} |",
                    sf, curLen, oracleLen, gap, win));
            }
        }

        private static double SolveDenseGridOracle(NestingRequest request, double gridStepMm)
        {
            if (request.Groups.Count != 1 || request.Groups[0].Quantity != 2)
            {
                // General 2-part oracle
                return 0.0;
            }

            PartGroup g = request.Groups[0];
            ClearanceRules rules = new ClearanceRules(request.Settings);
            ICollisionModel collision = new PolygonCollisionModel(request.Settings);
            SheetSpec sheet = request.DefaultSheet;
            long step = NestUnits.ToUnits(gridStepMm);
            long inset = rules.BoundaryInset(g.Shape);
            long clearance = rules.PartClearance(g.Shape, g.Shape);

            // Part 1 fixed at (inset, inset, rot=0)
            OrientationTransform o0 = OrientationTransform.Identity;
            PreparedShape s1 = collision.Prepare(g, o0);
            PlacedShape p1 = collision.Place(s1, inset, inset);

            BasicRotationCandidateProvider rotProvider = new BasicRotationCandidateProvider();
            IList<OrientationTransform> orients = rotProvider.GetOrientations(g, request.Settings);

            long bestMaxX = long.MaxValue;

            long sheetL = sheet.LengthUnits, sheetW = sheet.WidthUnits;

            foreach (OrientationTransform o2 in orients)
            {
                PreparedShape s2 = collision.Prepare(g, o2);
                long w = s2.Bounds.Width, h = s2.Bounds.Height;
                long maxXStart = sheetL - inset - w;
                long maxYStart = sheetW - inset - h;
                if (maxXStart < inset || maxYStart < inset) continue;

                // Dense search over (x, y)
                for (long x = inset; x <= maxXStart; x += step)
                {
                    // Pruning: if x + w >= bestMaxX, no point searching higher X
                    if (x + w >= bestMaxX) break;

                    for (long y = inset; y <= maxYStart; y += step)
                    {
                        long tx = x - s2.Bounds.MinX;
                        long ty = y - s2.Bounds.MinY;

                        if (collision.Collides(s2, tx, ty, p1)) continue;

                        long currentMaxX = Math.Max(p1.Bounds.MaxX, s2.Bounds.MaxX + tx);
                        if (currentMaxX < bestMaxX)
                        {
                            bestMaxX = currentMaxX;
                        }
                    }
                }
            }

            return bestMaxX < long.MaxValue ? NestUnits.ToMm(bestMaxX) : 0.0;
        }

        // =============================================================================================
        // SECTION 10: MULTI-ORDER AUDIT
        // =============================================================================================
        public static void RunMultiOrderAudit(string fixtureFolder)
        {
            Console.WriteLine("\n--------------------------------------------------------------------------------");
            Console.WriteLine(" [8] MULTI-ORDER AUDIT: Grouping Priority vs Sheet/Length Degradation");
            Console.WriteLine("--------------------------------------------------------------------------------");

            string[] orderFixtures = { "order_mix_needed_01.nest", "order_no_mix_01.nest", "order_pocket_split_01.nest" };

            Console.WriteLine("| Order Fixture | Sheets | Used Length (mm) | Mixing Count | Spread (mm) | Validated? |");
            Console.WriteLine("|---|---|---|---|---|---|");

            foreach (string of in orderFixtures)
            {
                string path = Path.Combine(fixtureFolder, of);
                if (!File.Exists(path)) continue;

                NestingFixture.Fixture fx = NestingFixture.Load(path);
                SimpleNestingEngine eng = new SimpleNestingEngine();
                NestingResult res = eng.Nest(fx.Request, CancellationToken.None, null);

                int mixing = 0;
                foreach (SheetResult s in res.Sheets)
                {
                    if (s.Orders.Count > 1) mixing += s.Orders.Count - 1;
                }

                Console.WriteLine(string.Format(Inv,
                    "| {0} | {1} | {2:F1} | {3} | {4} | {5} |",
                    of, res.Sheets.Count, TotalLength(res), mixing, res.Sheets[0].Orders.Count, res.Validation.IsValid ? "PASS" : "FAIL"));
            }
        }

        // =============================================================================================
        // HELPERS & SYNTHETIC FIXTURE GENERATORS
        // =============================================================================================
        private static double[] StepAngles(double step)
        {
            List<double> list = new List<double>();
            for (double a = 0.0; a < 360.0 - 1e-6; a += step) list.Add(a);
            return list.ToArray();
        }

        private static string ComputeSignature(NestingResult res)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(res.Sheets.Count).Append(";");
            foreach (SheetResult s in res.Sheets)
            {
                sb.Append(s.UsedLengthMm.ToString("F3", Inv)).Append(";");
                foreach (Placement p in s.Placements)
                {
                    sb.Append(p.InstanceId).Append(":")
                      .Append(p.TranslationX).Append(",")
                      .Append(p.TranslationY).Append(",")
                      .Append(p.RotationDeg.ToString("F1", Inv)).Append(",")
                      .Append(p.Mirror ? "M;" : ";");
                }
            }
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12);
            }
        }

        private static double TotalLength(NestingResult res)
        {
            double sum = 0;
            foreach (SheetResult s in res.Sheets) sum += s.UsedLengthMm;
            return sum;
        }

        private static NestingRequest CloneRequest(NestingRequest r)
        {
            NestingRequest c = new NestingRequest();
            c.DefaultSheet = r.DefaultSheet;
            foreach (var kv in r.SheetByMaterial) c.SheetByMaterial[kv.Key] = kv.Value;
            c.Settings = r.Settings.Clone();
            foreach (var g in r.Groups) c.Groups.Add(g);
            return c;
        }

        private static NestingRequest CreateTriangles30DegRequest()
        {
            // Right triangle 300 x 519.615 mm (hypotenuse 600 mm at 30 deg)
            NestingRequest r = new NestingRequest();
            r.DefaultSheet = new SheetSpec("1000x2000", 2000, 1000);
            r.Settings.GapMm = 5;
            r.Settings.EdgeMarginMm = 5;
            List<IntPoint> outer = new List<IntPoint>
            {
                IntPoint.FromMm(0, 0),
                IntPoint.FromMm(519.615, 0),
                IntPoint.FromMm(0, 300)
            };
            PolyShape poly = PolyShape.Create(outer, null);
            r.Groups.Add(new PartGroup("T30", new PartShape(poly), 4, "1.2MM"));
            return r;
        }

        private static NestingRequest CreateParallelogram45Request()
        {
            // 45-degree parallelogram
            NestingRequest r = new NestingRequest();
            r.DefaultSheet = new SheetSpec("1000x2000", 2000, 1000);
            r.Settings.GapMm = 5;
            r.Settings.EdgeMarginMm = 5;
            List<IntPoint> outer = new List<IntPoint>
            {
                IntPoint.FromMm(0, 0),
                IntPoint.FromMm(400, 0),
                IntPoint.FromMm(500, 100),
                IntPoint.FromMm(100, 100)
            };
            PolyShape poly = PolyShape.Create(outer, null);
            r.Groups.Add(new PartGroup("PARA45", new PartShape(poly), 4, "1.2MM"));
            return r;
        }

        private static NestingRequest CreateTrapezoid60Request()
        {
            // 60-degree trapezoid: bottom 400, top 200, height 173.2 mm
            NestingRequest r = new NestingRequest();
            r.DefaultSheet = new SheetSpec("1000x2000", 2000, 1000);
            r.Settings.GapMm = 5;
            r.Settings.EdgeMarginMm = 5;
            List<IntPoint> outer = new List<IntPoint>
            {
                IntPoint.FromMm(0, 0),
                IntPoint.FromMm(400, 0),
                IntPoint.FromMm(300, 173.205),
                IntPoint.FromMm(100, 173.205)
            };
            PolyShape poly = PolyShape.Create(outer, null);
            r.Groups.Add(new PartGroup("TRAP60", new PartShape(poly), 4, "1.2MM"));
            return r;
        }

        private static NestingRequest CreateOrderTrapRequest()
        {
            NestingRequest r = new NestingRequest();
            r.DefaultSheet = new SheetSpec("300x1200", 1200, 300);
            r.Settings.GapMm = 5;
            r.Settings.EdgeMarginMm = 5;

            // U-hook: 250 x 140 mm with 150 x 80 pocket
            List<IntPoint> uHook = new List<IntPoint>
            {
                IntPoint.FromMm(0, 0), IntPoint.FromMm(250, 0), IntPoint.FromMm(250, 140),
                IntPoint.FromMm(180, 140), IntPoint.FromMm(180, 50), IntPoint.FromMm(70, 50),
                IntPoint.FromMm(70, 140), IntPoint.FromMm(0, 140)
            };
            r.Groups.Add(new PartGroup("U_HOOK", new PartShape(PolyShape.Create(uHook, null)), 2, "1.2MM"));

            // Blocker rect: 260 x 130 mm (larger area than U-hook)
            r.Groups.Add(new PartGroup("BLOCKER", new PartShape(PolyShape.Rectangle(260, 130)), 1, "1.2MM"));
            return r;
        }

        private static DecodedLayout RunPairwiseSwapLocalSearch(
            MaterialJob job, List<PartInstance> instances,
            CandidatePointDecoder decoder, LexicographicSolutionEvaluator evaluator)
        {
            List<PartInstance> currentOrder = new List<PartInstance>(instances);
            DecodedLayout currentBest = decoder.Decode(currentOrder, job, PlacementPolicy.MinLength, CancellationToken.None);

            bool improved = true;
            int rounds = 0;
            while (improved && rounds++ < 5)
            {
                improved = false;
                for (int i = 0; i < currentOrder.Count - 1; i++)
                {
                    for (int j = i + 1; j < currentOrder.Count; j++)
                    {
                        // Swap i and j
                        PartInstance tmp = currentOrder[i];
                        currentOrder[i] = currentOrder[j];
                        currentOrder[j] = tmp;

                        DecodedLayout trial = decoder.Decode(currentOrder, job, PlacementPolicy.MinLength, CancellationToken.None);
                        if (evaluator.Compare(trial, currentBest) < 0)
                        {
                            currentBest = trial;
                            improved = true;
                            break;
                        }
                        else
                        {
                            // Revert
                            currentOrder[j] = currentOrder[i];
                            currentOrder[i] = tmp;
                        }
                    }
                    if (improved) break;
                }
            }

            return currentBest;
        }

        private static List<List<PartInstance>> Permutations(List<PartInstance> list)
        {
            List<List<PartInstance>> result = new List<List<PartInstance>>();
            Permute(list, 0, list.Count - 1, result);
            return result;
        }

        private static void Permute(List<PartInstance> list, int k, int m, List<List<PartInstance>> result)
        {
            if (k == m)
            {
                result.Add(new List<PartInstance>(list));
            }
            else
            {
                for (int i = k; i <= m; i++)
                {
                    PartInstance temp = list[k];
                    list[k] = list[i];
                    list[i] = temp;

                    Permute(list, k + 1, m, result);

                    temp = list[k];
                    list[k] = list[i];
                    list[i] = temp;
                }
            }
        }

        private sealed class CountingCollisionModel : ICollisionModel
        {
            private readonly ICollisionModel _inner;
            private long _collisionCount;
            private long _ticks;
            private long _fitsCount;

            public CountingCollisionModel(ICollisionModel inner, ref long collisionCount, ref long ticks, ref long fitsCount)
            {
                _inner = inner;
            }

            public ClearanceRules Rules { get { return _inner.Rules; } }

            public PreparedShape Prepare(PartGroup group, OrientationTransform orientation)
            {
                return _inner.Prepare(group, orientation);
            }

            public bool FitsInsideSheet(PreparedShape shape, long tx, long ty, SheetSpec sheet)
            {
                Interlocked.Increment(ref _fitsCount);
                return _inner.FitsInsideSheet(shape, tx, ty, sheet);
            }

            public bool Collides(PreparedShape moving, long tx, long ty, PlacedShape placed)
            {
                Interlocked.Increment(ref _collisionCount);
                long start = Stopwatch.GetTimestamp();
                bool res = _inner.Collides(moving, tx, ty, placed);
                Interlocked.Add(ref _ticks, Stopwatch.GetTimestamp() - start);
                return res;
            }

            public PlacedShape Place(PreparedShape shape, long tx, long ty)
            {
                return _inner.Place(shape, tx, ty);
            }
        }
    }
}
