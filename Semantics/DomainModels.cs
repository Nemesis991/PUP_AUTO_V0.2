namespace PUP_AUTO.Semantics
{
    /// <summary>
    /// Represents cadastral parcel data.
    /// </summary>
    public class ParcelData
    {
        public string ParcelId { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Ekatte { get; set; } = string.Empty;
        public double DocumentArea { get; set; }

        // --- Cadastral Register Fields ---
        public string SubDivision { get; set; } = string.Empty;
        public string TerritoryType { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        public string Locality { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;

        // --- MVP Math Test Fields ---
        public double TotalAreaSqm { get; set; }
        public double ServitudeGrossAreaSqm { get; set; }
        public double ServitudeNetAreaSqm { get; set; }
        public double PoleAreaSqm { get; set; }

        /// <summary>
        /// Unaffected remainder = Total - net servitude - pole footprint, in Square Meters.
        /// Clamped at 0 so it cannot report a negative area. Raw, unrounded — safe to use
        /// in further arithmetic (e.g. balance/total sums).
        /// </summary>
        public double RemainderAreaSqm => Math.Max(0.0, TotalAreaSqm - ServitudeNetAreaSqm - PoleAreaSqm);

        public List<string> AssignedPoleNumbers { get; set; } = new List<string>();
        public Dictionary<string, double> IndividualPoleAreas { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// Represents a physical transmission tower / pole.
    /// </summary>
    public class Pole
    {
        public string PoleId { get; set; } = string.Empty;
        public Dictionary<string, double> OverlappingParcels { get; set; } = new Dictionary<string, double>();

        /// <summary>Sequential number of the pole along the line.</summary>
        public int PoleNumber { get; set; }

        /// <summary>Footprint area of the pole in Square Meters.</summary>
        public double PoleAreaSqm { get; set; }

        /// <summary>Footprint centroid (average of the vertices), X coordinate.</summary>
        public double LocationX { get; set; }

        /// <summary>Footprint centroid (average of the vertices), Y coordinate.</summary>
        public double LocationY { get; set; }
        
        /// <summary>The extracted 4 vertices of the pole footprint.</summary>
        public List<VertexCoordinate> FootprintVertices { get; set; } = new List<VertexCoordinate>();
    }

    /// <summary>
    /// Structures the data row for output reporting.
    /// </summary>
    public class ReportRow
    {
        public string ParcelId { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;

        // --- Cadastral Register Fields ---
        public string SubDivision { get; set; } = string.Empty;
        public string TerritoryType { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        public string Locality { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;

        // --- Area Fields (Square Meters) ---
        /// <summary>Total parcel area from the cadastral register, in Square Meters.</summary>
        public double DocumentAreaSqm { get; set; }

        /// <summary>Servitude (right-of-way) area in Square Meters.</summary>
        public double ServitudeAreaSqm { get; set; }

        /// <summary>Combined pole footprint area in Square Meters.</summary>
        public double PoleAreaSqm { get; set; }

        public int PoleCount { get; set; }

        /// <summary>
        /// List of poles assigned to this parcel. Populated during MergeResults.
        /// Used to build "Стълб №24,Стълб №23" display strings and individual pole areas.
        /// </summary>
        public List<Pole> AssignedPoles { get; set; } = new List<Pole>();

        /// <summary>
        /// Remainder = Total Area - Servitude Area, in Square Meters. Raw, unrounded —
        /// safe to use in further arithmetic (e.g. balance/total sums).
        /// ServitudeAreaSqm is the gross parcel/servitude intersection, which already
        /// contains the pole footprints, so the pole area must NOT be subtracted again here.
        /// </summary>
        public double RemainderAreaSqm => Math.Max(0.0, DocumentAreaSqm - ServitudeAreaSqm);

        /// <summary>
        /// Formatted pole numbers string, e.g. "Стълб №24,Стълб №23".
        /// Returns empty string if no poles are assigned.
        /// </summary>
        public string PoleNumbers => AssignedPoles.Count == 0
            ? string.Empty
            : string.Join(",", AssignedPoles.OrderBy(p => p.PoleNumber).Select(p => $"Стълб №{p.PoleNumber}"));
    }

    /// <summary>
    /// Holds the X, Y coordinates of a polyline vertex.
    /// Used for coordinate register exports.
    /// </summary>
    public class VertexCoordinate
    {
        public int PointIndex { get; set; }
        public string PointLabel { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Represents a parcel geometry from a GeoJSON file, used for spatial matching
    /// of AutoCAD polylines to cadastral parcel IDs.
    /// </summary>
    public class GeoParcel
    {
        public string ParcelId { get; set; } = string.Empty;
        /// <summary>Centroid X coordinate (in projected CRS, e.g. BGS2005).</summary>
        public double CentroidX { get; set; }
        /// <summary>Centroid Y coordinate (in projected CRS, e.g. BGS2005).</summary>
        public double CentroidY { get; set; }
        /// <summary>Area in square meters from the cadastral register.</summary>
        public double AreaSqm { get; set; }
    }
}
