namespace AUTOCAD_COMMANDS
{
    partial class QuickCalculatorForm
    {
        private static class Theme
        {
            public static readonly System.Drawing.Color Background = System.Drawing.Color.FromArgb(24, 26, 34);
            public static readonly System.Drawing.Color Panel = System.Drawing.Color.FromArgb(35, 38, 50);
            public static readonly System.Drawing.Color Inset = System.Drawing.Color.FromArgb(26, 28, 38);
            public static readonly System.Drawing.Color Accent = System.Drawing.Color.FromArgb(64, 156, 255);
            public static readonly System.Drawing.Color AccentText = System.Drawing.Color.FromArgb(18, 20, 26);
            public static readonly System.Drawing.Color ButtonBg = System.Drawing.Color.FromArgb(45, 49, 64);
            public static readonly System.Drawing.Color ButtonHoverBg = System.Drawing.Color.FromArgb(58, 63, 82);
            public static readonly System.Drawing.Color TextPrimary = System.Drawing.Color.FromArgb(232, 235, 242);
            public static readonly System.Drawing.Color TextMuted = System.Drawing.Color.FromArgb(190, 195, 208);
        }

        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.txtDisplay = new System.Windows.Forms.TextBox();
            this.btnClear = new System.Windows.Forms.Button();
            this.btnDim = new System.Windows.Forms.Button();
            this.lstHistory = new System.Windows.Forms.ListBox();
            this.btnClearHistory = new System.Windows.Forms.Button();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.panelHistory = new System.Windows.Forms.Panel();
            this.panelExpression = new System.Windows.Forms.Panel();
            this.flowButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.SuspendLayout();
            // 
            // txtDisplay
            // Nền "chìm" (Theme.Inset) + font Consolas cho cảm giác màn hình số của calculator
            this.txtDisplay.BackColor = Theme.Inset;
            this.txtDisplay.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtDisplay.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtDisplay.Font = new System.Drawing.Font("Consolas", 14F, System.Drawing.FontStyle.Bold);
            this.txtDisplay.ForeColor = Theme.TextPrimary;
            this.txtDisplay.Location = new System.Drawing.Point(0, 0);
            this.txtDisplay.Margin = new System.Windows.Forms.Padding(0);
            this.txtDisplay.Name = "txtDisplay";
            this.txtDisplay.Size = new System.Drawing.Size(260, 38);
            this.txtDisplay.TabIndex = 0;
            this.txtDisplay.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.txtDisplay.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtDisplay_KeyDown);
            // 
            // btnClear
            // Nút phụ - dùng ButtonBg trung tính, không giành sự chú ý với nút DIM
            this.btnClear.BackColor = Theme.ButtonBg;
            this.btnClear.FlatAppearance.BorderSize = 0;
            this.btnClear.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClear.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnClear.ForeColor = Theme.TextPrimary;
            this.btnClear.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.btnClear.Name = "btnClear";
            this.btnClear.Padding = new System.Windows.Forms.Padding(0, 1, 0, 0);
            this.btnClear.Size = new System.Drawing.Size(76, 34);
            this.btnClear.TabIndex = 17;
            this.btnClear.Text = "C";
            this.btnClear.UseVisualStyleBackColor = false;
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);
            this.btnClear.MouseEnter += new System.EventHandler(this.btnAccent_MouseEnter);
            this.btnClear.MouseLeave += new System.EventHandler(this.btnAccent_MouseLeave);
            // 
            // btnDim
            // Nút hành động chính -> dùng màu Accent để nổi bật hẳn lên so với 2 nút còn lại
            this.btnDim.BackColor = Theme.Accent;
            this.btnDim.FlatAppearance.BorderSize = 0;
            this.btnDim.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDim.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnDim.ForeColor = Theme.AccentText;
            this.btnDim.Margin = new System.Windows.Forms.Padding(0);
            this.btnDim.Name = "btnDim";
            this.btnDim.Padding = new System.Windows.Forms.Padding(0, 1, 0, 0);
            this.btnDim.Size = new System.Drawing.Size(76, 34);
            this.btnDim.TabIndex = 18;
            this.btnDim.Text = "DIM";
            this.btnDim.UseVisualStyleBackColor = false;
            this.btnDim.Click += new System.EventHandler(this.btnDim_Click);
            this.btnDim.MouseEnter += new System.EventHandler(this.btnAccent_MouseEnter);
            this.btnDim.MouseLeave += new System.EventHandler(this.btnAccent_MouseLeave);
            // 
            // lstHistory
            // Cùng tông "chìm" với txtDisplay, chữ hơi mờ hơn để không cạnh tranh với nội dung chính
            this.lstHistory.BackColor = Theme.Inset;
            this.lstHistory.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lstHistory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstHistory.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lstHistory.ForeColor = Theme.TextMuted;
            this.lstHistory.FormattingEnabled = true;
            this.lstHistory.ItemHeight = 22;
            this.lstHistory.Location = new System.Drawing.Point(8, 30);
            this.lstHistory.Name = "lstHistory";
            this.lstHistory.Size = new System.Drawing.Size(244, 106);
            this.lstHistory.TabIndex = 19;
            this.lstHistory.DoubleClick += new System.EventHandler(this.lstHistory_DoubleClick);
            // 
            // btnClearHistory
            // 
            this.btnClearHistory.BackColor = Theme.ButtonBg;
            this.btnClearHistory.FlatAppearance.BorderSize = 0;
            this.btnClearHistory.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClearHistory.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnClearHistory.ForeColor = Theme.TextPrimary;
            this.btnClearHistory.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.btnClearHistory.Name = "btnClearHistory";
            this.btnClearHistory.Padding = new System.Windows.Forms.Padding(0, 1, 0, 0);
            this.btnClearHistory.Size = new System.Drawing.Size(76, 34);
            this.btnClearHistory.TabIndex = 20;
            this.btnClearHistory.Text = "Clear";
            this.btnClearHistory.UseVisualStyleBackColor = false;
            this.btnClearHistory.Click += new System.EventHandler(this.btnClearHistory_Click);
            this.btnClearHistory.MouseEnter += new System.EventHandler(this.btnAccent_MouseEnter);
            this.btnClearHistory.MouseLeave += new System.EventHandler(this.btnAccent_MouseLeave);
            // 
            // flowButtons
            // Tăng khoảng đệm trên/dưới và khoảng cách giữa các nút cho thoáng hơn
            this.flowButtons.AutoSize = false;
            this.flowButtons.Controls.Add(this.btnClear);
            this.flowButtons.Controls.Add(this.btnClearHistory);
            this.flowButtons.Controls.Add(this.btnDim);
            this.flowButtons.Dock = System.Windows.Forms.DockStyle.Top;
            this.flowButtons.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.flowButtons.Location = new System.Drawing.Point(0, 38);
            this.flowButtons.Margin = new System.Windows.Forms.Padding(0);
            this.flowButtons.Name = "flowButtons";
            this.flowButtons.Padding = new System.Windows.Forms.Padding(0, 8, 0, 10);
            this.flowButtons.Size = new System.Drawing.Size(260, 52);
            this.flowButtons.TabIndex = 21;
            this.flowButtons.WrapContents = false;
            this.flowButtons.Paint += new System.Windows.Forms.PaintEventHandler(this.flowButtons_Paint);
            // 
            // panelHistory
            // 
            this.panelHistory.BackColor = Theme.Panel;
            this.panelHistory.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelHistory.Controls.Add(this.lstHistory);
            this.panelHistory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelHistory.Location = new System.Drawing.Point(0, 0);
            this.panelHistory.Margin = new System.Windows.Forms.Padding(0);
            this.panelHistory.Name = "panelHistory";
            this.panelHistory.Padding = new System.Windows.Forms.Padding(8, 8, 8, 8);
            this.panelHistory.Size = new System.Drawing.Size(260, 168);
            // 
            // panelExpression
            // 
            this.panelExpression.BackColor = Theme.Panel;
            this.panelExpression.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelExpression.Controls.Add(this.txtDisplay);
            this.panelExpression.Controls.Add(this.flowButtons);
            this.panelExpression.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelExpression.Location = new System.Drawing.Point(0, 0);
            this.panelExpression.Margin = new System.Windows.Forms.Padding(0);
            this.panelExpression.Name = "panelExpression";
            this.panelExpression.Padding = new System.Windows.Forms.Padding(8, 8, 8, 8);
            this.panelExpression.Size = new System.Drawing.Size(260, 168);
            // 
            // splitMain
            // Thanh chia mỏng hơn (6px thay vì 10px) và cùng màu nền form nên
            // trông như một khe hở tinh tế thay vì một thanh xám thô.
            this.splitMain.BackColor = Theme.Background;
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.FixedPanel = System.Windows.Forms.FixedPanel.None;
            this.splitMain.Location = new System.Drawing.Point(0, 0);
            this.splitMain.Name = "splitMain";
            this.splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitMain.Panel1.Controls.Add(this.panelHistory);
            this.splitMain.Panel1MinSize = 90;
            this.splitMain.Panel2.Controls.Add(this.panelExpression);
            this.splitMain.Panel2MinSize = 90;
            this.splitMain.Size = new System.Drawing.Size(284, 420);
            this.splitMain.SplitterDistance = 190;
            this.splitMain.SplitterWidth = 6;
            this.splitMain.TabIndex = 22;
            // 
            // QuickCalculatorForm
            // 
            this.AcceptButton = null;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = Theme.Background;
            this.ClientSize = new System.Drawing.Size(284, 420);
            this.Controls.Add(this.splitMain);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ForeColor = Theme.TextPrimary;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.MinimumSize = new System.Drawing.Size(280, 360);
            this.Name = "QuickCalculatorForm";
            this.Padding = new System.Windows.Forms.Padding(8);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "DXCALC";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtDisplay;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.Button btnDim;
        private System.Windows.Forms.ListBox lstHistory;
        private System.Windows.Forms.Button btnClearHistory;
        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.Panel panelHistory;
        private System.Windows.Forms.Panel panelExpression;
        private System.Windows.Forms.FlowLayoutPanel flowButtons;
    }
}