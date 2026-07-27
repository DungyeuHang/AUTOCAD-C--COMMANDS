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

    internal sealed class SdxyTargetSettings
    {
        public SdxyTargetSettings()
        {
            AllowedTypeNames = new HashSet<string>(StringComparer.Ordinal);
            AllowedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SampleDescriptors = new List<SdxySampleDescriptor>();
        }

        public HashSet<string> AllowedTypeNames { get; }

        public HashSet<string> AllowedLayers { get; }

        public bool UseSampleType { get; set; }

        public bool UseSampleLayer { get; set; }

        public bool UseSampleLinetype { get; set; }

        public bool UseSampleColor { get; set; }

        public bool UseSampleBlockName { get; set; }

        public List<SdxySampleDescriptor> SampleDescriptors { get; }

        public SdxySampleDescriptor SampleDescriptor
        {
            get
            {
                return SampleDescriptors.Count == 0 ? null : SampleDescriptors[0];
            }
            set
            {
                SampleDescriptors.Clear();
                if (value != null)
                {
                    SampleDescriptors.Add(value.Clone());
                }
            }
        }

        public SdxyTargetSettings Clone()
        {
            SdxyTargetSettings clone = new SdxyTargetSettings
            {
                UseSampleType = UseSampleType,
                UseSampleLayer = UseSampleLayer,
                UseSampleLinetype = UseSampleLinetype,
                UseSampleColor = UseSampleColor,
                UseSampleBlockName = UseSampleBlockName
            };

            foreach (string typeName in AllowedTypeNames)
            {
                clone.AllowedTypeNames.Add(typeName);
            }

            foreach (string layerName in AllowedLayers)
            {
                clone.AllowedLayers.Add(layerName);
            }

            foreach (SdxySampleDescriptor sample in SampleDescriptors)
            {
                if (sample != null)
                {
                    clone.SampleDescriptors.Add(sample.Clone());
                }
            }

            return clone;
        }

        public static SdxyTargetSettings LoadFromStore()
        {
            return SdxyTargetSettingsStore.Load();
        }

        public void SaveToStore()
        {
            SdxyTargetSettingsStore.Save(this);
        }
    }
}
