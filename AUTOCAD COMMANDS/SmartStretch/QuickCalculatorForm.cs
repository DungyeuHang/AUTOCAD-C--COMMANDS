using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using AcAp = Autodesk.AutoCAD.ApplicationServices;

namespace AUTOCAD_COMMANDS
{
    public partial class QuickCalculatorForm : Form
    {
        private bool _isResultShown;
        private bool _allowClose;

        public QuickCalculatorForm()
        {
            InitializeComponent();
            QuickCalculatorState.RegisterForm(this);
        }

        private void InsertTextToDisplay(string text)
        {
            if (_isResultShown)
            {
                txtDisplay.Text = string.Empty;
                _isResultShown = false;
            }

            int selectionStart = txtDisplay.SelectionStart;
            int selectionLength = txtDisplay.SelectionLength;
            txtDisplay.Text = txtDisplay.Text.Remove(selectionStart, selectionLength);
            txtDisplay.Text = txtDisplay.Text.Insert(selectionStart, text);
            txtDisplay.SelectionStart = selectionStart + text.Length;
            txtDisplay.SelectionLength = 0;
            txtDisplay.Focus();
        }

        private void btnNumber_Click(object sender, EventArgs e)
        {
            Button button = (Button)sender;
            InsertTextToDisplay(button.Text);
        }

        private void btnAccent_MouseEnter(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                if (button == btnDim)
                {
                    button.BackColor = Color.FromArgb(80, 170, 255);
                    button.FlatAppearance.BorderColor = Color.FromArgb(80, 170, 255);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 170, 255);
                }
                else
                {
                    button.BackColor = Theme.ButtonHoverBg;
                    button.FlatAppearance.BorderColor = Theme.ButtonHoverBg;
                    button.FlatAppearance.MouseOverBackColor = Theme.ButtonHoverBg;
                }
            }
        }

        private void btnAccent_MouseLeave(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                if (button == btnDim)
                {
                    button.BackColor = Theme.Accent;
                    button.FlatAppearance.BorderColor = Theme.Accent;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 170, 255);
                }
                else
                {
                    button.BackColor = Theme.ButtonBg;
                    button.FlatAppearance.BorderColor = Theme.ButtonBg;
                    button.FlatAppearance.MouseOverBackColor = Theme.ButtonHoverBg;
                }
            }
        }

        private void flowButtons_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw a subtle drop shadow behind each button so they appear elevated.
            foreach (Control ctl in flowButtons.Controls)
            {
                if (ctl is Button btn)
                {
                    Rectangle rect = btn.Bounds;
                    Rectangle shadowRect = new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height);
                    using (var brush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                    {
                        g.FillRectangle(brush, shadowRect);
                    }
                }
            }
        }

        private void txtDisplay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnEquals_Click(this, EventArgs.Empty);
            }
        }

        private void btnOperator_Click(object sender, EventArgs e)
        {
            Button button = (Button)sender;
            // After '=', an operator continues from the result. A number or DIM
            // starts a new expression and is handled by InsertTextToDisplay.
            if (_isResultShown)
            {
                txtDisplay.SelectionStart = txtDisplay.TextLength;
                txtDisplay.SelectionLength = 0;
                _isResultShown = false;
            }

            InsertTextToDisplay($" {button.Text} ");
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtDisplay.Text = string.Empty;
            _isResultShown = false;
            QuickCalculatorState.ClearLastValue();
        }

        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            lstHistory.Items.Clear();
        }

        private void btnEquals_Click(object sender, EventArgs e)
        {
            string expression = txtDisplay.Text;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return;
            }

            try
            {
                double result = ExpressionEvaluator.Evaluate(expression);
                string resultString = result.ToString("0.###############", CultureInfo.InvariantCulture);

                txtDisplay.Text = resultString;
                _isResultShown = true;
                txtDisplay.SelectionStart = txtDisplay.Text.Length;
                txtDisplay.SelectionLength = 0;
                txtDisplay.Focus();

                QuickCalculatorState.SetLastValue(result);

                string historyEntry = $"{expression} = {resultString}";
                lstHistory.Items.Add(historyEntry);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Calculation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnDim_Click(object sender, EventArgs e)
        {
            var doc = AcAp.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("No active document.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            double? measurement = null;
            btnDim.Enabled = false;
            try
            {
                // Hide the form so the user can interact with AutoCAD
                this.Hide();

                // Execute the prompt in AutoCAD's command context
                await AcAp.Application.DocumentManager.ExecuteInCommandContextAsync(obj =>
                {
                    if (DimensionPrompt.TryPromptDimensionMeasurement(doc.Editor, doc.Database, "\nChọn DIM để lấy giá trị: ", out double value, allowZero: true))
                    {
                        measurement = value;
                    }
                    return Task.CompletedTask;
                }, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error picking dimension: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Show the form again, regardless of outcome
                btnDim.Enabled = true;
                this.Show();
                this.Activate();
            }

            if (measurement.HasValue)
            {
                string measurementString = measurement.Value.ToString("0.###", CultureInfo.InvariantCulture);

                // Preserve existing input and insert the measurement at the current caret position.
                // Avoid calling InsertTextToDisplay here because that method clears the display when
                // `_isResultShown` is true. We want to keep whatever the user has already typed
                // (e.g. "500-(") and append/insert the DIM value into that expression.
                int selectionStart = txtDisplay.SelectionStart;
                int selectionLength = txtDisplay.SelectionLength;

                string current = txtDisplay.Text ?? string.Empty;
                if (selectionStart < 0) selectionStart = current.Length;
                if (selectionStart > current.Length) selectionStart = current.Length;

                string newText = current.Remove(selectionStart, selectionLength).Insert(selectionStart, measurementString);
                txtDisplay.Text = newText;
                txtDisplay.SelectionStart = selectionStart + measurementString.Length;
                txtDisplay.SelectionLength = 0;
                txtDisplay.Focus();

                // We're now in edit mode (not just showing a result)
                _isResultShown = false;
            }
        }

        private void lstHistory_DoubleClick(object sender, EventArgs e)
        {
            if (lstHistory.SelectedItem == null)
            {
                return;
            }

            string selectedItem = lstHistory.SelectedItem.ToString();
            // Behavior:
            // - By default (double-click), insert the full expression (left side of '=')
            //   so the user can edit it (e.g. correct mistakes).
            // - If the user holds Ctrl while double-clicking, insert only the result
            //   (right side of '=') for quick reuse. This avoids breaking the
            //   previous quick-result workflow while giving an edit path.

            int separatorIndex = selectedItem.LastIndexOf('=');
            string left = separatorIndex >= 0
                ? selectedItem.Substring(0, separatorIndex).Trim()
                : selectedItem.Trim();
            string right = separatorIndex >= 0
                ? selectedItem.Substring(separatorIndex + 1).Trim()
                : string.Empty;

            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;

            if (ctrl && right.Length > 0)
            {
                // Insert only the result (old behavior)
                txtDisplay.Text = right;
                _isResultShown = true;
                txtDisplay.SelectionStart = txtDisplay.Text.Length;
                txtDisplay.SelectionLength = 0;
                txtDisplay.Focus();
            }
            else
            {
                // Insert the full expression for editing; preserve result state
                // by switching to edit mode.
                txtDisplay.Text = left;
                _isResultShown = false;
                txtDisplay.SelectionStart = txtDisplay.Text.Length;
                txtDisplay.SelectionLength = 0;
                txtDisplay.Focus();
            }
        }

        internal bool TryGetCurrentDisplayValue(out double value)
        {
            string currentText = txtDisplay.Text == null
                ? string.Empty
                : txtDisplay.Text.Trim();

            if (currentText.Length == 0)
            {
                value = 0.0;
                return false;
            }

            string normalizedNumber = currentText.Replace(',', '.');
            if (double.TryParse(
                normalizedNumber,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) &&
                !double.IsNaN(value) &&
                !double.IsInfinity(value))
            {
                return true;
            }

            try
            {
                value = ExpressionEvaluator.Evaluate(currentText);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                value = 0.0;
                return false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // We are hiding the form instead of closing it to keep it modeless
            // The actual closing is handled by the IExtensionApplication.Terminate
            if (e.CloseReason == CloseReason.UserClosing && !_allowClose)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        public void CloseForShutdown()
        {
            QuickCalculatorState.UnregisterForm(this);
            _allowClose = true;
            Close();
        }
    }
}
