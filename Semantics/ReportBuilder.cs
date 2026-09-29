namespace PUP_AUTO.Semantics
{
    /// <summary>
    /// Merges servitude areas, pole assignments and the external parcel database into
    /// report rows. Pure: no AutoCAD types, works on parcel IDs.
    /// </summary>
    public static class ReportBuilder
    {
        /// <summary>Owner shown for a parcel that exists in the drawing but not in the database.</summary>
        public const string NoDataOwner = "NO DATA";

        /// <summary>
        /// Merges servitude areas, pole assignments, and the external
        /// database into a flat list of <see cref="ReportRow"/>.
        /// CRUCIAL CHECK: if a ParcelId from geometry is missing from
        /// the database, calls <paramref name="logWarning"/> and uses "NO DATA" for the Owner.
        /// </summary>
        public static List<ReportRow> BuildReportRows(
            IEnumerable<string> parcelIds,
            Dictionary<string, ParcelData> parcelDb,
            Dictionary<string, double> servitudeAreas,
            List<Pole> assignedPoles,
            Action<string> logWarning)
        {
            var rows = new List<ReportRow>();

            // Collect all unique ParcelIds that appear in the geometry
            var geometryParcelIds = new HashSet<string>(parcelIds);

            // Also include any ParcelId assigned to a pole that may not have
            // its own polyline in the selection (edge case)
            foreach (var pole in assignedPoles)
            {
                foreach (var poleParcelId in pole.OverlappingParcels.Keys)
                {
                    geometryParcelIds.Add(poleParcelId);
                }
            }

            foreach (string parcelId in geometryParcelIds.OrderBy(id => id))
            {
                // ── CRUCIAL CHECK ──
                ParcelData? dbRecord = null;
                string owner;
                if (parcelDb.TryGetValue(parcelId, out dbRecord)
                    && dbRecord != null)
                {
                    owner = dbRecord.Owner;
                }
                else
                {
                    owner = NoDataOwner;
                    logWarning(
                        $"ParcelId '{parcelId}' exists in CAD geometry but is " +
                        "MISSING from the CadLibraryReader database. " +
                        "Using 'NO DATA' for Owner.");
                }

                // Servitude area for this parcel
                servitudeAreas.TryGetValue(parcelId, out double servArea);

                // Poles assigned to this parcel
                var polesInParcel = assignedPoles
                    .Where(p => p.OverlappingParcels.ContainsKey(parcelId))
                    .ToList();
                double poleArea = polesInParcel.Sum(p => p.OverlappingParcels[parcelId]);
                int poleCount   = polesInParcel.Count;

                rows.Add(new ReportRow
                {
                    ParcelId         = parcelId,
                    Owner            = owner,
                    ServitudeAreaSqm = servArea,
                    PoleAreaSqm      = poleArea,
                    PoleCount        = poleCount,
                    AssignedPoles    = polesInParcel,

                    // Cadastral register fields (safe: default to empty if dbRecord is null)
                    SubDivision      = dbRecord?.SubDivision   ?? string.Empty,
                    TerritoryType    = dbRecord?.TerritoryType  ?? string.Empty,
                    Usage            = dbRecord?.Usage          ?? string.Empty,
                    Locality         = dbRecord?.Locality       ?? string.Empty,
                    Category         = dbRecord?.Category       ?? string.Empty,
                    OwnershipType    = dbRecord?.OwnershipType  ?? string.Empty,
                    OwnerId          = dbRecord?.OwnerId        ?? string.Empty,
                    OwnerName        = dbRecord?.OwnerName      ?? string.Empty,
                    DocumentAreaSqm  = dbRecord?.DocumentArea   ?? 0.0
                });
            }

            return rows;
        }
    }
}
