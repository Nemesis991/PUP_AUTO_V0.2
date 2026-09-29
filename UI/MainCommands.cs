using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PUP_AUTO.Core;
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
        private const string LogFileName      = FileNames.LogFile;

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
                pdo.DefaultValue = SegmentDefaults.ServCommandDistanceM;
                pdo.AllowNegative = false;
                pdo.AllowZero = false;
                pdo.UseDefaultValue = true;
                
                PromptDoubleResult pdr = ed.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK) return;
                double dist = pdr.Value;
                
                // 3. Segment and append
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline? sourcePline = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
                    if (sourcePline == null) return;
                    
                    using (Polyline? cleanServitude = GeometrySanitizer.Sanitize(sourcePline, dist, GeometryTolerances.SanitizeMinVertexDistanceM))
                    {
                        if (cleanServitude != null)
                        {
                            // Ensure layer
                            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (!lt.Has(PluginLayers.SegmentedServitude))
                            {
                                lt.UpgradeOpen();
                                LayerTableRecord ltr = new LayerTableRecord();
                                ltr.Name = PluginLayers.SegmentedServitude;
                                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                                lt.Add(ltr);
                                tr.AddNewlyCreatedDBObject(ltr, true);
                            }
                            
                            Polyline newPline = (Polyline)cleanServitude.Clone();
                            newPline.Layer = PluginLayers.SegmentedServitude;
                            newPline.ColorIndex = 3; 
                            newPline.ConstantWidth = GeometryTolerances.SegmentedServitudeWidth;
                            
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

                    EnsureLayer(PluginLayers.PoleSteps, 7, LineWeight.LineWeight030);
                    EnsureLayer(PluginLayers.Diagonals, 8, LineWeight.LineWeight009);
                    EnsureLayer(PluginLayers.Text, 7, LineWeight.ByLayer);

                    foreach (var entry in PoleFootprintExtractor.ExtractAll(ModelSpaceBlocks(btr, tr, () => processed++), tr))
                    {
                        var result = entry.Result;

                        if (result.FootprintPolyline != null)
                        {
                            success++;
                            var pline = result.FootprintPolyline;
                            pline.Layer = PluginLayers.PoleSteps;
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
                                diag1.Layer = PluginLayers.Diagonals;
                                diag1.ColorIndex = 8;
                                diag1.LineWeight = LineWeight.LineWeight009;
                                btr.AppendEntity(diag1);
                                tr.AddNewlyCreatedDBObject(diag1, true);

                                Line diag2 = new Line(p1, p3);
                                diag2.Layer = PluginLayers.Diagonals;
                                diag2.ColorIndex = 8;
                                diag2.LineWeight = LineWeight.LineWeight009;
                                btr.AppendEntity(diag2);
                                tr.AddNewlyCreatedDBObject(diag2, true);
                            }

                            // Text
                            DBText text = new DBText();
                            text.SetDatabaseDefaults();
                            text.TextString = result.PoleNumber;
                            text.Layer = PluginLayers.Text;
                            text.ColorIndex = 7;
                            text.Height = GeometryTolerances.PoleLabelTextHeight;
                            
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
        //  Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Lazily yields every BlockReference of the block table record (key = handle),
        /// calling <paramref name="onBlock"/> for each one before it is extracted.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, BlockReference>> ModelSpaceBlocks(
            BlockTableRecord btr, Transaction tr, Action onBlock)
        {
            foreach (ObjectId objId in btr)
            {
                var blockRef = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                if (blockRef == null) continue;

                onBlock();
                yield return new KeyValuePair<string, BlockReference>(blockRef.Handle.ToString(), blockRef);
            }
        }

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
