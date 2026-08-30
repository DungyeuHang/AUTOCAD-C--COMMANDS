using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AUTOCAD_COMMANDS
{
    // Logo thương hiệu DungX dùng chung cho header Palette, icon cửa sổ
    // Calculator, và panel thương hiệu trên Ribbon - vẽ bằng vector nên không
    // cần quản lý file ảnh rời và luôn nét ở mọi kích thước.
    // Chữ "D" đậm làm chủ đạo (chữ cái đầu tên DungX) + một dấu X nhỏ cắt
    // chéo góc dưới phải như một chữ ký/con dấu riêng - đúng 1 màu accent,
    // không rườm rà nhiều màu.
    internal static class DungXLogo
    {
        private static readonly Color TileColor = Color.FromArgb(255, 24, 26, 31);
        private static readonly Color LetterColor = Color.FromArgb(255, 240, 241, 244);
        private static readonly Color MarkColor = RibbonPalette.DimensionAccent;

        public static void Draw(Graphics g, RectangleF bounds)
        {
            SmoothingMode previousMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = bounds.Width * 0.22f;
            using (GraphicsPath tile = RoundedRect(bounds, radius))
            using (SolidBrush tileBrush = new SolidBrush(TileColor))
            using (Pen borderPen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, bounds.Width * 0.03f)))
            {
                g.FillPath(tileBrush, tile);
                g.DrawPath(borderPen, tile);
            }

            DrawLetterD(g, bounds);
            DrawSignatureMark(g, bounds);

            g.SmoothingMode = previousMode;
        }

        private static void DrawLetterD(Graphics g, RectangleF bounds)
        {
            FontFamily family;
            try
            {
                family = new FontFamily("Segoe UI");
            }
            catch
            {
                family = FontFamily.GenericSansSerif;
            }

            using (family)
            using (GraphicsPath letterPath = new GraphicsPath())
            {
                float emSize = bounds.Height * 4f;
                letterPath.AddString(
                    "D",
                    family,
                    (int)FontStyle.Bold,
                    emSize,
                    new PointF(0f, 0f),
                    StringFormat.GenericTypographic);

                RectangleF letterBounds = letterPath.GetBounds();
                if (letterBounds.Width <= 0f || letterBounds.Height <= 0f)
                {
                    return;
                }

                float scale = Math.Min(
                    (bounds.Width * 0.6f) / letterBounds.Width,
                    (bounds.Height * 0.7f) / letterBounds.Height);

                using (Matrix matrix = new Matrix())
                {
                    matrix.Translate(-letterBounds.X, -letterBounds.Y);
                    matrix.Scale(scale, scale, MatrixOrder.Append);

                    float scaledWidth = letterBounds.Width * scale;
                    float scaledHeight = letterBounds.Height * scale;
                    float offsetX = bounds.X + bounds.Width * 0.22f;
                    float offsetY = bounds.Y + (bounds.Height - scaledHeight) / 2f;
                    matrix.Translate(offsetX, offsetY, MatrixOrder.Append);

                    letterPath.Transform(matrix);
                }

                using (SolidBrush letterBrush = new SolidBrush(LetterColor))
                {
                    g.FillPath(letterBrush, letterPath);
                }
            }
        }

        private static void DrawSignatureMark(Graphics g, RectangleF bounds)
        {
            float markSize = bounds.Width * 0.24f;
            float half = markSize / 2f;
            PointF center = new PointF(
                bounds.Right - markSize * 0.72f,
                bounds.Bottom - markSize * 0.72f);

            using (Pen markPen = new Pen(MarkColor, Math.Max(1.4f, bounds.Width * 0.05f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                g.DrawLine(markPen, center.X - half, center.Y - half, center.X + half, center.Y + half);
                g.DrawLine(markPen, center.X - half, center.Y + half, center.X + half, center.Y - half);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            float d = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Bitmap CreateBitmap(int size)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                Draw(g, new RectangleF(0, 0, size, size));
            }

            return bitmap;
        }

        // Icon cho tiêu đề Form (ví dụ QuickCalculatorForm). Handle HICON được
        // giữ sống theo vòng đời tiến trình - chấp nhận được vì chỉ gọi một
        // lần lúc tạo form, không phải trên hot path.
        public static Icon CreateIcon(int size)
        {
            using (Bitmap bitmap = CreateBitmap(size))
            {
                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }
    }
}
