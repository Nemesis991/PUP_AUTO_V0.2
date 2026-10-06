using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Synthetic data for the territory balance (made-up parcels, areas in m²).</summary>
    internal static class BalanceFixture
    {
        public const string Subtitle = "НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ";

        public static Nomenclatures Nomenclatures()
        {
            Nomenclatures all = RegisterFixture.Nomenclatures();
            all.Vidt.Add("2", "Територия за транспорт");
            return all;
        }

        public static void Add(CadRegisterData data, string id, string kat, string vids, string vidt, string ntp)
        {
            RegisterFixture.AddParcel(data, id, vids, kat, ntp, "М");
            data.Parcels[id].Vidt = vidt;
        }

        /// <summary>
        /// 6 parcels: category IV (1.1, 1.2, 1.6), VIII (1.3, 1.4), none (1.5); VIDS 5 / 3 / 7 (unknown); VIDT 3 / 2;
        /// NTP 2800 / 2230 / 9999 (unknown). Parcel 1.3 holds the two poles 10 and 11, the others none.
        /// </summary>
        public static CadRegisterData Data()
        {
            var data = new CadRegisterData { Ekatte = "06433", SettlementName = "с. Тестово" };
            Add(data, "06433.1.1", "4", "5", "3", "2800");
            Add(data, "06433.1.2", "4", "5", "3", "2800");
            Add(data, "06433.1.3", "8", "3", "3", "2230");
            Add(data, "06433.1.4", "8", "7", "2", "2800");
            Add(data, "06433.1.5", "0", "5", "2", "9999");
            Add(data, "06433.1.6", "4", "3", "3", "2230");
            return data;
        }

        public static List<RegisterParcelAreas> Areas() => new List<RegisterParcelAreas>
        {
            RegisterFixture.Areas("06433.1.1", 1000.0, 300.0),
            RegisterFixture.Areas("06433.1.2", 2000.0, 500.0),
            RegisterFixture.Areas("06433.1.3", 1500.0, 400.0),
            RegisterFixture.Areas("06433.1.4", 1200.0, 200.0),
            RegisterFixture.Areas("06433.1.5", 800.0, 100.0),
            RegisterFixture.Areas("06433.1.6", 600.0, 50.0)
        };

        public static List<PoleStepPiece> Pieces() => new List<PoleStepPiece>
        {
            RegisterFixture.Piece("06433.1.3", "Стълб №10", 12.0),
            RegisterFixture.Piece("06433.1.3", "Стълб №11", 14.0)
        };

        public static AffectedRegister Register(
            IEnumerable<RegisterParcelAreas>? areas = null, IEnumerable<PoleStepPiece>? pieces = null,
            CadRegisterData? data = null, Nomenclatures? nomenclatures = null) =>
            AffectedParcelsRegisterBuilder.Build(
                areas ?? Areas(), pieces ?? Pieces(), data ?? Data(), nomenclatures ?? Nomenclatures(), "НОВА ВЛ 110kV", Subtitle);

        public static TerritoryBalance Balance(AffectedRegister register, IEnumerable<PoleStepPiece> allPieces, Nomenclatures? nomenclatures = null) =>
            TerritoryBalanceBuilder.Build(
                register, TerritoryBalanceBuilder.AssignPolesToParcels(allPieces), "НОВА ВЛ 110kV", nomenclatures ?? Nomenclatures());

        public static TerritoryBalance Balance() => Balance(Register(), Pieces());
    }

    public class TerritoryBalanceBuilderTests
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
        public void Titles_AndTheFourTablesInOrder()
        {
            TerritoryBalance b = BalanceFixture.Balance();

            Assert.Equal("БАЛАНСИ НА ТЕРИТОРИЯТА НА ЗАСЕГНАТИТЕ ИМОТИ ЗА НОВА ВЛ 110kV", b.Title);
            Assert.Equal(BalanceFixture.Subtitle, b.Subtitle);
            Assert.Equal(
                new[] { "Категория земя", "Вид собственост", "Вид територия", "Начин на трайно ползване" },
                b.Tables.Select(t => t.GroupHeader).ToArray());
        }

        [Fact]
        public void RegisterRows_CarryTheRawCadCodes()
        {
            AffectedRegister r = BalanceFixture.Register();
            AffectedRegisterRow row = r.Rows.Single(x => x.IsFirstOfParcel && x.Number == "06433.1.4");

            Assert.Equal("8", row.KatCode);
            Assert.Equal("7", row.VidsCode);
            Assert.Equal("2", row.VidtCode);
            Assert.Equal("2800", row.NtpCode);
        }

        [Fact]
        public void Grouping_Order_AndSums_OfAllFourTables()
        {
            TerritoryBalance b = BalanceFixture.Balance();

            // Категория: IV, VIII, Без категория (last)
            BalanceTable kat = b.Tables[0];
            Assert.Equal(3, kat.Rows.Count);
            AssertRow(kat.Rows[0], "IV категория", 3, 3.600m, 0.850m, 0, 0m, 0.850m, 54.84m);
            AssertRow(kat.Rows[1], "VIII категория", 2, 2.700m, 0.574m, 2, 0.026m, 0.600m, 38.71m);
            AssertRow(kat.Rows[2], "Без категория", 1, 0.800m, 0.100m, 0, 0m, 0.100m, 6.45m);

            // Собственост: 3, 5, 7 (7 is not in the nomenclature)
            BalanceTable vids = b.Tables[1];
            Assert.Equal(3, vids.Rows.Count);
            AssertRow(vids.Rows[0], "Общинска публична", 2, 2.100m, 0.424m, 2, 0.026m, 0.450m, 29.03m);
            AssertRow(vids.Rows[1], "Частна", 3, 3.800m, 0.900m, 0, 0m, 0.900m, 58.06m);
            AssertRow(vids.Rows[2], "код 7", 1, 1.200m, 0.200m, 0, 0m, 0.200m, 12.90m);

            // Територия: 2, 3
            BalanceTable vidt = b.Tables[2];
            Assert.Equal(2, vidt.Rows.Count);
            AssertRow(vidt.Rows[0], "Територия за транспорт", 2, 2.000m, 0.300m, 0, 0m, 0.300m, 19.35m);
            AssertRow(vidt.Rows[1], "Земеделска територия", 4, 5.100m, 1.224m, 2, 0.026m, 1.250m, 80.65m);

            // НТП: 2230, 2800, 9999 (unknown: own row with the fallback text)
            BalanceTable ntp = b.Tables[3];
            Assert.Equal(3, ntp.Rows.Count);
            AssertRow(ntp.Rows[0], "За селскостопански, горски, ведомствен път", 2, 2.100m, 0.424m, 2, 0.026m, 0.450m, 29.03m);
            AssertRow(ntp.Rows[1], "Пасище", 3, 4.200m, 1.000m, 0, 0m, 1.000m, 64.52m);
            AssertRow(ntp.Rows[2], "код 9999", 1, 0.800m, 0.100m, 0, 0m, 0.100m, 6.45m);

            foreach (BalanceTable table in b.Tables)
            {
                Assert.Equal(Enumerable.Range(1, table.Rows.Count).ToArray(), table.Rows.Select(r => r.Number).ToArray());
            }
        }

        [Fact]
        public void TotalRows_AreEqualInAllTables_AndEqualTheRegisterTotals()
        {
            AffectedRegister register = BalanceFixture.Register();
            TerritoryBalance b = BalanceFixture.Balance(register, BalanceFixture.Pieces());

            foreach (BalanceTable table in b.Tables)
            {
                AssertRow(table.Total, "Общо:", 6, 7.100m, 1.524m, 2, 0.026m, 1.550m, 100m);
                Assert.Equal(6, table.Rows.Sum(r => r.ParcelCount));
                Assert.Equal(table.Total.AreaDka, table.Rows.Sum(r => r.AreaDka));
                Assert.Equal(table.Total.PoleCount, table.Rows.Sum(r => r.PoleCount));
            }
            Assert.Equal(7.1, register.TotalAreaDka);
            Assert.Equal(1.524, register.TotalRestrictedDka);
            Assert.Equal(0.026, register.TotalStepDka);
            Assert.Equal((decimal)register.TotalAreaDka, b.Tables[0].Total.AreaDka);
            Assert.Equal((decimal)register.TotalRestrictedDka, b.Tables[0].Total.RestrictedDka);
            Assert.Equal((decimal)register.TotalStepDka, b.Tables[0].Total.StepDka);
        }

        [Fact]
        public void DecimalSums_HaveNoFloatDrift()
        {
            // two step pieces of 27 and 9 m2 in two parcels of the same group: 0.027 + 0.009 = 0.036 exactly
            var pieces = new[]
            {
                RegisterFixture.Piece("06433.1.1", "Стълб №1", 27.0),
                RegisterFixture.Piece("06433.1.2", "Стълб №2", 9.0)
            };
            TerritoryBalance b = BalanceFixture.Balance(BalanceFixture.Register(pieces: pieces), pieces);

            Assert.Equal(0.036m, b.Tables[1].Rows.Single(r => r.Group == "Частна").StepDka);
            Assert.Equal(0.036m, b.Tables[0].Total.StepDka);
            Assert.Equal(0.036m, b.Tables[3].Total.StepDka);
        }

        [Fact]
        public void Percent_UsesTheSectionTotal_RoundsToTwoDecimals_AndTheTotalRowIs100()
        {
            TerritoryBalance b = BalanceFixture.Balance();

            foreach (BalanceTable table in b.Tables)
            {
                Assert.Equal(100m, table.Total.Percent);
                foreach (BalanceRow row in table.Rows)
                {
                    Assert.Equal(Math.Round(row.AffectedDka / 1.550m * 100m, 2, MidpointRounding.AwayFromZero), row.Percent);
                }
            }
        }

        [Fact]
        public void Percent_IsEmpty_WhenTheSectionTotalIsZero()
        {
            var areas = new[] { RegisterFixture.Areas("06433.1.1", 1000.0, 0.0), RegisterFixture.Areas("06433.1.2", 500.0, 0.0) };
            TerritoryBalance b = BalanceFixture.Balance(BalanceFixture.Register(areas, new PoleStepPiece[0]), new PoleStepPiece[0]);

            foreach (BalanceTable table in b.Tables)
            {
                Assert.Null(table.Total.Percent);
                Assert.All(table.Rows, r => Assert.Null(r.Percent));
                Assert.Equal(0m, table.Total.AffectedDka);
            }
        }

        [Fact]
        public void NoCategory_IsLast_AndNotInCad_IsLastInEveryTable_WithoutCodes()
        {
            var areas = BalanceFixture.Areas();
            areas.Add(RegisterFixture.Areas("06433.0.9", 400.0, 40.0)); // not in the .cad
            AffectedRegister register = BalanceFixture.Register(areas);
            TerritoryBalance b = BalanceFixture.Balance(register, BalanceFixture.Pieces());

            Assert.Equal(1, b.NotFoundCount);
            foreach (BalanceTable table in b.Tables)
            {
                BalanceRow last = table.Rows[table.Rows.Count - 1];
                Assert.Equal("Няма данни в .cad", last.Group);
                Assert.Equal(1, last.ParcelCount);
                Assert.Equal(0.400m, last.AreaDka);
                Assert.Equal(7, table.Total.ParcelCount);
                Assert.Equal(7.500m, table.Total.AreaDka);
            }
            Assert.Equal("Без категория", b.Tables[0].Rows[b.Tables[0].Rows.Count - 2].Group);
        }

        [Fact]
        public void TwoWholePolesInOneParcel_CountTwo()
        {
            TerritoryBalance b = BalanceFixture.Balance();

            Assert.Equal(2, b.Tables[0].Total.PoleCount);
            Assert.Equal(2, b.Tables[1].Rows.Single(r => r.Group == "Общинска публична").PoleCount);
        }

        [Fact]
        public void SplitPole_CountsOnceInTheLargestPiece_ButTheAreaOfBothPiecesStays()
        {
            // pole 20: 14 m2 in 1.1 (Частна), 18 m2 in 1.2 (Частна too, so move 1.2 to the municipal parcels)
            CadRegisterData data = BalanceFixture.Data();
            data.Parcels["06433.1.2"].Vids = "3";
            var pieces = new[]
            {
                RegisterFixture.Piece("06433.1.1", "Стълб №20", 14.0),
                RegisterFixture.Piece("06433.1.2", "Стълб №20", 18.0)
            };
            TerritoryBalance b = BalanceFixture.Balance(BalanceFixture.Register(pieces: pieces, data: data), pieces);

            BalanceRow privateOwner = b.Tables[1].Rows.Single(r => r.Group == "Частна");
            BalanceRow municipal = b.Tables[1].Rows.Single(r => r.Group == "Общинска публична");
            Assert.Equal(0, privateOwner.PoleCount);
            Assert.Equal(0.014m, privateOwner.StepDka);
            Assert.Equal(1, municipal.PoleCount);
            Assert.Equal(0.018m, municipal.StepDka);
            Assert.Equal(1, b.Tables[0].Total.PoleCount);
            Assert.Equal(0.032m, b.Tables[0].Total.StepDka);
        }

        [Fact]
        public void AssignPoles_EqualPiecesGoToTheSmallerParcelId_NumericallyCompared()
        {
            Dictionary<string, int> poles = TerritoryBalanceBuilder.AssignPolesToParcels(new[]
            {
                RegisterFixture.Piece("06433.1.10", "Стълб №30", 10.0),
                RegisterFixture.Piece("06433.1.9", "Стълб №30", 10.0)
            });

            Assert.Equal(1, poles["06433.1.9"]);
            Assert.False(poles.ContainsKey("06433.1.10"));
        }

        [Fact]
        public void AssignPoles_PiecesOfOnePoleInOneParcelAreAddedFirst_AndThePrefixIsIgnored()
        {
            Dictionary<string, int> poles = TerritoryBalanceBuilder.AssignPolesToParcels(new[]
            {
                RegisterFixture.Piece("06433.1.1", "Стълб №5", 6.0),
                RegisterFixture.Piece("06433.1.1", "5", 6.0),          // the same pole, the same parcel: 12 m2 together
                RegisterFixture.Piece("06433.1.2", "Стълб №5", 9.0)
            });

            Assert.Equal(1, poles["06433.1.1"]);
            Assert.False(poles.ContainsKey("06433.1.2"));
        }

        /// <summary>Two землища: 06433 (the fixture's 6 parcels, pole 41 whole in 1.2) and 06434 (two parcels). Pole 40 is on the border (larger piece in 06434).</summary>
        private static (TerritoryBalance A, TerritoryBalance B, PoleStepPiece[] Pieces) TwoZemlishta(params PoleStepPiece[] extra)
        {
            var dataB = new CadRegisterData { Ekatte = "06434", SettlementName = "с. Другово" };
            BalanceFixture.Add(dataB, "06434.2.1", "4", "5", "3", "2800");
            BalanceFixture.Add(dataB, "06434.2.2", "4", "3", "3", "2800");

            var all = new List<PoleStepPiece>
            {
                RegisterFixture.Piece("06433.1.1", "Стълб №40", 5.0),   // the small piece of the border pole is in 06433
                RegisterFixture.Piece("06434.2.1", "Стълб №40", 9.0),   // the larger one in 06434
                RegisterFixture.Piece("06433.1.2", "Стълб №41", 12.0)
            };
            all.AddRange(extra);

            AffectedRegister regA = BalanceFixture.Register(
                BalanceFixture.Areas(), all.Where(p => p.ParcelId.StartsWith("06433.")), BalanceFixture.Data());
            AffectedRegister regB = BalanceFixture.Register(
                new[] { RegisterFixture.Areas("06434.2.1", 700.0, 100.0), RegisterFixture.Areas("06434.2.2", 300.0, 50.0) },
                all.Where(p => p.ParcelId.StartsWith("06434.")), dataB);

            TerritoryBalance Build(AffectedRegister register)
            {
                SectionPoles poles = TerritoryBalanceBuilder.PolesOfSection(register, TerritoryBalanceBuilder.WinningParcels(all));
                return TerritoryBalanceBuilder.Build(register, poles.ByParcel, "ОБЕКТ", BalanceFixture.Nomenclatures(), poles.DistinctPoles);
            }
            return (Build(regA), Build(regB), all.ToArray());
        }

        [Fact]
        public void PoleOnTheBorderOfTwoZemlishta_IsCountedOnce_InTheZemlishteOfItsLargerPiece_ButKeepsStepAreaInBoth()
        {
            var (a, b, pieces) = TwoZemlishta();

            // pole 40: 5 m2 in 06433, 9 m2 in 06434 -> counted only in 06434; pole 41 only in 06433
            Assert.Equal(1, a.Tables[0].Total.PoleCount);
            Assert.Equal(1, a.Tables[1].Rows.Single(r => r.Group == "Частна").PoleCount);   // 41 (1.2); 40 is not counted at 1.1
            Assert.Equal(1, b.Tables[0].Total.PoleCount);
            Assert.Equal(1, b.Tables[1].Rows.Single(r => r.Group == "Частна").PoleCount);   // 40 (2.1)
            Assert.Equal(0, b.Tables[1].Rows.Single(r => r.Group == "Общинска публична").PoleCount);
            // the step AREA of each землище is still its own pieces, including the 5 m2 of pole 40 in 06433
            Assert.Equal(0.017m, a.Tables[0].Total.StepDka);
            Assert.Equal(0.009m, b.Tables[0].Total.StepDka);
            // the sum over землища is the number of distinct poles of the run
            Assert.Equal(2, a.Tables[0].Total.PoleCount + b.Tables[0].Total.PoleCount);
            Assert.Equal(2, pieces.Select(p => p.PoleNumber).Distinct().Count());
        }

        [Fact]
        public void PoleAcrossTwoParcelsOfOneZemlishte_IsStillCountedOnce_ForTheLargerPiece()
        {
            // pole 42: 4 m2 in 06433.1.1 (Частна), 10 m2 in 06433.1.3 (Общинска публична); and a small piece in the other землище
            var (a, b, _) = TwoZemlishta(
                RegisterFixture.Piece("06433.1.1", "Стълб №42", 4.0),
                RegisterFixture.Piece("06433.1.3", "Стълб №42", 10.0),
                RegisterFixture.Piece("06434.2.2", "Стълб №42", 1.0));

            Assert.Equal(2, a.Tables[0].Total.PoleCount);                                                 // 41 and 42; 40 is counted in 06434
            Assert.Equal(1, a.Tables[1].Rows.Single(r => r.Group == "Общинска публична").PoleCount);     // 42 -> 1.3
            Assert.Equal(1, a.Tables[1].Rows.Single(r => r.Group == "Частна").PoleCount);                // 41; not 42
            Assert.Equal(1, b.Tables[0].Total.PoleCount);                                                 // 40 only; 42's 1 m2 piece is not counted
            Assert.Equal(0, b.Tables[1].Rows.Single(r => r.Group == "Общинска публична").PoleCount);
        }

        [Fact]
        public void SumOfPolesOverAllZemlishta_EqualsTheDistinctPolesOfTheRun_InEveryTable()
        {
            var (a, b, pieces) = TwoZemlishta(
                RegisterFixture.Piece("06433.1.3", "Стълб №42", 10.0),
                RegisterFixture.Piece("06434.2.2", "Стълб №43", 7.0));

            Assert.Equal(2, a.DistinctPoles);   // 41, 42
            Assert.Equal(2, b.DistinctPoles);   // 40, 43
            foreach (TerritoryBalance balance in new[] { a, b })
            {
                Assert.True(balance.PoleCountsAgree, balance.Subtitle);
                Assert.All(balance.Tables, table => Assert.Equal(balance.DistinctPoles, table.Total.PoleCount));
            }
            int distinct = pieces.Select(p => p.PoleNumber).Distinct().Count();
            Assert.Equal(4, distinct);
            for (int t = 0; t < 4; t++) Assert.Equal(distinct, a.Tables[t].Total.PoleCount + b.Tables[t].Total.PoleCount);
        }

        [Fact]
        public void PoleCountCheck_FlagsAMismatch()
        {
            TerritoryBalance b = BalanceFixture.Balance();
            Assert.True(b.PoleCountsAgree);

            b.DistinctPoles = 3;                      // two poles are counted, three claimed
            Assert.False(b.PoleCountsAgree);
        }
    }

    public class TerritoryBalanceExporterTests : TempFolderTest
    {
        private static IReadOnlyList<T> L<T>(params T[] items) => items;

        private static string FormatCode(string path, string reference)
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                Worksheet ws = ((WorksheetPart)wb.GetPartById(wb.Workbook.Descendants<Sheet>().First().Id!)).Worksheet;
                Cell cell = ws.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);
                Stylesheet styles = wb.WorkbookStylesPart!.Stylesheet;
                CellFormat format = styles.CellFormats!.Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
                uint id = format.NumberFormatId?.Value ?? 0U;
                if (id == 1U) return "0";
                return styles.NumberingFormats!.Elements<NumberingFormat>().Single(n => n.NumberFormatId!.Value == id).FormatCode!.Value!;
            }
        }

        private static (bool Bold, bool Medium) TotalStyle(string path, string reference)
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                Worksheet ws = ((WorksheetPart)wb.GetPartById(wb.Workbook.Descendants<Sheet>().First().Id!)).Worksheet;
                Cell cell = ws.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);
                Stylesheet styles = wb.WorkbookStylesPart!.Stylesheet;
                CellFormat format = styles.CellFormats!.Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
                Font font = styles.Fonts!.Elements<Font>().ElementAt((int)format.FontId!.Value);
                Border border = styles.Borders!.Elements<Border>().ElementAt((int)format.BorderId!.Value);
                return (font.Bold != null, border.TopBorder!.Style!.Value == BorderStyleValues.Medium);
            }
        }

        [Fact]
        public void OneSection_TitlesTablesHeadersAndTotals()
        {
            string path = TerritoryBalanceExporter.Export(BalanceFixture.Balance(), NewDir("b1"));
            Assert.Equal(Path.Combine(Dir, "b1", "Баланси_на_територията.xlsx"), path);

            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(path).Sheets.Single();
            Assert.Equal("Баланси", s.Name);
            Assert.Equal("БАЛАНСИ НА ТЕРИТОРИЯТА НА ЗАСЕГНАТИТЕ ИМОТИ ЗА НОВА ВЛ 110kV", s.Text("A1"));
            Assert.Equal(BalanceFixture.Subtitle, s.Text("A2"));
            Assert.False(s.HasRowOnly(3));

            // headers at rows 4, 11, 18, 24; groups 3 / 3 / 2 / 3; totals 8, 15, 21, 28; two empty rows between tables
            int[] headerRows = { 4, 11, 18, 24 };
            int[] totalRows = { 8, 15, 21, 28 };
            string[] groupHeaders = { "Категория земя", "Вид собственост", "Вид територия", "Начин на трайно ползване" };
            string[] others =
            {
                "№", null!, "Имоти бр.", "Обща площ на имотите в дка", "Обща площ с ограничение в дка",
                "Стъпки на стълбове бр.", "Площ на стъпките в дка", "Засегната площ в дка", "Площ %"
            };
            for (int t = 0; t < 4; t++)
            {
                Assert.Equal(groupHeaders[t], s.Text($"B{headerRows[t]}"));
                for (int c = 0; c < 9; c++)
                {
                    if (others[c] != null) Assert.Equal(others[c], s.Text($"{(char)('A' + c)}{headerRows[t]}"));
                }
                Assert.Equal("Общо:", s.Text($"A{totalRows[t]}"));
                Assert.Contains($"A{totalRows[t]}:B{totalRows[t]}", s.Merges());
                if (t < 3)
                {
                    Assert.False(s.HasRowOnly(totalRows[t] + 1));
                    Assert.False(s.HasRowOnly(totalRows[t] + 2));
                    Assert.Equal(totalRows[t] + 3, headerRows[t + 1]);
                }
            }
            Assert.Equal(4, s.Merges().Count);
            Assert.False(s.HasRowOnly(29));

            // table 1 content
            Assert.Equal(1, s.Number("A5"));
            Assert.Equal("IV категория", s.Text("B5"));
            Assert.Equal(3, s.Number("C5"));
            Assert.Equal(3.6, s.Number("D5"));
            Assert.Equal(0.85, s.Number("E5"));
            Assert.Equal(0, s.Number("F5"));
            Assert.Equal(0, s.Number("G5"));
            Assert.Equal(0.85, s.Number("H5"));
            Assert.Equal(54.84, s.Number("I5"));
            Assert.Equal("Без категория", s.Text("B7"));
            Assert.Equal(6, s.Number("C8"));
            Assert.Equal(7.1, s.Number("D8"));
            Assert.Equal(2, s.Number("F8"));
            Assert.Equal(0.026, s.Number("G8"));
            Assert.Equal(1.55, s.Number("H8"));
            Assert.Equal(100, s.Number("I8"));

            // all four totals equal
            for (int t = 1; t < 4; t++)
            {
                foreach (char col in "CDEFGHI")
                {
                    Assert.Equal(s.Number($"{col}8"), s.Number($"{col}{totalRows[t]}"));
                }
            }

            // formats
            Assert.Equal("0", FormatCode(path, "C5"));
            Assert.Equal("0.000", FormatCode(path, "D5"));
            Assert.Equal("0.000", FormatCode(path, "G5"));
            Assert.Equal("0.000", FormatCode(path, "H5"));
            Assert.Equal("0", FormatCode(path, "F5"));
            Assert.Equal("0.00", FormatCode(path, "I5"));
            Assert.Equal("0.000", FormatCode(path, "D8"));
            Assert.Equal("0.00", FormatCode(path, "I8"));
            Assert.Equal((true, true), TotalStyle(path, "A8"));
            Assert.Equal((true, true), TotalStyle(path, "D8"));
            Assert.Equal((false, false), TotalStyle(path, "D5"));

            Assert.False(s.HasPane);
            Assert.Empty(WorkbookProbe.Open(path).PrintTitles);
        }

        [Fact]
        public void TotalPercent_IsEmpty_WhenTheSectionTotalIsZero()
        {
            var areas = new[] { RegisterFixture.Areas("06433.1.1", 1000.0, 0.0) };
            TerritoryBalance zero = BalanceFixture.Balance(BalanceFixture.Register(areas, new PoleStepPiece[0]), new PoleStepPiece[0]);

            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(TerritoryBalanceExporter.Export(zero, NewDir("b2"))).Sheets.Single();

            Assert.True(s.IsEmpty("I5"));
            Assert.True(s.IsEmpty("I6"));
        }

        [Fact]
        public void TwoSections_InOneSheet_SecondStartsAfterTwoEmptyRows_WithShiftedMerges_AndABreak()
        {
            string path = TerritoryBalanceExporter.Export(
                new[] { ("общ. Тест", L(BalanceFixture.Balance(), BalanceFixture.Balance())) }.ToList(), NewDir("b3"));
            WorkbookProbe probe = WorkbookProbe.Open(path);
            WorkbookProbe.SheetProbe s = probe.Sheets.Single();

            Assert.Equal("общ. Тест", s.Name);
            Assert.Equal("Общо:", s.Text("A28"));                  // last total of section 1
            Assert.False(s.HasRowOnly(29));
            Assert.False(s.HasRowOnly(30));
            Assert.Equal("БАЛАНСИ НА ТЕРИТОРИЯТА НА ЗАСЕГНАТИТЕ ИМОТИ ЗА НОВА ВЛ 110kV", s.Text("A31"));
            Assert.Equal(BalanceFixture.Subtitle, s.Text("A32"));
            Assert.Equal("Категория земя", s.Text("B34"));        // header: 31 + 3
            Assert.Equal("Общо:", s.Text("A58"));                  // 28 + 30
            Assert.Equal(7.1, s.Number("D58"));
            Assert.False(s.Exists("A59"));

            Assert.Equal(8, s.Merges().Count);
            Assert.Contains("A38:B38", s.Merges());
            Assert.Contains("A58:B58", s.Merges());
            Assert.Equal(new uint[] { 30 }, s.BreakIds().ToArray());
            Assert.False(s.HasPane);
            Assert.Empty(probe.PrintTitles);
        }

        [Fact]
        public void PageSetup_IsA4Portrait_OnePageWide()
        {
            string path = TerritoryBalanceExporter.Export(BalanceFixture.Balance(), NewDir("b4"));
            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(path).Sheets.Single();

            PageSetup setup = s.Worksheet.Descendants<PageSetup>().Single();
            Assert.Equal(OrientationValues.Portrait, setup.Orientation!.Value);
            Assert.Equal(9U, setup.PaperSize!.Value);
            Assert.Equal(1U, setup.FitToWidth!.Value);
        }

        [Fact]
        public void RegisterExporter_StillWritesLandscape()
        {
            string path = AffectedParcelsRegisterExporter.Export(RegisterFixture.Build(), NewDir("b5"));
            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(path).Sheets.Single();

            Assert.Equal(OrientationValues.Landscape, s.Worksheet.Descendants<PageSetup>().Single().Orientation!.Value);
        }
    }
}
