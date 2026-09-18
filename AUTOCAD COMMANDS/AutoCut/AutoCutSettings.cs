using System;

namespace AUTOCAD_COMMANDS
{
    [Flags]
    public enum AutoCutTargetTypeFlags
    {
        None = 0,
        Line = 1,
        Polyline = 2,
        Arc = 4,
        Circle = 8,
        Ellipse = 16,
        Spline = 32,
        AllCurves = Line | Polyline | Arc | Circle | Ellipse | Spline
    }

    public enum AutoCutTargetType
    {
        Line = 0,
        Polyline = 1,
        LineAndPolyline = 2,
        AllCurves = 3
    }

    [Flags]
    public enum AutoCutCutterTypeFlags
    {
        None = 0,
        Line = 1,
        Polyline = 2,
        Arc = 4,
        Circle = 8,
        Ellipse = 16,
        Spline = 32,
        All = Line | Polyline | Arc | Circle | Ellipse | Spline
    }

    public enum AutoCutTargetLayerFilterMode
    {
        AllLayers = 0,
        CurrentLayer = 1,
        SpecificLayer = 2
    }

    public enum AutoCutLayerFilterMode
    {
        SameAsTarget = 0,
        CurrentLayer = 1,
        AnyLayer = 2,
        CustomLayer = 3
    }

    public enum AutoCutColorFilterMode
    {
        SameAsTarget = 0,
        SameEffectiveColor = 1,
        ByLayer = 2,
        SpecificColor = 3,
        AnyColor = 4
    }

    public enum AutoCutCutMode
    {
        BetweenIntersections = 0,
        ClosedCutterRemoveInside = 1,
        ClosedCutterRemoveOutside = 2,
        RemoveShortest = 3,
        RemoveLongest = 4,
        SingleIntersectionRule = 5
    }

    public enum AutoCutSingleIntersectionRule
    {
        Skip = 0,
        RemoveBefore = 1,
        RemoveAfter = 2
    }

    public class AutoCutSettings
    {
        public AutoCutTargetTypeFlags TargetTypes { get; set; } = AutoCutTargetTypeFlags.AllCurves;
        public AutoCutTargetLayerFilterMode TargetLayerFilterMode { get; set; } = AutoCutTargetLayerFilterMode.AllLayers;
        public string TargetLayerName { get; set; } = string.Empty;
        public AutoCutTargetType TargetType
        {
            get
            {
                if (TargetTypes == AutoCutTargetTypeFlags.Line) return AutoCutTargetType.Line;
                if (TargetTypes == AutoCutTargetTypeFlags.Polyline) return AutoCutTargetType.Polyline;
                if (TargetTypes == (AutoCutTargetTypeFlags.Line | AutoCutTargetTypeFlags.Polyline)) return AutoCutTargetType.LineAndPolyline;
                return AutoCutTargetType.AllCurves;
            }
            set
            {
                switch (value)
                {
                    case AutoCutTargetType.Line: TargetTypes = AutoCutTargetTypeFlags.Line; break;
                    case AutoCutTargetType.Polyline: TargetTypes = AutoCutTargetTypeFlags.Polyline; break;
                    case AutoCutTargetType.LineAndPolyline: TargetTypes = AutoCutTargetTypeFlags.Line | AutoCutTargetTypeFlags.Polyline; break;
                    case AutoCutTargetType.AllCurves:
                    default:
                        TargetTypes = AutoCutTargetTypeFlags.AllCurves; break;
                }
            }
        }
        public AutoCutCutterTypeFlags CutterTypes { get; set; } = AutoCutCutterTypeFlags.All;
        public AutoCutLayerFilterMode LayerFilterMode { get; set; } = AutoCutLayerFilterMode.SameAsTarget;
        public string CustomLayerName { get; set; } = string.Empty;
        public AutoCutColorFilterMode ColorFilterMode { get; set; } = AutoCutColorFilterMode.SameAsTarget;
        public int SpecificColorIndex { get; set; } = 1;
        public double SearchTolerance { get; set; } = 1.0;
        public double DeduplicationTolerance { get; set; } = 1e-4;
        public AutoCutCutMode CutMode { get; set; } = AutoCutCutMode.BetweenIntersections;
        public AutoCutSingleIntersectionRule SingleIntersectionRule { get; set; } = AutoCutSingleIntersectionRule.Skip;
        public bool ShowSettingsBeforeSelection { get; set; } = true;
        public bool IgnoreElevation { get; set; } = true;

        public AutoCutSettings Clone()
        {
            return new AutoCutSettings
            {
                TargetTypes = this.TargetTypes,
                TargetLayerFilterMode = this.TargetLayerFilterMode,
                TargetLayerName = this.TargetLayerName,
                TargetType = this.TargetType,
                CutterTypes = this.CutterTypes,
                LayerFilterMode = this.LayerFilterMode,
                CustomLayerName = this.CustomLayerName,
                ColorFilterMode = this.ColorFilterMode,
                SpecificColorIndex = this.SpecificColorIndex,
                SearchTolerance = this.SearchTolerance,
                DeduplicationTolerance = this.DeduplicationTolerance,
                CutMode = this.CutMode,
                SingleIntersectionRule = this.SingleIntersectionRule,
                ShowSettingsBeforeSelection = this.ShowSettingsBeforeSelection,
                IgnoreElevation = this.IgnoreElevation
            };
        }
    }
}

