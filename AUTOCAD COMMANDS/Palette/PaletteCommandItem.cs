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

    internal sealed class PaletteCommandItem
    {
        public PaletteCommandItem(
            string commandName,
            string description,
            string sourceLabel,
            PaletteSourceKind sourceKind,
            string sourcePath)
        {
            CommandName = commandName;
            Description = description ?? string.Empty;
            SourceLabel = sourceLabel;
            SourceKind = sourceKind;
            SourcePath = sourcePath ?? string.Empty;
        }

        public string CommandName { get; }

        public string Description { get; set; }

        public string SourceLabel { get; }

        public PaletteSourceKind SourceKind { get; }

        public string SourcePath { get; }

        public bool IsFavorite { get; set; }

        public int ManualOrder { get; set; }

        public int UsageCount { get; set; }
    }
}
