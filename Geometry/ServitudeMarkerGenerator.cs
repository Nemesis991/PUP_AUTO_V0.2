using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.Core;

namespace PUP_AUTO.Geometry
{
    /// <summary>PLACEHOLDER — not production.</summary>
    public class ServitudeMarkerGenerator
    {
        private readonly Logger _logger;

        public ServitudeMarkerGenerator(Logger logger)
        {
            _logger = logger;
        }

        public void GenerateMarkers(
            Polyline servitudePline,
            Transaction tr,
            int startLeft = 5001,
            int startRight = 1)
        {
            try
            {
                // 1. Split the polyline into Left and Right sides.
                // We will find the two vertices that are furthest apart.
                // These represent the two extreme ends of the corridor.
                int maxI = 0;
                int maxJ = 0;
                double maxDist = 0;
                int numVerts = servitudePline.NumberOfVertices;

                for (int i = 0; i < numVerts; i++)
                {
                    Point2d pI = servitudePline.GetPoint2dAt(i);
                    for (int j = i + 1; j < numVerts; j++)
                    {
                        double d = pI.GetDistanceTo(servitudePline.GetPoint2dAt(j));
                        if (d > maxDist)
                        {
                            maxDist = d;
                            maxI = i;
                            maxJ = j;
                        }
                    }
                }

                if (maxDist == 0 || maxI == maxJ)
                {
                    _logger.LogWarning("Failed to find extreme points on the servitude polyline.");
                    return;
                }

                // Side 1: from maxI forward to maxJ
                var side1Pts = new List<Point2d>();
                int curr = maxI;
                while (true)
                {
                    side1Pts.Add(servitudePline.GetPoint2dAt(curr));
                    if (curr == maxJ) break;
                    curr = (curr + 1) % numVerts;
                }

                // Side 2: from maxI backward to maxJ
                var side2Pts = new List<Point2d>();
                curr = maxI;
                while (true)
                {
                    side2Pts.Add(servitudePline.GetPoint2dAt(curr));
                    if (curr == maxJ) break;
                    curr = (curr - 1 + numVerts) % numVerts;
                }

                // Remove duplicates in sides
                side1Pts = RemoveAdjacentDuplicates(side1Pts);
                side2Pts = RemoveAdjacentDuplicates(side2Pts);

                if (side1Pts.Count < 2 || side2Pts.Count < 2)
                {
                    _logger.LogWarning("Failed to extract two distinct sides from the servitude polyline.");
                    return;
                }

                // Create temporary polylines to measure distances
                using (var pl1 = CreatePolyline(side1Pts))
                using (var pl2 = CreatePolyline(side2Pts))
                {
                    // Ensure the two lines run in the same relative "corridor" direction if needed.
                    // For now, we will just number them as they are built. 
                    // Usually we want the numbering to go "forward" along the corridor.
                    if (pl2.StartPoint.DistanceTo(pl1.EndPoint) < pl2.StartPoint.DistanceTo(pl1.StartPoint))
                    {
                        pl2.ReverseCurve();
                    }

                    PlaceMarkersAlongPolyline(pl1, tr, startLeft);
                    PlaceMarkersAlongPolyline(pl2, tr, startRight);
                }

                _logger.LogSuccess($"Generated 20m markers on both sides of the servitude.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating servitude markers: {ex.Message}");
            }
        }

        private List<Point2d> RemoveAdjacentDuplicates(List<Point2d> pts)
        {
            var res = new List<Point2d>();
            foreach (var p in pts)
            {
                if (res.Count == 0 || res.Last().GetDistanceTo(p) > GeometryTolerances.DuplicatePointDistanceM)
                    res.Add(p);
            }
            return res;
        }

        private Polyline CreatePolyline(List<Point2d> pts)
        {
            var pl = new Polyline();
            for (int i = 0; i < pts.Count; i++)
            {
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            }
            return pl;
        }

        private void PlaceMarkersAlongPolyline(Polyline pline, Transaction tr, int startNum)
        {
            double totalLength = pline.Length;
            double step = GeometryTolerances.MarkerStepM;
            double parasiteTolerance = GeometryTolerances.MarkerParasiteToleranceM;

            var db = pline.Database ?? HostApplicationServices.WorkingDatabase;
            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            int currentNum = startNum;
            for (double dist = step; dist <= totalLength; dist += step)
            {
                // Skip if remaining length is small (parasite length)
                if (dist > 0 && (totalLength - dist) < parasiteTolerance)
                    break;

                Point3d pt = pline.GetPointAtDist(dist);
                
                // Get tangent to calculate text rotation (perpendicular to line)
                Vector3d tangent = pline.GetFirstDerivative(pline.GetParameterAtDistance(dist)).GetNormal();
                double angle = Math.Atan2(tangent.Y, tangent.X);
                double textAngle = angle + Math.PI / 2; // Perpendicular

                // 1. Create Point
                var dbPoint = new DBPoint(pt);
                // Set point layer/color if needed
                btr.AppendEntity(dbPoint);
                tr.AddNewlyCreatedDBObject(dbPoint, true);

                // 2. Create Text
                var dbText = new DBText();
                dbText.Position = pt;
                dbText.TextString = currentNum.ToString();
                dbText.Height = GeometryTolerances.MarkerTextHeight; // Adjust text height as needed
                dbText.Rotation = textAngle;
                
                // Offset text slightly so it's not exactly on the point
                Vector3d offsetDir = new Vector3d(Math.Cos(textAngle), Math.Sin(textAngle), 0);
                dbText.Position = pt + offsetDir * GeometryTolerances.MarkerTextOffsetM; 

                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                currentNum++;
            }
        }
    }
}
