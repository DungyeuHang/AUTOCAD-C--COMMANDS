using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using System;

namespace AUTOCAD_COMMANDS
{

    // Mô phỏng hình chữ nhật quét chọn giống AutoCAD gốc:
    // quét trái->phải = Window (viền xanh dương liền nét, nền xanh dương nhạt trong suốt).
    // quét phải->trái = Crossing (viền xanh lá đứt nét, nền xanh lá nhạt trong suốt).
    // Dùng chung cho SS/SCC/SSD/SSD2 ở bước quét crossing/window chọn đối tượng.
    internal sealed class SmartSelectionRubberBandJig : DrawJig
    {
        private static readonly Color WindowColor = Color.FromRgb(0, 0, 255);
        private static readonly Color CrossingColor = Color.FromRgb(0, 180, 0);
        private const double ComparisonTolerance = 1e-9;

        private readonly Point3d _firstCorner;
        private Point3d _secondCorner;

        public SmartSelectionRubberBandJig(Point3d firstCorner)
        {
            _firstCorner = firstCorner;
            _secondCorner = firstCorner;
        }

        public Point3d SecondCorner => _secondCorner;

        public bool IsWindowSelection => _secondCorner.X >= _firstCorner.X;

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            // Cố ý KHÔNG set BasePoint/UseBasePoint: nếu dùng rubber-band từ base point,
            // AutoCAD sẽ áp ORTHOMODE (F8) vào điểm thu được và biến vùng quét thành 1 đường
            // thẳng khi bật Ortho. Lấy điểm tự do để vùng quét không phụ thuộc F8.
            JigPromptPointOptions pointOptions =
                new JigPromptPointOptions(
                    "\nChọn góc đối diện (trái→phải = Window xanh dương, phải→trái = Crossing xanh lá): ");

            PromptPointResult pointResult = prompts.AcquirePoint(pointOptions);
            if (pointResult.Status == PromptStatus.Cancel)
            {
                return SamplerStatus.Cancel;
            }

            if (pointResult.Status != PromptStatus.OK)
            {
                return SamplerStatus.NoChange;
            }

            if (_secondCorner.DistanceTo(pointResult.Value) <= ComparisonTolerance)
            {
                return SamplerStatus.NoChange;
            }

            _secondCorner = pointResult.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            double minX = Math.Min(_firstCorner.X, _secondCorner.X);
            double maxX = Math.Max(_firstCorner.X, _secondCorner.X);
            double minY = Math.Min(_firstCorner.Y, _secondCorner.Y);
            double maxY = Math.Max(_firstCorner.Y, _secondCorner.Y);
            double z = _firstCorner.Z;

            Point3d corner1 = new Point3d(minX, minY, z);
            Point3d corner2 = new Point3d(maxX, minY, z);
            Point3d corner3 = new Point3d(maxX, maxY, z);
            Point3d corner4 = new Point3d(minX, maxY, z);

            bool isWindow = IsWindowSelection;
            Color color = isWindow ? WindowColor : CrossingColor;

            DrawFill(draw, corner1, corner2, corner3, corner4, color);
            DrawBorder(draw, corner1, corner2, corner3, corner4, color, dashed: !isWindow);

            return true;
        }

        private static void DrawFill(
            WorldDraw draw,
            Point3d corner1,
            Point3d corner2,
            Point3d corner3,
            Point3d corner4,
            Color color)
        {
            // Nền mờ trong suốt giống hiệu ứng tô màu vùng quét của AutoCAD.
            // Bọc try/catch để nếu entity Solid không vẽ được thì vẫn còn viền màu.
            try
            {
                using (Solid fill = new Solid(corner1, corner2, corner4, corner3))
                {
                    fill.Color = color;
                    fill.Transparency = new Transparency(60);
                    fill.WorldDraw(draw);
                }
            }
            catch
            {
            }
        }

        private static void DrawBorder(
            WorldDraw draw,
            Point3d corner1,
            Point3d corner2,
            Point3d corner3,
            Point3d corner4,
            Color color,
            bool dashed)
        {
            draw.SubEntityTraits.TrueColor =
                new EntityColor(color.ColorValue.R, color.ColorValue.G, color.ColorValue.B);

            DrawEdge(draw, corner1, corner2, dashed);
            DrawEdge(draw, corner2, corner3, dashed);
            DrawEdge(draw, corner3, corner4, dashed);
            DrawEdge(draw, corner4, corner1, dashed);
        }

        private static void DrawEdge(WorldDraw draw, Point3d start, Point3d end, bool dashed)
        {
            if (!dashed)
            {
                draw.Geometry.WorldLine(start, end);
                return;
            }

            double length = start.DistanceTo(end);
            if (length <= ComparisonTolerance)
            {
                return;
            }

            double dashLength = Math.Max(length / 18.0, ComparisonTolerance);
            Vector3d direction = (end - start).GetNormal();
            double traveled = 0.0;
            bool drawSegment = true;

            while (traveled < length)
            {
                double segmentLength = Math.Min(dashLength, length - traveled);
                if (drawSegment)
                {
                    Point3d segmentStart = start + (direction * traveled);
                    Point3d segmentEnd = start + (direction * (traveled + segmentLength));
                    draw.Geometry.WorldLine(segmentStart, segmentEnd);
                }

                traveled += segmentLength;
                drawSegment = !drawSegment;
            }
        }
    }
}
