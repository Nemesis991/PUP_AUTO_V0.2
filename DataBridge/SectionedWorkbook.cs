using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace PUP_AUTO.DataBridge
{
    /// <summary>How a sheet that holds exactly ONE section is set up (several sections: none of this applies).</summary>
    internal sealed class SingleSectionLayout
    {
        /// <summary>Rows above the frozen pane split (the pane scrolls below them).</summary>
        public int FreezeRows { get; set; }

        /// <summary>The rows repeated on every printed page (Print_Titles).</summary>
        public int FirstTitleRow { get; set; }
        public int LastTitleRow { get; set; }

        /// <summary>The header row an autofilter starts at; 0 = no autofilter.</summary>
        public int AutoFilterHeaderRow { get; set; }
    }

    /// <summary>
    /// Writes a workbook with one worksheet per item and one stacked section per report in it, sharing one stylesheet.
    /// Each section is written by the exporter (<c>writeSection</c> returns the next free row). Between sections: two empty
    /// rows and a manual page break, so every землище starts on a new printed page. A sheet with exactly one section keeps the
    /// freeze pane, Print_Titles and autofilter of the single-table layout; with several sections none of them is set,
    /// because each section has its own headers and Print_Titles can only repeat one block.
    /// </summary>
    internal static class SectionedWorkbook
    {
        public const int EmptyRowsBetweenSections = 2;

        public static void Write<T>(
            string filePath,
            IReadOnlyList<(string SheetName, IReadOnlyList<T> Sections)> sheets,
            Stylesheet stylesheet,
            double[] columnWidths,
            int columnCount,
            SingleSectionLayout single,
            Func<SheetData, MergeCells, T, int, int> writeSection)
        {
            using (SpreadsheetDocument document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = stylesheet;
                stylesPart.Stylesheet.Save();

                Sheets sheetList = workbookPart.Workbook.AppendChild(new Sheets());
                var printTitles = new List<DefinedName>();

                for (int index = 0; index < sheets.Count; index++)
                {
                    string sheetName = sheets[index].SheetName;
                    IReadOnlyList<T> sections = sheets[index].Sections;
                    bool oneSection = sections.Count == 1;

                    WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                    var sheetData = new SheetData();
                    var mergeCells = new MergeCells();
                    var rowBreaks = new RowBreaks();

                    int nextRow = 1;
                    for (int s = 0; s < sections.Count; s++)
                    {
                        if (s > 0)
                        {
                            nextRow += EmptyRowsBetweenSections;
                            // the break sits above the first row of the section (Id = the row before it)
                            rowBreaks.Append(new Break { Id = (uint)(nextRow - 1), Max = 16383U, ManualPageBreak = true });
                        }
                        nextRow = writeSection(sheetData, mergeCells, sections[s], nextRow);
                    }
                    int lastRow = nextRow - 1;

                    var columns = new Columns();
                    for (int c = 0; c < columnWidths.Length; c++)
                    {
                        columns.Append(new Column { Min = (uint)(c + 1), Max = (uint)(c + 1), Width = columnWidths[c], CustomWidth = true });
                    }

                    var sheetView = new SheetView { WorkbookViewId = 0U };
                    if (oneSection && single.FreezeRows > 0)
                    {
                        sheetView.Append(new Pane
                        {
                            VerticalSplit = single.FreezeRows,
                            TopLeftCell = "A" + (single.FreezeRows + 1),
                            ActivePane = PaneValues.BottomLeft,
                            State = PaneStateValues.Frozen
                        });
                        sheetView.Append(new Selection { Pane = PaneValues.BottomLeft });
                    }

                    var worksheet = new Worksheet(
                        new SheetProperties(new PageSetupProperties { FitToPage = true }),
                        new SheetViews(sheetView),
                        columns,
                        sheetData);

                    // Element order of the schema: autoFilter, mergeCells, pageMargins, pageSetup, rowBreaks
                    if (oneSection && single.AutoFilterHeaderRow > 0)
                    {
                        worksheet.Append(new AutoFilter
                        {
                            Reference = $"A{single.AutoFilterHeaderRow}:{(char)('A' + columnCount - 1)}{Math.Max(lastRow, single.AutoFilterHeaderRow)}"
                        });
                    }
                    if (mergeCells.HasChildren)
                    {
                        mergeCells.Count = (uint)mergeCells.ChildElements.Count;
                        worksheet.Append(mergeCells);
                    }
                    worksheet.Append(new PageMargins { Left = 0.4, Right = 0.4, Top = 0.6, Bottom = 0.6, Header = 0.3, Footer = 0.3 });
                    worksheet.Append(new PageSetup
                    {
                        PaperSize = 9U, // A4
                        Orientation = OrientationValues.Landscape,
                        FitToWidth = 1U,
                        FitToHeight = 0U // as many pages tall as needed
                    });
                    if (rowBreaks.HasChildren)
                    {
                        uint breaks = (uint)rowBreaks.ChildElements.Count;
                        rowBreaks.Count = breaks;
                        rowBreaks.ManualBreakCount = breaks;
                        worksheet.Append(rowBreaks);
                    }

                    worksheetPart.Worksheet = worksheet;
                    worksheetPart.Worksheet.Save();

                    sheetList.Append(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = (uint)(index + 1),
                        Name = sheetName
                    });

                    if (oneSection && single.LastTitleRow > 0)
                    {
                        // Print_Titles is scoped to its sheet: LocalSheetId is the sheet's position in the workbook
                        printTitles.Add(new DefinedName(
                            $"'{sheetName.Replace("'", "''")}'!${single.FirstTitleRow}:${single.LastTitleRow}")
                        {
                            Name = "_xlnm.Print_Titles",
                            LocalSheetId = (uint)index
                        });
                    }
                }

                if (printTitles.Count > 0)
                {
                    var definedNames = new DefinedNames();
                    foreach (DefinedName name in printTitles) definedNames.Append(name);
                    workbookPart.Workbook.AppendChild(definedNames);
                }
                workbookPart.Workbook.Save();
            }
        }
    }
}
