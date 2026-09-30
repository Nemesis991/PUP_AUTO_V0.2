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
                else if (c == '№') bytes[i] = 0xD5;
                else throw new ArgumentException($"Character U+{(int)c:X4} is not in MIK (position {i}).");
            }
            return bytes;
        }
    }

    /// <summary>
    /// A small hand-made synthetic AGKK .cad (format 4.02) in the row format of a real КАИС export: values comma
    /// separated in F order, strings in double quotes, empty numbers/dates as nothing, a trailing comma for the empty
    /// last field. Fake people, fake IDs, fake numbers — no real data. It is kept as readable text and encoded to MIK
    /// bytes by the tests. Lines end with CRLF like the real files.
    /// </summary>
    internal static class SyntheticCad
    {
        public const string Ekatte = "06433";

        // Fake ЕГН/БУЛСТАТ: leading zeros, a non-numeric value and a compound id must survive as text
        public const string FakeEgn1 = "0000000001";
        public const string FakeBulstat = "000123456";
        public const string FakeOddId = "8690П";
        public const string FakeCompoundId = "7497_0006082776";
        public const string FakeUnknownPerson = "9999999999";

        public const string Person1Name = "ТЕСТОВ ФИКТИВЕН ПЕТРОВ";
        public const string FirmNameUnescaped = "\"АГРО\" ЕООД";
        public const string OddPersonName = "ФИКТИВНА ТЕСТОВА";
        public const string CompoundPersonName = "ФИКТИВЕН ДРУГ";

        public const string Text =
@"HEADER
VERSION 4.02
EKATTE 06433
NAME с. Тестово
PROGRAM KAIS
UNKNOWNKEY something that is not known
CONTENTS PART
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
D ""a"",b,c,d,e,f,g,
END_TABLE

TABLE IZDATELI
F DOCIZD  S  4 0 1
F IME     C  30 0
F BEG_DATE   D  10 0
F END_DATE   D  10 0
D 6,""ОБЩИНСКА СЛУЖБА ""ЗГ""ГР.Ч.БРЯГ"",08.05.2016,
END_TABLE

TABLE POZEMLIMOTI
F IDENT    C  20 0 1
F VIDT     S  1 0 2
F VIDTOLD  S  1 0 2
F VIDS     S  2 0 2
F NTP      S  4 0 2
F NTPOLD   S  4 0 2
F MESTNOST S  4 0 3 MESTNOSTI
F PARTIDA  C 20 0
F ADDRCODE L 10 0 3 ADDRESS
F NOMER1   C 10 0
F KVARTAL  C 10 0
F PARCEL   C 10 0
F GODCAD   N  4 0
F GODREG   N  4 0
F CODZAP   S  4 0 3 ZAPOVEDI
F ZACON    S  2 0 2
F KAT      S  2 0
F NVAST    S  1 0 2
F VAVOD    B  1 0
F BEG_DATE D 10 0
F END_DATE D 10 0
D ""501.1"",3,1,5,2230,1700,17,"""",2,""050101"","""","""",,,206903,,8,,,29.03.1999,
D ""501.2"",3,1,11,2500,1700,17,"""",2,""050102"","""","""",,,206903,,0,,,29.03.1999,
D ""501.3"",1,1,3,,,,"""",,"""","""","""",,,,,,,,,
D ""501.4"",3,1,5,2230,1700,99,"""",2,""050104"","""","""",,,206903,,4,,,29.03.1999,
D """",3,1,5,2230,1700,17,"""",2,""050105"","""","""",,,206903,,5,,,29.03.1999,
D ""501.5"",3,1,5,2230,
D ""501.6"",3,1,5,2230,1700,17,"""",2,""050106"","""","""",,,206903,,5,,,29.03.1999,,
D ""501.1"",3,1,5,9999,1700,17,"""",2,""050101"","""","""",,,206903,,9,,,29.03.1999,
END_TABLE

TABLE PRAVA
F IDENT    C  20 0 1
F VIDS     S  2 0 2
F PERSON   C 13 0 3 PERSONS
F DOCCOD   L 10 0 3 DOCS
F DOCID1   S  3 0
F DOCID2   N  8 3
F PLDOC    N 11 3
F PTYPE    S  2 0 2
F PRAVOVID S  2 0 2
F SROK     D 10 0
F DOCIDENT C 30 0
F DOP      B  1 0
F BEG_DATE D 10 0
F END_DATE D 10 0
F END_TIME T  5 0
D ""501.1"",5,""0000000001"",1001,1,2,,0,1,,"""",F,06.06.2023,,
D ""501.1"",5,""000123456"",1002,1,2,,0,1,,"""",F,06.06.2023,,
D ""501.2"",11,""8690П"",1003,1,1,,0,3,,"""",F,06.06.2023,,
D ""501.2"",11,""7497_0006082776"",1007,1,2,,0,1,,"""",F,06.06.2023,,
D ""501.4"",5,""9999999999"",1004,,,,0,1,,"""",F,06.06.2023,,
D ""501.1.1"",5,""0000000001"",1005,1,1,,0,1,,"""",F,06.06.2023,,
D """",5,""0000000001"",1006,1,1,,0,1,,"""",F,06.06.2023,,
END_TABLE

TABLE MESTNOSTI
F MESTNOST  S  4 0 1
F NAME      C  20 0
F BEG_DATE   D  10 0
F END_DATE   D  10 0
D 17,""ТЕСТОВА МЕСТНОСТ"",29.03.1999,
D 18,""ДРУГА МЕСТНОСТ"",29.03.1999,
END_TABLE

TABLE PERSONS
F PERSON   C 13 0 1
F SUBTYPE  S  1 0 2
F NAME     C 45 0
F NSTATE   C  2 0 2
F ADDRCODE L 10 0 3 ADDRESS
F ADDR     C 50 0
F ADDRET   C  4 0
F ADDRAP   C  4 0
F FLAG     B  1 0
F SPERSON  C 10 0
F FIRMREG  C 50 0
F BEG_DATE D 10 0
F END_DATE D 10 0
D ""0000000001"",1,""ТЕСТОВ ФИКТИВЕН ПЕТРОВ"",""BG"",11,"""","""","""",F,"""","""",20.03.2018,
D ""0000000001"",1,""ТЕСТОВ ФИКТИВЕН ПЕТРОВ"",""BG"",12,"""","""","""",F,"""","""",21.03.2019,
D ""000123456"",4,""\АГРО\ ЕООД"",""BG"",13,"""","""","""",F,"""","""",20.03.2018,
D ""8690П"",1,""ФИКТИВНА ТЕСТОВА"",""BG"",14,"""","""","""",F,"""","""",20.03.2018,
D ""7497_0006082776"",1,""ФИКТИВЕН ДРУГ"",""BG"",15,"""","""","""",T,"""","""",20.03.2018,
END_TABLE

TABLE GORIMOTI
F IDENT    C 20 0 3 POZEMLIMOTI
F DL       C 2 0 2
F OTDEL    S 4 0
F PODOTDEL C 4 0
F AREA     N 8 0
F BEG_DATE D 10 0
F END_DATE D 10 0
D ""501.4"",""01"",45,""а"",50,29.03.1999,
D ""501.4"",""01"",46,""б"",5,29.03.1999,
D ""501.2"",""01"",7,"""",800,29.03.1999,
D ""999.9"",""01"",1,""в"",1,29.03.1999,
END_TABLE
";

        /// <summary>
        /// Rows copied from a real КАИС export (06433), with the field declarations of the real tables.
        /// The municipality's БУЛСТАТ is a public identifier, not personal data.
        /// </summary>
        public const string RealRows =
@"HEADER
VERSION 4.02
EKATTE 06433
NAME с. Бресте
CONTENTS PART
END_HEADER

CONTROL CADASTER
CONTUR_AREA 54.1 90657.681
END_CONTROL

TABLE POZEMLIMOTI
F IDENT    C  20 0 1
F VIDT     S  1 0 2
F VIDTOLD  S  1 0 2
F VIDS     S  2 0 2
F NTP      S  4 0 2
F NTPOLD   S  4 0 2
F MESTNOST S  4 0 3 MESTNOSTI
F PARTIDA  C 20 0
F ADDRCODE L 10 0 3 ADDRESS
F NOMER1   C 10 0
F KVARTAL  C 10 0
F PARCEL   C 10 0
F GODCAD   N  4 0
F GODREG   N  4 0
F CODZAP   S  4 0 3 ZAPOVEDI
F ZACON    S  2 0 2
F KAT      S  2 0
F NVAST    S  1 0 2
F VAVOD    B  1 0
F BEG_DATE D 10 0
F END_DATE D 10 0
D ""54.1"",3,1,3,2800,1400,7,"""",2,""054001"","""","""",,,206903,,8,,,29.03.1999,
D ""61.363"",3,1,3,2230,1700,,"""",,""000363"","""","""",,,206903,,0,,,31.05.2018,
END_TABLE

TABLE MESTNOSTI
F MESTNOST  S  4 0 1
F NAME      C  20 0
F BEG_DATE   D  10 0
F END_DATE   D  10 0
D 7,""СТРАНАТА"",29.03.1999,
END_TABLE

TABLE PRAVA
F IDENT    C  20 0 1
F VIDS     S  2 0 2
F PERSON   C 13 0 3 PERSONS
F DOCCOD   L 10 0 3 DOCS
F DOCID1   S  3 0
F DOCID2   N  8 3
F PLDOC    N 11 3
F PTYPE    S  2 0 2
F PRAVOVID S  2 0 2
F SROK     D 10 0
F DOCIDENT C 30 0
F DOP      B  1 0
F BEG_DATE D 10 0
F END_DATE D 10 0
F END_TIME T  5 0
D ""54.1"",3,""000414154"",14320341,1,1,,0,1,,"""",F,06.06.2023,,
END_TABLE

TABLE PERSONS
F PERSON   C 13 0 1
F SUBTYPE  S  1 0 2
F NAME     C 45 0
F NSTATE   C  2 0 2
F ADDRCODE L 10 0 3 ADDRESS
F ADDR     C 50 0
F ADDRET   C  4 0
F ADDRAP   C  4 0
F FLAG     B  1 0
F SPERSON  C 10 0
F FIRMREG  C 50 0
F BEG_DATE D 10 0
F END_DATE D 10 0
D ""000414154"",4,""ОБЩИНА ЧЕРВЕН БРЯГ"",""BG"",12,"""","""","""",F,"""","""",20.03.2018,
D ""000414154"",4,""ОБЩИНА ЧЕРВЕН БРЯГ"",""BG"",13,"""","""","""",F,"""","""",01.02.2020,
END_TABLE
";

        /// <summary>The text with CRLF line endings, as MIK bytes.</summary>
        public static byte[] Bytes(string text = Text) => MikTestEncoder.Encode(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));

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

        public static CadRegisterData Read(out List<string> warnings, string text = Text)
        {
            var collected = new List<string>();
            CadRegisterData data = new CadRegisterReader(collected.Add).Read(Bytes(text));
            warnings = collected;
            return data;
        }
    }
}
