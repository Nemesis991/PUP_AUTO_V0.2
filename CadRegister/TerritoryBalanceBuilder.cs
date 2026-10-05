using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One row of a balance table (a group, or the "Общо:" row). Areas are decares as printed (0.001), summed with decimal.</summary>
    public sealed class BalanceRow
    {
        public int Number { get; set; }
        public string Group { get; set; } = string.Empty;
        public int ParcelCount { get; set; }
        public decimal AreaDka { get; set; }          // sum of register column 7
        public decimal RestrictedDka { get; set; }    // sum of register column 8
        public int PoleCount { get; set; }
        public decimal StepDka { get; set; }          // sum of register column 11
        public decimal AffectedDka { get; set; }      // RestrictedDka + StepDka

        /// <summary>Share of the section's affected area, 0.01; null when the section total is 0.</summary>
        public decimal? Percent { get; set; }
    }

    public sealed class BalanceTable
    {
        /// <summary>Header of column B: "Категория земя", "Вид собственост", "Вид територия", "Начин на трайно ползване".</summary>
        public string GroupHeader { get; set; } = string.Empty;
        public List<BalanceRow> Rows { get; } = new List<BalanceRow>();
        public BalanceRow Total { get; set; } = new BalanceRow();
    }

    /// <summary>The balances of one землище: title, subtitle and the four tables.</summary>
    public sealed class TerritoryBalance
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public BalanceTable[] Tables { get; set; } = new BalanceTable[0];

        /// <summary>Parcels of the section that are not in the .cad (they are in the "Няма данни в .cad" row of every table).</summary>
        public int NotFoundCount { get; set; }
    }

    /// <summary>
    /// Builds the territory balance of one землище from the finished register of affected parcels (no AutoCAD types).
    /// The same parcel rows and printed areas, grouped by category, ownership, territory type and НТП, so both documents
    /// agree to the last decimal. Poles are counted once: <see cref="AssignPolesToParcels"/>.
    /// </summary>
    public static class TerritoryBalanceBuilder
    {
        public const string TitlePrefix = "БАЛАНСИ НА ТЕРИТОРИЯТА НА ЗАСЕГНАТИТЕ ИМОТИ ЗА ";
        public const string TotalLabel = "Общо:";
        public const string NoCategory = "Без категория";
        public const string NoCode = "Без код";
        public const string NotInCad = "Няма данни в .cad";

        public static readonly string[] GroupHeaders =
            { "Категория земя", "Вид собственост", "Вид територия", "Начин на трайно ползване" };

        /// <summary>
        /// Every pole is counted once, for the parcel that holds its largest piece (pieces of one pole in one parcel are
        /// added first; a tie goes to the smaller parcel ID by <see cref="PoleStepsTableBuilder.CompareParcelIds"/>).
        /// Give it ALL pieces of the run, not those of one section.
        /// </summary>
        public static Dictionary<string, int> AssignPolesToParcels(IEnumerable<PoleStepPiece> allPieces)
        {
            var sqmByPole = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
            foreach (PoleStepPiece piece in allPieces)
            {
                string pole = PoleLabels.StripPrefix(piece.PoleNumber);
                if (!sqmByPole.TryGetValue(pole, out Dictionary<string, double>? byParcel))
                {
                    byParcel = new Dictionary<string, double>(StringComparer.Ordinal);
                    sqmByPole[pole] = byParcel;
                }
                byParcel.TryGetValue(piece.ParcelId, out double sum);
                byParcel[piece.ParcelId] = sum + piece.PieceAreaSqm;
            }

            var polesByParcel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Dictionary<string, double> byParcel in sqmByPole.Values)
            {
                string? best = null;
                double bestSqm = 0.0;
                foreach (KeyValuePair<string, double> entry in byParcel)
                {
                    if (best == null || entry.Value > bestSqm ||
                        (entry.Value == bestSqm && PoleStepsTableBuilder.CompareParcelIds(entry.Key, best) < 0))
                    {
                        best = entry.Key;
                        bestSqm = entry.Value;
                    }
                }
                polesByParcel.TryGetValue(best!, out int count);
                polesByParcel[best!] = count + 1;
            }
            return polesByParcel;
        }

        /// <exception cref="InvalidOperationException">The four tables (or the register totals) do not agree.</exception>
        public static TerritoryBalance Build(
            AffectedRegister register,
            IReadOnlyDictionary<string, int> polesByParcel,
            string projectName,
            Nomenclatures nomenclatures)
        {
            var notFound = new HashSet<string>(register.NotFound, StringComparer.Ordinal);
            List<AffectedRegisterRow> parcels = register.Rows.Where(r => r.IsFirstOfParcel).ToList();

            decimal sectionAffected = 0m;
            foreach (AffectedRegisterRow row in parcels)
            {
                sectionAffected += (decimal)(row.RestrictedDka ?? 0.0) + (decimal)(row.StepDka ?? 0.0);
            }

            var keySelectors = new Func<AffectedRegisterRow, Group>[]
            {
                row => KatGroup(row.KatCode),
                row => CodeGroup(row.VidsCode, nomenclatures.Vids),
                row => CodeGroup(row.VidtCode, nomenclatures.Vidt),
                row => CodeGroup(row.NtpCode, nomenclatures.Ntp)
            };

            var tables = new BalanceTable[keySelectors.Length];
            for (int t = 0; t < tables.Length; t++)
            {
                tables[t] = BuildTable(GroupHeaders[t], parcels, notFound, keySelectors[t], polesByParcel, sectionAffected);
            }

            var balance = new TerritoryBalance
            {
                Title = TitlePrefix + projectName.Trim(),
                Subtitle = register.Subtitle,
                Tables = tables,
                NotFoundCount = notFound.Count
            };
            Verify(balance, register, parcels.Count);
            return balance;
        }

        // -----------------------------------------------------------------
        //  Groups
        // -----------------------------------------------------------------

        /// <summary>Sort class: 0 = numeric code, 1 = other code, 2 = empty code, 3 = not in the .cad.</summary>
        private sealed class Group
        {
            public string Key = string.Empty;
            public string Text = string.Empty;
            public int Class;
            public long Number;
        }

        private static Group NotInCadGroup() => new Group { Key = NotInCad, Text = NotInCad, Class = 3 };

        private static Group KatGroup(string kat)
        {
            string trimmed = kat.Trim();
            string roman = CategoryFormat.Format(trimmed);
            if (roman.Length == 0) return new Group { Key = NoCategory, Text = NoCategory, Class = 2 };
            return Coded(trimmed, roman + " категория");
        }

        private static Group CodeGroup(string code, Nomenclature nomenclature)
        {
            string trimmed = code.Trim();
            if (trimmed.Length == 0) return new Group { Key = NoCode, Text = NoCode, Class = 2 };
            return Coded(trimmed, nomenclature.TextOf(trimmed)); // an unknown code gives "код N" and one warning
        }

        private static Group Coded(string trimmed, string text)
        {
            // "01" and "1" are one code, like in the nomenclatures
            if (long.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long number))
            {
                return new Group { Key = number.ToString(System.Globalization.CultureInfo.InvariantCulture), Text = text, Class = 0, Number = number };
            }
            return new Group { Key = trimmed, Text = text, Class = 1 };
        }

        private static BalanceTable BuildTable(
            string header,
            List<AffectedRegisterRow> parcels,
            HashSet<string> notFound,
            Func<AffectedRegisterRow, Group> selector,
            IReadOnlyDictionary<string, int> polesByParcel,
            decimal sectionAffected)
        {
            var groups = new Dictionary<string, (Group Group, BalanceRow Row)>(StringComparer.Ordinal);
            var total = new BalanceRow { Group = TotalLabel };

            foreach (AffectedRegisterRow parcel in parcels)
            {
                Group group = notFound.Contains(parcel.Number) ? NotInCadGroup() : selector(parcel);
                string mapKey = group.Class + "|" + group.Key;
                if (!groups.TryGetValue(mapKey, out (Group Group, BalanceRow Row) entry))
                {
                    entry = (group, new BalanceRow { Group = group.Text });
                    groups[mapKey] = entry;
                }

                polesByParcel.TryGetValue(parcel.Number, out int poles);
                Add(entry.Row, parcel, poles);
                Add(total, parcel, poles);
            }

            var table = new BalanceTable { GroupHeader = header, Total = total };
            foreach ((Group Group, BalanceRow Row) entry in groups.Values
                .OrderBy(e => e.Group.Class)
                .ThenBy(e => e.Group.Number)
                .ThenBy(e => e.Group.Key, StringComparer.Ordinal))
            {
                table.Rows.Add(entry.Row);
            }

            for (int i = 0; i < table.Rows.Count; i++)
            {
                table.Rows[i].Number = i + 1;
                table.Rows[i].Percent = Percent(table.Rows[i].AffectedDka, sectionAffected);
            }
            total.Percent = sectionAffected == 0m ? (decimal?)null : 100m;
            return table;
        }

        private static void Add(BalanceRow row, AffectedRegisterRow parcel, int poles)
        {
            row.ParcelCount++;
            row.AreaDka += (decimal)(parcel.AreaDka ?? 0.0);
            row.RestrictedDka += (decimal)(parcel.RestrictedDka ?? 0.0);
            row.PoleCount += poles;
            row.StepDka += (decimal)(parcel.StepDka ?? 0.0);
            row.AffectedDka = row.RestrictedDka + row.StepDka;
        }

        private static decimal? Percent(decimal affected, decimal sectionAffected) =>
            sectionAffected == 0m ? (decimal?)null : Math.Round(affected / sectionAffected * 100m, 2, MidpointRounding.AwayFromZero);

        // -----------------------------------------------------------------
        //  Check
        // -----------------------------------------------------------------

        private static void Verify(TerritoryBalance balance, AffectedRegister register, int parcelCount)
        {
            BalanceRow first = balance.Tables[0].Total;
            foreach (BalanceTable table in balance.Tables)
            {
                BalanceRow t = table.Total;
                if (t.ParcelCount != first.ParcelCount || t.AreaDka != first.AreaDka || t.RestrictedDka != first.RestrictedDka ||
                    t.PoleCount != first.PoleCount || t.StepDka != first.StepDka || t.AffectedDka != first.AffectedDka)
                {
                    throw new InvalidOperationException($"Баланси: общите редове на таблиците не съвпадат ({balance.Subtitle}).");
                }
                if (table.Rows.Sum(r => r.ParcelCount) != parcelCount || t.ParcelCount != parcelCount)
                {
                    throw new InvalidOperationException($"Баланси: броят имоти в таблица \"{table.GroupHeader}\" не е равен на броя в регистъра ({balance.Subtitle}).");
                }
            }

            if (first.AreaDka != (decimal)register.TotalAreaDka ||
                first.RestrictedDka != (decimal)register.TotalRestrictedDka ||
                first.StepDka != (decimal)register.TotalStepDka)
            {
                throw new InvalidOperationException($"Баланси: общите суми не съвпадат с ОБЩО на регистъра ({balance.Subtitle}).");
            }
        }
    }
}
