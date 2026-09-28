using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Generates an Excel report by opening a 3-sheet template from _Templates,
    /// inserting data rows that copy the template row style, and shifting
    /// any footer content (formulas / signatures) downward.
    ///
    /// Sheets:
    ///   0 — "Засегнати имоти"  (affected parcels)
    ///   1 — "Стълбове"         (poles)
    ///   2 — "Баланси"          (balance summaries)
    /// </summary>
    public class ExcelReportGenerator
    {
        // ----------------------------------------------------------------
        //  Configuration constants – adjust to match the Excel template
        // ----------------------------------------------------------------

        /// <summary>Row index (0-based) of the first data row in each sheet.</summary>
        private const int DataStartRowIndex = 5;

        /// <summary>Relative path to the Excel template inside the project directory.</summary>
        private const string TemplateFolderName = "_Templates";
        private const string TemplateFileName   = "TemplateX.xls";

        // Sheet names (must match the template exactly)
        private const string SheetNameParcels  = "Засегнати имоти";
        private const string SheetNamePoles    = "Стълбове";
        private const string SheetNameBalances = "Баланси";

        // ---- Column indices: Sheet "Засегнати имоти" ----
        // These must match the 14-column header in TemplateX.xls; the same order is
        // used by the Word parcel register (Template02).
        private const int P_ColParcelId      = 0;
        private const int P_ColSubDivision   = 1;
        private const int P_ColTerritoryType = 2;
        private const int P_ColUsage         = 3;
        private const int P_ColLocality      = 4;
        private const int P_ColCategory      = 5;
        private const int P_ColDocArea       = 6;
        private const int P_ColServArea      = 7;
        private const int P_ColRemainder     = 8;
        private const int P_ColPoleNumbers   = 9;
        private const int P_ColPoleArea      = 10;
        private const int P_ColOwnershipType = 11;
        private const int P_ColOwnerId       = 12;
        private const int P_ColOwnerName     = 13;

        // ---- Column indices: Sheet "Стълбове" ----
        private const int T_ColRowNum    = 0;
        private const int T_ColPoleNum   = 1;
        private const int T_ColPoleArea  = 2;
        private const int T_ColParcelId  = 3;
        private const int T_ColOwnerName = 4;

        // ---- Column indices: Sheet "Баланси" ----
        private const int B_ColGroupValue   = 0;
        private const int B_ColParcelCount  = 1;
        private const int B_ColTotalArea    = 2;
        private const int B_ColServArea     = 3;
        private const int B_ColPoleCount    = 4;
        private const int B_ColPoleArea     = 5;

        // ----------------------------------------------------------------

        private readonly Logger _logger;
        private readonly string _templatePath;

        /// <summary>
        /// Caches one cloned, 3-decimal-formatted cell style per distinct source style,
        /// so repeated area-cell writes don't each allocate a new ICellStyle (the legacy
        /// .xls format caps the number of distinct styles per workbook). Reset per
        /// <see cref="GenerateReport"/> call, since style indices are workbook-specific.
        /// </summary>
        private readonly Dictionary<short, ICellStyle> _areaStyleCache = new Dictionary<short, ICellStyle>();

        /// <param name="projectDirectory">
        /// The root directory of the project; _Templates is resolved relative to it.
        /// </param>
        public ExcelReportGenerator(Logger logger, string projectDirectory)
        {
            _logger = logger;
            _templatePath = Path.Combine(projectDirectory, TemplateFolderName, TemplateFileName);
        }

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Opens the 3-sheet Excel template, populates all sheets with
        /// parcel data, pole data, and LINQ-grouped balance summaries,
        /// then saves the result to <paramref name="outputFilePath"/>.
        /// </summary>
        public void GenerateReport(
            List<ReportRow> data,
            List<Pole> poles,
            Dictionary<string, ParcelData> parcelDb,
            string outputFilePath)
        {
            if (!File.Exists(_templatePath))
            {
                _logger.LogError(
                    $"Excel template not found: {_templatePath}");
                return;
            }

            if (data == null || data.Count == 0)
            {
                _logger.LogWarning("GenerateReport called with empty data. Aborting.");
                return;
            }

            _areaStyleCache.Clear();

            IWorkbook workbook;
            try
            {
                using (var templateStream = new FileStream(
                           _templatePath, FileMode.Open, FileAccess.Read))
                {
                    // HSSFWorkbook handles the legacy .xls (BIFF8) format
                    workbook = new HSSFWorkbook(templateStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Failed to open Excel template '{_templatePath}': {ex.Message}");
                return;
            }

            try
            {
                // ==============================================================
                //  Sheet 1 — "Засегнати имоти" (Affected Parcels)
                // ==============================================================
                ISheet? sheetParcels = workbook.GetSheet(SheetNameParcels);
                if (sheetParcels == null)
                {
                    _logger.LogError(
                        $"Template is missing required sheet '{SheetNameParcels}'.");
                    return;
                }

                PopulateSheet(sheetParcels, DataStartRowIndex, data.Count,
                    (row, styleRow, index) =>
                    {
                        var d = data[index];

                        SetCell(row, P_ColParcelId,      d.ParcelId,             GetCellStyle(workbook, styleRow, P_ColParcelId));
                        SetCell(row, P_ColSubDivision,   d.SubDivision,          GetCellStyle(workbook, styleRow, P_ColSubDivision));
                        SetCell(row, P_ColTerritoryType, d.TerritoryType,        GetCellStyle(workbook, styleRow, P_ColTerritoryType));
                        SetCell(row, P_ColUsage,         d.Usage,                GetCellStyle(workbook, styleRow, P_ColUsage));
                        SetCell(row, P_ColLocality,      d.Locality,             GetCellStyle(workbook, styleRow, P_ColLocality));
                        SetCell(row, P_ColCategory,      d.Category,             GetCellStyle(workbook, styleRow, P_ColCategory));
                        SetCell(row, P_ColDocArea,       AreaUnits.SqmToDka(d.DocumentAreaSqM),  GetAreaCellStyle(workbook, GetCellStyle(workbook, styleRow, P_ColDocArea)));
                        SetCell(row, P_ColServArea,      AreaUnits.SqmToDka(d.ServitudeAreaSqM), GetAreaCellStyle(workbook, GetCellStyle(workbook, styleRow, P_ColServArea)));
                        SetCell(row, P_ColRemainder,     AreaUnits.SqmToDka(d.RemainderAreaSqM), GetAreaCellStyle(workbook, GetCellStyle(workbook, styleRow, P_ColRemainder)));
                        SetCell(row, P_ColPoleNumbers,   d.PoleNumbers,          GetCellStyle(workbook, styleRow, P_ColPoleNumbers));
                        SetCell(row, P_ColOwnershipType, d.OwnershipType,        GetCellStyle(workbook, styleRow, P_ColOwnershipType));
                        SetCell(row, P_ColOwnerId,       d.OwnerId,              GetCellStyle(workbook, styleRow, P_ColOwnerId));
                        SetCell(row, P_ColOwnerName,     d.OwnerName,            GetCellStyle(workbook, styleRow, P_ColOwnerName));

                        // The template leaves the pole-step area blank for parcels without a pole.
                        if (d.PoleCount > 0)
                            SetCell(row, P_ColPoleArea, AreaUnits.SqmToDka(d.PoleAreaSqM), GetAreaCellStyle(workbook, GetCellStyle(workbook, styleRow, P_ColPoleArea)));
                        else
                            SetCell(row, P_ColPoleArea, string.Empty, GetCellStyle(workbook, styleRow, P_ColPoleArea));
                    });

                _logger.LogSuccess(
                    $"Sheet '{SheetNameParcels}': {data.Count} rows written.");

                // ==============================================================
                //  Sheet 2 — "Стълбове" (Poles)
                // ==============================================================
                ISheet? sheetPoles = workbook.GetSheet(SheetNamePoles);
                if (sheetPoles == null && workbook.NumberOfSheets > 1)
                {
                    sheetPoles = workbook.GetSheetAt(1);
                    _logger.LogWarning($"Sheet '{SheetNamePoles}' not found by name. Using sheet at index 1: '{sheetPoles?.SheetName}'.");
                }
                if (sheetPoles == null)
                {
                    _logger.LogWarning($"Template is missing required sheet '{SheetNamePoles}'. Skipping poles sheet.");
                }

                // Sort poles by PoleNumber for consistent output
                var sortedPoles = poles
                    .OrderBy(p => p.PoleNumber)
                    .ToList();

                // Flatten poles so each overlapping parcel gets a row
                var flatPoles = new List<(Pole pole, string parcelId, double area)>();
                foreach (var p in sortedPoles)
                {
                    if (p.OverlappingParcels.Count == 0)
                    {
                        flatPoles.Add((p, string.Empty, p.PoleAreaSqM));
                    }
                    else
                    {
                        foreach (var kvp in p.OverlappingParcels)
                        {
                            flatPoles.Add((p, kvp.Key, kvp.Value));
                        }
                    }
                }

                if (sheetPoles != null)
                {
                    PopulateSheet(sheetPoles, DataStartRowIndex, flatPoles.Count,
                        (row, styleRow, index) =>
                        {
                            var flat = flatPoles[index];
                            var pole = flat.pole;
                            int rowNum = index + 1;

                            // Look up parcel owner name via flat.parcelId
                            string ownerName = string.Empty;
                            if (!string.IsNullOrEmpty(flat.parcelId)
                                && parcelDb.TryGetValue(flat.parcelId, out ParcelData? pd)
                                && pd != null)
                            {
                                ownerName = pd.OwnerName;
                            }

                            SetCell(row, T_ColRowNum,    rowNum,               GetCellStyle(workbook, styleRow, T_ColRowNum));
                            SetCell(row, T_ColPoleNum,   pole.PoleNumber,      GetCellStyle(workbook, styleRow, T_ColPoleNum));
                            SetCell(row, T_ColPoleArea,  AreaUnits.SqmToDka(flat.area), GetAreaCellStyle(workbook, GetCellStyle(workbook, styleRow, T_ColPoleArea)));
                            SetCell(row, T_ColParcelId,  flat.parcelId,        GetCellStyle(workbook, styleRow, T_ColParcelId));
                            SetCell(row, T_ColOwnerName, ownerName,            GetCellStyle(workbook, styleRow, T_ColOwnerName));
                        });

                    _logger.LogSuccess(
                        $"Sheet '{sheetPoles.SheetName}': {flatPoles.Count} rows written.");
                }

                // ==============================================================
                //  Sheet 3 — "Баланси" (Balances – 4 grouped summary tables)
                // ==============================================================
                ISheet? sheetBalances = workbook.GetSheet(SheetNameBalances);
                if (sheetBalances == null && workbook.NumberOfSheets > 2)
                {
                    sheetBalances = workbook.GetSheetAt(2);
                    _logger.LogWarning($"Sheet '{SheetNameBalances}' not found by name. Using sheet at index 2: '{sheetBalances?.SheetName}'.");
                }
                if (sheetBalances == null)
                {
                    _logger.LogWarning($"Template is missing required sheet '{SheetNameBalances}'. Skipping balances sheet.");
                }

                if (sheetBalances != null)
                {
                    PopulateBalancesSheet(workbook, sheetBalances, data);

                    _logger.LogSuccess(
                        $"Sheet '{sheetBalances.SheetName}': balance tables written.");
                }

                // ==============================================================
                //  Save
                // ==============================================================
                string? outputDir = Path.GetDirectoryName(outputFilePath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using (var outStream = new FileStream(
                           outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    workbook.Write(outStream);
                }

                _logger.LogSuccess(
                    $"Report saved successfully: {outputFilePath} " +
                    $"({data.Count} parcel rows, {sortedPoles.Count} pole rows).");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error generating report: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        //  Sheet population helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Generic helper: captures the template row for style cloning,
        /// shifts footer rows downward, and calls <paramref name="writeAction"/>
        /// for each data row.
        /// </summary>
        /// <param name="sheet">Target worksheet.</param>
        /// <param name="startRowIndex">0-based index of the first data row in the template.</param>
        /// <param name="rowCount">Number of data rows to insert.</param>
        /// <param name="writeAction">
        /// Delegate (targetRow, styleSourceRow, dataIndex) that writes a single row.
        /// </param>
        private void PopulateSheet(
            ISheet sheet,
            int startRowIndex,
            int rowCount,
            Action<IRow, IRow, int> writeAction)
        {
            if (rowCount <= 0) return;

            // Capture the template data row for style cloning
            IRow templateRow = sheet.GetRow(startRowIndex);
            if (templateRow == null)
            {
                _logger.LogWarning(
                    $"Template row at index {startRowIndex} is null in sheet " +
                    $"'{sheet.SheetName}'. Default style will be used.");
                templateRow = sheet.CreateRow(startRowIndex);
            }

            // Insert blank rows (template already has 1 data row; we need rowCount total)
            int rowsToInsert = rowCount - 1;
            if (rowsToInsert > 0)
            {
                ShiftRowsDown(sheet, startRowIndex + 1, rowsToInsert);
            }

            // Write each data row
            for (int i = 0; i < rowCount; i++)
            {
                int targetRowIndex = startRowIndex + i;
                IRow row = sheet.GetRow(targetRowIndex) ?? sheet.CreateRow(targetRowIndex);
                writeAction(row, templateRow, i);
            }
        }

        /// <summary>
        /// Populates the "Баланси" sheet with 4 summary tables grouped by
        /// Category, OwnershipType, TerritoryType, and Usage.
        /// Tables are written sequentially, each preceded by a title row.
        /// </summary>
        private void PopulateBalancesSheet(
            IWorkbook workbook,
            ISheet sheet,
            List<ReportRow> data)
        {
            // Each balance table: group name, then rows
            var groupings = new (string Title, Func<ReportRow, string> KeySelector)[]
            {
                ("По категория",          r => string.IsNullOrEmpty(r.Category)      ? "(без категория)"      : r.Category),
                ("По вид собственост",    r => string.IsNullOrEmpty(r.OwnershipType) ? "(без вид собственост)" : r.OwnershipType),
                ("По вид територия",      r => string.IsNullOrEmpty(r.TerritoryType) ? "(без вид територия)"   : r.TerritoryType),
                ("По начин на трайно ползване", r => string.IsNullOrEmpty(r.Usage)   ? "(без НТП)"             : r.Usage)
            };

            // Header labels for each summary table
            string[] headerLabels = new[]
            {
                "Групиране",
                "Брой имоти",
                "Обща площ, дка",
                "Площ сервитут, дка",
                "Брой стълбове",
                "Площ стълбове, дка"
            };

            int currentRow = DataStartRowIndex;

            // Get template row style if available
            IRow? templateRow = sheet.GetRow(DataStartRowIndex);

            foreach (var (title, keySelector) in groupings)
            {
                // Determine how many rows this table needs: 1 title + 1 header + N groups + 1 blank spacer
                var grouped = data
                    .GroupBy(keySelector)
                    .OrderBy(g => g.Key)
                    .ToList();

                int tableRows = 1 + 1 + grouped.Count + 1; // title + header + data + spacer

                // Ensure we have room: shift footer rows down
                int lastRow = sheet.LastRowNum;
                if (currentRow <= lastRow)
                {
                    sheet.ShiftRows(currentRow, lastRow, tableRows,
                        copyRowHeight: true,
                        resetOriginalRowHeight: false);
                }

                // ── Title row ──
                IRow titleRow = sheet.GetRow(currentRow) ?? sheet.CreateRow(currentRow);
                ICell titleCell = titleRow.GetCell(B_ColGroupValue) ?? titleRow.CreateCell(B_ColGroupValue);
                titleCell.SetCellType(CellType.String);
                titleCell.SetCellValue(title);

                // Apply bold style to title
                ICellStyle boldStyle = workbook.CreateCellStyle();
                IFont boldFont = workbook.CreateFont();
                boldFont.IsBold = true;
                boldStyle.SetFont(boldFont);
                titleCell.CellStyle = boldStyle;

                currentRow++;

                // ── Header row ──
                IRow headerRow = sheet.GetRow(currentRow) ?? sheet.CreateRow(currentRow);
                for (int col = 0; col < headerLabels.Length; col++)
                {
                    ICell cell = headerRow.GetCell(col) ?? headerRow.CreateCell(col);
                    cell.SetCellType(CellType.String);
                    cell.SetCellValue(headerLabels[col]);
                    cell.CellStyle = boldStyle;
                }
                currentRow++;

                // ── Data rows ──
                foreach (var group in grouped)
                {
                    IRow dataRow = sheet.GetRow(currentRow) ?? sheet.CreateRow(currentRow);
                    ICellStyle? numStyle = templateRow != null
                        ? GetCellStyle(workbook, templateRow, B_ColParcelCount)
                        : null;
                    ICellStyle areaStyle = GetAreaCellStyle(workbook, numStyle);

                    // Sum raw square-meter values, then convert to decares ONCE.
                    SetCell(dataRow, B_ColGroupValue,  group.Key,                                                   null);
                    SetCell(dataRow, B_ColParcelCount, group.Count(),                                               numStyle);
                    SetCell(dataRow, B_ColTotalArea,   AreaUnits.SqmToDka(group.Sum(r => r.DocumentAreaSqM)),       areaStyle);
                    SetCell(dataRow, B_ColServArea,    AreaUnits.SqmToDka(group.Sum(r => r.ServitudeAreaSqM)),      areaStyle);
                    SetCell(dataRow, B_ColPoleCount,   group.Sum(r => r.PoleCount),                                 numStyle);
                    SetCell(dataRow, B_ColPoleArea,    AreaUnits.SqmToDka(group.Sum(r => r.PoleAreaSqM)),           areaStyle);

                    currentRow++;
                }

                // ── Blank spacer row ──
                currentRow++;
            }
        }

        // ----------------------------------------------------------------
        //  Private helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Shifts all rows from <paramref name="firstRowIndex"/> to the end of
        /// the used-range downward by <paramref name="count"/> positions,
        /// preserving formulas and cell styles (footer / signature area).
        /// </summary>
        private void ShiftRowsDown(ISheet sheet, int firstRowIndex, int count)
        {
            // NPOI ShiftRows: (startRow, endRow, count)
            // We shift everything from the first footer row to the last used row.
            int lastUsedRow = sheet.LastRowNum;

            if (firstRowIndex > lastUsedRow)
            {
                // Nothing to shift – footer area is empty
                return;
            }

            sheet.ShiftRows(firstRowIndex, lastUsedRow, count,
                copyRowHeight: true,
                resetOriginalRowHeight: false);
        }

        /// <summary>Creates or retrieves a cell and sets it to a string value.</summary>
        private void SetCell(IRow row, int colIndex, string value, ICellStyle? style)
        {
            ICell cell = row.GetCell(colIndex) ?? row.CreateCell(colIndex);
            cell.SetCellValue(value ?? string.Empty);
            if (style != null) cell.CellStyle = style;
        }

        /// <summary>Creates or retrieves a cell and sets it to a numeric value.</summary>
        private void SetCell(IRow row, int colIndex, double value, ICellStyle? style)
        {
            ICell cell = row.GetCell(colIndex) ?? row.CreateCell(colIndex);
            cell.SetCellValue(value);
            if (style != null) cell.CellStyle = style;
        }

        /// <summary>Creates or retrieves a cell and sets it to an integer value.</summary>
        private void SetCell(IRow row, int colIndex, int value, ICellStyle? style)
        {
            ICell cell = row.GetCell(colIndex) ?? row.CreateCell(colIndex);
            cell.SetCellValue(value);
            if (style != null) cell.CellStyle = style;
        }

        /// <summary>Returns the cell style for a given column in the template row.</summary>
        private ICellStyle? GetCellStyle(IWorkbook workbook, IRow styleSourceRow, int colIndex)
        {
            ICell? templateCell = styleSourceRow.GetCell(colIndex);
            return templateCell?.CellStyle;
        }

        /// <summary>
        /// Returns a cell style identical to <paramref name="baseStyle"/> (borders, font,
        /// alignment) but with a "0.000" number format, so every area value in the report
        /// displays with exactly 3 decimals. Styles are cloned once per distinct source
        /// style and cached in <see cref="_areaStyleCache"/> to stay under the .xls style limit.
        /// </summary>
        private ICellStyle GetAreaCellStyle(IWorkbook workbook, ICellStyle? baseStyle)
        {
            short key = baseStyle?.Index ?? short.MinValue;
            if (_areaStyleCache.TryGetValue(key, out ICellStyle? cached))
                return cached;

            ICellStyle style = workbook.CreateCellStyle();
            if (baseStyle != null)
            {
                style.CloneStyleFrom(baseStyle);
            }
            style.DataFormat = workbook.CreateDataFormat().GetFormat("0.000");

            _areaStyleCache[key] = style;
            return style;
        }
    }
}
