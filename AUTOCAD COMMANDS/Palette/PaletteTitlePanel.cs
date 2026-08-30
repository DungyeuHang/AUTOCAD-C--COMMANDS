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

    // Header "DUNGX PROJECT" ở đầu DXPALETTE.
    // Nếu muốn đổi logo/tên thương hiệu thì sửa control này.
    internal sealed class PaletteTitlePanel : WF.Panel
    {
        private static readonly Color TitleTopColor = Color.FromArgb(42, 42, 46);
        private static readonly Color TitleBottomColor = Color.FromArgb(24, 24, 28);
        private static readonly Color TitleBorderColor = Color.FromArgb(84, 84, 90);
        private static readonly Color TitleTextColor = Color.FromArgb(236, 236, 238);
        private static readonly Color TitleSubtleColor = Color.FromArgb(164, 164, 170);

        public PaletteTitlePanel()
        {
            SetStyle(
                WF.ControlStyles.AllPaintingInWmPaint |
                WF.ControlStyles.OptimizedDoubleBuffer |
                WF.ControlStyles.ResizeRedraw |
                WF.ControlStyles.UserPaint,
                true);

            DoubleBuffered = true;
            Height = 40;
            MinimumSize = new Size(0, 40);
            Margin = new WF.Padding(0, 0, 0, 6);
        }

        protected override void OnPaintBackground(WF.PaintEventArgs pevent)
        {
            pevent.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(18, 18, 18));
        }

        protected override void OnPaint(WF.PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            using (GraphicsPath path = CreateRoundedPath(bounds, 8))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                bounds,
                TitleTopColor,
                TitleBottomColor,
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(TitleBorderColor))
            using (Pen innerPen = new Pen(Color.FromArgb(42, 255, 255, 255)))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);

                Rectangle innerBounds = Rectangle.Inflate(bounds, -1, -1);
                using (GraphicsPath innerPath = CreateRoundedPath(innerBounds, 7))
                {
                    e.Graphics.DrawPath(innerPen, innerPath);
                }
            }

            Rectangle glossBounds = new Rectangle(1, 1, Math.Max(1, Width - 4), Math.Max(8, Height / 2));
            using (GraphicsPath clipPath = CreateRoundedPath(bounds, 8))
            using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                glossBounds,
                Color.FromArgb(36, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(clipPath);
                e.Graphics.FillRectangle(glossBrush, glossBounds);
                e.Graphics.Restore(state);
            }

            Rectangle logoBounds = new Rectangle(10, 8, 24, 24);
            DungXLogo.Draw(e.Graphics, logoBounds);

            Rectangle titleBounds = new Rectangle(40, 6, Math.Max(60, Width - 114), 24);
            using (System.Drawing.Font titleFont = new System.Drawing.Font(
                "Segoe UI",
                11.25F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            using (System.Drawing.Font subFont = new System.Drawing.Font(
                "Segoe UI",
                7.75F,
                FontStyle.Bold,
                GraphicsUnit.Point))
            {
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    "DUNGX PROJECT",
                    titleFont,
                    titleBounds,
                    TitleTextColor,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);

                Rectangle subBounds = new Rectangle(40, 24, Math.Max(60, Width - 114), 12);
                WF.TextRenderer.DrawText(
                    e.Graphics,
                    "Custom Command Manager",
                    subFont,
                    subBounds,
                    TitleSubtleColor,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis);
            }

            DrawHeaderGlyphs(e.Graphics, Width - 52, 10);
        }

        private static void DrawHeaderGlyphs(Graphics graphics, int x, int y)
        {
            using (Pen linePen = new Pen(Color.FromArgb(198, 198, 202), 1.6f))
            {
                linePen.StartCap = LineCap.Round;
                linePen.EndCap = LineCap.Round;

                graphics.DrawLine(linePen, x, y + 2, x + 10, y + 2);
                graphics.DrawLine(linePen, x, y + 6, x + 10, y + 6);
                graphics.DrawLine(linePen, x + 20, y, x + 28, y + 8);
                graphics.DrawLine(linePen, x + 28, y, x + 20, y + 8);
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
