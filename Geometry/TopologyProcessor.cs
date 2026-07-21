using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// Performs boolean geometry operations using AutoCAD Regions for 
    /// servitude intersection and pole-to-parcel assignment.
    /// </summary>
    public class TopologyProcessor
    {
        private readonly Logger _logger;

        /// <summary>
        /// Minimum area threshold (sq.m.) below which an intersection is
        /// treated as a sliver / rounding artefact and ignored.
        /// </summary>
        private const double SliverTolerance = 0.001;

        public TopologyProcessor(Logger logger)
        {
            _logger = logger;
        }

        // -----------------------------------------------------------------
        //  1. Servitude ↔ Parcel intersections
        // -----------------------------------------------------------------

        /// <summary>
        /// Intersects a servitude polyline with a collection of parcel polylines.
        /// Returns a dictionary mapping each ParcelId to its intersected area.
        /// </summary>
        /// <param name="servitudePline">The closed Polyline representing the servitude.</param>
        /// <param name="parcelPolylines">
        /// Pairs of (ParcelId, closed Polyline) for every candidate parcel.
        /// </param>
        /// <param name="transaction">An active AutoCAD transaction.</param>
        /// <returns>ParcelId → intersected area (sq.m.).</returns>
        public Dictionary<string, double> CalculateServitudeIntersections(
            Polyline servitudePline,
            List<KeyValuePair<string, Polyline>> parcelPolylines,
            Transaction transaction)
        {
            var result = new Dictionary<string, double>();

            Region? servitudeRegion = null;
            try
            {
                servitudeRegion = CreateRegionFromPolyline(servitudePline);
                if (servitudeRegion == null)
                {
                    _logger.LogError("Failed to create Region from servitude polyline.");
                    return result;
                }

                foreach (var kvp in parcelPolylines)
                {
                    string parcelId = kvp.Key;
                    Polyline parcelPline = kvp.Value;

                    Region? parcelRegion = null;
                    Region? intersectRegion = null;
                    try
                    {
                        parcelRegion = CreateRegionFromPolyline(parcelPline);
                        if (parcelRegion == null)
                        {
                            _logger.LogWarning(
                                $"Failed to create Region for parcel {parcelId}. Skipped.");
                            continue;
                        }

                        // Clone the servitude region so the original is not mutated
                        intersectRegion = (Region)servitudeRegion.Clone();
                        intersectRegion.BooleanOperation(
                            BooleanOperationType.BoolIntersect, parcelRegion);

                        double area = intersectRegion.Area;
                        if (area > SliverTolerance)
                        {
                            result[parcelId] = area;
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        _logger.LogWarning(
                            $"Boolean intersect failed for parcel {parcelId}: {ex.Message}");
                    }
                    finally
                    {
                        intersectRegion?.Dispose();
                        parcelRegion?.Dispose();
                    }
                }
            }
            finally
            {
                servitudeRegion?.Dispose();
            }

            _logger.LogSuccess(
                $"Servitude intersections computed: {result.Count} parcels with non-zero area.");

            return result;
        }

        // -----------------------------------------------------------------
        //  2. Pole → Parcel assignment (Dominant Area)
        // -----------------------------------------------------------------

        /// <summary>
        /// Assigns each pole to the parcel with the MAXIMUM overlapping area
        /// ("Dominant Area Assignment"). Slivers are ignored.
        /// Poles that do not overlap any parcel trigger a Warning with
        /// the pole's AutoCAD Handle and centroid coordinates.
        /// </summary>
        /// <param name="polePolylines">
        /// Pairs of (PoleId, closed Polyline) for every candidate pole.
        /// </param>
        /// <param name="parcelPolylines">
        /// Pairs of (ParcelId, closed Polyline) for every candidate parcel.
        /// </param>
        /// <param name="transaction">An active AutoCAD transaction.</param>
        /// <returns>A list of Pole domain objects with AssignedParcelId populated.</returns>
        public List<Pole> AssignPolesToParcels(
            List<KeyValuePair<string, Polyline>> polePolylines,
            List<KeyValuePair<string, Polyline>> parcelPolylines,
            Transaction transaction)
        {
            var poles = new List<Pole>();

            foreach (var poleKvp in polePolylines)
            {
                string poleId = poleKvp.Key;
                Polyline polePline = poleKvp.Value;

                Region? poleRegion = null;
                try
                {
                    poleRegion = CreateRegionFromPolyline(polePline);
                    if (poleRegion == null)
                    {
                        _logger.LogWarning(
                            $"Failed to create Region for pole {poleId}. Skipped.");
                        continue;
                    }

                    double poleArea = poleRegion.Area;
                    string? bestParcelId = null;
                    double bestArea = 0.0;

                    foreach (var parcelKvp in parcelPolylines)
                    {
                        string parcelId = parcelKvp.Key;
                        Polyline parcelPline = parcelKvp.Value;

                        Region? parcelRegion = null;
                        Region? intersectRegion = null;
                        try
                        {
                            parcelRegion = CreateRegionFromPolyline(parcelPline);
                            if (parcelRegion == null) continue;

                            intersectRegion = (Region)poleRegion.Clone();
                            intersectRegion.BooleanOperation(
                                BooleanOperationType.BoolIntersect, parcelRegion);

                            double area = intersectRegion.Area;
                            if (area > SliverTolerance && area > bestArea)
                            {
                                bestArea = area;
                                bestParcelId = parcelId;
                            }
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            _logger.LogWarning(
                                $"Boolean intersect failed for pole {poleId} " +
                                $"with parcel {parcelId}: {ex.Message}");
                        }
                        finally
                        {
                            intersectRegion?.Dispose();
                            parcelRegion?.Dispose();
                        }
                    }

                    // Build the Pole domain object
                    Point3d centroid = GetPolylineCentroid(polePline);
                    var pole = new Pole
                    {
                        PoleId = poleId,
                        PoleAreaSqM = poleArea,
                        Location = centroid,
                        ObjectId = polePline.ObjectId
                    };

                    if (bestParcelId != null)
                    {
                        pole.AssignedParcelId = bestParcelId;
                    }
                    else
                    {
                        // CRUCIAL: log floating geometry with handle + coordinates
                        string handle = polePline.Handle.ToString();
                        _logger.LogWarning(
                            $"Pole {poleId} (Handle: {handle}, " +
                            $"X: {centroid.X:F3}, Y: {centroid.Y:F3}) " +
                            $"does NOT intersect any parcel — floating geometry.");
                    }

                    poles.Add(pole);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    _logger.LogError(
                        $"Unexpected error processing pole {poleId}: {ex.Message}");
                }
                finally
                {
                    poleRegion?.Dispose();
                }
            }

            _logger.LogSuccess(
                $"Pole assignment complete: {poles.Count} poles processed, " +
                $"{poles.Count(p => !string.IsNullOrEmpty(p.AssignedParcelId))} assigned.");

            return poles;
        }

        // -----------------------------------------------------------------
        //  Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Creates a temporary in-memory Region from a closed Polyline.
        /// Caller is responsible for disposing the returned Region.
        /// Returns null if the polyline cannot be converted.
        /// </summary>
        private Region? CreateRegionFromPolyline(Polyline pline)
        {
            if (pline == null || !pline.Closed)
            {
                _logger.LogWarning(
                    "Cannot create Region: polyline is null or not closed.");
                return null;
            }

            using (var curves = new DBObjectCollection())
            {
                curves.Add(pline);
                DBObjectCollection regions = Region.CreateFromCurves(curves);

                if (regions == null || regions.Count == 0)
                {
                    return null;
                }

                // Take the first region; dispose any extras
                Region result = (Region)regions[0];
                for (int i = 1; i < regions.Count; i++)
                {
                    regions[i].Dispose();
                }

                return result;
            }
        }

        /// <summary>
        /// Computes a simple centroid (average of vertices) for logging purposes.
        /// </summary>
        private Point3d GetPolylineCentroid(Polyline pline)
        {
            double sumX = 0, sumY = 0, sumZ = 0;
            int count = pline.NumberOfVertices;
            if (count == 0) return Point3d.Origin;

            for (int i = 0; i < count; i++)
            {
                Point3d pt = pline.GetPoint3dAt(i);
                sumX += pt.X;
                sumY += pt.Y;
                sumZ += pt.Z;
            }

            return new Point3d(sumX / count, sumY / count, sumZ / count);
        }
    }
}
