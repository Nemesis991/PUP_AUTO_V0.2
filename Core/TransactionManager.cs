using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.Core
{
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
        /// Map3D OD / XData / Handle.
        /// </summary>
        public List<KeyValuePair<string, Polyline>> SelectMultiplePolylines(
            Transaction transaction,
            string promptMessage,
            List<GeoParcel>? geoParcels = null)
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
            int matchedByOD = 0;
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

                bool isClosed = pline.Closed;
                if (!isClosed && pline.NumberOfVertices > 2)
                {
                    var p1 = pline.GetPoint2dAt(0);
                    var p2 = pline.GetPoint2dAt(pline.NumberOfVertices - 1);
                    if (p1.GetDistanceTo(p2) < 0.01)
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
                        _logger.LogWarning(
                            $"Could not spatially match polyline (Handle: {pline.Handle}) to any GeoJSON parcel. Using Handle as ID.");
                    }
                }
                else
                {
                    // === STRATEGY 2: Map 3D Object Data (legacy, often fails) ===
                    string? odCadNum = GetObjectDataFieldValue(pline.ObjectId, "cadnum");
                    if (!string.IsNullOrEmpty(odCadNum))
                    {
                        entityId = odCadNum!;
                        matchedByOD++;
                    }
                    else
                    {
                        // === STRATEGY 3: XData fallback ===
                        ResultBuffer? xdata = pline.XData;
                        if (xdata != null)
                        {
                            foreach (TypedValue tv in xdata)
                            {
                                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                {
                                    entityId = tv.Value?.ToString() ?? entityId;
                                    break;
                                }
                            }
                        }
                        fallbackToHandle++;
                    }
                }

                polylines.Add(new KeyValuePair<string, Polyline>(entityId, pline));
            }

            _logger.LogSuccess(
                $"Selected {polylines.Count} polylines from '{promptMessage}'. " +
                $"(Matched: {matchedByGeo} by GeoJSON, {matchedByOD} by Map3D OD, {fallbackToHandle} by Handle fallback)");

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
                if (dist < bestDist && dist < 50.0)
                {
                    // If area data is available, check area ratio as secondary validation
                    if (gp.AreaSqM > 0 && plineArea > 0)
                    {
                        double areaRatio = Math.Min(plineArea, gp.AreaSqM) / Math.Max(plineArea, gp.AreaSqM);
                        // Area must be within 50% to be considered a match
                        // (allow generous tolerance for projection differences)
                        if (areaRatio < 0.5) continue;
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
                        string tag = attRef.Tag.ToUpper();
                        if (tag == "НОМЕР_НА_СТЪЛБА" || tag == "СТЪЛБ_№" || tag == "NOMER")
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

        // -----------------------------------------------------------------
        //  Object Data (Map 3D) Helper
        // -----------------------------------------------------------------

        /// <summary>
        /// Uses reflection to dynamically read Map 3D Object Data (OD) from 
        /// Civil 3D/Map 3D's ManagedMapApi.dll without requiring a hard reference.
        /// Extracts fields like "cadnum" from imported SHP files.
        /// </summary>
        private string? GetObjectDataFieldValue(ObjectId objId, string fieldName)
        {
            try
            {
                var mapApi = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "ManagedMapApi");
                
                if (mapApi == null)
                {
                    try { mapApi = System.Reflection.Assembly.Load("ManagedMapApi"); } catch { }
                }
                
                if (mapApi == null)
                {
                    _logger.LogWarning("ManagedMapApi not found in AppDomain. Cannot read Object Data.");
                    return null;
                }

                Type? hostType = mapApi.GetType("Autodesk.Gis.Map.HostMapApplication");
                if (hostType == null)
                {
                    _logger.LogWarning("HostMapApplication type not found.");
                    return null;
                }

                dynamic? services = hostType.GetProperty("Services")?.GetValue(null);
                if (services == null)
                {
                    _logger.LogWarning("Map Services property is null.");
                    return null;
                }

                dynamic project = services.Project;
                dynamic odTables = project.ODTables;
                dynamic tableNames = odTables.GetTableNames();
                
                if (tableNames.Count == 0)
                {
                    _logger.LogWarning($"No Object Data Tables defined in this drawing! Cannot read Map3D OD for Handle {objId.Handle}.");
                    return null;
                }
                
                Type? openModeType = mapApi.GetType("Autodesk.Gis.Map.Constants+OpenMode");
                if (openModeType == null)
                {
                    openModeType = mapApi.GetType("Autodesk.Gis.Map.Constants.OpenMode");
                }
                if (openModeType == null)
                {
                    _logger.LogWarning("Map OpenMode type not found.");
                    return null;
                }

                object openModeForRead = Enum.Parse(openModeType, "ForRead");

                foreach (string tableName in tableNames)
                {
                    dynamic table = odTables[tableName];
                    dynamic records = table.GetObjectTableRecords((uint)0, objId, openModeForRead, false);
                    try
                    {
#pragma warning disable CS8602 
                        if (records != null && records.Count > 0)
                        {
                            dynamic record = records[0];
                            
                            // Find the index of the field (case-insensitive)
                            dynamic fieldDefs = table.FieldDefinitions;
                            int fieldCount = fieldDefs.Count;
                            int targetIndex = -1;
                            
                            List<string> foundFields = new List<string>();
                            for (int i = 0; i < fieldCount; i++)
                            {
                                string fName = fieldDefs[i].Name;
                                foundFields.Add(fName);
                                if (fName.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                                {
                                    targetIndex = i;
                                }
                            }

                            if (targetIndex >= 0)
                            {
                                dynamic fieldVal = record[targetIndex];
                                if (fieldVal != null)
                                {
                                    return fieldVal.StrValue;
                                }
                                else
                                {
                                    _logger.LogWarning($"Field '{fieldName}' found at index {targetIndex}, but its value is NULL for Handle {objId.Handle}.");
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Table '{tableName}' has {records.Count} records for Handle {objId.Handle}, but field '{fieldName}' was NOT FOUND. Available fields: {string.Join(", ", foundFields)}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Table '{tableName}' has 0 records attached to Handle {objId.Handle}.");
                        }
#pragma warning restore CS8602 
                    }
                    catch (System.Exception ex)
                    {
                        _logger.LogWarning($"Inner Map3D OD exception for {tableName}: {ex.Message}");
                    }
                    finally
                    {
                        if (records is IDisposable disp) disp.Dispose();
                    }
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning($"Failed to read Map3D Object Data via Reflection: {ex.Message}");
            }
            return null;
        }
    }
}
