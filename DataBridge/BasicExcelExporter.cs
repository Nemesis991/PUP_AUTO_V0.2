using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.DataBridge
{
    public static class BasicExcelExporter
    {
        public static void ExportMathTest(List<ParcelData> parcels, string outputDir)
        {
            string filePath = Path.Combine(outputDir, "MVP_Math_Test_Parcels.xlsx");

            using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
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
                    CreateCell("1. Идентификатор"),
                    CreateCell("2. TotalArea"),
                    CreateCell("3. ServitudeGrossAreaSqm"),
                    CreateCell("4. ServitudeNetAreaSqm"),
                    CreateCell("5. PoleAreaSqm"),
                    CreateCell("6. Остатък в дка"),
                    CreateCell("7. MathDifference"),
                    CreateCell("8. PoleNumbers")
                );
                sheetData.Append(headerRow);

                // Add Data Rows
                foreach (var parcel in parcels)
                {
                    double mathDiff = Math.Round(parcel.ServitudeGrossAreaSqm - (parcel.ServitudeNetAreaSqm + parcel.PoleAreaSqm), 3);
                    string checkStatus = Math.Abs(mathDiff) <= 0.001 ? "ОК" : "ГРЕШКА";
                    string diffStr = $"{mathDiff} - {checkStatus}";

                    // If there is no pole, we output 0 for ServitudeNetAreaSqm per user request.
                    string netAreaStr = parcel.PoleAreaSqm > 0.001
                        ? Math.Round(parcel.ServitudeNetAreaSqm, 2).ToString()
                        : "0";

                    double remainderSqm = Math.Round(parcel.TotalAreaSqm - parcel.ServitudeNetAreaSqm - parcel.PoleAreaSqm, 2);
                    string remainderStr = remainderSqm.ToString();

                    int poleCount = parcel.AssignedPoleNumbers?.Count ?? 0;

                    if (poleCount <= 1)
                    {
                        rowIndex++;
                        Row row = new Row();
                        string poleNumbersStr = poleCount == 1 ? parcel.AssignedPoleNumbers[0] : "";
                        row.Append(
                            CreateCell(parcel.ParcelId),
                            CreateCell(Math.Round(parcel.TotalAreaSqm, 2).ToString()),
                            CreateCell(Math.Round(parcel.ServitudeGrossAreaSqm, 2).ToString()),
                            CreateCell(netAreaStr),
                            CreateCell(Math.Round(parcel.PoleAreaSqm, 2).ToString()),
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
                            string pNum = parcel.AssignedPoleNumbers[i];
                            double indArea = parcel.IndividualPoleAreas != null && parcel.IndividualPoleAreas.ContainsKey(pNum) 
                                ? parcel.IndividualPoleAreas[pNum] 
                                : 0;

                            if (i == 0)
                            {
                                row.Append(
                                    CreateCell(parcel.ParcelId),
                                    CreateCell(Math.Round(parcel.TotalAreaSqm, 2).ToString()),
                                    CreateCell(Math.Round(parcel.ServitudeGrossAreaSqm, 2).ToString()),
                                    CreateCell(netAreaStr),
                                    CreateCell(Math.Round(indArea, 2).ToString()),
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
                                    CreateCell(Math.Round(indArea, 2).ToString()),
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
            return new Cell(new InlineString(new Text(text))) { DataType = CellValues.InlineString };
        }
    }
}
