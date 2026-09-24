using System;
using System.Collections.Generic;
using System.Globalization;
using AUTOCAD_COMMANDS.Nesting.Core;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    /// <summary>
    /// Recognition pipeline: contours -> parts -> metadata association -> numbering.
    /// Also converts accepted records into core <see cref="PartGroup"/>s.
    /// </summary>
    public sealed class PartRecognizer
    {
        private readonly RecognitionSettings _settings;

        public PartRecognizer(RecognitionSettings settings)
        {
            _settings = settings ?? new RecognitionSettings();
        }

        public RecognitionResult Recognize(IList<CurveChain> chains, IList<TextItem> texts)
        {
            RecognitionResult result = new RecognitionResult();
            List<RecognizedPart> records = new ContourLoopBuilder(_settings).Build(chains);

            // Reading order: top row first, then left to right (rows = 1/2 of the median height).
            List<RecognizedPart> valid = records.FindAll(r => r.Outer != null);
            double rowBand = MedianHeight(valid) * 0.5;
            valid.Sort((a, b) =>
            {
                double ya = Math.Round(a.MaxY / Math.Max(rowBand, 1.0)), yb = Math.Round(b.MaxY / Math.Max(rowBand, 1.0));
                int c = yb.CompareTo(ya);
                return c != 0 ? c : a.MinX.CompareTo(b.MinX);
            });

            int n = 0;
            foreach (RecognizedPart p in valid)
            {
                p.Index = ++n;
                p.Name = "P" + n.ToString(CultureInfo.InvariantCulture);
            }

            int bad = 0;
            foreach (RecognizedPart r in records)
            {
                if (r.Outer != null) continue;
                r.Index = ++n;
                r.Name = "LOI" + (++bad).ToString(CultureInfo.InvariantCulture);
            }

            // One source entity (typically a block) feeding several records cannot be output
            // per part without duplicating geometry -> report instead of guessing.
            Dictionary<int, List<RecognizedPart>> bySource = new Dictionary<int, List<RecognizedPart>>();
            foreach (RecognizedPart r in records)
            {
                foreach (int s in r.GeometrySources)
                {
                    List<RecognizedPart> owners;
                    if (!bySource.TryGetValue(s, out owners))
                    {
                        owners = new List<RecognizedPart>();
                        bySource[s] = owners;
                    }

                    if (!owners.Contains(r)) owners.Add(r);
                }
            }

            foreach (List<RecognizedPart> owners in bySource.Values)
            {
                if (owners.Count < 2) continue;
                foreach (RecognizedPart r in owners)
                {
                    r.Escalate(PartStatus.InvalidGeometry,
                        "Mot doi tuong (vd. block) chua hinh cua nhieu chi tiet - hay EXPLODE/tach block truoc");
                }
            }

            new TextPartAssociator(_settings).Associate(records, texts ?? new List<TextItem>(), result.GlobalWarnings);

            foreach (RecognizedPart r in records)
            {
                if (r.Status == PartStatus.InvalidGeometry)
                {
                    r.Include = false;
                    if (r.Material == null) r.Material = _settings.Metadata.DefaultMaterial;
                }
            }

            records.Sort((a, b) => a.Index.CompareTo(b.Index));
            result.Parts.AddRange(records);
            return result;
        }

        /// <summary>
        /// Attaches geometry from marking layers (bend lines, engraving) to the smallest part
        /// whose outline contains all its sample points. Returns false when no part contains it.
        /// </summary>
        public static bool AttachMarking(IList<RecognizedPart> records, int sourceIndex, IList<Pt> samples)
        {
            RecognizedPart owner = null;
            foreach (RecognizedPart part in records)
            {
                if (part.Outer == null) continue;
                if (owner != null && part.Outer.Area >= owner.Outer.Area) continue;

                List<IntPoint> ring = new List<IntPoint>(part.Outer.Points.Count);
                foreach (Pt q in part.Outer.Points) ring.Add(IntPoint.FromMm(q.X, q.Y));

                bool inside = samples.Count > 0;
                foreach (Pt p in samples)
                {
                    if (GeometryMath.PointInRing(IntPoint.FromMm(p.X, p.Y), ring) < 0)
                    {
                        inside = false;
                        break;
                    }
                }

                if (inside) owner = part;
            }

            if (owner == null) return false;
            if (!owner.MarkingSources.Contains(sourceIndex)) owner.MarkingSources.Add(sourceIndex);
            if (!owner.GeometrySources.Contains(sourceIndex)) owner.GeometrySources.Add(sourceIndex);
            return true;
        }

        private static double MedianHeight(List<RecognizedPart> parts)
        {
            if (parts.Count == 0) return 1.0;
            List<double> h = new List<double>();
            foreach (RecognizedPart p in parts) h.Add(p.Height);
            h.Sort();
            return h[h.Count / 2];
        }

        /// <summary>
        /// Local frame origin of a part = its bounding-box lower-left corner. The DWG writer uses
        /// the same point as the block base point, so core transforms map 1:1 to BlockReferences.
        /// </summary>
        public static Pt LocalOrigin(RecognizedPart part)
        {
            return new Pt(part.Outer.MinX, part.Outer.MinY);
        }

        /// <param name="arcToleranceMm">Chord tolerance used when discretising curves.</param>
        /// <remarks>
        /// Only the OUTER ring can make the real part bigger than the polygon (convex arc chords).
        /// Concave arcs and hole arcs are conservative (the polygon is bigger / the real hole is
        /// bigger), so a part whose outer ring is exact straight segments gets tolerance 0.
        /// </remarks>
        public static PartShape ToShape(RecognizedPart part, double arcToleranceMm)
        {
            Pt o = LocalOrigin(part);
            List<IList<IntPoint>> holes = new List<IList<IntPoint>>();
            foreach (RecognizedLoop h in part.Holes) holes.Add(ToLocal(h.Points, o));
            double tol = part.Outer.Approximated ? Math.Max(0.0, arcToleranceMm) : 0.0;
            return new PartShape(PolyShape.Create(ToLocal(part.Outer.Points, o), holes), tol);
        }

        private static List<IntPoint> ToLocal(List<Pt> pts, Pt o)
        {
            List<IntPoint> r = new List<IntPoint>(pts.Count);
            foreach (Pt p in pts) r.Add(IntPoint.FromMm(p.X - o.X, p.Y - o.Y));
            return r;
        }

        /// <summary>
        /// Records the user accepted, as core part groups. Throws when a critical record is still
        /// unresolved - the review UI must prevent that, this is the last safety net.
        /// </summary>
        public static List<PartGroup> ToPartGroups(IEnumerable<RecognizedPart> records, double arcToleranceMm)
        {
            List<PartGroup> groups = new List<PartGroup>();
            foreach (RecognizedPart r in records)
            {
                if (!r.Include) continue;
                if (!r.IsNestable)
                {
                    throw new InvalidOperationException("Ban ghi " + r.Name + " co hinh hoc loi, khong the dua vao ghep.");
                }

                if (r.Status == PartStatus.Ambiguous && !r.Confirmed)
                {
                    throw new InvalidOperationException("Ban ghi " + r.Name + " con MO HO (chua xac nhan).");
                }

                if (r.Quantity < 1) throw new InvalidOperationException("Ban ghi " + r.Name + " co SL < 1.");
                if (string.IsNullOrWhiteSpace(r.Material)) throw new InvalidOperationException("Ban ghi " + r.Name + " thieu vat lieu.");

                PartGroup g = new PartGroup(
                    "G" + r.Index.ToString("000", CultureInfo.InvariantCulture),
                    ToShape(r, arcToleranceMm),
                    r.Quantity,
                    SimpleNestingEngine.NormalizeMaterial(r.Material));
                g.Name = r.Name;
                g.SourceReference = r;
                groups.Add(g);
            }

            return groups;
        }
    }
}
