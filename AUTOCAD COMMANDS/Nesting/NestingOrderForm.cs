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

        private DataGridView _grid;
        private Label _summary;
        private Button _btnContinue;
        private bool _loading;

        /// <param name="summarize">
        /// Nhan dang thu mot tap doi tuong, tra ve {so chi tiet, tong SL}. Chi de hien thi.
        /// </param>
        public NestingOrderForm(Editor editor, Func<IList<ObjectId>, int[]> summarize)
        {
            _editor = editor;
            _summarize = summarize;
            BuildUi();
            LoadRows();
            UpdateState();
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
            _orders.ApplyScan(0, ids, false);
            if (!_orders[0].Scanned) return;

            Preview(_orders[0]);
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

            Label hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(10, 8, 10, 4),
                Text = "Go ten don hang vao bang, roi bam QUET PHOI tren dong do de chon chi tiet cua don ay."
                     + Environment.NewLine
                     + "Chay mot don thi de nguyen mot dong. Ten don khong duoc de trong."
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

            _summary = new Label { Left = 150, Top = 16, Width = 560, Height = 44, AutoSize = false };

            _btnContinue = new Button { Text = "TIEP TUC", Width = 120, Height = 30, DialogResult = DialogResult.OK };
            Button btnCancel = new Button { Text = "HUY", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            _btnContinue.Click += OnContinue;

            bottom.Controls.AddRange(new Control[] { btnAdd, _summary, _btnContinue, btnCancel });
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
            Controls.Add(hint);

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

        private void OnContinue(object sender, EventArgs e)
        {
            ReadNames();

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
