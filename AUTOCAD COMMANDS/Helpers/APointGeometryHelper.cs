using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    // Logic dùng chung cho APOINT / APOINT2: tuỳ chọn gộp các điểm trùng toạ độ
    // liên tiếp trên 1 pline thành 1 điểm, và dựng text APoint(...)/smart_pl(...).
    // Dùng chung giữa bước tạo thật và bước preview để 2 bên luôn khớp nhau.
    internal static class APointGeometryHelper
    {
        public const double DefaultTolerance = 1e-6;

        // Kiểu dò điểm trùng toạ độ phía trước khi dựng biểu thức tham chiếu:
        // - None: không dò xa, chỉ so với điểm liền trước (logic đơn giản cũ của APOINT).
        // - First: dò toàn bộ các điểm phía trước, lấy điểm có chỉ số nhỏ nhất.
        // - Last: dò toàn bộ các điểm phía trước, lấy điểm có chỉ số lớn nhất.
        public enum MatchMode
        {
            None,
            First,
            Last
        }

        public sealed class ReducedPoints
        {
            public List<Point3d> Points { get; }

            // Với điểm rút gọn thứ k, LastOriginalIndex[k] là chỉ số (0-based) của
            // đỉnh cuối cùng trong cụm điểm trùng toạ độ gốc tương ứng. Dùng để lấy
            // đúng bulge của đoạn đi ra khỏi cụm khi dựng smart_pl.
            public List<int> LastOriginalIndex { get; }

            public ReducedPoints(List<Point3d> points, List<int> lastOriginalIndex)
            {
                Points = points;
                LastOriginalIndex = lastOriginalIndex;
            }
        }

        public static ReducedPoints ReduceConsecutiveDuplicates(
            IReadOnlyList<Point3d> points,
            bool mergeDuplicates,
            double tolerance = DefaultTolerance)
        {
            List<Point3d> reducedPoints = new List<Point3d>();
            List<int> lastOriginalIndex = new List<int>();

            if (points.Count == 0)
            {
                return new ReducedPoints(reducedPoints, lastOriginalIndex);
            }

            int clusterStart = 0;
            for (int i = 1; i <= points.Count; i++)
            {
                bool endOfCluster = !mergeDuplicates
                    || i == points.Count
                    || !ArePointsEqual(points[i], points[clusterStart], tolerance);

                if (endOfCluster)
                {
                    reducedPoints.Add(points[clusterStart]);
                    lastOriginalIndex.Add(i - 1);
                    clusterStart = i;
                }
            }

            return new ReducedPoints(reducedPoints, lastOriginalIndex);
        }

        private static bool ArePointsEqual(Point3d a, Point3d b, double tolerance)
        {
            return Math.Abs(a.X - b.X) <= tolerance
                && Math.Abs(a.Y - b.Y) <= tolerance
                && Math.Abs(a.Z - b.Z) <= tolerance;
        }

        // Logic cũ của APOINT: luôn tham chiếu theo điểm liền trước.
        public static List<string> BuildSimpleDefinitionLines(
            IReadOnlyList<Point3d> points,
            string prefix,
            double tolerance = DefaultTolerance)
        {
            List<string> lines = new List<string>();
            Point3d? previousPoint = null;

            for (int i = 0; i < points.Count; i++)
            {
                Point3d point = points[i];
                int count = i + 1;
                string line;
                if (!previousPoint.HasValue)
                {
                    line =
                        $"{prefix}_p{count} = APoint({CadFormatHelper.FormatNumber(point.X, tolerance)}, {CadFormatHelper.FormatNumber(point.Y, tolerance)})";
                }
                else
                {
                    double dx = point.X - previousPoint.Value.X;
                    double dy = point.Y - previousPoint.Value.Y;
                    line =
                        $"{prefix}_p{count} = APoint({prefix}_p{count - 1}.x{CadFormatHelper.FormatOffset(dx, tolerance)}, {prefix}_p{count - 1}.y{CadFormatHelper.FormatOffset(dy, tolerance)})";
                }

                lines.Add(line);
                previousPoint = point;
            }

            return lines;
        }

        // Logic cũ của APOINT2: tham chiếu tuyến tính tới điểm trùng toạ độ gần nhất
        // (trừ khi matchMode = None, lúc đó chỉ so với điểm liền trước như APOINT cũ).
        public static List<string> BuildSmartDefinitionLines(
            IReadOnlyList<Point3d> points,
            string prefix,
            MatchMode matchMode,
            double tolerance = DefaultTolerance)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < points.Count; i++)
            {
                int count = i + 1;
                string line;
                if (i == 0)
                {
                    Point3d point = points[i];
                    line =
                        $"{prefix}_p{count} = APoint({CadFormatHelper.FormatNumber(point.X, tolerance)}, {CadFormatHelper.FormatNumber(point.Y, tolerance)})";
                }
                else
                {
                    string xExpression = BuildAxisExpression(points, i, prefix, true, matchMode, tolerance);
                    string yExpression = BuildAxisExpression(points, i, prefix, false, matchMode, tolerance);
                    line = $"{prefix}_p{count} = APoint({xExpression}, {yExpression})";
                }

                lines.Add(line);
            }

            return lines;
        }

        private static string BuildAxisExpression(
            IReadOnlyList<Point3d> points,
            int index,
            string prefix,
            bool isXAxis,
            MatchMode matchMode,
            double tolerance)
        {
            string axisName = isXAxis ? "x" : "y";
            double currentValue = GetAxisValue(points[index], isXAxis);
            double previousValue = GetAxisValue(points[index - 1], isXAxis);

            if (Math.Abs(currentValue - previousValue) <= tolerance)
            {
                return $"{prefix}_p{index}.{axisName}";
            }

            if (matchMode == MatchMode.Last)
            {
                for (int j = index - 2; j >= 0; j--)
                {
                    if (Math.Abs(currentValue - GetAxisValue(points[j], isXAxis)) <= tolerance)
                    {
                        return $"{prefix}_p{j + 1}.{axisName}";
                    }
                }
            }
            else if (matchMode == MatchMode.First)
            {
                for (int j = 0; j < index - 1; j++)
                {
                    if (Math.Abs(currentValue - GetAxisValue(points[j], isXAxis)) <= tolerance)
                    {
                        return $"{prefix}_p{j + 1}.{axisName}";
                    }
                }
            }

            return
                $"{prefix}_p{index}.{axisName}{CadFormatHelper.FormatOffset(currentValue - previousValue, tolerance)}";
        }

        private static double GetAxisValue(Point3d point, bool isXAxis)
        {
            return isXAxis ? point.X : point.Y;
        }

        // Lấy bulge của từng đoạn (vertex i -> i+1) theo đúng thứ tự đỉnh hiện có.
        public static double[] GetSegmentBulges(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            int segmentCount = Math.Max(0, polyline.NumberOfVertices - 1);
            double[] bulges = new double[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                bulges[i] = polyline.GetBulgeAt(i);
            }

            return bulges;
        }

        // Đảo chiều danh sách điểm + mảng bulge tương ứng (nếu reverse = true).
        // Đảo chiều 1 đoạn sẽ đổi dấu bulge của đoạn đó (chiều cung bị lật).
        public static void ApplyReverse(
            IReadOnlyList<Point3d> points,
            IReadOnlyList<double> segmentBulges,
            bool reverse,
            out List<Point3d> resultPoints,
            out double[] resultBulges)
        {
            resultPoints = new List<Point3d>(points);
            resultBulges = new double[segmentBulges.Count];
            for (int i = 0; i < segmentBulges.Count; i++)
            {
                resultBulges[i] = segmentBulges[i];
            }

            if (!reverse)
            {
                return;
            }

            resultPoints.Reverse();

            int segCount = segmentBulges.Count;
            for (int i = 0; i < segCount; i++)
            {
                resultBulges[i] = -segmentBulges[segCount - 1 - i];
            }
        }

        public static string BuildSmartShapeText(
            IReadOnlyList<double> segmentBulges,
            bool closed,
            IReadOnlyList<Point3d> reducedPoints,
            IReadOnlyList<int> lastOriginalIndexPerPoint,
            string prefix,
            double tolerance = DefaultTolerance)
        {
            List<string> arcsInfoItems = new List<string>();
            for (int i = 0; i < reducedPoints.Count - 1; i++)
            {
                int bulgeIndex = lastOriginalIndexPerPoint[i];
                double bulge = bulgeIndex >= 0 && bulgeIndex < segmentBulges.Count
                    ? segmentBulges[bulgeIndex]
                    : 0.0;
                if (Math.Abs(bulge) <= tolerance)
                {
                    continue;
                }

                double chord = reducedPoints[i].DistanceTo(reducedPoints[i + 1]);
                double theta = 4.0 * Math.Atan(bulge);
                double denominator = 2.0 * Math.Sin(theta / 2.0);
                if (Math.Abs(denominator) <= tolerance)
                {
                    continue;
                }

                double radius = chord / denominator;
                int startPointIndex = i + 1;
                int endPointIndex = i + 2;
                if (radius < 0.0)
                {
                    radius = Math.Abs(radius);
                    startPointIndex = i + 2;
                    endPointIndex = i + 1;
                }

                arcsInfoItems.Add($"({startPointIndex},{endPointIndex}): ({FormatRadius(radius)}, True)");
            }

            string closeText = closed ? "True" : "False";
            string arcsInfo = string.Join(", ", arcsInfoItems);
            return $"smart_pl(\"{prefix}_p\", 1, {reducedPoints.Count}, arcs_info={{{arcsInfo}}}, close={closeText})";
        }

        private static string FormatRadius(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static List<Point3d> GetPolylineVertices(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            List<Point3d> points = new List<Point3d>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                Point2d point2d = polyline.GetPoint2dAt(i);
                Point3d point = new Point3d(point2d.X, point2d.Y, polyline.Elevation)
                    .TransformBy(polyline.Ecs);
                points.Add(point);
            }

            return points;
        }
    }
}
