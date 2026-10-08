namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// One polyline segment from (X1, Y1) to (X2, Y2) with a bulge (0 = straight; tan(¼ of the included angle), positive =
    /// counter-clockwise). Plain numbers, no AutoCAD types: the arc maths the servitude register needs.
    /// </summary>
    public readonly struct BulgeSegment
    {
        /// <summary>Below this |bulge| the segment is treated as a straight line.</summary>
        public const double StraightBulge = 1e-12;

        public BulgeSegment(double x1, double y1, double x2, double y2, double bulge)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Bulge = bulge;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double Bulge { get; }

        public bool IsArc => Math.Abs(Bulge) > StraightBulge && Chord > 0;

        public double Chord => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));

        /// <summary>The same segment walked the other way.</summary>
        public BulgeSegment Reversed() => new BulgeSegment(X2, Y2, X1, Y1, -Bulge);

        /// <summary>Included angle in radians, signed like the bulge.</summary>
        public double IncludedAngle => 4 * Math.Atan(Bulge);

        public double Radius => Chord / (2 * Math.Abs(Math.Sin(IncludedAngle / 2)));

        /// <summary>The arc centre (meaningless for a straight segment).</summary>
        public (double X, double Y) Centre
        {
            get
            {
                double chord = Chord;
                double mx = (X1 + X2) / 2, my = (Y1 + Y2) / 2;
                // distance from the chord midpoint to the centre, towards the left of the chord for a positive bulge
                double d = chord * (1 - Bulge * Bulge) / (4 * Bulge);
                double ux = (X2 - X1) / chord, uy = (Y2 - Y1) / chord;
                return (mx - uy * d, my + ux * d);
            }
        }

        public double Length => IsArc ? Radius * Math.Abs(IncludedAngle) : Chord;

        /// <summary>The point at fraction <paramref name="t"/> (0..1) of the segment's length.</summary>
        public (double X, double Y) PointAt(double t)
        {
            if (!IsArc) return (X1 + (X2 - X1) * t, Y1 + (Y2 - Y1) * t);
            (double cx, double cy) = Centre;
            double start = Math.Atan2(Y1 - cy, X1 - cx);
            double angle = start + IncludedAngle * t;
            double r = Radius;
            return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }

        /// <summary>
        /// Where a point on (or near) the segment sits, as a fraction of its length (0 at the start, 1 at the end); points off
        /// the segment are projected onto its line or circle and may give values outside 0..1.
        /// </summary>
        public double FractionOf(double x, double y)
        {
            if (!IsArc)
            {
                double dx = X2 - X1, dy = Y2 - Y1, len2 = dx * dx + dy * dy;
                return len2 == 0 ? 0 : ((x - X1) * dx + (y - Y1) * dy) / len2;
            }
            (double cx, double cy) = Centre;
            double start = Math.Atan2(Y1 - cy, X1 - cx);
            double swept = Math.Atan2(y - cy, x - cx) - start;
            double sweep = IncludedAngle;
            // bring the swept angle into the direction of travel, 0..2π
            if (sweep > 0) { while (swept < 0) swept += 2 * Math.PI; while (swept >= 2 * Math.PI) swept -= 2 * Math.PI; }
            else { while (swept > 0) swept -= 2 * Math.PI; while (swept <= -2 * Math.PI) swept += 2 * Math.PI; }
            return swept / sweep;
        }

        /// <summary>The direction of travel (radians) at fraction <paramref name="t"/>.</summary>
        public double DirectionAt(double t)
        {
            if (!IsArc) return Math.Atan2(Y2 - Y1, X2 - X1);
            (double cx, double cy) = Centre;
            (double px, double py) = PointAt(t);
            double radial = Math.Atan2(py - cy, px - cx);
            return Bulge > 0 ? radial + Math.PI / 2 : radial - Math.PI / 2;
        }

        /// <summary>Points along the segment, start included, end excluded; arcs are split so the sagitta stays below <paramref name="sagitta"/>.</summary>
        public IEnumerable<(double X, double Y)> Sample(double sagitta)
        {
            yield return (X1, Y1);
            if (!IsArc) yield break;

            double r = Radius;
            double maxStep = sagitta >= r ? Math.PI / 2 : 2 * Math.Acos(1 - sagitta / r);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(IncludedAngle) / Math.Max(maxStep, 1e-6)));
            steps = Math.Min(steps, 4096);
            for (int i = 1; i < steps; i++) yield return PointAt((double)i / steps);
        }
    }

    /// <summary>
    /// A closed polygon (drawing X/Y) with its bounding box, for point-in-polygon tests. Arcs of the source polyline are
    /// sampled with a small sagitta, so the answer can differ from the real boundary only within that distance.
    /// </summary>
    public sealed class PlanarPolygon
    {
        /// <summary>Arcs are sampled with at most this sagitta (m).</summary>
        public const double ArcSagittaM = 0.001;

        private readonly double[] _x;
        private readonly double[] _y;

        public PlanarPolygon(IReadOnlyList<(double X, double Y)> points)
        {
            _x = points.Select(p => p.X).ToArray();
            _y = points.Select(p => p.Y).ToArray();
            Box = points.Count == 0
                ? new BoundingBox(0, 0, 0, 0)
                : new BoundingBox(_x.Min(), _y.Min(), _x.Max(), _y.Max());
        }

        /// <summary>A polygon from polyline vertices (X, Y, bulge of the segment to the next vertex), always closed.</summary>
        public static PlanarPolygon FromBulgeVertices(IReadOnlyList<(double X, double Y, double Bulge)> vertices)
        {
            var points = new List<(double X, double Y)>();
            for (int i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                points.AddRange(new BulgeSegment(a.X, a.Y, b.X, b.Y, a.Bulge).Sample(ArcSagittaM));
            }
            return new PlanarPolygon(points);
        }

        public BoundingBox Box { get; }

        public int Count => _x.Length;

        /// <summary>
        /// The fractions (0..1, sorted) along the segment where it meets this polygon's boundary. Exact for a straight segment;
        /// an arc is met through its sampled chords. The pure counterpart of the AutoCAD intersection, for tests.
        /// </summary>
        public List<double> CutFractions(BulgeSegment segment)
        {
            var fractions = new List<double>();
            var chain = segment.Sample(ArcSagittaM).ToList();
            chain.Add((segment.X2, segment.Y2));
            for (int c = 0; c + 1 < chain.Count; c++)
            {
                (double ax, double ay) = chain[c];
                (double bx, double by) = chain[c + 1];
                double rx = bx - ax, ry = by - ay;
                for (int i = 0, j = _x.Length - 1; i < _x.Length; j = i++)
                {
                    double sx = _x[i] - _x[j], sy = _y[i] - _y[j];
                    double denominator = rx * sy - ry * sx;
                    if (Math.Abs(denominator) < 1e-15) continue;
                    double t = ((_x[j] - ax) * sy - (_y[j] - ay) * sx) / denominator;
                    double u = ((_x[j] - ax) * ry - (_y[j] - ay) * rx) / denominator;
                    if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                    fractions.Add(segment.FractionOf(ax + rx * t, ay + ry * t));
                }
            }
            fractions.Sort();
            return fractions.Where(f => f > 1e-6 && f < 1 - 1e-6).ToList();
        }

        /// <summary>The distance from the point to the polygon's boundary (0 on it); inside or outside alike.</summary>
        public double DistanceToBoundary(double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 0, j = _x.Length - 1; i < _x.Length; j = i++)
            {
                double dx = _x[i] - _x[j], dy = _y[i] - _y[j];
                double length2 = dx * dx + dy * dy;
                double t = length2 == 0 ? 0 : ((x - _x[j]) * dx + (y - _y[j]) * dy) / length2;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double px = _x[j] + dx * t - x, py = _y[j] + dy * t - y;
                best = Math.Min(best, Math.Sqrt(px * px + py * py));
            }
            return best;
        }

        /// <summary>Even-odd ray casting; a point exactly on the boundary may go either way.</summary>
        public bool Contains(double x, double y)
        {
            if (_x.Length < 3 || x < Box.MinX || x > Box.MaxX || y < Box.MinY || y > Box.MaxY) return false;
            bool inside = false;
            for (int i = 0, j = _x.Length - 1; i < _x.Length; j = i++)
            {
                if ((_y[i] > y) != (_y[j] > y) &&
                    x < (_x[j] - _x[i]) * (y - _y[i]) / (_y[j] - _y[i]) + _x[i])
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
