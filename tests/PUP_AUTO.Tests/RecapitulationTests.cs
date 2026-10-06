using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Synthetic землища for the recapitulation: 06433 and 06434 (the municipality fixture) and a third one, 06435.</summary>
    internal static class RecapitulationFixture
    {
        public const string Project = "НОВА ВЛ 110kV";

        public static MunicipalityGroup Group(string municipality, string province)
        {
            return new MunicipalityGroup { Municipality = municipality, Province = province, SheetName = "общ. " + municipality };
        }

        /// <summary>06435: two parcels and one pole, so its totals differ from the other two.</summary>
        public static TerritoryBalance Third()
        {
            var data = new CadRegisterData { Ekatte = "06435", SettlementName = "с. Трето" };
            BalanceFixture.Add(data, "06435.3.1", "4", "5", "3", "2800");
            BalanceFixture.Add(data, "06435.3.2", "5", "3", "3", "2230");
            var pieces = new List<PoleStepPiece> { RegisterFixture.Piece("06435.3.1", "Стълб №70", 8.0) };
            AffectedRegister register = BalanceFixture.Register(
                new[] { RegisterFixture.Areas("06435.3.1", 900.0, 120.0), RegisterFixture.Areas("06435.3.2", 400.0, 60.0) },
                pieces, data);
            return BalanceFixture.Balance(register, pieces);
        }

        /// <summary>Municipality "Тест1" (06433 + 06434) and municipality "Тест2" (06435), by default in one област.</summary>
        public static List<(MunicipalityGroup Group, List<TerritoryBalance> Sections)> Groups(string province1 = "Област1", string province2 = "Област1")
        {
            var (a, b) = MunicipalityFixture.Sections();
            TerritoryBalance c = Third();
            return new List<(MunicipalityGroup, List<TerritoryBalance>)>
            {
                (Group("Тест1", province1), new List<TerritoryBalance> { a, b }),
                (Group("Тест2", province2), new List<TerritoryBalance> { c })
            };
        }
    }

    public class RecapitulationBuilderTests
    {
        [Fact]
        public void RowsSubtotalsAndOblastTotal_AreTheSums_WithNumbersAndNamesOnFirstRowsOnly()
        {
            var groups = RecapitulationFixture.Groups();
            RecapitulationSheet sheet = RecapitulationBuilder.Build(groups, RecapitulationFixture.Project, null, null).Single();

            Assert.Equal("обл. Област1", sheet.SheetName);
            Assert.Equal("ОБЩА РЕКАПИТУЛАЦИЯ НА ПЛОЩИТЕ ЗА НОВА ВЛ 110kV", sheet.Title);
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЛ. ОБЛАСТ1", sheet.Subtitle);
            Assert.Equal(2, sheet.Municipalities.Count);

            RecapitulationMunicipality m1 = sheet.Municipalities[0], m2 = sheet.Municipalities[1];
            Assert.Equal(new[] { 1, 2 }, sheet.Municipalities.Select(m => m.Number).ToArray());
            Assert.Equal(2, m1.Rows.Count);
            Assert.Single(m2.Rows);

            // a землище row is the Общо row of its 04 balance
            RecapitulationRow a = m1.Rows[0];
            Assert.Equal("с. Тестово", a.Settlement);
            Assert.Equal("06433", a.Ekatte);
            Assert.Equal(6, a.ParcelCount);
            Assert.Equal(7.100m, a.AreaDka);
            Assert.Equal(1.519m, a.RestrictedDka);
            Assert.Equal(2, a.PoleCount);
            Assert.Equal(0.031m, a.StepDka);
            Assert.Equal(1.550m, a.AffectedDka);

            // number, област and municipality only on the first rows
            Assert.Equal(1, m1.Rows[0].Number);
            Assert.Equal(0, m1.Rows[1].Number);
            Assert.Equal(2, m2.Rows[0].Number);
            Assert.Equal("Област1", m1.Rows[0].Province);
            Assert.Equal(string.Empty, m1.Rows[1].Province);
            Assert.Equal(string.Empty, m2.Rows[0].Province);
            Assert.Equal("Тест1", m1.Rows[0].Municipality);
            Assert.Equal(string.Empty, m1.Rows[1].Municipality);
            Assert.Equal("Тест2", m2.Rows[0].Municipality);

            // subtotals and total are sums
            Assert.Equal("Общо за общ. Тест1:", m1.TotalLabel);
            Assert.Equal(m1.Rows.Sum(r => r.ParcelCount), m1.Total.ParcelCount);
            Assert.Equal(10, m1.Total.ParcelCount);
            Assert.Equal(8.700m, m1.Total.AreaDka);
            Assert.Equal(4, m1.Total.PoleCount);
            Assert.Equal(m1.Total.ParcelCount + m2.Total.ParcelCount, sheet.Total.ParcelCount);
            Assert.Equal(m1.Total.AreaDka + m2.Total.AreaDka, sheet.Total.AreaDka);
            Assert.Equal(m1.Total.RestrictedDka + m2.Total.RestrictedDka, sheet.Total.RestrictedDka);
            Assert.Equal(m1.Total.PoleCount + m2.Total.PoleCount, sheet.Total.PoleCount);
            Assert.Equal(m1.Total.StepDka + m2.Total.StepDka, sheet.Total.StepDka);
            Assert.Equal(m1.Total.AffectedDka + m2.Total.AffectedDka, sheet.Total.AffectedDka);
            Assert.Equal("Общо за обл. Област1:", sheet.TotalLabel);

            // the oblast total takes every municipality (the official document leaves one out)
            Assert.Equal(12, sheet.Total.ParcelCount);
            Assert.Equal(5, sheet.Total.PoleCount);
        }

        [Fact]
        public void SettlementText_ComesFromTheEkatteRegister_WhenItHasTheCode()
        {
            var ekatte = new EkatteRegister();
            ekatte.Add(new EkatteEntry { Code = "06433", Kind = "с.", Name = "Царевец", Municipality = "Тест1", Province = "Област1" });

            RecapitulationSheet sheet = RecapitulationBuilder.Build(RecapitulationFixture.Groups(), RecapitulationFixture.Project, ekatte, null).Single();

            Assert.Equal("с. Царевец", sheet.Municipalities[0].Rows[0].Settlement);
            Assert.Equal("с. Другово", sheet.Municipalities[0].Rows[1].Settlement);   // not in the register: the name in the .cad
        }

        [Fact]
        public void TwoOblasti_GiveTwoSheets_EachWithItsOwnNumbersAndTotal()
        {
            List<RecapitulationSheet> sheets = RecapitulationBuilder.Build(
                RecapitulationFixture.Groups("Област1", "Област2"), RecapitulationFixture.Project, null, null);

            Assert.Equal(new[] { "обл. Област1", "обл. Област2" }, sheets.Select(s => s.SheetName).ToArray());
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЛ. ОБЛАСТ2", sheets[1].Subtitle);

            Assert.Equal(10, sheets[0].Total.ParcelCount);
            Assert.Equal(4, sheets[0].Total.PoleCount);
            Assert.Equal(2, sheets[1].Total.ParcelCount);
            Assert.Equal(1, sheets[1].Total.PoleCount);

            Assert.Equal(1, sheets[1].Municipalities[0].Number);                 // numbering restarts in each област
            Assert.Equal("Област2", sheets[1].Municipalities[0].Rows[0].Province);
            Assert.Equal("Общо за обл. Област2:", sheets[1].TotalLabel);
        }

        [Fact]
        public void MunicipalitySubtotal_EqualsTheCombinedBalance_AndATamperedOneDoesNot()
        {
            var groups = RecapitulationFixture.Groups();
            RecapitulationMunicipality m1 = RecapitulationBuilder.Build(groups, RecapitulationFixture.Project, null, null).Single().Municipalities[0];

            TerritoryBalance combined = TerritoryBalanceBuilder.Combine(groups[0].Sections, "Т", "П");
            Assert.True(RecapitulationBuilder.MatchesCombinedBalance(m1, combined));

            combined.Tables[0].Total.PoleCount++;
            Assert.False(RecapitulationBuilder.MatchesCombinedBalance(m1, combined));
        }

        [Fact]
        public void RouteLength_PerEkatte_RoundedFromUnroundedMetres()
        {
            var metres = new Dictionary<string, decimal> { ["06433"] = 1234.4m, ["06434"] = 1000.4m, ["06435"] = 0.4m };
            RecapitulationSheet sheet = RecapitulationBuilder.Build(RecapitulationFixture.Groups(), RecapitulationFixture.Project, null, metres).Single();

            RecapitulationMunicipality m1 = sheet.Municipalities[0];
            Assert.Equal(1.234m, m1.Rows[0].RouteKm);
            Assert.Equal(1.000m, m1.Rows[1].RouteKm);
            // 2234.8 m = 2.235 km, not 1.234 + 1.000
            Assert.Equal(2.235m, m1.Total.RouteKm);
            // 2235.2 m = 2.235 km
            Assert.Equal(2.235m, sheet.Total.RouteKm);
            Assert.Equal(0.000m, sheet.Municipalities[1].Rows[0].RouteKm);
        }

        [Fact]
        public void RouteLength_ColumnIsEmpty_WhenNoLengthsAreGiven()
        {
            foreach (IReadOnlyDictionary<string, decimal>? none in new IReadOnlyDictionary<string, decimal>?[] { null, new Dictionary<string, decimal>() })
            {
                RecapitulationSheet sheet = RecapitulationBuilder.Build(RecapitulationFixture.Groups(), RecapitulationFixture.Project, null, none).Single();

                Assert.All(sheet.Municipalities.SelectMany(m => m.Rows), r => Assert.Null(r.RouteKm));
                Assert.All(sheet.Municipalities, m => Assert.Null(m.Total.RouteKm));
                Assert.Null(sheet.Total.RouteKm);
            }
        }
    }

    public class RouteLengthsTests
    {
        [Fact]
        public void SplitParameters_AreSorted_WithoutEndsAndRepeats()
        {
            List<double> kept = RouteLengths.SortSplitParameters(new[] { 7.5, 0.0, 2.0, 2.0, 2.00000001, 10.0, 4.25, double.NaN }, 0.0, 10.0);

            Assert.Equal(new[] { 2.0, 4.25, 7.5 }, kept.ToArray());
        }

        [Fact]
        public void SumByEkatte_AddsTheParcelsOfOneEkatte_AndSkipsIdsWithoutOne()
        {
            Dictionary<string, decimal> sums = RouteLengths.SumByEkatte(new[]
            {
                new KeyValuePair<string, double>("06433.1.1", 100.25),
                new KeyValuePair<string, double>("06433.1.2", 50.5),
                new KeyValuePair<string, double>("06434.2.1", 10.0),
                new KeyValuePair<string, double>("без-точка", 99.0)
            });

            Assert.Equal(2, sums.Count);
            Assert.Equal(150.75m, sums["06433"]);
            Assert.Equal(10.0m, sums["06434"]);
        }

        [Fact]
        public void ToKm_RoundsToThreeDecimals_AwayFromZero()
        {
            Assert.Equal(1.654m, RouteLengths.ToKm(1653.5m));
            Assert.Equal(1.653m, RouteLengths.ToKm(1653.4m));
            Assert.Equal(0m, RouteLengths.ToKm(0m));
        }

        [Fact]
        public void PolygonContains_InsideOutsideAndConcave()
        {
            var square = new List<(double, double)> { (0, 0), (10, 0), (10, 10), (0, 10) };
            Assert.True(RouteLengths.PolygonContains(square, 5, 5));
            Assert.False(RouteLengths.PolygonContains(square, 15, 5));
            Assert.False(RouteLengths.PolygonContains(square, 5, -1));

            var u = new List<(double, double)> { (0, 0), (10, 0), (10, 10), (7, 10), (7, 3), (3, 3), (3, 10), (0, 10) };
            Assert.True(RouteLengths.PolygonContains(u, 1, 8));
            Assert.False(RouteLengths.PolygonContains(u, 5, 8));   // in the notch
        }

        [Fact]
        public void ArcPoints_LieOnTheArc_ForAHalfCircle()
        {
            // bulge 1 = a half circle from (0,0) to (10,0), counter-clockwise: centre (5,0), radius 5, below the chord
            List<(double X, double Y)> points = RouteLengths.ArcPoints(0, 0, 10, 0, 1.0, 4);

            Assert.Equal(3, points.Count);
            Assert.All(points, p => Assert.Equal(5.0, Math.Sqrt((p.X - 5) * (p.X - 5) + p.Y * p.Y), 9));
            Assert.All(points, p => Assert.True(p.Y < 0));
            Assert.Equal(5.0, points[1].X, 9);
            Assert.Equal(-5.0, points[1].Y, 9);
            Assert.Empty(RouteLengths.ArcPoints(0, 0, 10, 0, 0.0, 4));
        }
    }

    public class RecapitulationExporterTests : TempFolderTest
    {
        private static (string Format, bool Bold) Style(string path, string reference)
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                Worksheet ws = ((WorksheetPart)wb.GetPartById(wb.Workbook.Descendants<Sheet>().First().Id!)).Worksheet;
                Cell cell = ws.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);
                Stylesheet styles = wb.WorkbookStylesPart!.Stylesheet;
                CellFormat format = styles.CellFormats!.Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
                uint id = format.NumberFormatId?.Value ?? 0U;
                string code = id == 1U ? "0" : id == 0U ? "General"
                    : styles.NumberingFormats!.Elements<NumberingFormat>().Single(n => n.NumberFormatId!.Value == id).FormatCode!.Value!;
                Font font = styles.Fonts!.Elements<Font>().ElementAt((int)format.FontId!.Value);
                return (code, font.Bold != null);
            }
        }

        private string Write(string name, Dictionary<string, decimal>? metres, string province2 = "Област1")
        {
            List<RecapitulationSheet> sheets = RecapitulationBuilder.Build(
                RecapitulationFixture.Groups("Област1", province2), RecapitulationFixture.Project, null, metres);
            return RecapitulationExporter.Export(sheets, NewDir(name));
        }

        [Fact]
        public void Sheet_HasTitles_TwoHeaderRows_ObjectRow_Rows_AndTotals_InTheRightPlaces()
        {
            string path = Write("r1", null);
            Assert.Equal(Path.Combine(Dir, "r1", "Обща_рекапитулация.xlsx"), path);

            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(path).Sheets.Single();
            Assert.Equal("обл. Област1", s.Name);
            Assert.Equal("ОБЩА РЕКАПИТУЛАЦИЯ НА ПЛОЩИТЕ ЗА НОВА ВЛ 110kV", s.Text("A1"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЛ. ОБЛАСТ1", s.Text("A2"));
            Assert.False(s.HasRowOnly(3));

            string[] headers =
            {
                "№ по ред", "Област", "Община", "Землище", "ЕКАТТЕ", "Имоти", "Обща площ на имотите",
                "Площ с ново ограничение", "Брой стъпки на стълбове", "Площ за стъпки на стълбове",
                "Общо засегната площ", "Дължина на трасето"
            };
            for (int c = 0; c < 12; c++) Assert.Equal(headers[c], s.Text($"{(char)('A' + c)}4"));

            // the unit row
            Assert.Equal("[бр]", s.Text("F5"));
            Assert.Equal("[дка]", s.Text("G5"));
            Assert.Equal("[дка]", s.Text("H5"));
            Assert.True(s.IsEmpty("I5"));
            Assert.Equal("[дка]", s.Text("J5"));
            Assert.Equal("[дка]", s.Text("K5"));
            Assert.Equal("[km]", s.Text("L5"));

            Assert.Equal("НОВА ВЛ 110kV", s.Text("A6"));

            // rows 7, 8 (municipality 1), 9 its total, 10 (municipality 2), 11 its total, 12 the oblast total
            Assert.Equal(1, s.Number("A7"));
            Assert.Equal("Област1", s.Text("B7"));
            Assert.Equal("Тест1", s.Text("C7"));
            Assert.Equal("с. Тестово", s.Text("D7"));
            Assert.Equal("06433", s.Text("E7"));
            Assert.Equal(6, s.Number("F7"));
            Assert.Equal(7.1, s.Number("G7"));
            Assert.Equal(1.519, s.Number("H7"));
            Assert.Equal(2, s.Number("I7"));
            Assert.Equal(0.031, s.Number("J7"));
            Assert.Equal(1.55, s.Number("K7"));
            Assert.True(s.IsEmpty("L7"));                       // no axis: the column stays empty

            Assert.True(s.IsEmpty("A8"));
            Assert.True(s.IsEmpty("B8"));
            Assert.True(s.IsEmpty("C8"));
            Assert.Equal("с. Другово", s.Text("D8"));

            Assert.Equal("Общо за общ. Тест1:", s.Text("D9"));
            Assert.Equal(10, s.Number("F9"));
            Assert.Equal(8.7, s.Number("G9"));
            Assert.Equal(4, s.Number("I9"));

            Assert.Equal(2, s.Number("A10"));
            Assert.True(s.IsEmpty("B10"));
            Assert.Equal("Тест2", s.Text("C10"));
            Assert.Equal("Общо за общ. Тест2:", s.Text("D11"));

            Assert.Equal("Общо за обл. Област1:", s.Text("A12"));
            Assert.Equal(12, s.Number("F12"));
            Assert.Equal(5, s.Number("I12"));
            Assert.False(s.Exists("A13"));

            List<string> merges = s.Merges();
            foreach (string merge in new[] { "A1:L1", "A2:L2", "A4:A5", "B4:B5", "C4:C5", "D4:D5", "E4:E5", "I4:I5", "A6:L6", "D9:E9", "D11:E11", "A12:E12" })
            {
                Assert.Contains(merge, merges);
            }
            Assert.Equal(12, merges.Count);
        }

        [Fact]
        public void RouteColumn_HasNumbers_WhenLengthsAreGiven()
        {
            var metres = new Dictionary<string, decimal> { ["06433"] = 1234.4m, ["06434"] = 1000.4m, ["06435"] = 500m };
            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(Write("r2", metres)).Sheets.Single();

            Assert.Equal(1.234, s.Number("L7"));
            Assert.Equal(1.0, s.Number("L8"));
            Assert.Equal(2.235, s.Number("L9"));
            Assert.Equal(0.5, s.Number("L10"));
            Assert.Equal(2.735, s.Number("L12"));
        }

        [Fact]
        public void NumberFormats_AndBoldTotals()
        {
            string path = Write("r3", new Dictionary<string, decimal> { ["06433"] = 1000m });

            Assert.Equal("0", Style(path, "F7").Format);          // counts are integers
            Assert.Equal("0", Style(path, "I7").Format);
            Assert.Equal("0.000", Style(path, "G7").Format);      // areas and kilometres 0.000
            Assert.Equal("0.000", Style(path, "J7").Format);
            Assert.Equal("0.000", Style(path, "L7").Format);
            Assert.False(Style(path, "G7").Bold);

            Assert.True(Style(path, "D9").Bold);                  // the municipality total row
            Assert.Equal("0.000", Style(path, "G9").Format);
            Assert.True(Style(path, "A12").Bold);                 // the oblast total row
            Assert.Equal("0", Style(path, "F12").Format);
            Assert.True(Style(path, "A4").Bold);                  // headers
        }

        [Fact]
        public void TwoOblasti_AreTwoSheets_EachWithItsOwnTitleAndTotals()
        {
            WorkbookProbe probe = WorkbookProbe.Open(Write("r4", null, "Област2"));

            Assert.Equal(new[] { "обл. Област1", "обл. Област2" }, probe.Sheets.Select(x => x.Name).ToArray());
            WorkbookProbe.SheetProbe second = probe.Sheets[1];
            Assert.Equal("НА ТЕРИТОРИЯТА НА ОБЛ. ОБЛАСТ2", second.Text("A2"));
            Assert.Equal("Област2", second.Text("B7"));
            Assert.Equal("Общо за общ. Тест2:", second.Text("D8"));
            Assert.Equal("Общо за обл. Област2:", second.Text("A9"));
            Assert.Equal(2, second.Number("F9"));
        }

        [Fact]
        public void PageSetup_IsA4Landscape_OnePageWide()
        {
            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(Write("r5", null)).Sheets.Single();

            PageSetup setup = s.Worksheet.Descendants<PageSetup>().Single();
            Assert.Equal(DocumentFormat.OpenXml.Spreadsheet.OrientationValues.Landscape, setup.Orientation!.Value);
            Assert.Equal(9U, setup.PaperSize!.Value);
            Assert.Equal(1U, setup.FitToWidth!.Value);
        }
    }
}
