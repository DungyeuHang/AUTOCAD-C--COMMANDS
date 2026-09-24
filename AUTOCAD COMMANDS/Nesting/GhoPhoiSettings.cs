using System;
using System.Collections.Generic;
using AUTOCAD_COMMANDS.Nesting.Core;
using AUTOCAD_COMMANDS.Nesting.Recognition;

namespace AUTOCAD_COMMANDS.Nesting
{
    public enum GhoPhoiRotationMode
    {
        /// <summary>0 / 90 / 180 / 270.</summary>
        QuarterTurns,

        /// <summary>0 / 180 (keeps the grain / brushing direction).</summary>
        HalfTurns,

        /// <summary>Original orientation only.</summary>
        None
    }

    /// <summary>User settings of GHOPHOI (persisted by <see cref="GhoPhoiSettingsStore"/>).</summary>
    public sealed class GhoPhoiSettings
    {
        // ---- nesting ----
        public double GapMm { get; set; } = 5.0;

        public double EdgeMarginMm { get; set; } = 5.0;

        public bool AllowMirror { get; set; } = false;

        /// <summary>Part-in-part inside CLOSED holes. Concave notches are always allowed.</summary>
        public bool AllowPartInsideHole { get; set; } = false;

        public GhoPhoiRotationMode RotationMode { get; set; } = GhoPhoiRotationMode.QuarterTurns;

        public double TimeBudgetSeconds { get; set; } = 30.0;

        public int Seed { get; set; } = 1;

        public int ExtraSeededOrderings { get; set; } = 3;

        /// <summary>Arc chord tolerance for the computational polygon (mm). Added to every clearance.</summary>
        public double ArcToleranceMm { get; set; } = 0.05;

        // ---- recognition ----
        public double JoinToleranceMm { get; set; } = 0.05;

        public double MaxTextDistanceMm { get; set; } = 300.0;

        public string DefaultMaterial { get; set; } = "1.2MM";

        public int DefaultQuantity { get; set; } = 1;

        /// <summary>Layers whose geometry is carried with a part but never used as contour (bend lines...).</summary>
        public List<string> MarkingLayers { get; set; } = new List<string> { "_mss.dut" };

        // ---- sheets ----
        public string DefaultSheetName { get; set; } = string.Empty;

        /// <summary>Material -> sheet name chosen last time.</summary>
        public Dictionary<string, string> MaterialSheets { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ---- output ----
        public bool OutputAsBlocks { get; set; } = true;

        public bool LabelParts { get; set; } = true;

        public double SheetSpacingMm { get; set; } = 300.0;

        public bool OpenOutputDrawing { get; set; } = true;

        public bool SaveFixture { get; set; } = false;

        public bool AutoZoomInReview { get; set; } = true;

        public GhoPhoiSettings Clone()
        {
            GhoPhoiSettings c = (GhoPhoiSettings)MemberwiseClone();
            c.MarkingLayers = new List<string>(MarkingLayers);
            c.MaterialSheets = new Dictionary<string, string>(MaterialSheets, StringComparer.OrdinalIgnoreCase);
            return c;
        }

        public NestingSettings ToNestingSettings()
        {
            NestingSettings s = new NestingSettings
            {
                GapMm = GapMm,
                EdgeMarginMm = EdgeMarginMm,
                AllowMirror = AllowMirror,
                AllowPartInsideHole = AllowPartInsideHole,
                TimeBudgetSeconds = TimeBudgetSeconds,
                Seed = Seed,
                ExtraSeededOrderings = ExtraSeededOrderings
            };

            switch (RotationMode)
            {
                case GhoPhoiRotationMode.HalfTurns:
                    s.AllowedRotations = new List<double> { 0.0, 180.0 };
                    break;
                case GhoPhoiRotationMode.None:
                    s.AllowedRotations = new List<double> { 0.0 };
                    break;
                default:
                    s.AllowedRotations = new List<double> { 0.0, 90.0, 180.0, 270.0 };
                    break;
            }

            return s;
        }

        public RecognitionSettings ToRecognitionSettings()
        {
            RecognitionSettings r = new RecognitionSettings
            {
                JoinTolerance = JoinToleranceMm,
                MaxTextDistance = MaxTextDistanceMm
            };
            r.Metadata.DefaultMaterial = DefaultMaterial;
            r.Metadata.DefaultQuantity = DefaultQuantity;
            return r;
        }

        public bool IsMarkingLayer(string layer)
        {
            foreach (string l in MarkingLayers)
            {
                if (string.Equals(l, layer, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
