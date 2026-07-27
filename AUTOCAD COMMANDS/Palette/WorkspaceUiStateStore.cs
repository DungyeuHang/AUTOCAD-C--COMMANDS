using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AUTOCAD_COMMANDS
{
    // Một store duy nhất cho toàn bộ trạng thái UI của plugin.
    // Lưu ở AppData để việc cập nhật DLL/bundle không làm mất vị trí và visibility.
    internal static class WorkspaceUiStateStore
    {
        private static readonly object SyncRoot = new object();
        private static Dictionary<string, string> _state =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _filePath;
        private static bool _initialized;

        public static void Initialize(Editor editor)
        {
            EnsureInitialized();

            if (editor != null && !string.IsNullOrEmpty(_filePath))
            {
                editor.WriteMessage("\n[DUNGX] UI state file: " + _filePath);
            }
        }

        public static void Commit(Editor editor)
        {
            EnsureInitialized();

            lock (SyncRoot)
            {
                PersistLocked(editor);
            }
        }

        public static string GetValue(string key)
        {
            EnsureInitialized();

            lock (SyncRoot)
            {
                return _state.TryGetValue(key, out string value) ? value : string.Empty;
            }
        }

        public static void SaveValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            EnsureInitialized();

            lock (SyncRoot)
            {
                if (value == null)
                {
                    _state.Remove(key);
                }
                else
                {
                    _state[key] = value;
                }

                // Ghi ngay để state không phụ thuộc vào việc AutoCAD có gọi
                // IExtensionApplication.Terminate hay không.
                PersistLocked(null);
            }
        }

        public static void SaveValues(IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            EnsureInitialized();

            lock (SyncRoot)
            {
                foreach (KeyValuePair<string, string> pair in values)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        _state[pair.Key] = pair.Value ?? string.Empty;
                    }
                }

                // Quan trọng với DXPALETTE/DXRIBBON: trạng thái được lưu
                // trước khi AutoCAD bắt đầu teardown giao diện.
                PersistLocked(null);
            }
        }

        public static bool TryGetBool(string key, out bool value)
        {
            value = false;
            string text = GetValue(key);

            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            return string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "no", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetInt(string key, out int value)
        {
            return int.TryParse(
                GetValue(key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        public static bool TryGetDouble(string key, out double value)
        {
            return double.TryParse(
                GetValue(key),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        public static bool TryGetPoint(string keyPrefix, out Point point)
        {
            point = Point.Empty;

            if (!TryGetInt(keyPrefix + ".x", out int x) ||
                !TryGetInt(keyPrefix + ".y", out int y))
            {
                return false;
            }

            point = new Point(x, y);
            return true;
        }

        public static bool TryGetSize(string keyPrefix, out Size size)
        {
            size = Size.Empty;

            if (!TryGetInt(keyPrefix + ".width", out int width) ||
                !TryGetInt(keyPrefix + ".height", out int height) ||
                width <= 0 ||
                height <= 0)
            {
                return false;
            }

            size = new Size(width, height);
            return true;
        }

        public static string ToInvariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void EnsureInitialized()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrWhiteSpace(appData))
                {
                    appData = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                }

                string stateDirectory = Path.Combine(appData, "DUNGX", "AUTOCAD_COMMANDS");
                _filePath = Path.Combine(stateDirectory, "dungx_workspace_ui_state.txt");

                MigrateLegacyState(stateDirectory);
                LoadStateLocked();
                _initialized = true;
            }
        }

        private static void MigrateLegacyState(string stateDirectory)
        {
            if (File.Exists(_filePath))
            {
                return;
            }

            string assemblyDirectory =
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string legacyPath = Path.Combine(assemblyDirectory, "dungx_workspace_ui_state.txt");

            if (!File.Exists(legacyPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(stateDirectory);
                File.Copy(legacyPath, _filePath, false);
            }
            catch
            {
                // Nếu migration thất bại, state mới vẫn có thể được tạo lúc save.
            }
        }

        private static void LoadStateLocked()
        {
            _state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(_filePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    if (key.Length > 0)
                    {
                        _state[key] = line.Substring(separator + 1).Trim();
                    }
                }
            }
            catch
            {
                _state.Clear();
            }
        }

        private static void PersistLocked(Editor editor)
        {
            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (string.IsNullOrEmpty(directory))
                {
                    return;
                }

                Directory.CreateDirectory(directory);

                string temporaryPath = _filePath + ".tmp";
                string[] lines = _state
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Key + "=" + pair.Value)
                    .ToArray();

                File.WriteAllLines(temporaryPath, lines, Encoding.UTF8);

                if (File.Exists(_filePath))
                {
                    File.Replace(temporaryPath, _filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, _filePath);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string temporaryPath = _filePath + ".tmp";
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors.
                }

                editor?.WriteMessage("\n[DUNGX] Error saving UI state: " + ex.Message);
            }
        }
    }
}
