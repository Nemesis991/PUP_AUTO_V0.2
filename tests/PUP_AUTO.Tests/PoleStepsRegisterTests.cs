using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Synthetic data only (fake people, made-up numbers), on top of <see cref="RegisterFixture"/>.</summary>
    internal static class StepsFixture
    {
        public static Dictionary<string, double> Drawn(params (string Id, double Sqm)[] parcels)
        {
            var map = new Dictionary<string, double>();
            foreach (var p in parcels)
            {
                map.TryGetValue(p.Id, out double sum);
                map[p.Id] = sum + p.Sqm;
            }
            return map;
        }

        /// <summary>The default scenario of the affected register: 113 and 114 in 10.1, 114 also in 100.7.</summary>
        public static PoleStepsRegister Build(
            IEnumerable<PoleStepPiece>? pieces = null,
            IReadOnlyDictionary<string, double>? drawn = null,
            CadRegisterData? data = null,
            string project = "НОВА ВЛ 110kV")
        {
            return PoleStepsRegisterBuilder.Build(
                pieces ?? new[]
                {
                    RegisterFixture.Piece("06433.10.1", "Стълб №114", 12.0),   // unsorted on purpose
                    RegisterFixture.Piece("06433.10.1", "Стълб №113", 14.0),
                    RegisterFixture.Piece("06433.100.7", "Стълб №114", 8.0)
                },
                drawn ?? Drawn(("06433.10.1", 5380.0), ("06433.100.7", 1500.0), ("06433.9.2", 800.0)),
                data ?? RegisterFixture.Register(),
                RegisterFixture.Nomenclatures(),
                project,
                "НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ");
        }
    }

    public class PoleStepsRegisterBuilderTests
    {
        [Fact]
        public void Titles()
        {
            PoleStepsRegister r = StepsFixture.Build();

            Assert.Equal("РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ НОВА ВЛ 110kV", r.Title);
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", r.Subtitle);
        }

        [Fact]
        public void OnePoleInTwoParcels_IsTwoBlocksOrderedByParcelId_AndCountedOnce()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[]
                {
                    RegisterFixture.Piece("06433.10.1", "Стълб №103", 12.0),
                    RegisterFixture.Piece("06433.9.2", "Стълб №103", 5.0)
                });

            // 9.2 before 10.1 (numeric, not text order); 10.1 has a second owner row
            Assert.Equal(new[] { "06433.9.2", "06433.10.1", "" }, r.Rows.Select(x => x.ParcelId).ToArray());
            Assert.Equal(new[] { "Стълб №103", "Стълб №103", "" }, r.Rows.Select(x => x.PoleLabel).ToArray());
            Assert.Equal(new[] { true, true, false }, r.Rows.Select(x => x.IsFirstOfBlock).ToArray());
            Assert.Equal(1, r.PoleCount);
        }

        [Fact]
        public void OneParcelWithTwoPoles_IsTwoBlocks_WithParcelDataAndOwnersRepeated()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[]
                {
                    RegisterFixture.Piece("06433.10.1", "Стълб №114", 12.0),
                    RegisterFixture.Piece("06433.10.1", "Стълб №113", 14.0)
                });

            Assert.Equal(4, r.Rows.Count);
            Assert.Equal(new[] { "Стълб №113", "", "Стълб №114", "" }, r.Rows.Select(x => x.PoleLabel).ToArray());

            foreach (int i in new[] { 0, 2 })
            {
                PoleStepsRegisterRow row = r.Rows[i];
                Assert.Equal("06433.10.1", row.ParcelId);
                Assert.Equal("", row.Subdivisions);
                Assert.Equal("Земеделска територия", row.Vidt);
                Assert.Equal("За селскостопански, горски, ведомствен път", row.Ntp);
                Assert.Equal("ТЕСТОВА", row.Mestnost);
                Assert.Equal("VIII", row.Category);
                Assert.Equal(5.380, row.AreaDka);
                Assert.Equal("Частна", row.Vids);
                Assert.Equal(RegisterFixture.OwnerA, row.PersonId);
                Assert.Equal("ТЕСТОВ АЛФА", row.PersonName);

                PoleStepsRegisterRow next = r.Rows[i + 1];
                Assert.Equal(RegisterFixture.OwnerB, next.PersonId);
                Assert.Equal("\"АГРО\" ЕООД", next.PersonName);
            }
            Assert.Equal(0.014, r.Rows[0].PieceDka);
            Assert.Equal(0.012, r.Rows[2].PieceDka);
            Assert.Equal(2, r.PoleCount);
        }

        [Fact]
        public void Owners_OwnershipOnly_NoDuplicates_HeirsPrefix_ContinuationRowsHaveOnlyColumns11And12()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[]
                {
                    RegisterFixture.Piece("06433.9.2", "Стълб №1", 5.0),
                    RegisterFixture.Piece("06433.10.1", "Стълб №2", 5.0)
                });

            // 9.2: a single heirs owner
            Assert.Equal("н-ци на ФИКТИВЕН НАСЛЕДНИК", r.Rows[0].PersonName);
            Assert.Equal(RegisterFixture.HeirsId, r.Rows[0].PersonId);

            // 10.1: owner A (the duplicate right is dropped), owner B; the tenant (PRAVOVID 3) is not an owner
            Assert.Equal(3, r.Rows.Count);
            Assert.Equal(RegisterFixture.OwnerA, r.Rows[1].PersonId);
            PoleStepsRegisterRow cont = r.Rows[2];
            Assert.Equal(RegisterFixture.OwnerB, cont.PersonId);
            Assert.Equal("", cont.PoleLabel);
            Assert.Null(cont.PieceDka);
            Assert.Equal("", cont.ParcelId);
            Assert.Equal("", cont.Subdivisions);
            Assert.Equal("", cont.Vidt);
            Assert.Equal("", cont.Ntp);
            Assert.Equal("", cont.Mestnost);
            Assert.Equal("", cont.Category);
            Assert.Null(cont.AreaDka);
            Assert.Equal("", cont.Vids);                // column 10 is NOT repeated on continuation rows
        }

        [Fact]
        public void ParcelWithoutOwners_HasVidsAndNoPerson_AndIsReported()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[] { RegisterFixture.Piece("06433.100.7", "Стълб №114", 8.0) });

            Assert.Single(r.Rows);
            PoleStepsRegisterRow row = r.Rows[0];
            Assert.Equal("Общинска публична", row.Vids);
            Assert.Equal("", row.PersonId);
            Assert.Equal("", row.PersonName);
            Assert.Equal("", row.Category);             // KAT 0
            Assert.Equal(new[] { "06433.100.7" }, r.WithoutOwners);
            Assert.Empty(r.NotFound);
        }

        [Fact]
        public void ParcelNotInTheCad_HasOnlyColumns1_2_3_And9_AndIsReported()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[] { RegisterFixture.Piece("06433.999.9", "Стълб №7", 20.0) },
                drawn: StepsFixture.Drawn(("06433.999.9", 100.0)));

            Assert.Single(r.Rows);
            PoleStepsRegisterRow row = r.Rows[0];
            Assert.Equal("Стълб №7", row.PoleLabel);
            Assert.Equal(0.020, row.PieceDka);
            Assert.Equal("06433.999.9", row.ParcelId);
            Assert.Equal(0.100, row.AreaDka);
            Assert.Equal("", row.Subdivisions);
            Assert.Equal("", row.Vidt);
            Assert.Equal("", row.Ntp);
            Assert.Equal("", row.Mestnost);
            Assert.Equal("", row.Category);
            Assert.Equal("", row.Vids);
            Assert.Equal("", row.PersonId);
            Assert.Equal("", row.PersonName);
            Assert.Equal(new[] { "06433.999.9" }, r.NotFound);
            Assert.Empty(r.WithoutOwners);
        }

        [Fact]
        public void Rounding_PerParcelSumOfColumn2_EqualsColumn11OfTheAffectedRegister_AndTheTotalIsADecimalSum()
        {
            var pieces = new[]
            {
                RegisterFixture.Piece("06433.10.1", "Стълб №113", 27.0),   // 0.027
                RegisterFixture.Piece("06433.10.1", "Стълб №114", 9.0),    // 0.009
                RegisterFixture.Piece("06433.100.7", "Стълб №114", 8.4)    // 0.008
            };

            PoleStepsRegister steps = StepsFixture.Build(pieces: pieces);
            AffectedRegister affected = RegisterFixture.Build(
                parcels: new[]
                {
                    RegisterFixture.Areas("06433.10.1", 5380.0, 1200.0),
                    RegisterFixture.Areas("06433.100.7", 1500.0, 400.0)
                },
                pieces: pieces);

            foreach (string id in new[] { "06433.10.1", "06433.100.7" })
            {
                decimal sum = steps.Rows.Where(x => x.IsFirstOfBlock && x.ParcelId == id).Sum(x => (decimal)x.PieceDka!.Value);
                double step = affected.Rows.Single(x => x.IsFirstOfParcel && x.Number == id).StepDka!.Value;
                Assert.Equal((decimal)step, sum);
            }

            Assert.Equal(0.044, steps.TotalPieceDka);   // 0.027 + 0.009 + 0.008, no floating-point residue
            Assert.Equal(0.036m, steps.Rows.Where(x => x.ParcelId == "06433.10.1").Sum(x => (decimal)x.PieceDka!.Value));
        }

        [Fact]
        public void Column9_UsesTheSummedDrawnAreaOfTheParcelId_NotThePieceParcelArea()
        {
            // the parcel is drawn as two polylines (500 + 300 m²); the piece only knows one of them
            var piece = RegisterFixture.Piece("06433.9.2", "Стълб №1", 5.0);
            piece.ParcelAreaSqm = 300.0;

            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[] { piece },
                drawn: StepsFixture.Drawn(("06433.9.2", 500.0), ("06433.9.2", 300.0)));

            Assert.Equal(0.800, r.Rows[0].AreaDka);
        }

        [Fact]
        public void SamePoleTwiceInTheSameParcelId_IsOneBlock_WithThePiecesAddedBeforeRounding()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[]
                {
                    RegisterFixture.Piece("06433.9.2", "Стълб №5", 10.6),
                    RegisterFixture.Piece("06433.9.2", "Стълб №5", 10.6)
                });

            Assert.Single(r.Rows);
            Assert.Equal(0.021, r.Rows[0].PieceDka);     // 21.2 m², not 0.011 + 0.011
            Assert.Equal(0.021, r.TotalPieceDka);
            Assert.Equal(1, r.PoleCount);
        }

        [Fact]
        public void PolesAreOrderedNumerically_ThenByParcel()
        {
            PoleStepsRegister r = StepsFixture.Build(
                pieces: new[]
                {
                    RegisterFixture.Piece("06433.100.7", "Стълб №9", 1.0),
                    RegisterFixture.Piece("06433.9.2", "Стълб №10", 1.0),
                    RegisterFixture.Piece("06433.100.7", "Стълб №10", 1.0)
                });

            Assert.Equal(
                new[] { ("Стълб №9", "06433.100.7"), ("Стълб №10", "06433.9.2"), ("Стълб №10", "06433.100.7") },
                r.Rows.Where(x => x.IsFirstOfBlock).Select(x => (x.PoleLabel, x.ParcelId)).ToArray());
        }

        [Fact]
        public void NoPieces_IsAnEmptyRegister()
        {
            PoleStepsRegister r = StepsFixture.Build(pieces: new PoleStepPiece[0]);

            Assert.Empty(r.Rows);
            Assert.Equal(0, r.PoleCount);
            Assert.Equal(0.0, r.TotalPieceDka);
        }
    }

    public class PoleStepsRegisterExporterTests : IDisposable
    {
        private readonly string _dir;

        public PoleStepsRegisterExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private sealed class Sheet_
        {
            public Worksheet Worksheet = null!;
            public Stylesheet Styles = null!;
            public DefinedNames? DefinedNames;

            public Cell CellAt(string reference) =>
                Worksheet.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);

            public bool Exists(string reference) =>
                Worksheet.Descendants<Cell>().Any(c => c.CellReference!.Value == reference);

            public string? Text(string reference) => CellAt(reference).InlineString?.Text?.Text;

            public bool IsEmpty(string reference)
            {
                Cell c = CellAt(reference);
                return c.InlineString == null && c.CellValue == null;
            }

            public bool IsNumeric(string reference)
            {
                Cell c = CellAt(reference);
                return c.InlineString == null && c.CellValue != null && (c.DataType == null || c.DataType.Value == CellValues.Number);
            }

            public double Number(string reference) =>
                double.Parse(CellAt(reference).CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);

            public string? NumberFormatCode(string reference)
            {
                CellFormat format = Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)CellAt(reference).StyleIndex!.Value);
                uint id = format.NumberFormatId?.Value ?? 0U;
                if (id == 1U) return "0";
                if (id == 49U) return "@";
                return Styles.NumberingFormats?.Elements<NumberingFormat>()
                    .FirstOrDefault(n => n.NumberFormatId!.Value == id)?.FormatCode?.Value;
            }

            public List<string> Merges() =>
                Worksheet.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();
        }

        private Sheet_ Export(PoleStepsRegister? register = null)
        {
            string path = PoleStepsRegisterExporter.Export(register ?? StepsFixture.Build(), _dir);
            Assert.Equal(Path.Combine(_dir, "Регистър_на_стъпките_на_стълбовете.xlsx"), path);

            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var wb = doc.WorkbookPart!;
                var sheet = wb.Workbook.Descendants<Sheet>().Single();
                Assert.Equal("Регистър стъпки", sheet.Name!.Value);
                return new Sheet_
                {
                    Worksheet = ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet,
                    Styles = wb.WorkbookStylesPart!.Stylesheet,
                    DefinedNames = wb.Workbook.DefinedNames
                };
            }
        }

        private static readonly string[] Columns = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" };

        [Fact]
        public void TitleRows_GroupRow_Headers_AndTheNumbersRow()
        {
            Sheet_ s = Export();

            Assert.Equal("РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ НОВА ВЛ 110kV", s.Text("A1"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A2"));

            Assert.Equal("Стълб", s.Text("A3"));
            Assert.Equal("Собственик", s.Text("J3"));
            foreach (string c in new[] { "B", "C", "D", "E", "F", "G", "H", "I", "K", "L" })
            {
                Assert.True(s.IsEmpty(c + "3"), c + "3");
            }

            string[] headers =
            {
                "Номер на стълба", "Площ на стъпката в имота [дка]", "Номер на имот", "Подотдели",
                "Трайно предназначение на територията", "Нов НТП", "Местност", "Категория", "Площ на имота в дка",
                "Вид собственост", "ЕГН/БУЛСТАТ", "Име"
            };
            for (int i = 0; i < 12; i++)
            {
                Assert.Equal(headers[i], s.Text(Columns[i] + "4"));
                Assert.True(s.IsNumeric(Columns[i] + "5"));
                Assert.Equal(i + 1, s.Number(Columns[i] + "5"));
            }
            Assert.False(s.Exists("M4"));
        }

        [Fact]
        public void OnlyTheTwoGroupHeadersAreMerged()
        {
            Sheet_ s = Export();

            Assert.Equal(new[] { "A3:B3", "J3:L3" }, s.Merges().ToArray());
        }

        [Fact]
        public void PanesAndPrintTitles()
        {
            Sheet_ s = Export();

            Pane pane = s.Worksheet.Descendants<Pane>().Single();
            Assert.Equal(5D, pane.VerticalSplit!.Value);
            Assert.Equal("A6", pane.TopLeftCell!.Value);

            DefinedName printTitles = s.DefinedNames!.Elements<DefinedName>().Single();
            Assert.Equal("_xlnm.Print_Titles", printTitles.Name!.Value);
            Assert.Equal("'Регистър стъпки'!$3:$5", printTitles.Text);
        }

        [Fact]
        public void DataRows_BlocksAndContinuationRows()
        {
            Sheet_ s = Export();

            // rows 6-7: pole 113 in 10.1 (owner A + owner B); rows 8-9: pole 114 in 10.1; row 10: pole 114 in 100.7 (no owner)
            Assert.Equal("Стълб №113", s.Text("A6"));
            Assert.Equal(0.014, s.Number("B6"));
            Assert.Equal("06433.10.1", s.Text("C6"));
            Assert.True(s.IsEmpty("D6"));
            Assert.Equal("Земеделска територия", s.Text("E6"));
            Assert.Equal("VIII", s.Text("H6"));
            Assert.Equal(5.38, s.Number("I6"));
            Assert.Equal("Частна", s.Text("J6"));
            Assert.Equal("0000000001", s.Text("K6"));
            Assert.Equal("ТЕСТОВ АЛФА", s.Text("L6"));

            foreach (string c in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" })
            {
                Assert.True(s.IsEmpty(c + "7"), c + "7");
            }
            Assert.Equal("000123456", s.Text("K7"));
            Assert.Equal("\"АГРО\" ЕООД", s.Text("L7"));

            Assert.Equal("Стълб №114", s.Text("A8"));
            Assert.Equal("Стълб №114", s.Text("A10"));
            Assert.Equal("06433.100.7", s.Text("C10"));
            Assert.Equal("Общинска публична", s.Text("J10"));
            Assert.True(s.IsEmpty("K10"));
            Assert.True(s.IsEmpty("L10"));
        }

        [Fact]
        public void Ids_AreTextCells_WithTheTextFormat_AndAreasAreNumbersWithThreeDecimals()
        {
            Sheet_ s = Export();

            Assert.False(s.IsNumeric("K6"));
            Assert.Equal("@", s.NumberFormatCode("K6"));
            Assert.False(s.IsNumeric("C6"));

            Assert.True(s.IsNumeric("B6"));
            Assert.True(s.IsNumeric("I6"));
            Assert.Equal("0.000", s.NumberFormatCode("B6"));
            Assert.Equal("0.000", s.NumberFormatCode("I6"));
            Assert.Equal("0", s.NumberFormatCode("A5"));
        }

        [Fact]
        public void CountRow_HasTheDistinctPoleCount_AndTheSumOfThePieces()
        {
            Sheet_ s = Export();

            // 5 data rows (6-10), count row 11; pole 114 stands in two parcels but counts once
            Assert.Equal("Брой: 2", s.Text("A11"));
            Assert.Equal(0.034, s.Number("B11"));
            Assert.Equal("0.000", s.NumberFormatCode("B11"));
            foreach (string c in new[] { "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" })
            {
                Assert.True(s.IsEmpty(c + "11"), c + "11");
            }
            Assert.False(s.Exists("A12"));
        }
    }
}
