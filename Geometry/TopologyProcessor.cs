using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
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
        private const double SliverTolerance = GeometryTolerances.SliverAreaSqm;

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
        /// <returns>ParcelId → intersected area (sq.m.).</returns>
        public Dictionary<string, double> CalculateServitudeIntersections(
            Polyline servitudePline,
            List<KeyValuePair<string, Polyline>> parcelPolylines)
        {
            var result = new Dictionary<string, double>();

            Region? servitudeRegion = null;
            try
            {
                using (Polyline? cleanServitude = GeometrySanitizer.Sanitize(servitudePline, GeometryTolerances.SanitizeMaxSegmentLengthM, GeometryTolerances.SanitizeMinVertexDistanceM))
                {
                    servitudeRegion = SafeCreateRegion(cleanServitude!);
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
                            using (Polyline? cleanParcel = GeometrySanitizer.Sanitize(parcelPline, GeometryTolerances.SanitizeMaxSegmentLengthM, GeometryTolerances.SanitizeMinVertexDistanceM))
                            {
                                parcelRegion = SafeCreateRegion(cleanParcel!);
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
        /// <returns>A list of Pole domain objects with AssignedParcelId populated.</returns>
        public List<Pole> AssignPolesToParcels(
            List<KeyValuePair<string, Polyline>> polePolylines,
            List<KeyValuePair<string, Polyline>> parcelPolylines)
        {
            var poles = new List<Pole>();

            foreach (var poleKvp in polePolylines)
            {
                string poleId = poleKvp.Key;
                Polyline polePline = poleKvp.Value;

                Region? poleRegion = null;
                try
                {
                    poleRegion = SafeCreateRegion(polePline);
                    if (poleRegion == null)
                    {
                        _logger.LogWarning(
                            $"Failed to create Region for pole {poleId}. Skipped.");
                        continue;
                    }

                    double poleArea = poleRegion.Area;
                    Point3d centroid = GetPolylineCentroid(polePline);
                    var pole = new Pole
                    {
                        PoleId = poleId,
                        PoleAreaSqm = poleArea,
                        LocationX = centroid.X,
                        LocationY = centroid.Y
                    };

                    foreach (var parcelKvp in parcelPolylines)
                    {
                        string parcelId = parcelKvp.Key;
                        Polyline parcelPline = parcelKvp.Value;

                        Region? parcelRegion = null;
                        Region? intersectRegion = null;
                        try
                        {
                            parcelRegion = SafeCreateRegion(parcelPline);
                            if (parcelRegion == null) continue;

                            intersectRegion = (Region)poleRegion.Clone();
                            intersectRegion.BooleanOperation(
                                BooleanOperationType.BoolIntersect, parcelRegion);

                            double area = intersectRegion.Area;
                            if (area > SliverTolerance)
                            {
                                pole.OverlappingParcels[parcelId] = area;
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

                    if (pole.OverlappingParcels.Count == 0)
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
                $"{poles.Count(p => p.OverlappingParcels.Count > 0)} assigned.");

            return poles;
        }

        // -----------------------------------------------------------------
        //  3. MVP Math Test
        // -----------------------------------------------------------------

        public List<ParcelData> RunMvpMathTest(
            Polyline servitudePline,
            List<KeyValuePair<string, Polyline>> polePolylines,
            List<KeyValuePair<string, Polyline>> parcelPolylines)
        {
            var results = new List<ParcelData>();
            var perf = new PairStats();
            var servitudeWatch = new System.Diagnostics.Stopwatch();
            var total = System.Diagnostics.Stopwatch.StartNew();

            foreach (var parcelKvp in parcelPolylines)
            {
                Polyline parcelPline = parcelKvp.Value;
                string parcelId = XDataExtractor.GetParcelId(parcelPline);
                
                var pData = new ParcelData { ParcelId = parcelId };
                pData.TotalAreaSqm = parcelPline.Area;

                // 1. Gross Servitude Area
                servitudeWatch.Start();
                pData.ServitudeGrossAreaSqm = GetPreciseIntersectionArea(parcelPline, servitudePline);
                servitudeWatch.Stop();

                // 2. Pole Area & Intersecting Poles
                double totalPoleArea = 0;
                List<string> assignedPoles = new List<string>();
                List<Polyline> intersectingPolesList = new List<Polyline>();

                foreach (var poleKvp in polePolylines)
                {
                    string poleNumber = poleKvp.Key;
                    Polyline poleFootprintPoly = poleKvp.Value;

                    double intersectArea = perf.Pair(() => GetPreciseIntersectionArea(parcelPline, poleFootprintPoly));
                    
                    if (intersectArea > SliverTolerance)
                    {
                        totalPoleArea += intersectArea;
                        assignedPoles.Add(poleNumber);
                        pData.IndividualPoleAreas[poleNumber] = intersectArea;
                        intersectingPolesList.Add(poleFootprintPoly);
                    }
                }

                pData.PoleAreaSqm = totalPoleArea;
                pData.AssignedPoleNumbers = assignedPoles;

                // 3. Net Servitude Area
                pData.ServitudeNetAreaSqm = GetPreciseSubtractedArea(parcelPline, servitudePline, intersectingPolesList);

                results.Add(pData);
            }
            perf.Log(_logger, "RunMvpMathTest", parcelPolylines.Count, polePolylines.Count, total.ElapsedMilliseconds);
            _logger.LogPerf($"RunMvpMathTest servitude x parcel (gross): {servitudeWatch.ElapsedMilliseconds} ms");

            return results;
        }

        // -----------------------------------------------------------------
        //  4. Pole steps: footprint ∩ parcel pieces
        // -----------------------------------------------------------------

        /// <summary>
        /// For every pole footprint and every parcel, the area of footprint ∩ parcel, using the same
        /// overlap computation and sliver tolerance as <see cref="RunMvpMathTest"/>. Pairs at or below
        /// the sliver tolerance are dropped. Parcel IDs are the ones resolved at pick time; parcel
        /// area is the drawn polyline area.
        /// </summary>
        public PoleStepsGeometry ComputePoleStepPieces(
            List<KeyValuePair<string, Polyline>> polePolylines,
            List<KeyValuePair<string, Polyline>> parcelPolylines)
        {
            var result = new PoleStepsGeometry();

            foreach (var poleKvp in polePolylines)
            {
                result.Footprints.Add(new PoleFootprintArea
                {
                    PoleNumber = poleKvp.Key,
                    AreaSqm = poleKvp.Value.Area
                });
            }

            var perf = new PairStats();
            var total = System.Diagnostics.Stopwatch.StartNew();
            foreach (var parcelKvp in parcelPolylines)
            {
                Polyline parcelPline = parcelKvp.Value;
                double parcelArea = parcelPline.Area;

                foreach (var poleKvp in polePolylines)
                {
                    double intersectArea = perf.Pair(() => GetPreciseIntersectionArea(parcelPline, poleKvp.Value));
                    if (intersectArea > SliverTolerance)
                    {
                        result.Pieces.Add(new PoleStepPiece
                        {
                            ParcelId = parcelKvp.Key,
                            ParcelAreaSqm = parcelArea,
                            PoleNumber = poleKvp.Key,
                            PieceAreaSqm = intersectArea
                        });
                    }
                }
            }
            perf.Log(_logger, "ComputePoleStepPieces", parcelPolylines.Count, polePolylines.Count, total.ElapsedMilliseconds);

            return result;
        }

        // -----------------------------------------------------------------
        //  5. Register of affected parcels
        // -----------------------------------------------------------------

        /// <summary>
        /// Per parcel: drawn area and the gross servitude area (servitude ∩ parcel, poles included), computed by the
        /// same helper as <see cref="RunMvpMathTest"/>; plus the pole footprint areas and the footprint ∩ parcel
        /// pieces (as in <see cref="ComputePoleStepPieces"/>).
        /// Parcel IDs are the ones resolved at pick time.
        /// </summary>
        public RegisterGeometry ComputeRegisterGeometry(
            Polyline servitudePline,
            List<KeyValuePair<string, Polyline>> polePolylines,
            List<KeyValuePair<string, Polyline>> parcelPolylines)
        {
            var result = new RegisterGeometry();

            foreach (var poleKvp in polePolylines)
            {
                result.Footprints.Add(new PoleFootprintArea
                {
                    PoleNumber = poleKvp.Key,
                    AreaSqm = poleKvp.Value.Area
                });
            }

            var perf = new PairStats();
            var servitudeWatch = new System.Diagnostics.Stopwatch();
            var total = System.Diagnostics.Stopwatch.StartNew();
            foreach (var parcelKvp in parcelPolylines)
            {
                Polyline parcelPline = parcelKvp.Value;
                double parcelArea = parcelPline.Area;

                foreach (var poleKvp in polePolylines)
                {
                    double intersectArea = perf.Pair(() => GetPreciseIntersectionArea(parcelPline, poleKvp.Value));
                    if (intersectArea > SliverTolerance)
                    {
                        result.Pieces.Add(new PoleStepPiece
                        {
                            ParcelId = parcelKvp.Key,
                            ParcelAreaSqm = parcelArea,
                            PoleNumber = poleKvp.Key,
                            PieceAreaSqm = intersectArea
                        });
                    }
                }

                servitudeWatch.Start();
                double servitudeArea = GetPreciseIntersectionArea(parcelPline, servitudePline);
                servitudeWatch.Stop();
                result.Parcels.Add(new RegisterParcelAreas
                {
                    ParcelId = parcelKvp.Key,
                    DrawnAreaSqm = parcelArea,
                    ServitudeGrossAreaSqm = servitudeArea
                });
            }
            perf.Log(_logger, "ComputeRegisterGeometry", parcelPolylines.Count, polePolylines.Count, total.ElapsedMilliseconds);
            _logger.LogPerf($"ComputeRegisterGeometry servitude x parcel: {parcelPolylines.Count} calls, {servitudeWatch.ElapsedMilliseconds} ms");

            return result;
        }

        /// <summary>Counts for the [PERF] lines: pairs tried and pairs that gave a piece above the sliver tolerance.</summary>
        private sealed class PairStats
        {
            public int Tried;
            public int WithArea;
            public int Skipped;
            public long BooleanMs;
            private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

            public double Pair(Func<double> compute)
            {
                Tried++;
                _watch.Restart();
                double area = compute();
                _watch.Stop();
                BooleanMs += _watch.ElapsedMilliseconds;
                if (area > SliverTolerance) WithArea++;
                return area;
            }

            public void Log(Logger logger, string name, int parcels, int poles, long totalMs)
            {
                logger.LogPerf($"{name} pole x parcel: {parcels} parcels, {poles} poles, {Tried} pairs tried, " +
                               $"{WithArea} with area > sliver, {Skipped} skipped, pair loop {BooleanMs} ms, total {totalMs} ms");
            }
        }

        // -----------------------------------------------------------------
        //  Precise Math Helpers (Origin Shift)
        // -----------------------------------------------------------------

        /// <summary>The area (m²) where two polylines overlap; 0 when they only touch or are disjoint.</summary>
        public double IntersectionAreaSqm(Polyline a, Polyline b) => GetPreciseIntersectionArea(a, b);

        private double GetPreciseIntersectionArea(Polyline parcelPoly, Polyline subjectPoly)
        {
            if (parcelPoly == null || subjectPoly == null) return 0.0;
            
            using (Polyline p1 = (Polyline)parcelPoly.Clone())
            using (Polyline p2 = (Polyline)subjectPoly.Clone())
            {
                Point3d minPt = p1.GeometricExtents.MinPoint;
                Vector3d shift = minPt.GetVectorTo(Point3d.Origin);
                
                p1.TransformBy(Matrix3d.Displacement(shift));
                p2.TransformBy(Matrix3d.Displacement(shift));

                using (Region? r1 = SafeCreateRegion(p1))
                using (Region? r2 = SafeCreateRegion(p2))
                {
                    if (r1 == null || r2 == null) return 0.0;
                    
                    try
                    {
                        r1.BooleanOperation(BooleanOperationType.BoolIntersect, r2);
                        return r1.Area > SliverTolerance ? r1.Area : 0.0;
                    }
                    catch { return 0.0; }
                }
            }
        }

        private double GetPreciseSubtractedArea(Polyline parcelPoly, Polyline servitudePoly, List<Polyline> polesToSubtract)
        {
            if (parcelPoly == null || servitudePoly == null) return 0.0;
            
            using (Polyline p1 = (Polyline)parcelPoly.Clone())
            using (Polyline p2 = (Polyline)servitudePoly.Clone())
            {
                Point3d minPt = p1.GeometricExtents.MinPoint;
                Vector3d shift = minPt.GetVectorTo(Point3d.Origin);
                
                p1.TransformBy(Matrix3d.Displacement(shift));
                p2.TransformBy(Matrix3d.Displacement(shift));

                using (Region? r1 = SafeCreateRegion(p1))
                using (Region? r2 = SafeCreateRegion(p2))
                {
                    if (r1 == null || r2 == null) return 0.0;
                    
                    try
                    {
                        r1.BooleanOperation(BooleanOperationType.BoolIntersect, r2);
                        
                        if (r1.Area < SliverTolerance) return 0.0;

                        foreach (var polePoly in polesToSubtract)
                        {
                            using (Polyline poleClone = (Polyline)polePoly.Clone())
                            {
                                poleClone.TransformBy(Matrix3d.Displacement(shift));
                                using (Region? rPole = SafeCreateRegion(poleClone))
                                {
                                    if (rPole != null)
                                    {
                                        try
                                        {
                                            r1.BooleanOperation(BooleanOperationType.BoolSubtract, rPole);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        
                        return r1.Area;
                    }
                    catch { return 0.0; }
                }
            }
        }

        // -----------------------------------------------------------------
        //  Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Creates a temporary in-memory Region from a closed Polyline.
        /// Caller is responsible for disposing the returned Region.
        /// Returns null if the polyline cannot be converted.
        /// </summary>
        private static Region? SafeCreateRegion(Polyline polyline)
        {
            if (polyline == null || polyline.Area < GeometryTolerances.SliverAreaSqm) return null;
            
            // Clone the polyline to avoid eNotOpenForWrite when it was opened ForRead
            using (Polyline clone = (Polyline)polyline.Clone())
            {
                try
                {
                    // Ensure polyline is strictly closed
                    if (!clone.Closed) clone.Closed = true; 
                    using (var objs = new DBObjectCollection())
                    {
                        objs.Add(clone);
                        DBObjectCollection regions = Region.CreateFromCurves(objs);
                        if (regions != null && regions.Count > 0)
                        {
                            Region result = (Region)regions[0];
                            for (int i = 1; i < regions.Count; i++)
                            {
                                regions[i].Dispose();
                            }
                            return result;
                        }
                    }
                }
                catch { return null; }
            }
            return null;
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

        // -----------------------------------------------------------------
        //  3. Vertex extraction for coordinate registers
        // -----------------------------------------------------------------

        /// <summary>
        /// Extracts all vertices (X, Y) from a Polyline as a list of
        /// <see cref="VertexCoordinate"/> objects. Used for coordinate
        /// register documents (pole foundations and servitude corridors).
        /// </summary>
        /// <param name="pline">The source Polyline.</param>
        /// <param name="labelPrefix">
        /// Optional prefix for vertex labels, e.g. "5001" or "23-".
        /// If empty, vertices are labelled by index (1, 2, 3...).
        /// </param>
        public static List<VertexCoordinate> ExtractPolylineVertices(
            Polyline pline,
            string labelPrefix = "")
        {
            var vertices = new List<VertexCoordinate>();
            if (pline == null) return vertices;

            int count = pline.NumberOfVertices;
            for (int i = 0; i < count; i++)
            {
                Point3d pt = pline.GetPoint3dAt(i);
                vertices.Add(new VertexCoordinate
                {
                    PointIndex = i + 1,
                    PointLabel = string.IsNullOrEmpty(labelPrefix)
                        ? (i + 1).ToString()
                        : $"{labelPrefix}{i + 1}",
                    X = Math.Round(pt.X, 3),
                    Y = Math.Round(pt.Y, 3)
                });
            }

            return vertices;
        }
    }
}
