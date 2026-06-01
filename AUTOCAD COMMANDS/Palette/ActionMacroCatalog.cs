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

    // Quét Action Macro của AutoCAD để đưa vào DXPALETTE.
    internal static class ActionMacroCatalog
    {
        public static IEnumerable<PaletteCommandItem> BuildItems(
            Dictionary<string, string> savedDescriptions)
        {
            string actionFolder = GetDefaultActionsFolder();
            if (string.IsNullOrWhiteSpace(actionFolder) || !Directory.Exists(actionFolder))
            {
                return Enumerable.Empty<PaletteCommandItem>();
            }

            List<PaletteCommandItem> items = new List<PaletteCommandItem>();
            foreach (string filePath in Directory.GetFiles(actionFolder, "*.actm"))
            {
                string commandName = Path.GetFileNameWithoutExtension(filePath);
                string description = savedDescriptions.TryGetValue(commandName, out string saved)
                    ? saved
                    : "Action Recorder macro";

                items.Add(new PaletteCommandItem(
                    commandName,
                    description,
                    "Action Macro",
                    PaletteSourceKind.ActionMacro,
                    filePath));
            }

            return items;
        }

        private static string GetDefaultActionsFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(
                appData,
                "Autodesk",
                "AutoCAD 2022",
                "R24.1",
                "enu",
                "Support",
                "Actions");
        }
    }
}
