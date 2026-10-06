using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Duplicate parcel polylines, on axis-aligned rectangles (synthetic coordinates).</summary>
    public class DuplicatePolylinesTests
    {
        private sealed class Rect
        {
            public double X1, Y1, X2, Y2;
            public double Area => (X2 - X1) * (Y2 - Y1);
        }

        private static ParcelShape<Rect> Shape(string id, string handle, double x1, double y1, double x2, double y2)
        {
            var r = new Rect { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
            return new ParcelShape<Rect> { ParcelId = id, Handle = handle, AreaSqm = r.Area, Item = r };
        }

        private static double Overlap(ParcelShape<Rect> a, ParcelShape<Rect> b)
        {
            double w = Math.Min(a.Item.X2, b.Item.X2) - Math.Max(a.Item.X1, b.Item.X1);
            double h = Math.Min(a.Item.Y2, b.Item.Y2) - Math.Max(a.Item.Y1, b.Item.Y1);
            return w > 0 && h > 0 ? w * h : 0.0;
        }

        private static DuplicateFilterResult<Rect> Run(params ParcelShape<Rect>[] shapes) => DuplicatePolylines.Filter(shapes, Overlap);

        [Fact]
        public void IdenticalCopies_AreDuplicates_TheLowestHandleIsKept()
        {
            // the higher handle comes first in the list on purpose
            var result = Run(
                Shape("16122.2.1", "732AC", 0, 0, 10, 20),
                Shape("16122.2.1", "732AB", 0, 0, 10, 20),
                Shape("16122.2.2", "7330F", 50, 0, 60, 10));

            Assert.Equal(new[] { "732AB", "7330F" }, result.Kept.Select(s => s.Handle).OrderBy(h => h, StringComparer.Ordinal).ToArray());
            DuplicateParcel d = Assert.Single(result.Duplicates);
            Assert.Equal("16122.2.1", d.ParcelId);
            Assert.Equal("732AB", d.KeptHandle);
            Assert.Equal(new[] { "732AC" }, d.DroppedHandles.ToArray());
            Assert.Equal(1, result.DroppedCount);
            Assert.Empty(result.OverlappingParts);
        }

        [Fact]
        public void ThreeCopies_LeaveOne()
        {
            var result = Run(
                Shape("1.1", "A1", 0, 0, 5, 5), Shape("1.1", "A2", 0, 0, 5, 5), Shape("1.1", "A3", 0, 0, 5, 5));

            Assert.Equal("A1", Assert.Single(result.Kept).Handle);
            Assert.Equal(new[] { "A2", "A3" }, Assert.Single(result.Duplicates).DroppedHandles.ToArray());
        }

        [Fact]
        public void SameIdDisjointParts_AreBothKept_AndSummedAsBefore()
        {
            var result = Run(Shape("1.1", "A1", 0, 0, 10, 10), Shape("1.1", "A2", 20, 0, 30, 10));

            Assert.Equal(2, result.Kept.Count);
            Assert.Empty(result.Duplicates);
            Assert.Empty(result.OverlappingParts);
        }

        [Fact]
        public void PartsThatOnlyTouch_AreBothKept_WithoutAWarning()
        {
            var result = Run(Shape("1.1", "A1", 0, 0, 10, 10), Shape("1.1", "A2", 10, 0, 20, 10));

            Assert.Equal(2, result.Kept.Count);
            Assert.Empty(result.OverlappingParts);
        }

        [Fact]
        public void SameIdHalfOverlapping_AreBothKept_AndFlagged()
        {
            var result = Run(Shape("1.1", "A1", 0, 0, 10, 10), Shape("1.1", "A2", 5, 0, 15, 10));

            Assert.Equal(2, result.Kept.Count);
            Assert.Empty(result.Duplicates);
            Assert.Equal(new[] { "1.1" }, result.OverlappingParts.ToArray());
            Assert.Equal("имот 1.1: частите се застъпват", DuplicatePolylines.FormatOverlapWarning("1.1"));
        }

        [Fact]
        public void SameShapeUnderDifferentIds_IsNotADuplicate()
        {
            var result = Run(Shape("1.1", "A1", 0, 0, 10, 10), Shape("1.2", "A2", 0, 0, 10, 10));

            Assert.Equal(2, result.Kept.Count);
            Assert.Empty(result.Duplicates);
        }

        [Fact]
        public void AreaDifferenceOfAHundredthOfASquareMetre_IsNotADuplicate()
        {
            Assert.True(DuplicatePolylines.IsDuplicate(100.0, 100.005, 100.0));
            Assert.False(DuplicatePolylines.IsDuplicate(100.0, 100.02, 100.0));
            Assert.False(DuplicatePolylines.IsDuplicate(100.0, 100.0, 98.0));   // overlap only 98 %
            Assert.True(DuplicatePolylines.IsDuplicate(100.0, 100.0, 99.0));
        }

        [Fact]
        public void KeptShapes_KeepTheOrderTheyWereGivenIn()
        {
            var result = Run(
                Shape("1.3", "C", 40, 0, 45, 5),
                Shape("1.1", "A2", 0, 0, 5, 5),
                Shape("1.2", "B", 20, 0, 25, 5),
                Shape("1.1", "A1", 0, 0, 5, 5));

            Assert.Equal(new[] { "1.3", "1.2", "1.1" }, result.Kept.Select(s => s.ParcelId).ToArray());
            Assert.Equal("A1", result.Kept[2].Handle);
        }

        [Fact]
        public void Warning_NamesTheCountTheIdsAndTheHandles_AndLimitsTheList()
        {
            var many = Enumerable.Range(1, 22)
                .Select(i => new DuplicateParcel { ParcelId = "16122.2." + i, KeptHandle = "A" + i, DroppedHandles = { "B" + i } })
                .ToList();

            Assert.Equal(
                "2 имота са начертани два пъти (еднакви полилинии) — взета е по една: 16122.2.1 (732AB, 732AC), 16122.2.2 (7330F, 73310)",
                DuplicatePolylines.FormatWarning(new[]
                {
                    new DuplicateParcel { ParcelId = "16122.2.1", KeptHandle = "732AB", DroppedHandles = { "732AC" } },
                    new DuplicateParcel { ParcelId = "16122.2.2", KeptHandle = "7330F", DroppedHandles = { "73310" } }
                }));

            string limited = DuplicatePolylines.FormatWarning(many, 20);
            Assert.StartsWith("22 имота са начертани два пъти", limited);
            Assert.Contains("16122.2.20 (A20, B20)", limited);
            Assert.DoesNotContain("16122.2.21", limited);
            Assert.EndsWith(", …", limited);
            Assert.Contains("16122.2.22 (A22, B22)", DuplicatePolylines.FormatWarning(many));
        }

        [Fact]
        public void PoleFootprints_AreTheSame_WhenAreaAndBoxAgree()
        {
            double[] box = { 100.0, 200.0, 103.0, 204.0 };

            Assert.True(DuplicatePolylines.SameFootprint(12.0, box, 12.005, new[] { 100.005, 200.0, 103.0, 204.005 }));
            Assert.False(DuplicatePolylines.SameFootprint(12.0, box, 12.0, new[] { 100.5, 200.0, 103.5, 204.0 }));   // shifted
            Assert.False(DuplicatePolylines.SameFootprint(12.0, box, 12.5, box));                                  // other area
        }
    }
}
