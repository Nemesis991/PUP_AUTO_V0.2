using System.Globalization;

namespace PUP_AUTO.Semantics
{
    /// <summary>One picked parcel polyline as plain data: its parcel ID, handle, area and what the caller needs to find it again.</summary>
    public sealed class ParcelShape<T>
    {
        public string ParcelId { get; set; } = string.Empty;

        /// <summary>The handle as text (hex), e.g. "732AB"; the lowest one is kept of two identical copies.</summary>
        public string Handle { get; set; } = string.Empty;

        public double AreaSqm { get; set; }
        public T Item { get; set; } = default!;
    }

    /// <summary>A parcel ID drawn as identical copies: the kept handle and the dropped ones.</summary>
    public sealed class DuplicateParcel
    {
        public string ParcelId { get; set; } = string.Empty;
        public string KeptHandle { get; set; } = string.Empty;
        public List<string> DroppedHandles { get; } = new List<string>();
    }

    public sealed class DuplicateFilterResult<T>
    {
        /// <summary>The shapes to use, in the order they were given, without the dropped copies.</summary>
        public List<ParcelShape<T>> Kept { get; } = new List<ParcelShape<T>>();

        public List<DuplicateParcel> Duplicates { get; } = new List<DuplicateParcel>();

        /// <summary>Parcel IDs whose parts overlap without being identical (kept as they are, summed like before).</summary>
        public List<string> OverlappingParts { get; } = new List<string>();

        public int DroppedCount => Duplicates.Sum(d => d.DroppedHandles.Count);
    }

    /// <summary>
    /// Finds polylines that are drawn twice. Pure logic: the overlap area comes in as a function, so the AutoCAD region
    /// helper stays in the geometry code. Several parts of one parcel (same ID, not overlapping) are NOT duplicates.
    /// </summary>
    public static class DuplicatePolylines
    {
        /// <summary>Two copies of one parcel differ in area by less than this (m²).</summary>
        public const double AreaToleranceSqm = 0.01;

        /// <summary>... and the overlap is at least this share of the smaller one.</summary>
        public const double MinOverlapShare = 0.99;

        /// <summary>An overlap above this (m²) between two non-identical parts is reported.</summary>
        public const double PartialOverlapSqm = 0.01;

        /// <summary>
        /// Per parcel ID, the shapes are taken lowest handle first; a shape that is a duplicate of one already kept is dropped.
        /// <paramref name="overlapSqm"/> gives the intersection area of two shapes.
        /// </summary>
        public static DuplicateFilterResult<T> Filter<T>(IReadOnlyList<ParcelShape<T>> shapes, Func<ParcelShape<T>, ParcelShape<T>, double> overlapSqm)
        {
            var result = new DuplicateFilterResult<T>();
            var dropped = new HashSet<ParcelShape<T>>();

            foreach (IGrouping<string, ParcelShape<T>> group in shapes.GroupBy(s => s.ParcelId, StringComparer.Ordinal))
            {
                if (group.Count() < 2) continue;

                var kept = new List<ParcelShape<T>>();
                bool overlapping = false;
                DuplicateParcel? duplicate = null;
                foreach (ParcelShape<T> shape in group.OrderBy(s => HandleNumber(s.Handle)).ThenBy(s => s.Handle, StringComparer.Ordinal))
                {
                    bool isDuplicate = false;
                    foreach (ParcelShape<T> other in kept)
                    {
                        double overlap = overlapSqm(other, shape);
                        if (IsDuplicate(other.AreaSqm, shape.AreaSqm, overlap))
                        {
                            isDuplicate = true;
                            duplicate ??= new DuplicateParcel { ParcelId = group.Key, KeptHandle = other.Handle };
                            duplicate.DroppedHandles.Add(shape.Handle);
                            break;
                        }
                        if (overlap > PartialOverlapSqm) overlapping = true;
                    }

                    if (isDuplicate) dropped.Add(shape);
                    else kept.Add(shape);
                }

                if (duplicate != null) result.Duplicates.Add(duplicate);
                if (overlapping) result.OverlappingParts.Add(group.Key);
            }

            foreach (ParcelShape<T> shape in shapes)
            {
                if (!dropped.Contains(shape)) result.Kept.Add(shape);
            }
            return result;
        }

        /// <summary>Same area (within <see cref="AreaToleranceSqm"/>) and an overlap of at least <see cref="MinOverlapShare"/> of the smaller area.</summary>
        public static bool IsDuplicate(double areaA, double areaB, double overlapSqm) =>
            Math.Abs(areaA - areaB) < AreaToleranceSqm && overlapSqm >= MinOverlapShare * Math.Min(areaA, areaB);

        /// <summary>
        /// Two pole footprints are the same when their areas and their bounding boxes (minX, minY, maxX, maxY) agree
        /// within <paramref name="toleranceM"/> / <see cref="AreaToleranceSqm"/>.
        /// </summary>
        public static bool SameFootprint(double areaA, double[] boxA, double areaB, double[] boxB, double toleranceM = 0.01)
        {
            if (Math.Abs(areaA - areaB) >= AreaToleranceSqm) return false;
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(boxA[i] - boxB[i]) > toleranceM) return false;
            }
            return true;
        }

        /// <summary>The handle as a number (hex); a handle that is not hex sorts last.</summary>
        private static long HandleNumber(string handle) =>
            long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long n) ? n : long.MaxValue;

        /// <summary>
        /// "N имота са начертани два пъти (еднакви полилинии) — взета е по една: 16122.2.1 (732AB, 732AC), …";
        /// at most <paramref name="limit"/> parcels, "…" when there are more. Parcel IDs and handles only.
        /// </summary>
        public static string FormatWarning(IReadOnlyList<DuplicateParcel> duplicates, int limit = int.MaxValue)
        {
            IEnumerable<string> shown = duplicates.Take(limit)
                .Select(d => $"{d.ParcelId} ({string.Join(", ", new[] { d.KeptHandle }.Concat(d.DroppedHandles))})");
            string list = string.Join(", ", shown) + (duplicates.Count > limit ? ", …" : string.Empty);
            return $"{duplicates.Count} имота са начертани два пъти (еднакви полилинии) — взета е по една: {list}";
        }

        public static string FormatOverlapWarning(string parcelId) => $"имот {parcelId}: частите се застъпват";
    }
}
