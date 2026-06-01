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
    // ENTRY POINT CỦA PLUGIN
    // Initialize/Terminate được AutoCAD gọi khi NETLOAD hoặc bundle autoload.
    // Đây cũng là nơi khởi tạo tracker DXPALETTE và Ribbon.
    // ======================================================
    public class DungXPaletteEntry : IExtensionApplication
    {
        [CommandMethod("DXPALETTE")]
        public void ShowPalette()
        {
            DungXPaletteHost.ShowPalette();
        }

        [CommandMethod("DXPALETTERELOAD")]
        public void ReloadPalette()
        {
            DungXPaletteHost.ReloadPaletteData(true);
        }

        [CommandMethod("DXPALETTESETFOLDER")]
        public void SetLispFolder()
        {
            DungXPaletteHost.ChooseLispFolder(true);
        }

        [CommandMethod("DXRIBBON")]
        public void ShowRibbon()
        {
            DungXRibbonHost.ShowRibbon();
        }

        [CommandMethod("DXRIBBONRELOAD")]
        public void ReloadRibbon()
        {
            DungXRibbonHost.ReloadRibbon(true);
        }

        public void Initialize()
        {
            DungXPaletteHost.Initialize();
            DungXRibbonHost.Initialize();
        }

        public void Terminate()
        {
            DungXPaletteHost.Terminate();
            DungXRibbonHost.Terminate();
        }
    }
}
