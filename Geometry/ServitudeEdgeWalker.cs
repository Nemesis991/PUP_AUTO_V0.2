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

        /// <summary>Neighbouring shared vertices of one crossing reduced to a single shared point.</summary>
        public int SharedMerged { get; set; }

        /// <summary>A change of землище between two neighbouring points that no crossing had bridged: one of them was made shared.</summary>
        public int Bridged { get; set; }
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
            double pendingSnap = 0;

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
                    Direction = Average(incoming, outgoing),
                    OwnEkatte = ekatte
                };
                foreach (string code in pending) AddEkatte(vertexPoint, code);
                if (pending.Count > 0) vertexPoint.SnapDistanceM = pendingSnap;
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

                    // The nearer of the two vertices takes the crossing, so the choice does not depend on the direction the edge is
                    // walked; an exact tie goes to the vertex with the lower coordinate
                    bool startIsNearer = toStart < toEnd ||
                        (Math.Abs(toStart - toEnd) < 1e-9 && (vertex.X < next.X || (vertex.X == next.X && vertex.Y <= next.Y)));
                    if (toStart <= SnapTolM && startIsNearer)
                    {
                        AddEkatte(vertexPoint, crossing.From);
                        AddEkatte(vertexPoint, crossing.To);
                        vertexPoint.SnapDistanceM = toStart;
                        previousEkatte = crossing.To;
                        stats.SnappedCrossings++;
                        continue;
                    }
                    if (toEnd <= SnapTolM && (!startIsNearer || toStart > SnapTolM))
                    {
                        pendingSnap = toEnd;
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
            return Consolidate(points, resolver, stats);
        }

        /// <summary>
        /// Two checks that make the shared points independent of the direction the edge is walked. (1) Neighbouring points that are
        /// shared by the same pair of землища and lie within <see cref="BoundaryCrossingFinder.ClusterTolM"/> of each other are one
        /// crossing: only the one nearest the crossing stays shared, the others go back to their own землище. (2) Where two
        /// neighbouring points are in different землища and nothing is shared between them (a vertex ON the boundary, or hits that
        /// all fell on the segment ends), one of them is made shared: the one nearer the other землище's parcels.
        /// </summary>
        private static List<ServitudeEdgePointInput> Consolidate(
            List<ServitudeEdgePointInput> points, ServitudeEkatteResolver resolver, EdgeWalkStats stats)
        {
            // (1) clusters of shared points with the same pair
            int i = 0;
            while (i < points.Count)
            {
                if (points[i].Ekattes.Count < 2) { i++; continue; }
                int j = i;
                while (j + 1 < points.Count && SamePair(points[j], points[j + 1]) &&
                       Distance(points[j].X, points[j].Y, points[j + 1].X, points[j + 1].Y) <= BoundaryCrossingFinder.ClusterTolM)
                {
                    j++;
                }

                if (j > i)
                {
                    var group = points.Skip(i).Take(j - i + 1).ToList();
                    if (group.All(p => p.OwnEkatte != null && p.Ekattes.Contains(p.OwnEkatte, StringComparer.Ordinal)))
                    {
                        // the inserted boundary point (distance 0) wins, then the vertex nearest its crossing; a tie goes to the lower coordinate
                        ServitudeEdgePointInput winner = group
                            .OrderBy(p => p.IsBoundary ? 0 : 1).ThenBy(p => p.SnapDistanceM).ThenBy(p => p.X).ThenBy(p => p.Y).First();
                        foreach (ServitudeEdgePointInput loser in group.Where(p => !ReferenceEquals(p, winner)))
                        {
                            loser.Ekattes.Clear();
                            loser.Ekattes.Add(loser.OwnEkatte!);
                            stats.SharedMerged++;
                        }
                    }
                }
                i = j + 1;
            }

            // (2) a change of землище with nothing shared across it
            for (int k = 0; k + 1 < points.Count; k++)
            {
                ServitudeEdgePointInput a = points[k], b = points[k + 1];
                if (a.Ekattes.Count == 0 || b.Ekattes.Count == 0) continue;
                if (a.Ekattes.Intersect(b.Ekattes, StringComparer.Ordinal).Any()) continue;

                string from = a.Ekattes[a.Ekattes.Count - 1], to = b.Ekattes[0];
                double aToOther = resolver.DistanceToEkatte(to, a.X, a.Y);
                double bToOther = resolver.DistanceToEkatte(from, b.X, b.Y);
                bool shareA = aToOther < bToOther || (aToOther == bToOther && (a.X < b.X || (a.X == b.X && a.Y <= b.Y)));
                if (shareA) AddEkatte(a, to); else AddEkatte(b, from);
                (shareA ? a : b).SnapDistanceM = Math.Min(aToOther, bToOther);
                stats.Bridged++;
            }
            return points;
        }

        private static bool SamePair(ServitudeEdgePointInput a, ServitudeEdgePointInput b) =>
            a.Ekattes.Count == b.Ekattes.Count && a.Ekattes.All(e => b.Ekattes.Contains(e, StringComparer.Ordinal));

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
