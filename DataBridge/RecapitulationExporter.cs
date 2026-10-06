using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes Обща_рекапитулация.xlsx: one sheet per област holding the title, the subtitle, a two-row header (the second row
    /// holds the units), the object name row and one row per землище, with "Общо за общ. X:" after the землища of each
    /// municipality and "Общо за обл. X:" last. 12 columns A..L. Areas and kilometres are numbers with format 0.000, counts
    /// are integers. A4 landscape, one page wide.
    /// </summary>
    public static class RecapitulationExporter
    {
        public const string AreaFormatCode = "0.000";

        public static readonly string[] Headers =
        {
            "№ по ред", "Област", "Община", "Землище", "ЕКАТТЕ", "Имоти", "Обща площ на имотите",
            "Площ с ново ограничение", "Брой стъпки на стълбове", "Площ за стъпки на стълбове",
            "Общо засегната площ", "Дължина на трасето"
        };

        public static readonly string[] Units =
        {
            "", "", "", "", "", "[бр]", "[дка]", "[дка]", "", "[дка]", "[дка]", "[km]"
        };

        private const int ColumnCount = 12;     // A..L

        // cellXfs indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleSubtitle = 2;
        private const uint StyleHeader = 3;
        private const uint StyleText = 4;
        private const uint StyleInteger = 5;
        private const uint StyleArea = 6;
        private const uint StyleIndex = 7;
        private const uint StyleObject = 8;
        private const uint StyleTotalLabel = 9;
        private const uint StyleTotalInteger = 10;
        private const uint StyleTotalArea = 11;
        private const uint StyleTotalEmpty = 12;

        private static readonly double[] ColumnWidths = { 8, 12, 14, 16, 10, 9, 14, 14, 12, 14, 14, 12 };

        public static string Export(IReadOnlyList<RecapitulationSheet> sheets, string outputDir) =>
            Write(Path.Combine(outputDir, FileNames.RecapitulationFile), sheets);

        private static string Write(string filePath, IReadOnlyList<RecapitulationSheet> sheets)
        {
            SectionedWorkbook.Write(
                filePath,
                sheets.Select(s => (s.SheetName, (IReadOnlyList<RecapitulationSheet>)new[] { s })).ToList(),
                BuildStylesheet(), ColumnWidths, ColumnCount, new SingleSectionLayout(), WriteSheet,
                OrientationValues.Landscape);
            return filePath;
        }

        private static int WriteSheet(SheetData sheetData, MergeCells mergeCells, RecapitulationSheet sheet, int firstRow)
        {
            int row = firstRow;

            AddRow(sheetData, row, 22D, TextCell(1, row, sheet.Title, StyleTitle));
            Merge(mergeCells, 1, row, ColumnCount, row);
            row++;
            AddRow(sheetData, row, 20D, TextCell(1, row, sheet.Subtitle, StyleSubtitle));
            Merge(mergeCells, 1, row, ColumnCount, row);
            row += 2; // one empty row

            // two header rows: the names and the units
            int headerRow = row;
            var names = new Row { RowIndex = (uint)headerRow, Height = 48D, CustomHeight = true };
            var units = new Row { RowIndex = (uint)(headerRow + 1) };
            for (int c = 0; c < ColumnCount; c++)
            {
                names.Append(TextCell(c + 1, headerRow, Headers[c], StyleHeader));
                units.Append(TextCell(c + 1, headerRow + 1, Units[c], StyleHeader));
                if (Units[c].Length == 0) Merge(mergeCells, c + 1, headerRow, c + 1, headerRow + 1);
            }
            sheetData.Append(names);
            sheetData.Append(units);
            row = headerRow + 2;

            // the object name over the full width
            var objectRow = new Row { RowIndex = (uint)row };
            for (int c = 1; c <= ColumnCount; c++) objectRow.Append(c == 1 ? TextCell(1, row, sheet.ObjectName, StyleObject) : EmptyCell(c, row, StyleObject));
            sheetData.Append(objectRow);
            Merge(mergeCells, 1, row, ColumnCount, row);
            row++;

            foreach (RecapitulationMunicipality municipality in sheet.Municipalities)
            {
                foreach (RecapitulationRow data in municipality.Rows)
                {
                    var dataRow = new Row { RowIndex = (uint)row };
                    dataRow.Append(data.Number > 0
                        ? NumberCell(1, row, data.Number.ToString(CultureInfo.InvariantCulture), StyleIndex)
                        : EmptyCell(1, row, StyleIndex));
                    dataRow.Append(TextCell(2, row, data.Province, StyleText));
                    dataRow.Append(TextCell(3, row, data.Municipality, StyleText));
                    dataRow.Append(TextCell(4, row, data.Settlement, StyleText));
                    dataRow.Append(TextCell(5, row, data.Ekatte, StyleText));
                    AppendValues(dataRow, row, data, StyleInteger, StyleArea);
                    sheetData.Append(dataRow);
                    row++;
                }

                // "Общо за общ. X:" over D:E
                var totalRow = new Row { RowIndex = (uint)row };
                for (int c = 1; c <= 3; c++) totalRow.Append(EmptyCell(c, row, StyleTotalEmpty));
                totalRow.Append(TextCell(4, row, municipality.TotalLabel, StyleTotalLabel));
                totalRow.Append(EmptyCell(5, row, StyleTotalEmpty));
                AppendValues(totalRow, row, municipality.Total, StyleTotalInteger, StyleTotalArea);
                sheetData.Append(totalRow);
                Merge(mergeCells, 4, row, 5, row);
                row++;
            }

            // "Общо за обл. X:" over A:E
            var provinceRow = new Row { RowIndex = (uint)row };
            provinceRow.Append(TextCell(1, row, sheet.TotalLabel, StyleTotalLabel));
            for (int c = 2; c <= 5; c++) provinceRow.Append(EmptyCell(c, row, StyleTotalEmpty));
            AppendValues(provinceRow, row, sheet.Total, StyleTotalInteger, StyleTotalArea);
            sheetData.Append(provinceRow);
            Merge(mergeCells, 1, row, 5, row);
            return row + 1;
        }

        /// <summary>Columns F..L of a землище row or a "Общо" row.</summary>
        private static void AppendValues(Row target, int row, RecapitulationRow data, uint integerStyle, uint areaStyle)
        {
            target.Append(NumberCell(6, row, Int(data.ParcelCount), integerStyle));
            target.Append(NumberCell(7, row, Dec(data.AreaDka), areaStyle));
            target.Append(NumberCell(8, row, Dec(data.RestrictedDka), areaStyle));
            target.Append(NumberCell(9, row, Int(data.PoleCount), integerStyle));
            target.Append(NumberCell(10, row, Dec(data.StepDka), areaStyle));
            target.Append(NumberCell(11, row, Dec(data.AffectedDka), areaStyle));
            target.Append(data.RouteKm.HasValue
                ? NumberCell(12, row, Dec(data.RouteKm.Value), areaStyle)
                : EmptyCell(12, row, areaStyle));
        }

        private static void AddRow(SheetData sheetData, int row, double height, Cell cell)
        {
            var r = new Row { RowIndex = (uint)row, Height = height, CustomHeight = true };
            r.Append(cell);
            sheetData.Append(r);
        }

        private static void Merge(MergeCells mergeCells, int fromColumn, int fromRow, int toColumn, int toRow) =>
            mergeCells.Append(new MergeCell { Reference = new StringValue($"{Ref(fromColumn, fromRow)}:{Ref(toColumn, toRow)}") });

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

        // -----------------------------------------------------------------
        //  Styles (same fonts, fills and borders as the territory balance)
        // -----------------------------------------------------------------

        private static Stylesheet BuildStylesheet()
        {
            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = AreaFormatCode })
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
                Bordered(BorderStyleValues.Thin),      // 1 thin all round
                Bordered(BorderStyleValues.Medium))    // 2 heavier top (Общо rows)
            { Count = 3U };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };

            var cellFormats = new CellFormats(
                new CellFormat(),                                                                    // 0 default
                Format(0U, 2U, 0U, 0U, Align(HorizontalAlignmentValues.Center, false)),              // 1 title
                Format(0U, 3U, 0U, 0U, Align(HorizontalAlignmentValues.Center, false)),              // 2 subtitle
                Format(0U, 1U, 2U, 1U, Align(HorizontalAlignmentValues.Center, true)),               // 3 header and units
                Format(0U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, true)),                 // 4 text
                Format(1U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),               // 5 integer "0"
                Format(164U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),             // 6 area 0.000
                Format(1U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Center, false)),              // 7 municipality number
                Format(0U, 1U, 0U, 1U, Align(HorizontalAlignmentValues.Center, true)),               // 8 object name
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)),                // 9 total label
                Format(1U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),               // 10 total integer
                Format(164U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),             // 11 total area
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)))                // 12 total empty
            { Count = 13U };

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
