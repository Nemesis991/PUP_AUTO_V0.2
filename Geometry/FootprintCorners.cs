using System.Globalization;

namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// The footprint corners of a pole as its P-tag attribute VALUES give them ("X, Y" text): parsing, the counter-clockwise
    /// order of the footprint polyline, and the check that both are the same points. Plain numbers, no AutoCAD types.
    /// No transform and no rounding is applied anywhere here.
    /// </summary>
    public static class FootprintCorners
    {
        /// <summary>
        /// Parses "X, Y" strings the way the extractor always did (comma split, empty parts dropped, invariant culture); strings
        /// that do not parse are skipped. Returns the points in the order given, i.e. P1..P4.
        /// </summary>
        public static List<(double X, double Y)> Parse(IEnumerable<string> pointStrings)
        {
            var points = new List<(double, double)>();
            foreach (string text in pointStrings)
            {
                string[] parts = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                {
                    points.Add((x, y));
                }
            }
            return points;
        }

        /// <summary>The same points sorted counter-clockwise by angle around their mean: the order of the footprint polyline.</summary>
        public static List<(double X, double Y)> SortCounterClockwise(IReadOnlyList<(double X, double Y)> points)
        {
            double cx = points.Average(p => p.X);
            double cy = points.Average(p => p.Y);
            return points.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();
        }

        /// <summary>True when every corner equals some vertex within <paramref name="tolerance"/> (metres) in both axes.</summary>
        public static bool AllMatch(IReadOnlyList<(double X, double Y)> corners, IReadOnlyList<(double X, double Y)> vertices, double tolerance)
        {
            foreach ((double X, double Y) corner in corners)
            {
                if (!vertices.Any(v => Math.Abs(v.X - corner.X) <= tolerance && Math.Abs(v.Y - corner.Y) <= tolerance)) return false;
            }
            return true;
        }
    }
}
