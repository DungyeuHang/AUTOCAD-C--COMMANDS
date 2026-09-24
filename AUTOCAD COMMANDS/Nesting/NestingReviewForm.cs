using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AUTOCAD_COMMANDS.Nesting.Recognition;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// Review table: Part | Quantity | Material | Status (+ size, holes, notes).
    /// Safety rules enforced here (and again in PartRecognizer.ToPartGroups):
    ///   - INVALID GEOMETRY records can never be nested; if any exist the user must tick
    ///     an explicit acknowledgement that they are left out,
    ///   - AMBIGUOUS records must be confirmed (or their SL / material edited) before continuing,
    ///   - SL must be an integer >= 1, material must look like "1.2MM".
    /// Selecting a row zooms to and highlights the geometry in AutoCAD (callback).
    /// </summary>
    internal sealed class NestingReviewForm : Form
    {
        private const int ColIndex = 0, ColName = 1, ColSize = 2, ColHoles = 3, ColQty = 4, ColMaterial = 5,
                          ColStatus = 6, ColConfirm = 7, ColInclude = 8, ColNotes = 9;

        private readonly List<RecognizedPart> _records;
        private readonly Action<RecognizedPart> _zoom;
        private readonly MetadataParser _parser = new MetadataParser(new MetadataRules());

        private DataGridView _grid;
        private CheckBox _chkAckInvalid;
        private CheckBox _chkAutoZoom;
        private Label _summary;
        private Button _btnContinue;
        private bool _loading;

        /// <summary>User changed something (SL, material, confirm, include) - cancelling asks first.</summary>
        private bool _userEdited;

        public NestingReviewForm(RecognitionResult recognition, Action<RecognizedPart> zoom, bool autoZoom)
        {
            _records = recognition.Parts;
            _zoom = zoom;
            BuildUi(recognition.GlobalWarnings, autoZoom);
            LoadRows();
            UpdateState();
        }

        public bool AutoZoom { get { return _chkAutoZoom.Checked; } }

        private void BuildUi(List<string> warnings, bool autoZoom)
        {
            Text = "GHOPHOI - KIEM TRA CHI TIET TRUOC KHI GHEP";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(1100, 640);
            MinimumSize = new Size(800, 480);

            Label intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 6, 8, 0),
                ForeColor = Color.FromArgb(0, 102, 204),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Text = "Kiem tra SL / vat lieu cua tung chi tiet. Chon dong de zoom den chi tiet trong ban ve.\n" +
                       "Co the sua truc tiep cot SL va Vat lieu. Dong MO HO phai duoc xac nhan; dong LOI HINH HOC khong the ghep."
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = SystemColors.Window,
                EditMode = DataGridViewEditMode.EditOnEnter
            };

            AddText("#", 40, true);
            AddText("Chi tiet", 150, true);
            AddText("Kich thuoc (mm)", 120, true);
            AddText("Lo", 40, true);
            AddText("SL", 55, false);
            AddText("Vat lieu", 80, false);
            AddText("Trang thai", 120, true);
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Xac nhan", Width = 65 });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Ghep", Width = 50 });
            DataGridViewTextBoxColumn notes = new DataGridViewTextBoxColumn
            {
                HeaderText = "Ghi chu",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            _grid.Columns.Add(notes);

            _grid.SelectionChanged += (s, e) => ZoomSelected(false);
            _grid.CellDoubleClick += (s, e) => ZoomSelected(true);
            _grid.CellEndEdit += Grid_CellEndEdit;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellValueChanged += Grid_CellValueChanged;

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(8) };

            ListBox warningList = new ListBox
            {
                Dock = DockStyle.Top,
                Height = 80,
                HorizontalScrollbar = true,
                IntegralHeight = false
            };
            if (warnings.Count == 0) warningList.Items.Add("(khong co canh bao chung)");
            foreach (string w in warnings) warningList.Items.Add("(!) " + w);

            Label warnTitle = new Label { Dock = DockStyle.Top, Height = 18, Text = "Canh bao chung:" };

            _summary = new Label { Dock = DockStyle.Top, Height = 22, Padding = new Padding(0, 4, 0, 0) };

            _chkAckInvalid = new CheckBox
            {
                Dock = DockStyle.Top,
                Height = 24,
                ForeColor = Color.DarkRed,
                Text = "Toi da xem va dong y BO QUA cac ban ghi LOI HINH HOC (chung se KHONG duoc ghep)."
            };
            _chkAckInvalid.CheckedChanged += (s, e) => UpdateState();

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.RightToLeft
            };

            _btnContinue = new Button { Text = "TIEP TUC >  (cai dat ghep)", Width = 200, Height = 30 };
            _btnContinue.Click += (s, e) =>
            {
                if (!ValidateAll()) return;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancel = new Button { Text = "Huy", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            Button zoom = new Button { Text = "Zoom", Width = 90, Height = 30 };
            zoom.Click += (s, e) => ZoomSelected(true);

            _chkAutoZoom = new CheckBox { Text = "Tu dong zoom khi chon dong", Checked = autoZoom, AutoSize = true, Margin = new Padding(12, 8, 12, 0) };

            buttons.Controls.Add(_btnContinue);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(zoom);
            buttons.Controls.Add(_chkAutoZoom);

            bottom.Controls.Add(_chkAckInvalid);
            bottom.Controls.Add(_summary);
            bottom.Controls.Add(warningList);
            bottom.Controls.Add(warnTitle);
            bottom.Controls.Add(buttons);

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(intro);

            AcceptButton = null;
            CancelButton = cancel;
        }

        /// <summary>
        /// Esc / Huy / window close after real edits: ask before throwing the review away
        /// (EditOnEnter keeps a cell in edit mode, so a second Esc reaches the Cancel button).
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool byUser = e.CloseReason == CloseReason.None || e.CloseReason == CloseReason.UserClosing;
            if (DialogResult != DialogResult.OK && _userEdited && byUser)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Ban da sua SL / vat lieu / xac nhan trong bang nay.\nHuy bo va mat cac thay doi?",
                    "GHOPHOI - HUY KIEM TRA", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    DialogResult = DialogResult.None;
                }
            }

            base.OnFormClosing(e);
        }

        private void AddText(string header, int width, bool readOnly)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void LoadRows()
        {
            _loading = true;
            CultureInfo ci = CultureInfo.InvariantCulture;
            foreach (RecognizedPart r in _records)
            {
                int i = _grid.Rows.Add(
                    r.Index.ToString(ci),
                    r.Name,
                    string.Format(ci, "{0:0.#} x {1:0.#}", r.Width, r.Height),
                    r.Holes.Count.ToString(ci),
                    r.Quantity.ToString(ci),
                    r.Material,
                    StatusText(r.Status),
                    r.Confirmed,
                    r.Include,
                    r.NotesText);

                DataGridViewRow row = _grid.Rows[i];
                row.Tag = r;
                if (!r.IsNestable)
                {
                    row.Cells[ColInclude].ReadOnly = true;
                    row.Cells[ColConfirm].ReadOnly = true;
                    row.Cells[ColQty].ReadOnly = true;
                    row.Cells[ColMaterial].ReadOnly = true;
                }

                if (r.Status != PartStatus.Ambiguous) row.Cells[ColConfirm].ReadOnly = true;
                Colorize(row);
            }

            _loading = false;
        }

        private static string StatusText(PartStatus s)
        {
            switch (s)
            {
                case PartStatus.Ok: return "OK";
                case PartStatus.Warning: return "WARNING";
                case PartStatus.Ambiguous: return "AMBIGUOUS";
                default: return "INVALID GEOMETRY";
            }
        }

        private static void Colorize(DataGridViewRow row)
        {
            RecognizedPart r = (RecognizedPart)row.Tag;
            Color c;
            switch (r.Status)
            {
                case PartStatus.InvalidGeometry: c = Color.FromArgb(255, 205, 205); break;
                case PartStatus.Ambiguous: c = r.Confirmed ? Color.FromArgb(220, 240, 220) : Color.FromArgb(255, 225, 170); break;
                case PartStatus.Warning: c = Color.FromArgb(255, 250, 210); break;
                default: c = Color.White; break;
            }

            if (!r.Include && r.IsNestable) c = Color.Gainsboro;
            row.DefaultCellStyle.BackColor = c;
        }

        private string NormalizeMaterialInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (v.IndexOf("MM", StringComparison.OrdinalIgnoreCase) < 0) v += "MM";
            List<MetadataFact> facts = _parser.Parse(v);
            foreach (MetadataFact f in facts)
            {
                if (f.Kind == MetadataKind.Material) return f.Value;
            }

            return null;
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0) return;
            // Error text set by ApplyValueEdit stays visible until the next valid edit.
            UpdateState();
        }

        /// <summary>
        /// Applies an SL / material edit however the value was committed (keyboard edit,
        /// paste, accessibility tools). Invalid values are reverted to the previous value with a
        /// visible row error - never accepted and never parsed blindly - and the user is never
        /// trapped in the cell (EditOnEnter + a cancelled validation would block Esc).
        /// </summary>
        private void ApplyValueEdit(DataGridViewRow row, int column)
        {
            RecognizedPart r = (RecognizedPart)row.Tag;
            string value = (Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            row.ErrorText = string.Empty;

            if (column == ColQty)
            {
                int q;
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out q) || q < 1 || q > 100000)
                {
                    SetCellSilently(row, ColQty, r.Quantity.ToString(CultureInfo.InvariantCulture));
                    row.ErrorText = "SL phai la so nguyen >= 1 - da tra ve gia tri cu";
                    return;
                }

                if (q != r.Quantity)
                {
                    r.Quantity = q;
                    MarkEdited(row, r, "SL sua tay = " + q.ToString(CultureInfo.InvariantCulture));
                }

                SetCellSilently(row, ColQty, q.ToString(CultureInfo.InvariantCulture));
            }
            else if (column == ColMaterial)
            {
                string m = NormalizeMaterialInput(value);
                if (m == null)
                {
                    SetCellSilently(row, ColMaterial, r.Material);
                    row.ErrorText = "Vat lieu phai co dang 1.2MM - da tra ve gia tri cu";
                    return;
                }

                SetCellSilently(row, ColMaterial, m);
                if (!string.Equals(m, r.Material, StringComparison.OrdinalIgnoreCase))
                {
                    r.Material = m;
                    MarkEdited(row, r, "Vat lieu sua tay = " + m);
                }
            }
        }

        private void SetCellSilently(DataGridViewRow row, int column, object value)
        {
            _loading = true;
            try
            {
                row.Cells[column].Value = value;
            }
            finally
            {
                _loading = false;
            }
        }

        private void MarkEdited(DataGridViewRow row, RecognizedPart r, string note)
        {
            _userEdited = true;
            r.Notes.Add(note);
            if (r.Status == PartStatus.Ambiguous)
            {
                r.Confirmed = true;
                _loading = true;
                row.Cells[ColConfirm].Value = true;
                _loading = false;
            }

            row.Cells[ColNotes].Value = r.NotesText;
            Colorize(row);
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0) return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            RecognizedPart r = (RecognizedPart)row.Tag;

            if (e.ColumnIndex == ColQty || e.ColumnIndex == ColMaterial)
            {
                ApplyValueEdit(row, e.ColumnIndex);
            }
            else if (e.ColumnIndex == ColInclude)
            {
                r.Include = r.IsNestable && Convert.ToBoolean(row.Cells[ColInclude].Value);
                _userEdited = true;
            }
            else if (e.ColumnIndex == ColConfirm)
            {
                r.Confirmed = Convert.ToBoolean(row.Cells[ColConfirm].Value);
                _userEdited = true;
            }

            Colorize(row);
            UpdateState();
        }

        private void ZoomSelected(bool force)
        {
            if (_zoom == null || (!force && !_chkAutoZoom.Checked)) return;
            DataGridViewRow selected = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : _grid.CurrentRow;
            if (selected == null) return;
            RecognizedPart r = selected.Tag as RecognizedPart;
            if (r == null) return;

            try
            {
                _zoom(r);
            }
            catch
            {
                // Zoom is a convenience only.
            }
        }

        private int CountWhere(Func<RecognizedPart, bool> predicate)
        {
            int n = 0;
            foreach (RecognizedPart r in _records)
            {
                if (predicate(r)) n++;
            }

            return n;
        }

        private void UpdateState()
        {
            int invalid = CountWhere(r => !r.IsNestable);
            int ambiguousOpen = CountWhere(r => r.Include && r.Status == PartStatus.Ambiguous && !r.Confirmed);
            int included = CountWhere(r => r.Include && r.IsNestable);
            int qty = 0;
            foreach (RecognizedPart r in _records)
            {
                if (r.Include && r.IsNestable) qty += r.Quantity;
            }

            _chkAckInvalid.Visible = invalid > 0;
            _summary.Text = string.Format(CultureInfo.InvariantCulture,
                "Ghep: {0} chi tiet / tong SL {1}   |   Loi hinh hoc: {2}   |   Mo ho chua xac nhan: {3}   |   Canh bao: {4}",
                included, qty, invalid, ambiguousOpen, CountWhere(r => r.Status == PartStatus.Warning));
            _summary.ForeColor = invalid > 0 || ambiguousOpen > 0 ? Color.DarkRed : Color.DarkGreen;

            _btnContinue.Enabled = included > 0 && ambiguousOpen == 0 && (invalid == 0 || _chkAckInvalid.Checked);
        }

        private bool ValidateAll()
        {
            _grid.EndEdit();
            foreach (RecognizedPart r in _records)
            {
                if (!r.Include) continue;
                if (!r.IsNestable || (r.Status == PartStatus.Ambiguous && !r.Confirmed) ||
                    r.Quantity < 1 || string.IsNullOrWhiteSpace(r.Material))
                {
                    MessageBox.Show(this, "Ban ghi " + r.Name + " chua hop le (loi / mo ho / thieu SL hoac vat lieu).",
                        "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            return true;
        }
    }
}
