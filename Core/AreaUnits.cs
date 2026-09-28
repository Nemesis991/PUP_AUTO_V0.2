using System;
using System.Globalization;

namespace PUP_AUTO.Core
{
    /// <summary>
    /// Single conversion/rounding boundary for area values. All internal math stays in
    /// square meters (raw doubles); conversion to decares and rounding to 3 decimals
    /// happens here, only when a value is about to be written to a report.
    /// </summary>
    public static class AreaUnits
    {
        public const int DkaDecimals = 3;

        /// <summary>Culture used for all numeric text output (Word). Single switch point.</summary>
        public static CultureInfo OutputCulture { get; set; } = CultureInfo.InvariantCulture;

        /// <summary>Converts square meters to decares, rounded to 3 decimals (AwayFromZero). Call ONLY at the output boundary.</summary>
        public static double SqmToDka(double sqm) =>
            Math.Round(sqm / 1000.0, DkaDecimals, MidpointRounding.AwayFromZero);

        /// <summary>Formats square meters as a decare string with exactly 3 decimals.</summary>
        public static string FormatDka(double sqm) =>
            SqmToDka(sqm).ToString("F3", OutputCulture);
    }
}
