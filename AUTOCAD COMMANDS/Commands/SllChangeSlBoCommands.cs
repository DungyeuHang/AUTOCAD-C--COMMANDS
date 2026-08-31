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
    // Mục đích: đổi số lượng "SL: X" trong TEXT/MTEXT theo tỉ lệ số bộ gốc -> số bộ mới.
    // Lưu ý: chỉ đổi số ngay sau "SL:", giữ nguyên toàn bộ nội dung còn lại.
    // Mỗi đối tượng được tính lại từ giá trị SL gốc của chính nó (không cascading).
    // ======================================================
    public class SllChangeSlBoCommands
    {
        private static readonly Regex SlPattern = new Regex(@"SL:(\s*)(\d+)", RegexOptions.Compiled);

        // Flow:
        // 1. Hỏi số bộ gốc, số bộ mới.
        // 2. Quét chọn đối tượng (chỉ xử lý DBText/MText trong vùng chọn).
        // 3. Với mỗi text, tìm "SL: X", tính lại theo tỉ lệ rồi ghi trực tiếp vào entity.
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

                    if (!TryComputeNewText(originalText, originalBundles, newBundles, out string updatedText, out string error))
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
                    $"\nSLL_CHANGE_SL_BO: đã đổi {changedCount} text (bỏ qua {skippedNoMatchCount} không có \"SL:\", {errorCount} lỗi SL không chia hết cho số bộ gốc).");
            }
        }

        // Tìm mọi "SL: X" trong text và tính lại theo tỉ lệ originalBundles -> newBundles.
        // Trả về false + error khác null nếu có SL không chia hết cho số bộ gốc (không sửa gì cả).
        // Trả về false + error null nếu text không chứa "SL: X" nào (bỏ qua, không phải lỗi).
        private static bool TryComputeNewText(
            string originalText,
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

            if (!SlPattern.IsMatch(originalText))
            {
                return false;
            }

            string localError = null;

            string result = SlPattern.Replace(originalText, match =>
            {
                if (localError != null)
                {
                    return match.Value;
                }

                string whitespace = match.Groups[1].Value;
                string digits = match.Groups[2].Value;

                if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long originalSl))
                {
                    localError = $"không đọc được số lượng \"{match.Value}\".";
                    return match.Value;
                }

                if (originalSl % originalBundles != 0)
                {
                    localError =
                        $"SL: {originalSl} không chia hết cho số bộ gốc {originalBundles}.";
                    return match.Value;
                }

                long newSl = originalSl / originalBundles * newBundles;
                return "SL:" + whitespace + newSl.ToString(CultureInfo.InvariantCulture);
            });

            if (localError != null)
            {
                error = localError;
                return false;
            }

            updatedText = result;
            return true;
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
    }
}
