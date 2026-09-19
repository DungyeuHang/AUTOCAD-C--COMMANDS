using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - TRINH TU CAC BUOC CHAN (BEND SEQUENCE)
    // ------------------------------------------------------------------------------------------
    // Sinh hinh dang MAT CAT sau TUNG lan chan, de tho dung tai may chan.
    //
    // MO HINH VAT LY (quan trong - day la cho de sai nhat):
    //   Vat lieu KHONG doi khi chan. Mot canh luon co chieu dai vat lieu la FlatLength
    //   (do tu tiep diem den tiep diem), va moi vung chan luon co chieu dai vat lieu la BA.
    //
    //   * Bend CHUA chan : vung chan BA nam THANG  -> ve them mot doan dai BA.
    //   * Bend DA chan   : vung chan cuon lai      -> KHONG ve doan nao, chi XOAY huong di,
    //                      va hai canh ke duoc KEO DAI them OSSB de gap nhau tai DINH MOLD LINE.
    //
    // Nho mo hinh nay hai dau cua day trinh tu deu TU DONG dung:
    //   Buoc 0 : chua chan gi  -> tong chieu dai = sum(FlatLength) + sum(BA) = CHIEU RONG PHOI
    //   Buoc N : chan het      -> moi canh = chieu dai MOLD LINE = dung bang bien dang goc
    //
    // Day la ly do KHONG duoc ve trinh tu bang cach "noi cac canh mold line roi be dan": lam vay
    // buoc 0 se dai hon phoi that dung bang tong bu chan.
    // ==========================================================================================

    public enum FoilBendSequenceOrder
    {
        /// <summary>Chan lan luot tu dau bien dang den cuoi (mac dinh).</summary>
        ProfileOrder = 0,

        /// <summary>Chan nguoc lai, tu cuoi bien dang ve dau.</summary>
        Reverse = 1,

        /// <summary>Chan xen ke tu hai dau vao giua - cach pho bien voi bien dang mu / chu C.</summary>
        OutsideIn = 2
    }

    public class FoilBendStep
    {
        /// <summary>0 = phoi phang chua chan; 1..N = sau lan chan thu k.</summary>
        public int StepNumber { get; set; }

        /// <summary>Duong chan duoc thuc hien o buoc nay (null voi buoc 0).</summary>
        public FoilBendInfo FormedBend { get; set; }

        /// <summary>Duong gap khuc mo ta mat cat sau buoc nay, trong he toa do cuc bo cua buoc.</summary>
        public List<FoilPoint2d> Points { get; private set; } = new List<FoilPoint2d>();

        /// <summary>Vi tri dinh vua duoc chan (de danh dau tren ban ve).</summary>
        public FoilPoint2d MarkerPoint { get; set; }

        public double MinX { get; set; }

        public double MinY { get; set; }

        public double MaxX { get; set; }

        public double MaxY { get; set; }

        public double Width { get { return MaxX - MinX; } }

        public double Height { get { return MaxY - MinY; } }

        /// <summary>Tong chieu dai duong gap khuc cua buoc nay.</summary>
        public double OutlineLength { get; set; }

        public string Caption { get; set; } = string.Empty;
    }

    public static class FoilBendSequenceBuilder
    {
        /// <summary>
        /// Thu tu chan: tra ve danh sach INDEX (1-based) cua duong chan theo thu tu thuc hien.
        /// </summary>
        public static List<int> BuildOrder(int bendCount, FoilBendSequenceOrder order)
        {
            List<int> sequence = new List<int>(bendCount);

            switch (order)
            {
                case FoilBendSequenceOrder.Reverse:
                    for (int i = bendCount; i >= 1; i--)
                    {
                        sequence.Add(i);
                    }

                    break;

                case FoilBendSequenceOrder.OutsideIn:
                    int low = 1;
                    int high = bendCount;
                    while (low <= high)
                    {
                        sequence.Add(low);
                        if (high != low)
                        {
                            sequence.Add(high);
                        }

                        low++;
                        high--;
                    }

                    break;

                case FoilBendSequenceOrder.ProfileOrder:
                default:
                    for (int i = 1; i <= bendCount; i++)
                    {
                        sequence.Add(i);
                    }

                    break;
            }

            return sequence;
        }

        /// <summary>
        /// Sinh toan bo cac buoc: buoc 0 (phoi phang) roi lan luot sau moi lan chan.
        /// Tra ve danh sach rong neu ket qua khong dung duoc.
        /// </summary>
        public static List<FoilBendStep> Build(
            FoilFlatPatternResult result, FoilBendSequenceOrder order)
        {
            List<FoilBendStep> steps = new List<FoilBendStep>();
            if (result == null || !result.IsUsable)
            {
                return steps;
            }

            FoilSettings settings = result.Settings ?? new FoilSettings();
            List<int> sequence = BuildOrder(result.BendCount, order);
            HashSet<int> formed = new HashSet<int>();

            // Buoc 0: phoi phang.
            steps.Add(BuildStep(result, formed, 0, null, settings));

            for (int k = 0; k < sequence.Count; k++)
            {
                int bendIndex = sequence[k];
                formed.Add(bendIndex);

                FoilBendInfo bend = null;
                foreach (FoilBendInfo candidate in result.Bends)
                {
                    if (candidate.Index == bendIndex)
                    {
                        bend = candidate;
                        break;
                    }
                }

                steps.Add(BuildStep(result, formed, k + 1, bend, settings));
            }

            return steps;
        }

        private static FoilBendStep BuildStep(
            FoilFlatPatternResult result,
            HashSet<int> formed,
            int stepNumber,
            FoilBendInfo formedBend,
            FoilSettings settings)
        {
            FoilBendStep step = new FoilBendStep
            {
                StepNumber = stepNumber,
                FormedBend = formedBend
            };

            List<FoilProfileElement> elements = result.Elements;
            FoilPoint2d current = new FoilPoint2d(0.0, 0.0);
            FoilVector2d direction = new FoilVector2d(1.0, 0.0);

            step.Points.Add(current);

            for (int i = 0; i < elements.Count; i++)
            {
                FoilProfileElement element = elements[i];

                if (element.Kind == FoilElementKind.Flange)
                {
                    double length = element.FlatLength;

                    // Bend da chan thi hai canh ke duoc keo dai den DINH MOLD LINE.
                    FoilBendInfo before = NeighbourBend(elements, i - 1);
                    if (before != null && formed.Contains(before.Index))
                    {
                        length += before.AppliedSetbackNext;
                    }

                    FoilBendInfo after = NeighbourBend(elements, i + 1);
                    if (after != null && formed.Contains(after.Index))
                    {
                        length += after.AppliedSetbackPrev;
                    }

                    current = Advance(step, current, direction, length);
                }
                else
                {
                    FoilBendInfo bend = element.Bend;
                    if (formed.Contains(bend.Index))
                    {
                        // Da chan: vung chan cuon lai thanh goc, chi doi huong di.
                        double turn = bend.TurnSign >= 0.0 ? bend.BendAngleRad : -bend.BendAngleRad;
                        direction = direction.Rotate(turn);

                        if (formedBend != null && bend.Index == formedBend.Index)
                        {
                            step.MarkerPoint = current;
                        }
                    }
                    else
                    {
                        // Chua chan: vung chan con nam thang, van chiem dung BA vat lieu.
                        current = Advance(step, current, direction, bend.BendAllowance);
                    }
                }
            }

            RemoveCollinearPoints(step);
            ComputeBounds(step);
            step.Caption = BuildCaption(step, result, settings);

            if (formedBend == null && step.Points.Count > 0)
            {
                step.MarkerPoint = step.Points[0];
            }

            return step;
        }

        private static FoilBendInfo NeighbourBend(List<FoilProfileElement> elements, int index)
        {
            if (index < 0 || index >= elements.Count)
            {
                return null;
            }

            FoilProfileElement element = elements[index];
            return element.Kind == FoilElementKind.Bend ? element.Bend : null;
        }

        private static FoilPoint2d Advance(
            FoilBendStep step, FoilPoint2d current, FoilVector2d direction, double length)
        {
            if (length <= FoilMath.LengthTolerance)
            {
                return current;
            }

            FoilPoint2d next = current + direction * length;
            step.Points.Add(next);
            step.OutlineLength += length;
            return next;
        }

        /// <summary>
        /// Gop cac diem THANG HANG lai. Phan chua chan gom nhieu doan noi tiep cung huong
        /// (canh + vung chan con phang), neu giu nguyen thi polyline se co dinh thua.
        /// Chi bo diem khong co goc re that, nen hinh dang va chieu dai KHONG doi.
        /// </summary>
        private static void RemoveCollinearPoints(FoilBendStep step)
        {
            const double collinearTolerance = 1e-9;

            for (int i = step.Points.Count - 2; i >= 1; i--)
            {
                FoilVector2d incoming = step.Points[i] - step.Points[i - 1];
                FoilVector2d outgoing = step.Points[i + 1] - step.Points[i];

                if (FoilMath.AngleBetween(incoming, outgoing) <= collinearTolerance)
                {
                    step.Points.RemoveAt(i);
                }
            }
        }

        private static void ComputeBounds(FoilBendStep step)
        {
            step.MinX = double.MaxValue;
            step.MinY = double.MaxValue;
            step.MaxX = double.MinValue;
            step.MaxY = double.MinValue;

            foreach (FoilPoint2d p in step.Points)
            {
                if (p.X < step.MinX) step.MinX = p.X;
                if (p.Y < step.MinY) step.MinY = p.Y;
                if (p.X > step.MaxX) step.MaxX = p.X;
                if (p.Y > step.MaxY) step.MaxY = p.Y;
            }

            if (step.Points.Count == 0)
            {
                step.MinX = step.MinY = step.MaxX = step.MaxY = 0.0;
            }
        }

        private static string BuildCaption(
            FoilBendStep step, FoilFlatPatternResult result, FoilSettings settings)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string f = settings.DisplayFormat;

            if (step.FormedBend == null)
            {
                return string.Format(
                    ci,
                    "B0 - PHOI PHANG  {0} x {1}",
                    result.BlankLength.ToString(f, ci),
                    result.BlankWidth.ToString(f, ci));
            }

            return string.Format(
                ci,
                "B{0} - chan #{1}  {2} do  {3}  (vi tri {4})",
                step.StepNumber,
                step.FormedBend.Index,
                step.FormedBend.BendAngleDeg.ToString(f, ci),
                step.FormedBend.Direction == FoilBendDirection.Up ? "UP" : "DOWN",
                step.FormedBend.FlatPosition.ToString(f, ci));
        }
    }
}
