using System.Reflection;
using System.Text;

namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// Default reference data compiled into the plugin (EKATTE register and the nomenclatures), so nothing has to be
    /// copied next to the drawing. The sources are the CSVs in _Templates of the repository; a file with the same
    /// name in the drawing's _Templates overrides matching entries (see <see cref="Nomenclature"/>, <see cref="EkatteRegister"/>).
    /// </summary>
    public static class EmbeddedDefaults
    {
        private const string ResourcePrefix = "PUP_AUTO.Defaults.";

        /// <summary>The text of an embedded default file ("EKATTE.csv", "VIDT.csv", ...), or null when it is not embedded.</summary>
        public static string? ReadText(string fileName)
        {
            Assembly assembly = typeof(EmbeddedDefaults).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>Lines of a text, without the line terminators.</summary>
        public static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n").Split('\n');
    }
}
