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

        /// <summary>The route was reversed on request ("Обратна посока") after the normal orientation.</summary>
        public bool RouteReversedByRequest { get; set; }

        public int DuplicatesDropped { get; set; }

        /// <summary>What the walk along each edge found, for the one summary line per side.</summary>
        public EdgeWalkStats LeftStats { get; } = new EdgeWalkStats();
        public EdgeWalkStats RightStats { get; } = new EdgeWalkStats();

        /// <summary>Consecutive points closer than 1 cm that were merged into one, per side.</summary>
        public int LeftMerged { get; set; }
        public int RightMerged { get; set; }

        /// <summary>The servitude outline as a polygon, to check that a label anchor stays outside it.</summary>
        public PlanarPolygon? Outline { get; set; }

        /// <summary>Vertices in no picked parcel and not just off one of the previous point's землище: numbered, but left out.</summary>
        public int VerticesOutsideParcels => LeftStats.VerticesOutside + RightStats.VerticesOutside;

        /// <summary>Boundary points inserted where an edge crosses from one землище into another.</summary>
        public int BoundaryPointsInserted => LeftStats.BoundaryPoints + RightStats.BoundaryPoints;

        /// <summary>Crossings where the two землища do not touch: the point sits where the old EKATTE ends.</summary>
        public int GapsBetweenSettlements => LeftStats.Gaps + RightStats.Gaps;

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
            IReadOnlyList<KeyValuePair<string, Polyline>> parcels,
            bool reverseRoute = false)
        {
            var result = new ServitudeEdgePoints();

            var outline = new List<EdgeVertex>(servitude.NumberOfVertices);
            for (int i = 0; i < servitude.NumberOfVertices; i++)
            {
                Point2d point = servitude.GetPoint2dAt(i);
                outline.Add(new EdgeVertex(point.X, point.Y, servitude.GetBulgeAt(i)));
            }
            result.Box = Box(outline);
            result.Outline = PlanarPolygon.FromBulgeVertices(outline.Select(v => (v.X, v.Y, v.Bulge)).ToList());

            List<(double X, double Y)> axisPoints = AxisPoints(axis);
            ServitudeEdgeResult split = ServitudeEdges.Split(outline, servitude.Closed, axisPoints, poles, reverseRoute);
            result.AxisReversed = split.AxisReversed;
            result.RouteReversedByRequest = split.RouteReversedByRequest;
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

            var resolver = new ServitudeEkatteResolver(picked.Select(p => (p.Ekatte, p.Outline)).ToList());
            ServitudeEdgeWalker.CutProvider cuts = (segment, start, end) => Cuts(segment, picked, start, end);

            List<ServitudeEdgePointInput> left = ServitudeEdgeWalker.Walk(split.Left!, resolver, cuts, result.LeftStats);
            List<ServitudeEdgePointInput> right = ServitudeEdgeWalker.Walk(split.Right!, resolver, cuts, result.RightStats);

            // Safety net: two consecutive points closer than 1 cm are one point (the землища of the dropped one are kept)
            result.Left.AddRange(ServitudeRegisterBuilder.MergeCloseNeighbours(left, out int leftMerged));
            result.Right.AddRange(ServitudeRegisterBuilder.MergeCloseNeighbours(right, out int rightMerged));
            result.LeftMerged = leftMerged;
            result.RightMerged = rightMerged;
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
        //  Raw hits with the parcel boundaries (the walk itself is ServitudeEdgeWalker, pure maths)
        // -----------------------------------------------------------------

        /// <summary>
        /// The fractions along the segment where it meets the boundaries of the picked parcels near it. Empty when nothing
        /// can change along it: both ends in the same землище and no other one near.
        /// </summary>
        private static IReadOnlyList<double> Cuts(BulgeSegment segment, List<Parcel> parcels, string? startEkatte, string? endEkatte)
        {
            BoundingBox box = SegmentBox(segment);
            List<Parcel> near = parcels.Where(p => BoundingBox.MayOverlap(box, p.Outline.Box, 0)).ToList();
            if (near.Count == 0) return new List<double>();

            bool severalEkatte = near.Select(p => p.Ekatte).Distinct(StringComparer.Ordinal).Count() > 1;
            if (!severalEkatte && string.Equals(startEkatte, endEkatte, StringComparison.Ordinal)) return new List<double>();
            return Intersections(segment, near);
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
