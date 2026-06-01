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

    internal sealed class SdxySampleDescriptor
    {
        public SdxySampleDescriptor(
            string typeName,
            string typeDisplayName,
            string layerName,
            string linetypeName,
            string colorKey,
            string colorDisplayName,
            string blockName)
        {
            TypeName = typeName ?? string.Empty;
            TypeDisplayName = typeDisplayName ?? string.Empty;
            LayerName = layerName ?? string.Empty;
            LinetypeName = linetypeName ?? string.Empty;
            ColorKey = colorKey ?? string.Empty;
            ColorDisplayName = colorDisplayName ?? string.Empty;
            BlockName = blockName ?? string.Empty;
        }

        public string TypeName { get; }

        public string TypeDisplayName { get; }

        public string LayerName { get; }

        public string LinetypeName { get; }

        public string ColorKey { get; }

        public string ColorDisplayName { get; }

        public string BlockName { get; }

        public SdxySampleDescriptor Clone()
        {
            return new SdxySampleDescriptor(
                TypeName,
                TypeDisplayName,
                LayerName,
                LinetypeName,
                ColorKey,
                ColorDisplayName,
                BlockName);
        }

        public string BuildSummary()
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TypeDisplayName))
            {
                parts.Add("Type: " + TypeDisplayName);
            }

            if (!string.IsNullOrWhiteSpace(LayerName))
            {
                parts.Add("Layer: " + LayerName);
            }

            if (!string.IsNullOrWhiteSpace(LinetypeName))
            {
                parts.Add("Linetype: " + LinetypeName);
            }

            if (!string.IsNullOrWhiteSpace(ColorDisplayName))
            {
                parts.Add("Color: " + ColorDisplayName);
            }

            if (!string.IsNullOrWhiteSpace(BlockName))
            {
                parts.Add("Block: " + BlockName);
            }

            return parts.Count == 0
                ? "Chua co sample object."
                : string.Join(" | ", parts);
        }
    }
}
