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
    /// </summary>
    internal sealed class NestingSettingsForm : Form
    {
        private readonly GhoPhoiSettings _settings;
        private readonly List<SheetSpec> _catalog;
        private readonly SortedDictionary<string, int> _materials;
        private readonly Dictionary<string, double[]> _biggest;
        private bool _autoPickDone;

        private DataGridView _materialGrid;
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
        private CheckBox _chkDeterministic;
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
        {
            _settings = settings.Clone();
            _catalog = new List<SheetSpec>(catalog);
            _materials = materials;
            _biggest = biggest ?? new Dictionary<string, double[]>(StringComparer.Ordinal);
            BuildUi();
            LoadValues();
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

        public GhoPhoiSettings Settings { get { return _settings; } }

        public List<SheetSpec> Catalog { get { return _catalog; } }

        public bool CatalogChanged { get; private set; }

        /// <summary>Chosen sheet per material (valid after OK).</summary>
        public Dictionary<string, SheetSpec> SheetByMaterial { get; private set; }

        private void BuildUi()
        {
            Text = "GHOPHOI - CAI DAT GHEP PHOI";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(820, 640);
            MinimumSize = new Size(700, 600);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 175));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            // ---- material -> sheet ----
            GroupBox gMat = new GroupBox { Text = "Kho phoi cho tung vat lieu (moi vat lieu ghep rieng, KHONG tron)", Dock = DockStyle.Fill };
            _materialGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _materialGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vat lieu", ReadOnly = true, FillWeight = 30 });
            _materialGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tong SL", ReadOnly = true, FillWeight = 20 });
            _materialGrid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Kho phoi", FillWeight = 50, FlatStyle = FlatStyle.Flat });
            _materialGrid.DataError += (s, e) => { e.ThrowException = false; };
            gMat.Controls.Add(_materialGrid);

            // ---- catalog ----
            GroupBox gCat = new GroupBox { Text = "Danh muc kho phoi duoc phep (Rong = chieu Y, Dai = chieu X; vat lieu bo trong = tat ca)", Dock = DockStyle.Fill };
            _catalogGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ten", FillWeight = 30 });
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rong (mm)", FillWeight = 20 });
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dai (mm)", FillWeight = 20 });
            _catalogGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vat lieu tuong thich", FillWeight = 30 });
            _catalogGrid.CellEndEdit += (s, e) => { CatalogChanged = true; RefreshSheetChoices(); };
            _catalogGrid.UserDeletedRow += (s, e) => { CatalogChanged = true; RefreshSheetChoices(); };
            _catalogNote = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                ForeColor = Color.FromArgb(170, 0, 0)
            };

            gCat.Controls.Add(_catalogGrid);
            gCat.Controls.Add(_catalogNote);

            // ---- nesting parameters ----
            GroupBox gPar = new GroupBox { Text = "Thong so ghep", Dock = DockStyle.Fill };
            TableLayoutPanel par = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6 };
            for (int i = 0; i < 4; i++) par.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            _numGap = Num(0, 100, 2);
            _numMargin = Num(0, 200, 2);
            _numBudget = Num(1, 600, 0);
            _numSeed = Num(0, 1000000, 0);
            _numExtra = Num(0, 50, 0);
            _cboRotation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboRotation.Items.AddRange(new object[] { "0 / 90 / 180 / 270 do", "0 / 180 do (giu chieu van)", "Khong xoay" });
            _chkMirror = new CheckBox { Text = "Cho phep LAT GUONG (canh bao: doi chieu chi tiet chan!)", AutoSize = true, ForeColor = Color.DarkRed };

            AddRow(par, 0, "Khe cat giua chi tiet (mm):", _numGap, "Le mep to (mm):", _numMargin);
            AddRow(par, 1, "Huong xoay:", _cboRotation, "Thoi gian toi da (s):", _numBudget);
            AddRow(par, 2, "Seed:", _numSeed, "So thu tu ngau nhien them:", _numExtra);
            par.Controls.Add(_chkMirror, 0, 3);
            par.SetColumnSpan(_chkMirror, 4);
            _chkInsideHole = new CheckBox { Text = "Allow part inside closed holes (cho phep dat chi tiet vao LO KIN cua chi tiet khac)", AutoSize = true };
            par.Controls.Add(_chkInsideHole, 0, 4);
            par.SetColumnSpan(_chkInsideHole, 4);

            // Bat: may nhanh hay cham deu ra CUNG mot ket qua, doi lai co the chay qua han muc
            // gio (muon dung thi bam nut dung). Tat: quay ve hanh vi cu, co tran gio nhung may
            // cham co the ra bo cuc te hon - da do duoc chenh 2% tren ban ve that.
            _chkDeterministic = new CheckBox
            {
                Text = "Tim du so luot xep (ket qua khong phu thuoc toc do may - co the chay qua thoi gian toi da)",
                AutoSize = true
            };
            par.Controls.Add(_chkDeterministic, 0, 5);
            par.SetColumnSpan(_chkDeterministic, 4);

            // Giai thich ro: khi BAT thi "Thoi gian toi da" KHONG con la gioi han cung.
            ToolTip help = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
            Disposed += (s, e) => help.Dispose();
            help.SetToolTip(_chkDeterministic, DeterministicHelpText);
            help.SetToolTip(_numBudget, DeterministicHelpText);

            _ruleNote = new Label { Dock = DockStyle.Bottom, Height = 34, ForeColor = Color.FromArgb(0, 102, 204) };
            _numGap.ValueChanged += (s, e) => UpdateRuleNote();
            _numMargin.ValueChanged += (s, e) => UpdateRuleNote();
            gPar.Controls.Add(par);
            gPar.Controls.Add(_ruleNote);

            // ---- output ----
            GroupBox gOut = new GroupBox { Text = "Xuat ket qua", Dock = DockStyle.Fill };
            FlowLayoutPanel outp = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            _chkHere = new CheckBox { Text = "Ve thang vao ban ve nay (chon diem dat)", AutoSize = true };
            _chkBlocks = new CheckBox { Text = "Moi chi tiet la 1 BLOCK", AutoSize = true };
            _chkLabels = new CheckBox { Text = "Hien thi ma P + STT tren phoi", AutoSize = true };
            _chkOpen = new CheckBox { Text = "Mo ban ve sau khi tao", AutoSize = true };
            _chkFixture = new CheckBox { Text = "Luu fixture test (.nest)", AutoSize = true };
            _numSpacing = Num(0, 10000, 0);
            _numSpacing.Width = 80;
            outp.Controls.Add(_chkHere);
            outp.Controls.Add(_chkBlocks);
            outp.Controls.Add(_chkLabels);
            outp.Controls.Add(_chkOpen);
            outp.Controls.Add(_chkFixture);
            outp.Controls.Add(new Label { Text = "Khoang cach giua cac to (mm):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            outp.Controls.Add(_numSpacing);
            gOut.Controls.Add(outp);

            // ---- buttons ----
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button ok = new Button { Text = "GHEP PHOI", Width = 140, Height = 30 };
            ok.Click += (s, e) => { if (Commit()) { DialogResult = DialogResult.OK; Close(); } };
            Button cancel = new Button { Text = "Huy", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;

            root.Controls.Add(gMat, 0, 0);
            root.Controls.Add(gCat, 0, 1);
            root.Controls.Add(gPar, 0, 2);
            root.Controls.Add(gOut, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);
        }

        private static NumericUpDown Num(decimal min, decimal max, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = decimals > 0 ? 0.5M : 1M,
                Dock = DockStyle.Fill
            };
        }

        private static void AddRow(TableLayoutPanel p, int row, string l1, Control c1, string l2, Control c2)
        {
            p.Controls.Add(new Label { Text = l1, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            p.Controls.Add(c1, 1, row);
            p.Controls.Add(new Label { Text = l2, AutoSize = true, Anchor = AnchorStyles.Left }, 2, row);
            p.Controls.Add(c2, 3, row);
        }

        private static decimal Clamp(double v, NumericUpDown n)
        {
            decimal d = (decimal)v;
            if (d < n.Minimum) return n.Minimum;
            if (d > n.Maximum) return n.Maximum;
            return d;
        }

        private void LoadValues()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            foreach (SheetSpec s in _catalog)
            {
                _catalogGrid.Rows.Add(s.Name, s.WidthMm.ToString("0.###", ci), s.LengthMm.ToString("0.###", ci), string.Join(",", s.Materials.ToArray()));
            }

            foreach (KeyValuePair<string, int> m in _materials)
            {
                _materialGrid.Rows.Add(m.Key, m.Value.ToString(ci), null);
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
            _chkBlocks.Checked = _settings.OutputAsBlocks;
            _chkLabels.Checked = _settings.LabelParts;
            _chkDeterministic.Checked = _settings.DeterministicSearch;
            _chkOpen.Checked = _settings.OpenOutputDrawing;
            _chkFixture.Checked = _settings.SaveFixture;

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
                "Khoang cach THUC TE tren hinh chi tiet: chi tiet <-> chi tiet >= {0:0.##} mm (Gap), " +
                "chi tiet <-> mep to >= {1:0.##} mm (EdgeMargin).\n" +
                "Hai thong so doc lap. Chi tiet co cung tron: cong them dung sai day cung {2:0.##} mm.",
                gap, margin, _settings.ArcToleranceMm);
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
                    w <= 0 || l <= 0)
                {
                    error = "Dong kho phoi '" + name + "' khong hop le (can ten, rong > 0, dai > 0).";
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

        private void RefreshSheetChoices()
        {
            string error;
            List<SheetSpec> sheets = ReadCatalogGrid(out error);
            List<string> offered = new List<string>();

            foreach (DataGridViewRow row in _materialGrid.Rows)
            {
                string material = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
                DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)row.Cells[2];
                string current = Convert.ToString(cell.Value, CultureInfo.InvariantCulture);
                cell.Items.Clear();
                foreach (SheetSpec s in sheets)
                {
                    if (s.IsCompatibleWith(material)) cell.Items.Add(s.Name);
                }

                string preferred = current;
                string saved;
                if (string.IsNullOrEmpty(preferred) && _settings.MaterialSheets.TryGetValue(material, out saved)) preferred = saved;
                if (string.IsNullOrEmpty(preferred)) preferred = _settings.DefaultSheetName;

                if (!string.IsNullOrEmpty(preferred) && cell.Items.Contains(preferred)) cell.Value = preferred;
                else cell.Value = cell.Items.Count > 0 ? cell.Items[0] : null;

                offered.Add(material);

                // Kho da chon co chua noi chi tiet lon nhat khong? Neu khong thi tu doi sang
                // kho NHO NHAT ma chua duoc - de nguoi dung khong phai doan vi sao con chi tiet
                // "chua xep". Khong kho nao chua noi thi giu nguyen va noi ro o dong ghi chu.
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

            ShowCatalogProblems(sheets, error, offered);
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
                MessageBox.Show(this, error, "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Dictionary<string, SheetSpec> byName = new Dictionary<string, SheetSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (SheetSpec s in sheets) byName[s.Name] = s;

            Dictionary<string, SheetSpec> chosen = new Dictionary<string, SheetSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _materialGrid.Rows)
            {
                string material = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture);
                string sheetName = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture);
                SheetSpec sheet;
                if (string.IsNullOrEmpty(sheetName) || !byName.TryGetValue(sheetName, out sheet))
                {
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
            _settings.OutputAsBlocks = _chkBlocks.Checked;
            _settings.LabelParts = _chkLabels.Checked;
            _settings.DeterministicSearch = _chkDeterministic.Checked;
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
