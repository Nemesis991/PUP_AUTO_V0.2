using System.Text;
using PUP_AUTO.CadRegister;

namespace PUP_AUTO.Tests
{
    /// <summary>MIK encoder for tests only (the plugin never writes MIK): the inverse of <see cref="MikEncoding"/>.</summary>
    internal static class MikTestEncoder
    {
        public static byte[] Encode(string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 0x80) bytes[i] = (byte)c;
                else if (c >= 'А' && c <= 'Я') bytes[i] = (byte)(0x80 + (c - 'А'));
                else if (c >= 'а' && c <= 'я') bytes[i] = (byte)(0xA0 + (c - 'а'));
                else throw new ArgumentException($"Character U+{(int)c:X4} is not in MIK (position {i}).");
            }
            return bytes;
        }
    }

    /// <summary>
    /// A small hand-made synthetic AGKK .cad (format 4.02). Fake people, fake IDs, fake numbers — no real data.
    /// It is kept as readable text and encoded to MIK bytes by the tests. Lines end with CRLF like the real files.
    /// </summary>
    internal static class SyntheticCad
    {
        public const string Ekatte = "06433";

        // Fake ЕГН/БУЛСТАТ: leading zeros and a non-numeric value must survive as text
        public const string FakeEgn1 = "0000000001";
        public const string FakeBulstat = "000123456";
        public const string FakeOddId = "8690П";
        public const string FakeUnknownPerson = "9999999999";

        public const string Person1Name = "ТЕСТОВ ФИКТИВЕН ПЕТРОВ";
        public const string FirmNameRaw = "\\АГРО\\ ЕООД";
        public const string FirmNameUnescaped = "„АГРО“ ЕООД";
        public const string OddPersonName = "ФИКТИВНА ТЕСТОВА";

        public const string Text =
@"HEADER
VERSION    4.02
EKATTE     06433
NAME       с.ТЕСТОВО
PROGRAM    MKADWIN V6.29
UNKNOWNKEY something that is not known
END_HEADER


LAYER CADASTER
L  13     1 0 09.07.2026 0
28000115  5362.413   732.664 11  0  0; 28000117  5360.874   739.570 11  0  0;
T  17   1 5357.303  721.324   1.6 09.07.2026 0   13.964 CD
""Имот 501.1""
END_LAYER

CONTROL CADASTER
NUMBER_POINTS       0
NUMBER_LINES       42
CONTUR_AREA 501.1 1200.500
CONTUR_AREA 501.2 800.000
CONTUR_AREA 501.4 55.5
CONTUR_AREA 501.9 not-a-number
CONTUR_SURROUND 501.1 501.2
END_CONTROL

LAYER SOMEOTHERLAYER
END_LAYER

CONTROL SOMEOTHERLAYER
CONTUR_AREA 501.1 7777.000
END_CONTROL

TABLE SOMEUNKNOWNTABLE
F FOO C 5 0 1
D a,b,c,d,e,f,g
END_TABLE

TABLE POZEMLIMOTI
F IDENT    C  20 0 1
F KAT      S  2 0
F VIDT     S  1 0 2
F BRANDNEW S  4 0
F NTP      S  4 0 2
F MESTNOST S  4 0 3 MESTNOSTI
F VIDS     S  2 0 2
D 501.1,8,3,x,2230,17,5
D 501.2,0,3,x,2500,17,11
D 501.3,,1,,,,
D 501.4,4,3,x,2230,99,5
D ,5,3,x,2230,17,5
D 501.5,5,3,x,2230,17,5,extra
D 501.1,9,9,x,9999,17,5
END_TABLE

TABLE PRAVA
F IDENT    C  20 0 1
F VIDS     S  2 0 2
F PERSON   C 13 0 3 PERSONS
F DOCID1   S  3 0
F DOCID2   N  8 3
F PRAVOVID S  2 0 2
D 501.1,5,0000000001,1,2,1
D 501.1,5,000123456,1,2,1
D 501.2,11,8690П,1,1,3
D 501.4,5,9999999999,,,1
D 501.1.1,5,0000000001,1,1,1
D ,5,0000000001,1,1,1
END_TABLE

TABLE MESTNOSTI
F MESTNOST  S  4 0 1
F NAME      C  20 0
F BEG_DATE   D  10 0
D 17,ТЕСТОВА МЕСТНОСТ,
D 18,ДРУГА МЕСТНОСТ,
END_TABLE

TABLE PERSONS
F PERSON   C 13 0 1
F NAME     C 45 0
F ADDR     C 50 0
D 0000000001,ТЕСТОВ ФИКТИВЕН ПЕТРОВ,УЛ. ПЪРВА 1
D 0000000001,ТЕСТОВ ФИКТИВЕН ПЕТРОВ,УЛ. ВТОРА 2
D 000123456,\АГРО\ ЕООД,УЛ. ТРЕТА 3
D 8690П,ФИКТИВНА ТЕСТОВА,
END_TABLE
";

        /// <summary>The fixture with CRLF line endings, as MIK bytes.</summary>
        public static byte[] Bytes() => MikTestEncoder.Encode(Text.Replace("\r\n", "\n").Replace("\n", "\r\n"));

        /// <summary>1-based line number of the first line that contains <paramref name="snippet"/>.</summary>
        public static int LineOf(string snippet, int occurrence = 1)
        {
            string[] lines = Text.Replace("\r\n", "\n").Split('\n');
            int seen = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(snippet) && ++seen == occurrence) return i + 1;
            }
            throw new InvalidOperationException($"'{snippet}' is not in the fixture.");
        }

        public static CadRegisterData Read(out List<string> warnings)
        {
            var collected = new List<string>();
            CadRegisterData data = new CadRegisterReader(collected.Add).Read(Bytes());
            warnings = collected;
            return data;
        }
    }
}
