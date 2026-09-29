using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class PoleStepsTableBuilderTests
    {
        private static PoleStepPiece Piece(string parcel, double parcelArea, string pole, double pieceArea) =>
            new PoleStepPiece { ParcelId = parcel, ParcelAreaSqm = parcelArea, PoleNumber = pole, PieceAreaSqm = pieceArea };

        [Fact]
        public void SinglePoleInOneParcel()
        {
            var table = PoleStepsTableBuilder.Build(new[] { Piece("A", 1000.0, "5", 36.0) });

            var row = Assert.Single(table.Rows);
            Assert.Equal("A", row.ParcelId);
            Assert.Equal(1000.0, row.ParcelAreaSqm);
            Assert.Equal("5", row.PoleNumber);
            Assert.Equal(36.0, row.PieceAreaSqm);
            Assert.Equal(964.0, row.RemainderSqm);
            Assert.True(row.IsFirstOfParcel);
            Assert.Equal(1, row.ParcelRowCount);
            Assert.Equal(36.0, table.TotalPieceAreaSqm);
        }

        [Fact]
        public void OnePoleSplitOverTwoParcels_GivesOneRowPerParcel()
        {
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("B", 800.0, "1", 16.0),
                Piece("A", 500.0, "1", 20.0)
            });

            Assert.Equal(new[] { "A", "B" }, table.Rows.Select(r => r.ParcelId).ToArray());
            Assert.Equal(480.0, table.Rows[0].RemainderSqm);
            Assert.Equal(784.0, table.Rows[1].RemainderSqm);
            Assert.All(table.Rows, r => Assert.True(r.IsFirstOfParcel));
            Assert.Equal(36.0, table.TotalPieceAreaSqm);
        }

        [Fact]
        public void TwoPolesInOneParcel_RemainderSubtractsBoth_AndRowsAreGrouped()
        {
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 1000.0, "20", 30.0),
                Piece("A", 1000.0, "3", 40.0)
            });

            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(new[] { "3", "20" }, table.Rows.Select(r => r.PoleNumber).ToArray());
            Assert.All(table.Rows, r => Assert.Equal(930.0, r.RemainderSqm));

            Assert.True(table.Rows[0].IsFirstOfParcel);
            Assert.Equal(2, table.Rows[0].ParcelRowCount);
            Assert.False(table.Rows[1].IsFirstOfParcel);
            Assert.Equal(0, table.Rows[1].ParcelRowCount);
            Assert.Equal(70.0, table.TotalPieceAreaSqm);
        }

        [Fact]
        public void NoPieces_GivesEmptyTable()
        {
            var table = PoleStepsTableBuilder.Build(new PoleStepPiece[0]);

            Assert.Empty(table.Rows);
            Assert.Equal(0.0, table.TotalPieceAreaSqm);
        }

        [Fact]
        public void ParcelIds_AreSortedNumericallyPerSegment()
        {
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("61580.240.226", 1.0, "1", 0.5),
                Piece("61580.240.24", 1.0, "1", 0.5),
                Piece("61580.10.5", 1.0, "1", 0.5),
                Piece("61580.240.9", 1.0, "1", 0.5)
            });

            Assert.Equal(
                new[] { "61580.10.5", "61580.240.9", "61580.240.24", "61580.240.226" },
                table.Rows.Select(r => r.ParcelId).ToArray());
        }

        [Fact]
        public void ParcelIds_LeadingZerosAndVeryLongNumbersCompareNumerically()
        {
            Assert.True(PoleStepsTableBuilder.CompareParcelIds("2", "10") < 0);
            Assert.True(PoleStepsTableBuilder.CompareParcelIds("007", "8") < 0);
            Assert.True(PoleStepsTableBuilder.CompareParcelIds("99999999999999999999", "100000000000000000000") < 0);
        }

        [Fact]
        public void ParcelIds_ShorterPrefixFirst_NumbersBeforeText_HandlesLast()
        {
            var ids = new[] { "61580.240.24", "61580.240", "1A2B", "61580.240.24а", "61580.240.3" };
            var sorted = ids.OrderBy(i => i, Comparer<string>.Create(PoleStepsTableBuilder.CompareParcelIds)).ToArray();

            Assert.Equal(new[] { "61580.240", "61580.240.3", "61580.240.24", "61580.240.24а", "1A2B" }, sorted);
        }

        [Fact]
        public void PoleNumbers_NumericBeforeText_AscendingNumerically()
        {
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 1000.0, "ПС-1", 1.0),
                Piece("A", 1000.0, "10", 1.0),
                Piece("A", 1000.0, "9", 1.0)
            });

            Assert.Equal(new[] { "9", "10", "ПС-1" }, table.Rows.Select(r => r.PoleNumber).ToArray());
        }

        [Theory]
        [InlineData("5001", true, 5001.0)]
        [InlineData(" 12 ", true, 12.0)]
        [InlineData("7.5", true, 7.5)]
        [InlineData("1e3", false, 0.0)]
        [InlineData("ПС-1", false, 0.0)]
        [InlineData("", false, 0.0)]
        public void TryParsePoleNumber(string text, bool expectedOk, double expectedValue)
        {
            bool ok = PoleStepsTableBuilder.TryParsePoleNumber(text, out double value);

            Assert.Equal(expectedOk, ok);
            if (ok) Assert.Equal(expectedValue, value);
        }

        [Fact]
        public void Remainder_IsComputedFromRawSquareMeters_NotFromRoundedDecares()
        {
            // Each piece is 36.0004 m2 (0.036 dka when rounded). Rounded values would give
            // 10000 - 0.072 * 1000 = 9928.0; the raw remainder is 9927.9992.
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 10000.0, "1", 36.0004),
                Piece("A", 10000.0, "2", 36.0004)
            });

            Assert.Equal(9927.9992, table.Rows[0].RemainderSqm, 9);
            Assert.NotEqual(9928.0, table.Rows[0].RemainderSqm);
            Assert.Equal(72.0008, table.TotalPieceAreaSqm, 9);
        }

        // ---- coverage validation ----

        [Fact]
        public void FullyCoveredFootprint_IsNotReported()
        {
            var uncovered = PoleStepsTableBuilder.FindUncoveredSteps(
                new[] { new PoleFootprintArea { PoleNumber = "1", AreaSqm = 36.0 } },
                new[] { Piece("A", 500.0, "1", 20.0), Piece("B", 800.0, "1", 16.0) },
                0.001);

            Assert.Empty(uncovered);
        }

        [Fact]
        public void PartlyCoveredFootprint_ReportsTheMissingArea()
        {
            var uncovered = PoleStepsTableBuilder.FindUncoveredSteps(
                new[]
                {
                    new PoleFootprintArea { PoleNumber = "1", AreaSqm = 36.0 },
                    new PoleFootprintArea { PoleNumber = "2", AreaSqm = 36.0 }
                },
                new[] { Piece("A", 500.0, "1", 30.0), Piece("A", 500.0, "2", 36.0) },
                0.001);

            var step = Assert.Single(uncovered);
            Assert.Equal("1", step.PoleNumber);
            Assert.Equal(6.0, step.MissingSqm, 9);
        }

        [Fact]
        public void FootprintWithNoPieces_IsReportedInFull()
        {
            var uncovered = PoleStepsTableBuilder.FindUncoveredSteps(
                new[] { new PoleFootprintArea { PoleNumber = "7", AreaSqm = 36.0 } },
                new PoleStepPiece[0],
                0.001);

            Assert.Equal(36.0, Assert.Single(uncovered).MissingSqm);
        }

        [Fact]
        public void MissingAreaWithinTolerance_IsNotReported()
        {
            var uncovered = PoleStepsTableBuilder.FindUncoveredSteps(
                new[] { new PoleFootprintArea { PoleNumber = "1", AreaSqm = 36.0 } },
                new[] { Piece("A", 500.0, "1", 35.9995) },
                0.001);

            Assert.Empty(uncovered);
        }

        [Fact]
        public void Warning_UsesTheRequiredBulgarianText()
        {
            string text = PoleStepsTableBuilder.FormatUncoveredWarning(
                new UncoveredStep { PoleNumber = "12", MissingSqm = 6.0 });

            Assert.Equal("Стъпката на стълб №12 не е изцяло в избраните имоти (липсват 6.000 м²).", text);
        }

        [Fact]
        public void Warning_UsesInvariantDecimalPointUnderBulgarianCulture()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("bg-BG", false);

                string text = PoleStepsTableBuilder.FormatUncoveredWarning(
                    new UncoveredStep { PoleNumber = "1", MissingSqm = 0.1234 });

                Assert.Contains("липсват 0.123 м²", text);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }
}
