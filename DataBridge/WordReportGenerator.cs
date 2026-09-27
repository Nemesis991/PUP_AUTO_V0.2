using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.DataBridge
{
    /// <summary>
    /// Generates Word (.docm / .docx) reports by opening template documents,
    /// finding the data table, cloning a template row, and populating it
    /// with parcel, pole, balance, and coordinate data.
    ///
    /// Each template must contain a table with at least one data row
    /// (the row after the header row) that will be cloned for each record.
    /// </summary>
    public class WordReportGenerator
    {
        private readonly Logger _logger;
        private readonly string _templateDir;

        // Template file names in _Templates folder
        private const string Template02_Parcels           = "D306-31Y0-02-0A - Регистър на засегнатите имоти.docm";
        private const string Template03_PoleSteps         = "D306-31Y0-03-0A - Регистър на стъпките на стълбове.docm";
        private const string Template04_BalancesTerritory = "D306-31Y0-04-0A - Баланси територията.docm";
        private const string Template05_BalancesMunicip   = "D306-31Y0-05-0A - Общ Баланс за общината.docm";
        private const string Template06_Recapitulation    = "D306-31Y0-06-0A - Обща рекапитулация.docm";
        private const string Template07_CoordPoles        = "D306-31Y0-07-0A - Координатен регистър на стъпките на стълбовете.docm";
        private const string Template08_CoordServitude    = "D306-31Y0-08-0A - Координатен регистър на сервитута.docm";

        public WordReportGenerator(Logger logger, string projectDirectory)
        {
            _logger = logger;
            _templateDir = Path.Combine(projectDirectory, "_Templates");
        }

        // ================================================================
        //  PUBLIC API
        // ================================================================

        /// <summary>
        /// Generates all 7 Word reports into the specified output directory.
        /// </summary>
        public void GenerateAllReports(
            List<ReportRow> reportRows,
            List<Pole> assignedPoles,
            Dictionary<string, ParcelData> parcelDb,
            string outputDir,
            string settlementName = "",
            string ekatte = "",
            string municipality = "",
            string oblast = "",
            Dictionary<string, List<VertexCoordinate>>? poleVertices = null,
            List<VertexCoordinate>? servitudeVertices = null)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // 02 — Регистър на засегнатите имоти
            GenerateParcelRegister(reportRows, outputDir);

            // 03 — Регистър на стъпките на стълбове
            GeneratePoleStepsRegister(assignedPoles, parcelDb, reportRows, outputDir);

            // 04 — Баланси територията
            GenerateBalancesTerritory(reportRows, outputDir);

            // 05 — Общ Баланс за общината
            GenerateBalancesMunicipality(reportRows, parcelDb, outputDir, settlementName, municipality, oblast);

            // 06 — Обща рекапитулация
            GenerateRecapitulation(reportRows, outputDir);

            // 07 — Координатен регистър на стъпките на стълбовете
            if (poleVertices != null)
                GenerateCoordinateRegisterPoles(assignedPoles, poleVertices, outputDir);

            // 08 — Координатен регистър на сервитута
            if (servitudeVertices != null)
                GenerateCoordinateRegisterServitude(servitudeVertices, outputDir);

            _logger.LogSuccess($"Word reports generated in: {outputDir}");
        }

        // ================================================================
        //  02 — РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ
        // ================================================================

        /// <summary>
        /// Generates the affected parcels register.
        /// Table columns: Номер на имот | Подотдели | Трайно предназначение |
        /// Нов НТП | Местност | Категория | Площ имота дка | Площ с огранич. дка |
        /// Остатък дка | Стълб Номер | Стълб Площ | Вид собственост | ЕГН | Име
        /// </summary>
        private void GenerateParcelRegister(List<ReportRow> data, string outputDir)
        {
            string templatePath = Path.Combine(_templateDir, Template02_Parcels);
            string outputPath = Path.Combine(outputDir, Template02_Parcels);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping parcel register.");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    // Find the data table (skip small header/logo tables by finding the one with the most rows or specific column count)
                    var table = body.Descendants<Table>().OrderByDescending(t => t.Elements<TableRow>().Count()).FirstOrDefault();
                    if (table == null)
                    {
                        _logger.LogWarning("No table found in template.");
                        return;
                    }

                    // Get all rows — find the last data-like row to use as template
                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 3)
                    {
                        _logger.LogWarning("Parcel register template table has fewer than 3 rows.");
                        return;
                    }

                    // The template data row is typically the last row in a small template
                    // (rows[0] = title/merged, rows[1] = header, rows[2] = column numbers, rows[3+] = data)
                    TableRow templateRow = rows.Last();
                    var templateRowParent = templateRow.Parent;

                    // Remove the template row — we'll clone it for each data record
                    templateRow.Remove();

                    int rowNum = 0;
                    foreach (var d in data)
                    {
                        rowNum++;
                        var newRow = (TableRow)templateRow.CloneNode(true);
                        var cells = newRow.Elements<TableCell>().ToList();

                        // Map cells to columns based on the reference document structure
                        // Col 0: Номер на имот
                        // Col 1: Подотдели (SubDivision)
                        // Col 2: Трайно предназначение на територията
                        // Col 3: Нов НТП
                        // Col 4: Местност
                        // Col 5: Категория
                        // Col 6: Площ на имота в дка
                        // Col 7: Площ с ограничение в дка
                        // Col 8: Остатък в дка
                        // Col 9: Номер на стълба (Стълб)
                        // Col 10: Площ на стъпката на стълба [дка]
                        // Col 11: Вид собственост
                        // Col 12: ЕГН/БУЛСТАТ
                        // Col 13: Име
                        SetCellText(cells, 0,  d.ParcelId);
                        SetCellText(cells, 1,  d.SubDivision);
                        SetCellText(cells, 2,  d.TerritoryType);
                        SetCellText(cells, 3,  d.Usage);
                        SetCellText(cells, 4,  d.Locality);
                        SetCellText(cells, 5,  d.Category);
                        SetCellText(cells, 6,  d.DocumentAreaDecares.ToString("F3"));
                        SetCellText(cells, 7,  d.ServitudeAreaDecares.ToString("F3"));
                        SetCellText(cells, 8,  d.RemainderAreaDecares.ToString("F3"));
                        SetCellText(cells, 9,  d.PoleNumbers);
                        SetCellText(cells, 10, d.PoleCount > 0 ? d.PoleAreaDecares.ToString("F3") : "");
                        SetCellText(cells, 11, d.OwnershipType);
                        SetCellText(cells, 12, d.OwnerId);
                        SetCellText(cells, 13, d.OwnerName);

                        table.AppendChild(newRow);
                    }

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Parcel register saved: {outputPath} ({data.Count} rows)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating parcel register: {ex.Message}");
            }
        }

        // ================================================================
        //  03 — РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕ
        // ================================================================

        /// <summary>
        /// Generates the pole steps register.
        /// Table columns: Номер на стълба | Площ стъпка [дка] | Номер на имот |
        /// Предназначение | НТП | Площ имот | Вид собств. | ЕГН | Собственик
        /// </summary>
        private void GeneratePoleStepsRegister(
            List<Pole> poles,
            Dictionary<string, ParcelData> parcelDb,
            List<ReportRow> reportRows,
            string outputDir)
        {
            string templatePath = Path.Combine(_templateDir, Template03_PoleSteps);
            string outputPath = Path.Combine(outputDir, Template03_PoleSteps);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping pole steps register.");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    var table = body.Descendants<Table>().OrderByDescending(t => t.Elements<TableRow>().Count()).FirstOrDefault();
                    if (table == null)
                    {
                        _logger.LogWarning("No table found in pole steps template.");
                        return;
                    }

                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 3) return;

                    TableRow templateRow = rows.Last();
                    templateRow.Remove();

                    var sortedPoles = poles
                        .Where(p => p.OverlappingParcels.Count > 0)
                        .OrderBy(p => p.PoleNumber)
                        .ToList();

                    var flatPoles = new List<(Pole pole, string parcelId, double area)>();
                    foreach(var p in sortedPoles)
                    {
                        foreach (var kvp in p.OverlappingParcels) {
                            flatPoles.Add((p, kvp.Key, kvp.Value));
                        }
                    }

                    foreach (var flat in flatPoles)
                    {
                        var pole = flat.pole;
                        var parcelId = flat.parcelId;
                        double areaDecares = Math.Round(flat.area / 1000.0, 3);

                        var newRow = (TableRow)templateRow.CloneNode(true);
                        var cells = newRow.Elements<TableCell>().ToList();

                        // Look up parcel data
                        parcelDb.TryGetValue(parcelId, out ParcelData? pd);
                        var reportRow = reportRows.FirstOrDefault(r => r.ParcelId == parcelId);

                        // Col 0: №стълб (formatted as "Стълб №XX")
                        // Col 1: Площ стъпка [дка]
                        // Col 2: Номер на имот
                        // Col 3: Трайно предназначение
                        // Col 4: НТП
                        // Col 5: Площ на имота [дка]
                        // Col 6: Вид собственост
                        // Col 7: ЕГН/БУЛСТАТ
                        // Col 8: Собственик (Име)
                        SetCellText(cells, 0, $"№{pole.PoleNumber}");
                        SetCellText(cells, 1, areaDecares.ToString("F3"));
                        SetCellText(cells, 2, parcelId);
                        SetCellText(cells, 3, pd?.TerritoryType ?? "");
                        SetCellText(cells, 4, pd?.Usage ?? "");
                        SetCellText(cells, 5, reportRow != null ? reportRow.DocumentAreaDecares.ToString("F3") : "");
                        SetCellText(cells, 6, pd?.OwnershipType ?? "");
                        SetCellText(cells, 7, pd?.OwnerId ?? "");
                        SetCellText(cells, 8, pd?.OwnerName ?? "");

                        table.AppendChild(newRow);
                    }

                    // Add a totals row
                    var totalsRow = (TableRow)templateRow.CloneNode(true);
                    var totalCells = totalsRow.Elements<TableCell>().ToList();
                    SetCellText(totalCells, 0, "Общо:");
                    SetCellText(totalCells, 1, Math.Round(flatPoles.Sum(p => p.area) / 1000.0, 3).ToString("F3"));
                    for (int i = 2; i < totalCells.Count; i++)
                        SetCellText(totalCells, i, "");
                    table.AppendChild(totalsRow);

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Pole steps register saved: {outputPath} ({poles.Count(p => p.OverlappingParcels.Count > 0)} poles)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating pole steps register: {ex.Message}");
            }
        }

        // ================================================================
        //  04 — БАЛАНСИ ТЕРИТОРИЯТА
        // ================================================================

        /// <summary>
        /// Generates balance tables grouped by Category, OwnershipType,
        /// TerritoryType, and Usage. Each group produces a sub-table.
        /// </summary>
        private void GenerateBalancesTerritory(List<ReportRow> data, string outputDir)
        {
            string templatePath = Path.Combine(_templateDir, Template04_BalancesTerritory);
            string outputPath = Path.Combine(outputDir, Template04_BalancesTerritory);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping balances.");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    var tables = body.Descendants<Table>().ToList();

                    // The template should have 4 balance tables in order:
                    // 0: По категория
                    // 1: По вид собственост
                    // 2: По вид територия
                    // 3: По НТП (начин на трайно ползване)
                    var groupings = new (string Title, Func<ReportRow, string> KeySelector)[]
                    {
                        ("По категория",                    r => string.IsNullOrEmpty(r.Category)      ? "(без категория)"      : r.Category),
                        ("По вид собственост",              r => string.IsNullOrEmpty(r.OwnershipType) ? "(без вид собственост)" : r.OwnershipType),
                        ("По вид територия",                r => string.IsNullOrEmpty(r.TerritoryType) ? "(без вид територия)"   : r.TerritoryType),
                        ("По начин на трайно ползване",     r => string.IsNullOrEmpty(r.Usage)         ? "(без НТП)"             : r.Usage)
                    };

                    for (int t = 0; t < groupings.Length && t < tables.Count; t++)
                    {
                        var table = tables[t];
                        var tblRows = table.Elements<TableRow>().ToList();
                        if (tblRows.Count < 2) continue;

                        // Use last row as template
                        TableRow tplRow = tblRows.Last();
                        tplRow.Remove();

                        var grouped = data
                            .GroupBy(groupings[t].KeySelector)
                            .OrderBy(g => g.Key)
                            .ToList();

                        int rowNum = 0;
                        foreach (var group in grouped)
                        {
                            rowNum++;
                            var newRow = (TableRow)tplRow.CloneNode(true);
                            var cells = newRow.Elements<TableCell>().ToList();

                            // Balance columns:
                            // 0: № (row number)
                            // 1: Category/OwnershipType/etc. value
                            // 2: Брой имоти (count)
                            // 3: Обща площ, дка
                            // 4: Площ сервитут, дка
                            // 5: Брой стълбове
                            // 6: Площ стълбове, дка
                            // 7: Обща засегната площ, дка (servitude + poles)
                            // 8: % (percentage)
                            double servSum = Math.Round(group.Sum(r => r.ServitudeAreaDecares), 3);
                            double poleSum = Math.Round(group.Sum(r => r.PoleAreaDecares), 3);
                            double totalAffected = Math.Round(servSum + poleSum, 3);
                            double totalServ = Math.Round(data.Sum(r => r.ServitudeAreaDecares), 3);
                            double totalPoleAll = Math.Round(data.Sum(r => r.PoleAreaDecares), 3);
                            double totalAll = totalServ + totalPoleAll;
                            double pct = totalAll > 0 ? Math.Round(totalAffected / totalAll * 100.0, 2) : 0;

                            SetCellText(cells, 0, rowNum.ToString());
                            SetCellText(cells, 1, group.Key);
                            SetCellText(cells, 2, group.Count().ToString());
                            SetCellText(cells, 3, Math.Round(group.Sum(r => r.DocumentAreaDecares), 3).ToString("F3"));
                            SetCellText(cells, 4, servSum.ToString("F3"));
                            SetCellText(cells, 5, group.Sum(r => r.PoleCount).ToString());
                            SetCellText(cells, 6, poleSum.ToString("F3"));
                            SetCellText(cells, 7, totalAffected.ToString("F3"));
                            SetCellText(cells, 8, pct.ToString("F2"));

                            table.AppendChild(newRow);
                        }

                        // Totals row
                        var totRow = (TableRow)tplRow.CloneNode(true);
                        var totCells = totRow.Elements<TableCell>().ToList();
                        double totalServAll = Math.Round(data.Sum(r => r.ServitudeAreaDecares), 3);
                        double totalPolesAll = Math.Round(data.Sum(r => r.PoleAreaDecares), 3);
                        SetCellText(totCells, 0, "");
                        SetCellText(totCells, 1, "Общо:");
                        SetCellText(totCells, 2, data.Count.ToString());
                        SetCellText(totCells, 3, Math.Round(data.Sum(r => r.DocumentAreaDecares), 3).ToString("F3"));
                        SetCellText(totCells, 4, totalServAll.ToString("F3"));
                        SetCellText(totCells, 5, data.Sum(r => r.PoleCount).ToString());
                        SetCellText(totCells, 6, totalPolesAll.ToString("F3"));
                        SetCellText(totCells, 7, Math.Round(totalServAll + totalPolesAll, 3).ToString("F3"));
                        SetCellText(totCells, 8, "100.00");
                        table.AppendChild(totRow);
                    }

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Balances territory saved: {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating balances: {ex.Message}");
            }
        }

        // ================================================================
        //  07 — КООРДИНАТЕН РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ
        // ================================================================

        private void GenerateCoordinateRegisterPoles(
            List<Pole> poles,
            Dictionary<string, List<VertexCoordinate>> poleVertices,
            string outputDir)
        {
            string templatePath = Path.Combine(_templateDir, Template07_CoordPoles);
            string outputPath = Path.Combine(outputDir, Template07_CoordPoles);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping coordinate register (poles).");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    var table = body.Descendants<Table>().OrderByDescending(t => t.Elements<TableRow>().Count()).FirstOrDefault();
                    if (table == null) return;

                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 2) return;

                    TableRow tplRow = rows.Last();
                    tplRow.Remove();

                    var sortedPoles = poles.OrderBy(p => p.PoleNumber).ToList();

                    foreach (var pole in sortedPoles)
                    {
                        if (!poleVertices.TryGetValue(pole.PoleId, out var vertices))
                            continue;

                        // Header row for this pole
                        var headerRow = (TableRow)tplRow.CloneNode(true);
                        var hCells = headerRow.Elements<TableCell>().ToList();
                        SetCellText(hCells, 0, $"Стълб №{pole.PoleNumber}");
                        SetCellText(hCells, 1, $"Площ: {pole.PoleAreaDecares:F3} дка");
                        for (int c = 2; c < hCells.Count; c++) SetCellText(hCells, c, "");
                        table.AppendChild(headerRow);

                        // Centroid row
                        var centRow = (TableRow)tplRow.CloneNode(true);
                        var cCells = centRow.Elements<TableCell>().ToList();
                        SetCellText(cCells, 0, $"Център стълб");
                        SetCellText(cCells, 1, pole.Location.X.ToString("F3"));
                        SetCellText(cCells, 2, pole.Location.Y.ToString("F3"));
                        table.AppendChild(centRow);

                        // Vertex rows
                        foreach (var v in vertices)
                        {
                            var vRow = (TableRow)tplRow.CloneNode(true);
                            var vCells = vRow.Elements<TableCell>().ToList();
                            SetCellText(vCells, 0, v.PointLabel);
                            SetCellText(vCells, 1, v.X.ToString("F3"));
                            SetCellText(vCells, 2, v.Y.ToString("F3"));
                            table.AppendChild(vRow);
                        }
                    }

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Coordinate register (poles) saved: {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating coordinate register (poles): {ex.Message}");
            }
        }

        // ================================================================
        //  08 — КООРДИНАТЕН РЕГИСТЪР НА СЕРВИТУТА
        // ================================================================

        private void GenerateCoordinateRegisterServitude(
            List<VertexCoordinate> servitudeVertices,
            string outputDir)
        {
            string templatePath = Path.Combine(_templateDir, Template08_CoordServitude);
            string outputPath = Path.Combine(outputDir, Template08_CoordServitude);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping coordinate register (servitude).");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    var table = body.Descendants<Table>().OrderByDescending(t => t.Elements<TableRow>().Count()).FirstOrDefault();
                    if (table == null) return;

                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 2) return;

                    TableRow tplRow = rows.Last();
                    tplRow.Remove();

                    // The servitude coordinate register lists left and right boundary
                    // vertices in a two-column layout. We write all vertices sequentially.
                    foreach (var v in servitudeVertices)
                    {
                        var newRow = (TableRow)tplRow.CloneNode(true);
                        var cells = newRow.Elements<TableCell>().ToList();

                        // Col 0: Point label/number
                        // Col 1: X coordinate
                        // Col 2: Y coordinate
                        SetCellText(cells, 0, v.PointLabel);
                        SetCellText(cells, 1, v.X.ToString("F3"));
                        SetCellText(cells, 2, v.Y.ToString("F3"));

                        table.AppendChild(newRow);
                    }

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Coordinate register (servitude) saved: {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating coordinate register (servitude): {ex.Message}");
            }
        }

        // ================================================================
        //  05 — ОБЩ БАЛАНС ЗА ОБЩИНАТА
        // ================================================================

        /// <summary>
        /// Generates a municipality-level balance summary.
        /// Groups data by settlement (Ekatte from ParcelData) and produces
        /// a single aggregate table with totals per settlement.
        /// </summary>
        private void GenerateBalancesMunicipality(
            List<ReportRow> data,
            Dictionary<string, ParcelData> parcelDb,
            string outputDir,
            string settlementName = "",
            string municipality = "",
            string oblast = "")
        {
            string templatePath = Path.Combine(_templateDir, Template05_BalancesMunicip);
            string outputPath = Path.Combine(outputDir, Template05_BalancesMunicip);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping municipality balance.");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    var table = body.Descendants<Table>().OrderByDescending(t => t.Elements<TableRow>().Count()).FirstOrDefault();
                    if (table == null)
                    {
                        _logger.LogWarning("No table found in municipality balance template.");
                        return;
                    }

                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 2) return;

                    TableRow tplRow = rows.Last();
                    tplRow.Remove();

                    // Group by settlement — derive settlement name from Ekatte via parcelDb
                    // Use the Ekatte field as the settlement key
                    Func<ReportRow, string> settlementSelector = r =>
                    {
                        if (parcelDb.TryGetValue(r.ParcelId, out ParcelData? pd) && pd != null
                            && !string.IsNullOrEmpty(pd.Ekatte))
                        {
                            return pd.Ekatte;
                        }
                        return "(без землище)";
                    };

                    var grouped = data
                        .GroupBy(settlementSelector)
                        .OrderBy(g => g.Key)
                        .ToList();

                    int rowNum = 0;
                    foreach (var group in grouped)
                    {
                        rowNum++;
                        var newRow = (TableRow)tplRow.CloneNode(true);
                        var cells = newRow.Elements<TableCell>().ToList();

                        double servSum = Math.Round(group.Sum(r => r.ServitudeAreaDecares), 3);
                        double poleSum = Math.Round(group.Sum(r => r.PoleAreaDecares), 3);
                        double totalAffected = Math.Round(servSum + poleSum, 3);
                        double totalAll = Math.Round(data.Sum(r => r.ServitudeAreaDecares) + data.Sum(r => r.PoleAreaDecares), 3);
                        double pct = totalAll > 0 ? Math.Round(totalAffected / totalAll * 100.0, 2) : 0;

                        // Columns: №, Землище, Брой имоти, Обща площ, Площ сервитут,
                        //          Брой стълбове, Площ стълбове, Обща засегната площ, %
                        SetCellText(cells, 0, rowNum.ToString());
                        SetCellText(cells, 1, group.Key);
                        SetCellText(cells, 2, group.Count().ToString());
                        SetCellText(cells, 3, Math.Round(group.Sum(r => r.DocumentAreaDecares), 3).ToString("F3"));
                        SetCellText(cells, 4, servSum.ToString("F3"));
                        SetCellText(cells, 5, group.Sum(r => r.PoleCount).ToString());
                        SetCellText(cells, 6, poleSum.ToString("F3"));
                        SetCellText(cells, 7, totalAffected.ToString("F3"));
                        SetCellText(cells, 8, pct.ToString("F2"));

                        table.AppendChild(newRow);
                    }

                    // Totals row
                    var totRow = (TableRow)tplRow.CloneNode(true);
                    var totCells = totRow.Elements<TableCell>().ToList();
                    double totalServAll = Math.Round(data.Sum(r => r.ServitudeAreaDecares), 3);
                    double totalPolesAll = Math.Round(data.Sum(r => r.PoleAreaDecares), 3);
                    SetCellText(totCells, 0, "");
                    SetCellText(totCells, 1, "Общо:");
                    SetCellText(totCells, 2, data.Count.ToString());
                    SetCellText(totCells, 3, Math.Round(data.Sum(r => r.DocumentAreaDecares), 3).ToString("F3"));
                    SetCellText(totCells, 4, totalServAll.ToString("F3"));
                    SetCellText(totCells, 5, data.Sum(r => r.PoleCount).ToString());
                    SetCellText(totCells, 6, totalPolesAll.ToString("F3"));
                    SetCellText(totCells, 7, Math.Round(totalServAll + totalPolesAll, 3).ToString("F3"));
                    SetCellText(totCells, 8, "100.00");
                    table.AppendChild(totRow);

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Municipality balance saved: {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating municipality balance: {ex.Message}");
            }
        }

        // ================================================================
        //  06 — ОБЩА РЕКАПИТУЛАЦИЯ
        // ================================================================

        /// <summary>
        /// Generates a general recapitulation document summarizing
        /// totals per territory type across all parcels.
        /// </summary>
        private void GenerateRecapitulation(List<ReportRow> data, string outputDir)
        {
            string templatePath = Path.Combine(_templateDir, Template06_Recapitulation);
            string outputPath = Path.Combine(outputDir, Template06_Recapitulation);

            if (!File.Exists(templatePath))
            {
                _logger.LogWarning($"Template not found: {templatePath}. Skipping recapitulation.");
                return;
            }

            try
            {
                byte[] templateBytes = File.ReadAllBytes(templatePath);
                using (MemoryStream mem = new MemoryStream())
                {
                    mem.Write(templateBytes, 0, templateBytes.Length);
                    using (var doc = WordprocessingDocument.Open(mem, true))
                    {
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) return;

                    var table = body.Descendants<Table>().OrderByDescending(t => t.Elements<TableRow>().Count()).FirstOrDefault();
                    if (table == null)
                    {
                        _logger.LogWarning("No table found in recapitulation template.");
                        return;
                    }

                    var rows = table.Elements<TableRow>().ToList();
                    if (rows.Count < 2) return;

                    TableRow tplRow = rows.Last();
                    tplRow.Remove();

                    // Group by TerritoryType for recapitulation
                    var grouped = data
                        .GroupBy(r => string.IsNullOrEmpty(r.TerritoryType)
                            ? "(без вид територия)" : r.TerritoryType)
                        .OrderBy(g => g.Key)
                        .ToList();

                    int rowNum = 0;
                    foreach (var group in grouped)
                    {
                        rowNum++;
                        var newRow = (TableRow)tplRow.CloneNode(true);
                        var cells = newRow.Elements<TableCell>().ToList();

                        // Columns: №, Вид територия, Брой имоти, Обща площ (дка),
                        //          Площ сервитут (дка), Брой стълбове, Площ стълбове (дка)
                        SetCellText(cells, 0, rowNum.ToString());
                        SetCellText(cells, 1, group.Key);
                        SetCellText(cells, 2, group.Count().ToString());
                        SetCellText(cells, 3, Math.Round(group.Sum(r => r.DocumentAreaDecares), 3).ToString("F3"));
                        SetCellText(cells, 4, Math.Round(group.Sum(r => r.ServitudeAreaDecares), 3).ToString("F3"));
                        SetCellText(cells, 5, group.Sum(r => r.PoleCount).ToString());
                        SetCellText(cells, 6, Math.Round(group.Sum(r => r.PoleAreaDecares), 3).ToString("F3"));

                        table.AppendChild(newRow);
                    }

                    // Grand totals row
                    var totRow = (TableRow)tplRow.CloneNode(true);
                    var totCells = totRow.Elements<TableCell>().ToList();
                    SetCellText(totCells, 0, "");
                    SetCellText(totCells, 1, "Общо:");
                    SetCellText(totCells, 2, data.Count.ToString());
                    SetCellText(totCells, 3, Math.Round(data.Sum(r => r.DocumentAreaDecares), 3).ToString("F3"));
                    SetCellText(totCells, 4, Math.Round(data.Sum(r => r.ServitudeAreaDecares), 3).ToString("F3"));
                    SetCellText(totCells, 5, data.Sum(r => r.PoleCount).ToString());
                    SetCellText(totCells, 6, Math.Round(data.Sum(r => r.PoleAreaDecares), 3).ToString("F3"));
                    table.AppendChild(totRow);

                    doc.MainDocumentPart?.Document?.Save();
                    }
                    File.WriteAllBytes(outputPath, mem.ToArray());
                }

                _logger.LogSuccess($"Recapitulation saved: {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating recapitulation: {ex.Message}");
            }
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        /// <summary>
        /// Sets the text of a table cell at the given index.
        /// Preserves the existing paragraph formatting (font, size, alignment)
        /// from the template but replaces the text content.
        /// </summary>
        private void SetCellText(List<TableCell> cells, int index, string text)
        {
            if (index < 0 || index >= cells.Count) return;

            var cell = cells[index];
            var para = cell.Elements<Paragraph>().FirstOrDefault();

            if (para == null)
            {
                para = new Paragraph();
                cell.AppendChild(para);
            }

            // Preserve RunProperties from the first existing Run (font, size, bold, etc.)
            RunProperties? existingProps = null;
            var existingRun = para.Elements<Run>().FirstOrDefault();
            if (existingRun?.RunProperties != null)
            {
                existingProps = (RunProperties)existingRun.RunProperties.CloneNode(true);
            }

            // Clear all existing Runs
            para.RemoveAllChildren<Run>();

            // Create new Run with preserved formatting
            var newRun = new Run();
            if (existingProps != null)
            {
                newRun.RunProperties = existingProps;
            }
            newRun.AppendChild(new Text(text ?? string.Empty)
            {
                Space = SpaceProcessingModeValues.Preserve
            });

            para.AppendChild(newRun);
        }
    }
}
