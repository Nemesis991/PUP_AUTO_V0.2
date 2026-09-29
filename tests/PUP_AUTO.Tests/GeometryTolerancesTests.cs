using PUP_AUTO.Core;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Pins the values the named constants replaced (behaviour must not drift).</summary>
    public class GeometryTolerancesTests
    {
        [Fact]
        public void AreaTolerances_KeepTheirOriginalValues()
        {
            Assert.Equal(0.001, GeometryTolerances.SliverAreaSqm);
            Assert.Equal(0.001, GeometryTolerances.BalanceToleranceSqm);
            Assert.Equal(0.001, GeometryTolerances.PoleAreaPresenceSqm);
        }

        [Fact]
        public void DistanceTolerances_KeepTheirOriginalValues()
        {
            Assert.Equal(0.01, GeometryTolerances.ClosureDistanceM);
            Assert.Equal(0.01, GeometryTolerances.DuplicatePointDistanceM);
            Assert.Equal(50.0, GeometryTolerances.GeoMatchRadiusM);
            Assert.Equal(0.5, GeometryTolerances.GeoMatchMinAreaRatio);
            Assert.Equal(50.0, GeometryTolerances.SanitizeMaxSegmentLengthM);
            Assert.Equal(0.05, GeometryTolerances.SanitizeMinVertexDistanceM);
            Assert.Equal(10.0, GeometryTolerances.SanitizeParasiteSegmentM);
            Assert.Equal(1e-10, GeometryTolerances.BulgeEpsilon);
            Assert.Equal(1e-6, GeometryTolerances.ExactIntervalEpsilonM);
        }

        [Fact]
        public void MarkerAndDrawingConstants_KeepTheirOriginalValues()
        {
            Assert.Equal(20.0, GeometryTolerances.MarkerStepM);
            Assert.Equal(15.0, GeometryTolerances.MarkerParasiteToleranceM);
            Assert.Equal(2.0, GeometryTolerances.MarkerTextHeight);
            Assert.Equal(1.5, GeometryTolerances.MarkerTextOffsetM);
            Assert.Equal(1.0, GeometryTolerances.PoleLabelTextHeight);
            Assert.Equal(0.5, GeometryTolerances.SegmentedServitudeWidth);
        }

        [Fact]
        public void SegmentDefaults_ServAndWindowStaySeparate()
        {
            Assert.Equal(20.0, SegmentDefaults.ServCommandDistanceM);
            Assert.Equal(50.0, SegmentDefaults.WindowFallbackDistanceM);
        }
    }
}
