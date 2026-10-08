namespace PUP_AUTO.CadRegister
{
    /// <summary>One vertex of the servitude outline: drawing X/Y and the bulge of the segment that leaves it.</summary>
    public readonly struct EdgeVertex
    {
        public EdgeVertex(double x, double y, double bulge)
        {
            X = x;
            Y = y;
            Bulge = bulge;
        }

        public double X { get; }
        public double Y { get; }

        /// <summary>Bulge of the segment to the NEXT vertex of the same edge; unused on the last one.</summary>
        public double Bulge { get; }
    }

    /// <summary>One side of the servitude, ordered along the route (increasing station on the axis).</summary>
    public sealed class ServitudeEdge
    {
        public ServitudeEdge(IReadOnlyList<EdgeVertex> vertices, bool isLeft)
        {
            Vertices = vertices;
            IsLeft = isLeft;
        }

        public IReadOnlyList<EdgeVertex> Vertices { get; }

        /// <summary>True for the edge left of the route direction (counter-clockwise of it).</summary>
        public bool IsLeft { get; }
    }

    /// <summary>The two edges, or the reason the outline could not be split.</summary>
    public sealed class ServitudeEdgeResult
    {
        public ServitudeEdge? Left { get; set; }
        public ServitudeEdge? Right { get; set; }

        /// <summary>Set when the outline does not split into exactly one left and one right run; counts only, no coordinates.</summary>
        public string? Error { get; set; }

        /// <summary>True when the axis was reversed to run from the lowest-numbered pole towards the highest.</summary>
        public bool AxisReversed { get; set; }

        /// <summary>True when the route was reversed on request, after the normal orientation ("Обратна посока").</summary>
        public bool RouteReversedByRequest { get; set; }

        /// <summary>Consecutive duplicate vertices dropped from the outline.</summary>
        public int DuplicatesDropped { get; set; }

        public bool Ok => Error == null && Left != null && Right != null;
    }

    /// <summary>
    /// Splits the picked servitude outline into its LEFT and RIGHT edge relative to the route direction. Pure maths, no
    /// AutoCAD types: the outline comes in as vertices with bulges, the axis as a point list (arcs already sampled) and the
    /// poles as numbered points that only orient the axis.
    ///
    /// Each outline vertex gets a station along the axis (its closest point) and a side (the sign of the cross product with
    /// the axis tangent there). Walking the closed loop, the vertices form one run on the left and one on the right,
    /// separated by the two end caps; the caps themselves are not numbered. Each run is then ordered by increasing station.
    /// </summary>
    public static class ServitudeEdges
    {
        /// <summary>A vertex closer than this to the axis counts as neither left nor right (a cap corner).</summary>
        public const double SideToleranceM = 1e-6;

        /// <summary>Consecutive vertices closer than this are one vertex.</summary>
        public const double DuplicateToleranceM = 1e-9;

        /// <param name="outline">The servitude vertices in drawing order, with the bulge of each segment.</param>
        /// <param name="closed">True when the polyline is closed; an open one is closed from the last vertex to the first.</param>
        /// <param name="axis">The route axis as a point list, in its own drawing direction.</param>
        /// <param name="poles">Pole number (any text; numeric ones compare numerically) and position; empty keeps the axis direction.</param>
        /// <param name="reverseRoute">
        /// Counts from the other end: the oriented axis is reversed once more. "Left" and "right" stay relative to the direction
        /// of travel, so the two edges swap sides and the former right edge is numbered from the left start.
        /// </param>
        public static ServitudeEdgeResult Split(
            IReadOnlyList<EdgeVertex> outline,
            bool closed,
            IReadOnlyList<(double X, double Y)> axis,
            IReadOnlyList<(string Number, double X, double Y)> poles,
            bool reverseRoute = false)
        {
            var result = new ServitudeEdgeResult();
            if (axis.Count < 2)
            {
                result.Error = "оста на трасето има по-малко от 2 точки";
                return result;
            }

            List<EdgeVertex> loop = Deduplicate(outline, closed, out int dropped);
            result.DuplicatesDropped = dropped;
            if (loop.Count < 4)
            {
                result.Error = $"сервитутът има само {loop.Count} различни възела";
                return result;
            }

            List<(double X, double Y)> line = Orient(axis, poles, out bool reversed);
            result.AxisReversed = reversed;
            if (reverseRoute)
            {
                line.Reverse();
                result.RouteReversedByRequest = true;
            }

            var stations = new double[loop.Count];
            var sides = new double[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                (stations[i], sides[i]) = Project(line, loop[i].X, loop[i].Y);
            }

            List<List<int>> runs = Runs(sides);
            List<List<int>> left = runs.Where(r => sides[r[0]] > 0).ToList();
            List<List<int>> right = runs.Where(r => sides[r[0]] < 0).ToList();
            if (left.Count != 1 || right.Count != 1)
            {
                result.Error = $"сервитутът не се разделя на една лява и една дясна страна " +
                               $"(намерени са {left.Count} леви и {right.Count} десни участъка от {runs.Count}) — " +
                               "проверете дали оста минава през целия сервитут";
                return result;
            }

            result.Left = BuildEdge(loop, left[0], stations, true);
            result.Right = BuildEdge(loop, right[0], stations, false);
            return result;
        }

        /// <summary>The loop without consecutive duplicates (and without a closing vertex equal to the first).</summary>
        public static List<EdgeVertex> Deduplicate(IReadOnlyList<EdgeVertex> outline, bool closed, out int dropped)
        {
            var loop = new List<EdgeVertex>(outline.Count);
            foreach (EdgeVertex vertex in outline)
            {
                if (loop.Count > 0 && Near(loop[loop.Count - 1], vertex)) continue;
                loop.Add(vertex);
            }
            // An open polyline that comes back to its start, or a closed one drawn with the first point repeated
            while (loop.Count > 1 && Near(loop[0], loop[loop.Count - 1])) loop.RemoveAt(loop.Count - 1);
            dropped = outline.Count - loop.Count;
            _ = closed;     // the loop is closed either way; the flag only documents the source
            return loop;
        }

        private static bool Near(EdgeVertex a, EdgeVertex b) =>
            Math.Abs(a.X - b.X) <= DuplicateToleranceM && Math.Abs(a.Y - b.Y) <= DuplicateToleranceM;

        /// <summary>
        /// The axis running from the lowest-numbered pole towards the highest: reversed when its start is nearer the highest
        /// one. Without poles (or with only one) the drawing direction is kept.
        /// </summary>
        public static List<(double X, double Y)> Orient(
            IReadOnlyList<(double X, double Y)> axis,
            IReadOnlyList<(string Number, double X, double Y)> poles,
            out bool reversed)
        {
            reversed = false;
            var line = axis.ToList();
            if (poles.Count < 2) return line;

            var sorted = poles.OrderBy(p => p.Number, Comparer<string>.Create(Semantics.PoleStepsTableBuilder.ComparePoleNumbers)).ToList();
            (string _, double fx, double fy) = sorted[0];
            (string _, double lx, double ly) = sorted[sorted.Count - 1];

            (double X, double Y) start = line[0];
            double toFirst = Distance(start.X, start.Y, fx, fy);
            double toLast = Distance(start.X, start.Y, lx, ly);
            if (toLast < toFirst)
            {
                line.Reverse();
                reversed = true;
            }
            return line;
        }

        private static double Distance(double x1, double y1, double x2, double y2) =>
            Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

        /// <summary>
        /// The station (distance along the axis) and the signed offset of the point: positive to the LEFT of the axis
        /// direction. Measured at the closest point of the axis.
        /// </summary>
        public static (double Station, double Side) Project(IReadOnlyList<(double X, double Y)> axis, double x, double y)
        {
            double best = double.MaxValue, bestStation = 0, bestSide = 0, travelled = 0;
            for (int i = 0; i + 1 < axis.Count; i++)
            {
                (double ax, double ay) = axis[i];
                (double bx, double by) = axis[i + 1];
                double dx = bx - ax, dy = by - ay;
                double length2 = dx * dx + dy * dy;
                double length = Math.Sqrt(length2);
                if (length2 > 0)
                {
                    double t = ((x - ax) * dx + (y - ay) * dy) / length2;
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    double px = ax + dx * t, py = ay + dy * t;
                    double distance = Distance(x, y, px, py);
                    if (distance < best)
                    {
                        best = distance;
                        bestStation = travelled + length * t;
                        // cross product of the segment direction with the vector to the point: > 0 is to its left
                        bestSide = (dx * (y - ay) - dy * (x - ax)) / length;
                    }
                }
                travelled += length;
            }
            return (bestStation, Math.Abs(bestSide) <= SideToleranceM ? 0 : bestSide);
        }

        /// <summary>
        /// The maximal runs of same-signed vertices around the closed loop; zero-sided vertices (cap corners) separate runs
        /// and belong to none. A loop that is all one sign gives one run.
        /// </summary>
        private static List<List<int>> Runs(double[] sides)
        {
            int count = sides.Length;
            var runs = new List<List<int>>();

            // Start where the sign changes, so a run that wraps around index 0 stays in one piece
            int start = -1;
            for (int i = 0; i < count; i++)
            {
                if (Sign(sides[i]) != Sign(sides[(i + count - 1) % count])) { start = i; break; }
            }
            if (start < 0)
            {
                // every vertex has the same sign
                if (Sign(sides[0]) != 0) runs.Add(Enumerable.Range(0, count).ToList());
                return runs;
            }

            List<int>? current = null;
            int currentSign = 0;
            for (int step = 0; step < count; step++)
            {
                int i = (start + step) % count;
                int sign = Sign(sides[i]);
                if (sign == 0) { current = null; currentSign = 0; continue; }
                if (current == null || sign != currentSign)
                {
                    current = new List<int>();
                    currentSign = sign;
                    runs.Add(current);
                }
                current.Add(i);
            }
            return runs;
        }

        private static int Sign(double side) => side > 0 ? 1 : side < 0 ? -1 : 0;

        /// <summary>The run as an edge, ordered by increasing station (the vertex list is reversed when it runs backwards).</summary>
        private static ServitudeEdge BuildEdge(List<EdgeVertex> loop, List<int> run, double[] stations, bool isLeft)
        {
            var vertices = run.Select(i => loop[i]).ToList();

            // The bulge of a vertex belongs to the segment towards the NEXT vertex of the run; the last one has no segment
            var bulges = new List<double>(vertices.Count);
            for (int k = 0; k < run.Count; k++)
            {
                bool followedInRun = k + 1 < run.Count && run[k + 1] == (run[k] + 1) % loop.Count;
                bulges.Add(followedInRun ? loop[run[k]].Bulge : 0);
            }

            bool backwards = stations[run[run.Count - 1]] < stations[run[0]];
            if (backwards)
            {
                var flipped = new List<EdgeVertex>(vertices.Count);
                for (int k = vertices.Count - 1; k >= 0; k--)
                {
                    // walking the other way: the segment that leaves this vertex is the reverse of the one before it
                    double bulge = k > 0 ? -bulges[k - 1] : 0;
                    flipped.Add(new EdgeVertex(vertices[k].X, vertices[k].Y, bulge));
                }
                return new ServitudeEdge(flipped, isLeft);
            }

            var ordered = new List<EdgeVertex>(vertices.Count);
            for (int k = 0; k < vertices.Count; k++) ordered.Add(new EdgeVertex(vertices[k].X, vertices[k].Y, bulges[k]));
            return new ServitudeEdge(ordered, isLeft);
        }
    }
}
