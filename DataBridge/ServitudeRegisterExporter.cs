using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes Координатен_регистър_на_сервитута.xlsx (official 08): one sheet per municipality, one section per землище
    /// (title, EKATTE title, then a 6-column table with three header rows). The left and right edge are independent lists
    /// paired by row index: "Номер на точка | X[m] (север) | Y[m] (изток)" twice over.
    ///
    /// Portrait A4, one page wide, a page break before each землище. Coordinates are numbers with format 0.000.
    /// </summary>
    public static class ServitudeRegisterExporter
    {
        public const string NumberFormatCode = "0.000";
        public const string PointHeader = "Номер на точка";
        public const string LeftHeader = "СЕРВИТУТ - ЛЯВО";
        public const string RightHeader = "СЕРВИТУТ - ДЯСНО";
        public const string SystemHeader = "КС: БГС2005-кад";
        public const string NorthHeader = "X[m] (север)";
        public const string EastHeader = "Y[m] (изток)";

        /// <summary>Rows of the header block (the group header, the coordinate system and the column names).</summary>
        public const int HeaderRows = 3;

        private const int ColumnCount = 6;      // A..F
        private static readonly double[] ColumnWidths = { 16, 20, 20, 16, 20, 20 };

        // cellXfs indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleSubtitle = 2;
        private const uint StyleHeader = 3;
        private const uint StylePointNumber = 4;
        private const uint StyleCoordinate = 5;
        private const uint StyleEmpty = 6;

        /// <summary>Writes a workbook with one sheet per item (a municipality) and one section per землище, and returns its path.</summary>
        public static string Export(IReadOnlyList<(string SheetName, IReadOnlyList<ServitudeRegister> Sections)> sheets, string outputDir)
        {
            string filePath = Path.Combine(outputDir, FileNames.ServitudeRegisterFile);

            // A sheet with a single землище repeats its header block on every printed page; with several, each one starts
            // on its own page with its own headers (Print_Titles can only repeat one block per sheet)
            var single = new SingleSectionLayout { FirstTitleRow = 4, LastTitleRow = 4 + HeaderRows - 1 };
            SectionedWorkbook.Write(filePath, sheets, BuildStylesheet(), ColumnWidths, ColumnCount, single,
                WriteSection, OrientationValues.Portrait);
            return filePath;
        }

        /// <summary>Writes one section starting at <paramref name="firstRow"/> and returns the next free row.</summary>
        private static int WriteSection(SheetData sheetData, MergeCells mergeCells, ServitudeRegister register, int firstRow)
        {
            int row = firstRow;

            AppendMergedRow(sheetData, mergeCells, row++, register.Title, StyleTitle, 32D);
            AppendMergedRow(sheetData, mergeCells, row++, register.Subtitle, StyleSubtitle, 28D);
            row++; // empty row under the titles

            int headerTop = row;
            // Row 1: "Номер на точка" (merged down all three rows), then the side over its two coordinate columns
            var first = new Row { RowIndex = (uint)row };
            first.Append(TextCell(1, row, PointHeader, StyleHeader));
            first.Append(TextCell(2, row, LeftHeader, StyleHeader));
            first.Append(EmptyCell(3, row, StyleHeader));
            first.Append(TextCell(4, row, PointHeader, StyleHeader));
            first.Append(TextCell(5, row, RightHeader, StyleHeader));
            first.Append(EmptyCell(6, row, StyleHeader));
            sheetData.Append(first);
            row++;

            // Row 2: the coordinate system over the same two columns
            var second = new Row { RowIndex = (uint)row };
            second.Append(EmptyCell(1, row, StyleHeader));
            second.Append(TextCell(2, row, SystemHeader, StyleHeader));
            second.Append(EmptyCell(3, row, StyleHeader));
            second.Append(EmptyCell(4, row, StyleHeader));
            second.Append(TextCell(5, row, SystemHeader, StyleHeader));
            second.Append(EmptyCell(6, row, StyleHeader));
            sheetData.Append(second);
            row++;

            // Row 3: the column names
            var third = new Row { RowIndex = (uint)row };
            third.Append(EmptyCell(1, row, StyleHeader));
            third.Append(TextCell(2, row, NorthHeader, StyleHeader));
            third.Append(TextCell(3, row, EastHeader, StyleHeader));
            third.Append(EmptyCell(4, row, StyleHeader));
            third.Append(TextCell(5, row, NorthHeader, StyleHeader));
            third.Append(TextCell(6, row, EastHeader, StyleHeader));
            sheetData.Append(third);
            row++;

            int headerBottom = headerTop + HeaderRows - 1;
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{headerTop}:A{headerBottom}") });
            mergeCells.Append(new MergeCell { Reference = new StringValue($"D{headerTop}:D{headerBottom}") });
            mergeCells.Append(new MergeCell { Reference = new StringValue($"B{headerTop}:C{headerTop}") });
            mergeCells.Append(new MergeCell { Reference = new StringValue($"E{headerTop}:F{headerTop}") });
            mergeCells.Append(new MergeCell { Reference = new StringValue($"B{headerTop + 1}:C{headerTop + 1}") });
            mergeCells.Append(new MergeCell { Reference = new StringValue($"E{headerTop + 1}:F{headerTop + 1}") });

            foreach (ServitudeRegisterRow item in register.Rows)
            {
                var line = new Row { RowIndex = (uint)row };
                AppendSide(line, row, 1, item.Left);
                AppendSide(line, row, 4, item.Right);
                sheetData.Append(line);
                row++;
            }
            return row;
        }

        /// <summary>The three cells of one side: the number and its two coordinates, or three empty cells.</summary>
        private static void AppendSide(Row row, int rowIndex, int firstColumn, NumberedServitudePoint? point)
        {
            if (point == null)
            {
                for (int c = 0; c < 3; c++) row.Append(EmptyCell(firstColumn + c, rowIndex, StyleEmpty));
                return;
            }
            row.Append(IntegerCell(firstColumn, rowIndex, point.Number));
            row.Append(NumberCell(firstColumn + 1, rowIndex, point.Y));     // X(север) = the drawing's Y
            row.Append(NumberCell(firstColumn + 2, rowIndex, point.X));     // Y(изток) = the drawing's X
        }

        private static void AppendMergedRow(SheetData sheetData, MergeCells mergeCells, int rowIndex, string text, uint style, double? height = null)
        {
            var row = new Row { RowIndex = (uint)rowIndex };
            if (height.HasValue)
            {
                row.Height = height.Value;
                row.CustomHeight = true;
            }
            row.Append(TextCell(1, rowIndex, text, style));
            for (int c = 2; c <= ColumnCount; c++) row.Append(EmptyCell(c, rowIndex, style));
            sheetData.Append(row);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:F{rowIndex}") });
        }

        // -----------------------------------------------------------------
        //  Cells
        // -----------------------------------------------------------------

        private static string Ref(int column, int row) => $"{(char)('A' + column - 1)}{row}";

        private static Cell TextCell(int column, int row, string text, uint style)
        {
            if (text.Length == 0) return EmptyCell(column, row, style);
            Cell cell = SheetCells.InlineString(text, style);
            cell.CellReference = Ref(column, row);
            return cell;
        }

        private static Cell EmptyCell(int column, int row, uint style) =>
            new Cell { CellReference = Ref(column, row), StyleIndex = style };

        private static Cell IntegerCell(int column, int row, int value) =>
            new Cell
            {
                CellReference = Ref(column, row),
                StyleIndex = StylePointNumber,
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
            };

        /// <summary>Rounded to 3 decimals as printed; negative zero is written as 0.</summary>
        private static Cell NumberCell(int column, int row, double value)
        {
            double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
            if (rounded == 0.0) rounded = 0.0;
            return new Cell
            {
                CellReference = Ref(column, row),
                StyleIndex = StyleCoordinate,
                DataType = CellValues.Number,
                CellValue = new CellValue(rounded.ToString("R", CultureInfo.InvariantCulture))
            };
        }

        // -----------------------------------------------------------------
        //  Styles (the fonts, fills and borders of the 07)
        // -----------------------------------------------------------------

        private static Stylesheet BuildStylesheet()
        {
            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = NumberFormatCode })
            { Count = 1U };

            var fonts = new Fonts(
                new Font(new FontSize { Val = 10D }, new FontName { Val = "Calibri" }),                 // 0 normal
                new Font(new Bold(), new FontSize { Val = 10D }, new FontName { Val = "Calibri" }),     // 1 bold
                new Font(new Bold(), new FontSize { Val = 13D }, new FontName { Val = "Calibri" }),     // 2 title
                new Font(new Bold(), new FontSize { Val = 11D }, new FontName { Val = "Calibri" }))     // 3 subtitle
            { Count = 4U };

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
                new CellFormat(),                                                           // 0 default
                Format(0U, 2U, 0U, 0U, HorizontalAlignmentValues.Center, true),             // 1 title
                Format(0U, 3U, 0U, 0U, HorizontalAlignmentValues.Center, true),             // 2 subtitle
                Format(0U, 1U, 2U, 1U, HorizontalAlignmentValues.Center, true),             // 3 header
                Format(0U, 0U, 0U, 1U, HorizontalAlignmentValues.Center, false),            // 4 point number
                Format(164U, 0U, 0U, 1U, HorizontalAlignmentValues.Right, false),           // 5 coordinate 0.000
                Format(0U, 0U, 0U, 1U, HorizontalAlignmentValues.Center, false))            // 6 the empty side of a row
            { Count = 7U };

            var cellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U };

            return new Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
        }

        private static CellFormat Format(uint numberFormatId, uint fontId, uint fillId, uint borderId,
            HorizontalAlignmentValues horizontal, bool wrap) =>
            new CellFormat(new Alignment { Horizontal = horizontal, Vertical = VerticalAlignmentValues.Center, WrapText = wrap })
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
