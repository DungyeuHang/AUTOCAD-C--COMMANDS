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

    internal sealed class SmartStretchSelectionInput
    {
        private SmartStretchSelectionInput()
        {
        }

        public List<SmartStretchWindowSelection> Windows { get; private set; }

        public ObjectId[] SelectedObjectIds { get; private set; }

        public Dictionary<ObjectId, List<SmartStretchWindowSelection>> EffectiveWindowsByObject
        {
            get;
            private set;
        }

        public static SmartStretchSelectionInput CreateSelection(
            IEnumerable<SmartStretchWindowSelection> windows,
            IEnumerable<ObjectId> selectedObjectIds,
            IDictionary<ObjectId, List<SmartStretchWindowSelection>> effectiveWindowsByObject)
        {
            return new SmartStretchSelectionInput
            {
                Windows = windows?.ToList() ?? new List<SmartStretchWindowSelection>(),
                SelectedObjectIds = selectedObjectIds?.ToArray() ?? new ObjectId[0],
                EffectiveWindowsByObject =
                    effectiveWindowsByObject?.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value?.ToList() ?? new List<SmartStretchWindowSelection>())
                    ?? new Dictionary<ObjectId, List<SmartStretchWindowSelection>>()
            };
        }

        public IEnumerable<SmartStretchWindowSelection> GetEffectiveWindowsForObject(
            ObjectId objectId)
        {
            if (EffectiveWindowsByObject != null &&
                EffectiveWindowsByObject.TryGetValue(
                    objectId,
                    out List<SmartStretchWindowSelection> windows))
            {
                return windows;
            }

            return Enumerable.Empty<SmartStretchWindowSelection>();
        }
    }
}
