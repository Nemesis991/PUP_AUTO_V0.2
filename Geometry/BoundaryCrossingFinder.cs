namespace PUP_AUTO.Geometry
{
    /// <summary>One землище change along a servitude segment, after the raw hits of a boundary were merged into it.</summary>
    public sealed class BoundaryCrossing
    {
        /// <summary>Where the first EKATTE ends, as a fraction of the segment's length.</summary>
        public double Fraction { get; set; }

        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;

        /// <summary>How many raw hits (parcel-boundary intersections) were merged into this one crossing.</summary>
        public int MergedHits { get; set; }

        /// <summary>The distance (m) between the first and the last merged hit: the gap or overlap between the two землища here.</summary>
        public double SpanM { get; set; }

        /// <summary>True when the two землища overlap over the span; false for a gap (or no span at all).</summary>
        public bool IsOverlap { get; set; }
    }

    /// <summary>
    /// Turns the raw hits of a servitude segment with the picked parcels' boundaries into землище crossings: ONE per real
    /// crossing, however many parcel boundaries the segment meets there. Pure maths, no AutoCAD types.
    ///
    /// Neighbouring землища do not share exactly the same boundary line (small gaps or overlaps), so one crossing gives two or
    /// more hits a few mm to a few cm apart. The segment is cut at every hit, each piece is classified by its midpoint, and
    /// pieces shorter than <see cref="ClusterTolM"/> are ignored; what is left changes землище once per real crossing. A
    /// sliver that returns to the same землище (A → B → A within the tolerance) changes nothing and gives no point.
    /// </summary>
    public static class BoundaryCrossingFinder
    {
        /// <summary>Hits within this distance along a segment are one crossing, and pieces shorter than it are ignored (m).</summary>
        public const double ClusterTolM = 0.50;

        private const double PositionEpsilonM = 1e-9;

        /// <param name="cuts">Fractions (0..1) along the segment where it meets a parcel boundary, sorted.</param>
        /// <param name="startEkatte">The землище the segment starts in; null when its first vertex is in none.</param>
        public static List<BoundaryCrossing> Find(
            BulgeSegment segment, IReadOnlyList<double> cuts, ServitudeEkatteResolver resolver, string? startEkatte)
        {
            var crossings = new List<BoundaryCrossing>();
            double length = segment.Length;
            if (length <= 0 || cuts.Count == 0) return crossings;

            // The segment cut at every hit, each piece classified by its midpoint (positions in metres along it)
            var bounds = new List<double> { 0 };
            bounds.AddRange(cuts.Select(t => t * length));
            bounds.Add(length);
            var pieces = new List<(double From, double To, string? Ekatte)>();
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                double from = bounds[i], to = bounds[i + 1];
                if (to - from <= PositionEpsilonM) continue;
                (double mx, double my) = segment.PointAt((from + to) / 2 / length);
                pieces.Add((from, to, resolver.At(mx, my)));
            }

            // Tiny pieces between two землища are noise. The first and the last piece stay whatever their length: the vertex at that
            // end of the segment really is there, and the crossing next to it is the walker's to snap to that vertex
            List<(double From, double To, string? Ekatte)> kept = pieces
                .Where((p, i) => i == 0 || i == pieces.Count - 1 || p.To - p.From >= ClusterTolM).ToList();

            // Neighbouring pieces of the same землище (or both in none) are one run
            var runs = new List<(double From, double To, string? Ekatte)>();
            foreach (var piece in kept)
            {
                if (runs.Count > 0 && string.Equals(runs[runs.Count - 1].Ekatte, piece.Ekatte, StringComparison.Ordinal))
                {
                    runs[runs.Count - 1] = (runs[runs.Count - 1].From, piece.To, piece.Ekatte);
                }
                else
                {
                    runs.Add(piece);
                }
            }

            string? current = startEkatte;
            double endOfCurrent = 0;        // where the current землище last ended along the segment
            foreach ((double from, double to, string? ekatte) in runs)
            {
                if (ekatte == null) continue;       // a hole: the old землище ended before it, where endOfCurrent says

                if (current == null)
                {
                    // leaving no землище and entering one is not a crossing between two
                    current = ekatte;
                    endOfCurrent = to;
                    continue;
                }
                if (string.Equals(ekatte, current, StringComparison.Ordinal))
                {
                    endOfCurrent = to;
                    continue;
                }

                // current -> ekatte: ONE crossing, at the end of the first землище
                List<double> merged = cuts.Select(t => t * length)
                    .Where(p => p >= endOfCurrent - ClusterTolM && p <= from + ClusterTolM).ToList();
                if (merged.Count == 0) merged.Add(endOfCurrent);
                double span = merged.Max() - merged.Min();
                double midpoint = (merged.Max() + merged.Min()) / 2;
                (double cx, double cy) = segment.PointAt(midpoint / length);

                crossings.Add(new BoundaryCrossing
                {
                    Fraction = endOfCurrent / length,
                    From = current,
                    To = ekatte,
                    MergedHits = merged.Count,
                    SpanM = span,
                    IsOverlap = span > 0 && resolver.EkattesAt(cx, cy).Count > 1
                });
                current = ekatte;
                endOfCurrent = to;
            }
            return crossings;
        }
    }
}
