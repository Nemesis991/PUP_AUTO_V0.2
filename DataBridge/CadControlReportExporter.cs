using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes Контролна_справка_cad.xlsx: one row per right, the parcel data repeated on every row,
    /// no merged cells. Areas are numbers in m² (0.00); ЕГН/БУЛСТАТ is always text so leading zeros survive.
    /// </summary>
    public static class CadControlReportExporter
    {
        public const string SheetName = "Контролна справка";

        public static readonly string[] Headers =
        {
            "Имот",
            "ТП (код + текст)",
            "НТП (код + текст)",
            "Местност",
            "Категория",
            "Площ .cad (м²)",
            "Начертана площ (м²)",
            "Разлика (м²)",
            "Вид собственост",
            "Вид право",
            "Дял (DOCID1/DOCID2)",
            "ЕГН/БУЛСТАТ",
            "Име"
        };

        public const string AreaFormatCode = "0.00";

        // cellXfs indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleHeader = 2;
        private const uint StyleText = 3;
        private const uint StyleNumber = 4;
        private const uint StyleIdText = 5;

        private const int TitleRow = 1;
        private const int HeaderRow = 2;

        private static readonly double[] ColumnWidths = { 16, 26, 34, 18, 11, 15, 17, 14, 22, 20, 18, 16, 40 };

        /// <summary>Writes the file and returns its path.</summary>
        public static string Export(CadControlReport report, string outputDir)
        {
            string filePath = Path.Combine(outputDir, FileNames.CadControlReportFile);

            using (SpreadsheetDocument document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = BuildStylesheet();
                stylesPart.Stylesheet.Save();

                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                var sheetData = new SheetData();
                int lastRow = FillSheet(sheetData, report);

                var columns = new Columns();
                for (int c = 0; c < ColumnWidths.Length; c++)
                {
                    columns.Append(new Column { Min = (uint)(c + 1), Max = (uint)(c + 1), Width = ColumnWidths[c], CustomWidth = true });
                }

                worksheetPart.Worksheet = new Worksheet(
                    new SheetProperties(new PageSetupProperties { FitToPage = true }),
                    new SheetViews(new SheetView(
                        new Pane
                        {
                            VerticalSplit = HeaderRow,
                            TopLeftCell = "A" + (HeaderRow + 1),
                            ActivePane = PaneValues.BottomLeft,
                            State = PaneStateValues.Frozen
                        },
                        new Selection { Pane = PaneValues.BottomLeft })
                    { WorkbookViewId = 0U }),
                    columns,
                    sheetData,
                    new AutoFilter { Reference = $"A{HeaderRow}:{Ref(Headers.Length, Math.Max(lastRow, HeaderRow))}" },
                    new PageMargins { Left = 0.4, Right = 0.4, Top = 0.6, Bottom = 0.6, Header = 0.3, Footer = 0.3 },
                    new PageSetup
                    {
                        PaperSize = 9U, // A4
                        Orientation = OrientationValues.Landscape,
                        FitToWidth = 1U,
                        FitToHeight = 0U
                    });
                worksheetPart.Worksheet.Save();

                Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = SheetName
                });

                // Repeat the title + header rows on every printed page
                workbookPart.Workbook.AppendChild(new DefinedNames(
                    new DefinedName($"'{SheetName}'!$1:${HeaderRow}")
                    {
                        Name = "_xlnm.Print_Titles",
                        LocalSheetId = 0U
                    }));
                workbookPart.Workbook.Save();
            }

            return filePath;
        }

        /// <summary>Fills the rows and returns the index of the last one.</summary>
        private static int FillSheet(SheetData sheetData, CadControlReport report)
        {
            // Row 1: title in A1 (not merged; the text runs over the empty cells to its right)
            var titleRow = new Row { RowIndex = (uint)TitleRow, Height = 24D, CustomHeight = true };
            titleRow.Append(TextCell(1, TitleRow, report.Title, StyleTitle));
            sheetData.Append(titleRow);

            // Row 2: header
            var headerRow = new Row { RowIndex = (uint)HeaderRow, Height = 32D, CustomHeight = true };
            for (int c = 0; c < Headers.Length; c++)
            {
                headerRow.Append(TextCell(c + 1, HeaderRow, Headers[c], StyleHeader));
            }
            sheetData.Append(headerRow);

            int rowIndex = HeaderRow;
            foreach (CadControlRow data in report.Rows)
            {
                rowIndex++;
                var row = new Row { RowIndex = (uint)rowIndex };
                row.Append(TextCell(1, rowIndex, data.ParcelId, StyleText));
                row.Append(TextCell(2, rowIndex, data.Vidt, StyleText));
                row.Append(TextCell(3, rowIndex, data.Ntp, StyleText));
                row.Append(TextCell(4, rowIndex, data.Mestnost, StyleText));
                row.Append(TextCell(5, rowIndex, data.Category, StyleText));
                row.Append(NumberCell(6, rowIndex, data.CadAreaSqm));
                row.Append(NumberCell(7, rowIndex, data.DrawnAreaSqm));
                row.Append(NumberCell(8, rowIndex, data.DifferenceSqm));
                row.Append(TextCell(9, rowIndex, data.Vids, StyleText));
                row.Append(TextCell(10, rowIndex, data.PravoVid, StyleText));
                row.Append(TextCell(11, rowIndex, data.Share, StyleText));
                row.Append(TextCell(12, rowIndex, data.PersonId, StyleIdText));
                row.Append(TextCell(13, rowIndex, data.PersonName, StyleText));
                sheetData.Append(row);
            }
            return rowIndex;
        }

        // -----------------------------------------------------------------
        //  Cells
        // -----------------------------------------------------------------

        private static string Ref(int column, int row) => $"{(char)('A' + column - 1)}{row}";

        private static Cell TextCell(int column, int row, string text, uint style)
        {
            Cell cell = SheetCells.InlineString(text, style);
            cell.CellReference = Ref(column, row);
            return cell;
        }

        /// <summary>A number cell; a missing value is an empty (still bordered) cell. Negative zero is written as 0.</summary>
        private static Cell NumberCell(int column, int row, double? value)
        {
            var cell = new Cell { CellReference = Ref(column, row), StyleIndex = StyleNumber };
            if (value.HasValue)
            {
                double v = value.Value == 0.0 ? 0.0 : value.Value;
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(v.ToString("R", CultureInfo.InvariantCulture));
            }
            return cell;
        }

        // -----------------------------------------------------------------
        //  Styles
        // -----------------------------------------------------------------

        private static Stylesheet BuildStylesheet()
        {
            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = AreaFormatCode })
            { Count = 1U };

            var fonts = new Fonts(
                new Font(new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),                 // 0 normal
                new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" }),     // 1 bold
                new Font(new Bold(), new FontSize { Val = 14D }, new FontName { Val = "Calibri" }))     // 2 title
            { Count = 3U };

            var fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(
                    new ForegroundColor { Rgb = "FFD9D9D9" },
                    new BackgroundColor { Indexed = 64U })
                { PatternType = PatternValues.Solid }))
            { Count = 3U };

            var borders = new Borders(
                new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()),
                new Border(
                    new LeftBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                    new RightBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                    new TopBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                    new BottomBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                    new DiagonalBorder()))
            { Count = 2U };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };

            var cellFormats = new CellFormats(
                new CellFormat(),                                                                    // 0 default
                Format(0U, 2U, 0U, 0U, Align(HorizontalAlignmentValues.Left, false)),                // 1 title
                Format(0U, 1U, 2U, 1U, Align(HorizontalAlignmentValues.Center, true)),               // 2 header
                Format(0U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, false)),                // 3 text
                Format(164U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),             // 4 area 0.00
                Format(49U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, false)))               // 5 text format "@" (ЕГН/БУЛСТАТ)
            { Count = 6U };

            var cellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U };

            return new Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
        }

        private static Alignment Align(HorizontalAlignmentValues horizontal, bool wrap) =>
            new Alignment { Horizontal = horizontal, Vertical = VerticalAlignmentValues.Center, WrapText = wrap };

        private static CellFormat Format(uint numberFormatId, uint fontId, uint fillId, uint borderId, Alignment alignment) =>
            new CellFormat(alignment)
            {
                NumberFormatId = numberFormatId,
                FontId = fontId,
                FillId = fillId,
                BorderId = borderId,
                ApplyNumberFormat = numberFormatId != 0U,
                ApplyFont = fontId != 0U,
                ApplyFill = fillId != 0U,
                ApplyBorder = borderId != 0U,
                ApplyAlignment = true
            };
    }
}
