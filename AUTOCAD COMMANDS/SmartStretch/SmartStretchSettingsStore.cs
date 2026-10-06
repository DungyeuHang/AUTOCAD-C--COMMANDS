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

    // Quản lý lưu/đọc giá trị L gần nhất dùng chung cho SS, SSD, SSD2, SX, SY.
    // Dùng chung key "smartstretch.length" trong WorkspaceUiStateStore (%APPDATA%\DUNGX\AUTOCAD_COMMANDS)
    // để đồng bộ thống nhất giữa SS, SX, SY và không phụ thuộc quyền ghi file tại thư mục bundle/DLL.
    internal static class SmartStretchSettingsStore
    {
        public const string SettingKey = "smartstretch.length";
        public const double DefaultLength = 500.0;
        private const double ComparisonTolerance = 1e-6;

        public static double LoadLength()
        {
            // 1. Đọc từ WorkspaceUiStateStore dùng chung
            if (WorkspaceUiStateStore.TryGetDouble(SettingKey, out double value) &&
                Math.Abs(value) > ComparisonTolerance)
            {
                return value;
            }

            // 2. Migration fallback từ file txt cũ nếu có
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string legacyPath = Path.Combine(assemblyDir, "dungx_smart_stretch_length.txt");
                if (File.Exists(legacyPath))
                {
                    string text = (File.ReadAllText(legacyPath, Encoding.UTF8) ?? string.Empty).Trim();
                    if (double.TryParse(
                        text,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out double legacyValue) &&
                        Math.Abs(legacyValue) > ComparisonTolerance)
                    {
                        SaveLength(legacyValue);
                        return legacyValue;
                    }
                }
            }
            catch
            {
            }

            return DefaultLength;
        }

        public static void SaveLength(double value)
        {
            if (Math.Abs(value) <= ComparisonTolerance)
            {
                return;
            }

            // Lưu vào WorkspaceUiStateStore dùng chung cho cả SS, SX, SY
            WorkspaceUiStateStore.SaveValue(
                SettingKey,
                value.ToString(CultureInfo.InvariantCulture));

            // Cố gắng ghi ra file legacy nếu có thể (không gây crash nếu không có quyền ghi)
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    string legacyPath = Path.Combine(assemblyDir, "dungx_smart_stretch_length.txt");
                    File.WriteAllText(
                        legacyPath,
                        value.ToString("0.###", CultureInfo.InvariantCulture),
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
