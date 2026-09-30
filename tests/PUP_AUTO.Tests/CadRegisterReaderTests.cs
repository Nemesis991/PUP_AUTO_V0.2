using PUP_AUTO.CadRegister;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class MikEncodingTests
    {
        [Fact]
        public void RealHeaderBytes_DecodeToTheKnownWords()
        {
            // Bytes taken from the headers of real AGKK files (place names and the program's firm/coordinate-system lines)
            Assert.Equal("ЦАРЕВЕЦ", MikEncoding.Decode(new byte[] { 0x96, 0x80, 0x90, 0x85, 0x82, 0x85, 0x96 }));
            Assert.Equal("гр.ЧЕРВЕН БРЯГ", MikEncoding.Decode(new byte[]
                { 0xA3, 0xB0, 0x2E, 0x97, 0x85, 0x90, 0x82, 0x85, 0x8D, 0x20, 0x81, 0x90, 0x9F, 0x83 }));
            Assert.Equal("Фирма", MikEncoding.Decode(new byte[] { 0x94, 0xA8, 0xB0, 0xAC, 0xA0 }));
            Assert.Equal("Балтийска", MikEncoding.Decode(new byte[] { 0x81, 0xA0, 0xAB, 0xB2, 0xA8, 0xA9, 0xB1, 0xAA, 0xA0 }));
        }

        [Fact]
        public void WholeAlphabet_IsAlphabeticalFrom0x80To0xBF()
        {
            var bytes = new byte[64];
            for (int i = 0; i < 64; i++) bytes[i] = (byte)(0x80 + i);

            Assert.Equal(
                "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдежзийклмнопрстуфхцчшщъыьэюя",
                MikEncoding.Decode(bytes));
        }

        [Fact]
        public void Ascii_PassesThroughUnchanged()
        {
            byte[] ascii = System.Text.Encoding.ASCII.GetBytes("EKATTE 06433\r\nD 1,2\\3\"4;");
            Assert.Equal("EKATTE 06433\r\nD 1,2\\3\"4;", MikEncoding.Decode(ascii));
        }

        [Fact]
        public void BytesAbove0xBF_AreCountedAndReplaced()
        {
            string text = MikEncoding.Decode(new byte[] { 0x41, 0xC0, 0xFF, 0x42 }, out int undecodable);

            Assert.Equal("A��B", text);
            Assert.Equal(2, undecodable);
        }

        [Fact]
        public void RoundTrip_ThroughTheTestEncoder()
        {
            const string text = "Червен бряг, ул. Юрий Гагарин 5Я";
            Assert.Equal(text, MikEncoding.Decode(MikTestEncoder.Encode(text)));
        }
    }

    public class CadRegisterReaderTests
    {
        private static string F(string snippet, int occurrence = 1) => SyntheticCad.LineOf(snippet, occurrence).ToString();

        [Fact]
        public void Header_GivesEkatteAndSettlementName_WithLeadingZeroKept()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal("06433", data.Ekatte);
            Assert.Equal("с.ТЕСТОВО", data.SettlementName);
            Assert.Equal("4.02", data.Version);
        }

        [Fact]
        public void Parcels_AreKeyedByFullId_AndReadByFieldName()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal(new[] { "06433.501.1", "06433.501.2", "06433.501.3", "06433.501.4" },
                data.Parcels.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

            // Fields are declared in the order IDENT, KAT, VIDT, BRANDNEW, NTP, MESTNOST, VIDS — not the usual order
            CadastralParcel p = data.Parcels["06433.501.1"];
            Assert.Equal("06433.501.1", p.Id);
            Assert.Equal("8", p.Kat);
            Assert.Equal("3", p.Vidt);
            Assert.Equal("2230", p.Ntp);
            Assert.Equal("5", p.Vids);
            Assert.Equal("17", p.MestnostCode);
            Assert.Equal("ТЕСТОВА МЕСТНОСТ", p.MestnostName);
        }

        [Fact]
        public void FirstDuplicateParcelWins()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal("8", data.Parcels["06433.501.1"].Kat);
            Assert.Equal("2230", data.Parcels["06433.501.1"].Ntp);
        }

        [Fact]
        public void OfficialArea_ComesOnlyFromControlCadaster()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal(1200.5, data.Parcels["06433.501.1"].AreaSqm); // not 7777 from the other layer's CONTROL
            Assert.Equal(800.0, data.Parcels["06433.501.2"].AreaSqm);
            Assert.Null(data.Parcels["06433.501.3"].AreaSqm);
            Assert.Equal(55.5, data.Parcels["06433.501.4"].AreaSqm);
        }

        [Fact]
        public void RowWithFewerValues_KeepsMissingFieldsEmpty()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            CadastralParcel p = data.Parcels["06433.501.3"];
            Assert.Equal("1", p.Vidt);
            Assert.Equal("", p.Ntp);
            Assert.Equal("", p.Kat);
            Assert.Equal("", p.MestnostName);
        }

        [Fact]
        public void UnknownMestnostCode_GivesEmptyName()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            CadastralParcel p = data.Parcels["06433.501.4"];
            Assert.Equal("99", p.MestnostCode);
            Assert.Equal("", p.MestnostName);
        }

        [Fact]
        public void Rights_AreKeyedByParcel_WithPersonNameAndRawShare()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            IReadOnlyList<OwnershipRight> rights = data.RightsOf("06433.501.1");
            Assert.Equal(2, rights.Count);

            OwnershipRight first = rights[0];
            Assert.Equal("06433.501.1", first.ParcelId);
            Assert.Equal("1", first.PravoVid);
            Assert.Equal(SyntheticCad.FakeEgn1, first.PersonId);
            Assert.Equal(SyntheticCad.Person1Name, first.PersonName);
            Assert.Equal("1", first.DocId1);
            Assert.Equal("2", first.DocId2);

            Assert.Empty(data.RightsOf("06433.501.3"));
            Assert.Empty(data.RightsOf("06433.999.9"));
        }

        [Fact]
        public void PersonIds_AreText_LeadingZerosAndOddValuesKept()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal("0000000001", data.RightsOf("06433.501.1")[0].PersonId);
            Assert.Equal("000123456", data.RightsOf("06433.501.1")[1].PersonId);
            Assert.Equal("8690П", data.RightsOf("06433.501.2")[0].PersonId);
            Assert.Equal(SyntheticCad.OddPersonName, data.RightsOf("06433.501.2")[0].PersonName);
        }

        [Fact]
        public void BackslashesInANameBecomeQuotes()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal(SyntheticCad.FirmNameUnescaped, data.RightsOf("06433.501.1")[1].PersonName);
        }

        [Theory]
        [InlineData("\\АГРО\\", "„АГРО“")]
        [InlineData("\\А\\ и \\Б\\ ООД", "„А“ и „Б“ ООД")]
        [InlineData("\\НЕЗАТВОРЕНО", "„НЕЗАТВОРЕНО")]
        [InlineData("БЕЗ КАВИЧКИ", "БЕЗ КАВИЧКИ")]
        public void UnescapeQuotes_AlternatesOpeningAndClosing(string raw, string expected)
        {
            Assert.Equal(expected, CadRegisterReader.UnescapeQuotes(raw));
        }

        [Fact]
        public void DuplicatePersons_AreCountedOnce_ById()
        {
            CadRegisterData data = SyntheticCad.Read(out List<string> warnings);

            Assert.Equal(3, data.PersonCount); // 4 PERSONS rows, one ID appears twice (two addresses)
            Assert.DoesNotContain(warnings, w => w.Contains("различно име"));
        }

        [Fact]
        public void RightsOfOtherObjects_AreKeptUnderTheirOwnId()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Single(data.RightsOf("06433.501.1.1")); // a right on something that is not in POZEMLIMOTI
            Assert.False(data.Parcels.ContainsKey("06433.501.1.1"));
        }

        [Fact]
        public void UnknownSectionsFieldsAndHeaderKeys_AreSkippedWithoutWarnings()
        {
            SyntheticCad.Read(out List<string> warnings);

            Assert.DoesNotContain(warnings, w => w.Contains("SOMEUNKNOWNTABLE"));
            Assert.DoesNotContain(warnings, w => w.Contains("BRANDNEW"));
            Assert.DoesNotContain(warnings, w => w.Contains("UNKNOWNKEY"));
            Assert.DoesNotContain(warnings, w => w.Contains("CONTUR_SURROUND"));
        }

        [Fact]
        public void MalformedRows_WarnWithLineNumber_AndParsingContinues()
        {
            CadRegisterData data = SyntheticCad.Read(out List<string> warnings);

            // too many values for the declared fields
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D 501.5,5,3,x,2230,17,5,extra")}:") && w.Contains("POZEMLIMOTI") && w.Contains("8 стойности за 7 полета"));
            // empty key field
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D ,5,3,x,2230,17,5")}:") && w.Contains("POZEMLIMOTI") && w.Contains("IDENT"));
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D ,5,0000000001,1,1,1")}:") && w.Contains("PRAVA") && w.Contains("IDENT"));
            // a repeated parcel
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D 501.1,9,9,x,9999,17,5")}:") && w.Contains("501.1"));
            // an invalid CONTUR_AREA
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("CONTUR_AREA 501.9")}:") && w.Contains("CONTUR_AREA"));

            // the good rows around them were still read
            Assert.Equal(4, data.Parcels.Count);
            Assert.Equal(2, data.RightsOf("06433.501.1").Count);
        }

        [Fact]
        public void RightWithAnUnknownPerson_IsKept_WithEmptyName_AndOneSummaryWarning()
        {
            CadRegisterData data = SyntheticCad.Read(out List<string> warnings);

            OwnershipRight right = data.RightsOf("06433.501.4").Single();
            Assert.Equal(SyntheticCad.FakeUnknownPerson, right.PersonId);
            Assert.Equal("", right.PersonName);
            Assert.Single(warnings, w => w.Contains("липсва в PERSONS"));
        }

        [Fact]
        public void Warnings_NeverContainPersonalData()
        {
            SyntheticCad.Read(out List<string> warnings);
            Assert.NotEmpty(warnings);

            string all = string.Join("\n", warnings);
            foreach (string secret in new[]
            {
                SyntheticCad.FakeEgn1, SyntheticCad.FakeBulstat, SyntheticCad.FakeOddId, SyntheticCad.FakeUnknownPerson,
                SyntheticCad.Person1Name, SyntheticCad.OddPersonName, "АГРО", "ТЕСТОВ", "УЛ. "
            })
            {
                Assert.DoesNotContain(secret, all);
            }
        }

        [Fact]
        public void Fixture_GivesExactlyTheExpectedWarnings()
        {
            SyntheticCad.Read(out List<string> warnings);

            // 2 while reading (bad CONTUR_AREA, extra values) + 2 POZEMLIMOTI + 1 PRAVA + 1 unknown-person summary
            Assert.Equal(6, warnings.Count);
        }

        [Fact]
        public void TableWithoutItsKeyField_IsSkippedWithOneWarning()
        {
            string text = "HEADER\r\nEKATTE 11111\r\nEND_HEADER\r\nTABLE POZEMLIMOTI\r\nF VIDT S 1 0\r\nD 3\r\nD 4\r\nEND_TABLE\r\n";
            var warnings = new List<string>();

            CadRegisterData data = new CadRegisterReader(warnings.Add).Read(MikTestEncoder.Encode(text));

            Assert.Empty(data.Parcels);
            Assert.Single(warnings);
            Assert.Contains("IDENT", warnings[0]);
        }

        [Fact]
        public void DataRowBeforeItsFieldDeclaration_WarnsOncePerTable()
        {
            string text = "HEADER\r\nEKATTE 11111\r\nEND_HEADER\r\nTABLE POZEMLIMOTI\r\nD 1\r\nD 2\r\nF IDENT C 20 0 1\r\nD 3\r\nEND_TABLE\r\n";
            var warnings = new List<string>();

            CadRegisterData data = new CadRegisterReader(warnings.Add).Read(MikTestEncoder.Encode(text));

            Assert.Single(warnings);
            Assert.Contains("ред 5", warnings[0]);
            Assert.Equal(new[] { "11111.3" }, data.Parcels.Keys.ToArray());
        }

        [Fact]
        public void IdentThatAlreadyCarriesTheEkatte_IsNotPrefixedTwice()
        {
            Assert.Equal("06433.501.1", CadRegisterReader.FullId("06433", "501.1"));
            Assert.Equal("06433.501.1", CadRegisterReader.FullId("06433", "06433.501.1"));
            Assert.Equal("501.1", CadRegisterReader.FullId("", "501.1"));
        }

        [Fact]
        public void MissingEkatte_IsWarnedAndParcelsAreUnprefixed()
        {
            string text = "HEADER\r\nEND_HEADER\r\nTABLE POZEMLIMOTI\r\nF IDENT C 20 0 1\r\nD 501.1\r\nEND_TABLE\r\n";
            var warnings = new List<string>();

            CadRegisterData data = new CadRegisterReader(warnings.Add).Read(MikTestEncoder.Encode(text));

            Assert.Contains(warnings, w => w.Contains("ЕКАТТЕ"));
            Assert.Contains("501.1", data.Parcels.Keys);
        }

        [Fact]
        public void OtherVersion_IsWarned_ButStillRead()
        {
            string text = "HEADER\r\nVERSION 5.00\r\nEKATTE 06433\r\nEND_HEADER\r\nTABLE POZEMLIMOTI\r\nF IDENT C 20 0 1\r\nD 1.1\r\nEND_TABLE\r\n";
            var warnings = new List<string>();

            CadRegisterData data = new CadRegisterReader(warnings.Add).Read(MikTestEncoder.Encode(text));

            Assert.Contains(warnings, w => w.Contains("5.00"));
            Assert.Single(data.Parcels);
        }

        [Fact]
        public void BytesOutsideMik_AreWarnedOnce_WithACount()
        {
            byte[] bytes = MikTestEncoder.Encode("HEADER\r\nEKATTE 06433\r\nEND_HEADER\r\nNAME x\r\n");
            bytes[bytes.Length - 3] = 0xE5; // one byte in 0xC0..0xFF
            var warnings = new List<string>();

            new CadRegisterReader(warnings.Add).Read(bytes);

            Assert.Single(warnings, w => w.Contains("1 байта"));
        }

        [Theory]
        [InlineData("a,b,c", new[] { "a", "b", "c" })]
        [InlineData(" a , b ,c ", new[] { "a", "b", "c" })]
        [InlineData("a,,c,", new[] { "a", "", "c", "" })]
        [InlineData("\"a,b\",c", new[] { "a,b", "c" })]
        [InlineData("\"x \"\"y\"\" z\",1", new[] { "x \"y\" z", "1" })]
        [InlineData("'q',w", new[] { "q", "w" })]
        [InlineData("000414154,8690П", new[] { "000414154", "8690П" })]
        public void SplitCsv_HandlesQuotesAndKeepsText(string line, string[] expected)
        {
            Assert.Equal(expected, CadRegisterReader.SplitCsv(line).ToArray());
        }

        [Fact]
        public void LfOnlyLineEndings_AreAccepted()
        {
            byte[] bytes = MikTestEncoder.Encode(SyntheticCad.Text.Replace("\r\n", "\n"));
            CadRegisterData data = new CadRegisterReader().Read(bytes);

            Assert.Equal(4, data.Parcels.Count);
        }

        [Fact]
        public void EmptyFile_GivesAnEmptyRegister()
        {
            CadRegisterData data = new CadRegisterReader().Read(Array.Empty<byte>());

            Assert.Empty(data.Parcels);
            Assert.Equal("", data.Ekatte);
        }

        [Fact]
        public void EkatteOf_TakesThePartBeforeTheFirstDot()
        {
            Assert.Equal("06433", CadRegisterData.EkatteOf("06433.501.1"));
            Assert.Equal("", CadRegisterData.EkatteOf("Handle123"));
        }
    }
}
