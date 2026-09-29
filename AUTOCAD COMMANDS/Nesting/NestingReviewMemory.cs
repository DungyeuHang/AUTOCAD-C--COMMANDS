using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using AUTOCAD_COMMANDS.Nesting.Recognition;

namespace AUTOCAD_COMMANDS.Nesting
{
    /// <summary>Mot chinh sua tay o bang KIEM TRA cho mot chi tiet. Null = truong do khong bi sua.</summary>
    internal sealed class ReviewEdit
    {
        /// <summary>Danh tinh chi tiet: handle cac doi tuong hinh cua no, sap xep (xem <see cref="NestingReviewMemory.KeyOf"/>).</summary>
        public string Key = string.Empty;
        public int? Quantity;
        public string Material;
        public string Order;
        public bool? Include;
        public bool? Confirmed;

        public bool IsEmpty
        {
            get { return Quantity == null && Material == null && Order == null && Include == null && Confirmed == null; }
        }
    }

    /// <summary>
    /// NHO nhung gi nguoi dung SUA TAY o bang KIEM TRA (SL, vat lieu, don, tick Ghep / Xac nhan)
    /// cho tung chi tiet, theo tung ban ve - chay lai GHOPHOI thi ap lai, khong phai sua lai tu
    /// dau. Chi tiet duoc nhan ra bang HANDLE cac doi tuong hinh cua no, nen ban ve khong doi
    /// thi ap dung dung cai; ve lai / sua hinh chi tiet thi handle doi va chinh sua cu tu het
    /// tac dung (dung nhu mong doi). Nut RESET tren bang don hang xoa het.
    ///
    /// Luu canh ban luu bang don (<see cref="NestingOrderMemory"/>): ban ve co ten -> file
    /// "..._edits.tsv"; ban ve chua luu -> chi trong bo nho.
    /// </summary>
    internal static class NestingReviewMemory
    {
        private static readonly ConditionalWeakTable<Database, Dictionary<string, ReviewEdit>> Unsaved =
            new ConditionalWeakTable<Database, Dictionary<string, ReviewEdit>>();

        private static readonly object SyncRoot = new object();

        /// <summary>Handle (hex) cac doi tuong hinh cua chi tiet, sap xep, noi bang dau phay.</summary>
        public static string KeyOf(NestReadResult read, RecognizedPart part)
        {
            List<long> handles = new List<long>();
            foreach (int src in part.GeometrySources)
            {
                if (src < 0 || src >= read.Sources.Count) continue;
                ObjectId id = read.Sources[src].Id;
                if (id.IsNull) continue;
                long h = id.Handle.Value;
                if (!handles.Contains(h)) handles.Add(h);
            }

            if (handles.Count == 0) return string.Empty;
            handles.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (long h in handles)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(h.ToString("X", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Ghi nhan chinh sua cua lan chay nay: chi tiet co mat trong lan nay thi thay bang chinh
        /// sua moi (khong con sua gi thi xoa), chi tiet KHONG co mat (vd. lan nay chi quet mot
        /// don) thi giu nguyen chinh sua cu.
        /// </summary>
        public static void Merge(Database db, string drawingPath, IEnumerable<string> seenKeys, IEnumerable<ReviewEdit> edits)
        {
            lock (SyncRoot)
            {
                Dictionary<string, ReviewEdit> all = LoadAll(db, drawingPath);
                foreach (string k in seenKeys) all.Remove(k);
                foreach (ReviewEdit e in edits)
                {
                    if (e != null && e.Key.Length > 0 && !e.IsEmpty) all[e.Key] = e;
                }

                Unsaved.Remove(db);
                Unsaved.Add(db, all);
                WriteFile(EditsFile(drawingPath), all);
            }
        }

        /// <summary>Chinh sua da luu, theo danh tinh chi tiet. Khong co = rong.</summary>
        public static Dictionary<string, ReviewEdit> Load(Database db, string drawingPath)
        {
            lock (SyncRoot)
            {
                return new Dictionary<string, ReviewEdit>(LoadAll(db, drawingPath), StringComparer.Ordinal);
            }
        }

        /// <summary>Chi cho kiem thu: quen ban trong bo nho (nhu mo lai AutoCAD), file van giu.</summary>
        internal static void ForgetSession(Database db)
        {
            lock (SyncRoot) Unsaved.Remove(db);
        }

        public static void Clear(Database db, string drawingPath)
        {
            lock (SyncRoot)
            {
                Unsaved.Remove(db);
                string file = EditsFile(drawingPath);
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

        private static Dictionary<string, ReviewEdit> LoadAll(Database db, string drawingPath)
        {
            Dictionary<string, ReviewEdit> all;
            if (Unsaved.TryGetValue(db, out all)) return all;
            return ReadFile(EditsFile(drawingPath)) ?? new Dictionary<string, ReviewEdit>(StringComparer.Ordinal);
        }

        private static string EditsFile(string drawingPath)
        {
            string f = NestingOrderMemory.FileFor(drawingPath);
            return f == null ? null : Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_edits.tsv");
        }

        // Dong: key \t SL \t vat lieu \t don \t ghep \t xac nhan ; truong khong sua = "-".
        private static void WriteFile(string file, Dictionary<string, ReviewEdit> all)
        {
            if (file == null) return;
            try
            {
                if (all.Count == 0)
                {
                    if (File.Exists(file)) File.Delete(file);
                    return;
                }

                StringBuilder sb = new StringBuilder("#GHOPHOI_EDITS\n");
                foreach (ReviewEdit e in all.Values)
                {
                    sb.Append(e.Key).Append('\t')
                      .Append(e.Quantity.HasValue ? e.Quantity.Value.ToString(CultureInfo.InvariantCulture) : "-").Append('\t')
                      .Append(Text(e.Material)).Append('\t')
                      .Append(Text(e.Order)).Append('\t')
                      .Append(Flag(e.Include)).Append('\t')
                      .Append(Flag(e.Confirmed)).Append('\n');
                }

                Directory.CreateDirectory(Path.GetDirectoryName(file));
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

        private static Dictionary<string, ReviewEdit> ReadFile(string file)
        {
            if (file == null || !File.Exists(file)) return null;
            try
            {
                Dictionary<string, ReviewEdit> all = new Dictionary<string, ReviewEdit>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] f = line.Split('\t');
                    if (f.Length < 6 || f[0].Length == 0) continue;

                    ReviewEdit e = new ReviewEdit { Key = f[0] };
                    int q;
                    if (f[1] != "-" && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out q) && q >= 1) e.Quantity = q;
                    e.Material = ReadText(f[2]);
                    e.Order = ReadText(f[3]);
                    e.Include = ReadFlag(f[4]);
                    e.Confirmed = ReadFlag(f[5]);
                    if (!e.IsEmpty) all[e.Key] = e;
                }

                return all;
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

        // Van ban: "-" = khong sua; con lai co tien to "=" de phan biet voi chuoi rong.
        private static string Text(string s)
        {
            return s == null ? "-" : "=" + s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string ReadText(string s)
        {
            return s.StartsWith("=", StringComparison.Ordinal) ? s.Substring(1) : null;
        }

        private static string Flag(bool? b)
        {
            return b.HasValue ? (b.Value ? "1" : "0") : "-";
        }

        private static bool? ReadFlag(string s)
        {
            if (s == "1") return true;
            if (s == "0") return false;
            return null;
        }
    }
}
