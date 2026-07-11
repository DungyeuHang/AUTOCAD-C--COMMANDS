﻿using Autodesk.AutoCAD.ApplicationServices;
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
        private static readonly Color OuterBorderColor = Color.FromArgb(34, 41, 51);
        private static readonly Color BackgroundFillColor = Color.FromArgb(59, 68, 83);
        private static readonly Color InnerBorderColor = Color.FromArgb(80, 90, 105);
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

            Rectangle surfaceBounds = new Rectangle(0, 0, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            using (GraphicsPath surfacePath = CreateRoundedPath(surfaceBounds, 12))
            using (SolidBrush surfaceBrush = new SolidBrush(BackgroundFillColor))
            using (Pen borderPen = new Pen(InnerBorderColor))
            using (Pen outerPen = new Pen(OuterBorderColor))
            {
                e.Graphics.FillPath(surfaceBrush, surfacePath);
                e.Graphics.DrawPath(outerPen, surfacePath);
                e.Graphics.DrawPath(borderPen, surfacePath);
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
