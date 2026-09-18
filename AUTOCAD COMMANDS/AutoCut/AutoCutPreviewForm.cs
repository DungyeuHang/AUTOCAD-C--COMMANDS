using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS
{
    public class AutoCutPreviewForm : Form
    {
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly Editor _editor;
        private readonly AutoCutSettings _settings;
        private readonly AutoCutPreviewTransient _transientPreview;
        private AutoCutAnalysisResult _currentAnalysis;

        private Label _lblSummary;
        private TextBox _txtDiagnostics;
        private ComboBox _cboCutMode;
        private Button _btnApply;
        private Button _btnCancel;

        public AutoCutAnalysisResult ResultAnalysis => _currentAnalysis;

        public AutoCutPreviewForm(
            Database db,
            Transaction tr,
            Editor editor,
            AutoCutSettings settings,
            AutoCutAnalysisResult initialAnalysis,
            AutoCutPreviewTransient transientPreview)
        {
            _db = db;
            _tr = tr;
            _editor = editor;
            _settings = settings;
            _currentAnalysis = initialAnalysis;
            _transientPreview = transientPreview;

            InitializeComponent();
            UpdateUiFromAnalysis();
        }

        private void InitializeComponent()
        {
            Text = "ACC_AUTO_CUT - Kết Quả Phân Tích & Xác Nhận";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(620, 680);
            Size = new Size(660, 720);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Panel mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };
            Controls.Add(mainPanel);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.Controls.Add(layout);

            // 1. Statistics Group
            GroupBox grpStats = new GroupBox
            {
                Text = "BẢNG THỐNG KÊ KẾT QUẢ",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _lblSummary = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.Black
            };
            grpStats.Controls.Add(_lblSummary);
            layout.Controls.Add(grpStats, 0, 0);

            // 2. Quick Cut Mode Adjuster
            GroupBox grpAdjust = new GroupBox
            {
                Text = "ĐIỀU CHỈNH NHANH CHẾ ĐỘ CẮT (CUT MODE)",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            TableLayoutPanel pnlAdjust = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true
            };
            pnlAdjust.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlAdjust.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            pnlAdjust.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));

            _cboCutMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            _cboCutMode.Items.AddRange(new object[]
            {
                "1. BETWEEN INTERSECTIONS (Cắt đoạn giữa giao điểm)",
                "2. CLOSED CUTTER - REMOVE INSIDE (Cắt bên trong)",
                "3. CLOSED CUTTER - REMOVE OUTSIDE (Cắt bên ngoài)",
                "4. REMOVE SHORTEST (Cắt các đoạn ngắn / Notches)",
                "5. REMOVE LONGEST (Cắt đoạn dài nhất)",
                "6. SINGLE INTERSECTION RULE"
            });
            _cboCutMode.SelectedIndex = (int)_settings.CutMode;
            _cboCutMode.SelectedIndexChanged += OnRecomputeClicked;

            Button btnRecompute = new Button
            {
                Text = "Cập nhật",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnRecompute.Click += OnRecomputeClicked;

            Button btnInvert = new Button
            {
                Text = "Đảo Ngược (KEEP ↔ REMOVE)",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 243, 205),
                FlatStyle = FlatStyle.Flat
            };
            btnInvert.Click += OnInvertClicked;

            pnlAdjust.Controls.Add(_cboCutMode, 0, 0);
            pnlAdjust.Controls.Add(btnRecompute, 1, 0);
            pnlAdjust.Controls.Add(btnInvert, 2, 0);
            grpAdjust.Controls.Add(pnlAdjust);
            layout.Controls.Add(grpAdjust, 0, 1);

            // 3. Diagnostic Log Group
            GroupBox grpDiag = new GroupBox
            {
                Text = "NHẬT KÝ CHẨN ĐOÁN (DIAGNOSTIC DETAILS)",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 16, 10, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _txtDiagnostics = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5F, FontStyle.Regular),
                BackColor = Color.White
            };
            grpDiag.Controls.Add(_txtDiagnostics);
            layout.Controls.Add(grpDiag, 0, 2);

            // 4. Action Buttons
            TableLayoutPanel pnlButtons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Height = 46,
                Padding = new Padding(0, 6, 0, 0)
            };
            pnlButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            pnlButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            _btnApply = new Button
            {
                Text = "APPLY (Áp Dụng Cắt)",
                Dock = DockStyle.Fill,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            _btnApply.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            _btnCancel = new Button
            {
                Text = "CANCEL (Hủy Bỏ - Không Đổi Bản Vẽ)",
                Dock = DockStyle.Fill,
                DialogResult = DialogResult.Cancel,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };
            _btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            pnlButtons.Controls.Add(_btnApply, 0, 0);
            pnlButtons.Controls.Add(_btnCancel, 1, 0);
            layout.Controls.Add(pnlButtons, 0, 3);
        }

        private void UpdateUiFromAnalysis()
        {
            if (_currentAnalysis == null) return;

            _lblSummary.Text = _currentAnalysis.Report.FormatSummaryText() +
                               "\n* Chú ý: Các đoạn màu ĐỎ trên màn hình CAD sẽ bị CẮT BỎ khi bấm Apply.";

            _txtDiagnostics.Text = _currentAnalysis.Report.FormatDiagnosticLog();

            // Disable Apply if nothing to cut
            _btnApply.Enabled = _currentAnalysis.Report.SegmentsToRemoveCount > 0;
            if (!_btnApply.Enabled)
            {
                _btnApply.Text = "Không có đoạn nào cần cắt (0 REMOVE)";
                _btnApply.BackColor = Color.Gray;
                _lblSummary.Text += "\n💡 Gợi ý: Bấm nút 'Đảo Ngược (KEEP ↔ REMOVE)' hoặc chọn Chế độ '4. REMOVE SHORTEST' để chọn các đoạn notch cần cắt!";
            }
            else
            {
                _btnApply.Text = $"APPLY (Cắt bỏ {_currentAnalysis.Report.SegmentsToRemoveCount} đoạn)";
                _btnApply.BackColor = Color.FromArgb(46, 125, 50);
            }
        }

        private void OnInvertClicked(object sender, EventArgs e)
        {
            try
            {
                AutoCutGeometryEngine.InvertClassifications(_currentAnalysis);
                _transientPreview.DisplayPreview(_currentAnalysis, _editor);
                UpdateUiFromAnalysis();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lỗi đảo ngược: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRecomputeClicked(object sender, EventArgs e)
        {
            try
            {
                _settings.CutMode = (AutoCutCutMode)_cboCutMode.SelectedIndex;
                AutoCutSettingsStore.Save(_settings);

                // Collect target IDs
                var targetIds = _currentAnalysis.TargetPlans.Select(p => p.TargetId).ToList();

                // Re-run analysis
                _currentAnalysis.Dispose();
                _currentAnalysis = AutoCutGeometryEngine.Analyze(_db, _tr, targetIds, _settings);

                // Update transient preview
                _transientPreview.DisplayPreview(_currentAnalysis, _editor);

                // Update UI
                UpdateUiFromAnalysis();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Lỗi cập nhật preview: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
