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
        //  PUP_WINDOW command — Opens the WPF GUI
        // ------------------------------------------------------------------

        [CommandMethod("PUP_WINDOW")]
        public void PupWindow()
        {
            try
            {
                var window = new Windows.MainWindow();
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(window);
            }
            catch (System.Exception ex)
            {
                var ed = Autodesk.AutoCAD.ApplicationServices.Application
                    .DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[ERROR] Failed to open PUP_AUTO window: {ex.Message}\n");
            }
        }

        // ------------------------------------------------------------------
        //  PUP_SERV command — Segments a selected servitude
        // ------------------------------------------------------------------

        [CommandMethod("PUP_SERV")]
        public void PupServ()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                // 1. Ask user to select the servitude polyline
                PromptEntityOptions peo = new PromptEntityOptions("\nИзберете сервитут (Polyline) за сегментиране: ");
                peo.SetRejectMessage("\nМоля, изберете Polyline!");
                peo.AddAllowedClass(typeof(Polyline), true);
                
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;
                
                // 2. Ask user for the distance
                PromptDoubleOptions pdo = new PromptDoubleOptions("\nВъведете разстояние за сегментиране [20.0]: ");
                pdo.DefaultValue = 20.0;
                pdo.AllowNegative = false;
                pdo.AllowZero = false;
                pdo.UseDefaultValue = true;
                
                PromptDoubleResult pdr = ed.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK) return;
                double dist = pdr.Value;
                
                // 3. Segment and append
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline sourcePline = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
                    if (sourcePline == null) return;
                    
                    using (Polyline cleanServitude = GeometrySanitizer.Sanitize(sourcePline, dist, 0.05))
                    {
                        if (cleanServitude != null)
                        {
                            // Ensure layer
                            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (!lt.Has("segmented SERV"))
                            {
                                lt.UpgradeOpen();
                                LayerTableRecord ltr = new LayerTableRecord();
                                ltr.Name = "segmented SERV";
                                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                                lt.Add(ltr);
                                tr.AddNewlyCreatedDBObject(ltr, true);
                            }
                            
                            Polyline newPline = (Polyline)cleanServitude.Clone();
                            newPline.Layer = "segmented SERV";
                            newPline.ColorIndex = 3; 
                            newPline.ConstantWidth = 0.5;
                            
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                            btr.AppendEntity(newPline);
                            tr.AddNewlyCreatedDBObject(newPline, true);
                            
                            ed.WriteMessage($"\nУспешно сегментиран сервитут! Дължина на сегментите: {dist}м. Слой: 'segmented SERV'.");
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ГРЕШКА] PUP_SERV: {ex.Message}\n");
            }
        }

        // ------------------------------------------------------------------
        //  PUP_DRAW_FOOTPRINTS command — Diagnostic footprint extraction
        // ------------------------------------------------------------------

        [CommandMethod("PUP_DRAW_FOOTPRINTS")]
        public void PupDrawFootprints()
        {
            string projectDir = ResolveProjectDirectory();
            string logPath = Path.Combine(projectDir, LogFileName);
            var logger = new Logger(logPath);
            var txMgr = new Core.TransactionManager(logger);
            Editor ed = txMgr.GetEditor();

            try
            {
                ed.WriteMessage("\n═══ PUP_DRAW_FOOTPRINTS ═══\n");

                using (Transaction tr = txMgr.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(txMgr.GetDatabase().BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    int processed = 0;
                    int success = 0;
                    int failed = 0;

                    // Ensure required layers exist
                    LayerTable lt = (LayerTable)tr.GetObject(txMgr.GetDatabase().LayerTableId, OpenMode.ForRead);
                    Action<string, short, LineWeight> EnsureLayer = (name, colorIndex, lw) =>
                    {
                        if (!lt.Has(name))
                        {
                            lt.UpgradeOpen();
                            LayerTableRecord ltr = new LayerTableRecord();
                            ltr.Name = name;
                            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                            ltr.LineWeight = lw;
                            lt.Add(ltr);
                            tr.AddNewlyCreatedDBObject(ltr, true);
                        }
                    };

                    EnsureLayer("POLE_STEPS", 7, LineWeight.LineWeight030);
                    EnsureLayer("diagonali", 8, LineWeight.LineWeight009);
                    EnsureLayer("Текст", 7, LineWeight.ByLayer);

                    foreach (ObjectId objId in btr)
                    {
                        var blockRef = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                        if (blockRef == null) continue;

                        processed++;
                        var result = PoleFootprintExtractor.ExtractFootprint(blockRef, tr, logger);

                        if (result.FootprintPolyline != null)
                        {
                            success++;
                            var pline = result.FootprintPolyline;
                            pline.Layer = "POLE_STEPS";
                            pline.ColorIndex = 7;
                            pline.LineWeight = LineWeight.LineWeight030;
                            btr.AppendEntity(pline);
                            tr.AddNewlyCreatedDBObject(pline, true);

                            // Draw DBText and Diagonals
                            
                            // Diagonals
                            if (pline.NumberOfVertices >= 4)
                            {
                                Point3d p0 = pline.GetPoint3dAt(0);
                                Point3d p1 = pline.GetPoint3dAt(1);
                                Point3d p2 = pline.GetPoint3dAt(2);
                                Point3d p3 = pline.GetPoint3dAt(3);

                                Line diag1 = new Line(p0, p2);
                                diag1.Layer = "diagonali";
                                diag1.ColorIndex = 8;
                                diag1.LineWeight = LineWeight.LineWeight009;
                                btr.AppendEntity(diag1);
                                tr.AddNewlyCreatedDBObject(diag1, true);

                                Line diag2 = new Line(p1, p3);
                                diag2.Layer = "diagonali";
                                diag2.ColorIndex = 8;
                                diag2.LineWeight = LineWeight.LineWeight009;
                                btr.AppendEntity(diag2);
                                tr.AddNewlyCreatedDBObject(diag2, true);
                            }

                            // Text
                            DBText text = new DBText();
                            text.SetDatabaseDefaults();
                            text.TextString = result.PoleNumber;
                            text.Layer = "Текст";
                            text.ColorIndex = 7;
                            text.Height = 1.0;
                            
                            // CRITICAL ORDER FOR JUSTIFICATION:
                            text.Position = result.LabelPosition;             // 1. Set base position first
                            text.Justify = AttachmentPoint.BottomCenter;      // 2. Set justification (Matching LISP 'BC')
                            text.AlignmentPoint = result.LabelPosition;       // 3. MUST set AlignmentPoint AFTER Justify
                            text.Rotation = result.LabelRotation;             // 4. Set rotation last
                            
                            btr.AppendEntity(text);
                            tr.AddNewlyCreatedDBObject(text, true);
                        }
                        else
                        {
                            failed++;
                            logger.LogWarning(result.ErrorMessage);
                        }
                    }

                    ed.WriteMessage($"\nFound {processed} blocks. Extracted: {success}. Failed: {failed}.\n");
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError($"PUP_DRAW_FOOTPRINTS failed: {ex.Message}\n{ex.StackTrace}");
                ed.WriteMessage($"\n[ERROR] PUP_DRAW_FOOTPRINTS failed: {ex.Message}\n");
            }
        }

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

                    // 1b. Pole blocks (multiple)
                    var poleBlocks = txMgr.SelectMultipleBlockReferences(
                        tr, "\nSelect Pole blocks: ");
                    if (poleBlocks.Count == 0)
                    {
                        ed.WriteMessage("\n[ABORT] No pole blocks selected.\n");
                        return;
                    }

                    var polePolylines = new List<KeyValuePair<string, Polyline>>();
                    var footprintVerticesDict = new Dictionary<string, List<VertexCoordinate>>();
                    
                    foreach (var kvp in poleBlocks)
                    {
                        var result = PoleFootprintExtractor.ExtractFootprint(kvp.Value, tr, logger);
                        if (result.FootprintPolyline != null)
                        {
                            // In case of multiple blocks, if they don't have XData ID, use handle
                            string poleId = string.IsNullOrEmpty(result.PoleNumber) ? kvp.Key : result.PoleNumber;
                            polePolylines.Add(new KeyValuePair<string, Polyline>(poleId, result.FootprintPolyline));
                            
                            var pts = new List<VertexCoordinate>();
                            for(int i=0; i<result.FootprintPolyline.NumberOfVertices; i++)
                            {
                                var pt = result.FootprintPolyline.GetPoint3dAt(i);
                                pts.Add(new VertexCoordinate { PointIndex = i+1, PointLabel = $"{poleId}-{i+1}", X = Math.Round(pt.X,3), Y = Math.Round(pt.Y,3) });
                            }
                            footprintVerticesDict[poleId] = pts;
                        }
                        else
                        {
                            logger.LogWarning($"Could not extract footprint for block {kvp.Key}: {result.ErrorMessage}");
                        }
                    }

                    if (polePolylines.Count == 0)
                    {
                        ed.WriteMessage("\n[ABORT] No valid pole footprints extracted.\n");
                        return;
                    }

                    // 1c. Parcel polylines (multiple)
                    // Load GeoJSON geometries for spatial matching
                    List<GeoParcel>? geoParcels = null;
                    string cadFilePath2 = Path.Combine(projectDir, TestFilesFolder, CadDatabaseFile);
                    if (cadFilePath2.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
                    {
                        var geoReader = new CadLibraryReader(logger);
                        geoParcels = geoReader.LoadGeoJsonGeometries(cadFilePath2);
                    }

                    var parcelPolylines = txMgr.SelectMultiplePolylines(
                        tr, "\nSelect Parcel polylines: ", geoParcels);
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
                            
                    // Assign footprint vertices to Pole models
                    foreach (var p in assignedPoles)
                    {
                        if (footprintVerticesDict.TryGetValue(p.PoleId, out var fv))
                        {
                            p.FootprintVertices = fv;
                        }
                    }

                    int assignedCount =
                        assignedPoles.Count(p => p.OverlappingParcels.Count > 0);
                    logger.LogSuccess($"Pole extraction complete. Total: {polePolylines.Count}, " +
                        $"Assigned: {assignedCount}");
                    ed.WriteMessage(
                        $"  Poles processed: {assignedPoles.Count} " +
                        $"(assigned: {assignedCount}).\n");

                    // ======================================================
                    // Step 4 — Merge results into ReportRows
                    // ======================================================
                    ed.WriteMessage("\n── Step 4: Merge results ──\n");

                    List<ReportRow> reportRows = MergeResultsStatic(
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
                    // Step 5b — Extract coordinates
                    // ======================================================
                    ed.WriteMessage("\n── Step 5b: Extract coordinates ──\n");

                    var poleVertices = new Dictionary<string, List<VertexCoordinate>>();
                    foreach (var p in assignedPoles)
                    {
                        if (p.FootprintVertices != null && p.FootprintVertices.Count > 0)
                        {
                            poleVertices[p.PoleId] = p.FootprintVertices;
                        }
                    }
                    var servitudeVertices = topo.ExtractPolylineVertices(servitudePline);

                    ed.WriteMessage(
                        $"  Coordinates: {poleVertices.Count} poles, " +
                        $"{servitudeVertices.Count} servitude vertices.\n");

                    // ======================================================
                    // Step 5c — Generate Word reports
                    // ======================================================
                    ed.WriteMessage("\n── Step 5c: Generate Word reports ──\n");

                    var wordGen = new WordReportGenerator(logger, projectDir);
                    wordGen.GenerateAllReports(
                        reportRows, assignedPoles, parcelDb, projectDir,
                        poleVertices: poleVertices,
                        servitudeVertices: servitudeVertices);

                    ed.WriteMessage("  Word reports generated.\n");

                    // ======================================================
                    // Step 6 — Summary
                    // ======================================================
                    int warningCount = reportRows.Count(r => r.Owner == "NO DATA");
                    ed.WriteMessage(
                        $"\n═══ PUP_GENERATE COMPLETE ═══\n" +
                        $"  Processed {reportRows.Count} parcels.\n" +
                        $"  Poles assigned: {assignedCount}/{assignedPoles.Count}.\n" +
                        $"  Missing semantic data: {warningCount} parcels.\n" +
                        $"  Excel report: {outputPath}\n" +
                        $"  Word reports: {projectDir}\n" +
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
        public static List<ReportRow> MergeResultsStatic(
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
                foreach (var poleParcelId in pole.OverlappingParcels.Keys)
                {
                    geometryParcelIds.Add(poleParcelId);
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
                    .Where(p => p.OverlappingParcels.ContainsKey(parcelId))
                    .ToList();
                double poleArea = polesInParcel.Sum(p => p.OverlappingParcels[parcelId]);
                int poleCount   = polesInParcel.Count;

                rows.Add(new ReportRow
                {
                    ParcelId         = parcelId,
                    Owner            = owner,
                    ServitudeAreaSqM = servArea,
                    PoleAreaSqM      = poleArea,
                    PoleCount        = poleCount,
                    AssignedPoles    = polesInParcel,

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
