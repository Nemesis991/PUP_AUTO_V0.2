using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PUP_AUTO.Core;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Geometry;
using PUP_AUTO.Semantics;

// Register the command so AutoCAD discovers it at NETLOAD time
[assembly: CommandClass(typeof(PUP_AUTO.UI.MainCommands))]

namespace PUP_AUTO.UI
{
    /// <summary>
    /// Entry point for all user-facing AutoCAD commands.
    /// </summary>
    public class MainCommands
    {
        // Default subfolder / file names (relative to the drawing location)
        private const string TestFilesFolder  = "_TestFiles";
        private const string CadDatabaseFile  = "TemplateC.cad";
        private const string OutputFileName   = "PUP_Report.xls";
        private const string LogFileName      = "PUP_AUTO_Logs.txt";

        // ------------------------------------------------------------------
        //  PUP_GENERATE command
        // ------------------------------------------------------------------

        [CommandMethod("PUP_GENERATE")]
        public void PupGenerate()
        {
            // ── Resolve project directory from the active drawing path ──
            string projectDir = ResolveProjectDirectory();

            // ── Bootstrap shared services ────────────────────────────────
            string logPath = Path.Combine(projectDir, LogFileName);
            var logger  = new Logger(logPath);
            var txMgr   = new Core.TransactionManager(logger);
            var topo    = new TopologyProcessor(logger);

            Editor ed = txMgr.GetEditor();

            try
            {
                ed.WriteMessage("\n═══ PUP_GENERATE ═══\n");

                // ==========================================================
                // Step 1 — User selects geometry
                // ==========================================================
                ed.WriteMessage("\n── Step 1: Select geometry ──\n");

                using (Transaction tr = txMgr.StartTransaction())
                {
                    // 1a. Servitude polyline (single)
                    Polyline? servitudePline = txMgr.SelectSinglePolyline(
                        tr, "\nSelect the Servitude (Right of Way) polyline: ");
                    if (servitudePline == null)
                    {
                        ed.WriteMessage("\n[ABORT] No servitude selected.\n");
                        return;
                    }

                    // 1b. Pole polylines (multiple)
                    var polePolylines = txMgr.SelectMultiplePolylines(
                        tr, "\nSelect Pole polylines: ");
                    if (polePolylines.Count == 0)
                    {
                        ed.WriteMessage("\n[ABORT] No poles selected.\n");
                        return;
                    }

                    // 1c. Parcel polylines (multiple)
                    var parcelPolylines = txMgr.SelectMultiplePolylines(
                        tr, "\nSelect Parcel polylines: ");
                    if (parcelPolylines.Count == 0)
                    {
                        ed.WriteMessage("\n[ABORT] No parcels selected.\n");
                        return;
                    }

                    ed.WriteMessage(
                        $"\n  Servitude: 1 | Poles: {polePolylines.Count} " +
                        $"| Parcels: {parcelPolylines.Count}\n");

                    // ======================================================
                    // Step 2 — Load external database
                    // ======================================================
                    ed.WriteMessage("\n── Step 2: Load CAD database ──\n");

                    string cadFilePath = Path.Combine(
                        projectDir, TestFilesFolder, CadDatabaseFile);

                    var reader  = new CadLibraryReader(logger);
                    Dictionary<string, ParcelData> parcelDb =
                        reader.LoadLibrary(cadFilePath);

                    ed.WriteMessage(
                        $"  Database loaded: {parcelDb.Count} records.\n");

                    // ======================================================
                    // Step 3 — Topology calculations
                    // ======================================================
                    ed.WriteMessage("\n── Step 3: Calculate intersections ──\n");

                    // 3a. Servitude ↔ Parcel intersections
                    Dictionary<string, double> servitudeAreas =
                        topo.CalculateServitudeIntersections(
                            servitudePline, parcelPolylines, tr);

                    ed.WriteMessage(
                        $"  Servitude intersections: {servitudeAreas.Count} parcels.\n");

                    // 3b. Pole → Parcel dominant-area assignment
                    List<Pole> assignedPoles =
                        topo.AssignPolesToParcels(
                            polePolylines, parcelPolylines, tr);

                    int assignedCount =
                        assignedPoles.Count(p => !string.IsNullOrEmpty(p.AssignedParcelId));
                    ed.WriteMessage(
                        $"  Poles processed: {assignedPoles.Count} " +
                        $"(assigned: {assignedCount}).\n");

                    // ======================================================
                    // Step 4 — Merge results into ReportRows
                    // ======================================================
                    ed.WriteMessage("\n── Step 4: Merge results ──\n");

                    List<ReportRow> reportRows = MergeResults(
                        parcelPolylines,
                        parcelDb,
                        servitudeAreas,
                        assignedPoles,
                        logger);

                    ed.WriteMessage(
                        $"  Report rows generated: {reportRows.Count}\n");

                    // ======================================================
                    // Step 5 — Generate Excel report
                    // ======================================================
                    ed.WriteMessage("\n── Step 5: Generate Excel report ──\n");

                    string outputPath = Path.Combine(projectDir, OutputFileName);

                    var excelGen = new ExcelReportGenerator(logger, projectDir);
                    excelGen.GenerateReport(reportRows, assignedPoles, parcelDb, outputPath);

                    ed.WriteMessage($"  Report saved: {outputPath}\n");

                    // ======================================================
                    // Step 6 — Summary
                    // ======================================================
                    int warningCount = reportRows.Count(r => r.Owner == "NO DATA");
                    ed.WriteMessage(
                        $"\n═══ PUP_GENERATE COMPLETE ═══\n" +
                        $"  Processed {reportRows.Count} parcels.\n" +
                        $"  Poles assigned: {assignedCount}/{assignedPoles.Count}.\n" +
                        $"  Missing semantic data: {warningCount} parcels.\n" +
                        $"  Check log file for warnings: {logPath}\n");

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError($"PUP_GENERATE failed: {ex.Message}\n{ex.StackTrace}");
                ed.WriteMessage($"\n[ERROR] PUP_GENERATE failed: {ex.Message}\n");
                ed.WriteMessage($"  See log: {logPath}\n");
            }
        }

        // ------------------------------------------------------------------
        //  Result merging with missing-data check
        // ------------------------------------------------------------------

        /// <summary>
        /// Merges servitude areas, pole assignments, and the external
        /// database into a flat list of <see cref="ReportRow"/>.
        /// CRUCIAL CHECK: if a ParcelId from geometry is missing from
        /// the database, logs a Warning and uses "NO DATA" for the Owner.
        /// </summary>
        private static List<ReportRow> MergeResults(
            List<KeyValuePair<string, Polyline>> parcelPolylines,
            Dictionary<string, ParcelData> parcelDb,
            Dictionary<string, double> servitudeAreas,
            List<Pole> assignedPoles,
            Logger logger)
        {
            var rows = new List<ReportRow>();

            // Collect all unique ParcelIds that appear in the geometry
            var geometryParcelIds = new HashSet<string>(
                parcelPolylines.Select(kvp => kvp.Key));

            // Also include any ParcelId assigned to a pole that may not have
            // its own polyline in the selection (edge case)
            foreach (var pole in assignedPoles)
            {
                if (!string.IsNullOrEmpty(pole.AssignedParcelId))
                {
                    geometryParcelIds.Add(pole.AssignedParcelId);
                }
            }

            foreach (string parcelId in geometryParcelIds.OrderBy(id => id))
            {
                // ── CRUCIAL CHECK ──
                ParcelData? dbRecord = null;
                string owner;
                if (parcelDb.TryGetValue(parcelId, out dbRecord)
                    && dbRecord != null)
                {
                    owner = dbRecord.Owner;
                }
                else
                {
                    owner = "NO DATA";
                    logger.LogWarning(
                        $"ParcelId '{parcelId}' exists in CAD geometry but is " +
                        "MISSING from the CadLibraryReader database. " +
                        "Using 'NO DATA' for Owner.");
                }

                // Servitude area for this parcel
                servitudeAreas.TryGetValue(parcelId, out double servArea);

                // Poles assigned to this parcel
                var polesInParcel = assignedPoles
                    .Where(p => p.AssignedParcelId == parcelId)
                    .ToList();
                double poleArea = polesInParcel.Sum(p => p.PoleAreaSqM);
                int poleCount   = polesInParcel.Count;

                rows.Add(new ReportRow
                {
                    ParcelId         = parcelId,
                    Owner            = owner,
                    ServitudeAreaSqM = servArea,
                    PoleAreaSqM      = poleArea,
                    PoleCount        = poleCount,

                    // Cadastral register fields (safe: default to empty if dbRecord is null)
                    SubDivision      = dbRecord?.SubDivision   ?? string.Empty,
                    TerritoryType    = dbRecord?.TerritoryType  ?? string.Empty,
                    Usage            = dbRecord?.Usage          ?? string.Empty,
                    Locality         = dbRecord?.Locality       ?? string.Empty,
                    Category         = dbRecord?.Category       ?? string.Empty,
                    OwnershipType    = dbRecord?.OwnershipType  ?? string.Empty,
                    OwnerId          = dbRecord?.OwnerId        ?? string.Empty,
                    OwnerName        = dbRecord?.OwnerName      ?? string.Empty,
                    DocumentAreaSqM  = dbRecord?.DocumentArea   ?? 0.0
                });
            }

            return rows;
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves the project directory. Prefers the directory of the
        /// active drawing; falls back to the current working directory.
        /// </summary>
        private static string ResolveProjectDirectory()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    string? dir = Path.GetDirectoryName(doc.Name);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        return dir;
                    }
                }
            }
            catch
            {
                // Fall through to fallback
            }

            return Environment.CurrentDirectory;
        }
    }
}
