namespace PUP_AUTO.Core
{
    /// <summary>
    /// Named tolerances and geometry constants. Distances in drawing units (meters),
    /// areas in square meters.
    /// </summary>
    public static class GeometryTolerances
    {
        // --- Areas (m²) ---

        /// <summary>Boolean-operation results and polylines at or below this area are treated as slivers / empty.</summary>
        public const double SliverAreaSqm = 0.001;

        /// <summary>MVP report: |gross - (net + pole)| up to this value is "ОК", above is "ГРЕШКА".</summary>
        public const double BalanceToleranceSqm = 0.001;

        /// <summary>MVP report: a parcel has a pole only if its pole area is greater than this.</summary>
        public const double PoleAreaPresenceSqm = 0.001;

        // --- Distances (m) ---

        /// <summary>A polyline whose first and last vertex are closer than this counts as closed.</summary>
        public const double ClosureDistanceM = 0.01;

        /// <summary>Two bounding boxes further apart than this are skipped before a region boolean (touching edges are still tested).</summary>
        public const double BoundingBoxMarginM = 0.01;

        /// <summary>A route axis is intersected with the parcels in chunks of this many segments, each with its own box.</summary>
        public const int RouteChunkVertices = 50;

        /// <summary>Consecutive marker points closer than this are merged.</summary>
        public const double DuplicatePointDistanceM = 0.01;

        /// <summary>GeoJSON parcel matching: maximum centroid distance.</summary>
        public const double GeoMatchRadiusM = 50.0;

        /// <summary>GeoJSON parcel matching: minimum ratio of the smaller to the larger area.</summary>
        public const double GeoMatchMinAreaRatio = 0.5;

        // --- GeometrySanitizer ---

        public const double SanitizeMaxSegmentLengthM = 50.0;
        public const double SanitizeMinVertexDistanceM = 0.05;

        /// <summary>A trailing segment shorter than this is merged into the previous one.</summary>
        public const double SanitizeParasiteSegmentM = 10.0;

        /// <summary>Bulges with a smaller absolute value are treated as straight segments.</summary>
        public const double BulgeEpsilon = 1e-10;

        /// <summary>Tolerance for "the length is an exact multiple of the segment length".</summary>
        public const double ExactIntervalEpsilonM = 1e-6;

        // --- PUP_SERV segmentation ---

        /// <summary>PUP_SERV: an arc whose sagitta (|bulge| * chord / 2) is under this is treated as straight and gets no vertices.</summary>
        public const double ServStraightSagittaM = 0.001;

        // --- Servitude markers ---

        public const double MarkerStepM = 20.0;
        public const double MarkerParasiteToleranceM = 15.0;
        public const double MarkerTextHeight = 2.0;
        public const double MarkerTextOffsetM = 1.5;

        // --- Drawn entities ---

        public const double PoleLabelTextHeight = 1.0;
        public const double SegmentedServitudeWidth = 0.5;
    }

    /// <summary>
    /// Default segment lengths of the two servitude segmentation entry points.
    /// They are deliberately separate constants: they happen to differ.
    /// </summary>
    public static class SegmentDefaults
    {
        /// <summary>PUP_SERV command prompt default.</summary>
        public const double ServCommandDistanceM = 20.0;

        /// <summary>Window fallback when the distance text box cannot be parsed.</summary>
        public const double WindowFallbackDistanceM = 50.0;
    }
}
