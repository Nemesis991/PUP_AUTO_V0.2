using PUP_AUTO.CadRegister;
using PUP_AUTO.Geometry;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>
    /// One straight servitude edge, (0,35) to (200,35), walked over picked parcels of two землища: A ("78135") on the left and
    /// B ("69050") on the right, all spanning y 0..50. The raw hits come from <see cref="PlanarPolygon.CutFractions"/>, the pure
    /// counterpart of the AutoCAD intersection, so the tests exercise the real clustering and snapping.
    /// </summary>
    internal static class BoundaryFixture
    {
        public const string A = "78135";
        public const string B = "69050";

        public static PlanarPolygon Strip(double x1, double x2) => new PlanarPolygon(new List<(double X, double Y)>
        {
            (x1, 0), (x2, 0), (x2, 50), (x1, 50)
        });

        public static ServitudeEdge Edge(params double[] xs) =>
            new ServitudeEdge(xs.Select(x => new EdgeVertex(x, 35, 0)).ToList(), true);

        public static List<ServitudeEdgePointInput> Walk(
            ServitudeEdge edge, out EdgeWalkStats stats, params (string Ekatte, PlanarPolygon Outline)[] parcels)
        {
            var resolver = new ServitudeEkatteResolver(parcels);
            stats = new EdgeWalkStats();
            return ServitudeEdgeWalker.Walk(edge, resolver,
                (segment, start, end) => parcels.SelectMany(p => p.Outline.CutFractions(segment)).OrderBy(t => t).ToList(),
                stats);
        }
    }

    public class BoundaryCrossingTests
    {
        [Fact]
        public void ABoundaryWithA3CmGap_GivesOneBoundaryPoint()
        {
            // A ends at x = 100, B starts at x = 100.03: two hits 3 cm apart, one crossing
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(100.03, 200)));

            Assert.Equal(3, points.Count);
            ServitudeEdgePointInput boundary = points[1];
            Assert.True(boundary.IsBoundary);
            Assert.Equal(100, boundary.X, 6);           // where the first землище ends
            Assert.Equal(new[] { BoundaryFixture.A, BoundaryFixture.B }, boundary.Ekattes);
            Assert.Equal(2, boundary.MergedHits);
            Assert.Equal(0.03, boundary.SpanM, 4);
            Assert.False(boundary.IsOverlap);
            Assert.Equal(1, stats.BoundaryPoints);
            Assert.Equal(1, stats.Gaps);
        }

        [Fact]
        public void ABoundaryWithA2CmOverlap_GivesOneBoundaryPoint()
        {
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100.02)), (BoundaryFixture.B, BoundaryFixture.Strip(100, 200)));

            Assert.Equal(3, points.Count);
            Assert.Equal(1, stats.BoundaryPoints);
            ServitudeEdgePointInput boundary = points[1];
            Assert.Equal(100, boundary.X, 1);
            Assert.Equal(2, boundary.MergedHits);
            Assert.True(boundary.IsOverlap);
            Assert.Equal(0.02, boundary.SpanM, 4);
            Assert.Equal(1, stats.Overlaps);
        }

        [Fact]
        public void ABoundaryWithABiggerGap_StillGivesOnePoint_AtTheEndOfTheFirstЗемлище()
        {
            // 2 m of no picked parcel between the two: the point is where A ends
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out _,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(102, 200)));

            Assert.Equal(3, points.Count);
            Assert.Equal(100, points[1].X, 6);
            Assert.Equal(2, points[1].SpanM, 4);
        }

        [Fact]
        public void ACrossing3CmFromAVertex_AddsNoPoint_AndTheVertexIsListedInBothЗемлища()
        {
            // the edge has a vertex at x = 100.03, the boundary is at x = 100
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 100.03, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(100, 200)));

            Assert.Equal(3, points.Count);
            Assert.All(points, p => Assert.False(p.IsBoundary));
            Assert.Contains(BoundaryFixture.A, points[1].Ekattes);
            Assert.Contains(BoundaryFixture.B, points[1].Ekattes);
            Assert.Equal(0, stats.BoundaryPoints);
            Assert.Equal(1, stats.SnappedCrossings);
        }

        [Fact]
        public void ACrossing3CmAfterAVertex_AlsoSnapsToThatVertex()
        {
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 99.97, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(100, 200)));

            Assert.Equal(3, points.Count);
            Assert.Contains(BoundaryFixture.A, points[1].Ekattes);
            Assert.Contains(BoundaryFixture.B, points[1].Ekattes);
            Assert.Equal(1, stats.SnappedCrossings);
        }

        [Fact]
        public void ACrossingFurtherFromAVertexThanTheSnapTolerance_GetsItsOwnPoint()
        {
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 100.2, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(100, 200)));

            Assert.Equal(4, points.Count);
            Assert.True(points[1].IsBoundary);
            Assert.Equal(100, points[1].X, 6);
            Assert.Equal(0.2, points[1].NearestVertexM, 4);
            Assert.Equal(0, stats.SnappedCrossings);
        }

        [Fact]
        public void ASliverThatReturnsToTheSameЗемлище_GivesNoPoint()
        {
            // A, then 20 cm of B, then A again
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)),
                (BoundaryFixture.B, BoundaryFixture.Strip(100, 100.2)),
                (BoundaryFixture.A, BoundaryFixture.Strip(100.2, 200)));

            Assert.Equal(2, points.Count);
            Assert.Equal(0, stats.BoundaryPoints);
        }

        [Fact]
        public void TwoRealCrossings5MetresApart_GiveTwoPoints()
        {
            // A, 5 m of B, A again
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)),
                (BoundaryFixture.B, BoundaryFixture.Strip(100, 105)),
                (BoundaryFixture.A, BoundaryFixture.Strip(105, 200)));

            Assert.Equal(4, points.Count);
            Assert.Equal(2, stats.BoundaryPoints);
            Assert.Equal(100, points[1].X, 6);
            Assert.Equal(105, points[2].X, 6);
            Assert.Equal(new[] { BoundaryFixture.A, BoundaryFixture.B }, points[1].Ekattes);
            Assert.Equal(new[] { BoundaryFixture.B, BoundaryFixture.A }, points[2].Ekattes);
        }

        [Fact]
        public void AnEdgeThatLeavesThePickedParcels_GetsNoBoundaryPoint()
        {
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)));

            Assert.Equal(2, points.Count);
            Assert.Equal(0, stats.BoundaryPoints);
            Assert.Empty(points[1].Ekattes);        // numbered, but in no section
            Assert.Equal(1, stats.VerticesOutside);
        }

        [Fact]
        public void TheHitsOfOneCrossing_AreCountedOnTheSummary()
        {
            BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out EdgeWalkStats stats,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(100.03, 200)));

            Assert.Equal(2, stats.Vertices);
            Assert.Equal(2, stats.MergedHits);
        }
    }

    public class MergeCloseNeighboursTests
    {
        private static ServitudeEdgePointInput Point(double x, params string[] ekatte)
        {
            var point = new ServitudeEdgePointInput { X = x, Y = 0 };
            point.Ekattes.AddRange(ekatte);
            return point;
        }

        [Fact]
        public void TwoPointsCloserThan1Cm_AreOne_AndTheKeptOneGainsTheDroppedOnesЗемлища()
        {
            List<ServitudeEdgePointInput> kept = ServitudeRegisterBuilder.MergeCloseNeighbours(
                new[] { Point(0, "A"), Point(10, "A"), Point(10.005, "B"), Point(30, "B") }, out int dropped);

            Assert.Equal(1, dropped);
            Assert.Equal(3, kept.Count);
            Assert.Equal(new[] { "A", "B" }, kept[1].Ekattes);
            Assert.Equal(10, kept[1].X);
        }

        [Fact]
        public void PointsAtLeast1CmApart_AreAllKept()
        {
            List<ServitudeEdgePointInput> kept = ServitudeRegisterBuilder.MergeCloseNeighbours(
                new[] { Point(0, "A"), Point(0.01, "A"), Point(0.5, "A") }, out int dropped);

            Assert.Equal(0, dropped);
            Assert.Equal(3, kept.Count);
        }

        [Fact]
        public void ARunOfCloseNeighbours_CollapsesOntoTheFirst()
        {
            List<ServitudeEdgePointInput> kept = ServitudeRegisterBuilder.MergeCloseNeighbours(
                new[] { Point(5, "A"), Point(5.002, "A"), Point(5.004, "B") }, out int dropped);

            Assert.Equal(2, dropped);
            Assert.Single(kept);
            Assert.Equal(new[] { "A", "B" }, kept[0].Ekattes);
        }
    }

    /// <summary>The label must always sit OUTSIDE the servitude, whatever the corner, arc or boundary point it belongs to.</summary>
    public class ServitudeLabelOutsideTests
    {
        private static List<(string Number, double X, double Y)> Poles(double x1, double y1, double x2, double y2) =>
            new List<(string, double, double)> { ("1", x1, y1), ("2", x2, y2) };

        private static List<ServitudeEdgePointInput> WalkEdge(ServitudeEdge edge)
        {
            var resolver = new ServitudeEkatteResolver(new List<(string, PlanarPolygon)>());
            return ServitudeEdgeWalker.Walk(edge, resolver, (s, a, b) => new List<double>(), new EdgeWalkStats());
        }

        private static void AssertOutside(List<ServitudeEdgePointInput> points, bool isLeft, PlanarPolygon servitude)
        {
            int number = 1;
            foreach (ServitudeEdgePointInput point in points)
            {
                ServitudeLabelPlacement label = ServitudePointPlacement.PlaceOutside(
                    number++, point.X, point.Y, point.Direction, isLeft, servitude, out bool moved);
                Assert.False(servitude.Contains(label.AlignX, label.AlignY), $"label of point {number - 1} is inside the servitude");
                Assert.False(moved, $"label of point {number - 1} had to be moved");
            }
        }

        [Fact]
        public void AtASharpCorner_TheLabelsStayOutside()
        {
            // an L-shaped corridor 20 m wide: east for 100 m, then north
            var outline = new List<EdgeVertex>
            {
                new EdgeVertex(0, -10, 0), new EdgeVertex(110, -10, 0), new EdgeVertex(110, 100, 0),
                new EdgeVertex(90, 100, 0), new EdgeVertex(90, 10, 0), new EdgeVertex(0, 10, 0)
            };
            ServitudeEdgeResult split = ServitudeEdges.Split(outline, true,
                new List<(double, double)> { (0, 0), (100, 0), (100, 100) }, Poles(0, 0, 100, 100));
            Assert.True(split.Ok);
            PlanarPolygon servitude = PlanarPolygon.FromBulgeVertices(outline.Select(v => (v.X, v.Y, v.Bulge)).ToList());

            AssertOutside(WalkEdge(split.Left!), true, servitude);     // includes the inner corner (90, 10)
            AssertOutside(WalkEdge(split.Right!), false, servitude);   // includes the outer corner (110, -10)
        }

        [Fact]
        public void OnAnArc_TheLabelsStayOutsideAndPointAlongTheRadius()
        {
            // a quarter ring: the axis turns left round the origin, radius 100; edges at radius 90 (left) and 110 (right)
            const int segments = 6;
            double step = Math.PI / 2 / segments, bulge = Math.Tan(step / 4);
            var outer = Enumerable.Range(0, segments + 1)
                .Select(k => new EdgeVertex(110 * Math.Cos(k * step), 110 * Math.Sin(k * step), k < segments ? bulge : 0)).ToList();
            var inner = Enumerable.Range(0, segments + 1).Reverse()
                .Select(k => new EdgeVertex(90 * Math.Cos(k * step), 90 * Math.Sin(k * step), k > 0 ? -bulge : 0)).ToList();
            var outline = outer.Concat(inner).ToList();

            var axis = Enumerable.Range(0, 91).Select(d => (100 * Math.Cos(d * Math.PI / 180), 100 * Math.Sin(d * Math.PI / 180))).ToList();
            ServitudeEdgeResult split = ServitudeEdges.Split(outline, true, axis, Poles(100, 0, 0, 100));
            Assert.True(split.Ok, split.Error);
            PlanarPolygon servitude = PlanarPolygon.FromBulgeVertices(outline.Select(v => (v.X, v.Y, v.Bulge)).ToList());

            List<ServitudeEdgePointInput> left = WalkEdge(split.Left!);
            List<ServitudeEdgePointInput> right = WalkEdge(split.Right!);
            AssertOutside(left, true, servitude);
            AssertOutside(right, false, servitude);

            // the left edge is the inner arc, so its labels sit nearer the centre; the right edge's, further out
            foreach (ServitudeEdgePointInput point in left)
            {
                ServitudeLabelPlacement label = ServitudePointPlacement.Place(1, point.X, point.Y, point.Direction, true);
                Assert.Equal(90 - ServitudePointPlacement.LabelOffsetM, Math.Sqrt(label.AlignX * label.AlignX + label.AlignY * label.AlignY), 3);
            }
            foreach (ServitudeEdgePointInput point in right)
            {
                ServitudeLabelPlacement label = ServitudePointPlacement.Place(1, point.X, point.Y, point.Direction, false);
                Assert.Equal(110 + ServitudePointPlacement.LabelOffsetM, Math.Sqrt(label.AlignX * label.AlignX + label.AlignY * label.AlignY), 3);
            }
        }

        [Fact]
        public void AtAnEdgeEnd_TheLabelUsesTheOnlyDirectionThereIsAndStaysOutside()
        {
            var outline = new List<EdgeVertex>
            {
                new EdgeVertex(0, -10, 0), new EdgeVertex(100, -10, 0), new EdgeVertex(100, 10, 0), new EdgeVertex(0, 10, 0)
            };
            ServitudeEdgeResult split = ServitudeEdges.Split(outline, true,
                new List<(double, double)> { (0, 0), (100, 0) }, Poles(0, 0, 100, 0));
            PlanarPolygon servitude = PlanarPolygon.FromBulgeVertices(outline.Select(v => (v.X, v.Y, v.Bulge)).ToList());

            AssertOutside(WalkEdge(split.Left!), true, servitude);
            AssertOutside(WalkEdge(split.Right!), false, servitude);
        }

        [Fact]
        public void ABoundaryPoint_GetsItsLabelOutsideToo()
        {
            // the left edge of a corridor y 15..35 running east, crossing from A into B at x = 100
            var corridor = new PlanarPolygon(new List<(double X, double Y)> { (0, 15), (200, 15), (200, 35), (0, 35) });
            List<ServitudeEdgePointInput> points = BoundaryFixture.Walk(BoundaryFixture.Edge(0, 200), out _,
                (BoundaryFixture.A, BoundaryFixture.Strip(0, 100)), (BoundaryFixture.B, BoundaryFixture.Strip(100.03, 200)));
            Assert.True(points[1].IsBoundary);

            ServitudeLabelPlacement label = ServitudePointPlacement.PlaceOutside(
                5019, points[1].X, points[1].Y, points[1].Direction, true, corridor, out bool moved);

            Assert.False(moved);
            Assert.False(corridor.Contains(label.AlignX, label.AlignY));
            Assert.Equal(100, label.PointX, 6);
            Assert.True(label.AlignY > 35);         // north of the left edge, i.e. to the left of the route
        }

        [Fact]
        public void ALabelWhoseAnchorWouldBeInside_IsMovedToTheOtherSide()
        {
            // a left-edge point at (50, 98) running east: its anchor is 3.4 m north, at (50, 101.4)
            var covers = new PlanarPolygon(new List<(double X, double Y)> { (0, 99), (100, 99), (100, 110), (0, 110) });
            ServitudeLabelPlacement original = ServitudePointPlacement.Place(1, 50, 98, 0, true);
            Assert.True(covers.Contains(original.AlignX, original.AlignY));

            ServitudeLabelPlacement placed = ServitudePointPlacement.PlaceOutside(1, 50, 98, 0, true, covers, out bool moved);

            Assert.True(moved);
            Assert.False(covers.Contains(placed.AlignX, placed.AlignY));
            Assert.Equal(98 - ServitudePointPlacement.LabelOffsetM, placed.AlignY, 6);      // the other side: south
            Assert.NotEqual(original.MiddleLeft, placed.MiddleLeft);                          // with the justification swapped
        }

        [Fact]
        public void WhenBothSidesAreInside_TheOriginalStays_AndNothingIsClaimedMoved()
        {
            var everywhere = new PlanarPolygon(new List<(double X, double Y)> { (0, 0), (100, 0), (100, 100), (0, 100) });

            ServitudeLabelPlacement placed = ServitudePointPlacement.PlaceOutside(1, 50, 50, 0, true, everywhere, out bool moved);

            Assert.False(moved);
            Assert.Equal(ServitudePointPlacement.Place(1, 50, 50, 0, true).AlignY, placed.AlignY, 9);
        }

        [Fact]
        public void TheOtherSide_IsTheOppositeJustificationAndOffset()
        {
            ServitudeLabelPlacement original = ServitudePointPlacement.Place(1, 50, 50, 0, true);
            ServitudeLabelPlacement other = ServitudePointPlacement.Place(1, 50, 50, 0, false);

            Assert.NotEqual(original.MiddleLeft, other.MiddleLeft);
            Assert.Equal(100 - original.AlignY, other.AlignY, 9);
        }

        [Fact]
        public void WithoutAServitudePolygon_NothingIsChecked()
        {
            ServitudeLabelPlacement label = ServitudePointPlacement.PlaceOutside(1, 50, 50, 0, true, null, out bool moved);

            Assert.False(moved);
            Assert.Equal(ServitudePointPlacement.Place(1, 50, 50, 0, true).AlignX, label.AlignX, 9);
        }
    }

    /// <summary>
    /// The count starts at the first point inside the picked parcels, from either end of the route. The servitude is much longer
    /// than the picked parcels: points before and after them do not use up numbers.
    /// </summary>
    public class ServitudeNumberingFromPickedParcelsTests
    {
        private static ServitudeEdgePointInput Point(double x, bool inside)
        {
            var point = new ServitudeEdgePointInput { X = x, Y = 10 };
            if (inside) point.Ekattes.Add("78135");
            return point;
        }

        /// <summary>3 outside, 5 inside, 4 outside, as the route runs.</summary>
        private static List<ServitudeEdgePointInput> ThreeFiveFour() =>
            Enumerable.Range(0, 12).Select(i => Point(i, i >= 3 && i < 8)).ToList();

        [Fact]
        public void Forward_TheFirstIncludedPointGetsTheStartNumber()
        {
            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                ThreeFiveFour(), 5001, true, out int before, out int after);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                ThreeFiveFour(), 1, false, out _, out _);

            Assert.Equal(new[] { 5001, 5002, 5003, 5004, 5005 }, left.Select(p => p.Number));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, right.Select(p => p.Number));
            Assert.Equal(3, before);
            Assert.Equal(4, after);
            Assert.Equal(new[] { 3D, 4, 5, 6, 7 }, left.Select(p => p.X));
        }

        [Fact]
        public void Reversed_TheNumbersStillStartAtTheFirstIncludedPoint()
        {
            // the same servitude walked from the other end: 4 outside, 5 inside, 3 outside
            List<ServitudeEdgePointInput> reversed = ThreeFiveFour().AsEnumerable().Reverse().ToList();

            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                reversed, 5001, true, out int before, out int after);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                reversed, 1, false, out _, out _);

            Assert.Equal(new[] { 5001, 5002, 5003, 5004, 5005 }, left.Select(p => p.Number));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, right.Select(p => p.Number));
            Assert.Equal(4, before);
            Assert.Equal(3, after);
            Assert.Equal(new[] { 7D, 6, 5, 4, 3 }, left.Select(p => p.X));
        }

        [Fact]
        public void AGapInTheMiddle_StillUsesUpItsNumbers()
        {
            // 2 inside, 2 outside (the route leaves the picked parcels and comes back), 2 inside; plus an outside point at each end
            var points = new List<ServitudeEdgePointInput>
            {
                Point(0, false), Point(1, true), Point(2, true), Point(3, false), Point(4, false),
                Point(5, true), Point(6, true), Point(7, false)
            };

            List<NumberedServitudePoint> numbered = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                points, 5001, true, out int before, out int after);

            Assert.Equal(new[] { 5001, 5002, 5003, 5004, 5005, 5006 }, numbered.Select(p => p.Number));
            Assert.Equal(1, before);
            Assert.Equal(1, after);
            // the two gap points carry numbers 5003 and 5004 and no землище, so they stay out of every section
            Assert.Equal(new[] { 5003, 5004 }, numbered.Where(p => p.Ekattes.Count == 0).Select(p => p.Number));
            Assert.Equal(new[] { "5001–5002", "5005–5006" },
                ServitudeRegisterBuilder.NumberRanges(numbered.Where(p => p.Ekattes.Count > 0)));
        }

        [Fact]
        public void AnEdgeWithNoPointInsideThePickedParcels_HasNothingToNumber()
        {
            List<NumberedServitudePoint> numbered = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                Enumerable.Range(0, 5).Select(i => Point(i, false)).ToList(), 5001, true, out int before, out int after);

            Assert.Empty(numbered);
            Assert.Equal(5, before);
            Assert.Equal(0, after);
        }

        [Fact]
        public void ABoundaryPointAtTheEnd_CountsAsInside()
        {
            // the route's last included point is a boundary point listed in two землища
            var boundary = new ServitudeEdgePointInput { X = 5, Y = 10, IsBoundary = true };
            boundary.Ekattes.Add("78135");
            boundary.Ekattes.Add("69050");
            var points = new List<ServitudeEdgePointInput> { Point(0, false), Point(1, true), boundary, Point(6, false) };

            List<NumberedServitudePoint> numbered = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                points, 5001, true, out _, out _);

            Assert.Equal(new[] { 5001, 5002 }, numbered.Select(p => p.Number));
            Assert.True(numbered[1].IsBoundary);
        }

        [Fact]
        public void TheWholeServitudeIsLongerThanThePickedParcels_ButBothDirectionsListTheSamePoints()
        {
            // 1463 outside, 556 inside, 2 outside (about Мездра): the point counts, not the order, decide what is numbered
            var forward = Enumerable.Range(0, 2021).Select(i => Point(i, i >= 1463 && i < 2019)).ToList();
            List<ServitudeEdgePointInput> backward = forward.AsEnumerable().Reverse().ToList();

            List<NumberedServitudePoint> a = ServitudeRegisterBuilder.NumberInsidePickedParcels(forward, 5001, true, out _, out _);
            List<NumberedServitudePoint> b = ServitudeRegisterBuilder.NumberInsidePickedParcels(backward, 5001, true, out _, out _);

            Assert.Equal(556, a.Count);
            Assert.Equal(556, b.Count);
            Assert.Equal(5001, a[0].Number);
            Assert.Equal(5001, b[0].Number);
            Assert.Equal(5556, a[a.Count - 1].Number);
            Assert.Equal(5556, b[b.Count - 1].Number);
        }
    }

    public class ServitudeReverseRouteTests
    {
        private static readonly List<EdgeVertex> Outline = new List<EdgeVertex>
        {
            new EdgeVertex(0, -10, 0), new EdgeVertex(40, -10, 0), new EdgeVertex(100, -10, 0),
            new EdgeVertex(100, 10, 0), new EdgeVertex(60, 10, 0), new EdgeVertex(0, 10, 0)
        };

        private static readonly List<(double X, double Y)> Axis = new List<(double, double)> { (0, 0), (100, 0) };
        private static readonly List<(string, double, double)> Poles = new List<(string, double, double)> { ("1", 0, 0), ("2", 100, 0) };

        private static List<ServitudeEdgePointInput> Inputs(ServitudeEdge edge) =>
            edge.Vertices.Select(v => new ServitudeEdgePointInput { X = v.X, Y = v.Y }).ToList();

        [Fact]
        public void WithTheTick_TheSamePointsAreNumberedFromTheOtherEnd_AndTheSidesSwap()
        {
            ServitudeEdgeResult normal = ServitudeEdges.Split(Outline, true, Axis, Poles);
            ServitudeEdgeResult reversed = ServitudeEdges.Split(Outline, true, Axis, Poles, reverseRoute: true);
            Assert.True(normal.Ok && reversed.Ok);
            Assert.True(reversed.RouteReversedByRequest);
            Assert.False(normal.RouteReversedByRequest);

            List<NumberedServitudePoint> left = ServitudeRegisterBuilder.Number(Inputs(normal.Left!), 5001, true);
            List<NumberedServitudePoint> right = ServitudeRegisterBuilder.Number(Inputs(normal.Right!), 1, false);
            List<NumberedServitudePoint> leftReversed = ServitudeRegisterBuilder.Number(Inputs(reversed.Left!), 5001, true);
            List<NumberedServitudePoint> rightReversed = ServitudeRegisterBuilder.Number(Inputs(reversed.Right!), 1, false);

            // the same points overall
            var all = left.Concat(right).Select(p => (p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            var allReversed = leftReversed.Concat(rightReversed).Select(p => (p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            Assert.Equal(all, allReversed);

            // the former right edge is now the left one, walked from its other end
            Assert.Equal(right.Select(p => (p.X, p.Y)).Reverse(), leftReversed.Select(p => (p.X, p.Y)));
            Assert.Equal(left.Select(p => (p.X, p.Y)).Reverse(), rightReversed.Select(p => (p.X, p.Y)));

            // and the numbering starts at the other end of the route, with the same start numbers
            Assert.Equal(5001, leftReversed[0].Number);
            Assert.Equal(1, rightReversed[0].Number);
            Assert.Equal(100, leftReversed[0].X);       // the first left point is now at the far end
            Assert.Equal(0, left[0].X);
        }

        [Fact]
        public void WithoutTheTick_NothingChanges()
        {
            ServitudeEdgeResult a = ServitudeEdges.Split(Outline, true, Axis, Poles);
            ServitudeEdgeResult b = ServitudeEdges.Split(Outline, true, Axis, Poles, reverseRoute: false);

            Assert.Equal(a.Left!.Vertices.Select(v => (v.X, v.Y)), b.Left!.Vertices.Select(v => (v.X, v.Y)));
            Assert.Equal(a.Right!.Vertices.Select(v => (v.X, v.Y)), b.Right!.Vertices.Select(v => (v.X, v.Y)));
        }

        [Fact]
        public void TheSectionsFollowTheNewRouteOrder()
        {
            ServitudeEdgeResult reversed = ServitudeEdges.Split(Outline, true, Axis, Poles, reverseRoute: true);
            // west half in A, east half in B: walking east to west meets B first
            List<ServitudeEdgePointInput> left = Inputs(reversed.Left!);
            foreach (ServitudeEdgePointInput p in left) p.Ekattes.Add(p.X < 50 ? "A" : "B");
            List<NumberedServitudePoint> numbered = ServitudeRegisterBuilder.Number(left, 5001, true);

            Dictionary<string, double> order = ServitudeRegisterBuilder.RouteOrder(numbered, new List<NumberedServitudePoint>());

            Assert.True(order["B"] < order["A"]);
        }
    }
}
