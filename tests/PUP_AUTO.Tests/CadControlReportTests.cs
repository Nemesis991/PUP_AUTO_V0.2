using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;
using PUP_AUTO.DataBridge;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class CadControlReportBuilderTests
    {
        private static Nomenclatures TestNomenclatures(List<string>? warnings = null)
        {
            Action<string>? warn = warnings == null ? (Action<string>?)null : warnings.Add;
            var all = Nomenclatures.Empty(warn);
            all.Vidt.Add("3", "Земеделска територия");
            all.Ntp.Add("2230", "Нива");
            all.Vids.Add("5", "Частна");
            all.PravoVid.Add("1", "Собственост");
            return all;
        }

        private static CadControlReport Build(
            IEnumerable<KeyValuePair<string, double>> drawn, List<string>? warnings = null)
        {
            CadRegisterData data = SyntheticCad.Read(out _);
            return CadControlReportBuilder.Build(drawn, data, TestNomenclatures(warnings), "ЗАГЛАВИЕ");
        }

        private static KeyValuePair<string, double> Drawn(string id, double sqm) => new KeyValuePair<string, double>(id, sqm);

        [Fact]
        public void OneRowPerRight_ParcelDataRepeated()
        {
            CadControlReport report = Build(new[] { Drawn("06433.501.1", 1201.0) });

            Assert.Equal(2, report.Rows.Count);
            Assert.All(report.Rows, r =>
            {
                Assert.Equal("06433.501.1", r.ParcelId);
                Assert.Equal("3 – Земеделска територия", r.Vidt);
                Assert.Equal("2230 – Нива", r.Ntp);
                Assert.Equal("ТЕСТОВА МЕСТНОСТ", r.Mestnost);
                Assert.Equal("VIII", r.Category);
                Assert.Equal(1200.5, r.CadAreaSqm);
                Assert.Equal(1201.0, r.DrawnAreaSqm);
                Assert.Equal(0.5, r.DifferenceSqm!.Value, 6);
                Assert.Equal("5 – Частна", r.Vids);
                Assert.Equal("1 – Собственост", r.PravoVid);
                Assert.Equal("1/2", r.Share);
            });
            Assert.Equal(SyntheticCad.FakeEgn1, report.Rows[0].PersonId);
            Assert.Equal(SyntheticCad.Person1Name, report.Rows[0].PersonName);
            Assert.Equal(SyntheticCad.FakeBulstat, report.Rows[1].PersonId);
            Assert.Equal(SyntheticCad.FirmNameUnescaped, report.Rows[1].PersonName);
            Assert.Equal("ЗАГЛАВИЕ", report.Title);
        }

        [Fact]
        public void KatZero_IsEmpty_AndUnknownCodesShowKodN()
        {
            var warnings = new List<string>();
            CadControlReport report = Build(new[] { Drawn("06433.501.2", 800.0) }, warnings);

            Assert.Equal(2, report.Rows.Count);             // two rights: 8690П and 7497_0006082776
            CadControlRow row = report.Rows[0];
            Assert.Equal("", row.Category);                 // KAT 0
            Assert.Equal("код 2500", row.Ntp);              // not in the nomenclature
            Assert.Equal("код 11", row.Vids);
            Assert.Equal("код 3", row.PravoVid);
            Assert.Equal("8690П", row.PersonId);
            Assert.Equal("1/1", row.Share);
            Assert.Equal("7497_0006082776", report.Rows[1].PersonId);
            Assert.Equal("1/2", report.Rows[1].Share);
            Assert.Equal(0.0, row.DifferenceSqm);

            Assert.Equal(1, warnings.Count(w => w.Contains("код 2500")));
            Assert.Equal(1, warnings.Count(w => w.Contains("код 11")));   // repeated on the second row, still one warning
            Assert.Equal(1, warnings.Count(w => w.Contains("PRAVOVID") && w.Contains("код 3")));
        }

        [Fact]
        public void ParcelWithoutRights_StillGetsOneRow_AndIsListed()
        {
            CadControlReport report = Build(new[] { Drawn("06433.501.3", 10.0) });

            CadControlRow row = Assert.Single(report.Rows);
            Assert.Equal("06433.501.3", row.ParcelId);
            Assert.Equal("", row.PravoVid);
            Assert.Equal("", row.PersonId);
            Assert.Equal("", row.PersonName);
            Assert.Null(row.CadAreaSqm);
            Assert.Null(row.DifferenceSqm);
            Assert.Equal(new[] { "06433.501.3" }, report.WithoutRights);
        }

        [Fact]
        public void NotFoundAndForeignParcels_AreListed_NotInTheRows()
        {
            CadControlReport report = Build(new[]
            {
                Drawn("06433.501.1", 1200.5),
                Drawn("06433.777.7", 5.0),      // this землище, but not in the file
                Drawn("78135.10.1", 5.0),       // another EKATTE
                Drawn("0A1B2C", 5.0)            // an AutoCAD handle: no EKATTE part
            });

            Assert.Equal(2, report.Rows.Count);
            Assert.Equal(new[] { "06433.777.7", "0A1B2C", "78135.10.1" }, report.NotFound.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            var foreign = Assert.Single(report.ForeignEkatte);
            Assert.Equal("78135.10.1", foreign.Key);
            Assert.Equal("78135", foreign.Value);
        }

        [Fact]
        public void ParcelDrawnAsSeveralPolylines_IsSummed()
        {
            CadControlReport report = Build(new[] { Drawn("06433.501.2", 300.0), Drawn("06433.501.2", 500.0) });

            Assert.Equal(2, report.Rows.Count); // its two rights
            Assert.All(report.Rows, r => Assert.Equal(800.0, r.DrawnAreaSqm));
        }

        [Fact]
        public void Rows_AreSortedByParcelNumber_Naturally()
        {
            var ids = new[] { "06433.10.1", "06433.9.1", "06433.9.10", "06433.9.2", "06433.100.1" };

            var sorted = ids.OrderBy(x => x, Comparer<string>.Create(CadControlReportBuilder.CompareParcelIds)).ToArray();

            Assert.Equal(new[] { "06433.9.1", "06433.9.2", "06433.9.10", "06433.10.1", "06433.100.1" }, sorted);
        }

        [Fact]
        public void Share_IsDocId1SlashDocId2_AsRead()
        {
            Assert.Equal("1/2", CadControlReportBuilder.FormatShare("1", "2"));
            Assert.Equal("0/0", CadControlReportBuilder.FormatShare("0", "0"));
            Assert.Equal("3", CadControlReportBuilder.FormatShare("3", ""));
            Assert.Equal("7", CadControlReportBuilder.FormatShare("", "7"));
            Assert.Equal("", CadControlReportBuilder.FormatShare("", " "));
        }
    }

    public class CadControlReportExporterTests : IDisposable
    {
        private readonly string _dir;

        public CadControlReportExporterTests()
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
            public Workbook Workbook = null!;

            public Cell? CellAt(string reference) =>
                Worksheet.Descendants<Cell>().SingleOrDefault(c => c.CellReference!.Value == reference);

            public string? Text(string reference) => CellAt(reference)?.InlineString?.Text?.Text;

            public bool IsNumeric(string reference)
            {
                Cell? c = CellAt(reference);
                return c != null && c.InlineString == null && c.CellValue != null && (c.DataType == null || c.DataType.Value == CellValues.Number);
            }

            public double Number(string reference) =>
                double.Parse(CellAt(reference)!.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);
        }

        private Sheet_ Export(CadControlReport report, out string path)
        {
            path = CadControlReportExporter.Export(report, _dir);
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                var sheet = new Sheet_
                {
                    Workbook = wb.Workbook,
                    Worksheet = wb.WorksheetParts.Single().Worksheet
                };
                // The parts are loaded lazily; force the DOM before the package is closed
                _ = sheet.Worksheet.OuterXml;
                _ = sheet.Workbook.OuterXml;
                return sheet;
            }
        }

        private static CadControlReport SampleReport()
        {
            CadRegisterData data = SyntheticCad.Read(out _);
            var nomenclatures = Nomenclatures.Empty();
            nomenclatures.Ntp.Add("2230", "Нива");
            return CadControlReportBuilder.Build(
                new[]
                {
                    new KeyValuePair<string, double>("06433.501.1", 1201.25),
                    new KeyValuePair<string, double>("06433.501.3", 10.0)
                },
                data, nomenclatures, "НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ");
        }

        [Fact]
        public void WritesFileWithTheContractName()
        {
            Export(SampleReport(), out string path);

            Assert.Equal("Контролна_справка_cad.xlsx", Path.GetFileName(path));
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void TitleRow_ThenTheThirteenHeaders_InTheAgreedOrder()
        {
            Sheet_ s = Export(SampleReport(), out _);

            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A1"));
            string[] expected =
            {
                "Имот", "ТП (код + текст)", "НТП (код + текст)", "Местност", "Категория", "Площ .cad (м²)",
                "Начертана площ (м²)", "Разлика (м²)", "Вид собственост", "Вид право", "Дял (DOCID1/DOCID2)",
                "ЕГН/БУЛСТАТ", "Име"
            };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], s.Text($"{(char)('A' + i)}2"));
            }
        }

        [Fact]
        public void NoMergedCells()
        {
            Sheet_ s = Export(SampleReport(), out _);

            Assert.Empty(s.Worksheet.Descendants<MergeCells>());
            Assert.Empty(s.Worksheet.Descendants<MergeCell>());
        }

        [Fact]
        public void DataRows_OnePerRight_WithParcelDataRepeated()
        {
            Sheet_ s = Export(SampleReport(), out _);

            // 2 rights of 501.1 (rows 3, 4) + the parcel without rights (row 5)
            Assert.Equal("06433.501.1", s.Text("A3"));
            Assert.Equal("06433.501.1", s.Text("A4"));
            Assert.Equal("06433.501.3", s.Text("A5"));
            Assert.Null(s.CellAt("A6"));

            Assert.Equal("код 3", s.Text("B3"));            // VIDT not in the (empty) nomenclature
            Assert.Equal("2230 – Нива", s.Text("C3"));
            Assert.Equal("2230 – Нива", s.Text("C4"));
            Assert.Equal("ТЕСТОВА МЕСТНОСТ", s.Text("D4"));
            Assert.Equal("VIII", s.Text("E3"));
            Assert.Equal("1/2", s.Text("K3"));
            Assert.Equal(SyntheticCad.FirmNameUnescaped, s.Text("M4"));
        }

        [Fact]
        public void Areas_AreNumbers_AndDifferenceIsDrawnMinusCad()
        {
            Sheet_ s = Export(SampleReport(), out _);

            Assert.True(s.IsNumeric("F3"));
            Assert.Equal(1200.5, s.Number("F3"));
            Assert.Equal(1201.25, s.Number("G3"));
            Assert.Equal(0.75, s.Number("H3"));

            // no official area: the cell exists (bordered) but is empty, and so is the difference
            Assert.False(s.IsNumeric("F5"));
            Assert.True(s.IsNumeric("G5"));
            Assert.False(s.IsNumeric("H5"));
        }

        [Fact]
        public void PersonIds_AreTextCells_LeadingZerosKept()
        {
            Sheet_ s = Export(SampleReport(), out _);

            Assert.Equal("0000000001", s.Text("L3"));
            Assert.Equal("000123456", s.Text("L4"));
            Assert.False(s.IsNumeric("L3"));
            Assert.False(s.IsNumeric("L4"));
        }

        [Fact]
        public void HeaderRowIsFrozen_AndAutoFilterCoversTheTable()
        {
            Sheet_ s = Export(SampleReport(), out _);

            Pane pane = s.Worksheet.Descendants<Pane>().Single();
            Assert.Equal(2D, pane.VerticalSplit!.Value);
            Assert.Equal("A2:M5", s.Worksheet.Descendants<AutoFilter>().Single().Reference!.Value);
        }

        [Fact]
        public void EmptyReport_WritesJustTitleAndHeader()
        {
            var report = new CadControlReport { Title = "T" };

            Sheet_ s = Export(report, out _);

            Assert.Equal("T", s.Text("A1"));
            Assert.Equal("Имот", s.Text("A2"));
            Assert.Null(s.CellAt("A3"));
        }
    }
}
