using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Golden tests of Стъпки_на_стълбове.xlsx.</summary>
    public class PoleStepsExporterTests : IDisposable
    {
        private readonly string _dir;

        public PoleStepsExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static PoleStepPiece Piece(string parcel, double parcelArea, string pole, double pieceArea) =>
            new PoleStepPiece { ParcelId = parcel, ParcelAreaSqm = parcelArea, PoleNumber = pole, PieceAreaSqm = pieceArea };

        private sealed class Sheet_
        {
            public Workbook Workbook = null!;
            public Worksheet Worksheet = null!;
            public Stylesheet Styles = null!;
            public string Name = "";

            public Row RowAt(int index) => Worksheet.Descendants<Row>().Single(r => r.RowIndex!.Value == (uint)index);
            public Cell CellAt(string reference) => Worksheet.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);

            public string? Text(string reference) => CellAt(reference).InlineString?.Text?.Text;

            public bool IsNumeric(string reference)
            {
                Cell c = CellAt(reference);
                return c.InlineString == null && c.CellValue != null && (c.DataType == null || c.DataType.Value == CellValues.Number);
            }

            public double Number(string reference) =>
                double.Parse(CellAt(reference).CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);

            public CellFormat Format(string reference) =>
                Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)CellAt(reference).StyleIndex!.Value);

            public string? NumberFormatCode(string reference)
            {
                uint id = Format(reference).NumberFormatId?.Value ?? 0U;
                if (id == 1U) return "0"; // built-in format
                return Styles.NumberingFormats?.Elements<NumberingFormat>()
                    .FirstOrDefault(n => n.NumberFormatId!.Value == id)?.FormatCode?.Value;
            }

            public Font FontOf(string reference) =>
                Styles.Fonts!.Elements<Font>().ElementAt((int)Format(reference).FontId!.Value);

            public Border BorderOf(string reference) =>
                Styles.Borders!.Elements<Border>().ElementAt((int)Format(reference).BorderId!.Value);

            public List<string> Merges() =>
                Worksheet.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();
        }

        private Sheet_ Export(params PoleStepPiece[] pieces)
        {
            var table = PoleStepsTableBuilder.Build(pieces);
            string path = PoleStepsExporter.Export(table, _dir);
            Assert.Equal(Path.Combine(_dir, "Стъпки_на_стълбове.xlsx"), path);

            // Read fully into memory so the temp file is not left locked
            var doc = SpreadsheetDocument.Open(path, false);
            using (doc)
            {
                var wb = doc.WorkbookPart!;
                var sheet = wb.Workbook.Descendants<Sheet>().Single();
                return new Sheet_
                {
                    Workbook = wb.Workbook,
                    Name = sheet.Name!.Value!,
                    Worksheet = ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet,
                    Styles = wb.WorkbookStylesPart!.Stylesheet
                };
            }
        }

        [Fact]
        public void SheetName_TitleAndHeader()
        {
            var s = Export(Piece("A", 1000.0, "5", 36.0));

            Assert.Equal("Стъпки", s.Name);

            Assert.Equal("Площи на стъпките на стълбове", s.Text("A1"));
            Assert.Contains("A1:E1", s.Merges());
            Assert.True(s.FontOf("A1").Bold != null);
            Assert.True(s.FontOf("A1").FontSize!.Val!.Value > s.FontOf("A2").FontSize!.Val!.Value);

            Assert.Equal("Имот", s.Text("A2"));
            Assert.Equal("Площ на имота (дка)", s.Text("B2"));
            Assert.Equal("Стълб №", s.Text("C2"));
            Assert.Equal("Площ на стъпката (дка)", s.Text("D2"));
            Assert.Equal("Остатък без стъпката (дка)", s.Text("E2"));
        }

        [Fact]
        public void Header_IsBoldGrayCenteredWrappedWithBorders()
        {
            var s = Export(Piece("A", 1000.0, "5", 36.0));

            foreach (string cell in new[] { "A2", "B2", "C2", "D2", "E2" })
            {
                Assert.NotNull(s.FontOf(cell).Bold);

                var fill = s.Styles.Fills!.Elements<Fill>().ElementAt((int)s.Format(cell).FillId!.Value);
                Assert.Equal("FFD9D9D9", fill.PatternFill!.ForegroundColor!.Rgb!.Value);

                var alignment = s.Format(cell).Alignment!;
                Assert.Equal(HorizontalAlignmentValues.Center, alignment.Horizontal!.Value);
                Assert.True(alignment.WrapText!.Value);

                var border = s.BorderOf(cell);
                Assert.Equal(BorderStyleValues.Thin, border.LeftBorder!.Style!.Value);
                Assert.Equal(BorderStyleValues.Thin, border.RightBorder!.Style!.Value);
                Assert.Equal(BorderStyleValues.Thin, border.TopBorder!.Style!.Value);
                Assert.Equal(BorderStyleValues.Thin, border.BottomBorder!.Style!.Value);
            }
        }

        [Fact]
        public void DataRow_HasNumericCellsWithThreeDecimalFormat()
        {
            var s = Export(Piece("61580.240.24", 1000.0, "5", 36.0));

            Assert.Equal("61580.240.24", s.Text("A3"));
            Assert.True(s.IsNumeric("B3"));
            Assert.Equal(1.0, s.Number("B3"));           // 1000 m2
            Assert.True(s.IsNumeric("C3"));              // pole number "5" is a number, format "0"
            Assert.Equal(5.0, s.Number("C3"));
            Assert.Equal("0", s.NumberFormatCode("C3"));
            Assert.True(s.IsNumeric("D3"));
            Assert.Equal(0.036, s.Number("D3"));
            Assert.True(s.IsNumeric("E3"));
            Assert.Equal(0.964, s.Number("E3"));         // (1000 - 36) m2

            foreach (string cell in new[] { "B3", "D3", "E3" })
            {
                Assert.Equal("0.000", s.NumberFormatCode(cell));
                Assert.Equal(HorizontalAlignmentValues.Right, s.Format(cell).Alignment!.Horizontal!.Value);
            }
        }

        [Fact]
        public void AreasAreWrittenAsTheRoundedPrintedValues()
        {
            var s = Export(Piece("A", 10000.0, "1", 36.0004), Piece("A", 10000.0, "2", 36.0004));

            Assert.Equal(0.036, s.Number("D3"));
            Assert.Equal(0.036, s.Number("D4"));
            Assert.Equal(9.928, s.Number("E3"));   // 10.000 - 0.036 - 0.036
            Assert.Equal(0.072, s.Number("D5"));   // total of the printed step values
        }

        [Fact]
        public void RemainderAddsUpOnPaper_RealCases()
        {
            var s = Export(
                Piece("61580.421.8", 5380.2, "1", 13.5),
                Piece("61580.421.11", 6620.3, "2", 13.6));

            // 61580.421.8 sorts before 61580.421.11 (numeric segments)
            Assert.Equal("61580.421.8", s.Text("A3"));
            Assert.Equal(5.380, s.Number("B3"));
            Assert.Equal(0.014, s.Number("D3"));
            Assert.Equal(5.366, s.Number("E3"));

            Assert.Equal("61580.421.11", s.Text("A4"));
            Assert.Equal(6.620, s.Number("B4"));
            Assert.Equal(0.014, s.Number("D4"));
            Assert.Equal(6.606, s.Number("E4"));

            Assert.Equal(0.028, s.Number("D5"));
        }

        [Fact]
        public void PoleNumberWithLabel_IsWrittenAsANumberWithoutTheLabel()
        {
            var s = Export(Piece("A", 1000.0, "Стълб №162", 36.0));

            Assert.True(s.IsNumeric("C3"));
            Assert.Equal(162.0, s.Number("C3"));
            Assert.Equal("0", s.NumberFormatCode("C3"));
        }

        [Fact]
        public void PoleNumberWithLabelButNotANumber_IsTextWithoutTheLabel()
        {
            var s = Export(Piece("A", 1000.0, "Стълб №ПС-1", 36.0));

            Assert.False(s.IsNumeric("C3"));
            Assert.Equal("ПС-1", s.Text("C3"));
        }

        [Fact]
        public void NonNumericPoleNumber_IsWrittenAsText()
        {
            var s = Export(Piece("A", 1000.0, "ПС-1", 36.0));

            Assert.False(s.IsNumeric("C3"));
            Assert.Equal("ПС-1", s.Text("C3"));
        }

        [Fact]
        public void SeveralPolesInAParcel_RepeatParcelAreaAndRemainderOnEveryRow_WithoutMerging()
        {
            var s = Export(
                Piece("A", 1000.0, "1", 30.0),
                Piece("A", 1000.0, "2", 40.0),
                Piece("B", 500.0, "3", 10.0));

            // The title is the only merged region
            Assert.Equal(new[] { "A1:E1" }, s.Merges().ToArray());

            // Parcel A on rows 3 and 4: same parcel, same area, same remainder (1.000 - 0.030 - 0.040 = 0.930)
            Assert.Equal("A", s.Text("A3"));
            Assert.Equal("A", s.Text("A4"));
            Assert.Equal(1.000, s.Number("B3"));
            Assert.Equal(s.Number("B3"), s.Number("B4"));
            Assert.Equal(0.930, s.Number("E3"));
            Assert.Equal(s.Number("E3"), s.Number("E4"));
            Assert.True(s.IsNumeric("B4"));
            Assert.True(s.IsNumeric("E4"));

            // Their own step areas differ
            Assert.Equal(0.030, s.Number("D3"));
            Assert.Equal(0.040, s.Number("D4"));

            // Parcel B on row 5
            Assert.Equal("B", s.Text("A5"));
            Assert.Equal(0.490, s.Number("E5"));
        }

        [Fact]
        public void RepeatedCells_HaveTheSameFormatAndBordersAsTheFirstRow()
        {
            var s = Export(
                Piece("A", 1000.0, "1", 30.0),
                Piece("A", 1000.0, "2", 40.0),
                Piece("A", 1000.0, "3", 20.0));

            foreach (string column in new[] { "A", "B", "C", "D", "E" })
            {
                foreach (string row in new[] { "4", "5" })
                {
                    Assert.Equal(s.CellAt(column + "3").StyleIndex!.Value, s.CellAt(column + row).StyleIndex!.Value);
                    Assert.Equal(s.NumberFormatCode(column + "3"), s.NumberFormatCode(column + row));
                }

                // thin borders on all four sides of every data cell
                foreach (string row in new[] { "3", "4", "5" })
                {
                    Border b = s.BorderOf(column + row);
                    Assert.Equal(BorderStyleValues.Thin, b.LeftBorder!.Style!.Value);
                    Assert.Equal(BorderStyleValues.Thin, b.RightBorder!.Style!.Value);
                    Assert.Equal(BorderStyleValues.Thin, b.TopBorder!.Style!.Value);
                    Assert.Equal(BorderStyleValues.Thin, b.BottomBorder!.Style!.Value);
                }
            }
            Assert.Equal("0.000", s.NumberFormatCode("B5"));
            Assert.Equal("0.000", s.NumberFormatCode("E5"));
        }

        [Fact]
        public void RowsOfOneParcelStayTogether_InTheCurrentOrder()
        {
            var s = Export(
                Piece("B", 500.0, "9", 10.0),
                Piece("A", 1000.0, "2", 40.0),
                Piece("B", 500.0, "8", 5.0),
                Piece("A", 1000.0, "1", 30.0));

            Assert.Equal(new[] { "A", "A", "B", "B" }, new[] { "A3", "A4", "A5", "A6" }.Select(s.Text).ToArray());
            Assert.Equal(new[] { 1.0, 2.0, 8.0, 9.0 }, new[] { "C3", "C4", "C5", "C6" }.Select(s.Number).ToArray());
        }

        [Fact]
        public void TotalRow_SumsOnlyTheStepAreas()
        {
            var s = Export(
                Piece("A", 1000.0, "1", 30.0),
                Piece("A", 1000.0, "2", 40.0),
                Piece("B", 500.0, "3", 10.0));

            Assert.Equal("Общо", s.Text("A6"));
            Assert.Equal(0.080, s.Number("D6"));
            Assert.Equal("0.000", s.NumberFormatCode("D6"));
            Assert.NotNull(s.FontOf("D6").Bold);
            Assert.NotNull(s.FontOf("A6").Bold);
            Assert.Equal(BorderStyleValues.Medium, s.BorderOf("D6").TopBorder!.Style!.Value);

            // no other sums
            Assert.Null(s.CellAt("B6").CellValue);
            Assert.Null(s.CellAt("C6").CellValue);
            Assert.Null(s.CellAt("E6").CellValue);
        }

        [Fact]
        public void NoPairs_WritesHeaderAndMessageRow_WithoutTotal()
        {
            var s = Export();

            Assert.Equal("Имот", s.Text("A2"));
            Assert.Equal("Няма стъпки в избраните имоти", s.Text("A3"));
            Assert.Equal(new[] { "A1:E1" }, s.Merges().ToArray()); // the message row is not merged
            Assert.DoesNotContain(s.Worksheet.Descendants<Row>(), r => r.RowIndex!.Value > 3U);
        }

        [Fact]
        public void File_PassesOpenXmlSchemaValidation()
        {
            var table = PoleStepsTableBuilder.Build(new[]
            {
                Piece("A", 1000.0, "1", 30.0),
                Piece("A", 1000.0, "ПС-2", 40.0),
                Piece("B", 500.0, "3", 10.0)
            });
            string path = PoleStepsExporter.Export(table, _dir);

            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
                Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            }
        }

        [Fact]
        public void FreezePanes_ColumnWidths_PageSetup_AndPrintTitles()
        {
            var s = Export(Piece("A", 1000.0, "1", 30.0));

            var pane = s.Worksheet.Descendants<Pane>().Single();
            Assert.Equal(2D, pane.VerticalSplit!.Value);
            Assert.Equal("A3", pane.TopLeftCell!.Value);
            Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);

            Assert.Equal(5, s.Worksheet.Descendants<Column>().Count());
            Assert.All(s.Worksheet.Descendants<Column>(), c => Assert.True(c.Width!.Value >= 10));

            var setup = s.Worksheet.Descendants<PageSetup>().Single();
            Assert.Equal(9U, setup.PaperSize!.Value);                       // A4
            Assert.Equal(OrientationValues.Portrait, setup.Orientation!.Value);
            Assert.Equal(1U, setup.FitToWidth!.Value);
            Assert.Equal(0U, setup.FitToHeight!.Value);
            Assert.True(s.Worksheet.Descendants<PageSetupProperties>().Single().FitToPage!.Value);

            var name = s.Workbook.Descendants<DefinedName>().Single();
            Assert.Equal("_xlnm.Print_Titles", name.Name!.Value);
            Assert.Equal("'Стъпки'!$1:$2", name.Text);
        }
    }
}
