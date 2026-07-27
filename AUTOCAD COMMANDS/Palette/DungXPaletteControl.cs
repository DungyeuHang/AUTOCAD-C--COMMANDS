﻿using Autodesk.AutoCAD.ApplicationServices;
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

    // ======================================================
    // DXPALETTE UI
    // Bảng command chính:
    // - Filter theo Source/Type/Search.
    // - Favorite, sort Custom/A-Z/Used.
    // - Đếm số lần dùng command.
    // - Lưu width cột, layout, favorite, usage.
    // Lưu ý khi sửa UI: cố gắng chỉ sửa render/event UI, tránh đụng store nếu không cần.
    // ======================================================
    internal sealed class DungXPaletteControl : WF.UserControl
    {
        private static readonly Color BackgroundColor = Color.FromArgb(59, 68, 83);
        private static readonly Color PanelColor = Color.FromArgb(59, 68, 83);
        private static readonly Color BorderColor = Color.FromArgb(80, 90, 105);
        private static readonly Color ForegroundColor = Color.FromArgb(241, 241, 241);
        private static readonly Color AccentColor = Color.FromArgb(120, 130, 145);
        private static readonly Color SelectionColor = Color.FromArgb(46, 52, 64);
        private static readonly Color CardColor = Color.FromArgb(46, 52, 64);
        private static readonly Color CardBorderColor = Color.FromArgb(96, 107, 133);
        private static readonly Color CardShadowColor = Color.FromArgb(34, 41, 51);
        private static readonly Color HeaderAccentColor = Color.FromArgb(96, 107, 133);
        private static readonly Color MutedBadgeColor = Color.FromArgb(80, 90, 105);
        private static readonly Color FavoriteOnColor = Color.FromArgb(255, 204, 64);
        private static readonly Color FavoriteOffColor = Color.FromArgb(112, 112, 112);
        private static readonly Color CommandButtonNormalBgColor = Color.FromArgb(40, 46, 58);
        private static readonly Color CommandButtonHoverBgColor = Color.FromArgb(80, 90, 112);
        private static readonly Color CommandButtonShadowColor = Color.Black;

        private readonly WF.TextBox _searchBox;
        private readonly WF.TableLayoutPanel _filterPanel;
        private readonly WF.FlowLayoutPanel _buttonPanel;
        private readonly WF.Label _sourceLabel;
        private readonly WF.Label _typeLabel;
        private readonly WF.Label _sortLabel;
        private readonly WF.Label _searchLabel;
        private readonly WF.ComboBox _sourceFilter;
        private readonly WF.ComboBox _typeFilter;
        private readonly WF.ComboBox _sortModeFilter;
        private readonly WF.DataGridView _commandGrid;
        private readonly WF.Button _reloadButton;
        private readonly WF.Button _folderButton;
        private readonly WF.Button _refreshButton;
        private readonly WF.Button _addSourceButton;
        private readonly WF.Button _addManualButton;
        private readonly WF.Button _removeSourceButton;
        private readonly WF.Button _resetUsageButton;
        private readonly WF.Label _summaryLabel;
        private readonly WF.Label _usageSummaryLabel;
        private readonly WF.Label _statusLabel;
        private readonly WF.CheckBox _autoShowCheckBox;
        private List<PaletteCommandItem> _items;
        private Point _dragStartPoint;
        private int _dragRowIndex = -1;
        private bool _isApplyingColumnWidths;
        private int _hoveredCommandRowIndex = -1;
        private int _pressedCommandRowIndex = -1;

        public DungXPaletteControl()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            Dock = WF.DockStyle.Fill;
            BackColor = BackgroundColor;
            ForeColor = ForegroundColor;
            Font = new System.Drawing.Font(
                "Segoe UI",
                9F,
                FontStyle.Regular,
                GraphicsUnit.Point);

            PaletteChromePanel chromePanel = new PaletteChromePanel
            {
                Dock = WF.DockStyle.Fill,
                Padding = new WF.Padding(12),
                Margin = new WF.Padding(0),
                BackColor = BackgroundColor
            };
            Controls.Add(chromePanel);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new WF.Padding(4),
                BackColor = PanelColor
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            chromePanel.Controls.Add(layout);

            PaletteTitlePanel titlePanel = new PaletteTitlePanel
            {
                Dock = WF.DockStyle.Top,
                Margin = new WF.Padding(0, 0, 0, 6)
            };
            layout.Controls.Add(titlePanel, 0, 0);

            _filterPanel = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                ColumnCount = 8,
                AutoSize = true,
                BackColor = PanelColor
            };
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 140f));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            layout.Controls.Add(_filterPanel, 0, 1);

            _sourceLabel = CreateLabel("Source");
            _sourceLabel.Margin = new WF.Padding(0, 6, 8, 0);
            _filterPanel.Controls.Add(_sourceLabel, 0, 0);

            _sourceFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _sourceFilter.Items.AddRange(new object[] { "All", "DUNGX Custom", "DUNGX 2" });
            _sourceFilter.SelectedIndex = 0;
            _sourceFilter.SelectedIndexChanged += (_, __) => BindGrid();
            _filterPanel.Controls.Add(_sourceFilter, 1, 0);

            _typeLabel = CreateLabel("Type");
            _typeLabel.Margin = new WF.Padding(0, 6, 8, 0);
            _filterPanel.Controls.Add(_typeLabel, 2, 0);

            _typeFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _typeFilter.Items.AddRange(new object[]
            {
                "All",
                "LISP",
                "DLL",
                "VLX",
                "Action",
                "Manual"
            });
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectedIndexChanged += (_, __) => BindGrid();
            _filterPanel.Controls.Add(_typeFilter, 3, 0);

            _sortLabel = CreateLabel("Sort");
            _sortLabel.Margin = new WF.Padding(8, 6, 8, 0);
            _filterPanel.Controls.Add(_sortLabel, 4, 0);

            _sortModeFilter = new WF.ComboBox
            {
                Dock = WF.DockStyle.Fill,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                FlatStyle = WF.FlatStyle.Flat
            };
            _sortModeFilter.Items.AddRange(new object[]
            {
                "Custom",
                "A-Z",
                "Used"
            });
            _sortModeFilter.SelectedIndexChanged += SortModeFilter_SelectedIndexChanged;
            _filterPanel.Controls.Add(_sortModeFilter, 5, 0);

            _searchLabel = CreateLabel("Search");
            _searchLabel.Margin = new WF.Padding(8, 6, 8, 0);
            _filterPanel.Controls.Add(_searchLabel, 6, 0);

            _searchBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 0, 0, 0),
                BackColor = PanelColor,
                ForeColor = ForegroundColor,
                BorderStyle = WF.BorderStyle.FixedSingle
            };
            _searchBox.TextChanged += (_, __) => BindGrid();
            _searchBox.KeyDown += SearchBox_KeyDown;
            _filterPanel.Controls.Add(_searchBox, 7, 0);

            _buttonPanel = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new WF.Padding(0, 4, 0, 4),
                BackColor = PanelColor
            };
            layout.Controls.Add(_buttonPanel, 0, 2);

            _reloadButton = CreateButton("Reload LISP", (_, __) => ReloadLisps());
            _folderButton = CreateButton("LISP Folder", (_, __) => PickFolder());
            _addSourceButton = CreateButton("Add Source", (_, __) => AddSource());
            _addManualButton = CreateButton("Add Manual", (_, __) => AddManualAlias());
            _removeSourceButton = CreateButton("Remove Source", (_, __) => RemoveSelectedSource());
            _refreshButton = CreateButton("Refresh List", (_, __) => ReloadData(true));
            _resetUsageButton = CreateButton("Reset Stats", (_, __) => ResetUsageStats());
            _autoShowCheckBox = CreateCheckBox("Auto Open", AutoShowCheckBox_CheckedChanged);

            _buttonPanel.Controls.Add(_reloadButton);
            _buttonPanel.Controls.Add(_folderButton);
            _buttonPanel.Controls.Add(_addSourceButton);
            _buttonPanel.Controls.Add(_addManualButton);
            _buttonPanel.Controls.Add(_removeSourceButton);
            _buttonPanel.Controls.Add(_refreshButton);
            _buttonPanel.Controls.Add(_resetUsageButton);
            _buttonPanel.Controls.Add(_autoShowCheckBox);

            _summaryLabel = CreateLabel("Tong lenh: 0");
            _summaryLabel.Dock = WF.DockStyle.Fill;
            _summaryLabel.Padding = new WF.Padding(0, 2, 0, 6);
            _summaryLabel.Margin = new WF.Padding(0, 0, 0, 2);
            _summaryLabel.AutoEllipsis = true;
            _summaryLabel.ForeColor = Color.FromArgb(196, 196, 196);
            _summaryLabel.Font = new System.Drawing.Font(
                "Segoe UI",
                8.25F,
                FontStyle.Bold,
                GraphicsUnit.Point);
            layout.Controls.Add(_summaryLabel, 0, 3);

            _usageSummaryLabel = CreateLabel("Thong ke dung: chua co du lieu");
            _usageSummaryLabel.Dock = WF.DockStyle.Fill;
            _usageSummaryLabel.Padding = new WF.Padding(0, 0, 0, 6);
            _usageSummaryLabel.Margin = new WF.Padding(0, 0, 0, 2);
            _usageSummaryLabel.AutoEllipsis = true;
            _usageSummaryLabel.ForeColor = Color.FromArgb(156, 156, 156);
            _usageSummaryLabel.Font = new System.Drawing.Font(
                "Segoe UI",
                8.25F,
                FontStyle.Bold,
                GraphicsUnit.Point);
            layout.Controls.Add(_usageSummaryLabel, 0, 4);

            _commandGrid = CreateGrid();
            _commandGrid.AllowDrop = true;
            _commandGrid.CellClick += CommandGrid_CellClick;
            _commandGrid.KeyDown += CommandGrid_KeyDown;
            _commandGrid.CellEndEdit += CommandGrid_CellEndEdit;
            _commandGrid.MouseDown += CommandGrid_MouseDown;
            _commandGrid.MouseMove += CommandGrid_MouseMove;
            _commandGrid.MouseUp += CommandGrid_MouseUp;
            _commandGrid.MouseLeave += CommandGrid_MouseLeave;
            _commandGrid.DragOver += CommandGrid_DragOver;
            _commandGrid.DragDrop += CommandGrid_DragDrop;
            _commandGrid.ColumnWidthChanged += CommandGrid_ColumnWidthChanged;
            _commandGrid.CellPainting += CommandGrid_CellPainting;
            layout.Controls.Add(_commandGrid, 0, 5);
            EnableDoubleBuffer(_commandGrid);
            ApplySavedColumnWidths();

            _statusLabel = CreateLabel("San sang");
            _statusLabel.Dock = WF.DockStyle.Fill;
            _statusLabel.Padding = new WF.Padding(0, 8, 0, 0);
            _statusLabel.ForeColor = Color.FromArgb(186, 190, 198);
            layout.Controls.Add(_statusLabel, 0, 6);

            _items = new List<PaletteCommandItem>();
            Resize += (_, __) => ApplyResponsiveLayout();
            ApplyResponsiveLayout();
            SetSortMode(PaletteLayoutStore.LoadSortMode());
            _autoShowCheckBox.Checked = DungXPaletteHost.IsAutoShowEnabled();
            ReloadData(false);
        }

        public void ReloadData(bool showMessage)
        {
            string currentFilter = Convert.ToString(_sourceFilter.SelectedItem) ?? "All";
            string selectedCommand = GetSelectedCommandName();
            _items = PaletteCommandCatalog.BuildItems();
            PaletteCommandUsageTracker.SetKnownCommands(_items.Select(item => item.CommandName));
            PaletteUsageStore.ApplyUsage(_items);
            PaletteLayoutStore.ApplyLayout(_items);
            PaletteLayoutStore.SaveLayout(_items);
            RefreshSourceFilter(currentFilter);
            BindGrid(selectedCommand);

            string root = DungXLispResolver.GetDisplayRoot();
            bool ready = DungXLispResolver.TryResolveAllLispFiles(out _, out _);
            string status = ready
                ? $"Ready | LISP root: {root} | Tu dong quet command OK"
                : $"Chua thay du file LISP | Root hien tai: {root}";

            SetStatus(status);

            if (showMessage)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n" + status);
            }
        }

        public void SetStatus(string message)
        {
            _statusLabel.Text = message;
        }

        private void ApplyResponsiveLayout()
        {
            bool compact = Width <= 260;
            bool ultraCompact = Width <= 150;

            _filterPanel.SuspendLayout();
            _buttonPanel.SuspendLayout();

            _filterPanel.Controls.Clear();
            _filterPanel.ColumnStyles.Clear();
            _filterPanel.RowStyles.Clear();

            if (ultraCompact)
            {
                _sourceLabel.Visible = false;
                _typeLabel.Visible = false;
                _sortLabel.Visible = false;
                _searchLabel.Visible = false;

                _filterPanel.ColumnCount = 1;
                _filterPanel.RowCount = 4;
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));

                _sourceFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _typeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _sortModeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _searchBox.Margin = new WF.Padding(0);

                _filterPanel.Controls.Add(_sourceFilter, 0, 0);
                _filterPanel.Controls.Add(_typeFilter, 0, 1);
                _filterPanel.Controls.Add(_sortModeFilter, 0, 2);
                _filterPanel.Controls.Add(_searchBox, 0, 3);
            }
            else if (compact)
            {
                _sourceLabel.Visible = true;
                _typeLabel.Visible = true;
                _sortLabel.Visible = true;
                _searchLabel.Visible = true;

                _filterPanel.ColumnCount = 2;
                _filterPanel.RowCount = 4;
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
                _filterPanel.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));

                _sourceFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _typeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _sortModeFilter.Margin = new WF.Padding(0, 0, 0, 4);
                _searchBox.Margin = new WF.Padding(0);

                _filterPanel.Controls.Add(_sourceLabel, 0, 0);
                _filterPanel.Controls.Add(_sourceFilter, 1, 0);
                _filterPanel.Controls.Add(_typeLabel, 0, 1);
                _filterPanel.Controls.Add(_typeFilter, 1, 1);
                _filterPanel.Controls.Add(_sortLabel, 0, 2);
                _filterPanel.Controls.Add(_sortModeFilter, 1, 2);
                _filterPanel.Controls.Add(_searchLabel, 0, 3);
                _filterPanel.Controls.Add(_searchBox, 1, 3);
            }
            else
            {
                _sourceLabel.Visible = true;
                _typeLabel.Visible = true;
                _sortLabel.Visible = true;
                _searchLabel.Visible = true;

                _filterPanel.ColumnCount = 8;
                _filterPanel.RowCount = 1;
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 170f));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Absolute, 140f));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
                _filterPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));

                _sourceFilter.Margin = new WF.Padding(0);
                _typeFilter.Margin = new WF.Padding(0);
                _sortModeFilter.Margin = new WF.Padding(0);
                _searchBox.Margin = new WF.Padding(0);

                _filterPanel.Controls.Add(_sourceLabel, 0, 0);
                _filterPanel.Controls.Add(_sourceFilter, 1, 0);
                _filterPanel.Controls.Add(_typeLabel, 2, 0);
                _filterPanel.Controls.Add(_typeFilter, 3, 0);
                _filterPanel.Controls.Add(_sortLabel, 4, 0);
                _filterPanel.Controls.Add(_sortModeFilter, 5, 0);
                _filterPanel.Controls.Add(_searchLabel, 6, 0);
                _filterPanel.Controls.Add(_searchBox, 7, 0);
            }

            _buttonPanel.FlowDirection = compact
                ? WF.FlowDirection.TopDown
                : WF.FlowDirection.LeftToRight;
            _buttonPanel.WrapContents = compact;
            _buttonPanel.Visible = !compact;

            _reloadButton.Text = compact ? "LISP" : "Reload LISP";
            _folderButton.Text = compact ? "Dir" : "LISP Folder";
            _addSourceButton.Text = compact ? "+Src" : "Add Source";
            _addManualButton.Text = compact ? "+Cmd" : "Add Manual";
            _removeSourceButton.Text = compact ? "-Src" : "Remove Source";
            _refreshButton.Text = compact ? "Ref" : "Refresh List";
            _resetUsageButton.Text = compact ? "Reset" : "Reset Stats";

            _commandGrid.Columns["Favorite"].Visible = true;
            _commandGrid.Columns["Used"].Visible = true;
            _commandGrid.Columns["Description"].Visible = true;
            _commandGrid.Columns["Source"].Visible = true;
            _commandGrid.Columns["Favorite"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Command"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Used"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Description"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;
            _commandGrid.Columns["Source"].AutoSizeMode = WF.DataGridViewAutoSizeColumnMode.None;

            _statusLabel.Visible = !ultraCompact;

            _filterPanel.ResumeLayout();
            _buttonPanel.ResumeLayout();
        }

        private void BindGrid(string preferredCommandName = null)
        {
            // Rebind toàn bộ grid khi filter/sort/layout thay đổi.
            // Với usage count sau khi command chạy, code ưu tiên cập nhật nhẹ để tránh palette bị lag.
            preferredCommandName = preferredCommandName ?? GetSelectedCommandName();
            List<PaletteCommandItem> filteredItems = GetFilteredItems();
            UpdateSummary(filteredItems);

            _commandGrid.Rows.Clear();

            foreach (PaletteCommandItem item in filteredItems)
            {
                int rowIndex =
                    _commandGrid.Rows.Add(
                        item.IsFavorite ? "★" : "☆",
                        item.CommandName,
                        item.UsageCount,
                        item.Description,
                        item.SourceLabel);
                _commandGrid.Rows[rowIndex].Tag = item;
            }

            if (_commandGrid.Rows.Count > 0)
            {
                _commandGrid.ClearSelection();
                WF.DataGridViewRow rowToSelect =
                    _commandGrid.Rows
                        .Cast<WF.DataGridViewRow>()
                        .FirstOrDefault(row =>
                            string.Equals(
                                (row.Tag as PaletteCommandItem)?.CommandName,
                                preferredCommandName,
                                StringComparison.OrdinalIgnoreCase));

                if (rowToSelect != null)
                {
                    rowToSelect.Selected = true;
                    _commandGrid.CurrentCell = rowToSelect.Cells["Command"];
                }
            }
        }

        private void UpdateSummary(IReadOnlyCollection<PaletteCommandItem> filteredItems)
        {
            IReadOnlyCollection<PaletteCommandItem> allItems =
                _items ?? (IReadOnlyCollection<PaletteCommandItem>)Array.Empty<PaletteCommandItem>();
            IReadOnlyCollection<PaletteCommandItem> visibleItems =
                filteredItems ?? (IReadOnlyCollection<PaletteCommandItem>)Array.Empty<PaletteCommandItem>();
            int totalUsage = allItems
                .GroupBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .Sum(group => group.Max(item => item.UsageCount));
            int usedCommandCount = allItems
                .GroupBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .Count(group => group.Max(item => item.UsageCount) > 0);

            List<string> sourceParts = allItems
                .GroupBy(item => item.SourceLabel ?? string.Empty)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}: {group.Count()}")
                .ToList();

            string sourceSummary = sourceParts.Count == 0
                ? "Chua co source nao"
                : string.Join(" | ", sourceParts);

            _summaryLabel.Text =
                $"Tong lenh: {allItems.Count} | Dang hien: {visibleItems.Count} | Theo nguon: {sourceSummary}";
            _usageSummaryLabel.Text = totalUsage > 0
                ? $"Tong luot dung: {totalUsage} | So lenh da dung: {usedCommandCount}"
                : "Tong luot dung: 0 | So lenh da dung: 0";
        }

        private static WF.DataGridView CreateGrid()
        {
            WF.DataGridView grid = new WF.DataGridView
            {
                Dock = WF.DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = true,
                MultiSelect = false,
                SelectionMode = WF.DataGridViewSelectionMode.FullRowSelect,
                EditMode = WF.DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = PanelColor,
                BorderStyle = WF.BorderStyle.FixedSingle,
                GridColor = BorderColor,
                CellBorderStyle = WF.DataGridViewCellBorderStyle.SingleHorizontal,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ScrollBars = WF.ScrollBars.Both,
                AutoSizeColumnsMode = WF.DataGridViewAutoSizeColumnsMode.None,
                RowTemplate = { Height = 30 }
            };

            grid.ColumnHeadersHeight = 30;
            grid.ColumnHeadersHeightSizeMode = WF.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            grid.ColumnHeadersBorderStyle = WF.DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ForegroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = BackgroundColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = ForegroundColor;
            grid.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font(
                "Segoe UI",
                8.75F,
                FontStyle.Bold,
                GraphicsUnit.Point);

            grid.DefaultCellStyle.BackColor = PanelColor;
            grid.DefaultCellStyle.ForeColor = ForegroundColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.DefaultCellStyle.SelectionForeColor = ForegroundColor;
            grid.DefaultCellStyle.Padding = new WF.Padding(4, 2, 4, 2);

            grid.AlternatingRowsDefaultCellStyle.BackColor = PanelColor;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = ForegroundColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectionColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = ForegroundColor;

            WF.DataGridViewTextBoxColumn favoriteColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Favorite",
                HeaderText = "★",
                Width = 36,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            favoriteColumn.DefaultCellStyle.Alignment = WF.DataGridViewContentAlignment.MiddleCenter;

            WF.DataGridViewTextBoxColumn commandColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Command",
                HeaderText = "Command",
                Width = 150,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn usedColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Used",
                HeaderText = "Used",
                Width = 54,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            usedColumn.DefaultCellStyle.Alignment = WF.DataGridViewContentAlignment.MiddleCenter;
            WF.DataGridViewTextBoxColumn descriptionColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "Description",
                Width = 210,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };
            WF.DataGridViewTextBoxColumn sourceColumn = new WF.DataGridViewTextBoxColumn
            {
                Name = "Source",
                HeaderText = "Source",
                Width = 110,
                ReadOnly = true,
                SortMode = WF.DataGridViewColumnSortMode.NotSortable
            };

            grid.Columns.AddRange(
                favoriteColumn,
                commandColumn,
                usedColumn,
                descriptionColumn,
                sourceColumn);
            return grid;
        }

        private static WF.Label CreateLabel(string text)
        {
            return new WF.Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = ForegroundColor,
                BackColor = PanelColor,
                Anchor = WF.AnchorStyles.Left
            };
        }

        private static WF.Button CreateButton(string text, EventHandler onClick)
        {
            PaletteToolbarButton button = new PaletteToolbarButton
            {
                Text = text,
                Font = new System.Drawing.Font(
                    "Segoe UI",
                    8.5F,
                    FontStyle.Bold,
                    GraphicsUnit.Point)
            };
            button.Click += onClick;
            return button;
        }

        private static WF.CheckBox CreateCheckBox(string text, EventHandler onCheckedChanged)
        {
            WF.CheckBox checkBox = new WF.CheckBox
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(4, 7, 0, 0),
                BackColor = PanelColor,
                ForeColor = ForegroundColor
            };
            checkBox.CheckedChanged += onCheckedChanged;
            return checkBox;
        }

        private void AutoShowCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = _autoShowCheckBox.Checked;
            DungXPaletteHost.SetAutoShowEnabled(enabled);
            SetStatus(enabled
                ? "Da bat tu dong mo DXPALETTE khi khoi dong AutoCAD."
                : "Da tat tu dong mo DXPALETTE khi khoi dong AutoCAD.");
        }

        private void SortModeFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            PaletteSortMode mode = GetCurrentSortMode();
            PaletteLayoutStore.SaveSortMode(mode);
            BindGrid();
            SetStatus(
                mode == PaletteSortMode.Custom
                    ? "Dang sap xep theo yeu thich + thu tu tuy chinh."
                    : mode == PaletteSortMode.Used
                        ? "Dang sap xep theo so lan su dung."
                        : "Dang sap xep theo ABC.");
        }

        private IEnumerable<PaletteCommandItem> ApplySortMode(IEnumerable<PaletteCommandItem> items)
        {
            switch (GetCurrentSortMode())
            {
                case PaletteSortMode.Alphabetical:
                    return items
                        .OrderByDescending(item => item.IsFavorite)
                        .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.SourceLabel, StringComparer.OrdinalIgnoreCase);
                case PaletteSortMode.Used:
                    return items
                        .OrderByDescending(item => item.UsageCount)
                        .ThenByDescending(item => item.IsFavorite)
                        .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.SourceLabel, StringComparer.OrdinalIgnoreCase);
                default:
                    return items
                        .OrderByDescending(item => item.IsFavorite)
                        .ThenBy(item => item.ManualOrder)
                        .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase);
            }
        }

        private PaletteSortMode GetCurrentSortMode()
        {
            string selected = Convert.ToString(_sortModeFilter.SelectedItem) ?? "Custom";
            if (string.Equals(selected, "A-Z", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Alphabetical;
            }

            if (string.Equals(selected, "Used", StringComparison.OrdinalIgnoreCase))
            {
                return PaletteSortMode.Used;
            }

            return PaletteSortMode.Custom;
        }

        private void SetSortMode(PaletteSortMode mode)
        {
            string label =
                mode == PaletteSortMode.Alphabetical
                    ? "A-Z"
                    : mode == PaletteSortMode.Used
                        ? "Used"
                        : "Custom";
            int index = _sortModeFilter.FindStringExact(label);
            _sortModeFilter.SelectedIndex = index >= 0 ? index : 0;
        }

        private string GetSelectedCommandName()
        {
            return GetSelectedItem()?.CommandName;
        }

        private void CommandGrid_CellClick(object sender, WF.DataGridViewCellEventArgs e)
        {
            // Click 1 lần vào cột Command là chạy lệnh.
            // Click vào cột Favorite chỉ bật/tắt sao, không chạy lệnh.
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[e.RowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            string columnName = _commandGrid.Columns[e.ColumnIndex].Name;
            if (string.Equals(columnName, "Command", StringComparison.OrdinalIgnoreCase))
            {
                RunItem(item);
                return;
            }

            if (!string.Equals(
                columnName,
                "Favorite",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            item.IsFavorite = !item.IsFavorite;
            PaletteLayoutStore.SaveLayout(_items);
            BindGrid(item.CommandName);
            SetStatus(item.IsFavorite
                ? $"Da danh dau yeu thich: {item.CommandName}"
                : $"Da bo danh dau yeu thich: {item.CommandName}");
        }

        private void CommandGrid_CellDoubleClick(object sender, WF.DataGridViewCellEventArgs e)
        {
        }

        private void SearchBox_KeyDown(object sender, WF.KeyEventArgs e)
        {
            if (e.KeyCode != WF.Keys.Escape)
            {
                return;
            }

            if (_searchBox.TextLength > 0)
            {
                _searchBox.Clear();
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void CommandGrid_KeyDown(object sender, WF.KeyEventArgs e)
        {
            if (e.KeyCode == WF.Keys.Enter && _commandGrid.CurrentCell != null)
            {
                if (string.Equals(
                        _commandGrid.Columns[_commandGrid.CurrentCell.ColumnIndex].Name,
                        "Description",
                        StringComparison.OrdinalIgnoreCase) &&
                    _commandGrid.IsCurrentCellInEditMode)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                RunSelected();
            }
        }

        private void CommandGrid_CellEndEdit(object sender, WF.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 ||
                !string.Equals(
                    _commandGrid.Columns[e.ColumnIndex].Name,
                    "Description",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WF.DataGridViewRow row = _commandGrid.Rows[e.RowIndex];
            PaletteCommandItem item = row.Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            string description = Convert.ToString(row.Cells["Description"].Value) ?? string.Empty;
            item.Description = description.Trim();
            PaletteDescriptionStore.SaveDescription(item.CommandName, item.Description);
            SetStatus($"Da luu mo ta cho {item.CommandName}");
        }

        private void CommandGrid_MouseDown(object sender, WF.MouseEventArgs e)
        {
            _dragStartPoint = e.Location;
            WF.DataGridView.HitTestInfo hit = _commandGrid.HitTest(e.X, e.Y);
            _dragRowIndex = hit.RowIndex;
            if (e.Button == WF.MouseButtons.Left &&
                hit.RowIndex >= 0 &&
                hit.ColumnIndex >= 0 &&
                string.Equals(_commandGrid.Columns[hit.ColumnIndex].Name, "Command", StringComparison.OrdinalIgnoreCase))
            {
                _pressedCommandRowIndex = hit.RowIndex;
                _commandGrid.InvalidateRow(hit.RowIndex);
            }
        }

        private void CommandGrid_MouseMove(object sender, WF.MouseEventArgs e)
        {
            UpdateHoveredCommandRow(e.Location);

            if (e.Button != WF.MouseButtons.Left)
            {
                return;
            }

            if (GetCurrentSortMode() != PaletteSortMode.Custom)
            {
                return;
            }

            if (_dragRowIndex < 0 || _dragRowIndex >= _commandGrid.Rows.Count)
            {
                return;
            }

            Size dragSize = WF.SystemInformation.DragSize;
            Rectangle dragRect = new Rectangle(
                _dragStartPoint.X - dragSize.Width / 2,
                _dragStartPoint.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);

            if (dragRect.Contains(e.Location))
            {
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[_dragRowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                return;
            }

            _commandGrid.DoDragDrop(item, WF.DragDropEffects.Move);
        }

        private void CommandGrid_MouseUp(object sender, WF.MouseEventArgs e)
        {
            if (_pressedCommandRowIndex >= 0)
            {
                int previousPressed = _pressedCommandRowIndex;
                _pressedCommandRowIndex = -1;
                _commandGrid.InvalidateRow(previousPressed);
            }
        }

        private void CommandGrid_MouseLeave(object sender, EventArgs e)
        {
            if (_hoveredCommandRowIndex >= 0)
            {
                int previousHovered = _hoveredCommandRowIndex;
                _hoveredCommandRowIndex = -1;
                _commandGrid.InvalidateRow(previousHovered);
            }

            if (_pressedCommandRowIndex >= 0)
            {
                int previousPressed = _pressedCommandRowIndex;
                _pressedCommandRowIndex = -1;
                _commandGrid.InvalidateRow(previousPressed);
            }

            _commandGrid.Cursor = WF.Cursors.Default;
        }

        private void CommandGrid_DragOver(object sender, WF.DragEventArgs e)
        {
            if (GetCurrentSortMode() != PaletteSortMode.Custom ||
                !e.Data.GetDataPresent(typeof(PaletteCommandItem)))
            {
                e.Effect = WF.DragDropEffects.None;
                return;
            }

            e.Effect = WF.DragDropEffects.Move;
        }

        private void CommandGrid_DragDrop(object sender, WF.DragEventArgs e)
        {
            if (GetCurrentSortMode() != PaletteSortMode.Custom ||
                !e.Data.GetDataPresent(typeof(PaletteCommandItem)))
            {
                return;
            }

            PaletteCommandItem draggedItem =
                e.Data.GetData(typeof(PaletteCommandItem)) as PaletteCommandItem;
            if (draggedItem == null)
            {
                return;
            }

            Point clientPoint = _commandGrid.PointToClient(new Point(e.X, e.Y));
            WF.DataGridView.HitTestInfo hit = _commandGrid.HitTest(clientPoint.X, clientPoint.Y);
            int targetIndex = hit.RowIndex;

            List<PaletteCommandItem> visibleItems = _commandGrid.Rows
                .Cast<WF.DataGridViewRow>()
                .Select(row => row.Tag as PaletteCommandItem)
                .Where(item => item != null)
                .ToList();

            int currentIndex = visibleItems.FindIndex(item =>
                string.Equals(item.CommandName, draggedItem.CommandName, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                return;
            }

            if (targetIndex < 0 || targetIndex >= visibleItems.Count)
            {
                targetIndex = visibleItems.Count - 1;
            }

            PaletteCommandItem movingItem = visibleItems[currentIndex];
            visibleItems.RemoveAt(currentIndex);
            if (targetIndex > currentIndex)
            {
                targetIndex--;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, visibleItems.Count));
            visibleItems.Insert(targetIndex, movingItem);

            HashSet<PaletteCommandItem> visibleSet = new HashSet<PaletteCommandItem>(visibleItems);
            List<PaletteCommandItem> fullOrder = _items
                .OrderBy(item => item.ManualOrder)
                .ThenBy(item => item.CommandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int visibleIndex = 0;
            for (int i = 0; i < fullOrder.Count; i++)
            {
                if (!visibleSet.Contains(fullOrder[i]))
                {
                    continue;
                }

                fullOrder[i] = visibleItems[visibleIndex++];
            }

            for (int i = 0; i < fullOrder.Count; i++)
            {
                fullOrder[i].ManualOrder = i;
            }

            PaletteLayoutStore.SaveLayout(_items);
            BindGrid(draggedItem.CommandName);
            SetStatus($"Da cap nhat thu tu: {draggedItem.CommandName}");
        }

        private void UpdateHoveredCommandRow(Point location)
        {
            WF.DataGridView.HitTestInfo hit = _commandGrid.HitTest(location.X, location.Y);
            int hoveredRow = -1;
            bool isCommandCell = hit.RowIndex >= 0 &&
                hit.ColumnIndex >= 0 &&
                string.Equals(_commandGrid.Columns[hit.ColumnIndex].Name, "Command", StringComparison.OrdinalIgnoreCase);

            if (isCommandCell)
            {
                hoveredRow = hit.RowIndex;
            }

            if (_hoveredCommandRowIndex != hoveredRow)
            {
                int previousHovered = _hoveredCommandRowIndex;
                _hoveredCommandRowIndex = hoveredRow;

                if (previousHovered >= 0 && previousHovered < _commandGrid.Rows.Count)
                {
                    _commandGrid.InvalidateRow(previousHovered);
                }

                if (_hoveredCommandRowIndex >= 0 && _hoveredCommandRowIndex < _commandGrid.Rows.Count)
                {
                    _commandGrid.InvalidateRow(_hoveredCommandRowIndex);
                }
            }

            _commandGrid.Cursor = isCommandCell ? WF.Cursors.Hand : WF.Cursors.Default;
        }

        private void CommandGrid_ColumnWidthChanged(object sender, WF.DataGridViewColumnEventArgs e)
        {
            if (_isApplyingColumnWidths || e?.Column == null || e.Column.Width <= 0)
            {
                return;
            }

            PaletteLayoutStore.SaveColumnWidths(GetCurrentColumnWidths());
        }

        private Dictionary<string, int> GetCurrentColumnWidths()
        {
            Dictionary<string, int> widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (WF.DataGridViewColumn column in _commandGrid.Columns)
            {
                if (column == null || string.IsNullOrWhiteSpace(column.Name) || column.Width <= 0)
                {
                    continue;
                }

                widths[column.Name] = column.Width;
            }

            return widths;
        }

        private void ApplySavedColumnWidths()
        {
            if (_commandGrid.Columns.Count == 0)
            {
                return;
            }

            Dictionary<string, int> widths = PaletteLayoutStore.LoadColumnWidths();
            if (widths.Count == 0)
            {
                return;
            }

            _isApplyingColumnWidths = true;
            try
            {
                foreach (KeyValuePair<string, int> entry in widths)
                {
                    if (!_commandGrid.Columns.Contains(entry.Key))
                    {
                        continue;
                    }

                    _commandGrid.Columns[entry.Key].Width = Math.Max(24, entry.Value);
                }
            }
            finally
            {
                _isApplyingColumnWidths = false;
            }
        }

        private void CommandGrid_CellPainting(object sender, WF.DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = _commandGrid.Columns[e.ColumnIndex].Name;
            if (e.RowIndex < 0)
            {
                PaintPaletteHeaderCell(e);
                return;
            }

            PaletteCommandItem item = _commandGrid.Rows[e.RowIndex].Tag as PaletteCommandItem;
            if (item == null)
            {
                e.Handled = true;
                PaintRowBackground(e);
                return;
            }

            if (string.Equals(columnName, "Command", StringComparison.OrdinalIgnoreCase))
            {
                PaintCommandButtonCell(e, item);
            }
            else if (string.Equals(columnName, "Favorite", StringComparison.OrdinalIgnoreCase))
            {
                PaintFavoriteCell(e, item);
            }
            else if (string.Equals(columnName, "Used", StringComparison.OrdinalIgnoreCase))
            {
                PaintUsageBadgeCell(e, item);
            }
            else
            {
                PaintGenericCell(e);
            }
        }

        private void PaintPaletteHeaderCell(WF.DataGridViewCellPaintingEventArgs e)
        {
            e.Handled = true;
            e.PaintBackground(e.CellBounds, false);

            using (SolidBrush backBrush = new SolidBrush(BackgroundColor))
            {
                e.Graphics.FillRectangle(backBrush, e.CellBounds);
            }

            Rectangle accentRect = new Rectangle(
                e.CellBounds.X,
                e.CellBounds.Bottom - 3,
                e.CellBounds.Width,
                3);
            using (SolidBrush accentBrush = new SolidBrush(HeaderAccentColor))
            {
                e.Graphics.FillRectangle(accentBrush, accentRect);
            }

            Rectangle textBounds = Rectangle.Inflate(e.CellBounds, -8, -4);
            WF.TextRenderer.DrawText(
                e.Graphics,
                Convert.ToString(e.FormattedValue) ?? string.Empty,
                _commandGrid.ColumnHeadersDefaultCellStyle.Font ?? _commandGrid.Font,
                textBounds,
                ForegroundColor,
                WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);
        }

        private void PaintGenericCell(WF.DataGridViewCellPaintingEventArgs e)
        {
            e.Handled = true;
            PaintRowBackground(e);

            if (e.Value == null)
            {
                return;
            }

            WF.TextFormatFlags flags = WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis;
            Rectangle textBounds = Rectangle.Inflate(e.CellBounds, -4, -2);

            WF.TextRenderer.DrawText(
                e.Graphics,
                e.Value.ToString(),
                e.CellStyle.Font,
                textBounds,
                ForegroundColor,
                flags);
        }

        private void PaintCommandButtonCell(WF.DataGridViewCellPaintingEventArgs e, PaletteCommandItem item)
        {
            e.Handled = true;
            PaintRowBackground(e);

            bool hovered = e.RowIndex == _hoveredCommandRowIndex;
            bool pressed = e.RowIndex == _pressedCommandRowIndex;

            Color backColor;
            bool drawShadow = false;
            const int shadowOffset = 2;

            Rectangle availableBounds = Rectangle.Inflate(e.CellBounds, -4, -4);
            Rectangle buttonBounds = new Rectangle(
                availableBounds.X,
                availableBounds.Y,
                Math.Max(1, availableBounds.Width - shadowOffset),
                Math.Max(1, availableBounds.Height - shadowOffset));
            Rectangle textBounds = Rectangle.Inflate(buttonBounds, -8, -1);

            if (pressed)
            {
                backColor = CommandButtonHoverBgColor;
                buttonBounds.Offset(shadowOffset, shadowOffset);
                textBounds.Offset(shadowOffset, shadowOffset);
            }
            else if (hovered)
            {
                backColor = CommandButtonHoverBgColor; // Change background on hover
                drawShadow = true;
            }
            else
            {
                backColor = CommandButtonNormalBgColor;
                drawShadow = true;
            }

            if (drawShadow)
            {
                Rectangle shadowBounds = buttonBounds;
                shadowBounds.Offset(shadowOffset, shadowOffset);
                using (GraphicsPath shadowPath = CreatePaletteRoundedRectangle(shadowBounds, 5))
                using (SolidBrush shadowBrush = new SolidBrush(CommandButtonShadowColor))
                {
                    e.Graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            using (GraphicsPath buttonPath = CreatePaletteRoundedRectangle(buttonBounds, 5))
            using (SolidBrush fillBrush = new SolidBrush(backColor))
            {
                e.Graphics.FillPath(fillBrush, buttonPath);
            }

            using (System.Drawing.Font buttonFont = new System.Drawing.Font(
                "Segoe UI",
                8.75F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    item.CommandName,
                    buttonFont,
                    textBounds,
                    hovered || pressed ? Color.White : ForegroundColor,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);
            }
        }

        private void PaintUsageBadgeCell(WF.DataGridViewCellPaintingEventArgs e, PaletteCommandItem item)
        {
            e.Handled = true;
            PaintRowBackground(e);

            bool hasUsage = item.UsageCount > 0;

            Rectangle badgeBounds = new Rectangle(
                e.CellBounds.X + 10,
                e.CellBounds.Y + 8,
                Math.Max(24, e.CellBounds.Width - 20),
                Math.Max(18, e.CellBounds.Height - 16));

            Color badgeTop = hasUsage ? Color.FromArgb(86, 98, 118) : MutedBadgeColor;
            Color badgeBottom = hasUsage ? Color.FromArgb(66, 74, 90) : Color.FromArgb(60, 64, 72);

            using (LinearGradientBrush badgeBrush = new LinearGradientBrush(
                badgeBounds,
                badgeTop,
                badgeBottom,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(Color.FromArgb(92, 92, 92)))
            {
                e.Graphics.FillRectangle(badgeBrush, badgeBounds);
                e.Graphics.DrawRectangle(borderPen, badgeBounds);
            }

            using (System.Drawing.Font badgeFont = new System.Drawing.Font(
                "Segoe UI",
                8.5F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    item.UsageCount.ToString(CultureInfo.InvariantCulture),
                    badgeFont,
                    badgeBounds,
                    ForegroundColor,
                    WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.NoPadding);
            }
        }

        private void PaintFavoriteCell(WF.DataGridViewCellPaintingEventArgs e, PaletteCommandItem item)
        {
            e.Handled = true;
            PaintRowBackground(e);

            Rectangle badgeBounds = new Rectangle(
                e.CellBounds.X,
                e.CellBounds.Y,
                e.CellBounds.Width,
                e.CellBounds.Height);

            string starText = item.IsFavorite ? "★" : "☆";
            Color starColor = item.IsFavorite ? FavoriteOnColor : FavoriteOffColor;
            using (System.Drawing.Font starFont = new System.Drawing.Font(
                "Segoe UI Symbol",
                item.IsFavorite ? 12F : 11F,
                item.IsFavorite ? FontStyle.Bold : FontStyle.Regular,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    starText,
                    starFont,
                    e.CellBounds,
                    starColor,
                    WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.VerticalCenter);
            }
        }

        private void PaintRowBackground(WF.DataGridViewCellPaintingEventArgs e)
        {
            Color backColor = PanelColor;

            using (SolidBrush backBrush = new SolidBrush(backColor))
            using (Pen separatorPen = new Pen(BorderColor))
            {
                e.Graphics.FillRectangle(backBrush, e.CellBounds);
                e.Graphics.DrawLine(
                    separatorPen,
                    e.CellBounds.Left,
                    e.CellBounds.Bottom - 1,
                    e.CellBounds.Right,
                    e.CellBounds.Bottom - 1);
            }
        }

        private static (Color topColor, Color bottomColor, Color borderColor) GetCommandButtonColors(
            PaletteCommandItem item,
            bool selected,
            bool hovered,
            bool pressed)
        {
            if (pressed)
            {
                return (
                    Color.FromArgb(24, 26, 30),
                    Color.FromArgb(12, 12, 14),
                    Color.FromArgb(86, 90, 96));
            }

            if (hovered)
            {
                return (
                    Color.FromArgb(46, 50, 58),
                    Color.FromArgb(22, 24, 28),
                    Color.FromArgb(116, 126, 142));
            }

            if (selected)
            {
                return (
                    Color.FromArgb(38, 40, 46),
                    Color.FromArgb(18, 18, 20),
                    Color.FromArgb(96, 102, 114));
            }

            return (
                Color.FromArgb(32, 34, 38),
                Color.FromArgb(16, 16, 18),
                Color.FromArgb(72, 74, 78));
        }

        private static GraphicsPath CreatePaletteRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();

            if (diameter > bounds.Width)
            {
                diameter = bounds.Width;
            }

            if (diameter > bounds.Height)
            {
                diameter = bounds.Height;
            }

            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static void EnableDoubleBuffer(WF.Control control)
        {
            if (control == null)
            {
                return;
            }

            try
            {
                PropertyInfo property = typeof(WF.Control).GetProperty(
                    "DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                property?.SetValue(control, true, null);
            }
            catch
            {
            }
        }

        private List<PaletteCommandItem> GetFilteredItems()
        {
            string search = (_searchBox.Text ?? string.Empty).Trim();
            string source = Convert.ToString(_sourceFilter.SelectedItem) ?? "All";
            string type = Convert.ToString(_typeFilter.SelectedItem) ?? "All";

            IEnumerable<PaletteCommandItem> filtered = _items;

            if (!string.Equals(source, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(
                    item => string.Equals(item.SourceLabel, source, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(type, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(item => MatchesTypeFilter(item.SourceKind, type));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(item =>
                    item.CommandName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.SourceLabel.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return ApplySortMode(filtered).ToList();
        }

        private void RunSelected()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon command.");
                return;
            }

            RunItem(item);
        }

        private void RunItem(PaletteCommandItem item)
        {
            if (item == null)
            {
                SetStatus("Chua chon command.");
                return;
            }

            DungXPaletteHost.RunCommand(item);
        }

        private void ReloadLisps()
        {
            bool ok = DungXLispResolver.TryEnsureAllLispFiles(showPrompt: true, out List<string> missing);
            if (ok)
            {
                SetStatus("Da kiem tra xong 2 file LISP, san sang chay.");
            }
            else
            {
                SetStatus("Thieu file LISP: " + string.Join(", ", missing.Select(Path.GetFileName)));
            }

            ReloadData(false);
        }

        private void PickFolder()
        {
            bool selected = DungXPaletteHost.ChooseLispFolder(false);
            SetStatus(selected
                ? "Da cap nhat thu muc LISP."
                : "Khong thay doi thu muc LISP.");
        }

        private void AddSource()
        {
            using (WF.OpenFileDialog dialog = new WF.OpenFileDialog())
            {
                dialog.Title = "Chon file .dll, .lsp hoac .vlx de them vao palette";
                dialog.Filter = "Supported files|*.dll;*.lsp;*.vlx|DLL|*.dll|LISP|*.lsp|VLX|*.vlx";
                dialog.Multiselect = true;

                if (dialog.ShowDialog() != WF.DialogResult.OK)
                {
                    return;
                }

                int added = PaletteSourceStore.AddSources(dialog.FileNames);
                ReloadData(false);
                SetStatus($"Da them {added} source moi.");
            }
        }

        private void AddManualAlias()
        {
            string commandName = PaletteUiHelpers.ShowTextPrompt(
                "Them manual alias",
                "Nhap ten lenh / alias:");
            if (string.IsNullOrWhiteSpace(commandName))
            {
                SetStatus("Khong them manual alias.");
                return;
            }

            PaletteManualCommandStore.Save(commandName.Trim(), string.Empty);
            ReloadData(false);
            SetStatus($"Da them manual alias: {commandName.Trim()}");
        }

        private void RemoveSelectedSource()
        {
            PaletteCommandItem item = GetSelectedItem();
            if (item == null)
            {
                SetStatus("Chua chon dong nao de xoa source.");
                return;
            }

            if (item.SourceKind == PaletteSourceKind.ManualAlias)
            {
                PaletteManualCommandStore.Remove(item.CommandName);
                ReloadData(false);
                SetStatus("Da xoa manual alias.");
                return;
            }

            if (string.IsNullOrWhiteSpace(item.SourcePath) ||
                !PaletteSourceStore.Contains(item.SourcePath))
            {
                SetStatus("Dong dang chon khong phai source ngoai de xoa.");
                return;
            }

            PaletteSourceStore.RemoveSource(item.SourcePath);
            ReloadData(false);
            SetStatus("Da xoa source khoi palette.");
        }

        private void ResetUsageStats()
        {
            WF.DialogResult result = WF.MessageBox.Show(
                "Ban co chac muon reset toan bo thong ke su dung command?",
                "Reset DungX Stats",
                WF.MessageBoxButtons.YesNo,
                WF.MessageBoxIcon.Question);
            if (result != WF.DialogResult.Yes)
            {
                return;
            }

            PaletteUsageStore.Reset();
            foreach (PaletteCommandItem item in _items)
            {
                item.UsageCount = 0;
            }

            BindGrid(GetSelectedCommandName());
            SetStatus("Da reset thong ke su dung command.");
        }

        public void RecordUsage(string commandName, int usageCount)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            string selectedCommandName = GetSelectedCommandName();
            foreach (PaletteCommandItem item in _items.Where(current =>
                string.Equals(current.CommandName, commandName, StringComparison.OrdinalIgnoreCase)))
            {
                item.UsageCount = usageCount;
            }

            if (GetCurrentSortMode() == PaletteSortMode.Used)
            {
                BindGrid(selectedCommandName);
                return;
            }

            UpdateSummary(GetFilteredItems());

            foreach (WF.DataGridViewRow row in _commandGrid.Rows)
            {
                PaletteCommandItem item = row.Tag as PaletteCommandItem;
                if (item == null)
                {
                    continue;
                }

                row.Cells["Used"].Value = item.UsageCount;
            }
        }

        private void RefreshSourceFilter(string preferredSelection)
        {
            List<string> sources = _items
                .Select(item => item.SourceLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _sourceFilter.Items.Clear();
            _sourceFilter.Items.Add("All");
            foreach (string source in sources)
            {
                _sourceFilter.Items.Add(source);
            }

            int selectedIndex = 0;
            for (int i = 0; i < _sourceFilter.Items.Count; i++)
            {
                if (string.Equals(
                    Convert.ToString(_sourceFilter.Items[i]),
                    preferredSelection,
                    StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            _sourceFilter.SelectedIndex = selectedIndex;
        }

        private static bool MatchesTypeFilter(PaletteSourceKind sourceKind, string selectedType)
        {
            switch (selectedType)
            {
                case "LISP":
                    return sourceKind == PaletteSourceKind.Lisp;
                case "DLL":
                    return sourceKind == PaletteSourceKind.ManagedDll ||
                           sourceKind == PaletteSourceKind.BuiltInDll;
                case "VLX":
                    return sourceKind == PaletteSourceKind.Vlx;
                case "Action":
                    return sourceKind == PaletteSourceKind.ActionMacro;
                case "Manual":
                    return sourceKind == PaletteSourceKind.ManualAlias;
                default:
                    return true;
            }
        }

        private PaletteCommandItem GetSelectedItem()
        {
            if (_commandGrid.SelectedRows.Count == 0)
            {
                return null;
            }

            return _commandGrid.SelectedRows[0].Tag as PaletteCommandItem;
        }
    }
}
