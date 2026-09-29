using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Golden tests: pin the CURRENT content of MVP_Math_Test_Parcels.xlsx.</summary>
    public class BasicExcelExporterTests : IDisposable
    {
        private static readonly string[] ExpectedHeaders =
        {
            "1. Номер на имот",
            "2. Площ на имота в дка",
            "3. Брутна площ с ограничение в дка",
            "4. Нетна площ с ограничение в дка",
            "5. Площ на стълба в дка",
            "6. Остатък в дка",
            "7. Математическа разлика в дка",
            "8. Номер на стълба"
        };

        private readonly string _dir;

        public BasicExcelExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private (List<string[]> Rows, List<string> Merges, string SheetName) Export(params ParcelData[] parcels)
        {
            BasicExcelExporter.ExportMathTest(parcels.ToList(), _dir);

            string path = Path.Combine(_dir, "MVP_Math_Test_Parcels.xlsx");
            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                string sheetName = wb.Workbook.Descendants<Sheet>().Single().Name!.Value!;
                Worksheet ws = ((WorksheetPart)wb.GetPartById(wb.Workbook.Descendants<Sheet>().Single().Id!)).Worksheet;

                var rows = ws.Descendants<Row>()
                    .Select(r => r.Elements<Cell>().Select(c => c.InlineString?.Text?.Text ?? string.Empty).ToArray())
                    .ToList();
                var merges = ws.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();
                return (rows, merges, sheetName);
            }
        }

        private static ParcelData Parcel(string id, double total, double gross, double net, double pole, params string[] poles)
        {
            var p = new ParcelData
            {
                ParcelId = id,
                TotalAreaSqm = total,
                ServitudeGrossAreaSqm = gross,
                ServitudeNetAreaSqm = net,
                PoleAreaSqm = pole
            };
            p.AssignedPoleNumbers = poles.ToList();
            return p;
        }

        [Fact]
        public void FileAndSheetNames()
        {
            var (_, _, sheetName) = Export();

            Assert.True(File.Exists(Path.Combine(_dir, "MVP_Math_Test_Parcels.xlsx")));
            Assert.Equal("Math Test", sheetName);
        }

        [Fact]
        public void Header_UsesBulgarianTexts()
        {
            var (rows, _, _) = Export();

            Assert.Single(rows);
            Assert.Equal(ExpectedHeaders, rows[0]);
        }

        [Fact]
        public void SinglePole_RowValues()
        {
            // gross 5000 = net 4000 + pole 1000 -> balanced. Remainder = 10000 - 4000 - 1000.
            var (rows, merges, _) = Export(Parcel("61513.100.5", 10000.0, 5000.0, 4000.0, 1000.0, "P1"));

            Assert.Equal(2, rows.Count);
            Assert.Equal(
                new[] { "61513.100.5", "10.000", "5.000", "4.000", "1.000", "5.000", "0.000 - ОК", "P1" },
                rows[1]);
            Assert.Empty(merges);
        }

        [Fact]
        public void NoPole_NetAreaIsZeroAndPoleNumberEmpty()
        {
            // Net is 4000 in the data, but with no pole the column is forced to "0.000".
            var (rows, _, _) = Export(Parcel("A", 10000.0, 4000.0, 4000.0, 0.0));

            Assert.Equal(
                new[] { "A", "10.000", "4.000", "0.000", "0.000", "6.000", "0.000 - ОК", "" },
                rows[1]);
        }

        [Fact]
        public void NetAreaOnlyForcedToZeroWhenPoleAreaIsAtMostOneThousandthSqm()
        {
            var (rows, _, _) = Export(
                Parcel("A", 10000.0, 4000.0, 4000.0, 0.001),   // not > 0.001 -> forced to 0.000
                Parcel("B", 10000.0, 4000.0, 4000.0, 0.0011)); // > 0.001 -> real net

            Assert.Equal("0.000", rows[1][3]);
            Assert.Equal("4.000", rows[2][3]);
        }

        [Theory]
        [InlineData(1000.0, 1000.0, 0.0, "ОК")]        // diff 0
        [InlineData(1000.001, 1000.0, 0.0, "ОК")]      // diff ~0.001 (not greater) -> OK
        [InlineData(1000.0, 1000.001, 0.0, "ОК")]      // diff ~-0.001 -> OK
        [InlineData(1000.0011, 1000.0, 0.0, "ГРЕШКА")] // diff 0.0011 > 0.001
        [InlineData(1000.0, 1000.0011, 0.0, "ГРЕШКА")]
        [InlineData(1000.0, 900.0, 50.0, "ГРЕШКА")]    // gross != net + pole
        public void BalanceCheck_BoundaryIsOneThousandthSqm(double gross, double net, double pole, string expectedStatus)
        {
            var (rows, _, _) = Export(Parcel("A", 100000.0, gross, net, pole, pole > 0 ? new[] { "P1" } : new string[0]));

            Assert.EndsWith(" - " + expectedStatus, rows[1][6]);
        }

        [Fact]
        public void BalanceCheck_UsesRawSqmNotRoundedDecares()
        {
            // 0.0011 m2 is 0.0000011 dka -> displays as 0.000, yet the status is ГРЕШКА.
            var (rows, _, _) = Export(Parcel("A", 100000.0, 1000.0011, 1000.0, 0.0));

            Assert.Equal("0.000 - ГРЕШКА", rows[1][6]);
        }

        [Fact]
        public void MultiplePoles_OneRowPerPoleWithMergedCells()
        {
            var parcel = Parcel("A", 10000.0, 5000.0, 4000.0, 1000.0, "P1", "P2");
            parcel.IndividualPoleAreas["P1"] = 600.0;
            parcel.IndividualPoleAreas["P2"] = 400.0;

            var (rows, merges, _) = Export(parcel);

            Assert.Equal(3, rows.Count);
            Assert.Equal(
                new[] { "A", "10.000", "5.000", "4.000", "0.600", "5.000", "0.000 - ОК", "P1" },
                rows[1]);
            Assert.Equal(
                new[] { "", "", "", "", "0.400", "", "", "P2" },
                rows[2]);
            Assert.Equal(new[] { "A2:A3", "B2:B3", "C2:C3", "D2:D3", "F2:F3", "G2:G3" }, merges);
        }

        [Fact]
        public void MultiplePoles_MissingIndividualAreaIsZero()
        {
            var parcel = Parcel("A", 10000.0, 5000.0, 4000.0, 1000.0, "P1", "P2");
            parcel.IndividualPoleAreas["P1"] = 1000.0;

            var (rows, _, _) = Export(parcel);

            Assert.Equal("1.000", rows[1][4]);
            Assert.Equal("0.000", rows[2][4]);
        }

        [Fact]
        public void MergeRanges_FollowRowsOfPrecedingParcels()
        {
            var multi = Parcel("M", 10000.0, 5000.0, 4000.0, 1000.0, "P1", "P2");
            var (rows, merges, _) = Export(
                Parcel("X", 10000.0, 0.0, 0.0, 0.0),
                multi,
                Parcel("Y", 10000.0, 0.0, 0.0, 0.0));

            Assert.Equal(5, rows.Count);
            Assert.Equal("M", rows[2][0]);
            Assert.Equal("Y", rows[4][0]);
            Assert.Equal(new[] { "A3:A4", "B3:B4", "C3:C4", "D3:D4", "F3:F4", "G3:G4" }, merges);
        }

        [Fact]
        public void RowOrderFollowsInputOrder()
        {
            var (rows, _, _) = Export(
                Parcel("Z", 1000.0, 0.0, 0.0, 0.0),
                Parcel("A", 1000.0, 0.0, 0.0, 0.0));

            Assert.Equal("Z", rows[1][0]);
            Assert.Equal("A", rows[2][0]);
        }
    }
}
