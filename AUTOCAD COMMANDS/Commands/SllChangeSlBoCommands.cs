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
    // SLL_CHANGE_SL_BO
    // Mục đích: đổi số lượng trong TEXT/MTEXT theo tỉ lệ số bộ gốc -> số bộ mới,
    // hỗ trợ NHIỀU cấu trúc đầu vào (vì các phụ kiện khác nguồn có thể ghi SL khác kiểu
    // nhau trong cùng 1 vùng chọn) và MỘT cấu trúc đầu ra duy nhất.
    // Lưu ý: chỉ đổi phần khớp với cấu trúc, giữ nguyên toàn bộ nội dung còn lại.
    // Mỗi đối tượng được tính lại từ giá trị SL gốc của chính nó (không cascading,
    // không dùng FIND/REPLACE của AutoCAD - text mới sinh ra không bị xử lý lại).
    // Các cấu trúc đã nhập được lưu lại (qua WorkspaceUiStateStore) để dùng lại lần sau.
    // ======================================================
    public class SllChangeSlBoCommands
    {
        internal const string PlaceholderToken = "{X}";
        internal const string DefaultFormatPattern = "SL: " + PlaceholderToken;
        private const string RecentFormatsKey = "sll_change_sl_bo.recent_formats";
        private const char RecentFormatsSeparator = (char)0x1F;
        private const int MaxRecentFormats = 10;

        private const string LastOriginalBundlesKey = "sll_change_sl_bo.last_original_bundles";
        private const string LastNewBundlesKey = "sll_change_sl_bo.last_new_bundles";
        private const string LastInputFormatsKey = "sll_change_sl_bo.last_input_formats";
        private const string LastOutputFormatKey = "sll_change_sl_bo.last_output_format";

        // Flow:
        // 1. Hiện bảng nhập: số bộ gốc, số bộ mới, N cấu trúc SL đầu vào và 1 cấu trúc SL đầu ra
        //    (dùng {X} làm placeholder số lượng), tự điền lại theo đúng những gì người dùng đã
        //    nhập ở lần chạy trước (xem LoadLastSession/SaveLastSession). ComboBox gợi ý lại các
        //    cấu trúc đã dùng gần đây.
        // 2. Quét chọn đối tượng (chỉ xử lý DBText/MText trong vùng chọn).
        // 3. Với mỗi text, thử từng cấu trúc đầu vào (ưu tiên cấu trúc "cụ thể" hơn - xem
        //    BuildInputPatternRegexes), tìm phần khớp đầu tiên, tính lại SL theo tỉ lệ rồi
        //    sinh ra theo cấu trúc đầu ra, ghi trực tiếp vào entity đó.
        [CommandMethod("SLL_CHANGE_SL_BO")]
        public void ChangeSlBo()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            List<string> recentFormats = LoadRecentFormats();
            SllChangeSlBoSession lastSession = LoadLastSession();

            int originalBundles;
            int newBundles;
            List<string> inputPatterns;
            string outputPattern;

            using (SllChangeSlBoForm form = new SllChangeSlBoForm(recentFormats, lastSession))
            {
                if (Application.ShowModalDialog(form) != WF.DialogResult.OK)
                {
                    return;
                }

                originalBundles = form.OriginalBundles;
                newBundles = form.NewBundles;
                inputPatterns = form.InputPatterns;
                outputPattern = form.OutputPattern;
            }

            foreach (string inputPattern in inputPatterns)
            {
                RememberFormat(recentFormats, inputPattern);
            }

            RememberFormat(recentFormats, outputPattern);
            SaveLastSession(originalBundles, newBundles, inputPatterns, outputPattern);

            List<Regex> inputPatternRegexes = BuildInputPatternRegexes(inputPatterns);

            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nChọn các đối tượng TEXT/MTEXT cần đổi SL: "
            };

            PromptSelectionResult selectionResult = ed.GetSelection(selectionOptions);
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null ||
                selectionResult.Value.Count == 0)
            {
                ed.WriteMessage("\nSLL_CHANGE_SL_BO: chưa chọn được đối tượng nào.");
                return;
            }

            ObjectId[] objectIds = selectionResult.Value.GetObjectIds();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int changedCount = 0;
                int skippedNoMatchCount = 0;
                int errorCount = 0;

                foreach (ObjectId objectId in objectIds)
                {
                    if (objectId.IsNull)
                    {
                        continue;
                    }

                    Entity entity = tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity == null)
                    {
                        continue;
                    }

                    string originalText;
                    if (entity is DBText dbTextRead)
                    {
                        originalText = dbTextRead.TextString;
                    }
                    else if (entity is MText mTextRead)
                    {
                        originalText = mTextRead.Contents;
                    }
                    else
                    {
                        continue;
                    }

                    if (!TryComputeNewText(
                            originalText,
                            inputPatternRegexes,
                            outputPattern,
                            originalBundles,
                            newBundles,
                            out string updatedText,
                            out string error))
                    {
                        if (error != null)
                        {
                            errorCount++;
                            ed.WriteMessage($"\nSLL_CHANGE_SL_BO: bỏ qua đối tượng {objectId.Handle} - {error}");
                        }
                        else
                        {
                            skippedNoMatchCount++;
                        }

                        continue;
                    }

                    entity.UpgradeOpen();

                    if (entity is DBText dbText)
                    {
                        dbText.TextString = updatedText;
                    }
                    else if (entity is MText mText)
                    {
                        mText.Contents = updatedText;
                    }

                    changedCount++;
                }

                tr.Commit();

                ed.WriteMessage(
                    $"\nSLL_CHANGE_SL_BO: đã đổi {changedCount} text (bỏ qua {skippedNoMatchCount} không khớp cấu trúc nào, {errorCount} lỗi SL không chia hết cho số bộ gốc).");
            }
        }

        // Sắp xếp các cấu trúc đầu vào theo độ "cụ thể" giảm dần (đo bằng tổng số ký tự literal,
        // tức chiều dài chuỗi cấu trúc - phần {X} không đổi nên chuỗi dài hơn = nhiều chữ cố định
        // hơn = cụ thể hơn). Cấu trúc trùng nhau (so sánh y hệt từng ký tự) chỉ giữ lại 1 lần.
        // Quy tắc xác định khi có nhiều cấu trúc cùng khớp 1 text: với mỗi entity, các cấu trúc
        // được thử theo thứ tự cụ thể nhất trước, cấu trúc ĐẦU TIÊN khớp được dùng (xem
        // TryComputeNewText) - cấu trúc dài/cụ thể hơn thắng cấu trúc ngắn/chung chung hơn;
        // bằng độ dài thì giữ nguyên thứ tự người dùng đã nhập (LINQ OrderBy ổn định - stable sort).
        private static List<Regex> BuildInputPatternRegexes(IEnumerable<string> patterns)
        {
            return patterns
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(pattern => pattern.Length)
                .Select(BuildPatternRegex)
                .ToList();
        }

        // Tìm cấu trúc đầu vào đầu tiên (theo thứ tự đã sắp trong inputPatternRegexes) khớp với
        // text, rồi tính lại SL theo tỉ lệ originalBundles -> newBundles, sinh ra theo cấu trúc
        // đầu ra desiredPattern.
        // Trả về false + error khác null nếu có SL không chia hết cho số bộ gốc (không sửa gì cả).
        // Trả về false + error null nếu text không khớp cấu trúc đầu vào nào (bỏ qua, không phải lỗi).
        private static bool TryComputeNewText(
            string originalText,
            List<Regex> inputPatternRegexes,
            string desiredPattern,
            int originalBundles,
            int newBundles,
            out string updatedText,
            out string error)
        {
            updatedText = null;
            error = null;

            if (string.IsNullOrEmpty(originalText))
            {
                return false;
            }

            Regex matchedRegex = null;
            foreach (Regex regex in inputPatternRegexes)
            {
                if (regex.IsMatch(originalText))
                {
                    matchedRegex = regex;
                    break;
                }
            }

            if (matchedRegex == null)
            {
                return false;
            }

            string localError = null;

            string result = matchedRegex.Replace(originalText, match =>
            {
                if (localError != null)
                {
                    return match.Value;
                }

                string digits = match.Groups["value"].Value;

                if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long originalSl))
                {
                    localError = $"không đọc được số lượng \"{match.Value}\".";
                    return match.Value;
                }

                if (originalSl % originalBundles != 0)
                {
                    localError =
                        $"SL {originalSl} không chia hết cho số bộ gốc {originalBundles}.";
                    return match.Value;
                }

                long newSl = originalSl / originalBundles * newBundles;
                return ApplyPattern(desiredPattern, newSl);
            });

            if (localError != null)
            {
                error = localError;
                return false;
            }

            updatedText = result;
            return true;
        }

        // Cấu trúc đã được validate (đúng 1 placeholder {X}) trước khi tới đây.
        // Phần chữ trước/sau {X} được escape làm literal, {X} trở thành nhóm số nguyên.
        private static Regex BuildPatternRegex(string pattern)
        {
            int placeholderIndex = pattern.IndexOf(PlaceholderToken, StringComparison.Ordinal);
            string before = pattern.Substring(0, placeholderIndex);
            string after = pattern.Substring(placeholderIndex + PlaceholderToken.Length);

            string regexPattern = Regex.Escape(before) + "(?<value>\\d+)" + Regex.Escape(after);
            return new Regex(regexPattern, RegexOptions.Compiled);
        }

        private static string ApplyPattern(string pattern, long value)
        {
            int placeholderIndex = pattern.IndexOf(PlaceholderToken, StringComparison.Ordinal);
            string before = pattern.Substring(0, placeholderIndex);
            string after = pattern.Substring(placeholderIndex + PlaceholderToken.Length);

            return before + value.ToString(CultureInfo.InvariantCulture) + after;
        }

        // Cấu trúc hợp lệ khi chứa đúng 1 placeholder "{X}" (vd "SL: {X}", "(SL: {X})").
        // Từ chối: không có {X}, có {Y}/{x}, hoặc có nhiều hơn 1 {X}.
        // internal: dùng chung với SllChangeSlBoForm để validate ngay trong bảng nhập.
        internal static bool TryValidatePattern(string pattern, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(pattern))
            {
                error = "Cấu trúc không được để trống.";
                return false;
            }

            int count = 0;
            int searchIndex = 0;

            while (true)
            {
                int found = pattern.IndexOf(PlaceholderToken, searchIndex, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                count++;
                searchIndex = found + PlaceholderToken.Length;
            }

            if (count != 1)
            {
                error = $"Cấu trúc \"{pattern}\" phải chứa đúng 1 placeholder {{X}} (tìm thấy {count}).";
                return false;
            }

            return true;
        }

        private static List<string> LoadRecentFormats()
        {
            string raw = WorkspaceUiStateStore.GetValue(RecentFormatsKey);
            if (string.IsNullOrEmpty(raw))
            {
                return new List<string>();
            }

            return raw.Split(RecentFormatsSeparator)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToList();
        }

        private static void RememberFormat(List<string> recentFormats, string pattern)
        {
            recentFormats.RemoveAll(value => string.Equals(value, pattern, StringComparison.Ordinal));
            recentFormats.Insert(0, pattern);

            if (recentFormats.Count > MaxRecentFormats)
            {
                recentFormats.RemoveRange(MaxRecentFormats, recentFormats.Count - MaxRecentFormats);
            }

            WorkspaceUiStateStore.SaveValue(
                RecentFormatsKey,
                string.Join(RecentFormatsSeparator.ToString(), recentFormats));
        }

        // Toàn bộ trạng thái bảng nhập ở lần chạy gần nhất (khác với "recent formats" ở trên,
        // vốn chỉ là danh sách gợi ý cho ComboBox) - dùng để tự điền lại bảng y hệt lần trước.
        private static SllChangeSlBoSession LoadLastSession()
        {
            int originalBundles = WorkspaceUiStateStore.TryGetInt(LastOriginalBundlesKey, out int loadedOriginal) && loadedOriginal > 0
                ? loadedOriginal
                : 1;

            int newBundles = WorkspaceUiStateStore.TryGetInt(LastNewBundlesKey, out int loadedNew) && loadedNew > 0
                ? loadedNew
                : 1;

            List<string> inputPatterns = null;
            string rawInputPatterns = WorkspaceUiStateStore.GetValue(LastInputFormatsKey);
            if (!string.IsNullOrEmpty(rawInputPatterns))
            {
                inputPatterns = rawInputPatterns
                    .Split(RecentFormatsSeparator)
                    .Where(value => !string.IsNullOrEmpty(value))
                    .ToList();
            }

            if (inputPatterns == null || inputPatterns.Count == 0)
            {
                inputPatterns = new List<string> { DefaultFormatPattern };
            }

            string outputPattern = WorkspaceUiStateStore.GetValue(LastOutputFormatKey);
            if (string.IsNullOrEmpty(outputPattern))
            {
                outputPattern = DefaultFormatPattern;
            }

            return new SllChangeSlBoSession
            {
                OriginalBundles = originalBundles,
                NewBundles = newBundles,
                InputPatterns = inputPatterns,
                OutputPattern = outputPattern
            };
        }

        private static void SaveLastSession(
            int originalBundles,
            int newBundles,
            List<string> inputPatterns,
            string outputPattern)
        {
            WorkspaceUiStateStore.SaveValue(LastOriginalBundlesKey, WorkspaceUiStateStore.ToInvariant(originalBundles));
            WorkspaceUiStateStore.SaveValue(LastNewBundlesKey, WorkspaceUiStateStore.ToInvariant(newBundles));
            WorkspaceUiStateStore.SaveValue(
                LastInputFormatsKey,
                string.Join(RecentFormatsSeparator.ToString(), inputPatterns));
            WorkspaceUiStateStore.SaveValue(LastOutputFormatKey, outputPattern);
        }

        // Gộp toàn bộ giá trị bảng nhập ở lần chạy gần nhất để truyền vào SllChangeSlBoForm.
        internal sealed class SllChangeSlBoSession
        {
            public int OriginalBundles { get; set; }

            public int NewBundles { get; set; }

            public List<string> InputPatterns { get; set; }

            public string OutputPattern { get; set; }
        }
    }
}
