using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - CAU NOI GIUA AUTOCAD VA ENGINE THUAN
    // ------------------------------------------------------------------------------------------
    // Day la file DUY NHAT cua module cham vao ca hai the gioi:
    //   DOC : Polyline + DIMSTYLE  ->  mo hinh thuan (DimPlineInput / DimPlineStyle)
    //   GHI : ban ke hoach bo tri  ->  entity Dimension that
    // Nho vay engine phan tich/bo tri khong he biet den AutoCAD va van test duoc ngoai AutoCAD.
    // ==========================================================================================

    internal sealed class DimPlineReadResult
    {
        public bool Success;
        public string ErrorMessage = string.Empty;
        public DimPlineInput Input;

        /// <summary>Cao do Z de dat dim, lay theo mat phang cua polyline.</summary>
        public double Elevation;
    }

    internal static class DimPlineCadReader
    {
        /// <summary>
        /// Doc LWPolyline thanh mo hinh thuan.
        /// Chi nhan polyline nam trong mat phang song song XY cua WCS - truong hop khac bi tu
        /// choi CO THONG BAO thay vi tra ve hinh hoc sai am tham.
        /// </summary>
        public static DimPlineReadResult Read(Polyline polyline)
        {
            DimPlineReadResult result = new DimPlineReadResult();

            if (polyline == null)
            {
                result.ErrorMessage = "Khong doc duoc Polyline.";
                return result;
            }

            Vector3d normal = polyline.Normal;
            if (Math.Abs(normal.Z) < 0.999)
            {
                result.ErrorMessage =
                    "Polyline khong nam trong mat phang song song XY cua WCS - lenh nay chi ho tro polyline 2D.";
                return result;
            }

            int count = polyline.NumberOfVertices;
            if (count < 2)
            {
                result.ErrorMessage = "Polyline can it nhat 2 dinh.";
                return result;
            }

            // Normal huong -Z: he toa do doi tuong bi lat nen chieu quay cua bulge cung dao.
            double bulgeSign = normal.Z >= 0.0 ? 1.0 : -1.0;

            DimPlineInput input = new DimPlineInput();
            input.Closed = polyline.Closed;

            double elevation = 0.0;

            for (int i = 0; i < count; i++)
            {
                Point3d wcs;
                try
                {
                    wcs = polyline.GetPoint3dAt(i);
                }
                catch (System.Exception)
                {
                    result.ErrorMessage = "Khong doc duoc dinh so " + (i + 1) + " cua Polyline.";
                    return result;
                }

                double bulge = 0.0;
                try
                {
                    bulge = polyline.GetBulgeAt(i) * bulgeSign;
                }
                catch (System.Exception)
                {
                    bulge = 0.0;
                }

                if (i == 0)
                {
                    elevation = wcs.Z;
                }

                input.Add(wcs.X, wcs.Y, bulge);
            }

            // Polyline "kin bang mat" (dinh cuoi trung dinh dau nhung co Closed = false) rat
            // pho bien khi import. Coi la kin that de xac dinh duoc trong/ngoai.
            if (!input.Closed && input.Vertices.Count >= 3)
            {
                DimPlineVertex first = input.Vertices[0];
                DimPlineVertex last = input.Vertices[input.Vertices.Count - 1];

                if (Math.Abs(first.X - last.X) < 1e-7 && Math.Abs(first.Y - last.Y) < 1e-7)
                {
                    // Bulge cua doan cuoi chinh la bulge cua doan dong -> giu lai o dinh truoc do.
                    input.Vertices.RemoveAt(input.Vertices.Count - 1);
                    input.Closed = true;
                }
            }

            result.Input = input;
            result.Elevation = elevation;
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Lay co chu / co mui ten / so le THUC TE cua DIMSTYLE hien hanh.
        /// Engine bo tri dung chung de tinh be rong text va khoang ho toi thieu - khong co so
        /// nao bi hard-code, va lenh cung khong sua gi cua DIMSTYLE.
        /// </summary>
        public static DimPlineStyle ReadStyle(Database db)
        {
            if (db == null)
            {
                return new DimPlineStyle();
            }

            double dimScale = 1.0;
            double textHeight = 2.5;
            double arrowSize = 2.5;
            int decimals = 2;

            try
            {
                // DIMSCALE = 0 nghia la "tu chia theo viewport" - coi nhu 1 de con tinh duoc.
                dimScale = db.Dimscale > 1e-9 ? db.Dimscale : 1.0;
                textHeight = db.Dimtxt > 1e-9 ? db.Dimtxt * dimScale : 2.5;
                arrowSize = db.Dimasz > 1e-9 ? db.Dimasz * dimScale : textHeight;
                decimals = db.Dimdec;
            }
            catch (System.Exception)
            {
                // Giu nguyen gia tri mac dinh neu ban ve khong tra loi duoc.
            }

            return new DimPlineStyle(textHeight, arrowSize, decimals);
        }
    }

    internal sealed class DimPlineCreateResult
    {
        public bool Success;
        public string ErrorMessage = string.Empty;
        public List<ObjectId> CreatedIds = new List<ObjectId>();
    }

    internal static class DimPlineCadCreator
    {
        /// <summary>
        /// Tao entity Dimension that tu ban ke hoach da bo tri xong.
        /// Ham nay KHONG bat loi rieng le: mot dim loi lam ca me that bai de caller huy
        /// transaction, khong de lai ban ve nua voi.
        /// </summary>
        public static DimPlineCreateResult Create(
            Transaction tr,
            Database db,
            BlockTableRecord space,
            ObjectId layerId,
            DimPlinePlan plan,
            AutoDimPlineSettings settings,
            double elevation)
        {
            DimPlineCreateResult result = new DimPlineCreateResult();

            if (plan == null || !plan.IsValid)
            {
                result.ErrorMessage = plan != null ? plan.ErrorMessage : "Khong co ban ke hoach dim.";
                return result;
            }

            try
            {
                foreach (DimPlinePlacement p in plan.Placements)
                {
                    Dimension dim = BuildDimension(db, p, elevation);
                    if (dim == null)
                    {
                        continue;
                    }

                    ApplySettings(dim, layerId, settings);
                    space.AppendEntity(dim);
                    tr.AddNewlyCreatedDBObject(dim, true);
                    result.CreatedIds.Add(dim.ObjectId);
                }

                result.Success = true;
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static Dimension BuildDimension(Database db, DimPlinePlacement p, double z)
        {
            Point3d p1 = new Point3d(p.DefPoint1.X, p.DefPoint1.Y, z);
            Point3d p2 = new Point3d(p.DefPoint2.X, p.DefPoint2.Y, z);
            Point3d dimLine = new Point3d(p.DimLinePoint.X, p.DimLinePoint.Y, z);

            switch (p.Kind)
            {
                case DimKind.Linear:
                    return new RotatedDimension(p.Rotation, p1, p2, dimLine, string.Empty, db.Dimstyle);

                case DimKind.Aligned:
                    return new AlignedDimension(p1, p2, dimLine, string.Empty, db.Dimstyle);

                case DimKind.Radial:
                    return new RadialDimension(
                        new Point3d(p.ArcCenter.X, p.ArcCenter.Y, z),
                        new Point3d(p.ChordPoint.X, p.ChordPoint.Y, z),
                        p.LeaderLength,
                        string.Empty,
                        db.Dimstyle);

                default:
                    return null;
            }
        }

        private static void ApplySettings(Dimension dim, ObjectId layerId, AutoDimPlineSettings settings)
        {
            if (!layerId.IsNull)
            {
                dim.LayerId = layerId;
            }

            // DIMLFAC: he so nhan vao GIA TRI DO HIEN THI. Day la thu DUY NHAT lenh ghi de len
            // DIMSTYLE - co chu, mui ten, khoang ho... deu de dim tu thua huong DIMSTYLE hien
            // hanh y het nhu khi nguoi dung tu dim tay.
            if (settings != null && settings.LinearScale > 0.0)
            {
                dim.Dimlfac = settings.LinearScale;
            }
        }
    }
}
