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
        private static readonly Color DefaultTopColor = Color.FromArgb(50, 52, 58);
        private static readonly Color DefaultBottomColor = Color.FromArgb(22, 24, 28);
        private static readonly Color DefaultBorderColor = Color.FromArgb(84, 86, 92);
        private static readonly Color PrimaryTopColor = Color.FromArgb(88, 144, 212);
        private static readonly Color PrimaryBottomColor = Color.FromArgb(28, 72, 124);
        private static readonly Color PrimaryBorderColor = Color.FromArgb(102, 164, 232);
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

        public bool IsPrimary { get; set; }

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

            Rectangle shadowBounds = new Rectangle(1, 2, Math.Max(1, Width - 3), Math.Max(1, Height - 4));
            if (!_pressed)
            {
                using (GraphicsPath shadowPath = CreateRoundedPath(shadowBounds, 6))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
                {
                    e.Graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            Rectangle buttonBounds = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 3));
            if (_pressed)
            {
                buttonBounds.Offset(0, 1);
            }

            (Color topColor, Color bottomColor, Color borderColor) = GetColors();
            using (GraphicsPath buttonPath = CreateRoundedPath(buttonBounds, 6))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                buttonBounds,
                topColor,
                bottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(borderColor))
            using (Pen innerPen = new Pen(Color.FromArgb(60, 255, 255, 255)))
            {
                e.Graphics.FillPath(fillBrush, buttonPath);

                Rectangle glossBounds = new Rectangle(
                    buttonBounds.X + 1,
                    buttonBounds.Y + 1,
                    Math.Max(1, buttonBounds.Width - 2),
                    Math.Max(8, (buttonBounds.Height / 2) - 1));
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(buttonPath);
                using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                    glossBounds,
                    Color.FromArgb(IsPrimary ? 64 : 46, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(glossBrush, glossBounds);
                }

                e.Graphics.Restore(state);
                e.Graphics.DrawPath(borderPen, buttonPath);
                e.Graphics.DrawPath(innerPen, buttonPath);
            }

            if (_hovered && !_pressed)
            {
                using (GraphicsPath hoverPath = CreateRoundedPath(buttonBounds, 6))
                using (Pen hoverPen = new Pen(IsPrimary
                    ? Color.FromArgb(176, 214, 255)
                    : Color.FromArgb(132, 146, 168)))
                {
                    e.Graphics.DrawPath(hoverPen, hoverPath);
                }
            }

            Rectangle textBounds = Rectangle.Inflate(buttonBounds, -12, -2);
            if (_pressed)
            {
                textBounds.Offset(0, 1);
            }

            WF.TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                Enabled ? ForeColorNormal : Color.FromArgb(132, 132, 136),
                WF.TextFormatFlags.HorizontalCenter |
                WF.TextFormatFlags.VerticalCenter |
                WF.TextFormatFlags.EndEllipsis |
                WF.TextFormatFlags.NoPadding);
        }

        private (Color topColor, Color bottomColor, Color borderColor) GetColors()
        {
            if (!Enabled)
            {
                return (
                    Color.FromArgb(34, 34, 36),
                    Color.FromArgb(20, 20, 22),
                    Color.FromArgb(64, 64, 68));
            }

            if (IsPrimary)
            {
                if (_pressed)
                {
                    return (
                        Color.FromArgb(52, 102, 166),
                        Color.FromArgb(20, 58, 104),
                        Color.FromArgb(126, 186, 248));
                }

                if (_hovered)
                {
                    return (
                        Color.FromArgb(112, 168, 232),
                        Color.FromArgb(36, 86, 142),
                        Color.FromArgb(146, 208, 255));
                }

                return (PrimaryTopColor, PrimaryBottomColor, PrimaryBorderColor);
            }

            if (_pressed)
            {
                return (
                    Color.FromArgb(36, 38, 42),
                    Color.FromArgb(18, 18, 20),
                    Color.FromArgb(94, 96, 104));
            }

            if (_hovered)
            {
                return (
                    Color.FromArgb(66, 70, 78),
                    Color.FromArgb(28, 30, 34),
                    Color.FromArgb(118, 124, 136));
            }

            return (DefaultTopColor, DefaultBottomColor, DefaultBorderColor);
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
