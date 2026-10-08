namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// Decides which землище a servitude point is listed under, from the picked parcels' outlines. Pure maths, no AutoCAD
    /// types.
    ///
    /// A point inside a picked parcel takes that parcel's EKATTE. A point in no picked parcel takes the previous point's
    /// EKATTE only when it is within <see cref="GapToleranceM"/> of a picked parcel of that EKATTE (the tiny gaps at parcel
    /// edges); otherwise it has no землище and is left out of the register, while keeping its number. So when only some of the
    /// землища along the servitude are picked, the stretch beyond them is not credited to the last one.
    /// </summary>
    public sealed class ServitudeEkatteResolver
    {
        /// <summary>A point outside the parcels is still the previous землище's when it is this close to one of its parcels (m).</summary>
        public const double GapToleranceM = 2.0;

        private readonly IReadOnlyList<(string Ekatte, PlanarPolygon Outline)> _parcels;

        public ServitudeEkatteResolver(IReadOnlyList<(string Ekatte, PlanarPolygon Outline)> parcels)
        {
            _parcels = parcels;
        }

        /// <summary>The EKATTE of the picked parcel holding the point, or null when it is in none.</summary>
        public string? At(double x, double y)
        {
            foreach ((string ekatte, PlanarPolygon outline) in _parcels)
            {
                if (outline.Contains(x, y)) return ekatte;
            }
            return null;
        }

        /// <summary>Every picked EKATTE whose parcels hold the point (two where neighbouring землища overlap), without repeats.</summary>
        public List<string> EkattesAt(double x, double y)
        {
            var found = new List<string>();
            foreach ((string ekatte, PlanarPolygon outline) in _parcels)
            {
                if (outline.Contains(x, y) && !found.Contains(ekatte, StringComparer.Ordinal)) found.Add(ekatte);
            }
            return found;
        }

        /// <summary>The distance (m) from the point to the nearest picked parcel of the EKATTE; 0 inside one, infinity when it has none.</summary>
        public double DistanceToEkatte(string ekatte, double x, double y)
        {
            double best = double.PositiveInfinity;
            foreach ((string code, PlanarPolygon outline) in _parcels)
            {
                if (!string.Equals(code, ekatte, StringComparison.Ordinal)) continue;
                best = Math.Min(best, outline.Contains(x, y) ? 0 : outline.DistanceToBoundary(x, y));
            }
            return best;
        }

        /// <summary>
        /// The EKATTE the point is listed under: the parcel it is in, else <paramref name="previous"/> when the point is
        /// within the gap tolerance of a picked parcel of that EKATTE, else null (left out of the register).
        /// </summary>
        public string? Resolve(double x, double y, string? previous, out bool inherited)
        {
            inherited = false;
            string? inside = At(x, y);
            if (inside != null) return inside;
            if (previous != null && IsNear(previous, x, y))
            {
                inherited = true;
                return previous;
            }
            return null;
        }

        /// <summary>True when a picked parcel of the EKATTE lies within the gap tolerance of the point.</summary>
        public bool IsNear(string ekatte, double x, double y)
        {
            foreach ((string code, PlanarPolygon outline) in _parcels)
            {
                if (!string.Equals(code, ekatte, StringComparison.Ordinal)) continue;
                // the box is a cheap first cut: a parcel further than the tolerance on either axis cannot be near
                if (x < outline.Box.MinX - GapToleranceM || x > outline.Box.MaxX + GapToleranceM ||
                    y < outline.Box.MinY - GapToleranceM || y > outline.Box.MaxY + GapToleranceM) continue;
                if (outline.DistanceToBoundary(x, y) <= GapToleranceM) return true;
            }
            return false;
        }
    }
}
