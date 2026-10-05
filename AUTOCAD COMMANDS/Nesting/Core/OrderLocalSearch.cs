using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Muc tim kiem "Can bang": DOI CHO TUNG CHI TIET trong thu tu cua luot xep thang cuoc.
    ///
    /// Vi sao can: moi thu tu xep cua V1 deu giu cac BAN SAO cung nhom LIEN NHAU (sap theo
    /// khoa nhom roi so thu tu ban sao; nhieu cung theo nhom). Thu tu tot nhat tim bang vet can
    /// tren cac fixture nho lai XEN KE ban sao cua cac nhom khac nhau - ma V1 khong bao gio
    /// sinh ra duoc. Da do: them luot ngau nhien (3 -> 64) va doi cho CA KHOI nhom deu khong
    /// go them duoc mili nao; doi cho TUNG chi tiet khac nhom thi co.
    ///
    /// Cach lam (leo doi, first improvement, hoan toan tat dinh):
    ///   - Lan can: hoan doi vi tri i va i+d (d = 1..<see cref="Window"/>) neu hai chi tiet
    ///     KHAC nhom; bo trung theo chuoi ma nhom (giu cai sinh truoc).
    ///   - Moi lo <see cref="Batch"/> lan can giai ma song song; nhan lan can dau tien (chi so
    ///     nho nhat) tot hon HAN theo bo xep hang. Batch la HANG SO - khong theo so nhan - nen
    ///     tap lan can duoc thu va ket qua giong het giua may nhieu nhan / it nhan / tuan tu.
    ///   - Dung khi het <see cref="Budget"/> luot giai ma, khi da thu vong ca lan can ma
    ///     khong tot hon, khi nguoi dung bam Dung, hoac (chi khi tat "Tim du") khi het gio.
    ///
    /// Moi luot giai ma tao PlacedItem / PlacedShape RIENG (PlacedShape co bo dem _stamp khong
    /// an toan luong) - khong chia se "tien to" bo cuc giua cac luong.
    ///
    /// Day la tim kiem cuc bo co ngan sach, KHONG phai tim nghiem tot nhat: ket qua la cai tot
    /// nhat tim duoc trong ngan sach, va khong bao gio te hon ket qua V1 (chi nhan khi tot hon).
    /// </summary>
    internal sealed class OrderLocalSearch
    {
        /// <summary>Khoang cach xa nhat giua hai vi tri duoc hoan doi.</summary>
        public const int Window = 6;

        /// <summary>
        /// So luot giai ma toi da cho MOT vat lieu.
        ///
        /// Da do (W=3): ban ve that -1.54% chieu dai voi 32 luot (~4 lan thoi gian).
        /// Sau khi co chinh sach OM SAT, do lai tren 24 bo ngau nhien (chu nhat / L / U / T /
        /// tam giac, 10-28 nhom): W=3/32 -1.0%, W=6/64 -1.4%, W=8/96 -1.5% so voi muc Nhanh cu,
        /// thoi gian 3.2x / 4.4x / 5.8x. Chon W=6/64: phan lon loi ich, thoi gian con chap nhan
        /// duoc (ban ve that 5 to: ~6 s).
        /// </summary>
        public const int Budget = 64;

        /// <summary>So lan can giai ma cung luc trong mot lo. HANG SO de ket qua khong phu thuoc may.</summary>
        public const int Batch = 8;

        private readonly IPlacementDecoder _decoder;
        private readonly ISolutionEvaluator _evaluator;
        private readonly bool _pruning;

        /// <param name="pruning">
        /// Cho phep bo giai ma dung som khi chac chan khong tot hon (chi khi dung
        /// <see cref="CandidatePointDecoder"/> + <see cref="LexicographicSolutionEvaluator"/>,
        /// vi phep cat dua tren dung thu tu khoa cua bo xep hang do). Khong doi ket qua.
        /// </param>
        public OrderLocalSearch(IPlacementDecoder decoder, ISolutionEvaluator evaluator, bool pruning)
        {
            _decoder = decoder;
            _evaluator = evaluator;
            _pruning = pruning && decoder is CandidatePointDecoder && evaluator is LexicographicSolutionEvaluator;
        }

        public sealed class Result
        {
            public DecodedLayout Best;
            public int Decodes;
            public int Improvements;
            public int Pruned;
            public bool TimeBudgetHit;
            public bool Cancelled;
        }

        /// <param name="progress">Goi sau MOI lo voi so luot giai ma da dung (0..Budget).</param>
        public Result Run(
            MaterialJob job,
            List<PartInstance> startOrder,
            PlacementPolicy policy,
            DecodedLayout start,
            string startName,
            Stopwatch watch,
            CancellationToken cancellation,
            Action<int> progress)
        {
            Result result = new Result { Best = start };
            List<PartInstance> cur = new List<PartInstance>(startOrder);
            CandidatePointDecoder bounded = _pruning ? (CandidatePointDecoder)_decoder : null;
            int degree = job.Settings.MaxParallelism > 0 ? job.Settings.MaxParallelism : Environment.ProcessorCount;
            int pos = 0, sinceImprove = 0;
            List<List<PartInstance>> nb = null;

            while (result.Decodes < Budget)
            {
                if (cancellation.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                // Giong cach bo luot xep: o che do tat dinh khong nhin dong ho.
                if (!job.Settings.DeterministicSearch && watch.Elapsed.TotalSeconds > job.Settings.TimeBudgetSeconds)
                {
                    result.TimeBudgetHit = true;
                    break;
                }

                if (nb == null) nb = Neighbours(cur);
                int moves = nb.Count;
                if (moves == 0 || sinceImprove >= moves) break;

                int k = Math.Min(Batch, Math.Min(Budget - result.Decodes, moves - sinceImprove));
                DecodedLayout[] decoded = new DecodedLayout[k];
                DecodedLayout bound = result.Best;
                int batchPos = pos;
                ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(degree, k)) };
                Parallel.For(0, k, options, j =>
                {
                    List<PartInstance> order = nb[(batchPos + j) % moves];
                    decoded[j] = bounded != null
                        ? bounded.Decode(order, job, policy, cancellation, bound)
                        : _decoder.Decode(order, job, policy, cancellation);
                });

                result.Decodes += k;
                int accepted = -1;
                for (int j = 0; j < k; j++)
                {
                    DecodedLayout res = decoded[j];
                    if (res.Pruned) result.Pruned++;
                    if (accepted >= 0 || res.Cancelled || res.Pruned) continue;
                    if (_evaluator.Compare(res, result.Best) < 0) accepted = j;
                }

                if (accepted >= 0)
                {
                    int index = (batchPos + accepted) % moves;
                    DecodedLayout res = decoded[accepted];
                    res.OrderingName = startName + " + doi cho";
                    result.Best = res;
                    result.Improvements++;
                    cur = nb[index];
                    nb = null;
                    pos = index + 1;
                    sinceImprove = 0;
                }
                else
                {
                    pos = (pos + k) % moves;
                    sinceImprove += k;
                }

                if (progress != null) progress(result.Decodes);
            }

            if (cancellation.IsCancellationRequested) result.Cancelled = true;
            return result;
        }

        /// <summary>
        /// Moi hoan doi (i, i+d), d = 1..Window, giua hai chi tiet KHAC nhom; bo trung theo
        /// chuoi ma nhom cua thu tu moi (hai thu tu cung chuoi ma nhom cho ra cung hinh hoc).
        /// Thu tu sinh co dinh: i tang dan, trong moi i thi d tang dan.
        /// </summary>
        internal static List<List<PartInstance>> Neighbours(List<PartInstance> cur)
        {
            int n = cur.Count;
            List<List<PartInstance>> list = new List<List<PartInstance>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string[] ids = new string[n];
            for (int i = 0; i < n; i++) ids[i] = cur[i].PartGroupId;

            for (int i = 0; i < n; i++)
            {
                for (int d = 1; d <= Window && i + d < n; d++)
                {
                    int j = i + d;
                    if (string.Equals(ids[i], ids[j], StringComparison.Ordinal)) continue;

                    string a = ids[i];
                    ids[i] = ids[j];
                    ids[j] = a;
                    string key = string.Join("\u0001", ids);
                    ids[j] = ids[i];
                    ids[i] = a;
                    if (!seen.Add(key)) continue;

                    List<PartInstance> order = new List<PartInstance>(cur);
                    PartInstance t = order[i];
                    order[i] = order[j];
                    order[j] = t;
                    list.Add(order);
                }
            }

            return list;
        }
    }
}
