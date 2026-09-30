using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Synthetic data for the register of affected parcels (fake people, made-up numbers).</summary>
    internal static class RegisterFixture
    {
        public const string Ekatte = "06433";
        public const string HeirsId = "7000";
        public const string OwnerA = "0000000001";
        public const string OwnerB = "000123456";
        public const string Tenant = "5555555555";

        public static Nomenclatures Nomenclatures()
        {
            var all = CadRegister.Nomenclatures.Empty();
            all.Vidt.Add("3", "Земеделска територия");
            all.Ntp.Add("2230", "За селскостопански, горски, ведомствен път");
            all.Ntp.Add("2800", "Пасище");
            all.Vids.Add("3", "Общинска публична");
            all.Vids.Add("5", "Частна");
            return all;
        }

        public static OwnershipRight Right(string parcel, string person, string name, string pravoVid = "1", bool heirs = false) =>
            new OwnershipRight
            {
                ParcelId = parcel, PersonId = person, PersonName = name, PravoVid = pravoVid, PersonIsHeirs = heirs,
                DocId1 = "1", DocId2 = "2"
            };

        public static void AddParcel(
            CadRegisterData data, string id, string vids, string kat, string ntp, string mestnost,
            params OwnershipRight[] rights)
        {
            data.Parcels[id] = new CadastralParcel
            {
                Id = id, Vidt = "3", Ntp = ntp, Vids = vids, Kat = kat, MestnostName = mestnost
            };
            if (rights.Length > 0) data.Rights[id] = rights.ToList();
        }

        public static CadRegisterData Register()
        {
            var data = new CadRegisterData { Ekatte = Ekatte, SettlementName = "с. Тестово" };

            // 9.2: a single owner who is FLAG = T (heirs), no pole
            AddParcel(data, "06433.9.2", "3", "4", "2800", "СТРАНАТА",
                Right("06433.9.2", HeirsId, "ФИКТИВЕН НАСЛЕДНИК", heirs: true));

            // 10.1: two owners; the first owner also has a second ownership right (duplicate); a tenant is not an owner
            AddParcel(data, "06433.10.1", "5", "8", "2230", "ТЕСТОВА",
                Right("06433.10.1", OwnerA, "ТЕСТОВ АЛФА"),
                Right("06433.10.1", OwnerB, "\"АГРО\" ЕООД"),
                Right("06433.10.1", OwnerA, "ТЕСТОВ АЛФА"),
                Right("06433.10.1", Tenant, "ФИКТИВЕН АРЕНДАТОР", pravoVid: "3"));

            // 100.7: only a tenant, so no owner; KAT 0; forest subdivisions
            AddParcel(data, "06433.100.7", "3", "0", "2230", "",
                Right("06433.100.7", Tenant, "ФИКТИВЕН АРЕНДАТОР", pravoVid: "3"));
            data.Parcels["06433.100.7"].Subdivisions.Add(new CadSubdivision { Otdel = "45", Podotdel = "а" });
            data.Parcels["06433.100.7"].Subdivisions.Add(new CadSubdivision { Otdel = "46", Podotdel = "б" });
            return data;
        }

        public static RegisterParcelAreas Areas(string id, double drawnSqm, double netSqm) =>
            new RegisterParcelAreas { ParcelId = id, DrawnAreaSqm = drawnSqm, ServitudeNetAreaSqm = netSqm };

        public static PoleStepPiece Piece(string parcel, string pole, double sqm) =>
            new PoleStepPiece { ParcelId = parcel, PoleNumber = pole, PieceAreaSqm = sqm };

        /// <summary>The scenario: 3 parcels (given out of order), pole 114 split over 10.1 and 100.7, pole 113 only in 10.1.</summary>
        public static AffectedRegister Build(
            IEnumerable<RegisterParcelAreas>? parcels = null,
            IEnumerable<PoleStepPiece>? pieces = null,
            CadRegisterData? data = null,
            string project = "НОВА ВЛ 110kV")
        {
            return AffectedParcelsRegisterBuilder.Build(
                parcels ?? new[]
                {
                    Areas("06433.100.7", 1500.0, 400.0),
                    Areas("06433.10.1", 5380.0, 1200.0),
                    Areas("06433.9.2", 800.0, 300.0)
                },
                pieces ?? new[]
                {
                    Piece("06433.10.1", "Стълб №114", 12.0),   // unsorted on purpose
                    Piece("06433.10.1", "Стълб №113", 14.0),
                    Piece("06433.100.7", "Стълб №114", 8.0)   // the pole 114 is split over two parcels
                },
                data ?? Register(),
                Nomenclatures(),
                project,
                "НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ");
        }
    }

    public class AffectedParcelsRegisterBuilderTests
    {
        [Fact]
        public void Titles()
        {
            AffectedRegister r = RegisterFixture.Build();

            Assert.Equal("РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ОТ НОВА ВЛ 110kV", r.Title);
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", r.Subtitle);
        }

        [Fact]
        public void OneBlockPerParcel_SortedNumerically_OwnersOnFollowingRows()
        {
            AffectedRegister r = RegisterFixture.Build();

            // 9.2 before 10.1 before 100.7 (not the text order); the second owner of 10.1 gets its own row
            Assert.Equal(new[] { "06433.9.2", "06433.10.1", "", "06433.100.7" }, r.Rows.Select(x => x.Number).ToArray());
            Assert.Equal(new[] { true, true, false, true }, r.Rows.Select(x => x.IsFirstOfParcel).ToArray());
        }

        [Fact]
        public void Parcel_WithASingleHeirsOwner_HasThePrefixedName()
        {
            AffectedRegister r = RegisterFixture.Build();

            AffectedRegisterRow row = r.Rows[0];
            Assert.Equal("06433.9.2", row.Number);
            Assert.Equal("", row.Subdivisions);
            Assert.Equal("Земеделска територия", row.Vidt);
            Assert.Equal("Пасище", row.Ntp);
            Assert.Equal("СТРАНАТА", row.Mestnost);
            Assert.Equal("IV", row.Category);
            Assert.Equal(0.800, row.AreaDka);
            Assert.Equal(0.300, row.RestrictedDka);
            Assert.Equal(0.500, row.RemainderDka);
            Assert.Equal("", row.PoleNumbers);
            Assert.Null(row.StepDka);           // no pole in this parcel
            Assert.Equal("Общинска публична", row.Vids);
            Assert.Equal("7000", row.PersonId);
            Assert.Equal("н-ци на ФИКТИВЕН НАСЛЕДНИК", row.PersonName);
        }

        [Fact]
        public void TwoPoles_AreListedAscending_AndTheirRoundedPiecesAreSummed()
        {
            AffectedRegister r = RegisterFixture.Build();

            AffectedRegisterRow row = r.Rows[1];
            Assert.Equal("06433.10.1", row.Number);
            Assert.Equal("Стълб №113, Стълб №114", row.PoleNumbers);
            Assert.Equal(0.026, row.StepDka);        // 0.014 + 0.012
            Assert.Equal(5.380, row.AreaDka);
            Assert.Equal(1.200, row.RestrictedDka);
            Assert.Equal(4.154, row.RemainderDka);   // 5.380 - 1.200 - 0.026
            Assert.Equal("VIII", row.Category);
            Assert.Equal("За селскостопански, горски, ведомствен път", row.Ntp);
        }

        [Fact]
        public void TwoOwners_DuplicateRightOfOnePersonIsDropped_TenantIsNotAnOwner()
        {
            AffectedRegister r = RegisterFixture.Build();

            AffectedRegisterRow first = r.Rows[1];
            AffectedRegisterRow second = r.Rows[2];

            Assert.Equal("0000000001", first.PersonId);
            Assert.Equal("ТЕСТОВ АЛФА", first.PersonName);
            Assert.Equal("000123456", second.PersonId);
            Assert.Equal("\"АГРО\" ЕООД", second.PersonName);          // straight quotes kept
            Assert.DoesNotContain(r.Rows, x => x.PersonId == RegisterFixture.Tenant);
            Assert.Equal(4, r.Rows.Count);                               // A once, B once, no tenant row
        }

        [Fact]
        public void OwnersOfTheSameParcel_KeepTheirFirstOrder()
        {
            CadRegisterData data = RegisterFixture.Register();
            data.Rights["06433.10.1"] = new List<OwnershipRight>
            {
                RegisterFixture.Right("06433.10.1", "B2", "ВТОРИ"),
                RegisterFixture.Right("06433.10.1", "A1", "ПЪРВИ"),
                RegisterFixture.Right("06433.10.1", "B2", "ВТОРИ")
            };

            AffectedRegister r = RegisterFixture.Build(data: data);

            Assert.Equal(new[] { "B2", "A1" }, r.Rows.Skip(1).Take(2).Select(x => x.PersonId).ToArray());
        }

        [Fact]
        public void ContinuationRows_HaveColumns1To11Empty_AndTheOwnershipTypeRepeated()
        {
            AffectedRegister r = RegisterFixture.Build();

            AffectedRegisterRow cont = r.Rows[2];
            Assert.Equal("", cont.Number);
            Assert.Equal("", cont.Subdivisions);
            Assert.Equal("", cont.Vidt);
            Assert.Equal("", cont.Ntp);
            Assert.Equal("", cont.Mestnost);
            Assert.Equal("", cont.Category);
            Assert.Null(cont.AreaDka);
            Assert.Null(cont.RestrictedDka);
            Assert.Null(cont.RemainderDka);
            Assert.Equal("", cont.PoleNumbers);
            Assert.Null(cont.StepDka);
            Assert.Equal("Частна", cont.Vids);
        }

        [Fact]
        public void PoleSplitOverTwoParcels_EachParcelGetsItsOwnPiece()
        {
            AffectedRegister r = RegisterFixture.Build();

            Assert.Contains("Стълб №114", r.Rows[1].PoleNumbers);
            Assert.Equal(0.026, r.Rows[1].StepDka);   // 0.014 + 0.012
            Assert.Equal("Стълб №114", r.Rows[3].PoleNumbers);
            Assert.Equal(0.008, r.Rows[3].StepDka);
        }

        [Fact]
        public void ParcelWithoutOwners_IsOneRow_WithColumns12To14Empty_AndIsReported()
        {
            AffectedRegister r = RegisterFixture.Build();

            AffectedRegisterRow row = r.Rows[3];
            Assert.Equal("06433.100.7", row.Number);
            Assert.Equal("", row.Subdivisions);        // Подотдели stay empty even though the .cad has GORIMOTI rows
            Assert.Equal("", row.Category);            // KAT 0
            Assert.Equal(1.500, row.AreaDka);
            Assert.Equal(0.400, row.RestrictedDka);
            Assert.Equal(1.092, row.RemainderDka);     // 1.500 - 0.400 - 0.008
            Assert.Equal("", row.Vids);
            Assert.Equal("", row.PersonId);
            Assert.Equal("", row.PersonName);
            Assert.Equal(new[] { "06433.100.7" }, r.WithoutOwners);
            Assert.Empty(r.NotFound);
        }

        [Fact]
        public void Total_SumsPrintedValues_EachParcelOnce_AndAddsUp()
        {
            AffectedRegister r = RegisterFixture.Build();

            Assert.Equal(7.680, r.TotalAreaDka);          // 0.800 + 5.380 + 1.500
            Assert.Equal(1.900, r.TotalRestrictedDka);    // 0.300 + 1.200 + 0.400
            Assert.Equal(0.034, r.TotalStepDka);          // 0.026 + 0.008
            Assert.Equal(5.746, r.TotalRemainderDka);     // 0.500 + 4.154 + 1.092

            // the total row adds up on paper as well
            Assert.Equal(
                (decimal)r.TotalRemainderDka,
                (decimal)r.TotalAreaDka - (decimal)r.TotalRestrictedDka - (decimal)r.TotalStepDka);
        }

        [Fact]
        public void EveryParcelRow_AddsUpOnPaper()
        {
            var rng = new Random(20260930);
            var areas = new List<RegisterParcelAreas>();
            var pieces = new List<PoleStepPiece>();
            var data = new CadRegisterData { Ekatte = "06433" };
            for (int i = 1; i <= 200; i++)
            {
                string id = "06433." + i + ".1";
                RegisterFixture.AddParcel(data, id, "5", "6", "2230", "М", RegisterFixture.Right(id, "P" + i, "N" + i));
                double drawn = 1000 + rng.NextDouble() * 9000;
                areas.Add(RegisterFixture.Areas(id, drawn, rng.NextDouble() * drawn * 0.3));
                int poles = rng.Next(0, 4);
                for (int p = 0; p < poles; p++) pieces.Add(RegisterFixture.Piece(id, "Стълб №" + (100 + p), rng.NextDouble() * 60));
            }

            AffectedRegister r = RegisterFixture.Build(areas, pieces, data);

            foreach (AffectedRegisterRow row in r.Rows.Where(x => x.IsFirstOfParcel))
            {
                decimal expected = (decimal)row.AreaDka!.Value - (decimal)row.RestrictedDka!.Value - (decimal)(row.StepDka ?? 0.0);
                Assert.Equal(expected, (decimal)row.RemainderDka!.Value);
            }
            Assert.Equal(
                (decimal)r.TotalRemainderDka,
                r.Rows.Where(x => x.IsFirstOfParcel).Sum(x => (decimal)x.RemainderDka!.Value));
        }

        [Fact]
        public void ParcelNotInTheCad_GetsARowWithTheDrawnAreasOnly_AndIsReported()
        {
            AffectedRegister r = RegisterFixture.Build(
                parcels: new[]
                {
                    RegisterFixture.Areas("06433.999.9", 100.0, 50.0),
                    RegisterFixture.Areas("06433.9.2", 800.0, 300.0)
                },
                pieces: new PoleStepPiece[0]);

            Assert.Equal(new[] { "06433.9.2", "06433.999.9" }, r.Rows.Where(x => x.IsFirstOfParcel).Select(x => x.Number).ToArray());
            AffectedRegisterRow row = r.Rows[1];
            Assert.Equal(0.100, row.AreaDka);
            Assert.Equal(0.050, row.RestrictedDka);
            Assert.Equal(0.050, row.RemainderDka);
            Assert.Equal("", row.Vidt);
            Assert.Equal("", row.Category);
            Assert.Equal("", row.PersonId);
            Assert.Equal(new[] { "06433.999.9" }, r.NotFound);
            Assert.Empty(r.WithoutOwners);
            Assert.Equal(0.900, r.TotalAreaDka);
        }

        [Fact]
        public void NegativeRemainder_IsPrintedAsIs_AndReported()
        {
            AffectedRegister r = RegisterFixture.Build(
                parcels: new[] { RegisterFixture.Areas("06433.9.2", 100.0, 90.0) },
                pieces: new[] { RegisterFixture.Piece("06433.9.2", "Стълб №1", 20.0) });

            Assert.Equal(-0.010, r.Rows[0].RemainderDka);
            Assert.Equal(new[] { "06433.9.2" }, r.NegativeRemainder);
        }

        [Fact]
        public void ParcelPickedAsSeveralPolylines_IsOneBlock_WithSummedAreas()
        {
            AffectedRegister r = RegisterFixture.Build(
                parcels: new[]
                {
                    RegisterFixture.Areas("06433.9.2", 500.0, 100.0),
                    RegisterFixture.Areas("06433.9.2", 300.0, 200.0)
                },
                pieces: new PoleStepPiece[0]);

            Assert.Single(r.Rows);
            Assert.Equal(0.800, r.Rows[0].AreaDka);
            Assert.Equal(0.300, r.Rows[0].RestrictedDka);
        }

        [Fact]
        public void OwnershipRightCode_WithLeadingZero_IsStillOwnership()
        {
            CadRegisterData data = RegisterFixture.Register();
            data.Rights["06433.9.2"] = new List<OwnershipRight> { RegisterFixture.Right("06433.9.2", "X", "ИМЕ", pravoVid: "01") };

            AffectedRegister r = RegisterFixture.Build(data: data);

            Assert.Equal("X", r.Rows[0].PersonId);
        }

        [Fact]
        public void UnknownNomenclatureCode_IsKodN()
        {
            CadRegisterData data = RegisterFixture.Register();
            data.Parcels["06433.9.2"].Vids = "99";

            AffectedRegister r = RegisterFixture.Build(data: data);

            Assert.Equal("код 99", r.Rows[0].Vids);
        }

        [Fact]
        public void SubdivisionsFormatter_IsOtdelSlashPodotdel()
        {
            var parcel = new CadastralParcel();
            Assert.Equal("", AffectedParcelsRegisterBuilder.FormatSubdivisions(parcel));

            parcel.Subdivisions.Add(new CadSubdivision { Otdel = "45", Podotdel = "а" });
            parcel.Subdivisions.Add(new CadSubdivision { Otdel = "7", Podotdel = "" });
            parcel.Subdivisions.Add(new CadSubdivision());
            Assert.Equal("45/а, 7", AffectedParcelsRegisterBuilder.FormatSubdivisions(parcel));
        }
    }

    public class AffectedParcelsRegisterExporterTests : IDisposable
    {
        private readonly string _dir;

        public AffectedParcelsRegisterExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private sealed class Sheet_
        {
            public Workbook Workbook = null!;
            public Worksheet Worksheet = null!;
            public Stylesheet Styles = null!;

            public Cell CellAt(string reference) =>
                Worksheet.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);

            public bool Exists(string reference) =>
                Worksheet.Descendants<Cell>().Any(c => c.CellReference!.Value == reference);

            public string? Text(string reference) => CellAt(reference).InlineString?.Text?.Text;

            public bool IsEmpty(string reference)
            {
                Cell c = CellAt(reference);
                return c.InlineString == null && c.CellValue == null;
            }

            public bool IsNumeric(string reference)
            {
                Cell c = CellAt(reference);
                return c.InlineString == null && c.CellValue != null && (c.DataType == null || c.DataType.Value == CellValues.Number);
            }

            public double Number(string reference) =>
                double.Parse(CellAt(reference).CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);

            public CellFormat Format(string reference) =>
                Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)CellAt(reference).StyleIndex!.Value);

            public string? NumberFormatCode(string reference)
            {
                uint id = Format(reference).NumberFormatId?.Value ?? 0U;
                if (id == 1U) return "0";
                if (id == 49U) return "@";
                return Styles.NumberingFormats?.Elements<NumberingFormat>()
                    .FirstOrDefault(n => n.NumberFormatId!.Value == id)?.FormatCode?.Value;
            }

            public Border BorderOf(string reference) =>
                Styles.Borders!.Elements<Border>().ElementAt((int)Format(reference).BorderId!.Value);

            public List<string> Merges() =>
                Worksheet.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();
        }

        private Sheet_ Export(AffectedRegister? register = null)
        {
            string path = AffectedParcelsRegisterExporter.Export(register ?? RegisterFixture.Build(), _dir);
            Assert.Equal(Path.Combine(_dir, "Регистър_на_засегнатите_имоти.xlsx"), path);

            var doc = SpreadsheetDocument.Open(path, false);
            using (doc)
            {
                var wb = doc.WorkbookPart!;
                var sheet = wb.Workbook.Descendants<Sheet>().Single();
                return new Sheet_
                {
                    Workbook = wb.Workbook,
                    Worksheet = ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet,
                    Styles = wb.WorkbookStylesPart!.Stylesheet
                };
            }
        }

        private static readonly string[] Columns = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N" };

        [Fact]
        public void Titles_GroupRow_Headers_AndTheNumbersRow()
        {
            Sheet_ s = Export();

            Assert.Equal("РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ОТ НОВА ВЛ 110kV", s.Text("A1"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 06433, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ", s.Text("A2"));

            Assert.Equal("Стълб", s.Text("J3"));
            Assert.Equal("Собственик", s.Text("L3"));
            foreach (string c in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "K", "M", "N" })
            {
                Assert.True(s.IsEmpty(c + "3"), c + "3");
            }

            string[] headers =
            {
                "Номер на имот", "Подотдели", "Трайно предназначение на територията", "Нов НТП", "Местност", "Категория",
                "Площ на имота в дка", "Площ с ограничение в дка", "Остатък в дка", "Номер на стълба",
                "Площ на стъпката на стълба [дка]", "Вид собственост", "ЕГН/БУЛСТАТ", "Име"
            };
            for (int i = 0; i < 14; i++)
            {
                Assert.Equal(headers[i], s.Text(Columns[i] + "4"));
                Assert.True(s.IsNumeric(Columns[i] + "5"));
                Assert.Equal(i + 1, s.Number(Columns[i] + "5"));
            }
        }

        [Fact]
        public void OnlyTheHeaderGroupCellsAreMerged()
        {
            Sheet_ s = Export();

            Assert.Equal(new[] { "J3:K3", "L3:N3" }, s.Merges().ToArray());
        }

        [Fact]
        public void ParcelRows_ContinuationRow_AndTheOwnersColumns()
        {
            Sheet_ s = Export();

            // row 6: parcel 9.2, heirs
            Assert.Equal("06433.9.2", s.Text("A6"));
            Assert.Equal("IV", s.Text("F6"));
            Assert.Equal(0.8, s.Number("G6"));
            Assert.Equal(0.3, s.Number("H6"));
            Assert.Equal(0.5, s.Number("I6"));
            Assert.True(s.IsEmpty("J6"));
            Assert.True(s.IsEmpty("K6"));
            Assert.Equal("Общинска публична", s.Text("L6"));
            Assert.Equal("7000", s.Text("M6"));
            Assert.Equal("н-ци на ФИКТИВЕН НАСЛЕДНИК", s.Text("N6"));

            // row 7: parcel 10.1, first owner, two poles
            Assert.Equal("06433.10.1", s.Text("A7"));
            Assert.Equal("Стълб №113, Стълб №114", s.Text("J7"));
            Assert.Equal(0.026, s.Number("K7"));
            Assert.Equal(4.154, s.Number("I7"));
            Assert.Equal("Частна", s.Text("L7"));
            Assert.Equal("0000000001", s.Text("M7"));

            // row 8: second owner — columns 1-11 empty, not merged
            foreach (string c in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" })
            {
                Assert.True(s.IsEmpty(c + "8"), c + "8");
            }
            Assert.Equal("Частна", s.Text("L8"));
            Assert.Equal("000123456", s.Text("M8"));
            Assert.Equal("\"АГРО\" ЕООД", s.Text("N8"));

            // row 9: parcel without owners — columns 12-14 empty
            Assert.Equal("06433.100.7", s.Text("A9"));
            Assert.True(s.IsEmpty("B9"));      // Подотдели stay empty for now
            Assert.Equal(0.008, s.Number("K9"));
            Assert.True(s.IsEmpty("L9"));
            Assert.True(s.IsEmpty("M9"));
            Assert.True(s.IsEmpty("N9"));
        }

        [Fact]
        public void TotalRow_HasOboLabel_AndTheSums()
        {
            Sheet_ s = Export();

            Assert.Equal("ОБЩО:", s.Text("A10"));
            foreach (string c in new[] { "B", "C", "D", "E", "F", "J", "L", "M", "N" })
            {
                Assert.True(s.IsEmpty(c + "10"), c + "10");
            }
            Assert.Equal(7.68, s.Number("G10"));
            Assert.Equal(1.9, s.Number("H10"));
            Assert.Equal(5.746, s.Number("I10"));
            Assert.Equal(0.034, s.Number("K10"));
            Assert.False(s.Exists("A11"));
        }

        [Fact]
        public void NumbersAreNumeric_WithFormat0_000_AndIdsAreText()
        {
            Sheet_ s = Export();

            foreach (string cell in new[] { "G6", "H6", "I6", "K7", "G10", "H10", "I10", "K10" })
            {
                Assert.True(s.IsNumeric(cell), cell);
                Assert.Equal("0.000", s.NumberFormatCode(cell));
            }

            Assert.False(s.IsNumeric("M7"));
            Assert.False(s.IsNumeric("M8"));
            Assert.Equal("@", s.NumberFormatCode("M7"));
            Assert.Equal("000123456", s.Text("M8")); // leading zeros survive
            Assert.False(s.IsNumeric("A6"));
        }

        [Fact]
        public void EveryCellOfTheTable_HasThinBorders()
        {
            Sheet_ s = Export();

            for (int row = 3; row <= 10; row++)
            {
                foreach (string c in Columns)
                {
                    Border b = s.BorderOf(c + row);
                    Assert.Equal(BorderStyleValues.Thin, b.LeftBorder!.Style!.Value);
                    Assert.Equal(BorderStyleValues.Thin, b.RightBorder!.Style!.Value);
                    Assert.Equal(BorderStyleValues.Thin, b.BottomBorder!.Style!.Value);
                    Assert.NotNull(b.TopBorder!.Style);
                }
            }
        }

        [Fact]
        public void PageSetup_LandscapeA4_FitToWidth_AndRepeatedHeaderRows()
        {
            Sheet_ s = Export();

            PageSetup page = s.Worksheet.Descendants<PageSetup>().Single();
            Assert.Equal(9U, page.PaperSize!.Value);
            Assert.Equal(DocumentFormat.OpenXml.Spreadsheet.OrientationValues.Landscape, page.Orientation!.Value);
            Assert.Equal(1U, page.FitToWidth!.Value);
            Assert.Equal(0U, page.FitToHeight!.Value);
            Assert.True(s.Worksheet.Descendants<PageSetupProperties>().Single().FitToPage!.Value);

            DefinedName printTitles = s.Workbook.Descendants<DefinedName>().Single();
            Assert.Equal("_xlnm.Print_Titles", printTitles.Name!.Value);
            Assert.Equal("'Регистър'!$3:$5", printTitles.Text);
        }

        [Fact]
        public void File_PassesOpenXmlSchemaValidation()
        {
            string path = AffectedParcelsRegisterExporter.Export(RegisterFixture.Build(), _dir);

            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
                Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            }
        }

        [Fact]
        public void EmptyRegister_WritesTheHeaderAndAZeroTotal()
        {
            AffectedRegister empty = RegisterFixture.Build(parcels: new RegisterParcelAreas[0], pieces: new PoleStepPiece[0]);

            Sheet_ s = Export(empty);

            Assert.Equal("ОБЩО:", s.Text("A6"));
            Assert.Equal(0.0, s.Number("G6"));
        }
    }
}
