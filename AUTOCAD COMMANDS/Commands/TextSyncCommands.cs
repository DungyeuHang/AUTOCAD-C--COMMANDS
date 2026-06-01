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
    // TT_TEXT_CHANGE_5
    // Mục đích: lấy nội dung text gốc và thay nội dung cho các text height = 5 trong vùng chọn.
    // Lưu ý: chỉ đổi nội dung, không đổi layer/style/height/rotation.
    // Có hỗ trợ PickFirst để dùng FILTER trước rồi gọi lệnh.
    // ======================================================
    public class TextSyncCommands
    {
        private const double TargetTextHeight = 5.0;
        private const double TextHeightTolerance = 1e-6;

        // Flow:
        // 1. Chọn text gốc.
        // 2. Quét vùng text đích hoặc dùng PickFirst.
        // 3. Lọc DBText/MText có height = 5 rồi thay nội dung.
        [CommandMethod("TT_TEXT_CHANGE_5", CommandFlags.UsePickSet)]
        public void SyncTextHeightFiveContent()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            ObjectId[] targetIds = TryConsumePickFirst(ed);

            if (targetIds != null && targetIds.Length > 0)
            {
                ed.WriteMessage(
                    $"\nTT_TEXT_CHANGE_5: dùng {targetIds.Length} đối tượng PickFirst đã chọn sẵn.");
            }

            PromptEntityOptions sourceOptions =
                new PromptEntityOptions("\nChọn text gốc: ");
            sourceOptions.SetRejectMessage("\nChỉ hỗ trợ DBText hoặc MText.");
            sourceOptions.AddAllowedClass(typeof(DBText), true);
            sourceOptions.AddAllowedClass(typeof(MText), true);

            PromptEntityResult sourceResult = ed.GetEntity(sourceOptions);
            if (sourceResult.Status != PromptStatus.OK)
            {
                return;
            }

            object previousSelectionOffscreen = null;

            try
            {
                previousSelectionOffscreen = Application.GetSystemVariable("SELECTIONOFFSCREEN");
                Application.SetSystemVariable("SELECTIONOFFSCREEN", 2);

                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nQuét chọn vùng có text cần đổi nội dung: "
                };

                if (targetIds == null || targetIds.Length == 0)
                {
                    PromptSelectionResult selectionResult =
                        PromptForSelection(ed, selectionOptions.MessageForAdding);
                    if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null)
                    {
                        return;
                    }

                    targetIds = selectionResult.Value.GetObjectIds();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity sourceEntity =
                        tr.GetObject(sourceResult.ObjectId, OpenMode.ForRead) as Entity;
                    if (sourceEntity == null)
                    {
                        return;
                    }

                    TextSyncPayload payload = GetTextSyncPayload(sourceEntity);
                    if (!payload.IsValid)
                    {
                        ed.WriteMessage("\nKhông đọc được nội dung text gốc.");
                        return;
                    }

                    int replacedCount = 0;
                    int matchedCount = 0;

                    foreach (ObjectId objectId in targetIds)
                    {
                        if (objectId.IsNull)
                        {
                            continue;
                        }

                        Entity entity =
                            tr.GetObject(objectId, OpenMode.ForRead) as Entity;
                        if (entity == null)
                        {
                            continue;
                        }

                        if (!TryGetTextHeight(entity, out double textHeight) ||
                            Math.Abs(textHeight - TargetTextHeight) > TextHeightTolerance)
                        {
                            continue;
                        }

                        matchedCount++;

                        if (objectId == sourceResult.ObjectId)
                        {
                            continue;
                        }

                        entity.UpgradeOpen();

                        if (entity is DBText dbText)
                        {
                            if (!string.Equals(dbText.TextString, payload.PlainText, StringComparison.Ordinal))
                            {
                                dbText.TextString = payload.PlainText;
                                replacedCount++;
                            }
                        }
                        else if (entity is MText mText)
                        {
                            string desiredContent = payload.MTextContents ?? payload.PlainText;
                            if (!string.Equals(mText.Contents, desiredContent, StringComparison.Ordinal))
                            {
                                mText.Contents = desiredContent;
                                replacedCount++;
                            }
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nTT_TEXT_CHANGE_5: đã đổi nội dung {replacedCount} text (lọc được {matchedCount} text có height = {TargetTextHeight.ToString("0.###", CultureInfo.InvariantCulture)}).");
                }
            }
            finally
            {
                if (previousSelectionOffscreen != null)
                {
                    Application.SetSystemVariable("SELECTIONOFFSCREEN", previousSelectionOffscreen);
                }
            }
        }

        private static ObjectId[] TryConsumePickFirst(Editor ed)
        {
            PromptSelectionResult impliedResult = ed.SelectImplied();
            if (impliedResult.Status != PromptStatus.OK || impliedResult.Value == null)
            {
                return null;
            }

            ObjectId[] objectIds = impliedResult.Value.GetObjectIds();
            if (objectIds == null || objectIds.Length == 0)
            {
                return null;
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            return objectIds;
        }

        private static PromptSelectionResult PromptForSelection(Editor ed, string message)
        {
            while (true)
            {
                PromptSelectionOptions options = new PromptSelectionOptions
                {
                    MessageForAdding = message
                };

                PromptSelectionResult result = ed.GetSelection(options);
                if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
                {
                    return result;
                }

                if (result.Status == PromptStatus.Cancel)
                {
                    return result;
                }

                ed.WriteMessage("\nChưa chọn được đối tượng hợp lệ, hãy chọn lại.");
            }
        }

        private static TextSyncPayload GetTextSyncPayload(Entity entity)
        {
            // DBText dùng TextString, MText dùng Contents.
            // Payload giữ cả plain text và MText content để hạn chế mất format MText.
            if (entity is DBText dbText)
            {
                return new TextSyncPayload(dbText.TextString, dbText.TextString);
            }

            if (entity is MText mText)
            {
                return new TextSyncPayload(mText.Text, mText.Contents);
            }

            return TextSyncPayload.Invalid;
        }

        private static bool TryGetTextHeight(Entity entity, out double textHeight)
        {
            // Chỉ xử lý text chọn trực tiếp.
            // Text nằm trong block không được bóc ra ở lệnh này.
            if (entity is DBText dbText)
            {
                textHeight = dbText.Height;
                return true;
            }

            if (entity is MText mText)
            {
                textHeight = mText.TextHeight;
                return true;
            }

            textHeight = 0.0;
            return false;
        }

        private readonly struct TextSyncPayload
        {
            public static readonly TextSyncPayload Invalid =
                new TextSyncPayload(string.Empty, null);

            public TextSyncPayload(string plainText, string mTextContents)
            {
                PlainText = plainText ?? string.Empty;
                MTextContents = mTextContents;
            }

            public string PlainText { get; }

            public string MTextContents { get; }

            public bool IsValid => !string.IsNullOrEmpty(PlainText) || MTextContents != null;
        }
    }
}
