using System.Text;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One printed section of a report: the землище of one EKATTE and the picked parcels in it.</summary>
    public sealed class SettlementSection
    {
        public string Ekatte { get; set; } = string.Empty;

        /// <summary>The loaded .cad of the землище; an empty register (just the EKATTE) when none is loaded.</summary>
        public CadRegisterData Register { get; set; } = new CadRegisterData();

        /// <summary>True when a .cad is loaded for this EKATTE.</summary>
        public bool HasCad { get; set; }

        /// <summary>"НА ТЕРИТОРИЯТА НА С. БРЕСТЕ, ЕКАТТЕ 06433, ОБЩ. ЧЕРВЕН БРЯГ, ОБЛ. ПЛЕВЕН".</summary>
        public string EkatteTitle { get; set; } = string.Empty;

        /// <summary>"с. Бресте" (kind and name from the EKATTE register, else the name in the .cad); for messages.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The picked parcels in this землище, distinct, in the order they were given.</summary>
        public List<string> ParcelIds { get; } = new List<string>();

        /// <summary>The lowest pole number among the poles of these parcels; null when there are none.</summary>
        public string? LowestPole { get; set; }
    }

    /// <summary>One municipality = one sheet of a workbook, with one section per землище.</summary>
    public sealed class MunicipalityGroup
    {
        public string Municipality { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public List<SettlementSection> Sections { get; } = new List<SettlementSection>();

        /// <summary>The lowest pole number over all sections; null when there are none.</summary>
        public string? LowestPole { get; set; }
    }

    /// <summary>The groups plus what could not be grouped; the caller logs the warnings (counts and parcel IDs only).</summary>
    public sealed class MunicipalityGroupingResult
    {
        public List<MunicipalityGroup> Groups { get; } = new List<MunicipalityGroup>();

        /// <summary>Parcel IDs with no EKATTE part (no dot): left out of every report.</summary>
        public List<string> IgnoredParcelIds { get; } = new List<string>();

        /// <summary>EKATTE codes that are not in the EKATTE register (their parcels are in the "Неизвестна община" group).</summary>
        public List<string> UnknownEkatte { get; } = new List<string>();

        /// <summary>Sections of picked parcels whose EKATTE has no loaded .cad.</summary>
        public List<SettlementSection> SectionsWithoutCad { get; } = new List<SettlementSection>();
    }

    /// <summary>
    /// Groups picked parcels by municipality and землище for the three .cad reports. Pure logic, no AutoCAD types.
    /// Sections and sheets follow the line route: ordered by the lowest pole number they contain (numeric poles first,
    /// then text), those without poles after them, ties and the rest by EKATTE code.
    /// </summary>
    public static class MunicipalityGrouping
    {
        public const string UnknownMunicipality = "Неизвестна община";
        public const string SheetPrefix = "общ. ";
        public const int MaxSheetNameLength = 31;

        /// <param name="lowestPoleByParcelId">Lowest pole number per parcel ID (see <see cref="LowestPoleByParcel"/>); null for no poles.</param>
        public static MunicipalityGroupingResult Group(
            IEnumerable<string> parcelIds,
            CadRegisterSet set,
            EkatteRegister ekatte,
            IReadOnlyDictionary<string, string>? lowestPoleByParcelId = null)
        {
            var result = new MunicipalityGroupingResult();

            // Parcels by EKATTE, keeping the first-seen order of the IDs
            var sections = new Dictionary<string, SettlementSection>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in parcelIds)
            {
                if (!seen.Add(id)) continue;

                string code = CadRegisterData.EkatteOf(id);
                if (code.Length == 0)
                {
                    result.IgnoredParcelIds.Add(id);
                    continue;
                }

                if (!sections.TryGetValue(code, out SettlementSection? section))
                {
                    section = NewSection(code, set, ekatte);
                    sections[code] = section;
                }
                section.ParcelIds.Add(id);

                if (lowestPoleByParcelId != null && lowestPoleByParcelId.TryGetValue(id, out string? pole))
                {
                    section.LowestPole = LowerPole(section.LowestPole, pole);
                }
            }

            // Sections of the same municipality (and province) share a sheet
            var groups = new Dictionary<(string, string), MunicipalityGroup>();
            foreach (SettlementSection section in sections.Values)
            {
                string municipality = UnknownMunicipality;
                string province = string.Empty;
                if (ekatte.TryGet(section.Ekatte, out EkatteEntry entry))
                {
                    municipality = entry.Municipality;
                    province = entry.Province;
                }
                else
                {
                    result.UnknownEkatte.Add(section.Ekatte);
                }

                if (!section.HasCad) result.SectionsWithoutCad.Add(section);

                if (!groups.TryGetValue((municipality, province), out MunicipalityGroup? group))
                {
                    group = new MunicipalityGroup { Municipality = municipality, Province = province };
                    groups[(municipality, province)] = group;
                }
                group.Sections.Add(section);
                group.LowestPole = LowerPole(group.LowestPole, section.LowestPole);
            }

            foreach (MunicipalityGroup group in groups.Values)
            {
                group.Sections.Sort((a, b) => CompareByRoute(a.LowestPole, a.Ekatte, b.LowestPole, b.Ekatte));
            }

            result.Groups.AddRange(groups.Values);
            result.Groups.Sort((a, b) => CompareByRoute(a.LowestPole, a.Sections[0].Ekatte, b.LowestPole, b.Sections[0].Ekatte));
            result.UnknownEkatte.Sort(StringComparer.Ordinal);
            result.SectionsWithoutCad.Sort((a, b) => string.CompareOrdinal(a.Ekatte, b.Ekatte));

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MunicipalityGroup group in result.Groups)
            {
                string baseName = group.Municipality == UnknownMunicipality
                    ? UnknownMunicipality
                    : SheetPrefix + group.Municipality;
                group.SheetName = UniqueSheetName(baseName, usedNames);
            }
            return result;
        }

        private static SettlementSection NewSection(string code, CadRegisterSet set, EkatteRegister ekatte)
        {
            bool hasCad = set.ByEkatte.TryGetValue(code, out CadRegisterData? register);
            register ??= new CadRegisterData { Ekatte = code };

            string display = ekatte.TryGet(code, out EkatteEntry entry)
                ? $"{entry.Kind} {entry.Name}".Trim()
                : register.SettlementName;

            return new SettlementSection
            {
                Ekatte = code,
                Register = register,
                HasCad = hasCad,
                EkatteTitle = ekatte.FormatTitle(code, register.SettlementName),
                DisplayName = display
            };
        }

        /// <summary>Lowest pole number per parcel ID over all pieces (<see cref="PoleStepsTableBuilder.ComparePoleNumbers"/>).</summary>
        public static Dictionary<string, string> LowestPoleByParcel(IEnumerable<PoleStepPiece> pieces)
        {
            var lowest = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PoleStepPiece piece in pieces)
            {
                string pole = PoleLabels.StripPrefix(piece.PoleNumber);
                lowest[piece.ParcelId] = lowest.TryGetValue(piece.ParcelId, out string? known) ? LowerPole(known, pole)! : pole;
            }
            return lowest;
        }

        private static string? LowerPole(string? a, string? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return PoleStepsTableBuilder.ComparePoleNumbers(a, b) <= 0 ? a : b;
        }

        /// <summary>Sections with poles first (by lowest pole), then without poles; ties and the rest by EKATTE code.</summary>
        private static int CompareByRoute(string? poleA, string ekatteA, string? poleB, string ekatteB)
        {
            if (poleA != null && poleB != null)
            {
                int c = PoleStepsTableBuilder.ComparePoleNumbers(poleA, poleB);
                if (c != 0) return c;
            }
            else if (poleA != null) return -1;
            else if (poleB != null) return 1;
            return string.CompareOrdinal(ekatteA, ekatteB);
        }

        // -----------------------------------------------------------------
        //  Sheet names
        // -----------------------------------------------------------------

        /// <summary>Removes the characters Excel forbids in a sheet name (: \ / ? * [ ]) and a leading/trailing apostrophe; cuts to 31 characters.</summary>
        public static string CleanSheetName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (c == ':' || c == '\\' || c == '/' || c == '?' || c == '*' || c == '[' || c == ']') continue;
                sb.Append(c);
            }
            string cleaned = sb.ToString().Trim().Trim('\'').Trim();
            if (cleaned.Length > MaxSheetNameLength) cleaned = cleaned.Substring(0, MaxSheetNameLength).TrimEnd();
            return cleaned.Length == 0 ? "Лист" : cleaned;
        }

        /// <summary>
        /// The cleaned name, made unique (case-insensitive) in <paramref name="used"/> with " (2)", " (3)", ... and still within
        /// 31 characters. The chosen name is added to <paramref name="used"/>.
        /// </summary>
        public static string UniqueSheetName(string name, ISet<string> used)
        {
            string cleaned = CleanSheetName(name);
            string candidate = cleaned;
            for (int n = 2; used.Contains(candidate); n++)
            {
                string suffix = $" ({n})";
                string head = cleaned.Length + suffix.Length > MaxSheetNameLength
                    ? cleaned.Substring(0, MaxSheetNameLength - suffix.Length).TrimEnd()
                    : cleaned;
                candidate = head + suffix;
            }
            used.Add(candidate);
            return candidate;
        }
    }
}
