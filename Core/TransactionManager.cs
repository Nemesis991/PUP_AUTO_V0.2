using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.Core
{
    /// <summary>What <see cref="TransactionManager.SelectMultiplePolylines"/> skipped or could not identify.</summary>
    public class PolylineSelectionStats
    {
        public int SkippedPluginLayer { get; set; }
        public bool ServitudeExcluded { get; set; }

        /// <summary>Handles of kept polylines that fell back to the Handle as their ID (no parcel XData / GeoJSON match).</summary>
        public List<string> HandleFallbacks { get; } = new List<string>();
    }

    /// <summary>
    /// Wraps AutoCAD document / transaction operations, providing helpers
    /// for opening transactions and prompting users for entity selection.
    /// </summary>
    public class TransactionManager
    {
        private readonly Logger _logger;

        public TransactionManager(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>Returns the active AutoCAD Document.</summary>
        public Document GetActiveDocument()
        {
            return Application.DocumentManager.MdiActiveDocument;
        }

        /// <summary>Returns the Editor from the active document.</summary>
        public Editor GetEditor()
        {
            return GetActiveDocument().Editor;
        }

        /// <summary>Returns the Database of the active document.</summary>
        public Database GetDatabase()
        {
            return GetActiveDocument().Database;
        }

        /// <summary>
        /// Starts a new transaction on the active database.
        /// </summary>
        public Transaction StartTransaction()
        {
            return GetActiveDocument().Database.TransactionManager.StartTransaction();
        }

        // -----------------------------------------------------------------
        //  Selection helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Prompts the user to pick one entity. Returns the entity opened
        /// ForRead inside <paramref name="transaction"/>, or null if the
        /// selection is cancelled or the entity is not a closed Polyline.
        /// </summary>
        public Polyline? SelectSinglePolyline(
            Transaction transaction,
            string promptMessage)
        {
            Editor ed = GetEditor();
            var opts = new PromptEntityOptions($"\n{promptMessage}");
            opts.SetRejectMessage("\nEntity is not a Polyline.");
            opts.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult result = ed.GetEntity(opts);
            if (result.Status != PromptStatus.OK)
            {
                _logger.LogWarning("User cancelled single-entity selection.");
                return null;
            }

            var pline = transaction.GetObject(result.ObjectId, OpenMode.ForRead) as Polyline;
            if (pline == null || (!pline.Closed && pline.NumberOfVertices <= 2))
            {
                _logger.LogWarning(
                    $"Selected entity is not a closed Polyline (Handle: {result.ObjectId.Handle}).");
                return null;
            }

            return pline;
        }

        /// <summary>
        /// Prompts the user to select multiple entities filtered to Polylines.
        /// Returns a list of (entityName, Polyline) pairs opened ForRead
        /// inside <paramref name="transaction"/>.
        /// 
        /// When <paramref name="geoParcels"/> is provided, polylines are matched
        /// to GeoJSON parcels by centroid proximity. Otherwise falls back to
        /// XData / Handle.
        /// </summary>
        public List<KeyValuePair<string, Polyline>> SelectMultiplePolylines(
            Transaction transaction,
            string promptMessage,
            List<GeoParcel>? geoParcels = null,
            ObjectId excludeId = default,
            PolylineSelectionStats? stats = null)
        {
            var polylines = new List<KeyValuePair<string, Polyline>>();
            Editor ed = GetEditor();

            var opts = new PromptSelectionOptions();
            opts.MessageForAdding = promptMessage;

            // Filter to lightweight polylines only
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
            });

            PromptSelectionResult selResult = ed.GetSelection(opts, filter);
            if (selResult.Status != PromptStatus.OK)
            {
                _logger.LogWarning("User cancelled multi-polyline selection.");
                return polylines;
            }

            int matchedByGeo = 0;
            int matchedByXData = 0;
            int fallbackToHandle = 0;

            SelectionSet selSet = selResult.Value;
            foreach (SelectedObject selObj in selSet)
            {
                if (selObj == null) continue;

                var pline = transaction.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                if (pline == null)
                {
                    continue;
                }

                // Never treat the plugin's own drawing output or the servitude as a parcel
                if (PluginLayers.IsPluginLayer(pline.Layer))
                {
                    if (stats != null) stats.SkippedPluginLayer++;
                    continue;
                }
                if (!excludeId.IsNull && selObj.ObjectId == excludeId)
                {
                    if (stats != null) stats.ServitudeExcluded = true;
                    continue;
                }

                bool isClosed = pline.Closed;
                if (!isClosed && pline.NumberOfVertices > 2)
                {
                    var p1 = pline.GetPoint2dAt(0);
                    var p2 = pline.GetPoint2dAt(pline.NumberOfVertices - 1);
                    if (p1.GetDistanceTo(p2) < GeometryTolerances.ClosureDistanceM)
                    {
                        isClosed = true;
                    }
                }

                if (!isClosed)
                {
                    _logger.LogWarning(
                        $"Skipped non-closed or invalid polyline (Handle: {selObj.ObjectId.Handle}).");
                    continue;
                }

                // Use the Handle as a default identifier
                string entityId = pline.Handle.ToString();

                // === STRATEGY 1: Spatial matching against GeoJSON parcels ===
                if (geoParcels != null && geoParcels.Count > 0)
                {
                    string? matched = MatchPolylineToGeoParcel(pline, geoParcels);
                    if (matched != null)
                    {
                        entityId = matched;
                        matchedByGeo++;
                    }
                    else
                    {
                        fallbackToHandle++;
                        stats?.HandleFallbacks.Add(entityId);
                        _logger.LogWarning(
                            $"Could not spatially match polyline (Handle: {pline.Handle}) to any GeoJSON parcel. Using Handle as ID.");
                    }
                }
                else
                {
                    // === STRATEGY 2: XData Extractor ===
                    string parcelId = XDataExtractor.GetParcelId(pline);

                    if (parcelId != XDataNames.UnknownParcel && parcelId != XDataNames.XDataError)
                    {
                        entityId = parcelId;
                        matchedByXData++;
                    }
                    else
                    {
                        fallbackToHandle++;
                        stats?.HandleFallbacks.Add(entityId);
                    }
                }

                polylines.Add(new KeyValuePair<string, Polyline>(entityId, pline));
            }

            _logger.LogSuccess(
                $"Selected {polylines.Count} polylines from '{promptMessage}'. " +
                $"(Matched: {matchedByGeo} by GeoJSON, {matchedByXData} by XData, {fallbackToHandle} by Handle fallback)");

            return polylines;
        }

        /// <summary>
        /// Matches an AutoCAD polyline to a GeoJSON parcel by computing the
        /// polyline's centroid and finding the closest GeoJSON parcel centroid.
        /// Returns the matched ParcelId, or null if no match is found within tolerance.
        /// </summary>
        private string? MatchPolylineToGeoParcel(Polyline pline, List<GeoParcel> geoParcels)
        {
            // Compute centroid of the AutoCAD polyline
            double cx = 0, cy = 0;
            int n = pline.NumberOfVertices;
            if (n == 0) return null;

            for (int i = 0; i < n; i++)
            {
                var pt = pline.GetPoint2dAt(i);
                cx += pt.X;
                cy += pt.Y;
            }
            cx /= n;
            cy /= n;

            // Also compute area for secondary validation
            double plineArea = pline.Area;

            // Find best match by centroid distance
            string? bestMatch = null;
            double bestDist = double.MaxValue;

            foreach (var gp in geoParcels)
            {
                double dx = cx - gp.CentroidX;
                double dy = cy - gp.CentroidY;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // Must be within 50m tolerance (cadastral parcels can have large extents)
                if (dist < bestDist && dist < GeometryTolerances.GeoMatchRadiusM)
                {
                    // If area data is available, check area ratio as secondary validation
                    if (gp.AreaSqM > 0 && plineArea > 0)
                    {
                        double areaRatio = Math.Min(plineArea, gp.AreaSqM) / Math.Max(plineArea, gp.AreaSqM);
                        // Area must be within 50% to be considered a match
                        // (allow generous tolerance for projection differences)
                        if (areaRatio < GeometryTolerances.GeoMatchMinAreaRatio) continue;
                    }

                    bestDist = dist;
                    bestMatch = gp.ParcelId;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Prompts the user to select multiple BlockReferences.
        /// Returns a list of (entityName, BlockReference) pairs opened ForRead
        /// inside <paramref name="transaction"/>.
        /// </summary>
        public List<KeyValuePair<string, BlockReference>> SelectMultipleBlockReferences(
            Transaction transaction,
            string promptMessage)
        {
            var blocks = new List<KeyValuePair<string, BlockReference>>();
            Editor ed = GetEditor();

            var opts = new PromptSelectionOptions();
            opts.MessageForAdding = promptMessage;

            // Filter to BlockReferences (INSERT)
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "INSERT")
            });

            PromptSelectionResult selResult = ed.GetSelection(opts, filter);
            if (selResult.Status != PromptStatus.OK)
            {
                _logger.LogWarning("User cancelled block selection.");
                return blocks;
            }

            SelectionSet selSet = selResult.Value;
            foreach (SelectedObject selObj in selSet)
            {
                if (selObj == null) continue;

                var blockRef = transaction.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                if (blockRef == null)
                {
                    continue;
                }

                string entityId = blockRef.Handle.ToString();
                bool foundAttr = false;

                // Try to read ID from Block Attributes first (e.g. НОМЕР_НА_СТЪЛБА)
                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    var attRef = transaction.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null)
                    {
                        if (PoleAttributeTags.IsPoleNumberTag(attRef.Tag))
                        {
                            entityId = attRef.TextString;
                            foundAttr = true;
                            break;
                        }
                    }
                }

                // If no attribute found, try to read ID from XData
                if (!foundAttr)
                {
                    ResultBuffer? xdata = blockRef.XData;
                    if (xdata != null)
                    {
                        foreach (TypedValue tv in xdata)
                        {
                            if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                            {
                                entityId = tv.Value.ToString() ?? entityId;
                                break;
                            }
                        }
                    }
                }

                blocks.Add(new KeyValuePair<string, BlockReference>(entityId, blockRef));
            }

            _logger.LogSuccess(
                $"Selected {blocks.Count} blocks from '{promptMessage}'.");

            return blocks;
        }
    }
}
