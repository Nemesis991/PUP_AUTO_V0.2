using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Geometry;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>
    /// A straight axis from (0,0) east to (100,0) with a 20 m wide servitude: the left edge at y = +10, the right at y = −10.
    /// Poles 1 and 2 sit on the axis at its two ends.
    /// </summary>
    internal static class ServitudeFixture
    {
        public static readonly List<(double X, double Y)> Axis = new List<(double, double)> { (0, 0), (100, 0) };

        public static readonly List<(string Number, double X, double Y)> Poles =
            new List<(string, double, double)> { ("1", 0, 0), ("2", 100, 0) };

        /// <summary>The outline counter-clockwise: right edge west→east, then left edge east→west.</summary>
        public static List<EdgeVertex> Outline() => new List<EdgeVertex>
        {
            new EdgeVertex(0, -10, 0),
            new EdgeVertex(100, -10, 0),
            new EdgeVertex(100, 10, 0),
            new EdgeVertex(0, 10, 0)
        };

        public static ServitudeEdgePointInput Point(double x, double y, string ekatte, string? second = null, bool boundary = false)
        {
            var point = new ServitudeEdgePointInput { X = x, Y = y, Direction = 0, IsBoundary = boundary };
            point.Ekattes.Add(ekatte);
            if (second != null) point.Ekattes.Add(second);
            return point;
        }
    }

    public class ServitudeEdgesTests
    {
        [Fact]
        public void ARectangleAroundTheAxis_SplitsIntoOneLeftAndOneRightEdge()
        {
            ServitudeEdgeResult result = ServitudeEdges.Split(
                ServitudeFixture.Outline(), true, ServitudeFixture.Axis, ServitudeFixture.Poles);

            Assert.Null(result.Error);
            Assert.True(result.Ok);
            Assert.True(result.Left!.IsLeft);
            Assert.False(result.Right!.IsLeft);
            Assert.Equal(new[] { 10D, 10D }, result.Left.Vertices.Select(v => v.Y));
            Assert.Equal(new[] { -10D, -10D }, result.Right.Vertices.Select(v => v.Y));
        }

        [Fact]
        public void EachEdge_IsOrderedAlongTheRoute()
        {
            ServitudeEdgeResult result = ServitudeEdges.Split(
                ServitudeFixture.Outline(), true, ServitudeFixture.Axis, ServitudeFixture.Poles);

            // the left run comes out of the loop backwards (east to west) and is turned around
            Assert.Equal(new[] { 0D, 100D }, result.Left!.Vertices.Select(v => v.X));
            Assert.Equal(new[] { 0D, 100D }, result.Right!.Vertices.Select(v => v.X));
        }

        [Fact]
        public void AnAxisDrawnBackwards_IsOrientedByThePoles_AndGivesTheSameEdges()
        {
            var backwards = new List<(double X, double Y)> { (100, 0), (0, 0) };

            ServitudeEdgeResult result = ServitudeEdges.Split(
                ServitudeFixture.Outline(), true, backwards, ServitudeFixture.Poles);

            Assert.True(result.AxisReversed);
            Assert.Equal(new[] { 10D, 10D }, result.Left!.Vertices.Select(v => v.Y));
            Assert.Equal(new[] { 0D, 100D }, result.Left.Vertices.Select(v => v.X));
        }

        [Fact]
        public void WithoutPoles_TheAxisKeepsItsDrawingDirection()
        {
            var backwards = new List<(double X, double Y)> { (100, 0), (0, 0) };

            ServitudeEdgeResult result = ServitudeEdges.Split(
                ServitudeFixture.Outline(), true, backwards, new List<(string, double, double)>());

            Assert.False(result.AxisReversed);
            // the direction is now west, so the sides swap over
            Assert.Equal(new[] { -10D, -10D }, result.Left!.Vertices.Select(v => v.Y));
        }

        [Fact]
        public void AnOpenOutlineThatComesBackToItsStart_IsTreatedAsTheClosedLoop()
        {
            List<EdgeVertex> open = ServitudeFixture.Outline();
            open.Add(new EdgeVertex(0, -10, 0));    // the closing vertex, repeated

            ServitudeEdgeResult result = ServitudeEdges.Split(open, false, ServitudeFixture.Axis, ServitudeFixture.Poles);

            Assert.True(result.Ok);
            Assert.Equal(1, result.DuplicatesDropped);
            Assert.Equal(2, result.Left!.Vertices.Count);
            Assert.Equal(2, result.Right!.Vertices.Count);
        }

        [Fact]
        public void ConsecutiveDuplicateVertices_AreDropped()
        {
            var outline = new List<EdgeVertex>
            {
                new EdgeVertex(0, -10, 0), new EdgeVertex(0, -10, 0),
                new EdgeVertex(100, -10, 0), new EdgeVertex(100, 10, 0), new EdgeVertex(0, 10, 0)
            };

            ServitudeEdgeResult result = ServitudeEdges.Split(outline, true, ServitudeFixture.Axis, ServitudeFixture.Poles);

            Assert.True(result.Ok);
            Assert.Equal(1, result.DuplicatesDropped);
        }

        [Fact]
        public void AnOutlineEntirelyOnOneSide_IsReportedWithCountsOnly()
        {
            var oneSide = new List<EdgeVertex>
            {
                new EdgeVertex(0, 10, 0), new EdgeVertex(100, 10, 0),
                new EdgeVertex(100, 20, 0), new EdgeVertex(0, 20, 0)
            };

            ServitudeEdgeResult result = ServitudeEdges.Split(oneSide, true, ServitudeFixture.Axis, ServitudeFixture.Poles);

            Assert.False(result.Ok);
            Assert.Contains("1 леви и 0 десни", result.Error);
            Assert.DoesNotContain("10", result.Error!.Split('(')[0]);
        }

        [Fact]
        public void AnOutlineThatCrossesTheAxisFourTimes_IsReportedAsNotSplittable()
        {
            // a bow tie: left, right, left, right around the axis
            var bowTie = new List<EdgeVertex>
            {
                new EdgeVertex(10, 10, 0), new EdgeVertex(40, -10, 0),
                new EdgeVertex(60, 10, 0), new EdgeVertex(90, -10, 0)
            };

            ServitudeEdgeResult result = ServitudeEdges.Split(bowTie, true, ServitudeFixture.Axis, ServitudeFixture.Poles);

            Assert.False(result.Ok);
            Assert.Contains("2 леви и 2 десни", result.Error);
        }

        [Fact]
        public void AnAxisWithFewerThanTwoPoints_IsReported()
        {
            ServitudeEdgeResult result = ServitudeEdges.Split(
                ServitudeFixture.Outline(), true, new List<(double, double)> { (0, 0) }, ServitudeFixture.Poles);

            Assert.Equal("оста на трасето има по-малко от 2 точки", result.Error);
        }

        [Fact]
        public void TheStationAndSide_AreMeasuredAtTheClosestPointOfTheAxis()
        {
            (double station, double side) = ServitudeEdges.Project(ServitudeFixture.Axis, 30, 10);

            Assert.Equal(30, station, 9);
            Assert.Equal(10, side, 9);
        }

        [Fact]
        public void APointBeyondTheAxisEnd_ProjectsOntoItsLastPoint()
        {
            (double station, double side) = ServitudeEdges.Project(ServitudeFixture.Axis, 130, -10);

            Assert.Equal(100, station, 9);
            Assert.True(side < 0);
        }

        [Fact]
        public void AnArcSegmentOfTheOutline_KeepsItsBulge_AndNegatesItWhenTheRunIsTurnedAround()
        {
            List<EdgeVertex> outline = ServitudeFixture.Outline();
            outline[2] = new EdgeVertex(100, 10, 0.25);     // the left edge, drawn east -> west

            ServitudeEdgeResult result = ServitudeEdges.Split(outline, true, ServitudeFixture.Axis, ServitudeFixture.Poles);

            // turned around, the arc now leaves the first (west) vertex with the opposite bulge
            Assert.Equal(-0.25, result.Left!.Vertices[0].Bulge, 9);
            Assert.Equal(0, result.Left.Vertices[1].Bulge, 9);
        }
    }

    public class ServitudeNumberingTests
    {
        private static List<ServitudeEdgePointInput> LeftPoints() => new List<ServitudeEdgePointInput>
        {
            ServitudeFixture.Point(0, 10, "78135"),
            ServitudeFixture.Point(20, 10, "78135"),
            ServitudeFixture.Point(40, 10, "78135", "69050", boundary: true),
            ServitudeFixture.Point(60, 10, "69050"),
            ServitudeFixture.Point(80, 10, "69050")
        };

        private static List<ServitudeEdgePointInput> RightPoints() => new List<ServitudeEdgePointInput>
        {
            ServitudeFixture.Point(0, -10, "78135"),
            ServitudeFixture.Point(50, -10, "78135", "69050", boundary: true),
            ServitudeFixture.Point(90, -10, "69050")
        };

        [Fact]
        public void TheStartNumbers_AreRespected()
        {
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(LeftPoints(), 5001, true);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.Number(RightPoints(), 1, false);

            Assert.Equal(new[] { 5001, 5002, 5003, 5004, 5005 }, left.Select(p => p.Number));
            Assert.Equal(new[] { 1, 2, 3 }, right.Select(p => p.Number));
            Assert.All(left, p => Assert.True(p.IsLeft));
            Assert.All(right, p => Assert.False(p.IsLeft));
        }

        [Fact]
        public void OtherStartNumbers_AreUsedAsGiven()
        {
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(LeftPoints(), 7000, true);

            Assert.Equal(7000, left[0].Number);
            Assert.Equal(7004, left[4].Number);
        }

        [Fact]
        public void ABoundaryPoint_ConsumesOneNumber_AndStandsInBothSections()
        {
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(LeftPoints(), 5001, true);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.Number(RightPoints(), 1, false);

            ServitudeRegister first = ServitudeRegisterBuilder.Build(left, right, "78135", "НОВА ВЛ 110kV", "с. Царевец");
            ServitudeRegister second = ServitudeRegisterBuilder.Build(left, right, "69050", "НОВА ВЛ 110kV", "с. Старо село");

            // 5003 is the last left point of the first section and the first of the second
            Assert.Equal(5003, first.Rows.Last(r => r.Left != null).Left!.Number);
            Assert.Equal(5003, second.Rows.First(r => r.Left != null).Left!.Number);
            // and it is numbered once: the point after it is 5004
            Assert.Equal(5004, second.Rows.Where(r => r.Left != null).ElementAt(1).Left!.Number);
        }

        [Fact]
        public void APointOfOneЗемлище_IsNotListedInTheOther()
        {
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(LeftPoints(), 5001, true);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.Number(RightPoints(), 1, false);

            ServitudeRegister first = ServitudeRegisterBuilder.Build(left, right, "78135", "ВЛ", "с. Царевец");

            Assert.Equal(new[] { 5001, 5002, 5003 }, first.Rows.Where(r => r.Left != null).Select(r => r.Left!.Number));
            Assert.Equal(new[] { 1, 2 }, first.Rows.Where(r => r.Right != null).Select(r => r.Right!.Number));
        }

        [Fact]
        public void ARouteThatComesBack_GivesSeveralRunsOfNumbersInOneSection()
        {
            var there_and_back = new List<ServitudeEdgePointInput>
            {
                ServitudeFixture.Point(0, 10, "78135"),
                ServitudeFixture.Point(10, 10, "78135"),
                ServitudeFixture.Point(20, 10, "69050"),
                ServitudeFixture.Point(30, 10, "69050"),
                ServitudeFixture.Point(40, 10, "78135"),
                ServitudeFixture.Point(50, 10, "78135"),
                ServitudeFixture.Point(60, 10, "69050"),
                ServitudeFixture.Point(70, 10, "78135")
            };
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(there_and_back, 5001, true);

            ServitudeRegister section = ServitudeRegisterBuilder.Build(
                left, new List<NumberedServitudePoint>(), "78135", "ВЛ", "с. Царевец");

            List<string> ranges = ServitudeRegisterBuilder.NumberRanges(section.Rows.Select(r => r.Left!));
            Assert.Equal(new[] { "5001–5002", "5005–5006", "5008" }, ranges);
        }
    }

    public class ServitudeRegisterBuilderTests
    {
        private static List<NumberedServitudePoint> Side(bool isLeft, int start, int count, string ekatte)
        {
            var points = new List<ServitudeEdgePointInput>();
            for (int i = 0; i < count; i++) points.Add(ServitudeFixture.Point(i * 10, isLeft ? 10 : -10, ekatte));
            return ServitudeRegisterBuilder.Number(points, start, isLeft);
        }

        [Fact]
        public void TheTwoSides_ArePairedByRowIndex()
        {
            ServitudeRegister register = ServitudeRegisterBuilder.Build(
                Side(true, 5001, 3, "78135"), Side(false, 1, 3, "78135"), "78135", "ВЛ", "с. Царевец");

            Assert.Equal(3, register.Rows.Count);
            Assert.Equal(5001, register.Rows[0].Left!.Number);
            Assert.Equal(1, register.Rows[0].Right!.Number);
            Assert.Equal(5003, register.Rows[2].Left!.Number);
            Assert.Equal(3, register.Rows[2].Right!.Number);
        }

        [Fact]
        public void TheShorterSide_LeavesItsCellsEmpty()
        {
            ServitudeRegister register = ServitudeRegisterBuilder.Build(
                Side(true, 5001, 4, "78135"), Side(false, 1, 2, "78135"), "78135", "ВЛ", "с. Царевец");

            Assert.Equal(4, register.Rows.Count);
            Assert.Equal(4, register.LeftCount);
            Assert.Equal(2, register.RightCount);
            Assert.Null(register.Rows[2].Right);
            Assert.Null(register.Rows[3].Right);
            Assert.NotNull(register.Rows[3].Left);
        }

        [Fact]
        public void TheNumbers_AreAscendingDownEachColumn()
        {
            ServitudeRegister register = ServitudeRegisterBuilder.Build(
                Side(true, 5001, 5, "78135"), Side(false, 1, 5, "78135"), "78135", "ВЛ", "с. Царевец");

            List<int> left = register.Rows.Where(r => r.Left != null).Select(r => r.Left!.Number).ToList();
            List<int> right = register.Rows.Where(r => r.Right != null).Select(r => r.Right!.Number).ToList();
            Assert.Equal(left.OrderBy(n => n), left);
            Assert.Equal(right.OrderBy(n => n), right);
        }

        [Fact]
        public void TheTitle_IsThePrefixAndTheObject_AndTheSubtitleIsTheEkatteTitle()
        {
            ServitudeRegister register = ServitudeRegisterBuilder.Build(
                Side(true, 5001, 1, "78135"), Side(false, 1, 1, "78135"), "78135", " НОВА ВЛ 110kV ",
                "НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА");

            Assert.Equal("КООРДИНАТЕН РЕГИСТЪР НА СЕРВИТУТА НА НОВА ВЛ 110kV", register.Title);
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА", register.Subtitle);
        }

        [Fact]
        public void TheRouteOrder_IsWhereEachЗемлищеIsFirstMet()
        {
            var leftInput = new List<ServitudeEdgePointInput>
            {
                ServitudeFixture.Point(0, 10, "78135"),
                ServitudeFixture.Point(10, 10, "78135", "69050", boundary: true),
                ServitudeFixture.Point(20, 10, "69050"),
                ServitudeFixture.Point(30, 10, "16122")
            };
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(leftInput, 5001, true);

            Dictionary<string, double> order = ServitudeRegisterBuilder.RouteOrder(left, new List<NumberedServitudePoint>());

            // ordering the codes by their position gives the route, not the numeric order of the EKATTE codes
            Assert.Equal(new[] { "78135", "69050", "16122" }, order.OrderBy(p => p.Value).Select(p => p.Key));
        }

        [Fact]
        public void ARouteThatComesBack_KeepsTheЗемлищеAtItsFirstVisit()
        {
            var leftInput = new List<ServitudeEdgePointInput>
            {
                ServitudeFixture.Point(0, 10, "78135"),
                ServitudeFixture.Point(10, 10, "69050"),
                ServitudeFixture.Point(20, 10, "69050"),
                ServitudeFixture.Point(30, 10, "78135")
            };
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(leftInput, 5001, true);

            Dictionary<string, double> order = ServitudeRegisterBuilder.RouteOrder(left, new List<NumberedServitudePoint>());

            Assert.Equal(0, order["78135"]);
            Assert.True(order["69050"] > order["78135"]);
        }

        [Fact]
        public void TheTwoEdges_AreComparedAsFractions_NotAsNumbers()
        {
            // the right edge meets 16122 early although its numbers are much smaller than the left edge's
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(
                new List<ServitudeEdgePointInput>
                {
                    ServitudeFixture.Point(0, 10, "78135"),
                    ServitudeFixture.Point(50, 10, "78135"),
                    ServitudeFixture.Point(90, 10, "16122")
                }, 5001, true);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.Number(
                new List<ServitudeEdgePointInput>
                {
                    ServitudeFixture.Point(0, -10, "16122"),
                    ServitudeFixture.Point(90, -10, "78135")
                }, 1, false);

            Dictionary<string, double> order = ServitudeRegisterBuilder.RouteOrder(left, right);

            Assert.Equal(0, order["16122"]);
            Assert.Equal(0, order["78135"]);
        }

        [Fact]
        public void ASectionWithNoPointsOfEitherSide_HasNoRows()
        {
            ServitudeRegister register = ServitudeRegisterBuilder.Build(
                Side(true, 5001, 3, "78135"), Side(false, 1, 3, "78135"), "16122", "ВЛ", "с. Горна Бешовица");

            Assert.Empty(register.Rows);
        }
    }

    public class ServitudePointPlacementTests
    {
        private const double Degree = Math.PI / 180;

        /// <summary>The sample from Drawing1.dwg: a left-edge point whose label sits at θ = 339.36°.</summary>
        private const double SampleTheta = 339.36 * Degree;

        [Fact]
        public void ThetaIsTheEdgeDirectionMinus90Degrees_PointingFromLeftToRight()
        {
            // an edge running east: left is north, right is south, so θ points south
            Assert.Equal(270 * Degree, ServitudePointPlacement.Theta(0), 9);
            Assert.Equal(0, ServitudePointPlacement.Theta(90 * Degree), 9);
        }

        [Fact]
        public void TheSample_GivesMiddleRightAndAnAlignmentPoint3Point4MetresAway()
        {
            // the edge direction whose θ is the sample's
            double edge = SampleTheta + Math.PI / 2;

            ServitudeLabelPlacement placement = ServitudePointPlacement.Place(5349, 100, 200, edge, isLeft: true);

            Assert.Equal(SampleTheta, placement.Rotation, 6);
            Assert.False(placement.MiddleLeft);        // MiddleRight
            double dx = placement.AlignX - 100, dy = placement.AlignY - 200;
            Assert.Equal(3.40, Math.Sqrt(dx * dx + dy * dy), 6);
            // and it lies at θ + 180°, i.e. the text ends before the point
            Assert.Equal(ServitudePointPlacement.Normalize(SampleTheta + Math.PI), Math.Atan2(dy, dx) is var a && a < 0 ? a + 2 * Math.PI : a, 6);
        }

        [Fact]
        public void TheRightEdge_MirrorsTheLeftOne()
        {
            double edge = SampleTheta + Math.PI / 2;

            ServitudeLabelPlacement left = ServitudePointPlacement.Place(1, 0, 0, edge, isLeft: true);
            ServitudeLabelPlacement right = ServitudePointPlacement.Place(1, 0, 0, edge, isLeft: false);

            Assert.Equal(left.Rotation, right.Rotation, 9);
            Assert.True(right.MiddleLeft);
            Assert.Equal(-left.AlignX, right.AlignX, 9);
            Assert.Equal(-left.AlignY, right.AlignY, 9);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(89, false)]
        [InlineData(91, true)]
        [InlineData(180, true)]
        [InlineData(269, true)]
        [InlineData(271, false)]
        [InlineData(339.36, false)]
        public void AThetaThatWouldReadUpsideDown_NeedsTheFlip(double degrees, bool expected) =>
            Assert.Equal(expected, ServitudePointPlacement.NeedsFlip(degrees * Degree));

        [Fact]
        public void TheFlip_TurnsTheBlock_SwapsTheJustification_AndKeepsTheTextOnTheSameSide()
        {
            // θ = 180° would read upside down
            double edge = 180 * Degree + Math.PI / 2;

            ServitudeLabelPlacement left = ServitudePointPlacement.Place(7, 50, 60, edge, isLeft: true);

            Assert.Equal(0, left.Rotation, 9);          // 180° + 180°
            Assert.True(left.MiddleLeft);               // swapped from MiddleRight
            // unflipped, MiddleRight at local −3.4 with θ = 180° would land at x = 50 + 3.4; the flip keeps it there
            Assert.Equal(53.4, left.AlignX, 6);
            Assert.Equal(60, left.AlignY, 6);
        }

        [Fact]
        public void TheFlippedRightEdge_AlsoKeepsItsSide()
        {
            double edge = 180 * Degree + Math.PI / 2;

            ServitudeLabelPlacement right = ServitudePointPlacement.Place(7, 50, 60, edge, isLeft: false);

            Assert.Equal(0, right.Rotation, 9);
            Assert.False(right.MiddleLeft);             // swapped from MiddleLeft
            Assert.Equal(46.6, right.AlignX, 6);
        }

        [Fact]
        public void TheInsertionPoint_IsTheServitudePointItself()
        {
            ServitudeLabelPlacement placement = ServitudePointPlacement.Place(42, 123.456, 789.012, 0, isLeft: true);

            Assert.Equal(123.456, placement.PointX, 9);
            Assert.Equal(789.012, placement.PointY, 9);
            Assert.Equal("42", placement.Label);
        }
    }

    public class BulgeSegmentTests
    {
        [Fact]
        public void AStraightSegment_HasItsChordAsLength_AndInterpolatesLinearly()
        {
            var segment = new BulgeSegment(0, 0, 10, 0, 0);

            Assert.False(segment.IsArc);
            Assert.Equal(10, segment.Length, 9);
            Assert.Equal((5, 0), segment.PointAt(0.5));
            Assert.Equal(0.25, segment.FractionOf(2.5, 0), 9);
            Assert.Equal(0, segment.DirectionAt(0.5), 9);
        }

        [Fact]
        public void ASemicircle_HasBulgeOne_AndItsCentreOnTheChordMidpoint()
        {
            var segment = new BulgeSegment(0, 0, 0, 10, 1);

            Assert.True(segment.IsArc);
            Assert.Equal(5, segment.Radius, 9);
            Assert.Equal((0, 5), (Math.Round(segment.Centre.X, 9), Math.Round(segment.Centre.Y, 9)));
            Assert.Equal(5 * Math.PI, segment.Length, 6);
        }

        [Fact]
        public void APositiveBulge_TurnsCounterClockwise_SoAnEastwardChordBulgesSouth()
        {
            var segment = new BulgeSegment(0, 0, 10, 0, 0.5);

            // counter-clockwise around a centre ABOVE the chord, so the arc itself dips below it
            // (the convention ServitudeSegmenter already uses: sign(bulge) == sign(sweep))
            Assert.True(segment.Centre.Y > 0);
            (double x, double y) = segment.PointAt(0.5);
            Assert.Equal(5, x, 6);
            Assert.Equal(-0.5 * 0.5 * 10, y, 6);        // sagitta = |bulge| * chord / 2, below the chord
            Assert.Equal(0.5, segment.FractionOf(x, y), 6);
        }

        [Fact]
        public void ANegativeBulge_BulgesTheOtherWay()
        {
            var segment = new BulgeSegment(0, 0, 10, 0, -0.5);

            Assert.Equal(2.5, segment.PointAt(0.5).Y, 6);
            Assert.Equal(0.5, segment.FractionOf(segment.PointAt(0.5).X, segment.PointAt(0.5).Y), 6);
        }

        [Fact]
        public void SamplingAnArc_KeepsTheSagittaUnderTheLimit_AndStartsAtItsFirstPoint()
        {
            var segment = new BulgeSegment(0, 0, 20, 0, 1);     // a semicircle of radius 10
            List<(double X, double Y)> points = segment.Sample(0.001).ToList();

            Assert.Equal((0D, 0D), points[0]);
            Assert.DoesNotContain((20D, 0D), points);           // the end is excluded
            foreach ((double x, double y) in points)
            {
                Assert.Equal(10, Math.Sqrt((x - 10) * (x - 10) + y * y), 6);
            }
            Assert.True(points.Count > 100, $"expected a fine sampling, got {points.Count} points");
        }

        [Fact]
        public void AReversedSegment_WalksTheSameArcTheOtherWay()
        {
            var segment = new BulgeSegment(0, 0, 10, 0, 0.5);
            BulgeSegment back = segment.Reversed();

            Assert.Equal(segment.Length, back.Length, 9);
            Assert.Equal(segment.PointAt(0.25).X, back.PointAt(0.75).X, 6);
            Assert.Equal(segment.PointAt(0.25).Y, back.PointAt(0.75).Y, 6);
        }
    }

    public class ServitudeRegisterExporterTests : IDisposable
    {
        private readonly string _dir;

        public ServitudeRegisterExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private sealed class Book
        {
            public List<(string Name, Worksheet Sheet)> Sheets = new List<(string, Worksheet)>();
            public Stylesheet Styles = null!;

            public Cell? CellAt(int sheet, string reference) =>
                Sheets[sheet].Sheet.Descendants<Cell>().SingleOrDefault(c => c.CellReference!.Value == reference);

            public string? Text(int sheet, string reference) => CellAt(sheet, reference)?.InlineString?.Text?.Text;

            public double Number(int sheet, string reference) =>
                double.Parse(CellAt(sheet, reference)!.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);

            public bool IsEmpty(int sheet, string reference) => CellAt(sheet, reference)?.CellValue == null;

            public string? Format(int sheet, string reference)
            {
                CellFormat format = Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)CellAt(sheet, reference)!.StyleIndex!.Value);
                uint id = format.NumberFormatId?.Value ?? 0U;
                return Styles.NumberingFormats?.Elements<NumberingFormat>().FirstOrDefault(n => n.NumberFormatId!.Value == id)?.FormatCode?.Value;
            }

            public List<string> Merges(int sheet) =>
                Sheets[sheet].Sheet.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();

            public List<uint> Breaks(int sheet) =>
                Sheets[sheet].Sheet.Descendants<Break>().Select(b => b.Id!.Value).ToList();
        }

        /// <summary>Царевец with four left and three right points, the third left one on the boundary.</summary>
        private static ServitudeRegister Section()
        {
            var leftInput = new List<ServitudeEdgePointInput>
            {
                ServitudeFixture.Point(362934.476, 4780339.667, "78135"),
                ServitudeFixture.Point(362940.5, 4780345.25, "78135"),
                ServitudeFixture.Point(362950.125, 4780350.875, "78135", "69050", boundary: true),
                ServitudeFixture.Point(362960, 4780360, "69050")
            };
            var rightInput = new List<ServitudeEdgePointInput>
            {
                ServitudeFixture.Point(362975.115, 4780310.290, "78135"),
                ServitudeFixture.Point(362980, 4780315, "78135")
            };

            return ServitudeRegisterBuilder.Build(
                ServitudeRegisterBuilder.Number(leftInput, 5001, true),
                ServitudeRegisterBuilder.Number(rightInput, 1, false),
                "78135", "НОВА ВЛ 110kV",
                "НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА");
        }

        private Book Export(params (string SheetName, IReadOnlyList<ServitudeRegister> Sections)[] sheets)
        {
            string path = ServitudeRegisterExporter.Export(sheets, _dir);
            Assert.Equal(Path.Combine(_dir, "Координатен_регистър_на_сервитута.xlsx"), path);

            var book = new Book();
            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                foreach (Sheet sheet in wb.Workbook.Descendants<Sheet>())
                {
                    book.Sheets.Add((sheet.Name!.Value!, ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet));
                }
                book.Styles = wb.WorkbookStylesPart!.Stylesheet;
            }
            return book;
        }

        private Book ExportOne() => Export(("общ. Мездра", new[] { Section() }));

        [Fact]
        public void TheTitleAndSubtitle_SpanAllSixColumns()
        {
            Book book = ExportOne();

            Assert.Equal("КООРДИНАТЕН РЕГИСТЪР НА СЕРВИТУТА НА НОВА ВЛ 110kV", book.Text(0, "A1"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА", book.Text(0, "A2"));
            Assert.Contains("A1:F1", book.Merges(0));
            Assert.Contains("A2:F2", book.Merges(0));
        }

        [Fact]
        public void TheThreeHeaderRows_CarryTheOfficialTexts()
        {
            Book book = ExportOne();

            Assert.Equal("Номер на точка", book.Text(0, "A4"));
            Assert.Equal("СЕРВИТУТ - ЛЯВО", book.Text(0, "B4"));
            Assert.Equal("Номер на точка", book.Text(0, "D4"));
            Assert.Equal("СЕРВИТУТ - ДЯСНО", book.Text(0, "E4"));
            Assert.Equal("КС: БГС2005-кад", book.Text(0, "B5"));
            Assert.Equal("КС: БГС2005-кад", book.Text(0, "E5"));
            Assert.Equal("X[m] (север)", book.Text(0, "B6"));
            Assert.Equal("Y[m] (изток)", book.Text(0, "C6"));
            Assert.Equal("X[m] (север)", book.Text(0, "E6"));
            Assert.Equal("Y[m] (изток)", book.Text(0, "F6"));
        }

        [Fact]
        public void TheHeaderMerges_AreTheOfficialOnes()
        {
            List<string> merges = ExportOne().Merges(0);

            Assert.Contains("A4:A6", merges);       // "Номер на точка" down all three rows
            Assert.Contains("D4:D6", merges);
            Assert.Contains("B4:C4", merges);       // the side over its two coordinate columns
            Assert.Contains("E4:F4", merges);
            Assert.Contains("B5:C5", merges);       // and the coordinate system
            Assert.Contains("E5:F5", merges);
        }

        [Fact]
        public void APointRow_HasItsNumberAndTheCoordinatesSwappedIntoGeodeticOrder()
        {
            Book book = ExportOne();

            Assert.Equal(5001, book.Number(0, "A7"));
            Assert.Equal(4780339.667, book.Number(0, "B7"));    // X(север) = the drawing's Y
            Assert.Equal(362934.476, book.Number(0, "C7"));     // Y(изток) = the drawing's X
            Assert.Equal(1, book.Number(0, "D7"));
            Assert.Equal(4780310.290, book.Number(0, "E7"));
            Assert.Equal(362975.115, book.Number(0, "F7"));
        }

        [Fact]
        public void TheCoordinates_AreNumbersWithThreeDecimals()
        {
            Book book = ExportOne();

            Assert.Equal("0.000", book.Format(0, "B7"));
            Assert.Equal("0.000", book.Format(0, "C7"));
            Assert.Equal("0.000", book.Format(0, "E7"));
            Assert.Equal("0.000", book.Format(0, "F7"));
        }

        [Fact]
        public void TheShorterSide_LeavesItsThreeCellsEmpty()
        {
            Book book = ExportOne();

            // three left points remain in Царевец (5001..5003) against two right ones
            Assert.Equal(5003, book.Number(0, "A9"));
            Assert.True(book.IsEmpty(0, "D9"));
            Assert.True(book.IsEmpty(0, "E9"));
            Assert.True(book.IsEmpty(0, "F9"));
        }

        [Fact]
        public void TheBoundaryPoint_IsTheLastRowOfTheSection()
        {
            Book book = ExportOne();

            Assert.Equal(5003, book.Number(0, "A9"));
            Assert.Null(book.CellAt(0, "A10"));     // 5004 belongs to the next землище only
        }

        [Fact]
        public void TwoMunicipalities_GiveTwoSheets()
        {
            Book book = Export(
                ("общ. Мездра", new[] { Section() }),
                ("общ. Враца", new[] { Section() }));

            Assert.Equal(2, book.Sheets.Count);
            Assert.Equal("общ. Мездра", book.Sheets[0].Name);
            Assert.Equal("общ. Враца", book.Sheets[1].Name);
        }

        [Fact]
        public void EachЗемлищеAfterTheFirst_StartsOnANewPrintedPage()
        {
            Book book = Export(("общ. Мездра", new[] { Section(), Section() }));

            Assert.Single(book.Breaks(0));
            Assert.Empty(Export(("общ. Мездра", new[] { Section() })).Breaks(0));
        }

        [Fact]
        public void TheSheetIsPortraitAndOnePageWide()
        {
            Book book = ExportOne();
            PageSetup setup = book.Sheets[0].Sheet.Descendants<PageSetup>().Single();

            Assert.Equal(OrientationValues.Portrait, setup.Orientation!.Value);
            Assert.Equal(1U, setup.FitToWidth!.Value);
            Assert.Equal(0U, setup.FitToHeight!.Value);
        }

        [Fact]
        public void ASheetWithOneЗемлище_RepeatsItsHeaderBlockOnEveryPage()
        {
            string path = ServitudeRegisterExporter.Export(
                new[] { ("общ. Мездра", (IReadOnlyList<ServitudeRegister>)new[] { Section() }) }, _dir);

            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                DefinedNames? names = doc.WorkbookPart!.Workbook.Descendants<DefinedNames>().SingleOrDefault();
                DefinedName title = Assert.Single(names!.Elements<DefinedName>());
                Assert.Equal("_xlnm.Print_Titles", title.Name!.Value);
                Assert.Equal("'общ. Мездра'!$4:$6", title.Text);
            }
        }

        [Fact]
        public void AnOutputFileOpenInAnotherProgram_GivesOneReadableMessage()
        {
            string path = Path.Combine(_dir, "Координатен_регистър_на_сервитута.xlsx");
            File.WriteAllBytes(path, new byte[] { 0 });
            var sheets = new[] { ("общ. Мездра", (IReadOnlyList<ServitudeRegister>)new[] { Section() }) };

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // like Excel holding it
            {
                var ex = Assert.Throws<PUP_AUTO.Core.FileInUseException>(() => ServitudeRegisterExporter.Export(sheets, _dir));
                Assert.Equal("Файлът Координатен_регистър_на_сервитута.xlsx е отворен в друга програма — затворете го и пуснете отново.", ex.Message);
            }
        }
    }

    public class PlanarPolygonTests
    {
        private static readonly PlanarPolygon Square = new PlanarPolygon(new List<(double X, double Y)>
        {
            (0, 0), (10, 0), (10, 10), (0, 10)
        });

        [Fact]
        public void APointInside_IsContained_AndOneOutsideIsNot()
        {
            Assert.True(Square.Contains(5, 5));
            Assert.False(Square.Contains(15, 5));
            Assert.False(Square.Contains(5, -1));
        }

        [Fact]
        public void TheBoundingBox_IsTheExtentOfThePoints()
        {
            Assert.Equal(0, Square.Box.MinX);
            Assert.Equal(10, Square.Box.MaxY);
        }

        [Fact]
        public void APolygonFromBulgeVertices_SamplesItsArcs()
        {
            var polygon = PlanarPolygon.FromBulgeVertices(new List<(double X, double Y, double Bulge)>
            {
                (0, 0, 1),      // counter-clockwise semicircle (0,0) -> (10,0), so it dips below the chord
                (10, 0, 0)      // and a straight segment closes it back: the lower half-disc
            });

            Assert.True(polygon.Count > 50, $"expected the arc to be sampled finely, got {polygon.Count} points");
            Assert.True(polygon.Contains(5, -2));
            Assert.False(polygon.Contains(5, 2));
        }
    }
}
