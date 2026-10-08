using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.DataBridge
{
    public static class MvpMathTestExporter
    {
        public static void ExportMathTest(List<ParcelData> parcels, string outputDir)
        {
            string filePath = Path.Combine(outputDir, FileNames.MvpMathTestFile);

            using (SpreadsheetDocument spreadsheetDocument = WorkbookFile.Create(filePath))
            {
                // Add a WorkbookPart to the document.
                WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                // Add a WorksheetPart to the WorkbookPart.
                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData());

                // Add Sheets to the Workbook.
                Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());

                // Append a new worksheet and associate it with the workbook.
                Sheet sheet = new Sheet() { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Math Test" };
                sheets.Append(sheet);

                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
                if (sheetData == null) return;

                uint rowIndex = 1;
                MergeCells mergeCells = new MergeCells();

                // Add Headers
                Row headerRow = new Row();
                headerRow.Append(
                    CreateCell("1. Номер на имот"),
                    CreateCell("2. Площ на имота в дка"),
                    CreateCell("3. Брутна площ с ограничение в дка"),
                    CreateCell("4. Нетна площ с ограничение в дка"),
                    CreateCell("5. Площ на стълба в дка"),
                    CreateCell("6. Остатък в дка"),
                    CreateCell("7. Математическа разлика в дка"),
                    CreateCell("8. Номер на стълба")
                );
                sheetData.Append(headerRow);

                // Add Data Rows
                foreach (var parcel in parcels)
                {
                    // The balance check decides OK/ГРЕШКА on the raw, unrounded square-meter
                    // difference — never on an already-rounded decare value.
                    double diffSqm = parcel.ServitudeGrossAreaSqm - (parcel.ServitudeNetAreaSqm + parcel.PoleAreaSqm);
                    string checkStatus = Math.Abs(diffSqm) <= GeometryTolerances.BalanceToleranceSqm ? "ОК" : "ГРЕШКА";
                    string diffStr = $"{AreaUnits.FormatDka(diffSqm)} - {checkStatus}";

                    // If there is no pole, we output 0 for ServitudeNetAreaSqm per user request.
                    string netAreaStr = parcel.PoleAreaSqm > GeometryTolerances.PoleAreaPresenceSqm
                        ? AreaUnits.FormatDka(parcel.ServitudeNetAreaSqm)
                        : "0.000";

                    string remainderStr = AreaUnits.FormatDka(parcel.RemainderAreaSqm);

                    int poleCount = parcel.AssignedPoleNumbers?.Count ?? 0;

                    if (poleCount <= 1)
                    {
                        rowIndex++;
                        Row row = new Row();
                        string poleNumbersStr = poleCount == 1 ? parcel.AssignedPoleNumbers![0] : "";
                        row.Append(
                            CreateCell(parcel.ParcelId),
                            CreateCell(AreaUnits.FormatDka(parcel.TotalAreaSqm)),
                            CreateCell(AreaUnits.FormatDka(parcel.ServitudeGrossAreaSqm)),
                            CreateCell(netAreaStr),
                            CreateCell(AreaUnits.FormatDka(parcel.PoleAreaSqm)),
                            CreateCell(remainderStr),
                            CreateCell(diffStr),
                            CreateCell(poleNumbersStr)
                        );
                        sheetData.Append(row);
                    }
                    else
                    {
                        uint startRow = rowIndex + 1;
                        for (int i = 0; i < poleCount; i++)
                        {
                            rowIndex++;
                            Row row = new Row();
                            string pNum = parcel.AssignedPoleNumbers![i];
                            double indArea = parcel.IndividualPoleAreas != null && parcel.IndividualPoleAreas.ContainsKey(pNum) 
                                ? parcel.IndividualPoleAreas[pNum] 
                                : 0;

                            if (i == 0)
                            {
                                row.Append(
                                    CreateCell(parcel.ParcelId),
                                    CreateCell(AreaUnits.FormatDka(parcel.TotalAreaSqm)),
                                    CreateCell(AreaUnits.FormatDka(parcel.ServitudeGrossAreaSqm)),
                                    CreateCell(netAreaStr),
                                    CreateCell(AreaUnits.FormatDka(indArea)),
                                    CreateCell(remainderStr),
                                    CreateCell(diffStr),
                                    CreateCell(pNum)
                                );
                            }
                            else
                            {
                                row.Append(
                                    CreateCell(""),
                                    CreateCell(""),
                                    CreateCell(""),
                                    CreateCell(""),
                                    CreateCell(AreaUnits.FormatDka(indArea)),
                                    CreateCell(""),
                                    CreateCell(""),
                                    CreateCell(pNum)
                                );
                            }
                            sheetData.Append(row);
                        }
                        uint endRow = rowIndex;
                        
                        mergeCells.Append(new MergeCell() { Reference = new StringValue($"A{startRow}:A{endRow}") });
                        mergeCells.Append(new MergeCell() { Reference = new StringValue($"B{startRow}:B{endRow}") });
                        mergeCells.Append(new MergeCell() { Reference = new StringValue($"C{startRow}:C{endRow}") });
                        mergeCells.Append(new MergeCell() { Reference = new StringValue($"D{startRow}:D{endRow}") });
                        mergeCells.Append(new MergeCell() { Reference = new StringValue($"F{startRow}:F{endRow}") });
                        mergeCells.Append(new MergeCell() { Reference = new StringValue($"G{startRow}:G{endRow}") });
                    }
                }

                if (mergeCells.ChildElements.Count > 0)
                {
                    worksheetPart.Worksheet.InsertAfter(mergeCells, sheetData);
                }

                worksheetPart.Worksheet.Save();
            }
        }

        private static Cell CreateCell(string text)
        {
            return SheetCells.InlineString(text);
        }
    }
}
