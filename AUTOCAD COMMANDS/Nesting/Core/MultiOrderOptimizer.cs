using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Runs the decoder for several deterministic part orderings (and both placement policies)
    /// and keeps the best layout according to the evaluator. Optional extra orderings are
    /// seeded jitters of the combined heuristic - fully reproducible for the same seed.
    /// This is multi-start, not a genetic algorithm / simulated annealing.
    /// </summary>
    public sealed class MultiOrderOptimizer : IOptimizer
    {
        private readonly IPlacementDecoder _decoder;
        private readonly ISolutionEvaluator _evaluator;

        public MultiOrderOptimizer(IPlacementDecoder decoder, ISolutionEvaluator evaluator)
        {
            _decoder = decoder;
            _evaluator = evaluator;
        }

        private sealed class Ordering
        {
            public string Name;
            public Func<PartGroup, double> Key;       // larger key = placed earlier
        }

        public OptimizationOutcome Optimize(MaterialJob job, CancellationToken cancellation, Action<string> progress)
        {
            OptimizationOutcome outcome = new OptimizationOutcome();
            Stopwatch watch = Stopwatch.StartNew();

            List<Ordering> orderings = BuildOrderings(job);
            PlacementPolicy[] policies = { PlacementPolicy.LeftBottom, PlacementPolicy.MinLength };
            int total = orderings.Count * policies.Length;

            // Every (ordering, policy) run is independent and the core has no shared mutable
            // state, so runs execute in parallel. The winner is chosen afterwards in run-index
            // order with a strict "better than" - identical to the sequential result.
            DecodedLayout[] layouts = new DecodedLayout[total];
            int started = 0, skippedByBudget = 0, skippedByCancel = 0;
            int degree = job.Settings.MaxParallelism > 0 ? job.Settings.MaxParallelism : Environment.ProcessorCount;
            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(degree, total)) };

            Parallel.For(0, total, options, i =>
            {
                // Run 0 always executes so there is always a result.
                if (i > 0 && cancellation.IsCancellationRequested)
                {
                    Interlocked.Increment(ref skippedByCancel);
                    return;
                }

                if (i > 0 && watch.Elapsed.TotalSeconds > job.Settings.TimeBudgetSeconds)
                {
                    Interlocked.Increment(ref skippedByBudget);
                    return;
                }

                Ordering ordering = orderings[i / policies.Length];
                PlacementPolicy policy = policies[i % policies.Length];
                int n = Interlocked.Increment(ref started);
                if (progress != null)
                {
                    progress(string.Format(CultureInfo.InvariantCulture,
                        "{0}: thu tu {1}/{2} ({3}, {4})", job.Material, n, total, ordering.Name, policy));
                }

                List<PartInstance> order = Order(job.Instances, ordering.Key);
                DecodedLayout layout = _decoder.Decode(order, job, policy, cancellation);
                layout.OrderingName = ordering.Name + " / " + policy;
                layouts[i] = layout;
            });

            DecodedLayout partial = null;
            for (int i = 0; i < total; i++)
            {
                DecodedLayout layout = layouts[i];
                if (layout == null) continue;
                outcome.OrderingsTried++;
                if (layout.Cancelled)
                {
                    outcome.Cancelled = true;
                    if (partial == null) partial = layout;
                    continue;
                }

                if (outcome.Best == null || _evaluator.Compare(layout, outcome.Best) < 0)
                {
                    outcome.Best = layout;
                }
            }

            if (outcome.Best == null) outcome.Best = partial;
            if (skippedByCancel > 0) outcome.Cancelled = true;
            if (skippedByBudget > 0) outcome.TimeBudgetHit = true;
            return outcome;
        }

        private static List<Ordering> BuildOrderings(MaterialJob job)
        {
            double maxArea = 1, maxLong = 1;
            foreach (PartGroup g in job.Groups)
            {
                maxArea = Math.Max(maxArea, g.Shape.Polygon.NetArea);
                maxLong = Math.Max(maxLong, Math.Max(g.Shape.Polygon.Bounds.Width, g.Shape.Polygon.Bounds.Height));
            }

            Func<PartGroup, double> combined = g =>
                0.5 * g.Shape.Polygon.NetArea / maxArea +
                0.5 * Math.Max(g.Shape.Polygon.Bounds.Width, g.Shape.Polygon.Bounds.Height) / maxLong;

            List<Ordering> list = new List<Ordering>
            {
                new Ordering { Name = "Dien tich giam dan", Key = g => g.Shape.Polygon.NetArea },
                new Ordering { Name = "Chieu cao bao giam dan", Key = g => g.Shape.Polygon.Bounds.Height },
                new Ordering { Name = "Chieu rong bao giam dan", Key = g => g.Shape.Polygon.Bounds.Width },
                new Ordering { Name = "Canh dai nhat giam dan", Key = g => Math.Max(g.Shape.Polygon.Bounds.Width, g.Shape.Polygon.Bounds.Height) },
                new Ordering { Name = "Ket hop", Key = combined }
            };

            for (int k = 0; k < Math.Max(0, job.Settings.ExtraSeededOrderings); k++)
            {
                // System.Random with a fixed seed is deterministic on .NET Framework.
                Random rnd = new Random(unchecked(job.Settings.Seed * 7919 + k * 104729 + 17));
                Dictionary<string, double> jitter = new Dictionary<string, double>();
                List<PartGroup> sorted = new List<PartGroup>(job.Groups);
                sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                foreach (PartGroup g in sorted) jitter[g.Id] = 1.0 + (rnd.NextDouble() - 0.5) * 0.6;

                list.Add(new Ordering
                {
                    Name = "Ket hop + nhieu #" + (k + 1).ToString(CultureInfo.InvariantCulture),
                    Key = g => combined(g) * jitter[g.Id]
                });
            }

            return list;
        }

        private static List<PartInstance> Order(List<PartInstance> instances, Func<PartGroup, double> key)
        {
            List<PartInstance> order = new List<PartInstance>(instances);
            Dictionary<string, double> keys = new Dictionary<string, double>();
            foreach (PartInstance i in instances)
            {
                if (!keys.ContainsKey(i.PartGroupId)) keys[i.PartGroupId] = key(i.Group);
            }

            order.Sort((a, b) =>
            {
                int c = keys[b.PartGroupId].CompareTo(keys[a.PartGroupId]);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.PartGroupId, b.PartGroupId);
                if (c != 0) return c;
                return a.CopyIndex.CompareTo(b.CopyIndex);
            });

            return order;
        }
    }
}
