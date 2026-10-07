using System.Globalization;
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

        /// <summary>The open PUP_WINDOW, if any (single instance).</summary>
        private static Windows.MainWindow? _window;

        // ------------------------------------------------------------------
        //  PUP_WINDOW command — Opens the WPF GUI
        // ------------------------------------------------------------------

        [CommandMethod("PUP_WINDOW")]
        public void PupWindow()
        {
            try
            {
                // Single instance: bring the open window forward instead of creating another
                if (_window != null)
                {
                    if (_window.WindowState == System.Windows.WindowState.Minimized)
                        _window.WindowState = System.Windows.WindowState.Normal;
                    _window.Activate();
                    return;
                }

                var window = new Windows.MainWindow();
                window.Closed += (s, e) => _window = null;
                _window = window;
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(window);
            }
            catch (System.Exception ex)
            {
                _window = null;
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
                    
                    // Read and write OCS coordinates only (GetPoint2dAt / AddVertexAt): mixing in the WCS points of
                    // GetPointAtDist shifts vertices when the polyline's plane is not exactly the WCS plane.
                    var source = new List<PlineVertex>(sourcePline.NumberOfVertices);
                    for (int i = 0; i < sourcePline.NumberOfVertices; i++)
                    {
                        Point2d pt = sourcePline.GetPoint2dAt(i);
                        source.Add(new PlineVertex(pt.X, pt.Y, sourcePline.GetBulgeAt(i)));
                    }

                    // A line that runs back over itself at its start or end would get two different sets of vertices on
                    // the same arcs; keep one pass only.
                    var (cleaned, removedAtStart, removedAtEnd) = ServitudeSegmenter.RemoveRetracedEnds(source, sourcePline.Closed);
                    if (removedAtStart + removedAtEnd > 0)
                    {
                        ed.WriteMessage($"\nПремахнат е дублиран участък: {removedAtStart + removedAtEnd} възела (линията минава два пъти по едно и също място).");
                    }

                    if (!sourcePline.Closed && cleaned.Count > 1)
                    {
                        PlineVertex first = cleaned[0], last = cleaned[cleaned.Count - 1];
                        double gap = Math.Sqrt((last.X - first.X) * (last.X - first.X) + (last.Y - first.Y) * (last.Y - first.Y));
                        if (gap > GeometryTolerances.ClosureDistanceM && gap < OpenServitudeGapNoteM)
                        {
                            ed.WriteMessage($"\n[ВНИМАНИЕ] Сервитутът не е затворен: разстояние {gap.ToString("F2", CultureInfo.InvariantCulture)} м между началото и края.");
                        }
                    }

                    List<PlineVertex> segmented = ServitudeSegmenter.Segment(cleaned, sourcePline.Closed, dist);

                    Polyline newPline = BuildPolyline(sourcePline, segmented);
                    newPline.Layer = sourcePline.Layer;
                    newPline.Color = sourcePline.Color;
                    newPline.Linetype = sourcePline.Linetype;
                    newPline.ConstantWidth = GeometryTolerances.SegmentedServitudeWidth;

                    using (Polyline cleanedPline = BuildPolyline(sourcePline, cleaned))
                    {
                        foreach (string warning in CheckSegmentedServitude(cleanedPline, newPline, segmented, cleaned.Count))
                        {
                            ed.WriteMessage($"\n[ПРЕДУПРЕЖДЕНИЕ] PUP_SERV: {warning}");
                        }
                    }

                    DrawingWriter.EnsureLayer(db, tr, PluginLayers.SegmentedServitude, 3);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    DrawingWriter.Append(btr, tr, newPline, PluginLayers.SegmentedServitude, 3);

                    int added = segmented.Count(v => v.IsInserted);
                    ed.WriteMessage($"\nУспешно сегментиран сервитут! Дължина на сегментите: {dist}м. Слой: 'segmented SERV'. " +
                                    $"Запазени възли: {segmented.Count - added}, добавени в дъги: {added}.");
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ГРЕШКА] PUP_SERV: {ex.Message}\n");
            }
        }

        /// <summary>PUP_SERV notes an open servitude whose start and end are closer than this but not touching.</summary>
        private const double OpenServitudeGapNoteM = 2.0;

        /// <summary>A new, not database-resident polyline in the OCS of <paramref name="template"/> (Normal, Elevation, Closed).</summary>
        private static Polyline BuildPolyline(Polyline template, IReadOnlyList<PlineVertex> vertices)
        {
            var pline = new Polyline();
            pline.Normal = template.Normal;
            pline.Elevation = template.Elevation;
            for (int i = 0; i < vertices.Count; i++)
            {
                pline.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), vertices[i].Bulge, 0, 0);
            }
            pline.Closed = template.Closed;
            return pline;
        }

        /// <summary>
        /// PUP_SERV self-check against the cleaned source (retraced ends removed): same length, every cleaned vertex kept in
        /// order, and no vertex placed before the previous one along it. Returns the problems found (at most <c>MaxWarnings</c>).
        /// </summary>
        private static List<string> CheckSegmentedServitude(Polyline source, Polyline result, IReadOnlyList<PlineVertex> segmented, int sourceCount)
        {
            const double tolerance = 1e-6;
            const int MaxWarnings = 10;
            var warnings = new List<string>();

            if (Math.Abs(result.Length - source.Length) > tolerance)
                warnings.Add($"дължината се различава: {result.Length:F6} м вместо {source.Length:F6} м.");

            int k = 0;              // next source vertex expected in the result
            double previous = 0;    // distance along the source of the previous result vertex
            for (int j = 0; j < segmented.Count && warnings.Count < MaxWarnings; j++)
            {
                double along;
                if (!segmented[j].IsInserted)
                {
                    if (k >= sourceCount || result.GetPoint2dAt(j).GetDistanceTo(source.GetPoint2dAt(k)) > 1e-9)
                    {
                        warnings.Add($"възел {j} не съвпада с изходния възел {k}.");
                        k++;
                        continue;
                    }
                    along = source.GetDistanceAtParameter(k);
                    k++;
                }
                else
                {
                    try
                    {
                        along = source.GetDistAtPoint(result.GetPoint3dAt(j));
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        warnings.Add($"възел {j} не лежи върху изходния сервитут.");
                        continue;
                    }
                }

                if (along < previous - tolerance)
                    warnings.Add($"възел {j} е преди предходния ({along:F3} м < {previous:F3} м).");
                previous = along;
            }

            if (k != sourceCount && warnings.Count < MaxWarnings)
                warnings.Add($"запазени са {k} от {sourceCount} изходни възела.");
            return warnings;
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
            var selection = new SelectionService(logger);
            Editor ed = selection.GetEditor();

            try
            {
                ed.WriteMessage("\n═══ PUP_DRAW_FOOTPRINTS ═══\n");

                using (Transaction tr = selection.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(selection.GetDatabase().BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    int processed = 0;
                    int success = 0;
                    int failed = 0;

                    // Ensure required layers exist
                    var db = selection.GetDatabase();
                    DrawingWriter.EnsureLayer(db, tr, PluginLayers.PoleSteps, 7, LineWeight.LineWeight030);
                    DrawingWriter.EnsureLayer(db, tr, PluginLayers.Diagonals, 8, LineWeight.LineWeight009);
                    DrawingWriter.EnsureLayer(db, tr, PluginLayers.Text, 7, LineWeight.ByLayer);

                    foreach (var entry in PoleFootprintExtractor.ExtractAll(ModelSpaceBlocks(btr, tr, () => processed++), tr))
                    {
                        var result = entry.Result;

                        if (result.FootprintPolyline != null)
                        {
                            success++;
                            var pline = result.FootprintPolyline;
                            DrawingWriter.Append(btr, tr, pline, PluginLayers.PoleSteps, 7, LineWeight.LineWeight030);

                            // Draw DBText and Diagonals
                            
                            // Diagonals
                            if (pline.NumberOfVertices >= 4)
                            {
                                Point3d p0 = pline.GetPoint3dAt(0);
                                Point3d p1 = pline.GetPoint3dAt(1);
                                Point3d p2 = pline.GetPoint3dAt(2);
                                Point3d p3 = pline.GetPoint3dAt(3);

                                Line diag1 = new Line(p0, p2);
                                DrawingWriter.Append(btr, tr, diag1, PluginLayers.Diagonals, 8, LineWeight.LineWeight009);

                                Line diag2 = new Line(p1, p3);
                                DrawingWriter.Append(btr, tr, diag2, PluginLayers.Diagonals, 8, LineWeight.LineWeight009);
                            }

                            // Text
                            DBText text = new DBText();
                            text.SetDatabaseDefaults();
                            text.TextString = result.PoleNumber;
                            text.Height = GeometryTolerances.PoleLabelTextHeight;
                            
                            // CRITICAL ORDER FOR JUSTIFICATION:
                            text.Position = result.LabelPosition;             // 1. Set base position first
                            text.Justify = AttachmentPoint.BottomCenter;      // 2. Set justification (Matching LISP 'BC')
                            text.AlignmentPoint = result.LabelPosition;       // 3. MUST set AlignmentPoint AFTER Justify
                            text.Rotation = result.LabelRotation;             // 4. Set rotation last
                            
                            DrawingWriter.Append(btr, tr, text, PluginLayers.Text, 7);
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
