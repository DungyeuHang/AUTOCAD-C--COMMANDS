using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Bang nhap lieu cua DX_FOIL. Hien TRUOC khi chon bien dang, dung nhu quy trinh yeu cau.
    /// Giao dien viet tay theo dung phong cach cac form khac cua project (Segoe UI 9F,
    /// TableLayoutPanel, nhan tieng Viet).
    ///
    /// Form nay KHONG tham chieu AutoCAD: danh sach layer duoc tang lenh truyen vao
    /// (FoilDrawingBuilder.GetLayerNames). Nho vay giao dien kiem tra duoc ngoai AutoCAD.
    /// </summary>
    public class FoilInputForm : Form
    {
        private readonly FoilSettings _settings;
        private readonly List<string> _drawingLayers = new List<string>();

        private NumericUpDown _numBlankLength;
        private NumericUpDown _numThickness;
        private NumericUpDown _numRadius;
        private CheckBox _chkCompensateThickness;
        private CheckBox _chkInvertOffsetSide;
        private Label _lblDimensionPreview;

        private RadioButton _rbCustom;
        private RadioButton _rbBendDeduction;
        private RadioButton _rbBendAllowance;
        private RadioButton _rbBendTable;

        private Panel _pnlCustom;
        private Panel _pnlKFactor;
        private Panel _pnlBendTable;

        private NumericUpDown _numCustomFactor;
        private CheckBox _chkScaleByAngle;
        private Label _lblCustomPreview;

        private NumericUpDown _numKFactor;
        private Label _lblKPreview;

        private TextBox _txtBendTablePath;
        private Button _btnBrowseTable;
        private Button _btnCreateSampleTable;

        private ComboBox _cboBendLayer;
        private ComboBox _cboOutlineLayer;
        private ComboBox _cboUpColorMode;
        private NumericUpDown _numUpColor;
        private ComboBox _cboDownColorMode;
        private NumericUpDown _numDownColor;
        private ComboBox _cboBendLineMode;

        private CheckBox _chkDrawSteps;
        private ComboBox _cboSequenceOrder;
        private ComboBox _cboStepLayer;

        private ComboBox _cboArcInterpretation;
        private NumericUpDown _numBlankRotation;
        private NumericUpDown _numPrecision;
        private NumericUpDown _numMinBendAngle;
        private NumericUpDown _numMaxBendAngle;
        private CheckBox _chkInvertDirection;
        private CheckBox _chkAllowThicknessOutline;
        private CheckBox _chkZoom;

        public FoilInputForm(FoilSettings settings, IEnumerable<string> drawingLayers)
        {
            _settings = settings != null ? settings.Clone() : new FoilSettings();

            if (drawingLayers != null)
            {
                _drawingLayers.AddRange(drawingLayers);
                _drawingLayers.Sort(StringComparer.OrdinalIgnoreCase);
            }

            InitializeComponent();
            LoadSettingsToControls();
            UpdateMethodPanels();
            UpdatePreviewLabels();
        }

        public FoilSettings GetSettings()
        {
            SaveControlsToSettings();
            return _settings;
        }

        private void InitializeComponent()
        {
            Text = "DX_FOIL - DAN PHOI TON CHAN";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 700);
            Size = new Size(680, 820);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Panel scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };
            Controls.Add(scrollHost);

            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            scrollHost.Controls.Add(content);

            int row = 0;

            Label hint = new Label
            {
                Text = "Nhap thong so phoi, sau do bam CHON BIEN DANG de chi ra polyline mat cat.\n" +
                       "Chuong trinh se tinh chieu rong trien khai va ve phoi kem toan bo duong chan.",
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 102, 204),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            };
            AddRow(content, ref row, hint);

            AddRow(content, ref row, BuildBlankGroup());
            AddRow(content, ref row, BuildMethodGroup());
            AddRow(content, ref row, BuildOutputGroup());
            AddRow(content, ref row, BuildStepGroup());
            AddRow(content, ref row, BuildAdvancedGroup());

            // ---- Nut lenh ----
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };

            Button btnCancel = new Button
            {
                Text = "Huy",
                DialogResult = DialogResult.Cancel,
                Width = 110,
                Height = 34
            };

            Button btnOk = new Button
            {
                Text = "CHON BIEN DANG >>",
                Width = 180,
                Height = 34,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnOk.Click += OnOkClick;

            Button btnCalibrate = new Button
            {
                Text = "Hieu chuan...",
                Width = 130,
                Height = 34
            };
            btnCalibrate.Click += OnCalibrateClick;

            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnCalibrate);
            Controls.Add(buttons);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private GroupBox BuildBlankGroup()
        {
            GroupBox group = CreateGroupBox("1. THONG TIN PHOI");
            TableLayoutPanel panel = CreateGrid(3);

            _numBlankLength = CreateNumeric(0.001m, 1000000m, 2, 1m);
            _numThickness = CreateNumeric(0.001m, 1000m, 3, 0.1m);
            _numRadius = CreateNumeric(0m, 10000m, 3, 0.1m);

            _numThickness.ValueChanged += (s, e) => UpdatePreviewLabels();
            _numRadius.ValueChanged += (s, e) => UpdatePreviewLabels();

            _chkCompensateThickness = new CheckBox
            {
                Text = "Cong be day tai goc LOM  (duong chan that = duong ngoai + so vong tron * T)",
                AutoSize = true
            };
            _chkCompensateThickness.CheckedChanged += (s, e) =>
            {
                _chkInvertOffsetSide.Enabled = _chkCompensateThickness.Checked;
                UpdatePreviewLabels();
            };

            _chkInvertOffsetSide = new CheckBox
            {
                Text = "     Dao nguoc phia offset  -  tick neu tool bu NHAM phia",
                AutoSize = true
            };
            _chkInvertOffsetSide.CheckedChanged += (s, e) => UpdatePreviewLabels();

            _lblDimensionPreview = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 120, 0),
                Font = new Font("Consolas", 8.5F, FontStyle.Regular)
            };

            AddField(panel, 0, "Chieu dai phoi:", _numBlankLength, "mm  (giu nguyen, khong tinh lai)");
            AddField(panel, 1, "Chieu day ton T:", _numThickness, "mm");
            AddField(panel, 2, "Ban kinh chan trong R:", _numRadius, "mm  (mat trong cua goc chan)");

            panel.Controls.Add(_chkCompensateThickness, 0, 3);
            panel.SetColumnSpan(_chkCompensateThickness, 3);
            panel.Controls.Add(_chkInvertOffsetSide, 0, 4);
            panel.SetColumnSpan(_chkInvertOffsetSide, 3);
            panel.Controls.Add(_lblDimensionPreview, 0, 5);
            panel.SetColumnSpan(_lblDimensionPreview, 3);

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox BuildMethodGroup()
        {
            GroupBox group = CreateGroupBox("2. PHUONG PHAP TINH DAN");

            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _rbCustom = new RadioButton { Text = "Custom / Shop Rule  (quy tac xuong dang dung)", AutoSize = true, Checked = true };
            _rbBendDeduction = new RadioButton { Text = "Bend Deduction + K-Factor  (cong thuc tieu chuan)", AutoSize = true };
            _rbBendAllowance = new RadioButton { Text = "Bend Allowance + K-Factor  (cong thuc tieu chuan)", AutoSize = true };
            _rbBendTable = new RadioButton { Text = "Custom Bend Table  (bang chan do thuc te cua xuong)", AutoSize = true };

            _rbCustom.CheckedChanged += OnMethodChanged;
            _rbBendDeduction.CheckedChanged += OnMethodChanged;
            _rbBendAllowance.CheckedChanged += OnMethodChanged;
            _rbBendTable.CheckedChanged += OnMethodChanged;

            panel.Controls.Add(_rbCustom);
            panel.Controls.Add(_rbBendDeduction);
            panel.Controls.Add(_rbBendAllowance);
            panel.Controls.Add(_rbBendTable);

            // ----- Tham so quy tac xuong -----
            _pnlCustom = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 6, 0, 6) };
            TableLayoutPanel customGrid = CreateGrid(3);

            _numCustomFactor = CreateNumeric(0m, 100m, 4, 0.1m);
            _numCustomFactor.ValueChanged += (s, e) => UpdatePreviewLabels();

            _chkScaleByAngle = new CheckBox
            {
                Text = "Ty le theo goc chan  (nhan them tan(alpha/2) - TAT de giu dung quy tac hien tai)",
                AutoSize = true
            };

            _lblCustomPreview = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 120, 0),
                Font = new Font("Consolas", 8.5F, FontStyle.Regular)
            };

            AddField(customGrid, 0, "He so bu (Factor):", _numCustomFactor, "x T tai moi dau canh co chan");
            customGrid.Controls.Add(_chkScaleByAngle, 0, 1);
            customGrid.SetColumnSpan(_chkScaleByAngle, 3);
            customGrid.Controls.Add(_lblCustomPreview, 0, 2);
            customGrid.SetColumnSpan(_lblCustomPreview, 3);

            _pnlCustom.Controls.Add(customGrid);
            panel.Controls.Add(_pnlCustom);

            // ----- Tham so K-factor -----
            _pnlKFactor = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 6, 0, 6) };
            TableLayoutPanel kGrid = CreateGrid(3);

            _numKFactor = CreateNumeric(0m, 1m, 4, 0.01m);
            _numKFactor.ValueChanged += (s, e) => UpdatePreviewLabels();

            _lblKPreview = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 120, 0),
                Font = new Font("Consolas", 8.5F, FontStyle.Regular)
            };

            AddField(kGrid, 0, "K-Factor:", _numKFactor, "vi tri truc trung hoa (thuong 0.30 - 0.50)");
            kGrid.Controls.Add(_lblKPreview, 0, 1);
            kGrid.SetColumnSpan(_lblKPreview, 3);

            _pnlKFactor.Controls.Add(kGrid);
            panel.Controls.Add(_pnlKFactor);

            // ----- Tham so bang chan -----
            _pnlBendTable = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 6, 0, 6) };
            TableLayoutPanel tableGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true
            };
            tableGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            tableGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
            tableGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));

            _txtBendTablePath = new TextBox { Dock = DockStyle.Fill };
            _btnBrowseTable = new Button { Text = "Chon file...", Dock = DockStyle.Fill };
            _btnBrowseTable.Click += OnBrowseTableClick;
            _btnCreateSampleTable = new Button { Text = "Tao bang mau", Dock = DockStyle.Fill };
            _btnCreateSampleTable.Click += OnCreateSampleTableClick;

            Label tableHint = new Label
            {
                Text = "File TSV: Thickness <tab> Radius <tab> AngleDeg <tab> Deduction [<tab> Allowance]",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font("Consolas", 8.5F, FontStyle.Regular)
            };

            tableGrid.Controls.Add(_txtBendTablePath, 0, 0);
            tableGrid.Controls.Add(_btnBrowseTable, 1, 0);
            tableGrid.Controls.Add(_btnCreateSampleTable, 2, 0);
            tableGrid.Controls.Add(tableHint, 0, 1);
            tableGrid.SetColumnSpan(tableHint, 3);

            _pnlBendTable.Controls.Add(tableGrid);
            panel.Controls.Add(_pnlBendTable);

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox BuildOutputGroup()
        {
            GroupBox group = CreateGroupBox("3. DAU RA (LAYER / MAU)");
            TableLayoutPanel panel = CreateGrid(3);

            _cboBendLayer = CreateLayerCombo();
            _cboOutlineLayer = CreateLayerCombo();

            _cboUpColorMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboUpColorMode.Items.AddRange(new object[] { "ByLayer", "Ma mau ACI" });
            _cboUpColorMode.SelectedIndexChanged += (s, e) =>
                _numUpColor.Enabled = _cboUpColorMode.SelectedIndex == 1;

            _cboDownColorMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboDownColorMode.Items.AddRange(new object[] { "ByLayer", "Ma mau ACI" });
            _cboDownColorMode.SelectedIndexChanged += (s, e) =>
                _numDownColor.Enabled = _cboDownColorMode.SelectedIndex == 1;

            _numUpColor = CreateNumeric(0m, 256m, 0, 1m);
            _numDownColor = CreateNumeric(0m, 256m, 0, 1m);

            _cboBendLineMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboBendLineMode.Items.AddRange(new object[]
            {
                "1 duong (tam vung chan)",
                "2 duong (tiep tuyen)"
            });

            AddField(panel, 0, "Layer duong chan:", _cboBendLayer, "tu tao neu chua co");
            AddField(panel, 1, "Layer bien phoi:", _cboOutlineLayer, "de trong = layer hien hanh");
            AddField(panel, 2, "Mau chan LEN (UP):", _cboUpColorMode, string.Empty);
            AddField(panel, 3, "   ma mau ACI (UP):", _numUpColor, string.Empty);
            AddField(panel, 4, "Mau chan XUONG (DOWN):", _cboDownColorMode, string.Empty);
            AddField(panel, 5, "   ma mau ACI (DOWN):", _numDownColor, "ACI 8 = xam chuan AutoCAD");
            AddField(panel, 6, "Cach ve duong chan:", _cboBendLineMode, string.Empty);

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox BuildStepGroup()
        {
            GroupBox group = CreateGroupBox("4. HINH CAC BUOC CHAN");
            TableLayoutPanel panel = CreateGrid(3);

            _chkDrawSteps = new CheckBox
            {
                Text = "Ve day hinh trinh tu cac buoc chan ben duoi phoi",
                AutoSize = true
            };
            _chkDrawSteps.CheckedChanged += (s, e) =>
            {
                _cboSequenceOrder.Enabled = _chkDrawSteps.Checked;
                _cboStepLayer.Enabled = _chkDrawSteps.Checked;
            };

            _cboSequenceOrder = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _cboSequenceOrder.Items.AddRange(new object[]
            {
                "Lan luot tu dau (mac dinh)",
                "Nguoc lai, tu cuoi",
                "Hai dau vao giua"
            });

            _cboStepLayer = CreateLayerCombo();

            panel.Controls.Add(_chkDrawSteps, 0, 0);
            panel.SetColumnSpan(_chkDrawSteps, 3);
            AddField(panel, 1, "Thu tu chan:", _cboSequenceOrder, "B0 = phoi phang, roi tung buoc");
            AddField(panel, 2, "Layer hinh cac buoc:", _cboStepLayer, "tu tao neu chua co");

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox BuildAdvancedGroup()
        {
            GroupBox group = CreateGroupBox("5. NANG CAO");
            TableLayoutPanel panel = CreateGrid(3);

            _cboArcInterpretation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboArcInterpretation.Items.AddRange(new object[]
            {
                "Ban kinh chan TRONG (mac dinh)",
                "Ban kinh chan NGOAI",
                "Ban kinh duong tam ton"
            });

            _numBlankRotation = CreateNumeric(-360m, 360m, 4, 15m);
            _numPrecision = CreateNumeric(0m, 6m, 0, 1m);
            _numMinBendAngle = CreateNumeric(0m, 89m, 4, 0.1m);
            _numMaxBendAngle = CreateNumeric(1m, 179.9m, 4, 1m);

            _chkInvertDirection = new CheckBox { Text = "Dao huong chan UP <-> DOWN", AutoSize = true };
            _chkAllowThicknessOutline = new CheckBox
            {
                Text = "Cho phep nhan polyline kin la duong bao CO BE DAY (khong khuyen khich)",
                AutoSize = true
            };
            _chkZoom = new CheckBox { Text = "Zoom va chon phoi sau khi tao", AutoSize = true };

            AddField(panel, 0, "Y nghia ban kinh cung ve:", _cboArcInterpretation, "khi polyline co bo tron goc");
            AddField(panel, 1, "Goc xoay phoi:", _numBlankRotation, "do  (0 = chieu dai nam ngang)");
            AddField(panel, 2, "So chu so thap phan:", _numPrecision, "chi anh huong HIEN THI");
            AddField(panel, 3, "Goc re toi thieu tinh la chan:", _numMinBendAngle, "do");
            AddField(panel, 4, "Goc chan toi da cho phep:", _numMaxBendAngle, "do  (OSSB phan ky khi -> 180)");

            panel.Controls.Add(_chkInvertDirection, 0, 5);
            panel.SetColumnSpan(_chkInvertDirection, 3);
            panel.Controls.Add(_chkAllowThicknessOutline, 0, 6);
            panel.SetColumnSpan(_chkAllowThicknessOutline, 3);
            panel.Controls.Add(_chkZoom, 0, 7);
            panel.SetColumnSpan(_chkZoom, 3);

            group.Controls.Add(panel);
            return group;
        }

        // ==============================================================================
        // NAP / LUU GIA TRI
        // ==============================================================================

        private void LoadSettingsToControls()
        {
            _numBlankLength.Value = ToDecimal(_settings.BlankLength, _numBlankLength);
            _numThickness.Value = ToDecimal(_settings.Thickness, _numThickness);
            _numRadius.Value = ToDecimal(_settings.InsideRadius, _numRadius);
            _chkCompensateThickness.Checked =
                _settings.ThicknessCompensation != FoilThicknessCompensationMode.None;
            _chkInvertOffsetSide.Checked =
                _settings.ThicknessCompensation == FoilThicknessCompensationMode.TurnRight;
            _chkInvertOffsetSide.Enabled = _chkCompensateThickness.Checked;

            switch (_settings.Method)
            {
                case FoilBendMethod.BendDeductionKFactor: _rbBendDeduction.Checked = true; break;
                case FoilBendMethod.BendAllowanceKFactor: _rbBendAllowance.Checked = true; break;
                case FoilBendMethod.BendTable: _rbBendTable.Checked = true; break;
                default: _rbCustom.Checked = true; break;
            }

            _numCustomFactor.Value = ToDecimal(_settings.CustomFactor, _numCustomFactor);
            _chkScaleByAngle.Checked = _settings.CustomRuleScaleByAngle;
            _numKFactor.Value = ToDecimal(_settings.KFactor, _numKFactor);
            _txtBendTablePath.Text = _settings.BendTablePath ?? string.Empty;

            _cboBendLayer.Text = _settings.BendLayerName;
            _cboOutlineLayer.Text = _settings.OutlineLayerName ?? string.Empty;

            _cboUpColorMode.SelectedIndex = _settings.BendUpColorMode == FoilColorMode.ByLayer ? 0 : 1;
            _cboDownColorMode.SelectedIndex = _settings.BendDownColorMode == FoilColorMode.ByLayer ? 0 : 1;
            _numUpColor.Value = ToDecimal(_settings.BendUpColorIndex, _numUpColor);
            _numDownColor.Value = ToDecimal(_settings.BendDownColorIndex, _numDownColor);
            _numUpColor.Enabled = _cboUpColorMode.SelectedIndex == 1;
            _numDownColor.Enabled = _cboDownColorMode.SelectedIndex == 1;

            _cboBendLineMode.SelectedIndex =
                _settings.BendLineMode == FoilBendLineMode.SingleCenterLine ? 0 : 1;

            _chkDrawSteps.Checked = _settings.DrawBendSteps;
            _cboSequenceOrder.SelectedIndex = (int)_settings.BendSequenceOrder;
            _cboStepLayer.Text = _settings.StepLayerName;
            _cboSequenceOrder.Enabled = _chkDrawSteps.Checked;
            _cboStepLayer.Enabled = _chkDrawSteps.Checked;

            _cboArcInterpretation.SelectedIndex = (int)_settings.ArcRadiusInterpretation;
            _numBlankRotation.Value = ToDecimal(_settings.BlankRotationDeg, _numBlankRotation);
            _numPrecision.Value = ToDecimal(_settings.Precision, _numPrecision);
            _numMinBendAngle.Value = ToDecimal(_settings.MinBendAngleDeg, _numMinBendAngle);
            _numMaxBendAngle.Value = ToDecimal(_settings.MaxBendAngleDeg, _numMaxBendAngle);

            _chkInvertDirection.Checked = _settings.InvertBendDirection;
            _chkAllowThicknessOutline.Checked = _settings.AllowThicknessOutlineInput;
            _chkZoom.Checked = _settings.ZoomToResult;
        }

        private void SaveControlsToSettings()
        {
            _settings.BlankLength = (double)_numBlankLength.Value;
            _settings.Thickness = (double)_numThickness.Value;
            _settings.InsideRadius = (double)_numRadius.Value;
            // Hai o tick anh xa sang che do bu be day:
            //   khong tick          -> None
            //   tick, khong dao     -> BendsUp
            //   tick, co dao        -> BendsDown
            if (!_chkCompensateThickness.Checked)
            {
                _settings.ThicknessCompensation = FoilThicknessCompensationMode.None;
            }
            else
            {
                _settings.ThicknessCompensation = _chkInvertOffsetSide.Checked
                    ? FoilThicknessCompensationMode.TurnRight
                    : FoilThicknessCompensationMode.TurnLeft;
            }

            if (_rbBendDeduction.Checked) _settings.Method = FoilBendMethod.BendDeductionKFactor;
            else if (_rbBendAllowance.Checked) _settings.Method = FoilBendMethod.BendAllowanceKFactor;
            else if (_rbBendTable.Checked) _settings.Method = FoilBendMethod.BendTable;
            else _settings.Method = FoilBendMethod.CustomShopRule;

            _settings.CustomFactor = (double)_numCustomFactor.Value;
            _settings.CustomRuleScaleByAngle = _chkScaleByAngle.Checked;
            _settings.KFactor = (double)_numKFactor.Value;
            _settings.BendTablePath = _txtBendTablePath.Text.Trim();

            _settings.BendLayerName = string.IsNullOrWhiteSpace(_cboBendLayer.Text)
                ? "_mss.dut"
                : _cboBendLayer.Text.Trim();
            _settings.OutlineLayerName = _cboOutlineLayer.Text.Trim();

            _settings.BendUpColorMode = _cboUpColorMode.SelectedIndex == 1
                ? FoilColorMode.AciIndex : FoilColorMode.ByLayer;
            _settings.BendDownColorMode = _cboDownColorMode.SelectedIndex == 1
                ? FoilColorMode.AciIndex : FoilColorMode.ByLayer;
            _settings.BendUpColorIndex = (int)_numUpColor.Value;
            _settings.BendDownColorIndex = (int)_numDownColor.Value;

            _settings.BendLineMode = _cboBendLineMode.SelectedIndex == 1
                ? FoilBendLineMode.TangentPair : FoilBendLineMode.SingleCenterLine;

            _settings.DrawBendSteps = _chkDrawSteps.Checked;
            _settings.BendSequenceOrder =
                (FoilBendSequenceOrder)Math.Max(0, _cboSequenceOrder.SelectedIndex);
            _settings.StepLayerName = string.IsNullOrWhiteSpace(_cboStepLayer.Text)
                ? "_mss.buocchan"
                : _cboStepLayer.Text.Trim();

            _settings.ArcRadiusInterpretation =
                (FoilArcRadiusInterpretation)Math.Max(0, _cboArcInterpretation.SelectedIndex);
            _settings.BlankRotationDeg = (double)_numBlankRotation.Value;
            _settings.Precision = (int)_numPrecision.Value;
            _settings.MinBendAngleDeg = (double)_numMinBendAngle.Value;
            _settings.MaxBendAngleDeg = (double)_numMaxBendAngle.Value;

            _settings.InvertBendDirection = _chkInvertDirection.Checked;
            _settings.AllowThicknessOutlineInput = _chkAllowThicknessOutline.Checked;
            _settings.ZoomToResult = _chkZoom.Checked;
        }

        // ==============================================================================
        // SU KIEN
        // ==============================================================================

        private void OnMethodChanged(object sender, EventArgs e)
        {
            UpdateMethodPanels();
            UpdatePreviewLabels();
        }

        private void UpdateMethodPanels()
        {
            _pnlCustom.Visible = _rbCustom.Checked;
            _pnlKFactor.Visible = _rbBendDeduction.Checked || _rbBendAllowance.Checked;
            _pnlBendTable.Visible = _rbBendTable.Checked;
        }

        /// <summary>
        /// Hien ngay ket qua bu chan cho mot chan 90 do voi tham so dang nhap,
        /// de nguoi dung thay tac dung cua tung o truoc khi chay.
        /// </summary>
        private void UpdatePreviewLabels()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            double t = (double)_numThickness.Value;
            double r = (double)_numRadius.Value;

            // Chan 90 do: tan(45) = 1 nen luong bu tai moi goc lom dung bang T.
            _lblDimensionPreview.Text = !_chkCompensateThickness.Checked
                ? "Duong bao ngoai duoc coi la duong chan that o MOI goc - khong cong gi."
                : string.Format(
                    ci,
                    "Chan 90 do, T = {0:0.###}   ->   canh ve 15 co 1 vong tron = {1:0.###}" +
                    Environment.NewLine +
                    "                                canh ve 34 co 2 vong tron = {2:0.###}" +
                    Environment.NewLine +
                    "Canh chi co goc LOI giu nguyen.",
                    t,
                    15.0 + t,
                    34.0 + 2.0 * t);

            double factor = (double)_numCustomFactor.Value;
            double ossbCustom = factor * t;
            _lblCustomPreview.Text = string.Format(
                ci,
                "Vi du T = {0:0.###}: canh 20 mm co 1 duong chan -> {1:0.####} mm" +
                Environment.NewLine +
                "                      canh 20 mm co 2 duong chan -> {2:0.####} mm",
                t,
                20.0 - ossbCustom,
                20.0 - 2.0 * ossbCustom);

            double k = (double)_numKFactor.Value;
            double alpha = Math.PI / 2.0;
            double ba = alpha * (r + k * t);
            double ossb = (r + t) * Math.Tan(alpha / 2.0);
            double bd = 2.0 * ossb - ba;

            _lblKPreview.Text = string.Format(
                ci,
                "Chan 90 do voi T = {0:0.###}, R = {1:0.###}, K = {2:0.####}:" + Environment.NewLine +
                "  BA = {3:0.#####}   OSSB = {4:0.#####}   BD = {5:0.#####}",
                t, r, k, ba, ossb, bd);
        }

        private void OnBrowseTableClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Chon file bang chan (TSV)";
                dialog.Filter = "File bang chan (*.tsv;*.txt;*.csv)|*.tsv;*.txt;*.csv|Tat ca (*.*)|*.*";
                if (!string.IsNullOrWhiteSpace(_txtBendTablePath.Text))
                {
                    try
                    {
                        dialog.InitialDirectory = System.IO.Path.GetDirectoryName(_txtBendTablePath.Text);
                    }
                    catch
                    {
                    }
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _txtBendTablePath.Text = dialog.FileName;
                }
            }
        }

        private void OnCreateSampleTableClick(object sender, EventArgs e)
        {
            string path = FoilSettingsStore.GetDefaultBendTablePath();

            if (System.IO.File.Exists(path))
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "File bang chan mac dinh da ton tai:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine + "Chi dung lai file nay, khong ghi de?",
                    "DX_FOIL",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    _txtBendTablePath.Text = path;
                    return;
                }
            }

            FoilBendTable table = new FoilBendTable();
            double t = (double)_numThickness.Value;
            double r = (double)_numRadius.Value;
            double k = (double)_numKFactor.Value;

            // Sinh bang mau tu chinh cong thuc K-factor de xuong co diem xuat phat roi sua dan
            // theo so lieu do thuc te.
            double[] angles = { 30, 45, 60, 90, 120, 135 };
            foreach (double angleDeg in angles)
            {
                double alpha = angleDeg * FoilMath.DegToRad;
                double ba = alpha * (r + k * t);
                double ossb = (r + t) * Math.Tan(alpha / 2.0);
                table.Add(new FoilBendTableEntry
                {
                    Thickness = t,
                    Radius = r,
                    AngleDeg = angleDeg,
                    BendDeduction = Math.Round(2.0 * ossb - ba, 4),
                    BendAllowance = Math.Round(ba, 4)
                });
            }

            string error;
            if (table.SaveToFile(path, out error))
            {
                _txtBendTablePath.Text = path;
                MessageBox.Show(
                    this,
                    "Da tao bang chan mau tai:" + Environment.NewLine + path +
                    Environment.NewLine + Environment.NewLine +
                    "Hay sua cac gia tri Deduction theo so do thuc te cua may chan.",
                    "DX_FOIL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, error, "DX_FOIL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnCalibrateClick(object sender, EventArgs e)
        {
            SaveControlsToSettings();
            using (FoilCalibrationForm form = new FoilCalibrationForm(_settings))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.HasResult)
                {
                    _numKFactor.Value = ToDecimal(
                        FoilMath.Clamp(form.Result.KFactor, 0.0, 1.0), _numKFactor);
                    _rbBendDeduction.Checked = true;
                    UpdatePreviewLabels();
                }
            }
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            SaveControlsToSettings();

            List<string> errors = FoilValidation.ValidateSettings(_settings);

            if (_settings.Method == FoilBendMethod.BendTable)
            {
                string path = _settings.BendTablePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = FoilSettingsStore.GetDefaultBendTablePath();
                }

                string error;
                if (FoilBendTable.LoadFromFile(path, out error) == null)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Vui long sua cac loi sau:" + Environment.NewLine + Environment.NewLine +
                    "  - " + string.Join(Environment.NewLine + "  - ", errors.ToArray()),
                    "DX_FOIL - Du lieu chua hop le",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        // ==============================================================================
        // TIEN ICH DUNG GIAO DIEN
        // ==============================================================================

        private static void AddRow(TableLayoutPanel host, ref int row, Control control)
        {
            control.Dock = control is Label ? DockStyle.Top : DockStyle.Fill;
            host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            host.Controls.Add(control, 0, row);
            row++;
        }

        private static GroupBox CreateGroupBox(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 16, 10, 10),
                Margin = new Padding(0, 0, 0, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
        }

        private static TableLayoutPanel CreateGrid(int columns)
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
            if (columns > 2)
            {
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            }

            return panel;
        }

        private static void AddField(
            TableLayoutPanel panel, int row, string label, Control editor, string suffix)
        {
            panel.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 7, 3, 3),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            }, 0, row);

            editor.Dock = DockStyle.Fill;
            editor.Margin = new Padding(3, 4, 3, 4);
            panel.Controls.Add(editor, 1, row);

            if (!string.IsNullOrEmpty(suffix))
            {
                panel.Controls.Add(new Label
                {
                    Text = suffix,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    ForeColor = Color.DimGray,
                    Margin = new Padding(6, 7, 3, 3),
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
                }, 2, row);
            }
        }

        private static NumericUpDown CreateNumeric(
            decimal min, decimal max, int decimals, decimal increment)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = increment,
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Right,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
        }

        private ComboBox CreateLayerCombo()
        {
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Dock = DockStyle.Fill,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            foreach (string layer in _drawingLayers)
            {
                combo.Items.Add(layer);
            }

            return combo;
        }

        private static decimal ToDecimal(double value, NumericUpDown target)
        {
            try
            {
                decimal d = (decimal)value;
                if (d < target.Minimum) return target.Minimum;
                if (d > target.Maximum) return target.Maximum;
                return decimal.Round(d, target.DecimalPlaces);
            }
            catch
            {
                return target.Minimum;
            }
        }
    }
}
