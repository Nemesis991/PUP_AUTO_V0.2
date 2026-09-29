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
            Assert.Equal(1.0, row.ParcelAreaDka);
            Assert.Equal("5", row.PoleNumber);
            Assert.Equal(0.036, row.PieceAreaDka);
            Assert.Equal(0.964, row.RemainderDka);
            Assert.True(row.IsFirstOfParcel);
            Assert.Equal(1, row.ParcelRowCount);
            Assert.Equal(0.036, table.TotalPieceAreaDka);
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
            Assert.Equal(0.48, table.Rows[0].RemainderDka);
            Assert.Equal(0.784, table.Rows[1].RemainderDka);
            Assert.All(table.Rows, r => Assert.True(r.IsFirstOfParcel));
            Assert.Equal(0.036, table.TotalPieceAreaDka);
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
            Assert.All(table.Rows, r => Assert.Equal(0.93, r.RemainderDka));

            Assert.True(table.Rows[0].IsFirstOfParcel);
            Assert.Equal(2, table.Rows[0].ParcelRowCount);
            Assert.False(table.Rows[1].IsFirstOfParcel);
            Assert.Equal(0, table.Rows[1].ParcelRowCount);
            Assert.Equal(0.07, table.TotalPieceAreaDka);
        }

        [Fact]
        public void NoPieces_GivesEmptyTable()
        {
            var table = PoleStepsTableBuilder.Build(new PoleStepPiece[0]);

            Assert.Empty(table.Rows);
            Assert.Equal(0.0, table.TotalPieceAreaDka);
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
        [InlineData("7.5", false, 0.0)]      // not a whole number: written as text
        [InlineData("1e3", false, 0.0)]
        [InlineData("ПС-1", false, 0.0)]
        [InlineData("", false, 0.0)]
        public void TryParsePoleNumber(string text, bool expectedOk, double expectedValue)
        {
            bool ok = PoleStepsTableBuilder.TryParsePoleNumber(text, out double value);

            Assert.Equal(expectedOk, ok);
            if (ok) Assert.Equal(expectedValue, value);
        }

        // ---- remainder adds up on paper: round(parcel) - SUM(round(each piece)) ----

        [Fact]
        public void Remainder_RealCase_5380_minus_0014_is_5366()
        {
            // raw: 5380.2 m2 - 13.5 m2 = 5366.7 m2 -> 5.367 dka (does not add up on paper).
            // printed: 5.380 - 0.014 = 5.366.
            var table = PoleStepsTableBuilder.Build(new[] { Piece("61580.421.8", 5380.2, "1", 13.5) });

            var row = Assert.Single(table.Rows);
            Assert.Equal(5.380, row.ParcelAreaDka);
            Assert.Equal(0.014, row.PieceAreaDka);
            Assert.Equal(5.366, row.RemainderDka);
        }

        [Fact]
        public void Remainder_RealCase_6620_minus_0014_is_6606()
        {
            // raw: 6620.3 - 13.6 = 6606.7 m2 -> 6.607 dka; printed: 6.620 - 0.014 = 6.606.
            var table = PoleStepsTableBuilder.Build(new[] { Piece("61580.421.11", 6620.3, "2", 13.6) });

            var row = Assert.Single(table.Rows);
            Assert.Equal(6.620, row.ParcelAreaDka);
            Assert.Equal(0.014, row.PieceAreaDka);
            Assert.Equal(6.606, row.RemainderDka);
        }

        [Fact]
        public void Remainder_ParcelWithTwoPoles_SubtractsBothPrintedValues()
        {
            // raw remainder 1000.0 - 30.4 - 40.4 = 929.2 m2 -> 0.929 dka; printed 1.000 - 0.030 - 0.040 = 0.930.
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 1000.0, "1", 30.4),
                Piece("A", 1000.0, "2", 40.4)
            });

            Assert.Equal(new[] { 0.030, 0.040 }, table.Rows.Select(r => r.PieceAreaDka).ToArray());
            Assert.All(table.Rows, r => Assert.Equal(1.0, r.ParcelAreaDka));
            Assert.All(table.Rows, r => Assert.Equal(0.930, r.RemainderDka));
        }

        [Fact]
        public void EveryParcel_AddsUpExactlyOnPaper()
        {
            var rnd = new Random(12345);
            var pieces = new List<PoleStepPiece>();
            for (int parcel = 0; parcel < 40; parcel++)
            {
                double parcelArea = 500 + rnd.NextDouble() * 20000;
                int poles = 1 + rnd.Next(3);
                for (int p = 0; p < poles; p++)
                    pieces.Add(Piece("P" + parcel, parcelArea, (p + 1).ToString(), 5 + rnd.NextDouble() * 40));
            }

            var table = PoleStepsTableBuilder.Build(pieces);

            foreach (var group in table.Rows.GroupBy(r => r.ParcelId))
            {
                decimal steps = group.Sum(r => (decimal)r.PieceAreaDka);
                decimal printed = (decimal)group.First().ParcelAreaDka - steps;
                Assert.Equal(printed, (decimal)group.First().RemainderDka);
            }
        }

        [Fact]
        public void Total_IsTheSumOfThePrintedStepValues()
        {
            // 13.5 m2 prints 0.014 (three times = 0.042); the raw sum 40.5 m2 would print 0.041.
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 5000.0, "1", 13.5),
                Piece("B", 5000.0, "2", 13.5),
                Piece("C", 5000.0, "3", 13.5)
            });

            Assert.Equal(0.042, table.TotalPieceAreaDka);
        }

        // ---- pole number label ----

        [Fact]
        public void PoleNumber_HasThePrefixStripped()
        {
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 1000.0, "Стълб №162", 5.0),
                Piece("A", 1000.0, "Стълб № 20", 5.0),
                Piece("A", 1000.0, "ПС-1", 5.0)
            });

            Assert.Equal(new[] { "20", "162", "ПС-1" }, table.Rows.Select(r => r.PoleNumber).ToArray());
        }

        [Fact]
        public void PolePrefix_ComesFromTheCentralConstant()
        {
            Assert.Equal("Стълб №", PoleLabels.Prefix);
            Assert.Equal("162", PoleLabels.StripPrefix(PoleLabels.Prefix + "162"));
            Assert.Equal("162", PoleLabels.StripPrefix("162"));
            Assert.Equal("162", PoleLabels.StripPrefix("  стълб №162 "));
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

            // the "Стълб №" label is not repeated
            Assert.Equal(text, PoleStepsTableBuilder.FormatUncoveredWarning(
                new UncoveredStep { PoleNumber = "Стълб №12", MissingSqm = 6.0 }));
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
