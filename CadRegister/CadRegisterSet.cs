using PUP_AUTO.Core;

namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// The loaded AGKK .cad files, one землище each, keyed by EKATTE. Plain C#: no AutoCAD or WPF types.
    /// A project crosses several землища, so the reports pick the землище of every parcel from this set.
    /// </summary>
    public sealed class CadRegisterSet
    {
        private readonly Dictionary<string, CadRegisterData> _byEkatte = new Dictionary<string, CadRegisterData>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _sourceFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _writeTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, CadRegisterData> ByEkatte => _byEkatte;

        /// <summary>The path each землище was read from, by EKATTE.</summary>
        public IReadOnlyDictionary<string, string> SourceFiles => _sourceFiles;

        public int Count => _byEkatte.Count;

        /// <summary>The землище of a full parcel ID ("06433.77.12" -> the file of EKATTE 06433).</summary>
        public bool TryGetFor(string parcelId, out CadRegisterData data)
        {
            string ekatte = CadRegisterData.EkatteOf(parcelId);
            if (ekatte.Length > 0 && _byEkatte.TryGetValue(ekatte, out CadRegisterData? found))
            {
                data = found;
                return true;
            }
            data = null!;
            return false;
        }

        /// <summary>
        /// Reads each file with <see cref="CadRegisterReader"/>. A file that cannot be read, or has no parcels, is skipped
        /// with a warning that names it; the others still load. Two files with the same EKATTE: the newer one is kept.
        /// Reader warnings are passed on prefixed with the file name. Warnings never contain row content.
        /// </summary>
        public static CadRegisterSet Load(IEnumerable<string> paths, Action<string> warn)
        {
            var set = new CadRegisterSet();
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                CadRegisterData data;
                try
                {
                    data = new CadRegisterReader(w => warn($"{name}: {w}")).ReadFile(path);
                }
                catch (Exception ex)
                {
                    warn($"{name}: файлът не може да се прочете ({ex.Message}) — пропуснат.");
                    continue;
                }

                if (data.Parcels.Count == 0)
                {
                    warn($"{name}: в .cad няма имоти (таблица POZEMLIMOTI) — файлът е пропуснат.");
                    continue;
                }
                if (data.Ekatte.Length == 0)
                {
                    warn($"{name}: в хедъра няма ЕКАТТЕ — файлът е пропуснат.");
                    continue;
                }

                DateTime writeTime = SafeWriteTime(path);
                if (set._byEkatte.ContainsKey(data.Ekatte))
                {
                    string existing = set._sourceFiles[data.Ekatte];
                    bool candidateWins = PreferNewer(set._writeTimes[data.Ekatte], writeTime);
                    warn($"ЕКАТТЕ {data.Ekatte} е в два файла: {Path.GetFileName(existing)} и {name} — " +
                         $"взет е {(candidateWins ? name : Path.GetFileName(existing))} (по-новият).");
                    if (!candidateWins) continue;
                }

                set._byEkatte[data.Ekatte] = data;
                set._sourceFiles[data.Ekatte] = path;
                set._writeTimes[data.Ekatte] = writeTime;
            }
            return set;
        }

        /// <summary>True when the candidate file is strictly newer than the one already held; a tie keeps the first.</summary>
        public static bool PreferNewer(DateTime existingWriteTime, DateTime candidateWriteTime) =>
            candidateWriteTime > existingWriteTime;

        /// <summary>The .cad files of a folder and all its subfolders, in path order, without the template library.</summary>
        public static List<string> FindCadFiles(string folder)
        {
            return Directory.EnumerateFiles(folder, "*.cad", SearchOption.AllDirectories)
                .Where(f => !string.Equals(Path.GetFileName(f), FileNames.CadLibraryFile, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTime(path); }
            catch { return DateTime.MinValue; }
        }
    }
}
