namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// One point of a servitude edge as it comes out of the drawing: its position, the землища it belongs to (two at a
    /// землище boundary) and the direction of the edge there (radians, for the block rotation).
    /// </summary>
    public sealed class ServitudeEdgePointInput
    {
        /// <summary>Drawing X (east).</summary>
        public double X { get; set; }

        /// <summary>Drawing Y (north).</summary>
        public double Y { get; set; }

        /// <summary>The EKATTE codes this point is listed under; a boundary point has two.</summary>
        public List<string> Ekattes { get; } = new List<string>();

        /// <summary>The direction of travel along the edge at this point (radians).</summary>
        public double Direction { get; set; }

        /// <summary>True for a point inserted where the edge crosses a землище boundary (not an outline vertex).</summary>
        public bool IsBoundary { get; set; }

        /// <summary>Boundary points only: the distance (m) to the nearest outline vertex of the edge.</summary>
        public double NearestVertexM { get; set; }

        /// <summary>Boundary points only: how many raw parcel-boundary hits were merged into this point.</summary>
        public int MergedHits { get; set; }

        /// <summary>Boundary points only: the gap (or overlap) between the two землища here, in metres.</summary>
        public double SpanM { get; set; }

        /// <summary>Boundary points only: true when the span is an overlap of the two землища, false for a gap.</summary>
        public bool IsOverlap { get; set; }
    }

    /// <summary>A numbered point of one edge, ready for the table and for the drawing.</summary>
    public sealed class NumberedServitudePoint
    {
        public int Number { get; set; }

        /// <summary>Drawing X (east); printed as Y[m] (изток).</summary>
        public double X { get; set; }

        /// <summary>Drawing Y (north); printed as X[m] (север).</summary>
        public double Y { get; set; }

        public double Direction { get; set; }

        public bool IsLeft { get; set; }

        public bool IsBoundary { get; set; }

        /// <summary>Diagnostics of a boundary point, carried over from <see cref="ServitudeEdgePointInput"/>.</summary>
        public double NearestVertexM { get; set; }
        public int MergedHits { get; set; }
        public double SpanM { get; set; }
        public bool IsOverlap { get; set; }

        public List<string> Ekattes { get; } = new List<string>();
    }

    /// <summary>One row of the table: the left point and the right point that share it; either side may be empty.</summary>
    public sealed class ServitudeRegisterRow
    {
        public NumberedServitudePoint? Left { get; set; }
        public NumberedServitudePoint? Right { get; set; }
    }

    /// <summary>One землище section of the coordinate register of the servitude (official 08).</summary>
    public sealed class ServitudeRegister
    {
        /// <summary>"КООРДИНАТЕН РЕГИСТЪР НА СЕРВИТУТА НА &lt;обект&gt;".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The EKATTE title, the same one the other reports use.</summary>
        public string Subtitle { get; set; } = string.Empty;

        public List<ServitudeRegisterRow> Rows { get; } = new List<ServitudeRegisterRow>();

        public int LeftCount => Rows.Count(r => r.Left != null);
        public int RightCount => Rows.Count(r => r.Right != null);
    }

    /// <summary>
    /// Numbers the points of the two servitude edges and splits them into землище sections (official 08). Pure logic, no
    /// AutoCAD types.
    ///
    /// Left points are numbered from <c>startLeft</c> (5001 by default) and right points from <c>startRight</c> (1),
    /// consecutively along the route over the whole picked servitude. A point on a землище boundary keeps its single number
    /// and is listed in both sections, so the numbers after it shift by one. Inside one section the two lists are
    /// independent and are paired by row index; the shorter side leaves its cells empty.
    /// </summary>
    public static class ServitudeRegisterBuilder
    {
        public const string TitlePrefix = "КООРДИНАТЕН РЕГИСТЪР НА СЕРВИТУТА НА ";
        public const int DefaultLeftStart = 5001;
        public const int DefaultRightStart = 1;

        /// <summary>Consecutive points closer than this are one point (m).</summary>
        public const double MinPointSpacingM = 0.01;

        /// <summary>
        /// Safety net before numbering: of two consecutive points closer than <see cref="MinPointSpacingM"/> the first is kept
        /// and the second dropped, its землища added to the kept one so no section loses it. Done before the numbers are
        /// handed out, so the numbering stays continuous.
        /// </summary>
        public static List<ServitudeEdgePointInput> MergeCloseNeighbours(
            IEnumerable<ServitudeEdgePointInput> points, out int dropped)
        {
            var kept = new List<ServitudeEdgePointInput>();
            dropped = 0;
            foreach (ServitudeEdgePointInput point in points)
            {
                if (kept.Count > 0)
                {
                    ServitudeEdgePointInput last = kept[kept.Count - 1];
                    double dx = point.X - last.X, dy = point.Y - last.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < MinPointSpacingM)
                    {
                        foreach (string ekatte in point.Ekattes)
                        {
                            if (!last.Ekattes.Contains(ekatte, StringComparer.Ordinal)) last.Ekattes.Add(ekatte);
                        }
                        dropped++;
                        continue;
                    }
                }
                kept.Add(point);
            }
            return kept;
        }

        /// <summary>
        /// Numbers one edge's points like <see cref="Number"/>, but only from the first point inside the picked parcels to the
        /// last one (in route order). Points before the first and after the last do not use up numbers and are not returned, so the
        /// first included point gets <paramref name="start"/> whichever end of the route is counted from. A gap in the middle
        /// (the route leaves the picked parcels and comes back) still uses up its numbers: those points are returned with a number
        /// and no землище, and stay out of every section.
        /// </summary>
        /// <param name="outsideBefore">Points before the first included one, not numbered.</param>
        /// <param name="outsideAfter">Points after the last included one, not numbered.</param>
        public static List<NumberedServitudePoint> NumberInsidePickedParcels(
            IReadOnlyList<ServitudeEdgePointInput> points, int start, bool isLeft, out int outsideBefore, out int outsideAfter)
        {
            int first = -1, last = -1;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Ekattes.Count == 0) continue;
                if (first < 0) first = i;
                last = i;
            }

            if (first < 0)
            {
                outsideBefore = points.Count;
                outsideAfter = 0;
                return new List<NumberedServitudePoint>();
            }
            outsideBefore = first;
            outsideAfter = points.Count - 1 - last;
            return Number(points.Skip(first).Take(last - first + 1), start, isLeft);
        }

        /// <summary>Numbers one edge's points in order, from <paramref name="start"/>.</summary>
        public static List<NumberedServitudePoint> Number(
            IEnumerable<ServitudeEdgePointInput> points, int start, bool isLeft)
        {
            var numbered = new List<NumberedServitudePoint>();
            int next = start;
            foreach (ServitudeEdgePointInput point in points)
            {
                var item = new NumberedServitudePoint
                {
                    Number = next++,
                    X = point.X,
                    Y = point.Y,
                    Direction = point.Direction,
                    IsLeft = isLeft,
                    IsBoundary = point.IsBoundary,
                    NearestVertexM = point.NearestVertexM,
                    MergedHits = point.MergedHits,
                    SpanM = point.SpanM,
                    IsOverlap = point.IsOverlap
                };
                item.Ekattes.AddRange(point.Ekattes);
                numbered.Add(item);
            }
            return numbered;
        }

        /// <summary>
        /// One section: the numbered points of both edges that belong to <paramref name="ekatte"/>, paired by row index.
        /// </summary>
        public static ServitudeRegister Build(
            IReadOnlyList<NumberedServitudePoint> left,
            IReadOnlyList<NumberedServitudePoint> right,
            string ekatte,
            string projectName,
            string ekatteTitle)
        {
            var register = new ServitudeRegister
            {
                Title = TitlePrefix + projectName.Trim(),
                Subtitle = ekatteTitle
            };

            List<NumberedServitudePoint> mine = left.Where(p => p.Ekattes.Contains(ekatte, StringComparer.Ordinal)).ToList();
            List<NumberedServitudePoint> theirs = right.Where(p => p.Ekattes.Contains(ekatte, StringComparer.Ordinal)).ToList();

            for (int i = 0; i < Math.Max(mine.Count, theirs.Count); i++)
            {
                register.Rows.Add(new ServitudeRegisterRow
                {
                    Left = i < mine.Count ? mine[i] : null,
                    Right = i < theirs.Count ? theirs[i] : null
                });
            }
            return register;
        }

        /// <summary>
        /// How early along the route each землище is first met, as a fraction of each edge's length (0 at the start, 1 at the
        /// end). The two edges are numbered on different scales, so their positions are compared as fractions and the
        /// earliest of the two wins. Sections are ordered by this: a route that comes back to a землище keeps its first visit.
        /// </summary>
        public static Dictionary<string, double> RouteOrder(
            IReadOnlyList<NumberedServitudePoint> left, IReadOnlyList<NumberedServitudePoint> right)
        {
            var earliest = new Dictionary<string, double>(StringComparer.Ordinal);
            Collect(left);
            Collect(right);
            return earliest;

            void Collect(IReadOnlyList<NumberedServitudePoint> edge)
            {
                for (int i = 0; i < edge.Count; i++)
                {
                    double position = edge.Count <= 1 ? 0 : (double)i / (edge.Count - 1);
                    foreach (string ekatte in edge[i].Ekattes)
                    {
                        if (!earliest.TryGetValue(ekatte, out double known) || position < known) earliest[ekatte] = position;
                    }
                }
            }
        }

        /// <summary>The runs of consecutive numbers of one side in one section, as "5001–5018" texts, for the log.</summary>
        public static List<string> NumberRanges(IEnumerable<NumberedServitudePoint> points)
        {
            var ranges = new List<string>();
            int first = 0, previous = 0;
            bool open = false;
            foreach (NumberedServitudePoint point in points)
            {
                if (open && point.Number == previous + 1) { previous = point.Number; continue; }
                if (open) ranges.Add(Range(first, previous));
                first = previous = point.Number;
                open = true;
            }
            if (open) ranges.Add(Range(first, previous));
            return ranges;
        }

        private static string Range(int first, int last) => first == last ? first.ToString() : $"{first}–{last}";
    }
}
