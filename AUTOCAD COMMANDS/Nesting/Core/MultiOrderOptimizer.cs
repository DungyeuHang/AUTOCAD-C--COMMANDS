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

            /// <summary>Xep het chi tiet cua mot don roi moi sang don khac.</summary>
            public bool GroupByOrder;
        }

        /// <summary>
        /// So luot ghep se chay cho MOT vat lieu: moi thu tu xep x hai chinh sach dat.
        /// Biet truoc con so nay thi moi ve duoc thanh tien trinh that.
        /// </summary>
        public static int RunCount(NestingSettings settings)
        {
            return RunCount(settings, false);
        }

        /// <param name="orderAware">
        /// Co tu hai don hang tro len: khi do chay them may thu tu xep GOM THEO DON.
        /// </param>
        public static int RunCount(NestingSettings settings, bool orderAware)
        {
            int extra = settings != null ? Math.Max(0, settings.ExtraSeededOrderings) : 0;
            int grouped = orderAware ? OrderOrderingCount : 0;
            return (BaseOrderingCount + extra + grouped) * PlacementPolicyCount;
        }

        /// <summary>So thu tu xep co dinh trong <see cref="BuildOrderings"/> (chua tinh nhieu).</summary>
        private const int BaseOrderingCount = 5;

        /// <summary>
        /// So thu tu xep GOM THEO DON, chi them vao khi co tu hai don tro len.
        ///
        /// Chay mot don thi chung trung y het thu tu goc nen chi ton thoi gian vo ich - do la
        /// truong hop thuong gap nhat, khong duoc lam no cham di.
        /// </summary>
        private const int OrderOrderingCount = 2;

        private const int PlacementPolicyCount = 2;

        public OptimizationOutcome Optimize(MaterialJob job, CancellationToken cancellation, Action<NestingProgress> progress)
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
            int started = 0, completed = 0, skippedByBudget = 0, skippedByCancel = 0;
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

                // KHOI LUONG VIEC, KHONG PHAI DONG HO.
                //
                // Khoi luong viec logic cua buoc nay chinh la SO LUOT XEP (total), va con so do
                // chi phu thuoc cai dat: so thu tu xep co dinh, so luot nhieu, va co nhieu don
                // hay khong. Vi vay o che do tat dinh, khong cat bot gi ca - may nhanh xong som,
                // may cham xong muon, nhung CA HAI chay dung tung ay luot va ra dung cung mot
                // ket qua.
                //
                // Cach cu (cat theo dong ho) lam tap luot chay duoc phu thuoc toc do may. Da do
                // tren ban ve that (47 nhom, SL 85, han muc 30 giay):
                //     song song: 16 + 16 luot, khong cham han muc -> 12779 mm
                //     tuan tu  :  7 +  4 luot, CHAM han muc       -> 13050 mm (te hon 2%)
                // Van giu lai cach do sau mot o cai dat, cho ai can tran thoi gian hon can ket
                // qua giong nhau giua cac may.
                //
                // Duong thoat khi chay qua lau la NGUOI DUNG bam dung (cancellation), chu khong
                // phai cai dong ho - nhu vay ai quyet dinh dung la ro rang.
                if (i > 0 && !job.Settings.DeterministicSearch
                    && watch.Elapsed.TotalSeconds > job.Settings.TimeBudgetSeconds)
                {
                    Interlocked.Increment(ref skippedByBudget);
                    return;
                }

                Ordering ordering = orderings[i / policies.Length];
                PlacementPolicy policy = policies[i % policies.Length];
                Interlocked.Increment(ref started);

                List<PartInstance> order = Order(job.Instances, ordering.Key, ordering.GroupByOrder);
                DecodedLayout layout = _decoder.Decode(order, job, policy, cancellation);
                layout.OrderingName = ordering.Name + " / " + policy;
                layouts[i] = layout;

                // Bao sau khi CHAY XONG, khong phai luc bat dau: cac luot chay song song nen
                // neu bao luc bat dau thi thanh tien trinh nhay vot len gan het ngay tu dau
                // roi dung yen - nhin con te hon la khong co.
                int done = Interlocked.Increment(ref completed);
                if (progress != null)
                {
                    progress(new NestingProgress(string.Format(CultureInfo.InvariantCulture,
                        "{0}: thu tu {1}/{2} ({3}, {4})", job.Material, done, total, ordering.Name, policy),
                        done, total));
                }
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

            // Phep kiem thoi gian o tren chi chan duoc mot lan chay CHUA BAT DAU. Mot lan da
            // bat dau thi khong co gi dung no lai giua chung - bo giai ma chi dung khi nguoi
            // dung bam Dung. Tren may co so nhan >= so lan chay (16 nhan / 16 lan chay), MOI
            // lan chay deu kip bat dau khi dong ho con gan 0, nen skippedByBudget luon = 0 va
            // truoc day nguoi dung KHONG he duoc bao gi, du da chay gap nhieu lan thoi gian
            // cho phep (do duoc: dat 15 s, chay 169,7 s tren mot ban ve san xuat that).
            //
            // O day chi sua phan BAO CAO cho dung su that. Viec cat ngang mot lan chay dang
            // do se lam ket qua phu thuoc toc do may - do la mot thay doi ve hanh vi, khong
            // phai viec cua mot ban sua loi bao cao.
            // Chi bao "het gio" khi that su CO luot bi bo vi dong ho. O che do tat dinh thi
            // khong bao gio co, du chay lau hon han muc - vi chay lau khong lam ket qua xau di.
            if (skippedByBudget > 0)
            {
                outcome.TimeBudgetHit = true;
            }

            outcome.RunsPlanned = total;
            outcome.ElapsedSeconds = watch.Elapsed.TotalSeconds;
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

            // Gom theo don: xep xong het mot don roi moi sang don khac. Dieu nay TU NO khong
            // quyet dinh gi - no chi de ra them mot phuong an de bo xep hang chon. Neu phuong
            // an gom don ton them to hoac them chieu dai thi no thua ngay o khoa 2 / khoa 3.
            if (job.OrderAware)
            {
                list.Add(new Ordering { Name = "Theo don + dien tich giam dan", Key = g => g.Shape.Polygon.NetArea, GroupByOrder = true });
                list.Add(new Ordering { Name = "Theo don + ket hop", Key = combined, GroupByOrder = true });
            }

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

        private static List<PartInstance> Order(List<PartInstance> instances, Func<PartGroup, double> key, bool groupByOrder)
        {
            List<PartInstance> order = new List<PartInstance>(instances);
            Dictionary<string, double> keys = new Dictionary<string, double>();
            foreach (PartInstance i in instances)
            {
                if (!keys.ContainsKey(i.PartGroupId)) keys[i.PartGroupId] = key(i.Group);
            }

            order.Sort((a, b) =>
            {
                if (groupByOrder)
                {
                    int byOrder = string.CompareOrdinal(a.Order, b.Order);
                    if (byOrder != 0) return byOrder;
                }

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
