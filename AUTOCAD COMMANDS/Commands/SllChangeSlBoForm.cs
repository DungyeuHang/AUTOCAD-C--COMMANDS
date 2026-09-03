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
    // Bảng nhập cho SLL_CHANGE_SL_BO: số bộ gốc/mới, NHIỀU cấu trúc SL đầu vào (mỗi dòng
    // có nút "-" để xoá, luôn giữ tối thiểu 1 dòng) và MỘT cấu trúc SL đầu ra.
    // Toàn bộ layout dùng TableLayoutPanel lồng nhau (giống SdxySettingsForm/
    // BlockDefinitionPickerForm) để auto-size ổn định, không dùng GroupBox.AutoSize
    // (có hạn chế khi kết hợp với control Dock=Fill bên trong).
    internal sealed class SllChangeSlBoForm : WF.Form
    {
        private readonly List<string> _recentFormats;
        private readonly string _defaultFormat;
        private readonly List<string> _inputFormatValues = new List<string>();
        private readonly WF.TableLayoutPanel _inputFormatsLayout;
        private readonly WF.TextBox _originalBundlesBox;
        private readonly WF.TextBox _newBundlesBox;
        private readonly WF.ComboBox _outputFormatBox;

        public SllChangeSlBoForm(IEnumerable<string> recentFormats, string defaultFormat)
        {
            _recentFormats = recentFormats?.ToList() ?? new List<string>();
            _defaultFormat = string.IsNullOrEmpty(defaultFormat)
                ? SllChangeSlBoCommands.DefaultFormatPattern
                : defaultFormat;
            _inputFormatValues.Add(_defaultFormat);

            Text = "SLL_CHANGE_SL_BO - Đổi số lượng SL";
            StartPosition = WF.FormStartPosition.CenterParent;
            MinimumSize = new Size(480, 420);
            Size = new Size(520, 480);
            FormBorderStyle = WF.FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            WF.Panel scrollHost = new WF.Panel
            {
                Dock = WF.DockStyle.Fill,
                AutoScroll = true,
                Padding = new WF.Padding(14, 12, 14, 12)
            };

            WF.TableLayoutPanel content = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = WF.AutoSizeMode.GrowAndShrink
            };
            content.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            scrollHost.Controls.Add(content);

            int row = 0;

            WF.Label hintLabel = new WF.Label
            {
                Text = "Dùng {X} làm placeholder cho số lượng trong mọi cấu trúc, vd: \"SL: {X}\", \"SL{X}\", \"(SL: {X})\".",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(440, 0),
                Margin = new WF.Padding(0, 0, 0, 12)
            };
            AddContentRow(content, ref row, hintLabel);

            AddContentRow(content, ref row, CreateSectionHeader("SỐ LƯỢNG BỘ"));
            AddContentRow(content, ref row, CreateSeparator());

            WF.TableLayoutPanel bundleFields = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = WF.AutoSizeMode.GrowAndShrink,
                Margin = new WF.Padding(0, 8, 0, 16)
            };
            bundleFields.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            bundleFields.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));

            _originalBundlesBox = new WF.TextBox { Dock = WF.DockStyle.Fill, Text = "1" };
            AddFieldRow(bundleFields, 0, "Số bộ gốc:", _originalBundlesBox);

            _newBundlesBox = new WF.TextBox { Dock = WF.DockStyle.Fill, Text = "1" };
            AddFieldRow(bundleFields, 1, "Số bộ mới:", _newBundlesBox);

            AddContentRow(content, ref row, bundleFields);

            AddContentRow(content, ref row, CreateSectionHeader("CẤU TRÚC SL ĐẦU VÀO (có thể thêm nhiều)"));
            AddContentRow(content, ref row, CreateSeparator());

            _inputFormatsLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = WF.AutoSizeMode.GrowAndShrink,
                Margin = new WF.Padding(0, 8, 0, 4)
            };
            _inputFormatsLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            _inputFormatsLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            AddContentRow(content, ref row, _inputFormatsLayout);

            WF.Button addFormatButton = new WF.Button
            {
                Text = "+ Thêm cấu trúc",
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 16)
            };
            addFormatButton.Click += (_, __) => AddInputFormatRow();
            AddContentRow(content, ref row, addFormatButton);

            AddContentRow(content, ref row, CreateSectionHeader("CẤU TRÚC SL ĐẦU RA (duy nhất)"));
            AddContentRow(content, ref row, CreateSeparator());

            _outputFormatBox = CreateFormatComboBox(_defaultFormat);
            _outputFormatBox.Margin = new WF.Padding(0, 8, 0, 4);
            AddContentRow(content, ref row, _outputFormatBox);

            Controls.Add(scrollHost);

            WF.Panel footerPanel = new WF.Panel
            {
                Dock = WF.DockStyle.Bottom,
                Height = 54,
                Padding = new WF.Padding(14, 10, 14, 12)
            };

            WF.TableLayoutPanel footerLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            footerLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            footerLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));

            WF.Button cancelButton = new WF.Button
            {
                Text = "Cancel",
                AutoSize = true,
                Height = 30,
                DialogResult = WF.DialogResult.Cancel,
                Anchor = WF.AnchorStyles.Left
            };

            WF.Button primaryButton = new WF.Button
            {
                Text = "CHỌN ĐỐI TƯỢNG",
                Dock = WF.DockStyle.Fill,
                Height = 30,
                Margin = new WF.Padding(10, 0, 0, 0),
                FlatStyle = WF.FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new System.Drawing.Font(Font, FontStyle.Bold)
            };
            primaryButton.FlatAppearance.BorderSize = 0;
            primaryButton.Click += (_, __) => ConfirmAndClose();

            footerLayout.Controls.Add(cancelButton, 0, 0);
            footerLayout.Controls.Add(primaryButton, 1, 0);
            footerPanel.Controls.Add(footerLayout);
            Controls.Add(footerPanel);

            AcceptButton = primaryButton;
            CancelButton = cancelButton;

            RebuildInputFormatRows();
        }

        public int OriginalBundles { get; private set; }

        public int NewBundles { get; private set; }

        public List<string> InputPatterns { get; private set; }

        public string OutputPattern { get; private set; }

        private static void AddContentRow(WF.TableLayoutPanel content, ref int row, WF.Control control)
        {
            content.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            content.Controls.Add(control, 0, row);
            row++;
        }

        private static WF.Label CreateSectionHeader(string text)
        {
            return new WF.Label
            {
                Text = text,
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 40)
            };
        }

        private static WF.Panel CreateSeparator()
        {
            return new WF.Panel
            {
                Dock = WF.DockStyle.Fill,
                Height = 1,
                Margin = new WF.Padding(0, 4, 0, 0),
                BackColor = Color.FromArgb(220, 220, 220)
            };
        }

        private WF.ComboBox CreateFormatComboBox(string value)
        {
            WF.ComboBox comboBox = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDown,
                AutoCompleteMode = WF.AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = WF.AutoCompleteSource.ListItems
            };

            foreach (string format in _recentFormats)
            {
                comboBox.Items.Add(format);
            }

            comboBox.Text = value ?? string.Empty;
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

        // Vẽ lại toàn bộ danh sách dòng cấu trúc đầu vào từ _inputFormatValues.
        // Đơn giản hơn nhiều so với thêm/bớt từng dòng trong TableLayoutPanel, và vì số dòng
        // thường chỉ vài dòng nên chi phí dựng lại không đáng kể.
        private void RebuildInputFormatRows()
        {
            _inputFormatsLayout.SuspendLayout();
            _inputFormatsLayout.Controls.Clear();
            _inputFormatsLayout.RowStyles.Clear();
            _inputFormatsLayout.RowCount = _inputFormatValues.Count;

            for (int i = 0; i < _inputFormatValues.Count; i++)
            {
                int rowIndex = i;
                _inputFormatsLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));

                WF.ComboBox combo = CreateFormatComboBox(_inputFormatValues[rowIndex]);
                combo.Margin = new WF.Padding(0, 0, 6, 4);
                combo.TextChanged += (_, __) => _inputFormatValues[rowIndex] = combo.Text;
                _inputFormatsLayout.Controls.Add(combo, 0, rowIndex);

                WF.Button removeButton = new WF.Button
                {
                    Text = "−",
                    Width = 32,
                    Height = 23,
                    Margin = new WF.Padding(0, 0, 0, 4),
                    Enabled = _inputFormatValues.Count > 1
                };
                removeButton.Click += (_, __) => RemoveInputFormatRow(rowIndex);
                _inputFormatsLayout.Controls.Add(removeButton, 1, rowIndex);
            }

            _inputFormatsLayout.ResumeLayout(true);
        }

        private void AddInputFormatRow()
        {
            string suggestion = _recentFormats.Count > 0 ? _recentFormats[0] : _defaultFormat;
            _inputFormatValues.Add(suggestion);
            RebuildInputFormatRows();
        }

        private void RemoveInputFormatRow(int index)
        {
            if (_inputFormatValues.Count <= 1)
            {
                return;
            }

            _inputFormatValues.RemoveAt(index);
            RebuildInputFormatRows();
        }

        private void ConfirmAndClose()
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

            List<string> inputPatterns = new List<string>();
            for (int i = 0; i < _inputFormatValues.Count; i++)
            {
                string pattern = (_inputFormatValues[i] ?? string.Empty).Trim();
                if (pattern.Length == 0)
                {
                    ShowValidationError($"Cấu trúc SL đầu vào #{i + 1} không được để trống.");
                    return;
                }

                if (!SllChangeSlBoCommands.TryValidatePattern(pattern, out string inputError))
                {
                    ShowValidationError($"Cấu trúc SL đầu vào #{i + 1}: {inputError}");
                    return;
                }

                // Trùng cấu trúc: chỉ giữ 1 lần, không báo lỗi (xử lý gọn theo yêu cầu).
                if (!inputPatterns.Contains(pattern, StringComparer.Ordinal))
                {
                    inputPatterns.Add(pattern);
                }
            }

            string outputPattern = (_outputFormatBox.Text ?? string.Empty).Trim();
            if (!SllChangeSlBoCommands.TryValidatePattern(outputPattern, out string outputError))
            {
                ShowValidationError("Cấu trúc SL đầu ra: " + outputError);
                return;
            }

            OriginalBundles = originalBundles;
            NewBundles = newBundles;
            InputPatterns = inputPatterns;
            OutputPattern = outputPattern;

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
