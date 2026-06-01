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

    internal sealed class DungXRibbonCommandHandler : System.Windows.Input.ICommand
    {
        private readonly PaletteCommandItem _item;

        public DungXRibbonCommandHandler(PaletteCommandItem item)
        {
            _item = item;
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            PaletteCommandItem item = _item;

            if (item == null && parameter is PaletteCommandItem directItem)
            {
                item = directItem;
            }

            if (item == null && parameter is RibbonButton ribbonButton)
            {
                item = ribbonButton.CommandParameter as PaletteCommandItem
                    ?? ribbonButton.Tag as PaletteCommandItem;
            }

            if (item != null)
            {
                DungXPaletteHost.RunCommand(item);
            }
        }
    }
}
