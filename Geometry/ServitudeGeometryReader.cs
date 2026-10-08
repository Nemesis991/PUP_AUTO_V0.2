using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.CadRegister;

namespace PUP_AUTO.Geometry
{
    /// <summary>The two edges' points with their землища, and what had to be reported about them.</summary>
    public sealed class ServitudeEdgePoints
    {
        public List<ServitudeEdgePointInput> Left { get; } = new List<ServitudeEdgePointInput>();
        public List<ServitudeEdgePointInput> Right { get; } = new List<ServitudeEdgePointInput>();

        /// <summary>Set when the outline could not be split; nothing else is filled in. Counts only, no coordinates.</summary>
        public string? Error { get; set; }

        public bool AxisReversed { get; set; }
        public int DuplicatesDropped { get; set; }

        /// <summary>Vertices that fell in no picked parcel and took the EKATTE of the point before them.</summary>
        public int VerticesOutsideParcels { get; set; }

        /// <summary>Boundary points inserted where an edge crosses from one землище into another.</summary>
        public int BoundaryPointsInserted { get; set; }

        /// <summary>Crossings where the two землища do not touch: the point sits where the old EKATTE ends.</summary>
        public int GapsBetweenSettlements { get; set; }

        /// <summary>The bounding box of the servitude outline, for replacing the blocks of a previous run.</summary>
        public BoundingBox Box { get; set; }

        public bool Ok => Error == null;
    }

    /// <summary>
    /// Turns the picked servitude, route axis, poles and parcels into the numbered points of the two servitude edges
    /// (official 08). Runs inside the caller's transaction and writes nothing to the drawing.
    ///
    /// The split into a left and a right edge is <see cref="ServitudeEdges"/>; this class reads the geometry for it, finds
    /// the землище of every point (point in polygon with a bounding-box prefilter) and inserts a point wherever an edge
    /// crosses from one землище into another, so the boundary point is listed in both sections.
    /// </summary>
    public static class ServitudeGeometryReader
    {
        /// <summary>Arcs of the axis are sampled with at most this sagitta (m); the axis only sets stations and sides.</summary>
        public const double AxisSagittaM = 0.05;

        /// <summary>A vertex this close to a землище boundary is already on it: no second point is inserted.</summary>
        public const double OnBoundaryToleranceM = 0.001;

        /// <summary>Two intersections closer than this along a segment are one crossing.</summary>
        public const double SameCrossingToleranceM = 1e-6;

        /// <summary>One picked parcel: its EKATTE, its outline for point-in-polygon and the polyline for the intersections.</summary>
        private sealed class Parcel
        {
            public string Ekatte = string.Empty;
            public PlanarPolygon Outline = null!;
            public Polyline Pline = null!;
        }

        /// <param name="servitude">The picked servitude polyline.</param>
        /// <param name="axis">The picked route-axis curves, in pick order.</param>
        /// <param name="poles">Pole number and position; they only orient the axis.</param>
        /// <param name="parcels">The picked parcels by ID (the EKATTE is the part before the first dot).</param>
        public static ServitudeEdgePoints Read(
            Polyline servitude,
            IReadOnlyList<Curve> axis,
            IReadOnlyList<(string Number, double X, double Y)> poles,
            IReadOnlyList<KeyValuePair<string, Polyline>> parcels)
        {
            var result = new ServitudeEdgePoints();

            var outline = new List<EdgeVertex>(servitude.NumberOfVertices);
            for (int i = 0; i < servitude.NumberOfVertices; i++)
            {
                Point2d point = servitude.GetPoint2dAt(i);
                outline.Add(new EdgeVertex(point.X, point.Y, servitude.GetBulgeAt(i)));
            }
            result.Box = Box(outline);

            List<(double X, double Y)> axisPoints = AxisPoints(axis);
            ServitudeEdgeResult split = ServitudeEdges.Split(outline, servitude.Closed, axisPoints, poles);
            result.AxisReversed = split.AxisReversed;
            result.DuplicatesDropped = split.DuplicatesDropped;
            if (!split.Ok)
            {
                result.Error = split.Error;
                return result;
            }

            List<Parcel> picked = parcels
                .Where(p => CadRegisterData.EkatteOf(p.Key).Length > 0)
                .Select(p => new Parcel
                {
                    Ekatte = CadRegisterData.EkatteOf(p.Key),
                    Outline = Outline(p.Value),
                    Pline = p.Value
                })
                .ToList();

            result.Left.AddRange(Walk(split.Left!, picked, result));
            result.Right.AddRange(Walk(split.Right!, picked, result));
            return result;
        }

        private static BoundingBox Box(IReadOnlyList<EdgeVertex> outline)
        {
            if (outline.Count == 0) return new BoundingBox(0, 0, 0, 0);
            return new BoundingBox(
                outline.Min(v => v.X), outline.Min(v => v.Y), outline.Max(v => v.X), outline.Max(v => v.Y));
        }

        /// <summary>The parcel's outline as a polygon, arcs sampled.</summary>
        private static PlanarPolygon Outline(Polyline pline)
        {
            var vertices = new List<(double X, double Y, double Bulge)>(pline.NumberOfVertices);
            for (int i = 0; i < pline.NumberOfVertices; i++)
            {
                Point2d point = pline.GetPoint2dAt(i);
                vertices.Add((point.X, point.Y, pline.GetBulgeAt(i)));
            }
            return PlanarPolygon.FromBulgeVertices(vertices);
        }

        /// <summary>
        /// The axis curves as one point list: polyline vertices (arcs sampled), chained end to end so a route picked as
        /// several curves still runs in one direction. The station and side only need the shape, not the exact parameters.
        /// </summary>
        public static List<(double X, double Y)> AxisPoints(IReadOnlyList<Curve> axis)
        {
            var pieces = axis.Select(CurvePoints).Where(p => p.Count > 1).ToList();
            if (pieces.Count == 0) return new List<(double X, double Y)>();

            var chained = new List<(double X, double Y)>(pieces[0]);
            pieces.RemoveAt(0);
            while (pieces.Count > 0)
            {
                (double X, double Y) tail = chained[chained.Count - 1];
                int best = 0;
                bool atStart = true;
                double bestDistance = double.MaxValue;
                for (int i = 0; i < pieces.Count; i++)
                {
                    double toStart = Distance(tail, pieces[i][0]);
                    double toEnd = Distance(tail, pieces[i][pieces[i].Count - 1]);
                    if (toStart < bestDistance) { bestDistance = toStart; best = i; atStart = true; }
                    if (toEnd < bestDistance) { bestDistance = toEnd; best = i; atStart = false; }
                }
                List<(double X, double Y)> piece = pieces[best];
                pieces.RemoveAt(best);
                if (!atStart) piece.Reverse();
                chained.AddRange(piece.Skip(Distance(tail, piece[0]) <= ServitudeEdges.DuplicateToleranceM ? 1 : 0));
            }
            return chained;
        }

        private static double Distance((double X, double Y) a, (double X, double Y) b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static List<(double X, double Y)> CurvePoints(Curve curve)
        {
            var points = new List<(double X, double Y)>();
            if (curve is Polyline pline)
            {
                for (int i = 0; i < pline.NumberOfVertices; i++)
                {
                    Point2d a = pline.GetPoint2dAt(i);
                    bool last = i == pline.NumberOfVertices - 1;
                    if (last && !pline.Closed) { points.Add((a.X, a.Y)); break; }
                    Point2d b = pline.GetPoint2dAt(last ? 0 : i + 1);
                    points.AddRange(new BulgeSegment(a.X, a.Y, b.X, b.Y, pline.GetBulgeAt(i)).Sample(AxisSagittaM));
                }
                if (pline.Closed && points.Count > 0) points.Add(points[0]);
                return points;
            }

            // Line, Arc and anything else: walked by distance
            double length = SafeLength(curve);
            if (length <= 0) return points;
            int steps = curve is Line ? 1 : Math.Max(1, (int)Math.Ceiling(length / Math.Max(AxisSagittaM * 20, 0.5)));
            steps = Math.Min(steps, 4096);
            for (int i = 0; i <= steps; i++)
            {
                try
                {
                    Point3d point = curve.GetPointAtDist(length * i / steps);
                    points.Add((point.X, point.Y));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // a curve that cannot be walked contributes nothing; the others still orient the axis
                    return points;
                }
            }
            return points;
        }

        private static double SafeLength(Curve curve)
        {
            try
            {
                return curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return 0;
            }
        }

        // -----------------------------------------------------------------
        //  Walking one edge
        // -----------------------------------------------------------------

        /// <summary>
        /// The edge's points in order: every vertex, plus a point wherever the edge crosses into another землище. Each
        /// point carries the землища it is listed under (two on a boundary) and the direction of the edge there.
        /// </summary>
        private static List<ServitudeEdgePointInput> Walk(ServitudeEdge edge, List<Parcel> parcels, ServitudeEdgePoints result)
        {
            var points = new List<ServitudeEdgePointInput>();
            IReadOnlyList<EdgeVertex> vertices = edge.Vertices;
            string? previousEkatte = null;

            // A crossing that falls on the next vertex makes that vertex the boundary point, listed in both землища
            string? pendingEkatte = null;

            for (int i = 0; i < vertices.Count; i++)
            {
                EdgeVertex vertex = vertices[i];
                double incoming = i > 0 ? Direction(vertices[i - 1], vertices[i], 1) : double.NaN;
                double outgoing = i + 1 < vertices.Count ? Direction(vertices[i], vertices[i + 1], 0) : double.NaN;

                string? ekatte = EkatteAt(parcels, vertex.X, vertex.Y);
                if (ekatte == null)
                {
                    ekatte = previousEkatte;
                    result.VerticesOutsideParcels++;
                }

                var point = new ServitudeEdgePointInput
                {
                    X = vertex.X,
                    Y = vertex.Y,
                    Direction = Average(incoming, outgoing)
                };
                if (pendingEkatte != null) point.Ekattes.Add(pendingEkatte);
                if (ekatte != null) AddEkatte(point, ekatte);
                points.Add(point);
                previousEkatte = ekatte ?? pendingEkatte;
                pendingEkatte = null;

                if (i + 1 >= vertices.Count) break;

                // The segment to the next vertex: a change of EKATTE along it gets its own point
                var segment = new BulgeSegment(vertex.X, vertex.Y, vertices[i + 1].X, vertices[i + 1].Y, vertex.Bulge);
                string? nextEkatte = EkatteAt(parcels, vertices[i + 1].X, vertices[i + 1].Y);
                foreach ((double x, double y, string left, string right) in Crossings(segment, parcels, ekatte, nextEkatte, result))
                {
                    // A vertex already on the boundary is listed in both землища instead of getting a twin
                    if (Near(points[points.Count - 1], x, y))
                    {
                        AddEkatte(points[points.Count - 1], right);
                        previousEkatte = right;
                        continue;
                    }
                    if (Near(vertices[i + 1], x, y))
                    {
                        pendingEkatte = left;       // the next vertex is on the boundary: it is listed in both землища
                        continue;
                    }

                    var crossing = new ServitudeEdgePointInput
                    {
                        X = x,
                        Y = y,
                        Direction = segment.DirectionAt(segment.FractionOf(x, y)),
                        IsBoundary = true
                    };
                    crossing.Ekattes.Add(left);
                    AddEkatte(crossing, right);
                    points.Add(crossing);
                    previousEkatte = right;
                    result.BoundaryPointsInserted++;
                }
            }
            return points;
        }

        private static void AddEkatte(ServitudeEdgePointInput point, string ekatte)
        {
            if (!point.Ekattes.Contains(ekatte, StringComparer.Ordinal)) point.Ekattes.Add(ekatte);
        }

        private static bool Near(ServitudeEdgePointInput point, double x, double y) =>
            Math.Abs(point.X - x) <= OnBoundaryToleranceM && Math.Abs(point.Y - y) <= OnBoundaryToleranceM;

        private static bool Near(EdgeVertex vertex, double x, double y) =>
            Math.Abs(vertex.X - x) <= OnBoundaryToleranceM && Math.Abs(vertex.Y - y) <= OnBoundaryToleranceM;

        /// <summary>The direction of travel along the segment between two vertices, at fraction <paramref name="t"/>.</summary>
        private static double Direction(EdgeVertex from, EdgeVertex to, double t) =>
            new BulgeSegment(from.X, from.Y, to.X, to.Y, from.Bulge).DirectionAt(t);

        /// <summary>The mean of the two adjacent directions; at the ends of an edge the only one there is.</summary>
        private static double Average(double incoming, double outgoing)
        {
            if (double.IsNaN(incoming)) return double.IsNaN(outgoing) ? 0 : outgoing;
            if (double.IsNaN(outgoing)) return incoming;
            // averaged as unit vectors, so 350° and 10° give 0° and not 180°
            double x = Math.Cos(incoming) + Math.Cos(outgoing);
            double y = Math.Sin(incoming) + Math.Sin(outgoing);
            return x == 0 && y == 0 ? incoming : Math.Atan2(y, x);
        }

        /// <summary>The EKATTE of the picked parcel holding the point, or null when it is in none.</summary>
        private static string? EkatteAt(List<Parcel> parcels, double x, double y)
        {
            foreach (Parcel parcel in parcels)
            {
                if (parcel.Outline.Contains(x, y)) return parcel.Ekatte;
            }
            return null;
        }

        /// <summary>
        /// The землище changes along one segment: the parcel boundaries it crosses, sorted along it, with the EKATTE before
        /// and after each one. Only the changes are returned; a crossing inside one землище is not a boundary.
        /// </summary>
        private static List<(double X, double Y, string Left, string Right)> Crossings(
            BulgeSegment segment, List<Parcel> parcels, string? startEkatte, string? endEkatte, ServitudeEdgePoints result)
        {
            var crossings = new List<(double X, double Y, string Left, string Right)>();

            // Nothing to look for when both ends are in the same землище and no other one is near the segment
            BoundingBox box = SegmentBox(segment);
            List<Parcel> near = parcels.Where(p => BoundingBox.MayOverlap(box, p.Outline.Box, 0)).ToList();
            if (near.Count == 0) return crossings;
            bool severalEkatte = near.Select(p => p.Ekatte).Distinct(StringComparer.Ordinal).Count() > 1;
            if (!severalEkatte && string.Equals(startEkatte, endEkatte, StringComparison.Ordinal)) return crossings;

            List<double> cuts = Intersections(segment, near);
            if (cuts.Count == 0) return crossings;

            // The EKATTE of each piece between two consecutive cuts, taken at its midpoint
            var bounds = new List<double> { 0 };
            bounds.AddRange(cuts);
            bounds.Add(1);
            var pieces = new List<(double From, double To, string? Ekatte)>();
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                double from = bounds[i], to = bounds[i + 1];
                if (to - from <= SameCrossingToleranceM) continue;
                (double mx, double my) = segment.PointAt((from + to) / 2);
                pieces.Add((from, to, EkatteAt(near, mx, my)));
            }

            string? current = startEkatte;
            double? gapStart = null;
            foreach ((double from, double to, string? ekatte) in pieces)
            {
                if (ekatte == null)
                {
                    // A hole between the two землища: remember where the known one ended
                    gapStart ??= from;
                    continue;
                }
                if (current == null) { current = ekatte; gapStart = null; continue; }
                if (string.Equals(ekatte, current, StringComparison.Ordinal)) { gapStart = null; continue; }

                double cut = gapStart ?? from;
                if (gapStart != null) result.GapsBetweenSettlements++;
                (double x, double y) = segment.PointAt(cut);
                crossings.Add((x, y, current, ekatte));
                current = ekatte;
                gapStart = null;
            }
            return crossings;
        }

        private static BoundingBox SegmentBox(BulgeSegment segment)
        {
            // The arc's sampled points, so a bulging segment's box is not just its chord's
            List<(double X, double Y)> points = segment.Sample(PlanarPolygon.ArcSagittaM).ToList();
            points.Add((segment.X2, segment.Y2));
            return new BoundingBox(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }

        /// <summary>
        /// The fractions along the segment where it meets the boundaries of the given parcels, sorted and de-duplicated.
        /// The segment is built as a <see cref="Line"/> or an <see cref="Arc"/> and disposed again.
        /// </summary>
        private static List<double> Intersections(BulgeSegment segment, List<Parcel> parcels)
        {
            var fractions = new List<double>();
            Curve? curve = null;
            try
            {
                curve = ToCurve(segment);
                if (curve == null) return fractions;

                foreach (Parcel parcel in parcels)
                {
                    using (var hits = new Point3dCollection())
                    {
                        try
                        {
                            curve.IntersectWith(parcel.Pline, Intersect.OnBothOperands, hits, IntPtr.Zero, IntPtr.Zero);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception)
                        {
                            continue;   // this parcel contributes no crossing; the others still do
                        }
                        foreach (Point3d hit in hits)
                        {
                            double t = segment.FractionOf(hit.X, hit.Y);
                            if (t > SameCrossingToleranceM && t < 1 - SameCrossingToleranceM) fractions.Add(t);
                        }
                    }
                }
            }
            finally
            {
                curve?.Dispose();
            }

            fractions.Sort();
            var unique = new List<double>();
            foreach (double t in fractions)
            {
                if (unique.Count == 0 || t - unique[unique.Count - 1] > SameCrossingToleranceM) unique.Add(t);
            }
            return unique;
        }

        /// <summary>The segment as a new, not database-resident <see cref="Line"/> or <see cref="Arc"/>; null when degenerate.</summary>
        private static Curve? ToCurve(BulgeSegment segment)
        {
            if (segment.Chord <= 0) return null;
            if (!segment.IsArc) return new Line(new Point3d(segment.X1, segment.Y1, 0), new Point3d(segment.X2, segment.Y2, 0));

            (double cx, double cy) = segment.Centre;
            double start = Math.Atan2(segment.Y1 - cy, segment.X1 - cx);
            double end = Math.Atan2(segment.Y2 - cy, segment.X2 - cx);
            // An Arc always runs counter-clockwise from its start angle, so a clockwise segment is given the other way round
            if (segment.Bulge < 0) (start, end) = (end, start);
            return new Arc(new Point3d(cx, cy, 0), segment.Radius, start, end);
        }
    }
}
