using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AUTOCAD_COMMANDS.Nesting.Core;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// Nesting settings: sheet per material (from the approved catalog only), gap, edge margin,
    /// rotations, mirror (OFF by default), time budget / orderings, output options.
    /// The sheet catalog can be edited here (saved to ghophoi_sheets.tsv).
    ///
    /// Bo cuc theo TAB, moi tab mot viec, de buoc CHON KHO PHOI khong bi chen giua danh muc kho
    /// va hang chuc o thong so nhu truoc:
    ///   1. Vat lieu &amp; kho phoi - moi dong mot vat lieu (LOAI + DO DAY), chon kho, thay ngay
    ///      chi tiet lon nhat co VUA kho khong;
    ///   2. Thong so ghep - khe cat, le mep, xoay, muc tim kiem;
    ///   3. Xuat ket qua;
    ///   4. Danh muc kho phoi - sua danh sach kho duoc phep.
    /// </summary>
    internal sealed class NestingSettingsForm : Form
    {
        private const int ColType = 0, ColThick = 1, ColParts = 2, ColQty = 3, ColBiggest = 4, ColSheet = 5, ColCheck = 6;

        private static readonly Color Good = Color.FromArgb(0, 128, 0);
        private static readonly Color Bad = Color.FromArgb(190, 0, 0);
        private static readonly Color Info = Color.FromArgb(0, 102, 204);

        private readonly GhoPhoiSettings _settings;
        private readonly List<SheetSpec> _catalog;
        private readonly SortedDictionary<string, int> _materials;
        private readonly Dictionary<string, double[]> _biggest;
        private readonly Dictionary<string, int> _partCounts;
        private bool _autoPickDone;
        private bool _loading;

        private TabControl _tabs;
        private TabPage _tabCatalog;
        private DataGridView _materialGrid;
        private Label _materialSummary;
        private DataGridView _catalogGrid;
        private Label _catalogNote;
        private NumericUpDown _numGap;
        private NumericUpDown _numMargin;
        private NumericUpDown _numBudget;
        private NumericUpDown _numSeed;
        private NumericUpDown _numExtra;
        private NumericUpDown _numSpacing;
        private ComboBox _cboRotation;
        private CheckBox _chkMirror;
        private CheckBox _chkInsideHole;
        private CheckBox _chkHere;
        private CheckBox _chkBlocks;
        private CheckBox _chkLabels;
        private CheckBox _chkLabelNameOrder;
        private CheckBox _chkDeterministic;
        private ComboBox _cboSearch;
        private ComboBox _cboAlgorithm;
        private CheckBox _chkOpen;
        private CheckBox _chkFixture;
        private Label _ruleNote;

        /// <param name="materials">Material -> total quantity to nest.</param>
        /// <param name="biggest">
        /// Vat lieu -> {canh dai nhat, canh ngan nhat} cua chi tiet LON NHAT thuoc vat lieu do.
        /// Dung de tu chon san kho phoi DU LON: neu kho dang nho hon chi tiet thi du thuat toan
        /// co gioi den may cung khong xep duoc, va nguoi dung chi thay "chua xep" ma khong biet
        /// vi sao.
        /// </param>
        public NestingSettingsForm(
            GhoPhoiSettings settings, List<SheetSpec> catalog, SortedDictionary<string, int> materials,
            Dictionary<string, double[]> biggest)
            : this(settings, catalog, materials, biggest, null)
        {
        }

        /// <param name="partCounts">Vat lieu -> so CHI TIET (nhom hinh) - chi de hien thi. Null = khong hien.</param>
        public NestingSettingsForm(
            GhoPhoiSettings settings, List<SheetSpec> catalog, SortedDictionary<string, int> materials,
            Dictionary<string, double[]> biggest, Dictionary<string, int> partCounts)
        {
            _settings = settings.Clone();
            _catalog = new List<SheetSpec>(catalog);
            _materials = materials;
            _biggest = biggest ?? new Dictionary<string, double[]>(StringComparer.Ordinal);
            _partCounts = partCounts;
            BuildUi();
            LoadValues();
            DialogPlacement.Attach(this, "settings");
        }

        /// <summary>Help cho o "Tim du so luot xep" va o "Thoi gian toi da".</summary>
        internal const string DeterministicHelpText =
            "TIM DU SO LUOT XEP\r\n" +
            "\r\n" +
            "BAT (mac dinh):\r\n" +
            "  - Chay DU so luot xep da cau hinh, khong cat bot.\r\n" +
            "  - Ket qua on dinh: cung du lieu + cung cai dat thi moi may ra cung mot bo cuc.\r\n" +
            "  - Thoi gian thuc te CO THE VUOT \"Thoi gian toi da\" (o nay khi do KHONG phai gioi han cung).\r\n" +
            "  - Muon dung som: bam nut \"Dung (giu ket qua tot nhat)\".\r\n" +
            "\r\n" +
            "TAT:\r\n" +
            "  - Het \"Thoi gian toi da\" thi khong bat dau luot xep moi (luot dang chay van chay xong).\r\n" +
            "  - May nhanh / cham co the chay duoc so luot khac nhau.\r\n" +
            "  - Vi vay ket qua co the khac nhau giua cac may hoac giua cac lan chay.";

        /// <summary>Help cho o "Muc tim kiem".</summary>
        internal const string SearchEffortHelpText =
            "MUC TIM KIEM\r\n" +
            "\r\n" +
            "Nhanh (mac dinh): moi chi tiet thu 3 cach dat (trai-duoi, ngan nhat, OM SAT) tren nhieu thu tu xep.\r\n" +
            "\r\n" +
            "Can bang:\r\n" +
            "  - Sau khi co ket qua tot nhat, chay them toi da 64 luot DOI THU TU chi tiet.\r\n" +
            "  - Chi nhan cach xep moi khi no tot hon, nen khong te hon muc Nhanh.\r\n" +
            "  - Thuong ngan hon them khoang 1%, nhung KHONG dam bao luon tot hon.\r\n" +
            "  - Lau gap khoang 3 lan muc Nhanh.\r\n" +
            "  - Bam \"Dung\" se giu ket qua tot nhat da co.";

        /// <summary>Help cho o "Thuat toan xep".</summary>
        internal const string AlgorithmHelpText =
            "THUAT TOAN XEP (cach tim vi tri dat cho tung chi tiet)\r\n" +
            "\r\n" +
            "Diem ung vien (mac dinh): quet doc chieu dai to + vi tri cham nhau + nen sat. Nhanh.\r\n" +
            "\r\n" +
            "NFP da giac that: tinh vung KHONG-VUA chinh xac (tong Minkowski) giua chi tiet dang xep\r\n" +
            "  va moi chi tiet da dat, roi lay ca vi tri trong khe giua hai chi tiet. Manh nhat voi\r\n" +
            "  hinh cong / lom long vao nhau. Cham hon khoang 3-4 lan. Lo kin van xu ly nhu cu.\r\n" +
            "\r\n" +
            "So sanh: chay CA HAI, hien bang doi chieu (so to, chieu dai, thoi gian) va de ban chon\r\n" +
            "  ket qua de ve. Tren bo thu: moi cach thang khoang mot nua so bai, chon cai tot hon\r\n" +
            "  ngan hon trung binh ~0.4%.\r\n" +
            "\r\n" +
            "Moi cach deu qua cung phep kiem va cham chinh xac va cung validator.";

        public GhoPhoiSettings Settings { get { return _settings; } }

        public List<SheetSpec> Catalog { get { return _catalog; } }

        public bool CatalogChanged { get; private set; }

        /// <summary>Chosen sheet per material (valid after OK).</summary>
        public Dictionary<string, SheetSpec> SheetByMaterial { get; private set; }

        // ==================================================================================
        // GIAO DIEN
        // ==================================================================================
        private void BuildUi()
        {
            Text = "GHOPHOI - CAI DAT GHEP PHOI";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(760, 520);

            ToolTip help = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
            Disposed += (s, e) => help.Dispose();

            _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            _tabs.TabPages.Add(BuildMaterialTab());
            _tabs.TabPages.Add(BuildParameterTab(help));
            _tabs.TabPages.Add(BuildOutputTab(help));
            _tabCatalog = BuildCatalogTab();
            _tabs.TabPages.Add(_tabCatalog);

            // ---- buttons (luon nhin thay, khong nam trong tab nao) ----
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 6, 8, 6)
            };
            Button ok = new Button { Text = "GHEP PHOI  >", Width = 150, Height = 32, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            ok.Click += (s, e) =>
            {
                if (!Commit()) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Button cancel = new Button { Text = "Huy", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;

            Controls.Add(_tabs);
            Controls.Add(buttons);
        }

        private TabPage BuildMaterialTab()
        {
            TabPage page = new TabPage("1. Vat lieu & kho phoi") { Padding = new Padding(10) };

            Label intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                ForeColor = Info,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Text = "Moi dong la MOT vat lieu (loai + do day) - ghep RIENG, khong bao gio tron (INOX 1.2 va THEP 1.2 la hai dong).\r\n" +
                       "Chon kho phoi o cot \"Kho phoi\". Cot \"Kiem tra\" cho biet chi tiet lon nhat co vua kho khong."
            };

            _materialGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32,
                EnableHeadersVisualStyles = false
            };
            _materialGrid.RowTemplate.Height = 30;
            _materialGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _materialGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 247);
            _materialGrid.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            _materialGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
            _materialGrid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
            _materialGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);

            _materialGrid.Columns.Add(Col("Loai vat lieu", 16, true, true));
            _materialGrid.Columns.Add(Col("Do day", 11, true, true));
            _materialGrid.Columns.Add(Col("So chi tiet", 10, true, false));
            _materialGrid.Columns.Add(Col("Tong SL", 9, true, false));
            _materialGrid.Columns.Add(Col("Chi tiet lon nhat (mm)", 17, true, false));
            DataGridViewComboBoxColumn sheetColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "Kho phoi  (bam de chon)",
                FillWeight = 22,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            sheetColumn.DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 230);
            _materialGrid.Columns.Add(sheetColumn);
            _materialGrid.Columns.Add(Col("Kiem tra", 15, true, true));
            _materialGrid.Columns[ColParts].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _materialGrid.Columns[ColQty].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            _materialGrid.DataError += (s, e) => { e.ThrowException = false; };
            _materialGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Combo trong luoi chi bao doi gia tri khi roi o - chot ngay de cot Kiem tra cap nhat luon.
                if (_materialGrid.IsCurrentCellDirty) _materialGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _materialGrid.CellValueChanged += (s, e) =>
            {
                if (!_loading && e.RowIndex >= 0 && e.ColumnIndex == ColSheet) UpdateMaterialChecks();
            };

            Panel foot = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            _materialSummary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            LinkLabel editCatalog = new LinkLabel
            {
                Dock = DockStyle.Right,
                Width = 260,
                TextAlign = ContentAlignment.MiddleRight,
                Text = "Thieu kho phoi? Sua danh muc kho phoi..."
            };
            editCatalog.LinkClicked += (s, e) => _tabs.SelectedTab = _tabCatalog;
            foot.Controls.Add(_materialSummary);
            foot.Controls.Add(editCatalog);

            page.Controls.Add(_materialGrid);
            page.Controls.Add(foot);
            page.Controls.Add(intro);
            return page;
        }

        private TabPage BuildParameterTab(ToolTip help)
        {
            TabPage page = new TabPage("2. Thong so ghep") { Padding = new Padding(10), AutoScroll = true };

            _numGap = Num(0, 100, 2);
            _numMargin = Num(0, 200, 2);
            _numBudget = Num(1, 600, 0);
            _numSeed = Num(0, 1000000, 0);
            _numExtra = Num(0, 50, 0);
            _cboRotation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _cboRotation.Items.AddRange(new object[] { "0 / 90 / 180 / 270 do", "0 / 180 do (giu chieu van)", "Khong xoay" });
            _chkMirror = new CheckBox { Text = "Cho phep LAT GUONG (canh bao: doi chieu chi tiet chan!)", AutoSize = true, ForeColor = Color.DarkRed };
            _chkInsideHole = new CheckBox { Text = "Cho phep dat chi tiet vao LO KIN cua chi tiet khac", AutoSize = true };

            // Bat: may nhanh hay cham deu ra CUNG mot ket qua, doi lai co the chay qua han muc
            // gio (muon dung thi bam nut dung). Tat: quay ve hanh vi cu, co tran gio nhung may
            // cham co the ra bo cuc te hon - da do duoc chenh 2% tren ban ve that.
            _chkDeterministic = new CheckBox
            {
                Text = "Tim du so luot xep (ket qua khong phu thuoc toc do may)",
                AutoSize = true
            };

            // Thu tu muc trong danh sach = gia tri SearchEffort (0 = Nhanh, 1 = Can bang).
            _cboSearch = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
            _cboSearch.Items.AddRange(new object[] { "Nhanh", "Can bang - thu them cach xep, it phoi hon, cham hon" });

            TableLayoutPanel spacing = Grid2();
            AddPair(spacing, "Khe cat giua chi tiet (mm):", _numGap);
            AddPair(spacing, "Le mep to (mm):", _numMargin);
            _ruleNote = new Label { AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Info, Margin = new Padding(3, 6, 3, 0) };
            spacing.Controls.Add(_ruleNote, 0, spacing.RowCount);
            spacing.SetColumnSpan(_ruleNote, 2);
            spacing.RowCount++;

            TableLayoutPanel turning = Grid2();
            AddPair(turning, "Huong xoay:", _cboRotation);
            AddWide(turning, _chkMirror);
            AddWide(turning, _chkInsideHole);

            // Thu tu muc = gia tri GhoPhoiAlgorithmMode.
            _cboAlgorithm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
            _cboAlgorithm.Items.AddRange(new object[]
            {
                "Diem ung vien (nhanh - mac dinh)",
                "NFP da giac that (cham hon 3-4 lan, manh voi hinh cong / long nhau)",
                "Chay CA HAI va so sanh - chon ket qua tot hon"
            });

            TableLayoutPanel search = Grid2();
            Label algorithmLabel = AddPair(search, "Thuat toan xep:", _cboAlgorithm);
            Label searchLabel = AddPair(search, "Muc tim kiem:", _cboSearch);
            AddWide(search, _chkDeterministic);
            AddPair(search, "Thoi gian toi da (giay):", _numBudget);
            AddPair(search, "So thu tu ngau nhien them:", _numExtra);
            AddPair(search, "Seed:", _numSeed);

            help.SetToolTip(_chkDeterministic, DeterministicHelpText);
            help.SetToolTip(_numBudget, DeterministicHelpText);
            help.SetToolTip(_cboSearch, SearchEffortHelpText);
            help.SetToolTip(_cboAlgorithm, AlgorithmHelpText);
            help.SetToolTip(algorithmLabel, AlgorithmHelpText);
            help.SetToolTip(searchLabel, SearchEffortHelpText);

            _numGap.ValueChanged += (s, e) => UpdateRuleNote();
            _numMargin.ValueChanged += (s, e) =>
            {
                UpdateRuleNote();
                UpdateMaterialChecks();       // le mep doi thi "vua kho" cung doi
            };

            FlowLayoutPanel stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            stack.Controls.Add(Section("Khoang cach", spacing));
            stack.Controls.Add(Section("Xoay / lat / lo kin", turning));
            stack.Controls.Add(Section("Muc tim kiem (thu tu xep: luon TAM TO TRUOC, tam nho lap cho trong)", search));
            page.Controls.Add(stack);
            return page;
        }

        private TabPage BuildOutputTab(ToolTip help)
        {
            TabPage page = new TabPage("3. Xuat ket qua") { Padding = new Padding(10) };

            _chkHere = new CheckBox { Text = "Ve thang vao ban ve nay (chon diem dat)", AutoSize = true };
            _chkBlocks = new CheckBox { Text = "EXPLODE + PURGE sau khi ghep (bo tick = giu block de ghep lai)", AutoSize = true };
            _chkLabels = new CheckBox { Text = "Hien thi ma P + STT tren phoi", AutoSize = true };
            _chkLabelNameOrder = new CheckBox { Text = "Ghi them ten phoi + ten don hang (ca khi chi co mot don)", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
            _chkLabels.CheckedChanged += (s, e) => _chkLabelNameOrder.Enabled = _chkLabels.Checked;
            _chkOpen = new CheckBox { Text = "Mo ban ve sau khi tao (khi xuat ra file moi)", AutoSize = true };
            _chkFixture = new CheckBox { Text = "Luu fixture test (.nest)", AutoSize = true };
            _numSpacing = Num(0, 10000, 0);

            help.SetToolTip(_chkBlocks,
                "TICK (mac dinh): xep xong EXPLODE het block va PURGE sach - ban ve gon, di cat CNC." + Environment.NewLine +
                "BO TICK: moi chi tiet la 1 BLOCK mang san SL / vat lieu / don. Muon ghep lai toi uu hon" + Environment.NewLine +
                "thi go GHOPHOI, quet ca vung ket qua cu la du - khong can quet lai ban ve goc." + Environment.NewLine +
                "Khung to / nhan / chu (layer GHOPHOI_*) tu dong bi bo qua khi quet.");

            TableLayoutPanel t = Grid2();
            AddWide(t, _chkHere);
            AddWide(t, _chkBlocks);
            AddWide(t, _chkLabels);
            AddWide(t, _chkLabelNameOrder);
            AddWide(t, _chkOpen);
            AddWide(t, _chkFixture);
            AddPair(t, "Khoang cach giua cac to (mm):", _numSpacing);

            FlowLayoutPanel stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            stack.Controls.Add(Section("Xuat ket qua", t));
            page.Controls.Add(stack);
            return page;
        }

        private TabPage BuildCatalogTab()
        {
            TabPage page = new TabPage("Danh muc kho phoi") { Padding = new Padding(10) };

            Label intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 50,
                Text = "Kho phoi duoc phep dung. Rong = chieu Y, Dai = chieu X. Them dong o cuoi bang, xoa dong bang phim Delete.\r\n" +
                       "\"Vat lieu tuong thich\": de TRONG = moi vat lieu; ghi loai (INOX), do day (1.2MM) hoac ca hai (INOX 1.2MM), cach nhau dau phay."
            };

            _catalogGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ten", FillWeight = 30 });
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rong (mm)", FillWeight = 18 });
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dai (mm)", FillWeight = 18 });
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vat lieu tuong thich", FillWeight = 34 });
            _catalogGrid.CellEndEdit += (s, e) => { CatalogChanged = true; RefreshSheetChoices(); };
            _catalogGrid.UserDeletedRow += (s, e) => { CatalogChanged = true; RefreshSheetChoices(); };
            _catalogNote = new Label { Dock = DockStyle.Bottom, Height = 40, ForeColor = Bad };

            page.Controls.Add(_catalogGrid);
            page.Controls.Add(_catalogNote);
            page.Controls.Add(intro);
            return page;
        }

        private static DataGridViewTextBoxColumn Col(string header, int weight, bool readOnly, bool bold)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            if (bold) c.DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            return c;
        }

        private static GroupBox Section(string title, Control body)
        {
            GroupBox g = new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 6, 10, 8),
                Margin = new Padding(0, 0, 0, 10),
                MinimumSize = new Size(700, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            // KHONG Dock: GroupBox tu co gian theo kich thuoc that cua noi dung (AutoSize).
            body.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            body.Location = new Point(10, 22);
            g.Controls.Add(body);
            return g;
        }

        private static TableLayoutPanel Grid2()
        {
            TableLayoutPanel t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static Label AddPair(TableLayoutPanel t, string label, Control c)
        {
            Label l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
            t.Controls.Add(l, 0, t.RowCount);
            t.Controls.Add(c, 1, t.RowCount);
            t.RowCount++;
            return l;
        }

        private static void AddWide(TableLayoutPanel t, Control c)
        {
            t.Controls.Add(c, 0, t.RowCount);
            t.SetColumnSpan(c, 2);
            t.RowCount++;
        }

        private static NumericUpDown Num(decimal min, decimal max, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = decimals > 0 ? 0.5M : 1M,
                Width = 110
            };
        }

        private static decimal Clamp(double v, NumericUpDown n)
        {
            return Clamp(v, n.Minimum, n.Maximum);
        }

        /// <summary>Khong bao gio nem loi: (decimal) cua NaN / vo cuc / so &gt; 7.9e28 se nem OverflowException.</summary>
        private static decimal Clamp(double v, decimal min, decimal max)
        {
            if (double.IsNaN(v)) return min;
            if (v <= (double)min) return min;
            if (v >= (double)max) return max;
            decimal d = (decimal)v;
            if (d < min) return min;
            if (d > max) return max;
            return d;
        }

        // ==================================================================================
        // DU LIEU
        // ==================================================================================
        private void LoadValues()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            _loading = true;
            try
            {
                foreach (SheetSpec s in _catalog)
                {
                    _catalogGrid.Rows.Add(s.Name, s.WidthMm.ToString("0.###", ci), s.LengthMm.ToString("0.###", ci), string.Join(",", s.Materials.ToArray()));
                }

                foreach (KeyValuePair<string, int> m in _materials)
                {
                    string type = MaterialName.TypeOf(m.Key), thickness = MaterialName.ThicknessOf(m.Key);
                    int parts;
                    string partText = _partCounts != null && _partCounts.TryGetValue(m.Key, out parts) ? parts.ToString(ci) : "-";
                    double[] size;
                    string biggest = _biggest.TryGetValue(m.Key, out size) && size.Length >= 2
                        ? string.Format(ci, "{0:0} x {1:0}", size[0], size[1]) : "-";

                    int row = _materialGrid.Rows.Add(
                        type.Length > 0 ? type : "(khong ghi loai)",
                        thickness.Length > 0 ? thickness : m.Key,
                        partText, m.Value.ToString(ci), biggest, null, string.Empty);
                    _materialGrid.Rows[row].Tag = m.Key;
                    if (type.Length == 0) _materialGrid.Rows[row].Cells[ColType].Style.ForeColor = Color.DimGray;
                }

                _numGap.Value = Clamp(_settings.GapMm, _numGap);
                _numMargin.Value = Clamp(_settings.EdgeMarginMm, _numMargin);
                _numBudget.Value = Clamp(_settings.TimeBudgetSeconds, _numBudget);
                _numSeed.Value = Clamp(_settings.Seed, _numSeed);
                _numExtra.Value = Clamp(_settings.ExtraSeededOrderings, _numExtra);
                _numSpacing.Value = Clamp(_settings.SheetSpacingMm, _numSpacing);
                _cboRotation.SelectedIndex = (int)_settings.RotationMode;
                _chkMirror.Checked = _settings.AllowMirror;
                _chkInsideHole.Checked = _settings.AllowPartInsideHole;
                _chkHere.Checked = _settings.OutputToCurrentDrawing;
                _chkBlocks.Checked = !_settings.OutputAsBlocks;
                _chkLabels.Checked = _settings.LabelParts;
                _chkLabelNameOrder.Checked = _settings.LabelPartNameAndOrder;
                _chkLabelNameOrder.Enabled = _settings.LabelParts;
                _chkDeterministic.Checked = _settings.DeterministicSearch;
                _cboSearch.SelectedIndex = _settings.SearchEffort == SearchEffort.Balanced ? 1 : 0;
                _cboAlgorithm.SelectedIndex = Math.Max(0, Math.Min(2, (int)_settings.Algorithm));
                _chkOpen.Checked = _settings.OpenOutputDrawing;
                _chkFixture.Checked = _settings.SaveFixture;
            }
            finally
            {
                _loading = false;
            }

            // Phai goi SAU khi da nap le mep: phep thu "kho nay co chua noi chi tiet lon nhat
            // khong" tru le mep hai phia, ma luc nay _numMargin moi co gia tri that. Goi truoc
            // thi no do bang le mep = 0 va co the ket luan vua trong khi thuc te khong vua.
            RefreshSheetChoices();
            _autoPickDone = true;

            UpdateRuleNote();
        }

        private void UpdateRuleNote()
        {
            double gap = (double)_numGap.Value, margin = (double)_numMargin.Value;
            _ruleNote.Text = string.Format(CultureInfo.InvariantCulture,
                "Khoang cach THUC TE tren hinh chi tiet: chi tiet <-> chi tiet >= {0:0.##} mm, chi tiet <-> mep to >= {1:0.##} mm.\n" +
                "Hai thong so doc lap. Chi tiet co cung tron: cong them dung sai day cung {2:0.##} mm.",
                gap, margin, _settings.ArcToleranceMm);
        }

        private static string MaterialOf(DataGridViewRow row)
        {
            return row.Tag as string ?? string.Empty;
        }

        private List<SheetSpec> ReadCatalogGrid(out string error)
        {
            error = null;
            List<SheetSpec> list = new List<SheetSpec>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _catalogGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string name = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
                string ws = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture);
                string ls = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture);
                string mats = Convert.ToString(row.Cells[3].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(ws) && string.IsNullOrWhiteSpace(ls)) continue;

                double w, l;
                if (string.IsNullOrWhiteSpace(name) ||
                    !double.TryParse((ws ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out w) ||
                    !double.TryParse((ls ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out l) ||
                    !(w > 0) || !(l > 0) || double.IsInfinity(w) || double.IsInfinity(l) ||
                    w > SimpleNestingEngine.MaxSheetMm || l > SimpleNestingEngine.MaxSheetMm)
                {
                    error = "Dong kho phoi '" + name + "' khong hop le (can ten, rong > 0, dai > 0, toi da 1000000 mm).";
                    continue;
                }

                if (!names.Add(name.Trim()))
                {
                    error = "Ten kho phoi bi trung: " + name;
                    continue;
                }

                SheetSpec s = new SheetSpec(name.Trim(), Math.Max(w, l), Math.Min(w, l));
                foreach (string m in mats.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (m.Trim().Length > 0) s.Materials.Add(SimpleNestingEngine.NormalizeMaterial(m));
                }

                list.Add(s);
            }

            return list;
        }

        /// <summary>
        /// Kho da chon lan truoc cho vat lieu nay. Ban luu cu chi ghi DO DAY ("1.2MM") - van
        /// dung duoc cho "THEP 1.2MM" khi chua tung chon rieng cho vat lieu co loai.
        /// </summary>
        private string SavedSheetFor(string material)
        {
            string saved;
            if (_settings.MaterialSheets.TryGetValue(material, out saved)) return saved;
            string thickness = MaterialName.ThicknessOf(material);
            if (thickness.Length > 0 && _settings.MaterialSheets.TryGetValue(thickness, out saved)) return saved;
            return null;
        }

        private void RefreshSheetChoices()
        {
            string error;
            List<SheetSpec> sheets = ReadCatalogGrid(out error);
            List<string> offered = new List<string>();

            _loading = true;
            try
            {
                foreach (DataGridViewRow row in _materialGrid.Rows)
                {
                    string material = MaterialOf(row);
                    DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)row.Cells[ColSheet];
                    string current = Convert.ToString(cell.Value, CultureInfo.InvariantCulture);
                    cell.Items.Clear();
                    foreach (SheetSpec s in sheets)
                    {
                        if (s.IsCompatibleWith(material)) cell.Items.Add(s.Name);
                    }

                    string preferred = current;
                    if (string.IsNullOrEmpty(preferred)) preferred = SavedSheetFor(material);
                    if (string.IsNullOrEmpty(preferred)) preferred = _settings.DefaultSheetName;

                    if (!string.IsNullOrEmpty(preferred) && cell.Items.Contains(preferred)) cell.Value = preferred;
                    else cell.Value = cell.Items.Count > 0 ? cell.Items[0] : null;

                    offered.Add(material);

                    // Kho da chon co chua noi chi tiet lon nhat khong? Neu khong thi tu doi sang
                    // kho NHO NHAT ma chua duoc - de nguoi dung khong phai doan vi sao con chi tiet
                    // "chua xep". Khong kho nao chua noi thi giu nguyen va noi ro o cot Kiem tra.
                    // Chi tu chon MOT LAN luc mo bang. Sau do nguoi dung lam chu: neu ho co y chon
                    // kho nho (chap nhan vai chi tiet khong vua) thi khong duoc tu doi lai moi lan
                    // ho sua danh muc.
                    string fitting = _autoPickDone ? null : SmallestSheetThatFits(sheets, material);
                    if (fitting != null &&
                        !string.Equals(Convert.ToString(cell.Value, CultureInfo.InvariantCulture), fitting, StringComparison.Ordinal) &&
                        !Fits(FindSheet(sheets, Convert.ToString(cell.Value, CultureInfo.InvariantCulture)), material))
                    {
                        cell.Value = fitting;
                    }
                }
            }
            finally
            {
                _loading = false;
            }

            ShowCatalogProblems(sheets, error, offered);
            UpdateMaterialChecks(sheets);
        }

        private void UpdateMaterialChecks()
        {
            if (_materialGrid == null || _catalogGrid == null || _loading) return;
            string error;
            UpdateMaterialChecks(ReadCatalogGrid(out error));
        }

        /// <summary>Cot "Kiem tra" + dong tong ket: vat lieu nao chua co kho, kho nao nho hon chi tiet.</summary>
        private void UpdateMaterialChecks(List<SheetSpec> sheets)
        {
            int missing = 0, tooSmall = 0, qty = 0;
            foreach (DataGridViewRow row in _materialGrid.Rows)
            {
                string material = MaterialOf(row);
                DataGridViewCell check = row.Cells[ColCheck];
                SheetSpec sheet = FindSheet(sheets, Convert.ToString(row.Cells[ColSheet].Value, CultureInfo.InvariantCulture));
                int n;
                if (_materials.TryGetValue(material, out n)) qty += n;

                if (((DataGridViewComboBoxCell)row.Cells[ColSheet]).Items.Count == 0)
                {
                    missing++;
                    check.Value = "X  Khong co kho phu hop";
                    check.Style.ForeColor = Bad;
                }
                else if (sheet == null)
                {
                    missing++;
                    check.Value = "X  Chua chon kho";
                    check.Style.ForeColor = Bad;
                }
                else if (!Fits(sheet, material))
                {
                    tooSmall++;
                    check.Value = "!  Chi tiet lon hon kho";
                    check.Style.ForeColor = Bad;
                }
                else
                {
                    check.Value = "OK - vua kho";
                    check.Style.ForeColor = Good;
                }
            }

            string text = string.Format(CultureInfo.InvariantCulture, "{0} vat lieu, tong SL {1}.", _materialGrid.Rows.Count, qty);
            if (missing > 0) text += string.Format(CultureInfo.InvariantCulture, "  {0} vat lieu CHUA CO KHO - chon kho hoac them vao danh muc.", missing);
            if (tooSmall > 0) text += string.Format(CultureInfo.InvariantCulture, "  {0} vat lieu co chi tiet LON HON kho dang chon (se bi 'chua xep').", tooSmall);
            _materialSummary.Text = text;
            _materialSummary.ForeColor = missing > 0 || tooSmall > 0 ? Bad : Good;
        }

        /// <summary>
        /// Noi ro vi sao mot dong vua go KHONG hien ra o o chon kho.
        ///
        /// Truoc day ba truong hop nay deu bi loai LANG LE: dong thieu so / so khong hop le,
        /// ten kho bi trung, va kho co cot "Vat lieu tuong thich" ghi mot chuoi khong khop vat
        /// lieu nao. Nguoi dung go xong, kho khong hien ra, va khong co mot chu nao giai thich.
        /// </summary>
        private void ShowCatalogProblems(List<SheetSpec> sheets, string error, List<string> offered)
        {
            if (_catalogNote == null) return;

            List<string> problems = new List<string>();
            if (!string.IsNullOrEmpty(error)) problems.Add(error);

            foreach (SheetSpec s in sheets)
            {
                if (s.Materials.Count == 0) continue;      // de trong = dung cho tat ca

                bool usable = false;
                foreach (string m in offered)
                {
                    if (s.IsCompatibleWith(m)) usable = true;
                }

                if (!usable)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture,
                        "Kho '{0}' khong hien ra vi cot 'Vat lieu tuong thich' ghi '{1}' - khong khop vat lieu nao dang co ({2}). De TRONG = dung cho moi vat lieu.",
                        s.Name, string.Join(",", s.Materials.ToArray()), string.Join(", ", offered.ToArray())));
                }
            }

            _catalogNote.Text = problems.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, problems.ToArray());
        }

        /// <summary>Kho nho nhat trong danh muc chua duoc chi tiet lon nhat cua vat lieu nay.</summary>
        private string SmallestSheetThatFits(List<SheetSpec> sheets, string material)
        {
            SheetSpec best = null;
            foreach (SheetSpec s in sheets)
            {
                if (!s.IsCompatibleWith(material)) continue;
                if (!Fits(s, material)) continue;
                if (best == null || s.LengthMm * s.WidthMm < best.LengthMm * best.WidthMm) best = s;
            }

            return best != null ? best.Name : null;
        }

        /// <summary>
        /// Chi tiet lon nhat cua vat lieu nay co dat vua kho <paramref name="sheet"/> khong.
        /// Tru le mep hai phia, va cho phep xoay (canh dai cua chi tiet ung voi canh dai cua to).
        /// </summary>
        private bool Fits(SheetSpec sheet, string material)
        {
            if (sheet == null) return false;

            double[] size;
            if (!_biggest.TryGetValue(material, out size) || size.Length < 2) return true;

            double margin = (double)_numMargin.Value;
            double usableLong = Math.Max(sheet.LengthMm, sheet.WidthMm) - 2.0 * margin;
            double usableShort = Math.Min(sheet.LengthMm, sheet.WidthMm) - 2.0 * margin;

            return size[0] <= usableLong && size[1] <= usableShort;
        }

        private static SheetSpec FindSheet(List<SheetSpec> sheets, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (SheetSpec s in sheets)
            {
                if (string.Equals(s.Name, name, StringComparison.Ordinal)) return s;
            }

            return null;
        }

        private bool Commit()
        {
            _catalogGrid.EndEdit();
            _materialGrid.EndEdit();

            string error;
            List<SheetSpec> sheets = ReadCatalogGrid(out error);
            if (error != null)
            {
                _tabs.SelectedTab = _tabCatalog;
                MessageBox.Show(this, error, "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Dictionary<string, SheetSpec> byName = new Dictionary<string, SheetSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (SheetSpec s in sheets) byName[s.Name] = s;

            Dictionary<string, SheetSpec> chosen = new Dictionary<string, SheetSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _materialGrid.Rows)
            {
                string material = MaterialOf(row);
                string sheetName = Convert.ToString(row.Cells[ColSheet].Value, CultureInfo.InvariantCulture);
                SheetSpec sheet;
                if (string.IsNullOrEmpty(sheetName) || !byName.TryGetValue(sheetName, out sheet))
                {
                    _tabs.SelectedIndex = 0;
                    _materialGrid.CurrentCell = row.Cells[ColSheet];
                    MessageBox.Show(this, "Chua chon kho phoi (trong danh muc) cho vat lieu " + material + ".",
                        "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                chosen[material] = sheet;
            }

            if (_chkMirror.Checked && !_settings.AllowMirror)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Lat guong se doi chieu trai/phai cua chi tiet. Chi tiet se chan/uon sau nay co the bi SAI chieu.\n\nVan cho phep lat guong?",
                    "GHOPHOI - LAT GUONG", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return false;
            }

            _settings.GapMm = (double)_numGap.Value;
            _settings.EdgeMarginMm = (double)_numMargin.Value;
            _settings.TimeBudgetSeconds = (double)_numBudget.Value;
            _settings.Seed = (int)_numSeed.Value;
            _settings.ExtraSeededOrderings = (int)_numExtra.Value;
            _settings.SheetSpacingMm = (double)_numSpacing.Value;
            _settings.RotationMode = (GhoPhoiRotationMode)Math.Max(0, _cboRotation.SelectedIndex);
            _settings.AllowMirror = _chkMirror.Checked;
            _settings.AllowPartInsideHole = _chkInsideHole.Checked;
            _settings.OutputToCurrentDrawing = _chkHere.Checked;
            _settings.OutputAsBlocks = !_chkBlocks.Checked;
            _settings.LabelParts = _chkLabels.Checked;
            _settings.LabelPartNameAndOrder = _chkLabelNameOrder.Checked;
            _settings.DeterministicSearch = _chkDeterministic.Checked;
            _settings.SearchEffort = _cboSearch.SelectedIndex == 1 ? SearchEffort.Balanced : SearchEffort.Fast;
            _settings.Algorithm = (GhoPhoiAlgorithmMode)Math.Max(0, _cboAlgorithm.SelectedIndex);
            _settings.OpenOutputDrawing = _chkOpen.Checked;
            _settings.SaveFixture = _chkFixture.Checked;
            foreach (KeyValuePair<string, SheetSpec> kv in chosen)
            {
                _settings.MaterialSheets[kv.Key] = kv.Value.Name;
                _settings.DefaultSheetName = kv.Value.Name;
            }

            _catalog.Clear();
            _catalog.AddRange(sheets);
            SheetByMaterial = chosen;
            return true;
        }
    }
}
