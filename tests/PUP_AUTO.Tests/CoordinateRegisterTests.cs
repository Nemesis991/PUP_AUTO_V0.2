using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PUP_AUTO.CadRegister;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Geometry;
using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    internal static class CoordinateFixture
    {
        public const string Title78135 = "НА ТЕРИТОРИЯТА НА С. ТЕСТОВО, ЕКАТТЕ 78135, ОБЩ. ТЕСТ, ОБЛ. ТЕСТ";

        /// <summary>A square footprint around (cx, cy), clockwise in map view from its north-west corner.</summary>
        public static List<(double X, double Y)> Square(double cx, double cy, double side)
        {
            double h = side / 2;
            return new List<(double, double)> { (cx - h, cy + h), (cx + h, cy + h), (cx + h, cy - h), (cx - h, cy - h) };
        }

        public static PoleCorners Pole(string number, double cx, double cy, double side = 7.42) =>
            new PoleCorners { PoleNumber = number, Corners = Square(cx, cy, side), LabelRotation = 0.5 };

        /// <summary>Poles 2, 3, 1 (unsorted); pole 3 stands on the border with most of it in 78135.84.442.</summary>
        public static (List<PoleCorners> Poles, Dictionary<string, string> Winners) Run()
        {
            var poles = new List<PoleCorners>
            {
                Pole("Стълб №2", 362990, 4780400),
                Pole("3", 363010, 4780500),
                Pole("1", 362978.79, 4780339.391)
            };
            var pieces = new List<PoleStepPiece>
            {
                new PoleStepPiece { PoleNumber = "Стълб №2", ParcelId = "78135.84.34", PieceAreaSqm = 30 },
                new PoleStepPiece { PoleNumber = "1", ParcelId = "78135.84.34", PieceAreaSqm = 55 },
                new PoleStepPiece { PoleNumber = "3", ParcelId = "78135.84.442", PieceAreaSqm = 20 },
                new PoleStepPiece { PoleNumber = "3", ParcelId = "85000.10.5", PieceAreaSqm = 15 }
            };
            return (poles, TerritoryBalanceBuilder.WinningParcels(pieces));
        }

        public static CoordinateRegister Build78135()
        {
            var (poles, winners) = Run();
            return CoordinateRegisterBuilder.Build(poles, winners, new[] { "78135.84.34", "78135.84.442" }, "НОВА ВЛ 110kV", Title78135);
        }
    }

    public class CoordinateRegisterBuilderTests
    {
        [Fact]
        public void PolesAreSortedByNumber_AndABorderPoleIsListedOnceWhereItsLargerPieceIs()
        {
            var (poles, winners) = CoordinateFixture.Run();

            CoordinateRegister tsarevets = CoordinateFixture.Build78135();
            CoordinateRegister other = CoordinateRegisterBuilder.Build(poles, winners, new[] { "85000.10.5" }, "НОВА ВЛ 110kV", "X");

            Assert.Equal(new[] { "1", "2", "3" }, tsarevets.Blocks.Select(b => b.PoleNumber));
            Assert.Equal(new[] { "84.34", "84.34", "84.442" }, tsarevets.Blocks.Select(b => b.ParcelLabel));
            Assert.Empty(other.Blocks);
            Assert.Equal("КООРДИНАТЕН РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ЗА НОВА ВЛ 110kV", tsarevets.Title);
            Assert.Equal(CoordinateFixture.Title78135, tsarevets.Subtitle);
            Assert.Equal("Стълб №1, попадащ в имот 84.34", CoordinateRegisterBuilder.BlockTitle(tsarevets.Blocks[0]));
        }

        [Fact]
        public void Coordinates_AreInGeodeticOrder_XNorthIsTheDrawingsY()
        {
            CoordinateRegisterBlock pole1 = CoordinateFixture.Build78135().Blocks[0];

            Assert.Equal(4780339.391, pole1.Centre.North, 6);
            Assert.Equal(362978.790, pole1.Centre.East, 6);
            Assert.Equal("1", pole1.Centre.Label);

            // Corner 1 is the north-west corner of the square: drawing (X - 3.71, Y + 3.71)
            Assert.Equal("1-1", pole1.Corners[0].Label);
            Assert.Equal(4780339.391 + 3.71, pole1.Corners[0].North, 6);
            Assert.Equal(362978.79 - 3.71, pole1.Corners[0].East, 6);
        }

        [Fact]
        public void Centre_IsTheMeanOfTheCorners_AndTheAreaIsTheWholeFootprint()
        {
            var corners = new List<(double X, double Y)> { (0, 10), (6, 10), (8, 2), (0, 0) };   // clockwise, not a square

            (double x, double y) = CoordinateRegisterBuilder.Centre(corners);

            Assert.Equal(3.5, x, 9);
            Assert.Equal(5.5, y, 9);
            Assert.Equal("Площ на стъпката: 55.1кв.м", CoordinateRegisterBuilder.AreaText(7.42 * 7.42));
            Assert.Equal(55.0564, CoordinateFixture.Build78135().Blocks[0].AreaSqm, 6);
            Assert.Equal("Площ на стъпката: 36.1кв.м", CoordinateRegisterBuilder.AreaText(36.05));   // half away from zero
        }

        [Fact]
        public void CounterClockwiseCorners_AreReversedKeepingVertexOneFirst()
        {
            List<(double X, double Y)> clockwise = CoordinateFixture.Square(100, 200, 6);
            var counter = new List<(double X, double Y)> { clockwise[0], clockwise[3], clockwise[2], clockwise[1] };

            List<(double X, double Y)> ordered = CoordinateRegisterBuilder.Clockwise(counter, out bool reversed);
            List<(double X, double Y)> unchanged = CoordinateRegisterBuilder.Clockwise(clockwise, out bool notReversed);

            Assert.True(reversed);
            Assert.Equal(clockwise, ordered);
            Assert.False(notReversed);
            Assert.Equal(clockwise, unchanged);

            var poles = new List<PoleCorners> { new PoleCorners { PoleNumber = "7", Corners = counter } };
            var winners = new Dictionary<string, string> { ["7"] = "78135.1.1" };
            CoordinateRegister register = CoordinateRegisterBuilder.Build(poles, winners, new[] { "78135.1.1" }, "X", "Y");
            Assert.Equal(new[] { "7" }, register.Reversed);
            Assert.Equal(clockwise[0].Y, register.Blocks[0].Corners[0].North, 9);
        }

        [Fact]
        public void AFootprintWithFiveCorners_IsPrintedWhole_AndReported()
        {
            var five = new List<(double X, double Y)> { (0, 10), (5, 12), (10, 10), (10, 0), (0, 0) };
            var poles = new List<PoleCorners> { new PoleCorners { PoleNumber = "9", Corners = five } };
            var winners = new Dictionary<string, string> { ["9"] = "78135.1.1" };

            CoordinateRegister register = CoordinateRegisterBuilder.Build(poles, winners, new[] { "78135.1.1" }, "X", "Y");

            Assert.Equal(new[] { "9-1", "9-2", "9-3", "9-4", "9-5" }, register.Blocks[0].Corners.Select(c => c.Label));
            Assert.Equal(new[] { "9" }, register.NotFourCorners);
        }

        [Theory]
        [InlineData("78135.84.442", "84.442")]
        [InlineData("78135.84.34", "84.34")]
        [InlineData("84", "84")]
        public void ParcelLabel_DropsTheEkattePrefix(string id, string expected)
        {
            Assert.Equal(expected, CoordinateRegisterBuilder.ParcelLabel(id));
        }
    }

    public class CoordinateRegisterExporterTests : IDisposable
    {
        private readonly string _dir;

        public CoordinateRegisterExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private sealed class Book
        {
            public List<(string Name, Worksheet Sheet)> Sheets = new List<(string, Worksheet)>();
            public Stylesheet Styles = null!;

            public Cell CellAt(int sheet, string reference) =>
                Sheets[sheet].Sheet.Descendants<Cell>().Single(c => c.CellReference!.Value == reference);

            public string? Text(int sheet, string reference) => CellAt(sheet, reference).InlineString?.Text?.Text;

            public double Number(int sheet, string reference) =>
                double.Parse(CellAt(sheet, reference).CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture);

            public string? Format(int sheet, string reference)
            {
                CellFormat format = Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)CellAt(sheet, reference).StyleIndex!.Value);
                uint id = format.NumberFormatId?.Value ?? 0U;
                return Styles.NumberingFormats?.Elements<NumberingFormat>().FirstOrDefault(n => n.NumberFormatId!.Value == id)?.FormatCode?.Value;
            }

            public List<string> Merges(int sheet) =>
                Sheets[sheet].Sheet.Descendants<MergeCell>().Select(m => m.Reference!.Value!).ToList();
        }

        private Book Export()
        {
            CoordinateRegister first = CoordinateFixture.Build78135();
            var second = new CoordinateRegister { Title = "T2", Subtitle = "S2" };
            second.Blocks.Add(first.Blocks[2]);

            var sheets = new List<(string SheetName, IReadOnlyList<CoordinateRegister> Sections)>
            {
                ("общ. Мездра", new[] { first }),
                ("общ. Враца", new[] { second })
            };
            string path = CoordinateRegisterExporter.Export(sheets, _dir);
            Assert.Equal(Path.Combine(_dir, "Координатен_регистър_на_стъпките.xlsx"), path);

            var book = new Book();
            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart wb = doc.WorkbookPart!;
                foreach (Sheet sheet in wb.Workbook.Descendants<Sheet>())
                {
                    book.Sheets.Add((sheet.Name!.Value!, ((WorksheetPart)wb.GetPartById(sheet.Id!)).Worksheet));
                }
                book.Styles = wb.WorkbookStylesPart!.Stylesheet;
            }
            return book;
        }

        [Fact]
        public void AnOutputFileOpenInAnotherProgram_GivesOneReadableMessage()
        {
            string path = Path.Combine(_dir, "Координатен_регистър_на_стъпките.xlsx");
            File.WriteAllBytes(path, new byte[] { 0 });
            var sheets = new List<(string SheetName, IReadOnlyList<CoordinateRegister> Sections)>
            {
                ("общ. Мездра", new[] { CoordinateFixture.Build78135() })
            };

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))   // like Excel holding it
            {
                var ex = Assert.Throws<PUP_AUTO.Core.FileInUseException>(() => CoordinateRegisterExporter.Export(sheets, _dir));
                Assert.Equal("Файлът Координатен_регистър_на_стъпките.xlsx е отворен в друга програма — затворете го и пуснете отново.", ex.Message);
            }
        }

        [Theory]
        [InlineData(unchecked((int)0x80070020), true)]    // sharing violation
        [InlineData(unchecked((int)0x80070021), true)]    // lock violation
        [InlineData(unchecked((int)0x80070002), false)]   // file not found
        [InlineData(unchecked((int)0x80070005), false)]   // access denied
        public void OnlySharingAndLockViolations_CountAsFileInUse(int hresult, bool expected)
        {
            Assert.Equal(expected, PUP_AUTO.Core.FileInUseException.IsSharingViolation(hresult));
        }

        [Fact]
        public void OneSheetPerMunicipality_PortraitAndOnePageWide()
        {
            Book book = Export();

            Assert.Equal(new[] { "общ. Мездра", "общ. Враца" }, book.Sheets.Select(s => s.Name));
            PageSetup setup = book.Sheets[0].Sheet.Descendants<PageSetup>().Single();
            Assert.Equal(OrientationValues.Portrait, setup.Orientation!.Value);
            Assert.Equal(1U, setup.FitToWidth!.Value);
            Assert.Equal("T2", book.Text(1, "A1"));
            Assert.Equal("Стълб №3, попадащ в имот 84.442", book.Text(1, "A4"));
        }

        [Fact]
        public void ABlock_HasTitle_Header_CentreCornersAndArea()
        {
            Book book = Export();

            Assert.Equal("КООРДИНАТЕН РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ЗА НОВА ВЛ 110kV", book.Text(0, "A1"));
            Assert.Equal(CoordinateFixture.Title78135, book.Text(0, "A2"));

            Assert.Equal("Стълб №1, попадащ в имот 84.34", book.Text(0, "A4"));
            Assert.Equal(new[] { "№", "X(север)", "Y(изток)" }, new[] { "A5", "B5", "C5" }.Select(r => book.Text(0, r)));
            Assert.Equal("Координати на центъра", book.Text(0, "A6"));
            Assert.Equal("1", book.Text(0, "A7"));
            Assert.Equal(4780339.391, book.Number(0, "B7"));
            Assert.Equal(362978.79, book.Number(0, "C7"));
            Assert.Equal("0.000", book.Format(0, "B7"));
            Assert.Equal("0.000", book.Format(0, "C7"));
            Assert.Equal("Координати на чупките", book.Text(0, "A8"));
            Assert.Equal(new[] { "1-1", "1-2", "1-3", "1-4" }, new[] { "A9", "A10", "A11", "A12" }.Select(r => book.Text(0, r)));
            Assert.Equal("Площ на стъпката: 55.1кв.м", book.Text(0, "A13"));

            // One empty row, then the next pole
            Assert.DoesNotContain(book.Sheets[0].Sheet.Descendants<Row>(), r => r.RowIndex!.Value == 14);
            Assert.Equal("Стълб №2, попадащ в имот 84.34", book.Text(0, "A15"));

            List<string> merges = book.Merges(0);
            foreach (int row in new[] { 1, 2, 4, 6, 8, 13, 15 }) Assert.Contains($"A{row}:C{row}", merges);
            Assert.DoesNotContain("A5:C5", merges);
            Assert.DoesNotContain("A7:C7", merges);
        }
    }

    public class FootprintCornersTests
    {
        // Pole 20's P-tag values as the block gives them: not in ring order, with the spacing the attributes have
        private static readonly string[] Tags = { "363604.082, 4784635.716", "363609.720,4784635.397", " 363609.401 , 4784629.759", "363603.763, 4784630.078" };

        [Fact]
        public void ParsedCorners_AreTheAttributeValuesExactly_InP1ToP4Order()
        {
            List<(double X, double Y)> corners = FootprintCorners.Parse(Tags);

            Assert.Equal(
                new[] { (363604.082, 4784635.716), (363609.720, 4784635.397), (363609.401, 4784629.759), (363603.763, 4784630.078) },
                corners);
        }

        [Fact]
        public void TheSortedFootprint_IsTheSameSetOfPoints_AndCounterClockwise()
        {
            List<(double X, double Y)> corners = FootprintCorners.Parse(Tags);

            List<(double X, double Y)> sorted = FootprintCorners.SortCounterClockwise(corners);

            Assert.Equal(corners.OrderBy(p => p.X).ThenBy(p => p.Y), sorted.OrderBy(p => p.X).ThenBy(p => p.Y));
            Assert.True(CoordinateRegisterBuilder.SignedArea(sorted) > 0);
            Assert.True(FootprintCorners.AllMatch(corners, sorted, 0.001));
        }

        [Fact]
        public void ACornerMoreThanAMillimetreFromEveryVertex_DoesNotMatch()
        {
            List<(double X, double Y)> corners = FootprintCorners.Parse(Tags);
            List<(double X, double Y)> sorted = FootprintCorners.SortCounterClockwise(corners);
            var moved = new List<(double X, double Y)>(corners) { [2] = (corners[2].X + 0.002, corners[2].Y) };
            var near = new List<(double X, double Y)>(corners) { [2] = (corners[2].X + 0.0005, corners[2].Y) };

            Assert.False(FootprintCorners.AllMatch(moved, sorted, 0.001));
            Assert.True(FootprintCorners.AllMatch(near, sorted, 0.001));
        }

        [Fact]
        public void UnparsableValues_AreSkipped()
        {
            Assert.Equal(new[] { (1.5, 2.5) }, FootprintCorners.Parse(new[] { "abc", "1.5, 2.5", "7" }));
        }
    }

    public class CoordinateTextTests
    {
        [Theory]
        [InlineData("363604.08", 2)]
        [InlineData("363604.082", 3)]
        [InlineData("12", 0)]
        [InlineData(" 4784635.7200 ", 4)]
        [InlineData("-0.5", 1)]
        public void DecimalCount_CountsTheDigitsAfterTheDot(string number, int expected)
        {
            Assert.Equal(expected, CoordinateText.DecimalCount(number));
        }

        [Theory]
        [InlineData("363604.08, 4784635.72", true)]      // LUPREC 2
        [InlineData("363604.082, 4784635.72", true)]     // one rounded coordinate is enough
        [InlineData("363604.082, 4784635.716", false)]
        [InlineData("363604.0820, 4784635.7160", false)]
        [InlineData("363604.1, 4784635.7", true)]
        [InlineData("363604, 4784635", true)]
        public void HasFewerDecimals_FlagsTextRoundedByTheUnitsPrecision(string point, bool expected)
        {
            Assert.Equal(expected, CoordinateText.HasFewerDecimals(point));
        }

        [Fact]
        public void ARawValue_IsConsistentWithItsRoundedText_OnlyWithinHalfAUnitOfTheLastDigit()
        {
            Assert.True(CoordinateText.IsConsistent(363604.082, 4784635.716, "363604.08, 4784635.72"));
            Assert.True(CoordinateText.IsConsistent(363604.08, 4784635.7249, "363604.08, 4784635.72"));
            Assert.False(CoordinateText.IsConsistent(363604.09, 4784635.716, "363604.08, 4784635.72"));   // another object
            Assert.False(CoordinateText.IsConsistent(0, 0, "363604.08, 4784635.72"));
            Assert.True(CoordinateText.IsConsistent(1, 2, "not a point"));
        }

        [Fact]
        public void FormatPoint_RoundTripsThroughTheFootprintParser()
        {
            string text = CoordinateText.FormatPoint(363604.082, 4784635.716);

            Assert.Equal("363604.082, 4784635.716", text);
            Assert.Equal(new[] { (363604.082, 4784635.716) }, FootprintCorners.Parse(new[] { text }));
        }

        [Fact]
        public void TheObjectAndPropertyAreTakenFromAFieldCode()
        {
            const string code = @"%<\AcObjProp Object(%<\_ObjId 2305843009213833000>%).InsertionPoint \f ""%lu2%pt2""   >%";

            var parsed = CoordinateText.ParseObjectProperty(code);

            Assert.NotNull(parsed);
            Assert.Equal(2305843009213833000L, parsed!.Value.ObjectId);
            Assert.Equal("InsertionPoint", parsed.Value.Property);
            Assert.Null(CoordinateText.ParseObjectProperty(@"%<\AcVar Date \f ""yyyy"">%"));
            Assert.Null(CoordinateText.ParseObjectProperty(string.Empty));
        }
    }

    public class PoleCornerPlacementTests
    {
        // Pole 20 of the official drawing: corners 20-1 .. 20-4 and the label rotation
        private static readonly List<(double X, double Y)> Pole20 = new List<(double, double)>
        {
            (363604.082, 4784635.716), (363609.720, 4784635.397), (363609.401, 4784629.759), (363603.763, 4784630.078)
        };

        private static readonly double Theta = 86.76 * Math.PI / 180;

        [Fact]
        public void Pole20_GetsTheJustificationsAndAlignmentPointsOfTheOfficialDrawing()
        {
            List<CornerLabelPlacement> placed = PoleCornerPlacement.Place("20", Pole20, Theta, out double theta);

            Assert.Equal(Theta, theta);
            Assert.Equal(new[] { "20-1", "20-2", "20-3", "20-4" }, placed.Select(p => p.Label));
            Assert.Equal(
                new[] { CornerLabelHorizontal.Left, CornerLabelHorizontal.Left, CornerLabelHorizontal.Right, CornerLabelHorizontal.Right },
                placed.Select(p => p.Horizontal));
            Assert.Equal(
                new[] { CornerLabelVertical.Baseline, CornerLabelVertical.Top, CornerLabelVertical.Top, CornerLabelVertical.Baseline },
                placed.Select(p => p.Vertical));

            (double X, double Y)[] expected =
            {
                (363604.308, 4784639.710), (363609.946, 4784639.391), (363609.175, 4784625.765), (363603.537, 4784626.084)
            };
            for (int i = 0; i < 4; i++)
            {
                Assert.True(Math.Abs(placed[i].AlignX - expected[i].X) <= 0.002, $"20-{i + 1} X {placed[i].AlignX}");
                Assert.True(Math.Abs(placed[i].AlignY - expected[i].Y) <= 0.002, $"20-{i + 1} Y {placed[i].AlignY}");

                double distance = Math.Sqrt(Math.Pow(placed[i].AlignX - Pole20[i].X, 2) + Math.Pow(placed[i].AlignY - Pole20[i].Y, 2));
                Assert.Equal(4.0, distance, 9);
            }
        }

        [Fact]
        public void ForwardLeftCorner_OfPole20_IsCornerOne_AndTheFallbackRotatesTheListToStartThere()
        {
            Assert.Equal(0, PoleCornerPlacement.ForwardLeftIndex(Pole20, Theta));

            // Same clockwise ring started at 20-3: the fallback brings 20-1 back to the front, order kept
            var shifted = new List<(double X, double Y)> { Pole20[2], Pole20[3], Pole20[0], Pole20[1] };
            Assert.Equal(Pole20, CoordinateRegisterBuilder.StartAtForwardLeft(shifted, Theta));
            Assert.Equal(Pole20, CoordinateRegisterBuilder.StartAtForwardLeft(Pole20, Theta));
        }

        [Fact]
        public void OnlyLeftBaseline_GoesInPosition_TheOtherThreeUseTheAlignmentPoint()
        {
            List<CornerLabelPlacement> placed = PoleCornerPlacement.Place("20", Pole20, Theta, out _);

            // 20-1 Left/Baseline, 20-2 Left/Top, 20-3 Right/Top, 20-4 Right/Baseline
            Assert.Equal(new[] { true, false, false, false }, placed.Select(p => p.UsesPosition));
        }

        [Fact]
        public void TheDefaultStartRuleIsThePTagOrder()
        {
            Assert.Equal(CornerStartRule.PTagOrder, CoordinateRegisterBuilder.StartRule);
        }

        [Theory]
        [InlineData("GBP032", true)]
        [InlineData("gbp032", true)]
        [InlineData(" GBP032 ", true)]
        [InlineData("GBP0320", false)]
        [InlineData("Stalb", false)]
        public void CornerBlocksAreRecognisedByName(string name, bool expected)
        {
            Assert.Equal(expected, PUP_AUTO.Core.PoleCornerBlockNames.IsCornerBlock(name));
        }

        [Fact]
        public void WithoutALabelRotation_TheEdgeFromTheLastCornerToTheFirstIsUsed()
        {
            PoleCornerPlacement.Place("20", Pole20, null, out double theta);

            Assert.Equal(86.76, theta * 180 / Math.PI, 1);
        }
    }
}
