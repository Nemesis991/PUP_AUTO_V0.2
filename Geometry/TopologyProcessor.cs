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
            BoundingBox?[] poleBoxes = polePolylines.Select(k => BoxOf(k.Value)).ToArray();
            BoundingBox? servitudeBox = BoxOf(servitudePline);
            var servitudeTimes = new SubjectTimes();
            int servitudeCalls = 0;
            LogServitudeShape(_logger, servitudePline);
            using var servitudeCache = new ServitudeCache(servitudePline, _logger);

            foreach (var parcelKvp in parcelPolylines)
            {
                Polyline parcelPline = parcelKvp.Value;
                BoundingBox? parcelBox = BoxOf(parcelPline);
                bool servitudeNear = BoundingBox.MayOverlap(parcelBox, servitudeBox, BoxMargin);
                using var shifted = new ShiftedParcel(parcelPline);
                string parcelId = XDataExtractor.GetParcelId(parcelPline);
                
                var pData = new ParcelData { ParcelId = parcelId };
                pData.TotalAreaSqm = parcelPline.Area;

                // 1. Gross Servitude Area
                servitudeWatch.Start();
                if (servitudeNear) servitudeCalls++;
                pData.ServitudeGrossAreaSqm = servitudeNear ? shifted.IntersectionArea(servitudeCache, servitudePline, servitudeTimes) : 0.0;
                servitudeWatch.Stop();

                // 2. Pole Area & Intersecting Poles
                double totalPoleArea = 0;
                List<string> assignedPoles = new List<string>();
                List<Polyline> intersectingPolesList = new List<Polyline>();

                for (int poleIndex = 0; poleIndex < polePolylines.Count; poleIndex++)
                {
                    var poleKvp = polePolylines[poleIndex];
                    string poleNumber = poleKvp.Key;
                    Polyline poleFootprintPoly = poleKvp.Value;

                    if (!BoundingBox.MayOverlap(parcelBox, poleBoxes[poleIndex], BoxMargin))
                    {
                        perf.Skipped++;
                        continue;
                    }

                    double intersectArea = perf.Pair(() => shifted.IntersectionArea(poleFootprintPoly));
                    
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
                pData.ServitudeNetAreaSqm = servitudeNear
                    ? GetPreciseSubtractedArea(parcelPline, servitudePline, intersectingPolesList)
                    : 0.0;

                results.Add(pData);
            }
            perf.Log(_logger, "RunMvpMathTest", parcelPolylines.Count, polePolylines.Count, total.ElapsedMilliseconds);
            _logger.LogPerf($"RunMvpMathTest servitude x parcel (gross): {servitudeWatch.ElapsedMilliseconds} ms");
            servitudeTimes.Log(_logger, "RunMvpMathTest servitude x parcel (gross) split", servitudeCalls);

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
            BoundingBox?[] poleBoxes = polePolylines.Select(k => BoxOf(k.Value)).ToArray();
            foreach (var parcelKvp in parcelPolylines)
            {
                Polyline parcelPline = parcelKvp.Value;
                double parcelArea = parcelPline.Area;

                BoundingBox? parcelBox = BoxOf(parcelPline);
                using var shifted = new ShiftedParcel(parcelPline);
                for (int poleIndex = 0; poleIndex < polePolylines.Count; poleIndex++)
                {
                    var poleKvp = polePolylines[poleIndex];
                    if (!BoundingBox.MayOverlap(parcelBox, poleBoxes[poleIndex], BoxMargin))
                    {
                        perf.Skipped++;
                        continue;
                    }
                    double intersectArea = perf.Pair(() => shifted.IntersectionArea(poleKvp.Value));
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
            BoundingBox?[] poleBoxes = polePolylines.Select(k => BoxOf(k.Value)).ToArray();
            BoundingBox? servitudeBox = BoxOf(servitudePline);
            var servitudeTimes = new SubjectTimes();
            int servitudeCalls = 0;
            LogServitudeShape(_logger, servitudePline);
            using var servitudeCache = new ServitudeCache(servitudePline, _logger);
            foreach (var parcelKvp in parcelPolylines)
            {
                Polyline parcelPline = parcelKvp.Value;
                double parcelArea = parcelPline.Area;

                BoundingBox? parcelBox = BoxOf(parcelPline);
                using var shifted = new ShiftedParcel(parcelPline);
                for (int poleIndex = 0; poleIndex < polePolylines.Count; poleIndex++)
                {
                    var poleKvp = polePolylines[poleIndex];
                    if (!BoundingBox.MayOverlap(parcelBox, poleBoxes[poleIndex], BoxMargin))
                    {
                        perf.Skipped++;
                        continue;
                    }
                    double intersectArea = perf.Pair(() => shifted.IntersectionArea(poleKvp.Value));
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
                bool servitudeNearParcel = BoundingBox.MayOverlap(BoxOf(parcelPline), servitudeBox, BoxMargin);
                if (servitudeNearParcel) servitudeCalls++;
                double servitudeArea = servitudeNearParcel
                    ? shifted.IntersectionArea(servitudeCache, servitudePline, servitudeTimes)
                    : 0.0;
                servitudeWatch.Stop();
                result.Parcels.Add(new RegisterParcelAreas
                {
                    ParcelId = parcelKvp.Key,
                    DrawnAreaSqm = parcelArea,
                    ServitudeGrossAreaSqm = servitudeArea
                });
            }
            perf.Log(_logger, "ComputeRegisterGeometry", parcelPolylines.Count, polePolylines.Count, total.ElapsedMilliseconds);
            _logger.LogPerf($"ComputeRegisterGeometry servitude x parcel: {parcelPolylines.Count} parcels, {servitudeWatch.ElapsedMilliseconds} ms");
            servitudeTimes.Log(_logger, "ComputeRegisterGeometry servitude x parcel split", servitudeCalls);

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

        private const double BoxMargin = GeometryTolerances.BoundingBoxMarginM;

        /// <summary>
        /// The 2D extents of a polyline, read once per polyline. Null when AutoCAD cannot give them: the pair is then
        /// not skipped, so the boolean decides exactly as before.
        /// </summary>
        private static BoundingBox? BoxOf(Polyline pline)
        {
            try
            {
                Extents3d e = pline.GeometricExtents;
                return new BoundingBox(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The area (m²) where two polylines overlap; 0 when they only touch or are disjoint.</summary>
        public double IntersectionAreaSqm(Polyline a, Polyline b) => GetPreciseIntersectionArea(a, b);

        private static double GetPreciseIntersectionArea(Polyline parcelPoly, Polyline subjectPoly)
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

        /// <summary>Stopwatch ticks spent in the three parts of one subject ∩ parcel call, summed over a loop ([PERF] only).</summary>
        private sealed class SubjectTimes
        {
            public long ParcelRegion;   // the parcel's own region, when this call was its first use
            public long CloneShift;
            public long CreateRegion;
            public long Boolean;

            private static long Ms(long ticks) => ticks * 1000 / System.Diagnostics.Stopwatch.Frequency;

            public void Log(Logger logger, string name, int calls)
            {
                logger.LogPerf($"{name}: {calls} calls, parcel region {Ms(ParcelRegion)} ms, clone+shift {Ms(CloneShift)} ms, region build {Ms(CreateRegion)} ms, boolean {Ms(Boolean)} ms");
            }
        }

        /// <summary>One [PERF] line about the servitude: vertices, arc segments and extents (counts and metres only).</summary>
        private static void LogServitudeShape(Logger logger, Polyline servitude)
        {
            try
            {
                int vertices = servitude.NumberOfVertices;
                int segments = servitude.Closed ? vertices : vertices - 1;
                int arcs = 0;
                for (int i = 0; i < segments; i++)
                {
                    if (Math.Abs(servitude.GetBulgeAt(i)) > GeometryTolerances.BulgeEpsilon) arcs++;
                }
                Extents3d e = servitude.GeometricExtents;
                logger.LogPerf($"Servitude shape: {vertices} vertices, {arcs} arc segments, " +
                               $"extents {e.MaxPoint.X - e.MinPoint.X:F0} x {e.MaxPoint.Y - e.MinPoint.Y:F0} m");
            }
            catch (Exception ex)
            {
                logger.LogPerf($"Servitude shape: unavailable ({ex.GetType().Name})");
            }
        }

        /// <summary>
        /// The servitude region, built once (shifted so its extents' minimum is the origin: the "G frame") and cloned for every
        /// parcel, instead of cloning the polyline and building the region again for each parcel. Owns an unmanaged ACIS
        /// region: dispose it. <see cref="Failed"/> means it could not be built and callers use the per-parcel path.
        /// </summary>
        private sealed class ServitudeCache : IDisposable
        {
            public Region? Region { get; private set; }
            public bool Failed { get; private set; }

            /// <summary>The move from the drawing's frame to the G frame.</summary>
            public Vector3d ToGFrame { get; private set; }

            public ServitudeCache(Polyline servitude, Logger logger)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    using (Polyline clone = (Polyline)servitude.Clone())
                    {
                        Point3d minPt = clone.GeometricExtents.MinPoint;
                        ToGFrame = minPt.GetVectorTo(Point3d.Origin);
                        clone.TransformBy(Matrix3d.Displacement(ToGFrame));
                        Region = SafeCreateRegion(clone);
                    }
                }
                catch
                {
                    Region?.Dispose();
                    Region = null;
                    Failed = true;
                }
                long ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                logger.LogPerf($"Servitude region built once: {ms} ms{(Failed ? " (failed, per-parcel path used)" : Region == null ? " (no region)" : string.Empty)}");
            }

            public void Dispose()
            {
                Region?.Dispose();
                Region = null;
            }
        }

        /// <summary>
        /// One parcel for many intersections: the origin shift is computed and the parcel region built once, on first use, and
        /// every subject (pole footprint, servitude) is intersected with a clone of that region. Same shift rule, same operand
        /// order (parcel ∩ subject) and same sliver rule as <see cref="GetPreciseIntersectionArea"/>, so the areas are the same.
        /// Owns an unmanaged ACIS region: dispose it.
        /// </summary>
        private sealed class ShiftedParcel : IDisposable
        {
            private readonly Polyline _parcel;
            private bool _built;
            private Vector3d _shift;
            private Region? _region;

            public ShiftedParcel(Polyline parcel)
            {
                _parcel = parcel;
            }

            public double IntersectionArea(Polyline subject, SubjectTimes? times = null)
            {
                if (subject == null) return 0.0;
                if (!_built)
                {
                    long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    Build();
                    if (times != null) times.ParcelRegion += System.Diagnostics.Stopwatch.GetTimestamp() - b0;
                }
                if (_region == null) return 0.0;

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                using (Polyline p2 = (Polyline)subject.Clone())
                {
                    p2.TransformBy(Matrix3d.Displacement(_shift));
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (Region? r2 = SafeCreateRegion(p2))
                    {
                        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (times != null) { times.CloneShift += t1 - t0; times.CreateRegion += t2 - t1; }
                        if (r2 == null) return 0.0;

                        Region work;
                        try
                        {
                            work = (Region)_region.Clone();
                        }
                        catch
                        {
                            return GetPreciseIntersectionArea(_parcel, subject); // as before, region by region
                        }
                        using (work)
                        {
                            try
                            {
                                work.BooleanOperation(BooleanOperationType.BoolIntersect, r2);
                                return work.Area > SliverTolerance ? work.Area : 0.0;
                            }
                            catch { return 0.0; }
                            finally
                            {
                                if (times != null) times.Boolean += System.Diagnostics.Stopwatch.GetTimestamp() - t2;
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Parcel ∩ servitude with the servitude region cloned from the cache and moved from the G frame into this parcel's
            /// frame; same operand order and sliver rule as <see cref="IntersectionArea(Polyline, SubjectTimes)"/>.
            /// </summary>
            public double IntersectionArea(ServitudeCache cache, Polyline servitude, SubjectTimes? times = null)
            {
                if (cache.Failed) return IntersectionArea(servitude, times);

                if (!_built)
                {
                    long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    Build();
                    if (times != null) times.ParcelRegion += System.Diagnostics.Stopwatch.GetTimestamp() - b0;
                }
                if (_region == null || cache.Region == null) return 0.0;

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                Region subject;
                try
                {
                    subject = (Region)cache.Region.Clone();
                    subject.TransformBy(Matrix3d.Displacement(_shift - cache.ToGFrame));
                }
                catch
                {
                    return IntersectionArea(servitude, times); // as before, per parcel
                }

                using (subject)
                {
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (times != null) times.CloneShift += t1 - t0;
                    Region work;
                    try
                    {
                        work = (Region)_region.Clone();
                    }
                    catch
                    {
                        return IntersectionArea(servitude, times);
                    }
                    using (work)
                    {
                        try
                        {
                            work.BooleanOperation(BooleanOperationType.BoolIntersect, subject);
                            return work.Area > SliverTolerance ? work.Area : 0.0;
                        }
                        catch { return 0.0; }
                        finally
                        {
                            if (times != null) times.Boolean += System.Diagnostics.Stopwatch.GetTimestamp() - t1;
                        }
                    }
                }
            }

            private void Build()
            {
                _built = true;
                using (Polyline p1 = (Polyline)_parcel.Clone())
                {
                    Point3d minPt = p1.GeometricExtents.MinPoint;
                    _shift = minPt.GetVectorTo(Point3d.Origin);
                    p1.TransformBy(Matrix3d.Displacement(_shift));
                    _region = SafeCreateRegion(p1);
                }
            }

            public void Dispose()
            {
                _region?.Dispose();
                _region = null;
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
