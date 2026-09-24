using System;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// Lexicographic comparison (not a weighted utilisation score):
    ///   1. fewer unplaced parts,
    ///   2. fewer sheets,
    ///   3. smaller total used sheet length (the unused tail of a sheet is a remnant, not waste),
    ///   4. tighter packing: smaller total used bounding area (used length x used height).
    /// Lengths are compared with a 1 mm tolerance so that noise cannot flip a decision.
    /// </summary>
    public sealed class LexicographicSolutionEvaluator : ISolutionEvaluator
    {
        private const long LengthTolerance = 1000;

        public int Compare(DecodedLayout a, DecodedLayout b)
        {
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;

            int c = a.Unplaced.Count.CompareTo(b.Unplaced.Count);
            if (c != 0) return c;

            c = a.Sheets.Count.CompareTo(b.Sheets.Count);
            if (c != 0) return c;

            long lenA = TotalUsedLength(a), lenB = TotalUsedLength(b);
            if (Math.Abs(lenA - lenB) > LengthTolerance) return lenA.CompareTo(lenB);

            double areaA = TotalUsedArea(a), areaB = TotalUsedArea(b);
            if (Math.Abs(areaA - areaB) > (double)LengthTolerance * LengthTolerance) return areaA.CompareTo(areaB);

            return 0;
        }

        public static long TotalUsedLength(DecodedLayout layout)
        {
            long sum = 0;
            foreach (DecodedSheet s in layout.Sheets) sum += s.MaxX;
            return sum;
        }

        private static double TotalUsedArea(DecodedLayout layout)
        {
            double sum = 0;
            foreach (DecodedSheet s in layout.Sheets) sum += (double)s.MaxX * s.MaxY;
            return sum;
        }
    }
}
