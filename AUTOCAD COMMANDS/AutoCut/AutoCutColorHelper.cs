using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using System;

namespace AUTOCAD_COMMANDS
{
    internal static class AutoCutColorHelper
    {
        public static Color GetEffectiveColor(Entity entity, Transaction tr, Database db)
        {
            if (entity == null)
            {
                return Color.FromColorIndex(ColorMethod.ByAci, 7);
            }

            Color col = entity.Color;
            if (col.IsByLayer)
            {
                try
                {
                    ObjectId layerId = entity.LayerId;
                    if (layerId.IsValid)
                    {
                        LayerTableRecord layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
                        if (layer != null)
                        {
                            return layer.Color;
                        }
                    }
                }
                catch
                {
                    // fallback
                }
            }
            else if (col.IsByBlock)
            {
                return Color.FromColorIndex(ColorMethod.ByAci, 7);
            }

            return col;
        }

        public static bool MatchesFilter(Entity cutter, Entity target, AutoCutSettings settings, Transaction tr, Database db)
        {
            if (cutter == null || target == null || settings == null)
            {
                return false;
            }

            switch (settings.ColorFilterMode)
            {
                case AutoCutColorFilterMode.AnyColor:
                    return true;

                case AutoCutColorFilterMode.ByLayer:
                    return cutter.Color.IsByLayer;

                case AutoCutColorFilterMode.SpecificColor:
                    Color effC = GetEffectiveColor(cutter, tr, db);
                    return effC.ColorIndex == settings.SpecificColorIndex;

                case AutoCutColorFilterMode.SameAsTarget:
                    Color cCol = cutter.Color;
                    Color tCol = target.Color;

                    if (tCol.IsByLayer)
                    {
                        return cCol.IsByLayer && string.Equals(cutter.Layer, target.Layer, StringComparison.OrdinalIgnoreCase);
                    }
                    if (tCol.IsByAci)
                    {
                        return cCol.IsByAci && cCol.ColorIndex == tCol.ColorIndex;
                    }
                    if (tCol.IsByColor)
                    {
                        return cCol.IsByColor && cCol.ColorValue.ToArgb() == tCol.ColorValue.ToArgb();
                    }
                    if (tCol.IsByBlock)
                    {
                        return cCol.IsByBlock;
                    }
                    return cCol == tCol;

                case AutoCutColorFilterMode.SameEffectiveColor:
                    Color effCutter = GetEffectiveColor(cutter, tr, db);
                    Color effTarget = GetEffectiveColor(target, tr, db);

                    if (effCutter.IsByAci && effTarget.IsByAci)
                    {
                        return effCutter.ColorIndex == effTarget.ColorIndex;
                    }

                    // Compare RGB values
                    return effCutter.ColorValue.R == effTarget.ColorValue.R &&
                           effCutter.ColorValue.G == effTarget.ColorValue.G &&
                           effCutter.ColorValue.B == effTarget.ColorValue.B;

                default:
                    return true;
            }
        }

        public static string FormatColorDisplay(Color color)
        {
            if (color == null) return "Unknown";
            if (color.IsByLayer) return "ByLayer";
            if (color.IsByBlock) return "ByBlock";
            if (color.IsByColor)
            {
                var cv = color.ColorValue;
                return $"RGB({cv.R},{cv.G},{cv.B})";
            }
            return $"ACI {color.ColorIndex}";
        }
    }
}

