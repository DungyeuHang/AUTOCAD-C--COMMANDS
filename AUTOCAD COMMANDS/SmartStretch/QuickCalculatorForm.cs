using System;
using System.Drawing;
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
                button.BackColor = Color.FromArgb(98, 98, 98);
                button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(98, 98, 98);
            }
        }

        private void btnAccent_MouseLeave(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                button.BackColor = Color.FromArgb(74, 74, 74);
                button.FlatAppearance.BorderColor = Color.FromArgb(74, 74, 74);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(98, 98, 98);
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
                txtDisplay.SelectAll();
                txtDisplay.Focus();

                QuickCalculatorState.SetLastValue(result);

                string historyEntry = $"{expression} = {resultString}";
                lstHistory.Items.Insert(0, historyEntry);
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
                InsertTextToDisplay(measurement.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        private void lstHistory_DoubleClick(object sender, EventArgs e)
        {
            if (lstHistory.SelectedItem == null)
            {
                return;
            }

            string selectedItem = lstHistory.SelectedItem.ToString();
            // "2 + 2 = 4" -> put the old result 4 into the current input.
            int separatorIndex = selectedItem.LastIndexOf('=');
            string currentValue = separatorIndex >= 0
                ? selectedItem.Substring(separatorIndex + 1).Trim()
                : selectedItem.Trim();

            txtDisplay.Text = currentValue;
            _isResultShown = true;
            txtDisplay.SelectAll();
            txtDisplay.Focus();
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
