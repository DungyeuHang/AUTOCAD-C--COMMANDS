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

    // Khung ngoài của DXPALETTE: vẽ nền tối, viền, bóng và accent line.
    // Chỉ phục vụ giao diện, không chứa logic command.
    internal sealed class PaletteChromePanel : WF.Panel
    {
        private static readonly Color OuterFrameColor = Color.FromArgb(14, 14, 14);
        private static readonly Color SurfaceTopColor = Color.FromArgb(38, 38, 40);
        private static readonly Color SurfaceBottomColor = Color.FromArgb(18, 18, 20);
        private static readonly Color BorderColor = Color.FromArgb(68, 68, 72);
        public PaletteChromePanel()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            DoubleBuffered = true;
        }

        protected override void OnPaintBackground(WF.PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(12, 12, 12));
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadowBounds = new Rectangle(2, 3, Math.Max(1, Width - 6), Math.Max(1, Height - 7));
            using (GraphicsPath shadowPath = CreateRoundedPath(shadowBounds, 12))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(95, 0, 0, 0)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            Rectangle surfaceBounds = new Rectangle(0, 0, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            using (GraphicsPath surfacePath = CreateRoundedPath(surfaceBounds, 12))
            using (LinearGradientBrush surfaceBrush = new LinearGradientBrush(
                surfaceBounds,
                SurfaceTopColor,
                SurfaceBottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(BorderColor))
            using (Pen innerPen = new Pen(Color.FromArgb(46, 255, 255, 255)))
            using (Pen outerPen = new Pen(OuterFrameColor))
            {
                e.Graphics.FillPath(surfaceBrush, surfacePath);
                e.Graphics.DrawPath(outerPen, surfacePath);
                e.Graphics.DrawPath(borderPen, surfacePath);

                Rectangle innerBounds = Rectangle.Inflate(surfaceBounds, -1, -1);
                using (GraphicsPath innerPath = CreateRoundedPath(innerBounds, 10))
                {
                    e.Graphics.DrawPath(innerPen, innerPath);
                }
            }

            Rectangle highlightBounds = new Rectangle(2, 2, Math.Max(1, Width - 9), Math.Max(6, (Height / 5)));
            using (GraphicsPath highlightPath = CreateRoundedPath(highlightBounds, 10))
            using (LinearGradientBrush highlightBrush = new LinearGradientBrush(
                highlightBounds,
                Color.FromArgb(48, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                GraphicsState state = e.Graphics.Save();
                Rectangle clipBounds = new Rectangle(1, 1, Math.Max(1, Width - 6), Math.Max(1, Height - 6));
                using (GraphicsPath clipPath = CreateRoundedPath(clipBounds, 12))
                {
                    e.Graphics.SetClip(clipPath);
                    e.Graphics.FillPath(highlightBrush, highlightPath);
                }

                e.Graphics.Restore(state);
            }

            Rectangle accentBounds = new Rectangle(12, Math.Max(10, Height - 16), Math.Max(10, Width - 28), 2);
            using (LinearGradientBrush accentBrush = new LinearGradientBrush(
                accentBounds,
                Color.FromArgb(0, 82, 152, 218),
                Color.FromArgb(180, 82, 152, 218),
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(accentBrush, accentBounds);
            }
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
