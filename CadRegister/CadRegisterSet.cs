using PUP_AUTO.Core;

namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// The loaded AGKK .cad files, keyed by EKATTE (one землище each). Plain C#: no AutoCAD or WPF types.
    /// A project crosses several землища, so the reports pick the землище of every parcel from this set.
    /// A землище may come in several files (КАИС extracts are often ordered in parts): they are merged into one register.
    /// </summary>
    public sealed class CadRegisterSet
    {
        private readonly Dictionary<string, CadRegisterData> _byEkatte = new Dictionary<string, CadRegisterData>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _sourceFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, CadRegisterData> ByEkatte => _byEkatte;

        /// <summary>The paths each землище was read from (one or more files), by EKATTE, in the order they were given.</summary>
        public IReadOnlyDictionary<string, List<string>> SourceFiles => _sourceFiles;

        public int Count => _byEkatte.Count;

        /// <summary>The files a землище was read from; empty for an unknown EKATTE.</summary>
        public IReadOnlyList<string> SourceFilesOf(string ekatte) =>
            _sourceFiles.TryGetValue(ekatte, out List<string>? files) ? files : (IReadOnlyList<string>)Array.Empty<string>();

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

        private sealed class LoadedFile
        {
            public int Index;
            public string Path = string.Empty;
            public CadRegisterData Data = null!;
            public DateTime WriteTime;
        }

        /// <summary>
        /// Reads each file with <see cref="CadRegisterReader"/>. A file that cannot be read, or has no parcels, is skipped
        /// with a warning that names it; the others still load. Several files with the same EKATTE are merged
        /// (<see cref="Merge"/>) and one line about it goes to <paramref name="info"/>.
        /// Reader warnings are passed on prefixed with the file name. Warnings never contain row content.
        /// </summary>
        public static CadRegisterSet Load(IEnumerable<string> paths, Action<string> warn, Action<string>? info = null)
        {
            var set = new CadRegisterSet();
            var byEkatte = new Dictionary<string, List<LoadedFile>>(StringComparer.Ordinal);
            var order = new List<string>();
            int index = 0;
            foreach (string path in paths)
            {
                string name = System.IO.Path.GetFileName(path);
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

                if (!byEkatte.TryGetValue(data.Ekatte, out List<LoadedFile>? files))
                {
                    files = new List<LoadedFile>();
                    byEkatte[data.Ekatte] = files;
                    order.Add(data.Ekatte);
                }
                files.Add(new LoadedFile { Index = index++, Path = path, Data = data, WriteTime = SafeWriteTime(path) });
            }

            foreach (string ekatte in order)
            {
                List<LoadedFile> files = byEkatte[ekatte];
                set._sourceFiles[ekatte] = files.Select(f => f.Path).ToList();
                if (files.Count == 1)
                {
                    set._byEkatte[ekatte] = files[0].Data;
                    continue;
                }

                // Oldest first, the newest last (it overrides); a tie in time: the file given first wins
                List<LoadedFile> oldestFirst = files.OrderBy(f => f.WriteTime).ThenByDescending(f => f.Index).ToList();
                set._byEkatte[ekatte] = Merge(oldestFirst.Select(f => f.Data).ToList(), out int inSeveralFiles);
                info?.Invoke(
                    $"ЕКАТТЕ {ekatte}: обединени {files.Count} файла ({string.Join(", ", files.Select(f => System.IO.Path.GetFileName(f.Path)))}), " +
                    $"{set._byEkatte[ekatte].Parcels.Count} имота, {inSeveralFiles} в повече от един файл — взет е по-новият.");
            }
            return set;
        }

        /// <summary>
        /// Merges the registers of one землище, given oldest first. Parcels: the union; a parcel in several files comes from the
        /// newest one together with its own rights (the rights of two files are never mixed for one parcel). The header
        /// (name, version) is the newest file's. <see cref="CadRegisterData.PersonCount"/> counts the distinct persons of all files.
        /// </summary>
        /// <param name="inSeveralFiles">The number of parcels that are in more than one file.</param>
        public static CadRegisterData Merge(IReadOnlyList<CadRegisterData> oldestFirst, out int inSeveralFiles)
        {
            CadRegisterData newest = oldestFirst[oldestFirst.Count - 1];
            var merged = new CadRegisterData { Ekatte = newest.Ekatte, SettlementName = newest.SettlementName, Version = newest.Version };
            var personIds = new HashSet<string>(StringComparer.Ordinal);
            inSeveralFiles = 0;

            foreach (CadRegisterData file in oldestFirst)
            {
                foreach (KeyValuePair<string, CadastralParcel> parcel in file.Parcels)
                {
                    if (merged.Parcels.ContainsKey(parcel.Key)) inSeveralFiles++;
                    merged.Rights.Remove(parcel.Key); // the rights of an older file go away with its parcel
                    merged.Parcels[parcel.Key] = parcel.Value;
                    if (file.Rights.TryGetValue(parcel.Key, out List<OwnershipRight>? rights)) merged.Rights[parcel.Key] = rights;
                }
                foreach (KeyValuePair<string, List<OwnershipRight>> orphan in file.Rights)
                {
                    // rights of a parcel that is not in this file's POZEMLIMOTI: kept only when nobody else has the parcel
                    if (!file.Parcels.ContainsKey(orphan.Key) && !merged.Parcels.ContainsKey(orphan.Key)) merged.Rights[orphan.Key] = orphan.Value;
                }
                personIds.UnionWith(file.PersonIds);
            }

            foreach (string id in personIds) merged.PersonIds.Add(id);
            merged.PersonCount = personIds.Count;
            return merged;
        }

        /// <summary>The .cad files of a folder and all its subfolders, in path order, without the template library.</summary>
        public static List<string> FindCadFiles(string folder)
        {
            return Directory.EnumerateFiles(folder, "*.cad", SearchOption.AllDirectories)
                .Where(f => !string.Equals(System.IO.Path.GetFileName(f), FileNames.CadLibraryFile, StringComparison.OrdinalIgnoreCase))
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
