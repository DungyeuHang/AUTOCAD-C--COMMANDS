using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - ENGINE BO TRI (buoc PLAN)
    // ------------------------------------------------------------------------------------------
    // Day la phan quan trong nhat cua lenh. Nguyen tac:
    //
    //   1. Voi tung doan, cham diem CA HAI PHIA kha di roi chon phia tot hon:
    //        - chieu dai duong giong
    //        - so lan duong giong cat qua bien dang
    //        - phap tuyen huong ra ngoai cua doan (voi bien dang kin)
    //        - can bang so luong dim hai phia
    //   2. Feature nam sat mep ngoai -> dat o BANG ngoai bao hinh.
    //      Feature nam sau ben trong -> thu dat NGAY CANH no (giong dim tay) de khong phai keo
    //      duong giong dai loe ngoe. Vi tri cuc bo chi duoc nhan khi da kiem tra:
    //        - duong kich thuoc khong cat qua net ve
    //        - hai duong giong khong cat qua net ve
    //        - text khong cham text cua dim khac
    //      Khong dat duoc thi TU DONG quay ve bang ngoai -> khong bao gio "lieu".
    //   3. Cung mot bang, cac dim duoc XEP HANG bang thuat toan xep khoang (interval packing):
    //      hai dim chi duoc chung mot hang khi CA khoang do CA hop text deu khong de len nhau.
    //      Nho vay dim noi tiep nhau (dang chuoi) van nam chung mot hang dung nhu ve tay,
    //      con dim chong lan nhau thi tu dong day ra hang ngoai.
    //   4. Dim bao tong the luon nam o hang NGOAI CUNG cua phia no, cach tang feature mot
    //      khoang ho rieng.
    //
    // File thuan, khong tham chieu AutoCAD.
    // ==========================================================================================

    public static class DimPlinePlanner
    {
        // Trong so cham diem chon phia. Thanh phan chieu dai duong giong duoc chuan hoa theo
        // duong cheo bao hinh nen cac trong so nay khong phu thuoc do lon ban ve.
        private const double WeightExtensionLength = 1.00;
        private const double WeightCrossing = 0.30;
        private const double WeightOutwardNormal = 0.55;
        private const double WeightBalance = 0.35;

        private const int MaxNearFeatureSteps = 3;
        private const int MaxSkewPushOutSteps = 8;
        private const int MaxOverallPushOutRows = 6;

        public static DimPlinePlan Plan(
            DimPlineInput input,
            AutoDimPlineSettings settings,
            DimPlineStyle style)
        {
            AutoDimPlineSettings cfg = settings ?? new AutoDimPlineSettings();
            DimPlinePlan plan = new DimPlinePlan();

            List<string> errors = cfg.Validate();
            if (errors.Count > 0)
            {
                plan.ErrorMessage = string.Join(" ", errors.ToArray());
                return plan;
            }

            DimPlineAnalysis analysis = DimPlineAnalyzer.Analyze(input, cfg);
            if (!analysis.IsValid)
            {
                plan.Analysis = analysis;
                plan.ErrorMessage = analysis.ErrorMessage;
                return plan;
            }

            return Plan(analysis, cfg, style);
        }

        public static DimPlinePlan Plan(
            DimPlineAnalysis analysis,
            AutoDimPlineSettings settings,
            DimPlineStyle style)
        {
            AutoDimPlineSettings cfg = settings ?? new AutoDimPlineSettings();
            DimPlineStyle st = style ?? new DimPlineStyle();
            DimPlinePlan plan = new DimPlinePlan();
            plan.Analysis = analysis;

            if (analysis == null || !analysis.IsValid)
            {
                plan.ErrorMessage = analysis != null ? analysis.ErrorMessage : "Khong phan tich duoc polyline.";
                return plan;
            }

            if (!analysis.Bounds.IsValid)
            {
                plan.ErrorMessage = "Polyline khong co bao hinh hop le.";
                return plan;
            }

            plan.DroppedZeroLength = analysis.DroppedZeroLengthSegments;

            // Khoang cach / buoc xep hang KHONG bao gio duoc nho hon co chu cua DIMSTYLE, neu
            // khong thi du nguoi dung nhap gi thi text cung se de len nhau.
            plan.EffectiveDistance = Math.Max(cfg.DistanceFromPline, st.TextHeight * 1.5);
            plan.EffectiveSpacing = Math.Max(cfg.DimensionSpacing, st.TextHeight * 2.0);

            LayoutContext ctx = new LayoutContext
            {
                Analysis = analysis,
                Settings = cfg,
                Style = st,
                Plan = plan,
                Bounds = analysis.Bounds,
                Clearance = Math.Max(st.ArrowSize * 0.8, st.TextHeight * 0.5),
                Epsilon = Math.Max(1e-9, analysis.Bounds.Diagonal * 1e-9)
            };

            List<Candidate> features = new List<Candidate>();
            List<Candidate> skewOrArc = new List<Candidate>();

            BuildCandidates(ctx, features, skewOrArc);
            RemoveDuplicates(features, plan, Math.Max(ctx.Epsilon, analysis.Bounds.Diagonal * 1e-6));

            List<Candidate> overall = BuildOverallCandidates(ctx, features);

            List<Candidate> horizontal = Filter(features, DimOrientation.Horizontal);
            List<Candidate> vertical = Filter(features, DimOrientation.Vertical);

            AssignSides(ctx, horizontal, DimSide.Bottom, DimSide.Top);
            AssignSides(ctx, vertical, DimSide.Left, DimSide.Right);

            foreach (Candidate c in features)
            {
                double half = c.TextWidth * 0.5 + ctx.Clearance * 0.5;
                c.TextLow = c.Center - half;
                c.TextHigh = c.Center + half;
            }

            // Tach cai nen dat canh feature ra khoi cai nen o bang ngoai.
            double nearTrigger = plan.EffectiveDistance + plan.EffectiveSpacing;
            List<Candidate> nearFeature = new List<Candidate>();

            foreach (Candidate c in features)
            {
                if (cfg.PlaceNearFeature &&
                    ExtensionLength(c, c.Side, ctx.Bounds) > nearTrigger)
                {
                    c.WantsNearFeature = true;
                    nearFeature.Add(c);
                }
                else
                {
                    ctx.Band(c.Side).Add(c);
                }
            }

            PackRows(ctx, DimSide.Bottom);
            PackRows(ctx, DimSide.Top);
            PackRows(ctx, DimSide.Left);
            PackRows(ctx, DimSide.Right);

            foreach (DimSide side in AllSides)
            {
                foreach (Candidate c in ctx.Band(side).Candidates)
                {
                    plan.Placements.Add(BuildLinearPlacement(ctx, c, 0.0, false));
                }
            }

            // Dat canh feature sau khi bang da co cho, de con kiem tra va cham voi bang.
            nearFeature.Sort(delegate (Candidate x, Candidate y)
            {
                int cmp = x.Measured.CompareTo(y.Measured);
                if (cmp != 0) return cmp;
                return x.Low.CompareTo(y.Low);
            });

            foreach (Candidate c in nearFeature)
            {
                if (!TryPlaceNearFeature(ctx, c))
                {
                    // Khong an toan -> quay ve bang ngoai, xep hang binh thuong.
                    c.WantsNearFeature = false;
                    PlaceBandFallback(ctx, c);
                }
            }

            // Dim bao tong the: chon phia con thoang hon roi day ra hang NGOAI CUNG cua phia do.
            foreach (Candidate c in overall)
            {
                PlaceOverall(ctx, c);
            }

            PlaceSkewAndArc(ctx, skewOrArc);

            BuildReport(plan, analysis, cfg, st);
            plan.IsValid = true;
            return plan;
        }

        private static readonly DimSide[] AllSides =
        {
            DimSide.Bottom, DimSide.Top, DimSide.Left, DimSide.Right
        };

        // ==================================================================================
        // MO HINH NOI BO
        // ==================================================================================

        private sealed class Candidate
        {
            public DimKind Kind = DimKind.Linear;
            public DimOrientation Orientation;

            /// <summary>Khoang do tren truc do (X voi dim ngang, Y voi dim doc).</summary>
            public double Low;
            public double High;

            /// <summary>Toa do co dinh cua feature (Y voi dim ngang, X voi dim doc).</summary>
            public double AlongCoord;

            public double Measured;
            public string Text;
            public double TextWidth;

            public int SegmentIndex = -1;
            public bool IsOverall;
            public DimPlineSegment Segment;

            public DimSide Side;
            public int Row;
            public double ChosenScore;
            public double OtherScore;
            public int Crossings;

            public double TextLow;
            public double TextHigh;

            public bool WantsNearFeature;

            public double Center { get { return (Low + High) * 0.5; } }
        }

        private sealed class Band
        {
            public List<Candidate> Candidates = new List<Candidate>();
            public List<List<Candidate>> Rows = new List<List<Candidate>>();

            public void Add(Candidate c)
            {
                Candidates.Add(c);
            }

            public int RowCount { get { return Rows.Count; } }
        }

        private sealed class LayoutContext
        {
            public DimPlineAnalysis Analysis;
            public AutoDimPlineSettings Settings;
            public DimPlineStyle Style;
            public DimPlinePlan Plan;
            public DimBox Bounds;
            public double Clearance;
            public double Epsilon;

            private readonly Dictionary<DimSide, Band> _bands = new Dictionary<DimSide, Band>();

            public Band Band(DimSide side)
            {
                Band band;
                if (!_bands.TryGetValue(side, out band))
                {
                    band = new Band();
                    _bands[side] = band;
                }

                return band;
            }
        }

        // ==================================================================================
        // TAO CANDIDATE
        // ==================================================================================

        private static void BuildCandidates(
            LayoutContext ctx,
            List<Candidate> features,
            List<Candidate> skewOrArc)
        {
            AutoDimPlineSettings cfg = ctx.Settings;
            DimPlineStyle st = ctx.Style;
            DimPlinePlan plan = ctx.Plan;

            foreach (DimPlineSegment s in ctx.Analysis.Segments)
            {
                if (s.IsArc)
                {
                    if (cfg.ArcMode == DimPlineArcMode.Skip)
                    {
                        plan.SkippedArcs++;
                        continue;
                    }

                    string radiusText = FormatValue(s.ArcRadius, cfg, st);
                    skewOrArc.Add(new Candidate
                    {
                        Kind = DimKind.Radial,
                        Orientation = DimOrientation.Skew,
                        Measured = s.ArcRadius,
                        SegmentIndex = s.Index,
                        Segment = s,
                        Text = radiusText,
                        TextWidth = st.EstimateTextWidth("R" + radiusText)
                    });
                    continue;
                }

                if (s.Length < cfg.MinSegmentLength)
                {
                    plan.SkippedTooShort++;
                    continue;
                }

                if (s.Orientation == DimOrientation.Skew)
                {
                    if (cfg.SkewMode == DimPlineSkewMode.Skip)
                    {
                        plan.SkippedSkew++;
                        continue;
                    }

                    string skewText = FormatValue(s.Length, cfg, st);
                    skewOrArc.Add(new Candidate
                    {
                        Kind = DimKind.Aligned,
                        Orientation = DimOrientation.Skew,
                        Measured = s.Length,
                        SegmentIndex = s.Index,
                        Segment = s,
                        Text = skewText,
                        TextWidth = st.EstimateTextWidth(skewText)
                    });
                    continue;
                }

                Candidate c = new Candidate
                {
                    Kind = DimKind.Linear,
                    Orientation = s.Orientation,
                    SegmentIndex = s.Index,
                    Segment = s
                };

                if (s.Orientation == DimOrientation.Horizontal)
                {
                    c.Low = Math.Min(s.Start.X, s.End.X);
                    c.High = Math.Max(s.Start.X, s.End.X);
                    c.AlongCoord = (s.Start.Y + s.End.Y) * 0.5;
                }
                else
                {
                    c.Low = Math.Min(s.Start.Y, s.End.Y);
                    c.High = Math.Max(s.Start.Y, s.End.Y);
                    c.AlongCoord = (s.Start.X + s.End.X) * 0.5;
                }

                c.Measured = c.High - c.Low;
                c.Text = FormatValue(c.Measured, cfg, st);
                c.TextWidth = st.EstimateTextWidth(c.Text);
                features.Add(c);
            }
        }

        private static List<Candidate> BuildOverallCandidates(LayoutContext ctx, List<Candidate> features)
        {
            List<Candidate> overall = new List<Candidate>();
            AutoDimPlineSettings cfg = ctx.Settings;

            if (!cfg.CreateOverall)
            {
                return overall;
            }

            DimBox b = ctx.Bounds;
            double dupTolerance = Math.Max(ctx.Epsilon, b.Diagonal * 1e-6);

            if (b.Width > Math.Max(cfg.MinSegmentLength, ctx.Epsilon))
            {
                overall.Add(MakeOverall(DimOrientation.Horizontal, b.MinX, b.MaxX, ctx));
            }

            if (b.Height > Math.Max(cfg.MinSegmentLength, ctx.Epsilon))
            {
                overall.Add(MakeOverall(DimOrientation.Vertical, b.MinY, b.MaxY, ctx));
            }

            // Mot feature do dung bang khoang bao tong the la dim thua -> bo feature, giu overall.
            for (int i = features.Count - 1; i >= 0; i--)
            {
                Candidate f = features[i];
                foreach (Candidate o in overall)
                {
                    if (o.Orientation == f.Orientation &&
                        Math.Abs(o.Low - f.Low) <= dupTolerance &&
                        Math.Abs(o.High - f.High) <= dupTolerance)
                    {
                        features.RemoveAt(i);
                        ctx.Plan.SkippedDuplicate++;
                        break;
                    }
                }
            }

            return overall;
        }

        private static Candidate MakeOverall(DimOrientation orientation, double low, double high, LayoutContext ctx)
        {
            Candidate c = new Candidate
            {
                Kind = DimKind.Linear,
                Orientation = orientation,
                Low = low,
                High = high,
                Measured = high - low,
                IsOverall = true,
                SegmentIndex = -1
            };

            c.Text = FormatValue(c.Measured, ctx.Settings, ctx.Style);
            c.TextWidth = ctx.Style.EstimateTextWidth(c.Text);

            double half = c.TextWidth * 0.5 + ctx.Clearance * 0.5;
            c.TextLow = c.Center - half;
            c.TextHigh = c.Center + half;
            return c;
        }

        private static void RemoveDuplicates(List<Candidate> features, DimPlinePlan plan, double tolerance)
        {
            for (int i = 0; i < features.Count; i++)
            {
                for (int j = features.Count - 1; j > i; j--)
                {
                    Candidate a = features[i];
                    Candidate b = features[j];

                    if (a.Orientation == b.Orientation &&
                        Math.Abs(a.Low - b.Low) <= tolerance &&
                        Math.Abs(a.High - b.High) <= tolerance)
                    {
                        features.RemoveAt(j);
                        plan.SkippedDuplicate++;
                    }
                }
            }
        }

        private static List<Candidate> Filter(List<Candidate> source, DimOrientation orientation)
        {
            List<Candidate> result = new List<Candidate>();
            foreach (Candidate c in source)
            {
                if (c.Orientation == orientation)
                {
                    result.Add(c);
                }
            }

            return result;
        }

        // ==================================================================================
        // CHON PHIA
        // ==================================================================================

        private static void AssignSides(LayoutContext ctx, List<Candidate> candidates, DimSide sideA, DimSide sideB)
        {
            if (candidates.Count == 0)
            {
                return;
            }

            DimPlineAnalysis analysis = ctx.Analysis;
            AutoDimPlineSettings cfg = ctx.Settings;
            DimBox b = ctx.Bounds;

            double norm = Math.Max(b.Diagonal, 1e-9);
            double skipTolerance = ctx.Epsilon;
            bool horizontal = sideA == DimSide.Bottom;

            List<ScoredCandidate> scored = new List<ScoredCandidate>();

            foreach (Candidate c in candidates)
            {
                double extA = horizontal ? c.AlongCoord - b.MinY : c.AlongCoord - b.MinX;
                double extB = horizontal ? b.MaxY - c.AlongCoord : b.MaxX - c.AlongCoord;

                double targetA = horizontal ? b.MinY - Math.Max(1.0, b.Height) : b.MinX - Math.Max(1.0, b.Width);
                double targetB = horizontal ? b.MaxY + Math.Max(1.0, b.Height) : b.MaxX + Math.Max(1.0, b.Width);

                DimPoint p1 = horizontal ? new DimPoint(c.Low, c.AlongCoord) : new DimPoint(c.AlongCoord, c.Low);
                DimPoint p2 = horizontal ? new DimPoint(c.High, c.AlongCoord) : new DimPoint(c.AlongCoord, c.High);

                int crossA =
                    DimMath.CountRayCrossings(analysis.FlatPoints, analysis.Closed, p1, horizontal, targetA, skipTolerance) +
                    DimMath.CountRayCrossings(analysis.FlatPoints, analysis.Closed, p2, horizontal, targetA, skipTolerance);
                int crossB =
                    DimMath.CountRayCrossings(analysis.FlatPoints, analysis.Closed, p1, horizontal, targetB, skipTolerance) +
                    DimMath.CountRayCrossings(analysis.FlatPoints, analysis.Closed, p2, horizontal, targetB, skipTolerance);

                double normalA = 0.0;
                double normalB = 0.0;
                if (c.Segment != null && c.Segment.HasOutwardNormal)
                {
                    double outward = horizontal ? c.Segment.OutwardY : c.Segment.OutwardX;
                    if (outward < -0.5) normalA = 1.0;
                    else if (outward > 0.5) normalB = 1.0;
                }

                scored.Add(new ScoredCandidate
                {
                    Candidate = c,
                    CostA = WeightExtensionLength * (extA / norm) + WeightCrossing * crossA - WeightOutwardNormal * normalA,
                    CostB = WeightExtensionLength * (extB / norm) + WeightCrossing * crossB - WeightOutwardNormal * normalB,
                    CrossA = crossA,
                    CrossB = crossB
                });
            }

            // Quyet dinh cai "chac chan" truoc; cai luong lu de sau se chiu anh huong cua
            // thanh phan can bang -> hai phia khong bi lech han ve mot ben.
            scored.Sort(delegate (ScoredCandidate x, ScoredCandidate y)
            {
                double dx = Math.Abs(x.CostA - x.CostB);
                double dy = Math.Abs(y.CostA - y.CostB);
                int cmp = dy.CompareTo(dx);
                if (cmp != 0) return cmp;
                return x.Candidate.Low.CompareTo(y.Candidate.Low);
            });

            int countA = 0;
            int countB = 0;
            double total = Math.Max(1, candidates.Count);

            foreach (ScoredCandidate sc in scored)
            {
                double finalA = sc.CostA + WeightBalance * (countA / total);
                double finalB = sc.CostB + WeightBalance * (countB / total);

                DimSide autoSide = cfg.AutoLayout && finalB < finalA ? sideB : sideA;
                DimSide chosen = ApplyBias(autoSide, sideA, sideB, cfg);

                sc.Candidate.Side = chosen;
                sc.Candidate.ChosenScore = chosen == sideA ? finalA : finalB;
                sc.Candidate.OtherScore = chosen == sideA ? finalB : finalA;
                sc.Candidate.Crossings = chosen == sideA ? sc.CrossA : sc.CrossB;

                if (chosen == sideA) countA++;
                else countB++;
            }
        }

        private sealed class ScoredCandidate
        {
            public Candidate Candidate;
            public double CostA;
            public double CostB;
            public int CrossA;
            public int CrossB;
        }

        private static DimSide ApplyBias(DimSide autoSide, DimSide sideA, DimSide sideB, AutoDimPlineSettings cfg)
        {
            switch (cfg.SideBias)
            {
                case DimPlineSideBias.BottomLeft:
                    return sideA;
                case DimPlineSideBias.TopRight:
                    return sideB;
                case DimPlineSideBias.Flip:
                    return autoSide == sideA ? sideB : sideA;
                default:
                    return cfg.AutoLayout ? autoSide : sideA;
            }
        }

        // ==================================================================================
        // XEP HANG (INTERVAL PACKING)
        // ==================================================================================

        private static void PackRows(LayoutContext ctx, DimSide side)
        {
            Band band = ctx.Band(side);
            if (band.Candidates.Count == 0)
            {
                return;
            }

            DimBox bounds = ctx.Bounds;

            // Feature nam gan mep ngoai nhat duoc hang trong cung -> duong giong ngan nhat va
            // khong phai vuot qua dim khac, dung nhu cach dim tay.
            band.Candidates.Sort(delegate (Candidate x, Candidate y)
            {
                double ex = ExtensionLength(x, side, bounds);
                double ey = ExtensionLength(y, side, bounds);
                int cmp = ex.CompareTo(ey);
                if (cmp != 0) return cmp;
                cmp = x.Low.CompareTo(y.Low);
                if (cmp != 0) return cmp;
                return y.Measured.CompareTo(x.Measured);
            });

            foreach (Candidate c in band.Candidates)
            {
                PlaceInBandRows(band, c, ctx.Epsilon);
            }
        }

        /// <summary>
        /// Duong thoat cho candidate khong dat duoc canh feature: nhet vao bang ngoai.
        /// Xep hang chi biet den cac candidate CUNG BANG, nen con phai kiem tra them voi moi dim
        /// da dat (ke ca dim canh feature dat truoc do) roi day ra hang ngoai neu con vuong.
        /// </summary>
        private static void PlaceBandFallback(LayoutContext ctx, Candidate c)
        {
            Band band = ctx.Band(c.Side);
            band.Candidates.Add(c);
            PlaceInBandRows(band, c, ctx.Epsilon);

            for (int attempt = 0; attempt < MaxOverallPushOutRows; attempt++)
            {
                DimPlinePlacement candidate = BuildLinearPlacement(ctx, c, 0.0, false);

                bool hit = false;
                foreach (DimPlinePlacement placed in ctx.Plan.Placements)
                {
                    if (placed.TextBox != null && candidate.TextBox.Overlaps(placed.TextBox, ctx.Clearance))
                    {
                        hit = true;
                        break;
                    }
                }

                if (!hit || attempt == MaxOverallPushOutRows - 1)
                {
                    ctx.Plan.Placements.Add(candidate);
                    return;
                }

                c.Row++;
                while (band.Rows.Count <= c.Row)
                {
                    band.Rows.Add(new List<Candidate>());
                }
            }
        }

        private static void PlaceInBandRows(Band band, Candidate c, double eps)
        {
            int target = -1;

            for (int r = 0; r < band.Rows.Count; r++)
            {
                bool conflict = false;
                foreach (Candidate placed in band.Rows[r])
                {
                    if (Conflicts(c, placed, eps))
                    {
                        conflict = true;
                        break;
                    }
                }

                if (!conflict)
                {
                    target = r;
                    break;
                }
            }

            if (target < 0)
            {
                band.Rows.Add(new List<Candidate>());
                target = band.Rows.Count - 1;
            }

            c.Row = target;
            band.Rows[target].Add(c);
        }

        /// <summary>
        /// Hai dim chi duoc chung mot hang khi CA khoang do CA hop text deu khong de len nhau.
        /// Khoang do cham nhau tai dau mut (dim noi tiep dang chuoi) KHONG bi coi la xung dot -
        /// do chinh la cach mot nguoi ve dim tay.
        /// </summary>
        private static bool Conflicts(Candidate a, Candidate b, double eps)
        {
            bool spanOverlap = a.Low < b.High - eps && a.High > b.Low + eps;
            bool textOverlap = a.TextLow < b.TextHigh && a.TextHigh > b.TextLow;
            return spanOverlap || textOverlap;
        }

        private static double ExtensionLength(Candidate c, DimSide side, DimBox b)
        {
            switch (side)
            {
                case DimSide.Bottom:
                    return c.AlongCoord - b.MinY;
                case DimSide.Top:
                    return b.MaxY - c.AlongCoord;
                case DimSide.Left:
                    return c.AlongCoord - b.MinX;
                default:
                    return b.MaxX - c.AlongCoord;
            }
        }

        // ==================================================================================
        // DAT CANH FEATURE
        // ==================================================================================

        /// <summary>
        /// Thu dat dim ngay canh feature. Chi nhan vi tri khi duong kich thuoc va CA HAI duong
        /// giong deu khong cat qua net ve, va text khong cham dim nao khac. Tra ve false de
        /// caller quay ve bang ngoai.
        /// </summary>
        private static bool TryPlaceNearFeature(LayoutContext ctx, Candidate c)
        {
            DimPlinePlan plan = ctx.Plan;
            bool horizontal = c.Orientation == DimOrientation.Horizontal;

            // Huong day ra mac dinh la phia da cham diem.
            double preferred = (c.Side == DimSide.Bottom || c.Side == DimSide.Left) ? -1.0 : 1.0;

            // Khi nguoi dung da ep huong (SideBias khac Auto, hoac tat Auto layout) thi ton
            // trong lua chon do tuyet doi: khong tu y lat sang phia kia.
            bool sideIsForced =
                !ctx.Settings.AutoLayout || ctx.Settings.SideBias != DimPlineSideBias.Auto;

            if (!sideIsForced && c.Segment != null && c.Segment.HasOutwardNormal)
            {
                // Bien dang kin: huong dung de dat dim cuc bo la phap tuyen huong ra ngoai vat lieu.
                double outward = horizontal ? c.Segment.OutwardY : c.Segment.OutwardX;
                if (Math.Abs(outward) > 0.5)
                {
                    preferred = outward > 0.0 ? 1.0 : -1.0;
                }
            }

            double[] signs = sideIsForced
                ? new[] { preferred }
                : new[] { preferred, -preferred };

            foreach (double sign in signs)
            {
                for (int step = 0; step < MaxNearFeatureSteps; step++)
                {
                    double offset = plan.EffectiveDistance + step * plan.EffectiveSpacing;
                    double coord = c.AlongCoord + sign * offset;

                    if (!IsNearFeaturePositionFree(ctx, c, horizontal, coord))
                    {
                        continue;
                    }

                    DimPlinePlacement p = BuildPlacementAt(ctx, c, coord, true);
                    p.Side = horizontal
                        ? (sign < 0.0 ? DimSide.Bottom : DimSide.Top)
                        : (sign < 0.0 ? DimSide.Left : DimSide.Right);
                    plan.Placements.Add(p);
                    return true;
                }
            }

            return false;
        }

        private static bool IsNearFeaturePositionFree(LayoutContext ctx, Candidate c, bool horizontal, double coord)
        {
            DimPlineAnalysis analysis = ctx.Analysis;

            // 1. Duong kich thuoc khong duoc cat qua net ve.
            if (DimMath.CountSegmentCrossings(
                    analysis.FlatPoints, analysis.Closed, horizontal, coord, c.Low, c.High, ctx.Epsilon) > 0)
            {
                return false;
            }

            // 2. Hai duong giong khong duoc cat qua net ve.
            DimPoint d1 = horizontal ? new DimPoint(c.Low, c.AlongCoord) : new DimPoint(c.AlongCoord, c.Low);
            DimPoint d2 = horizontal ? new DimPoint(c.High, c.AlongCoord) : new DimPoint(c.AlongCoord, c.High);

            if (DimMath.CountRayCrossings(analysis.FlatPoints, analysis.Closed, d1, horizontal, coord, ctx.Epsilon) > 0 ||
                DimMath.CountRayCrossings(analysis.FlatPoints, analysis.Closed, d2, horizontal, coord, ctx.Epsilon) > 0)
            {
                return false;
            }

            // 3. Text khong duoc cham text cua dim khac, va duong kich thuoc khong duoc
            //    trung len duong kich thuoc khac cung phuong.
            DimTextBox box = MakeTextBox(ctx, c, coord, horizontal);

            // Dim dat trong long ban ve can khoang ho rong rai hon dim o bang ngoai, vi xung
            // quanh no con net ve chu khong phai vung trong.
            double textGap = ctx.Clearance * 1.5;

            foreach (DimPlinePlacement placed in ctx.Plan.Placements)
            {
                if (placed.TextBox != null && box.Overlaps(placed.TextBox, textGap))
                {
                    return false;
                }

                if (placed.Kind != DimKind.Linear || placed.Orientation != c.Orientation)
                {
                    continue;
                }

                double placedCoord = horizontal ? placed.DimLinePoint.Y : placed.DimLinePoint.X;
                if (Math.Abs(placedCoord - coord) > ctx.Clearance)
                {
                    continue;
                }

                double pLow = horizontal
                    ? Math.Min(placed.DefPoint1.X, placed.DefPoint2.X)
                    : Math.Min(placed.DefPoint1.Y, placed.DefPoint2.Y);
                double pHigh = horizontal
                    ? Math.Max(placed.DefPoint1.X, placed.DefPoint2.X)
                    : Math.Max(placed.DefPoint1.Y, placed.DefPoint2.Y);

                if (DimMath.IntervalsOverlap(c.Low, c.High, pLow, pHigh))
                {
                    return false;
                }
            }

            return true;
        }

        private static DimTextBox MakeTextBox(LayoutContext ctx, Candidate c, double coord, bool horizontal)
        {
            return horizontal
                ? new DimTextBox(c.Center, coord, c.TextWidth, ctx.Style.TextHeight)
                : new DimTextBox(coord, c.Center, ctx.Style.TextHeight, c.TextWidth);
        }

        // ==================================================================================
        // DUNG PLACEMENT
        // ==================================================================================

        private static DimPlinePlacement BuildLinearPlacement(
            LayoutContext ctx,
            Candidate c,
            double extraOffset,
            bool nearFeature)
        {
            DimPlinePlan plan = ctx.Plan;
            DimBox b = ctx.Bounds;
            double offset = plan.EffectiveDistance + c.Row * plan.EffectiveSpacing + extraOffset;

            double coord;
            switch (c.Side)
            {
                case DimSide.Bottom:
                    coord = b.MinY - offset;
                    break;
                case DimSide.Top:
                    coord = b.MaxY + offset;
                    break;
                case DimSide.Left:
                    coord = b.MinX - offset;
                    break;
                default:
                    coord = b.MaxX + offset;
                    break;
            }

            DimPlinePlacement p = BuildPlacementAt(ctx, c, coord, nearFeature);
            p.Side = c.Side;
            return p;
        }

        private static DimPlinePlacement BuildPlacementAt(
            LayoutContext ctx,
            Candidate c,
            double coord,
            bool nearFeature)
        {
            DimBox b = ctx.Bounds;
            bool horizontal = c.Orientation == DimOrientation.Horizontal;

            DimPlinePlacement p = new DimPlinePlacement
            {
                Kind = DimKind.Linear,
                Orientation = c.Orientation,
                MeasuredValue = c.Measured,
                DisplayText = c.Text,
                Side = c.Side,
                Row = c.Row,
                IsOverall = c.IsOverall,
                IsNearFeature = nearFeature,
                SegmentIndex = c.SegmentIndex,
                ChosenSideScore = c.ChosenScore,
                OtherSideScore = c.OtherScore,
                GeometryCrossings = c.Crossings,
                TextBox = MakeTextBox(ctx, c, coord, horizontal)
            };

            if (horizontal)
            {
                // Dim bao tong the lay diem goc o mep bao hinh, dim feature lay ngay tren doan.
                double anchorY = c.IsOverall
                    ? (coord < b.MinY ? b.MinY : b.MaxY)
                    : c.AlongCoord;

                p.Rotation = 0.0;
                p.DefPoint1 = new DimPoint(c.Low, anchorY);
                p.DefPoint2 = new DimPoint(c.High, anchorY);
                p.DimLinePoint = new DimPoint(c.Center, coord);
            }
            else
            {
                double anchorX = c.IsOverall
                    ? (coord < b.MinX ? b.MinX : b.MaxX)
                    : c.AlongCoord;

                p.Rotation = Math.PI * 0.5;
                p.DefPoint1 = new DimPoint(anchorX, c.Low);
                p.DefPoint2 = new DimPoint(anchorX, c.High);
                p.DimLinePoint = new DimPoint(coord, c.Center);
            }

            return p;
        }

        private static void PlaceOverall(LayoutContext ctx, Candidate c)
        {
            DimPlinePlan plan = ctx.Plan;

            DimSide sideA = c.Orientation == DimOrientation.Horizontal ? DimSide.Bottom : DimSide.Left;
            DimSide sideB = c.Orientation == DimOrientation.Horizontal ? DimSide.Top : DimSide.Right;

            int rowsA = ctx.Band(sideA).RowCount;
            int rowsB = ctx.Band(sideB).RowCount;

            DimSide chosen = rowsA <= rowsB ? sideA : sideB;
            c.Side = ApplyBias(chosen, sideA, sideB, ctx.Settings);
            c.Row = ctx.Band(c.Side).RowCount;

            // Day tiep ra ngoai neu con vuong dim dat canh feature.
            for (int attempt = 0; attempt < MaxOverallPushOutRows; attempt++)
            {
                DimPlinePlacement candidate = BuildLinearPlacement(ctx, c, plan.EffectiveSpacing * 0.5, false);

                bool hit = false;
                foreach (DimPlinePlacement placed in plan.Placements)
                {
                    if (placed.TextBox != null && candidate.TextBox.Overlaps(placed.TextBox, ctx.Clearance))
                    {
                        hit = true;
                        break;
                    }
                }

                if (!hit || attempt == MaxOverallPushOutRows - 1)
                {
                    plan.Placements.Add(candidate);
                    ctx.Band(c.Side).Rows.Add(new List<Candidate> { c });
                    return;
                }

                c.Row++;
            }
        }

        // ==================================================================================
        // DOAN XIEN VA DOAN CUNG
        // ----------------------------------------------------------------------------------
        // Hai loai nay khong xep hang theo truc duoc nen dung quy tac cuc bo: dat theo phap
        // tuyen huong ra ngoai, roi day dan ra cho toi khi text khong con cham dim khac.
        // ==================================================================================

        private static void PlaceSkewAndArc(LayoutContext ctx, List<Candidate> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            DimPlinePlan plan = ctx.Plan;
            DimPlineStyle st = ctx.Style;
            DimBox b = ctx.Bounds;

            List<DimTextBox> occupied = new List<DimTextBox>();
            foreach (DimPlinePlacement placed in plan.Placements)
            {
                if (placed.TextBox != null)
                {
                    occupied.Add(placed.TextBox);
                }
            }

            foreach (Candidate c in items)
            {
                DimPlineSegment s = c.Segment;
                if (s == null)
                {
                    continue;
                }

                DimPoint anchor;
                double normalX;
                double normalY;

                if (c.Kind == DimKind.Radial)
                {
                    double midAngle = s.ArcStartAngle + s.ArcSweepAngle * 0.5;
                    anchor = new DimPoint(
                        s.ArcCenter.X + s.ArcRadius * Math.Cos(midAngle),
                        s.ArcCenter.Y + s.ArcRadius * Math.Sin(midAngle));
                    normalX = Math.Cos(midAngle);
                    normalY = Math.Sin(midAngle);
                }
                else
                {
                    anchor = s.Mid;
                    if (s.HasOutwardNormal)
                    {
                        normalX = s.OutwardX;
                        normalY = s.OutwardY;
                    }
                    else
                    {
                        // Polyline mo: khong co trong/ngoai, dung phap tuyen huong ra xa tam bao hinh.
                        double dx = s.End.X - s.Start.X;
                        double dy = s.End.Y - s.Start.Y;
                        double len = Math.Max(DimMath.Hypot(dx, dy), 1e-9);
                        normalX = dy / len;
                        normalY = -dx / len;

                        if (normalX * (b.CenterX - anchor.X) + normalY * (b.CenterY - anchor.Y) > 0.0)
                        {
                            normalX = -normalX;
                            normalY = -normalY;
                        }
                    }
                }

                double textLength = Math.Max(c.TextWidth, st.TextHeight);
                DimTextBox box = null;
                double distance = plan.EffectiveDistance;

                for (int step = 0; step < MaxSkewPushOutSteps; step++)
                {
                    distance = plan.EffectiveDistance + step * plan.EffectiveSpacing;
                    box = new DimTextBox(
                        anchor.X + normalX * distance,
                        anchor.Y + normalY * distance,
                        textLength,
                        st.TextHeight * 1.4);

                    bool hit = false;
                    foreach (DimTextBox other in occupied)
                    {
                        if (box.Overlaps(other, ctx.Clearance))
                        {
                            hit = true;
                            break;
                        }
                    }

                    if (!hit)
                    {
                        break;
                    }
                }

                occupied.Add(box);

                DimPlinePlacement p = new DimPlinePlacement
                {
                    Kind = c.Kind,
                    Orientation = DimOrientation.Skew,
                    MeasuredValue = c.Measured,
                    DisplayText = c.Text,
                    SegmentIndex = c.SegmentIndex,
                    IsNearFeature = true,
                    Row = 0,
                    TextBox = box,
                    DimLinePoint = new DimPoint(anchor.X + normalX * distance, anchor.Y + normalY * distance),
                    Side = ClassifySide(normalX, normalY)
                };

                if (c.Kind == DimKind.Radial)
                {
                    p.ArcCenter = s.ArcCenter;
                    p.ChordPoint = anchor;
                    p.LeaderLength = distance;
                    p.DefPoint1 = s.ArcCenter;
                    p.DefPoint2 = anchor;
                }
                else
                {
                    p.DefPoint1 = s.Start;
                    p.DefPoint2 = s.End;
                }

                plan.Placements.Add(p);
            }
        }

        private static DimSide ClassifySide(double nx, double ny)
        {
            if (Math.Abs(nx) >= Math.Abs(ny))
            {
                return nx >= 0.0 ? DimSide.Right : DimSide.Left;
            }

            return ny >= 0.0 ? DimSide.Top : DimSide.Bottom;
        }

        // ==================================================================================
        // TIEN ICH
        // ==================================================================================

        /// <summary>
        /// Chuoi text hien thi = gia tri hinh hoc * LinearScale (DIMLFAC).
        /// CHI dung de uoc luong be rong text khi bo tri. Moi phep tinh toa do van dung
        /// gia tri hinh hoc goc - do la diem then chot de DIMLFAC khong lam vo bo cuc.
        /// </summary>
        private static string FormatValue(double measured, AutoDimPlineSettings cfg, DimPlineStyle st)
        {
            double displayed = measured * cfg.LinearScale;
            return displayed.ToString(
                "F" + st.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static void BuildReport(
            DimPlinePlan plan,
            DimPlineAnalysis analysis,
            AutoDimPlineSettings cfg,
            DimPlineStyle st)
        {
            plan.Report.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Bao hinh {0}  kin={1}  doan={2}  cung={3}  xien={4}",
                analysis.Bounds, analysis.Closed, analysis.Segments.Count, analysis.ArcCount, analysis.SkewCount));

            plan.Report.Add(string.Format(
                CultureInfo.InvariantCulture,
                "DIMSTYLE: textH={0:0.###} arrow={1:0.###} dec={2} | distance={3:0.###} spacing={4:0.###} (yeu cau {5:0.###}/{6:0.###})",
                st.TextHeight, st.ArrowSize, st.DecimalPlaces,
                plan.EffectiveDistance, plan.EffectiveSpacing, cfg.DistanceFromPline, cfg.DimensionSpacing));

            foreach (DimPlineSegment s in analysis.Segments)
            {
                plan.Report.Add("  SEG " + s);
            }

            foreach (DimPlinePlacement p in plan.Placements)
            {
                plan.Report.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "  DIM {0,-7} {1,-10} value={2,9:0.###} text={3,-9} side={4,-6} row={5} {6} cross={7} score={8:0.###}/{9:0.###} at {10}",
                    p.Kind, p.Orientation, p.MeasuredValue, p.DisplayText, p.Side, p.Row,
                    p.IsOverall ? "OVERALL" : (p.IsNearFeature ? "near   " : "band   "),
                    p.GeometryCrossings, p.ChosenSideScore, p.OtherSideScore, p.DimLinePoint));
            }
        }
    }
}
