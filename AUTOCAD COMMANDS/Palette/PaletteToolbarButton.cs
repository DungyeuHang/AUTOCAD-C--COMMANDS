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

    // Nút toolbar custom kiểu glossy.
    // Dùng cho Reload LISP, Add Source, Reset Stats...
    internal sealed class PaletteToolbarButton : WF.Button
    {
        private static readonly Color ForeColorNormal = Color.FromArgb(242, 242, 244);
        private static readonly Color NormalBgColor = Color.FromArgb(40, 46, 58);
        private static readonly Color HoverBgColor = Color.FromArgb(80, 90, 112);
        private static readonly Color DisabledBgColor = Color.FromArgb(34, 34, 36);
        private static readonly Color DisabledForeColor = Color.FromArgb(132, 132, 136);
        private static readonly Color ShadowColor = Color.Black;
        private bool _hovered;
        private bool _pressed;

        public PaletteToolbarButton()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            DoubleBuffered = true;
            AutoSize = true;
            AutoSizeMode = WF.AutoSizeMode.GrowAndShrink;
            Margin = new WF.Padding(0, 0, 8, 0);
            Padding = new WF.Padding(14, 5, 14, 5);
            MinimumSize = new Size(76, 30);
            Cursor = WF.Cursors.Hand;
            FlatStyle = WF.FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = ForeColorNormal;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(WF.MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == WF.MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(WF.MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaintBackground(WF.PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(18, 18, 18));
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color backColor;
            Color foreColor = Enabled ? ForeColorNormal : DisabledForeColor;
            const int shadowOffset = 2;
            bool drawShadow = false;

            Rectangle buttonBounds = new Rectangle(0, 0, Math.Max(1, Width - shadowOffset), Math.Max(1, Height - shadowOffset));
            Rectangle textBounds = buttonBounds;

            if (!Enabled)
            {
                backColor = DisabledBgColor;
            }
            else if (_pressed)
            {
                backColor = HoverBgColor;
                buttonBounds.Offset(shadowOffset, shadowOffset);
                textBounds.Offset(shadowOffset, shadowOffset);
            }
            else if (_hovered)
            {
                backColor = HoverBgColor;
                drawShadow = true;
            }
            else
            {
                backColor = NormalBgColor;
                drawShadow = true;
            }

            if (drawShadow)
            {
                Rectangle shadowBounds = buttonBounds;
                shadowBounds.Offset(shadowOffset, shadowOffset);
                using (GraphicsPath shadowPath = CreateRoundedPath(shadowBounds, 5))
                using (SolidBrush shadowBrush = new SolidBrush(ShadowColor))
                {
                    e.Graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            using (GraphicsPath buttonPath = CreateRoundedPath(buttonBounds, 5))
            using (SolidBrush fillBrush = new SolidBrush(backColor))
            {
                e.Graphics.FillPath(fillBrush, buttonPath);
            }

            WF.TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                foreColor,
                WF.TextFormatFlags.HorizontalCenter |
                WF.TextFormatFlags.VerticalCenter |
                WF.TextFormatFlags.EndEllipsis |
                WF.TextFormatFlags.NoPadding);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
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
    }
}
