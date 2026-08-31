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
    // với cấu trúc chứa số lượng (vd "SL: {X}") do người dùng nhập, không hardcode.
    // Lưu ý: chỉ đổi phần khớp với cấu trúc, giữ nguyên toàn bộ nội dung còn lại.
    // Mỗi đối tượng được tính lại từ giá trị SL gốc của chính nó (không cascading,
    // không dùng FIND/REPLACE của AutoCAD).
    // Các cấu trúc đã nhập được lưu lại (qua WorkspaceUiStateStore) để dùng lại lần sau.
    // ======================================================
    public class SllChangeSlBoCommands
    {
        private const string PlaceholderToken = "{X}";
        private const string DefaultFormatPattern = "SL: " + PlaceholderToken;
        private const string RecentFormatsKey = "sll_change_sl_bo.recent_formats";
        private const char RecentFormatsSeparator = (char)0x1F;
        private const int MaxRecentFormats = 6;

        // Flow:
        // 1. Hỏi số bộ gốc, số bộ mới.
        // 2. Hỏi cấu trúc SL hiện tại và cấu trúc SL mong muốn (dùng {X} làm placeholder số lượng).
        //    Gợi ý các cấu trúc đã dùng gần đây, cho phép gõ số thứ tự để dùng lại.
        // 3. Quét chọn đối tượng (chỉ xử lý DBText/MText trong vùng chọn).
        // 4. Với mỗi text, tìm phần khớp cấu trúc hiện tại, tính lại SL theo tỉ lệ rồi
        //    sinh ra theo cấu trúc mong muốn, ghi trực tiếp vào entity đó.
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

            if (!TryPromptPositiveInteger(ed, "\nSố bộ gốc: ", out int originalBundles))
            {
                return;
            }

            if (!TryPromptPositiveInteger(ed, "\nSố bộ mới: ", out int newBundles))
            {
                return;
            }

            List<string> recentFormats = LoadRecentFormats();
            string currentDefault = recentFormats.Count > 0 ? recentFormats[0] : DefaultFormatPattern;

            if (!TryPromptFormatPattern(ed, "Cấu trúc SL hiện tại", currentDefault, recentFormats, out string currentPattern))
            {
                return;
            }

            RememberFormat(recentFormats, currentPattern);

            string desiredDefault = recentFormats.Count > 0 ? recentFormats[0] : DefaultFormatPattern;

            if (!TryPromptFormatPattern(ed, "Cấu trúc SL mong muốn", desiredDefault, recentFormats, out string desiredPattern))
            {
                return;
            }

            RememberFormat(recentFormats, desiredPattern);

            Regex currentPatternRegex = BuildPatternRegex(currentPattern);

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
                            currentPatternRegex,
                            desiredPattern,
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
                    $"\nSLL_CHANGE_SL_BO: đã đổi {changedCount} text (bỏ qua {skippedNoMatchCount} không khớp cấu trúc, {errorCount} lỗi SL không chia hết cho số bộ gốc).");
            }
        }

        // Tìm mọi phần khớp với cấu trúc hiện tại trong text và tính lại SL theo tỉ lệ
        // originalBundles -> newBundles, sinh ra theo cấu trúc mong muốn.
        // Trả về false + error khác null nếu có SL không chia hết cho số bộ gốc (không sửa gì cả).
        // Trả về false + error null nếu text không khớp cấu trúc hiện tại (bỏ qua, không phải lỗi).
        private static bool TryComputeNewText(
            string originalText,
            Regex currentPatternRegex,
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

            if (!currentPatternRegex.IsMatch(originalText))
            {
                return false;
            }

            string localError = null;

            string result = currentPatternRegex.Replace(originalText, match =>
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
        private static bool TryValidatePattern(string pattern, out string error)
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

        private static bool TryPromptFormatPattern(
            Editor ed,
            string label,
            string defaultPattern,
            List<string> recentFormats,
            out string pattern)
        {
            pattern = null;

            while (true)
            {
                StringBuilder message = new StringBuilder();
                message.Append('\n').Append(label).Append(" (dùng {X} làm số lượng)");

                if (recentFormats.Count > 0)
                {
                    message.Append("\n  Mẫu gần đây: ");
                    for (int i = 0; i < recentFormats.Count; i++)
                    {
                        if (i > 0)
                        {
                            message.Append("  ");
                        }

                        message.Append('[').Append(i + 1).Append("] \"").Append(recentFormats[i]).Append('"');
                    }

                    message.Append(" - gõ số thứ tự để dùng lại mẫu.");
                }

                message.Append('\n').Append(label).Append(" <").Append(defaultPattern).Append(">: ");

                PromptStringOptions options = new PromptStringOptions(message.ToString())
                {
                    AllowSpaces = true,
                    UseDefaultValue = false
                };
                // PromptStringOptions không hỗ trợ Keywords - tự xử lý số thứ tự mẫu gần đây bên dưới.

                PromptResult result = ed.GetString(options);
                if (result.Status == PromptStatus.Cancel)
                {
                    return false;
                }

                string raw = result.Status == PromptStatus.None || string.IsNullOrWhiteSpace(result.StringResult)
                    ? defaultPattern
                    : result.StringResult.Trim();

                if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int recentIndex) &&
                    recentIndex >= 1 && recentIndex <= recentFormats.Count)
                {
                    raw = recentFormats[recentIndex - 1];
                }

                if (!TryValidatePattern(raw, out string error))
                {
                    ed.WriteMessage($"\nSLL_CHANGE_SL_BO: {error}");
                    continue;
                }

                pattern = raw;
                return true;
            }
        }

        private static bool TryPromptPositiveInteger(Editor ed, string message, out int value)
        {
            value = 0;

            PromptIntegerOptions options = new PromptIntegerOptions(message)
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = false
            };

            PromptIntegerResult result = ed.GetInteger(options);
            if (result.Status != PromptStatus.OK)
            {
                return false;
            }

            value = result.Value;
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
    }
}
