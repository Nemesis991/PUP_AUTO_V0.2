using PUP_AUTO.Geometry;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class ServitudeSegmenterTests
    {
        private const double D = 20.0;

        /// <summary>An open one-arc polyline on the circle (cx, cy, r) from angle a0, sweeping <paramref name="sweep"/> (signed).</summary>
        private static List<PlineVertex> Arc(double cx, double cy, double r, double a0, double sweep)
        {
            return new List<PlineVertex>
            {
                new PlineVertex(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0), Math.Tan(sweep / 4)),
                new PlineVertex(cx + r * Math.Cos(a0 + sweep), cy + r * Math.Sin(a0 + sweep), 0)
            };
        }

        /// <summary>A counter-clockwise arc of the given length on a circle of radius 30 around (cx, cy), starting at angle 0.3.</summary>
        private static List<PlineVertex> ArcOfLength(double length, double cx = 0, double cy = 0)
            => Arc(cx, cy, 30, 0.3, length / 30);

        private static (double X, double Y) OnCircle(double cx, double cy, double r, double angle)
            => (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));

        private static double TotalLength(IReadOnlyList<PlineVertex> v, bool closed)
        {
            double sum = 0;
            int segments = closed ? v.Count : v.Count - 1;
            for (int i = 0; i < segments; i++) sum += ServitudeSegmenter.SegmentLength(v[i], v[(i + 1) % v.Count]);
            return sum;
        }

        private static List<PlineVertex> Inserted(IEnumerable<PlineVertex> v) => v.Where(p => p.IsInserted).ToList();

        private static void AssertOriginalsKept(IReadOnlyList<PlineVertex> source, IReadOnlyList<PlineVertex> result)
        {
            List<PlineVertex> originals = result.Where(p => !p.IsInserted).ToList();
            Assert.Equal(source.Count, originals.Count);
            for (int i = 0; i < source.Count; i++)
            {
                Assert.Equal(source[i].X, originals[i].X);
                Assert.Equal(source[i].Y, originals[i].Y);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StraightOnly_OutputEqualsInput(bool closed)
        {
            var square = new List<PlineVertex>
            {
                new PlineVertex(0, 0, 0), new PlineVertex(100, 0, 0), new PlineVertex(100, 100, 0), new PlineVertex(0, 100, 0)
            };

            List<PlineVertex> result = ServitudeSegmenter.Segment(square, closed, D);

            Assert.Equal(square, result);
        }

        [Fact]
        public void Arc50_GetsVerticesAt20And40()
        {
            List<PlineVertex> arc = ArcOfLength(50);

            List<PlineVertex> result = ServitudeSegmenter.Segment(arc, false, D);

            List<PlineVertex> inserted = Inserted(result);
            Assert.Equal(2, inserted.Count);
            AssertAt(inserted[0], 0.3 + 20.0 / 30);
            AssertAt(inserted[1], 0.3 + 40.0 / 30);
            AssertOriginalsKept(arc, result);
        }

        [Fact]
        public void Arc45_DropsTheVertexTooCloseToTheEnd()
        {
            List<PlineVertex> inserted = Inserted(ServitudeSegmenter.Segment(ArcOfLength(45), false, D));

            Assert.Single(inserted);
            AssertAt(inserted[0], 0.3 + 20.0 / 30);
        }

        [Theory]
        [InlineData(15)]   // shorter than D
        [InlineData(20)]   // exactly D
        [InlineData(22)]   // 20 would leave 2 m < D/3
        public void ShortArc_GetsOneVertexAtItsMiddle(double length)
        {
            List<PlineVertex> inserted = Inserted(ServitudeSegmenter.Segment(ArcOfLength(length), false, D));

            Assert.Single(inserted);
            AssertAt(inserted[0], 0.3 + length / 2 / 30);
        }

        private static void AssertAt(PlineVertex p, double angle)
        {
            (double x, double y) = OnCircle(0, 0, 30, angle);
            Assert.Equal(x, p.X, 9);
            Assert.Equal(y, p.Y, 9);
        }

        [Theory]
        [InlineData(-0.8 * Math.PI)]   // negative bulge
        [InlineData(1.5 * Math.PI)]    // bulge > 1: more than half a circle
        [InlineData(-1.7 * Math.PI)]
        public void InsertedPoints_AreOnTheCircle_InSweepOrder_AndEveryPieceStaysOnIt(double sweep)
        {
            const double cx = 12, cy = -7, r = 30, a0 = 1.1;
            List<PlineVertex> arc = Arc(cx, cy, r, a0, sweep);

            List<PlineVertex> result = ServitudeSegmenter.Segment(arc, false, D);

            Assert.True(Inserted(result).Count > 1);
            double previous = 0;
            for (int i = 1; i < result.Count - 1; i++)
            {
                PlineVertex p = result[i];
                Assert.Equal(r, Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)), 9);

                // Angle travelled from the start in the sweep direction, unwrapped to (0, |sweep|)
                double angle = Math.Atan2(p.Y - cy, p.X - cx) - a0;
                double travelled = Math.Sign(sweep) * angle;
                travelled = ((travelled % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                Assert.True(travelled > previous && travelled < Math.Abs(sweep), $"vertex {i} out of order");
                previous = travelled;
            }

            // Each piece's own bulge describes the original circle
            for (int i = 0; i < result.Count - 1; i++)
            {
                (double px, double py, double pr) = ServitudeSegmenter.Circle(result[i], result[i + 1]);
                Assert.Equal(cx, px, 9);
                Assert.Equal(cy, py, 9);
                Assert.Equal(r, pr, 9);
                Assert.Equal(Math.Sign(sweep), Math.Sign(result[i].Bulge));
            }
        }

        [Fact]
        public void TotalLength_IsUnchanged()
        {
            var pline = new List<PlineVertex>
            {
                new PlineVertex(0, 0, 0),
                new PlineVertex(100, 0, 0.4),
                new PlineVertex(150, 80, -1.3),
                new PlineVertex(160, 90, 0.05),
                new PlineVertex(260, 90, 0)
            };

            foreach (bool closed in new[] { false, true })
            {
                List<PlineVertex> result = ServitudeSegmenter.Segment(pline, closed, D);
                Assert.True(Inserted(result).Count > 0);
                Assert.Equal(TotalLength(pline, closed), TotalLength(result, closed), 9);
                AssertOriginalsKept(pline, result);
            }
        }

        [Fact]
        public void NearlyStraightArc_GetsNoVertex()
        {
            // chord 100 m: sagitta = |bulge| * 50
            var flat = new List<PlineVertex> { new PlineVertex(0, 0, 0.0009 / 50), new PlineVertex(100, 0, 0) };
            var curved = new List<PlineVertex> { new PlineVertex(0, 0, 0.002 / 50), new PlineVertex(100, 0, 0) };

            Assert.Equal(flat, ServitudeSegmenter.Segment(flat, false, D));
            Assert.NotEmpty(Inserted(ServitudeSegmenter.Segment(curved, false, D)));
        }

        [Fact]
        public void ClosedPolyline_SegmentsTheClosingArc_WithoutDuplicatingTheFirstVertex()
        {
            // Square-ish outline whose closing edge (0,100) -> (0,0) is a half circle
            var pline = new List<PlineVertex>
            {
                new PlineVertex(0, 0, 0), new PlineVertex(100, 0, 0), new PlineVertex(100, 100, 0), new PlineVertex(0, 100, 1)
            };

            List<PlineVertex> result = ServitudeSegmenter.Segment(pline, true, D);

            // Half circle of radius 50: L = 157.08 -> vertices at 20..140, the one at 140 leaves 17.08 >= 6.67
            Assert.Equal(7, Inserted(result).Count);
            Assert.Equal(pline[0], result[0]);
            Assert.False(result[result.Count - 1].X == 0 && result[result.Count - 1].Y == 0);
            Assert.True(result[result.Count - 1].IsInserted);
            Assert.Equal(1, result.Count(p => p.X == 0 && p.Y == 0));
            Assert.Equal(TotalLength(pline, true), TotalLength(result, true), 9);
        }

        [Fact]
        public void LargeCoordinates_GiveTheSameResultAsNearTheOrigin()
        {
            const double ox = 8_500_000.123, oy = 4_700_000.456;
            List<PlineVertex> near = ServitudeSegmenter.Segment(ArcOfLength(95), false, D);
            List<PlineVertex> far = ServitudeSegmenter.Segment(ArcOfLength(95, ox, oy), false, D);

            Assert.Equal(near.Count, far.Count);
            for (int i = 0; i < near.Count; i++)
            {
                Assert.Equal(near[i].X, far[i].X - ox, 6);
                Assert.Equal(near[i].Y, far[i].Y - oy, 6);
                Assert.Equal(near[i].Bulge, far[i].Bulge, 6);
            }
        }

        [Fact]
        public void VerticesOneCentimetreApart_AreBothKept()
        {
            var pline = new List<PlineVertex>
            {
                new PlineVertex(0, 0, 0), new PlineVertex(0.01, 0, 0), new PlineVertex(0.01, 0, 0.5), new PlineVertex(60, 30, 0)
            };

            List<PlineVertex> result = ServitudeSegmenter.Segment(pline, false, D);

            AssertOriginalsKept(pline, result);   // including the zero-length segment
        }
    }
}
