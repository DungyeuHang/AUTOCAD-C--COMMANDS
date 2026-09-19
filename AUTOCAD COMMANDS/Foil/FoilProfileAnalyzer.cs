using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - GEOMETRY PROCESSOR
    // ------------------------------------------------------------------------------------------
    // Bien FoilProfile thanh chuoi phan tu xen ke:  Flange - Bend - Flange - Bend - Flange ...
    //
    // NGUYEN TAC: KHONG bao gio dua vao "doan nao thang dung thi tru chieu day". Bend duoc xac
    // dinh hoan toan bang hinh hoc:
    //   * Tai moi DINH noi 2 doan: so sanh VECTOR TIEP TUYEN ra/vao. Goc giua chung chinh la
    //     BEND ANGLE alpha. alpha ~ 0 => di thang, khong phai bend.
    //   * Moi doan CUNG (bulge != 0) tu no da la mot bend co ban kinh va goc quet that.
    // Nho vay bien dang U / C / Z / nhieu bac / goc nhon / goc tu deu dung mot duong code.
    // ==========================================================================================

    public enum FoilElementKind
    {
        Flange = 0,
        Bend = 1
    }

    public class FoilProfileElement
    {
        public FoilElementKind Kind { get; set; }

        // ----- Flange -----

        /// <summary>Chieu dai canh DO TRUC TIEP TREN POLYLINE (chua quy doi mat).</summary>
        public double MoldLineLength { get; set; }

        /// <summary>
        /// Chieu dai canh theo MOLD LINE NGOAI, tuc da cong be day neu polyline ve theo mat trong.
        /// Do FoilFlatPatternCalculator dien.
        /// </summary>
        public double EffectiveMoldLineLength { get; set; }

        public int SegmentIndex { get; set; } = -1;

        public FoilPoint2d Start { get; set; }

        public FoilPoint2d End { get; set; }

        public FoilVector2d Direction { get; set; }

        // ----- Bend -----

        public FoilBendInfo Bend { get; set; }

        // ----- Ket qua trai phang (do FoilFlatPatternCalculator dien) -----

        /// <summary>Vi tri V bat dau cua phan tu tren phoi.</summary>
        public double FlatStart { get; set; }

        /// <summary>Vi tri V ket thuc cua phan tu tren phoi.</summary>
        public double FlatEnd { get; set; }

        /// <summary>Chieu dai phan tu sau khi trai phang.</summary>
        public double FlatLength { get { return FlatEnd - FlatStart; } }
    }

    public class FoilProfileAnalysis
    {
        public FoilProfile NormalizedProfile { get; set; }

        public List<FoilProfileElement> Elements { get; private set; }
            = new List<FoilProfileElement>();

        public List<FoilBendInfo> Bends { get; private set; }
            = new List<FoilBendInfo>();

        public List<string> Notes { get; private set; } = new List<string>();

        public List<string> Warnings { get; private set; } = new List<string>();

        public List<string> Errors { get; private set; } = new List<string>();

        public bool HasErrors { get { return Errors.Count > 0; } }

        /// <summary>Tong chieu dai bien dang theo mold line (chua tru bu chan).</summary>
        public double MoldLineTotalLength { get; set; }

        public bool WasClosed { get; set; }
    }

    public static class FoilProfileAnalyzer
    {
        /// <summary>
        /// Phan tich bien dang. <paramref name="profile"/> nen la profile THO doc tu polyline;
        /// ham nay tu chuan hoa huong duyet va tu mo vong kin.
        /// </summary>
        public static FoilProfileAnalysis Analyze(FoilProfile profile, FoilSettings settings)
        {
            FoilProfileAnalysis result = new FoilProfileAnalysis();

            if (profile == null || profile.SegmentCount == 0)
            {
                result.Errors.Add("Bien dang rong hoac khong hop le.");
                return result;
            }

            if (settings == null)
            {
                result.Errors.Add("Thieu cau hinh tinh toan.");
                return result;
            }

            result.WasClosed = profile.Closed;

            // ---- 1. Vong kin: canh bao neu day la duong bao CO BE DAY ----
            if (profile.Closed)
            {
                double impliedThickness;
                if (FoilProfileBuilder.LooksLikeThicknessOutline(
                        profile, settings.Thickness, out impliedThickness))
                {
                    string message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Polyline kin nay trong giong DUONG BAO CO BE DAY (be day suy ra ~ {0:0.###} mm, " +
                        "gan bang chieu day ton {1:0.###} mm). DX_FOIL can bien dang MOT NET (duong tam " +
                        "hoac mold line), khong phai duong bao 2 mat.",
                        impliedThickness,
                        settings.Thickness);

                    if (!settings.AllowThicknessOutlineInput)
                    {
                        result.Errors.Add(message +
                            " Hay ve lai bien dang mot net, hoac bat tuy chon \"Cho phep duong bao co be day\" " +
                            "trong cai dat neu ban chac chan.");
                        return result;
                    }

                    result.Warnings.Add(message + " Tuy chon cho phep dang BAT - ket qua co the sai.");
                }
            }

            // ---- 2. Kiem tra bien dang tu cat (chi canh bao) ----
            int selfIntersections = FoilProfileBuilder.CountSelfIntersections(
                profile, settings.DuplicatePointTolerance);
            if (selfIntersections > 0)
            {
                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Phat hien {0} diem TU CAT tren bien dang. Mot tiet dien ton thuc te khong the tu cat - " +
                    "hay kiem tra lai polyline truoc khi tao phoi.",
                    selfIntersections));
            }

            // ---- 3. Chuan hoa huong duyet roi mo vong kin ----
            FoilProfile working = FoilProfileBuilder.NormalizeOrientation(
                profile, settings.DuplicatePointTolerance, result.Notes);
            working = FoilProfileBuilder.OpenClosedLoop(working, result.Notes);
            result.NormalizedProfile = working;

            // ---- 4. Duyet tao phan tu ----
            int n = working.SegmentCount;
            int bendIndex = 0;

            for (int i = 0; i < n; i++)
            {
                FoilProfileSegment seg = working.Segments[i];

                if (seg.IsArc)
                {
                    bendIndex++;
                    FoilBendInfo arcBend = BuildArcBend(seg, i, bendIndex, settings, result);
                    if (arcBend == null)
                    {
                        return result;
                    }

                    result.Bends.Add(arcBend);
                    result.Elements.Add(new FoilProfileElement
                    {
                        Kind = FoilElementKind.Bend,
                        Bend = arcBend,
                        SegmentIndex = i
                    });
                }
                else
                {
                    result.Elements.Add(new FoilProfileElement
                    {
                        Kind = FoilElementKind.Flange,
                        MoldLineLength = seg.Length,
                        SegmentIndex = i,
                        Start = seg.Start,
                        End = seg.End,
                        Direction = seg.StartDirection
                    });

                    result.MoldLineTotalLength += seg.Length;
                }

                // Dinh noi giua doan i va doan i+1.
                if (i < n - 1)
                {
                    FoilProfileSegment next = working.Segments[i + 1];
                    FoilVector2d incoming = seg.EndDirection;
                    FoilVector2d outgoing = next.StartDirection;

                    double alpha = FoilMath.AngleBetween(incoming, outgoing);
                    if (alpha <= settings.MinBendAngleRad)
                    {
                        // Tiep tuyen lien tuc: day chi la diem noi hinh hoc, khong phai bend.
                        continue;
                    }

                    if (alpha >= settings.MaxBendAngleRad)
                    {
                        result.Errors.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Dinh tai {0} co goc chan {1:0.##} do, vuot nguong cho phep {2:0.##} do. " +
                            "Bien dang gap nguoc lai khong the chan duoc.",
                            seg.End,
                            alpha * FoilMath.RadToDeg,
                            settings.MaxBendAngleDeg));
                        return result;
                    }

                    bendIndex++;
                    result.Bends.Add(BuildSharpBend(
                        seg, next, i, bendIndex, alpha, incoming, outgoing, settings, result));
                    result.Elements.Add(new FoilProfileElement
                    {
                        Kind = FoilElementKind.Bend,
                        Bend = result.Bends[result.Bends.Count - 1],
                        SegmentIndex = i
                    });
                }
            }

            if (result.Bends.Count == 0)
            {
                result.Notes.Add("Bien dang khong co duong chan nao - phoi se la tam phang.");
            }

            return result;
        }

        private static FoilBendInfo BuildSharpBend(
            FoilProfileSegment previous,
            FoilProfileSegment next,
            int previousSegmentIndex,
            int bendIndex,
            double alpha,
            FoilVector2d incoming,
            FoilVector2d outgoing,
            FoilSettings settings,
            FoilProfileAnalysis result)
        {
            // Dung nguong CROSS rat nho, KHONG dung MinBendAngle: xem ghi chu trong FoilMath.TurnSign.
            // Tai day alpha da duoc xac nhan nam trong (MinBendAngle, MaxBendAngle) nen cross chi
            // bang 0 khi hai vector thuc su cung phuong.
            double turnSign = FoilMath.TurnSign(incoming, outgoing, FoilMath.AngleTolerance);

            // Phap tuyen cuc bo: tro vao PHIA LOM cua goc (phia tam cong).
            FoilVector2d localNormal = (outgoing - incoming).Normalized();

            FoilBendInfo bend = new FoilBendInfo
            {
                Index = bendIndex,
                Kind = FoilBendKind.SharpVertex,
                Vertex = previous.End,
                PreviousSegmentIndex = previousSegmentIndex,
                NextSegmentIndex = previousSegmentIndex + 1,
                IncomingDirection = incoming,
                OutgoingDirection = outgoing,
                BendAngleRad = alpha,
                TurnSign = turnSign,
                LocalNormal = localNormal,
                InsideRadius = settings.InsideRadius,
                Thickness = settings.Thickness,
                SkewAngleRad = 0.0,
                SourceGeometry = string.Format(
                    CultureInfo.InvariantCulture,
                    "Dinh goc nhon giua doan #{0} va #{1} tai {2}",
                    previousSegmentIndex + 1,
                    previousSegmentIndex + 2,
                    previous.End)
            };

            bend.Direction = ResolveDirection(turnSign, settings);

            // Setback chi tru vao phan CANH THANG. Neu canh ke la cung thi cung do da mang
            // chieu dai trien khai chinh xac cua rieng no, khong duoc tru them lan nua.
            bend.SetbackAppliesPrev = !previous.IsArc;
            bend.SetbackAppliesNext = !next.IsArc;

            if (previous.IsArc || next.IsArc)
            {
                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Duong chan #{0}: mot canh ke la CUNG. Setback chi ap dung cho canh thang.",
                    bendIndex));
            }

            return bend;
        }

        private static FoilBendInfo BuildArcBend(
            FoilProfileSegment arc,
            int segmentIndex,
            int bendIndex,
            FoilSettings settings,
            FoilProfileAnalysis result)
        {
            double alpha = Math.Abs(arc.SweepAngle);

            if (alpha >= settings.MaxBendAngleRad)
            {
                result.Errors.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Cung #{0} co goc quet {1:0.##} do, vuot nguong goc chan cho phep {2:0.##} do.",
                    segmentIndex + 1,
                    alpha * FoilMath.RadToDeg,
                    settings.MaxBendAngleDeg));
                return null;
            }

            double insideRadius = ResolveArcInsideRadius(arc.Radius, settings);
            if (insideRadius < 0.0)
            {
                result.Warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Cung #{0} co ban kinh ve {1:0.###} mm nho hon chieu day ton - ban kinh chan trong " +
                    "bi kep ve 0. Kiem tra lai tuy chon \"Y nghia ban kinh cung\".",
                    segmentIndex + 1,
                    arc.Radius));
                insideRadius = 0.0;
            }

            double turnSign = arc.SweepAngle >= 0.0 ? 1.0 : -1.0;

            FoilBendInfo bend = new FoilBendInfo
            {
                Index = bendIndex,
                Kind = FoilBendKind.Arc,
                Vertex = arc.MidPoint(),
                PreviousSegmentIndex = segmentIndex - 1,
                NextSegmentIndex = segmentIndex + 1,
                IncomingDirection = arc.StartDirection,
                OutgoingDirection = arc.EndDirection,
                BendAngleRad = alpha,
                TurnSign = turnSign,
                LocalNormal = (arc.Center - arc.MidPoint()).Normalized(),
                InsideRadius = insideRadius,
                Thickness = settings.Thickness,
                SkewAngleRad = 0.0,

                // Cung tu no DA la vung chan: canh ke da duoc cat den diem tiep tuyen,
                // nen khong ap setback o ca hai ben.
                SetbackAppliesPrev = false,
                SetbackAppliesNext = false,

                SourceGeometry = string.Format(
                    CultureInfo.InvariantCulture,
                    "Cung ve san #{0}: R_ve = {1:0.####}, goc quet = {2:0.##} do",
                    segmentIndex + 1,
                    arc.Radius,
                    arc.SweepAngle * FoilMath.RadToDeg)
            };

            bend.Direction = ResolveDirection(turnSign, settings);
            return bend;
        }

        private static double ResolveArcInsideRadius(double drawnRadius, FoilSettings settings)
        {
            switch (settings.ArcRadiusInterpretation)
            {
                case FoilArcRadiusInterpretation.OutsideRadius:
                    return drawnRadius - settings.Thickness;
                case FoilArcRadiusInterpretation.MidRadius:
                    return drawnRadius - settings.Thickness * 0.5;
                case FoilArcRadiusInterpretation.InsideRadius:
                default:
                    return drawnRadius;
            }
        }

        /// <summary>
        /// Huong chan duoc suy tu DAU CUA GOC RE theo chieu duyet DA CHUAN HOA, nen ket qua
        /// khong phu thuoc vao viec nguoi dung ve polyline theo chieu nao (CW hay CCW).
        /// Neu quy uoc cua xuong nguoc lai, bat FoilSettings.InvertBendDirection de dao toan bo.
        /// </summary>
        private static FoilBendDirection ResolveDirection(double turnSign, FoilSettings settings)
        {
            bool up = turnSign >= 0.0;
            if (settings.InvertBendDirection)
            {
                up = !up;
            }

            return up ? FoilBendDirection.Up : FoilBendDirection.Down;
        }
    }
}
