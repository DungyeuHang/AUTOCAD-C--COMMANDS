using System;
using System.Collections.Generic;
using System.Globalization;

namespace AUTOCAD_COMMANDS.Nesting.Recognition
{
    // ==========================================================================================
    // GHOPHOI - RECOGNITION model (plain C#, no AutoCAD).
    // The AutoCAD layer (NestingSelectionReader) converts entities into CurveChain / TextItem
    // with a SourceIndex pointing back to its own entity list; everything here is testable.
    // ==========================================================================================

    public struct Pt
    {
        public readonly double X;
        public readonly double Y;

        public Pt(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double DistanceTo(Pt o)
        {
            double dx = X - o.X, dy = Y - o.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", X, Y);
        }
    }

    /// <summary>A polyline approximation of one curve entity (or one curve of an exploded block).</summary>
    public sealed class CurveChain
    {
        public CurveChain(int sourceIndex, List<Pt> points, bool closed)
            : this(sourceIndex, points, closed, false)
        {
        }

        /// <param name="approximated">True when the points approximate a curve (arc, circle, spline...).</param>
        public CurveChain(int sourceIndex, List<Pt> points, bool closed, bool approximated)
        {
            SourceIndex = sourceIndex;
            Points = points;
            Closed = closed;
            Approximated = approximated;
        }

        public int SourceIndex { get; private set; }

        public List<Pt> Points { get; private set; }

        public bool Closed { get; private set; }

        /// <summary>Chord approximation of a curve (true) or exact straight segments (false).</summary>
        public bool Approximated { get; private set; }

        public Pt Start { get { return Points[0]; } }

        public Pt End { get { return Points[Points.Count - 1]; } }
    }

    public sealed class TextItem
    {
        public TextItem(int sourceIndex, string text, Pt position)
        {
            SourceIndex = sourceIndex;
            Text = text ?? string.Empty;
            Position = position;
        }

        public int SourceIndex { get; private set; }

        public string Text { get; private set; }

        /// <summary>Centre of the text's extents (insertion point when extents are unavailable).</summary>
        public Pt Position { get; private set; }
    }

    public enum PartStatus
    {
        Ok,
        Warning,
        Ambiguous,
        InvalidGeometry
    }

    public sealed class RecognizedLoop
    {
        public RecognizedLoop(List<Pt> points, List<int> sources)
            : this(points, sources, false)
        {
        }

        public RecognizedLoop(List<Pt> points, List<int> sources, bool approximated)
        {
            Points = points;
            Sources = sources;
            Approximated = approximated;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Pt p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            Area = Math.Abs(SignedArea(points));
        }

        public List<Pt> Points { get; private set; }

        public List<int> Sources { get; private set; }

        /// <summary>At least one piece of the loop approximates a curve.</summary>
        public bool Approximated { get; private set; }

        public double Area { get; private set; }

        public double MinX { get; private set; }

        public double MinY { get; private set; }

        public double MaxX { get; private set; }

        public double MaxY { get; private set; }

        public static double SignedArea(List<Pt> pts)
        {
            double s = 0;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                s += (pts[j].X - pts[0].X) * (pts[i].Y - pts[0].Y) - (pts[i].X - pts[0].X) * (pts[j].Y - pts[0].Y);
            }

            return s * 0.5;
        }
    }

    /// <summary>One review record: a recognised part, or an invalid-geometry record.</summary>
    public sealed class RecognizedPart
    {
        public RecognizedPart()
        {
            Holes = new List<RecognizedLoop>();
            MarkingSources = new List<int>();
            TextSources = new List<int>();
            Notes = new List<string>();
            GeometrySources = new List<int>();
            Quantity = 1;
            Include = true;
        }

        public int Index { get; set; }

        public string Name { get; set; }

        /// <summary>Null for invalid-geometry records that have no closed outer contour.</summary>
        public RecognizedLoop Outer { get; set; }

        public List<RecognizedLoop> Holes { get; private set; }

        /// <summary>Open geometry lying inside the part (bend lines, engraving...) - output only.</summary>
        public List<int> MarkingSources { get; private set; }

        /// <summary>Every source entity that makes up this record (contours + markings).</summary>
        public List<int> GeometrySources { get; private set; }

        /// <summary>Texts whose metadata was assigned to this part.</summary>
        public List<int> TextSources { get; private set; }

        public int Quantity { get; set; }

        public string Material { get; set; }

        public bool QuantityFromText { get; set; }

        public bool MaterialFromText { get; set; }

        public PartStatus Status { get; set; }

        public List<string> Notes { get; private set; }

        /// <summary>User decision in the review table.</summary>
        public bool Include { get; set; }

        /// <summary>User confirmed an AMBIGUOUS / edited record.</summary>
        public bool Confirmed { get; set; }

        public double MinX { get; set; }

        public double MinY { get; set; }

        public double MaxX { get; set; }

        public double MaxY { get; set; }

        public double Width { get { return MaxX - MinX; } }

        public double Height { get { return MaxY - MinY; } }

        public bool IsNestable { get { return Status != PartStatus.InvalidGeometry && Outer != null; } }

        public void Escalate(PartStatus status, string note)
        {
            if (status > Status) Status = status;
            if (!string.IsNullOrEmpty(note) && !Notes.Contains(note)) Notes.Add(note);
        }

        public string NotesText
        {
            get { return string.Join("; ", Notes.ToArray()); }
        }
    }

    public sealed class RecognitionSettings
    {
        /// <summary>Endpoints closer than this are joined into one contour node (mm).</summary>
        public double JoinTolerance { get; set; } = 0.05;

        /// <summary>Loops smaller than this are zero-area / degenerate (mm^2).</summary>
        public double MinLoopArea { get; set; } = 1.0;

        /// <summary>A text farther than this from every part is not associated (mm).</summary>
        public double MaxTextDistance { get; set; } = 300.0;

        /// <summary>Second-nearest part within best x ratio ... is ambiguous.</summary>
        public double AmbiguityRatio { get; set; } = 1.5;

        /// <summary>... or within this absolute distance difference (mm).</summary>
        public double AmbiguityAbsolute { get; set; } = 5.0;

        public MetadataRules Metadata { get; set; } = new MetadataRules();
    }

    public sealed class RecognitionResult
    {
        public RecognitionResult()
        {
            Parts = new List<RecognizedPart>();
            GlobalWarnings = new List<string>();
        }

        public List<RecognizedPart> Parts { get; private set; }

        public List<string> GlobalWarnings { get; private set; }

        public int IgnoredEntityCount { get; set; }
    }
}
