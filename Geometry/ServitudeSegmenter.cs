using PUP_AUTO.Core;

namespace PUP_AUTO.Geometry
{
    /// <summary>One polyline vertex in its own (OCS) coordinates; <see cref="Bulge"/> describes the segment that starts here.</summary>
    public readonly struct PlineVertex
    {
        public PlineVertex(double x, double y, double bulge, bool isInserted = false)
        {
            X = x;
            Y = y;
            Bulge = bulge;
            IsInserted = isInserted;
        }

        public double X { get; }
        public double Y { get; }
        public double Bulge { get; }

        /// <summary>True for a vertex added on an arc, false for an original vertex.</summary>
        public bool IsInserted { get; }
    }

    /// <summary>
    /// PUP_SERV segmentation: keeps every original vertex and adds vertices only on arc segments, so the result lies exactly
    /// on the original polyline. Plain numbers, no AutoCAD types.
    /// </summary>
    public static class ServitudeSegmenter
    {
        /// <summary>An inserted vertex closer than this to the segment's end vertex is not added.</summary>
        private const double EndVertexToleranceM = 1e-6;

        /// <summary>A retraced segment's bulge must be the negated bulge of its mirror segment within this.</summary>
        private const double RetraceBulgeTolerance = 1e-6;

        /// <summary>
        /// Removes a retraced tail (and then head) of an open polyline: a run at the end that goes back over the previous
        /// vertices exactly (same points within <see cref="GeometryTolerances.RetraceToleranceM"/>, negated bulges), or the
        /// mirror case at the start. Only exact duplicates go, so nothing drawn once is lost. A retrace in the middle of the
        /// line and a closed polyline are left as they are.
        /// </summary>
        public static (List<PlineVertex> Vertices, int RemovedAtStart, int RemovedAtEnd) RemoveRetracedEnds(IReadOnlyList<PlineVertex> vertices, bool closed)
        {
            var list = vertices.ToList();
            if (closed || list.Count < 3) return (list, 0, 0);

            int removedAtEnd = RetracedTail(list);
            if (removedAtEnd > 0)
            {
                list.RemoveRange(list.Count - removedAtEnd, removedAtEnd);
                PlineVertex last = list[list.Count - 1];
                list[list.Count - 1] = new PlineVertex(last.X, last.Y, 0);   // no segment starts at the end of an open polyline
            }

            int removedAtStart = RetracedHead(list);
            if (removedAtStart > 0) list.RemoveRange(0, removedAtStart);

            return (list, removedAtStart, removedAtEnd);
        }

        /// <summary>The largest m such that vertex t+k mirrors t-k for k = 1..m, with t = n-1-m the turnaround (0 if none).</summary>
        private static int RetracedTail(IReadOnlyList<PlineVertex> v)
        {
            int n = v.Count;
            for (int m = (n - 1) / 2; m >= 1; m--)
            {
                int t = n - 1 - m;
                bool retraced = true;
                for (int k = 1; k <= m && retraced; k++)
                    retraced = Mirrors(v[t + k], v[t - k], v[t + k - 1].Bulge, v[t - k].Bulge);
                if (retraced) return m;
            }
            return 0;
        }

        /// <summary>The largest m such that vertex m-k mirrors m+k for k = 1..m, with m the turnaround (0 if none).</summary>
        private static int RetracedHead(IReadOnlyList<PlineVertex> v)
        {
            int n = v.Count;
            for (int m = (n - 1) / 2; m >= 1; m--)
            {
                bool retraced = true;
                for (int k = 1; k <= m && retraced; k++)
                    retraced = Mirrors(v[m - k], v[m + k], v[m - k].Bulge, v[m + k - 1].Bulge);
                if (retraced) return m;
            }
            return 0;
        }

        private static bool Mirrors(PlineVertex a, PlineVertex b, double bulgeA, double bulgeB)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy) <= GeometryTolerances.RetraceToleranceM
                && Math.Abs(bulgeA + bulgeB) < RetraceBulgeTolerance;
        }

        /// <summary>The total length of a polyline given as vertices (arc length for arcs).</summary>
        public static double Length(IReadOnlyList<PlineVertex> vertices, bool closed)
        {
            double sum = 0;
            int segments = closed ? vertices.Count : vertices.Count - 1;
            for (int i = 0; i < segments; i++) sum += SegmentLength(vertices[i], vertices[(i + 1) % vertices.Count]);
            return sum;
        }

        /// <summary>
        /// The segmented vertex list. Straight segments are copied as they are. An arc of length L gets one vertex at L/2
        /// when L &lt;= <paramref name="distance"/>, otherwise vertices at D, 2D, ... from its start, dropping the last one
        /// when the piece after it would be shorter than D/3 (and falling back to the midpoint when none is left).
        /// For a closed polyline the closing segment (last vertex to first) is segmented too.
        /// </summary>
        public static List<PlineVertex> Segment(IReadOnlyList<PlineVertex> vertices, bool closed, double distance)
        {
            var result = new List<PlineVertex>();
            int n = vertices.Count;
            if (n == 0) return result;

            int segmentCount = closed ? n : n - 1;
            for (int i = 0; i < n; i++)
            {
                PlineVertex start = vertices[i];
                if (i >= segmentCount)
                {
                    // Last vertex of an open polyline: no segment starts here
                    result.Add(new PlineVertex(start.X, start.Y, start.Bulge));
                    continue;
                }

                PlineVertex end = vertices[(i + 1) % n];
                if (IsStraight(start, end))
                {
                    result.Add(new PlineVertex(start.X, start.Y, start.Bulge));
                    continue;
                }

                double length = ArcLength(start, end);
                List<double> cuts = Cuts(length, distance);
                double theta = 4 * Math.Atan(start.Bulge);

                // Start vertex carries the first piece's bulge, each inserted vertex the bulge of the piece after it
                result.Add(new PlineVertex(start.X, start.Y, PieceBulge(theta, cuts[0], length)));
                for (int k = 0; k < cuts.Count; k++)
                {
                    (double x, double y) = PointOnArc(start, end, cuts[k] / length);
                    double next = k == cuts.Count - 1 ? length : cuts[k + 1];
                    result.Add(new PlineVertex(x, y, PieceBulge(theta, next - cuts[k], length), isInserted: true));
                }
            }
            return result;
        }

        /// <summary>
        /// True when the segment is drawn as a straight line: zero bulge, a zero-length chord, or a sagitta under
        /// <see cref="GeometryTolerances.ServStraightSagittaM"/>.
        /// </summary>
        public static bool IsStraight(PlineVertex start, PlineVertex end)
        {
            double bulge = Math.Abs(start.Bulge);
            if (bulge < GeometryTolerances.BulgeEpsilon) return true;
            double chord = Chord(start, end);
            if (chord == 0) return true;
            return bulge * chord / 2 < GeometryTolerances.ServStraightSagittaM;
        }

        /// <summary>The length of the segment from <paramref name="start"/> to <paramref name="end"/> (arc length for an arc).</summary>
        public static double SegmentLength(PlineVertex start, PlineVertex end)
        {
            double chord = Chord(start, end);
            double bulge = Math.Abs(start.Bulge);
            if (bulge < GeometryTolerances.BulgeEpsilon || chord == 0) return chord;
            return ArcLength(start, end);
        }

        /// <summary>The distances along an arc of <paramref name="length"/> at which vertices are inserted.</summary>
        public static List<double> Cuts(double length, double distance)
        {
            var cuts = new List<double>();
            if (length <= distance)
            {
                cuts.Add(length / 2);
                return cuts;
            }

            for (int k = 1; k * distance < length - EndVertexToleranceM; k++)
                cuts.Add(k * distance);

            // Too close to the end vertex: don't add the last one
            if (cuts.Count > 0 && length - cuts[cuts.Count - 1] < distance / 3)
                cuts.RemoveAt(cuts.Count - 1);

            if (cuts.Count == 0) cuts.Add(length / 2);
            return cuts;
        }

        /// <summary>
        /// The point at fraction <paramref name="t"/> (0..1) of the arc: the start vector rotated by t * theta around the
        /// centre. Computed relative to the start vertex so large coordinates keep their precision.
        /// </summary>
        public static (double X, double Y) PointOnArc(PlineVertex start, PlineVertex end, double t)
        {
            (double cx, double cy) = CenterRelative(start, end);
            double angle = 4 * Math.Atan(start.Bulge) * t;
            double vx = -cx, vy = -cy;                       // start vertex relative to the centre
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double rx = vx * cos - vy * sin;
            double ry = vx * sin + vy * cos;
            return (start.X + (cx + rx), start.Y + (cy + ry));
        }

        /// <summary>The arc's centre in absolute coordinates and its radius.</summary>
        public static (double X, double Y, double Radius) Circle(PlineVertex start, PlineVertex end)
        {
            (double cx, double cy) = CenterRelative(start, end);
            return (start.X + cx, start.Y + cy, Radius(start, end));
        }

        private static double ArcLength(PlineVertex start, PlineVertex end)
            => Radius(start, end) * Math.Abs(4 * Math.Atan(start.Bulge));

        private static double Radius(PlineVertex start, PlineVertex end)
        {
            double bulge = Math.Abs(start.Bulge);
            return Chord(start, end) * (1 + bulge * bulge) / (4 * bulge);
        }

        /// <summary>The centre relative to the start vertex: chord midpoint moved along the chord's left normal.</summary>
        private static (double X, double Y) CenterRelative(PlineVertex start, PlineVertex end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            double b = start.Bulge;
            double offset = chord * (1 - b * b) / (4 * b);   // signed distance from the chord midpoint
            return (dx / 2 - dy / chord * offset, dy / 2 + dx / chord * offset);
        }

        private static double Chord(PlineVertex start, PlineVertex end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PieceBulge(double theta, double pieceLength, double length)
            => Math.Tan(theta * pieceLength / length / 4);
    }
}
