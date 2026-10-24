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

    internal sealed class SmartStretchWindowSelection
    {
        public SmartStretchWindowSelection(
            Point3d firstPoint,
            Point3d secondPoint,
            SmartStretchSelectionMode mode,
            bool isWindowSelection)
        {
            FirstPoint = firstPoint;
            SecondPoint = secondPoint;
            Mode = mode;
            IsWindowSelection = isWindowSelection;
        }

        public Point3d FirstPoint { get; }

        public Point3d SecondPoint { get; }

        public SmartStretchSelectionMode Mode { get; }

        // true = quét trái->phải (Window, chỉ đối tượng nằm trọn trong vùng).
        // false = quét phải->trái (Crossing, cả đối tượng chỉ giao với vùng).
        // Khi replay lại cho native STRETCH/COPY phải dùng đúng keyword _W/_C
        // tương ứng, nếu không vùng thực thi sẽ bắt thêm đối tượng không được
        // bôi sáng ở bước xem trước.
        public bool IsWindowSelection { get; }
    }
}
