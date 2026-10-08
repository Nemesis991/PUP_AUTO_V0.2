using System.Globalization;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One pole footprint as read from the drawing: the corners in drawing X (east) / Y (north), in P-tag order.</summary>
    public sealed class PoleCorners
    {
        /// <summary>Pole number, with or without the "Стълб №" prefix.</summary>
        public string PoleNumber { get; set; } = string.Empty;

        public List<(double X, double Y)> Corners { get; set; } = new List<(double, double)>();

        /// <summary>Rotation of the pole's number label in radians; null when the block has no number label.</summary>
        public double? LabelRotation { get; set; }
    }

    /// <summary>One printed point: label and the Bulgarian geodetic X (north) / Y (east).</summary>
    public sealed class CoordinateRegisterPoint
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>X(север) = the drawing's Y.</summary>
        public double North { get; set; }

        /// <summary>Y(изток) = the drawing's X.</summary>
        public double East { get; set; }
    }

    /// <summary>One pole block of the register.</summary>
    public sealed class CoordinateRegisterBlock
    {
        /// <summary>Pole number without the "Стълб №" prefix.</summary>
        public string PoleNumber { get; set; } = string.Empty;

        /// <summary>The parcel holding the pole's largest piece, without the EKATTE prefix ("84.34").</summary>
        public string ParcelLabel { get; set; } = string.Empty;

        public CoordinateRegisterPoint Centre { get; set; } = new CoordinateRegisterPoint();

        /// <summary>The corners clockwise in map view, labelled "N-1", "N-2", ...</summary>
        public List<CoordinateRegisterPoint> Corners { get; } = new List<CoordinateRegisterPoint>();

        /// <summary>The whole footprint area in m² (not rounded).</summary>
        public double AreaSqm { get; set; }
    }

    /// <summary>One землище section of the coordinate register of the pole steps.</summary>
    public sealed class CoordinateRegister
    {
        /// <summary>"КООРДИНАТЕН РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ЗА &lt;text&gt;".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The EKATTE title.</summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>Pole blocks in ascending pole number.</summary>
        public List<CoordinateRegisterBlock> Blocks { get; } = new List<CoordinateRegisterBlock>();

        /// <summary>Poles whose footprint does not have 4 corners (all its vertices are printed).</summary>
        public List<string> NotFourCorners { get; } = new List<string>();

        /// <summary>Poles whose corners came counter-clockwise and were reversed (vertex 1 kept first).</summary>
        public List<string> Reversed { get; } = new List<string>();
    }

    /// <summary>Which corner is "N-1".</summary>
    public enum CornerStartRule
    {
        /// <summary>The block's own P1..P4 order (clockwise-fixed, vertex 1 kept).</summary>
        PTagOrder,

        /// <summary>
        /// Fallback: corner 1 = the corner forward-left of the pole's label rotation θ, then clockwise. Only for poles that have
        /// a label rotation; the others keep the P-tag order.
        /// </summary>
        ForwardLeftOfLabel
    }

    /// <summary>
    /// Builds the coordinate register of the pole steps (official 07) from plain data, no AutoCAD types. A pole is listed
    /// once, in the землище of the parcel that holds its largest piece (<see cref="TerritoryBalanceBuilder.WinningParcels"/>).
    /// Coordinates are in Bulgarian geodetic order: X = north (drawing Y), Y = east (drawing X).
    /// </summary>
    public static class CoordinateRegisterBuilder
    {
        /// <summary>
        /// The start-corner switch. Stays <see cref="CornerStartRule.PTagOrder"/>; set to <see cref="CornerStartRule.ForwardLeftOfLabel"/>
        /// only if the check against the official N-1..N-4 shows that the P-tag order does not match.
        /// </summary>
        public static CornerStartRule StartRule { get; set; } = CornerStartRule.PTagOrder;

        public const string TitlePrefix = "КООРДИНАТЕН РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ЗА ";
        public const string BlockTitleFormat = "Стълб №{0}, попадащ в имот {1}";
        public const string CentreHeading = "Координати на центъра";
        public const string CornersHeading = "Координати на чупките";
        public const string AreaPrefix = "Площ на стъпката: ";
        public const string AreaSuffix = "кв.м";

        /// <param name="winningParcels">Pole number (no prefix) -> parcel ID of its largest piece, over the WHOLE run.</param>
        /// <param name="sectionParcelIds">The parcel IDs of this землище.</param>
        public static CoordinateRegister Build(
            IEnumerable<PoleCorners> poles,
            IReadOnlyDictionary<string, string> winningParcels,
            IEnumerable<string> sectionParcelIds,
            string projectName,
            string ekatteTitle)
        {
            var result = new CoordinateRegister
            {
                Title = TitlePrefix + projectName.Trim(),
                Subtitle = ekatteTitle
            };

            var inSection = new HashSet<string>(sectionParcelIds, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoleCorners pole in poles)
            {
                string number = PoleLabels.StripPrefix(pole.PoleNumber);
                if (pole.Corners.Count < 3 || !seen.Add(number)) continue;
                if (!winningParcels.TryGetValue(number, out string? parcel) || !inSection.Contains(parcel)) continue;

                List<(double X, double Y)> corners = Clockwise(pole.Corners, out bool reversed);
                if (reversed) result.Reversed.Add(number);
                if (StartRule == CornerStartRule.ForwardLeftOfLabel && pole.LabelRotation.HasValue)
                    corners = StartAtForwardLeft(corners, pole.LabelRotation.Value);
                if (corners.Count != 4) result.NotFourCorners.Add(number);

                (double cx, double cy) = Centre(corners);
                var block = new CoordinateRegisterBlock
                {
                    PoleNumber = number,
                    ParcelLabel = ParcelLabel(parcel),
                    Centre = new CoordinateRegisterPoint { Label = number, North = cy, East = cx },
                    AreaSqm = Math.Abs(SignedArea(corners))
                };
                for (int i = 0; i < corners.Count; i++)
                {
                    block.Corners.Add(new CoordinateRegisterPoint
                    {
                        Label = CornerLabel(number, i),
                        North = corners[i].Y,
                        East = corners[i].X
                    });
                }
                result.Blocks.Add(block);
            }

            result.Blocks.Sort((a, b) => PoleStepsTableBuilder.ComparePoleNumbers(a.PoleNumber, b.PoleNumber));
            result.NotFourCorners.Sort(PoleStepsTableBuilder.ComparePoleNumbers);
            result.Reversed.Sort(PoleStepsTableBuilder.ComparePoleNumbers);
            return result;
        }

        /// <summary>"Стълб №1, попадащ в имот 84.34".</summary>
        public static string BlockTitle(CoordinateRegisterBlock block) =>
            string.Format(CultureInfo.InvariantCulture, BlockTitleFormat, block.PoleNumber, block.ParcelLabel);

        /// <summary>"Площ на стъпката: 55.1кв.м" (1 decimal, half away from zero).</summary>
        public static string AreaText(double areaSqm) =>
            AreaPrefix + Math.Round(areaSqm, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + AreaSuffix;

        /// <summary>"20-1" for the first corner of pole 20.</summary>
        public static string CornerLabel(string poleNumber, int index) => $"{poleNumber}-{index + 1}";

        /// <summary>"78135.84.34" -> "84.34"; an ID without a dot is returned unchanged.</summary>
        public static string ParcelLabel(string parcelId)
        {
            string ekatte = CadRegisterData.EkatteOf(parcelId);
            return ekatte.Length == 0 ? parcelId : parcelId.Substring(ekatte.Length + 1);
        }

        /// <summary>
        /// The corners clockwise in map view (X east, Y north). Counter-clockwise input is reversed keeping vertex 1 first;
        /// the start corner is never re-picked.
        /// </summary>
        public static List<(double X, double Y)> Clockwise(IReadOnlyList<(double X, double Y)> corners, out bool reversed)
        {
            var list = corners.ToList();
            reversed = SignedArea(list) > 0;
            if (reversed) list.Reverse(1, list.Count - 1);
            return list;
        }

        /// <summary>The clockwise corners rotated so the forward-left corner of θ comes first (unchanged when there is none).</summary>
        public static List<(double X, double Y)> StartAtForwardLeft(List<(double X, double Y)> clockwise, double theta)
        {
            int start = Geometry.PoleCornerPlacement.ForwardLeftIndex(clockwise, theta);
            if (start <= 0) return clockwise;
            return clockwise.Skip(start).Concat(clockwise.Take(start)).ToList();
        }

        /// <summary>The average of the corners.</summary>
        public static (double X, double Y) Centre(IReadOnlyList<(double X, double Y)> corners) =>
            (corners.Average(c => c.X), corners.Average(c => c.Y));

        /// <summary>Shoelace area, positive for counter-clockwise. Computed relative to the first corner for precision.</summary>
        public static double SignedArea(IReadOnlyList<(double X, double Y)> corners)
        {
            if (corners.Count < 3) return 0;
            double x0 = corners[0].X, y0 = corners[0].Y, twice = 0;
            for (int i = 0; i < corners.Count; i++)
            {
                (double X, double Y) a = corners[i], b = corners[(i + 1) % corners.Count];
                twice += (a.X - x0) * (b.Y - y0) - (b.X - x0) * (a.Y - y0);
            }
            return twice / 2;
        }
    }
}
