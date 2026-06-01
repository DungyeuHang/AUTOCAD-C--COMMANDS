using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AUTOCAD_COMMANDS
{
    internal static class CadMTextHelper
    {
        public static void AddMText(
            BlockTableRecord owner,
            Transaction tr,
            ObjectId layerId,
            Point3d location,
            double width,
            string contents,
            double textHeight)
        {
            AddMText(
                owner,
                tr,
                layerId,
                location,
                width,
                contents,
                AttachmentPoint.TopLeft,
                textHeight);
        }

        public static void AddMText(
            BlockTableRecord owner,
            Transaction tr,
            ObjectId layerId,
            Point3d location,
            double width,
            string contents,
            AttachmentPoint attachment,
            double textHeight)
        {
            MText text = new MText
            {
                Location = location,
                Width = width,
                TextHeight = textHeight,
                Attachment = attachment,
                Contents = ToMTextContents(contents),
                LayerId = layerId
            };

            owner.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static string ToMTextContents(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("\n", "\\P");
        }
    }
}
