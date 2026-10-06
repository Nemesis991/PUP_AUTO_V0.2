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

    /// <summary>
    /// Length of the route axis inside each picked parcel: the axis is split where it crosses a parcel boundary, and each piece
    /// goes to the parcel that contains its midpoint. Runs on the AutoCAD thread; every split piece is disposed.
    /// The plain-number parts are in <see cref="RouteLengths"/>.
    /// </summary>
    public static class RouteLengthCalculator
    {
        private const int ArcChords = 16;
        private const double BoxMarginM = 0.01;

        private readonly struct Box
        {
            public readonly double MinX, MinY, MaxX, MaxY;

            public Box(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }

        /// <summary>False only when both boxes are known and further apart than the margin; an unknown box may overlap anything.</summary>
        private static bool MayOverlap(Box? a, Box? b, double margin)
        {
            if (!a.HasValue || !b.HasValue) return true;
            Box x = a.Value, y = b.Value;
            return x.MinX <= y.MaxX + margin && y.MinX <= x.MaxX + margin &&
                   x.MinY <= y.MaxY + margin && y.MinY <= x.MaxY + margin;
        }

        private sealed class ParcelRing
        {
            public string Id = string.Empty;
            public Polyline Polyline = null!;
            public Box? Extents;
            public List<(double X, double Y)> Ring = new List<(double X, double Y)>();
        }

        public static RouteLengthResult Compute(IEnumerable<Curve> axes, List<KeyValuePair<string, Polyline>> parcels)
        {
            var result = new RouteLengthResult();
            var rings = parcels.Select(p => BuildRing(p.Key, p.Value)).ToList();

            foreach (Curve axis in axes)
            {
                List<double> parameters = SplitParameters(axis, rings);

                if (parameters.Count == 0)
                {
                    Assign(result, axis, rings); // the axis itself, not a copy: not disposed here
                    continue;
                }

                DBObjectCollection pieces;
                try
                {
                    pieces = axis.GetSplitCurves(new DoubleCollection(parameters.ToArray()));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    Assign(result, axis, rings);
                    continue;
                }

                foreach (DBObject piece in pieces)
                {
                    using (piece)
                    {
                        if (piece is Curve curve) Assign(result, curve, rings);
                    }
                }
            }
            return result;
        }

        private static List<double> SplitParameters(Curve axis, List<ParcelRing> rings)
        {
            Box? axisBox = BoxOf(axis);
            var parameters = new List<double>();
            foreach (ParcelRing ring in rings)
            {
                if (!MayOverlap(axisBox, ring.Extents, BoxMarginM)) continue;

                using (var points = new Point3dCollection())
                {
                    try
                    {
                        axis.IntersectWith(ring.Polyline, Intersect.OnBothOperands, new Plane(Point3d.Origin, Vector3d.ZAxis), points, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        continue;
                    }

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
                }
            }
            return RouteLengths.SortSplitParameters(parameters, axis.StartParam, axis.EndParam);
        }

        /// <summary>Adds the length of a piece to the parcel that contains its midpoint, or to the outside total.</summary>
        private static void Assign(RouteLengthResult result, Curve piece, List<ParcelRing> rings)
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
            foreach (ParcelRing ring in rings)
            {
                if (!MayOverlap(new Box(middle.X, middle.Y, middle.X, middle.Y), ring.Extents, 0.0)) continue;
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

        private static Box? BoxOf(Entity entity)
        {
            try
            {
                Extents3d e = entity.GeometricExtents;
                return new Box(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
            }
            catch
            {
                return null;
            }
        }
    }
}
