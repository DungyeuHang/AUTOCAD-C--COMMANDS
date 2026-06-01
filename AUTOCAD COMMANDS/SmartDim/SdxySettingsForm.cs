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

    internal sealed class SdxySettingsForm : WF.Form
    {
        private readonly List<SdxyEntityTypeChoice> _availableTypes;
        private readonly List<string> _availableLayers;
        private readonly WF.ComboBox _namedFilterCombo;
        private readonly WF.CheckedListBox _typeList;
        private readonly WF.CheckedListBox _layerList;
        private readonly WF.Label _filterPreviewLabel;
        private readonly WF.Label _typeCountLabel;
        private readonly WF.Label _layerCountLabel;
        private readonly WF.Label _sampleSummaryLabel;
        private readonly WF.ListBox _sampleListBox;
        private readonly WF.Label _sampleListCountLabel;
        private readonly WF.CheckBox _sampleTypeCheckBox;
        private readonly WF.CheckBox _sampleLayerCheckBox;
        private readonly WF.CheckBox _sampleLinetypeCheckBox;
        private readonly WF.CheckBox _sampleColorCheckBox;
        private readonly WF.CheckBox _sampleBlockNameCheckBox;
        private readonly WF.ComboBox _sampleTypeValueCombo;
        private readonly WF.ComboBox _sampleLayerValueCombo;
        private readonly WF.TextBox _sampleLinetypeValueTextBox;
        private readonly WF.TextBox _sampleColorValueTextBox;
        private readonly WF.TextBox _sampleBlockNameValueTextBox;
        private readonly Dictionary<string, SdxyTargetSettings> _namedFilters;
        private SdxyTargetSettings _draftSettings;
        private string _selectedNamedFilterName;
        private int _selectedSampleIndex;
        private bool _suppressSampleEditorEvents;

        public SdxySettingsForm(
            IEnumerable<SdxyEntityTypeChoice> availableTypes,
            IEnumerable<string> availableLayers,
            SdxyTargetSettings currentSettings)
        {
            _availableTypes = availableTypes?.ToList() ?? new List<SdxyEntityTypeChoice>();
            _availableLayers = availableLayers?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
            _namedFilters = SdxyNamedFilterStore.LoadAll();
            _draftSettings = currentSettings?.Clone() ?? new SdxyTargetSettings();
            _selectedNamedFilterName = SdxyNamedFilterStore.LoadCurrentName();
            _selectedSampleIndex = _draftSettings.SampleDescriptors.Count - 1;

            Text = "SDXY Target Settings";
            StartPosition = WF.FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 620);
            Size = new Size(760, 680);
            FormBorderStyle = WF.FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new WF.Padding(10)
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            Controls.Add(layout);

            WF.GroupBox namedFiltersGroup = new WF.GroupBox
            {
                Text = "Named Filters",
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Padding = new WF.Padding(10, 20, 10, 10),
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(namedFiltersGroup, 0, 0);

            WF.TableLayoutPanel namedFiltersLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            namedFiltersLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            namedFiltersLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            namedFiltersLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            namedFiltersLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            namedFiltersGroup.Controls.Add(namedFiltersLayout);

            WF.Label currentPresetLabel = new WF.Label
            {
                Text = "Current:",
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 4, 8, 0)
            };
            namedFiltersLayout.Controls.Add(currentPresetLabel, 0, 0);

            _namedFilterCombo = new WF.ComboBox
            {
                Dock = WF.DockStyle.Top,
                DropDownStyle = WF.ComboBoxStyle.DropDownList,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            _namedFilterCombo.SelectedIndexChanged += (_, __) => UpdateNamedFilterButtons();
            namedFiltersLayout.Controls.Add(_namedFilterCombo, 1, 0);

            WF.FlowLayoutPanel namedButtons = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new WF.Padding(0)
            };
            namedFiltersLayout.Controls.Add(namedButtons, 1, 1);

            WF.Button saveAsNamedButton = CreateSmallButton("Save As...");
            saveAsNamedButton.Click += (_, __) => SaveCurrentAsNamedFilter();
            namedButtons.Controls.Add(saveAsNamedButton);

            WF.Button loadNamedButton = CreateSmallButton("Load");
            loadNamedButton.Click += (_, __) => LoadSelectedNamedFilter();
            namedButtons.Controls.Add(loadNamedButton);

            WF.Button deleteNamedButton = CreateSmallButton("Delete");
            deleteNamedButton.Click += (_, __) => DeleteSelectedNamedFilter();
            namedButtons.Controls.Add(deleteNamedButton);

            WF.Label introLabel = new WF.Label
            {
                Text =
                    "Chon cac doi tuong ma SDXY duoc phep dim toi. " +
                    "Neu bo trong hoac check het thi xem nhu khong loc. " +
                    "Sample object co the dung de loc them theo type/layer/linetype/color/block.",
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(introLabel, 0, 1);

            _filterPreviewLabel = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                BorderStyle = WF.BorderStyle.FixedSingle,
                Padding = new WF.Padding(10),
                Margin = new WF.Padding(0, 0, 0, 8),
                Font = new System.Drawing.Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };
            layout.Controls.Add(_filterPreviewLabel, 0, 2);

            WF.TabControl tabs = new WF.TabControl
            {
                Dock = WF.DockStyle.Fill
            };
            layout.Controls.Add(tabs, 0, 3);

            WF.TabPage typesPage = new WF.TabPage("Types");
            tabs.TabPages.Add(typesPage);
            WF.TableLayoutPanel typesLayout = CreateTabLayout(typesPage);
            WF.FlowLayoutPanel typeButtons = CreateButtonPanel();
            typesLayout.Controls.Add(typeButtons, 0, 0);

            WF.Button allTypesButton = CreateSmallButton("All");
            allTypesButton.Click += (_, __) => SetAllChecked(_typeList, true);
            typeButtons.Controls.Add(allTypesButton);

            WF.Button noneTypesButton = CreateSmallButton("None");
            noneTypesButton.Click += (_, __) => SetAllChecked(_typeList, false);
            typeButtons.Controls.Add(noneTypesButton);

            WF.Button commonTypesButton = CreateSmallButton("Common");
            commonTypesButton.Click += (_, __) => ApplyCommonTypesSelection();
            typeButtons.Controls.Add(commonTypesButton);

            _typeList = new WF.CheckedListBox
            {
                Dock = WF.DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _typeList.ItemCheck += TypeList_ItemCheck;
            typesLayout.Controls.Add(_typeList, 0, 1);

            _typeCountLabel = new WF.Label
            {
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            typesLayout.Controls.Add(_typeCountLabel, 0, 2);

            WF.TabPage layersPage = new WF.TabPage("Layers");
            tabs.TabPages.Add(layersPage);
            WF.TableLayoutPanel layersLayout = CreateTabLayout(layersPage);
            WF.FlowLayoutPanel layerButtons = CreateButtonPanel();
            layersLayout.Controls.Add(layerButtons, 0, 0);

            WF.Button allLayersButton = CreateSmallButton("All");
            allLayersButton.Click += (_, __) => SetAllChecked(_layerList, true);
            layerButtons.Controls.Add(allLayersButton);

            WF.Button noneLayersButton = CreateSmallButton("None");
            noneLayersButton.Click += (_, __) => SetAllChecked(_layerList, false);
            layerButtons.Controls.Add(noneLayersButton);

            _layerList = new WF.CheckedListBox
            {
                Dock = WF.DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _layerList.ItemCheck += LayerList_ItemCheck;
            layersLayout.Controls.Add(_layerList, 0, 1);

            _layerCountLabel = new WF.Label
            {
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            layersLayout.Controls.Add(_layerCountLabel, 0, 2);

            WF.TabPage samplePage = new WF.TabPage("Sample");
            tabs.TabPages.Add(samplePage);
            WF.TableLayoutPanel sampleLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new WF.Padding(8)
            };
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            samplePage.Controls.Add(sampleLayout);

            _sampleSummaryLabel = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 10)
            };
            sampleLayout.Controls.Add(_sampleSummaryLabel, 0, 0);

            WF.FlowLayoutPanel sampleButtons = CreateButtonPanel();
            sampleLayout.Controls.Add(sampleButtons, 0, 1);

            WF.Button pickSampleButton = CreateSmallButton("Pick sample...");
            pickSampleButton.Click += (_, __) => RequestPickSample();
            sampleButtons.Controls.Add(pickSampleButton);

            WF.Button addCurrentSampleButton = CreateSmallButton("Add current");
            addCurrentSampleButton.Click += (_, __) => AddCurrentSampleFromEditor();
            sampleButtons.Controls.Add(addCurrentSampleButton);

            WF.Button removeSampleButton = CreateSmallButton("Remove selected");
            removeSampleButton.Click += (_, __) => RemoveSelectedSample();
            sampleButtons.Controls.Add(removeSampleButton);

            WF.Button clearSampleButton = CreateSmallButton("Clear editor");
            clearSampleButton.Click += (_, __) =>
            {
                ClearSampleEditor();
            };
            sampleButtons.Controls.Add(clearSampleButton);

            WF.GroupBox sampleListGroup = new WF.GroupBox
            {
                Text = "Saved sample objects (OR)",
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                Padding = new WF.Padding(12, 24, 12, 12),
                Margin = new WF.Padding(0, 0, 0, 10)
            };
            sampleLayout.Controls.Add(sampleListGroup, 0, 2);

            WF.TableLayoutPanel sampleListLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true
            };
            sampleListLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            sampleListLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            sampleListGroup.Controls.Add(sampleListLayout);

            _sampleListBox = new WF.ListBox
            {
                Dock = WF.DockStyle.Fill,
                Height = 120
            };
            _sampleListBox.SelectedIndexChanged += (_, __) => LoadSelectedSampleIntoEditors();
            sampleListLayout.Controls.Add(_sampleListBox, 0, 0);

            _sampleListCountLabel = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            sampleListLayout.Controls.Add(_sampleListCountLabel, 0, 1);

            WF.GroupBox sampleValuesGroup = new WF.GroupBox
            {
                Text = "Sample values",
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                Padding = new WF.Padding(12, 24, 12, 12),
                Margin = new WF.Padding(0, 0, 0, 10)
            };
            sampleLayout.Controls.Add(sampleValuesGroup, 0, 3);

            WF.TableLayoutPanel sampleValuesLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                AutoSize = true
            };
            sampleValuesLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.AutoSize));
            sampleValuesLayout.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 100f));
            sampleValuesGroup.Controls.Add(sampleValuesLayout);

            _sampleTypeValueCombo = new WF.ComboBox
            {
                Dock = WF.DockStyle.Top,
                DropDownStyle = WF.ComboBoxStyle.DropDown
            };
            foreach (SdxyEntityTypeChoice choice in _availableTypes)
            {
                _sampleTypeValueCombo.Items.Add(choice);
            }

            _sampleLayerValueCombo = new WF.ComboBox
            {
                Dock = WF.DockStyle.Top,
                DropDownStyle = WF.ComboBoxStyle.DropDown
            };
            foreach (string layerName in _availableLayers)
            {
                _sampleLayerValueCombo.Items.Add(layerName);
            }

            _sampleLinetypeValueTextBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top
            };

            _sampleColorValueTextBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top
            };

            _sampleBlockNameValueTextBox = new WF.TextBox
            {
                Dock = WF.DockStyle.Top
            };

            AddSampleValueRow(sampleValuesLayout, 0, "Type:", _sampleTypeValueCombo);
            AddSampleValueRow(sampleValuesLayout, 1, "Layer:", _sampleLayerValueCombo);
            AddSampleValueRow(sampleValuesLayout, 2, "Linetype:", _sampleLinetypeValueTextBox);
            AddSampleValueRow(sampleValuesLayout, 3, "Color key:", _sampleColorValueTextBox);
            AddSampleValueRow(sampleValuesLayout, 4, "Block name:", _sampleBlockNameValueTextBox);

            WF.Label sampleHintLabel = new WF.Label
            {
                Text = "Pick sample de lay nhanh attribute, sau do co the sua tay va luu thanh preset. Color key ho tro: ByLayer, ByBlock, ACI:1, RGB:255,0,0.",
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 6, 0, 0)
            };
            sampleValuesLayout.Controls.Add(sampleHintLabel, 0, 5);
            sampleValuesLayout.SetColumnSpan(sampleHintLabel, 2);

            WF.GroupBox sampleGroup = new WF.GroupBox
            {
                Text = "Match sample attributes",
                Dock = WF.DockStyle.Top,
                AutoSize = true,
                Padding = new WF.Padding(12, 24, 12, 12)
            };
            sampleLayout.Controls.Add(sampleGroup, 0, 4);

            WF.FlowLayoutPanel sampleCheckPanel = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                AutoSize = true,
                FlowDirection = WF.FlowDirection.TopDown,
                WrapContents = false
            };
            sampleGroup.Controls.Add(sampleCheckPanel);

            _sampleTypeCheckBox = CreateSampleCheckBox("Match type");
            _sampleLayerCheckBox = CreateSampleCheckBox("Match layer");
            _sampleLinetypeCheckBox = CreateSampleCheckBox("Match linetype");
            _sampleColorCheckBox = CreateSampleCheckBox("Match color");
            _sampleBlockNameCheckBox = CreateSampleCheckBox("Match block name");
            sampleCheckPanel.Controls.Add(_sampleTypeCheckBox);
            sampleCheckPanel.Controls.Add(_sampleLayerCheckBox);
            sampleCheckPanel.Controls.Add(_sampleLinetypeCheckBox);
            sampleCheckPanel.Controls.Add(_sampleColorCheckBox);
            sampleCheckPanel.Controls.Add(_sampleBlockNameCheckBox);

            _sampleTypeValueCombo.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleTypeValueCombo.SelectedIndexChanged += (_, __) => RefreshSampleEditorState();
            _sampleLayerValueCombo.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleLayerValueCombo.SelectedIndexChanged += (_, __) => RefreshSampleEditorState();
            _sampleLinetypeValueTextBox.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleColorValueTextBox.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleBlockNameValueTextBox.TextChanged += (_, __) => RefreshSampleEditorState();
            _sampleTypeCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleLayerCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleLinetypeCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleColorCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();
            _sampleBlockNameCheckBox.CheckedChanged += (_, __) => RefreshSampleEditorState();

            WF.FlowLayoutPanel footer = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                FlowDirection = WF.FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(footer, 0, 4);

            WF.Button okButton = new WF.Button
            {
                Text = "OK",
                AutoSize = true,
                DialogResult = WF.DialogResult.OK
            };
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

            LoadNamedFilterItems();
            LoadTypeItems();
            LoadLayerItems();
            LoadSampleState();
            RefreshFilterPreview();
        }

        public SdxySettingsFormAction PendingAction { get; private set; }

        public SdxyTargetSettings ResultSettings => BuildSettings();

        public string SelectedNamedFilterName => _selectedNamedFilterName ?? string.Empty;

        private static WF.TableLayoutPanel CreateTabLayout(WF.Control parent)
        {
            WF.TableLayoutPanel layout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new WF.Padding(8)
            };
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100f));
            layout.RowStyles.Add(new WF.RowStyle(WF.SizeType.AutoSize));
            parent.Controls.Add(layout);
            return layout;
        }

        private static WF.FlowLayoutPanel CreateButtonPanel()
        {
            return new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                FlowDirection = WF.FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new WF.Padding(0, 0, 0, 8)
            };
        }

        private static WF.Button CreateSmallButton(string text)
        {
            return new WF.Button
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 8, 0)
            };
        }

        private static WF.CheckBox CreateSampleCheckBox(string text)
        {
            return new WF.CheckBox
            {
                Text = text,
                AutoSize = true,
                Margin = new WF.Padding(0, 0, 0, 6)
            };
        }

        private static void AddSampleValueRow(
            WF.TableLayoutPanel layout,
            int rowIndex,
            string labelText,
            WF.Control control)
        {
            WF.Label label = new WF.Label
            {
                Text = labelText,
                AutoSize = true,
                Dock = WF.DockStyle.Fill,
                Margin = new WF.Padding(0, 4, 8, 0)
            };

            control.Margin = new WF.Padding(0, 0, 0, 6);
            layout.Controls.Add(label, 0, rowIndex);
            layout.Controls.Add(control, 1, rowIndex);
        }

        private void LoadTypeItems()
        {
            _typeList.Items.Clear();
            HashSet<string> selected =
                _draftSettings.AllowedTypeNames.Count == 0
                    ? new HashSet<string>(_availableTypes.Select(item => item.TypeName), StringComparer.Ordinal)
                    : new HashSet<string>(_draftSettings.AllowedTypeNames, StringComparer.Ordinal);

            foreach (SdxyEntityTypeChoice choice in _availableTypes)
            {
                int index = _typeList.Items.Add(choice);
                _typeList.SetItemChecked(index, selected.Contains(choice.TypeName));
            }

            UpdateTypeCountLabel();
        }

        private void LoadLayerItems()
        {
            _layerList.Items.Clear();
            HashSet<string> selected =
                _draftSettings.AllowedLayers.Count == 0
                    ? new HashSet<string>(_availableLayers, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(_draftSettings.AllowedLayers, StringComparer.OrdinalIgnoreCase);

            foreach (string layerName in _availableLayers)
            {
                int index = _layerList.Items.Add(layerName);
                _layerList.SetItemChecked(index, selected.Contains(layerName));
            }

            UpdateLayerCountLabel();
        }

        private void ApplyCommonTypesSelection()
        {
            for (int i = 0; i < _typeList.Items.Count; i++)
            {
                bool isCommon =
                    _typeList.Items[i] is SdxyEntityTypeChoice choice &&
                    choice.IsCommon;
                _typeList.SetItemChecked(i, isCommon);
            }

            UpdateTypeCountLabel();
        }

        private void SetAllChecked(WF.CheckedListBox list, bool isChecked)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, isChecked);
            }

            UpdateTypeCountLabel();
            UpdateLayerCountLabel();
        }

        private void UpdateTypeCountLabel()
        {
            _typeCountLabel.Text =
                $"Dang chon {_typeList.CheckedItems.Count}/{_typeList.Items.Count} type.";
        }

        private void UpdateLayerCountLabel()
        {
            _layerCountLabel.Text =
                $"Dang chon {_layerList.CheckedItems.Count}/{_layerList.Items.Count} layer.";
        }

        private void TypeList_ItemCheck(object sender, WF.ItemCheckEventArgs e)
        {
            UpdateCheckedCountLabel(_typeList, _typeCountLabel, "type", e);
            QueueRefreshFilterPreview();
        }

        private void LayerList_ItemCheck(object sender, WF.ItemCheckEventArgs e)
        {
            UpdateCheckedCountLabel(_layerList, _layerCountLabel, "layer", e);
            QueueRefreshFilterPreview();
        }

        private static void UpdateCheckedCountLabel(
            WF.CheckedListBox list,
            WF.Label label,
            string noun,
            WF.ItemCheckEventArgs e)
        {
            if (list == null || label == null)
            {
                return;
            }

            int checkedCount = list.CheckedItems.Count;
            if (e != null)
            {
                if (e.CurrentValue != WF.CheckState.Checked &&
                    e.NewValue == WF.CheckState.Checked)
                {
                    checkedCount++;
                }
                else if (e.CurrentValue == WF.CheckState.Checked &&
                    e.NewValue != WF.CheckState.Checked)
                {
                    checkedCount--;
                }
            }

            label.Text = $"Dang chon {checkedCount}/{list.Items.Count} {noun}.";
        }

        private void LoadSampleState()
        {
            LoadSampleListItems();
            SdxySampleDescriptor sample = GetSelectedOrCurrentSample();

            _suppressSampleEditorEvents = true;
            try
            {
                SetSampleTypeEditorValue(sample);
                _sampleLayerValueCombo.Text = sample?.LayerName ?? string.Empty;
                _sampleLinetypeValueTextBox.Text = sample?.LinetypeName ?? string.Empty;
                _sampleColorValueTextBox.Text = sample?.ColorKey ?? string.Empty;
                _sampleBlockNameValueTextBox.Text = sample?.BlockName ?? string.Empty;
            }
            finally
            {
                _suppressSampleEditorEvents = false;
            }

            _sampleTypeCheckBox.Checked = _draftSettings.UseSampleType;
            _sampleLayerCheckBox.Checked = _draftSettings.UseSampleLayer;
            _sampleLinetypeCheckBox.Checked = _draftSettings.UseSampleLinetype;
            _sampleColorCheckBox.Checked = _draftSettings.UseSampleColor;
            _sampleBlockNameCheckBox.Checked = _draftSettings.UseSampleBlockName;
            RefreshSampleEditorState();
        }

        private void LoadNamedFilterItems()
        {
            _namedFilterCombo.Items.Clear();
            foreach (string name in _namedFilters.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                _namedFilterCombo.Items.Add(name);
            }

            if (!string.IsNullOrWhiteSpace(_selectedNamedFilterName) &&
                _namedFilters.ContainsKey(_selectedNamedFilterName))
            {
                _namedFilterCombo.SelectedItem = _selectedNamedFilterName;
            }
            else if (_namedFilterCombo.Items.Count > 0)
            {
                _namedFilterCombo.SelectedIndex = 0;
            }

            UpdateNamedFilterButtons();
        }

        private void UpdateNamedFilterButtons()
        {
            if (_namedFilterCombo.SelectedItem is string selectedName &&
                !string.IsNullOrWhiteSpace(selectedName))
            {
                _selectedNamedFilterName = selectedName;
            }
        }

        private void SaveCurrentAsNamedFilter()
        {
            string inputName = PaletteUiHelpers.ShowTextPrompt(
                "SDXY Named Filter",
                "Nhap ten filter de luu:");
            string filterName = NormalizeNamedFilterName(inputName);
            if (string.IsNullOrWhiteSpace(filterName))
            {
                return;
            }

            if (_namedFilters.ContainsKey(filterName))
            {
                WF.DialogResult overwriteResult = WF.MessageBox.Show(
                    $"Filter '{filterName}' da ton tai. Ghi de?",
                    "SDXY Named Filter",
                    WF.MessageBoxButtons.YesNo,
                    WF.MessageBoxIcon.Question);
                if (overwriteResult != WF.DialogResult.Yes)
                {
                    return;
                }
            }

            _namedFilters[filterName] = BuildSettings();
            _selectedNamedFilterName = filterName;
            SdxyNamedFilterStore.SaveAll(_namedFilters);
            LoadNamedFilterItems();
            _namedFilterCombo.SelectedItem = filterName;
        }

        private void LoadSelectedNamedFilter()
        {
            if (!(_namedFilterCombo.SelectedItem is string filterName) ||
                string.IsNullOrWhiteSpace(filterName) ||
                !_namedFilters.TryGetValue(filterName, out SdxyTargetSettings settings))
            {
                return;
            }

            _draftSettings = settings.Clone();
            _selectedNamedFilterName = filterName;
            _selectedSampleIndex = _draftSettings.SampleDescriptors.Count > 0 ? 0 : -1;
            LoadTypeItems();
            LoadLayerItems();
            LoadSampleState();
            RefreshFilterPreview();
        }

        private void DeleteSelectedNamedFilter()
        {
            if (!(_namedFilterCombo.SelectedItem is string filterName) ||
                string.IsNullOrWhiteSpace(filterName))
            {
                return;
            }

            WF.DialogResult deleteResult = WF.MessageBox.Show(
                $"Xoa filter '{filterName}'?",
                "SDXY Named Filter",
                WF.MessageBoxButtons.YesNo,
                WF.MessageBoxIcon.Question);
            if (deleteResult != WF.DialogResult.Yes)
            {
                return;
            }

            _namedFilters.Remove(filterName);
            if (string.Equals(_selectedNamedFilterName, filterName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedNamedFilterName = string.Empty;
            }

            SdxyNamedFilterStore.SaveAll(_namedFilters);
            LoadNamedFilterItems();
        }

        private static string NormalizeNamedFilterName(string name)
        {
            string normalized = (name ?? string.Empty).Trim();
            normalized = normalized.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized;
        }

        private void RequestPickSample()
        {
            _draftSettings = BuildSettings();
            PendingAction = SdxySettingsFormAction.PickSample;
            Close();
        }

        private SdxyTargetSettings BuildSettings()
        {
            SdxyTargetSettings settings = new SdxyTargetSettings
            {
                UseSampleType = _sampleTypeCheckBox.Checked,
                UseSampleLayer = _sampleLayerCheckBox.Checked,
                UseSampleLinetype = _sampleLinetypeCheckBox.Checked,
                UseSampleColor = _sampleColorCheckBox.Checked,
                UseSampleBlockName = _sampleBlockNameCheckBox.Checked
            };

            HashSet<string> selectedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in _typeList.CheckedItems)
            {
                if (item is SdxyEntityTypeChoice choice &&
                    !string.IsNullOrWhiteSpace(choice.TypeName))
                {
                    selectedTypes.Add(choice.TypeName);
                }
            }

            if (selectedTypes.Count > 0 && selectedTypes.Count < _availableTypes.Count)
            {
                foreach (string typeName in selectedTypes)
                {
                    settings.AllowedTypeNames.Add(typeName);
                }
            }

            HashSet<string> selectedLayers =
                new HashSet<string>(_layerList.CheckedItems.Cast<string>(), StringComparer.OrdinalIgnoreCase);
            if (selectedLayers.Count > 0 && selectedLayers.Count < _availableLayers.Count)
            {
                foreach (string layerName in selectedLayers)
                {
                    settings.AllowedLayers.Add(layerName);
                }
            }

            foreach (SdxySampleDescriptor sample in BuildSampleDescriptorListFromUi())
            {
                settings.SampleDescriptors.Add(sample);
            }

            if (settings.SampleDescriptors.Count == 0)
            {
                settings.UseSampleType = false;
                settings.UseSampleLayer = false;
                settings.UseSampleLinetype = false;
                settings.UseSampleColor = false;
                settings.UseSampleBlockName = false;
            }
            else
            {
                bool hasType = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.TypeName));
                bool hasLayer = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.LayerName));
                bool hasLinetype = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.LinetypeName));
                bool hasColor = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.ColorKey));
                bool hasBlock = settings.SampleDescriptors.Any(sample => !string.IsNullOrWhiteSpace(sample.BlockName));

                if (!hasType)
                {
                    settings.UseSampleType = false;
                }

                if (!hasLayer)
                {
                    settings.UseSampleLayer = false;
                }

                if (!hasLinetype)
                {
                    settings.UseSampleLinetype = false;
                }

                if (!hasColor)
                {
                    settings.UseSampleColor = false;
                }

                if (!hasBlock)
                {
                    settings.UseSampleBlockName = false;
                }
            }

            return settings;
        }

        private void QueueRefreshFilterPreview()
        {
            if (IsHandleCreated)
            {
                BeginInvoke((Action)RefreshFilterPreview);
            }
        }

        private void RefreshFilterPreview()
        {
            if (_filterPreviewLabel == null)
            {
                return;
            }

            SdxyTargetSettings settings = BuildSettings();
            string typeText = settings.AllowedTypeNames.Count == 0
                ? "All types"
                : $"{settings.AllowedTypeNames.Count} type";
            string layerText = settings.AllowedLayers.Count == 0
                ? "All layers"
                : $"{settings.AllowedLayers.Count} layer";

            List<string> sampleModes = GetEnabledSampleModeLabels(settings);
            string sampleText = settings.SampleDescriptors.Count == 0
                ? "No sample objects"
                : $"{settings.SampleDescriptors.Count} sample object(s)" +
                  (sampleModes.Count == 0 ? string.Empty : $" match {string.Join("+", sampleModes)}");

            _filterPreviewLabel.Text =
                "Current filter = " + typeText + " AND " + layerText + " AND " + sampleText;
        }

        private List<string> GetEnabledSampleModeLabels(SdxyTargetSettings settings)
        {
            List<string> modes = new List<string>();
            if (settings.UseSampleType) modes.Add("Type");
            if (settings.UseSampleLayer) modes.Add("Layer");
            if (settings.UseSampleLinetype) modes.Add("Linetype");
            if (settings.UseSampleColor) modes.Add("Color");
            if (settings.UseSampleBlockName) modes.Add("Block");
            return modes;
        }

        private void SetSampleTypeEditorValue(SdxySampleDescriptor sample)
        {
            string sampleTypeName = sample?.TypeName ?? string.Empty;
            SdxyEntityTypeChoice matchedChoice = _availableTypes.FirstOrDefault(choice =>
                string.Equals(choice.TypeName, sampleTypeName, StringComparison.OrdinalIgnoreCase));
            if (matchedChoice != null)
            {
                _sampleTypeValueCombo.SelectedItem = matchedChoice;
                return;
            }

            _sampleTypeValueCombo.SelectedItem = null;
            _sampleTypeValueCombo.Text = !string.IsNullOrWhiteSpace(sample?.TypeDisplayName)
                ? sample.TypeDisplayName
                : sampleTypeName;
        }

        private SdxySampleDescriptor BuildSampleDescriptorFromEditors()
        {
            string typeName = string.Empty;
            string typeDisplayName = string.Empty;

            if (_sampleTypeValueCombo.SelectedItem is SdxyEntityTypeChoice selectedChoice)
            {
                typeName = selectedChoice.TypeName ?? string.Empty;
                typeDisplayName = selectedChoice.DisplayName ?? string.Empty;
            }
            else
            {
                string sampleTypeText = (_sampleTypeValueCombo.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(sampleTypeText))
                {
                    SdxyEntityTypeChoice matchedChoice = _availableTypes.FirstOrDefault(choice =>
                        string.Equals(choice.DisplayName, sampleTypeText, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(choice.TypeName, sampleTypeText, StringComparison.OrdinalIgnoreCase));
                    if (matchedChoice != null)
                    {
                        typeName = matchedChoice.TypeName ?? string.Empty;
                        typeDisplayName = matchedChoice.DisplayName ?? string.Empty;
                    }
                    else
                    {
                        typeName = sampleTypeText;
                        typeDisplayName = sampleTypeText;
                    }
                }
            }

            string layerName = (_sampleLayerValueCombo.Text ?? string.Empty).Trim();
            string linetypeName = (_sampleLinetypeValueTextBox.Text ?? string.Empty).Trim();
            string colorKey = (_sampleColorValueTextBox.Text ?? string.Empty).Trim();
            string blockName = (_sampleBlockNameValueTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(typeName) &&
                string.IsNullOrWhiteSpace(layerName) &&
                string.IsNullOrWhiteSpace(linetypeName) &&
                string.IsNullOrWhiteSpace(colorKey) &&
                string.IsNullOrWhiteSpace(blockName))
            {
                return null;
            }

            string colorDisplayName = colorKey;
            if (_draftSettings.SampleDescriptor != null &&
                string.Equals(_draftSettings.SampleDescriptor.ColorKey, colorKey, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_draftSettings.SampleDescriptor.ColorDisplayName))
            {
                colorDisplayName = _draftSettings.SampleDescriptor.ColorDisplayName;
            }

            return new SdxySampleDescriptor(
                typeName,
                typeDisplayName,
                layerName,
                linetypeName,
                colorKey,
                colorDisplayName,
                blockName);
        }

        private List<SdxySampleDescriptor> BuildSampleDescriptorListFromUi()
        {
            List<SdxySampleDescriptor> samples = _draftSettings.SampleDescriptors
                .Where(sample => sample != null)
                .Select(sample => sample.Clone())
                .ToList();

            SdxySampleDescriptor currentEditorSample = BuildSampleDescriptorFromEditors();
            if (_selectedSampleIndex >= 0 && _selectedSampleIndex < samples.Count)
            {
                if (currentEditorSample == null)
                {
                    samples.RemoveAt(_selectedSampleIndex);
                }
                else
                {
                    samples[_selectedSampleIndex] = currentEditorSample;
                }
            }
            else if (currentEditorSample != null)
            {
                samples.Add(currentEditorSample);
            }

            return samples;
        }

        private void LoadSampleListItems()
        {
            List<SdxySampleDescriptor> samples = _draftSettings.SampleDescriptors
                .Where(sample => sample != null)
                .ToList();

            _sampleListBox.Items.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                string summary = samples[i].BuildSummary();
                _sampleListBox.Items.Add($"{i + 1}. {summary}");
            }

            _sampleListCountLabel.Text = $"Dang luu {samples.Count} sample object.";
            if (samples.Count == 0)
            {
                _selectedSampleIndex = -1;
                _sampleListBox.ClearSelected();
                return;
            }

            if (_selectedSampleIndex < 0 || _selectedSampleIndex >= samples.Count)
            {
                _selectedSampleIndex = samples.Count - 1;
            }

            if (_sampleListBox.Items.Count > 0)
            {
                _suppressSampleEditorEvents = true;
                try
                {
                    _sampleListBox.SelectedIndex = _selectedSampleIndex;
                }
                finally
                {
                    _suppressSampleEditorEvents = false;
                }
            }
        }

        private SdxySampleDescriptor GetSelectedOrCurrentSample()
        {
            if (_selectedSampleIndex >= 0 &&
                _selectedSampleIndex < _draftSettings.SampleDescriptors.Count)
            {
                return _draftSettings.SampleDescriptors[_selectedSampleIndex];
            }

            return null;
        }

        private void LoadSelectedSampleIntoEditors()
        {
            if (_suppressSampleEditorEvents)
            {
                return;
            }

            _selectedSampleIndex = _sampleListBox.SelectedIndex;
            SdxySampleDescriptor sample = GetSelectedOrCurrentSample();

            _suppressSampleEditorEvents = true;
            try
            {
                SetSampleTypeEditorValue(sample);
                _sampleLayerValueCombo.Text = sample?.LayerName ?? string.Empty;
                _sampleLinetypeValueTextBox.Text = sample?.LinetypeName ?? string.Empty;
                _sampleColorValueTextBox.Text = sample?.ColorKey ?? string.Empty;
                _sampleBlockNameValueTextBox.Text = sample?.BlockName ?? string.Empty;
            }
            finally
            {
                _suppressSampleEditorEvents = false;
            }

            RefreshSampleEditorState();
        }

        private void AddCurrentSampleFromEditor()
        {
            SdxySampleDescriptor sample = BuildSampleDescriptorFromEditors();
            if (sample == null)
            {
                return;
            }

            _draftSettings.SampleDescriptors.Add(sample);
            _selectedSampleIndex = _draftSettings.SampleDescriptors.Count - 1;
            LoadSampleListItems();
            RefreshSampleEditorState();
        }

        private void RemoveSelectedSample()
        {
            if (_selectedSampleIndex < 0 || _selectedSampleIndex >= _draftSettings.SampleDescriptors.Count)
            {
                return;
            }

            _draftSettings.SampleDescriptors.RemoveAt(_selectedSampleIndex);
            if (_selectedSampleIndex >= _draftSettings.SampleDescriptors.Count)
            {
                _selectedSampleIndex = _draftSettings.SampleDescriptors.Count - 1;
            }

            LoadSampleListItems();
            LoadSelectedSampleIntoEditors();
            RefreshSampleEditorState();
        }

        private void ClearSampleEditor()
        {
            _selectedSampleIndex = -1;
            _suppressSampleEditorEvents = true;
            try
            {
                _sampleListBox.ClearSelected();
                _sampleTypeValueCombo.SelectedItem = null;
                _sampleTypeValueCombo.Text = string.Empty;
                _sampleLayerValueCombo.Text = string.Empty;
                _sampleLinetypeValueTextBox.Text = string.Empty;
                _sampleColorValueTextBox.Text = string.Empty;
                _sampleBlockNameValueTextBox.Text = string.Empty;
            }
            finally
            {
                _suppressSampleEditorEvents = false;
            }

            RefreshSampleEditorState();
        }

        private void RefreshSampleEditorState()
        {
            if (_suppressSampleEditorEvents)
            {
                return;
            }

            if (_selectedSampleIndex >= 0 && _selectedSampleIndex < _draftSettings.SampleDescriptors.Count)
            {
                SdxySampleDescriptor selectedSample = BuildSampleDescriptorFromEditors();
                if (selectedSample != null)
                {
                    _draftSettings.SampleDescriptors[_selectedSampleIndex] = selectedSample;
                    LoadSampleListItems();
                }
            }

            SdxySampleDescriptor sample = BuildSampleDescriptorFromEditors();
            List<string> activeConditions = new List<string>();

            if (_sampleTypeCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.TypeName))
            {
                activeConditions.Add("Type");
            }

            if (_sampleLayerCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.LayerName))
            {
                activeConditions.Add("Layer");
            }

            if (_sampleLinetypeCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.LinetypeName))
            {
                activeConditions.Add("Linetype");
            }

            if (_sampleColorCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.ColorKey))
            {
                activeConditions.Add("Color");
            }

            if (_sampleBlockNameCheckBox.Checked && !string.IsNullOrWhiteSpace(sample?.BlockName))
            {
                activeConditions.Add("Block");
            }

            _sampleSummaryLabel.Text = sample == null
                ? "Chua co sample/filter value. Bam Pick sample de lay nhanh, hoac nhap tay cac attribute ben duoi."
                : sample.BuildSummary() + Environment.NewLine +
                  "Dang match: " +
                  (activeConditions.Count == 0 ? "chua chon attribute nao." : string.Join(" + ", activeConditions));

            UpdateSampleCheckBoxState(_sampleTypeCheckBox, !string.IsNullOrWhiteSpace(sample?.TypeName));
            UpdateSampleCheckBoxState(_sampleLayerCheckBox, !string.IsNullOrWhiteSpace(sample?.LayerName));
            UpdateSampleCheckBoxState(_sampleLinetypeCheckBox, !string.IsNullOrWhiteSpace(sample?.LinetypeName));
            UpdateSampleCheckBoxState(_sampleColorCheckBox, !string.IsNullOrWhiteSpace(sample?.ColorKey));
            UpdateSampleCheckBoxState(_sampleBlockNameCheckBox, !string.IsNullOrWhiteSpace(sample?.BlockName));
            RefreshFilterPreview();
        }

        private static void UpdateSampleCheckBoxState(WF.CheckBox checkBox, bool isEnabled)
        {
            if (checkBox == null)
            {
                return;
            }

            checkBox.Enabled = isEnabled;
            if (!isEnabled)
            {
                checkBox.Checked = false;
            }
        }
    }
}
