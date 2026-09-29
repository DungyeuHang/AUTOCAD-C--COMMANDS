using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// BANG DON HANG - buoc dau tien cua GHOPHOI khi chay nhieu don.
    ///
    /// Vi sao la mot cai BANG chu khong phai hoi lan luot "ten don?" roi "quet di" roi lai
    /// "ten don?": hoi lan luot thi ten da nhap TRUOC DO bien mat, nguoi dung quet den don thu
    /// ba khong con nhin thay hai don dau minh da quet nhung gi. Bang thi luc nao cung thay
    /// het, sua duoc, xoa duoc, quet lai duoc.
    ///
    /// Hop thoai dang modal ma van phai cho chon doi tuong trong AutoCAD: dung
    /// <see cref="Editor.StartUserInteraction(Form)"/> de tam nhuong quyen dieu khien cho ban
    /// ve roi lay lai - bang khong bi dong, khong mat du lieu dang nhap.
    ///
    /// Moi LUAT (ten trung, ten trong, quet lai, xoa dong) deu nam trong
    /// <see cref="NestingOrderList"/> de kiem thu duoc ma khong phai mo hop thoai. O day chi
    /// con phan hien thi va hoi nguoi dung.
    ///
    /// So chi tiet / SL tren bang la XEM TRUOC nhan dang tung luot quet. Con so chinh thuc la
    /// o bang KIEM TRA sau do, vi khi ghep chung ca cac don thi viec gan chu co the khac.
    /// </summary>
    internal sealed class NestingOrderForm : Form
    {
        private const int ColIndex = 0, ColName = 1, ColParts = 2, ColQty = 3, ColScan = 4, ColDelete = 5;

        private readonly NestingOrderList _orders = new NestingOrderList();
        private readonly Editor _editor;
        private readonly Func<IList<ObjectId>, int[]> _summarize;

        private readonly Action<IList<NestingOrderEntry>> _save;
        private readonly Action _clearSaved;

        private DataGridView _grid;
        private Label _hint;
        private Label _summary;
        private Button _btnContinue;
        private bool _loading;

        private const string HintText =
            "Go ten don hang vao bang, roi bam QUET PHOI tren dong do de chon chi tiet cua don ay."
            + "\r\nChay mot don thi de nguyen mot dong. Ten don khong duoc de trong.";

        /// <param name="summarize">
        /// Nhan dang thu mot tap doi tuong, tra ve {so chi tiet, tong SL}. Chi de hien thi.
        /// </param>
        public NestingOrderForm(Editor editor, Func<IList<ObjectId>, int[]> summarize)
            : this(editor, summarize, null, 0, null, null)
        {
        }

        /// <param name="restored">Bang da luu lan truoc (rong/null = bat dau moi).</param>
        /// <param name="missing">So doi tuong trong bang da luu khong con trong ban ve.</param>
        /// <param name="save">Luu bang khi dong hop thoai (ca TIEP TUC lan HUY).</param>
        /// <param name="clearSaved">Xoa ban luu (nut RESET).</param>
        public NestingOrderForm(
            Editor editor, Func<IList<ObjectId>, int[]> summarize,
            IList<NestingOrderEntry> restored, int missing,
            Action<IList<NestingOrderEntry>> save, Action clearSaved)
        {
            _editor = editor;
            _summarize = summarize;
            _save = save;
            _clearSaved = clearSaved;
            BuildUi();

            if (restored != null && restored.Count > 0)
            {
                _orders.Restore(restored);
                foreach (NestingOrderEntry o in _orders.Items)
                {
                    if (o.Scanned) Preview(o);
                }

                int objects = 0;
                foreach (NestingOrderEntry o in _orders.Items) objects += o.Ids.Count;
                _hint.Text = HintText + "\r\n" + string.Format(CultureInfo.InvariantCulture,
                    "DA NAP LAI bang lan truoc: {0} don, {1} doi tuong{2}. Sua dong nao can sua; bam RESET de xoa het.",
                    _orders.Count, objects,
                    missing > 0 ? " (" + missing.ToString(CultureInfo.InvariantCulture) + " doi tuong da bi xoa khoi ban ve - bo qua)" : string.Empty);
                _hint.ForeColor = Color.FromArgb(0, 102, 204);
            }

            LoadRows();
            UpdateState();
            DialogPlacement.Attach(this, "orders");
        }

        /// <summary>Cac don da khai, theo dung thu tu tren bang. Chi doc sau khi bam TIEP TUC.</summary>
        public IList<NestingOrderEntry> Orders { get { return _orders.Items; } }

        /// <summary>
        /// Dien san dong dau bang cac doi tuong nguoi dung da chon TRUOC khi go lenh.
        ///
        /// Ai chi chay mot don thi chon hinh roi go GHOPHOI la xong - bang hien ra da co san
        /// du lieu, chi viec bam TIEP TUC, khong bat chon lai lan nua.
        /// </summary>
        public void FillFirstRow(IEnumerable<ObjectId> ids)
        {
            // Bang lan truoc da nap lai: KHONG de len dong dau (mat luot quet cu), ma dua phan
            // vua chon vao mot dong MOI.
            int row = 0;
            if (_orders.AnyScanned)
            {
                _orders.Add();
                row = _orders.Count - 1;
            }

            _orders.ApplyScan(row, ids, false);
            if (!_orders[row].Scanned)
            {
                if (row > 0) _orders.Remove(row);
                return;
            }

            Preview(_orders[row]);
            LoadRows();
            UpdateState();
        }

        private void BuildUi()
        {
            Text = "GHOPHOI - DON HANG";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(880, 420);
            MinimumSize = new Size(700, 340);

            _hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Padding = new Padding(10, 8, 10, 4),
                Text = HintText
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window
            };

            _grid.Columns.Add(TextColumn("STT", 24, true));
            _grid.Columns.Add(TextColumn("TEN DON HANG", 170, false));
            _grid.Columns.Add(TextColumn("SO CHI TIET", 50, true));
            _grid.Columns.Add(TextColumn("SL", 34, true));
            _grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = "THAO TAC",
                Text = "QUET PHOI",
                UseColumnTextForButtonValue = true,
                FillWeight = 60
            });
            _grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = string.Empty,
                Text = "XOA",
                UseColumnTextForButtonValue = true,
                FillWeight = 34
            });

            _grid.CellContentClick += OnCellClick;
            _grid.CellEndEdit += OnCellEndEdit;

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 90 };

            Button btnAdd = new Button { Text = "+ THEM DON", Left = 10, Top = 10, Width = 130, Height = 30 };
            btnAdd.Click += OnAddOrder;

            Button btnReset = new Button { Text = "RESET", Left = 10, Top = 48, Width = 130, Height = 30 };
            btnReset.Click += OnReset;

            _summary = new Label { Left = 150, Top = 16, Width = 560, Height = 44, AutoSize = false };

            _btnContinue = new Button { Text = "TIEP TUC", Width = 120, Height = 30, DialogResult = DialogResult.OK };
            Button btnCancel = new Button { Text = "HUY", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            _btnContinue.Click += OnContinue;

            bottom.Controls.AddRange(new Control[] { btnAdd, btnReset, _summary, _btnContinue, btnCancel });
            bottom.Resize += delegate
            {
                btnCancel.Left = bottom.ClientSize.Width - btnCancel.Width - 10;
                _btnContinue.Left = btnCancel.Left - _btnContinue.Width - 8;
                btnCancel.Top = bottom.ClientSize.Height - 40;
                _btnContinue.Top = btnCancel.Top;
                _summary.Width = Math.Max(120, _btnContinue.Left - _summary.Left - 10);
            };

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(_hint);

            // Enter khong duoc coi la TIEP TUC: nguoi dung dang go ten don trong bang.
            AcceptButton = null;
            CancelButton = btnCancel;
        }

        private static DataGridViewTextBoxColumn TextColumn(string header, int weight, bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private void OnAddOrder(object sender, EventArgs e)
        {
            ReadNames();
            _orders.Add();
            LoadRows();
            UpdateState();
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading) return;
            ReadNames();
            UpdateState();
        }

        private void LoadRows()
        {
            _loading = true;
            try
            {
                _grid.Rows.Clear();
                for (int i = 0; i < _orders.Count; i++)
                {
                    NestingOrderEntry o = _orders[i];
                    int row = _grid.Rows.Add(
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        o.Name,
                        o.Scanned ? o.PartCount.ToString(CultureInfo.InvariantCulture) : "-",
                        o.Scanned ? o.Quantity.ToString(CultureInfo.InvariantCulture) : "-",
                        o.Scanned ? "QUET LAI" : "QUET PHOI",
                        "XOA");

                    _grid.Rows[row].Cells[ColIndex].Style.BackColor = SystemColors.Control;
                    _grid.Rows[row].Cells[ColParts].Style.BackColor = SystemColors.Control;
                    _grid.Rows[row].Cells[ColQty].Style.BackColor = SystemColors.Control;
                }
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Lay ten tu bang vao mo hinh - bang moi la cho nguoi dung go.</summary>
        private void ReadNames()
        {
            // CHOT O DANG SUA TRUOC DA. Nguoi dung go ten don xong bam thang TIEP TUC (hoac
            // QUET PHOI) thi o van con dang sua: chu moi go nam trong o soan thao, con
            // Cells[...].Value van la gia tri CU. Doc ngay luc do la lay nham ten cu - quet
            // nham don, hoac bao "chua co ten" du nguoi dung vua go xong.
            _grid.EndEdit();

            for (int i = 0; i < _orders.Count && i < _grid.Rows.Count; i++)
            {
                object v = _grid.Rows[i].Cells[ColName].Value;
                _orders.SetName(i, v == null ? string.Empty : v.ToString());
            }
        }

        private void OnCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _orders.Count) return;
            ReadNames();

            if (e.ColumnIndex == ColDelete)
            {
                Delete(e.RowIndex);
                return;
            }

            if (e.ColumnIndex == ColScan) Scan(e.RowIndex);
        }

        private void Delete(int index)
        {
            NestingOrderEntry o = _orders[index];
            if (o.Scanned)
            {
                string question = string.Format(CultureInfo.InvariantCulture,
                    "Xoa don \"{0}\" cung {1} doi tuong da quet?", o.Name, o.Ids.Count);
                if (MessageBox.Show(question, "GHOPHOI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
            }

            _orders.Remove(index);
            LoadRows();
            UpdateState();
        }

        private void Scan(int index)
        {
            string why = _orders.WhyCannotScan(index);
            if (why != null)
            {
                Warn(why);
                return;
            }

            NestingOrderEntry order = _orders[index];

            // Da quet roi thi KHONG tu y thay the: hoi ro la them vao hay quet lai tu dau.
            bool append = false;
            if (order.Scanned)
            {
                string question = string.Format(CultureInfo.InvariantCulture,
                    "Don \"{0}\" da co {1} doi tuong.{2}{2}CO    = quet THEM vao don nay{2}KHONG = bo het va quet LAI tu dau{2}HUY   = khong lam gi",
                    order.Name, order.Ids.Count, Environment.NewLine);
                DialogResult answer = MessageBox.Show(question, "GHOPHOI", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                append = answer == DialogResult.Yes;
            }

            ObjectId[] picked = Pick(order.Name);
            if (picked == null || picked.Length == 0) return;

            _orders.ApplyScan(index, picked, append);
            Preview(order);
            LoadRows();
            UpdateState();
        }

        private void Preview(NestingOrderEntry order)
        {
            int[] counts = _summarize != null ? _summarize(order.Ids) : null;
            order.PartCount = counts != null && counts.Length > 0 ? counts[0] : 0;
            order.Quantity = counts != null && counts.Length > 1 ? counts[1] : 0;
        }

        /// <summary>
        /// Tam nhuong quyen dieu khien cho ban ve de nguoi dung chon doi tuong, roi lay lai.
        /// Bang van con nguyen, khong mat gi.
        /// </summary>
        private ObjectId[] Pick(string orderName)
        {
            using (EditorUserInteraction interaction = _editor.StartUserInteraction(this))
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = Environment.NewLine + "GHOPHOI [" + orderName + "] - chon chi tiet cua don nay: "
                };

                PromptSelectionResult sel = _editor.GetSelection(options);
                interaction.End();
                return sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds() : null;
            }
        }

        private void UpdateState()
        {
            int scanned = 0, parts = 0, qty = 0;
            foreach (NestingOrderEntry o in _orders.Items)
            {
                if (!o.Scanned) continue;
                scanned++;
                parts += o.PartCount;
                qty += o.Quantity;
            }

            _summary.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} don da quet / {1} dong. Tam tinh: {2} chi tiet, tong SL {3}.{4}Con so chinh thuc o bang KIEM TRA.",
                scanned, _orders.Count, parts, qty, Environment.NewLine);

            _btnContinue.Enabled = scanned > 0;
        }

        private void OnReset(object sender, EventArgs e)
        {
            if (MessageBox.Show("Xoa HET bang don hang, cac phoi da quet va cac chinh sua o bang KIEM TRA (ca ban da luu)?", "GHOPHOI",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _orders.Reset();
            if (_clearSaved != null) _clearSaved();
            _hint.Text = HintText;
            _hint.ForeColor = SystemColors.ControlText;
            LoadRows();
            UpdateState();
        }

        /// <summary>
        /// Luu bang TRUOC khi chot (chot se bo dong chua quet va xoa ten khi chi co mot don) -
        /// lan sau mo lai phai thay dung bang nguoi dung da go.
        /// </summary>
        private void SaveTable()
        {
            if (_save == null) return;
            try
            {
                _save(new List<NestingOrderEntry>(_orders.Items));
            }
            catch (Exception)
            {
                // luu bang chi la tien ich - hong thi thoi, khong duoc lam hong lenh
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // TIEP TUC da luu trong OnContinue (truoc khi chot). HUY / nut X thi luu o day.
            if (DialogResult != DialogResult.OK)
            {
                ReadNames();
                SaveTable();
            }

            base.OnFormClosing(e);
        }

        private void OnContinue(object sender, EventArgs e)
        {
            ReadNames();
            SaveTable();

            string error = _orders.Finalize();
            if (error == null) return;

            Warn(error);
            DialogResult = DialogResult.None;
        }

        private static void Warn(string message)
        {
            MessageBox.Show(message, "GHOPHOI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
