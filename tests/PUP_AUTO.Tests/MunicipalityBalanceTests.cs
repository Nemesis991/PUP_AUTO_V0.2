using PUP_AUTO.CadRegister;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Two synthetic землища of one municipality for the municipality balance (made-up parcels, areas in m²).</summary>
    internal static class MunicipalityFixture
    {
        public const string Title = "ОБЩИ БАЛАНСИ НА ТЕРИТОРИЯТА НА ЗАСЕГНАТИТЕ ИМОТИ ЗА НОВА ВЛ 110kV";
        public const string Subtitle = "НА ТЕРИТОРИЯТА НА ОБЩИНА ТЕСТ, ОБЛ. ТЕСТ";

        /// <summary>
        /// 06433: the 6 parcels of <see cref="BalanceFixture"/>, poles 10 and 11 in 1.3, the border pole 40 (5 m²) in 1.1.
        /// 06434: 2.1 (IV, Частна, 3, 2800; pole 40 9 m²), 2.2 (V, VIDS 1 unknown, 3, 2230; pole 50), 2.3 (no KAT, no VIDS,
        /// VIDT 4 unknown, 2800) and 9.9, which is not in the .cad.
        /// </summary>
        public static (TerritoryBalance A, TerritoryBalance B) Sections()
        {
            var dataB = new CadRegisterData { Ekatte = "06434", SettlementName = "с. Другово" };
            BalanceFixture.Add(dataB, "06434.2.1", "4", "5", "3", "2800");
            BalanceFixture.Add(dataB, "06434.2.2", "5", "1", "3", "2230");
            BalanceFixture.Add(dataB, "06434.2.3", "", "", "4", "2800");

            var all = new List<PoleStepPiece>(BalanceFixture.Pieces())
            {
                RegisterFixture.Piece("06433.1.1", "Стълб №40", 5.0),
                RegisterFixture.Piece("06434.2.1", "Стълб №40", 9.0),
                RegisterFixture.Piece("06434.2.2", "Стълб №50", 6.0)
            };

            AffectedRegister regA = BalanceFixture.Register(
                BalanceFixture.Areas(), all.Where(p => p.ParcelId.StartsWith("06433.")), BalanceFixture.Data());
            AffectedRegister regB = BalanceFixture.Register(
                new[]
                {
                    RegisterFixture.Areas("06434.2.1", 700.0, 100.0),
                    RegisterFixture.Areas("06434.2.2", 300.0, 50.0),
                    RegisterFixture.Areas("06434.2.3", 400.0, 0.0),
                    RegisterFixture.Areas("06434.9.9", 200.0, 20.0)
                },
                all.Where(p => p.ParcelId.StartsWith("06434.")), dataB);

            TerritoryBalance Build(AffectedRegister register)
            {
                SectionPoles poles = TerritoryBalanceBuilder.PolesOfSection(register, TerritoryBalanceBuilder.WinningParcels(all));
                return TerritoryBalanceBuilder.Build(register, poles.ByParcel, "НОВА ВЛ 110kV", BalanceFixture.Nomenclatures(), poles.DistinctPoles);
            }
            return (Build(regA), Build(regB));
        }

        public static TerritoryBalance Combined()
        {
            var (a, b) = Sections();
            return TerritoryBalanceBuilder.Combine(new[] { a, b }, Title, Subtitle);
        }
    }

    public class MunicipalityBalanceBuilderTests
    {
        private static void AssertRow(BalanceRow row, string group, int parcels, decimal area, decimal restricted, int poles, decimal step, decimal affected, decimal? percent)
        {
            Assert.Equal(group, row.Group);
            Assert.Equal(parcels, row.ParcelCount);
            Assert.Equal(area, row.AreaDka);
            Assert.Equal(restricted, row.RestrictedDka);
            Assert.Equal(poles, row.PoleCount);
            Assert.Equal(step, row.StepDka);
            Assert.Equal(affected, row.AffectedDka);
            Assert.Equal(percent, row.Percent);
        }

        [Fact]
        public void Sections_AreWhatTheCombinationStartsFrom()
        {
            var (a, b) = MunicipalityFixture.Sections();

            AssertRow(a.Tables[0].Total, "Общо:", 6, 7.100m, 1.519m, 2, 0.031m, 1.550m, 100m);
            AssertRow(b.Tables[0].Total, "Общо:", 4, 1.600m, 0.155m, 2, 0.015m, 0.170m, 100m);
        }

        [Fact]
        public void Categories_MergedByKey_SummedExactly_InOrder_WithGroupsOfOneSectionOnly()
        {
            BalanceTable kat = MunicipalityFixture.Combined().Tables[0];

            Assert.Equal(5, kat.Rows.Count);
            AssertRow(kat.Rows[0], "IV категория", 4, 4.300m, 0.936m, 1, 0.014m, 0.950m, 55.23m);   // both землища
            AssertRow(kat.Rows[1], "V категория", 1, 0.300m, 0.044m, 1, 0.006m, 0.050m, 2.91m);     // only 06434
            AssertRow(kat.Rows[2], "VIII категория", 2, 2.700m, 0.574m, 2, 0.026m, 0.600m, 34.88m); // only 06433
            AssertRow(kat.Rows[3], "Без категория", 2, 1.200m, 0.100m, 0, 0m, 0.100m, 5.81m);
            AssertRow(kat.Rows[4], "Няма данни в .cad", 1, 0.200m, 0.020m, 0, 0m, 0.020m, 1.16m);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, kat.Rows.Select(r => r.Number).ToArray());
        }

        [Fact]
        public void Ownership_UnknownCodesAndSpecialGroups_KeepTheirOrder()
        {
            BalanceTable vids = MunicipalityFixture.Combined().Tables[1];

            Assert.Equal(
                new[] { "код 1", "Общинска публична", "Частна", "код 7", "Без код", "Няма данни в .cad" },
                vids.Rows.Select(r => r.Group).ToArray());
            AssertRow(vids.Rows[2], "Частна", 4, 4.500m, 0.986m, 1, 0.014m, 1.000m, 58.14m);

            BalanceTable vidt = MunicipalityFixture.Combined().Tables[2];
            Assert.Equal(
                new[] { "Територия за транспорт", "Земеделска територия", "код 4", "Няма данни в .cad" },
                vidt.Rows.Select(r => r.Group).ToArray());
        }

        [Fact]
        public void TotalRows_AreTheSumOfTheSections_InAllFourTables_AndPercentIs100()
        {
            var (a, b) = MunicipalityFixture.Sections();
            TerritoryBalance m = TerritoryBalanceBuilder.Combine(new[] { a, b }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle);

            for (int t = 0; t < 4; t++)
            {
                AssertRow(m.Tables[t].Total, "Общо:", 10, 8.700m, 1.674m, 4, 0.046m, 1.720m, 100m);
                Assert.Equal(a.Tables[t].GroupHeader, m.Tables[t].GroupHeader);
                Assert.Equal(10, m.Tables[t].Rows.Sum(r => r.ParcelCount));
                Assert.Equal(1.720m, m.Tables[t].Rows.Sum(r => r.AffectedDka));
                foreach (BalanceRow row in m.Tables[t].Rows)
                {
                    Assert.Equal(Math.Round(row.AffectedDka / 1.720m * 100m, 2, MidpointRounding.AwayFromZero), row.Percent);
                }
            }
            Assert.Equal(1, m.NotFoundCount);
        }

        [Fact]
        public void BorderPole_IsCountedOnce_InTheZemlishteOfItsLargestPiece_AndOnceInTheMunicipality()
        {
            var (a, b) = MunicipalityFixture.Sections();
            TerritoryBalance m = TerritoryBalanceBuilder.Combine(new[] { a, b }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle);

            // pole 40: 5 m2 in 06433 (1.1), 9 m2 in 06434 (2.1): counted only in 06434, its step area stays in both
            Assert.Equal(0, a.Tables[1].Rows.Single(r => r.Group == "Частна").PoleCount);
            Assert.Equal(1, b.Tables[1].Rows.Single(r => r.Group == "Частна").PoleCount);
            Assert.Equal(1, m.Tables[1].Rows.Single(r => r.Group == "Частна").PoleCount);
            Assert.Equal(a.Tables[0].Total.PoleCount + b.Tables[0].Total.PoleCount, m.Tables[0].Total.PoleCount);
            Assert.Equal(4, m.Tables[0].Total.PoleCount);   // distinct poles of the run: 10, 11, 40, 50
        }

        [Fact]
        public void BorderPoleBetweenTwoMunicipalities_IsCountedOnceInTheTotals()
        {
            // the two землища stand for two municipalities: a municipality balance is made of one землище each
            var (a, b) = MunicipalityFixture.Sections();
            TerritoryBalance ma = TerritoryBalanceBuilder.Combine(new[] { a }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle);
            TerritoryBalance mb = TerritoryBalanceBuilder.Combine(new[] { b }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle);

            Assert.Equal(2, ma.Tables[0].Total.PoleCount);   // 10, 11
            Assert.Equal(2, mb.Tables[0].Total.PoleCount);   // 40, 50
            Assert.Equal(0.031m, ma.Tables[0].Total.StepDka);
            Assert.Equal(0.015m, mb.Tables[0].Total.StepDka);
        }

        [Fact]
        public void Percent_IsEmpty_WhenTheMunicipalityTotalIsZero()
        {
            var areas = new[] { RegisterFixture.Areas("06433.1.1", 1000.0, 0.0) };
            TerritoryBalance zero = BalanceFixture.Balance(BalanceFixture.Register(areas, new PoleStepPiece[0]), new PoleStepPiece[0]);

            TerritoryBalance m = TerritoryBalanceBuilder.Combine(new[] { zero, zero }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle);

            foreach (BalanceTable table in m.Tables)
            {
                Assert.Null(table.Total.Percent);
                Assert.All(table.Rows, r => Assert.Null(r.Percent));
                Assert.Equal(2, table.Total.ParcelCount);
            }
        }

        [Fact]
        public void OneZemlishte_GivesTheSameNumbers_UnderTheMunicipalityTitle()
        {
            TerritoryBalance section = BalanceFixture.Balance();

            TerritoryBalance m = TerritoryBalanceBuilder.Combine(new[] { section }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle);

            Assert.Equal(MunicipalityFixture.Title, m.Title);
            Assert.Equal(MunicipalityFixture.Subtitle, m.Subtitle);
            for (int t = 0; t < 4; t++)
            {
                Assert.Equal(section.Tables[t].Rows.Count, m.Tables[t].Rows.Count);
                for (int i = 0; i < section.Tables[t].Rows.Count; i++)
                {
                    BalanceRow s = section.Tables[t].Rows[i];
                    AssertRow(m.Tables[t].Rows[i], s.Group, s.ParcelCount, s.AreaDka, s.RestrictedDka, s.PoleCount, s.StepDka, s.AffectedDka, s.Percent);
                }
            }
        }

        [Fact]
        public void TamperedSection_IsFlagged()
        {
            var (a, b) = MunicipalityFixture.Sections();
            b.Tables[2].Rows[0].AreaDka += 0.001m;   // a row no longer adds up to its section's Общо row

            var ex = Assert.Throws<InvalidOperationException>(
                () => TerritoryBalanceBuilder.Combine(new[] { a, b }, MunicipalityFixture.Title, MunicipalityFixture.Subtitle));
            Assert.Contains("Вид територия", ex.Message);
            Assert.Contains(MunicipalityFixture.Subtitle, ex.Message);
        }

        [Fact]
        public void TitleAndSubtitle_OfAMunicipality()
        {
            Assert.Equal("ОБЩИ БАЛАНСИ НА ТЕРИТОРИЯТА НА ЗАСЕГНАТИТЕ ИМОТИ ЗА НОВА ВЛ 110kV",
                TerritoryBalanceBuilder.MunicipalityTitle(" НОВА ВЛ 110kV "));
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЩИНА МЕЗДРА, ОБЛ. ВРАЦА", TerritoryBalanceBuilder.MunicipalitySubtitle("Мездра", "Враца"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЩИНА ЧЕРВЕН БРЯГ", TerritoryBalanceBuilder.MunicipalitySubtitle("Червен бряг", ""));
            Assert.Equal("НА ТЕРИТОРИЯТА НА НЕИЗВЕСТНА ОБЩИНА",
                TerritoryBalanceBuilder.MunicipalitySubtitle(MunicipalityGrouping.UnknownMunicipality, ""));
        }
    }

    public class MunicipalityBalanceExporterTests : TempFolderTest
    {
        [Fact]
        public void SecondWorkbook_HasOneSheetPerMunicipality_WithTheFourTables()
        {
            TerritoryBalance first = MunicipalityFixture.Combined();
            TerritoryBalance second = TerritoryBalanceBuilder.Combine(
                new[] { BalanceFixture.Balance() },
                MunicipalityFixture.Title,
                TerritoryBalanceBuilder.MunicipalitySubtitle("Бета", "Враца"));

            string path = TerritoryBalanceExporter.ExportMunicipalities(
                new List<(string, TerritoryBalance)> { ("общ. Тест", first), ("общ. Бета", second) }, NewDir("m1"));
            Assert.Equal(Path.Combine(Dir, "m1", "Общ_баланс_за_общината.xlsx"), path);

            WorkbookProbe probe = WorkbookProbe.Open(path);
            Assert.Equal(new[] { "общ. Тест", "общ. Бета" }, probe.Sheets.Select(s => s.Name).ToArray());
            Assert.Empty(probe.PrintTitles);

            WorkbookProbe.SheetProbe s1 = probe.Sheets[0];
            Assert.Equal(MunicipalityFixture.Title, s1.Text("A1"));
            Assert.Equal(MunicipalityFixture.Subtitle, s1.Text("A2"));
            Assert.False(s1.HasPane);
            Assert.Empty(s1.BreakIds());

            // table 1 (5 groups): header 4, rows 5-9, Общо 10; table 2 (6 groups) header 13, Общо 20;
            // table 3 (4 groups) header 23, Общо 28; table 4 (4 groups: 2230, 2800, 9999, Няма данни) header 31, Общо 36
            Assert.Equal("Категория земя", s1.Text("B4"));
            Assert.Equal("IV категория", s1.Text("B5"));
            Assert.Equal("Няма данни в .cad", s1.Text("B9"));
            Assert.Equal("Вид собственост", s1.Text("B13"));
            Assert.Equal("Вид територия", s1.Text("B23"));
            Assert.Equal("Начин на трайно ползване", s1.Text("B31"));
            foreach (int total in new[] { 10, 20, 28, 36 })
            {
                Assert.Equal("Общо:", s1.Text($"A{total}"));
                Assert.Contains($"A{total}:B{total}", s1.Merges());
                Assert.Equal(10, s1.Number($"C{total}"));
                Assert.Equal(8.7, s1.Number($"D{total}"));
                Assert.Equal(4, s1.Number($"F{total}"));
                Assert.Equal(1.72, s1.Number($"H{total}"));
                Assert.Equal(100, s1.Number($"I{total}"));
            }
            Assert.Equal(4, s1.Merges().Count);
            Assert.False(s1.Exists("A37"));

            WorkbookProbe.SheetProbe s2 = probe.Sheets[1];
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЩИНА БЕТА, ОБЛ. ВРАЦА", s2.Text("A2"));
            Assert.Equal(4, s2.Merges().Count);
            Assert.Equal(7.1, s2.Number("D8"));
        }
    }
}
