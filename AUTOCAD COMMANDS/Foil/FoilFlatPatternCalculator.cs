using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - BEND CALCULATION ENGINE (TRAI PHANG)
    // ------------------------------------------------------------------------------------------
    // Bo cuc phoi duoc dung bang MOT bo dem chay doc theo chieu rong trien khai (truc V):
    //
    //   pos = 0
    //   voi moi CANH:
    //       flat = L_moldline - setback_dau - setback_cuoi
    //       pos += flat
    //   voi moi BEND:
    //       zoneStart = pos ; zoneEnd = pos + BA ; pos = zoneEnd
    //       duong chan ve tai tam vung: (zoneStart + zoneEnd) / 2
    //
    // Tong cong:  W = sum(L) - sum(setback) + sum(BA) = sum(L) - sum(BD)   (dong nhat thuc)
    //
    // VI TRI DUONG CHAN VI THE LUON DUOC TICH LUY TU HINH HOC THAT + BU CHAN THAT.
    // Khong bao gio chia deu khoang cach.
    // ==========================================================================================

    public static class FoilFlatPatternCalculator
    {
        public static FoilFlatPatternResult Calculate(
            FoilProfileAnalysis analysis,
            FoilSettings settings,
            IFoilBendStrategy strategy)
        {
            FoilFlatPatternResult result = new FoilFlatPatternResult
            {
                Settings = settings,
                BlankLength = settings != null ? settings.BlankLength : 0.0
            };

            if (analysis == null)
            {
                result.Errors.Add("Khong co ket qua phan tich bien dang.");
                return result;
            }

            result.Notes.AddRange(analysis.Notes);
            result.Warnings.AddRange(analysis.Warnings);

            if (analysis.HasErrors)
            {
                result.Errors.AddRange(analysis.Errors);
                return result;
            }

            if (settings == null || strategy == null)
            {
                result.Errors.Add("Thieu cau hinh hoac chien luoc tinh toan.");
                return result;
            }

            result.MethodName = strategy.DisplayName;
            result.FormulaDescription = strategy.FormulaDescription;

            // ---- 1. Tinh bu chan cho tung bend ----
            foreach (FoilBendInfo bend in analysis.Bends)
            {
                FoilBendCompensation comp;
                try
                {
                    comp = strategy.Compute(new FoilBendRequest
                    {
                        BendAngleRad = bend.BendAngleRad,
                        InsideRadius = bend.InsideRadius,
                        Thickness = bend.Thickness,
                        Settings = settings
                    });
                }
                catch (FoilCalculationException ex)
                {
                    result.Errors.Add(ex.Message);
                    return result;
                }

                bend.KFactor = comp.KFactorUsed;
                bend.BendAllowance = comp.BendAllowance;
                bend.OutsideSetback = comp.OutsideSetback;
                bend.BendDeduction = comp.BendDeduction;
                bend.AppliedSetbackPrev = bend.SetbackAppliesPrev ? comp.OutsideSetback : 0.0;
                bend.AppliedSetbackNext = bend.SetbackAppliesNext ? comp.OutsideSetback : 0.0;
                bend.MoldLineOffset = ComputeMoldLineOffset(bend, settings);
                bend.CalculationNote = comp.Note;

                result.Bends.Add(bend);
            }

            // Tong mold line duoc tinh lai SAU khi quy doi tu mat trong (neu can).
            result.MoldLineTotalLength = 0.0;

            // ---- 2. Bo cuc doc theo truc V ----
            List<FoilProfileElement> elements = analysis.Elements;
            double position = 0.0;

            for (int i = 0; i < elements.Count; i++)
            {
                FoilProfileElement element = elements[i];

                if (element.Kind == FoilElementKind.Flange)
                {
                    // BUOC 1: quy kich thuoc VE ra MOLD LINE NGOAI.
                    //   Vi du canh ve 15 mm co 1 duong chan 90 do, T = 1.2:
                    //       mold line = 15 + 1.2 = 16.2
                    //   canh ve 34 mm co 2 duong chan 90 do:
                    //       mold line = 34 + 2 * 1.2 = 36.4
                    double moldLine = element.MoldLineLength
                                      + MoldLineOffsetBefore(elements, i)
                                      + MoldLineOffsetAfter(elements, i);
                    element.EffectiveMoldLineLength = moldLine;
                    result.MoldLineTotalLength += moldLine;

                    // BUOC 2: tru bu chan tren kich thuoc MOLD LINE.
                    double trimStart = SetbackBefore(elements, i);
                    double trimEnd = SetbackAfter(elements, i);
                    double flatLength = moldLine - trimStart - trimEnd;

                    if (flatLength < -settings.DuplicatePointTolerance)
                    {
                        result.Errors.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Canh #{0} dai {1:0.####} mm (mold line {2:0.####} mm) nhung tong setback hai dau " +
                            "la {3:0.####} mm => chieu dai trien khai am ({4:0.####} mm). Canh qua ngan so voi " +
                            "ban kinh chan R hoac he so bu qua lon.",
                            element.SegmentIndex + 1,
                            element.MoldLineLength,
                            moldLine,
                            trimStart + trimEnd,
                            flatLength));
                        return result;
                    }

                    if (flatLength < 0.0)
                    {
                        flatLength = 0.0;
                    }

                    element.FlatStart = position;
                    position += flatLength;
                    element.FlatEnd = position;
                }
                else
                {
                    FoilBendInfo bend = element.Bend;
                    element.FlatStart = position;
                    bend.FlatZoneStart = position;
                    position += bend.BendAllowance;
                    bend.FlatZoneEnd = position;
                    element.FlatEnd = position;

                    // Duong chan duoc ve tai TAM vung chan.
                    // Voi BA = 0 (quy tac xuong / bang chan khong co cot Allowance) tam vung
                    // trung voi chinh diem do, dung nhu mong doi.
                    bend.FlatPosition = (bend.FlatZoneStart + bend.FlatZoneEnd) * 0.5;
                }

                result.Elements.Add(element);
            }

            result.BlankWidth = position;

            // ---- 3. Kiem tra ket qua ----
            if (result.BlankWidth <= 0.0)
            {
                result.Errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Chieu rong phoi tinh ra khong hop le ({0:0.####} mm). Kiem tra lai bien dang, " +
                    "chieu day va he so bu chan.",
                    result.BlankWidth));
                return result;
            }

            if (result.BlankLength <= 0.0)
            {
                result.Errors.Add("Chieu dai phoi phai lon hon 0.");
                return result;
            }

            // Kiem tra dong nhat thuc: W phai bang sum(L) - sum(bu thuc te).
            double expected = result.MoldLineTotalLength;
            foreach (FoilBendInfo b in result.Bends)
            {
                expected -= b.EffectiveDeduction;
            }

            if (Math.Abs(expected - result.BlankWidth) > 1e-6)
            {
                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Kiem tra noi bo: chieu rong bo cuc ({0:0.######}) lech so voi tong ly thuyet " +
                    "({1:0.######}). Hay bao loi nay.",
                    result.BlankWidth,
                    expected));
            }

            foreach (FoilBendInfo b in result.Bends)
            {
                if (b.FlatPosition < 0.0 || b.FlatPosition > result.BlankWidth)
                {
                    result.Warnings.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Duong chan #{0} nam ngoai pham vi phoi (V = {1:0.####}, chieu rong = {2:0.####}).",
                        b.Index, b.FlatPosition, result.BlankWidth));
                }
            }

            return result;
        }

        /// <summary>
        /// Luong cong them tai GOC LOM de di tu duong bao ngoai ra DUONG CHAN THAT,
        /// tinh cho MOT dau canh:
        ///     offset = T * tan(alpha / 2)
        /// Chan vuong goc => tan(45) = 1 => offset = T, dung nhu quy uoc "1 vong tron = 1 be day".
        ///
        /// CHI ap cho nhung duong chan thuoc huong da chon (goc lom). Cac goc LOI con lai giu
        /// nguyen nhu cu - day chinh la cho truoc day tinh sai vi da cong cho MOI goc.
        ///
        /// Khong ap cho bend la CUNG ve san: canh da duoc cat den diem tiep tuyen, khong co
        /// dinh mold line de keo dai.
        /// </summary>
        private static double ComputeMoldLineOffset(FoilBendInfo bend, FoilSettings settings)
        {
            if (bend.Kind != FoilBendKind.SharpVertex)
            {
                return 0.0;
            }

            // Bam vao DAU GOC RE, KHONG bam vao nhan UP/DOWN.
            // Nhan UP/DOWN co the bi nguoi dung lat bang InvertBendDirection cho hop quy uoc
            // xuong; neu bam vao nhan thi doi mot tuy chon HIEN THI se lam doi KICH THUOC PHOI.
            bool compensate;
            switch (settings.ThicknessCompensation)
            {
                case FoilThicknessCompensationMode.AllBends:
                    compensate = true;
                    break;
                case FoilThicknessCompensationMode.TurnLeft:
                    compensate = bend.TurnSign >= 0.0;
                    break;
                case FoilThicknessCompensationMode.TurnRight:
                    compensate = bend.TurnSign < 0.0;
                    break;
                case FoilThicknessCompensationMode.None:
                default:
                    compensate = false;
                    break;
            }

            if (!compensate)
            {
                return 0.0;
            }

            double halfAngle = FoilMath.Clamp(
                bend.BendAngleRad * 0.5, 0.0, settings.MaxBendAngleRad * 0.5);

            return bend.Thickness * Math.Tan(halfAngle);
        }

        private static double MoldLineOffsetBefore(List<FoilProfileElement> elements, int flangeIndex)
        {
            if (flangeIndex <= 0)
            {
                return 0.0;
            }

            FoilProfileElement previous = elements[flangeIndex - 1];
            if (previous.Kind != FoilElementKind.Bend || !previous.Bend.SetbackAppliesNext)
            {
                return 0.0;
            }

            return previous.Bend.MoldLineOffset;
        }

        private static double MoldLineOffsetAfter(List<FoilProfileElement> elements, int flangeIndex)
        {
            if (flangeIndex >= elements.Count - 1)
            {
                return 0.0;
            }

            FoilProfileElement next = elements[flangeIndex + 1];
            if (next.Kind != FoilElementKind.Bend || !next.Bend.SetbackAppliesPrev)
            {
                return 0.0;
            }

            return next.Bend.MoldLineOffset;
        }

        /// <summary>Setback ap vao DAU canh, lay tu bend dung ngay truoc canh do (neu co).</summary>
        private static double SetbackBefore(List<FoilProfileElement> elements, int flangeIndex)
        {
            if (flangeIndex <= 0)
            {
                return 0.0;
            }

            FoilProfileElement previous = elements[flangeIndex - 1];
            if (previous.Kind != FoilElementKind.Bend)
            {
                return 0.0;
            }

            return previous.Bend.AppliedSetbackNext;
        }

        /// <summary>Setback ap vao CUOI canh, lay tu bend dung ngay sau canh do (neu co).</summary>
        private static double SetbackAfter(List<FoilProfileElement> elements, int flangeIndex)
        {
            if (flangeIndex >= elements.Count - 1)
            {
                return 0.0;
            }

            FoilProfileElement next = elements[flangeIndex + 1];
            if (next.Kind != FoilElementKind.Bend)
            {
                return 0.0;
            }

            return next.Bend.AppliedSetbackPrev;
        }
    }
}
