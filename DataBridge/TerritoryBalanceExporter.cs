using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes Баланси_на_територията.xlsx. One sheet per municipality, one section per землище. A section is the title row,
    /// the EKATTE title row, an empty row and four 9-column tables (категория, собственост, територия, НТП), each
    /// followed by two empty rows. The last row of a table is "Общо:" (A:B merged). Areas are numbers with format 0.000,
    /// the percent 0.00, counts are integers. A4 portrait, one page wide.
    /// </summary>
    public static class TerritoryBalanceExporter
    {
        public const string SheetName = "Баланси";
        public const string AreaFormatCode = "0.000";
        public const string PercentFormatCode = "0.00";
        public const int EmptyRowsBetweenTables = 2;

        public static readonly string[] Headers =
        {
            "№",
            "{group}",
            "Имоти бр.",
            "Обща площ на имотите в дка",
            "Обща площ с ограничение в дка",
            "Стъпки на стълбове бр.",
            "Площ на стъпките в дка",
            "Засегната площ в дка",
            "Площ %"
        };

        private const int ColumnCount = 9;      // A..I

        // cellXfs indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleSubtitle = 2;
        private const uint StyleHeader = 3;
        private const uint StyleText = 4;
        private const uint StyleInteger = 5;
        private const uint StyleArea = 6;
        private const uint StylePercent = 7;
        private const uint StyleIndex = 8;
        private const uint StyleTotalLabel = 9;
        private const uint StyleTotalInteger = 10;
        private const uint StyleTotalArea = 11;
        private const uint StyleTotalPercent = 12;
        private const uint StyleTotalEmpty = 13;

        private static readonly double[] ColumnWidths = { 6, 44, 10, 16, 16, 14, 14, 14, 10 };

        /// <summary>Writes a workbook with ONE sheet (<see cref="SheetName"/>) holding one землище and returns its path.</summary>
        public static string Export(TerritoryBalance balance, string outputDir) =>
            Export(new[] { (SheetName, (IReadOnlyList<TerritoryBalance>)new[] { balance }) }, outputDir);

        /// <summary>
        /// Writes a workbook with one sheet per item (a municipality) and one section per землище in it, stacked one
        /// under the other, and returns its path. No freeze pane and no Print_Titles: four tables cannot share one header.
        /// </summary>
        public static string Export(IReadOnlyList<(string SheetName, IReadOnlyList<TerritoryBalance> Sections)> sheets, string outputDir) =>
            Write(Path.Combine(outputDir, FileNames.TerritoryBalanceFile), sheets);

        /// <summary>
        /// Writes Общ_баланс_за_общината.xlsx: one sheet per municipality holding its combined balance (one section, the same
        /// layout; the title and subtitle come from the balance) and returns its path.
        /// </summary>
        public static string ExportMunicipalities(IReadOnlyList<(string SheetName, TerritoryBalance Balance)> sheets, string outputDir) =>
            Write(
                Path.Combine(outputDir, FileNames.MunicipalityBalanceFile),
                sheets.Select(s => (s.SheetName, (IReadOnlyList<TerritoryBalance>)new[] { s.Balance })).ToList());

        private static string Write(string filePath, IReadOnlyList<(string SheetName, IReadOnlyList<TerritoryBalance> Sections)> sheets)
        {
            SectionedWorkbook.Write(
                filePath, sheets, BuildStylesheet(), ColumnWidths, ColumnCount, new SingleSectionLayout(), WriteSection,
                OrientationValues.Portrait);
            return filePath;
        }

        /// <summary>Writes one section starting at <paramref name="firstRow"/> and returns the next free row.</summary>
        private static int WriteSection(SheetData sheetData, MergeCells mergeCells, TerritoryBalance balance, int firstRow)
        {
            var titleRow = new Row { RowIndex = (uint)firstRow, Height = 22D, CustomHeight = true };
            titleRow.Append(TextCell(1, firstRow, balance.Title, StyleTitle));
            sheetData.Append(titleRow);

            var subtitleRow = new Row { RowIndex = (uint)(firstRow + 1), Height = 20D, CustomHeight = true };
            subtitleRow.Append(TextCell(1, firstRow + 1, balance.Subtitle, StyleSubtitle));
            sheetData.Append(subtitleRow);

            int rowIndex = firstRow + 3; // one empty row after the subtitle
            for (int t = 0; t < balance.Tables.Length; t++)
            {
                if (t > 0) rowIndex += EmptyRowsBetweenTables;
                rowIndex = WriteTable(sheetData, mergeCells, balance.Tables[t], rowIndex);
            }
            return rowIndex;
        }

        /// <summary>Writes the header, the group rows and the "Общо:" row; returns the next free row.</summary>
        private static int WriteTable(SheetData sheetData, MergeCells mergeCells, BalanceTable table, int firstRow)
        {
            var headerRow = new Row { RowIndex = (uint)firstRow, Height = 48D, CustomHeight = true };
            for (int c = 0; c < Headers.Length; c++)
            {
                string text = Headers[c] == "{group}" ? table.GroupHeader : Headers[c];
                headerRow.Append(TextCell(c + 1, firstRow, text, StyleHeader));
            }
            sheetData.Append(headerRow);

            int rowIndex = firstRow;
            foreach (BalanceRow data in table.Rows)
            {
                rowIndex++;
                var row = new Row { RowIndex = (uint)rowIndex };
                row.Append(NumberCell(1, rowIndex, data.Number.ToString(CultureInfo.InvariantCulture), StyleIndex));
                row.Append(TextCell(2, rowIndex, data.Group, StyleText));
                row.Append(NumberCell(3, rowIndex, Int(data.ParcelCount), StyleInteger));
                row.Append(NumberCell(4, rowIndex, Dec(data.AreaDka), StyleArea));
                row.Append(NumberCell(5, rowIndex, Dec(data.RestrictedDka), StyleArea));
                row.Append(NumberCell(6, rowIndex, Int(data.PoleCount), StyleInteger));
                row.Append(NumberCell(7, rowIndex, Dec(data.StepDka), StyleArea));
                row.Append(NumberCell(8, rowIndex, Dec(data.AffectedDka), StyleArea));
                row.Append(OptionalNumberCell(9, rowIndex, data.Percent, StylePercent));
                sheetData.Append(row);
            }

            rowIndex++;
            BalanceRow total = table.Total;
            var totalRow = new Row { RowIndex = (uint)rowIndex };
            totalRow.Append(TextCell(1, rowIndex, BalanceTotalLabel, StyleTotalLabel));
            totalRow.Append(EmptyCell(2, rowIndex, StyleTotalEmpty));
            totalRow.Append(NumberCell(3, rowIndex, Int(total.ParcelCount), StyleTotalInteger));
            totalRow.Append(NumberCell(4, rowIndex, Dec(total.AreaDka), StyleTotalArea));
            totalRow.Append(NumberCell(5, rowIndex, Dec(total.RestrictedDka), StyleTotalArea));
            totalRow.Append(NumberCell(6, rowIndex, Int(total.PoleCount), StyleTotalInteger));
            totalRow.Append(NumberCell(7, rowIndex, Dec(total.StepDka), StyleTotalArea));
            totalRow.Append(NumberCell(8, rowIndex, Dec(total.AffectedDka), StyleTotalArea));
            totalRow.Append(OptionalNumberCell(9, rowIndex, total.Percent, StyleTotalPercent, StyleTotalEmpty));
            sheetData.Append(totalRow);
            mergeCells.Append(new MergeCell { Reference = new StringValue($"A{rowIndex}:B{rowIndex}") });
            return rowIndex + 1;
        }

        private const string BalanceTotalLabel = TerritoryBalanceBuilder.TotalLabel;

        // -----------------------------------------------------------------
        //  Cells
        // -----------------------------------------------------------------

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>The builder holds the printed (rounded) values as decimals; negative zero is written as 0.</summary>
        private static string Dec(decimal value) =>
            (value == 0m ? 0m : value).ToString(CultureInfo.InvariantCulture);

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

        private static Cell NumberCell(int column, int row, string invariantValue, uint style) =>
            new Cell
            {
                CellReference = Ref(column, row),
                StyleIndex = style,
                DataType = CellValues.Number,
                CellValue = new CellValue(invariantValue)
            };

        private static Cell OptionalNumberCell(int column, int row, decimal? value, uint style, uint? emptyStyle = null) =>
            value.HasValue ? NumberCell(column, row, Dec(value.Value), style) : EmptyCell(column, row, emptyStyle ?? style);

        // -----------------------------------------------------------------
        //  Styles
        // -----------------------------------------------------------------

        private static Stylesheet BuildStylesheet()
        {
            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = AreaFormatCode },
                new NumberingFormat { NumberFormatId = 165U, FormatCode = PercentFormatCode })
            { Count = 2U };

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
                Bordered(BorderStyleValues.Thin),      // 1 thin all round
                Bordered(BorderStyleValues.Medium))    // 2 heavier top (Общо row)
            { Count = 3U };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };

            var cellFormats = new CellFormats(
                new CellFormat(),                                                                    // 0 default
                Format(0U, 2U, 0U, 0U, Align(HorizontalAlignmentValues.Left, false)),                // 1 title
                Format(0U, 3U, 0U, 0U, Align(HorizontalAlignmentValues.Left, false)),                // 2 subtitle
                Format(0U, 1U, 2U, 1U, Align(HorizontalAlignmentValues.Center, true)),               // 3 header
                Format(0U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, true)),                 // 4 text
                Format(1U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),               // 5 integer "0"
                Format(164U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),             // 6 area 0.000
                Format(165U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),             // 7 percent 0.00
                Format(1U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Center, false)),              // 8 row number
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)),                // 9 total label
                Format(1U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),               // 10 total integer
                Format(164U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),             // 11 total area
                Format(165U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),             // 12 total percent
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)))                // 13 total empty
            { Count = 14U };

            var cellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U };

            return new Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
        }

        private static Border Bordered(BorderStyleValues top) =>
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
