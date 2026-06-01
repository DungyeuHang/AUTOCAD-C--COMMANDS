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
    // UI CHỌN BLOCK CHO BBB
    // Form nhỏ để lọc/chọn block definition trong bản vẽ hiện tại.
    // ======================================================
    internal sealed class BlockDefinitionChoice
    {
        public BlockDefinitionChoice(ObjectId id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }

        public ObjectId Id { get; }

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }
    }
}
