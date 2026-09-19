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

        /// <summary>Vi tri dinh vua chan, trong WCS.</summary>
        public FoilPoint2d Marker { get; set; }

        /// <summary>Goc trai-duoi cua dong chu nhan, trong WCS.</summary>
        public FoilPoint2d LabelPosition { get; set; }

        public string Label { get; set; } = string.Empty;

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
        /// Xep day hinh cac buoc chan thanh MOT HANG ngay ben duoi phoi, trong he (U,V) roi
        /// transform ve WCS cung mot khung voi phoi. Nho vay ca cum di theo goc xoay cua phoi.
        /// </summary>
        private static void AddBendSteps(
            FoilFlatPatternGeometry geometry,
            FoilFlatPatternResult result,
            FoilSettings settings,
            FoilBlankFrame frame,
            double blankLength,
            double blankWidth)
        {
            List<FoilBendStep> steps = FoilBendSequenceBuilder.Build(result, settings.BendSequenceOrder);
            if (steps.Count == 0)
            {
                return;
            }

            double maxWidth = 0.0;
            double maxHeight = 0.0;
            foreach (FoilBendStep step in steps)
            {
                if (step.Width > maxWidth) maxWidth = step.Width;
                if (step.Height > maxHeight) maxHeight = step.Height;
            }

            if (maxWidth <= FoilMath.LengthTolerance)
            {
                return;
            }

            double gapFactor = settings.StepGapFactor > 0.0 ? settings.StepGapFactor : 0.25;
            double gap = maxWidth * gapFactor;
            double pitch = maxWidth + gap;

            double textHeight = settings.StepTextHeight > 0.0
                ? settings.StepTextHeight
                : Math.Max(maxHeight, maxWidth * 0.08) * 0.18;
            geometry.StepTextHeight = textHeight;

            // Hang buoc chan nam duoi canh duoi cua phoi (V = 0), cach ra mot khoang.
            double rowTop = -Math.Max(blankWidth * 0.15, maxHeight * 0.6);
            double labelV = rowTop - maxHeight - textHeight * 1.6;

            for (int i = 0; i < steps.Count; i++)
            {
                FoilBendStep step = steps[i];
                FoilBendStepGeometry placed = new FoilBendStepGeometry
                {
                    Step = step,
                    Label = step.Caption
                };

                // Dat sao cho moi buoc thang hang theo day (V) va cach deu theo (U).
                double offsetU = i * pitch - step.MinX;
                double offsetV = rowTop - maxHeight - step.MinY;

                foreach (FoilPoint2d p in step.Points)
                {
                    placed.Points.Add(frame.ToWorld(p.X + offsetU, p.Y + offsetV));
                }

                if (step.FormedBend != null)
                {
                    placed.HasMarker = true;
                    placed.Marker = frame.ToWorld(
                        step.MarkerPoint.X + offsetU,
                        step.MarkerPoint.Y + offsetV);
                }

                placed.LabelPosition = frame.ToWorld(i * pitch, labelV);
                geometry.Steps.Add(placed);
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
