using PUP_AUTO.Geometry;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class RouteChunksTests
    {
        [Fact]
        public void Ranges_CoverEverySegmentOnce_AndShareEndVertices()
        {
            Assert.Equal(new[] { (0, 50), (50, 100), (100, 120) }, RouteChunks.Ranges(121, 50).ToArray());
            Assert.Equal(new[] { (0, 50), (50, 100) }, RouteChunks.Ranges(101, 50).ToArray());   // exactly two full chunks
            Assert.Equal(new[] { (0, 3) }, RouteChunks.Ranges(4, 50).ToArray());                // short: one chunk
            Assert.Empty(RouteChunks.Ranges(1, 50));

            List<(int First, int Last)> ranges = RouteChunks.Ranges(1001, 7);
            Assert.Equal(1000, ranges.Sum(r => r.Last - r.First));                                // 1000 segments, none twice
            for (int i = 1; i < ranges.Count; i++) Assert.Equal(ranges[i - 1].Last, ranges[i].First);
        }

        [Fact]
        public void Candidates_AreTheChunksNearTheTarget_AndAnUnknownBoxIsNeverSkipped()
        {
            var boxes = new List<BoundingBox?>
            {
                new BoundingBox(0, 0, 10, 1),
                new BoundingBox(10, 0, 20, 1),
                null,
                new BoundingBox(100, 0, 110, 1)
            };

            Assert.Equal(new[] { 1, 2 }, RouteChunks.Candidates(boxes, new BoundingBox(15, -5, 16, 5), 0.01).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, RouteChunks.Candidates(boxes, new BoundingBox(10, -5, 10, 5), 0.01).ToArray()); // shared vertex
            Assert.Equal(new[] { 0, 1, 2, 3 }, RouteChunks.Candidates(boxes, null, 0.01).ToArray());
        }

        // ---- the chunked search finds the same intersection points as the whole axis ----

        private static List<(double X, double Y)> Rectangle(double x0, double y0, double x1, double y1) =>
            new List<(double, double)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0) };

        private static BoundingBox Box(IReadOnlyList<(double X, double Y)> points) =>
            new BoundingBox(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));

        /// <summary>Crossing points of two polylines (straight segments), rounded so the two searches can be compared as sets.</summary>
        private static HashSet<(double, double)> Crossings(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
        {
            var points = new HashSet<(double, double)>();
            for (int i = 0; i < a.Count - 1; i++)
            {
                for (int j = 0; j < b.Count - 1; j++)
                {
                    (double px, double py) = a[i];
                    double rx = a[i + 1].X - px, ry = a[i + 1].Y - py;
                    (double qx, double qy) = b[j];
                    double sx = b[j + 1].X - qx, sy = b[j + 1].Y - qy;
                    double denominator = rx * sy - ry * sx;
                    if (denominator == 0) continue;
                    double t = ((qx - px) * sy - (qy - py) * sx) / denominator;
                    double u = ((qx - px) * ry - (qy - py) * rx) / denominator;
                    if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                    points.Add((Math.Round(px + t * rx, 9), Math.Round(py + t * ry, 9)));
                }
            }
            return points;
        }

        [Fact]
        public void ChunkedSearch_FindsTheSamePointsAsTheWholeAxis_ThreeCrossedAndTwoFarParcels()
        {
            // a zig-zag axis of 121 vertices from x = 0 to x = 600
            var axis = new List<(double X, double Y)>();
            for (int i = 0; i <= 120; i++) axis.Add((i * 5.0, i % 2 == 0 ? 0.0 : 3.0));

            var parcels = new List<List<(double X, double Y)>>
            {
                Rectangle(40, -10, 90, 10),        // crossed
                Rectangle(245, -10, 255, 10),      // crossed, around the chunk boundary at x = 250
                Rectangle(505, -10, 560, 10),      // crossed, only by the last chunk
                Rectangle(40, 500, 90, 520),       // far away
                Rectangle(5000, -10, 5100, 10)     // far away
            };

            List<(int First, int Last)> ranges = RouteChunks.Ranges(axis.Count, 50);
            var chunks = ranges.Select(r => axis.GetRange(r.First, r.Last - r.First + 1)).ToList();
            var chunkBoxes = chunks.Select(c => (BoundingBox?)Box(c)).ToList();
            BoundingBox axisBox = Box(axis);

            int calls = 0;
            foreach (List<(double X, double Y)> parcel in parcels)
            {
                HashSet<(double, double)> whole = Crossings(axis, parcel);

                var chunked = new HashSet<(double, double)>();
                if (BoundingBox.MayOverlap(axisBox, Box(parcel), 0.01))
                {
                    foreach (int i in RouteChunks.Candidates(chunkBoxes, Box(parcel), 0.01))
                    {
                        calls++;
                        chunked.UnionWith(Crossings(chunks[i], parcel));
                    }
                }

                Assert.Equal(whole.OrderBy(p => p).ToList(), chunked.OrderBy(p => p).ToList());
            }

            Assert.Equal(2, Crossings(axis, parcels[0]).Count);
            Assert.Equal(4, calls);   // one chunk each for the outer two crossed parcels, two at the chunk boundary, none for the far ones
        }
    }
}
