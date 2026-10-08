using PUP_AUTO.CadRegister;

namespace PUP_AUTO.Geometry
{
    /// <summary>What the walk along one edge found, in counts (for the one summary line per side).</summary>
    public sealed class EdgeWalkStats
    {
        public int Vertices { get; set; }
        public int BoundaryPoints { get; set; }
        public int MergedHits { get; set; }

        /// <summary>Crossings that fell on an existing vertex, which was listed in both землища instead of getting a twin.</summary>
        public int SnappedCrossings { get; set; }

        /// <summary>Vertices in no picked parcel and not just off the previous point's землище: numbered, but left out.</summary>
        public int VerticesOutside { get; set; }

        /// <summary>Boundary points with a gap between the two землища, and with an overlap.</summary>
        public int Gaps { get; set; }
        public int Overlaps { get; set; }
    }

    /// <summary>
    /// Walks one servitude edge and lists its points: every vertex, plus one point where the edge crosses from one землище
    /// into another. Pure maths, no AutoCAD types: the raw hits with the parcel boundaries come in through a delegate.
    ///
    /// A crossing within <see cref="SnapTolM"/> of a vertex adds no point; that vertex is listed in both землища, with one
    /// number. Otherwise the crossing is one point at the end of the first землище (see <see cref="BoundaryCrossingFinder"/>).
    /// </summary>
    public static class ServitudeEdgeWalker
    {
        /// <summary>A crossing this close to an existing vertex of the edge is that vertex (m).</summary>
        public const double SnapTolM = 0.05;

        /// <summary>The fractions along a segment where it meets the picked parcels' boundaries, sorted.</summary>
        public delegate IReadOnlyList<double> CutProvider(BulgeSegment segment, string? startEkatte, string? endEkatte);

        public static List<ServitudeEdgePointInput> Walk(
            ServitudeEdge edge, ServitudeEkatteResolver resolver, CutProvider cuts, EdgeWalkStats stats)
        {
            var points = new List<ServitudeEdgePointInput>();
            IReadOnlyList<EdgeVertex> vertices = edge.Vertices;
            stats.Vertices += vertices.Count;
            string? previousEkatte = null;

            // A crossing that falls on the next vertex makes that vertex the boundary point, listed in both землища
            var pending = new List<string>();

            for (int i = 0; i < vertices.Count; i++)
            {
                EdgeVertex vertex = vertices[i];
                double incoming = i > 0 ? Direction(vertices[i - 1], vertices[i], 1) : double.NaN;
                double outgoing = i + 1 < vertices.Count ? Direction(vertices[i], vertices[i + 1], 0) : double.NaN;

                // In a picked parcel, or just off one of the previous point's землище; anything else is left out of the register
                string? ekatte = resolver.Resolve(vertex.X, vertex.Y, previousEkatte ?? pending.FirstOrDefault(), out _);
                if (ekatte == null && pending.Count == 0) stats.VerticesOutside++;

                var vertexPoint = new ServitudeEdgePointInput
                {
                    X = vertex.X,
                    Y = vertex.Y,
                    Direction = Average(incoming, outgoing)
                };
                foreach (string code in pending) AddEkatte(vertexPoint, code);
                if (ekatte != null) AddEkatte(vertexPoint, ekatte);
                points.Add(vertexPoint);
                previousEkatte = ekatte ?? pending.FirstOrDefault();
                pending.Clear();

                if (i + 1 >= vertices.Count) break;

                // The segment to the next vertex: a change of EKATTE along it gets its own point
                EdgeVertex next = vertices[i + 1];
                var segment = new BulgeSegment(vertex.X, vertex.Y, next.X, next.Y, vertex.Bulge);
                string? nextEkatte = resolver.At(next.X, next.Y);
                IReadOnlyList<double> hits = cuts(segment, ekatte, nextEkatte);
                foreach (BoundaryCrossing crossing in BoundaryCrossingFinder.Find(segment, hits, resolver, ekatte))
                {
                    (double x, double y) = segment.PointAt(crossing.Fraction);
                    double toStart = Distance(vertex.X, vertex.Y, x, y);
                    double toEnd = Distance(next.X, next.Y, x, y);

                    if (toStart <= SnapTolM)
                    {
                        AddEkatte(vertexPoint, crossing.From);
                        AddEkatte(vertexPoint, crossing.To);
                        previousEkatte = crossing.To;
                        stats.SnappedCrossings++;
                        continue;
                    }
                    if (toEnd <= SnapTolM)
                    {
                        pending.Clear();
                        pending.Add(crossing.From);
                        pending.Add(crossing.To);
                        previousEkatte = crossing.To;
                        stats.SnappedCrossings++;
                        continue;
                    }

                    var boundary = new ServitudeEdgePointInput
                    {
                        X = x,
                        Y = y,
                        Direction = segment.DirectionAt(crossing.Fraction),
                        IsBoundary = true,
                        NearestVertexM = Math.Min(toStart, toEnd),
                        MergedHits = crossing.MergedHits,
                        SpanM = crossing.SpanM,
                        IsOverlap = crossing.IsOverlap
                    };
                    boundary.Ekattes.Add(crossing.From);
                    AddEkatte(boundary, crossing.To);
                    points.Add(boundary);
                    previousEkatte = crossing.To;

                    stats.BoundaryPoints++;
                    stats.MergedHits += crossing.MergedHits;
                    if (crossing.SpanM > 0) { if (crossing.IsOverlap) stats.Overlaps++; else stats.Gaps++; }
                }
            }
            return points;
        }

        private static void AddEkatte(ServitudeEdgePointInput point, string ekatte)
        {
            if (!point.Ekattes.Contains(ekatte, StringComparer.Ordinal)) point.Ekattes.Add(ekatte);
        }

        private static double Distance(double x1, double y1, double x2, double y2) =>
            Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

        /// <summary>The direction of travel along the segment between two vertices, at fraction <paramref name="t"/>.</summary>
        private static double Direction(EdgeVertex from, EdgeVertex to, double t) =>
            new BulgeSegment(from.X, from.Y, to.X, to.Y, from.Bulge).DirectionAt(t);

        /// <summary>The mean of the two adjacent directions (the bisector at a corner or an arc vertex); at the ends of an edge the only one there is.</summary>
        public static double Average(double incoming, double outgoing)
        {
            if (double.IsNaN(incoming)) return double.IsNaN(outgoing) ? 0 : outgoing;
            if (double.IsNaN(outgoing)) return incoming;
            // averaged as unit vectors, so 350° and 10° give 0° and not 180°
            double x = Math.Cos(incoming) + Math.Cos(outgoing);
            double y = Math.Sin(incoming) + Math.Sin(outgoing);
            return Math.Abs(x) < 1e-12 && Math.Abs(y) < 1e-12 ? incoming : Math.Atan2(y, x);
        }
    }
}
