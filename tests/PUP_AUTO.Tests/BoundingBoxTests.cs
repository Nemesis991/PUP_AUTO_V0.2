using PUP_AUTO.Geometry;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class BoundingBoxTests
    {
        private const double Margin = 0.01;
        private static BoundingBox Box(double x1, double y1, double x2, double y2) => new BoundingBox(x1, y1, x2, y2);

        [Fact]
        public void DisjointBoxes_DoNotOverlap_OnEitherAxis()
        {
            Assert.False(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(20, 0, 30, 10), Margin));   // apart on X
            Assert.False(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(0, 20, 10, 30), Margin));   // apart on Y
            Assert.False(BoundingBox.MayOverlap(Box(20, 20, 30, 30), Box(0, 0, 10, 10), Margin));  // symmetric
        }

        [Fact]
        public void BoxesApartByLessThanTheMargin_AreStillTested()
        {
            Assert.True(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(10.005, 0, 20, 10), Margin));
            Assert.False(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(10.02, 0, 20, 10), Margin));
        }

        [Fact]
        public void TouchingBoxes_AreTested_EdgeAndCorner()
        {
            Assert.True(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(10, 0, 20, 10), Margin));    // shared edge
            Assert.True(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(10, 10, 20, 20), Margin));   // shared corner
        }

        [Fact]
        public void OverlappingAndContainedBoxes_AreTested()
        {
            Assert.True(BoundingBox.MayOverlap(Box(0, 0, 10, 10), Box(5, 5, 15, 15), Margin));
            Assert.True(BoundingBox.MayOverlap(Box(0, 0, 100, 100), Box(40, 40, 41, 41), Margin)); // contained
            Assert.True(BoundingBox.MayOverlap(Box(40, 40, 41, 41), Box(0, 0, 100, 100), Margin));
        }

        [Fact]
        public void UnknownBox_IsNeverSkipped()
        {
            Assert.True(BoundingBox.MayOverlap(null, Box(0, 0, 1, 1), Margin));
            Assert.True(BoundingBox.MayOverlap(Box(0, 0, 1, 1), null, Margin));
        }
    }
}
