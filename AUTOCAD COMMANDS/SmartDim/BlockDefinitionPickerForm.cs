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

    internal sealed class BlockDefinitionPickerForm : WF.Form
    {
        private readonly List<BlockDefinitionChoice> _allBlocks;
        private readonly WF.TextBox _searchBox;
        private readonly WF.ListBox _listBox;
        private readonly WF.Label _countLabel;
        private readonly WF.Button _okButton;

        public BlockDefinitionPickerForm(IEnumerable<BlockDefinitionChoice> blocks)
        {
            _allBlocks = blocks?
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<BlockDefinitionChoice>();

            Text = "Chon Block Nguon";
            StartPosition = WF.FormStartPosition.CenterParent;
            MinimumSize = new Size(420, 520);
            Size = new Size(460, 580);
            FormBorderStyle = WF.FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new WF.Padding(10)
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            Controls.Add(layout);

            WF.Label searchLabel = new WF.Label
            {
                Text = "Tim block:",
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 6)
            };
            layout.Controls.Add(searchLabel, 0, 0);

            _searchBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            _searchBox.TextChanged += (_, __) => ApplyFilter();
            layout.Controls.Add(_searchBox, 0, 1);

            _listBox = new WF.ListBox
            {
                Dock = WF.DockStyle.Fill,
                IntegralHeight = false
            };
            _listBox.SelectedIndexChanged += (_, __) => UpdateSelectionState();
            _listBox.DoubleClick += (_, __) => ConfirmSelection();
            layout.Controls.Add(_listBox, 0, 2);

            WF.FlowLayoutPanel footer = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                FlowDirection = WF.FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(footer, 0, 3);

            _okButton = new WF.Button
            {
                Text = "OK",
                AutoSize = true,
                Enabled = false
            };
            _okButton.Click += (_, __) => ConfirmSelection();
            footer.Controls.Add(_okButton);

            WF.Button cancelButton = new WF.Button
            {
                Text = "Cancel",
                AutoSize = true,
                DialogResult = WF.DialogResult.Cancel
            };
            footer.Controls.Add(cancelButton);

            _countLabel = new WF.Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new WF.Padding(0, 8, 12, 0)
            };
            footer.Controls.Add(_countLabel);

            AcceptButton = _okButton;
            CancelButton = cancelButton;

            ApplyFilter();
        }

        public ObjectId SelectedBlockId =>
            _listBox.SelectedItem is BlockDefinitionChoice choice
                ? choice.Id
                : ObjectId.Null;

        private void ApplyFilter()
        {
            string keyword = (_searchBox.Text ?? string.Empty).Trim();
            IEnumerable<BlockDefinitionChoice> filtered = _allBlocks;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                filtered = filtered.Where(item =>
                    item.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            List<BlockDefinitionChoice> items = filtered.ToList();
            _listBox.BeginUpdate();
            _listBox.Items.Clear();
            foreach (BlockDefinitionChoice item in items)
            {
                _listBox.Items.Add(item);
            }
            _listBox.EndUpdate();

            if (_listBox.Items.Count > 0)
            {
                _listBox.SelectedIndex = 0;
            }

            _countLabel.Text = $"{items.Count} block";
            UpdateSelectionState();
        }

        private void UpdateSelectionState()
        {
            _okButton.Enabled = _listBox.SelectedItem is BlockDefinitionChoice;
        }

        private void ConfirmSelection()
        {
            if (!(_listBox.SelectedItem is BlockDefinitionChoice))
            {
                return;
            }

            DialogResult = WF.DialogResult.OK;
            Close();
        }
    }
}
