using System;
using System.Collections.Generic;
using System.Globalization;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    /// <summary>
    /// Turns loose curve chains into parts:
    ///   1. closed chains are loops; open chains are joined end-to-end (JoinTolerance),
    ///   2. dangling chains are pruned; branching nodes are reported (never guessed),
    ///   3. loops are validated (>= 3 vertices, non-zero area, not self-intersecting),
    ///   4. containment depth: even depth = part outline, odd depth = hole of its parent,
    ///      so a part drawn inside another part's hole is still its own part,
    ///   5. crossing/touching contours are reported,
    ///   6. open geometry inside a part is kept as marking (bend lines...), open geometry
    ///      outside every part is an INVALID GEOMETRY record (probably a broken contour).
    /// Every selected entity ends up in a part, a marking, or an explicit invalid record.
    /// </summary>
    public sealed class ContourLoopBuilder
    {
        private readonly RecognitionSettings _settings;

        /// <summary>So duong bao trung khit da duoc gop lam 1 (xem <see cref="AddLoop"/>).</summary>
        public int MergedDuplicateLoops { get; private set; }

        /// <summary>Vi tri mot duong bao trung khit, de nguoi dung con biet duong ma don ban ve.</summary>
        public string FirstDuplicateAt { get; private set; }

        /// <summary>So nhom hinh HO nam ngoai moi chi tiet da bi bo qua.</summary>
        public int DroppedOpenGroups { get; private set; }

        public ContourLoopBuilder(RecognitionSettings settings)
        {
            _settings = settings ?? new RecognitionSettings();
        }

        private sealed class Edge
        {
            public CurveChain Chain;
            public int A;
            public int B;
            public bool Removed;
        }

        private sealed class Loop
        {
            public RecognizedLoop Data;
            public IntPoint[] Ring;
            public int Parent = -1;
            public int Depth;
            public bool Invalid;
            public string InvalidReason;
            public RecognizedPart Part;
        }

        public List<RecognizedPart> Build(IList<CurveChain> chains)
        {
            List<RecognizedPart> records = new List<RecognizedPart>();
            List<Loop> loops = new List<Loop>();
            List<Edge> edges = new List<Edge>();
            List<Pt> nodes = new List<Pt>();

            // ---- 1. closed chains + endpoint graph for open chains ----
            NodeIndex index = new NodeIndex(Math.Max(1e-9, _settings.JoinTolerance), nodes);
            foreach (CurveChain c in chains)
            {
                if (c.Points.Count < 2) continue;

                if (c.Closed || (c.Points.Count > 2 && c.Start.DistanceTo(c.End) <= _settings.JoinTolerance))
                {
                    AddLoop(loops, records, new List<Pt>(c.Points), new List<int> { c.SourceIndex }, c.Approximated);
                    continue;
                }

                edges.Add(new Edge { Chain = c, A = index.Find(c.Start), B = index.Find(c.End) });
            }

            int[] degree = new int[nodes.Count];
            List<int>[] incident = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) incident[i] = new List<int>();
            for (int e = 0; e < edges.Count; e++)
            {
                degree[edges[e].A]++;
                degree[edges[e].B]++;
                incident[edges[e].A].Add(e);
                incident[edges[e].B].Add(e);
            }

            // ---- 2. prune dangling chains ----
            List<Edge> dangling = new List<Edge>();
            List<int> open = new List<int>();
            HashSet<int> openNodes = new HashSet<int>();

            // openNodes se gom MOI nut lo ra trong luc tia dan (boc tung lop). De biet mot nhom
            // hinh ho co phai la duong bao dinh ve kin hay khong thi can DAU HO THAT SU - tuc
            // la nut co bac 1 ngay tu dau, truoc khi tia.
            HashSet<int> freeEnds = new HashSet<int>();
            Queue<int> queue = new Queue<int>();
            for (int n = 0; n < nodes.Count; n++)
            {
                if (degree[n] == 1)
                {
                    queue.Enqueue(n);
                    freeEnds.Add(n);
                }
            }

            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                if (degree[n] != 1) continue;
                openNodes.Add(n);
                foreach (int e in incident[n])
                {
                    Edge edge = edges[e];
                    if (edge.Removed) continue;
                    edge.Removed = true;
                    dangling.Add(edge);
                    degree[edge.A]--;
                    degree[edge.B]--;
                    int other = edge.A == n ? edge.B : edge.A;
                    if (degree[other] == 1) queue.Enqueue(other);
                    break;
                }
            }

            // ---- 3. remaining components: cycles or branching ----
            bool[] visited = new bool[edges.Count];
            List<List<Edge>> branchingComponents = new List<List<Edge>>();
            List<Pt> branchPoints = new List<Pt>();
            for (int e0 = 0; e0 < edges.Count; e0++)
            {
                if (edges[e0].Removed || visited[e0]) continue;

                List<Edge> component = new List<Edge>();
                bool branching = false;
                Pt branchAt = default(Pt);
                Stack<int> stack = new Stack<int>();
                stack.Push(e0);
                visited[e0] = true;
                while (stack.Count > 0)
                {
                    Edge edge = edges[stack.Pop()];
                    component.Add(edge);
                    foreach (int n in new[] { edge.A, edge.B })
                    {
                        if (degree[n] > 2 && !branching)
                        {
                            branching = true;
                            branchAt = nodes[n];
                        }

                        foreach (int next in incident[n])
                        {
                            if (!edges[next].Removed && !visited[next])
                            {
                                visited[next] = true;
                                stack.Push(next);
                            }
                        }
                    }
                }

                if (branching)
                {
                    branchingComponents.Add(component);
                    branchPoints.Add(branchAt);
                    continue;
                }

                List<Pt> pts;
                List<int> sources;
                WalkCycle(component, incident, edges, out pts, out sources);
                AddLoop(loops, records, pts, sources, component.Exists(e => e.Chain.Approximated));
            }

            // ---- 4. containment hierarchy ----
            loops.Sort((a, b) => b.Data.Area.CompareTo(a.Data.Area));
            for (int i = 0; i < loops.Count; i++)
            {
                // j runs from the smallest larger loop to the largest: first hit is the tightest container.
                for (int j = i - 1; j >= 0; j--)
                {
                    if (!BoxContains(loops[j].Data, loops[i].Data)) continue;
                    if (GeometryMath.PointInRing(loops[i].Ring[0], loops[j].Ring) > 0)
                    {
                        loops[i].Parent = j;
                        loops[i].Depth = loops[j].Depth + 1;
                        break;
                    }
                }
            }

            // ---- 5. build parts, check crossing contours ----
            List<RecognizedPart> parts = new List<RecognizedPart>();
            foreach (Loop loop in loops)
            {
                if (loop.Depth % 2 != 0) continue;
                RecognizedPart part = new RecognizedPart { Outer = loop.Data };
                part.MinX = loop.Data.MinX;
                part.MinY = loop.Data.MinY;
                part.MaxX = loop.Data.MaxX;
                part.MaxY = loop.Data.MaxY;
                part.GeometrySources.AddRange(loop.Data.Sources);
                loop.Part = part;
                parts.Add(part);
            }

            foreach (Loop loop in loops)
            {
                if (loop.Depth % 2 == 0) continue;
                RecognizedPart owner = loops[loop.Parent].Part;
                loop.Part = owner;
                owner.Holes.Add(loop.Data);
                owner.GeometrySources.AddRange(loop.Data.Sources);
            }

            for (int i = 0; i < loops.Count; i++)
            {
                if (loops[i].Invalid) loops[i].Part.Escalate(PartStatus.InvalidGeometry, loops[i].InvalidReason);

                for (int j = i + 1; j < loops.Count; j++)
                {
                    if (!BoxOverlap(loops[i].Data, loops[j].Data)) continue;
                    IntPoint touch;
                    if (GeometryMath.RingsTouch(loops[i].Ring, loops[j].Ring, out touch))
                    {
                        // Duong bao TRUNG KHIT da duoc gop lam 1 tu truoc (xem AddLoop), nen den
                        // day chi con truong hop cham / cat that su.
                        string msg = string.Format(CultureInfo.InvariantCulture,
                            "Duong bao cat/cham duong bao khac (lo cham bien hoac 2 chi tiet chong nhau) - tai ({0:0.##}, {1:0.##})",
                            NestUnits.ToMm(touch.X), NestUnits.ToMm(touch.Y));
                        loops[i].Part.Escalate(PartStatus.InvalidGeometry, msg);
                        loops[j].Part.Escalate(PartStatus.InvalidGeometry, msg);
                    }
                }
            }

            // ---- 6. open / branching geometry ----
            foreach (List<Edge> group in GroupDangling(dangling))
            {
                AssignOpenGeometry(group, parts, records, "Duong bao HO (khong kin)", openNodes, freeEnds, nodes);
            }

            for (int k = 0; k < branchingComponents.Count; k++)
            {
                AssignOpenGeometry(branchingComponents[k], parts, records,
                    "Hinh hoc re nhanh tai " + branchPoints[k] + " (hon 2 doi tuong gap nhau 1 diem)", null, null, nodes);
            }

            records.InsertRange(0, parts);
            return records;
        }

        private void AddLoop(List<Loop> loops, List<RecognizedPart> records, List<Pt> pts, List<int> sources, bool approximated)
        {
            // Drop duplicate closing point.
            if (pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) <= _settings.JoinTolerance)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            List<IntPoint> ints = new List<IntPoint>(pts.Count);
            foreach (Pt p in pts) ints.Add(IntPoint.FromMm(p.X, p.Y));
            IntPoint[] ring = GeometryMath.CleanRing(ints);

            RecognizedLoop data = new RecognizedLoop(pts, sources, approximated);
            string invalid = null;
            if (ring.Length < 3 || data.Area < _settings.MinLoopArea)
            {
                // Zero-area loops (duplicated/overlapping lines) cannot become a part.
                RecognizedPart rec = InvalidRecord(sources, data.MinX, data.MinY, data.MaxX, data.MaxY,
                    "Duong bao dien tich ~0 (doi tuong trung/chong len nhau?)");
                records.Add(rec);
                return;
            }

            int ea, eb;
            if (!GeometryMath.IsSimpleRing(ring, out ea, out eb))
            {
                invalid = "Duong bao tu cat (self-intersecting)";
            }

            // Duong bao TRUNG KHIT voi mot duong da co (ve de len nhau hai lan) khong phai la
            // mot chi tiet / mot lo thu hai - no la CHINH cai do. Gop lam 1 va bo ban sao di:
            // giu lai thi hai duong bao se "cham nhau" o moi diem va ca chi tiet bi bao la
            // hinh hoc loi, con neu xuat ca hai thi may CNC se cat hai lan cung mot duong.
            foreach (Loop existing in loops)
            {
                if (!SameLoop(existing.Data, data)) continue;

                MergedDuplicateLoops++;
                if (FirstDuplicateAt == null)
                {
                    FirstDuplicateAt = string.Format(CultureInfo.InvariantCulture,
                        "({0:0.##}, {1:0.##})", data.MinX, data.MinY);
                }

                return;
            }

            loops.Add(new Loop { Data = data, Ring = ring, Invalid = invalid != null, InvalidReason = invalid });
        }

        private static RecognizedPart InvalidRecord(List<int> sources, double minX, double minY, double maxX, double maxY, string reason)
        {
            RecognizedPart rec = new RecognizedPart { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Include = false };
            rec.GeometrySources.AddRange(sources);
            rec.Escalate(PartStatus.InvalidGeometry, reason);
            return rec;
        }

        private void AssignOpenGeometry(
            List<Edge> group, List<RecognizedPart> parts, List<RecognizedPart> records, string reason,
            HashSet<int> openNodes, HashSet<int> freeEnds, List<Pt> nodes)
        {
            // Smallest part whose outline contains every point of the group -> marking.
            RecognizedPart owner = null;
            foreach (RecognizedPart part in parts)
            {
                if (owner != null && part.Outer.Area >= owner.Outer.Area) continue;
                bool inside = true;
                foreach (Edge e in group)
                {
                    foreach (Pt p in e.Chain.Points)
                    {
                        if (!PointInOrOn(p, part.Outer.Points)) { inside = false; break; }
                    }

                    if (!inside) break;
                }

                if (inside) owner = part;
            }

            List<int> sources = new List<int>();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Edge e in group)
            {
                if (!sources.Contains(e.Chain.SourceIndex)) sources.Add(e.Chain.SourceIndex);
                foreach (Pt p in e.Chain.Points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
            }

            if (owner != null)
            {
                foreach (int s in sources)
                {
                    if (!owner.MarkingSources.Contains(s)) owner.MarkingSources.Add(s);
                    if (!owner.GeometrySources.Contains(s)) owner.GeometrySources.Add(s);
                }

                owner.Escalate(PartStatus.Warning, string.Format(CultureInfo.InvariantCulture,
                    "Co {0} duong ho ben trong (duong chan/khac) - giu nguyen khi xuat, khong dung de ghep",
                    owner.MarkingSources.Count));
                return;
            }

            string where = string.Empty;
            if (openNodes != null)
            {
                foreach (Edge e in group)
                {
                    int n = openNodes.Contains(e.A) ? e.A : (openNodes.Contains(e.B) ? e.B : -1);
                    if (n >= 0)
                    {
                        where = " - dau ho tai " + nodes[n];
                        break;
                    }
                }
            }

            // Hinh nam ngoai MOI chi tiet. Tren ban ve san xuat that co hang chuc thu nhu the
            // (duong dan, ghi chu, net thua) - bat nguoi dung xac nhan tung cai la vo ich.
            //
            // Nhung KHONG duoc bo tat: mot duong bao dinh ve kin ma bi ho mot khe nho cung roi
            // vao day, va do moi la thu nguoi dung CAN thay de sua. Phan biet bang chinh hai
            // dau ho: gan nhau so voi kich thuoc cua no thi la duong bao ho (giu lai, bao loi);
            // xa nhau thi khong phai chi tiet (bo).
            if (freeEnds != null && !LooksLikeAlmostClosed(group, freeEnds, nodes, minX, minY, maxX, maxY))
            {
                DroppedOpenGroups++;
                return;
            }

            records.Add(InvalidRecord(sources, minX, minY, maxX, maxY, reason + where));
        }

        /// <summary>
        /// Nhom hinh ho nay co giong mot duong bao dinh ve KIN ma bi ho khe khong?
        ///
        /// Dieu kien: dung HAI dau ho, va khoang cach giua chung khong qua 5% duong cheo hop
        /// bao cua chinh nhom do. Quy tac nay khong phu thuoc don vi hay ty le ban ve: khe ho
        /// 2 mm tren chi tiet 100x50 la 1.8% (giu lai), con mot duong dan thi hai dau cach
        /// nhau gan bang chieu dai cua no (bo di).
        /// </summary>
        private static bool LooksLikeAlmostClosed(
            List<Edge> group, HashSet<int> freeEnds, List<Pt> nodes,
            double minX, double minY, double maxX, double maxY)
        {
            List<int> ends = new List<int>();
            foreach (Edge e in group)
            {
                if (freeEnds.Contains(e.A) && !ends.Contains(e.A)) ends.Add(e.A);
                if (freeEnds.Contains(e.B) && !ends.Contains(e.B)) ends.Add(e.B);
                if (ends.Count > 2) return false;
            }

            if (ends.Count != 2) return false;

            double gap = nodes[ends[0]].DistanceTo(nodes[ends[1]]);
            double w = maxX - minX, h = maxY - minY;
            double diagonal = Math.Sqrt(w * w + h * h);

            return diagonal > 0.0 && gap <= diagonal * 0.05;
        }

        private static bool PointInOrOn(Pt p, List<Pt> ring)
        {
            return GeometryMath.PointInRing(IntPoint.FromMm(p.X, p.Y), ToInts(ring)) >= 0;
        }

        private static List<IntPoint> ToInts(List<Pt> ring)
        {
            List<IntPoint> r = new List<IntPoint>(ring.Count);
            foreach (Pt p in ring) r.Add(IntPoint.FromMm(p.X, p.Y));
            return r;
        }

        private static List<List<Edge>> GroupDangling(List<Edge> dangling)
        {
            // Union chains that share a node.
            List<List<Edge>> groups = new List<List<Edge>>();
            Dictionary<int, List<Edge>> byNode = new Dictionary<int, List<Edge>>();
            foreach (Edge e in dangling)
            {
                List<Edge> ga, gb;
                byNode.TryGetValue(e.A, out ga);
                byNode.TryGetValue(e.B, out gb);
                List<Edge> g = ga ?? gb;
                if (g == null)
                {
                    g = new List<Edge>();
                    groups.Add(g);
                }

                g.Add(e);
                if (ga != null && gb != null && ga != gb)
                {
                    g.AddRange(gb);
                    groups.Remove(gb);
                    foreach (Edge moved in gb)
                    {
                        byNode[moved.A] = g;
                        byNode[moved.B] = g;
                    }
                }

                byNode[e.A] = g;
                byNode[e.B] = g;
            }

            return groups;
        }

        private static void WalkCycle(List<Edge> component, List<int>[] incident, List<Edge> edges, out List<Pt> pts, out List<int> sources)
        {
            pts = new List<Pt>();
            sources = new List<int>();
            HashSet<Edge> used = new HashSet<Edge>();
            Edge current = component[0];
            int node = current.A;

            while (current != null && !used.Contains(current))
            {
                used.Add(current);
                if (!sources.Contains(current.Chain.SourceIndex)) sources.Add(current.Chain.SourceIndex);

                bool forward = current.A == node;
                List<Pt> cp = current.Chain.Points;
                int start = pts.Count == 0 ? 0 : 1;
                if (forward)
                {
                    for (int i = start; i < cp.Count; i++) pts.Add(cp[i]);
                }
                else
                {
                    for (int i = cp.Count - 1 - start; i >= 0; i--) pts.Add(cp[i]);
                }

                node = forward ? current.B : current.A;
                Edge next = null;
                foreach (int e in incident[node])
                {
                    if (!edges[e].Removed && !used.Contains(edges[e]))
                    {
                        next = edges[e];
                        break;
                    }
                }

                current = next;
            }
        }

        private static bool BoxContains(RecognizedLoop outer, RecognizedLoop inner)
        {
            return outer.MinX <= inner.MinX && outer.MinY <= inner.MinY && outer.MaxX >= inner.MaxX && outer.MaxY >= inner.MaxY;
        }

        /// <summary>
        /// Hai duong bao co phai la MOT khong (ve trung len nhau): cung hop bao va cung dien
        /// tich. Chi dung de CHON CAU CHU cho de hieu - ket luan hop le / khong hop le khong
        /// he thay doi.
        /// </summary>
        private static bool SameLoop(RecognizedLoop a, RecognizedLoop b)
        {
            const double boxTolerance = 0.01;      // mm

            // So dinh phai bang nhau: hai duong bao KHAC nhau ma trung ca hop bao lan dien tich
            // la chuyen hiem, nhung gop nham hai lo khac nhau thi mat hinh - nen doi them dieu
            // kien nay truoc khi dam gop.
            if (a.Points.Count != b.Points.Count) return false;

            if (Math.Abs(a.MinX - b.MinX) > boxTolerance) return false;
            if (Math.Abs(a.MinY - b.MinY) > boxTolerance) return false;
            if (Math.Abs(a.MaxX - b.MaxX) > boxTolerance) return false;
            if (Math.Abs(a.MaxY - b.MaxY) > boxTolerance) return false;

            double biggest = Math.Max(a.Area, b.Area);
            if (biggest <= 0.0) return true;

            return Math.Abs(a.Area - b.Area) / biggest <= 0.001;
        }

        private static bool BoxOverlap(RecognizedLoop a, RecognizedLoop b)
        {
            return a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinY <= b.MaxY && b.MinY <= a.MaxY;
        }

        /// <summary>Grid hash joining endpoints within the tolerance.</summary>
        private sealed class NodeIndex
        {
            private readonly double _tol;
            private readonly List<Pt> _nodes;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

            public NodeIndex(double tol, List<Pt> nodes)
            {
                _tol = tol;
                _nodes = nodes;
            }

            private static long Key(long cx, long cy)
            {
                return (cx * 73856093L) ^ (cy * 19349663L);
            }

            public int Find(Pt p)
            {
                long cx = (long)Math.Floor(p.X / _tol), cy = (long)Math.Floor(p.Y / _tol);
                int best = -1;
                double bestD = double.MaxValue;
                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dy = -1; dy <= 1; dy++)
                    {
                        List<int> cell;
                        if (!_cells.TryGetValue(Key(cx + dx, cy + dy), out cell)) continue;
                        foreach (int n in cell)
                        {
                            double d = _nodes[n].DistanceTo(p);
                            if (d <= _tol && d < bestD)
                            {
                                bestD = d;
                                best = n;
                            }
                        }
                    }
                }

                if (best >= 0) return best;

                _nodes.Add(p);
                int id = _nodes.Count - 1;
                List<int> own;
                long k = Key(cx, cy);
                if (!_cells.TryGetValue(k, out own))
                {
                    own = new List<int>();
                    _cells[k] = own;
                }

                own.Add(id);
                return id;
            }
        }
    }
}
