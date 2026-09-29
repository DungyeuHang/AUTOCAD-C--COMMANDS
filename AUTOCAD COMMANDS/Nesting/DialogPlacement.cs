using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// Nho KICH THUOC + VI TRI cac hop thoai GHOPHOI giua cac lan chay (keo rong bang KIEM TRA
    /// mot lan la lan sau van rong nhu vay). File %AppData%\DUNGX\AUTOCAD_COMMANDS\ghophoi_ui.tsv.
    /// Vi tri cu nam ngoai moi man hinh (vd. rut man hinh phu) thi bo qua, mo giua nhu mac dinh.
    /// </summary>
    internal static class DialogPlacement
    {
        private static readonly object SyncRoot = new object();

        private static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DUNGX", "AUTOCAD_COMMANDS", "ghophoi_ui.tsv");
            }
        }

        /// <summary>Goi o cuoi constructor cua hop thoai (truoc khi hien).</summary>
        public static void Attach(Form form, string name)
        {
            try
            {
                Rectangle saved;
                if (TryGet(name, out saved) && OnSomeScreen(saved))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Bounds = new Rectangle(saved.Location,
                        new Size(Math.Max(saved.Width, form.MinimumSize.Width), Math.Max(saved.Height, form.MinimumSize.Height)));
                }
            }
            catch (Exception)
            {
                // chi la tien ich
            }

            form.FormClosing += (s, e) =>
            {
                try
                {
                    Rectangle r = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
                    if (r.Width > 0 && r.Height > 0) Set(name, r);
                }
                catch (Exception)
                {
                    // chi la tien ich
                }
            };
        }

        private static bool OnSomeScreen(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle overlap = Rectangle.Intersect(s.WorkingArea, r);
                if (overlap.Width >= 100 && overlap.Height >= 60) return true;
            }

            return false;
        }

        private static bool TryGet(string name, out Rectangle r)
        {
            r = Rectangle.Empty;
            Dictionary<string, Rectangle> all = ReadAll();
            return all.TryGetValue(name, out r);
        }

        private static void Set(string name, Rectangle r)
        {
            lock (SyncRoot)
            {
                Dictionary<string, Rectangle> all = ReadAll();
                all[name] = r;
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, Rectangle> kv in all)
                {
                    sb.Append(kv.Key).Append('\t')
                      .Append(kv.Value.X.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.Y.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.Width.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(kv.Value.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
        }

        private static Dictionary<string, Rectangle> ReadAll()
        {
            Dictionary<string, Rectangle> all = new Dictionary<string, Rectangle>(StringComparer.Ordinal);
            if (!File.Exists(FilePath)) return all;

            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string[] f = line.Split('\t');
                if (f.Length < 5) continue;
                int x, y, w, h;
                CultureInfo ci = CultureInfo.InvariantCulture;
                if (int.TryParse(f[1], NumberStyles.Integer, ci, out x) && int.TryParse(f[2], NumberStyles.Integer, ci, out y) &&
                    int.TryParse(f[3], NumberStyles.Integer, ci, out w) && int.TryParse(f[4], NumberStyles.Integer, ci, out h) &&
                    w > 0 && h > 0)
                {
                    all[f[0]] = new Rectangle(x, y, w, h);
                }
            }

            return all;
        }
    }
}
