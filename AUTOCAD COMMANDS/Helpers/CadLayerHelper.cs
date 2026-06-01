using Autodesk.AutoCAD.DatabaseServices;

namespace AUTOCAD_COMMANDS
{
    internal static class CadLayerHelper
    {
        public static ObjectId EnsureLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable layerTable =
                tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (layerTable == null)
            {
                return ObjectId.Null;
            }

            if (layerTable.Has(layerName))
            {
                return layerTable[layerName];
            }

            layerTable.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord
            {
                Name = layerName
            };

            ObjectId layerId = layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layerId;
        }
    }
}
