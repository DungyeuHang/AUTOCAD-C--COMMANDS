using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS
{
    public class AutoCutSettingsForm : Form
    {
        private readonly AutoCutSettings _settings;
        private readonly Database _db;
        private readonly List<string> _drawingLayers = new List<string>();

        private RadioButton _rbTargetAll;
        private RadioButton _rbTargetLine;
        private RadioButton _rbTargetPolyline;
        private RadioButton _rbTargetBoth;
        private RadioButton _rbTargetCircleArc;

        private ComboBox _cboTargetLayerMode;
        private ComboBox _cboTargetLayerName;
        private Button _btnPickTargetLayer;

        private CheckBox _chkCutterLine;
        private CheckBox _chkCutterPolyline;
        private CheckBox _chkCutterArc;
        private CheckBox _chkCutterCircle;
        private CheckBox _chkCutterEllipse;
        private CheckBox _chkCutterSpline;

        private ComboBox _cboLayerFilter;
        private ComboBox _cboCustomLayer;
        private Button _btnPickCutterLayer;

        private ComboBox _cboColorFilter;
        private NumericUpDown _numSpecificColor;

        private ComboBox _cboCutMode;
        private ComboBox _cboSingleIntersection;

        private NumericUpDown _numSearchTolerance;
        private NumericUpDown _numDeduplicationTolerance;
        private CheckBox _chkIgnoreElevation;
        private CheckBox _chkShowSettingsBeforeSelection;

        public bool ProceedToSelectTarget { get; private set; }

        public AutoCutSettingsForm(AutoCutSettings settings, Database db = null)
        {
            _settings = settings?.Clone() ?? new AutoCutSettings();
            _db = db ?? Application.DocumentManager.MdiActiveDocument?.Database;
            LoadDrawingLayers();
            InitializeComponent();
            LoadSettingsToControls();
        }

        private void LoadDrawingLayers()
        {
            _drawingLayers.Clear();
            if (_db == null) return;
            try
            {
                using (Transaction tr = _db.TransactionManager.StartOpenCloseTransaction())
                {
                    LayerTable lt = tr.GetObject(_db.LayerTableId, OpenMode.ForRead) as LayerTable;
                    if (lt != null)
                    {
                        foreach (ObjectId lId in lt)
                        {
                            LayerTableRecord ltr = tr.GetObject(lId, OpenMode.ForRead) as LayerTableRecord;
                            if (ltr != null && !string.IsNullOrWhiteSpace(ltr.Name))
                            {
                                _drawingLayers.Add(ltr.Name);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            _drawingLayers.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public AutoCutSettings GetSettings()
        {
            SaveControlsToSettings();
            return _settings;
        }

        private void InitializeComponent()
        {
            Text = "ACC_AUTO_CUT - Cài Đặt Tự Động Cắt";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(620, 780);
            Size = new Size(660, 820);
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

            // Header Banner / Hint
            Label hintLabel = new Label
            {
                Text = "QUAN TRỌNG: Bạn chỉ cần chọn TARGET (biên dạng cần cắt).\nChương trình sẽ TỰ ĐỘNG TÌM các đối tượng CUTTER trong bản vẽ theo cài đặt dưới đây.",
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 102, 204),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            };
            AddRow(content, ref row, hintLabel);

            // Group 1: Target Type & Target Layer
            GroupBox grpTarget = CreateGroupBox("1. ĐỐI TƯỢNG TARGET (Biên dạng gốc cần cắt)");
            TableLayoutPanel pnlTarget = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            pnlTarget.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlTarget.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            _rbTargetAll = new RadioButton { Text = "Tất cả các loại CURVE (Line, Pline, Arc, Circle, Ellipse, Spline) [Mặc định]", AutoSize = true, Checked = true };
            _rbTargetBoth = new RadioButton { Text = "Cả LINE + PLINE", AutoSize = true };
            _rbTargetCircleArc = new RadioButton { Text = "Chỉ CIRCLE & ARC", AutoSize = true };
            _rbTargetLine = new RadioButton { Text = "Chỉ LINE", AutoSize = true };
            _rbTargetPolyline = new RadioButton { Text = "Chỉ PLINE", AutoSize = true };

            pnlTarget.Controls.Add(_rbTargetAll, 0, 0);
            pnlTarget.SetColumnSpan(_rbTargetAll, 2);
            pnlTarget.Controls.Add(_rbTargetBoth, 0, 1);
            pnlTarget.Controls.Add(_rbTargetCircleArc, 1, 1);
            pnlTarget.Controls.Add(_rbTargetLine, 0, 2);
            pnlTarget.Controls.Add(_rbTargetPolyline, 1, 2);

            // Separator / Target Layer Sub-header
            Label lblTargetLayerTitle = new Label
            {
                Text = "─ LỌC TARGET THEO LAYER TRONG BẢN VẼ (Hỗ trợ quét chọn nhanh) ─",
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 102, 204),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(0, 10, 0, 4)
            };
            pnlTarget.Controls.Add(lblTargetLayerTitle, 0, 3);
            pnlTarget.SetColumnSpan(lblTargetLayerTitle, 2);

            TableLayoutPanel pnlTargetLayer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true
            };
            pnlTargetLayer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            pnlTargetLayer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43f));
            pnlTargetLayer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15f));

            _cboTargetLayerMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboTargetLayerMode.Items.AddRange(new object[]
            {
                "Tất cả Layer (Không lọc) [Mặc định]",
                "Chỉ Layer hiện hành (Current Layer)",
                "Chỉ Layer chỉ định (Chọn bên phải):"
            });
            _cboTargetLayerMode.SelectedIndex = 0;

            _cboTargetLayerName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Enabled = false };
            foreach (string l in _drawingLayers)
            {
                _cboTargetLayerName.Items.Add(l);
            }

            _btnPickTargetLayer = new Button
            {
                Text = "🔍 Pick",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Enabled = false
            };
            _btnPickTargetLayer.Click += OnPickTargetLayerClicked;

            _cboTargetLayerMode.SelectedIndexChanged += (s, e) =>
            {
                bool isSpecific = _cboTargetLayerMode.SelectedIndex == (int)AutoCutTargetLayerFilterMode.SpecificLayer;
                _cboTargetLayerName.Enabled = isSpecific;
                _btnPickTargetLayer.Enabled = isSpecific;
            };

            pnlTargetLayer.Controls.Add(_cboTargetLayerMode, 0, 0);
            pnlTargetLayer.Controls.Add(_cboTargetLayerName, 1, 0);
            pnlTargetLayer.Controls.Add(_btnPickTargetLayer, 2, 0);

            pnlTarget.Controls.Add(pnlTargetLayer, 0, 4);
            pnlTarget.SetColumnSpan(pnlTargetLayer, 2);

            grpTarget.Controls.Add(pnlTarget);
            AddRow(content, ref row, grpTarget);

            // Group 2: Cutter Types
            GroupBox grpCutter = CreateGroupBox("2. LOẠI CUTTER TỰ ĐỘNG TÌM KIẾM (Ứng viên cắt)");
            TableLayoutPanel pnlCutter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            pnlCutter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            pnlCutter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            pnlCutter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));

            _chkCutterLine = new CheckBox { Text = "LINE", AutoSize = true, Checked = true };
            _chkCutterPolyline = new CheckBox { Text = "PLINE", AutoSize = true, Checked = true };
            _chkCutterArc = new CheckBox { Text = "ARC", AutoSize = true, Checked = true };
            _chkCutterCircle = new CheckBox { Text = "CIRCLE", AutoSize = true, Checked = true };
            _chkCutterEllipse = new CheckBox { Text = "ELLIPSE", AutoSize = true, Checked = true };
            _chkCutterSpline = new CheckBox { Text = "SPLINE", AutoSize = true, Checked = true };

            pnlCutter.Controls.Add(_chkCutterLine, 0, 0);
            pnlCutter.Controls.Add(_chkCutterPolyline, 1, 0);
            pnlCutter.Controls.Add(_chkCutterArc, 2, 0);
            pnlCutter.Controls.Add(_chkCutterCircle, 0, 1);
            pnlCutter.Controls.Add(_chkCutterEllipse, 1, 1);
            pnlCutter.Controls.Add(_chkCutterSpline, 2, 1);
            grpCutter.Controls.Add(pnlCutter);
            AddRow(content, ref row, grpCutter);

            // Group 3: Layer & Color Filter
            GroupBox grpFilter = CreateGroupBox("3. BỘ LỌC LAYER & MÀU SẮC CHO CUTTER");
            TableLayoutPanel pnlFilter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            pnlFilter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
            pnlFilter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            pnlFilter.Controls.Add(new Label { Text = "Bộ lọc Layer Cutter:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _cboLayerFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboLayerFilter.Items.AddRange(new object[]
            {
                "Same as Target (Cùng Layer với Target) [Mặc định]",
                "Current Layer (Chỉ Layer hiện hành)",
                "Any Layer (Bất kỳ Layer nào)",
                "Custom Layer (Layer tùy chỉnh chỉ định)"
            });
            _cboLayerFilter.SelectedIndex = 0;
            pnlFilter.Controls.Add(_cboLayerFilter, 1, 0);

            pnlFilter.Controls.Add(new Label { Text = "Custom Layer:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            TableLayoutPanel pnlCustomCutter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            pnlCustomCutter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 82f));
            pnlCustomCutter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18f));

            _cboCustomLayer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Enabled = false };
            foreach (string l in _drawingLayers)
            {
                _cboCustomLayer.Items.Add(l);
            }

            _btnPickCutterLayer = new Button { Text = "🔍 Pick", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), Enabled = false };
            _btnPickCutterLayer.Click += OnPickCutterLayerClicked;

            _cboLayerFilter.SelectedIndexChanged += (s, e) =>
            {
                bool isCustom = _cboLayerFilter.SelectedIndex == 3;
                _cboCustomLayer.Enabled = isCustom;
                _btnPickCutterLayer.Enabled = isCustom;
            };

            pnlCustomCutter.Controls.Add(_cboCustomLayer, 0, 0);
            pnlCustomCutter.Controls.Add(_btnPickCutterLayer, 1, 0);
            pnlFilter.Controls.Add(pnlCustomCutter, 1, 1);

            pnlFilter.Controls.Add(new Label { Text = "Bộ lọc Màu sắc:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            _cboColorFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboColorFilter.Items.AddRange(new object[]
            {
                "Same as Target (Cùng thuộc tính Màu) [Mặc định]",
                "Same Effective Color (Cùng màu hiển thị thực tế)",
                "ByLayer (Màu theo Layer)",
                "Specific Color (Màu ACI cụ thể)",
                "Any Color (Bất kỳ Màu nào)"
            });
            _cboColorFilter.SelectedIndex = 0;
            _cboColorFilter.SelectedIndexChanged += (s, e) => _numSpecificColor.Enabled = _cboColorFilter.SelectedIndex == 3;
            pnlFilter.Controls.Add(_cboColorFilter, 1, 2);

            pnlFilter.Controls.Add(new Label { Text = "Specific Color ACI:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            _numSpecificColor = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 1, Dock = DockStyle.Fill, Enabled = false };
            pnlFilter.Controls.Add(_numSpecificColor, 1, 3);

            grpFilter.Controls.Add(pnlFilter);
            AddRow(content, ref row, grpFilter);

            // Group 4: Cut Mode
            GroupBox grpMode = CreateGroupBox("4. CHẾ ĐỘ CẮT (CUT MODE)");
            TableLayoutPanel pnlMode = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            pnlMode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            pnlMode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            pnlMode.Controls.Add(new Label { Text = "Chế độ cắt chính:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _cboCutMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboCutMode.Items.AddRange(new object[]
            {
                "1. BETWEEN INTERSECTIONS (Cắt đoạn giữa 2 giao điểm) [Mặc định]",
                "2. CLOSED CUTTER - REMOVE INSIDE (Cắt đoạn bên trong cutter kín)",
                "3. CLOSED CUTTER - REMOVE OUTSIDE (Cắt đoạn bên ngoài cutter kín)",
                "4. REMOVE SHORTEST (Cắt các đoạn ngắn / Notches)",
                "5. REMOVE LONGEST (Cắt đoạn dài nhất)",
                "6. SINGLE INTERSECTION RULE (Theo quy tắc 1 giao điểm)"
            });
            _cboCutMode.SelectedIndex = 0;
            pnlMode.Controls.Add(_cboCutMode, 1, 0);

            pnlMode.Controls.Add(new Label { Text = "Giao 1 điểm (Single):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            _cboSingleIntersection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cboSingleIntersection.Items.AddRange(new object[]
            {
                "Skip (Bỏ qua, không cắt) [Mặc định]",
                "Remove Before (Cắt bỏ phần trước giao điểm)",
                "Remove After (Cắt bỏ phần sau giao điểm)"
            });
            _cboSingleIntersection.SelectedIndex = 0;
            pnlMode.Controls.Add(_cboSingleIntersection, 1, 1);

            grpMode.Controls.Add(pnlMode);
            AddRow(content, ref row, grpMode);

            // Group 5: Tolerance & General
            GroupBox grpTol = CreateGroupBox("5. DUNG SAI & CẤU HÌNH BỔ TRỢ");
            TableLayoutPanel pnlTol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            pnlTol.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
            pnlTol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            pnlTol.Controls.Add(new Label { Text = "Search Tolerance (mm/đơn vị):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _numSearchTolerance = new NumericUpDown { DecimalPlaces = 3, Minimum = 0.001M, Maximum = 1000M, Increment = 0.5M, Value = 1.0M, Dock = DockStyle.Fill };
            pnlTol.Controls.Add(_numSearchTolerance, 1, 0);

            pnlTol.Controls.Add(new Label { Text = "Deduplication Tolerance:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            _numDeduplicationTolerance = new NumericUpDown { DecimalPlaces = 5, Minimum = 0.00001M, Maximum = 1M, Increment = 0.0001M, Value = 0.0001M, Dock = DockStyle.Fill };
            pnlTol.Controls.Add(_numDeduplicationTolerance, 1, 1);

            _chkIgnoreElevation = new CheckBox { Text = "Bỏ qua chênh lệch cao độ Z (Chiếu 2D XY)", AutoSize = true, Checked = true };
            pnlTol.Controls.Add(_chkIgnoreElevation, 0, 2);
            pnlTol.SetColumnSpan(_chkIgnoreElevation, 2);

            _chkShowSettingsBeforeSelection = new CheckBox { Text = "Luôn hiển thị hộp thoại Cài đặt này khi chạy ACC_AUTO_CUT", AutoSize = true, Checked = true };
            pnlTol.Controls.Add(_chkShowSettingsBeforeSelection, 0, 3);
            pnlTol.SetColumnSpan(_chkShowSettingsBeforeSelection, 2);

            grpTol.Controls.Add(pnlTol);
            AddRow(content, ref row, grpTol);

            // Bottom Buttons Panel
            TableLayoutPanel buttonPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 3,
                Height = 44,
                Padding = new Padding(8, 6, 8, 6)
            };
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            Button btnSelect = new Button
            {
                Text = "Chọn TARGET & Xem Trước >>",
                Dock = DockStyle.Fill,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnSelect.Click += (s, e) =>
            {
                ProceedToSelectTarget = true;
                SaveControlsToSettings();
                AutoCutSettingsStore.Save(_settings);
                Close();
            };

            Button btnSaveOnly = new Button
            {
                Text = "Lưu Cài Đặt",
                Dock = DockStyle.Fill,
                DialogResult = DialogResult.OK
            };
            btnSaveOnly.Click += (s, e) =>
            {
                ProceedToSelectTarget = false;
                SaveControlsToSettings();
                AutoCutSettingsStore.Save(_settings);
                Close();
            };

            Button btnCancel = new Button
            {
                Text = "Đóng",
                Dock = DockStyle.Fill,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.Click += (s, e) => Close();

            buttonPanel.Controls.Add(btnSelect, 0, 0);
            buttonPanel.Controls.Add(btnSaveOnly, 1, 0);
            buttonPanel.Controls.Add(btnCancel, 2, 0);

            Controls.Add(buttonPanel);
        }

        private void OnPickTargetLayerClicked(object sender, EventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;
                using (ed.StartUserInteraction(this))
                {
                    PromptEntityOptions peo = new PromptEntityOptions("\nChọn 1 đối tượng trên bản vẽ để lấy Layer Target: ");
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.OK)
                    {
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                _cboTargetLayerMode.SelectedIndex = (int)AutoCutTargetLayerFilterMode.SpecificLayer;
                                _cboTargetLayerName.Text = ent.Layer;
                            }
                            tr.Commit();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lỗi pick đối tượng: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnPickCutterLayerClicked(object sender, EventArgs e)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;
                using (ed.StartUserInteraction(this))
                {
                    PromptEntityOptions peo = new PromptEntityOptions("\nChọn 1 đối tượng trên bản vẽ để lấy Layer Cutter: ");
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.OK)
                    {
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                _cboLayerFilter.SelectedIndex = 3;
                                _cboCustomLayer.Text = ent.Layer;
                            }
                            tr.Commit();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lỗi pick đối tượng: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static GroupBox CreateGroupBox(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
                Margin = new Padding(0, 0, 0, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
        }

        private static void AddRow(TableLayoutPanel table, ref int row, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(control, 0, row++);
        }

        private void LoadSettingsToControls()
        {
            // Target type
            if (_settings.TargetTypes == AutoCutTargetTypeFlags.Line) _rbTargetLine.Checked = true;
            else if (_settings.TargetTypes == AutoCutTargetTypeFlags.Polyline) _rbTargetPolyline.Checked = true;
            else if (_settings.TargetTypes == (AutoCutTargetTypeFlags.Line | AutoCutTargetTypeFlags.Polyline)) _rbTargetBoth.Checked = true;
            else if (_settings.TargetTypes == (AutoCutTargetTypeFlags.Circle | AutoCutTargetTypeFlags.Arc)) _rbTargetCircleArc.Checked = true;
            else _rbTargetAll.Checked = true;

            // Target layer
            _cboTargetLayerMode.SelectedIndex = (int)_settings.TargetLayerFilterMode;
            _cboTargetLayerName.Text = _settings.TargetLayerName ?? string.Empty;
            bool isTargetSpecific = _settings.TargetLayerFilterMode == AutoCutTargetLayerFilterMode.SpecificLayer;
            _cboTargetLayerName.Enabled = isTargetSpecific;
            _btnPickTargetLayer.Enabled = isTargetSpecific;

            // Cutter types
            _chkCutterLine.Checked = _settings.CutterTypes.HasFlag(AutoCutCutterTypeFlags.Line);
            _chkCutterPolyline.Checked = _settings.CutterTypes.HasFlag(AutoCutCutterTypeFlags.Polyline);
            _chkCutterArc.Checked = _settings.CutterTypes.HasFlag(AutoCutCutterTypeFlags.Arc);
            _chkCutterCircle.Checked = _settings.CutterTypes.HasFlag(AutoCutCutterTypeFlags.Circle);
            _chkCutterEllipse.Checked = _settings.CutterTypes.HasFlag(AutoCutCutterTypeFlags.Ellipse);
            _chkCutterSpline.Checked = _settings.CutterTypes.HasFlag(AutoCutCutterTypeFlags.Spline);

            // Cutter layer filter
            _cboLayerFilter.SelectedIndex = (int)_settings.LayerFilterMode;
            _cboCustomLayer.Text = _settings.CustomLayerName ?? string.Empty;
            bool isCutterCustom = _settings.LayerFilterMode == AutoCutLayerFilterMode.CustomLayer;
            _cboCustomLayer.Enabled = isCutterCustom;
            _btnPickCutterLayer.Enabled = isCutterCustom;

            // Color filter
            _cboColorFilter.SelectedIndex = (int)_settings.ColorFilterMode;
            _numSpecificColor.Value = Math.Max(1, Math.Min(255, _settings.SpecificColorIndex));
            _numSpecificColor.Enabled = _settings.ColorFilterMode == AutoCutColorFilterMode.SpecificColor;

            // Cut Mode
            _cboCutMode.SelectedIndex = (int)_settings.CutMode;
            _cboSingleIntersection.SelectedIndex = (int)_settings.SingleIntersectionRule;

            // Tolerances
            _numSearchTolerance.Value = (decimal)Math.Max(0.001, _settings.SearchTolerance);
            _numDeduplicationTolerance.Value = (decimal)Math.Max(0.00001, _settings.DeduplicationTolerance);
            _chkIgnoreElevation.Checked = _settings.IgnoreElevation;
            _chkShowSettingsBeforeSelection.Checked = _settings.ShowSettingsBeforeSelection;
        }

        private void SaveControlsToSettings()
        {
            // Target type
            if (_rbTargetAll.Checked) _settings.TargetTypes = AutoCutTargetTypeFlags.AllCurves;
            else if (_rbTargetBoth.Checked) _settings.TargetTypes = AutoCutTargetTypeFlags.Line | AutoCutTargetTypeFlags.Polyline;
            else if (_rbTargetCircleArc.Checked) _settings.TargetTypes = AutoCutTargetTypeFlags.Circle | AutoCutTargetTypeFlags.Arc;
            else if (_rbTargetLine.Checked) _settings.TargetTypes = AutoCutTargetTypeFlags.Line;
            else if (_rbTargetPolyline.Checked) _settings.TargetTypes = AutoCutTargetTypeFlags.Polyline;
            else _settings.TargetTypes = AutoCutTargetTypeFlags.AllCurves;

            // Target layer
            _settings.TargetLayerFilterMode = (AutoCutTargetLayerFilterMode)_cboTargetLayerMode.SelectedIndex;
            _settings.TargetLayerName = _cboTargetLayerName.Text.Trim();

            // Cutter types
            AutoCutCutterTypeFlags cf = AutoCutCutterTypeFlags.None;
            if (_chkCutterLine.Checked) cf |= AutoCutCutterTypeFlags.Line;
            if (_chkCutterPolyline.Checked) cf |= AutoCutCutterTypeFlags.Polyline;
            if (_chkCutterArc.Checked) cf |= AutoCutCutterTypeFlags.Arc;
            if (_chkCutterCircle.Checked) cf |= AutoCutCutterTypeFlags.Circle;
            if (_chkCutterEllipse.Checked) cf |= AutoCutCutterTypeFlags.Ellipse;
            if (_chkCutterSpline.Checked) cf |= AutoCutCutterTypeFlags.Spline;
            _settings.CutterTypes = cf;

            // Cutter layer
            _settings.LayerFilterMode = (AutoCutLayerFilterMode)_cboLayerFilter.SelectedIndex;
            _settings.CustomLayerName = _cboCustomLayer.Text.Trim();

            // Color
            _settings.ColorFilterMode = (AutoCutColorFilterMode)_cboColorFilter.SelectedIndex;
            _settings.SpecificColorIndex = (int)_numSpecificColor.Value;

            // Cut mode
            _settings.CutMode = (AutoCutCutMode)_cboCutMode.SelectedIndex;
            _settings.SingleIntersectionRule = (AutoCutSingleIntersectionRule)_cboSingleIntersection.SelectedIndex;

            // Tolerances
            _settings.SearchTolerance = (double)_numSearchTolerance.Value;
            _settings.DeduplicationTolerance = (double)_numDeduplicationTolerance.Value;
            _settings.IgnoreElevation = _chkIgnoreElevation.Checked;
            _settings.ShowSettingsBeforeSelection = _chkShowSettingsBeforeSelection.Checked;
        }
    }
}
