using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // FOIL - MO HINH MAY CHAN (PRESS BRAKE) VA KIEM TRA KHA THI CUA MOT LAN CHAN
    // ------------------------------------------------------------------------------------------
    // Muc tieu: tra loi duoc cau hoi "voi hinh dang HIEN TAI cua chi tiet, co the chan duong
    // chan X tiep theo duoc khong?" - bang HINH HOC, khong bang quy uoc cung.
    //
    // MO HINH VAT LY (air bending, mat cat 2D nhin doc theo duong chan):
    //
    //            |  chay dao (punch) di xuong theo truc Y
    //            |
    //         \  |  /                     gamma = nua goc dao
    //          \ | /
    //   ________\|/________   y = 0  = MAT TREN CUA COI (die)
    //   |       \   /       |
    //   |        \ /        |  coi chu V, khau do V (DieOpening)
    //   |         v         |
    //   +-------------------+  than coi, nua be rong = DieBodyHalfWidth
    //
    //   - Ton nam tren 2 vai coi (x = +-V/2, y = 0).
    //   - Dao an xuong tai x = 0; khi chan xong, HAI canh deu ngoc len alpha/2 so voi phuong
    //     ngang, duong phan giac cua goc chan TRUNG voi truc dao (truc Y).
    //   - Vi vay goc chan tren may LUON "mo len tren". Muon chan mot goc re nguoc lai thi phai
    //     LAT TON. Day la ly do quy trinh chan bi rang buoc thu tu.
    //
    // QUY UOC HE TOA DO MAY (machine frame) dung trong file nay:
    //       goc toa do = dinh mold-line cua duong chan dang thuc hien
    //       truc Y     = huong di len cua khau do coi (truc dao)
    //       truc +X    = PHIA TRUOC may (phia nguoi dung dung)  -> vat lieu duoc phep thong xuong
    //       truc -X    = PHIA SAU may (phia cu hau / backgauge) -> KHONG duoc thong xuong
    //
    // BON DIEU KIEN DUOC KIEM TRA:
    //   1. TUA COI   : phan nam trong pham vi than coi (|x| <= DieBodyHalfWidth) khong duoc
    //                  thap hon mat coi. Phan vuot ra ngoai than coi chi duoc thong xuong o
    //                  PHIA TRUOC (mot phia duy nhat).
    //   2. VA DAO    : phan da chan tu goc chan thu nhat tro di khong duoc lot vao long dao,
    //                  ca luc dat phoi vao (chua chan) lan luc chan xong.
    //   3. CU HAU    : phai co mot canh lam chuan ga o phia sau, dai toi thieu MinGaugeLength.
    //   4. CANH NHO NHAT: hai canh ke duong chan phai du dai de gac len vai coi (>= V/2 + R + T).
    //
    // Nguon quy tac: thuc hanh press-brake tieu chuan (Amada / Trumpf / Bystronic bending
    // guideline; "Bend sequence" trong Machinery's Handbook - chuong sheet metal).
    // ==========================================================================================

    /// <summary>Muc do nghiem trong cua mot van de phat hien khi kiem tra mot lan chan.</summary>
    public enum FoilBendIssueLevel
    {
        /// <summary>Chi la luu y, van chan duoc.</summary>
        Note = 0,

        /// <summary>Chan duoc nhung kho / rui ro - nen doi thu tu neu con cach khac.</summary>
        Warning = 1,

        /// <summary>KHONG chan duoc theo hinh dang hien tai.</summary>
        Blocking = 2
    }

    public class FoilBendIssue
    {
        public FoilBendIssue(FoilBendIssueLevel level, string message)
        {
            Level = level;
            Message = message ?? string.Empty;
        }

        public FoilBendIssueLevel Level { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return Message;
        }
    }

    /// <summary>
    /// Kich thuoc dung cu da duoc GIAI (da thay het cac gia tri "0 = tu dong" bang so that).
    /// Tach rieng khoi FoilSettings de moi noi dung cung mot bo so.
    /// </summary>
    public class FoilToolingGeometry
    {
        /// <summary>V - khau do coi.</summary>
        public double DieOpening { get; set; }

        /// <summary>Goc long coi (do).</summary>
        public double DieIncludedAngleDeg { get; set; }

        /// <summary>Nua be rong than coi. Ngoai pham vi nay vat lieu duoc thong xuong tu do.</summary>
        public double DieBodyHalfWidth { get; set; }

        /// <summary>Goc dao (do).</summary>
        public double PunchIncludedAngleDeg { get; set; }

        /// <summary>Ban kinh mui dao.</summary>
        public double PunchTipRadius { get; set; }

        /// <summary>Chieu cao lam viec cua dao - tren muc nay la do ga / dam may.</summary>
        public double PunchHeight { get; set; }

        /// <summary>
        /// Nua be day LUOI DAO. Dao that chi NHON O MUI (doan ngan), phia tren la luoi
        /// song song. Neu coi ca dao la hinh chem thi mo hinh se bao va dao o moi cho.
        /// </summary>
        public double PunchBladeHalfWidth { get; set; }

        /// <summary>Nua be rong do ga / dam may phia tren PunchHeight.</summary>
        public double PunchShankHalfWidth { get; set; }

        /// <summary>Chieu cao ma phan mui nhon dat toi be day luoi dao.</summary>
        public double PunchNoseHeight
        {
            get
            {
                double tan = Math.Tan(PunchHalfAngleRad);
                if (tan <= 1e-9) return 0.0;
                double h = (PunchBladeHalfWidth - PunchTipRadius) / tan;
                return h > 0.0 ? h : 0.0;
            }
        }

        /// <summary>Chieu cao mo toi da cua may. 0 = khong kiem tra.</summary>
        public double MaxPartHeight { get; set; }

        /// <summary>Chieu dai chuan ga toi thieu de dung duoc cu hau.</summary>
        public double MinGaugeLength { get; set; }

        /// <summary>Canh ngan nhat co the gac len vai coi = V/2 + R + T.</summary>
        public double MinFlangeLength { get; set; }

        /// <summary>Nua goc chem cua dao (radian) - do tu truc dao.</summary>
        public double PunchHalfAngleRad
        {
            get { return PunchIncludedAngleDeg * 0.5 * FoilMath.DegToRad; }
        }

        /// <summary>
        /// Goc chan LON NHAT ma dao nay voi duoc (radian): canh ngoc len phai thoat khoi ma dao.
        ///     90 - alpha/2 > gamma   =>   alpha &lt; 180 - PunchIncludedAngle
        /// </summary>
        public double MaxBendAngleRad
        {
            get { return (180.0 - PunchIncludedAngleDeg) * FoilMath.DegToRad; }
        }

        /// <summary>
        /// Giai bo kich thuoc dung cu tu cau hinh. Moi gia tri 0 duoc thay bang cong thuc
        /// thong dung cua nghe, phu thuoc be day T va ban kinh trong R.
        /// </summary>
        public static FoilToolingGeometry Resolve(FoilSettings settings)
        {
            FoilSettings s = settings ?? new FoilSettings();
            double t = s.Thickness > 0.0 ? s.Thickness : 1.0;
            double r = s.InsideRadius > 0.0 ? s.InsideRadius : t;

            FoilToolingGeometry g = new FoilToolingGeometry();

            // V: nguoi dung nhap, hoac V = factor * T (quy tac 6T..10T, mac dinh 8T).
            double factor = s.DieOpeningFactor > 0.0 ? s.DieOpeningFactor : 8.0;
            g.DieOpening = s.DieOpening > 0.0 ? s.DieOpening : factor * t;

            g.DieIncludedAngleDeg = s.DieIncludedAngleDeg > 0.0 ? s.DieIncludedAngleDeg : 88.0;
            g.PunchIncludedAngleDeg = s.PunchIncludedAngleDeg > 0.0 ? s.PunchIncludedAngleDeg : 85.0;
            g.PunchTipRadius = s.PunchTipRadius > 0.0 ? s.PunchTipRadius : r;

            g.DieBodyHalfWidth = s.DieBodyHalfWidth > 0.0
                ? s.DieBodyHalfWidth
                : Math.Max(g.DieOpening * 1.2, 15.0);

            g.PunchHeight = s.PunchHeight > 0.0 ? s.PunchHeight : Math.Max(g.DieOpening * 6.0, 100.0);
            g.PunchBladeHalfWidth = s.PunchBladeHalfWidth > 0.0
                ? s.PunchBladeHalfWidth
                : Math.Max(6.0, g.DieOpening * 0.35);
            g.PunchShankHalfWidth = Math.Max(30.0, g.PunchBladeHalfWidth * 4.0);

            g.MaxPartHeight = s.MaxPartHeight > 0.0 ? s.MaxPartHeight : 0.0;

            // Canh nho nhat gac duoc len vai coi.
            g.MinFlangeLength = g.DieOpening * 0.5 + r + t;

            // Chuan ga cu hau: thuc te can it nhat bang khau do coi moi ga on dinh.
            g.MinGaugeLength = s.MinGaugeLength > 0.0 ? s.MinGaugeLength : g.DieOpening;

            return g;
        }

        /// <summary>
        /// Nua be rong cua dao tai chieu cao y (y do tu MUI DAO di len). Dao that co 3 doan:
        ///     0 .. PunchNoseHeight   mui nhon hinh chem, goc PunchIncludedAngle
        ///     .. PunchHeight         luoi dao song song, day PunchBladeHalfWidth * 2
        ///     tren PunchHeight       do ga / dam may, rong han
        /// </summary>
        public double PunchHalfWidthAt(double y)
        {
            if (y <= 0.0)
            {
                return PunchTipRadius;
            }

            if (y >= PunchHeight)
            {
                return PunchShankHalfWidth;
            }

            double nose = PunchNoseHeight;
            if (y >= nose)
            {
                return PunchBladeHalfWidth;
            }

            return PunchTipRadius + y * Math.Tan(PunchHalfAngleRad);
        }
    }

    /// <summary>
    /// Ket qua kiem tra "chan duong chan nay tiep theo" tren hinh dang hien tai.
    /// </summary>
    public class FoilBendMounting
    {
        /// <summary>Duong chan duoc xet.</summary>
        public int BendIndex { get; set; }

        /// <summary>True neu chan duoc (khong co van de muc Blocking).</summary>
        public bool Feasible { get; set; }

        /// <summary>True neu phai LAT TON so voi chieu duyet goc cua bien dang.</summary>
        public bool Flipped { get; set; }

        /// <summary>
        /// True neu phai XOAY 180 do trong mat phang (doi dau nao quay vao cu hau).
        /// </summary>
        public bool EndSwapped { get; set; }

        /// <summary>Chieu dai chuan ga o phia sau (phia cu hau).</summary>
        public double GaugeLength { get; set; }

        /// <summary>Khoang ho nho nhat den PHAN LAM VIEC cua dao (am = va dao).</summary>
        public double PunchClearance { get; set; }

        /// <summary>Khoang ho nho nhat den DO GA phia tren dao (am = chi tiet cao qua).</summary>
        public double ShankClearance { get; set; }

        /// <summary>
        /// True neu THUC SU co phan da chan de do voi dao. Lan chan dau tien tren phoi phang
        /// khong co gi de do - khi do PunchClearance khong mang y nghia va KHONG duoc tinh
        /// phat, neu khong moi lan chan dau tien deu bi coi la "sat dao".
        /// </summary>
        public bool PunchConstrained { get; set; }

        /// <summary>Chieu cao lon nhat cua chi tiet sau khi chan.</summary>
        public double PartHeight { get; set; }

        /// <summary>Gia (cost) de xep thu tu - cang nho cang de chan.</summary>
        public double Cost { get; set; }

        /// <summary>Hinh dang chi tiet SAU lan chan nay, trong HE TOA DO MAY.</summary>
        public List<FoilPoint2d> MachinePoints { get; private set; } = new List<FoilPoint2d>();

        /// <summary>Hinh dang chi tiet TRUOC lan chan nay, trong HE TOA DO MAY.</summary>
        public List<FoilPoint2d> MachinePointsBefore { get; private set; } = new List<FoilPoint2d>();

        public List<FoilBendIssue> Issues { get; private set; } = new List<FoilBendIssue>();

        public bool HasBlocking
        {
            get
            {
                foreach (FoilBendIssue issue in Issues)
                {
                    if (issue.Level == FoilBendIssueLevel.Blocking) return true;
                }

                return false;
            }
        }

        public void Add(FoilBendIssueLevel level, string message)
        {
            Issues.Add(new FoilBendIssue(level, message));
        }
    }

    public static class FoilPressBrakeModel
    {
        /// <summary>Dung sai chieu cao khi xet "cham mat coi".</summary>
        private const double RestTolerance = 1e-6;

        /// <summary>
        /// Kiem tra kha nang chan duong <paramref name="bendIndex"/> tren hinh dang hien tai.
        /// </summary>
        /// <param name="shape">Hinh dang chi tiet TRUOC lan chan nay (he toa do cua bien dang).</param>
        /// <param name="bend">Duong chan duoc xet - phai CHUA duoc chan trong shape.</param>
        /// <param name="tooling">Kich thuoc dung cu da giai.</param>
        public static FoilBendMounting Evaluate(
            FoilFormedShape shape, FoilBendInfo bend, FoilToolingGeometry tooling)
        {
            FoilBendMounting mount = new FoilBendMounting { BendIndex = bend.Index };

            int startNode = shape.ZoneStartNode[bend.Index];
            int endNode = shape.ZoneEndNode[bend.Index];
            if (startNode < 0 || endNode < 0)
            {
                mount.Add(FoilBendIssueLevel.Blocking, "Khong dinh vi duoc vung chan tren hinh dang.");
                return mount;
            }

            // --- 1. Dua vung chan ve truc X, tam vung chan ve goc toa do -------------------
            FoilPoint2d mid = shape.ZoneMid[bend.Index];
            FoilVector2d axis = shape.ZoneDirection[bend.Index].Normalized();
            FoilVector2d normal = axis.Perpendicular();

            int count = shape.Points.Count;
            double[] lx = new double[count];
            double[] ly = new double[count];
            for (int i = 0; i < count; i++)
            {
                FoilVector2d d = shape.Points[i] - mid;
                lx[i] = d.Dot(axis);
                ly[i] = d.Dot(normal);
            }

            // May LUON chan mo len tren => goc re duong. Goc re am thi phai LAT TON.
            //
            // MAT TON CHI PHU THUOC DAU GOC RE. Xet trong khong gian 3D (mat cat nam trong mat
            // phang XY cua may, chieu dai ton chay doc truc Z), bon cach dat tam ton la:
            //     giu nguyen              -> (x, y)    mat A
            //     xoay 180 quanh truc Y   -> (-x, y)   mat A   (doi dau truoc/sau may - XOAY)
            //     xoay 180 quanh truc X   -> (x, -y)   mat B   (LAT)
            //     xoay 180 quanh truc Z   -> (-x, -y)  mat B   (LAT)
            // Chi phep dao dau y moi lat mat ton. Soi guong theo x tuy la phep bien doi nghich
            // huong TRONG MAT PHANG, nhung trong khong gian no la mot phep XOAY that - khong lat.
            //
            // Ap dieu kien "khau chan mo len tren" vao bon phep tren:
            //     goc re +1 -> chi { giu nguyen, (-x,y) } hop le  => luon chan o MAT A
            //     goc re -1 -> chi { (x,-y), (-x,-y) }   hop le  => luon chan o MAT B
            // Vi vay mount.Flipped bam thang vao dau goc re, va viec doi dau vao cu hau (soi
            // guong theo x o buoc duoi) KHONG duoc dung vao no.
            bool flipped = bend.TurnSign < 0.0;
            if (flipped)
            {
                for (int i = 0; i < count; i++) ly[i] = -ly[i];
            }

            mount.Flipped = flipped;

            // --- 2. Chon dau nao quay vao cu hau ------------------------------------------
            // Vat lieu thong XUONG duoi mat coi chi duoc phep o PHIA TRUOC may (+X). Neu ca hai
            // phia deu co phan thong xuong thi khong ga duoc.
            bool leftSags = false;
            bool rightSags = false;
            for (int i = 0; i < count; i++)
            {
                if (ly[i] >= -RestTolerance) continue;
                if (Math.Abs(lx[i]) <= tooling.DieBodyHalfWidth) continue;   // xu ly rieng o buoc 3
                if (i <= startNode) leftSags = true;
                else if (i >= endNode) rightSags = true;
            }

            // Chi con DUY NHAT mot bac tu do: soi guong qua truc Y (doi dau nao quay vao cu hau).
            // Xoay 180 do trong mat phang se lam khau chan mo XUONG nen khong dung duoc; ket hop
            // xoay + lat ton chinh la phep soi guong x -> -x, giu nguyen chieu "len".
            bool endSwapped = leftSags && !rightSags;   // dua phan thong xuong ve phia truoc (+X)
            if (endSwapped)
            {
                for (int i = 0; i < count; i++) lx[i] = -lx[i];
            }

            mount.EndSwapped = endSwapped;

            if (leftSags && rightSags)
            {
                mount.Add(FoilBendIssueLevel.Blocking,
                    "Ca hai phia deu co phan da chan chuc xuong duoi mat coi - khong dat duoc phoi.");
            }

            // --- 3. Dieu kien TUA COI ------------------------------------------------------
            for (int i = 0; i < count; i++)
            {
                if (ly[i] >= -RestTolerance) continue;

                if (Math.Abs(lx[i]) <= tooling.DieBodyHalfWidth)
                {
                    mount.Add(FoilBendIssueLevel.Blocking,
                        "Phan da chan nam duoi mat coi ngay canh khau do - va vao than coi.");
                    break;
                }

                if (lx[i] < 0.0)
                {
                    mount.Add(FoilBendIssueLevel.Blocking,
                        "Phan da chan chuc xuong o phia cu hau - va vao than may.");
                    break;
                }
            }

            // --- 4. Hai canh ke phai gac duoc len vai coi ---------------------------------
            double leftFlange = FlangeLength(shape, bend.Index, true);
            double rightFlange = FlangeLength(shape, bend.Index, false);
            double shortFlange = Math.Min(leftFlange, rightFlange);
            if (shortFlange + 1e-9 < tooling.MinFlangeLength)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Canh ke chi dai {0:0.##} mm < canh nho nhat {1:0.##} mm cua coi V{2:0.##}.",
                    shortFlange, tooling.MinFlangeLength, tooling.DieOpening));
            }

            // --- 5. Goc chan so voi goc dao ------------------------------------------------
            if (bend.BendAngleRad > tooling.MaxBendAngleRad + 1e-9)
            {
                bool hem = bend.BendAngleDeg >= 150.0;
                mount.Add(
                    hem ? FoilBendIssueLevel.Note : FoilBendIssueLevel.Warning,
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        hem
                            ? "Goc {0:0.#} do - day la BE MEP (hem): phai chan nhon truoc roi ep bet, 2 nguyen cong."
                            : "Goc {0:0.#} do vuot qua kha nang cua dao {1:0.#} do - can dao nhon hon.",
                        bend.BendAngleDeg,
                        tooling.PunchIncludedAngleDeg));
            }

            // --- 6. Hinh dang TRUOC va SAU khi chan, trong he toa do may -------------------
            //
            // TRUOC : vung chan con nam THANG tren truc X, dai BA, tam tai goc toa do.
            //
            // SAU   : vung chan cuon lai thanh GOC. Hai viec phai lam cung luc, neu thieu mot
            //         thi hinh trong he toa do may se KHONG trung voi hinh o he bien dang:
            //           (a) xoay moi nua len alpha/2 quanh goc toa do;
            //           (b) doi dinh: vung chan dai BA bien mat, thay bang DINH MOLD-LINE, va
            //               hai canh ke DAI THEM dung bang setback cua chinh duong chan nay.
            //         Vi vay moi nua con phai TINH TIEN doc theo canh mot doan
            //               delta = AppliedSetback - BA/2
            //         (am cung duoc - khi BA/2 lon hon setback thi canh bi keo vao).
            //
            //         Neu bo qua (b), goc chan se bi ve CUT va hai canh ke ngan mat OSSB - loi
            //         nay chi lo ra voi cac phuong phap co BA > 0 (K-factor), con quy tac xuong
            //         BA = 0 thi khong thay gi.
            double half = FoilMath.Clamp(bend.BendAngleRad * 0.5, 0.0, Math.PI * 0.5);
            double cosH = Math.Cos(half);
            double sinH = Math.Sin(half);
            double halfZone = bend.BendAllowance * 0.5;

            bool startSideIsLeft = !endSwapped;

            // Huong di RA XA dinh goc, cua tung nua, sau khi da xoay.
            FoilVector2d startOut = startSideIsLeft
                ? new FoilVector2d(-cosH, sinH)
                : new FoilVector2d(cosH, sinH);
            FoilVector2d endOut = startSideIsLeft
                ? new FoilVector2d(cosH, sinH)
                : new FoilVector2d(-cosH, sinH);

            double startShift = bend.AppliedSetbackPrev - halfZone;
            double endShift = bend.AppliedSetbackNext - halfZone;

            for (int i = 0; i < count; i++)
            {
                mount.MachinePointsBefore.Add(new FoilPoint2d(lx[i], ly[i]));

                double x = lx[i];
                double y = ly[i];

                if (i > startNode && i < endNode)
                {
                    // Nut nam trong long vung chan (chi xay ra khi vung chan bi chia nho):
                    // sau khi chan no thuoc ve dinh goc.
                    mount.MachinePoints.Add(new FoilPoint2d(0.0, 0.0));
                    continue;
                }

                bool onStartSide = i <= startNode;

                // (a) xoay len alpha/2 - dau xoay theo nua nam ben -X hay +X.
                bool rotateNegative = onStartSide == startSideIsLeft;
                double rx = rotateNegative ? (x * cosH + y * sinH) : (x * cosH - y * sinH);
                double ry = rotateNegative ? (-x * sinH + y * cosH) : (x * sinH + y * cosH);

                if (i == startNode || i == endNode)
                {
                    // (b) hai bien cua vung chan gop lai thanh DINH mold-line tai goc toa do.
                    mount.MachinePoints.Add(new FoilPoint2d(0.0, 0.0));
                    continue;
                }

                FoilVector2d outward = onStartSide ? startOut : endOut;
                double shift = onStartSide ? startShift : endShift;

                mount.MachinePoints.Add(new FoilPoint2d(
                    rx + outward.X * shift, ry + outward.Y * shift));
            }

            // --- 7. Va dao ------------------------------------------------------------------
            // Chi xet phan NGOAI hai canh ke (tu goc da chan dau tien tro di): hai canh ke luon
            // ap vao ma dao nen khong the tinh la va - viec do da kiem o buoc 5.
            int leftGuard = FirstFormedCornerNode(shape, startNode, true);
            int rightGuard = FirstFormedCornerNode(shape, endNode, false);

            // Phan LAM VIEC cua dao (mui nhon + luoi): va vao day la van de THU TU CHAN - doi
            // thu tu co the tranh duoc, nen tinh la chan han.
            double blade = Math.Min(
                PunchClearance(mount.MachinePointsBefore, leftGuard, rightGuard, tooling, true),
                PunchClearance(mount.MachinePoints, leftGuard, rightGuard, tooling, true));

            mount.PunchConstrained = blade != double.MaxValue;
            mount.PunchClearance = mount.PunchConstrained ? blade : 0.0;

            if (mount.PunchConstrained && blade < 0.0)
            {
                mount.Add(FoilBendIssueLevel.Blocking,
                    "Phan da chan lot vao long dao - va dao khi chan.");
            }

            // Phan DO GA phia tren chieu cao lam viec: va vao day la van de CHON DUNG CU
            // (can dao cao hon), doi thu tu thuong khong go duoc - chi canh bao.
            double shank = Math.Min(
                PunchClearance(mount.MachinePointsBefore, leftGuard, rightGuard, tooling, false),
                PunchClearance(mount.MachinePoints, leftGuard, rightGuard, tooling, false));

            mount.ShankClearance = shank == double.MaxValue ? 0.0 : shank;

            if (shank != double.MaxValue && shank < 0.0)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Chi tiet vuot qua chieu cao lam viec {0:0.##} mm cua dao - can dao cao hon.",
                    tooling.PunchHeight));
            }

            // --- 8. Chieu cao chi tiet ------------------------------------------------------
            double maxY = 0.0;
            foreach (FoilPoint2d p in mount.MachinePoints)
            {
                if (p.Y > maxY) maxY = p.Y;
            }

            mount.PartHeight = maxY;

            if (tooling.MaxPartHeight > 0.0 && maxY > tooling.MaxPartHeight)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Chi tiet cao {0:0.##} mm, vuot chieu cao mo {1:0.##} mm cua may.",
                    maxY, tooling.MaxPartHeight));
            }

            // --- 9. Cu hau -------------------------------------------------------------------
            // Phia sau la -X. Sau khi (co the) doi dau, canh o phia -X la canh ga vao cu.
            double gauge = endSwapped ? rightFlange : leftFlange;
            mount.GaugeLength = gauge;

            if (gauge + 1e-9 < tooling.MinGaugeLength)
            {
                double other = endSwapped ? leftFlange : rightFlange;
                if (other >= tooling.MinGaugeLength && !leftSags && !rightSags)
                {
                    // Doi dau de lay canh dai hon lam chuan ga - khong co phan thong xuong nen
                    // viec doi dau la tu do.
                    mount.EndSwapped = !endSwapped;
                    for (int i = 0; i < mount.MachinePoints.Count; i++)
                    {
                        FoilPoint2d p = mount.MachinePoints[i];
                        mount.MachinePoints[i] = new FoilPoint2d(-p.X, p.Y);
                        FoilPoint2d q = mount.MachinePointsBefore[i];
                        mount.MachinePointsBefore[i] = new FoilPoint2d(-q.X, q.Y);
                    }

                    gauge = other;
                    mount.GaugeLength = gauge;
                }
                else
                {
                    mount.Add(FoilBendIssueLevel.Warning, string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Chuan ga phia cu hau chi dai {0:0.##} mm < {1:0.##} mm - de lech vi tri.",
                        gauge, tooling.MinGaugeLength));
                }
            }

            mount.Feasible = !mount.HasBlocking;
            mount.Cost = ComputeCost(mount, tooling);
            return mount;
        }

        /// <summary>
        /// Gia cua mot lan chan. Cang nho cang de thao tac. Chi dung de XEP HANG cac phuong an
        /// kha thi - khong mang y nghia vat ly tuyet doi.
        /// </summary>
        private static double ComputeCost(FoilBendMounting mount, FoilToolingGeometry tooling)
        {
            double cost = 0.0;

            foreach (FoilBendIssue issue in mount.Issues)
            {
                if (issue.Level == FoilBendIssueLevel.Blocking) cost += 10000.0;
                else if (issue.Level == FoilBendIssueLevel.Warning) cost += 50.0;
            }

            // Khoang ho den dao cang lon cang chac an - nhung chi tinh khi co thuc su cai gi do
            // de do. Lan chan dau tien tren phoi phang khong co phan da chan nao nen bo qua.
            if (mount.PunchConstrained && mount.PunchClearance < tooling.DieOpening)
            {
                cost += (tooling.DieOpening - Math.Max(mount.PunchClearance, 0.0)) * 0.5;
            }

            // Chi tiet cang cao cang kho cam.
            cost += mount.PartHeight * 0.02;

            return cost;
        }

        /// <summary>
        /// Khoang ho nho nhat giua duong gap khuc va than dao, xet theo TUNG DOAN chu khong chi
        /// theo dinh: mot canh da chan hoan toan co the quet NGANG qua than dao trong khi hai dau
        /// cua no van nam ngoai (bien dang long hep va sau roi dung y nhu vay).
        ///
        /// Chi xet phan NGOAI vung bao ve - tuc tu goc da chan dau tien tro ra. Hai canh ke
        /// duong chan dang thuc hien luon ap vao ma dao nen khong tinh la va; viec dao co du
        /// nhon cho goc chan hay khong da duoc kiem rieng.
        ///
        /// Tra ve double.MaxValue neu khong co doan nao phai xet.
        /// </summary>
        private static double PunchClearance(
            List<FoilPoint2d> points,
            int leftGuard,
            int rightGuard,
            FoilToolingGeometry tooling,
            bool workingPart)
        {
            double best = double.MaxValue;

            for (int i = 0; i < points.Count - 1; i++)
            {
                bool beyondLeft = leftGuard >= 0 && (i + 1) <= leftGuard;
                bool beyondRight = rightGuard >= 0 && i >= rightGuard;
                if (!beyondLeft && !beyondRight) continue;

                double gap = SegmentPunchGap(points[i], points[i + 1], tooling, workingPart);
                if (gap < best) best = gap;
            }

            return best;
        }

        /// <summary>
        /// Khoang ho nho nhat cua MOT DOAN THANG so voi than dao.
        ///
        /// Nua be rong dao la ham TUYEN TINH TUNG KHUC theo chieu cao (mui nhon / luoi song song
        /// / do ga), con |x| tuyen tinh tung khuc doc theo doan. Vi vay hieu cua chung cung tuyen
        /// tinh tung khuc, va cuc tieu chi co the roi vao hai dau doan hoac cac diem gay. Chi can
        /// lay gia tri tai dung nhung diem do la duoc ket qua CHINH XAC, khong can lay mau.
        /// </summary>
        private static double SegmentPunchGap(
            FoilPoint2d a, FoilPoint2d b, FoilToolingGeometry tooling, bool workingPart)
        {
            List<double> stops = new List<double> { 0.0, 1.0 };
            AddCrossing(stops, a.X, b.X, 0.0);                        // doan cat truc dao
            AddCrossing(stops, a.Y, b.Y, 0.0);                        // ngang mat coi
            AddCrossing(stops, a.Y, b.Y, tooling.PunchNoseHeight);    // het phan mui nhon
            AddCrossing(stops, a.Y, b.Y, tooling.PunchHeight);        // het chieu cao lam viec

            double best = double.MaxValue;

            for (int i = 0; i < stops.Count; i++)
            {
                double t = stops[i];
                double y = a.Y + (b.Y - a.Y) * t;
                if (y < 0.0) continue;   // duoi mat coi khong co than dao

                // Tach hai vung: phan lam viec cua dao, va do ga phia tren no.
                if (workingPart)
                {
                    if (y > tooling.PunchHeight) continue;
                }
                else
                {
                    if (y < tooling.PunchHeight) continue;
                }

                double x = a.X + (b.X - a.X) * t;
                double gap = Math.Abs(x) - tooling.PunchHalfWidthAt(y);
                if (gap < best) best = gap;
            }

            return best;
        }

        private static void AddCrossing(List<double> stops, double from, double to, double target)
        {
            double delta = to - from;
            if (Math.Abs(delta) <= 1e-12) return;

            double t = (target - from) / delta;
            if (t > 0.0 && t < 1.0) stops.Add(t);
        }

        /// <summary>
        /// Chi so nut cua GOC DA CHAN dau tien khi di tu vung chan ra phia
        /// <paramref name="toLeft"/>. Tra ve -1 neu phia do khong con goc da chan nao
        /// (=> khong co gi de va dao).
        /// </summary>
        private static int FirstFormedCornerNode(FoilFormedShape shape, int fromNode, bool toLeft)
        {
            if (toLeft)
            {
                for (int i = fromNode; i >= 0; i--)
                {
                    if (shape.IsFormedCorner[i]) return i;
                }
            }
            else
            {
                for (int i = fromNode; i < shape.Points.Count; i++)
                {
                    if (shape.IsFormedCorner[i]) return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Chieu dai vat lieu tu vung chan di ra mot phia, den GOC DA CHAN dau tien hoac den
        /// het chi tiet. Day chinh la doan dung lam chuan ga cu hau.
        /// </summary>
        public static double FlangeLength(FoilFormedShape shape, int bendIndex, bool toLeft)
        {
            int start = toLeft ? shape.ZoneStartNode[bendIndex] : shape.ZoneEndNode[bendIndex];
            if (start < 0) return 0.0;

            double length = 0.0;

            if (toLeft)
            {
                for (int i = start; i > 0; i--)
                {
                    length += shape.Points[i].DistanceTo(shape.Points[i - 1]);
                    if (shape.IsFormedCorner[i - 1]) break;
                }
            }
            else
            {
                for (int i = start; i < shape.Points.Count - 1; i++)
                {
                    length += shape.Points[i].DistanceTo(shape.Points[i + 1]);
                    if (shape.IsFormedCorner[i + 1]) break;
                }
            }

            return length;
        }

        /// <summary>
        /// Duong bao CO CUA COI trong he toa do may: vai trai - long chu V - vai phai.
        /// </summary>
        public static List<FoilPoint2d> BuildDieOutline(FoilToolingGeometry tooling)
        {
            double halfV = tooling.DieOpening * 0.5;
            double halfAngle = tooling.DieIncludedAngleDeg * 0.5 * FoilMath.DegToRad;
            double depth = halfAngle > 1e-6 ? halfV / Math.Tan(halfAngle) : halfV;
            double body = Math.Max(tooling.DieBodyHalfWidth, halfV * 1.2);
            double bottom = -(depth + Math.Max(depth * 0.6, tooling.DieOpening * 0.4));

            return new List<FoilPoint2d>
            {
                new FoilPoint2d(-body, bottom),
                new FoilPoint2d(-body, 0.0),
                new FoilPoint2d(-halfV, 0.0),
                new FoilPoint2d(0.0, -depth),
                new FoilPoint2d(halfV, 0.0),
                new FoilPoint2d(body, 0.0),
                new FoilPoint2d(body, bottom)
            };
        }

        /// <summary>
        /// Duong bao CHAY DAO trong he toa do may, ve tu mui dao di len.
        /// </summary>
        public static List<FoilPoint2d> BuildPunchOutline(
            FoilToolingGeometry tooling, double bendAngleRad, double partHeight)
        {
            // Mui dao dung tai dinh goc chan; chieu cao ve lay theo chi tiet de hinh can doi.
            double height = Math.Max(partHeight * 1.15, tooling.DieOpening * 2.5);
            double tip = Math.Max(tooling.PunchTipRadius, tooling.DieOpening * 0.02);
            double blade = Math.Max(tooling.PunchBladeHalfWidth, tip);
            double nose = Math.Min(tooling.PunchNoseHeight, height);

            return new List<FoilPoint2d>
            {
                new FoilPoint2d(-blade, height),
                new FoilPoint2d(-blade, nose),
                new FoilPoint2d(-tip, 0.0),
                new FoilPoint2d(tip, 0.0),
                new FoilPoint2d(blade, nose),
                new FoilPoint2d(blade, height)
            };
        }
    }
}
