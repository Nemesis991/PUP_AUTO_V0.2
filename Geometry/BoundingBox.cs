namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// A 2D axis-aligned box (drawing units, meters) used as a cheap pre-check before an expensive region boolean:
    /// two shapes whose boxes are apart cannot overlap, so their intersection area is 0. Plain numbers, no AutoCAD types.
    /// </summary>
    public readonly struct BoundingBox
    {
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public BoundingBox(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>
        /// False only when the boxes are further apart than <paramref name="margin"/> on X or on Y, so touching or nearly
        /// touching boxes are still tested. A box that is not known (null) may overlap anything.
        /// </summary>
        public static bool MayOverlap(BoundingBox? a, BoundingBox? b, double margin)
        {
            if (!a.HasValue || !b.HasValue) return true;
            BoundingBox x = a.Value, y = b.Value;
            return x.MinX <= y.MaxX + margin && y.MinX <= x.MaxX + margin &&
                   x.MinY <= y.MaxY + margin && y.MinY <= x.MaxY + margin;
        }
    }
}
