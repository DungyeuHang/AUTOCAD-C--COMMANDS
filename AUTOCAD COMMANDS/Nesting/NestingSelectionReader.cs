using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AUTOCAD_COMMANDS.Nesting.Recognition;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>One selected top-level entity (the thing that is cloned into the output).</summary>
    internal sealed class NestSource
    {
        public ObjectId Id;
        public string Kind;
        public string Layer;

        /// <summary>
        /// Ten don hang cua luot quet da chon doi tuong nay. Rong = chay mot don (nhu truoc).
        ///
        /// Gan tu ben ngoai theo LUOT QUET, khong bao gio doc tu ban ve: khong suy tu chu,
        /// khong suy tu layer, khong suy tu hinh hoc.
        /// </summary>
        public string Order = string.Empty;
    }

    internal sealed class NestReadResult
    {
        public readonly List<NestSource> Sources = new List<NestSource>();
        public readonly List<CurveChain> Chains = new List<CurveChain>();
        public readonly List<TextItem> Texts = new List<TextItem>();

        /// <summary>Marking-layer geometry: source index -> sample points (attached to parts later).</summary>
        public readonly List<KeyValuePair<int, List<Pt>>> Markings = new List<KeyValuePair<int, List<Pt>>>();

        /// <summary>
        /// Engraving-layer TEXT / MTEXT: source index -> anchor point (the text centre, so that
        /// justification is already accounted for). Attached to the containing part later.
        /// These never reach <see cref="Texts"/>, so they are never read as SL / material.
        /// </summary>
        public readonly List<KeyValuePair<int, Pt>> Engravings = new List<KeyValuePair<int, Pt>>();

        public readonly List<string> Warnings = new List<string>();
        public int IgnoredCount;
    }

    /// <summary>
    /// AutoCAD -> recognition input. READ ONLY: never modifies the drawing.
    /// Supported: LINE, ARC, CIRCLE, LWPOLYLINE (with bulges), POLYLINE 2D/3D, ELLIPSE, SPLINE,
    /// TEXT, MTEXT and BLOCK references (exploded in memory, the block stays one source).
    /// Arcs are discretised so that the chord deviation never exceeds the tolerance; the core
    /// adds the same tolerance to every clearance.
    /// </summary>
    internal static class NestingSelectionReader
    {
        private const int MaxBlockDepth = 8;
        private const int MaxSamplesPerCurve = 20000;

        public static NestReadResult Read(Transaction tr, IEnumerable<ObjectId> ids, GhoPhoiSettings settings)
        {
            return Read(tr, ids, settings, null);
        }

        /// <param name="orderByEntity">
        /// Doi tuong -> ten don hang, lay tu luot quet da chon no. Null = chay mot don.
        /// </param>
        public static NestReadResult Read(
            Transaction tr, IEnumerable<ObjectId> ids, GhoPhoiSettings settings, IDictionary<ObjectId, string> orderByEntity)
        {
            NestReadResult result = new NestReadResult();
            Dictionary<string, int> ignoredKinds = new Dictionary<string, int>();
            int nonPlanar = 0;

            foreach (ObjectId id in ids)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                string order;
                if (orderByEntity == null || !orderByEntity.TryGetValue(id, out order)) order = string.Empty;

                int source = result.Sources.Count;
                result.Sources.Add(new NestSource { Id = id, Kind = ent.GetType().Name, Layer = ent.Layer, Order = order });

                ReadEntity(ent, ent.Database, source, settings, result, ignoredKinds, ref nonPlanar, 0,
                    settings.IsMarkingLayer(ent.Layer), settings.IsEngravingLayer(ent.Layer));
            }

            foreach (KeyValuePair<string, int> kv in ignoredKinds)
            {
                result.IgnoredCount += kv.Value;
                result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Bo qua {0} doi tuong {1} (khong phai hinh cat / text).", kv.Value, kv.Key));
            }

            if (nonPlanar > 0)
            {
                result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Bo qua {0} doi tuong KHONG nam trong mat phang XY (normal khac truc Z).", nonPlanar));
            }

            return result;
        }

        private static void ReadEntity(
            Entity ent, Database db, int source, GhoPhoiSettings settings, NestReadResult result,
            Dictionary<string, int> ignored, ref int nonPlanar, int depth, bool marking, bool engraving)
        {
            double tol = Math.Max(1e-4, settings.ArcToleranceMm);

            // Chu tren layer KHAC la hinh khac len chi tiet, khong phai thong tin: no di
            // thang sang danh sach Engravings, khong bao gio duoc doc SL / vat lieu.
            MText mtext = ent as MText;
            if (mtext != null)
            {
                Pt centre = TextCenter(ent, mtext.Location);
                if (engraving) result.Engravings.Add(new KeyValuePair<int, Pt>(source, centre));
                else result.Texts.Add(new TextItem(source, mtext.Text, centre));
                return;
            }

            DBText text = ent as DBText;
            if (text != null)
            {
                Pt centre = DbTextCenter(text, db);
                if (engraving) result.Engravings.Add(new KeyValuePair<int, Pt>(source, centre));
                else result.Texts.Add(new TextItem(source, text.TextString, centre));
                return;
            }

            BlockReference br = ent as BlockReference;
            if (br != null)
            {
                if (depth >= MaxBlockDepth)
                {
                    Count(ignored, "block long qua sau");
                    return;
                }

                using (DBObjectCollection parts = new DBObjectCollection())
                {
                    try
                    {
                        br.Explode(parts);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        Count(ignored, "block khong explode duoc");
                        return;
                    }

                    foreach (DBObject o in parts)
                    {
                        Entity child = o as Entity;
                        if (child != null && child.Visible)
                        {
                            ReadEntity(child, db, source, settings, result, ignored, ref nonPlanar, depth + 1,
                                marking || settings.IsMarkingLayer(child.Layer),
                                engraving || settings.IsEngravingLayer(child.Layer));
                        }

                        o.Dispose();
                    }
                }

                return;
            }

            Curve curve = ent as Curve;
            if (curve == null)
            {
                Count(ignored, ent.GetType().Name.ToUpperInvariant());
                return;
            }

            if (!IsPlanarXY(curve))
            {
                nonPlanar++;
                return;
            }

            bool closed;
            List<Pt> pts = Discretize(curve, tol, out closed);
            if (pts == null || pts.Count < 2)
            {
                Count(ignored, curve.GetType().Name.ToUpperInvariant() + " (dai 0)");
                return;
            }

            if (marking)
            {
                result.Markings.Add(new KeyValuePair<int, List<Pt>>(source, pts));
                return;
            }

            result.Chains.Add(new CurveChain(source, pts, closed, IsApproximated(curve)));
        }

        private static void Count(Dictionary<string, int> ignored, string kind)
        {
            int n;
            ignored.TryGetValue(kind, out n);
            ignored[kind] = n + 1;
        }

        /// <summary>
        /// Centre of a DBText. A justified text (not Left/Base) whose alignment has never been
        /// adjusted (text written by other software / code) reports extents at the wrong place,
        /// which can move it onto a neighbouring part. The extents are therefore taken from an
        /// in-memory clone adjusted by AutoCAD itself (font metrics included). AdjustAlignment
        /// needs the owning database to be the working database, so it is switched temporarily
        /// and always restored. The drawing entity itself is never modified.
        /// </summary>
        internal static Pt DbTextCenter(DBText text, Database db)
        {
            bool justified = text.HorizontalMode != TextHorizontalMode.TextLeft || text.VerticalMode != TextVerticalMode.TextBase;
            if (!justified || db == null) return TextCenter(text, text.Position);

            Database previous = HostApplicationServices.WorkingDatabase;
            bool switched = false;
            try
            {
                if (previous != db)
                {
                    HostApplicationServices.WorkingDatabase = db;
                    switched = true;
                }

                using (DBText copy = (DBText)text.Clone())
                {
                    copy.AdjustAlignment(db);
                    Extents3d e = copy.GeometricExtents;
                    return new Pt((e.MinPoint.X + e.MaxPoint.X) * 0.5, (e.MinPoint.Y + e.MaxPoint.Y) * 0.5);
                }
            }
            catch
            {
                // AlignmentPoint is always on the text box of a justified text.
                return new Pt(text.AlignmentPoint.X, text.AlignmentPoint.Y);
            }
            finally
            {
                if (switched) HostApplicationServices.WorkingDatabase = previous;
            }
        }

        private static Pt TextCenter(Entity ent, Point3d fallback)
        {
            try
            {
                Extents3d e = ent.GeometricExtents;
                return new Pt((e.MinPoint.X + e.MaxPoint.X) * 0.5, (e.MinPoint.Y + e.MaxPoint.Y) * 0.5);
            }
            catch
            {
                return new Pt(fallback.X, fallback.Y);
            }
        }

        private static bool IsPlanarXY(Curve curve)
        {
            Vector3d normal;
            Arc arc = curve as Arc;
            Circle circle = curve as Circle;
            Polyline pl = curve as Polyline;
            Polyline2d pl2 = curve as Polyline2d;
            Ellipse el = curve as Ellipse;

            if (arc != null) normal = arc.Normal;
            else if (circle != null) normal = circle.Normal;
            else if (pl != null) normal = pl.Normal;
            else if (pl2 != null) normal = pl2.Normal;
            else if (el != null) normal = el.Normal;
            else if (curve is Line) return true;
            else
            {
                try
                {
                    if (!curve.IsPlanar) return false;
                    normal = curve.GetPlane().Normal;
                }
                catch
                {
                    // Straight curves have no unique plane; they are fine in XY.
                    return true;
                }
            }

            return normal.IsParallelTo(Vector3d.ZAxis, new Tolerance(1e-6, 1e-6));
        }

        /// <summary>Polyline approximation in WCS XY. Chord error &lt;= tol for arcs.</summary>
        internal static List<Pt> Discretize(Curve curve, double tol, out bool closed)
        {
            closed = false;

            Line line = curve as Line;
            if (line != null)
            {
                return new List<Pt> { P(line.StartPoint), P(line.EndPoint) };
            }

            Circle circle = curve as Circle;
            if (circle != null)
            {
                closed = true;
                List<Pt> c = SampleParam(circle, 0.0, 2 * Math.PI, ArcSegments(circle.Radius, 2 * Math.PI, tol));
                c.RemoveAt(c.Count - 1);
                return c;
            }

            Arc arc = curve as Arc;
            if (arc != null)
            {
                return SampleParam(arc, arc.StartParam, arc.EndParam, ArcSegments(arc.Radius, arc.EndParam - arc.StartParam, tol));
            }

            Polyline pl = curve as Polyline;
            if (pl != null)
            {
                return DiscretizePolyline(pl, tol, out closed);
            }

            // Polyline2d/3d, ellipse, spline...: adaptive sampling on the curve parameter.
            // For heavy polylines every integer parameter is a vertex: sample per vertex span so
            // that no corner is ever cut.
            closed = curve.Closed;
            List<Pt> pts = new List<Pt>();
            double t0 = curve.StartParam, t1 = curve.EndParam;
            bool vertexParams = curve is Polyline2d || curve is Polyline3d;
            int spans = vertexParams ? Math.Max(1, (int)Math.Round(t1 - t0)) : 64;
            Point3d prev = curve.GetPointAtParameter(t0);
            pts.Add(P(prev));
            for (int i = 1; i <= spans; i++)
            {
                double ta = t0 + (t1 - t0) * (i - 1) / spans;
                double tb = i == spans ? t1 : t0 + (t1 - t0) * i / spans;
                Point3d b = curve.GetPointAtParameter(tb);
                if (!(curve is Polyline3d)) Refine(curve, ta, tb, prev, b, tol, pts, 0);
                pts.Add(P(b));
                prev = b;
                if (pts.Count > MaxSamplesPerCurve) break;
            }

            if (closed && pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        /// <summary>True when the chain approximates a curve; false for exact straight segments.</summary>
        internal static bool IsApproximated(Curve curve)
        {
            if (curve is Line || curve is Polyline3d) return false;

            Polyline pl = curve as Polyline;
            if (pl != null)
            {
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    if (Math.Abs(pl.GetBulgeAt(i)) > 1e-12) return true;
                }

                return false;
            }

            // Arc, circle, ellipse, spline, Polyline2d (may be fitted / have bulges).
            return true;
        }

        private static void Refine(Curve curve, double ta, double tb, Point3d a, Point3d b, double tol, List<Pt> pts, int level)
        {
            if (level > 12) return;
            double tm = (ta + tb) * 0.5;
            Point3d m = curve.GetPointAtParameter(tm);
            double dev = Math.Sqrt(Core.GeometryMath.PointSegmentDistanceSquared(m.X, m.Y, a.X, a.Y, b.X, b.Y));
            if (dev <= tol * 0.5) return;
            Refine(curve, ta, tm, a, m, tol, pts, level + 1);
            pts.Add(P(m));
            Refine(curve, tm, tb, m, b, tol, pts, level + 1);
        }

        private static List<Pt> DiscretizePolyline(Polyline pl, double tol, out bool closed)
        {
            int n = pl.NumberOfVertices;
            closed = pl.Closed;
            List<Pt> pts = new List<Pt>();
            if (n == 0) return pts;

            // OCS -> WCS (handles polylines with normal -Z, e.g. after MIRROR).
            Matrix3d toWcs = Matrix3d.PlaneToWorld(pl.Normal);
            double z = pl.Elevation;
            Func<double, double, Pt> w = (x, y) => P(new Point3d(x, y, z).TransformBy(toWcs));

            int segments = closed ? n : n - 1;
            Point2d first = pl.GetPoint2dAt(0);
            pts.Add(w(first.X, first.Y));

            for (int i = 0; i < segments; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % n);
                double bulge = pl.GetBulgeAt(i);

                if (Math.Abs(bulge) > 1e-12 && a.GetDistanceTo(b) > 1e-12)
                {
                    double theta = 4.0 * Math.Atan(bulge);                 // signed sweep
                    double chord = a.GetDistanceTo(b);
                    double radius = Math.Abs(chord / (2.0 * Math.Sin(theta / 2.0)));
                    Point2d mid = new Point2d((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    Vector2d dir = (b - a).GetNormal();
                    Vector2d left = new Vector2d(-dir.Y, dir.X);
                    // Signed offset of the centre from the chord midpoint (left of a->b for a CCW
                    // arc below 180 deg; cos turns negative above 180 deg and flips the side).
                    double offset = Math.Sign(theta) * radius * Math.Cos(theta / 2.0);
                    Point2d centre = mid + left * offset;

                    double start = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
                    int segs = ArcSegments(radius, Math.Abs(theta), tol);
                    for (int k = 1; k < segs; k++)
                    {
                        double ang = start + theta * k / segs;
                        pts.Add(w(centre.X + radius * Math.Cos(ang), centre.Y + radius * Math.Sin(ang)));
                    }
                }

                if (!closed || i < segments - 1)
                {
                    pts.Add(w(b.X, b.Y));
                }
            }

            // Remove consecutive duplicates (zero-length segments).
            List<Pt> clean = new List<Pt>(pts.Count);
            foreach (Pt p in pts)
            {
                if (clean.Count == 0 || clean[clean.Count - 1].DistanceTo(p) > 1e-9) clean.Add(p);
            }

            return clean;
        }

        /// <summary>Segments so that the sagitta r(1 - cos(step/2)) &lt;= tol.</summary>
        internal static int ArcSegments(double radius, double sweep, double tol)
        {
            sweep = Math.Abs(sweep);
            if (radius <= tol) return Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 4)));
            double step = 2.0 * Math.Acos(1.0 - tol / radius);
            int n = (int)Math.Ceiling(sweep / step);
            return Math.Max(n, Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 2))));
        }

        private static List<Pt> SampleParam(Curve c, double t0, double t1, int segments)
        {
            List<Pt> pts = new List<Pt>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                pts.Add(P(c.GetPointAtParameter(t0 + (t1 - t0) * i / segments)));
            }

            return pts;
        }

        private static Pt P(Point3d p)
        {
            return new Pt(p.X, p.Y);
        }
    }
}
