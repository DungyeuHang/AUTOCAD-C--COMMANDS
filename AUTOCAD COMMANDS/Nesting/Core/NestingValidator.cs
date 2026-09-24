using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Independent, authoritative check of a nesting result. It does NOT reuse the decoder's
    /// collision model: it re-applies every placement to the original part polygon and measures
    /// production requirements directly (brute-force edge distances, exact integer comparisons):
    ///   - transform is finite and allowed (rotation in the allowed set, mirror only if enabled),
    ///   - part lies inside the sheet with at least EdgeMargin (+ its tolerance) to every edge,
    ///   - no two parts on a sheet overlap / cross / contain each other,
    ///   - no part lies inside a closed hole of another unless AllowPartInsideHole,
    ///   - every pair keeps at least Gap (+ both tolerances),
    ///   - every requested instance is placed or reported unplaced exactly once, nothing extra,
    ///   - sheet material equals part material.
    /// There is no slack: 0.001 mm too close is a failure.
    /// </summary>
    public sealed class NestingValidator : INestingValidator
    {
        /// <summary>Relative guard against floating-point noise in squared distances (~1e-10 mm).</summary>
        private const double RelativeEpsilon = 1e-12;

        public ValidationResult Validate(NestingRequest request, NestingResult result)
        {
            ValidationResult vr = new ValidationResult();
            CultureInfo ci = CultureInfo.InvariantCulture;
            NestingSettings settings = request.Settings ?? new NestingSettings();
            ClearanceRules rules = new ClearanceRules(settings);

            Dictionary<string, PartGroup> groups = new Dictionary<string, PartGroup>(StringComparer.Ordinal);
            foreach (PartGroup g in request.Groups) groups[g.Id] = g;

            CheckQuantities(request, result, groups, vr);

            double minPart = double.PositiveInfinity, minEdge = double.PositiveInfinity;

            foreach (SheetResult sheet in result.Sheets)
            {
                List<KeyValuePair<Placement, PolyShape>> shapes = new List<KeyValuePair<Placement, PolyShape>>();
                List<PartGroup> owners = new List<PartGroup>();
                long L = sheet.Sheet.LengthUnits, W = sheet.Sheet.WidthUnits;

                foreach (Placement p in sheet.Placements)
                {
                    PartGroup g;
                    if (p.PartGroupId == null || !groups.TryGetValue(p.PartGroupId, out g))
                    {
                        vr.Issues.Add(new ValidationIssue(ValidationIssueKind.UnknownPart,
                            "Placement " + p.InstanceId + " tham chieu chi tiet khong ton tai."));
                        continue;
                    }

                    if (!string.Equals(SimpleNestingEngine.NormalizeMaterial(g.Material),
                                       SimpleNestingEngine.NormalizeMaterial(sheet.Material), StringComparison.Ordinal))
                    {
                        vr.Issues.Add(new ValidationIssue(ValidationIssueKind.MaterialMismatch, string.Format(ci,
                            "{0} ({1}) nam tren to phoi vat lieu {2}.", p.InstanceId, g.Material, sheet.Material)));
                    }

                    if (p.SheetIndex != sheet.Index)
                    {
                        vr.Issues.Add(new ValidationIssue(ValidationIssueKind.InvalidTransform,
                            p.InstanceId + ": SheetIndex khong khop to phoi chua no."));
                    }

                    string transformError = CheckTransform(p, settings);
                    if (transformError != null)
                    {
                        vr.Issues.Add(new ValidationIssue(ValidationIssueKind.InvalidTransform, p.InstanceId + ": " + transformError));
                        continue;
                    }

                    PolyShape world = g.Shape.Polygon.Transform(p.Orientation, p.TranslationX, p.TranslationY);
                    shapes.Add(new KeyValuePair<Placement, PolyShape>(p, world));
                    owners.Add(g);

                    // Sheet containment + edge margin: the polygon's vertices bound it exactly.
                    LongRect b = world.Bounds;
                    long edge = Math.Min(Math.Min(b.MinX, b.MinY), Math.Min(L - b.MaxX, W - b.MaxY));
                    long requiredEdge = rules.BoundaryInset(g.Shape);
                    minEdge = Math.Min(minEdge, edge);
                    if (edge < 0)
                    {
                        vr.Issues.Add(new ValidationIssue(ValidationIssueKind.OutsideSheet, string.Format(ci,
                            "{0} nam ngoai to phoi {1} (#{2}).", p.InstanceId, sheet.Sheet.Name, sheet.NumberInMaterial)));
                    }
                    else if (edge < requiredEdge)
                    {
                        vr.Issues.Add(new ValidationIssue(ValidationIssueKind.EdgeMargin, string.Format(ci,
                            "{0}: cach mep to {1:0.###} mm < yeu cau {2:0.###} mm.",
                            p.InstanceId, NestUnits.ToMm(edge), NestUnits.ToMm(requiredEdge))));
                    }
                }

                for (int i = 0; i < shapes.Count; i++)
                {
                    for (int j = i + 1; j < shapes.Count; j++)
                    {
                        PolyShape a = shapes[i].Value, c = shapes[j].Value;
                        long required = rules.PartClearance(owners[i].Shape, owners[j].Shape);
                        if (!a.Bounds.Overlaps(c.Bounds, required + 1)) continue;

                        string ia = shapes[i].Key.InstanceId, ib = shapes[j].Key.InstanceId;
                        if (Overlap(a, c))
                        {
                            minPart = 0;
                            vr.Issues.Add(new ValidationIssue(ValidationIssueKind.Overlap, string.Format(ci,
                                "{0} va {1} chong len nhau (to {2} #{3}).", ia, ib, sheet.Material, sheet.NumberInMaterial)));
                            continue;
                        }

                        if (!settings.AllowPartInsideHole && (InsideHole(a, c) || InsideHole(c, a)))
                        {
                            vr.Issues.Add(new ValidationIssue(ValidationIssueKind.PartInsideHole, string.Format(ci,
                                "{0} va {1}: mot chi tiet nam trong LO KIN cua chi tiet kia (cai dat khong cho phep).", ia, ib)));
                        }

                        double d2 = MinDistanceSquared(a, c);
                        minPart = Math.Min(minPart, Math.Sqrt(d2));
                        double req2 = (double)required * required;
                        if (d2 < req2 * (1.0 - RelativeEpsilon))
                        {
                            vr.Issues.Add(new ValidationIssue(ValidationIssueKind.InsufficientGap, string.Format(ci,
                                "{0} va {1}: khe {2:0.###} mm < yeu cau {3:0.###} mm (to {4} #{5}).",
                                ia, ib, NestUnits.ToMm(Math.Sqrt(d2)), NestUnits.ToMm(required), sheet.Material, sheet.NumberInMaterial)));
                        }
                    }
                }
            }

            if (!double.IsInfinity(minPart)) vr.MinPartDistanceMm = NestUnits.ToMm(minPart);
            if (!double.IsInfinity(minEdge)) vr.MinEdgeDistanceMm = NestUnits.ToMm(minEdge);
            return vr;
        }

        /// <summary>
        /// Every instance id must be one of the requested "group#1..group#N", appear exactly once
        /// (placed or unplaced), and every group must be fully accounted for.
        /// </summary>
        private static void CheckQuantities(NestingRequest request, NestingResult result, Dictionary<string, PartGroup> groups, ValidationResult vr)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (PartGroup g in request.Groups)
            {
                for (int k = 1; k <= g.Quantity; k++) expected.Add(g.Id + "#" + k.ToString(ci));
            }

            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> perGroup = new Dictionary<string, int>(StringComparer.Ordinal);
            Action<string, string> count = (instanceId, groupId) =>
            {
                string id = instanceId ?? string.Empty;
                int n;
                seen.TryGetValue(id, out n);
                seen[id] = n + 1;
                if (n == 1)
                {
                    vr.Issues.Add(new ValidationIssue(ValidationIssueKind.DuplicatePlacement,
                        "Chi tiet " + id + " xuat hien nhieu lan."));
                }

                if (n == 0 && !expected.Contains(id))
                {
                    vr.Issues.Add(new ValidationIssue(ValidationIssueKind.ExtraPlacement,
                        "Chi tiet " + id + " khong nam trong so luong yeu cau (thua)."));
                }

                if (groupId != null && id.Length > 0 && !id.StartsWith(groupId + "#", StringComparison.Ordinal))
                {
                    vr.Issues.Add(new ValidationIssue(ValidationIssueKind.ExtraPlacement,
                        "Chi tiet " + id + " khong thuoc nhom " + groupId + "."));
                }

                int m;
                perGroup.TryGetValue(groupId ?? string.Empty, out m);
                perGroup[groupId ?? string.Empty] = m + 1;
            };

            foreach (Placement p in result.Placements) count(p.InstanceId, p.PartGroupId);
            foreach (UnplacedPart u in result.Unplaced) count(u.InstanceId, u.PartGroupId);

            foreach (PartGroup g in request.Groups)
            {
                int n;
                perGroup.TryGetValue(g.Id, out n);
                if (n != g.Quantity)
                {
                    vr.Issues.Add(new ValidationIssue(ValidationIssueKind.QuantityMismatch, string.Format(ci,
                        "Chi tiet {0}: yeu cau {1}, ket qua co {2} (da xep + chua xep).", g.Name, g.Quantity, n)));
                }
            }

            foreach (string id in expected)
            {
                if (!seen.ContainsKey(id))
                {
                    vr.Issues.Add(new ValidationIssue(ValidationIssueKind.QuantityMismatch,
                        "Chi tiet " + id + " bi THIEU (khong co trong ket qua)."));
                }
            }
        }

        private static string CheckTransform(Placement p, NestingSettings settings)
        {
            if (double.IsNaN(p.RotationDeg) || double.IsInfinity(p.RotationDeg)) return "goc xoay khong hop le.";
            if (p.Mirror && !settings.AllowMirror) return "bi lat guong trong khi cai dat KHONG cho phep lat guong.";

            List<double> allowed = settings.AllowedRotations != null && settings.AllowedRotations.Count > 0
                ? settings.AllowedRotations
                : new List<double> { 0.0 };

            OrientationTransform actual = new OrientationTransform(p.RotationDeg, false);
            foreach (double r in allowed)
            {
                if (Math.Abs(new OrientationTransform(r, false).RotationDeg - actual.RotationDeg) < 1e-6) return null;
            }

            return string.Format(CultureInfo.InvariantCulture, "goc xoay {0:0.###} khong nam trong danh sach cho phep.", p.RotationDeg);
        }

        private static IEnumerable<IntPoint[]> Rings(PolyShape s)
        {
            yield return s.Outer;
            foreach (IntPoint[] h in s.Holes) yield return h;
        }

        private static bool Overlap(PolyShape a, PolyShape b)
        {
            foreach (IntPoint[] ra in Rings(a))
            {
                foreach (IntPoint[] rb in Rings(b))
                {
                    if (GeometryMath.RingsTouch(ra, rb)) return true;
                }
            }

            // No boundary contact: overlap only if one lies inside the other's material.
            return GeometryMath.PointInMaterial(a.Outer[0], b) || GeometryMath.PointInMaterial(b.Outer[0], a);
        }

        /// <summary>With no boundary contact: is <paramref name="inner"/> inside a closed hole of <paramref name="outer"/>?</summary>
        private static bool InsideHole(PolyShape inner, PolyShape outer)
        {
            foreach (IntPoint[] hole in outer.Holes)
            {
                if (GeometryMath.PointInRing(inner.Outer[0], hole) > 0) return true;
            }

            return false;
        }

        private static double MinDistanceSquared(PolyShape a, PolyShape b)
        {
            double best = double.PositiveInfinity;
            foreach (IntPoint[] ra in Rings(a))
            {
                foreach (IntPoint[] rb in Rings(b))
                {
                    for (int i = 0; i < ra.Length; i++)
                    {
                        IntPoint p = ra[i], q = ra[(i + 1) % ra.Length];
                        for (int j = 0; j < rb.Length; j++)
                        {
                            double d = GeometryMath.SegmentDistanceSquared(p, q, rb[j], rb[(j + 1) % rb.Length]);
                            if (d < best) best = d;
                        }
                    }
                }
            }

            return best;
        }
    }
}
