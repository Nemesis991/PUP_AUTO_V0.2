using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes the pole-steps table (Стъпки_на_стълбове.xlsx). Areas are real numbers in decares
    /// (already rounded by <see cref="PoleStepsTableBuilder"/>) with number format 0.000; a numeric
    /// pole number uses the built-in format "0".
    /// </summary>
    public static class PoleStepsExporter
    {
        public const string SheetName = "Стъпки";
        public const string Title = "Площи на стъпките на стълбове";
        public const string NoStepsMessage = "Няма стъпки в избраните имоти";

        public static readonly string[] Headers =
        {
            "Имот",
            "Площ на имота (дка)",
            "Стълб №",
            "Площ на стъпката (дка)",
            "Остатък без стъпката (дка)"
        };

        public const string TotalLabel = "Общо";
        public const string NumberFormatCode = "0.000";

        // Style (cellXfs) indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleHeader = 2;
        private const uint StyleText = 3;
        private const uint StyleNumber = 4;
        private const uint StylePole = 5;
        private const uint StyleTotalLabel = 6;
        private const uint StyleTotalNumber = 7;
        private const uint StyleTotalEmpty = 8;
        private const uint StyleMessage = 9;

        private const int HeaderRow = 2;
        private const int ColumnCount = 5; // A..E

        /// <summary>Writes the file and returns its path.</summary>
        public static string Export(PoleStepsTable table, string outputDir)
        {
            string filePath = Path.Combine(outputDir, FileNames.PoleStepsFile);

            using (SpreadsheetDocument document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = BuildStylesheet();
                stylesPart.Stylesheet.Save();

                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                var sheetData = new SheetData();
                var mergeCells = new MergeCells();
                FillSheet(sheetData, mergeCells, table);

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
                    new Columns(
                        Column(1, 18),
                        Column(2, 18),
                        Column(3, 11),
                        Column(4, 18),
                        Column(5, 22)),
                    sheetData,
                    mergeCells,
                    new PageMargins { Left = 0.7, Right = 0.7, Top = 0.75, Bottom = 0.75, Header = 0.3, Footer = 0.3 },
                    new PageSetup
                    {
                        PaperSize = 9U, // A4
                        Orientation = OrientationValues.Portrait,
                        FitToWidth = 1U,
                        FitToHeight = 0U // as many pages tall as needed
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

        private static void FillSheet(SheetData sheetData, MergeCells mergeCells, PoleStepsTable table)
        {
            // Row 1: title, merged across the table (the only merged region of the sheet)
            var titleRow = new Row { RowIndex = 1U, Height = 24D, CustomHeight = true };
            titleRow.Append(TextCell(1, 1, Title, StyleTitle));
            for (int c = 2; c <= ColumnCount; c++) titleRow.Append(EmptyCell(c, 1, StyleTitle));
            sheetData.Append(titleRow);
            mergeCells.Append(Merge("A1", "E1"));

            // Row 2: header
            var headerRow = new Row { RowIndex = (uint)HeaderRow, Height = 32D, CustomHeight = true };
            for (int c = 0; c < Headers.Length; c++)
                headerRow.Append(TextCell(c + 1, HeaderRow, Headers[c], StyleHeader));
            sheetData.Append(headerRow);

            int rowIndex = HeaderRow;

            if (table.Rows.Count == 0)
            {
                rowIndex++;
                var messageRow = new Row { RowIndex = (uint)rowIndex };
                messageRow.Append(TextCell(1, rowIndex, NoStepsMessage, StyleMessage));
                for (int c = 2; c <= ColumnCount; c++) messageRow.Append(EmptyCell(c, rowIndex, StyleMessage));
                sheetData.Append(messageRow);
                return;
            }

            foreach (PoleStepsRow data in table.Rows)
            {
                rowIndex++;
                var row = new Row { RowIndex = (uint)rowIndex };

                // No vertical merges: parcel, parcel area and remainder are repeated on every row of the parcel
                row.Append(TextCell(1, rowIndex, data.ParcelId, StyleText));
                row.Append(NumberCell(2, rowIndex, Clean(data.ParcelAreaDka), StyleNumber));
                row.Append(PoleCell(3, rowIndex, data.PoleNumber));
                row.Append(NumberCell(4, rowIndex, Clean(data.PieceAreaDka), StyleNumber));
                row.Append(NumberCell(5, rowIndex, Clean(data.RemainderDka), StyleNumber));

                sheetData.Append(row);
            }

            // Last row: total of the step areas only
            rowIndex++;
            var totalRow = new Row { RowIndex = (uint)rowIndex };
            totalRow.Append(TextCell(1, rowIndex, TotalLabel, StyleTotalLabel));
            totalRow.Append(EmptyCell(2, rowIndex, StyleTotalEmpty));
            totalRow.Append(EmptyCell(3, rowIndex, StyleTotalEmpty));
            totalRow.Append(NumberCell(4, rowIndex, Clean(table.TotalPieceAreaDka), StyleTotalNumber));
            totalRow.Append(EmptyCell(5, rowIndex, StyleTotalEmpty));
            sheetData.Append(totalRow);
        }

        // -----------------------------------------------------------------
        //  Cells
        // -----------------------------------------------------------------

        /// <summary>The table already holds the printed (rounded) decare values; negative zero is written as 0.</summary>
        private static double Clean(double dka) => dka == 0.0 ? 0.0 : dka;

        private static string Ref(int column, int row) => $"{(char)('A' + column - 1)}{row}";

        private static Cell TextCell(int column, int row, string text, uint style)
        {
            Cell cell = SheetCells.InlineString(text, style);
            cell.CellReference = Ref(column, row);
            return cell;
        }

        private static Cell EmptyCell(int column, int row, uint style) =>
            new Cell { CellReference = Ref(column, row), StyleIndex = style };

        private static Cell NumberCell(int column, int row, double value, uint style) =>
            new Cell
            {
                CellReference = Ref(column, row),
                StyleIndex = style,
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture))
            };

        /// <summary>Pole number as a number if it parses, otherwise as text.</summary>
        private static Cell PoleCell(int column, int row, string poleNumber)
        {
            if (PoleStepsTableBuilder.TryParsePoleNumber(poleNumber, out double number))
                return NumberCell(column, row, number, StylePole);
            return TextCell(column, row, poleNumber, StylePole);
        }

        private static MergeCell Merge(string from, string to) =>
            new MergeCell { Reference = new StringValue($"{from}:{to}") };

        private static Column Column(uint index, double width) =>
            new Column { Min = index, Max = index, Width = width, CustomWidth = true };

        // -----------------------------------------------------------------
        //  Styles
        // -----------------------------------------------------------------

        private static Stylesheet BuildStylesheet()
        {
            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = NumberFormatCode })
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
                ThinBorder(BorderStyleValues.Thin),      // 1 thin all round
                ThinBorder(BorderStyleValues.Medium))    // 2 thin, heavier top (total row)
            { Count = 3U };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };

            var cellFormats = new CellFormats(
                new CellFormat(),                                                                    // 0 default
                Format(0U, 2U, 0U, 0U, Align(HorizontalAlignmentValues.Center, false)),              // 1 title
                Format(0U, 1U, 2U, 1U, Align(HorizontalAlignmentValues.Center, true)),               // 2 header
                Format(0U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, false)),                // 3 text
                Format(164U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),             // 4 area 0.000
                Format(1U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Center, false)),              // 5 pole number, format "0"
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)),                // 6 total label
                Format(164U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),             // 7 total number
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)),                // 8 total empty
                Format(0U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, false)))                // 9 message (not merged, so left-aligned)
            { Count = 10U };

            var cellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U };

            return new Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
        }

        private static Border ThinBorder(BorderStyleValues top) =>
            new Border(
                new LeftBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new RightBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new TopBorder(new Color { Auto = true }) { Style = top },
                new BottomBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
                new DiagonalBorder());

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
