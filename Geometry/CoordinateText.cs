using System.Globalization;
using System.Text.RegularExpressions;

namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// Helpers for the coordinate text of the pole attributes ("X, Y", formatted by the drawing's UNITS precision) and for the
    /// field codes behind it. Plain strings and numbers, no AutoCAD types.
    /// </summary>
    public static class CoordinateText
    {
        /// <summary>Coordinates written with fewer decimals than this are rounded by the drawing's UNITS precision.</summary>
        public const int FullPrecisionDecimals = 3;

        private static readonly Regex ObjectProperty = new Regex(
            @"Object\(\s*%<\\_ObjId\s+(?<id>\d+)\s*>%\s*\)\.(?<property>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        /// <summary>The decimals written in one number: "363604.08" -> 2, "12" -> 0, "1.5000" -> 4.</summary>
        public static int DecimalCount(string number)
        {
            string text = number.Trim();
            int dot = text.IndexOf('.');
            if (dot < 0) return 0;
            int count = 0;
            for (int i = dot + 1; i < text.Length && char.IsDigit(text[i]); i++) count++;
            return count;
        }

        /// <summary>The smallest decimal count of the first two parts of "X, Y"; null when it has fewer than two parts.</summary>
        public static int? MinDecimals(string pointText)
        {
            string[] parts = pointText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            return Math.Min(DecimalCount(parts[0]), DecimalCount(parts[1]));
        }

        /// <summary>True when "X, Y" has a coordinate with fewer than <paramref name="minDecimals"/> decimals.</summary>
        public static bool HasFewerDecimals(string pointText, int minDecimals = FullPrecisionDecimals)
        {
            int? min = MinDecimals(pointText);
            return min.HasValue && min.Value < minDecimals;
        }

        /// <summary>
        /// True when the raw values are what the text shows after rounding to its own decimals (half a unit of the last digit):
        /// the guard against a field that points at the wrong object. An unparsable text accepts any raw value.
        /// </summary>
        public static bool IsConsistent(double x, double y, string pointText)
        {
            string[] parts = pointText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return true;
            return PartMatches(x, parts[0]) && PartMatches(y, parts[1]);
        }

        private static bool PartMatches(double raw, string text)
        {
            if (!double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double shown)) return true;
            return Math.Abs(raw - shown) <= 0.5 * Math.Pow(10, -DecimalCount(text)) + 1e-9;
        }

        /// <summary>"X, Y" with the shortest text that reads back as the same doubles.</summary>
        public static string FormatPoint(double x, double y) =>
            x.ToString("R", CultureInfo.InvariantCulture) + ", " + y.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// The object and property a field code reads: <c>%&lt;\AcObjProp Object(%&lt;\_ObjId 2305843009213833000&gt;%).InsertionPoint …&gt;%</c>
        /// -> (2305843009213833000, "InsertionPoint"). Null for any other field code.
        /// </summary>
        public static (long ObjectId, string Property)? ParseObjectProperty(string fieldCode)
        {
            Match match = ObjectProperty.Match(fieldCode);
            if (!match.Success || !long.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long id)) return null;
            return (id, match.Groups["property"].Value);
        }
    }
}
