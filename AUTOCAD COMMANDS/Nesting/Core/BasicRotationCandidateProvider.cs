using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS.Nesting.Core
{
    /// <summary>
    /// V1 orientations: the configured quarter turns (default 0/90/180/270), plus their mirrored
    /// versions only when AllowMirror is on. Orientations that produce the same polygon (e.g. 180
    /// deg of a rectangle) are dropped. No continuous rotation in V1.
    /// </summary>
    public sealed class BasicRotationCandidateProvider : IRotationCandidateProvider
    {
        public IList<OrientationTransform> GetOrientations(PartGroup group, NestingSettings settings)
        {
            List<OrientationTransform> result = new List<OrientationTransform>();
            List<string> seenKeys = new List<string>();

            List<double> rotations = settings.AllowedRotations != null && settings.AllowedRotations.Count > 0
                ? settings.AllowedRotations
                : new List<double> { 0.0 };

            bool[] mirrorOptions = settings.AllowMirror ? new[] { false, true } : new[] { false };

            foreach (bool mirror in mirrorOptions)
            {
                foreach (double rotation in rotations)
                {
                    OrientationTransform o = new OrientationTransform(rotation, mirror);
                    if (result.Contains(o)) continue;

                    string key = ShapeKey(group.Shape.Polygon.Transform(o, 0, 0));
                    if (seenKeys.Contains(key)) continue;

                    seenKeys.Add(key);
                    result.Add(o);
                }
            }

            return result;
        }

        /// <summary>Order-independent key of the polygon normalised to its bounding-box corner.</summary>
        private static string ShapeKey(PolyShape shape)
        {
            long ox = shape.Bounds.MinX, oy = shape.Bounds.MinY;
            List<string> rings = new List<string> { RingKey(shape.Outer, ox, oy) };
            List<string> holes = new List<string>();
            foreach (IntPoint[] h in shape.Holes) holes.Add(RingKey(h, ox, oy));
            holes.Sort(StringComparer.Ordinal);
            rings.AddRange(holes);
            return string.Join("|", rings.ToArray());
        }

        private static string RingKey(IntPoint[] ring, long ox, long oy)
        {
            List<long> xs = new List<long>(ring.Length);
            string[] pts = new string[ring.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                pts[i] = (ring[i].X - ox).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                         (ring[i].Y - oy).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            Array.Sort(pts, StringComparer.Ordinal);
            return string.Join(";", pts);
        }
    }
}
