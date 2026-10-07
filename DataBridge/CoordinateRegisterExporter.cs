using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes Координатен_регистър_на_стъпките.xlsx (official 07): one sheet per municipality, one section per землище (title,
    /// EKATTE title, then one 3-column block per pole: "Стълб №N, попадащ в имот …", the №/X(север)/Y(изток) header, the
    /// centre, the corners and the footprint area). One empty row between blocks; portrait A4, one page wide.
    /// Coordinates are numbers with format 0.000.
    /// </summary>
    public static class CoordinateRegisterExporter
    {
        public const string NumberFormatCode = "0.000";
        public static readonly string[] Headers = { "№", "X(север)", "Y(изток)" };

        private const int ColumnCount = 3;      // A..C
        private static readonly double[] ColumnWidths = { 18, 20, 20 };

        // cellXfs indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleSubtitle = 2;
        private const uint StyleBlockTitle = 3;
        private const uint StyleHeader = 4;
        private const uint StyleLabelRow = 5;
        private const uint StylePointLabel = 6;
        private const uint StyleCoordinate = 7;

        /// <summary>Writes a workbook with one sheet per item (a municipality) and one section per землище, and returns its path.</summary>
        public static string Export(IReadOnlyList<(string SheetName, IReadOnlyList<CoordinateRegister> Sections)> sheets, string outputDir)
        {
            string filePath = Path.Combine(outputDir, FileNames.CoordinateRegisterFile);
            SectionedWorkbook.Write(filePath, sheets, BuildStylesheet(), ColumnWidths, ColumnCount, new SingleSectionLayout(),
                WriteSection, OrientationValues.Portrait);
            return filePath;
        }

        /// <summary>Writes one section starting at <paramref name="firstRow"/> and returns the next free row.</summary>
        private static int WriteSection(SheetData sheetData, MergeCells mergeCells, CoordinateRegister register, int firstRow)
        {
            int row = firstRow;

            // Titles span the three columns and wrap: the sheet is narrow
            AppendMergedRow(sheetData, mergeCells, row++, register.Title, StyleTitle, 36D);
            AppendMergedRow(sheetData, mergeCells, row++, register.Subtitle, StyleSubtitle, 30D);
            row++; // empty row under the titles

            for (int b = 0; b < register.Blocks.Count; b++)
            {
                if (b > 0) row++; // one empty row between pole blocks
                CoordinateRegisterBlock block = register.Blocks[b];

                AppendMergedRow(sheetData, mergeCells, row++, CoordinateRegisterBuilder.BlockTitle(block), StyleBlockTitle);

                var header = new Row { RowIndex = (uint)row };
                for (int c = 0; c < Headers.Length; c++) header.Append(TextCell(c + 1, row, Headers[c], StyleHeader));
                sheetData.Append(header);
                row++;

                AppendMergedRow(sheetData, mergeCells, row++, CoordinateRegisterBuilder.CentreHeading, StyleLabelRow);
                AppendPointRow(sheetData, row++, block.Centre);

                AppendMergedRow(sheetData, mergeCells, row++, CoordinateRegisterBuilder.CornersHeading, StyleLabelRow);
                foreach (CoordinateRegisterPoint corner in block.Corners) AppendPointRow(sheetData, row++, corner);

                AppendMergedRow(sheetData, mergeCells, row++, CoordinateRegisterBuilder.AreaText(block.AreaSqm), StyleLabelRow);
            }
            return row;
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
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:C{rowIndex}") });
        }

        private static void AppendPointRow(SheetData sheetData, int rowIndex, CoordinateRegisterPoint point)
        {
            var row = new Row { RowIndex = (uint)rowIndex };
            row.Append(TextCell(1, rowIndex, point.Label, StylePointLabel));
            row.Append(NumberCell(2, rowIndex, point.North, StyleCoordinate));
            row.Append(NumberCell(3, rowIndex, point.East, StyleCoordinate));
            sheetData.Append(row);
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

        /// <summary>Rounded to 3 decimals as printed; negative zero is written as 0.</summary>
        private static Cell NumberCell(int column, int row, double value, uint style)
        {
            double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
            if (rounded == 0.0) rounded = 0.0;
            return new Cell
            {
                CellReference = Ref(column, row),
                StyleIndex = style,
                DataType = CellValues.Number,
                CellValue = new CellValue(rounded.ToString("R", CultureInfo.InvariantCulture))
            };
        }

        // -----------------------------------------------------------------
        //  Styles
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
                Format(0U, 1U, 0U, 1U, HorizontalAlignmentValues.Left, false),              // 3 "Стълб №N, попадащ в имот …"
                Format(0U, 1U, 2U, 1U, HorizontalAlignmentValues.Center, false),            // 4 header
                Format(0U, 0U, 0U, 1U, HorizontalAlignmentValues.Left, false),              // 5 merged label rows
                Format(0U, 0U, 0U, 1U, HorizontalAlignmentValues.Left, false),              // 6 point label
                Format(164U, 0U, 0U, 1U, HorizontalAlignmentValues.Right, false))           // 7 coordinate 0.000
            { Count = 8U };

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
