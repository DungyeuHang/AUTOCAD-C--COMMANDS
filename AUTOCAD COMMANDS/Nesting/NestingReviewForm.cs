using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// Review table: Part | Quantity | Material type | Thickness | Status (+ size, holes, notes).
    /// Safety rules enforced here (and again in PartRecognizer.ToPartGroups):
    ///   - INVALID GEOMETRY records can never be nested; if any exist the user must tick
    ///     an explicit acknowledgement that they are left out,
    ///   - AMBIGUOUS records must be confirmed (or their SL / material edited) before continuing,
    ///   - SL must be an integer >= 1, material must look like "1.2MM".
    /// Selecting a row zooms to and highlights the geometry in AutoCAD (callback).
    /// </summary>
    internal sealed class NestingReviewForm : Form
    {
        private const int ColIndex = 0, ColOrder = 1, ColName = 2, ColSize = 3, ColHoles = 4, ColQty = 5,
                          ColType = 6, ColThick = 7, ColStatus = 8, ColConfirm = 9, ColInclude = 10, ColNotes = 11;

        /// <summary>Muc hien trong o "loai mac dinh" cho lua chon KHONG phan loai (chi do day).</summary>
        private const string NoTypeItem = "(khong phan loai)";

        /// <summary>Loai vat lieu goi y san (go tay loai khac van duoc).</summary>
        internal static readonly string[] KnownTypes = { "THEP", "INOX", "INOX 201", "INOX 304", "INOX 316", "INOX 430", "MA KEM", "TON LANH", "TON", "NHOM", "DONG" };

        private readonly List<RecognizedPart> _records;
        private readonly Action<RecognizedPart> _zoom;
        private MetadataParser _parser = new MetadataParser(new MetadataRules());

        /// <summary>Quy doi ten loai cua xuong ("TON=THEP"...). Xem <see cref="MetadataRules.MaterialTypeAliases"/>.</summary>
        private List<string> _aliases = new List<string>();

        /// <summary>Chi tiet nguoi dung DA sua loai bang tay - doi loai mac dinh khong dung vao.</summary>
        private readonly HashSet<RecognizedPart> _typeEdited = new HashSet<RecognizedPart>();

        private string _defaultType;
        private ComboBox _cboDefaultType;

        private DataGridView _grid;
        private CheckBox _chkAckInvalid;
        private CheckBox _chkAutoZoom;
        private Label _summary;
        private Button _btnContinue;
        private bool _loading;

        /// <summary>User changed something (SL, material, confirm, include) - cancelling asks first.</summary>
        private bool _userEdited;

        public NestingReviewForm(RecognitionResult recognition, Action<RecognizedPart> zoom, bool autoZoom)
            : this(recognition, zoom, autoZoom, string.Empty)
        {
        }

        public NestingReviewForm(RecognitionResult recognition, Action<RecognizedPart> zoom, bool autoZoom, string defaultType)
            : this(recognition, zoom, autoZoom, defaultType, null)
        {
        }

        /// <param name="defaultType">Loai vat lieu dang dung cho chi tiet ban ve khong ghi loai.</param>
        /// <param name="aliases">Quy doi ten loai dang dung ("TON=THEP"...).</param>
        public NestingReviewForm(RecognitionResult recognition, Action<RecognizedPart> zoom, bool autoZoom, string defaultType, IList<string> aliases)
        {
            _records = recognition.Parts;
            _zoom = zoom;
            _defaultType = (defaultType ?? string.Empty).Trim().ToUpperInvariant();
            SetAliases(aliases);
            BuildUi(recognition.GlobalWarnings, autoZoom);
            LoadRows();
            UpdateState();
            DialogPlacement.Attach(this, "review");
        }

        public bool AutoZoom { get { return _chkAutoZoom.Checked; } }

        /// <summary>Quy doi ten loai sau khi nguoi dung sua (luu lai cho lan sau).</summary>
        public List<string> MaterialTypeAliases { get { return new List<string>(_aliases); } }

        private void SetAliases(IEnumerable<string> aliases)
        {
            _aliases = new List<string>();
            foreach (KeyValuePair<string, string> a in MetadataParser.ParseAliases(aliases)) _aliases.Add(a.Key + "=" + a.Value);
            _parser = new MetadataParser(new MetadataRules { MaterialTypeAliases = new List<string>(_aliases) });
        }

        /// <summary>
        /// Mo bang quy doi; OK thi DOI NGAY loai cua moi dong dang co ten can quy doi (vd. moi
        /// dong "TON" thanh "THEP"). Chu MOI them vao bang (vd. "TOLE") co tac dung tu lan quet sau.
        /// </summary>
        private void EditAliases()
        {
            using (MaterialAliasForm form = new MaterialAliasForm(_aliases))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                SetAliases(form.Aliases);
            }

            int changed = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                RecognizedPart r = (RecognizedPart)row.Tag;
                string type = MaterialName.TypeOf(r.Material);
                string mapped = _parser.MapType(type);
                if (string.Equals(type, mapped, StringComparison.Ordinal)) continue;
                SetMaterial(row, r, MaterialName.WithType(r.Material, mapped));
                changed++;
            }

            string def = _parser.MapType(_defaultType);
            if (!string.Equals(def, _defaultType, StringComparison.Ordinal))
            {
                _defaultType = def;
                _cboDefaultType.Text = def.Length == 0 ? NoTypeItem : def;
            }

            UpdateState();
            if (changed > 0)
            {
                MessageBox.Show(this, "Da doi loai vat lieu cho " + changed.ToString(CultureInfo.InvariantCulture) + " dong theo bang quy doi.",
                    "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Loai vat lieu mac dinh nguoi dung chon o bang nay (rong = khong phan loai).</summary>
        public string DefaultMaterialType { get { return _defaultType; } }

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
                Text = "Kiem tra SL / loai vat lieu / do day cua tung chi tiet. Chon dong de zoom den chi tiet trong ban ve.\n" +
                       "Sua truc tiep o cot SL, Loai VL, Day. Dong MO HO phai duoc xac nhan (hoac bo tick Ghep); dong LOI HINH HOC khong the ghep."
            };

            // LOAI MAC DINH: chi tiet nao ban ve KHONG ghi loai thi theo o nay. Dat o day (khong
            // phai bang cai dat) vi day la luc nguoi dung nhin thay ket qua doi ngay tren bang.
            FlowLayoutPanel typeBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(8, 4, 8, 0),
                WrapContents = false
            };
            typeBar.Controls.Add(new Label
            {
                Text = "Loai vat lieu cho chi tiet ban ve KHONG ghi loai:",
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            });
            _cboDefaultType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 150 };
            _cboDefaultType.Items.Add(NoTypeItem);
            _cboDefaultType.Items.AddRange(KnownTypes);
            _cboDefaultType.Text = _defaultType.Length == 0 ? NoTypeItem : _defaultType;
            _cboDefaultType.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(ApplyDefaultType));
            _cboDefaultType.Leave += (s, e) => ApplyDefaultType();
            _cboDefaultType.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ApplyDefaultType();
            };
            typeBar.Controls.Add(_cboDefaultType);
            Button aliasButton = new Button { Text = "Quy doi loai...", AutoSize = true, Margin = new Padding(8, 2, 0, 0) };
            aliasButton.Click += (s, e) => EditAliases();
            typeBar.Controls.Add(aliasButton);
            typeBar.Controls.Add(new Label
            {
                Text = "(chu tren ban ve nhu \"INOX\", \"SUS304\", \"THEP\" luon thang; INOX 1.2MM va THEP 1.2MM ghep RIENG)",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(8, 6, 0, 0)
            });

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

            // Cot DON HANG: chi hien khi that su chay nhieu don. Chay mot don ma van bay ra
            // mot cot rong thi chi to chat cho, khong noi them duoc dieu gi.
            DataGridViewComboBoxColumn orderColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "Don hang",
                Width = 120,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing
            };

            List<string> orders = new List<string>();
            foreach (RecognizedPart r in _records)
            {
                if (!string.IsNullOrEmpty(r.Order) && !orders.Contains(r.Order)) orders.Add(r.Order);
            }

            orders.Sort(StringComparer.Ordinal);
            foreach (string o in orders) orderColumn.Items.Add(o);
            orderColumn.Visible = orders.Count > 0;
            _grid.Columns.Add(orderColumn);

            AddText("Chi tiet", 150, true);
            AddText("Kich thuoc (mm)", 120, true);
            AddText("Lo", 40, true);
            AddText("SL", 55, false);
            AddText("Loai VL", 90, false);
            AddText("Day", 70, false);
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
            _grid.EditingControlShowing += (s, e) =>
            {
                // Goi y loai vat lieu khi go o cot Loai VL; cot khac thi tat goi y.
                TextBox box = e.Control as TextBox;
                if (box == null) return;
                if (_grid.CurrentCell != null && _grid.CurrentCell.ColumnIndex == ColType)
                {
                    AutoCompleteStringCollection list = new AutoCompleteStringCollection();
                    list.AddRange(KnownTypes);
                    box.AutoCompleteCustomSource = list;
                    box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    box.CharacterCasing = CharacterCasing.Upper;
                }
                else
                {
                    box.AutoCompleteMode = AutoCompleteMode.None;
                    box.CharacterCasing = CharacterCasing.Normal;
                }
            };

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
                // CHOT O DANG SUA TRUOC DA: nguoi dung go SL / vat lieu / don hang xong bam
                // thang TIEP TUC thi o van con dang sua, va gia tri moi go van nam trong o
                // soan thao chu chua vao ban ghi. Khong chot thi lan ghep chay bang so CU.
                _grid.EndEdit();

                // O loai mac dinh vua go ma chua roi o (Enter / bam ra ngoai) cung phai ap dung.
                ApplyDefaultType();

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
            Controls.Add(typeBar);
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
                    r.Order,
                    r.Name,
                    string.Format(ci, "{0:0.#} x {1:0.#}", r.Width, r.Height),
                    r.Holes.Count.ToString(ci),
                    r.Quantity.ToString(ci),
                    MaterialName.TypeOf(r.Material),
                    MaterialName.ThicknessOf(r.Material),
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
                    row.Cells[ColType].ReadOnly = true;
                    row.Cells[ColThick].ReadOnly = true;
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

        /// <summary>
        /// Doc o DO DAY: "1.5", "1,5", "1.5mm" -> "1.5MM". Go ca loai ("INOX 1.5") thi tra them
        /// loai qua <paramref name="type"/>. Null = khong doc duoc do day.
        /// </summary>
        private string NormalizeThicknessInput(string value, out string type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (v.IndexOf("MM", StringComparison.OrdinalIgnoreCase) < 0) v += "MM";
            string thickness = null;
            foreach (MetadataFact f in _parser.Parse(v))
            {
                if (f.Kind == MetadataKind.Material && thickness == null) thickness = f.Value;
                if (f.Kind == MetadataKind.MaterialType && type == null) type = f.Value;
            }

            return thickness;
        }

        /// <summary>
        /// Doc o LOAI: ten quen thuoc ("inox", "sus304", "thép") -> ten chuan; ten khac thi giu
        /// nguyen (viet hoa). Rong = khong phan loai. Null = khong hop le.
        /// </summary>
        private string NormalizeTypeInput(string value)
        {
            string v = (value ?? string.Empty).Trim();
            if (v.Length == 0 || v == NoTypeItem) return string.Empty;
            string known = _parser.ParseType(v);
            if (known != null) return known;

            v = System.Text.RegularExpressions.Regex.Replace(v.ToUpperInvariant(), @"\s+", " ");
            if (v.Length > 24) return null;
            foreach (char c in v)
            {
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-') return null;
            }

            // Khong de loai trong giong mot do day ("1.2MM") - se bi tach nham.
            return MaterialName.IsThickness(v) || char.IsDigit(v[0]) ? null : v;
        }

        /// <summary>Doi loai mac dinh: chi tiet KHONG ghi loai va chua bi sua tay doi theo.</summary>
        private void ApplyDefaultType()
        {
            string type = NormalizeTypeInput(_cboDefaultType.Text);
            if (type == null)
            {
                MessageBox.Show(this, "Loai vat lieu khong hop le: \"" + _cboDefaultType.Text + "\".", "GHOPHOI",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _cboDefaultType.Text = _defaultType.Length == 0 ? NoTypeItem : _defaultType;
                return;
            }

            _cboDefaultType.Text = type.Length == 0 ? NoTypeItem : type;
            if (string.Equals(type, _defaultType, StringComparison.Ordinal)) return;

            string old = _defaultType;
            _defaultType = type;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                RecognizedPart r = (RecognizedPart)row.Tag;
                if (r.MaterialTypeFromText || _typeEdited.Contains(r)) continue;
                if (!string.Equals(MaterialName.TypeOf(r.Material), old, StringComparison.Ordinal)) continue;

                r.Material = MaterialName.WithType(r.Material, type);
                SetCellSilently(row, ColType, MaterialName.TypeOf(r.Material));
            }

            UpdateState();
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

            if (column == ColOrder)
            {
                // Sua duoc vi co ban ghi bi dinh vao hai don (duong bao ghep tu hai luot quet).
                // Bat nguoi dung quet lai ca ban ve chi vi mot chi tiet thi qua nang tay.
                if (value.Length > 0 && !string.Equals(value, r.Order, StringComparison.Ordinal))
                {
                    r.Order = value;
                    MarkEdited(row, r, "Don hang sua tay = " + value);
                }

                return;
            }

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
            else if (column == ColThick)
            {
                string typed;
                string t = NormalizeThicknessInput(value, out typed);
                if (t == null)
                {
                    SetCellSilently(row, ColThick, MaterialName.ThicknessOf(r.Material));
                    row.ErrorText = "Do day phai la so mm (vd. 1.2 hoac 1.2MM) - da tra ve gia tri cu";
                    return;
                }

                string type = typed ?? MaterialName.TypeOf(r.Material);
                if (typed != null) _typeEdited.Add(r);
                SetMaterial(row, r, MaterialName.Compose(type, t));
            }
            else if (column == ColType)
            {
                string type = NormalizeTypeInput(value);
                if (type == null)
                {
                    SetCellSilently(row, ColType, MaterialName.TypeOf(r.Material));
                    row.ErrorText = "Loai vat lieu chi gom chu / so (vd. THEP, INOX 304) - da tra ve gia tri cu";
                    return;
                }

                _typeEdited.Add(r);
                SetMaterial(row, r, MaterialName.WithType(r.Material, type));
            }
        }

        private void SetMaterial(DataGridViewRow row, RecognizedPart r, string material)
        {
            SetCellSilently(row, ColType, MaterialName.TypeOf(material));
            SetCellSilently(row, ColThick, MaterialName.ThicknessOf(material));
            if (!string.Equals(material, r.Material, StringComparison.OrdinalIgnoreCase))
            {
                r.Material = material;
                MarkEdited(row, r, "Vat lieu sua tay = " + material);
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

            if (e.ColumnIndex == ColQty || e.ColumnIndex == ColType || e.ColumnIndex == ColThick || e.ColumnIndex == ColOrder)
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
                    r.Quantity < 1 || MaterialName.ThicknessOf(r.Material).Length == 0)
                {
                    MessageBox.Show(this, "Ban ghi " + r.Name + " chua hop le (loi / mo ho / thieu SL hoac do day).",
                        "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Bang QUY DOI ten loai vat lieu cua xuong: moi dong "chu tren ban ve" -> "loai vat lieu".
    /// Vd. xuong goi thep tam la "TON": dong TON -> THEP thi moi chi tiet ghi "TON 1.2MM" duoc
    /// ghep chung voi THEP 1.2MM.
    /// </summary>
    internal sealed class MaterialAliasForm : Form
    {
        private readonly DataGridView _grid;

        public MaterialAliasForm(IEnumerable<string> aliases)
        {
            Text = "GHOPHOI - QUY DOI LOAI VAT LIEU";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(560, 380);

            Label intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 76,
                Padding = new Padding(8, 8, 8, 0),
                Text = "Xuong ban goi vat lieu khac ten chuong trinh? Them dong o day.\r\n" +
                       "Vd.  TON -> THEP  (ghi \"TON 1.2MM\" la thep 1.2),  TOLE -> THEP,  TON LANH -> MA KEM.\r\n" +
                       "Khong phan biet dau / hoa thuong. Loai co san: THEP, INOX, INOX 304, MA KEM, TON LANH, TON, NHOM, DONG.\r\n" +
                       "Xoa dong: chon dong roi bam Delete."
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chu tren ban ve (vd. TON)" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Doi thanh loai (vd. THEP)" });
            foreach (KeyValuePair<string, string> a in MetadataParser.ParseAliases(aliases)) _grid.Rows.Add(a.Key, a.Value);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            Button ok = new Button { Text = "OK", Width = 90, Height = 28, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Huy", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => _grid.EndEdit();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            Controls.Add(_grid);
            Controls.Add(buttons);
            Controls.Add(intro);
            CancelButton = cancel;
        }

        /// <summary>Cac dong hop le dang "CHU=LOAI".</summary>
        public List<string> Aliases
        {
            get
            {
                List<string> raw = new List<string>();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    raw.Add(Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) + "=" +
                            Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture));
                }

                List<string> clean = new List<string>();
                foreach (KeyValuePair<string, string> a in MetadataParser.ParseAliases(raw)) clean.Add(a.Key + "=" + a.Value);
                return clean;
            }
        }
    }
}
