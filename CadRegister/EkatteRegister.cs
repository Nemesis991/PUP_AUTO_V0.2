using System.Text;
using PUP_AUTO.Core;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One settlement of the NSI EKATTE register.</summary>
    public sealed class EkatteEntry
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>"с." or "гр." (or "ман.").</summary>
        public string Kind { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Municipality { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
    }

    /// <summary>
    /// EKATTE register from a CSV with the columns EKATTE;вид;име;община;област (UTF-8, header line optional).
    /// The bundled file is derived from the NSI EKATTE register (nsi.bg/nrnm/ekatte).
    /// </summary>
    public sealed class EkatteRegister
    {
        private readonly Dictionary<string, EkatteEntry> _entries = new Dictionary<string, EkatteEntry>();

        public int Count => _entries.Count;

        /// <summary>Loads only the file; a missing file is a warning.</summary>
        public static EkatteRegister Load(string path, Action<string>? warn = null)
        {
            var register = new EkatteRegister();
            if (!File.Exists(path))
            {
                warn?.Invoke($"Липсва регистърът на ЕКАТТЕ: {path}.");
                return register;
            }
            register.Merge(File.ReadAllLines(path, Encoding.UTF8), warn);
            return register;
        }

        /// <summary>
        /// The embedded register, then the file (when it exists) on top: a code in the file replaces the embedded
        /// entry of that code. A missing file is normal (no warning).
        /// </summary>
        public static EkatteRegister LoadWithDefaults(string? path, Action<string>? warn = null)
        {
            var register = new EkatteRegister();
            string? embedded = EmbeddedDefaults.ReadText(FileNames.EkatteRegisterFile);
            if (embedded != null)
            {
                register.Merge(EmbeddedDefaults.SplitLines(embedded), warn);
            }
            if (path != null && File.Exists(path))
            {
                register.Merge(File.ReadAllLines(path, Encoding.UTF8), warn);
            }
            return register;
        }

        private void Merge(string[] lines, Action<string>? warn)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                string[] cells = line.Split(';');
                if (cells.Length < 5)
                {
                    if (i > 0) warn?.Invoke($"ЕКАТТЕ регистър, ред {i + 1}: очаквани 5 колони, редът е пропуснат.");
                    continue;
                }

                string code = cells[0].Trim();
                if (!IsCode(code))
                {
                    if (i > 0) warn?.Invoke($"ЕКАТТЕ регистър, ред {i + 1}: невалиден код, редът е пропуснат.");
                    continue; // the header line
                }

                _entries[code] = new EkatteEntry
                {
                    Code = code,
                    Kind = cells[1].Trim(),
                    Name = cells[2].Trim(),
                    Municipality = cells[3].Trim(),
                    Province = cells[4].Trim()
                };
            }
        }

        public void Add(EkatteEntry entry) => _entries[entry.Code] = entry;

        public bool TryGet(string code, out EkatteEntry entry) => _entries.TryGetValue(code.Trim(), out entry!);

        /// <summary>
        /// "НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА".
        /// A code missing from the register gives the shorter "НА ТЕРИТОРИЯТА НА &lt;fallbackName&gt;, ЕКАТТЕ &lt;код&gt;".
        /// </summary>
        public string FormatTitle(string code, string fallbackName)
        {
            if (TryGet(code, out EkatteEntry entry))
            {
                return Upper($"НА ТЕРИТОРИЯТА НА {entry.Kind} {entry.Name}, ЕКАТТЕ {entry.Code}, " +
                             $"ОБЩ. {entry.Municipality}, ОБЛ. {entry.Province}");
            }
            string name = fallbackName.Trim();
            return Upper(name.Length == 0
                ? $"НА ТЕРИТОРИЯТА НА ЕКАТТЕ {code}"
                : $"НА ТЕРИТОРИЯТА НА {name}, ЕКАТТЕ {code}");
        }

        private static string Upper(string text) => text.ToUpperInvariant();

        private static bool IsCode(string code)
        {
            if (code.Length == 0) return false;
            foreach (char c in code)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
