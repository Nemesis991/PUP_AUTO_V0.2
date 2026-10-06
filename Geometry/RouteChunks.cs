namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// A long route axis cut logically into chunks of consecutive vertices, so a parcel is intersected only with the chunks
    /// near it instead of the whole axis. Plain numbers, no AutoCAD types.
    /// </summary>
    public static class RouteChunks
    {
        /// <summary>
        /// Vertex ranges (first, last; inclusive) of an open polyline with <paramref name="vertexCount"/> vertices. Every segment
        /// is in exactly one chunk; neighbouring chunks share their end vertex. One range for the whole polyline when it has at
        /// most <paramref name="chunkSize"/> segments.
        /// </summary>
        public static List<(int First, int Last)> Ranges(int vertexCount, int chunkSize)
        {
            var ranges = new List<(int, int)>();
            if (vertexCount < 2) return ranges;
            if (chunkSize < 1) chunkSize = 1;

            for (int first = 0; first < vertexCount - 1; first += chunkSize)
            {
                ranges.Add((first, Math.Min(first + chunkSize, vertexCount - 1)));
            }
            return ranges;
        }

        /// <summary>The indexes of the chunks whose box may overlap <paramref name="target"/> (an unknown box always may).</summary>
        public static List<int> Candidates(IReadOnlyList<BoundingBox?> chunkBoxes, BoundingBox? target, double margin)
        {
            var candidates = new List<int>();
            for (int i = 0; i < chunkBoxes.Count; i++)
            {
                if (BoundingBox.MayOverlap(chunkBoxes[i], target, margin)) candidates.Add(i);
            }
            return candidates;
        }
    }
}
