namespace PUP_AUTO.Geometry
{
    public enum CornerLabelHorizontal { Left, Right }

    public enum CornerLabelVertical { Baseline, Top }

    /// <summary>Where the NOMER label of one corner block goes: justification and alignment point (drawing units).</summary>
    public readonly struct CornerLabelPlacement
    {
        public CornerLabelPlacement(string label, double cornerX, double cornerY, CornerLabelHorizontal horizontal,
            CornerLabelVertical vertical, double alignX, double alignY)
        {
            Label = label;
            CornerX = cornerX;
            CornerY = cornerY;
            Horizontal = horizontal;
            Vertical = vertical;
            AlignX = alignX;
            AlignY = alignY;
        }

        public string Label { get; }

        /// <summary>
        /// True for Left + Baseline: AutoCAD rejects <c>AlignmentPoint</c> there (eNotApplicable), the point goes in
        /// <c>Position</c>. For the other three justifications it goes in <c>AlignmentPoint</c>.
        /// </summary>
        public bool UsesPosition => Horizontal == CornerLabelHorizontal.Left && Vertical == CornerLabelVertical.Baseline;

        public double CornerX { get; }
        public double CornerY { get; }
        public CornerLabelHorizontal Horizontal { get; }
        public CornerLabelVertical Vertical { get; }
        public double AlignX { get; }
        public double AlignY { get; }
    }

    /// <summary>
    /// Places the corner labels of a pole footprint just outside the square, reading along the line (measured on the official
    /// drawing). In the block's frame (x along θ, y to its left) each corner's position relative to the footprint centre
    /// decides: forward end Left at local (+offset, 0), back end Right at (−offset, 0); left side Baseline, right side Top.
    /// Plain numbers, no AutoCAD types.
    /// </summary>
    public static class PoleCornerPlacement
    {
        /// <summary>The NOMER alignment point sits this many block units from the insertion point.</summary>
        public const double LabelOffsetBlockUnits = 2.0;

        /// <summary>The corner blocks are inserted at this uniform scale.</summary>
        public const double BlockScale = 2.0;

        /// <param name="corners">Clockwise corners (drawing X/Y), vertex 1 first.</param>
        /// <param name="rotation">θ in radians; null = the direction of the edge from the last corner to the first.</param>
        public static List<CornerLabelPlacement> Place(
            string poleNumber, IReadOnlyList<(double X, double Y)> corners, double? rotation, out double theta)
        {
            theta = rotation ?? EdgeDirection(corners);
            double ux = Math.Cos(theta), uy = Math.Sin(theta);
            double cx = corners.Average(c => c.X), cy = corners.Average(c => c.Y);
            double offset = LabelOffsetBlockUnits * BlockScale;

            var placements = new List<CornerLabelPlacement>();
            for (int i = 0; i < corners.Count; i++)
            {
                (double x, double y) = corners[i];
                double dx = x - cx, dy = y - cy;
                double u = dx * ux + dy * uy;      // along θ
                double v = -dx * uy + dy * ux;     // to the left of θ

                bool forward = u >= 0;
                double sign = forward ? 1 : -1;
                placements.Add(new CornerLabelPlacement(
                    $"{poleNumber}-{i + 1}", x, y,
                    forward ? CornerLabelHorizontal.Left : CornerLabelHorizontal.Right,
                    v > 0 ? CornerLabelVertical.Baseline : CornerLabelVertical.Top,
                    x + sign * offset * ux,
                    y + sign * offset * uy));
            }
            return placements;
        }

        /// <summary>
        /// The index of the corner that is forward-left of θ (u &gt; 0, v &gt; 0 in the frame of <see cref="Place"/>), or -1 when
        /// no corner is strictly there (e.g. a footprint turned exactly along θ).
        /// </summary>
        public static int ForwardLeftIndex(IReadOnlyList<(double X, double Y)> corners, double theta)
        {
            double ux = Math.Cos(theta), uy = Math.Sin(theta);
            double cx = corners.Average(c => c.X), cy = corners.Average(c => c.Y);
            for (int i = 0; i < corners.Count; i++)
            {
                double dx = corners[i].X - cx, dy = corners[i].Y - cy;
                if (dx * ux + dy * uy > 0 && -dx * uy + dy * ux > 0) return i;
            }
            return -1;
        }

        /// <summary>The direction (radians) of the edge from the last corner to the first.</summary>
        public static double EdgeDirection(IReadOnlyList<(double X, double Y)> corners)
        {
            (double X, double Y) last = corners[corners.Count - 1], first = corners[0];
            return Math.Atan2(first.Y - last.Y, first.X - last.X);
        }
    }
}
