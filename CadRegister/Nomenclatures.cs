using System.Globalization;
using System.Text;

namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// One code -> text table, loaded from an editable UTF-8 CSV with the lines "code;text".
    /// An optional header line ("код;текст" / "code;text"), empty lines and lines starting with '#' are skipped.
    /// </summary>
    public sealed class Nomenclature
    {
        private readonly Dictionary<string, string> _entries = new Dictionary<string, string>();
        private readonly HashSet<string> _warnedCodes = new HashSet<string>();
        private readonly Action<string> _warn;

        public string Name { get; }

        public int Count => _entries.Count;

        public Nomenclature(string name, Action<string>? warn = null)
        {
            Name = name;
            _warn = warn ?? (_ => { });
        }

        /// <summary>Loads the file; a missing file is one warning and an empty nomenclature.</summary>
        public static Nomenclature Load(string name, string path, Action<string>? warn = null)
        {
            var nomenclature = new Nomenclature(name, warn);
            if (!File.Exists(path))
            {
                nomenclature._warn($"Номенклатура {name}: липсва файлът {path}.");
                return nomenclature;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            bool firstLine = true;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                int separator = line.IndexOf(';');
                if (separator < 0)
                {
                    nomenclature._warn($"Номенклатура {name}, ред {i + 1}: няма разделител ';', редът е пропуснат.");
                    continue;
                }

                string code = Unquote(line.Substring(0, separator));
                string text = Unquote(line.Substring(separator + 1));
                bool wasFirst = firstLine;
                firstLine = false;

                if (wasFirst && IsHeader(code)) continue;
                if (code.Length == 0)
                {
                    nomenclature._warn($"Номенклатура {name}, ред {i + 1}: празен код, редът е пропуснат.");
                    continue;
                }

                string key = NormalizeCode(code);
                if (nomenclature._entries.ContainsKey(key))
                {
                    nomenclature._warn($"Номенклатура {name}, ред {i + 1}: повторен код {code}, взет е първият.");
                    continue;
                }
                nomenclature._entries[key] = text;
            }

            if (nomenclature._entries.Count == 0)
            {
                nomenclature._warn($"Номенклатура {name} е празна — кодовете ще се показват като \"код N\".");
            }
            return nomenclature;
        }

        public void Add(string code, string text) => _entries[NormalizeCode(code)] = text;

        /// <summary>True when the code has an entry. An empty code is not found.</summary>
        public bool TryGet(string code, out string text)
        {
            text = string.Empty;
            string trimmed = code.Trim();
            if (trimmed.Length == 0) return false;
            return _entries.TryGetValue(NormalizeCode(trimmed), out text!);
        }

        /// <summary>
        /// "code – text" for a known code, "код N" for an unknown one (warns once per code), empty for an empty code.
        /// </summary>
        public string Describe(string code)
        {
            string trimmed = code.Trim();
            if (trimmed.Length == 0) return string.Empty;
            if (TryGet(trimmed, out string text)) return $"{trimmed} – {text}";
            return FallbackText(trimmed);
        }

        private string FallbackText(string code)
        {
            string trimmed = code.Trim();
            if (trimmed.Length == 0) return string.Empty;
            if (_warnedCodes.Add(NormalizeCode(trimmed)))
            {
                _warn($"Номенклатура {Name}: няма текст за код {trimmed}.");
            }
            return $"код {trimmed}";
        }

        /// <summary>"01" and "1" are the same code; other codes only differ by trimming.</summary>
        private static string NormalizeCode(string code)
        {
            string trimmed = code.Trim();
            bool digitsOnly = trimmed.Length > 0;
            foreach (char c in trimmed)
            {
                if (c < '0' || c > '9') { digitsOnly = false; break; }
            }
            if (!digitsOnly) return trimmed;

            string withoutZeros = trimmed.TrimStart('0');
            return withoutZeros.Length == 0 ? "0" : withoutZeros;
        }

        private static bool IsHeader(string firstCell) =>
            firstCell.Equals("код", StringComparison.OrdinalIgnoreCase) ||
            firstCell.Equals("code", StringComparison.OrdinalIgnoreCase);

        private static string Unquote(string cell)
        {
            string value = cell.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2).Replace("\"\"", "\"").Trim();
            }
            return value;
        }
    }

    /// <summary>The four editable nomenclatures of the control report.</summary>
    public sealed class Nomenclatures
    {
        public const string VidtFile = "VIDT.csv";
        public const string NtpFile = "NTP.csv";
        public const string VidsFile = "VIDS.csv";
        public const string PravoVidFile = "PRAVOVID.csv";

        /// <summary>Вид територия (ТП).</summary>
        public Nomenclature Vidt { get; }

        /// <summary>Начин на трайно ползване (НТП).</summary>
        public Nomenclature Ntp { get; }

        /// <summary>Вид собственост.</summary>
        public Nomenclature Vids { get; }

        /// <summary>Вид право.</summary>
        public Nomenclature PravoVid { get; }

        public Nomenclatures(Nomenclature vidt, Nomenclature ntp, Nomenclature vids, Nomenclature pravoVid)
        {
            Vidt = vidt;
            Ntp = ntp;
            Vids = vids;
            PravoVid = pravoVid;
        }

        /// <summary>Loads the four CSV files from <paramref name="folder"/>.</summary>
        public static Nomenclatures Load(string folder, Action<string>? warn = null) =>
            new Nomenclatures(
                Nomenclature.Load("VIDT", Path.Combine(folder, VidtFile), warn),
                Nomenclature.Load("NTP", Path.Combine(folder, NtpFile), warn),
                Nomenclature.Load("VIDS", Path.Combine(folder, VidsFile), warn),
                Nomenclature.Load("PRAVOVID", Path.Combine(folder, PravoVidFile), warn));

        /// <summary>Four empty nomenclatures (every code shows as "код N").</summary>
        public static Nomenclatures Empty(Action<string>? warn = null) =>
            new Nomenclatures(
                new Nomenclature("VIDT", warn),
                new Nomenclature("NTP", warn),
                new Nomenclature("VIDS", warn),
                new Nomenclature("PRAVOVID", warn));
    }

    /// <summary>KAT (category) as printed: a number becomes Roman numerals (8 -> VIII), 0 or empty gives an empty cell.</summary>
    public static class CategoryFormat
    {
        private static readonly int[] Values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly string[] Symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        /// <summary>Anything that is not a whole number 1..3999 is returned as written (trimmed), without validation.</summary>
        public static string Format(string kat)
        {
            string trimmed = kat.Trim();
            if (trimmed.Length == 0) return string.Empty;

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) return trimmed;
            if (number == 0) return string.Empty;
            if (number > 3999) return trimmed;

            var sb = new StringBuilder();
            for (int i = 0; i < Values.Length; i++)
            {
                while (number >= Values[i])
                {
                    sb.Append(Symbols[i]);
                    number -= Values[i];
                }
            }
            return sb.ToString();
        }
    }
}
