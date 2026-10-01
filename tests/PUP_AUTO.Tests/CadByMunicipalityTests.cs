using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Temp folder with synthetic .cad files (fake EKATTE codes, fake people) and exported workbooks.</summary>
    public abstract class TempFolderTest : IDisposable
    {
        protected readonly string Dir;

        protected TempFolderTest()
        {
            Dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, true); } catch (IOException) { }
        }

        /// <summary>The synthetic .cad of <paramref name="ekatte"/> (the fixture with the EKATTE of its header replaced).</summary>
        protected string WriteCad(string relativePath, string ekatte, string? text = null)
        {
            string path = Path.Combine(Dir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string content = (text ?? SyntheticCad.Text).Replace("EKATTE 06433", "EKATTE " + ekatte);
            File.WriteAllBytes(path, SyntheticCad.Bytes(content));
            return path;
        }

        protected string NewDir(string name)
        {
            string dir = Path.Combine(Dir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public class CadRegisterSetTests : TempFolderTest
    {
        private const string EmptyCad = "HEADER\nVERSION 4.02\nEKATTE 99999\nEND_HEADER\n";

        [Fact]
        public void TwoFilesInSubfolders_LoadAsTwoSettlements()
        {
            WriteCad(@"област А\общ. Алфа\first.cad", "11111");
            WriteCad(@"област Б\second.cad", "22222");

            var warnings = new List<string>();
            CadRegisterSet set = CadRegisterSet.Load(CadRegisterSet.FindCadFiles(Dir), warnings.Add);

            // the synthetic fixture has a few malformed rows on purpose: the reader warnings come through, no file is dropped
            Assert.DoesNotContain(warnings, w => w.Contains("не може да се прочете"));
            Assert.Equal(2, set.Count);
            Assert.Equal(new[] { "11111", "22222" }, set.ByEkatte.Keys.OrderBy(k => k).ToArray());
            Assert.EndsWith("first.cad", set.SourceFiles["11111"]);
            Assert.True(set.TryGetFor("22222.501.1", out CadRegisterData data));
            Assert.Equal("22222", data.Ekatte);
            Assert.False(set.TryGetFor("33333.501.1", out _));
            Assert.False(set.TryGetFor("501.1", out _));     // no EKATTE part
        }

        [Fact]
        public void UnreadableFile_IsSkippedWithAWarningThatNamesIt()
        {
            string good = WriteCad("good.cad", "11111");
            string missing = Path.Combine(Dir, "gone.cad");

            var warnings = new List<string>();
            CadRegisterSet set = CadRegisterSet.Load(new[] { missing, good }, warnings.Add);

            Assert.Equal(1, set.Count);
            Assert.Single(warnings, w => w.Contains("gone.cad") && w.Contains("пропуснат"));
        }

        [Fact]
        public void ZeroParcelFile_IsSkippedWithAWarning()
        {
            string empty = WriteCad("empty.cad", "99999", EmptyCad);
            string good = WriteCad("good.cad", "11111");

            var warnings = new List<string>();
            CadRegisterSet set = CadRegisterSet.Load(new[] { empty, good }, warnings.Add);

            Assert.Equal(new[] { "11111" }, set.ByEkatte.Keys.ToArray());
            Assert.Single(warnings, w => w.Contains("empty.cad") && w.Contains("няма имоти"));
        }

        [Fact]
        public void TemplateCad_IsIgnoredByTheFolderScan_CaseInsensitively()
        {
            WriteCad("ok.cad", "11111");
            WriteCad(@"lib\TemplateC.cad", "22222");
            WriteCad(@"lib\TEMPLATEC.CAD", "33333");

            List<string> files = CadRegisterSet.FindCadFiles(Dir);

            Assert.Single(files);
            Assert.EndsWith("ok.cad", files[0]);
        }

        [Fact]
        public void SameEkatteInTwoFiles_TheNewerFileWins_AndBothAreNamedInTheWarning()
        {
            string older = WriteCad("older.cad", "11111");
            string newer = WriteCad("newer.cad", "11111");
            File.SetLastWriteTime(older, new DateTime(2026, 1, 1));
            File.SetLastWriteTime(newer, new DateTime(2026, 6, 1));

            var warnings = new List<string>();
            CadRegisterSet set = CadRegisterSet.Load(new[] { newer, older }, warnings.Add);

            Assert.Equal(1, set.Count);
            Assert.EndsWith("newer.cad", set.SourceFiles["11111"]);
            Assert.Single(warnings, w => w.Contains("older.cad") && w.Contains("newer.cad"));

            // the order the files are given in does not matter
            set = CadRegisterSet.Load(new[] { older, newer }, _ => { });
            Assert.EndsWith("newer.cad", set.SourceFiles["11111"]);
        }

        [Fact]
        public void PreferNewer_IsStrict_ATieKeepsTheFirstFile()
        {
            var t = new DateTime(2026, 3, 1);

            Assert.True(CadRegisterSet.PreferNewer(t, t.AddSeconds(1)));
            Assert.False(CadRegisterSet.PreferNewer(t, t));
            Assert.False(CadRegisterSet.PreferNewer(t, t.AddSeconds(-1)));
        }

        [Fact]
        public void Warnings_NeverCarryRowContent()
        {
            string text = SyntheticCad.Text + "\nTABLE PRAVA\nD \"unterminated\n";
            string path = WriteCad("a.cad", "11111", text);

            var warnings = new List<string>();
            CadRegisterSet.Load(new[] { path }, warnings.Add);

            Assert.DoesNotContain(warnings, w => w.Contains(SyntheticCad.FakeEgn1) || w.Contains(SyntheticCad.Person1Name));
        }
    }

    public class MunicipalityGroupingTests : TempFolderTest
    {
        private static EkatteRegister Ekatte()
        {
            var register = new EkatteRegister();
            register.Add(new EkatteEntry { Code = "11111", Kind = "с.", Name = "Първо", Municipality = "Алфа", Province = "Плевен" });
            register.Add(new EkatteEntry { Code = "22222", Kind = "с.", Name = "Второ", Municipality = "Алфа", Province = "Плевен" });
            register.Add(new EkatteEntry { Code = "33333", Kind = "гр.", Name = "Трето", Municipality = "Бета", Province = "Враца" });
            register.Add(new EkatteEntry { Code = "44444", Kind = "с.", Name = "Четвърто", Municipality = "Алфа", Province = "Враца" });
            return register;
        }

        private CadRegisterSet SetOf(params string[] ekattes)
        {
            var paths = ekattes.Select(e => WriteCad(e + ".cad", e)).ToList();
            return CadRegisterSet.Load(paths, _ => { });
        }

        private static Dictionary<string, string> Poles(params (string Parcel, string Pole)[] items) =>
            items.ToDictionary(i => i.Parcel, i => i.Pole);

        private static readonly string[] ThreeSettlements =
        {
            "11111.1.1", "11111.1.2", "22222.5.1", "33333.7.1"
        };

        [Fact]
        public void ThreeSettlementsOfTwoMunicipalities_AreTwoSheets_OrderedByTheLowestPole()
        {
            CadRegisterSet set = SetOf("11111", "22222", "33333");
            var poles = Poles(("33333.7.1", "3"), ("22222.5.1", "10"), ("11111.1.1", "27"), ("11111.1.2", "9"));

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(ThreeSettlements, set, Ekatte(), poles);

            Assert.Equal(new[] { "общ. Бета", "общ. Алфа" }, result.Groups.Select(g => g.SheetName).ToArray());   // pole 3 before pole 9
            Assert.Equal(new[] { "33333" }, result.Groups[0].Sections.Select(s => s.Ekatte).ToArray());
            // in Алфа: 11111 holds pole 9 (and 27), 22222 pole 10 — numeric order, not text order
            Assert.Equal(new[] { "11111", "22222" }, result.Groups[1].Sections.Select(s => s.Ekatte).ToArray());
            Assert.Equal("9", result.Groups[1].Sections[0].LowestPole);
            Assert.Equal(new[] { "11111.1.1", "11111.1.2" }, result.Groups[1].Sections[0].ParcelIds.ToArray());
            Assert.Empty(result.IgnoredParcelIds);
            Assert.Empty(result.UnknownEkatte);
            Assert.Empty(result.SectionsWithoutCad);
        }

        [Fact]
        public void SectionsFollowTheRoute_NotTheEkatteOrder()
        {
            CadRegisterSet set = SetOf("11111", "22222");
            var poles = Poles(("22222.5.1", "1"), ("11111.1.1", "50"));

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "22222.5.1" }, set, Ekatte(), poles);

            Assert.Equal(new[] { "22222", "11111" }, result.Groups.Single().Sections.Select(s => s.Ekatte).ToArray());
        }

        [Fact]
        public void SectionsWithoutPoles_ComeAfterTheOnesWithPoles_ByEkatte()
        {
            CadRegisterSet set = SetOf("11111", "22222", "33333");
            var poles = Poles(("22222.5.1", "4"));

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "22222.5.1" }, set, Ekatte(), poles);

            Assert.Equal(new[] { "22222", "11111" }, result.Groups.Single().Sections.Select(s => s.Ekatte).ToArray());
        }

        [Fact]
        public void WithoutPoles_SectionsAndSheetsAreOrderedByEkatte()
        {
            CadRegisterSet set = SetOf("11111", "22222", "33333");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "33333.7.1", "22222.5.1", "11111.1.1" }, set, Ekatte());

            Assert.Equal(new[] { "общ. Алфа", "общ. Бета" }, result.Groups.Select(g => g.SheetName).ToArray());
            Assert.Equal(new[] { "11111", "22222" }, result.Groups[0].Sections.Select(s => s.Ekatte).ToArray());
        }

        [Fact]
        public void SameMunicipalityNameInTwoProvinces_AreTwoGroupsWithUniqueSheetNames()
        {
            CadRegisterSet set = SetOf("11111", "44444");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "44444.2.2" }, set, Ekatte());

            Assert.Equal(2, result.Groups.Count);
            Assert.Equal(new[] { "Плевен", "Враца" }, result.Groups.Select(g => g.Province).ToArray());
            Assert.Equal(new[] { "общ. Алфа", "общ. Алфа (2)" }, result.Groups.Select(g => g.SheetName).ToArray());
        }

        [Fact]
        public void EkatteTitle_IsTheOneOfTheEkatteRegister()
        {
            CadRegisterSet set = SetOf("11111");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(new[] { "11111.1.1" }, set, Ekatte());

            SettlementSection section = result.Groups.Single().Sections.Single();
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ПЪРВО, ЕКАТТЕ 11111, ОБЩ. АЛФА, ОБЛ. ПЛЕВЕН", section.EkatteTitle);
            Assert.Equal("с. Първо", section.DisplayName);
            Assert.True(section.HasCad);
        }

        [Fact]
        public void UnknownEkatte_GoesToTheUnknownMunicipality_WithTheCodesReported()
        {
            CadRegisterSet set = SetOf("11111");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "99999.4.4", "88888.1.1" }, set, Ekatte());

            Assert.Equal(new[] { "общ. Алфа", "Неизвестна община" }, result.Groups.Select(g => g.SheetName).ToArray());
            Assert.Equal(new[] { "88888", "99999" }, result.UnknownEkatte.ToArray());
            MunicipalityGroup unknown = result.Groups[1];
            Assert.Equal(new[] { "88888", "99999" }, unknown.Sections.Select(s => s.Ekatte).ToArray());
            Assert.Equal("НА ТЕРИТОРИЯТА НА ЕКАТТЕ 99999", unknown.Sections[1].EkatteTitle);
        }

        [Fact]
        public void ParcelIdWithoutADot_IsLeftOutOfEveryGroup_AndReported()
        {
            CadRegisterSet set = SetOf("11111");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "NOEKATTE", "42" }, set, Ekatte());

            Assert.Equal(new[] { "NOEKATTE", "42" }, result.IgnoredParcelIds.ToArray());
            Assert.Equal(new[] { "11111.1.1" }, result.Groups.Single().Sections.Single().ParcelIds.ToArray());
        }

        [Fact]
        public void DuplicateParcelIds_AreListedOnce()
        {
            CadRegisterSet set = SetOf("11111");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "11111.1.1", "11111.1.2" }, set, Ekatte());

            Assert.Equal(new[] { "11111.1.1", "11111.1.2" }, result.Groups.Single().Sections.Single().ParcelIds.ToArray());
        }

        [Fact]
        public void SectionWithoutALoadedCad_StillExists_AndEveryParcelIsNotFound()
        {
            CadRegisterSet set = SetOf("11111");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "22222.5.1", "22222.5.2" }, set, Ekatte());

            SettlementSection section = result.Groups.Single().Sections.Single(s => s.Ekatte == "22222");
            Assert.False(section.HasCad);
            Assert.Equal("22222", section.Register.Ekatte);
            Assert.Empty(section.Register.Parcels);
            Assert.Equal(new[] { "22222" }, result.SectionsWithoutCad.Select(s => s.Ekatte).ToArray());
            Assert.Equal("с. Второ", result.SectionsWithoutCad[0].DisplayName);
            Assert.Equal(2, result.SectionsWithoutCad[0].ParcelIds.Count);

            // exactly as today: with an empty register every parcel is a "not found" row
            var drawn = section.ParcelIds.Select(id => new KeyValuePair<string, double>(id, 100.0)).ToList();
            CadControlReport control = CadControlReportBuilder.Build(drawn, section.Register, RegisterFixture.Nomenclatures(), section.EkatteTitle);
            Assert.Equal(new[] { "22222.5.1", "22222.5.2" }, control.NotFound.ToArray());

            AffectedRegister affected = AffectedParcelsRegisterBuilder.Build(
                section.ParcelIds.Select(id => RegisterFixture.Areas(id, 100.0, 10.0)), new PoleStepPiece[0],
                section.Register, RegisterFixture.Nomenclatures(), "ОБЕКТ", section.EkatteTitle);
            Assert.Equal(new[] { "22222.5.1", "22222.5.2" }, affected.NotFound.ToArray());

            PoleStepsRegister steps = PoleStepsRegisterBuilder.Build(
                new[] { RegisterFixture.Piece("22222.5.1", "Стълб №1", 5.0) }, new Dictionary<string, double> { ["22222.5.1"] = 100.0 },
                section.Register, RegisterFixture.Nomenclatures(), "ОБЕКТ", section.EkatteTitle);
            Assert.Equal(new[] { "22222.5.1" }, steps.NotFound.ToArray());
        }

        [Fact]
        public void LowestPoleByParcel_UsesTheNumericOrder()
        {
            Dictionary<string, string> lowest = MunicipalityGrouping.LowestPoleByParcel(new[]
            {
                RegisterFixture.Piece("11111.1.1", "Стълб №10", 1.0),
                RegisterFixture.Piece("11111.1.1", "Стълб №9", 1.0),
                RegisterFixture.Piece("11111.1.1", "Стълб №100", 1.0),
                RegisterFixture.Piece("22222.5.1", "Стълб №7", 1.0)
            });

            Assert.Equal("9", lowest["11111.1.1"]);
            Assert.Equal("7", lowest["22222.5.1"]);
        }

        [Fact]
        public void PolesOfSeveralSectionsOfOneParcelSet_GiveTheSectionTheirLowest()
        {
            CadRegisterSet set = SetOf("11111");

            MunicipalityGroupingResult result = MunicipalityGrouping.Group(
                new[] { "11111.1.1", "11111.1.2" }, set, Ekatte(), Poles(("11111.1.1", "12"), ("11111.1.2", "8")));

            Assert.Equal("8", result.Groups.Single().Sections.Single().LowestPole);
            Assert.Equal("8", result.Groups.Single().LowestPole);
        }
    }

    public class SheetNameTests
    {
        [Fact]
        public void ForbiddenCharacters_AreRemoved()
        {
            Assert.Equal("общ. ABCDEF", MunicipalityGrouping.CleanSheetName("общ. A:B\\C/D?E*[F]"));
        }

        [Fact]
        public void LongNames_AreCutTo31Characters()
        {
            string name = MunicipalityGrouping.CleanSheetName("общ. " + new string('Х', 60));

            Assert.Equal(MunicipalityGrouping.MaxSheetNameLength, name.Length);
            Assert.StartsWith("общ. ХХХ", name);
        }

        [Fact]
        public void EmptyName_BecomesAPlaceholder_AndEdgeApostrophesAreDropped()
        {
            Assert.Equal("Лист", MunicipalityGrouping.CleanSheetName(":*?"));
            Assert.Equal("абв", MunicipalityGrouping.CleanSheetName("'абв'"));
        }

        [Fact]
        public void Collisions_GetUniqueSuffixes_CaseInsensitively_WithinTheLimit()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string longName = "общ. " + new string('Х', 60);

            Assert.Equal("общ. Алфа", MunicipalityGrouping.UniqueSheetName("общ. Алфа", used));
            Assert.Equal("общ. АЛФА (2)", MunicipalityGrouping.UniqueSheetName("общ. АЛФА", used));
            Assert.Equal("общ. Алфа (3)", MunicipalityGrouping.UniqueSheetName("общ. Алфа", used));

            string first = MunicipalityGrouping.UniqueSheetName(longName, used);
            string second = MunicipalityGrouping.UniqueSheetName(longName, used);
            string third = MunicipalityGrouping.UniqueSheetName(longName, used);
            Assert.Equal(31, first.Length);
            Assert.EndsWith(" (2)", second);
            Assert.EndsWith(" (3)", third);
            Assert.All(new[] { first, second, third }, n => Assert.True(n.Length <= 31));
            Assert.Equal(3, new HashSet<string>(new[] { first, second, third }).Count);
        }
    }

    /// <summary>The cells, merges, breaks and defined names of a written workbook.</summary>
    internal sealed class WorkbookProbe
    {
        public sealed class SheetProbe
        {
            public string Name = "";
            public Worksheet Worksheet = null!;

            public Cell? CellAt(string reference) =>
                Worksheet.Descendants<Cell>().SingleOrDefault(c => c.CellReference!.Value == reference);

            public bool Exists(string reference) => CellAt(reference) != null;

            public string? Text(string reference) => CellAt(reference)?.InlineString?.Text?.Text;

            public bool IsEmpty(string reference)
            {
                Cell? c = CellAt(reference);
                return c == null || (c.InlineString == null && c.CellValue == null);
            }

            public double Number(string reference) =>
                double.Parse(CellAt(reference)!.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);

            public List<string> Merges() => Worksheet.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();

            public List<uint> BreakIds() => Worksheet.Descendants<Break>().Select(b => b.Id!.Value).ToList();

            public bool HasPane => Worksheet.Descendants<Pane>().Any();

            public bool HasRowOnly(int row) =>
                Worksheet.Descendants<Row>().Any(r => r.RowIndex!.Value == (uint)row);

            /// <summary>Every cell as "text" or "#number" plus its style index, by reference: for comparing two files.</summary>
            public Dictionary<string, string> Dump() =>
                Worksheet.Descendants<Cell>().ToDictionary(
                    c => c.CellReference!.Value!,
                    c => (c.InlineString?.Text?.Text ?? (c.CellValue != null ? "#" + c.CellValue.Text : "")) + "|" + c.StyleIndex?.Value);
        }

        public List<SheetProbe> Sheets = new List<SheetProbe>();
        public List<DefinedName> PrintTitles = new List<DefinedName>();

        public static WorkbookProbe Open(string path)
        {
            var probe = new WorkbookProbe();
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                foreach (Sheet sheet in wb.Workbook.Descendants<Sheet>())
                {
                    probe.Sheets.Add(new SheetProbe
                    {
                        Name = sheet.Name!.Value!,
                        Worksheet = ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet
                    });
                }
                if (wb.Workbook.DefinedNames != null)
                {
                    probe.PrintTitles.AddRange(wb.Workbook.DefinedNames.Elements<DefinedName>());
                }
            }
            return probe;
        }
    }

    /// <summary>Several sections and sheets, for each of the three exporters.</summary>
    public class StackedSectionsExporterTests : TempFolderTest
    {
        private static CadControlReport Control()
        {
            CadRegisterData data = SyntheticCad.Read(out _);
            var nomenclatures = Nomenclatures.Empty();
            nomenclatures.Ntp.Add("2230", "Нива");
            return CadControlReportBuilder.Build(
                new[]
                {
                    new KeyValuePair<string, double>("06433.501.1", 1201.25),
                    new KeyValuePair<string, double>("06433.501.3", 10.0)
                },
                data, nomenclatures, "НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ");
        }

        private static IReadOnlyList<T> L<T>(params T[] items) => items;

        private static List<(string SheetName, IReadOnlyList<T> Sections)> Sheets<T>(params (string, IReadOnlyList<T>)[] sheets) =>
            sheets.Select(s => (s.Item1, s.Item2)).ToList();

        // ---------------- control report: section 1 = rows 1..5 (title, header, 3 data rows), next free row 6 ----------------

        [Fact]
        public void Control_SecondSection_StartsAfterTwoEmptyRows_WithItsOwnTitleAndHeader_AndABreak()
        {
            string path = CadControlReportExporter.Export(Sheets(("общ. Тест", L(Control(), Control()))), NewDir("c1"));
            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(path).Sheets.Single();

            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A1"));
            Assert.Equal("06433.501.3", s.Text("A5"));                    // last data row of section 1
            Assert.False(s.HasRowOnly(6));
            Assert.False(s.HasRowOnly(7));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A8"));
            Assert.Equal("Имот", s.Text("A9"));
            Assert.Equal("Име", s.Text("N9"));
            Assert.Equal("06433.501.1", s.Text("A10"));
            Assert.Equal("06433.501.3", s.Text("A12"));
            Assert.Equal(1200.5, s.Number("F10"));
            Assert.False(s.Exists("A13"));
            Assert.Equal(new uint[] { 7 }, s.BreakIds().ToArray());      // the break sits above row 8
            Assert.False(s.HasPane);
            Assert.Empty(s.Worksheet.Descendants<AutoFilter>());
            Assert.Empty(WorkbookProbe.Open(path).PrintTitles);
        }

        [Fact]
        public void Control_TwoSheets_NamesOrder_AndPrintTitlesOnTheSingleSectionSheet()
        {
            string path = CadControlReportExporter.Export(
                Sheets(("общ. Алфа", L(Control(), Control())), ("общ. Бета", L(Control()))), NewDir("c2"));
            WorkbookProbe probe = WorkbookProbe.Open(path);

            Assert.Equal(new[] { "общ. Алфа", "общ. Бета" }, probe.Sheets.Select(x => x.Name).ToArray());
            Assert.Equal("Имот", probe.Sheets[0].Text("A9"));            // two sections
            Assert.False(probe.Sheets[1].Exists("A9"));                  // one section
            Assert.False(probe.Sheets[0].HasPane);
            Assert.True(probe.Sheets[1].HasPane);
            Assert.Empty(probe.Sheets[1].BreakIds());

            DefinedName names = probe.PrintTitles.Single();
            Assert.Equal("_xlnm.Print_Titles", names.Name!.Value);
            Assert.Equal(1U, names.LocalSheetId!.Value);                 // the second sheet
            Assert.Equal("'общ. Бета'!$1:$2", names.Text);
            Assert.Equal("A2:N5", probe.Sheets[1].Worksheet.Descendants<AutoFilter>().Single().Reference!.Value);
        }

        [Fact]
        public void Control_OneSectionOneSheet_IsTheSameAsTheOldEntryPoint()
        {
            string oldPath = CadControlReportExporter.Export(Control(), NewDir("c3a"));
            string newPath = CadControlReportExporter.Export(Sheets(("общ. Алфа", L(Control()))), NewDir("c3b"));
            WorkbookProbe old = WorkbookProbe.Open(oldPath), fresh = WorkbookProbe.Open(newPath);

            Assert.Equal(old.Sheets.Single().Dump(), fresh.Sheets.Single().Dump());
            Assert.Equal(old.Sheets.Single().Merges(), fresh.Sheets.Single().Merges());
            Assert.Equal(CadControlReportExporter.SheetName, old.Sheets.Single().Name);
            Assert.Equal("общ. Алфа", fresh.Sheets.Single().Name);
            Assert.True(fresh.Sheets.Single().HasPane);
            Assert.Equal("'общ. Алфа'!$1:$2", fresh.PrintTitles.Single().Text);
            Assert.Equal(0U, fresh.PrintTitles.Single().LocalSheetId!.Value);
        }

        // ---------------- affected register: section 1 = rows 1..10 (5 header rows, 4 data rows, ОБЩО), next free row 11 ----------------

        [Fact]
        public void Affected_SecondSection_StartsAfterTwoEmptyRows_WithMovedMerges_ItsOwnTotal_AndABreak()
        {
            string path = AffectedParcelsRegisterExporter.Export(
                Sheets(("общ. Тест", L(RegisterFixture.Build(), RegisterFixture.Build(project: "ДРУГ ОБЕКТ")))), NewDir("a1"));
            WorkbookProbe probe = WorkbookProbe.Open(path);
            WorkbookProbe.SheetProbe s = probe.Sheets.Single();

            Assert.Equal("ОБЩО:", s.Text("A10"));                         // total of section 1
            Assert.False(s.HasRowOnly(11));
            Assert.False(s.HasRowOnly(12));
            Assert.Equal("РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ОТ НОВА ВЛ 110kV", s.Text("A1"));
            Assert.Equal("РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ОТ ДРУГ ОБЕКТ", s.Text("A13"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A14"));
            Assert.Equal("Стълб", s.Text("J15"));
            Assert.Equal("Собственик", s.Text("L15"));
            Assert.Equal("Номер на имот", s.Text("A16"));
            Assert.Equal("Име", s.Text("N16"));
            for (int c = 0; c < 14; c++)
            {
                Assert.Equal(c + 1, s.Number($"{(char)('A' + c)}17"));      // the numbers row
            }
            Assert.Equal("06433.9.2", s.Text("A18"));
            Assert.Equal("ОБЩО:", s.Text("A22"));
            Assert.Equal(7.68, s.Number("G22"));
            Assert.False(s.Exists("A23"));

            Assert.Equal(new[] { "J3:K3", "L3:N3", "J15:K15", "L15:N15" }, s.Merges().ToArray());
            Assert.Equal(new uint[] { 12 }, s.BreakIds().ToArray());
            Assert.False(s.HasPane);
            Assert.Empty(probe.PrintTitles);
        }

        [Fact]
        public void Affected_TwoSheets_NamesOrder_AndPrintTitlesOnTheSingleSectionSheet()
        {
            string path = AffectedParcelsRegisterExporter.Export(
                Sheets(("общ. Алфа", L(RegisterFixture.Build(), RegisterFixture.Build())), ("общ. Бета", L(RegisterFixture.Build()))),
                NewDir("a2"));
            WorkbookProbe probe = WorkbookProbe.Open(path);

            Assert.Equal(new[] { "общ. Алфа", "общ. Бета" }, probe.Sheets.Select(x => x.Name).ToArray());
            Assert.Equal("Номер на имот", probe.Sheets[0].Text("A16"));
            Assert.False(probe.Sheets[1].Exists("A16"));
            Assert.Equal(4, probe.Sheets[0].Merges().Count);
            Assert.Equal(new[] { "J3:K3", "L3:N3" }, probe.Sheets[1].Merges().ToArray());

            DefinedName names = probe.PrintTitles.Single();
            Assert.Equal(1U, names.LocalSheetId!.Value);
            Assert.Equal("'общ. Бета'!$3:$5", names.Text);
            Assert.True(probe.Sheets[1].HasPane);
            Assert.False(probe.Sheets[0].HasPane);
        }

        [Fact]
        public void Affected_OneSectionOneSheet_IsTheSameAsTheOldEntryPoint()
        {
            string oldPath = AffectedParcelsRegisterExporter.Export(RegisterFixture.Build(), NewDir("a3a"));
            string newPath = AffectedParcelsRegisterExporter.Export(Sheets(("общ. Алфа", L(RegisterFixture.Build()))), NewDir("a3b"));
            WorkbookProbe old = WorkbookProbe.Open(oldPath), fresh = WorkbookProbe.Open(newPath);

            Assert.Equal(old.Sheets.Single().Dump(), fresh.Sheets.Single().Dump());
            Assert.Equal(old.Sheets.Single().Merges(), fresh.Sheets.Single().Merges());
            Assert.Equal(new[] { "J3:K3", "L3:N3" }, fresh.Sheets.Single().Merges().ToArray());
            Assert.Equal("Регистър", old.Sheets.Single().Name);
            Assert.True(fresh.Sheets.Single().HasPane);
            Assert.Equal("'общ. Алфа'!$3:$5", fresh.PrintTitles.Single().Text);
            Assert.Empty(fresh.Sheets.Single().BreakIds());
        }

        // ---------------- pole-steps register: section 1 = rows 1..11 (5 header rows, 5 data rows, Брой), next free row 12 ----------------

        [Fact]
        public void Steps_SecondSection_StartsAfterTwoEmptyRows_WithMovedMerges_ItsOwnCount_AndABreak()
        {
            string path = PoleStepsRegisterExporter.Export(
                Sheets(("общ. Тест", L(StepsFixture.Build(), StepsFixture.Build(project: "ДРУГ ОБЕКТ")))), NewDir("s1"));
            WorkbookProbe probe = WorkbookProbe.Open(path);
            WorkbookProbe.SheetProbe s = probe.Sheets.Single();

            Assert.Equal("Брой: 2", s.Text("A11"));                        // count row of section 1
            Assert.False(s.HasRowOnly(12));
            Assert.False(s.HasRowOnly(13));
            Assert.Equal("РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ НОВА ВЛ 110kV", s.Text("A1"));
            Assert.Equal("РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ ДРУГ ОБЕКТ", s.Text("A14"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A15"));
            Assert.Equal("Стълб", s.Text("A16"));
            Assert.Equal("Собственик", s.Text("J16"));
            Assert.Equal("Номер на стълба", s.Text("A17"));
            Assert.Equal("Име", s.Text("L17"));
            for (int c = 0; c < 12; c++)
            {
                Assert.Equal(c + 1, s.Number($"{(char)('A' + c)}18"));      // the numbers row
            }
            Assert.Equal("Стълб №113", s.Text("A19"));
            Assert.Equal("Брой: 2", s.Text("A24"));
            Assert.Equal(0.034, s.Number("B24"));
            Assert.False(s.Exists("A25"));

            Assert.Equal(new[] { "A3:B3", "J3:L3", "A16:B16", "J16:L16" }, s.Merges().ToArray());
            Assert.Equal(new uint[] { 13 }, s.BreakIds().ToArray());
            Assert.False(s.HasPane);
            Assert.Empty(probe.PrintTitles);
        }

        [Fact]
        public void Steps_TwoSheets_NamesOrder_AndPrintTitlesOnTheSingleSectionSheet()
        {
            string path = PoleStepsRegisterExporter.Export(
                Sheets(("общ. Алфа", L(StepsFixture.Build(), StepsFixture.Build())), ("общ. Бета", L(StepsFixture.Build()))),
                NewDir("s2"));
            WorkbookProbe probe = WorkbookProbe.Open(path);

            Assert.Equal(new[] { "общ. Алфа", "общ. Бета" }, probe.Sheets.Select(x => x.Name).ToArray());
            Assert.Equal("Номер на стълба", probe.Sheets[0].Text("A17"));
            Assert.False(probe.Sheets[1].Exists("A17"));
            Assert.Equal(new[] { "A3:B3", "J3:L3" }, probe.Sheets[1].Merges().ToArray());

            DefinedName names = probe.PrintTitles.Single();
            Assert.Equal(1U, names.LocalSheetId!.Value);
            Assert.Equal("'общ. Бета'!$3:$5", names.Text);
            Assert.True(probe.Sheets[1].HasPane);
            Assert.False(probe.Sheets[0].HasPane);
        }

        [Fact]
        public void Steps_OneSectionOneSheet_IsTheSameAsTheOldEntryPoint()
        {
            string oldPath = PoleStepsRegisterExporter.Export(StepsFixture.Build(), NewDir("s3a"));
            string newPath = PoleStepsRegisterExporter.Export(Sheets(("общ. Алфа", L(StepsFixture.Build()))), NewDir("s3b"));
            WorkbookProbe old = WorkbookProbe.Open(oldPath), fresh = WorkbookProbe.Open(newPath);

            Assert.Equal(old.Sheets.Single().Dump(), fresh.Sheets.Single().Dump());
            Assert.Equal(old.Sheets.Single().Merges(), fresh.Sheets.Single().Merges());
            Assert.Equal(new[] { "A3:B3", "J3:L3" }, fresh.Sheets.Single().Merges().ToArray());
            Assert.Equal("Регистър стъпки", old.Sheets.Single().Name);
            Assert.True(fresh.Sheets.Single().HasPane);
            Assert.Equal("'общ. Алфа'!$3:$5", fresh.PrintTitles.Single().Text);
            Assert.Empty(fresh.Sheets.Single().BreakIds());
        }

        [Fact]
        public void ThreeSections_HaveTwoBreaks_EachAboveItsSection()
        {
            string path = PoleStepsRegisterExporter.Export(
                Sheets(("общ. Тест", L(StepsFixture.Build(), StepsFixture.Build(), StepsFixture.Build()))), NewDir("s4"));
            WorkbookProbe.SheetProbe s = WorkbookProbe.Open(path).Sheets.Single();

            Assert.Equal(new uint[] { 13, 26 }, s.BreakIds().ToArray());   // sections start at rows 1, 14 and 27 (11 rows + 2 empty each)
            Assert.Equal("РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ НОВА ВЛ 110kV", s.Text("A27"));
            Assert.Equal("Брой: 2", s.Text("A37"));
        }
    }
}
