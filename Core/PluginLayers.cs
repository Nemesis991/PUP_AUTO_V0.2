namespace PUP_AUTO.Core
{
    /// <summary>
    /// Names of the drawing layers the plugin creates. Parcel selection must never
    /// pick up geometry on these layers.
    /// </summary>
    public static class PluginLayers
    {
        public const string PoleSteps = "POLE_STEPS";
        public const string Diagonals = "diagonali";
        public const string Text = "Текст";
        public const string SegmentedServitude = "segmented SERV";

        public static readonly string[] All = { PoleSteps, Diagonals, Text, SegmentedServitude };

        /// <summary>AutoCAD layer names are case-insensitive.</summary>
        public static bool IsPluginLayer(string layerName)
        {
            foreach (string name in All)
            {
                if (string.Equals(name, layerName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
