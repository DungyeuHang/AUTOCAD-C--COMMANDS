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

        /// <summary>
        /// Chieu cao THAN COI (tu mat coi xuong den dam may). Than coi chi la mot THANH HEP,
        /// khong phai mat phang vo han: chi tiet hinh chu U / chu MU hoan toan co the cuoi om
        /// lay than coi, mien la long cua no rong hon than coi va canh no ngan hon chieu cao nay.
        /// </summary>
        public double DieHeight { get; set; }

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

        /// <summary>Nua be rong DAM DUOI (ban may) - phan do coi, rong hon than coi nhieu.</summary>
        public double BedHalfWidth { get; set; }

        /// <summary>Chieu sau dam duoi ve phia duoi.</summary>
        public double BedDepth { get; set; }

        /// <summary>Nua be rong KE COI (ham kep giu chan coi) - rong hon than coi.</summary>
        public double DieHolderHalfWidth { get; set; }

        /// <summary>Chieu cao ham kep coi, tinh tu mat dam duoi len.</summary>
        public double DieHolderHeight { get; set; }

        /// <summary>Nua be rong DAM TREN (ram) - khoi mang do ga dao.</summary>
        public double RamHalfWidth { get; set; }

        /// <summary>Chieu day dam tren (chi de ve).</summary>
        public double RamHeight { get; set; }

        /// <summary>Chieu cao mat chan cua NGON CU HAU, tinh tu mat phoi len.</summary>
        public double BackGaugeHeight { get; set; }

        /// <summary>Chieu dai than cu hau keo ve phia sau.</summary>
        public double BackGaugeLength { get; set; }

        /// <summary>Khoang vuon toi da cua cu hau. Xa hon the thi khong ga duoc bang cu.</summary>
        public double BackGaugeTravel { get; set; }

        /// <summary>Do lech than cua DAO CO NGONG so voi mui dao.</summary>
        public double GooseneckOffset { get; set; }

        /// <summary>Chieu cao bat dau lech cua dao co ngong.</summary>
        public double GooseneckHeight { get; set; }

        /// <summary>Nua be day luoi cua dao co ngong (mong hon dao thang).</summary>
        public double GooseneckBladeHalfWidth { get; set; }

        /// <summary>Goc mui cua DAO NHON - dung cho goc chan lon (hem, goc nhon).</summary>
        public double AcuteIncludedAngleDeg { get; set; }

        /// <summary>Cho phep tu doi sang dao khac khi dao thang bi vuong.</summary>
        public bool UseToolLibrary { get; set; }

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

            // Coi cang nho thi than coi cang hep. Quy tac thuc dung: be rong than coi ~ 2 x V
            // (vai coi o V/2, con lai la mep coi). Truoc day dat co dinh 15 mm nen coi nho van
            // bi coi la to, lam nhieu bien dang binh thuong bi bao nham la khong chan duoc.
            g.DieBodyHalfWidth = s.DieBodyHalfWidth > 0.0
                ? s.DieBodyHalfWidth
                : Math.Max(g.DieOpening, 8.0);

            // Chieu cao than coi quyet dinh HOC BEN CANH COI sau bao nhieu - canh da chan
            // thong xuong nong hon muc nay thi con thoat, sau hon la cham dam. Coi that cho
            // khau V10 cao co 40-60 mm, truoc day lay 30 mm nen hoc bi nong hon thuc te.
            g.DieHeight = s.DieHeight > 0.0
                ? s.DieHeight
                : Math.Max(g.DieOpening * 3.0, 40.0);

            g.PunchHeight = s.PunchHeight > 0.0 ? s.PunchHeight : Math.Max(g.DieOpening * 6.0, 100.0);
            g.PunchBladeHalfWidth = s.PunchBladeHalfWidth > 0.0
                ? s.PunchBladeHalfWidth
                : Math.Max(6.0, g.DieOpening * 0.35);
            g.PunchShankHalfWidth = Math.Max(30.0, g.PunchBladeHalfWidth * 4.0);

            g.BedHalfWidth = s.BedHalfWidth > 0.0
                ? s.BedHalfWidth
                : Math.Max(g.DieBodyHalfWidth * 3.0, 60.0);
            // Dam duoi chi can day du de khong the "lot" qua - moi duong xuong deu phai cat
            // qua no. Ve mong lai thi o hinh gon hon ma khong bo sot va cham nao.
            g.BedDepth = s.BedDepth > 0.0 ? s.BedDepth : Math.Max(g.DieHeight * 2.0, 80.0);

            // KE COI: coi khong dat truc tiep len dam ma bi hai ham kep giu chan, hai ham nay
            // RONG HON than coi. Nho the phan long giua hai ham moi la cho thoat cho vat lieu
            // thong xuong - do chinh la "khoang trong co gioi han" cua ban may, khong phai
            // muon thong xuong bao nhieu cung duoc.
            g.DieHolderHalfWidth = s.DieHolderHalfWidth > 0.0
                ? s.DieHolderHalfWidth
                : g.DieBodyHalfWidth + Math.Max(g.DieOpening * 0.4, 4.0);
            g.DieHolderHeight = s.DieHolderHeight > 0.0
                ? s.DieHolderHeight
                : Math.Max(g.DieHeight * 0.2, 8.0);

            // DAM TREN: mat duoi dam nam ngay tren dinh phan lam viec cua dao. Canh da chan
            // dung cao hon muc nay se DOI DAM - day la gioi han chieu cao that cua may.
            g.RamHalfWidth = s.RamHalfWidth > 0.0
                ? s.RamHalfWidth
                : Math.Max(g.PunchShankHalfWidth * 2.0, g.DieOpening * 6.0);
            g.RamHeight = Math.Max(g.DieOpening * 1.5, 18.0);

            // CU HAU: ngon cu chan vao mep phoi phia sau. Vat lieu chay tiep ve phia sau o
            // ngang tam ngon cu se vuong ngon cu.
            g.BackGaugeHeight = s.BackGaugeHeight > 0.0
                ? s.BackGaugeHeight
                : Math.Max(g.DieOpening * 0.8, 10.0);
            g.BackGaugeLength = Math.Max(g.DieOpening * 2.5, 25.0);
            g.BackGaugeTravel = s.BackGaugeTravel > 0.0 ? s.BackGaugeTravel : 500.0;

            g.GooseneckBladeHalfWidth = s.GooseneckBladeHalfWidth > 0.0
                ? s.GooseneckBladeHalfWidth
                : Math.Max(g.PunchBladeHalfWidth * 0.5, 2.0);
            g.GooseneckOffset = s.GooseneckOffset > 0.0
                ? s.GooseneckOffset
                : Math.Max(g.DieOpening * 1.5, 16.0);
            g.GooseneckHeight = s.GooseneckHeight > 0.0
                ? s.GooseneckHeight
                : Math.Max(g.DieOpening * 1.2, 12.0);

            g.AcuteIncludedAngleDeg = s.AcuteIncludedAngleDeg > 0.0 ? s.AcuteIncludedAngleDeg : 30.0;
            g.UseToolLibrary = s.UseToolLibrary;

            g.MaxPartHeight = s.MaxPartHeight > 0.0 ? s.MaxPartHeight : 0.0;

            // Canh nho nhat gac duoc len vai coi.
            g.MinFlangeLength = g.DieOpening * 0.5 + r + t;

            // Chuan ga cu hau: thuc te can it nhat bang khau do coi moi ga on dinh.
            g.MinGaugeLength = s.MinGaugeLength > 0.0 ? s.MinGaugeLength : g.DieOpening;

            return g;
        }

        // ------------------------------------------------------------------------------
        // NHO LAI BO DUNG CU
        // ------------------------------------------------------------------------------
        // Ham tim thu tu chan goi phep kiem va cham hang chuc nghin lan. Neu moi lan lai dung
        // lai tu dau ca bo dao / coi / dam duoi thi phan lon thoi gian se di vao viec cap phat
        // bo nho chu khong phai vao hinh hoc. Bo dung cu chi phu thuoc CAU HINH nen nho lai duoc.
        private FoilToolShape _die;
        private FoilToolShape _bed;
        private FoilToolShape _dieHolder;
        private FoilToolShape _ram;
        private readonly Dictionary<int, FoilToolShape> _gaugeCache
            = new Dictionary<int, FoilToolShape>();
        private readonly Dictionary<int, List<FoilToolShape>> _punchCache
            = new Dictionary<int, List<FoilToolShape>>();

        /// <summary>Than coi (nho lai sau lan dung dau tien).</summary>
        public FoilToolShape Die
        {
            get { return _die ?? (_die = BuildDie()); }
        }

        /// <summary>Dam duoi (nho lai sau lan dung dau tien).</summary>
        public FoilToolShape Bed
        {
            get { return _bed ?? (_bed = BuildBed()); }
        }

        /// <summary>Ham kep coi (nho lai sau lan dung dau tien).</summary>
        public FoilToolShape DieHolder
        {
            get { return _dieHolder ?? (_dieHolder = BuildDieHolder()); }
        }

        /// <summary>Dam tren (nho lai sau lan dung dau tien).</summary>
        public FoilToolShape Ram
        {
            get { return _ram ?? (_ram = BuildRam()); }
        }

        /// <summary>
        /// Bo chay dao du dung cho chi tiet cao <paramref name="partHeight"/>.
        ///
        /// Chieu cao duoc LAM TRON LEN theo buoc, roi nho lai theo buoc do. Lam tron LEN nen
        /// dao luon du cao - khong bo sot va cham nao - va con dao duoc VE ra chinh la con dao
        /// da duoc KIEM, vi ca hai deu lay tu day.
        /// </summary>
        public List<FoilToolShape> Punches(double partHeight)
        {
            double step = Math.Max(DieOpening, 5.0);
            double height = partHeight > 0.0 ? partHeight : 0.0;

            int key = (int)Math.Ceiling(height / step);
            if (key < 1) key = 1;

            List<FoilToolShape> set;
            if (!_punchCache.TryGetValue(key, out set))
            {
                set = BuildPunches(key * step);
                _punchCache[key] = set;
            }

            return set;
        }

        /// <summary>
        /// Duong bao KIN cua THAN COI: hai vai coi, long chu V va than coi ben duoi.
        /// </summary>
        public FoilToolShape BuildDie()
        {
            double halfV = DieOpening * 0.5;
            double halfAngle = DieIncludedAngleDeg * 0.5 * FoilMath.DegToRad;
            double depth = halfAngle > 1e-6 ? halfV / Math.Tan(halfAngle) : halfV;
            double body = Math.Max(DieBodyHalfWidth, halfV * 1.2);
            double bottom = -Math.Max(DieHeight, depth * 1.2);

            return new FoilToolShape("coi V" + DieOpening.ToString("0.#",
                System.Globalization.CultureInfo.InvariantCulture), new List<FoilPoint2d>
            {
                new FoilPoint2d(-body, bottom),
                new FoilPoint2d(-body, 0.0),
                new FoilPoint2d(-halfV, 0.0),
                new FoilPoint2d(0.0, -depth),
                new FoilPoint2d(halfV, 0.0),
                new FoilPoint2d(body, 0.0),
                new FoilPoint2d(body, bottom)
            });
        }

        /// <summary>
        /// Duong bao DAM DUOI (ban may) - khoi do coi, rong hon than coi nhieu. Vat lieu chuc
        /// xuong canh coi phai NE duoc khoi nay, day la gioi han that cua may.
        /// </summary>
        public FoilToolShape BuildBed()
        {
            double top = -Math.Max(DieHeight, 1.0);

            // Dam duoi LECH chu khong doi xung: coi duoc dat gan MEP TRUOC de tho voi toi, con
            // than dam keo ve PHIA SAU. Nho vay phia truoc coi la khoang trong - do chinh la
            // cho chi tiet dai duoc thong xuong, va cung la ly do phai chon dau nao quay vao
            // cu hau. Neu ve dam doi xung thi moi canh dai chuc xuong deu bi bao va cham.
            double front = Math.Max(DieBodyHalfWidth, DieOpening * 0.5);

            return new FoilToolShape("dam duoi", new List<FoilPoint2d>
            {
                new FoilPoint2d(-BedHalfWidth, top),
                new FoilPoint2d(front, top),
                new FoilPoint2d(front, top - BedDepth),
                new FoilPoint2d(-BedHalfWidth, top - BedDepth)
            });
        }

        /// <summary>
        /// KE COI (ham kep): hai ham thep kep chan coi, dat tren mat dam duoi va RONG HON
        /// than coi. Vat lieu chuc xuong canh coi chi co the len loi trong khe hep giua canh
        /// coi va ham kep, chu khong phai khong gian trong.
        /// </summary>
        public FoilToolShape BuildDieHolder()
        {
            double bottom = -Math.Max(DieHeight, 1.0);
            double top = bottom + DieHolderHeight;
            double half = Math.Max(DieHolderHalfWidth, DieBodyHalfWidth + 1.0);

            return new FoilToolShape("ke coi", new List<FoilPoint2d>
            {
                new FoilPoint2d(-half, bottom),
                new FoilPoint2d(half, bottom),
                new FoilPoint2d(half, top),
                new FoilPoint2d(-half, top)
            });
        }

        /// <summary>
        /// DAM TREN (ram): mat duoi dam o dung chieu cao lam viec cua dao. Day la TRAN cua
        /// khong gian gia cong - moi canh da chan dung cao hon muc nay deu doi dam.
        /// </summary>
        public FoilToolShape BuildRam()
        {
            double bottom = Math.Max(PunchHeight, 1.0);
            double top = bottom + Math.Max(RamHeight, 1.0);
            double half = Math.Max(RamHalfWidth, PunchShankHalfWidth + 1.0);

            return new FoilToolShape("dam tren", new List<FoilPoint2d>
            {
                new FoilPoint2d(-half, bottom),
                new FoilPoint2d(half, bottom),
                new FoilPoint2d(half, top),
                new FoilPoint2d(-half, top)
            });
        }

        /// <summary>
        /// NGON CU HAU dat o dung khoang cach ga <paramref name="gauge"/>: mat chan cua ngon
        /// cu nam tai x = -gauge, than cu keo ve phia sau.
        ///
        /// Mat chan duoc lui ra sau mot khe rat nho de chinh mep phoi TI VAO cu (day la muc
        /// dich cua no) khong bi tinh la va cham. Phan vat lieu chay TIEP ve phia sau, o ngang
        /// tam ngon cu, moi la va cham that.
        /// </summary>
        public FoilToolShape BackGauge(double gauge)
        {
            // Ham tim thu tu goi phep kiem hang chuc nghin lan, va rat nhieu lan trong so do
            // dung chung mot khoang ga (cac canh bang nhau cua cung mot bien dang). Lam tron
            // vi tri ngon cu ve luoi 0.1 mm - dung bang khe ho da chua san - de nho lai duoc.
            int key = (int)Math.Round(Math.Max(gauge, 0.0) * 10.0);

            FoilToolShape shape;
            if (!_gaugeCache.TryGetValue(key, out shape))
            {
                shape = BuildBackGauge(key * 0.1);
                _gaugeCache[key] = shape;
            }

            return shape;
        }

        public FoilToolShape BuildBackGauge(double gauge)
        {
            const double clearance = 0.1;

            double face = -Math.Max(gauge, 0.0) - clearance;
            double back = face - Math.Max(BackGaugeLength, 1.0);
            double top = Math.Max(BackGaugeHeight, 1.0);
            double bottom = -clearance;

            return new FoilToolShape("cu hau", new List<FoilPoint2d>
            {
                new FoilPoint2d(back, bottom),
                new FoilPoint2d(face, bottom),
                new FoilPoint2d(face, top),
                new FoilPoint2d(back, top)
            });
        }

        /// <summary>
        /// Danh sach CHAY DAO co the dung, xep theo thu tu uu tien (dao thang truoc).
        ///
        /// Chi ve / kiem den chieu cao <paramref name="partHeight"/> cong mot khoang du: phan
        /// dao cao hon dinh chi tiet thi khong the cham vao chi tiet, nen cat bot KHONG lam
        /// mat mot va cham nao, ma hinh ve lai gon.
        /// </summary>
        public List<FoilToolShape> BuildPunches(double partHeight)
        {
            double height = Math.Max(partHeight * 1.15 + DieOpening, DieOpening * 3.0);

            List<FoilToolShape> punches = new List<FoilToolShape>
            {
                BuildStraightPunch("dao thang", PunchIncludedAngleDeg, PunchBladeHalfWidth, height)
            };

            if (!UseToolLibrary)
            {
                return punches;
            }

            // Dao co ngong: than dao lech han sang mot ben de canh da chan chui qua duoc.
            // Hai chieu lap - vi canh can tranh co the o ben nao cung duoc.
            FoilToolShape goose = BuildGooseneckPunch("dao co ngong", height);
            punches.Add(goose);
            punches.Add(goose.Mirrored("dao co ngong lap nguoc"));

            // Dao nhon: cho goc chan lon (be mep, goc nhon) ma dao thang khong voi toi.
            punches.Add(BuildStraightPunch(
                "dao nhon " + AcuteIncludedAngleDeg.ToString("0",
                    System.Globalization.CultureInfo.InvariantCulture) + " do",
                AcuteIncludedAngleDeg, GooseneckBladeHalfWidth, height));

            return punches;
        }

        private FoilToolShape BuildStraightPunch(
            string name, double includedAngleDeg, double bladeHalf, double height)
        {
            double tip = Math.Max(PunchTipRadius, DieOpening * 0.02);
            double blade = Math.Max(bladeHalf, tip);
            double tan = Math.Tan(includedAngleDeg * 0.5 * FoilMath.DegToRad);
            double nose = tan > 1e-9 ? (blade - tip) / tan : 0.0;
            if (nose < 0.0) nose = 0.0;

            double top = Math.Max(height, nose * 1.2);
            List<FoilPoint2d> outline = new List<FoilPoint2d>
            {
                new FoilPoint2d(-tip, 0.0),
                new FoilPoint2d(-blade, nose)
            };

            // Tren chieu cao lam viec la do ga, rong hon han.
            if (top > PunchHeight)
            {
                outline.Add(new FoilPoint2d(-blade, PunchHeight));
                outline.Add(new FoilPoint2d(-PunchShankHalfWidth, PunchHeight));
                outline.Add(new FoilPoint2d(-PunchShankHalfWidth, top));
                outline.Add(new FoilPoint2d(PunchShankHalfWidth, top));
                outline.Add(new FoilPoint2d(PunchShankHalfWidth, PunchHeight));
                outline.Add(new FoilPoint2d(blade, PunchHeight));
            }
            else
            {
                outline.Add(new FoilPoint2d(-blade, top));
                outline.Add(new FoilPoint2d(blade, top));
            }

            outline.Add(new FoilPoint2d(blade, nose));
            outline.Add(new FoilPoint2d(tip, 0.0));

            return new FoilToolShape(name, outline, includedAngleDeg);
        }

        /// <summary>
        /// Dao CO NGONG: mui dao van o truc dao, nhung than dao LECH HAN sang mot ben ke tu
        /// chieu cao GooseneckHeight. Nho cho lech do ma canh da chan dung cao ben phia con lai
        /// chui qua duoc - day dung la cong dung cua no trong xuong.
        /// </summary>
        private FoilToolShape BuildGooseneckPunch(string name, double height)
        {
            double tip = Math.Max(PunchTipRadius, DieOpening * 0.02);
            double blade = Math.Max(GooseneckBladeHalfWidth, tip);
            double tan = Math.Tan(PunchIncludedAngleDeg * 0.5 * FoilMath.DegToRad);
            double nose = tan > 1e-9 ? (blade - tip) / tan : 0.0;
            if (nose < 0.0) nose = 0.0;

            double neck = Math.Max(GooseneckHeight, nose * 1.5);
            double rise = Math.Max(GooseneckOffset * 0.6, DieOpening);
            double top = Math.Max(height, neck + rise + DieOpening);
            double offset = GooseneckOffset;

            // Di len theo MA DUOC MIEN (ben trai), qua dinh, roi xuong ma con lai.
            return new FoilToolShape(name, new List<FoilPoint2d>
            {
                new FoilPoint2d(-tip, 0.0),
                new FoilPoint2d(-blade, nose),
                new FoilPoint2d(-blade, neck),
                new FoilPoint2d(-blade + offset, neck + rise),
                new FoilPoint2d(-blade + offset, top),
                new FoilPoint2d(blade + offset, top),
                new FoilPoint2d(blade + offset, neck + rise),
                new FoilPoint2d(blade, neck),
                new FoilPoint2d(blade, nose),
                new FoilPoint2d(tip, 0.0)
            }, PunchIncludedAngleDeg);
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

        /// <summary>Tong chieu dai vat lieu thong RA NGOAI may (phia tho dung).</summary>
        public double FrontLength { get; set; }

        /// <summary>Tong chieu dai vat lieu nam VAO TRONG may (phia cu hau).</summary>
        public double BackLength { get; set; }

        /// <summary>CHAY DAO duoc chon cho lan chan nay - chinh la hinh se duoc ve.</summary>
        public FoilToolShape Punch { get; set; }

        /// <summary>THAN COI - hinh se duoc ve.</summary>
        public FoilToolShape Die { get; set; }

        /// <summary>DAM DUOI (ban may) - hinh se duoc ve.</summary>
        public FoilToolShape Bed { get; set; }

        /// <summary>HAM KEP COI - hinh se duoc ve.</summary>
        public FoilToolShape DieHolder { get; set; }

        /// <summary>DAM TREN. Null khi chi tiet thap hon mat duoi dam (khong the cham toi).</summary>
        public FoilToolShape Ram { get; set; }

        /// <summary>NGON CU HAU, dat o dung khoang cach ga cua lan chan nay.</summary>
        public FoilToolShape BackGauge { get; set; }

        /// <summary>
        /// Toan bo hinh dung cu / bo phan may cua lan chan nay, theo thu tu ve (duoi truoc).
        /// Day dung la bo da duoc dung de kiem va cham, nen cai VE RA chinh la cai da KIEM.
        /// </summary>
        public List<FoilToolShape> Tools { get; private set; } = new List<FoilToolShape>();

        /// <summary>0 = dung dao mac dinh; lon hon 0 = da phai doi sang dao khac.</summary>
        public int PunchIndex { get; set; }

        /// <summary>Ten chay dao da chon.</summary>
        public string PunchName { get { return Punch != null ? Punch.Name : string.Empty; } }

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
            return Evaluate(shape, bend, tooling, true);
        }

        /// <summary>
        /// <paramref name="measureClearance"/> = false thi bo qua viec DO khoang ho den dung cu.
        /// Viec do khong anh huong ket luan cham hay khong, chi de bao cao - ma no lai la phan
        /// ton thoi gian nhat. Luc tim thu tu (hang chuc nghin lan goi) thi tat di.
        /// </summary>
        public static FoilBendMounting Evaluate(
            FoilFormedShape shape,
            FoilBendInfo bend,
            FoilToolingGeometry tooling,
            bool measureClearance)
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
            // Phan chi tiet thong xuong SAU HON DAY COI thi da ra khoi vung than coi, luc do
            // no chi con duoc phep nam o PHIA TRUOC may (khoang trong noi tho dung). Neu ca hai
            // phia deu co phan nhu vay thi khong dat duoc phoi len may.
            //
            // Luu y: phan chi tiet chi thong xuong NONG HON day coi thi khong tinh o day - no
            // duoc xet bang phep giao voi khoi than coi o buoc 3, va hoan toan co the cuoi om
            // lay coi ma khong cham gi.
            bool leftDeep = false;
            bool rightDeep = false;
            for (int i = 0; i < count; i++)
            {
                if (ly[i] >= -tooling.DieHeight) continue;
                if (i <= startNode) leftDeep = true;
                else if (i >= endNode) rightDeep = true;
            }

            // Chi con DUY NHAT mot bac tu do: soi guong qua truc Y (doi dau nao quay vao cu hau).
            // Xoay 180 do trong mat phang se lam khau chan mo XUONG nen khong dung duoc; ket hop
            // xoay + lat ton chinh la phep soi guong x -> -x, giu nguyen chieu "len".
            //
            // QUY TAC CHON: DOAN DAI PHAI QUAY RA NGOAI (phia truoc, +X).
            //
            // Tho dung phia truoc va do phan dai; phia sau la cu hau va than may, khong co cho
            // cho mot doan dai chui vao. Vi vay canh NGAN lam chuan ga vao cu hau, con canh DAI
            // thong ra ngoai. Rieng phan thong xuong sau hon day coi thi bat buoc phai o phia
            // truoc, nen dieu kien do duoc uu tien truoc.
            double leftBulk = SideLength(shape, bend.Index, true);
            double rightBulk = SideLength(shape, bend.Index, false);

            bool endSwapped;
            if (leftDeep != rightDeep)
            {
                endSwapped = leftDeep;              // dua phan thong xuong ve phia truoc
            }
            else
            {
                endSwapped = leftBulk > rightBulk;  // dua doan DAI ra ngoai
            }

            if (endSwapped)
            {
                for (int i = 0; i < count; i++) lx[i] = -lx[i];
            }

            mount.EndSwapped = endSwapped;
            mount.FrontLength = endSwapped ? leftBulk : rightBulk;
            mount.BackLength = endSwapped ? rightBulk : leftBulk;

            if (mount.BackLength > mount.FrontLength + FoilMath.LengthTolerance)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Doan dai {0:0.##} mm buoc phai quay vao trong may (do dau kia thong xuong) - kho thao tac.",
                    mount.BackLength));
            }

            if (leftDeep && rightDeep)
            {
                mount.Add(FoilBendIssueLevel.Blocking,
                    "Hai dau chi tiet deu thong xuong sau hon day coi - kho dat phoi len may.");
            }

            // --- 4. Hai canh ke phai gac duoc len vai coi ---------------------------------
            double leftFlange = FlangeLength(shape, bend.Index, true);
            double rightFlange = FlangeLength(shape, bend.Index, false);
            double shortFlange = Math.Min(leftFlange, rightFlange);

            // Chuan ga phai biet tu day: ngon cu hau duoc dat o dung khoang nay, va no la mot
            // vat the that trong may nen phai co mat truoc khi kiem va cham o buoc 7.
            double gauge = endSwapped ? rightFlange : leftFlange;
            mount.GaugeLength = gauge;
            if (shortFlange + 1e-9 < tooling.MinFlangeLength)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Canh ke chi dai {0:0.##} mm < canh nho nhat {1:0.##} mm cua coi V{2:0.##}.",
                    shortFlange, tooling.MinFlangeLength, tooling.DieOpening));
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

            // --- 7. DAT LEN MAY: KIEM VA CHAM TUNG BO PHAN, ROI CHON CHAY DAO -------------
            //
            // Khong gian trong may KHONG phai la khoang trong tu do. Tu duoi len no bi chan
            // boi: dam duoi, ham kep coi, than coi; phia sau la ngon cu hau dat o dung khoang
            // ga; phia tren la dam mang do ga dao. Moi mon deu la mot da giac KIN, va chinh
            // nhung da giac nay duoc ve ra ban ve - nen cai tho NHIN THAY la cai da duoc KIEM.
            //
            // Rieng phep kiem voi coi va dao thi mien phan NGOAI hai canh ke duong chan dang
            // thuc hien (tu goc da chan dau tien tro ra): hai canh ke chinh la vat lieu dang
            // duoc uon, no ap vao ma dao va an xuong long chu V nen khong tinh la va cham.
            int leftGuard = FirstFormedCornerNode(shape, startNode, true);
            int rightGuard = FirstFormedCornerNode(shape, endNode, false);

            double partHeight = 0.0;
            foreach (FoilPoint2d p in mount.MachinePoints)
            {
                if (p.Y > partHeight) partHeight = p.Y;
            }

            foreach (FoilPoint2d p in mount.MachinePointsBefore)
            {
                if (p.Y > partHeight) partHeight = p.Y;
            }

            // Coi, ham kep va dam duoi la co dinh - khong co lua chon nao khac.
            mount.Die = tooling.Die;
            mount.DieHolder = tooling.DieHolder;
            mount.Bed = tooling.Bed;

            if (HitsTool(mount, leftGuard, rightGuard, mount.Die))
            {
                mount.Add(FoilBendIssueLevel.Blocking,
                    "Phan da chan cham than coi - can coi hep hon hoac doi thu tu.");
            }

            if (HitsTool(mount, leftGuard, rightGuard, mount.DieHolder))
            {
                mount.Add(FoilBendIssueLevel.Blocking,
                    "Phan da chan cham ham kep coi - khe hai ben coi khong du rong.");
            }

            if (HitsTool(mount, leftGuard, rightGuard, mount.Bed))
            {
                mount.Add(FoilBendIssueLevel.Blocking,
                    "Phan da chan cham dam duoi cua may - can doi thu tu hoac ke cao coi.");
            }

            // DAM TREN chi can xet khi chi tiet voi toi no. Thap hon mat duoi dam thi khong
            // the cham - luc do khong kiem va cung KHONG VE, de hinh khong bi keo cao vo ich.
            if (partHeight >= tooling.PunchHeight - FoilMath.LengthTolerance)
            {
                mount.Ram = tooling.Ram;

                // Muc CANH BAO chu khong phai CHAN: doi dam tren khong phai loi cua thu tu
                // chan ma la loi CHIEU CAO DAO. Doi thu tu co the tranh duoc mot phan, nhung
                // cach chua that la lap dao cao hon - nen phai noi ra dung nhu vay, chu khong
                // tuyen bo la khong chan duoc.
                if (HitsTool(mount, leftGuard, rightGuard, mount.Ram))
                {
                    mount.Add(FoilBendIssueLevel.Warning, string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Chi tiet cao {0:0.##} mm doi DAM TREN - can dao cao hon {1:0.##} mm.",
                        partHeight, tooling.PunchHeight));
                }
            }

            // NGON CU HAU dat o dung khoang ga cua lan chan nay. Chi xet luc DAT PHOI (truoc
            // khi chan): do la luc phoi ti vao cu. Khi dao an xuong thi canh sau nhac len va
            // cu hau cung lui ra, nen khong con y nghia gi de kiem.
            mount.BackGauge = tooling.BackGauge(gauge);

            // Bo qua som: khong co diem nao lui ra sau mat cu thi chac chan khong cham. Phep
            // so sanh nay chi duyet toa do X nen re hon han phep kiem da giac.
            double behind = 0.0;
            foreach (FoilPoint2d q in mount.MachinePointsBefore)
            {
                if (q.X < behind) behind = q.X;
            }

            if (behind < mount.BackGauge.MaxX &&
                ChainHitsAll(mount.MachinePointsBefore, mount.BackGauge))
            {
                mount.Add(FoilBendIssueLevel.Warning,
                    "Phan phia sau vuong NGON CU HAU - phai ha cu, xoay ngon cu hoac ga bang cu truoc.");
            }

            if (gauge > tooling.BackGaugeTravel)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Chuan ga {0:0.##} mm vuot khoang vuon {1:0.##} mm cua cu hau - phai lay dau tay.",
                    gauge, tooling.BackGaugeTravel));
            }

            // CHAY DAO thi co the doi: thu lan luot ca bo, lay con DAU TIEN khong vuong.
            // Con dao duoc chon chinh la con se duoc VE ra ban ve.
            List<FoilToolShape> punches = tooling.Punches(partHeight);
            FoilToolShape chosen = null;
            int chosenIndex = 0;

            for (int i = 0; i < punches.Count; i++)
            {
                FoilToolShape candidate = punches[i];

                // Dao phai du nhon cho goc chan nay.
                if (bend.BendAngleDeg >= candidate.MaxBendAngleDeg - 1e-9) continue;

                if (HitsTool(mount, leftGuard, rightGuard, candidate)) continue;

                chosen = candidate;
                chosenIndex = i;
                break;
            }

            if (chosen == null)
            {
                chosen = punches[0];
                chosenIndex = 0;

                bool tooBlunt = true;
                foreach (FoilToolShape candidate in punches)
                {
                    if (bend.BendAngleDeg < candidate.MaxBendAngleDeg - 1e-9) tooBlunt = false;
                }

                mount.Add(FoilBendIssueLevel.Blocking, tooBlunt
                    ? string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Goc chan {0:0.#} do vuot kha nang cua moi loai dao dang khai bao.",
                        bend.BendAngleDeg)
                    : "Moi loai dao deu vuong phan da chan - can doi thu tu chan.");
            }

            mount.Punch = chosen;
            mount.PunchIndex = chosenIndex;

            // Thu tu ve: duoi truoc, tren sau.
            mount.Tools.Add(mount.Bed);
            mount.Tools.Add(mount.DieHolder);
            mount.Tools.Add(mount.Die);
            mount.Tools.Add(mount.BackGauge);
            if (mount.Ram != null) mount.Tools.Add(mount.Ram);
            mount.Tools.Add(chosen);

            if (measureClearance)
            {
                double gap = DistanceToTool(mount, leftGuard, rightGuard, chosen);
                mount.PunchConstrained = gap != double.MaxValue;
                mount.PunchClearance = mount.PunchConstrained ? gap : 0.0;
            }

            // Be mep (hem): goc qua lon thi tren may phai chan nhon truoc roi ep bet - 2 nguyen cong.
            if (bend.BendAngleDeg >= 150.0)
            {
                mount.Add(FoilBendIssueLevel.Note, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Goc {0:0.#} do - be mep: chan nhon truoc roi ep bet, 2 nguyen cong.",
                    bend.BendAngleDeg));
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
            // Phia sau la -X; canh o phia do la canh ga vao cu hau.
            //
            // KHONG duoc doi dau chi de lay chuan ga dai hon: lam vay se day DOAN DAI vao trong
            // may, va nhu vay thi khong ai chan duoc. Chuan ga ngan chi la mot luu y - tho van
            // ga duoc bang cach ha cu hoac dat com chan. Con doan dai chui vao may thi chiu.
            if (gauge + 1e-9 < tooling.MinGaugeLength)
            {
                mount.Add(FoilBendIssueLevel.Warning, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Chuan ga phia cu hau chi dai {0:0.##} mm < {1:0.##} mm - de lech vi tri.",
                    gauge, tooling.MinGaugeLength));
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

            // Chi tiet cang cao cang kho cam.
            cost += mount.PartHeight * 0.02;

            // Phai doi sang dao dac chung la mat cong thay dung cu - uu tien dao thang.
            cost += mount.PunchIndex * 12.0;

            return cost;
        }

        /// <summary>
        /// Duong gap khuc chi tiet co CHAM vao mon dung cu nay khong - xet ca luc DAT PHOI VAO
        /// (chua chan) lan luc CHAN XONG, va xet theo TUNG DOAN chu khong theo dinh: mot canh
        /// dai hoan toan co the quet ngang qua dung cu trong khi hai dau van nam ngoai.
        /// </summary>
        private static bool HitsTool(
            FoilBendMounting mount, int leftGuard, int rightGuard, FoilToolShape tool)
        {
            if (tool == null) return false;

            return ChainHits(mount.MachinePointsBefore, leftGuard, rightGuard, tool)
                || ChainHits(mount.MachinePoints, leftGuard, rightGuard, tool);
        }

        private static bool ChainHits(
            List<FoilPoint2d> points, int leftGuard, int rightGuard, FoilToolShape tool)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (!OutsideGuard(i, leftGuard, rightGuard)) continue;
                if (!tool.MayTouch(points[i], points[i + 1])) continue;

                if (FoilToolGeometry.SegmentHitsPolygon(points[i], points[i + 1], tool.Outline))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Khoang ho nho nhat den mon dung cu. double.MaxValue neu khong co gi de do.</summary>
        private static double DistanceToTool(
            FoilBendMounting mount, int leftGuard, int rightGuard, FoilToolShape tool)
        {
            if (tool == null) return double.MaxValue;

            double best = double.MaxValue;
            best = Math.Min(best, ChainDistance(mount.MachinePointsBefore, leftGuard, rightGuard, tool));
            best = Math.Min(best, ChainDistance(mount.MachinePoints, leftGuard, rightGuard, tool));
            return best;
        }

        private static double ChainDistance(
            List<FoilPoint2d> points, int leftGuard, int rightGuard, FoilToolShape tool)
        {
            double best = double.MaxValue;

            for (int i = 0; i < points.Count - 1; i++)
            {
                if (!OutsideGuard(i, leftGuard, rightGuard)) continue;

                double d = FoilToolGeometry.SegmentDistanceToPolygon(
                    points[i], points[i + 1], tool.Outline);
                if (d < best) best = d;
            }

            return best;
        }

        /// <summary>
        /// Kiem va cham tren TOAN BO duong gap khuc, khong mien doan nao.
        ///
        /// Phep kiem voi coi / dao phai mien hai canh dang duoc uon (chung ap vao ma dao va an
        /// xuong long chu V - do la viec binh thuong, khong phai va cham). Nhung NGON CU HAU
        /// nam xa coi, khong co doan nao duoc quyen cham no ca, nen dung ham nay.
        /// </summary>
        private static bool ChainHitsAll(List<FoilPoint2d> points, FoilToolShape tool)
        {
            if (tool == null) return false;

            for (int i = 0; i < points.Count - 1; i++)
            {
                if (!tool.MayTouch(points[i], points[i + 1])) continue;

                if (FoilToolGeometry.SegmentHitsPolygon(points[i], points[i + 1], tool.Outline))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Doan nay co nam NGOAI hai canh ke duong chan dang thuc hien khong.</summary>
        private static bool OutsideGuard(int index, int leftGuard, int rightGuard)
        {
            bool beyondLeft = leftGuard >= 0 && (index + 1) <= leftGuard;
            bool beyondRight = rightGuard >= 0 && index >= rightGuard;
            return beyondLeft || beyondRight;
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
        /// TONG chieu dai vat lieu ve mot phia cua duong chan, di het den dau chi tiet.
        ///
        /// Khac voi <see cref="FlangeLength"/> (chi do den goc da chan dau tien, dung lam chuan
        /// ga): day la KHOI LUONG that ma tho phai cam o phia do. No quyet dinh dau nao quay ra
        /// ngoai may.
        /// </summary>
        public static double SideLength(FoilFormedShape shape, int bendIndex, bool toLeft)
        {
            int start = toLeft ? shape.ZoneStartNode[bendIndex] : shape.ZoneEndNode[bendIndex];
            if (start < 0) return 0.0;

            double length = 0.0;

            if (toLeft)
            {
                for (int i = start; i > 0; i--)
                {
                    length += shape.Points[i].DistanceTo(shape.Points[i - 1]);
                }
            }
            else
            {
                for (int i = start; i < shape.Points.Count - 1; i++)
                {
                    length += shape.Points[i].DistanceTo(shape.Points[i + 1]);
                }
            }

            return length;
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

    }
}
