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

        /// <summary>
        /// Layers whose TEXT / MTEXT is ENGRAVING on the part: carried into the output block and
        /// transformed with it, never read as SL / material and never used for nesting.
        ///
        /// Deliberately a separate layer from the contour layer: on a real drawing the contour
        /// layer carries both information ("SL: 2", "1.2MM") and marking ("H", part codes), and
        /// nothing in the text itself tells the two apart. The layer is the operator's explicit
        /// statement of intent - no guessing.
        /// </summary>
        public List<string> EngravingLayers { get; set; } = new List<string> { "_mss.khac" };

        // ---- sheets ----
        public string DefaultSheetName { get; set; } = string.Empty;

        /// <summary>Material -> sheet name chosen last time.</summary>
        public Dictionary<string, string> MaterialSheets { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ---- output ----
        public bool OutputAsBlocks { get; set; } = true;

        /// <summary>
        /// Ve them mot nhan ten chi tiet vao GIUA moi chi tiet tren to.
        ///
        /// MAC DINH TAT. Nhan nay la chu do GHOPHOI tu dat ra ("P11 D-D-347-566-1" = so thu tu
        /// cua chuong trinh GHEP voi chu cua nguoi dung), dat o TAM chi tiet voi co chu tu tinh
        /// (den 15 mm). Tren ban ve san xuat that, nguoi dung nhin thay no va tuong chuong trinh
        /// da SUA chu cua ho roi phong to mang ra giua - trong khi chu goc van nam yen cho cu.
        /// Khong ai yeu cau cai nhan nay, nen khong bat san.
        ///
        /// Muon co chu tren chi tiet thi dung <see cref="EngravingLayers"/>: chu cua chinh nguoi
        /// dung, nguyen van, dung co, dung vi tri.
        /// </summary>
        public bool LabelParts { get; set; } = false;

        public double SheetSpacingMm { get; set; } = 300.0;

        public bool OpenOutputDrawing { get; set; } = true;

        /// <summary>
        /// Ve ket qua THANG vao ban ve dang mo tai mot diem nguoi dung chon, thay vi tao mot
        /// file DWG moi.
        ///
        /// Luu y an toan: bat tuy chon nay thi GHOPHOI khong con la lenh CHI DOC nua. No van
        /// chi THEM entity moi va khong dong vao hinh goc, va Ctrl+Z hoan tac duoc - nhung day
        /// la mot thay doi that ve hanh vi, nen de thanh mot o tick rieng chu khong am tham.
        /// </summary>
        public bool OutputToCurrentDrawing { get; set; } = true;

        public bool SaveFixture { get; set; } = false;

        public bool AutoZoomInReview { get; set; } = true;

        public GhoPhoiSettings Clone()
        {
            GhoPhoiSettings c = (GhoPhoiSettings)MemberwiseClone();
            c.MarkingLayers = new List<string>(MarkingLayers);
            c.EngravingLayers = new List<string>(EngravingLayers);
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

        public bool IsEngravingLayer(string layer)
        {
            foreach (string l in EngravingLayers)
            {
                if (string.Equals(l, layer, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
