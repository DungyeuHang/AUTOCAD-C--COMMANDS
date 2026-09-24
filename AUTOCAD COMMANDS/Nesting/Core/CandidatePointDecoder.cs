using System;
using System.Collections.Generic;
using System.Threading;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// V1 decoder: candidate-point placement + real polygon collision + compaction.
    /// For each part (in the given order) it tries the already open sheets first (first fit),
    /// then opens a new sheet. On a sheet it:
    ///   1. builds candidate positions for the part's bounding-box corner from the sheet
    ///      boundary, the bounding boxes of placed parts and (sampled) vertices of placed parts,
    ///   2. for every orientation sweeps the candidates, keeps the first valid Y for each X,
    ///   3. compacts that position (slide left, slide down, repeat) with collision checks,
    ///   4. keeps the best position according to the <see cref="PlacementPolicy"/>.
    /// Not NFP, not a metaheuristic.
    /// </summary>
    public sealed class CandidatePointDecoder : IPlacementDecoder
    {
        /// <summary>Smallest compaction step (0.05 mm).</summary>
        private const long MinSlideStep = 50;

        /// <summary>First compaction step (25 mm), halved on every blocked move.</summary>
        private const long InitialSlideStep = 25000;

        /// <summary>How many of the best uncompacted candidates are compacted per part.</summary>
        private const int CompactedCandidates = 6;

        /// <summary>Score quantum for the LeftBottom policy (1 mm) so that tiny compaction noise does not dominate.</summary>
        private const long ScoreQuantum = 1000;

        public DecodedLayout Decode(IList<PartInstance> order, MaterialJob job, PlacementPolicy policy, CancellationToken cancellation)
        {
            DecodedLayout layout = new DecodedLayout();
            ICollisionModel collision = job.Collision;

            foreach (PartInstance instance in order)
            {
                if (cancellation.IsCancellationRequested)
                {
                    layout.Cancelled = true;
                    layout.Unplaced.Add(new UnplacedPart(instance.Id, instance.PartGroupId, "Da huy boi nguoi dung."));
                    continue;
                }

                List<PreparedShape> orientations;
                if (!job.Orientations.TryGetValue(instance.PartGroupId, out orientations) || orientations.Count == 0)
                {
                    string reason;
                    job.UnfitReasons.TryGetValue(instance.PartGroupId, out reason);
                    layout.Unplaced.Add(new UnplacedPart(instance.Id, instance.PartGroupId,
                        reason ?? "Khong co huong xoay nao vua kho phoi."));
                    continue;
                }

                bool placed = false;
                foreach (DecodedSheet sheet in layout.Sheets)
                {
                    PlacedItem item = TryPlace(instance, orientations, sheet, job, collision, policy);
                    if (item != null)
                    {
                        AddItem(sheet, item);
                        placed = true;
                        break;
                    }
                }

                if (placed) continue;

                DecodedSheet fresh = new DecodedSheet();
                PlacedItem first = TryPlace(instance, orientations, fresh, job, collision, policy);
                if (first == null)
                {
                    layout.Unplaced.Add(new UnplacedPart(instance.Id, instance.PartGroupId,
                        "Khong tim duoc vi tri hop le ngay ca tren to phoi trong."));
                    continue;
                }

                AddItem(fresh, first);
                layout.Sheets.Add(fresh);
            }

            return layout;
        }

        private static void AddItem(DecodedSheet sheet, PlacedItem item)
        {
            sheet.Items.Add(item);
            if (item.Placed.Bounds.MaxX > sheet.MaxX) sheet.MaxX = item.Placed.Bounds.MaxX;
            if (item.Placed.Bounds.MaxY > sheet.MaxY) sheet.MaxY = item.Placed.Bounds.MaxY;
        }

        private struct Score
        {
            public long K1;
            public long K2;
            public long K3;

            public bool BetterThan(Score o)
            {
                if (K1 != o.K1) return K1 < o.K1;
                if (K2 != o.K2) return K2 < o.K2;
                return K3 < o.K3;
            }
        }

        private static Score MakeScore(PreparedShape s, long tx, long ty, PlacementPolicy policy)
        {
            long minX = s.Bounds.MinX + tx, minY = s.Bounds.MinY + ty;
            long maxX = s.Bounds.MaxX + tx, maxY = s.Bounds.MaxY + ty;
            Score score;
            if (policy == PlacementPolicy.MinLength)
            {
                score.K1 = maxX / ScoreQuantum;
                score.K2 = maxY / ScoreQuantum;
                score.K3 = minX;
            }
            else
            {
                score.K1 = minX / ScoreQuantum;
                score.K2 = minY / ScoreQuantum;
                score.K3 = maxY;
            }

            return score;
        }

        private PlacedItem TryPlace(
            PartInstance instance,
            List<PreparedShape> orientations,
            DecodedSheet sheet,
            MaterialJob job,
            ICollisionModel collision,
            PlacementPolicy policy)
        {
            SheetSpec spec = job.Sheet;
            ClearanceRules rules = collision.Rules;
            long sheetL = spec.LengthUnits, sheetW = spec.WidthUnits;
            PartShape part = instance.Group.Shape;
            long inset = rules.BoundaryInset(part);

            // Pair clearance per placed item (Gap + both approximation tolerances).
            int n = sheet.Items.Count;
            long[] clearance = new long[n];
            for (int i = 0; i < n; i++) clearance[i] = rules.PartClearance(part.ToleranceUnits, sheet.Items[i].Placed.ToleranceUnits);

            // Valid uncompacted positions: the first valid Y for every candidate X and
            // orientation. NO pruning of X candidates (see audit in the class summary).
            List<Candidate> candidates = new List<Candidate>();
            List<PlacedItem> column = new List<PlacedItem>(n);
            List<long> xs = new List<long>();
            List<long> ys = new List<long>();

            foreach (PreparedShape shape in orientations)
            {
                long w = shape.Bounds.Width, h = shape.Bounds.Height;
                long maxXStart = sheetL - inset - w;
                long maxYStart = sheetW - inset - h;
                if (maxXStart < inset || maxYStart < inset) continue;

                // X candidates for the bounding-box left edge: sheet boundary, beside / aligned
                // with placed parts, reflex (concave) corners, and - only when part-in-part is
                // allowed - holes big enough for this part.
                xs.Clear();
                xs.Add(inset);
                xs.Add(maxXStart);
                for (int i = 0; i < n; i++)
                {
                    PlacedItem p = sheet.Items[i];
                    long c = clearance[i];
                    LongRect b = p.Placed.Bounds;
                    xs.Add(b.MaxX + c);
                    xs.Add(b.MinX);
                    xs.Add(b.MinX - c - w);
                    foreach (IntPoint v in p.ReflexVertices)
                    {
                        xs.Add(v.X + c);
                        xs.Add(v.X - c - w);
                    }

                    if (rules.AllowPartInsideHole)
                    {
                        foreach (LongRect hb in p.HoleBounds)
                        {
                            if (HoleCanHold(hb, w, h, c)) xs.Add(hb.MinX + c);
                        }
                    }
                }

                SortUniqueInRange(xs, inset, maxXStart);

                foreach (long x in xs)
                {
                    // Only parts overlapping this column can collide (exact bbox argument).
                    column.Clear();
                    ys.Clear();
                    ys.Add(inset);
                    ys.Add(maxYStart);
                    for (int i = 0; i < n; i++)
                    {
                        PlacedItem p = sheet.Items[i];
                        long c = clearance[i];
                        LongRect b = p.Placed.Bounds;
                        if (!(b.MinX < x + w + c && x < b.MaxX + c)) continue;

                        column.Add(p);
                        ys.Add(b.MaxY + c);
                        ys.Add(b.MinY);
                        ys.Add(b.MinY - c - h);
                        foreach (IntPoint v in p.ReflexVertices)
                        {
                            ys.Add(v.Y + c);
                            ys.Add(v.Y - c - h);
                        }

                        if (rules.AllowPartInsideHole)
                        {
                            foreach (LongRect hb in p.HoleBounds)
                            {
                                if (HoleCanHold(hb, w, h, c)) ys.Add(hb.MinY + c);
                            }
                        }
                    }

                    SortUniqueInRange(ys, inset, maxYStart);

                    foreach (long y in ys)
                    {
                        long tx = x - shape.Bounds.MinX;
                        long ty = y - shape.Bounds.MinY;
                        if (!IsValid(shape, tx, ty, column, spec, collision)) continue;

                        candidates.Add(new Candidate { Shape = shape, Tx = tx, Ty = ty, Score = MakeScore(shape, tx, ty, policy), Order = candidates.Count });

                        // The lowest valid Y is the one that matters for this X.
                        break;
                    }
                }
            }

            if (candidates.Count == 0) return null;

            candidates.Sort((a, b) => a.Score.BetterThan(b.Score) ? -1 : (b.Score.BetterThan(a.Score) ? 1 : a.Order.CompareTo(b.Order)));

            Candidate chosen = null;
            for (int i = 0; i < candidates.Count && i < CompactedCandidates; i++)
            {
                Candidate c = candidates[i];
                long tx = c.Tx, ty = c.Ty;
                Compact(c.Shape, ref tx, ref ty, sheet.Items, spec, collision);
                c.Tx = tx;
                c.Ty = ty;
                c.Score = MakeScore(c.Shape, tx, ty, policy);
                if (chosen == null || c.Score.BetterThan(chosen.Score)) chosen = c;
            }

            return new PlacedItem(instance, chosen.Shape, chosen.Tx, chosen.Ty, collision.Place(chosen.Shape, chosen.Tx, chosen.Ty));
        }

        private sealed class Candidate
        {
            public PreparedShape Shape;
            public long Tx;
            public long Ty;
            public Score Score;
            public int Order;
        }

        private static bool HoleCanHold(LongRect hole, long w, long h, long clearance)
        {
            return hole.Width >= w + 2 * clearance && hole.Height >= h + 2 * clearance;
        }

        private static void SortUniqueInRange(List<long> values, long min, long max)
        {
            values.RemoveAll(v => v < min || v > max);
            values.Sort();
            int k = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (k == 0 || values[i] != values[k - 1]) values[k++] = values[i];
            }

            values.RemoveRange(k, values.Count - k);
        }

        private static bool IsValid(PreparedShape shape, long tx, long ty, List<PlacedItem> items, SheetSpec spec, ICollisionModel collision)
        {
            if (!collision.FitsInsideSheet(shape, tx, ty, spec)) return false;
            foreach (PlacedItem p in items)
            {
                if (collision.Collides(shape, tx, ty, p.Placed)) return false;
            }

            return true;
        }

        /// <summary>Slides left then down (repeatedly) as long as the position stays valid.</summary>
        private static void Compact(PreparedShape shape, ref long tx, ref long ty, List<PlacedItem> items, SheetSpec spec, ICollisionModel collision)
        {
            for (int round = 0; round < 6; round++)
            {
                long movedX = Slide(shape, ref tx, ref ty, -1, 0, items, spec, collision);
                long movedY = Slide(shape, ref tx, ref ty, 0, -1, items, spec, collision);
                if (movedX < MinSlideStep && movedY < MinSlideStep) break;
            }
        }

        private static long Slide(PreparedShape shape, ref long tx, ref long ty, int dx, int dy, List<PlacedItem> items, SheetSpec spec, ICollisionModel collision)
        {
            long inset = collision.Rules.BoundaryInset(shape.Group.Shape);
            long room = dx != 0 ? shape.Bounds.MinX + tx - inset : shape.Bounds.MinY + ty - inset;
            if (room <= 0) return 0;

            // Straight to the boundary when nothing is in the way.
            if (IsValid(shape, tx + dx * room, ty + dy * room, items, spec, collision))
            {
                tx += dx * room;
                ty += dy * room;
                return room;
            }

            long moved = 0;
            long step = Math.Min(room, InitialSlideStep);
            while (step >= MinSlideStep)
            {
                if (moved + step <= room &&
                    IsValid(shape, tx + dx * (moved + step), ty + dy * (moved + step), items, spec, collision))
                {
                    moved += step;
                }
                else
                {
                    step /= 2;
                }
            }

            tx += dx * moved;
            ty += dy * moved;
            return moved;
        }
    }
}
