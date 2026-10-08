using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// Result of extracting a footprint from a BlockReference.
    /// </summary>
    public class PoleFootprintResult
    {
        public Polyline? FootprintPolyline { get; set; }
        public string PoleNumber { get; set; } = string.Empty;
        public Point3d LabelPosition { get; set; }
        public double LabelRotation { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// The footprint corners in P-tag order (P1, P2, ...), as the block gives them. <see cref="FootprintPolyline"/> holds the
        /// same points sorted counter-clockwise around their centre instead. Null when extraction failed.
        /// </summary>
        public List<Point2d>? CornerPoints { get; set; }

        /// <summary>
        /// True when a corner was read from plain text (or an unreadable field's text) with fewer than 3 decimals: the drawing's
        /// UNITS precision rounded it and the true value is lost.
        /// </summary>
        public bool RoundedByUnits { get; set; }

        /// <summary>How many of the corner values were read exactly from a field.</summary>
        public int FieldValues { get; set; }

        /// <summary>True when the LUPREC fallback had to re-evaluate this block's P-tag fields.</summary>

        public bool FallbackUsed { get; set; }


        /// <summary>The field code of the first P-tag field of the block (diagnostics), else null.</summary>
        public string? FieldCodeSample { get; set; }
    }

    /// <summary>
    /// One pole block after footprint extraction: the raw result plus the pole ID and the
    /// footprint vertices used by the window and the coordinate reports.
    /// </summary>
    public class PoleFootprintEntry
    {
        /// <summary>Key the block was supplied with (its handle or attribute-derived ID).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The pole block the entry was extracted from.</summary>
        public ObjectId BlockId { get; set; }

        public PoleFootprintResult Result { get; set; } = new PoleFootprintResult();

        /// <summary>The pole number when the block has one, otherwise <see cref="Key"/>.</summary>
        public string PoleId { get; set; } = string.Empty;

        /// <summary>Footprint vertices labelled "PoleId-1" ...; null when extraction failed.</summary>
        public List<VertexCoordinate>? Vertices { get; set; }
    }

    /// <summary>
    /// Universal Dynamic Block Footprint Extractor for PUP_AUTO.
    /// Extracts the 4-point expropriation footprint from a dynamic block reference
    /// based on strict "P-tag" logic (ignoring "TP-tags").
    /// </summary>
    public static class PoleFootprintExtractor
    {
        /// <summary>
        /// Extracts the footprint of every block, lazily and in order, so callers that draw
        /// while enumerating keep their original interleaving of extraction and drawing.
        /// </summary>
        public static IEnumerable<PoleFootprintEntry> ExtractAll(
            IEnumerable<KeyValuePair<string, BlockReference>> poleBlocks,
            Transaction tr)
        {
            foreach (var kvp in poleBlocks)
            {
                // The plugin's own marker blocks carry a NOMER tag but are never poles: skipped silently on every path
                if (IsPluginMarkerBlock(kvp.Value, tr)) continue;

                var entry = new PoleFootprintEntry
                {
                    Key = kvp.Key,
                    BlockId = kvp.Value.ObjectId,
                    Result = ExtractFootprint(kvp.Value, tr)
                };
                entry.PoleId = string.IsNullOrEmpty(entry.Result.PoleNumber) ? kvp.Key : entry.Result.PoleNumber;

                if (entry.Result.FootprintPolyline != null)
                {
                    entry.Vertices = TopologyProcessor.ExtractPolylineVertices(
                        entry.Result.FootprintPolyline, $"{entry.PoleId}-");
                }

                yield return entry;
            }
        }

        public static PoleFootprintResult ExtractFootprint(BlockReference blockRef, Transaction tr)
        {
            var result = new PoleFootprintResult();
            
            try
            {
                // 1. Get Effective Block Name
                string blockName = GetEffectiveName(blockRef, tr);

                // 2. Read active visibility state
                string visibilityState = GetVisibilityState(blockRef);

                // 3. Read attributes (Pole Number and Coordinates)
                string poleNumber = string.Empty;
                var pTags = new Dictionary<string, string>(); // Tag -> Value
                var stats = new PoleValueStats();

                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null)
                    {
                        string tag = PoleAttributeTags.Normalize(attRef.Tag);

                        if (PoleAttributeTags.IsPoleNumberTag(tag))
                        {
                            poleNumber = attRef.TextString;
                            result.LabelPosition = (attRef.Justify == AttachmentPoint.BaseLeft) ? attRef.Position : attRef.AlignmentPoint;
                            result.LabelRotation = attRef.Rotation;
                        }
                        else if (tag.StartsWith("TP"))
                        {
                            pTags[tag] = attRef.TextString;
                        }
                        else if (tag.StartsWith("P"))
                        {
                            // A field gives the exact value; the text is rounded to the drawing's UNITS precision
                            pTags[tag] = PoleAttributeValues.Read(tag, attRef, tr, stats);
                        }
                    }
                }
                // Fields whose raw value could not be read: re-evaluated once with LUPREC 8 (restored afterwards)
                foreach (var exact in PoleAttributeValues.ReadAtFullPrecision(blockRef.Database, tr, stats)) pTags[exact.Key] = exact.Value;
                result.FallbackUsed = stats.FallbackUsed;
                result.FieldCodeSample = stats.FieldCodeSample;

                result.PoleNumber = poleNumber;

                // 4. Attribute Matching Hierarchy (STRICTLY REQUIRE 'P' TAGS, IGNORE 'TP' TAGS)
                // Filter out any TP tags
                var validPTags = pTags.Where(kvp => !kvp.Key.StartsWith("TP")).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var pointStrings = new List<string>();

                // Priority 1 (Visibility Match)
                if (!string.IsNullOrEmpty(visibilityState))
                {
                    string p1v = $"P1-{visibilityState.ToUpperInvariant()}";
                    string p2v = $"P2-{visibilityState.ToUpperInvariant()}";
                    string p3v = $"P3-{visibilityState.ToUpperInvariant()}";
                    string p4v = $"P4-{visibilityState.ToUpperInvariant()}";

                    if (validPTags.Keys.Any(k => k.Contains(p1v) || k.StartsWith(p1v)))
                    {
                        // Found visibility specific tags
                        foreach (string key in PoleAttributeTags.PointTags)
                        {
                            var match = validPTags.FirstOrDefault(k => k.Key.StartsWith($"{key}-{visibilityState.ToUpperInvariant()}")).Value;
                            if (match != null) pointStrings.Add(match);
                        }
                    }
                }

                // Priority 2 (Direct Match)
                if (pointStrings.Count < 4)
                {
                    pointStrings.Clear();
                    foreach (string key in PoleAttributeTags.PointTags)
                    {
                        if (validPTags.TryGetValue(key, out string? match) && match != null)
                        {
                            pointStrings.Add(match);
                        }
                    }
                }

                if (pointStrings.Count < 4)
                {
                    result.ErrorMessage = $"Could not find 4 valid P-tags for block {blockName} (Pole: {poleNumber}). Found {pointStrings.Count}.";
                    return result;
                }

                // 5. Parse string values "X, Y"
                var parsed = FootprintCorners.Parse(pointStrings);

                if (parsed.Count < 4)
                {
                    result.ErrorMessage = $"Failed to parse 4 valid coordinate points for block {blockName} (Pole: {poleNumber}).";
                    return result;
                }

                // The attribute VALUES as parsed, in P1..P4 order: no transform, no rounding
                result.CornerPoints = parsed.Select(p => new Point2d(p.X, p.Y)).ToList();

                // Values that did not come exactly from a field are the text as the drawing's UNITS precision wrote it
                result.RoundedByUnits = pointStrings.Any(s => !stats.ExactValues.Contains(s) && CoordinateText.HasFewerDecimals(s));
                result.FieldValues = pointStrings.Count(stats.ExactValues.Contains);

                // 6. Sort vertices counter-clockwise around centroid
                var sortedPoints = FootprintCorners.SortCounterClockwise(parsed).Select(p => new Point2d(p.X, p.Y)).ToList();

                // 7. Construct closed Polyline
                var pline = new Polyline();
                for (int i = 0; i < sortedPoints.Count; i++)
                {
                    pline.AddVertexAt(i, sortedPoints[i], 0, 0, 0);
                }
                pline.Closed = true;

                result.FootprintPolyline = pline;

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error extracting footprint: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Just the pole number (from the number attribute; empty when the block has none) and the insert's position, without
        /// extracting the footprint. Used where the poles only order something, e.g. orienting the route axis.
        /// </summary>
        public static (string Number, double X, double Y) ReadNumberAndPosition(BlockReference blockRef, Transaction tr)
        {
            string number = string.Empty;
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef &&
                    PoleAttributeTags.IsPoleNumberTag(PoleAttributeTags.Normalize(attRef.Tag)))
                {
                    number = attRef.TextString;
                    break;
                }
            }
            return (number, blockRef.Position.X, blockRef.Position.Y);
        }

        /// <summary>True for a GBP032 corner block (the plugin's own marker, never a pole), whatever its attributes say.</summary>
        public static bool IsCornerBlock(BlockReference blockRef, Transaction tr) =>
            PoleCornerBlockNames.IsCornerBlock(GetEffectiveName(blockRef, tr));

        /// <summary>
        /// True for any block the plugin itself draws (the GBP032 footprint corners, the SERV_TOCHKA servitude points).
        /// They carry a NOMER tag, so without this they would look like poles to every pick and command.
        /// </summary>
        public static bool IsPluginMarkerBlock(BlockReference blockRef, Transaction tr)
        {
            string name = GetEffectiveName(blockRef, tr);
            return PoleCornerBlockNames.IsCornerBlock(name) || ServitudePointBlockNames.IsServitudePointBlock(name);
        }

        private static string GetEffectiveName(BlockReference blockRef, Transaction tr)
        {
            if (blockRef.IsDynamicBlock)
            {
                var btr = tr.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr != null)
                {
                    return btr.Name;
                }
            }
            return blockRef.Name;
        }

        private static string GetVisibilityState(BlockReference blockRef)
        {
            if (blockRef.IsDynamicBlock && blockRef.DynamicBlockReferencePropertyCollection != null)
            {
                foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
                {
                    if (PoleAttributeTags.IsVisibilityProperty(prop.PropertyName))
                    {
                        return prop.Value.ToString();
                    }
                }
            }
            return string.Empty;
        }
    }
}
