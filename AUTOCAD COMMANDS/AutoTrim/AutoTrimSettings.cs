using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    public enum AutoTrimMode
    {
        TrimOutside = 0, // Cắt bỏ các phần bên ngoài đường mốc, giữ lại bên trong (Mặc định)
        TrimInside = 1   // Cắt bỏ các phần bên trong đường mốc, giữ lại bên ngoài
    }

    public class AutoTrimSettings
    {
        public AutoTrimMode Mode { get; set; } = AutoTrimMode.TrimOutside;

        /// <summary>
        /// Chỉ cắt các đối tượng thực sự cắt qua (giao với) đường mốc.
        /// Tuyệt đối KHÔNG xóa các đối tượng nằm ngoài không cắt qua.
        /// </summary>
        public bool OnlyTrimCrossingObjects { get; set; } = true;

        /// <summary>
        /// Dung sai hình học khi xét giao điểm và điểm trong/ngoài đa giác
        /// </summary>
        public double Tolerance { get; set; } = 0.01;

        /// <summary>
        /// Hiển thị Transient Graphics preview trực tiếp trên màn hình CAD trước khi áp dụng
        /// </summary>
        public bool ShowTransientPreview { get; set; } = true;

        /// <summary>
        /// Hỏi xác nhận trước khi commit database
        /// </summary>
        public bool ConfirmBeforeCommit { get; set; } = true;

        /// <summary>
        /// Tự động sao chép thuộc tính gốc (Layer, Color, Linetype, LineWeight) sang các đoạn giữ lại
        /// </summary>
        public bool KeepOriginalProperties { get; set; } = true;
    }

    internal static class AutoTrimSettingsStore
    {
        private static readonly object SyncRoot = new object();
        private static string _filePath;

        private static string GetFilePath()
        {
            if (!string.IsNullOrEmpty(_filePath))
            {
                return _filePath;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            }

            string dir = Path.Combine(appData, "DUNGX", "AUTOCAD_COMMANDS");
            _filePath = Path.Combine(dir, "autotrim_settings.tsv");
            return _filePath;
        }

        public static AutoTrimSettings Load()
        {
            lock (SyncRoot)
            {
                AutoTrimSettings settings = new AutoTrimSettings();
                string path = GetFilePath();

                if (!File.Exists(path))
                {
                    return settings;
                }

                try
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    foreach (string rawLine in lines)
                    {
                        if (string.IsNullOrWhiteSpace(rawLine)) continue;
                        string[] parts = rawLine.Split(new[] { '\t' }, 2);
                        if (parts.Length < 2) continue;

                        string key = parts[0].Trim();
                        string val = parts[1].Trim();

                        switch (key)
                        {
                            case "Mode":
                                if (Enum.TryParse(val, out AutoTrimMode mode)) settings.Mode = mode;
                                break;
                            case "OnlyTrimCrossingObjects":
                                if (bool.TryParse(val, out bool otc)) settings.OnlyTrimCrossingObjects = otc;
                                break;
                            case "Tolerance":
                                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double tol)) settings.Tolerance = tol;
                                break;
                            case "ShowTransientPreview":
                                if (bool.TryParse(val, out bool stp)) settings.ShowTransientPreview = stp;
                                break;
                            case "ConfirmBeforeCommit":
                                if (bool.TryParse(val, out bool cbc)) settings.ConfirmBeforeCommit = cbc;
                                break;
                            case "KeepOriginalProperties":
                                if (bool.TryParse(val, out bool kop)) settings.KeepOriginalProperties = kop;
                                break;
                        }
                    }
                }
                catch
                {
                }

                return settings;
            }
        }

        public static void Save(AutoTrimSettings settings)
        {
            if (settings == null) return;

            lock (SyncRoot)
            {
                try
                {
                    string path = GetFilePath();
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"Mode\t{settings.Mode}");
                    sb.AppendLine($"OnlyTrimCrossingObjects\t{settings.OnlyTrimCrossingObjects}");
                    sb.AppendLine($"Tolerance\t{settings.Tolerance.ToString(CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"ShowTransientPreview\t{settings.ShowTransientPreview}");
                    sb.AppendLine($"ConfirmBeforeCommit\t{settings.ConfirmBeforeCommit}");
                    sb.AppendLine($"KeepOriginalProperties\t{settings.KeepOriginalProperties}");

                    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
