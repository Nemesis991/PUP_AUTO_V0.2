namespace PUP_AUTO.Semantics
{
    /// <summary>The "Стълб №" label used in front of a pole number in reports and in pole block attributes.</summary>
    public static class PoleLabels
    {
        public const string Prefix = "Стълб №";

        /// <summary>"Стълб №162" -> "162". Text without the prefix is returned trimmed and otherwise unchanged.</summary>
        public static string StripPrefix(string poleNumber)
        {
            string text = poleNumber.Trim();
            if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(Prefix.Length).Trim();
            }
            return text;
        }
    }
}
