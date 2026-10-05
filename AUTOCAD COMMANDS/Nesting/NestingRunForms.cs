using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AUTOCAD_COMMANDS.Nesting.Core;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// Runs the nesting core on a worker thread. Safe because Nesting/Core never touches the
    /// AutoCAD API; all AutoCAD calls stay on the main thread.
    /// </summary>
    internal sealed class NestingProgressForm : Form
    {
        private readonly INestingEngine _engine;
        private readonly NestingRequest _request;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Label _status;
        private readonly ProgressBar _bar;
        private readonly System.Windows.Forms.Timer _timer;
        private volatile string _progress = "Dang chuan bi...";

        /// <summary>Phan tram da xong; -1 = chua co so lieu (van de vach chay vo dinh).</summary>
        private volatile int _percent = -1;
        private Task<NestingResult> _task;
        private DateTime _started;

        public NestingProgressForm(INestingEngine engine, NestingRequest request)
            : this(engine, request, null)
        {
        }

        /// <param name="what">Ghi them vao tieu de (vd. "lan 2/2 - NFP") khi chay so sanh.</param>
        public NestingProgressForm(INestingEngine engine, NestingRequest request, string what)
        {
            _engine = engine;
            _request = request;

            Text = string.IsNullOrEmpty(what) ? "GHOPHOI - DANG GHEP PHOI" : "GHOPHOI - DANG GHEP PHOI (" + what + ")";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(460, 130);

            _status = new Label { Left = 12, Top = 12, Width = 436, Height = 36, Text = _progress };

            // Bat dau bang vach chay vo dinh, doi lan bao tien do dau tien la chuyen sang
            // thanh 0 -> 100 that. Trong luc chuan bi (chua chay luot nao) thi chua co so de bao.
            _bar = new ProgressBar
            {
                Left = 12,
                Top = 52,
                Width = 436,
                Height = 18,
                Style = ProgressBarStyle.Marquee,
                Minimum = 0,
                Maximum = 100
            };
            Button cancel = new Button { Text = "Dung (giu ket qua tot nhat)", Left = 248, Top = 84, Width = 200, Height = 30 };
            cancel.Click += (s, e) =>
            {
                _cts.Cancel();
                cancel.Enabled = false;
                _progress = "Dang dung...";
            };

            Controls.Add(_status);
            Controls.Add(_bar);
            Controls.Add(cancel);

            _timer = new System.Windows.Forms.Timer { Interval = 200 };
            _timer.Tick += Timer_Tick;
        }

        public NestingResult Result { get; private set; }

        public Exception Error { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _started = DateTime.Now;
            CancellationToken token = _cts.Token;
            _task = Task.Run(() => _engine.Nest(_request, token, p =>
            {
                _progress = p.Message;

                // Cac luot chay song song nen lan bao ve KHONG dam bao dung thu tu: luot thu 10
                // co the bao truoc luot thu 9. Lay gia tri LON NHAT da thay thi thanh tien trinh
                // khong bao gio lui - lui mot cai la nguoi dung tuong treo may.
                if (p.Total > 0 && p.Percent > _percent) _percent = p.Percent;
            }));
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            int percent = _percent;
            if (percent >= 0)
            {
                if (_bar.Style != ProgressBarStyle.Continuous) _bar.Style = ProgressBarStyle.Continuous;

                // Thanh tien trinh cua Windows co hieu ung truot khi tang dan; dat vot len roi
                // lui lai 1 se nhay den dung cho ngay, khong bi tre mot nhip.
                int v = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
                if (v < 100)
                {
                    _bar.Value = v + 1;
                    _bar.Value = v;
                }
                else
                {
                    _bar.Value = 100;
                }
            }

            _status.Text = string.Format("{0}\n{1}Thoi gian: {2:0} s",
                _progress,
                percent >= 0 ? percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%   " : string.Empty,
                (DateTime.Now - _started).TotalSeconds);
            if (_task == null || !_task.IsCompleted) return;

            _timer.Stop();
            if (_task.IsFaulted)
            {
                Exception ex = _task.Exception;
                Error = ex != null && ex.InnerException != null ? ex.InnerException : ex;
            }
            else
            {
                Result = _task.Result;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// ControlBox = false bo nut dong nhung KHONG chan Alt+F4. Truoc day Alt+F4 dong form khi
        /// luong ghep van chay: lenh bao "khong co ket qua", con luong ghep chay ngam tiep voi
        /// CancellationTokenSource da bi Dispose. Gio dong form trong luc dang ghep = bam "Dung":
        /// yeu cau dung, cho luong ghep ket thuc, roi form tu dong voi ket qua tot nhat da co.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_task != null && !_task.IsCompleted)
            {
                e.Cancel = true;
                if (!_cts.IsCancellationRequested) _cts.Cancel();
                _progress = "Dang dung...";
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _cts.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Shows the statistics / validation report and gates the drawing creation.</summary>
    internal sealed class NestingResultForm : Form
    {
        private readonly RadioButton[] _choices;

        public NestingResultForm(string report, bool canCreate, string blockReason, string createText = "TAO BAN VE MOI")
            : this(report, null, new[] { canCreate }, new[] { blockReason }, 0, createText)
        {
        }

        /// <summary>
        /// Che do SO SANH: <paramref name="choices"/> = ten tung phuong an; nguoi dung chon mot cai
        /// de ve. Nut tao ban ve bat / tat theo phuong an dang chon (phuong an khong qua validator
        /// thi khong ve duoc).
        /// </summary>
        public NestingResultForm(string report, string[] choices, bool[] canCreate, string[] blockReasons, int defaultChoice, string createText)
        {
            Text = "GHOPHOI - KET QUA GHEP PHOI";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(820, 560);

            TextBox box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                Text = report.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
                BackColor = SystemColors.Window
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            Button create = new Button { Text = createText, Width = 200, Height = 30, DialogResult = DialogResult.OK };
            Button close = new Button { Text = "Dong", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            Label reason = new Label
            {
                AutoSize = true,
                ForeColor = Color.DarkRed,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Padding = new Padding(0, 8, 12, 0)
            };
            buttons.Controls.Add(create);
            buttons.Controls.Add(close);
            buttons.Controls.Add(reason);

            Controls.Add(box);
            if (choices != null && choices.Length > 1)
            {
                FlowLayoutPanel pick = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8, 6, 8, 0) };
                pick.Controls.Add(new Label { Text = "Ve ket qua cua:", AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 4, 8, 0) });
                _choices = new RadioButton[choices.Length];
                for (int i = 0; i < choices.Length; i++)
                {
                    _choices[i] = new RadioButton { Text = choices[i], AutoSize = true, Checked = i == defaultChoice, Margin = new Padding(0, 2, 18, 0) };
                    pick.Controls.Add(_choices[i]);
                }

                Controls.Add(pick);
            }

            Controls.Add(buttons);
            CancelButton = close;

            // O chu nhan focus dau tien thi bi boi den ca bai - bo boi den cho de doc.
            Shown += (s, e) =>
            {
                box.SelectionStart = 0;
                box.SelectionLength = 0;
            };

            Action refresh = () =>
            {
                int k = SelectedChoice;
                bool ok = k >= 0 && k < canCreate.Length && canCreate[k];
                create.Enabled = ok;
                reason.Text = ok || blockReasons == null || k >= blockReasons.Length ? string.Empty : blockReasons[k];
                AcceptButton = ok ? create : null;
            };

            if (_choices != null)
            {
                foreach (RadioButton rb in _choices) rb.CheckedChanged += (s, e) => refresh();
            }

            refresh();
        }

        /// <summary>Phuong an duoc chon (0 khi khong co lua chon).</summary>
        public int SelectedChoice
        {
            get
            {
                if (_choices == null) return 0;
                for (int i = 0; i < _choices.Length; i++)
                {
                    if (_choices[i].Checked) return i;
                }

                return 0;
            }
        }
    }
}
