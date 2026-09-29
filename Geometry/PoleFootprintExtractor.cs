using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.Core;

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
    }

    /// <summary>
    /// Universal Dynamic Block Footprint Extractor for PUP_AUTO.
    /// Extracts the 4-point expropriation footprint from a dynamic block reference
    /// based on strict "P-tag" logic (ignoring "TP-tags").
    /// </summary>
    public static class PoleFootprintExtractor
    {
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

                foreach (ObjectId attId in blockRef.AttributeCollection)
                {
                    var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null)
                    {
                        string tag = PoleAttributeTags.Normalize(attRef.Tag);
                        string val = attRef.TextString;

                        if (PoleAttributeTags.IsPoleNumberTag(tag))
                        {
                            poleNumber = val;
                            result.LabelPosition = (attRef.Justify == AttachmentPoint.BaseLeft) ? attRef.Position : attRef.AlignmentPoint;
                            result.LabelRotation = attRef.Rotation;
                        }
                        else if (tag.StartsWith("P") || tag.StartsWith("TP"))
                        {
                            pTags[tag] = val;
                        }
                    }
                }

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
                var points = new List<Point2d>();
                foreach (string ptStr in pointStrings)
                {
                    string[] parts = ptStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 &&
                        double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                    {
                        points.Add(new Point2d(x, y));
                    }
                }

                if (points.Count < 4)
                {
                    result.ErrorMessage = $"Failed to parse 4 valid coordinate points for block {blockName} (Pole: {poleNumber}).";
                    return result;
                }

                // 6. Sort vertices counter-clockwise around centroid
                double cx = points.Average(p => p.X);
                double cy = points.Average(p => p.Y);
                Point2d centroid = new Point2d(cx, cy);

                var sortedPoints = points.OrderBy(p => Math.Atan2(p.Y - centroid.Y, p.X - centroid.X)).ToList();

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
