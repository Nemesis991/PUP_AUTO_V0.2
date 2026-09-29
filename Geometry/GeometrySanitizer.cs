using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.Core;

namespace PUP_AUTO.Geometry
{
    public static class GeometrySanitizer
    {
        public static Polyline? Sanitize(Polyline source, double maxSegmentLength = GeometryTolerances.SanitizeMaxSegmentLengthM, double minVertexDistance = GeometryTolerances.SanitizeMinVertexDistanceM)
        {
            if (source == null) return null;

            // PHASE 1: DENSIFICATION (plseg logic)
            Polyline densifiedPoly = new Polyline();
            int densifiedIndex = 0;

            for (int i = 0; i < source.NumberOfVertices; i++)
            {
                if (i == source.NumberOfVertices - 1 && !source.Closed)
                {
                    // Add the last vertex
                    densifiedPoly.AddVertexAt(densifiedIndex++, source.GetPoint2dAt(i), 0, 0, 0);
                    break;
                }

                double startDist = source.GetDistanceAtParameter(i);
                double endDist = (i == source.NumberOfVertices - 1) ? source.Length : source.GetDistanceAtParameter(i + 1);
                double length = endDist - startDist;

                if (length > maxSegmentLength)
                {
                    double originalBulge = source.GetBulgeAt(i);
                    double totalTheta = 4 * Math.Atan(originalBulge);
                    double parasiteThreshold = GeometryTolerances.SanitizeParasiteSegmentM;

                    List<double> dists = new List<double>();
                    dists.Add(startDist);

                    int numIntervals = (int)Math.Floor(length / maxSegmentLength);
                    bool isExact = Math.Abs(length - numIntervals * maxSegmentLength) < GeometryTolerances.ExactIntervalEpsilonM;
                    int ptsToAdd = isExact ? numIntervals : numIntervals + 1;

                    for (int j = 1; j < ptsToAdd; j++)
                    {
                        double d = startDist + j * maxSegmentLength;
                        if ((endDist - d) >= parasiteThreshold)
                        {
                            dists.Add(d);
                        }
                    }
                    dists.Add(endDist);

                    for (int k = 0; k < dists.Count - 1; k++)
                    {
                        double subLength = dists[k + 1] - dists[k];
                        double newBulge = 0;
                        if (Math.Abs(originalBulge) > GeometryTolerances.BulgeEpsilon)
                        {
                            double subTheta = totalTheta * (subLength / length);
                            newBulge = Math.Tan(subTheta / 4);
                        }

                        Point3d pt3d = source.GetPointAtDist(dists[k]);
                        densifiedPoly.AddVertexAt(densifiedIndex++, new Point2d(pt3d.X, pt3d.Y), newBulge, 0, 0);
                    }
                }
                else
                {
                    Point2d pt1 = source.GetPoint2dAt(i);
                    densifiedPoly.AddVertexAt(densifiedIndex++, pt1, source.GetBulgeAt(i), 0, 0);
                }
            }

            densifiedPoly.Closed = source.Closed;

            // PHASE 2: CLEANING (cleansrv logic)
            Polyline cleanPoly = new Polyline();
            int cleanIndex = 0;

            for (int i = 0; i < densifiedPoly.NumberOfVertices; i++)
            {
                if (i == densifiedPoly.NumberOfVertices - 1)
                {
                    if (densifiedPoly.Closed)
                    {
                        // Check distance to the first vertex
                        Point2d pt = densifiedPoly.GetPoint2dAt(i);
                        Point2d firstPt = cleanPoly.NumberOfVertices > 0 ? cleanPoly.GetPoint2dAt(0) : densifiedPoly.GetPoint2dAt(0);
                        double dist = pt.GetDistanceTo(firstPt);
                        double bulge = densifiedPoly.GetBulgeAt(i);

                        if (dist < minVertexDistance && Math.Abs(bulge) < GeometryTolerances.BulgeEpsilon)
                        {
                            // Skip adding the last vertex to close the loop
                        }
                        else
                        {
                            cleanPoly.AddVertexAt(cleanIndex++, pt, bulge, 0, 0);
                        }
                    }
                    else
                    {
                        // Open polyline, always add the last point
                        cleanPoly.AddVertexAt(cleanIndex++, densifiedPoly.GetPoint2dAt(i), densifiedPoly.GetBulgeAt(i), 0, 0);
                    }
                    break;
                }

                Point2d currentPt = densifiedPoly.GetPoint2dAt(i);
                Point2d nextPt = densifiedPoly.GetPoint2dAt(i + 1);
                double currentBulge = densifiedPoly.GetBulgeAt(i);
                double distanceToNext = currentPt.GetDistanceTo(nextPt);

                if (distanceToNext < minVertexDistance && Math.Abs(currentBulge) < GeometryTolerances.BulgeEpsilon)
                {
                    // Micro-segment: skip vertex i so the previous vertex connects straight to nextPt.
                    // Its bulge is 0, so there is no arc to carry over.
                }
                else
                {
                    cleanPoly.AddVertexAt(cleanIndex++, currentPt, currentBulge, 0, 0);
                }
            }

            cleanPoly.Closed = densifiedPoly.Closed;
            
            // Copy properties from source
            cleanPoly.Elevation = source.Elevation;
            cleanPoly.Normal = source.Normal;
            cleanPoly.Layer = source.Layer;
            cleanPoly.Color = source.Color;
            cleanPoly.Linetype = source.Linetype;
            
            densifiedPoly.Dispose();

            return cleanPoly;
        }
    }
}
