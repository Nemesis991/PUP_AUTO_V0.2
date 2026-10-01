using System.Globalization;
using PUP_AUTO.Core;

namespace PUP_AUTO.Semantics
{
    /// <summary>The part of one pole footprint that lies inside one parcel.</summary>
    public class PoleStepPiece
    {
        public string ParcelId { get; set; } = string.Empty;

        /// <summary>Drawn parcel area in m² (same value on every piece of the parcel).</summary>
        public double ParcelAreaSqm { get; set; }

        public string PoleNumber { get; set; } = string.Empty;

        /// <summary>Area of footprint ∩ parcel in m².</summary>
        public double PieceAreaSqm { get; set; }
    }

    /// <summary>The full area of one pole footprint.</summary>
    public class PoleFootprintArea
    {
        public string PoleNumber { get; set; } = string.Empty;
        public double AreaSqm { get; set; }
    }

    /// <summary>Footprint areas and footprint ∩ parcel pieces computed from the drawing.</summary>
    public class PoleStepsGeometry
    {
        public List<PoleFootprintArea> Footprints { get; } = new List<PoleFootprintArea>();
        public List<PoleStepPiece> Pieces { get; } = new List<PoleStepPiece>();
    }

    /// <summary>A pole footprint that is not entirely inside the picked parcels.</summary>
    public class UncoveredStep
    {
        public string PoleNumber { get; set; } = string.Empty;
        public double MissingSqm { get; set; }
    }

    /// <summary>
    /// One table row: a (parcel, pole) pair. Areas are decares already rounded to 3 decimals,
    /// exactly as printed, so every row satisfies ParcelAreaDka - (its parcel's steps) = RemainderDka.
    /// </summary>
    public class PoleStepsRow
    {
        public string ParcelId { get; set; } = string.Empty;
        public double ParcelAreaDka { get; set; }

        /// <summary>Rounded parcel area minus the sum of the rounded step pieces of the whole parcel.</summary>
        public double RemainderDka { get; set; }

        /// <summary>Pole number without the "Стълб №" prefix.</summary>
        public string PoleNumber { get; set; } = string.Empty;
        public double PieceAreaDka { get; set; }

        /// <summary>True on the first row of a parcel.</summary>
        public bool IsFirstOfParcel { get; set; }

        /// <summary>Number of rows of the parcel; set on the first row, 0 on the others.</summary>
        public int ParcelRowCount { get; set; }
    }

    public class PoleStepsTable
    {
        public List<PoleStepsRow> Rows { get; } = new List<PoleStepsRow>();

        /// <summary>Sum of the printed (rounded) step values, in decares.</summary>
        public double TotalPieceAreaDka { get; set; }
    }

    /// <summary>
    /// Builds the pole-steps table from plain data (no AutoCAD types). Areas come in as raw m² and
    /// are rounded to decares once, per printed value, through <see cref="AreaUnits"/>; the remainder
    /// is then derived from those printed values so the table adds up on paper.
    /// </summary>
    public static class PoleStepsTableBuilder
    {
        public static PoleStepsTable Build(IEnumerable<PoleStepPiece> pieces)
        {
            var table = new PoleStepsTable();

            var byParcel = new Dictionary<string, List<PoleStepPiece>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var piece in pieces)
            {
                if (!byParcel.TryGetValue(piece.ParcelId, out var list))
                {
                    list = new List<PoleStepPiece>();
                    byParcel[piece.ParcelId] = list;
                    order.Add(piece.ParcelId);
                }
                list.Add(piece);
            }

            order.Sort(CompareParcelIds);

            foreach (string parcelId in order)
            {
                var parcelPieces = byParcel[parcelId];
                var sorted = StableSortByPole(parcelPieces);

                // Round every printed value first, then subtract the printed values (decimal
                // arithmetic, so 5.380 - 0.014 is exactly 5.366).
                double parcelDka = AreaUnits.SqmToDka(sorted[0].ParcelAreaSqm);
                decimal remainder = (decimal)parcelDka;
                foreach (var piece in sorted)
                {
                    remainder -= (decimal)AreaUnits.SqmToDka(piece.PieceAreaSqm);
                }

                for (int i = 0; i < sorted.Count; i++)
                {
                    table.Rows.Add(new PoleStepsRow
                    {
                        ParcelId = parcelId,
                        ParcelAreaDka = parcelDka,
                        RemainderDka = (double)remainder,
                        PoleNumber = PoleLabels.StripPrefix(sorted[i].PoleNumber),
                        PieceAreaDka = AreaUnits.SqmToDka(sorted[i].PieceAreaSqm),
                        IsFirstOfParcel = i == 0,
                        ParcelRowCount = i == 0 ? sorted.Count : 0
                    });
                }
            }

            decimal total = 0m;
            foreach (var row in table.Rows) total += (decimal)row.PieceAreaDka;
            table.TotalPieceAreaDka = (double)total;
            return table;
        }

        private static List<PoleStepPiece> StableSortByPole(List<PoleStepPiece> pieces)
        {
            return pieces
                .Select((p, index) => (Piece: p, Index: index))
                .OrderBy(x => x, Comparer<(PoleStepPiece Piece, int Index)>.Create((a, b) =>
                {
                    int c = ComparePoleNumbers(a.Piece.PoleNumber, b.Piece.PoleNumber);
                    return c != 0 ? c : a.Index.CompareTo(b.Index);
                }))
                .Select(x => x.Piece)
                .ToList();
        }

        /// <summary>
        /// Footprints whose pieces over all picked parcels do not add up to the full footprint
        /// area (missing area greater than <paramref name="toleranceSqm"/>).
        /// </summary>
        public static List<UncoveredStep> FindUncoveredSteps(
            IEnumerable<PoleFootprintArea> footprints,
            IEnumerable<PoleStepPiece> pieces,
            double toleranceSqm)
        {
            var coveredByPole = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var piece in pieces)
            {
                coveredByPole.TryGetValue(piece.PoleNumber, out double sum);
                coveredByPole[piece.PoleNumber] = sum + piece.PieceAreaSqm;
            }

            var result = new List<UncoveredStep>();
            foreach (var footprint in footprints)
            {
                coveredByPole.TryGetValue(footprint.PoleNumber, out double covered);
                double missing = footprint.AreaSqm - covered;
                if (missing > toleranceSqm)
                {
                    result.Add(new UncoveredStep { PoleNumber = footprint.PoleNumber, MissingSqm = missing });
                }
            }
            return result;
        }

        public static string FormatUncoveredWarning(UncoveredStep step)
        {
            return $"Стъпката на стълб №{PoleLabels.StripPrefix(step.PoleNumber)} не е изцяло в избраните имоти " +
                   $"(липсват {step.MissingSqm.ToString("F3", CultureInfo.InvariantCulture)} м²).";
        }

        // -----------------------------------------------------------------
        //  Ordering
        // -----------------------------------------------------------------

        /// <summary>
        /// Compares parcel IDs segment by segment (split on '.'): numeric segments numerically
        /// (61580.240.24 before 61580.240.226), numeric before text, text ordinally.
        /// </summary>
        public static int CompareParcelIds(string a, string b)
        {
            string[] sa = a.Split('.');
            string[] sb = b.Split('.');
            int n = Math.Min(sa.Length, sb.Length);
            for (int i = 0; i < n; i++)
            {
                int c = CompareSegments(sa[i], sb[i]);
                if (c != 0) return c;
            }
            if (sa.Length != sb.Length) return sa.Length.CompareTo(sb.Length);
            return string.CompareOrdinal(a, b);
        }

        private static int CompareSegments(string a, string b)
        {
            bool na = IsDigits(a), nb = IsDigits(b);
            if (na && nb) return CompareDigits(a, b);
            if (na) return -1;
            if (nb) return 1;
            int c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char ch in s)
            {
                if (ch < '0' || ch > '9') return false;
            }
            return true;
        }

        /// <summary>Numeric comparison of digit strings of any length.</summary>
        private static int CompareDigits(string a, string b)
        {
            string ta = a.TrimStart('0');
            string tb = b.TrimStart('0');
            if (ta.Length != tb.Length) return ta.Length.CompareTo(tb.Length);
            return string.CompareOrdinal(ta, tb);
        }

        /// <summary>Numeric pole numbers ascending, then text pole numbers ordinally.</summary>
        public static int ComparePoleNumbers(string a, string b)
        {
            a = PoleLabels.StripPrefix(a);
            b = PoleLabels.StripPrefix(b);
            bool na = TryParsePoleNumber(a, out double da);
            bool nb = TryParsePoleNumber(b, out double db);
            if (na && nb)
            {
                int c = da.CompareTo(db);
                return c != 0 ? c : string.CompareOrdinal(a, b);
            }
            if (na) return -1;
            if (nb) return 1;
            int t = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return t != 0 ? t : string.CompareOrdinal(a, b);
        }

        /// <summary>True if the pole number is a whole number (digits with an optional sign).</summary>
        public static bool TryParsePoleNumber(string text, out double value)
        {
            bool ok = long.TryParse(
                text.Trim(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out long whole);
            value = whole;
            return ok;
        }
    }
}
