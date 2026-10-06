using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.Geometry
{
    /// <summary>The route length found inside the picked parcels; metres only.</summary>
    public sealed class RouteLengthResult
    {
        /// <summary>Metres of axis per parcel ID.</summary>
        public Dictionary<string, double> MetresByParcelId { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>Metres of axis that lie in none of the picked parcels.</summary>
        public double OutsideMetres { get; set; }

        public int Pieces { get; set; }
    }

    /// <summary>Counts and stopwatch ticks of one route-length run, for the [PERF] lines (no personal data).</summary>
    public sealed class RouteLengthStats
    {
        public int Curves;
        public int Vertices;
        public int ArcSegments;
        public double LengthM;
        public double ExtentX;
        public double ExtentY;

        public int IntersectCalls;
        public int IntersectPoints;
        public int IntersectSkipped;
        public long IntersectTicks;
        public long IntersectMaxTicks;
        public long ParameterTicks;

        public int SplitCalls;
        public int SplitPieces;
        public long SplitTicks;

        public int ContainsTried;
        public int ContainsSkipped;
        public long AssignTicks;

        private static long Ms(long ticks) => ticks * 1000 / System.Diagnostics.Stopwatch.Frequency;

        public void Log(Logger logger)
        {
            logger.LogPerf($"Route axis: {Curves} curves, {Vertices} vertices, {ArcSegments} arc segments, " +
                           $"length {LengthM:F0} m, extents {ExtentX:F0} x {ExtentY:F0} m");
            logger.LogPerf($"Route IntersectWith: {IntersectCalls} calls, {IntersectSkipped} skipped by box, {IntersectPoints} points, " +
                           $"{Ms(IntersectTicks)} ms, longest call {Ms(IntersectMaxTicks)} ms; point -> parameter {Ms(ParameterTicks)} ms");
            logger.LogPerf($"Route GetSplitCurves: {SplitCalls} calls, {SplitPieces} pieces, {Ms(SplitTicks)} ms");
            logger.LogPerf($"Route piece -> parcel: {ContainsTried} point-in-polygon tests, {ContainsSkipped} skipped by box, {Ms(AssignTicks)} ms");
        }

        /// <summary>The axis facts: counted once per run.</summary>
        internal void AddAxis(Curve axis)
        {
            Curves++;
            try
            {
                LengthM += axis.GetDistanceAtParameter(axis.EndParam) - axis.GetDistanceAtParameter(axis.StartParam);
                if (axis is Polyline pline)
                {
                    Vertices += pline.NumberOfVertices;
                    int segments = pline.Closed ? pline.NumberOfVertices : pline.NumberOfVertices - 1;
                    for (int i = 0; i < segments; i++)
                    {
                        if (Math.Abs(pline.GetBulgeAt(i)) > GeometryTolerances.BulgeEpsilon) ArcSegments++;
                    }
                }
                else
                {
                    Vertices += 2;
                    if (axis is Arc) ArcSegments++;
                }
                Extents3d e = axis.GeometricExtents;
                ExtentX = Math.Max(ExtentX, e.MaxPoint.X - e.MinPoint.X);
                ExtentY = Math.Max(ExtentY, e.MaxPoint.Y - e.MinPoint.Y);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // facts for the log only
            }
        }
    }

    /// <summary>
    /// Length of the route axis inside each picked parcel: the axis is split where it crosses a parcel boundary, and each piece
    /// goes to the parcel that contains its midpoint. Runs on the AutoCAD thread; every split piece is disposed.
    /// The plain-number parts are in <see cref="RouteLengths"/>.
    /// </summary>
    public static class RouteLengthCalculator
    {
        private const int ArcChords = 16;
        private const double BoxMarginM = GeometryTolerances.BoundingBoxMarginM;

        private sealed class ParcelRing
        {
            public string Id = string.Empty;
            public Polyline Polyline = null!;
            public BoundingBox? Extents;
            public List<(double X, double Y)> Ring = new List<(double X, double Y)>();
        }

        public static RouteLengthResult Compute(IEnumerable<Curve> axes, List<KeyValuePair<string, Polyline>> parcels, RouteLengthStats? stats = null)
        {
            stats ??= new RouteLengthStats();
            var result = new RouteLengthResult();
            var rings = parcels.Select(p => BuildRing(p.Key, p.Value)).ToList();

            foreach (Curve axis in axes)
            {
                stats.AddAxis(axis);
                List<double> parameters = SplitParameters(axis, rings, stats);

                if (parameters.Count == 0)
                {
                    Assign(result, axis, rings, stats); // the axis itself, not a copy: not disposed here
                    continue;
                }

                DBObjectCollection pieces;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    pieces = axis.GetSplitCurves(new DoubleCollection(parameters.ToArray()));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    Assign(result, axis, rings, stats);
                    continue;
                }
                finally
                {
                    stats.SplitCalls++;
                    stats.SplitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                }
                stats.SplitPieces += pieces.Count;

                foreach (DBObject piece in pieces)
                {
                    using (piece)
                    {
                        if (piece is Curve curve) Assign(result, curve, rings, stats);
                    }
                }
            }
            return result;
        }

        private static List<double> SplitParameters(Curve axis, List<ParcelRing> rings, RouteLengthStats stats)
        {
            BoundingBox? axisBox = BoxOf(axis);
            var parameters = new List<double>();
            foreach (ParcelRing ring in rings)
            {
                if (!BoundingBox.MayOverlap(axisBox, ring.Extents, BoxMarginM))
                {
                    stats.IntersectSkipped++;
                    continue;
                }

                using (var points = new Point3dCollection())
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        axis.IntersectWith(ring.Polyline, Intersect.OnBothOperands, new Plane(Point3d.Origin, Vector3d.ZAxis), points, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        continue;
                    }
                    finally
                    {
                        long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                        stats.IntersectCalls++;
                        stats.IntersectTicks += ticks;
                        stats.IntersectMaxTicks = Math.Max(stats.IntersectMaxTicks, ticks);
                    }
                    stats.IntersectPoints += points.Count;

                    long p0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    foreach (Point3d point in points)
                    {
                        try
                        {
                            parameters.Add(axis.GetParameterAtPoint(axis.GetClosestPointTo(point, false)));
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception)
                        {
                            // a point that cannot be put on the axis cannot split it
                        }
                    }
                    stats.ParameterTicks += System.Diagnostics.Stopwatch.GetTimestamp() - p0;
                }
            }
            return RouteLengths.SortSplitParameters(parameters, axis.StartParam, axis.EndParam);
        }

        /// <summary>Adds the length of a piece to the parcel that contains its midpoint, or to the outside total.</summary>
        private static void Assign(RouteLengthResult result, Curve piece, List<ParcelRing> rings, RouteLengthStats stats)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                AssignCore(result, piece, rings, stats);
            }
            finally
            {
                stats.AssignTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            }
        }

        private static void AssignCore(RouteLengthResult result, Curve piece, List<ParcelRing> rings, RouteLengthStats stats)
        {
            double length;
            Point3d middle;
            try
            {
                length = piece.GetDistanceAtParameter(piece.EndParam) - piece.GetDistanceAtParameter(piece.StartParam);
                middle = piece.GetPointAtDist(piece.GetDistanceAtParameter(piece.StartParam) + length / 2.0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return;
            }

            result.Pieces++;
            // the piece goes where its midpoint is: a parcel whose box does not hold that point cannot contain it
            var middleBox = new BoundingBox(middle.X, middle.Y, middle.X, middle.Y);
            foreach (ParcelRing ring in rings)
            {
                if (!BoundingBox.MayOverlap(middleBox, ring.Extents, 0.0))
                {
                    stats.ContainsSkipped++;
                    continue;
                }
                stats.ContainsTried++;
                if (!RouteLengths.PolygonContains(ring.Ring, middle.X, middle.Y)) continue;

                result.MetresByParcelId.TryGetValue(ring.Id, out double sum);
                result.MetresByParcelId[ring.Id] = sum + length;
                return;
            }
            result.OutsideMetres += length;
        }

        private static ParcelRing BuildRing(string id, Polyline polyline)
        {
            var ring = new ParcelRing { Id = id, Polyline = polyline, Extents = BoxOf(polyline) };
            int count = polyline.NumberOfVertices;
            for (int i = 0; i < count; i++)
            {
                Point2d p = polyline.GetPoint2dAt(i);
                ring.Ring.Add((p.X, p.Y));
                double bulge = polyline.GetBulgeAt(i);
                if (Math.Abs(bulge) > GeometryTolerances.BulgeEpsilon && (i < count - 1 || polyline.Closed))
                {
                    Point2d next = polyline.GetPoint2dAt((i + 1) % count);
                    ring.Ring.AddRange(RouteLengths.ArcPoints(p.X, p.Y, next.X, next.Y, bulge, ArcChords));
                }
            }
            return ring;
        }

        private static BoundingBox? BoxOf(Entity entity)
        {
            try
            {
                Extents3d e = entity.GeometricExtents;
                return new BoundingBox(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
            }
            catch
            {
                return null;
            }
        }
    }
}
