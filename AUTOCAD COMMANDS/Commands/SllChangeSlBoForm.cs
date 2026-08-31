using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;


namespace AUTOCAD_COMMANDS
{
    // Bảng nhập cho SLL_CHANGE_SL_BO: số bộ gốc/mới và cấu trúc SL hiện tại/mong muốn,
    // thay cho việc hỏi lần lượt từng dòng trên command line.
    internal sealed class SllChangeSlBoForm : WF.Form
    {
        private readonly WF.TextBox _originalBundlesBox;
        private readonly WF.TextBox _newBundlesBox;
        private readonly WF.ComboBox _currentPatternBox;
        private readonly WF.ComboBox _desiredPatternBox;

        public SllChangeSlBoForm(IEnumerable<string> recentFormats, string defaultPattern)
        {
            List<string> formats = recentFormats?.ToList() ?? new List<string>();

            Text = "SLL_CHANGE_SL_BO - Đổi số lượng SL";
            StartPosition = WF.FormStartPosition.CenterParent;
            MinimumSize = new Size(460, 300);
            Size = new Size(500, 320);
            FormBorderStyle = WF.FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            WF.TableLayoutPanel root = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new WF.Padding(12)
            };
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            root.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            Controls.Add(root);

            WF.TableLayoutPanel fields = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                ColumnCount = 2,
                RowCount = 4,
                AutoSize = true
            };
            fields.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            fields.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++)
            {
                fields.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            }
            root.Controls.Add(fields, 0, 0);

            _originalBundlesBox = new WF.TextBox { Dock = WF.DockStyle.Fill, Text = "1" };
            AddFieldRow(fields, 0, "Số bộ gốc:", _originalBundlesBox);

            _newBundlesBox = new WF.TextBox { Dock = WF.DockStyle.Fill, Text = "1" };
            AddFieldRow(fields, 1, "Số bộ mới:", _newBundlesBox);

            _currentPatternBox = CreatePatternComboBox(formats, defaultPattern);
            AddFieldRow(fields, 2, "Cấu trúc SL hiện tại:", _currentPatternBox);

            _desiredPatternBox = CreatePatternComboBox(formats, defaultPattern);
            AddFieldRow(fields, 3, "Cấu trúc SL mong muốn:", _desiredPatternBox);

            WF.Label hintLabel = new WF.Label
            {
                Text = "Dùng {X} làm placeholder cho số lượng. Ví dụ: \"SL: {X}\", \"SL{X}\", \"(SL: {X})\".",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Dock = WF.DockStyle.Top,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            root.Controls.Add(hintLabel, 0, 1);

            WF.FlowLayoutPanel footer = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Bottom,
                FlowDirection = WF.FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 10, 0, 0)
            };
            root.Controls.Add(footer, 0, 2);

            WF.Button okButton = new WF.Button
            {
                Text = "OK",
                AutoSize = true
            };
            okButton.Click += (_, __) => ConfirmSelection();
            footer.Controls.Add(okButton);

            WF.Button cancelButton = new WF.Button
            {
                Text = "Cancel",
                AutoSize = true,
                DialogResult = WF.DialogResult.Cancel
            };
            footer.Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public int OriginalBundles { get; private set; }

        public int NewBundles { get; private set; }

        public string CurrentPattern { get; private set; }

        public string DesiredPattern { get; private set; }

        private static WF.ComboBox CreatePatternComboBox(List<string> formats, string defaultPattern)
        {
            WF.ComboBox comboBox = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDown,
                AutoCompleteMode = WF.AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = WF.AutoCompleteSource.ListItems
            };

            foreach (string format in formats)
            {
                comboBox.Items.Add(format);
            }

            comboBox.Text = defaultPattern;
            return comboBox;
        }

        private static void AddFieldRow(WF.TableLayoutPanel layout, int rowIndex, string labelText, WF.Control control)
        {
            WF.Label label = new WF.Label
            {
                Text = labelText,
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new WF.Padding(0, 6, 8, 0)
            };
            control.Margin = new WF.Padding(0, 2, 0, 4);
            layout.Controls.Add(label, 0, rowIndex);
            layout.Controls.Add(control, 1, rowIndex);
        }

        private void ConfirmSelection()
        {
            if (!TryParsePositiveInt(_originalBundlesBox.Text, out int originalBundles))
            {
                ShowValidationError("Số bộ gốc phải là số nguyên dương.");
                return;
            }

            if (!TryParsePositiveInt(_newBundlesBox.Text, out int newBundles))
            {
                ShowValidationError("Số bộ mới phải là số nguyên dương.");
                return;
            }

            string currentPattern = (_currentPatternBox.Text ?? string.Empty).Trim();
            if (!SllChangeSlBoCommands.TryValidatePattern(currentPattern, out string currentError))
            {
                ShowValidationError("Cấu trúc SL hiện tại: " + currentError);
                return;
            }

            string desiredPattern = (_desiredPatternBox.Text ?? string.Empty).Trim();
            if (!SllChangeSlBoCommands.TryValidatePattern(desiredPattern, out string desiredError))
            {
                ShowValidationError("Cấu trúc SL mong muốn: " + desiredError);
                return;
            }

            OriginalBundles = originalBundles;
            NewBundles = newBundles;
            CurrentPattern = currentPattern;
            DesiredPattern = desiredPattern;

            DialogResult = WF.DialogResult.OK;
            Close();
        }

        private static bool TryParsePositiveInt(string text, out int value)
        {
            return int.TryParse(
                       (text ?? string.Empty).Trim(),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out value)
                   && value > 0;
        }

        private void ShowValidationError(string message)
        {
            WF.MessageBox.Show(this, message, "SLL_CHANGE_SL_BO", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
        }
    }
}
