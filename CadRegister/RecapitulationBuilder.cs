namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// One row of the recapitulation: a землище, or a "Общо" row (municipality or област). Areas are decares as printed, summed
    /// with decimal. <see cref="RouteMetres"/> is the unrounded route length; <see cref="RouteKm"/> is it rounded to 0.001 km.
    /// </summary>
    public sealed class RecapitulationRow
    {
        /// <summary>Municipality number 1, 2, 3 ... on its first row; 0 elsewhere.</summary>
        public int Number { get; set; }
        public string Province { get; set; } = string.Empty;       // only on the very first data row of the sheet
        public string Municipality { get; set; } = string.Empty;   // only on the first row of the municipality
        public string Settlement { get; set; } = string.Empty;
        public string Ekatte { get; set; } = string.Empty;

        public int ParcelCount { get; set; }
        public decimal AreaDka { get; set; }
        public decimal RestrictedDka { get; set; }
        public int PoleCount { get; set; }
        public decimal StepDka { get; set; }
        public decimal AffectedDka { get; set; }

        /// <summary>Null when no route length is known (no axis picked).</summary>
        public decimal? RouteMetres { get; set; }
        public decimal? RouteKm => RouteMetres.HasValue ? RouteLengths.ToKm(RouteMetres.Value) : (decimal?)null;
    }

    public sealed class RecapitulationMunicipality
    {
        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>The sheet name of the municipality group; the key to its 05 balance.</summary>
        public string Key { get; set; } = string.Empty;

        public List<RecapitulationRow> Rows { get; } = new List<RecapitulationRow>();
        public RecapitulationRow Total { get; set; } = new RecapitulationRow();
        public string TotalLabel { get; set; } = string.Empty;
    }

    /// <summary>One област = one sheet.</summary>
    public sealed class RecapitulationSheet
    {
        public string SheetName { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>The object name row under the header (the project text as typed).</summary>
        public string ObjectName { get; set; } = string.Empty;

        public List<RecapitulationMunicipality> Municipalities { get; } = new List<RecapitulationMunicipality>();
        public RecapitulationRow Total { get; set; } = new RecapitulationRow();
        public string TotalLabel { get; set; } = string.Empty;
    }

    /// <summary>
    /// Builds "Обща рекапитулация на площите": one table per област, one row per землище, a subtotal per municipality and
    /// a total per област. Nothing is recomputed: the row of a землище is the "Общо:" row of its territory balance (04), the
    /// subtotals are the sums of those rows, so 06 = Σ 04 = 05. Pure data, no AutoCAD types.
    /// </summary>
    public static class RecapitulationBuilder
    {
        public const string TitlePrefix = "ОБЩА РЕКАПИТУЛАЦИЯ НА ПЛОЩИТЕ ЗА ";
        public const string UnknownProvince = "Неизвестна област";
        public const string ProvinceSheetPrefix = "обл. ";

        /// <param name="groups">The municipalities with the balances of their землища, in route order.</param>
        /// <param name="routeMetres">Route length in metres per EKATTE; null or empty = no axis picked, the column stays empty.</param>
        public static List<RecapitulationSheet> Build(
            IReadOnlyList<(MunicipalityGroup Group, List<TerritoryBalance> Sections)> groups,
            string projectName,
            EkatteRegister? ekatte,
            IReadOnlyDictionary<string, decimal>? routeMetres)
        {
            bool hasRoute = routeMetres != null && routeMetres.Count > 0;
            var sheets = new List<RecapitulationSheet>();
            var byProvince = new Dictionary<string, RecapitulationSheet>(StringComparer.Ordinal);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((MunicipalityGroup group, List<TerritoryBalance> sections) in groups)
            {
                if (sections.Count == 0) continue;

                string province = group.Province.Trim();
                if (!byProvince.TryGetValue(province, out RecapitulationSheet? sheet))
                {
                    bool known = province.Length > 0;
                    sheet = new RecapitulationSheet
                    {
                        Province = province,
                        SheetName = MunicipalityGrouping.UniqueSheetName(known ? ProvinceSheetPrefix + province : UnknownProvince, usedNames),
                        Title = TitlePrefix + projectName.Trim(),
                        Subtitle = known ? ("НА ТЕРИТОРИЯТА НА ОБЛ. " + province).ToUpperInvariant() : "НА ТЕРИТОРИЯТА НА НЕИЗВЕСТНА ОБЛАСТ",
                        ObjectName = projectName.Trim(),
                        TotalLabel = known ? $"Общо за обл. {province}:" : "Общо за неизвестна област:"
                    };
                    byProvince[province] = sheet;
                    sheets.Add(sheet);
                }

                var municipality = new RecapitulationMunicipality
                {
                    Number = sheet.Municipalities.Count + 1,
                    Name = group.Municipality,
                    Key = group.SheetName,
                    TotalLabel = group.Municipality == MunicipalityGrouping.UnknownMunicipality
                        ? $"Общо за {group.Municipality}:"
                        : $"Общо за общ. {group.Municipality}:"
                };

                foreach (TerritoryBalance balance in sections)
                {
                    BalanceRow total = balance.Tables[0].Total; // all four tables have the same totals
                    var row = new RecapitulationRow
                    {
                        Settlement = SettlementText(balance, ekatte),
                        Ekatte = balance.Ekatte,
                        ParcelCount = total.ParcelCount,
                        AreaDka = total.AreaDka,
                        RestrictedDka = total.RestrictedDka,
                        PoleCount = total.PoleCount,
                        StepDka = total.StepDka,
                        AffectedDka = total.AffectedDka
                    };
                    if (hasRoute)
                    {
                        routeMetres!.TryGetValue(balance.Ekatte, out decimal metres);
                        row.RouteMetres = metres;
                    }
                    municipality.Rows.Add(row);
                }

                municipality.Rows[0].Number = municipality.Number;
                municipality.Rows[0].Municipality = municipality.Name;
                municipality.Total = Sum(municipality.Rows);
                sheet.Municipalities.Add(municipality);
            }

            foreach (RecapitulationSheet sheet in sheets)
            {
                sheet.Municipalities[0].Rows[0].Province = sheet.Province;
                sheet.Total = Sum(sheet.Municipalities.Select(m => m.Total));
            }
            return sheets;
        }

        /// <summary>"с. Царевец" from the EKATTE register, else the name in the .cad, else the code.</summary>
        private static string SettlementText(TerritoryBalance balance, EkatteRegister? ekatte)
        {
            if (ekatte != null && balance.Ekatte.Length > 0 && ekatte.TryGet(balance.Ekatte, out EkatteEntry entry))
            {
                return $"{entry.Kind} {entry.Name}".Trim();
            }
            return balance.SettlementName.Trim().Length > 0 ? balance.SettlementName.Trim() : balance.Ekatte;
        }

        private static RecapitulationRow Sum(IEnumerable<RecapitulationRow> rows)
        {
            var total = new RecapitulationRow();
            foreach (RecapitulationRow row in rows)
            {
                total.ParcelCount += row.ParcelCount;
                total.AreaDka += row.AreaDka;
                total.RestrictedDka += row.RestrictedDka;
                total.PoleCount += row.PoleCount;
                total.StepDka += row.StepDka;
                total.AffectedDka += row.AffectedDka;
                if (row.RouteMetres.HasValue) total.RouteMetres = (total.RouteMetres ?? 0m) + row.RouteMetres.Value;
            }
            return total;
        }

        /// <summary>True when the municipality subtotal equals the Общо of its combined balance (05): parcels, areas, poles, steps.</summary>
        public static bool MatchesCombinedBalance(RecapitulationMunicipality municipality, TerritoryBalance combined)
        {
            BalanceRow expected = combined.Tables[0].Total;
            RecapitulationRow got = municipality.Total;
            return got.ParcelCount == expected.ParcelCount && got.AreaDka == expected.AreaDka &&
                   got.RestrictedDka == expected.RestrictedDka && got.PoleCount == expected.PoleCount &&
                   got.StepDka == expected.StepDka && got.AffectedDka == expected.AffectedDka;
        }
    }
}
