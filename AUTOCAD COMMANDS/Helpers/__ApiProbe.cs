using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WF = System.Windows.Forms;

namespace AUTOCAD_COMMANDS
{
    internal static class ApiProbe
    {
        internal static void Probe(Database db, Transaction tr, Editor ed, Curve target, Entity cutter)
        {
            Point3dCollection pts = new Point3dCollection();
            target.IntersectWith(cutter, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
            target.IntersectWith(cutter, Intersect.ExtendArgument, pts, IntPtr.Zero, IntPtr.Zero);
            target.IntersectWith(cutter, Intersect.ExtendThis, pts, IntPtr.Zero, IntPtr.Zero);
            target.IntersectWith(cutter, Intersect.ExtendBoth, pts, IntPtr.Zero, IntPtr.Zero);
            using (Plane xy = new Plane(Point3d.Origin, Vector3d.ZAxis))
            {
                target.IntersectWith(cutter, Intersect.OnBothOperands, xy, pts, IntPtr.Zero, IntPtr.Zero);
            }

            Point3d snapped = target.GetClosestPointTo(pts[0], false);
            double t = target.GetParameterAtPoint(snapped);
            double d0 = target.GetDistanceAtParameter(target.StartParam);
            double d1 = target.GetDistanceAtParameter(target.EndParam);
            Point3d mid = target.GetPointAtDist((d0 + d1) * 0.5);
            Point3d midP = target.GetPointAtParameter((target.StartParam + target.EndParam) * 0.5);
            double tAtDist = target.GetParameterAtDistance(d1 * 0.5);
            double distAtPt = target.GetDistAtPoint(snapped);
            bool closed = target.Closed;
            bool periodic = target.IsPeriodic;
            Vector3d tangent = target.GetFirstDerivative(t);

            List<double> ps = new List<double> { t };
            DBObjectCollection pieces = target.GetSplitCurves(new DoubleCollection(ps.ToArray()));
            DBObjectCollection pieces2 = target.GetSplitCurves(pts);

            BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            List<Entity> kept = new List<Entity>();
            for (int i = 0; i < pieces.Count; i++)
            {
                Curve piece = pieces[i] as Curve;
                if (piece == null) { continue; }
                space.AppendEntity(piece);
                tr.AddNewlyCreatedDBObject(piece, true);
                piece.SetPropertiesFrom(target);
                kept.Add(piece);
            }
            for (int i = 0; i < pieces2.Count; i++)
            {
                DBObject unused = pieces2[i];
                unused.Dispose();
            }
            pieces.Dispose();
            pieces2.Dispose();

            target.UpgradeOpen();
            target.Erase();
            target.Erase(true);

            if (kept.Count > 1)
            {
                Autodesk.AutoCAD.Geometry.IntegerCollection joined =
                    kept[0].JoinEntities(kept.Skip(1).ToArray());
                kept[0].JoinEntity(kept[1]);
                int n = joined.Count;
                ed.WriteMessage("\n{0}", n);
            }

            Autodesk.AutoCAD.DatabaseServices.Polyline lw =
                target as Autodesk.AutoCAD.DatabaseServices.Polyline;
            if (lw != null)
            {
                bool c1 = lw.Closed;
                int nv = lw.NumberOfVertices;
                double bulge = lw.GetBulgeAt(0);
                SegmentType st = lw.GetSegmentType(0);
                bool hasB = lw.HasBulges;
                double len = lw.Length;
                Vector3d nrm = lw.Normal;
                double elev = lw.Elevation;
                ed.WriteMessage("\n{0}{1}{2}{3}{4}{5}{6}{7}", c1, nv, bulge, st, hasB, len, nrm, elev);
            }
            Polyline2d p2 = target as Polyline2d; if (p2 != null) { ed.WriteMessage("\n{0}", p2.Closed); }
            Polyline3d p3 = target as Polyline3d; if (p3 != null) { ed.WriteMessage("\n{0}", p3.Closed); }
            Spline sp = target as Spline; if (sp != null) { ed.WriteMessage("\n{0}", sp.Closed); }
            Circle ci = target as Circle; if (ci != null) { ed.WriteMessage("\n{0}{1}", ci.Closed, ci.Normal); }
            Ellipse el = target as Ellipse; if (el != null) { ed.WriteMessage("\n{0}{1}", el.Closed, el.StartParam); }

            Autodesk.AutoCAD.Colors.Color col = cutter.Color;
            Autodesk.AutoCAD.Colors.ColorMethod cm = col.ColorMethod;
            bool byLayer = col.IsByLayer;
            bool byBlock = col.IsByBlock;
            bool byAci = col.IsByAci;
            short aci = col.ColorIndex;
            int entAci = cutter.ColorIndex;
            Autodesk.AutoCAD.Colors.EntityColor ec = cutter.EntityColor;
            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(cutter.LayerId, OpenMode.ForRead);
            Autodesk.AutoCAD.Colors.Color layerCol = ltr.Color;
            bool locked = ltr.IsLocked; bool frozen = ltr.IsFrozen; bool off = ltr.IsOff;
            string lname = ltr.Name;
            Autodesk.AutoCAD.Colors.Color made = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
            Autodesk.AutoCAD.Colors.Color rgb = Autodesk.AutoCAD.Colors.Color.FromRgb(255, 0, 0);
            bool sameColor = made.Equals(rgb) || made.ColorValue == rgb.ColorValue;

            Extents3d ex = cutter.GeometricExtents;
            double diag = ex.MinPoint.DistanceTo(ex.MaxPoint);

            IntegerCollection vps = new IntegerCollection();
            using (Autodesk.AutoCAD.DatabaseServices.Polyline ghost =
                new Autodesk.AutoCAD.DatabaseServices.Polyline())
            {
                ghost.SetDatabaseDefaults(db);
                ghost.ColorIndex = 1;
                TransientManager.CurrentTransientManager.AddTransient(
                    ghost, TransientDrawingMode.DirectShortTerm, 128, vps);
                TransientManager.CurrentTransientManager.UpdateTransient(ghost, vps);
                TransientManager.CurrentTransientManager.EraseTransient(ghost, vps);
                TransientManager.CurrentTransientManager.EraseTransients(
                    TransientDrawingMode.DirectShortTerm, 128, vps);
            }

            double gEq = Tolerance.Global.EqualPoint;

            try { double bad = target.GetParameterAtPoint(Point3d.Origin); ed.WriteMessage("\n{0}", bad); }
            catch (Autodesk.AutoCAD.Runtime.Exception exn)
            {
                if (exn.ErrorStatus == ErrorStatus.PointNotOnEntity ||
                    exn.ErrorStatus == ErrorStatus.InvalidInput ||
                    exn.ErrorStatus == ErrorStatus.NotApplicable ||
                    exn.ErrorStatus == ErrorStatus.OnLockedLayer ||
                    exn.ErrorStatus == ErrorStatus.NullExtents ||
                    exn.ErrorStatus == ErrorStatus.InvalidExtents ||
                    exn.ErrorStatus == ErrorStatus.NoIntersections ||
                    exn.ErrorStatus == ErrorStatus.EmbeddedIntersections ||
                    exn.ErrorStatus == ErrorStatus.NotImplementedYet ||
                    exn.ErrorStatus == ErrorStatus.DegenerateGeometry)
                {
                    ed.WriteMessage("\nx");
                }
            }

            PromptSelectionResult impl = ed.SelectImplied();
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            Handle h = cutter.Handle;
            long hv = h.Value;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            using (DocumentLock dl = doc.LockDocument()) { ed.WriteMessage("\n{0}", dl != null); }

            List<ObjectId> ids = new List<ObjectId>();
            BlockTableRecord ro = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in ro) { ids.Add(id); }

            using (Line ray = new Line(mid, mid + new Vector3d(1.0, 0.37, 0.0) * 1000.0))
            {
                Point3dCollection hits = new Point3dCollection();
                cutter.IntersectWith(ray, Intersect.OnBothOperands, hits, IntPtr.Zero, IntPtr.Zero);
                ed.WriteMessage("\n{0}", hits.Count);
            }

            if (WF.Application.OpenForms.Count > 0 && Color.Red.R == 0) { }
            ed.WriteMessage("\n{0} {1} {2} {3} {4} {5} {6} {7} {8} {9} {10} {11} {12} {13} {14} {15}",
                t, midP, tAtDist, distAtPt, closed, periodic, tangent, mid, diag, gEq,
                cm, byLayer, byAci, aci, entAci, byBlock);
            ed.WriteMessage("\n{0} {1} {2} {3} {4} {5} {6} {7} {8} {9}",
                ec, layerCol, locked, frozen, off, lname, made, rgb, ids.Count, sameColor);
            ed.WriteMessage("\n{0} {1}", impl.Status, hv);
        }
    }
}
