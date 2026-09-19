using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Hieu chuan theo mieng chan thu: nhap chieu dai phoi phang va kich thuoc hai canh sau khi
    /// chan, tool giai nguoc ra BD / BA / K-factor thuc te cua may chan.
    /// </summary>
    public class FoilCalibrationForm : Form
    {
        private NumericUpDown _numThickness;
        private NumericUpDown _numRadius;
        private NumericUpDown _numAngle;
        private NumericUpDown _numFlatLength;
        private NumericUpDown _numFlange1;
        private NumericUpDown _numFlange2;
        private TextBox _txtResult;
        private Button _btnSaveToTable;

        private readonly FoilSettings _settings;

        public FoilCalibrationResult Result { get; private set; }

        public bool HasResult { get { return Result != null && Result.Success; } }

        public FoilCalibrationForm(FoilSettings settings)
        {
            _settings = settings ?? new FoilSettings();
            InitializeComponent();

            _numThickness.Value = Clamp(_settings.Thickness, _numThickness);
            _numRadius.Value = Clamp(_settings.InsideRadius, _numRadius);
            _numAngle.Value = 90m;
            _numFlatLength.Value = 47.88m;
            _numFlange1.Value = 20m;
            _numFlange2.Value = 30m;

            Recalculate();
        }

        private void InitializeComponent()
        {
            Text = "DX_FOIL - HIEU CHUAN THEO MAY CHAN THUC TE";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 520);
            Size = new Size(580, 560);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            Controls.Add(main);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            main.Controls.Add(layout);

            Label hint = new Label
            {
                Text = "Cat mot mieng phoi co chieu dai DA BIET, chan mot lan, roi do lai hai canh\n" +
                       "theo MOLD LINE (keo dai hai mat ngoai den khi cat nhau tai dinh goc nhon).",
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 102, 204),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            };
            layout.Controls.Add(hint, 0, 0);

            GroupBox grpInput = new GroupBox
            {
                Text = "SO LIEU DO DUOC",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _numThickness = CreateNumeric(0.001m, 1000m, 4);
            _numRadius = CreateNumeric(0m, 10000m, 4);
            _numAngle = CreateNumeric(0.1m, 179.9m, 4);
            _numFlatLength = CreateNumeric(0.001m, 1000000m, 4);
            _numFlange1 = CreateNumeric(0.001m, 1000000m, 4);
            _numFlange2 = CreateNumeric(0.001m, 1000000m, 4);

            AddField(grid, 0, "Chieu day T:", _numThickness, "mm");
            AddField(grid, 1, "Ban kinh trong R:", _numRadius, "mm");
            AddField(grid, 2, "Goc chan alpha:", _numAngle, "do  (chan vuong goc = 90)");
            AddField(grid, 3, "Chieu dai phoi phang:", _numFlatLength, "mm  (truoc khi chan)");
            AddField(grid, 4, "Canh 1 sau khi chan:", _numFlange1, "mm  (theo mold line)");
            AddField(grid, 5, "Canh 2 sau khi chan:", _numFlange2, "mm  (theo mold line)");

            _numThickness.ValueChanged += (s, e) => Recalculate();
            _numRadius.ValueChanged += (s, e) => Recalculate();
            _numAngle.ValueChanged += (s, e) => Recalculate();
            _numFlatLength.ValueChanged += (s, e) => Recalculate();
            _numFlange1.ValueChanged += (s, e) => Recalculate();
            _numFlange2.ValueChanged += (s, e) => Recalculate();

            grpInput.Controls.Add(grid);
            layout.Controls.Add(grpInput, 0, 1);

            GroupBox grpResult = new GroupBox
            {
                Text = "KET QUA GIAI NGUOC",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 16, 8, 8),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _txtResult = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                BackColor = Color.White
            };
            grpResult.Controls.Add(_txtResult);
            layout.Controls.Add(grpResult, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };

            Button btnClose = new Button
            {
                Text = "Dong",
                DialogResult = DialogResult.Cancel,
                Width = 110,
                Height = 32
            };

            Button btnApply = new Button
            {
                Text = "Dung K-Factor nay",
                DialogResult = DialogResult.OK,
                Width = 160,
                Height = 32,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _btnSaveToTable = new Button { Text = "Ghi vao bang chan", Width = 150, Height = 32 };
            _btnSaveToTable.Click += OnSaveToTableClick;

            buttons.Controls.Add(btnApply);
            buttons.Controls.Add(btnClose);
            buttons.Controls.Add(_btnSaveToTable);
            Controls.Add(buttons);

            AcceptButton = btnApply;
            CancelButton = btnClose;
        }

        private void Recalculate()
        {
            Result = FoilCalibration.Solve(new FoilCalibrationInput
            {
                Thickness = (double)_numThickness.Value,
                InsideRadius = (double)_numRadius.Value,
                BendAngleDeg = (double)_numAngle.Value,
                FlatLength = (double)_numFlatLength.Value,
                Flange1 = (double)_numFlange1.Value,
                Flange2 = (double)_numFlange2.Value
            });

            _txtResult.Text = Result.FormatReport(4).Replace("\n", Environment.NewLine);
            _btnSaveToTable.Enabled = Result.Success;
        }

        private void OnSaveToTableClick(object sender, EventArgs e)
        {
            if (Result == null || !Result.Success)
            {
                return;
            }

            string path = string.IsNullOrWhiteSpace(_settings.BendTablePath)
                ? FoilSettingsStore.GetDefaultBendTablePath()
                : _settings.BendTablePath;

            string error;
            FoilBendTable table = FoilBendTable.LoadFromFile(path, out error) ?? new FoilBendTable();

            table.Add(new FoilBendTableEntry
            {
                Thickness = (double)_numThickness.Value,
                Radius = (double)_numRadius.Value,
                AngleDeg = (double)_numAngle.Value,
                BendDeduction = Math.Round(Result.BendDeduction, 4),
                BendAllowance = Math.Round(Result.BendAllowance, 4)
            });

            if (table.SaveToFile(path, out error))
            {
                MessageBox.Show(
                    this,
                    "Da ghi ket qua hieu chuan vao bang chan:" + Environment.NewLine + path,
                    "DX_FOIL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, error, "DX_FOIL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static NumericUpDown CreateNumeric(decimal min, decimal max, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = 0.1m,
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Right,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
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

        private static decimal Clamp(double value, NumericUpDown target)
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
