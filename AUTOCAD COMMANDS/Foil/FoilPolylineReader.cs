using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - DOC POLYLINE AUTOCAD -> FoilProfile
    // ------------------------------------------------------------------------------------------
    // Day la RANH GIOI giua AutoCAD va calculation engine. Sau ham nay khong con kieu du lieu
    // nao cua AutoCAD di sau vao engine nua.
    //
    // Ho tro: LWPolyline (Polyline). Polyline2d / Polyline3d duoc doc qua duong chuyen doi
    // toa do dinh (khong co bulge => goc nhon).
    // ==========================================================================================

    public class FoilPolylineReadResult
    {
        public FoilProfile Profile { get; set; }

        public List<string> Notes { get; private set; } = new List<string>();

        public List<string> Errors { get; private set; } = new List<string>();

        public bool Success { get { return Profile != null && Errors.Count == 0; } }

        /// <summary>Cao do Z cua bien dang goc (dung lam cao do mac dinh cho phoi).</summary>
        public double Elevation { get; set; }

        /// <summary>Khung bao cua bien dang trong WCS - dung de goi y diem dat phoi.</summary>
        public double MinX { get; set; }

        public double MinY { get; set; }

        public double MaxX { get; set; }

        public double MaxY { get; set; }
    }

    public static class FoilPolylineReader
    {
        public static FoilPolylineReadResult Read(Entity entity, FoilSettings settings)
        {
            FoilPolylineReadResult result = new FoilPolylineReadResult();

            if (entity == null)
            {
                result.Errors.Add("Khong co doi tuong nao duoc chon.");
                return result;
            }

            double tolerance = settings != null ? settings.DuplicatePointTolerance : 1e-6;
            List<FoilRawVertex> vertices = new List<FoilRawVertex>();
            bool closed = false;

            Polyline lw = entity as Polyline;
            if (lw != null)
            {
                if (lw.NumberOfVertices < 2)
                {
                    result.Errors.Add("Polyline phai co it nhat 2 dinh.");
                    return result;
                }

                // Kiem tra polyline co nam trong mat phang song song XY cua WCS khong.
                if (Math.Abs(lw.Normal.Z) < 0.999999)
                {
                    result.Errors.Add(
                        "Polyline khong nam trong mat phang XY cua WCS (Normal lech). " +
                        "DX_FOIL chi trien khai duoc bien dang 2D phang.");
                    return result;
                }

                // QUAN TRONG - OCS vs WCS:
                // GetPoint2dAt tra ve toa do trong OCS cua chinh polyline, KHONG phai WCS.
                // Neu polyline co Normal = (0,0,-1) (gap sau mot so thao tac mirror / UCS am) thi
                // truc X cua OCS la (-1,0,0), va doc bang GetPoint2dAt se cho bien dang BI LAT
                // GUONG - sai thu tu canh va sai vi tri duong chan.
                // Vi vay luon doc qua GetPoint3dAt (tra ve WCS) va dao dau bulge khi mat phang bi lat,
                // vi dau bulge duoc dinh nghia trong mat phang rieng cua polyline.
                bool flipped = lw.Normal.Z < 0.0;
                if (flipped)
                {
                    result.Notes.Add(
                        "Polyline co Normal huong xuong (0,0,-1) - da quy doi toa do ve WCS va dao dau bulge.");
                }

                result.Elevation = lw.NumberOfVertices > 0 ? lw.GetPoint3dAt(0).Z : lw.Elevation;

                for (int i = 0; i < lw.NumberOfVertices; i++)
                {
                    Point3d p = lw.GetPoint3dAt(i);

                    // Bulge cua doan cuoi chi co nghia khi polyline dong.
                    double bulge = lw.GetBulgeAt(i);
                    if (!lw.Closed && i == lw.NumberOfVertices - 1)
                    {
                        bulge = 0.0;
                    }

                    if (flipped)
                    {
                        bulge = -bulge;
                    }

                    vertices.Add(new FoilRawVertex(new FoilPoint2d(p.X, p.Y), bulge));
                }

                closed = lw.Closed;
            }
            else
            {
                Polyline2d p2d = entity as Polyline2d;
                Polyline3d p3d = entity as Polyline3d;

                if (p2d == null && p3d == null)
                {
                    result.Errors.Add(
                        "Doi tuong da chon khong phai POLYLINE. Hay chon mot LWPOLYLINE bien dang.");
                    return result;
                }

                result.Notes.Add(
                    "Doi tuong la POLYLINE kieu cu - da doc theo dinh, bo qua bulge (coi cac goc la goc nhon).");

                try
                {
                    DBObjectCollection exploded = new DBObjectCollection();
                    entity.Explode(exploded);

                    List<Point3d> points = new List<Point3d>();
                    foreach (DBObject obj in exploded)
                    {
                        Line line = obj as Line;
                        if (line != null)
                        {
                            if (points.Count == 0)
                            {
                                points.Add(line.StartPoint);
                            }

                            points.Add(line.EndPoint);
                        }

                        obj.Dispose();
                    }

                    if (points.Count < 2)
                    {
                        result.Errors.Add("Khong doc duoc dinh nao tu POLYLINE kieu cu.");
                        return result;
                    }

                    result.Elevation = points[0].Z;
                    foreach (Point3d p in points)
                    {
                        vertices.Add(new FoilRawVertex(new FoilPoint2d(p.X, p.Y), 0.0));
                    }

                    closed = p2d != null ? p2d.Closed : p3d.Closed;
                    if (closed && vertices.Count > 2 &&
                        vertices[0].Point.DistanceTo(vertices[vertices.Count - 1].Point) <= tolerance)
                    {
                        vertices.RemoveAt(vertices.Count - 1);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add("Khong doc duoc POLYLINE kieu cu: " + ex.Message);
                    return result;
                }
            }

            // Khung bao (tinh tren dinh - du de goi y vi tri dat phoi).
            result.MinX = double.MaxValue;
            result.MinY = double.MaxValue;
            result.MaxX = double.MinValue;
            result.MaxY = double.MinValue;
            foreach (FoilRawVertex v in vertices)
            {
                if (v.Point.X < result.MinX) result.MinX = v.Point.X;
                if (v.Point.Y < result.MinY) result.MinY = v.Point.Y;
                if (v.Point.X > result.MaxX) result.MaxX = v.Point.X;
                if (v.Point.Y > result.MaxY) result.MaxY = v.Point.Y;
            }

            try
            {
                Extents3d extents = entity.GeometricExtents;
                result.MinX = extents.MinPoint.X;
                result.MinY = extents.MinPoint.Y;
                result.MaxX = extents.MaxPoint.X;
                result.MaxY = extents.MaxPoint.Y;
            }
            catch
            {
                // Giu lai khung bao tinh tu dinh neu khong lay duoc extents.
            }

            result.Profile = FoilProfileBuilder.Build(vertices, closed, tolerance, result.Notes);
            if (result.Profile == null)
            {
                result.Errors.Add("Khong dung duoc bien dang hop le tu polyline da chon.");
                return result;
            }

            result.Notes.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Da doc bien dang: {0} doan, {1}, tong chieu dai {2:0.####} mm.",
                result.Profile.SegmentCount,
                result.Profile.Closed ? "KIN" : "HO",
                result.Profile.TotalLength));

            return result;
        }
    }
}
