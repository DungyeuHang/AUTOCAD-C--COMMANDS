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
    // IPP / IPS - INSERT PHAO PG / PGS
    // - Chọn 2 DIM để lấy RỘNG và CAO.
    // - Nhập số ôm tường để suy ra tên block: PG-xx hoặc PGS-xx.
    // - Chèn block và cập nhật dynamic properties CAO / RONG.
    // ======================================================
    public class InsertPhaoCommands
    {
        private const double InsertPhaoTolerance = 1e-6;
        private const int DefaultWrapOnWall = 60;

        [CommandMethod("IPP_INSERT_PG")]
        public void InsertPg()
        {
            RunInsertPhaoCommand("PG", "IPP_INSERT_PG");
        }

        [CommandMethod("IPS_INSERT_PGS")]
        public void InsertPgs()
        {
            RunInsertPhaoCommand("PGS", "IPS_INSERT_PGS");
        }

        private static void RunInsertPhaoCommand(string blockPrefix, string commandLabel)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc?.Editor;
            Database db = doc?.Database;

            if (doc == null || ed == null || db == null)
            {
                return;
            }

            if (!TryPromptWrapOnWall(ed, out int wrapOnWall))
            {
                return;
            }

            if (!TryPromptDimensionMeasurement(
                ed,
                db,
                "\nChọn DIM chiều RỘNG: ",
                out double valueWidth))
            {
                return;
            }

            if (!TryPromptDimensionMeasurement(
                ed,
                db,
                "\nChọn DIM chiều CAO: ",
                out double valueHeight))
            {
                return;
            }

            string blockName = blockPrefix + "-" + wrapOnWall.ToString(CultureInfo.InvariantCulture);
            if (!TryPromptInsertionPoint(ed, out Point3d insertionPoint))
            {
                return;
            }

            if (!TryGetOrLoadBlockDefinition(db, blockName, out ObjectId blockDefinitionId, out string errorMessage))
            {
                ed.WriteMessage($"\n{commandLabel}: {errorMessage}");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord currentSpace =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                if (currentSpace == null)
                {
                    ed.WriteMessage($"\n{commandLabel}: không mở được không gian hiện tại để chèn block.");
                    return;
                }

                BlockReference blockReference =
                    new BlockReference(insertionPoint, blockDefinitionId)
                    {
                        ScaleFactors = new Scale3d(1.0),
                        Rotation = 0.0
                    };

                currentSpace.AppendEntity(blockReference);
                tr.AddNewlyCreatedDBObject(blockReference, true);

                AppendBlockAttributes(blockReference, tr);
                UpdateDynamicProperties(blockReference, valueWidth, valueHeight);
                blockReference.RecordGraphicsModified(true);

                tr.Commit();
            }

            ed.WriteMessage(
                $"\n{commandLabel}: đã chèn block {blockName} và cập nhật tham số động.");
        }

        private static bool TryPromptWrapOnWall(Editor ed, out int wrapOnWall)
        {
            PromptIntegerOptions options =
                new PromptIntegerOptions(
                    $"\nNhập phào ôm tường <{DefaultWrapOnWall.ToString(CultureInfo.InvariantCulture)}>: ");
            options.AllowNegative = false;
            options.AllowZero = false;
            options.AllowNone = true;
            options.DefaultValue = DefaultWrapOnWall;
            options.UseDefaultValue = true;

            PromptIntegerResult result = ed.GetInteger(options);
            if (result.Status == PromptStatus.Cancel)
            {
                wrapOnWall = DefaultWrapOnWall;
                return false;
            }

            wrapOnWall = result.Status == PromptStatus.None
                ? DefaultWrapOnWall
                : result.Value;
            return true;
        }

        private static bool TryPromptDimensionMeasurement(
            Editor ed,
            Database db,
            string message,
            out double measurement)
        {
            measurement = 0.0;

            while (true)
            {
                PromptEntityOptions options = new PromptEntityOptions(message);
                options.SetRejectMessage("\nHãy chọn đúng đối tượng DIMENSION.");
                options.AddAllowedClass(typeof(Dimension), false);

                PromptEntityResult result = ed.GetEntity(options);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Dimension dimension =
                        tr.GetObject(result.ObjectId, OpenMode.ForRead) as Dimension;
                    if (dimension == null)
                    {
                        ed.WriteMessage("\nKhông đọc được DIM. Hãy chọn lại.");
                        continue;
                    }

                    measurement = Math.Abs(dimension.Measurement);
                    if (measurement <= InsertPhaoTolerance)
                    {
                        ed.WriteMessage("\nDIM có measurement không hợp lệ. Hãy chọn lại.");
                        continue;
                    }

                    return true;
                }
            }
        }

        private static bool TryPromptInsertionPoint(Editor ed, out Point3d insertionPoint)
        {
            PromptPointOptions options =
                new PromptPointOptions("\nChọn điểm chèn block: ");
            options.AllowNone = true;

            PromptPointResult result = ed.GetPoint(options);
            if (result.Status == PromptStatus.OK)
            {
                insertionPoint = result.Value;
                return true;
            }

            if (result.Status == PromptStatus.None)
            {
                object lastPoint = Application.GetSystemVariable("LASTPOINT");
                if (lastPoint is Point3d point)
                {
                    insertionPoint = point;
                    return true;
                }
            }

            insertionPoint = Point3d.Origin;
            return false;
        }

        private static bool TryGetOrLoadBlockDefinition(
            Database db,
            string blockName,
            out ObjectId blockDefinitionId,
            out string errorMessage)
        {
            blockDefinitionId = ObjectId.Null;
            errorMessage = string.Empty;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable =
                    tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                if (blockTable != null && blockTable.Has(blockName))
                {
                    blockDefinitionId = blockTable[blockName];
                    return true;
                }
            }

            string blockFilePath;
            try
            {
                blockFilePath = HostApplicationServices.Current.FindFile(
                    blockName + ".dwg",
                    db,
                    FindFileHint.Default);
            }
            catch
            {
                errorMessage =
                    $"không tìm thấy block '{blockName}' trong bản vẽ hoặc Support File Search Path.";
                return false;
            }

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(
                        blockFilePath,
                        FileOpenMode.OpenForReadAndAllShare,
                        false,
                        null);
                    blockDefinitionId = db.Insert(blockName, sourceDb, false);
                }
            }
            catch (System.Exception ex)
            {
                errorMessage = $"không nạp được block '{blockName}': {ex.Message}";
                return false;
            }

            return !blockDefinitionId.IsNull;
        }

        private static void UpdateDynamicProperties(
            BlockReference blockReference,
            double valueWidth,
            double valueHeight)
        {
            if (blockReference == null || !blockReference.IsDynamicBlock)
            {
                return;
            }

            foreach (DynamicBlockReferenceProperty property in blockReference.DynamicBlockReferencePropertyCollection)
            {
                if (property == null || property.ReadOnly)
                {
                    continue;
                }

                string propertyName = property.PropertyName ?? string.Empty;
                if (propertyName.Equals("CAO", StringComparison.OrdinalIgnoreCase))
                {
                    property.Value = valueHeight;
                }
                else if (propertyName.Equals("RONG", StringComparison.OrdinalIgnoreCase))
                {
                    property.Value = valueWidth;
                }
            }
        }

        private static void AppendBlockAttributes(BlockReference blockReference, Transaction tr)
        {
            BlockTableRecord definition =
                tr.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (definition == null || !definition.HasAttributeDefinitions)
            {
                return;
            }

            foreach (ObjectId entityId in definition)
            {
                AttributeDefinition attributeDefinition =
                    tr.GetObject(entityId, OpenMode.ForRead) as AttributeDefinition;
                if (attributeDefinition == null || attributeDefinition.Constant)
                {
                    continue;
                }

                AttributeReference attributeReference = new AttributeReference();
                attributeReference.SetAttributeFromBlock(
                    attributeDefinition,
                    blockReference.BlockTransform);
                attributeReference.Position =
                    attributeDefinition.Position.TransformBy(blockReference.BlockTransform);

                if (attributeReference.IsMTextAttribute)
                {
                    attributeReference.UpdateMTextAttribute();
                }

                blockReference.AttributeCollection.AppendAttribute(attributeReference);
                tr.AddNewlyCreatedDBObject(attributeReference, true);
            }
        }
    }
}
