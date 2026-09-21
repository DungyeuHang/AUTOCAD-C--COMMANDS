using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS
{
    // ==========================================================================================
    // AUTO DIM PLINE - BANG CAI DAT
    // Don gian, gon, nho cai dat lan truoc. Moi o nhap deu duoc kiem tra truoc khi dong form:
    // nhap sai thi bao ngay tai cho chu khong crash va khong chay voi gia tri rac.
    // ==========================================================================================
    public class AutoDimPlineSettingsForm : Form
    {
        private readonly AutoDimPlineSettings _settings;
        private readonly Database _db;
        private readonly List<string> _layers = new List<string>();

        private TextBox _txtLinearScale;
        private ComboBox _cboLayer;
        private TextBox _txtDistance;
        private TextBox _txtSpacing;
        private TextBox _txtMinSegment;

        private CheckBox _chkAutoLayout;
        private CheckBox _chkOverall;
        private CheckBox _chkNearFeature;

        private ComboBox _cboSideBias;
        private ComboBox _cboArcMode;
        private ComboBox _cboSkewMode;

        private Label _lblError;

        public AutoDimPlineSettingsForm(AutoDimPlineSettings settings, Database db)
        {
            _settings = settings != null ? settings.Clone() : new AutoDimPlineSettings();
            _db = db ?? AcApplication.DocumentManager.MdiActiveDocument?.Database;

            LoadDrawingLayers();
            BuildUi();
            LoadSettingsToControls();
        }

        public AutoDimPlineSettings GetSettings()
        {
            return _settings.Clone();
        }

        private void LoadDrawingLayers()
        {
            if (_db == null)
            {
                return;
            }

            try
            {
                using (Transaction tr = _db.TransactionManager.StartOpenCloseTransaction())
                {
                    LayerTable lt = tr.GetObject(_db.LayerTableId, OpenMode.ForRead) as LayerTable;
                    if (lt != null)
                    {
                        foreach (ObjectId id in lt)
                        {
                            LayerTableRecord ltr = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                            if (ltr != null && !string.IsNullOrWhiteSpace(ltr.Name))
                            {
                                _layers.Add(ltr.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Khong doc duoc bang layer thi van cho nhap tay.
            }

            _layers.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void BuildUi()
        {
            Text = "AUTO DIM PLINE";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 452);
            Font = new Font("Segoe UI", 9f);

            int labelLeft = 14;
            int fieldLeft = 168;
            int fieldWidth = 228;
            int y = 16;
            const int rowStep = 30;

            AddLabel("Linear scale (DIMLFAC):", labelLeft, y + 3);
            _txtLinearScale = AddTextBox(fieldLeft, y, 90);
            y += rowStep;

            AddLabel("Dim layer:", labelLeft, y + 3);
            _cboLayer = new ComboBox
            {
                Left = fieldLeft,
                Top = y,
                Width = fieldWidth,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            foreach (string layer in _layers)
            {
                _cboLayer.Items.Add(layer);
            }
            Controls.Add(_cboLayer);
            y += rowStep;

            AddLabel("Distance from PL:", labelLeft, y + 3);
            _txtDistance = AddTextBox(fieldLeft, y, 90);
            y += rowStep;

            AddLabel("Dim spacing:", labelLeft, y + 3);
            _txtSpacing = AddTextBox(fieldLeft, y, 90);
            y += rowStep;

            AddLabel("Min segment:", labelLeft, y + 3);
            _txtMinSegment = AddTextBox(fieldLeft, y, 90);
            y += rowStep + 6;

            _chkAutoLayout = AddCheckBox("Auto layout (tu chon phia dat dim)", labelLeft, y);
            y += 26;

            _chkOverall = AddCheckBox("Create overall dimensions", labelLeft, y);
            y += 26;

            _chkNearFeature = AddCheckBox("Dim sat feature khi co the (bo cuc gon)", labelLeft, y);
            y += 32;

            AddLabel("Side preference:", labelLeft, y + 3);
            _cboSideBias = AddCombo(fieldLeft, y, fieldWidth, new[]
            {
                "Auto (thuat toan tu chon)",
                "Reverse (lat nguoc ket qua Auto)",
                "Always bottom / left",
                "Always top / right"
            });
            y += rowStep;

            AddLabel("Arc segments:", labelLeft, y + 3);
            _cboArcMode = AddCombo(fieldLeft, y, fieldWidth, new[]
            {
                "Skip (bo qua va bao lai)",
                "Radius dimension"
            });
            y += rowStep;

            AddLabel("Skew segments:", labelLeft, y + 3);
            _cboSkewMode = AddCombo(fieldLeft, y, fieldWidth, new[]
            {
                "Skip (bo qua va bao lai)",
                "Aligned dimension"
            });
            y += rowStep + 4;

            _lblError = new Label
            {
                Left = labelLeft,
                Top = y,
                Width = 392,
                Height = 34,
                ForeColor = Color.Firebrick,
                Text = string.Empty
            };
            Controls.Add(_lblError);
            y += 40;

            Button apply = new Button
            {
                Text = "APPLY",
                Left = 214,
                Top = y,
                Width = 88,
                DialogResult = DialogResult.None
            };
            apply.Click += OnApply;
            Controls.Add(apply);

            Button cancel = new Button
            {
                Text = "CANCEL",
                Left = 308,
                Top = y,
                Width = 88,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(cancel);

            AcceptButton = apply;
            CancelButton = cancel;
        }

        private Label AddLabel(string text, int left, int top)
        {
            Label label = new Label { Text = text, Left = left, Top = top, AutoSize = true };
            Controls.Add(label);
            return label;
        }

        private TextBox AddTextBox(int left, int top, int width)
        {
            TextBox box = new TextBox { Left = left, Top = top, Width = width };
            Controls.Add(box);
            return box;
        }

        private CheckBox AddCheckBox(string text, int left, int top)
        {
            CheckBox box = new CheckBox { Text = text, Left = left, Top = top, AutoSize = true };
            Controls.Add(box);
            return box;
        }

        private ComboBox AddCombo(int left, int top, int width, string[] items)
        {
            ComboBox combo = new ComboBox
            {
                Left = left,
                Top = top,
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange(items);
            Controls.Add(combo);
            return combo;
        }

        private void LoadSettingsToControls()
        {
            _txtLinearScale.Text = Format(_settings.LinearScale);
            _txtDistance.Text = Format(_settings.DistanceFromPline);
            _txtSpacing.Text = Format(_settings.DimensionSpacing);
            _txtMinSegment.Text = Format(_settings.MinSegmentLength);

            _cboLayer.Text = _settings.DimensionLayer;

            _chkAutoLayout.Checked = _settings.AutoLayout;
            _chkOverall.Checked = _settings.CreateOverall;
            _chkNearFeature.Checked = _settings.PlaceNearFeature;

            _cboSideBias.SelectedIndex = (int)_settings.SideBias;
            _cboArcMode.SelectedIndex = (int)_settings.ArcMode;
            _cboSkewMode.SelectedIndex = (int)_settings.SkewMode;
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private void OnApply(object sender, EventArgs e)
        {
            AutoDimPlineSettings parsed = new AutoDimPlineSettings
            {
                AngleToleranceDegrees = _settings.AngleToleranceDegrees,
                Verbose = _settings.Verbose,
                AutoLayout = _chkAutoLayout.Checked,
                CreateOverall = _chkOverall.Checked,
                PlaceNearFeature = _chkNearFeature.Checked,
                SideBias = (DimPlineSideBias)Math.Max(0, _cboSideBias.SelectedIndex),
                ArcMode = (DimPlineArcMode)Math.Max(0, _cboArcMode.SelectedIndex),
                SkewMode = (DimPlineSkewMode)Math.Max(0, _cboSkewMode.SelectedIndex),
                DimensionLayer = (_cboLayer.Text ?? string.Empty).Trim()
            };

            List<string> errors = new List<string>();

            double value;
            if (TryReadNumber(_txtLinearScale, "Linear scale", errors, out value)) parsed.LinearScale = value;
            if (TryReadNumber(_txtDistance, "Distance from PL", errors, out value)) parsed.DistanceFromPline = value;
            if (TryReadNumber(_txtSpacing, "Dim spacing", errors, out value)) parsed.DimensionSpacing = value;
            if (TryReadNumber(_txtMinSegment, "Min segment", errors, out value)) parsed.MinSegmentLength = value;

            errors.AddRange(parsed.Validate());

            if (errors.Count > 0)
            {
                _lblError.Text = string.Join("  ", errors.ToArray());
                return;
            }

            CopyInto(parsed, _settings);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool TryReadNumber(TextBox box, string name, List<string> errors, out double value)
        {
            if (double.TryParse(
                    (box.Text ?? string.Empty).Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }

            errors.Add(name + " phai la so.");
            return false;
        }

        private static void CopyInto(AutoDimPlineSettings source, AutoDimPlineSettings target)
        {
            target.LinearScale = source.LinearScale;
            target.DimensionLayer = source.DimensionLayer;
            target.DistanceFromPline = source.DistanceFromPline;
            target.DimensionSpacing = source.DimensionSpacing;
            target.MinSegmentLength = source.MinSegmentLength;
            target.CreateOverall = source.CreateOverall;
            target.AutoLayout = source.AutoLayout;
            target.PlaceNearFeature = source.PlaceNearFeature;
            target.SideBias = source.SideBias;
            target.ArcMode = source.ArcMode;
            target.SkewMode = source.SkewMode;
            target.AngleToleranceDegrees = source.AngleToleranceDegrees;
            target.Verbose = source.Verbose;
        }
    }
}
