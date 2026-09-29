using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>
    /// NHO BANG DON HANG lan chay truoc (ten don + cac doi tuong da quet) cho TUNG BAN VE, de
    /// lan sau mo GHOPHOI la bang da co san: quet sai / thieu thi chi sua dong do, khong phai
    /// quet lai het. Nut RESET tren bang goi <see cref="Clear"/>.
    ///
    /// Doi tuong duoc nho bang HANDLE (khong doi khi dong / mo lai ban ve), khong phai ObjectId.
    /// Ban ve DA CO TEN: ghi ra file %AppData%\DUNGX\AUTOCAD_COMMANDS\ghophoi_orders\ (mot file
    /// mot ban ve, dat ten theo duong dan) - tat AutoCAD van con. Ban ve CHUA LUU: chi nho
    /// trong bo nho den khi dong ban ve. KHONG ghi gi vao chinh ban ve cua nguoi dung.
    /// </summary>
    internal static class NestingOrderMemory
    {
        private sealed class Snapshot
        {
            public readonly List<KeyValuePair<string, List<long>>> Rows = new List<KeyValuePair<string, List<long>>>();
        }

        private static readonly ConditionalWeakTable<Database, Snapshot> Unsaved = new ConditionalWeakTable<Database, Snapshot>();
        private static readonly object SyncRoot = new object();

        private static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DUNGX", "AUTOCAD_COMMANDS", "ghophoi_orders");
            }
        }

        /// <summary>Luu bang hien tai. Bang khong co dong nao da quet = xoa ban luu.</summary>
        /// <param name="drawingPath">Duong dan ban ve, null/rong neu ban ve chua duoc luu.</param>
        public static void Save(Database db, string drawingPath, IEnumerable<NestingOrderEntry> entries)
        {
            Snapshot snap = new Snapshot();
            foreach (NestingOrderEntry e in entries)
            {
                List<long> handles = new List<long>();
                foreach (ObjectId id in e.Ids)
                {
                    if (id.IsNull || id.IsErased || id.Database != db) continue;
                    handles.Add(id.Handle.Value);
                }

                if (handles.Count > 0) snap.Rows.Add(new KeyValuePair<string, List<long>>(e.Name ?? string.Empty, handles));
            }

            if (snap.Rows.Count == 0)
            {
                ClearOrders(db, drawingPath);
                return;
            }

            lock (SyncRoot)
            {
                Unsaved.Remove(db);
                Unsaved.Add(db, snap);

                string file = FileFor(drawingPath);
                if (file == null) return;

                StringBuilder sb = new StringBuilder();
                sb.Append("#GHOPHOI_ORDERS\t").Append(drawingPath).Append('\n');
                foreach (KeyValuePair<string, List<long>> row in snap.Rows)
                {
                    sb.Append(Clean(row.Key)).Append('\t');
                    for (int i = 0; i < row.Value.Count; i++)
                    {
                        if (i > 0) sb.Append(';');
                        sb.Append(row.Value[i].ToString("X", CultureInfo.InvariantCulture));
                    }

                    sb.Append('\n');
                }

                try
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                }
                catch (IOException)
                {
                    // van con ban trong bo nho cho phien nay
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>
        /// Nap lai bang da luu. Doi tuong nao da bi xoa khoi ban ve thi bo qua va dem vao
        /// <paramref name="missing"/> de bao cho nguoi dung. Khong co ban luu = danh sach rong.
        /// </summary>
        public static List<NestingOrderEntry> Load(Database db, string drawingPath, out int missing)
        {
            missing = 0;
            Snapshot snap = null;

            lock (SyncRoot)
            {
                // Uu tien ban trong bo nho (moi nhat trong phien nay), roi moi den file.
                if (!Unsaved.TryGetValue(db, out snap)) snap = ReadFile(FileFor(drawingPath));
            }

            List<NestingOrderEntry> result = new List<NestingOrderEntry>();
            if (snap == null) return result;

            foreach (KeyValuePair<string, List<long>> row in snap.Rows)
            {
                NestingOrderEntry entry = new NestingOrderEntry { Name = row.Key };
                foreach (long h in row.Value)
                {
                    ObjectId id;
                    if (db.TryGetObjectId(new Handle(h), out id) && !id.IsNull && !id.IsErased && !entry.Ids.Contains(id))
                    {
                        entry.Ids.Add(id);
                    }
                    else
                    {
                        missing++;
                    }
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Nut RESET: xoa HET ban luu cua ban ve nay - bang don hang lan chinh sua o bang KIEM
        /// TRA (<see cref="NestingReviewMemory"/>).
        /// </summary>
        public static void Clear(Database db, string drawingPath)
        {
            ClearOrders(db, drawingPath);
            NestingReviewMemory.Clear(db, drawingPath);
        }

        private static void ClearOrders(Database db, string drawingPath)
        {
            lock (SyncRoot)
            {
                Unsaved.Remove(db);
                string file = FileFor(drawingPath);
                try
                {
                    if (file != null && File.Exists(file)) File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static Snapshot ReadFile(string file)
        {
            if (file == null || !File.Exists(file)) return null;

            try
            {
                Snapshot snap = new Snapshot();
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t');
                    if (tab < 0) continue;

                    List<long> handles = new List<long>();
                    foreach (string part in line.Substring(tab + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        long h;
                        if (long.TryParse(part.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out h)) handles.Add(h);
                    }

                    if (handles.Count > 0) snap.Rows.Add(new KeyValuePair<string, List<long>>(line.Substring(0, tab).Trim(), handles));
                }

                return snap.Rows.Count > 0 ? snap : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>File luu cua mot ban ve: ten = bam SHA1 cua duong dan (khong phan biet hoa thuong).</summary>
        internal static string FileFor(string drawingPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;

            string key;
            try
            {
                key = Path.GetFullPath(drawingPath).ToUpperInvariant();
            }
            catch (Exception)
            {
                key = drawingPath.Trim().ToUpperInvariant();
            }

            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return Path.Combine(Folder, Path.GetFileNameWithoutExtension(drawingPath) + "_" + sb + ".tsv");
            }
        }

        private static string Clean(string s)
        {
            return (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
