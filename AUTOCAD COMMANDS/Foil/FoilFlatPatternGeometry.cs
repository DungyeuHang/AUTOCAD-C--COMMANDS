using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - SINH HINH HOC PHOI (VAN THUAN, CHUA CHAM AUTOCAD)
    // ------------------------------------------------------------------------------------------
    // Tach rieng khoi FoilDrawingBuilder de co the unit-test vi tri / goc cua tung duong chan
    // ma khong can mo AutoCAD.
    //
    // XU LY DUONG CHAN XIEN:
    //   Duong chan KHONG bao gio duoc mo ta bang "toa do X" hay "toa do Y". No luon la
    //   (diem goc, vector huong) trong he (U,V) cua phoi:
    //       diem goc  = (L/2, v)        v = vi tri trien khai da tinh
    //       huong     = (cos(skew), sin(skew))
    //   roi duoc CAT bang bien phoi bang thuat toan clip da giac loi, cuoi cung transform
    //   ve WCS bang FoilBlankFrame. Nho vay goc nghieng va vi tri deu dung ke ca khi phoi
    //   duoc dat xoay trong ban ve.
    // ==========================================================================================

    public class FoilBendLineGeometry
    {
        public FoilBendInfo Bend { get; set; }

        /// <summary>Diem dau trong WCS.</summary>
        public FoilPoint2d Start { get; set; }

        /// <summary>Diem cuoi trong WCS.</summary>
        public FoilPoint2d End { get; set; }

        /// <summary>Vi tri V da dung de dung duong nay.</summary>
        public double FlatPosition { get; set; }

        /// <summary>True neu day la mot trong 2 duong tiep tuyen cua che do TangentPair.</summary>
        public bool IsTangentLine { get; set; }

        public double Length { get { return Start.DistanceTo(End); } }
    }

    /// <summary>Mot buoc chan da duoc dat vao vi tri trong WCS.</summary>
    public class FoilBendStepGeometry
    {
        public FoilBendStep Step { get; set; }

        /// <summary>Duong gap khuc cua buoc, da dat vao vi tri trong WCS.</summary>
        public List<FoilPoint2d> Points { get; private set; } = new List<FoilPoint2d>();

        /// <summary>Duong bao coi (die) cua o nay trong WCS. Rong neu khong ve dung cu.</summary>
        public List<FoilPoint2d> DiePoints { get; private set; } = new List<FoilPoint2d>();

        /// <summary>Duong bao chay dao cua o nay trong WCS. Rong neu khong ve dung cu.</summary>
        public List<FoilPoint2d> PunchPoints { get; private set; } = new List<FoilPoint2d>();

        /// <summary>Vi tri dinh vua chan, trong WCS.</summary>
        public FoilPoint2d Marker { get; set; }

        /// <summary>Goc trai-duoi cua dong chu nhan chinh, trong WCS.</summary>
        public FoilPoint2d LabelPosition { get; set; }

        /// <summary>Goc trai-duoi cua dong chu phu, trong WCS.</summary>
        public FoilPoint2d DetailPosition { get; set; }

        public string Label { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public bool HasMarker { get; set; }
    }

    public class FoilFlatPatternGeometry
    {
        /// <summary>4 dinh hinh chu nhat phoi trong WCS, theo chieu CCW.</summary>
        public List<FoilPoint2d> Outline { get; private set; } = new List<FoilPoint2d>();

        public List<FoilBendLineGeometry> BendLines { get; private set; }
            = new List<FoilBendLineGeometry>();

        /// <summary>Day hinh trinh tu cac buoc chan, dat ben duoi phoi.</summary>
        public List<FoilBendStepGeometry> Steps { get; private set; }
            = new List<FoilBendStepGeometry>();

        /// <summary>Chieu cao chu dung cho nhan cac buoc (da quy doi ra don vi ban ve).</summary>
        public double StepTextHeight { get; set; }

        /// <summary>Phuong an chan da lap (thu tu + canh bao cong nghe). Null neu khong ve buoc.</summary>
        public FoilBendPlan Plan { get; set; }

        public List<string> Warnings { get; private set; } = new List<string>();

        public FoilBlankFrame Frame { get; set; }

        public static FoilFlatPatternGeometry Build(
            FoilFlatPatternResult result,
            FoilPoint2d insertionPoint,
            double rotationRad)
        {
            FoilFlatPatternGeometry geometry = new FoilFlatPatternGeometry();

            if (result == null || !result.IsUsable)
            {
                geometry.Warnings.Add("Khong co ket qua trien khai hop le de sinh hinh hoc.");
                return geometry;
            }

            double length = result.BlankLength;
            double width = result.BlankWidth;

            FoilBlankFrame frame = new FoilBlankFrame(insertionPoint, rotationRad);
            geometry.Frame = frame;

            // Bien phoi trong he cuc bo (U,V), theo chieu CCW.
            List<FoilPoint2d> localOutline = new List<FoilPoint2d>
            {
                new FoilPoint2d(0.0, 0.0),
                new FoilPoint2d(length, 0.0),
                new FoilPoint2d(length, width),
                new FoilPoint2d(0.0, width)
            };

            foreach (FoilPoint2d local in localOutline)
            {
                geometry.Outline.Add(frame.ToWorld(local));
            }

            FoilSettings settings = result.Settings ?? new FoilSettings();

            foreach (FoilBendInfo bend in result.Bends)
            {
                if (settings.BendLineMode == FoilBendLineMode.TangentPair && bend.BendAllowance > 0.0)
                {
                    AddBendLine(geometry, bend, bend.FlatZoneStart, true, localOutline, frame, length);
                    AddBendLine(geometry, bend, bend.FlatZoneEnd, true, localOutline, frame, length);
                }
                else
                {
                    AddBendLine(geometry, bend, bend.FlatPosition, false, localOutline, frame, length);
                }
            }

            if (settings.DrawBendSteps)
            {
                AddBendSteps(geometry, result, settings, frame, length, width);
            }

            return geometry;
        }

        /// <summary>
        /// Xep cac hinh buoc chan thanh mot LUOI ben duoi phoi.
        ///
        /// Ba nguyen tac chong chong hinh:
        ///   1. O luoi duoc tinh theo HOP BAO CHUNG cua ca hinh chi tiet LAN hinh dung cu,
        ///      nen hinh nao cung nam gon trong o cua no.
        ///   2. Be rong o con phai du chua DONG CHU - truoc day chieu cao chu duoc suy tu
        ///      chieu cao hinh, nen voi bien dang cao thi chu dai gap may lan o va de len nhau.
        ///   3. Khi mot hang dai hon chieu dai phoi thi TU DONG XUONG DONG.
        ///
        /// Cac o deu duoc neo theo MAT COI (y = 0 cua he toa do may) nen ca day nhin thang hang.
        /// </summary>
        private static void AddBendSteps(
            FoilFlatPatternGeometry geometry,
            FoilFlatPatternResult result,
            FoilSettings settings,
            FoilBlankFrame frame,
            double blankLength,
            double blankWidth)
        {
            FoilBendPlan plan = FoilBendSequenceBuilder.Plan(result, settings);
            geometry.Plan = plan;

            List<FoilBendStep> steps = plan.Steps;
            if (steps.Count == 0)
            {
                return;
            }

            foreach (string warning in plan.Warnings)
            {
                geometry.Warnings.Add(warning);
            }

            bool drawTooling = settings.DrawTooling;
            FoilToolingGeometry tooling = plan.Tooling ?? FoilToolingGeometry.Resolve(settings);

            List<FoilPoint2d> dieOutline = drawTooling
                ? FoilPressBrakeModel.BuildDieOutline(tooling)
                : new List<FoilPoint2d>();

            // ---- 1. Hinh cua tung o, trong he toa do may ----
            List<List<FoilPoint2d>> shapes = new List<List<FoilPoint2d>>();
            List<List<FoilPoint2d>> punches = new List<List<FoilPoint2d>>();

            double left = 0.0, right = 0.0, down = 0.0, up = 0.0;

            for (int i = 0; i < steps.Count; i++)
            {
                List<FoilPoint2d> shape = FoilBendSequenceBuilder.Simplify(steps[i].MachinePoints);
                shapes.Add(shape);

                double partHeight = 0.0;
                foreach (FoilPoint2d p in shape)
                {
                    if (p.Y > partHeight) partHeight = p.Y;
                }

                List<FoilPoint2d> punch = (drawTooling && steps[i].FormedBend != null)
                    ? FoilPressBrakeModel.BuildPunchOutline(
                        tooling, steps[i].FormedBend.BendAngleRad, partHeight)
                    : new List<FoilPoint2d>();
                punches.Add(punch);

                Measure(shape, ref left, ref right, ref down, ref up);
                Measure(dieOutline, ref left, ref right, ref down, ref up);
                Measure(punch, ref left, ref right, ref down, ref up);
            }

            double cellWidth = left + right;
            double cellHeight = down + up;

            if (cellWidth <= FoilMath.LengthTolerance)
            {
                return;
            }

            // ---- 2. Chieu cao chu: theo KICH THUOC O, khong theo chieu cao hinh ----
            double textHeight = settings.StepTextHeight > 0.0
                ? settings.StepTextHeight
                : Math.Max(cellWidth, cellHeight) * 0.045;

            if (textHeight <= FoilMath.LengthTolerance)
            {
                textHeight = Math.Max(blankWidth * 0.02, 1.0);
            }

            geometry.StepTextHeight = textHeight;

            // ---- 3. O phai du rong cho dong chu dai nhat ----
            int longest = 0;
            foreach (FoilBendStep step in steps)
            {
                if (step.Caption != null && step.Caption.Length > longest) longest = step.Caption.Length;
                if (step.Detail != null && step.Detail.Length > longest) longest = step.Detail.Length;
            }

            // Be rong trung binh cua mot ky tu chu SHX/TTF mac dinh ~ 0.62 lan chieu cao.
            double labelWidth = longest * textHeight * 0.62;
            if (labelWidth > cellWidth)
            {
                cellWidth = labelWidth;
            }

            double gapFactor = settings.StepGapFactor > 0.0 ? settings.StepGapFactor : 0.25;
            double pitchX = cellWidth * (1.0 + gapFactor);
            double labelBlock = textHeight * 3.4;
            double pitchY = cellHeight + labelBlock + cellHeight * gapFactor + textHeight;

            // ---- 4. So cot: xuong dong khi vuot chieu dai phoi ----
            int columns = settings.StepColumns > 0
                ? settings.StepColumns
                : (int)Math.Floor(blankLength / pitchX);

            if (columns < 1) columns = 1;
            if (columns > steps.Count) columns = steps.Count;

            // ---- 5. Dat tung o ----
            double rowGap = Math.Max(blankWidth * 0.12, cellHeight * 0.35);
            double firstRowDieLine = -rowGap - up;   // mat coi cua hang dau

            for (int i = 0; i < steps.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                double originU = column * pitchX + left;
                double originV = firstRowDieLine - row * pitchY;

                FoilBendStepGeometry placed = new FoilBendStepGeometry
                {
                    Step = steps[i],
                    Label = steps[i].Caption,
                    Detail = steps[i].Detail
                };

                foreach (FoilPoint2d p in shapes[i])
                {
                    placed.Points.Add(frame.ToWorld(p.X + originU, p.Y + originV));
                }

                foreach (FoilPoint2d p in dieOutline)
                {
                    placed.DiePoints.Add(frame.ToWorld(p.X + originU, p.Y + originV));
                }

                foreach (FoilPoint2d p in punches[i])
                {
                    placed.PunchPoints.Add(frame.ToWorld(p.X + originU, p.Y + originV));
                }

                if (steps[i].FormedBend != null)
                {
                    // Trong he toa do may, dinh goc chan luon nam tai goc toa do.
                    placed.HasMarker = true;
                    placed.Marker = frame.ToWorld(originU, originV);
                }

                double labelU = column * pitchX;
                double labelV = originV - down - textHeight * 1.6;
                placed.LabelPosition = frame.ToWorld(labelU, labelV);
                placed.DetailPosition = frame.ToWorld(labelU, labelV - textHeight * 1.5);

                geometry.Steps.Add(placed);
            }
        }

        private static void Measure(
            List<FoilPoint2d> points, ref double left, ref double right, ref double down, ref double up)
        {
            foreach (FoilPoint2d p in points)
            {
                if (-p.X > left) left = -p.X;
                if (p.X > right) right = p.X;
                if (-p.Y > down) down = -p.Y;
                if (p.Y > up) up = p.Y;
            }
        }

        private static void AddBendLine(
            FoilFlatPatternGeometry geometry,
            FoilBendInfo bend,
            double flatPosition,
            bool isTangentLine,
            List<FoilPoint2d> localOutline,
            FoilBlankFrame frame,
            double blankLength)
        {
            // Neo tai GIUA chieu dai phoi: voi duong chan xien, diem neo o giua giu duong chan
            // can doi tren phoi. Voi duong chan khong xien, vi tri neo khong anh huong ket qua.
            FoilPoint2d localAnchor = new FoilPoint2d(blankLength * 0.5, flatPosition);
            FoilVector2d localDirection = bend.BendLineDirection;

            FoilPoint2d a;
            FoilPoint2d b;
            if (!FoilLineClipper.ClipInfiniteLine(localAnchor, localDirection, localOutline, out a, out b))
            {
                geometry.Warnings.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Duong chan #{0} (V = {1:0.####}) khong cat duoc bien phoi - da bo qua.",
                    bend.Index,
                    flatPosition));
                return;
            }

            if (a.DistanceTo(b) <= FoilMath.LengthTolerance)
            {
                geometry.Warnings.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Duong chan #{0} (V = {1:0.####}) chi cham bien phoi tai mot diem - da bo qua.",
                    bend.Index,
                    flatPosition));
                return;
            }

            geometry.BendLines.Add(new FoilBendLineGeometry
            {
                Bend = bend,
                Start = frame.ToWorld(a),
                End = frame.ToWorld(b),
                FlatPosition = flatPosition,
                IsTangentLine = isTangentLine
            });
        }
    }
}
