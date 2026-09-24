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
        private readonly System.Windows.Forms.Timer _timer;
        private volatile string _progress = "Dang chuan bi...";
        private Task<NestingResult> _task;
        private DateTime _started;

        public NestingProgressForm(INestingEngine engine, NestingRequest request)
        {
            _engine = engine;
            _request = request;

            Text = "GHOPHOI - DANG GHEP PHOI";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(460, 130);

            _status = new Label { Left = 12, Top = 12, Width = 436, Height = 36, Text = _progress };
            ProgressBar bar = new ProgressBar { Left = 12, Top = 52, Width = 436, Height = 18, Style = ProgressBarStyle.Marquee };
            Button cancel = new Button { Text = "Dung (giu ket qua tot nhat)", Left = 248, Top = 84, Width = 200, Height = 30 };
            cancel.Click += (s, e) =>
            {
                _cts.Cancel();
                cancel.Enabled = false;
                _progress = "Dang dung...";
            };

            Controls.Add(_status);
            Controls.Add(bar);
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
            _task = Task.Run(() => _engine.Nest(_request, token, msg => _progress = msg));
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _status.Text = string.Format("{0}\nThoi gian: {1:0} s", _progress, (DateTime.Now - _started).TotalSeconds);
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
        public NestingResultForm(string report, bool canCreate, string blockReason)
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
            Button create = new Button { Text = "TAO BAN VE MOI", Width = 160, Height = 30, DialogResult = DialogResult.OK, Enabled = canCreate };
            Button close = new Button { Text = "Dong", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(create);
            buttons.Controls.Add(close);

            if (!canCreate)
            {
                buttons.Controls.Add(new Label
                {
                    Text = blockReason,
                    AutoSize = true,
                    ForeColor = Color.DarkRed,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Padding = new Padding(0, 8, 12, 0)
                });
            }

            Controls.Add(box);
            Controls.Add(buttons);
            AcceptButton = canCreate ? create : null;
            CancelButton = close;
        }
    }
}
