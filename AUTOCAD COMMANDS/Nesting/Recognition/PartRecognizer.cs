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
            ContourLoopBuilder builder = new ContourLoopBuilder(_settings);
            List<RecognizedPart> records = builder.Build(chains);

            if (builder.MergedDuplicateLoops > 0)
            {
                result.GlobalWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} duong bao ve TRUNG KHIT len nhau - da gop lam 1 (vi du tai {1}). Nen xoa bot trong ban ve goc.",
                    builder.MergedDuplicateLoops, builder.FirstDuplicateAt ?? "-"));
            }

            if (builder.DroppedOpenGroups > 0)
            {
                result.GlobalWarnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} nhom hinh HO nam ngoai moi chi tiet - da bo qua (khong ghep). Neu thieu chi tiet nao thi kiem xem duong bao co bi ho khe khong.",
                    builder.DroppedOpenGroups));
            }

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
        /// <summary>
        /// Diem trong / tren mot vong (1 / 0 / -1), KIEM HOP BAO TRUOC. Ngoai hop bao dong thi chac
        /// chan -1 (giong het PointInRing); trong hop bao thi moi hieu toa do deu nho hon kich
        /// thuoc vong, nen tich so long khong the tran ke ca khi ban ve nam cach goc hang nghin km.
        /// Truoc day PointInRing chay thang tren toa do the gioi: diem xa ~3e6 mm co the tran.
        /// </summary>
        internal static int PointInLoop(Pt p, RecognizedLoop loop)
        {
            if (p.X < loop.MinX || p.X > loop.MaxX || p.Y < loop.MinY || p.Y > loop.MaxY) return -1;
            List<IntPoint> ring = new List<IntPoint>(loop.Points.Count);
            foreach (Pt q in loop.Points) ring.Add(IntPoint.FromMm(q.X, q.Y));
            return GeometryMath.PointInRing(IntPoint.FromMm(p.X, p.Y), ring);
        }

        /// <summary>
        /// Diem nam trong VAT LIEU cua chi tiet: trong (hoac tren) vong ngoai va KHONG nam han
        /// trong mot lo. Diem nam trong lo la khoang TRONG - khong thuoc chi tiet do (co the thuoc
        /// chi tiet khac nam trong lo). Tren bien lo = tren bien vat lieu -> van tinh la trong.
        /// </summary>
        internal static bool InMaterial(RecognizedPart part, Pt p)
        {
            if (part.Outer == null || PointInLoop(p, part.Outer) < 0) return false;
            foreach (RecognizedLoop h in part.Holes)
            {
                if (PointInLoop(p, h) > 0) return false;
            }

            return true;
        }

        public static bool AttachMarking(IList<RecognizedPart> records, int sourceIndex, IList<Pt> samples)
        {
            RecognizedPart owner = null;
            foreach (RecognizedPart part in records)
            {
                if (part.Outer == null) continue;
                if (owner != null && part.Outer.Area >= owner.Outer.Area) continue;

                bool inside = samples.Count > 0;
                foreach (Pt p in samples)
                {
                    if (PointInLoop(p, part.Outer) < 0)
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

        /// <summary>
        /// Attaches one engraving TEXT / MTEXT to the part that contains it.
        ///
        /// Innermost part wins, and the anchor must lie in the part's MATERIAL: engraving is
        /// physically on the part, so an anchor inside a closed HOLE of a frame belongs to the
        /// part sitting in that hole (or to nothing) - never to the frame around it. Text
        /// that sits outside every part is NOT attached and the caller reports it; silently
        /// dropping it would lose a marking the operator asked for.
        ///
        /// The source is added to GeometrySources, which is what the output stage clones into the
        /// part block. Nothing is added to the nesting polygon, so engraving can never affect
        /// collision or placement.
        /// </summary>
        public static bool AttachEngraving(IList<RecognizedPart> records, int sourceIndex, Pt anchor)
        {
            RecognizedPart owner = null;
            foreach (RecognizedPart part in records)
            {
                if (part.Outer == null) continue;
                if (owner != null && part.Outer.Area >= owner.Outer.Area) continue;
                if (InMaterial(part, anchor)) owner = part;
            }

            if (owner == null) return false;
            if (!owner.EngravingSources.Contains(sourceIndex)) owner.EngravingSources.Add(sourceIndex);
            if (!owner.GeometrySources.Contains(sourceIndex)) owner.GeometrySources.Add(sourceIndex);
            return true;
        }

        /// <summary>
        /// Chi tiet KHONG co chu SL / vat lieu nao, nhung GIONG HET HINH mot chi tiet khac CO
        /// chu -> MO HO, bat nguoi dung xem lai.
        ///
        /// Truoc day no lang le nhan SL 1 / vat lieu mac dinh, va hai truong hop thuong gap deu
        /// ra sai ma khong ai biet:
        ///   - BAN SAO cua chi tiet (ket qua ghep cu ve vao ban ve nay, copy de nhap...) bi quet
        ///     cung -> cung mot phoi xuat hien HAI LAN, mot lan o vat lieu mac dinh (1.2MM);
        ///   - cap chi tiet DOI XUNG dung chung mot chu ("CHAN DOI XUNG") -> chiec kia mat do day.
        /// Ca hai deu phai do NGUOI quyet: bo tick Ghep (ban sao) hoac sua SL / vat lieu.
        ///
        /// Goi SAU khi da gan chu va lay lai thong tin tu block cu. Tra ve so chi tiet bi danh dau.
        /// </summary>
        public static int FlagUntextedTwins(IList<RecognizedPart> parts)
        {
            List<RecognizedPart> withText = new List<RecognizedPart>();
            foreach (RecognizedPart p in parts)
            {
                if (p.IsNestable && p.TextSources.Count > 0) withText.Add(p);
            }

            if (withText.Count == 0) return 0;

            int flagged = 0;
            foreach (RecognizedPart p in parts)
            {
                if (!p.IsNestable || p.TextSources.Count > 0) continue;

                RecognizedPart twin = withText.Find(t => SameShape(t, p));
                if (twin == null) continue;

                flagged++;
                string note = string.Format(CultureInfo.InvariantCulture,
                    "GIONG HET hinh {0} nhung KHONG co chu SL / vat lieu (dang tam lay SL {1}, {2}) - neu la BAN SAO (vd. ket qua ghep cu) thi bo tick Ghep, neu la chi tiet doi xung thi sua SL / vat lieu",
                    twin.Name, p.Quantity, p.Material);

                // Ghi chu nay quan trong hon cac ghi chu "mac dinh" - dat len DAU de nhin thay ngay.
                p.Escalate(PartStatus.Ambiguous, null);
                if (!p.Notes.Contains(note)) p.Notes.Insert(0, note);
            }

            return flagged;
        }

        /// <summary>Cung hinh (ke ca da xoay / lat): cung so lo, dien tich va chu vi lech duoi 0.3%.</summary>
        private static bool SameShape(RecognizedPart a, RecognizedPart b)
        {
            if (a.Holes.Count != b.Holes.Count) return false;
            double areaA = NetArea(a), areaB = NetArea(b);
            if (!Close(areaA, areaB)) return false;
            return Close(Perimeter(a.Outer.Points), Perimeter(b.Outer.Points));
        }

        private static bool Close(double x, double y)
        {
            return Math.Abs(x - y) <= 0.003 * Math.Max(Math.Abs(x), Math.Abs(y)) + 1e-6;
        }

        private static double NetArea(RecognizedPart p)
        {
            double a = p.Outer.Area;
            foreach (RecognizedLoop h in p.Holes) a -= h.Area;
            return a;
        }

        private static double Perimeter(List<Pt> ring)
        {
            double sum = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) sum += ring[i].DistanceTo(ring[j]);
            return sum;
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
                    SimpleNestingEngine.NormalizeMaterial(r.Material),
                    r.Order);
                g.Name = r.Name;
                g.SourceReference = r;
                groups.Add(g);
            }

            return groups;
        }
    }
}
