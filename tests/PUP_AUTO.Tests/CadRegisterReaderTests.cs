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
        public void Byte0xD5_IsTheNumeroSign()
        {
            // "Стълб №98" as seen in an MKAD export
            byte[] bytes = { 0x91, 0xB2, 0xBA, 0xAB, 0xA1, 0x20, 0xD5, 0x39, 0x38 };

            string text = MikEncoding.Decode(bytes, out int undecodable);

            Assert.Equal("Стълб №98", text);
            Assert.Equal(0, undecodable);
        }

        [Fact]
        public void Ascii_PassesThroughUnchanged()
        {
            byte[] ascii = System.Text.Encoding.ASCII.GetBytes("EKATTE 06433\r\nD 1,2\\3\"4;");
            Assert.Equal("EKATTE 06433\r\nD 1,2\\3\"4;", MikEncoding.Decode(ascii));
        }

        [Fact]
        public void OtherBytesAbove0xBF_AreCountedAndReplaced()
        {
            string text = MikEncoding.Decode(new byte[] { 0x41, 0xC0, 0xD4, 0xD6, 0xFF, 0x42 }, out int undecodable);

            Assert.Equal("A����B", text);
            Assert.Equal(4, undecodable);
        }

        [Fact]
        public void RoundTrip_ThroughTheTestEncoder()
        {
            const string text = "Червен бряг, ул. Юрий Гагарин 5Я, Стълб №98";
            Assert.Equal(text, MikEncoding.Decode(MikTestEncoder.Encode(text)));
        }
    }

    public class CadRegisterReaderTests
    {
        private static string F(string snippet, int occurrence = 1) => SyntheticCad.LineOf(snippet, occurrence).ToString();

        [Fact]
        public void Header_SingleSpaced_GivesEkatteAndSettlementName_WithLeadingZeroKept()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal("06433", data.Ekatte);
            Assert.Equal("с. Тестово", data.SettlementName);
            Assert.Equal("4.02", data.Version);
        }

        [Fact]
        public void Header_PaddedSpaces_AreSplitOnWhitespaceToo()
        {
            string text = "HEADER\r\nVERSION    4.02\r\nEKATTE     06433\r\nNAME       с.БРЕСТЕ\r\nEND_HEADER\r\n";

            CadRegisterData data = new CadRegisterReader().Read(MikTestEncoder.Encode(text));

            Assert.Equal("06433", data.Ekatte);
            Assert.Equal("с.БРЕСТЕ", data.SettlementName);
            Assert.Equal("4.02", data.Version);
        }

        [Fact]
        public void RealRows_ReadAsInARealExport()
        {
            CadRegisterData data = SyntheticCad.Read(out List<string> warnings, SyntheticCad.RealRows);

            Assert.Empty(warnings);
            Assert.Equal("с. Бресте", data.SettlementName);
            Assert.Equal(2, data.Parcels.Count);

            CadastralParcel p = data.Parcels["06433.54.1"];
            Assert.Equal("3", p.Vidt);
            Assert.Equal("3", p.Vids);
            Assert.Equal("2800", p.Ntp);
            Assert.Equal("8", p.Kat);
            Assert.Equal("7", p.MestnostCode);
            Assert.Equal("СТРАНАТА", p.MestnostName);
            Assert.Equal(90657.681, p.AreaSqm);

            CadastralParcel q = data.Parcels["06433.61.363"];
            Assert.Equal("2230", q.Ntp);
            Assert.Equal("0", q.Kat);
            Assert.Equal("", q.MestnostCode);
            Assert.Equal("", q.MestnostName);
            Assert.Null(q.AreaSqm);

            OwnershipRight right = Assert.Single(data.RightsOf("06433.54.1"));
            Assert.Equal("000414154", right.PersonId);   // quoted text, leading zeros kept
            Assert.Equal("ОБЩИНА ЧЕРВЕН БРЯГ", right.PersonName);
            Assert.Equal("1", right.PravoVid);
            Assert.Equal("1", right.DocId1);
            Assert.Equal("1", right.DocId2);
            Assert.Equal(1, data.PersonCount); // the same id twice with different addresses/dates
        }

        [Fact]
        public void Columns_AreReadByFieldName_NotByPosition()
        {
            string text =
                "HEADER\r\nEKATTE 11111\r\nEND_HEADER\r\n" +
                "TABLE POZEMLIMOTI\r\nF KAT S 2 0\r\nF NTP S 4 0\r\nF UNKNOWNFIELD S 4 0\r\nF IDENT C 20 0 1\r\nF VIDT S 1 0\r\n" +
                "D 5,2230,\"x\",\"7.1\",3\r\n" +
                "END_TABLE\r\n";

            CadRegisterData data = new CadRegisterReader().Read(MikTestEncoder.Encode(text));

            CadastralParcel p = data.Parcels["11111.7.1"];
            Assert.Equal("5", p.Kat);
            Assert.Equal("2230", p.Ntp);
            Assert.Equal("3", p.Vidt);
        }

        [Fact]
        public void Parcels_AreKeyedByFullId_AndReadByFieldName()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal(new[] { "06433.501.1", "06433.501.2", "06433.501.3", "06433.501.4" },
                data.Parcels.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

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
        public void EmptyValues_StayEmpty()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            CadastralParcel p = data.Parcels["06433.501.3"];
            Assert.Equal("1", p.Vidt);
            Assert.Equal("", p.Ntp);
            Assert.Equal("", p.Kat);
            Assert.Equal("", p.MestnostCode);
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
        public void PersonIds_AreText_LeadingZerosOddAndCompoundValuesKept()
        {
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.Equal("0000000001", data.RightsOf("06433.501.1")[0].PersonId);
            Assert.Equal("000123456", data.RightsOf("06433.501.1")[1].PersonId);
            Assert.Equal("8690П", data.RightsOf("06433.501.2")[0].PersonId);
            Assert.Equal(SyntheticCad.OddPersonName, data.RightsOf("06433.501.2")[0].PersonName);
            Assert.Equal("7497_0006082776", data.RightsOf("06433.501.2")[1].PersonId);
            Assert.Equal(SyntheticCad.CompoundPersonName, data.RightsOf("06433.501.2")[1].PersonName);
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

            Assert.Equal(4, data.PersonCount); // 5 PERSONS rows, one ID appears twice (two addresses)
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
            Assert.DoesNotContain(warnings, w => w.Contains("IZDATELI"));
            Assert.DoesNotContain(warnings, w => w.Contains("UNKNOWNKEY"));
            Assert.DoesNotContain(warnings, w => w.Contains("CONTUR_SURROUND"));
        }

        [Fact]
        public void MalformedRows_WarnWithLineNumber_AndParsingContinues()
        {
            CadRegisterData data = SyntheticCad.Read(out List<string> warnings);

            // fewer values than declared fields (6 for 21)
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D \"501.5\"")}:") && w.Contains("POZEMLIMOTI") && w.Contains("6 стойности за 21 полета"));
            // more values than declared fields (22 for 21)
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D \"501.6\"")}:") && w.Contains("POZEMLIMOTI") && w.Contains("22 стойности за 21 полета"));
            // empty key field
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D \"\",3,1,5")}:") && w.Contains("POZEMLIMOTI") && w.Contains("IDENT"));
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D \"\",5,")}:") && w.Contains("PRAVA") && w.Contains("IDENT"));
            // a repeated parcel
            Assert.Contains(warnings, w =>
                w.Contains($"ред {F("D \"501.1\",3,1,5,9999")}:") && w.Contains("501.1"));
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
                SyntheticCad.FakeEgn1, SyntheticCad.FakeBulstat, SyntheticCad.FakeOddId, SyntheticCad.FakeCompoundId,
                SyntheticCad.FakeUnknownPerson, SyntheticCad.Person1Name, SyntheticCad.OddPersonName,
                SyntheticCad.CompoundPersonName, "АГРО", "ТЕСТОВ ", "BG"
            })
            {
                Assert.DoesNotContain(secret, all);
            }
        }

        [Fact]
        public void Fixture_GivesExactlyTheExpectedWarnings()
        {
            SyntheticCad.Read(out List<string> warnings);

            // 3 while reading (bad CONTUR_AREA, too few values, too many values) + 2 POZEMLIMOTI (empty IDENT, repeated
            // parcel) + 1 PRAVA (empty IDENT) + 1 unknown-person summary
            Assert.Equal(7, warnings.Count);
        }

        [Fact]
        public void TableWithoutItsKeyField_IsSkippedWithOneWarning()
        {
            string text = "HEADER\r\nEKATTE 11111\r\nEND_HEADER\r\nTABLE POZEMLIMOTI\r\nF VIDT S 1 0\r\nF NTP S 4 0\r\nD 3,2230\r\nD 4,2230\r\nEND_TABLE\r\n";
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
        public void ContentsPart_IsNotUsedToDecideAboutData()
        {
            // "CONTENTS PART" also appears in extracts WITH data; only the rows count
            CadRegisterData data = SyntheticCad.Read(out _);

            Assert.NotEmpty(data.Parcels);
        }

        [Fact]
        public void NoParcelRows_GiveAnEmptyRegister()
        {
            string text = "HEADER\r\nVERSION 4.02\r\nEKATTE 06433\r\nCONTENTS PART\r\nEND_HEADER\r\nTABLE POZEMLIMOTI\r\nF IDENT C 20 0 1\r\nEND_TABLE\r\n";

            CadRegisterData data = new CadRegisterReader().Read(MikTestEncoder.Encode(text));

            Assert.Empty(data.Parcels);
            Assert.Equal("06433", data.Ekatte);
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
            bytes[bytes.Length - 3] = 0xE5; // one byte in 0xC0..0xFF (not the numero sign)
            var warnings = new List<string>();

            new CadRegisterReader(warnings.Add).Read(bytes);

            Assert.Single(warnings, w => w.Contains("1 байта"));
        }

        [Theory]
        [InlineData("a,b,c", new[] { "a", "b", "c" })]
        [InlineData(" a , b ,c ", new[] { "a", "b", "c" })]
        [InlineData("a,,c,", new[] { "a", "", "c", "" })]
        [InlineData("\"a,b\",c", new[] { "a,b", "c" })]
        [InlineData("\"\",x", new[] { "", "x" })]
        [InlineData("x,\"\"", new[] { "x", "" })]
        [InlineData("\"x \"y\" z\",1", new[] { "x \"y\" z", "1" })]
        [InlineData("6,\"ОБЩИНСКА СЛУЖБА \"ЗГ\"ГР.Ч.БРЯГ\",08.05.2016,", new[] { "6", "ОБЩИНСКА СЛУЖБА \"ЗГ\"ГР.Ч.БРЯГ", "08.05.2016", "" })]
        [InlineData("\"000414154\",4,F,06.06.2023,,", new[] { "000414154", "4", "F", "06.06.2023", "", "" })]
        [InlineData("'q',w", new[] { "'q'", "w" })]
        [InlineData("\"7497_0006082776\",\"8690П\"", new[] { "7497_0006082776", "8690П" })]
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
