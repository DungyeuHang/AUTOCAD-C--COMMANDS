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

        /// <summary>
        /// Tim DU so luot xep, khong cat bot theo dong ho. MAC DINH BAT.
        ///
        /// Bat: may nhanh hay cham deu chay dung tung ay luot va ra DUNG cung mot ket qua.
        /// Chay lau hon han muc thi cu de chay - muon dung thi bam nut dung.
        ///
        /// Tat: quay ve hanh vi cu - co tran thoi gian, doi lai ket qua co the khac nhau giua
        /// cac may (da do duoc chenh 2% tren ban ve that).
        /// </summary>
        public bool DeterministicSearch { get; set; } = true;

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
        /// Ghi ma P + so thu tu vao GIUA moi chi tiet tren to ("P01", "P02", ...).
        ///
        /// Nhan nay nam tren layer KHONG IN va KHONG CAT, chi de doi chieu khi ra xuong:
        /// "chi tiet P07 la cai nao tren to nay".
        ///
        /// Truoc day nhan nay ghep SO THU TU voi CHU CUA NGUOI DUNG ("P11 D-D-347-566-1") va
        /// phong to dat vao giua chi tiet, nen nhin tren ban ve cu tuong chuong trinh da sua
        /// chu cua ho roi mang ra giua - vi vay no bi tat di. Gio nhan chi con DUNG ma P + so
        /// thu tu, khong dinh gi den chu cua nguoi dung nua, nen bat lai duoc.
        ///
        /// Chi tiet nao DA CO chu cat cua chinh nguoi dung thi khong ve nhan - de khong thanh
        /// hai dong chu chong len nhau.
        ///
        /// Muon co chu THAT tren chi tiet thi dung <see cref="EngravingLayers"/>: chu cua chinh
        /// nguoi dung, nguyen van, dung co, dung vi tri.
        /// </summary>
        public bool LabelParts { get; set; } = true;

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
                DeterministicSearch = DeterministicSearch,
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
