using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - TRINH TU CAC BUOC CHAN (BEND SEQUENCE)
    // ------------------------------------------------------------------------------------------
    // Sinh hinh dang MAT CAT sau TUNG lan chan, de tho dung tai may chan.
    //
    // MO HINH VAT LY (quan trong - day la cho de sai nhat):
    //   Vat lieu KHONG doi khi chan. Mot canh luon co chieu dai vat lieu la FlatLength
    //   (do tu tiep diem den tiep diem), va moi vung chan luon co chieu dai vat lieu la BA.
    //
    //   * Bend CHUA chan : vung chan BA nam THANG  -> ve them mot doan dai BA.
    //   * Bend DA chan   : vung chan cuon lai      -> KHONG ve doan nao, chi XOAY huong di,
    //                      va hai canh ke duoc KEO DAI them OSSB de gap nhau tai DINH MOLD LINE.
    //
    //   Nho mo hinh nay hai dau cua day trinh tu deu TU DONG dung:
    //     Buoc 0 : chua chan gi  -> tong chieu dai = sum(FlatLength) + sum(BA) = CHIEU RONG PHOI
    //     Buoc N : chan het      -> moi canh = chieu dai MOLD LINE = dung bang bien dang goc
    //
    // THU TU CHAN (phan quan trong ve CONG NGHE):
    //   Chan theo thu tu 1,2,3... nhu bien dang duoc ve la KHONG DUNG trong thuc te. Sau moi
    //   lan chan, chi tiet thay doi hinh dang va co the khong con dat vua vao coi / khong con
    //   tranh duoc chay dao. Vi vay che do mac dinh la AutoFeasible: tim thu tu chan bang
    //   TIM KIEM CO KIEM TRA VA CHAM (xem FoilPressBrake.cs), thay vi ap dat mot thu tu cung.
    //
    // HE TOA DO:
    //   * Points        - he toa do cua BIEN DANG (dung de kiem tra chieu dai vat lieu).
    //   * MachinePoints - HE TOA DO MAY: goc = dinh goc chan, truc Y = truc dao, +X = phia
    //                     truoc may. Day moi la hinh de VE cho tho, vi no cho biet phai dat
    //                     phoi theo chieu nao.
    // ==========================================================================================

    public enum FoilBendSequenceOrder
    {
        /// <summary>Chan lan luot tu dau bien dang den cuoi.</summary>
        ProfileOrder = 0,

        /// <summary>Chan nguoc lai, tu cuoi bien dang ve dau.</summary>
        Reverse = 1,

        /// <summary>Chan xen ke tu hai dau vao giua.</summary>
        OutsideIn = 2,

        /// <summary>
        /// TU DONG (mac dinh): tim thu tu chan kha thi bang mo hinh may chan - kiem tra va coi,
        /// va dao, chuan ga cu hau va so lan phai lat ton.
        /// </summary>
        AutoFeasible = 3
    }

    // ==========================================================================================
    // HINH DANG CHI TIET SAU MOT SO LAN CHAN
    // ------------------------------------------------------------------------------------------
    // Giu nguyen TAT CA cac nut (khong gop diem thang hang) de con anh xa duoc "vung chan cua
    // duong chan so k nam o doan nao". Viec gop diem chi lam khi xuat ra de ve.
    // ==========================================================================================
    public class FoilFormedShape
    {
        public FoilFormedShape(int maxBendIndex)
        {
            int size = maxBendIndex + 1;
            ZoneStartNode = new int[size];
            ZoneEndNode = new int[size];
            CornerNode = new int[size];
            ZoneMid = new FoilPoint2d[size];
            ZoneDirection = new FoilVector2d[size];

            for (int i = 0; i < size; i++)
            {
                ZoneStartNode[i] = -1;
                ZoneEndNode[i] = -1;
                CornerNode[i] = -1;
            }
        }

        public List<FoilPoint2d> Points { get; private set; } = new List<FoilPoint2d>();

        /// <summary>True tai nut la mot goc DA CHAN (co goc re that).</summary>
        public bool[] IsFormedCorner { get; set; }

        /// <summary>Nut bat dau vung chan (chi voi duong chan CHUA chan).</summary>
        public int[] ZoneStartNode { get; private set; }

        /// <summary>Nut ket thuc vung chan. Bang ZoneStartNode neu BA ~ 0.</summary>
        public int[] ZoneEndNode { get; private set; }

        /// <summary>Nut goc (chi voi duong chan DA chan).</summary>
        public int[] CornerNode { get; private set; }

        /// <summary>Tam vung chan - dung lam goc he toa do may.</summary>
        public FoilPoint2d[] ZoneMid { get; private set; }

        /// <summary>Huong di qua vung chan.</summary>
        public FoilVector2d[] ZoneDirection { get; private set; }

        public double OutlineLength { get; set; }

        /// <summary>
        /// Dung hinh dang chi tiet khi tap <paramref name="formed"/> da duoc chan.
        /// </summary>
        public static FoilFormedShape Build(FoilFlatPatternResult result, HashSet<int> formed)
        {
            int maxIndex = 0;
            foreach (FoilBendInfo b in result.Bends)
            {
                if (b.Index > maxIndex) maxIndex = b.Index;
            }

            FoilFormedShape shape = new FoilFormedShape(maxIndex);
            List<bool> corners = new List<bool>();

            FoilPoint2d current = new FoilPoint2d(0.0, 0.0);
            FoilVector2d direction = new FoilVector2d(1.0, 0.0);

            shape.Points.Add(current);
            corners.Add(false);

            List<FoilProfileElement> elements = result.Elements;

            for (int i = 0; i < elements.Count; i++)
            {
                FoilProfileElement element = elements[i];

                if (element.Kind == FoilElementKind.Flange)
                {
                    double length = element.FlatLength;

                    // Bend da chan thi hai canh ke duoc keo dai den DINH MOLD LINE.
                    FoilBendInfo before = NeighbourBend(elements, i - 1);
                    if (before != null && formed.Contains(before.Index))
                    {
                        length += before.AppliedSetbackNext;
                    }

                    FoilBendInfo after = NeighbourBend(elements, i + 1);
                    if (after != null && formed.Contains(after.Index))
                    {
                        length += after.AppliedSetbackPrev;
                    }

                    current = Advance(shape, corners, current, direction, length);
                }
                else
                {
                    FoilBendInfo bend = element.Bend;

                    if (formed.Contains(bend.Index))
                    {
                        // Da chan: vung chan cuon lai thanh goc, chi doi huong di.
                        shape.CornerNode[bend.Index] = shape.Points.Count - 1;
                        corners[shape.Points.Count - 1] = true;

                        double turn = bend.TurnSign >= 0.0 ? bend.BendAngleRad : -bend.BendAngleRad;
                        direction = direction.Rotate(turn);
                    }
                    else
                    {
                        // Chua chan: vung chan con nam thang, van chiem dung BA vat lieu.
                        int start = shape.Points.Count - 1;
                        FoilPoint2d from = current;
                        current = Advance(shape, corners, current, direction, bend.BendAllowance);

                        shape.ZoneStartNode[bend.Index] = start;
                        shape.ZoneEndNode[bend.Index] = shape.Points.Count - 1;
                        shape.ZoneMid[bend.Index] = new FoilPoint2d(
                            (from.X + current.X) * 0.5, (from.Y + current.Y) * 0.5);
                        shape.ZoneDirection[bend.Index] = direction;
                    }
                }
            }

            shape.IsFormedCorner = corners.ToArray();
            return shape;
        }

        private static FoilBendInfo NeighbourBend(List<FoilProfileElement> elements, int index)
        {
            if (index < 0 || index >= elements.Count)
            {
                return null;
            }

            FoilProfileElement element = elements[index];
            return element.Kind == FoilElementKind.Bend ? element.Bend : null;
        }

        private static FoilPoint2d Advance(
            FoilFormedShape shape,
            List<bool> corners,
            FoilPoint2d current,
            FoilVector2d direction,
            double length)
        {
            if (length <= FoilMath.LengthTolerance)
            {
                return current;
            }

            FoilPoint2d next = current + direction * length;
            shape.Points.Add(next);
            corners.Add(false);
            shape.OutlineLength += length;
            return next;
        }
    }

    public class FoilBendStep
    {
        /// <summary>0 = phoi phang chua chan; 1..N = sau lan chan thu k.</summary>
        public int StepNumber { get; set; }

        /// <summary>Duong chan duoc thuc hien o buoc nay (null voi buoc 0).</summary>
        public FoilBendInfo FormedBend { get; set; }

        /// <summary>Duong gap khuc mo ta mat cat sau buoc nay, trong HE TOA DO BIEN DANG.</summary>
        public List<FoilPoint2d> Points { get; private set; } = new List<FoilPoint2d>();

        /// <summary>
        /// Duong gap khuc sau buoc nay trong HE TOA DO MAY (goc = dinh goc chan, +Y = truc dao,
        /// +X = phia truoc may). Day la hinh dung de VE cho tho.
        /// </summary>
        public List<FoilPoint2d> MachinePoints { get; private set; } = new List<FoilPoint2d>();

        /// <summary>Vi tri dinh vua duoc chan (he toa do bien dang).</summary>
        public FoilPoint2d MarkerPoint { get; set; }

        /// <summary>True neu phai LAT TON so voi chieu duyet bien dang.</summary>
        public bool Flipped { get; set; }

        /// <summary>True neu phai doi dau quay vao cu hau so voi chieu duyet bien dang.</summary>
        public bool EndSwapped { get; set; }

        /// <summary>True neu buoc truoc do dat ton theo chieu nguoc lai (phai LAT).</summary>
        public bool FlipChanged { get; set; }

        /// <summary>Chieu dai chuan ga vao cu hau.</summary>
        public double GaugeLength { get; set; }

        /// <summary>Khoang ho nho nhat den long dao.</summary>
        public double PunchClearance { get; set; }

        public bool Feasible { get; set; } = true;

        public List<FoilBendIssue> Issues { get; private set; } = new List<FoilBendIssue>();

        public double MinX { get; set; }

        public double MinY { get; set; }

        public double MaxX { get; set; }

        public double MaxY { get; set; }

        public double Width { get { return MaxX - MinX; } }

        public double Height { get { return MaxY - MinY; } }

        /// <summary>Tong chieu dai duong gap khuc cua buoc nay.</summary>
        public double OutlineLength { get; set; }

        /// <summary>Dong chu chinh, ngan gon.</summary>
        public string Caption { get; set; } = string.Empty;

        /// <summary>Dong chu phu: thong so ga dat. Rong neu khong co gi dang noi.</summary>
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>Toan bo phuong an chan: thu tu + tung buoc + canh bao chung.</summary>
    public class FoilBendPlan
    {
        public List<FoilBendStep> Steps { get; private set; } = new List<FoilBendStep>();

        /// <summary>Thu tu chan (danh sach Index cua duong chan).</summary>
        public List<int> Order { get; private set; } = new List<int>();

        public FoilToolingGeometry Tooling { get; set; }

        /// <summary>True neu moi lan chan deu kha thi tren mo hinh may.</summary>
        public bool AllFeasible { get; set; } = true;

        /// <summary>So lan phai lat ton trong ca quy trinh.</summary>
        public int FlipCount { get; set; }

        public List<string> Warnings { get; private set; } = new List<string>();

        public string OrderDescription { get; set; } = string.Empty;
    }

    public static class FoilBendSequenceBuilder
    {
        /// <summary>Do rong chum toi da. Du lon de gan nhu luon ra phuong an toi uu.</summary>
        private const int MaxBeamWidth = 192;

        /// <summary>Do rong chum toi thieu - van du de thoat khoi cac ngo cut thong thuong.</summary>
        private const int MinBeamWidth = 24;

        /// <summary>
        /// Tran KHOI LUONG TINH: moi muc duyet (chum x ung vien) nen cong don khong doi.
        /// Khoi luong xap xi N * N * W, nen giu N*N*W <= hang so nay.
        /// </summary>
        private const int BeamWorkBudget = 60000;

        /// <summary>
        /// Do rong chum theo so duong chan. Voi so duong chan thuc te (<= 12) van la MaxBeamWidth
        /// nen ket qua trung tim kiem vet can (xem test 50); voi bien dang rat nhieu duong chan
        /// thi hep dan lai de thoi gian khong bung.
        /// </summary>
        private static int BeamWidthFor(int bendCount)
        {
            if (bendCount <= 1) return 1;

            long budget = BeamWorkBudget / ((long)bendCount * bendCount);
            if (budget > MaxBeamWidth) return MaxBeamWidth;
            if (budget < MinBeamWidth) return MinBeamWidth;
            return (int)budget;
        }

        /// <summary>
        /// Thu tu chan CO DINH (khong xet may): tra ve danh sach INDEX (1-based).
        /// AutoFeasible khong dung duoc o day - dung <see cref="Plan"/>.
        /// </summary>
        public static List<int> BuildOrder(int bendCount, FoilBendSequenceOrder order)
        {
            List<int> sequence = new List<int>(bendCount);

            switch (order)
            {
                case FoilBendSequenceOrder.Reverse:
                    for (int i = bendCount; i >= 1; i--)
                    {
                        sequence.Add(i);
                    }

                    break;

                case FoilBendSequenceOrder.OutsideIn:
                    int low = 1;
                    int high = bendCount;
                    while (low <= high)
                    {
                        sequence.Add(low);
                        if (high != low)
                        {
                            sequence.Add(high);
                        }

                        low++;
                        high--;
                    }

                    break;

                case FoilBendSequenceOrder.ProfileOrder:
                default:
                    for (int i = 1; i <= bendCount; i++)
                    {
                        sequence.Add(i);
                    }

                    break;
            }

            return sequence;
        }

        /// <summary>
        /// Giu lai cho code cu / test: sinh cac buoc theo mot thu tu CO DINH.
        /// </summary>
        public static List<FoilBendStep> Build(
            FoilFlatPatternResult result, FoilBendSequenceOrder order)
        {
            FoilSettings s = (result != null && result.Settings != null)
                ? result.Settings.Clone()
                : new FoilSettings();
            s.BendSequenceOrder = order;
            return Plan(result, s).Steps;
        }

        /// <summary>
        /// Lap phuong an chan hoan chinh: chon thu tu, kiem tra tung lan chan tren mo hinh may,
        /// sinh hinh dang cua tung buoc trong ca hai he toa do.
        /// </summary>
        public static FoilBendPlan Plan(FoilFlatPatternResult result, FoilSettings settings)
        {
            FoilBendPlan plan = new FoilBendPlan();
            if (result == null || !result.IsUsable)
            {
                return plan;
            }

            FoilSettings s = settings ?? result.Settings ?? new FoilSettings();
            FoilToolingGeometry tooling = FoilToolingGeometry.Resolve(s);
            plan.Tooling = tooling;

            Dictionary<int, FoilBendInfo> byIndex = new Dictionary<int, FoilBendInfo>();
            foreach (FoilBendInfo b in result.Bends)
            {
                byIndex[b.Index] = b;
            }

            List<int> order;
            if (s.BendSequenceOrder == FoilBendSequenceOrder.AutoFeasible)
            {
                order = SearchFeasibleOrder(result, byIndex, tooling, s);
                plan.OrderDescription = "Tu dong (co kiem tra va coi / va dao / cu hau)";
            }
            else
            {
                order = BuildOrder(result.BendCount, s.BendSequenceOrder);
                plan.OrderDescription = DescribeOrder(s.BendSequenceOrder);
            }

            plan.Order.AddRange(order);

            // ---- Buoc 0: phoi phang ----
            HashSet<int> formed = new HashSet<int>();
            FoilFormedShape shape = FoilFormedShape.Build(result, formed);

            FoilBendStep zero = MakeStep(result, shape, 0, null, s);
            if (order.Count > 0 && byIndex.ContainsKey(order[0]))
            {
                // Dat phoi phang len coi dung nhu luc chuan bi cho lan chan dau tien.
                FoilBendMounting first = FoilPressBrakeModel.Evaluate(shape, byIndex[order[0]], tooling);
                zero.MachinePoints.AddRange(first.MachinePointsBefore);
            }
            else
            {
                zero.MachinePoints.AddRange(shape.Points);
            }

            plan.Steps.Add(zero);

            // ---- Cac buoc chan ----
            bool previousFlipped = false;
            bool havePrevious = false;

            for (int k = 0; k < order.Count; k++)
            {
                int bendIndex = order[k];
                FoilBendInfo bend;
                if (!byIndex.TryGetValue(bendIndex, out bend))
                {
                    continue;
                }

                FoilBendMounting mount = FoilPressBrakeModel.Evaluate(shape, bend, tooling);

                formed.Add(bendIndex);
                FoilFormedShape next = FoilFormedShape.Build(result, formed);

                FoilBendStep step = MakeStep(result, next, k + 1, bend, s);
                step.MachinePoints.AddRange(mount.MachinePoints);
                step.Flipped = mount.Flipped;
                step.EndSwapped = mount.EndSwapped;
                step.GaugeLength = mount.GaugeLength;
                step.PunchClearance = mount.PunchClearance;
                step.Feasible = mount.Feasible;
                step.Issues.AddRange(mount.Issues);
                step.FlipChanged = havePrevious && mount.Flipped != previousFlipped;

                if (step.FlipChanged)
                {
                    plan.FlipCount++;
                }

                if (!mount.Feasible)
                {
                    plan.AllFeasible = false;
                }

                step.Caption = BuildCaption(step, result, s);
                step.Detail = BuildDetail(step, s);

                plan.Steps.Add(step);

                previousFlipped = mount.Flipped;
                havePrevious = true;
                shape = next;
            }

            CollectWarnings(plan, s);
            return plan;
        }

        // ======================================================================================
        // TIM THU TU CHAN KHA THI
        // --------------------------------------------------------------------------------------
        // Tim kiem theo CHUM (beam search) tren khong gian cac hoan vi: moi muc them mot duong
        // chan, giu lai BeamWidth phuong an re nhat. Voi so duong chan thuc te (<= 15) chum nay
        // du rong de ket qua trung voi tim kiem vet can.
        //
        // Gia cua mot lan chan gom:
        //   * phat rat nang neu VA COI / VA DAO (de thu tu kha thi luon thang thu tu khong kha thi)
        //   * phat neu phai LAT TON so voi lan chan truoc (moi lan lat la mot lan mat cong)
        //   * phat nhe khi ho dao it, chi tiet cao, chuan ga ngan
        // ======================================================================================
        private static List<int> SearchFeasibleOrder(
            FoilFlatPatternResult result,
            Dictionary<int, FoilBendInfo> byIndex,
            FoilToolingGeometry tooling,
            FoilSettings settings)
        {
            List<int> all = new List<int>();
            foreach (FoilBendInfo b in result.Bends)
            {
                all.Add(b.Index);
            }

            all.Sort();

            if (all.Count <= 1)
            {
                return all;
            }

            double flipPenalty = settings.FlipPenalty > 0.0 ? settings.FlipPenalty : 25.0;
            int beamWidth = BeamWidthFor(all.Count);

            List<SearchNode> beam = new List<SearchNode> { SearchNode.Root() };

            for (int level = 0; level < all.Count; level++)
            {
                List<SearchNode> candidates = new List<SearchNode>();

                foreach (SearchNode node in beam)
                {
                    HashSet<int> formed = new HashSet<int>(node.Order);
                    FoilFormedShape shape = FoilFormedShape.Build(result, formed);

                    for (int i = 0; i < all.Count; i++)
                    {
                        int candidate = all[i];
                        if (formed.Contains(candidate)) continue;

                        FoilBendMounting mount =
                            FoilPressBrakeModel.Evaluate(shape, byIndex[candidate], tooling);

                        double cost = node.Cost + mount.Cost;
                        if (node.HasPrevious && mount.Flipped != node.LastFlipped)
                        {
                            cost += flipPenalty;
                        }

                        // Uu tien rat nhe thu tu tu nhien cua bien dang khi moi thu ngang nhau,
                        // de bien dang don gian van cho ra 1,2,3... quen thuoc.
                        cost += candidate * 1e-6;

                        candidates.Add(node.Extend(candidate, cost, mount.Flipped));
                    }
                }

                if (candidates.Count == 0)
                {
                    break;
                }

                candidates.Sort(CompareNodes);
                if (candidates.Count > beamWidth)
                {
                    candidates.RemoveRange(beamWidth, candidates.Count - beamWidth);
                }

                beam = candidates;
            }

            return beam.Count > 0 ? beam[0].Order : all;
        }

        private static int CompareNodes(SearchNode a, SearchNode b)
        {
            int c = a.Cost.CompareTo(b.Cost);
            if (c != 0) return c;

            for (int i = 0; i < a.Order.Count && i < b.Order.Count; i++)
            {
                c = a.Order[i].CompareTo(b.Order[i]);
                if (c != 0) return c;
            }

            return 0;
        }

        private class SearchNode
        {
            public List<int> Order;
            public double Cost;
            public bool LastFlipped;
            public bool HasPrevious;

            public static SearchNode Root()
            {
                return new SearchNode { Order = new List<int>(), Cost = 0.0 };
            }

            public SearchNode Extend(int bendIndex, double cost, bool flipped)
            {
                List<int> next = new List<int>(Order.Count + 1);
                next.AddRange(Order);
                next.Add(bendIndex);

                return new SearchNode
                {
                    Order = next,
                    Cost = cost,
                    LastFlipped = flipped,
                    HasPrevious = true
                };
            }
        }

        // ======================================================================================
        // SINH MOT BUOC
        // ======================================================================================
        private static FoilBendStep MakeStep(
            FoilFlatPatternResult result,
            FoilFormedShape shape,
            int stepNumber,
            FoilBendInfo formedBend,
            FoilSettings settings)
        {
            FoilBendStep step = new FoilBendStep
            {
                StepNumber = stepNumber,
                FormedBend = formedBend,
                OutlineLength = shape.OutlineLength
            };

            step.Points.AddRange(shape.Points);

            if (formedBend != null && formedBend.Index < shape.CornerNode.Length)
            {
                int corner = shape.CornerNode[formedBend.Index];
                if (corner >= 0 && corner < shape.Points.Count)
                {
                    step.MarkerPoint = shape.Points[corner];
                }
            }

            RemoveCollinearPoints(step);
            ComputeBounds(step);

            if (formedBend == null && step.Points.Count > 0)
            {
                step.MarkerPoint = step.Points[0];
            }

            step.Caption = BuildCaption(step, result, settings);
            return step;
        }

        /// <summary>
        /// Gop cac diem THANG HANG lai. Phan chua chan gom nhieu doan noi tiep cung huong
        /// (canh + vung chan con phang), neu giu nguyen thi polyline se co dinh thua.
        /// Chi bo diem khong co goc re that, nen hinh dang va chieu dai KHONG doi.
        /// </summary>
        private static void RemoveCollinearPoints(FoilBendStep step)
        {
            const double collinearTolerance = 1e-9;

            for (int i = step.Points.Count - 2; i >= 1; i--)
            {
                FoilVector2d incoming = step.Points[i] - step.Points[i - 1];
                FoilVector2d outgoing = step.Points[i + 1] - step.Points[i];

                if (FoilMath.AngleBetween(incoming, outgoing) <= collinearTolerance)
                {
                    step.Points.RemoveAt(i);
                }
            }
        }

        /// <summary>Gop diem thang hang tren mot duong gap khuc bat ky (dung cho he toa do may).</summary>
        public static List<FoilPoint2d> Simplify(List<FoilPoint2d> points)
        {
            List<FoilPoint2d> copy = new List<FoilPoint2d>(points);

            for (int i = copy.Count - 2; i >= 1; i--)
            {
                FoilVector2d incoming = copy[i] - copy[i - 1];
                FoilVector2d outgoing = copy[i + 1] - copy[i];

                if (incoming.Length <= FoilMath.LengthTolerance ||
                    FoilMath.AngleBetween(incoming, outgoing) <= 1e-9)
                {
                    copy.RemoveAt(i);
                }
            }

            return copy;
        }

        private static void ComputeBounds(FoilBendStep step)
        {
            step.MinX = double.MaxValue;
            step.MinY = double.MaxValue;
            step.MaxX = double.MinValue;
            step.MaxY = double.MinValue;

            foreach (FoilPoint2d p in step.Points)
            {
                if (p.X < step.MinX) step.MinX = p.X;
                if (p.Y < step.MinY) step.MinY = p.Y;
                if (p.X > step.MaxX) step.MaxX = p.X;
                if (p.Y > step.MaxY) step.MaxY = p.Y;
            }

            if (step.Points.Count == 0)
            {
                step.MinX = step.MinY = step.MaxX = step.MaxY = 0.0;
            }
        }

        private static string DescribeOrder(FoilBendSequenceOrder order)
        {
            switch (order)
            {
                case FoilBendSequenceOrder.Reverse: return "Nguoc chieu bien dang";
                case FoilBendSequenceOrder.OutsideIn: return "Tu hai dau vao giua";
                case FoilBendSequenceOrder.AutoFeasible: return "Tu dong";
                default: return "Theo chieu bien dang";
            }
        }

        private static string BuildCaption(
            FoilBendStep step, FoilFlatPatternResult result, FoilSettings settings)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;

            // Dong chu nay quyet dinh BE RONG O trong luoi hinh, nen giu that ngan: chi lam
            // tron den 1 chu so. So day du van nam o bang ket qua.
            const string f = "0.#";

            if (step.FormedBend == null)
            {
                return string.Format(
                    ci,
                    "B0  PHOI PHANG  {0} x {1}",
                    result.BlankLength.ToString(f, ci),
                    result.BlankWidth.ToString(f, ci));
            }

            string flag = step.FlipChanged ? "  [LAT TON]" : string.Empty;
            if (!step.Feasible)
            {
                flag += "  [!]";
            }

            return string.Format(
                ci,
                "B{0}  chan #{1}  {2} do{3}",
                step.StepNumber,
                step.FormedBend.Index,
                step.FormedBend.BendAngleDeg.ToString(f, ci),
                flag);
        }

        private static string BuildDetail(FoilBendStep step, FoilSettings settings)
        {
            if (step.FormedBend == null)
            {
                return string.Empty;
            }

            CultureInfo ci = CultureInfo.InvariantCulture;
            const string f = "0.#";

            string text = string.Format(
                ci,
                "cu {0} | {1}",
                step.GaugeLength.ToString(f, ci),
                step.Flipped ? "mat B" : "mat A");

            foreach (FoilBendIssue issue in step.Issues)
            {
                if (issue.Level == FoilBendIssueLevel.Blocking)
                {
                    return text + "  |  KHONG CHAN DUOC";
                }
            }

            return text;
        }

        private static void CollectWarnings(FoilBendPlan plan, FoilSettings settings)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            HashSet<string> seen = new HashSet<string>();

            foreach (FoilBendStep step in plan.Steps)
            {
                foreach (FoilBendIssue issue in step.Issues)
                {
                    if (issue.Level == FoilBendIssueLevel.Note) continue;

                    string line = string.Format(ci, "B{0} (#{1}): {2}",
                        step.StepNumber,
                        step.FormedBend != null ? step.FormedBend.Index : 0,
                        issue.Message);

                    if (seen.Add(line))
                    {
                        plan.Warnings.Add(line);
                    }
                }
            }

            if (plan.FlipCount > 0)
            {
                plan.Warnings.Add(string.Format(ci,
                    "Quy trinh can LAT TON {0} lan.", plan.FlipCount));
            }
        }
    }
}
