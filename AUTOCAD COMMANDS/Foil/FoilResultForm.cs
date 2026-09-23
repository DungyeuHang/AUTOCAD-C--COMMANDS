using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Font = System.Drawing.Font;

namespace AUTOCAD_COMMANDS
{
    /// <summary>
    /// Bang ket qua + xem truoc cua DX_FOIL. Nguoi dung kiem tra so lieu roi moi bam TAO PHOI;
    /// truoc khi bam thi ban ve chua he bi thay doi.
    /// </summary>
    public class FoilResultForm : Form
    {
        private readonly FoilProfile _profile;

        /// <summary>
        /// Tinh lai ket qua theo mot che do bu be day khac. Do tang lenh cung cap de form
        /// khong phai biet gi ve AutoCAD hay ve chien luoc tinh.
        /// </summary>
        private readonly Func<FoilThicknessCompensationMode, FoilFlatPatternResult> _recompute;

        private FoilFlatPatternResult _result;
        private ComboBox _cboCompensation;
        private bool _suppressCompensationEvent;

        private Label _lblSummary;
        private TextBox _txtDetails;
        private FoilPreviewPanel _preview;
        private FoilStepPreviewPanel _stepPreview;
        private GroupBox _grpSteps;
        private Button _btnCreate;

        /// <summary>Ket qua nguoi dung thuc su chon (sau khi doi che do bu be day).</summary>
        public FoilFlatPatternResult SelectedResult { get { return _result; } }

        public FoilResultForm(
            FoilFlatPatternResult result,
            FoilProfile profile,
            Func<FoilThicknessCompensationMode, FoilFlatPatternResult> recompute = null)
        {
            _result = result;
            _profile = profile;
            _recompute = recompute;

            InitializeComponent();
            FillCompensationChoices();
            FillContent();
        }

        private void InitializeComponent()
        {
            Text = "DX_FOIL - KET QUA TRIEN KHAI PHOI";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(780, 800);
            Size = new Size(860, 900);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            Controls.Add(main);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 185f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            main.Controls.Add(layout);

            // ---- 1. Tom tat + chon che do bu be day ----
            GroupBox grpSummary = new GroupBox
            {
                Text = "TOM TAT",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            TableLayoutPanel summaryLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _lblSummary = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Font = new Font("Consolas", 9.5F, FontStyle.Regular)
            };
            summaryLayout.Controls.Add(_lblSummary, 0, 0);

            TableLayoutPanel compLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            compLayout.Controls.Add(new Label
            {
                Text = "Bu be day tai goc:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 7, 3, 3),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            }, 0, 0);

            _cboCompensation = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F, FontStyle.Regular)
            };
            _cboCompensation.SelectedIndexChanged += OnCompensationChanged;
            compLayout.Controls.Add(_cboCompensation, 1, 0);

            summaryLayout.Controls.Add(compLayout, 0, 1);
            grpSummary.Controls.Add(summaryLayout);
            layout.Controls.Add(grpSummary, 0, 0);

            // ---- 2. Xem truoc ----
            GroupBox grpPreview = new GroupBox
            {
                Text = "XEM TRUOC  (trai: bien dang goc   -   phai: phoi da trai phang)",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 16, 8, 8),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _preview = new FoilPreviewPanel { Dock = DockStyle.Fill };
            grpPreview.Controls.Add(_preview);
            layout.Controls.Add(grpPreview, 0, 1);

            // ---- 3. Trinh tu cac buoc chan ----
            _grpSteps = new GroupBox
            {
                Text = "CAC BUOC CHAN",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 16, 8, 8),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _stepPreview = new FoilStepPreviewPanel { Dock = DockStyle.Fill };
            _grpSteps.Controls.Add(_stepPreview);
            layout.Controls.Add(_grpSteps, 0, 2);

            // ---- 4. Chi tiet ----
            GroupBox grpDetails = new GroupBox
            {
                Text = "CHI TIET TINH TOAN",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 16, 8, 8),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _txtDetails = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                BackColor = Color.White
            };
            grpDetails.Controls.Add(_txtDetails);
            layout.Controls.Add(grpDetails, 0, 3);

            // ---- Nut lenh ----
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };

            Button btnCancel = new Button
            {
                Text = "Huy",
                DialogResult = DialogResult.Cancel,
                Width = 110,
                Height = 34
            };

            _btnCreate = new Button
            {
                Text = "TAO PHOI",
                DialogResult = DialogResult.OK,
                Width = 160,
                Height = 34,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            Button btnCopy = new Button { Text = "Copy ket qua", Width = 130, Height = 34 };
            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_txtDetails.Text);
                }
                catch
                {
                }
            };

            buttons.Controls.Add(_btnCreate);
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnCopy);
            Controls.Add(buttons);

            AcceptButton = _btnCreate;
            CancelButton = btnCancel;
        }

        /// <summary>
        /// Tinh san CA BON phuong an bu be day va hien chieu rong phoi cua tung phuong an,
        /// de nguoi dung doi chieu voi so lieu tinh tay roi chon dung, khong phai doan.
        /// </summary>
        private void FillCompensationChoices()
        {
            FoilSettings s = _result.Settings ?? new FoilSettings();
            string f = s.DisplayFormat;
            CultureInfo ci = CultureInfo.InvariantCulture;

            FoilThicknessCompensationMode[] modes =
            {
                FoilThicknessCompensationMode.None,
                FoilThicknessCompensationMode.TurnLeft,
                FoilThicknessCompensationMode.TurnRight,
                FoilThicknessCompensationMode.AllBends
            };

            string[] labels =
            {
                "Khong cong",
                "Mot phia  (A)",
                "Mot phia  (B - dao nguoc)",
                "TAT CA cac goc"
            };

            _suppressCompensationEvent = true;
            _cboCompensation.Items.Clear();

            int selected = 0;
            for (int i = 0; i < modes.Length; i++)
            {
                FoilFlatPatternResult r = ResultFor(modes[i]);
                string width = r != null && r.IsUsable
                    ? r.BlankWidth.ToString(f, ci) + " mm"
                    : "(khong tinh duoc)";
                int circles = r == null ? 0 : CountCompensated(r);

                _cboCompensation.Items.Add(new CompensationChoice
                {
                    Mode = modes[i],
                    Text = string.Format(
                        ci, "{0,-26} -> phoi {1,-12} ({2} vong tron)", labels[i], width, circles)
                });

                if (modes[i] == s.ThicknessCompensation)
                {
                    selected = i;
                }
            }

            _cboCompensation.SelectedIndex = selected;
            _cboCompensation.Enabled = _recompute != null;
            _suppressCompensationEvent = false;
        }

        private FoilFlatPatternResult ResultFor(FoilThicknessCompensationMode mode)
        {
            FoilSettings s = _result.Settings ?? new FoilSettings();
            if (s.ThicknessCompensation == mode)
            {
                return _result;
            }

            if (_recompute == null)
            {
                return null;
            }

            try
            {
                return _recompute(mode);
            }
            catch
            {
                return null;
            }
        }

        private static int CountCompensated(FoilFlatPatternResult result)
        {
            int count = 0;
            foreach (FoilBendInfo b in result.Bends)
            {
                if (b.IsThicknessCompensated)
                {
                    count++;
                }
            }

            return count;
        }

        private void OnCompensationChanged(object sender, EventArgs e)
        {
            if (_suppressCompensationEvent)
            {
                return;
            }

            CompensationChoice choice = _cboCompensation.SelectedItem as CompensationChoice;
            if (choice == null)
            {
                return;
            }

            FoilFlatPatternResult updated = ResultFor(choice.Mode);
            if (updated == null || !updated.IsUsable)
            {
                return;
            }

            _result = updated;
            FillContent();
        }

        private sealed class CompensationChoice
        {
            public FoilThicknessCompensationMode Mode { get; set; }

            public string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private void FillContent()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            FoilSettings s = _result.Settings ?? new FoilSettings();
            string f = s.DisplayFormat;

            int up = 0;
            int down = 0;
            foreach (FoilBendInfo b in _result.Bends)
            {
                if (b.Direction == FoilBendDirection.Up) up++;
                else down++;
            }

            _lblSummary.Text = string.Format(
                ci,
                "Chieu dai phoi   : {0} mm        So duong chan : {2}  ({3} UP / {4} DOWN)" + Environment.NewLine +
                "Chieu rong phoi  : {1} mm        Phuong phap   : {5}" + Environment.NewLine +
                "Tong bu chan     : {6} mm        Layer duong chan: {7}",
                _result.BlankLength.ToString(f, ci),
                _result.BlankWidth.ToString(f, ci),
                _result.BendCount,
                up,
                down,
                _result.MethodName,
                _result.TotalDeduction.ToString(f, ci),
                s.BendLayerName);

            _txtDetails.Text = _result.FormatReport().Replace("\n", Environment.NewLine);

            // TextBox multiline mac dinh boi den toan bo khi nhan focus - bo chon di cho de doc.
            _txtDetails.SelectionStart = 0;
            _txtDetails.SelectionLength = 0;

            _preview.SetData(_result, _profile);

            FoilBendPlan plan = FoilBendSequenceBuilder.Plan(_result, s);
            _stepPreview.SetData(plan);

            List<string> orderNames = new List<string>();
            foreach (int index in plan.Order) orderNames.Add("#" + index.ToString(ci));

            _grpSteps.Text = string.Format(
                ci,
                "CAC BUOC CHAN  ({0} buoc)   thu tu: {1}   lat ton {2} | doi dau {3} | thay dao {4}{5}",
                Math.Max(plan.Steps.Count - 1, 0),
                orderNames.Count > 0 ? string.Join(" > ", orderNames.ToArray()) : "-",
                plan.FlipCount,
                plan.TurnCount,
                plan.ToolChanges,
                plan.AllFeasible ? string.Empty : "   (*) co buoc can luu y ve dung cu");

            if (_result.Warnings.Count > 0)
            {
                _lblSummary.ForeColor = Color.FromArgb(180, 90, 0);
            }

            if (!_result.IsUsable)
            {
                _btnCreate.Enabled = false;
            }
        }
    }

    /// <summary>
    /// Ve day hinh trinh tu chan, MOI O LA MOT LAN DAT PHOI LEN MAY:
    /// coi va chay dao ve mo phia sau, mat cat chi tiet ve dam phia tren, dung HE TOA DO MAY
    /// nen nhin vao la biet phai dat phoi theo chieu nao.
    /// Buoc nao mo hinh bao khong chan duoc thi o do duoc to vien do.
    /// </summary>
    public class FoilStepPreviewPanel : Panel
    {
        private FoilBendPlan _plan;
        private List<List<FoilPoint2d>> _shapes = new List<List<FoilPoint2d>>();
        private List<List<List<FoilPoint2d>>> _tools = new List<List<List<FoilPoint2d>>>();

        private double _scale = 1.0;
        private double _left, _right, _down, _up;
        private int _cellWidth = 140;

        public FoilStepPreviewPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(28, 28, 28);
            AutoScroll = true;
        }

        public void SetData(FoilBendPlan plan)
        {
            _plan = plan;
            _shapes.Clear();
            _tools.Clear();

            if (plan != null && plan.Steps.Count > 0)
            {
                foreach (FoilBendStep step in plan.Steps)
                {
                    _shapes.Add(FoilBendSequenceBuilder.Simplify(step.MachinePoints));
                    List<List<FoilPoint2d>> parts = new List<List<FoilPoint2d>>();
                    foreach (FoilToolShape tool in step.Tools)
                    {
                        if (tool != null) parts.Add(tool.Outline);
                    }

                    _tools.Add(parts);
                }
            }

            RecomputeLayout();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RecomputeLayout();
        }

        /// <summary>
        /// Tinh ty le va be rong o MOT LAN o day, KHONG lam trong OnPaint.
        /// Gan AutoScrollMinSize trong OnPaint se kich hoat layout lai giua luc dang ve,
        /// gay vong lap paint/layout va co the cham vao control da bi dispose.
        /// </summary>
        private void RecomputeLayout()
        {
            if (_plan == null || _shapes.Count == 0)
            {
                AutoScrollMinSize = Size.Empty;
                return;
            }

            _left = _right = _down = _up = 0.0;
            for (int i = 0; i < _shapes.Count; i++)
            {
                Measure(_shapes[i]);
                foreach (List<FoilPoint2d> part in _tools[i])
                {
                    Measure(part);
                }
            }

            double width = _left + _right;
            double height = _down + _up;
            if (width <= 1e-9)
            {
                AutoScrollMinSize = Size.Empty;
                return;
            }

            const int cellPadding = 10;
            const int captionHeight = 30;
            const int maxCellWidth = 260;
            const int minCellWidth = 130;

            int cellHeight = Math.Max(ClientSize.Height - 20, 40);
            int drawHeight = Math.Max(cellHeight - captionHeight - cellPadding, 20);

            // MOT ty le duy nhat cho moi buoc de so sanh duoc bang mat.
            double scaleByHeight = height > 1e-9 ? (drawHeight - cellPadding) / height : double.MaxValue;
            double scaleByWidth = (maxCellWidth - 2.0 * cellPadding) / width;

            double scale = Math.Min(scaleByHeight, scaleByWidth);
            if (scale <= 0.0 || double.IsInfinity(scale) || double.IsNaN(scale))
            {
                scale = 1.0;
            }

            _scale = scale;
            _cellWidth = Math.Max(
                (int)Math.Ceiling(width * scale) + 2 * cellPadding, minCellWidth);

            Size required = new Size(_cellWidth * _shapes.Count + 8, 0);
            if (AutoScrollMinSize != required)
            {
                AutoScrollMinSize = required;
            }
        }

        private void Measure(List<FoilPoint2d> points)
        {
            foreach (FoilPoint2d p in points)
            {
                if (-p.X > _left) _left = -p.X;
                if (p.X > _right) _right = p.X;
                if (-p.Y > _down) _down = -p.Y;
                if (p.Y > _up) _up = p.Y;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_plan == null || _shapes.Count == 0)
            {
                return;
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int cellPadding = 10;
            const int captionHeight = 30;

            int cellHeight = Math.Max(ClientSize.Height - 20, 40);
            int drawHeight = Math.Max(cellHeight - captionHeight - cellPadding, 20);

            double scale = _scale;
            int cellWidth = _cellWidth;
            int scrollX = AutoScrollPosition.X;

            using (Font captionFont = new Font("Segoe UI", 7.5F))
            using (Font detailFont = new Font("Segoe UI", 6.75F))
            using (Brush captionBrush = new SolidBrush(Color.FromArgb(215, 215, 215)))
            using (Brush detailBrush = new SolidBrush(Color.FromArgb(140, 140, 140)))
            using (Brush badBrush = new SolidBrush(Color.FromArgb(245, 170, 80)))
            using (Pen separator = new Pen(Color.FromArgb(60, 60, 60)))
            using (Pen toolPen = new Pen(Color.FromArgb(95, 95, 95), 1.3f))
            using (StringFormat clip = new StringFormat(StringFormatFlags.NoWrap)
            {
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                for (int i = 0; i < _shapes.Count; i++)
                {
                    FoilBendStep step = _plan.Steps[i];
                    int cellX = scrollX + i * cellWidth;
                    if (cellX > Width || cellX + cellWidth < 0)
                    {
                        continue;
                    }

                    if (i > 0)
                    {
                        g.DrawLine(separator, cellX, 4, cellX, cellHeight);
                    }

                    // Goc toa do may (dinh goc chan) nam cung mot cho o moi o.
                    float slack = (float)((cellWidth - (_left + _right) * scale) / 2.0);
                    float originX = cellX + slack + (float)(_left * scale);
                    float originY = captionHeight + (float)((drawHeight - (_down + _up) * scale) / 2.0)
                                    + (float)(_up * scale);

                    foreach (List<FoilPoint2d> part in _tools[i])
                    {
                        DrawChain(g, toolPen, part, scale, originX, originY, true);
                    }

                    Color color = step.FormedBend == null
                        ? Color.FromArgb(150, 200, 255)
                        : (step.Feasible ? Color.FromArgb(240, 220, 90) : Color.FromArgb(245, 160, 60));

                    using (Pen pen = new Pen(color, 1.8f))
                    {
                        DrawChain(g, pen, _shapes[i], scale, originX, originY);
                    }

                    if (step.FormedBend != null)
                    {
                        Color markerColor = step.FormedBend.Direction == FoilBendDirection.Up
                            ? Color.FromArgb(255, 80, 80)
                            : Color.FromArgb(170, 170, 170);

                        using (Brush brush = new SolidBrush(markerColor))
                        {
                            g.FillEllipse(brush, originX - 3.5f, originY - 3.5f, 7f, 7f);
                        }
                    }

                    string caption = step.FormedBend == null
                        ? "B0  phoi phang"
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "B{0}  #{1} {2}{3}",
                            step.StepNumber,
                            step.FormedBend.Index,
                            step.FormedBend.Direction == FoilBendDirection.Up ? "UP" : "DOWN",
                            step.FlipChanged ? "  LAT" : string.Empty);

                    // Chu phai duoc KEP trong o cua no, neu khong se tran sang o ben canh.
                    RectangleF captionBox = new RectangleF(cellX + 5, 2, cellWidth - 10, 13);
                    RectangleF detailBox = new RectangleF(cellX + 5, 15, cellWidth - 10, 13);

                    g.DrawString(caption, captionFont,
                        step.Feasible ? captionBrush : badBrush, captionBox, clip);

                    if (!string.IsNullOrEmpty(step.Detail))
                    {
                        g.DrawString(step.Detail, detailFont, detailBrush, detailBox, clip);
                    }
                }
            }
        }

        private static void DrawChain(
            Graphics g, Pen pen, List<FoilPoint2d> points, double scale, float originX, float originY)
        {
            DrawChain(g, pen, points, scale, originX, originY, false);
        }

        private static void DrawChain(
            Graphics g, Pen pen, List<FoilPoint2d> points,
            double scale, float originX, float originY, bool closed)
        {
            for (int k = 0; k < points.Count - 1; k++)
            {
                g.DrawLine(pen,
                    ToScreen(points[k], scale, originX, originY),
                    ToScreen(points[k + 1], scale, originX, originY));
            }

            if (closed && points.Count > 2)
            {
                g.DrawLine(pen,
                    ToScreen(points[points.Count - 1], scale, originX, originY),
                    ToScreen(points[0], scale, originX, originY));
            }
        }

        private static PointF ToScreen(FoilPoint2d p, double scale, float originX, float originY)
        {
            return new PointF(
                originX + (float)(p.X * scale),
                originY - (float)(p.Y * scale));
        }
    }

    /// <summary>
    /// Ve xem truoc bang GDI+: bien dang goc ben trai, phoi va cac duong chan ben phai.
    /// Chi la hinh minh hoa de kiem tra bang mat - hinh hoc that do FoilFlatPatternGeometry sinh.
    /// </summary>
    public class FoilPreviewPanel : Panel
    {
        private FoilFlatPatternResult _result;
        private FoilProfile _profile;

        public FoilPreviewPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(28, 28, 28);
        }

        public void SetData(FoilFlatPatternResult result, FoilProfile profile)
        {
            _result = result;
            _profile = profile;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_result == null)
            {
                return;
            }

            int half = Width / 2;
            Rectangle leftArea = new Rectangle(8, 8, half - 16, Height - 16);
            Rectangle rightArea = new Rectangle(half + 8, 8, half - 16, Height - 16);

            using (Pen divider = new Pen(Color.FromArgb(70, 70, 70)))
            {
                g.DrawLine(divider, half, 4, half, Height - 4);
            }

            DrawProfile(g, leftArea);
            DrawBlank(g, rightArea);
        }

        private void DrawProfile(Graphics g, Rectangle area)
        {
            if (_profile == null || _profile.SegmentCount == 0)
            {
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (FoilProfileSegment seg in _profile.Segments)
            {
                Expand(seg.Start, ref minX, ref minY, ref maxX, ref maxY);
                Expand(seg.End, ref minX, ref minY, ref maxX, ref maxY);
            }

            double w = Math.Max(maxX - minX, 1e-9);
            double h = Math.Max(maxY - minY, 1e-9);
            double scale = Math.Min((area.Width - 24) / w, (area.Height - 34) / h);
            if (scale <= 0.0 || double.IsInfinity(scale))
            {
                return;
            }

            float offsetX = area.Left + (float)((area.Width - w * scale) / 2.0);
            float offsetY = area.Top + (float)((area.Height - h * scale) / 2.0) + 10f;

            using (Pen pen = new Pen(Color.FromArgb(240, 220, 90), 2f))
            {
                foreach (FoilProfileSegment seg in _profile.Segments)
                {
                    // Cung duoc ve xap xi bang cac doan nho - day chi la hinh xem truoc.
                    int steps = seg.IsArc ? 16 : 1;
                    FoilPoint2d previous = seg.Start;
                    for (int i = 1; i <= steps; i++)
                    {
                        FoilPoint2d current = seg.IsArc
                            ? PointOnArc(seg, (double)i / steps)
                            : seg.End;

                        g.DrawLine(pen,
                            ToScreen(previous, minX, maxY, scale, offsetX, offsetY),
                            ToScreen(current, minX, maxY, scale, offsetX, offsetY));
                        previous = current;
                    }
                }
            }

            using (Brush brush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (Font font = new Font("Segoe UI", 8F))
            {
                g.DrawString("BIEN DANG GOC", font, brush, area.Left + 4, area.Top);
            }
        }

        private void DrawBlank(Graphics g, Rectangle area)
        {
            double length = _result.BlankLength;
            double width = _result.BlankWidth;
            if (length <= 0.0 || width <= 0.0)
            {
                return;
            }

            double scale = Math.Min((area.Width - 24) / length, (area.Height - 44) / width);
            if (scale <= 0.0 || double.IsInfinity(scale))
            {
                return;
            }

            float blankW = (float)(length * scale);
            float blankH = (float)(width * scale);
            float x0 = area.Left + (area.Width - blankW) / 2f;
            float y0 = area.Top + (area.Height - blankH) / 2f + 10f;

            using (Pen pen = new Pen(Color.FromArgb(230, 230, 230), 1.6f))
            {
                g.DrawRectangle(pen, x0, y0, blankW, blankH);
            }

            float lastLabelY = float.MinValue;

            foreach (FoilBendInfo bend in _result.Bends)
            {
                // V = 0 ve o DAY hinh chu nhat, nen doi truc khi ve len man hinh.
                float y = y0 + blankH - (float)(bend.FlatPosition * scale);

                Color color = bend.Direction == FoilBendDirection.Up
                    ? Color.FromArgb(255, 80, 80)
                    : Color.FromArgb(150, 150, 150);

                using (Pen pen = new Pen(color, 1.4f))
                {
                    if (bend.Direction == FoilBendDirection.Down)
                    {
                        pen.DashStyle = DashStyle.Dash;
                    }

                    g.DrawLine(pen, x0, y, x0 + blankW, y);
                }

                // Duong chan da duoc CONG BE DAY (goc lom) duoc khoanh tron, dung quy uoc
                // danh dau tay cua xuong - de kiem tra ngay co khoanh dung phia khong.
                if (bend.IsThicknessCompensated)
                {
                    using (Pen markPen = new Pen(color, 1.2f))
                    {
                        markPen.DashStyle = DashStyle.Dash;
                        float cx = x0 + blankW * 0.5f;
                        g.DrawEllipse(markPen, cx - 7f, y - 7f, 14f, 14f);
                    }
                }

                // Chi ghi so thu tu khi con du cho, tranh cac nhan de len nhau.
                if (Math.Abs(y - lastLabelY) < 10f)
                {
                    continue;
                }

                lastLabelY = y;
                using (Brush brush = new SolidBrush(color))
                using (Font font = new Font("Segoe UI", 7F))
                {
                    g.DrawString(
                        "#" + bend.Index.ToString(CultureInfo.InvariantCulture),
                        font, brush, x0 - 18f, y - 7f);
                }
            }

            using (Brush brush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (Font font = new Font("Segoe UI", 8F))
            {
                string caption = string.Format(
                    CultureInfo.InvariantCulture,
                    "PHOI  {0:0.##} x {1:0.##}   (do = UP, xam dut = DOWN)",
                    length, width);
                g.DrawString(caption, font, brush, area.Left + 4, area.Top);
            }
        }

        private static FoilPoint2d PointOnArc(FoilProfileSegment seg, double t)
        {
            FoilVector2d toStart = seg.Start - seg.Center;
            double startAngle = toStart.AngleRad;
            double angle = startAngle + seg.SweepAngle * t;
            return new FoilPoint2d(
                seg.Center.X + seg.Radius * Math.Cos(angle),
                seg.Center.Y + seg.Radius * Math.Sin(angle));
        }

        private static void Expand(
            FoilPoint2d p, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        private static PointF ToScreen(
            FoilPoint2d p, double minX, double maxY, double scale, float offsetX, float offsetY)
        {
            return new PointF(
                offsetX + (float)((p.X - minX) * scale),
                offsetY + (float)((maxY - p.Y) * scale));
        }
    }
}
