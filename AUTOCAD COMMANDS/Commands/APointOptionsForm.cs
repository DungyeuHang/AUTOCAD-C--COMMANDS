using System;
using System.Drawing;
using System.Windows.Forms;

namespace AUTOCAD_COMMANDS
{
    internal enum APointDialogAction
    {
        Cancel,
        Generate,
        ReselectPline
    }

    // Bảng tuỳ chọn dùng chung cho APOINT (gồm cả logic cũ của APOINT2):
    // - Chọn lại pline (không cần thoát hẳn lệnh).
    // - Bật/tắt gộp các điểm trùng toạ độ liên tiếp trên pline.
    // - Chọn kiểu dò điểm trùng First/Last.
    // - Đảo chiều thứ tự vẽ (không đụng vào pline gốc).
    // - Xem trước toàn bộ text APoint(...)/smart_pl(...) trước khi thực sự vẽ.
    // Layout dùng toạ độ tuyệt đối (Anchor) thay vì TableLayoutPanel để tránh co
    // giãn sai và viền ô nhập mất tích trên theme tối.
    internal sealed class APointOptionsForm : Form
    {
        private static readonly Color BackColorDark = Color.FromArgb(24, 24, 24);
        private static readonly Color PanelColorDark = Color.FromArgb(32, 32, 32);
        private static readonly Color InputColorDark = Color.FromArgb(45, 45, 48);
        private static readonly Color BorderColorDark = Color.FromArgb(90, 90, 96);
        private static readonly Color BorderColorFocus = Color.FromArgb(0, 122, 204);
        private static readonly Color TextColorLight = Color.FromArgb(230, 230, 230);
        private static readonly Color MutedTextColor = Color.FromArgb(160, 160, 160);
        private static readonly Color AccentColor = Color.FromArgb(0, 122, 204);

        private const int FormMargin = 16;

        private readonly Func<string, bool, APointGeometryHelper.MatchMode, bool, string> _previewBuilder;

        private Label _lblPlineInfo;
        private Panel _txtPrefixBorder;
        private TextBox _txtPrefix;
        private Panel _txtTextHeightBorder;
        private TextBox _txtTextHeight;
        private CheckBox _chkMergeDuplicates;
        private CheckBox _chkReverse;
        private RadioButton _radNone;
        private RadioButton _radFirst;
        private RadioButton _radLast;
        private Panel _pnlMatchMode;
        private Panel _txtPreviewBorder;
        private TextBox _txtPreview;
        private Button _btnGenerate;

        public string Prefix { get; private set; }
        public bool MergeDuplicates { get; private set; }
        public APointGeometryHelper.MatchMode MatchMode { get; private set; }
        public bool ReverseDirection { get; private set; }
        public double TextHeight { get; private set; }
        public APointDialogAction Action { get; private set; } = APointDialogAction.Cancel;

        public APointOptionsForm(
            string title,
            bool supportsMatchMode,
            string plineInfo,
            string initialPrefix,
            bool initialMerge,
            APointGeometryHelper.MatchMode initialMatchMode,
            bool initialReverse,
            double initialTextHeight,
            Func<string, bool, APointGeometryHelper.MatchMode, bool, string> previewBuilder)
        {
            _previewBuilder = previewBuilder;

            InitializeComponent(title, supportsMatchMode, plineInfo);

            _txtPrefix.Text = initialPrefix ?? string.Empty;
            _chkMergeDuplicates.Checked = initialMerge;
            _chkReverse.Checked = initialReverse;
            _radNone.Checked = initialMatchMode == APointGeometryHelper.MatchMode.None;
            _radFirst.Checked = initialMatchMode == APointGeometryHelper.MatchMode.First;
            _radLast.Checked = initialMatchMode == APointGeometryHelper.MatchMode.Last;
            _txtTextHeight.Text = initialTextHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);

            UpdatePreview();
        }

        private void InitializeComponent(string title, bool supportsMatchMode, string plineInfo)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(680, 640);
            Size = new Size(760, 760);
            BackColor = BackColorDark;
            ForeColor = TextColorLight;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            int y = FormMargin;

            // --- Dòng pline: label + nút chọn lại ---
            Button btnReselect = CreateSecondaryButton("Chọn lại Pline");
            btnReselect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnReselect.Location = new Point(ClientSize.Width - FormMargin - btnReselect.Width, y - 4);
            btnReselect.Click += (s, e) =>
            {
                Action = APointDialogAction.ReselectPline;
                DialogResult = DialogResult.Retry;
                Close();
            };
            Controls.Add(btnReselect);

            _lblPlineInfo = new Label
            {
                Text = plineInfo,
                ForeColor = TextColorLight,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(FormMargin, y),
                Size = new Size(ClientSize.Width - 2 * FormMargin - btnReselect.Width - 12, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_lblPlineInfo);

            y += 40;
            y += DrawSeparator(y);

            // --- Prefix + cỡ chữ (cùng 1 hàng) ---
            int halfWidth = (ClientSize.Width - 2 * FormMargin - 20) / 2;

            Label lblPrefix = CreateCaptionLabel("Tiền tố điểm (vd: bl_fr):", new Point(FormMargin, y));
            Controls.Add(lblPrefix);

            Label lblTextHeight = CreateCaptionLabel("Cỡ chữ (text height):", new Point(FormMargin + halfWidth + 20, y));
            Controls.Add(lblTextHeight);

            y += 22;

            _txtPrefix = CreateDarkTextBox(out _txtPrefixBorder);
            _txtPrefixBorder.Location = new Point(FormMargin, y);
            _txtPrefixBorder.Size = new Size(halfWidth, 28);
            _txtPrefix.TextChanged += (s, e) => UpdatePreview();
            Controls.Add(_txtPrefixBorder);

            _txtTextHeight = CreateDarkTextBox(out _txtTextHeightBorder);
            _txtTextHeightBorder.Location = new Point(FormMargin + halfWidth + 20, y);
            _txtTextHeightBorder.Size = new Size(halfWidth, 28);
            _txtTextHeight.TextChanged += (s, e) => UpdatePreview();
            Controls.Add(_txtTextHeightBorder);

            y += 40;

            // --- Gộp điểm trùng toạ độ ---
            _chkMergeDuplicates = CreateCheckBox(
                "Gộp các điểm trùng toạ độ liên tiếp trên pline (vd p2 = p3 -> coi là 1 điểm)",
                new Point(FormMargin, y));
            _chkMergeDuplicates.CheckedChanged += (s, e) => UpdatePreview();
            Controls.Add(_chkMergeDuplicates);

            y += 24;
            Label lblMergeHint = new Label
            {
                Text = "Tắt = giữ nguyên toàn bộ điểm kể cả khi trùng toạ độ (mặc định).",
                ForeColor = MutedTextColor,
                AutoSize = true,
                Location = new Point(FormMargin + 20, y)
            };
            Controls.Add(lblMergeHint);

            y += 28;

            // --- Đảo chiều ---
            _chkReverse = CreateCheckBox(
                "Đảo chiều thứ tự vẽ điểm (chỉ đổi thứ tự circle/label, không sửa pline gốc)",
                new Point(FormMargin, y));
            _chkReverse.CheckedChanged += (s, e) => UpdatePreview();
            Controls.Add(_chkReverse);

            y += 36;

            // --- Match mode (None/First/Last) ---
            _pnlMatchMode = new Panel
            {
                Location = new Point(FormMargin, y),
                Size = new Size(ClientSize.Width - 2 * FormMargin, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Visible = supportsMatchMode
            };
            Label lblMatchMode = CreateCaptionLabel("Dò điểm trùng toạ độ theo chỉ số:", new Point(0, 0));
            _radNone = new RadioButton
            {
                Text = "None - không dò (chỉ so điểm liền trước)",
                ForeColor = TextColorLight,
                AutoSize = true,
                Location = new Point(0, 24)
            };
            _radFirst = new RadioButton
            {
                Text = "First - nhỏ nhất (mặc định)",
                ForeColor = TextColorLight,
                AutoSize = true,
                Location = new Point(280, 24)
            };
            _radLast = new RadioButton
            {
                Text = "Last - lớn nhất",
                ForeColor = TextColorLight,
                AutoSize = true,
                Location = new Point(470, 24)
            };
            _radNone.CheckedChanged += (s, e) => UpdatePreview();
            _radFirst.CheckedChanged += (s, e) => UpdatePreview();
            _radLast.CheckedChanged += (s, e) => UpdatePreview();
            _pnlMatchMode.Controls.Add(lblMatchMode);
            _pnlMatchMode.Controls.Add(_radNone);
            _pnlMatchMode.Controls.Add(_radFirst);
            _pnlMatchMode.Controls.Add(_radLast);
            Controls.Add(_pnlMatchMode);

            if (supportsMatchMode)
            {
                y += 58;
            }

            y += DrawSeparator(y);

            // --- Tiêu đề preview ---
            Label lblPreviewTitle = CreateCaptionLabel("Xem trước kết quả (chưa vẽ lên bản vẽ):", new Point(FormMargin, y));
            lblPreviewTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(lblPreviewTitle);
            y += 26;

            // --- Nút hành động (neo đáy, đặt trước để tính chiều cao preview) ---
            Button btnCancel = CreateSecondaryButton("Huỷ");
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Location = new Point(ClientSize.Width - FormMargin - btnCancel.Width, ClientSize.Height - FormMargin - btnCancel.Height - 2);
            btnCancel.Click += (s, e) =>
            {
                Action = APointDialogAction.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _btnGenerate = CreatePrimaryButton("Tạo điểm");
            _btnGenerate.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnGenerate.Location = new Point(btnCancel.Left - 12 - _btnGenerate.Width, btnCancel.Top - 2);
            _btnGenerate.Click += (s, e) => OnGenerateClicked();

            // --- Preview (chiếm hết phần còn lại, neo cả 4 cạnh) ---
            _txtPreview = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                BackColor = InputColorDark,
                ForeColor = TextColorLight,
                Font = new Font("Consolas", 9.5F),
                Location = new Point(1, 1)
            };
            _txtPreviewBorder = new Panel
            {
                BackColor = BorderColorDark,
                Padding = new Padding(1),
                Location = new Point(FormMargin, y),
                Size = new Size(
                    ClientSize.Width - 2 * FormMargin,
                    ClientSize.Height - y - FormMargin - btnCancel.Height - 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            _txtPreview.Dock = DockStyle.Fill;
            _txtPreviewBorder.Controls.Add(_txtPreview);
            Controls.Add(_txtPreviewBorder);

            Controls.Add(btnCancel);
            Controls.Add(_btnGenerate);

            AcceptButton = null;
            CancelButton = btnCancel;
        }

        private int DrawSeparator(int y)
        {
            Panel line = new Panel
            {
                BackColor = PanelColorDark,
                Location = new Point(FormMargin, y),
                Size = new Size(ClientSize.Width - 2 * FormMargin, 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(line);
            return 14;
        }

        private void OnGenerateClicked()
        {
            Prefix = (_txtPrefix.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(Prefix))
            {
                MessageBox.Show(this, "Tiền tố điểm không được rỗng.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            double textHeight;
            if (!double.TryParse(
                    _txtTextHeight.Text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out textHeight)
                || textHeight <= 0.0)
            {
                MessageBox.Show(this, "Cỡ chữ phải là số dương.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MergeDuplicates = _chkMergeDuplicates.Checked;
            MatchMode = _radNone.Checked
                ? APointGeometryHelper.MatchMode.None
                : (_radLast.Checked ? APointGeometryHelper.MatchMode.Last : APointGeometryHelper.MatchMode.First);
            ReverseDirection = _chkReverse.Checked;
            TextHeight = textHeight;
            Action = APointDialogAction.Generate;
            DialogResult = DialogResult.OK;
            Close();
        }

        public void SetPlineInfo(string plineInfo)
        {
            _lblPlineInfo.Text = plineInfo;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_previewBuilder == null || _txtPreview == null)
            {
                return;
            }

            string prefix = string.IsNullOrWhiteSpace(_txtPrefix.Text) ? "p" : _txtPrefix.Text.Trim();
            try
            {
                APointGeometryHelper.MatchMode matchMode = _radNone.Checked
                    ? APointGeometryHelper.MatchMode.None
                    : (_radLast.Checked ? APointGeometryHelper.MatchMode.Last : APointGeometryHelper.MatchMode.First);
                string result = _previewBuilder(
                    prefix,
                    _chkMergeDuplicates.Checked,
                    matchMode,
                    _chkReverse.Checked);
                _txtPreview.Text = (result ?? string.Empty).Replace("\n", Environment.NewLine);
            }
            catch (Exception ex)
            {
                _txtPreview.Text = "(Không dựng được preview: " + ex.Message + ")";
            }
        }

        private static Label CreateCaptionLabel(string text, Point location)
        {
            return new Label
            {
                Text = text,
                ForeColor = TextColorLight,
                AutoSize = true,
                Location = location
            };
        }

        private static CheckBox CreateCheckBox(string text, Point location)
        {
            return new CheckBox
            {
                Text = text,
                ForeColor = TextColorLight,
                AutoSize = true,
                Location = location
            };
        }

        // Textbox dark theme, viền 1px rõ ràng bằng cách bọc trong Panel màu sáng hơn.
        private static TextBox CreateDarkTextBox(out Panel border)
        {
            TextBox textBox = new TextBox
            {
                BackColor = InputColorDark,
                ForeColor = TextColorLight,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F)
            };

            Panel borderPanel = new Panel
            {
                BackColor = BorderColorDark,
                Padding = new Padding(1)
            };
            borderPanel.Controls.Add(textBox);

            textBox.Enter += (s, e) => borderPanel.BackColor = BorderColorFocus;
            textBox.Leave += (s, e) => borderPanel.BackColor = BorderColorDark;

            border = borderPanel;
            return textBox;
        }

        private static Button CreatePrimaryButton(string text)
        {
            Button button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentColor,
                ForeColor = Color.White,
                Width = 130,
                Height = 34
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Button CreateSecondaryButton(string text)
        {
            Button button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = InputColorDark,
                ForeColor = TextColorLight,
                Width = 140,
                Height = 30
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderColorDark;
            return button;
        }
    }
}
