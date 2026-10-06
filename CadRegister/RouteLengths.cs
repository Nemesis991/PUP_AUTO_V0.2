namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// The plain-number parts of the route length per землище (no AutoCAD types): which split parameters to keep,
    /// summing the metres of the axis pieces per EKATTE, the kilometre rounding and a point-in-polygon test.
    /// </summary>
    public static class RouteLengths
    {
        /// <summary>
        /// The parameters to split an axis at: sorted ascending, without those at (or within <paramref name="gap"/> of) the
        /// start or the end, and without repeats (an axis that crosses a shared parcel corner gives the same point twice).
        /// </summary>
        public static List<double> SortSplitParameters(IEnumerable<double> parameters, double start, double end, double gap = 1e-7)
        {
            var sorted = parameters.Where(p => !double.IsNaN(p)).OrderBy(p => p).ToList();
            var kept = new List<double>();
            foreach (double p in sorted)
            {
                if (p <= start + gap || p >= end - gap) continue;
                if (kept.Count > 0 && p - kept[kept.Count - 1] <= gap) continue;
                kept.Add(p);
            }
            return kept;
        }

        /// <summary>Metres per parcel ID -> metres per EKATTE (the part of the ID before the first dot); IDs without one are left out.</summary>
        public static Dictionary<string, decimal> SumByEkatte(IEnumerable<KeyValuePair<string, double>> metresByParcelId)
        {
            var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> entry in metresByParcelId)
            {
                string ekatte = CadRegisterData.EkatteOf(entry.Key);
                if (ekatte.Length == 0) continue;
                result.TryGetValue(ekatte, out decimal sum);
                result[ekatte] = sum + (decimal)entry.Value;
            }
            return result;
        }

        /// <summary>Kilometres, 3 decimals, from metres (sums are made in metres and rounded once).</summary>
        public static decimal ToKm(decimal metres) => Math.Round(metres / 1000m, 3, MidpointRounding.AwayFromZero);

        /// <summary>
        /// The points of a circular arc strictly between its ends (<paramref name="segments"/> - 1 of them) for a polyline
        /// segment with a bulge, so an arc side of a parcel can be tested as a ring of short chords.
        /// </summary>
        public static List<(double X, double Y)> ArcPoints(double x0, double y0, double x1, double y1, double bulge, int segments)
        {
            var points = new List<(double, double)>();
            double dx = x1 - x0, dy = y1 - y0;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord == 0.0 || bulge == 0.0 || segments < 2) return points;

            // the centre lies on the left of the chord for a counter-clockwise arc (bulge > 0)
            double offset = chord / 2.0 * (1.0 - bulge * bulge) / (2.0 * bulge);
            double cx = (x0 + x1) / 2.0 - dy / chord * offset;
            double cy = (y0 + y1) / 2.0 + dx / chord * offset;
            double radius = Math.Sqrt((x0 - cx) * (x0 - cx) + (y0 - cy) * (y0 - cy));
            double startAngle = Math.Atan2(y0 - cy, x0 - cx);
            double sweep = 4.0 * Math.Atan(bulge);
            for (int i = 1; i < segments; i++)
            {
                double angle = startAngle + sweep * i / segments;
                points.Add((cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)));
            }
            return points;
        }

        /// <summary>Even-odd point-in-polygon test; the ring is implicitly closed. A point exactly on an edge may count either way.</summary>
        public static bool PolygonContains(IReadOnlyList<(double X, double Y)> ring, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                (double xi, double yi) = ring[i];
                (double xj, double yj) = ring[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }
    }
}
