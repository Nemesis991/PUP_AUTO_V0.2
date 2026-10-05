using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Writes Регистър_на_стъпките_на_стълбовете.xlsx. Layout: title row, EKATTE title row, a group row ("Стълб", "Собственик"),
    /// the 12 headers, a row with the numbers 1..12, then one block per (pole, parcel) piece (first owner on the block row,
    /// the other owners below with only columns 11-12 filled) and the count row. Only the two header group cells are merged.
    /// Areas are numbers with format 0.000; IDs (including ЕГН/БУЛСТАТ) are text.
    /// </summary>
    public static class PoleStepsRegisterExporter
    {
        public const string SheetName = "Регистър стъпки";
        public const string NumberFormatCode = "0.000";
        public const string PoleGroupTitle = "Стълб";
        public const string OwnerGroupTitle = "Собственик";

        public static readonly string[] Headers =
        {
            "Номер на стълба",
            "Площ на стъпката в имота [дка]",
            "Номер на имот",
            "Подотдели",
            "Трайно предназначение на територията",
            "Нов НТП",
            "Местност",
            "Категория",
            "Площ на имота в дка",
            "Вид собственост",
            "ЕГН/БУЛСТАТ",
            "Име"
        };

        private const int ColumnCount = 12;      // A..L
        private const int GroupRow = 3;
        private const int HeaderRow = 4;
        private const int NumbersRow = 5;

        // cellXfs indexes, see BuildStylesheet
        private const uint StyleTitle = 1;
        private const uint StyleSubtitle = 2;
        private const uint StyleHeader = 3;
        private const uint StyleText = 4;
        private const uint StyleNumber = 5;
        private const uint StyleIdText = 6;
        private const uint StyleIndex = 7;
        private const uint StyleTotalLabel = 8;
        private const uint StyleTotalNumber = 9;
        private const uint StyleTotalEmpty = 10;

        private static readonly double[] ColumnWidths = { 14, 16, 17, 12, 22, 26, 16, 10, 12, 18, 16, 36 };

        /// <summary>Writes a workbook with ONE sheet (<see cref="SheetName"/>) holding one землище and returns its path.</summary>
        public static string Export(PoleStepsRegister register, string outputDir) =>
            Export(new[] { (SheetName, (IReadOnlyList<PoleStepsRegister>)new[] { register }) }, outputDir);

        /// <summary>
        /// Writes a workbook with one sheet per item (a municipality) and one section per землище in it, stacked one
        /// under the other, and returns its path.
        /// </summary>
        public static string Export(IReadOnlyList<(string SheetName, IReadOnlyList<PoleStepsRegister> Sections)> sheets, string outputDir)
        {
            string filePath = Path.Combine(outputDir, FileNames.PoleStepsRegisterFile);

            // One section keeps the freeze pane and Print_Titles (the group row, headers and numbers rows)
            var layout = new SingleSectionLayout { FreezeRows = NumbersRow, FirstTitleRow = GroupRow, LastTitleRow = NumbersRow };
            SectionedWorkbook.Write(filePath, sheets, BuildStylesheet(), ColumnWidths, ColumnCount, layout, WriteSection);
            return filePath;
        }

        /// <summary>Writes one section starting at <paramref name="firstRow"/> and returns the next free row.</summary>
        private static int WriteSection(SheetData sheetData, MergeCells mergeCells, PoleStepsRegister register, int firstRow)
        {
            int titleRowIndex = firstRow;
            int groupRowIndex = firstRow + (GroupRow - 1);
            int headerRowIndex = firstRow + (HeaderRow - 1);
            int numbersRowIndex = firstRow + (NumbersRow - 1);

            // Rows 1-2 of the section: titles (not merged; the text runs over the empty cells to its right)
            var titleRow = new Row { RowIndex = (uint)titleRowIndex, Height = 22D, CustomHeight = true };
            titleRow.Append(TextCell(1, titleRowIndex, register.Title, StyleTitle));
            sheetData.Append(titleRow);

            var subtitleRow = new Row { RowIndex = (uint)(titleRowIndex + 1), Height = 20D, CustomHeight = true };
            subtitleRow.Append(TextCell(1, titleRowIndex + 1, register.Subtitle, StyleSubtitle));
            sheetData.Append(subtitleRow);

            // Row 3: group headers. Columns 3-9 stay empty above their headers (no vertical merges).
            var groupRow = new Row { RowIndex = (uint)groupRowIndex };
            for (int c = 1; c <= ColumnCount; c++)
            {
                string text = c == 1 ? PoleGroupTitle : c == 10 ? OwnerGroupTitle : string.Empty;
                groupRow.Append(text.Length > 0 ? TextCell(c, groupRowIndex, text, StyleHeader) : EmptyCell(c, groupRowIndex, StyleHeader));
            }
            sheetData.Append(groupRow);
            mergeCells.Append(Merge($"A{groupRowIndex}", $"B{groupRowIndex}"));
            mergeCells.Append(Merge($"J{groupRowIndex}", $"L{groupRowIndex}"));

            // Row 4: headers
            var headerRow = new Row { RowIndex = (uint)headerRowIndex, Height = 48D, CustomHeight = true };
            for (int c = 0; c < Headers.Length; c++)
            {
                headerRow.Append(TextCell(c + 1, headerRowIndex, Headers[c], StyleHeader));
            }
            sheetData.Append(headerRow);

            // Row 5: column numbers 1..12
            var numbersRow = new Row { RowIndex = (uint)numbersRowIndex };
            for (int c = 1; c <= ColumnCount; c++)
            {
                numbersRow.Append(NumberCell(c, numbersRowIndex, c, StyleIndex));
            }
            sheetData.Append(numbersRow);

            int rowIndex = numbersRowIndex;
            foreach (PoleStepsRegisterRow data in register.Rows)
            {
                rowIndex++;
                var row = new Row { RowIndex = (uint)rowIndex };
                row.Append(TextCell(1, rowIndex, data.PoleLabel, StyleText));
                row.Append(OptionalNumberCell(2, rowIndex, data.PieceDka, StyleNumber));
                row.Append(TextCell(3, rowIndex, data.ParcelId, StyleText));
                row.Append(TextCell(4, rowIndex, data.Subdivisions, StyleText));
                row.Append(TextCell(5, rowIndex, data.Vidt, StyleText));
                row.Append(TextCell(6, rowIndex, data.Ntp, StyleText));
                row.Append(TextCell(7, rowIndex, data.Mestnost, StyleText));
                row.Append(TextCell(8, rowIndex, data.Category, StyleText));
                row.Append(OptionalNumberCell(9, rowIndex, data.AreaDka, StyleNumber));
                row.Append(TextCell(10, rowIndex, data.Vids, StyleText));
                row.Append(TextCell(11, rowIndex, data.PersonId, StyleIdText));
                row.Append(TextCell(12, rowIndex, data.PersonName, StyleText));
                sheetData.Append(row);
            }

            // Last row: "Брой: N" (distinct poles) in column 1, the sum of the printed pieces in column 2
            rowIndex++;
            var totalRow = new Row { RowIndex = (uint)rowIndex };
            totalRow.Append(TextCell(1, rowIndex, PoleStepsRegisterBuilder.CountLabel + register.PoleCount, StyleTotalLabel));
            totalRow.Append(NumberCell(2, rowIndex, Clean(register.TotalPieceDka), StyleTotalNumber));
            for (int c = 3; c <= ColumnCount; c++) totalRow.Append(EmptyCell(c, rowIndex, StyleTotalEmpty));
            sheetData.Append(totalRow);
            return rowIndex + 1;
        }

        // -----------------------------------------------------------------
        //  Cells
        // -----------------------------------------------------------------

        /// <summary>The builder already holds the printed (rounded) decare values; negative zero is written as 0.</summary>
        private static double Clean(double dka) => dka == 0.0 ? 0.0 : dka;

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

        private static Cell NumberCell(int column, int row, double value, uint style) =>
            new Cell
            {
                CellReference = Ref(column, row),
                StyleIndex = style,
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture))
            };

        private static Cell OptionalNumberCell(int column, int row, double? value, uint style) =>
            value.HasValue ? NumberCell(column, row, Clean(value.Value), style) : EmptyCell(column, row, style);

        private static MergeCell Merge(string from, string to) =>
            new MergeCell { Reference = new StringValue($"{from}:{to}") };

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
                Bordered(BorderStyleValues.Thin),      // 1 thin all round
                Bordered(BorderStyleValues.Medium))    // 2 heavier top (ОБЩО row)
            { Count = 3U };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };

            var cellFormats = new CellFormats(
                new CellFormat(),                                                                    // 0 default
                Format(0U, 2U, 0U, 0U, Align(HorizontalAlignmentValues.Left, false)),                // 1 title
                Format(0U, 3U, 0U, 0U, Align(HorizontalAlignmentValues.Left, false)),                // 2 subtitle
                Format(0U, 1U, 2U, 1U, Align(HorizontalAlignmentValues.Center, true)),               // 3 header
                Format(0U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, true)),                 // 4 text
                Format(164U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Right, false)),             // 5 area 0.000
                Format(49U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Left, false)),               // 6 text format "@" (ЕГН/БУЛСТАТ)
                Format(1U, 0U, 0U, 1U, Align(HorizontalAlignmentValues.Center, false)),              // 7 column numbers, format "0"
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)),                // 8 total label
                Format(164U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Right, false)),             // 9 total number
                Format(0U, 1U, 0U, 2U, Align(HorizontalAlignmentValues.Left, false)))                // 10 total empty
            { Count = 11U };

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
