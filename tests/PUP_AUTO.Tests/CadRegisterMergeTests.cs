using PUP_AUTO.CadRegister;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Several .cad files of one землище (КАИС extracts ordered in parts) are merged, not "newer wins".</summary>
    public class CadRegisterMergeTests : TempFolderTest
    {
        /// <summary>A tiny synthetic .cad: parcels (ident, VIDS, owner person ID) and one owner per parcel.</summary>
        private string WriteMini(string name, string ekatte, params (string Ident, string Vids, string Person)[] rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("HEADER\nVERSION 4.02\nEKATTE ").Append(ekatte).Append("\nNAME с. Мини\nEND_HEADER\n");
            sb.Append("TABLE POZEMLIMOTI\nF IDENT C 20 0 1\nF VIDT S 1 0 2\nF VIDS S 2 0 2\nF NTP S 4 0 2\nF KAT S 2 0\nF END_DATE D 10 0\n");
            foreach (var r in rows) sb.Append("D \"").Append(r.Ident).Append("\",3,").Append(r.Vids).Append(",2800,7,\n");
            sb.Append("END_TABLE\nTABLE PRAVA\nF IDENT C 20 0 1\nF PERSON C 13 0 3 PERSONS\nF PRAVOVID S 2 0 2\nF DOCID1 S 3 0\nF DOCID2 N 8 3\nF END_DATE D 10 0\n");
            foreach (var r in rows) sb.Append("D \"").Append(r.Ident).Append("\",\"").Append(r.Person).Append("\",1,1,2,\n");
            sb.Append("END_TABLE\nTABLE PERSONS\nF PERSON C 13 0 1\nF NAME C 40 0\nF FLAG S 1 0\nF END_DATE D 10 0\n");
            foreach (string person in rows.Select(r => r.Person).Distinct()) sb.Append("D \"").Append(person).Append("\",\"ФИКТИВЕН ").Append(person).Append("\",F,\n");
            sb.Append("END_TABLE\n");

            string path = Path.Combine(Dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, SyntheticCad.Bytes(sb.ToString()));
            return path;
        }

        private static void SetTimes(string older, string newer)
        {
            File.SetLastWriteTime(older, new DateTime(2026, 6, 1, 10, 0, 0));
            File.SetLastWriteTime(newer, new DateTime(2026, 6, 1, 12, 0, 0));
        }

        [Fact]
        public void TwoPartsOfOneZemlishte_AreMerged_AndBothPartsAreFound()
        {
            string part1 = WriteMini("1.cad", "78135", ("49.60", "5", "P1"), ("49.64", "5", "P2"));
            string part2 = WriteMini("2.cad", "78135", ("49.54", "5", "P3"), ("49.59", "5", "P2"));
            SetTimes(part1, part2);

            var warnings = new List<string>();
            var infos = new List<string>();
            // both orders: the result must not depend on which part is newer
            foreach (string[] paths in new[] { new[] { part1, part2 }, new[] { part2, part1 } })
            {
                CadRegisterSet set = CadRegisterSet.Load(paths, warnings.Add, infos.Add);

                Assert.Equal(1, set.Count);
                CadRegisterData data = set.ByEkatte["78135"];
                Assert.Equal(4, data.Parcels.Count);
                foreach (string id in new[] { "78135.49.54", "78135.49.59", "78135.49.60", "78135.49.64" })
                {
                    Assert.True(set.TryGetFor(id, out CadRegisterData found), id);
                    Assert.Same(data, found);
                    Assert.Single(data.RightsOf(id));
                }
                Assert.Equal(3, data.PersonCount);                    // P1, P2, P3: P2 is in both parts
                Assert.Equal(2, set.SourceFilesOf("78135").Count);
                Assert.Empty(set.SourceFilesOf("00000"));
            }
            Assert.Empty(warnings);
        }

        [Fact]
        public void MergeInfoLine_NamesTheFiles_TheParcelCount_AndTheOverlap()
        {
            string part1 = WriteMini("a.cad", "69050", ("1.1", "5", "P1"), ("1.2", "5", "P1"));
            string part2 = WriteMini("b.cad", "69050", ("1.2", "3", "P2"), ("1.3", "5", "P2"));
            SetTimes(part1, part2);

            var infos = new List<string>();
            CadRegisterSet.Load(new[] { part1, part2 }, _ => { }, infos.Add);

            Assert.Equal("ЕКАТТЕ 69050: обединени 2 файла (a.cad, b.cad), 3 имота, 1 в повече от един файл — взет е по-новият.", Assert.Single(infos));
        }

        [Fact]
        public void ParcelInTwoFiles_ComesFromTheNewerFile_WithItsOwnRightsOnly()
        {
            string older = WriteMini("old.cad", "11111", ("5.1", "5", "OLD1"), ("5.2", "5", "OLD2"));
            string newer = WriteMini("new.cad", "11111", ("5.1", "3", "NEW1"));
            SetTimes(older, newer);

            CadRegisterSet set = CadRegisterSet.Load(new[] { older, newer }, _ => { });
            CadRegisterData data = set.ByEkatte["11111"];

            Assert.Equal(2, data.Parcels.Count);
            Assert.Equal("3", data.Parcels["11111.5.1"].Vids);                       // the newer file's parcel
            Assert.Equal(new[] { "NEW1" }, data.RightsOf("11111.5.1").Select(r => r.PersonId).ToArray());   // not mixed with OLD1
            Assert.Equal("5", data.Parcels["11111.5.2"].Vids);                       // only in the older file: kept
            Assert.Equal(new[] { "OLD2" }, data.RightsOf("11111.5.2").Select(r => r.PersonId).ToArray());
            Assert.Equal(3, data.PersonCount);
        }

        [Fact]
        public void ParcelInTwoFiles_WithTheSameTime_TheFirstGivenFileWins()
        {
            string first = WriteMini("f.cad", "11111", ("5.1", "5", "P1"));
            string second = WriteMini("s.cad", "11111", ("5.1", "3", "P2"));
            File.SetLastWriteTime(first, new DateTime(2026, 6, 1));
            File.SetLastWriteTime(second, new DateTime(2026, 6, 1));

            CadRegisterSet set = CadRegisterSet.Load(new[] { first, second }, _ => { });

            Assert.Equal("5", set.ByEkatte["11111"].Parcels["11111.5.1"].Vids);
        }

        [Fact]
        public void OneFile_IsNotMerged_AndGivesNoInfoLine()
        {
            string only = WriteMini("only.cad", "11111", ("5.1", "5", "P1"));

            var infos = new List<string>();
            CadRegisterSet set = CadRegisterSet.Load(new[] { only }, _ => { }, infos.Add);

            Assert.Empty(infos);
            Assert.Equal(new[] { only }, set.SourceFilesOf("11111").ToArray());
            Assert.Equal(1, set.ByEkatte["11111"].PersonCount);
        }

        [Fact]
        public void ParcelOfThePartThatUsedToBeDropped_IsNoLongerNotFoundInTheRegister()
        {
            string part1 = WriteMini("1.cad", "78135", ("49.60", "5", "P1"));
            string part2 = WriteMini("2.cad", "78135", ("49.54", "5", "P2"));
            SetTimes(part1, part2);
            CadRegisterSet set = CadRegisterSet.Load(new[] { part1, part2 }, _ => { });
            CadRegisterData data = set.ByEkatte["78135"];

            AffectedRegister register = AffectedParcelsRegisterBuilder.Build(
                new[] { RegisterFixture.Areas("78135.49.54", 15494.0, 0.0), RegisterFixture.Areas("78135.49.60", 1000.0, 0.0) },
                new PUP_AUTO.Semantics.PoleStepPiece[0], data, RegisterFixture.Nomenclatures(), "ОБЕКТ", "ЗАГЛАВИЕ");

            Assert.Empty(register.NotFound);
            Assert.All(register.Rows.Where(r => r.IsFirstOfParcel), r => Assert.Equal("7", r.KatCode));
        }

        [Fact]
        public void Reader_ListsTheDistinctPersonIds()
        {
            string path = WriteMini("p.cad", "11111", ("5.1", "5", "P1"), ("5.2", "5", "P1"), ("5.3", "5", "P2"));

            CadRegisterData data = new CadRegisterReader().ReadFile(path);

            Assert.Equal(2, data.PersonCount);
            Assert.Equal(new[] { "P1", "P2" }, data.PersonIds.OrderBy(p => p).ToArray());
        }
    }
}
