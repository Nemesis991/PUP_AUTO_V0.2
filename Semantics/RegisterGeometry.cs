namespace PUP_AUTO.Semantics
{
    /// <summary>Areas of one picked parcel for the register of affected parcels, in m².</summary>
    public class RegisterParcelAreas
    {
        public string ParcelId { get; set; } = string.Empty;

        /// <summary>Drawn parcel area.</summary>
        public double DrawnAreaSqm { get; set; }

        /// <summary>Servitude ∩ parcel without the pole footprints (the MVP "net servitude" area).</summary>
        public double ServitudeNetAreaSqm { get; set; }
    }

    /// <summary>Everything the register needs from the drawing: parcel areas plus the pole footprints and pieces.</summary>
    public class RegisterGeometry
    {
        public List<RegisterParcelAreas> Parcels { get; } = new List<RegisterParcelAreas>();
        public List<PoleFootprintArea> Footprints { get; } = new List<PoleFootprintArea>();
        public List<PoleStepPiece> Pieces { get; } = new List<PoleStepPiece>();
    }
}
