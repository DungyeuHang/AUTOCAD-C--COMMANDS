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




    // ======================================================
    // CAA_change_pline
    // Mục đích: chuẩn hóa polyline trước khi dùng cho các workflow tự động.
    // Cách làm:
    // - Chọn 1 lightweight Polyline.
    // - Hỏi có set Closed hay bỏ qua, có lưu lựa chọn lần cuối.
    // - Hỏi hướng polyline, hiển thị rõ CCW/CW theo tiếng Việt, có lưu lựa chọn lần cuối.
    // - Luôn yêu cầu người dùng pick điểm đầu mong muốn.
    // - Ép chiều vertex theo hướng người dùng chọn nếu polyline kín.
    // - Đổi điểm đầu theo vertex người dùng chọn.
    // Lưu ý: với polyline hở, chỉ cho đổi điểm đầu giữa 2 đầu mút để tránh đổi hình.
    // ======================================================
    public class ChangePolylineCommand
    {
        private const double CoordinateTolerance = 1e-8;

        [CommandMethod("CAA_change_pline")]
        public void ChangePolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            CaaCloseMode closeMode = CaaPolylineSettingsStore.LoadCloseMode();
            CaaDirectionMode directionMode = CaaPolylineSettingsStore.LoadDirectionMode();

            if (!TryPromptCaaEntity(
                ed,
                ref closeMode,
                ref directionMode,
                out ObjectId polylineId))
            {
                return;
            }

            if (!TryPromptCaaStartPoint(
                ed,
                ref closeMode,
                ref directionMode,
                out Point2d pickedStartPoint))
            {
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Autodesk.AutoCAD.DatabaseServices.Polyline polyline =
                    tr.GetObject(polylineId, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                if (polyline == null)
                {
                    ed.WriteMessage("\nCAA_change_pline: không đọc được polyline.");
                    return;
                }

                if (polyline.NumberOfVertices < 2)
                {
                    ed.WriteMessage("\nCAA_change_pline: polyline phải có ít nhất 2 vertex.");
                    return;
                }

                if (closeMode == CaaCloseMode.Close)
                {
                    polyline.Closed = true;
                }

                bool reversed = false;
                bool startChanged = false;

                if (polyline.Closed && polyline.NumberOfVertices >= 3)
                {
                    double signedArea = GetPolylineSignedArea(polyline);
                    bool isCounterClockwise = signedArea >= -CoordinateTolerance;
                    bool shouldBeCounterClockwise = directionMode == CaaDirectionMode.CCW;
                    if (isCounterClockwise != shouldBeCounterClockwise)
                    {
                        polyline.ReverseCurve();
                        reversed = true;
                    }
                }
                else if (!polyline.Closed)
                {
                    int requestedStartIndex = FindClosestVertex(polyline, pickedStartPoint);

                    // Với polyline hở, chỉ được đổi giữa 2 đầu mút.
                    // Không xoay vertex giữa lên đầu vì như vậy sẽ làm đổi path.
                    if (requestedStartIndex == polyline.NumberOfVertices - 1)
                    {
                        polyline.ReverseCurve();
                        reversed = true;
                        startChanged = true;
                    }
                    else if (requestedStartIndex != 0)
                    {
                        ed.WriteMessage(
                            "\nCAA_change_pline: polyline đang hở nên chỉ nhận điểm đầu ở 1 trong 2 đầu mút để tránh đổi hình. Chọn Close nếu muốn đổi sang vertex giữa.");
                    }
                }

                if (polyline.Closed && polyline.NumberOfVertices >= 3)
                {
                    int startIndex = FindClosestVertex(polyline, pickedStartPoint);
                    if (startIndex > 0)
                    {
                        RotateClosedPolylineStart(polyline, startIndex);
                        startChanged = true;
                    }
                }

                tr.Commit();

                ed.WriteMessage(
                    $"\nCAA_change_pline: xong. Closed={(polyline.Closed ? "Yes" : "No")}, Reverse={(reversed ? "Yes" : "No")}, đổi điểm đầu={(startChanged ? "Yes" : "No")}.");
            }
        }

        private static int FindClosestVertex(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            Point2d targetPoint)
        {
            int bestIndex = 0;
            double bestDistanceSquared = double.MaxValue;

            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                Point2d point = polyline.GetPoint2dAt(i);
                double dx = point.X - targetPoint.X;
                double dy = point.Y - targetPoint.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestIndex = i;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return bestIndex;
        }

        private static double GetPolylineSignedArea(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            double area = 0.0;
            int vertexCount = polyline.NumberOfVertices;
            for (int i = 0; i < vertexCount; i++)
            {
                Point2d current = polyline.GetPoint2dAt(i);
                Point2d next = polyline.GetPoint2dAt((i + 1) % vertexCount);
                area += current.X * next.Y - next.X * current.Y;
            }

            return area / 2.0;
        }

        private static void RotateClosedPolylineStart(
            Autodesk.AutoCAD.DatabaseServices.Polyline polyline,
            int startIndex)
        {
            if (startIndex <= 0 || startIndex >= polyline.NumberOfVertices)
            {
                return;
            }

            List<CaaPolylineVertex> vertices = ReadVertices(polyline);
            List<CaaPolylineVertex> reordered = vertices
                .Skip(startIndex)
                .Concat(vertices.Take(startIndex))
                .ToList();

            for (int i = 0; i < reordered.Count; i++)
            {
                CaaPolylineVertex vertex = reordered[i];
                polyline.SetPointAt(i, vertex.Point);
                polyline.SetBulgeAt(i, vertex.Bulge);
                polyline.SetStartWidthAt(i, vertex.StartWidth);
                polyline.SetEndWidthAt(i, vertex.EndWidth);
            }
        }

        private static List<CaaPolylineVertex> ReadVertices(Autodesk.AutoCAD.DatabaseServices.Polyline polyline)
        {
            List<CaaPolylineVertex> vertices = new List<CaaPolylineVertex>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                vertices.Add(
                    new CaaPolylineVertex(
                        polyline.GetPoint2dAt(i),
                        polyline.GetBulgeAt(i),
                        polyline.GetStartWidthAt(i),
                        polyline.GetEndWidthAt(i)));
            }

            return vertices;
        }

        private static bool TryPromptCaaStartPoint(
            Editor ed,
            ref CaaCloseMode closeMode,
            ref CaaDirectionMode directionMode,
            out Point2d pickedStartPoint)
        {
            pickedStartPoint = Point2d.Origin;

            while (true)
            {
                PromptPointOptions startPointOptions =
                    new PromptPointOptions(
                        $"\nChọn điểm đầu mong muốn (pline hở: chọn gần đầu mút) hoặc [Settings] <Close={closeMode}, Dir={directionMode}>: ");
                startPointOptions.AppendKeywordsToMessage = false;
                startPointOptions.Keywords.Add("Settings");

                PromptPointResult startPointResult = ed.GetPoint(startPointOptions);
                if (startPointResult.Status == PromptStatus.OK)
                {
                    pickedStartPoint =
                        new Point2d(startPointResult.Value.X, startPointResult.Value.Y);
                    return true;
                }

                if (startPointResult.Status == PromptStatus.Keyword &&
                    string.Equals(startPointResult.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PromptForCaaSettings(ed, ref closeMode, ref directionMode))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }

        private static bool TryPromptCaaEntity(
            Editor ed,
            ref CaaCloseMode closeMode,
            ref CaaDirectionMode directionMode,
            out ObjectId polylineId)
        {
            polylineId = ObjectId.Null;

            while (true)
            {
                PromptEntityOptions entityOptions =
                    new PromptEntityOptions(
                        $"\nChọn polyline cần chuẩn hóa hoặc [Settings] <Close={closeMode}, Dir={directionMode}>: ");
                entityOptions.AppendKeywordsToMessage = false;
                entityOptions.SetRejectMessage("\nChỉ hỗ trợ lightweight Polyline.");
                entityOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
                entityOptions.Keywords.Add("Settings");

                PromptEntityResult entityResult = ed.GetEntity(entityOptions);
                if (entityResult.Status == PromptStatus.OK)
                {
                    polylineId = entityResult.ObjectId;
                    return true;
                }

                if (entityResult.Status == PromptStatus.Keyword &&
                    string.Equals(entityResult.StringResult, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (!PromptForCaaSettings(ed, ref closeMode, ref directionMode))
                    {
                        return false;
                    }

                    continue;
                }

                return false;
            }
        }

        private static bool PromptForCaaSettings(
            Editor ed,
            ref CaaCloseMode closeMode,
            ref CaaDirectionMode directionMode)
        {
            PromptKeywordOptions closeOptions =
                new PromptKeywordOptions(
                    $"\nXử lý closed polyline [Close/Skip] <{closeMode}>: ");
            closeOptions.AllowNone = true;
            closeOptions.Keywords.Add("Close");
            closeOptions.Keywords.Add("Skip");
            closeOptions.Keywords.Default = closeMode.ToString();

            PromptResult closeResult = ed.GetKeywords(closeOptions);
            if (closeResult.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (closeResult.Status == PromptStatus.OK &&
                Enum.TryParse(closeResult.StringResult, true, out CaaCloseMode parsedCloseMode))
            {
                closeMode = parsedCloseMode;
            }

            CaaPolylineSettingsStore.SaveCloseMode(closeMode);

            PromptKeywordOptions directionOptions =
                new PromptKeywordOptions(
                    $"\nChọn hướng polyline [CCW=Nguoc chieu kim dong ho/CW=Cung chieu kim dong ho] <{directionMode}>: ");
            directionOptions.AllowNone = true;
            directionOptions.Keywords.Add("CCW");
            directionOptions.Keywords.Add("CW");
            directionOptions.Keywords.Default = directionMode.ToString();

            PromptResult directionResult = ed.GetKeywords(directionOptions);
            if (directionResult.Status == PromptStatus.Cancel)
            {
                return false;
            }

            if (directionResult.Status == PromptStatus.OK &&
                Enum.TryParse(directionResult.StringResult, true, out CaaDirectionMode parsedDirectionMode))
            {
                directionMode = parsedDirectionMode;
            }

            CaaPolylineSettingsStore.SaveDirectionMode(directionMode);
            return true;
        }

        private readonly struct CaaPolylineVertex
        {
            public CaaPolylineVertex(Point2d point, double bulge, double startWidth, double endWidth)
            {
                Point = point;
                Bulge = bulge;
                StartWidth = startWidth;
                EndWidth = endWidth;
            }

            public Point2d Point { get; }

            public double Bulge { get; }

            public double StartWidth { get; }

            public double EndWidth { get; }
        }

        private enum CaaCloseMode
        {
            Close,
            Skip
        }

        private enum CaaDirectionMode
        {
            CCW,
            CW
        }

        private static class CaaPolylineSettingsStore
        {
            private static readonly string CloseModeFilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "caa_change_pline_settings.txt");

            private static readonly string DirectionModeFilePath =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "caa_change_pline_direction_mode.txt");

            public static CaaCloseMode LoadCloseMode()
            {
                try
                {
                    if (!File.Exists(CloseModeFilePath))
                    {
                        return CaaCloseMode.Close;
                    }

                    string raw = File.ReadAllText(CloseModeFilePath, Encoding.UTF8).Trim();
                    return Enum.TryParse(raw, true, out CaaCloseMode mode)
                        ? mode
                        : CaaCloseMode.Close;
                }
                catch
                {
                    return CaaCloseMode.Close;
                }
            }

            public static void SaveCloseMode(CaaCloseMode mode)
            {
                try
                {
                    File.WriteAllText(CloseModeFilePath, mode.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }

            public static CaaDirectionMode LoadDirectionMode()
            {
                try
                {
                    if (!File.Exists(DirectionModeFilePath))
                    {
                        return CaaDirectionMode.CCW;
                    }

                    string raw = File.ReadAllText(DirectionModeFilePath, Encoding.UTF8).Trim();
                    return Enum.TryParse(raw, true, out CaaDirectionMode mode)
                        ? mode
                        : CaaDirectionMode.CCW;
                }
                catch
                {
                    return CaaDirectionMode.CCW;
                }
            }

            public static void SaveDirectionMode(CaaDirectionMode mode)
            {
                try
                {
                    File.WriteAllText(DirectionModeFilePath, mode.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
