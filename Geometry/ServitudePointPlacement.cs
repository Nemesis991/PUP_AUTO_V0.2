namespace PUP_AUTO.Geometry
{
    /// <summary>Where the NOMER label of one servitude point block goes: justification, rotation and alignment point.</summary>
    public readonly struct ServitudeLabelPlacement
    {
        public ServitudeLabelPlacement(int number, double pointX, double pointY, double rotation, bool middleLeft,
            double alignX, double alignY)
        {
            Number = number;
            PointX = pointX;
            PointY = pointY;
            Rotation = rotation;
            MiddleLeft = middleLeft;
            AlignX = alignX;
            AlignY = alignY;
        }

        public int Number { get; }

        /// <summary>The insertion point: the servitude point itself (drawing X/Y).</summary>
        public double PointX { get; }
        public double PointY { get; }

        /// <summary>The block's rotation (radians), after any readability flip.</summary>
        public double Rotation { get; }

        /// <summary>True for MiddleLeft, false for MiddleRight.</summary>
        public bool MiddleLeft { get; }

        /// <summary>The attribute's alignment point in world coordinates.</summary>
        public double AlignX { get; }
        public double AlignY { get; }

        public string Label => Number.ToString();
    }

    /// <summary>
    /// Places the number label of a servitude point just outside the servitude, reading across the edge (measured on
    /// Simeon's sample: a left-edge point at θ = 339.36°, the text ending 3.40 m before the point).
    ///
    /// θ is perpendicular to the edge, pointing from the LEFT side towards the RIGHT side (the edge direction minus 90°),
    /// so on the left edge it points into the servitude and the label goes behind the point (MiddleRight at local −3.4),
    /// while on the right edge it points out of the servitude and the label goes ahead of it (MiddleLeft at local +3.4).
    /// A θ that would read upside down (90° &lt; θ &lt; 270°) is turned by 180°, and the side's offset and justification are
    /// swapped with it, so the text stays outside the servitude and still reads left to right.
    ///
    /// Plain numbers, no AutoCAD types.
    /// </summary>
    public static class ServitudePointPlacement
    {
        /// <summary>The alignment point sits this many metres from the insertion point (drawing units, scale 1).</summary>
        public const double LabelOffsetM = 3.4;

        /// <summary>θ of a point whose edge runs in <paramref name="edgeDirection"/> (radians), before the readability flip.</summary>
        public static double Theta(double edgeDirection) => Normalize(edgeDirection - Math.PI / 2);

        /// <summary>True when a label at this θ would read upside down and the block must be turned by 180°.</summary>
        public static bool NeedsFlip(double theta)
        {
            double normalized = Normalize(theta);
            return normalized > Math.PI / 2 && normalized < 3 * Math.PI / 2;
        }

        /// <summary>The placement of one point's label.</summary>
        /// <param name="edgeDirection">The direction of travel along the edge at the point (radians).</param>
        public static ServitudeLabelPlacement Place(int number, double x, double y, double edgeDirection, bool isLeft)
        {
            double theta = Theta(edgeDirection);

            // Outside the servitude: behind the point on the left edge, ahead of it on the right edge
            double offset = isLeft ? -LabelOffsetM : LabelOffsetM;
            bool middleLeft = !isLeft;

            if (NeedsFlip(theta))
            {
                theta = Normalize(theta + Math.PI);
                offset = -offset;           // the same side of the servitude, now measured along the turned θ
                middleLeft = !middleLeft;   // and the text still reads away from the point
            }

            return new ServitudeLabelPlacement(number, x, y, theta, middleLeft,
                x + offset * Math.Cos(theta), y + offset * Math.Sin(theta));
        }

        /// <summary>
        /// <see cref="Place"/>, then a check that the label anchor is OUTSIDE the servitude. If it falls inside (a sharp corner, a
        /// hairpin, a narrow strip), the label goes to the other side of the point instead: θ+180° with the justification and
        /// the offset swapped, which is the placement of the opposite edge. When that side is inside as well, the original stays.
        /// </summary>
        /// <param name="servitude">The servitude outline; null skips the check.</param>
        /// <param name="movedToOtherSide">True when the anchor was inside and the label was moved to the other side.</param>
        public static ServitudeLabelPlacement PlaceOutside(
            int number, double x, double y, double edgeDirection, bool isLeft, PlanarPolygon? servitude, out bool movedToOtherSide)
        {
            movedToOtherSide = false;
            ServitudeLabelPlacement placement = Place(number, x, y, edgeDirection, isLeft);
            if (servitude == null || !servitude.Contains(placement.AlignX, placement.AlignY)) return placement;

            ServitudeLabelPlacement other = Place(number, x, y, edgeDirection, !isLeft);
            if (servitude.Contains(other.AlignX, other.AlignY)) return placement;

            movedToOtherSide = true;
            return other;
        }

        /// <summary>
        /// <see cref="PlaceOutside"/> for a point of a walk that may have been reversed ("Обратна посока"). The label of a point must
        /// look the same whichever way the numbers run, so it is always worked out in the FORWARD orientation of the route: a
        /// reversed walk turns every edge direction by 180° and swaps left and right, which is undone here before the placement.
        /// Only the number differs between the two runs.
        /// </summary>
        /// <param name="walkDirection">The direction of travel along the edge in the walk that numbered the point (radians).</param>
        /// <param name="isLeftOfWalk">The side of that walk's direction of travel the point's edge is on.</param>
        /// <param name="routeReversed">True when that walk ran against the forward orientation of the route.</param>
        public static ServitudeLabelPlacement PlaceInForwardOrientation(
            int number, double x, double y, double walkDirection, bool isLeftOfWalk, bool routeReversed,
            PlanarPolygon? servitude, out bool movedToOtherSide)
        {
            if (routeReversed)
            {
                walkDirection = Normalize(walkDirection + Math.PI);
                isLeftOfWalk = !isLeftOfWalk;
            }
            return PlaceOutside(number, x, y, walkDirection, isLeftOfWalk, servitude, out movedToOtherSide);
        }

        /// <summary>An angle brought into [0, 2π).</summary>
        public static double Normalize(double radians)
        {
            double full = 2 * Math.PI;
            double value = radians % full;
            return value < 0 ? value + full : value;
        }
    }
}
