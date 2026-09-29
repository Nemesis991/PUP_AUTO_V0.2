using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Characterisation tests of the parcel / servitude / pole merge (moved from MainCommands).</summary>
    public class ReportBuilderTests
    {
        private static Pole PoleOn(string poleId, params (string parcel, double area)[] overlaps)
        {
            var pole = new Pole { PoleId = poleId };
            foreach (var (parcel, area) in overlaps) pole.OverlappingParcels[parcel] = area;
            return pole;
        }

        private static List<ReportRow> Merge(
            string[] ids,
            Dictionary<string, ParcelData>? db = null,
            Dictionary<string, double>? servitude = null,
            List<Pole>? poles = null,
            List<string>? warnings = null)
        {
            return ReportBuilder.MergeResultsStatic(
                ids,
                db ?? new Dictionary<string, ParcelData>(),
                servitude ?? new Dictionary<string, double>(),
                poles ?? new List<Pole>(),
                warnings != null ? warnings.Add : _ => { });
        }

        [Fact]
        public void NoDataOwnerConstant_IsByteIdentical()
        {
            Assert.Equal("NO DATA", ReportBuilder.NoDataOwner);
        }

        [Fact]
        public void ParcelMissingFromDatabase_GetsNoDataOwner_AndOneWarning()
        {
            var warnings = new List<string>();

            var rows = Merge(new[] { "P-1" }, warnings: warnings);

            var row = Assert.Single(rows);
            Assert.Equal("NO DATA", row.Owner);
            Assert.Equal(string.Empty, row.OwnerName);
            Assert.Equal(0.0, row.DocumentAreaSqM);
            var warning = Assert.Single(warnings);
            Assert.Equal(
                "ParcelId 'P-1' exists in CAD geometry but is MISSING from the CadLibraryReader database. Using 'NO DATA' for Owner.",
                warning);
        }

        [Fact]
        public void ParcelInDatabase_CopiesRegisterFields_NoWarning()
        {
            var warnings = new List<string>();
            var db = new Dictionary<string, ParcelData>
            {
                ["P-1"] = new ParcelData
                {
                    ParcelId = "P-1", Owner = "Иван", SubDivision = "s", TerritoryType = "t", Usage = "u",
                    Locality = "l", Category = "c", OwnershipType = "o", OwnerId = "id", OwnerName = "name",
                    DocumentArea = 1234.5
                }
            };

            var row = Assert.Single(Merge(new[] { "P-1" }, db, warnings: warnings));

            Assert.Empty(warnings);
            Assert.Equal("Иван", row.Owner);
            Assert.Equal("s", row.SubDivision);
            Assert.Equal("t", row.TerritoryType);
            Assert.Equal("u", row.Usage);
            Assert.Equal("l", row.Locality);
            Assert.Equal("c", row.Category);
            Assert.Equal("o", row.OwnershipType);
            Assert.Equal("id", row.OwnerId);
            Assert.Equal("name", row.OwnerName);
            Assert.Equal(1234.5, row.DocumentAreaSqM);
        }

        [Fact]
        public void ServitudeArea_IsLooked_UpByParcelId_DefaultsToZero()
        {
            var rows = Merge(
                new[] { "A", "B" },
                servitude: new Dictionary<string, double> { ["A"] = 42.5 });

            Assert.Equal(42.5, rows[0].ServitudeAreaSqM);
            Assert.Equal(0.0, rows[1].ServitudeAreaSqM);
        }

        [Fact]
        public void PolesOnParcel_AreSummedAndCounted()
        {
            var poles = new List<Pole>
            {
                PoleOn("1", ("A", 0.036)),
                PoleOn("2", ("A", 0.04), ("B", 0.01)),
                PoleOn("3", ("B", 0.02))
            };

            var rows = Merge(new[] { "A", "B" }, poles: poles);

            Assert.Equal(2, rows[0].PoleCount);
            Assert.Equal(0.036 + 0.04, rows[0].PoleAreaSqM);
            Assert.Equal(new[] { "1", "2" }, rows[0].AssignedPoles.Select(p => p.PoleId).ToArray());
            Assert.Equal(2, rows[1].PoleCount);
            Assert.Equal(0.01 + 0.02, rows[1].PoleAreaSqM);
        }

        [Fact]
        public void ParcelKnownOnlyFromAPole_StillGetsARow()
        {
            var rows = Merge(new[] { "A" }, poles: new List<Pole> { PoleOn("1", ("Z", 1.0)) });

            Assert.Equal(new[] { "A", "Z" }, rows.Select(r => r.ParcelId).ToArray());
            Assert.Equal(1, rows[1].PoleCount);
        }

        [Fact]
        public void RowsAreSortedById_AndDuplicatesCollapsed()
        {
            var rows = Merge(new[] { "C", "A", "B", "A" });

            Assert.Equal(new[] { "A", "B", "C" }, rows.Select(r => r.ParcelId).ToArray());
        }

        [Fact]
        public void NoParcels_GivesNoRows()
        {
            Assert.Empty(Merge(new string[0]));
        }
    }
}
