using System.Text;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;
using Xunit;

namespace PUP_AUTO.Tests
{
    public class NomenclatureTests : IDisposable
    {
        private readonly string _dir;

        public NomenclatureTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private string Write(string name, string content, bool bom = true)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, content, new UTF8Encoding(bom));
            return path;
        }

        [Fact]
        public void Load_ReadsCodeAndText_SkippingHeaderCommentsAndEmptyLines()
        {
            string path = Write("VIDT.csv", "код;текст\r\n\r\n# коментар\r\n1;Селскостопански\r\n3 ; Урбанизирана; територия \r\n");
            var warnings = new List<string>();

            Nomenclature n = Nomenclature.Load("VIDT", path, warnings.Add);

            Assert.Equal(2, n.Count);
            Assert.True(n.TryGet("1", out string t1));
            Assert.Equal("Селскостопански", t1);
            Assert.True(n.TryGet("3", out string t3));
            Assert.Equal("Урбанизирана; територия", t3); // only the first ';' separates
            Assert.Empty(warnings);
        }

        [Fact]
        public void Load_AcceptsEnglishHeader_AndFilesWithoutBom()
        {
            string path = Write("A.csv", "code;text\r\n7;Седем\r\n", bom: false);

            Nomenclature n = Nomenclature.Load("A", path);

            Assert.Equal(1, n.Count);
            Assert.Equal("7 – Седем", n.Describe("7"));
        }

        [Fact]
        public void Load_QuotedCells_AreUnquoted()
        {
            string path = Write("A.csv", "\"код\";\"текст\"\r\n\"2230\";\"Нива; \"\"обработваема\"\"\"\r\n");

            Nomenclature n = Nomenclature.Load("A", path);

            Assert.True(n.TryGet("2230", out string text));
            Assert.Equal("Нива; \"обработваема\"", text);
        }

        [Fact]
        public void HeaderOnlyFile_IsEmpty_WithOneWarning()
        {
            string path = Write("VIDT.csv", "код;текст\r\n");
            var warnings = new List<string>();

            Nomenclature n = Nomenclature.Load("VIDT", path, warnings.Add);

            Assert.Equal(0, n.Count);
            Assert.Single(warnings);
            Assert.Contains("празна", warnings[0]);
        }

        [Fact]
        public void MissingFile_IsOneWarning_AndAnEmptyNomenclature()
        {
            var warnings = new List<string>();

            Nomenclature n = Nomenclature.Load("NTP", Path.Combine(_dir, "NTP.csv"), warnings.Add);

            Assert.Equal(0, n.Count);
            Assert.Single(warnings);
            Assert.Contains("липсва", warnings[0]);
        }

        [Fact]
        public void BadLines_AreWarned_AndSkipped()
        {
            string path = Write("A.csv", "1;Едно\r\nбез разделител\r\n;без код\r\n1;Повторение\r\n2;Две\r\n");
            var warnings = new List<string>();

            Nomenclature n = Nomenclature.Load("A", path, warnings.Add);

            Assert.Equal(2, n.Count);
            Assert.Equal("1 – Едно", n.Describe("1")); // the first entry of a repeated code wins
            Assert.Contains(warnings, w => w.Contains("ред 2") && w.Contains("разделител"));
            Assert.Contains(warnings, w => w.Contains("ред 3") && w.Contains("празен код"));
            Assert.Contains(warnings, w => w.Contains("ред 4") && w.Contains("повторен код 1"));
        }

        [Fact]
        public void UnknownCode_ShowsKodN_AndWarnsOncePerCode()
        {
            var warnings = new List<string>();
            var n = new Nomenclature("NTP", warnings.Add);
            n.Add("2230", "Нива");

            Assert.Equal("код 7", n.Describe("7"));
            Assert.Equal("код 7", n.Describe("7"));
            Assert.Equal("код 7", n.Describe(" 7 "));
            Assert.Equal("код 8", n.Describe("8"));
            Assert.Equal("2230 – Нива", n.Describe("2230"));

            Assert.Equal(2, warnings.Count); // once for 7, once for 8
            Assert.Contains("код 7", warnings[0]);
            Assert.Contains("NTP", warnings[0]);
            Assert.Contains("код 8", warnings[1]);
        }

        [Fact]
        public void EmptyCode_GivesEmptyText_AndNoWarning()
        {
            var warnings = new List<string>();
            var n = new Nomenclature("VIDT", warnings.Add);

            Assert.Equal("", n.Describe(""));
            Assert.Equal("", n.Describe("   "));
            Assert.Empty(warnings);
        }

        [Fact]
        public void LeadingZeros_DoNotChangeTheCode()
        {
            var n = new Nomenclature("VIDS");
            n.Add("5", "Частна");
            n.Add("0", "Няма данни");

            Assert.Equal("05 – Частна", n.Describe("05"));
            Assert.Equal("00 – Няма данни", n.Describe("00"));
        }

        [Fact]
        public void Nomenclatures_LoadsAllFourFilesFromAFolder()
        {
            Write("VIDT.csv", "код;текст\r\n3;ТП три\r\n");
            Write("NTP.csv", "код;текст\r\n2230;НТП\r\n");
            Write("VIDS.csv", "код;текст\r\n5;Частна\r\n");
            Write("PRAVOVID.csv", "код;текст\r\n1;Собственост\r\n");

            Nomenclatures all = Nomenclatures.Load(_dir);

            Assert.Equal("3 – ТП три", all.Vidt.Describe("3"));
            Assert.Equal("2230 – НТП", all.Ntp.Describe("2230"));
            Assert.Equal("5 – Частна", all.Vids.Describe("5"));
            Assert.Equal("1 – Собственост", all.PravoVid.Describe("1"));
        }

        [Fact]
        public void EmbeddedDefaults_AreUsedWithoutAnyFile_AndSeededWithTheVerifiedValues()
        {
            var warnings = new List<string>();

            Nomenclatures all = Nomenclatures.Load(Path.Combine(_dir, "no-such-folder"), warnings.Add);

            Assert.Equal("3 – Земеделска територия", all.Vidt.Describe("3"));
            Assert.Equal("2800 – Пасище", all.Ntp.Describe("2800"));
            Assert.Equal("2230 – За селскостопански, горски, ведомствен път", all.Ntp.Describe("2230"));
            Assert.Equal("3 – Общинска публична", all.Vids.Describe("3"));
            Assert.Equal("1 – Собственост", all.PravoVid.Describe("1"));

            Assert.Empty(warnings); // a missing folder is not a warning
        }

        [Fact]
        public void EmbeddedDefaults_HoldTheVerifiedOfficialRegisterValues()
        {
            Nomenclatures all = Nomenclatures.Load(Path.Combine(_dir, "no-such-folder"));

            string[,] vidt = { { "2", "Територия за транспорт" }, { "3", "Земеделска територия" }, { "4", "Горска територия" }, { "5", "Води и водни площи" } };
            for (int i = 0; i < vidt.GetLength(0); i++) Assert.Equal(vidt[i, 1], all.Vidt.TextOf(vidt[i, 0]));

            string[,] ntp =
            {
                { "2220", "За местен път" }, { "2230", "За селскостопански, горски, ведомствен път" }, { "2500", "Нива" },
                { "2700", "Ливада" }, { "2800", "Пасище" }, { "2900", "Иглолистна гора" }, { "2910", "Широколистна гора" },
                { "3040", "Друг вид недървопроизводителна горска площ" }, { "3100", "Водно течение, река" },
                { "3200", "Отводнителен канал" }, { "3240", "За водностопанско, хидромелиоративно съоръжение" },
                { "3250", "За друг вид водно течение, водна площ, съоръжение" }
            };
            for (int i = 0; i < ntp.GetLength(0); i++) Assert.Equal(ntp[i, 1], all.Ntp.TextOf(ntp[i, 0]));

            string[,] vids =
            {
                { "1", "Държавна публична" }, { "2", "Държавна частна" }, { "3", "Общинска публична" },
                { "4", "Общинска частна" }, { "5", "Частна" }, { "7", "Обществени организации" }, { "11", "Съсобственост" }
            };
            for (int i = 0; i < vids.GetLength(0); i++) Assert.Equal(vids[i, 1], all.Vids.TextOf(vids[i, 0]));

            Assert.Equal("Собственост", all.PravoVid.TextOf("1"));
        }

        [Fact]
        public void TextOf_IsTheTextAlone_KodNForUnknown_EmptyForEmpty()
        {
            var warnings = new List<string>();
            var n = new Nomenclature("VIDS", warnings.Add);
            n.Add("5", "Частна");

            Assert.Equal("Частна", n.TextOf("5"));
            Assert.Equal("код 6", n.TextOf("6"));
            Assert.Equal("код 6", n.TextOf("6"));
            Assert.Equal("", n.TextOf(""));
            Assert.Single(warnings);
        }

        [Fact]
        public void FileInTemplates_OverridesMatchingCodes_AndKeepsTheOtherDefaults()
        {
            Write("VIDT.csv", "код;текст\r\n3;Земеделска (моя)\r\n9;Девет\r\n");
            Write("NTP.csv", "код;текст\r\n2800;Пасище, мера\r\n");

            Nomenclatures all = Nomenclatures.Load(_dir);

            Assert.Equal("3 – Земеделска (моя)", all.Vidt.Describe("3"));
            Assert.Equal("9 – Девет", all.Vidt.Describe("9"));
            Assert.Equal("2800 – Пасище, мера", all.Ntp.Describe("2800"));
            Assert.Equal("2230 – За селскостопански, горски, ведомствен път", all.Ntp.Describe("2230")); // default stays
        }

        [Fact]
        public void HeaderOnlyFile_DoesNotBlankTheEmbeddedDefaults()
        {
            Write("VIDT.csv", "код;текст\r\n");
            Write("NTP.csv", "код;текст\r\n");
            Write("VIDS.csv", "код;текст\r\n");
            var warnings = new List<string>();

            Nomenclatures all = Nomenclatures.Load(_dir, warnings.Add);

            Assert.Equal("3 – Земеделска територия", all.Vidt.Describe("3"));
            Assert.Equal("2800 – Пасище", all.Ntp.Describe("2800"));
            Assert.Equal("3 – Общинска публична", all.Vids.Describe("3"));
            Assert.DoesNotContain(warnings, w => w.Contains("VIDT") || w.Contains("NTP") || w.Contains("VIDS"));
        }

        [Fact]
        public void BadLineInAnOverrideFile_IsWarnedWithItsLine_AndTheRestStillApplies()
        {
            Write("NTP.csv", "код;текст\r\nбез разделител\r\n2800;Мера\r\n");
            var warnings = new List<string>();

            Nomenclatures all = Nomenclatures.Load(_dir, warnings.Add);

            Assert.Contains(warnings, w => w.Contains("NTP") && w.Contains("ред 2"));
            Assert.Equal("2800 – Мера", all.Ntp.Describe("2800"));
        }

        [Fact]
        public void EmbeddedNomenclatureFiles_ArePresentInTheAssembly()
        {
            foreach (string file in new[] { Nomenclatures.VidtFile, Nomenclatures.NtpFile, Nomenclatures.VidsFile, Nomenclatures.PravoVidFile })
            {
                Assert.NotNull(EmbeddedDefaults.ReadText(file));
            }
            Assert.Null(EmbeddedDefaults.ReadText("NOSUCHFILE.csv"));
        }

        [Theory]
        [InlineData("1", "I")]
        [InlineData("4", "IV")]
        [InlineData("8", "VIII")]
        [InlineData("9", "IX")]
        [InlineData("10", "X")]
        [InlineData("08", "VIII")]
        [InlineData(" 6 ", "VI")]
        [InlineData("3999", "MMMCMXCIX")]
        [InlineData("0", "")]
        [InlineData("00", "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("4000", "4000")]
        [InlineData("X", "X")]
        [InlineData("8а", "8а")]
        [InlineData("-1", "-1")]
        public void Category_NumberBecomesRoman_ZeroAndEmptyBecomeEmpty(string kat, string expected)
        {
            Assert.Equal(expected, CategoryFormat.Format(kat));
        }
    }

    public class EkatteRegisterTests : IDisposable
    {
        private readonly string _dir;

        public EkatteRegisterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "PUP_AUTO_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private string Write(string content)
        {
            string path = Path.Combine(_dir, "EKATTE.csv");
            File.WriteAllText(path, content, new UTF8Encoding(true));
            return path;
        }

        [Fact]
        public void Title_HasKindNameEkatteMunicipalityAndProvince_InCapitals()
        {
            EkatteRegister register = EkatteRegister.Load(Write(
                "ЕКАТТЕ;вид;име;община;област\r\n06433;с.;Бресте;Червен бряг;Плевен\r\n80501;гр.;Червен бряг;Червен бряг;Плевен\r\n"));

            Assert.Equal(2, register.Count);
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. БРЕСТЕ, ЕКАТТЕ 06433, ОБЩ. ЧЕРВЕН БРЯГ, ОБЛ. ПЛЕВЕН",
                register.FormatTitle("06433", "с.БРЕСТЕ"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА ГР. ЧЕРВЕН БРЯГ, ЕКАТТЕ 80501, ОБЩ. ЧЕРВЕН БРЯГ, ОБЛ. ПЛЕВЕН",
                register.FormatTitle("80501", ""));
        }

        [Fact]
        public void Header_IsOptional()
        {
            EkatteRegister register = EkatteRegister.Load(Write("06433;с.;Бресте;Червен бряг;Плевен\r\n"));

            Assert.True(register.TryGet("06433", out EkatteEntry entry));
            Assert.Equal("Бресте", entry.Name);
        }

        [Fact]
        public void UnknownCode_FallsBackToTheHeaderName()
        {
            EkatteRegister register = EkatteRegister.Load(Write("06433;с.;Бресте;Червен бряг;Плевен\r\n"));

            Assert.Equal("НА ТЕРИТОРИЯТА НА С.ТЕСТОВО, ЕКАТТЕ 99999", register.FormatTitle("99999", "с.ТЕСТОВО"));
            Assert.Equal("НА ТЕРИТОРИЯТА НА ЕКАТТЕ 99999", register.FormatTitle("99999", ""));
        }

        [Fact]
        public void BadRows_AreWarnedAndSkipped()
        {
            var warnings = new List<string>();
            EkatteRegister register = EkatteRegister.Load(
                Write("ЕКАТТЕ;вид;име;община;област\r\n06433;с.;Бресте\r\nXX;с.;А;Б;В\r\n78135;с.;Царевец;Мездра;Враца\r\n"),
                warnings.Add);

            Assert.Equal(1, register.Count);
            Assert.Equal(2, warnings.Count);
            Assert.Contains(warnings, w => w.Contains("ред 2"));
            Assert.Contains(warnings, w => w.Contains("ред 3"));
        }

        [Fact]
        public void MissingFile_IsAWarning()
        {
            var warnings = new List<string>();

            EkatteRegister register = EkatteRegister.Load(Path.Combine(_dir, "nope.csv"), warnings.Add);

            Assert.Equal(0, register.Count);
            Assert.Single(warnings);
        }

        [Fact]
        public void EmbeddedRegister_HasTheSettlementsInUse_WithoutAnyFile()
        {
            var warnings = new List<string>();

            EkatteRegister register = EkatteRegister.LoadWithDefaults(Path.Combine(_dir, "nope.csv"), warnings.Add);

            Assert.Empty(warnings); // a missing override file is normal
            Assert.True(register.Count > 5000);
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. БРЕСТЕ, ЕКАТТЕ 06433, ОБЩ. ЧЕРВЕН БРЯГ, ОБЛ. ПЛЕВЕН", register.FormatTitle("06433", ""));
            Assert.Equal("НА ТЕРИТОРИЯТА НА ГР. ЧЕРВЕН БРЯГ, ЕКАТТЕ 80501, ОБЩ. ЧЕРВЕН БРЯГ, ОБЛ. ПЛЕВЕН", register.FormatTitle("80501", ""));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА", register.FormatTitle("78135", ""));
        }

        [Fact]
        public void FileInTemplates_OverridesMatchingCodes_AndKeepsTheEmbeddedRest()
        {
            EkatteRegister register = EkatteRegister.LoadWithDefaults(Write(
                "ЕКАТТЕ;вид;име;община;област\r\n06433;с.;Нова Бресте;Друга;Друга област\r\n99999;с.;Ново;Общ;Обл\r\n"));

            Assert.Equal("НА ТЕРИТОРИЯТА НА С. НОВА БРЕСТЕ, ЕКАТТЕ 06433, ОБЩ. ДРУГА, ОБЛ. ДРУГА ОБЛАСТ", register.FormatTitle("06433", ""));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. НОВО, ЕКАТТЕ 99999, ОБЩ. ОБЩ, ОБЛ. ОБЛ", register.FormatTitle("99999", ""));
            Assert.Equal("НА ТЕРИТОРИЯТА НА С. ЦАРЕВЕЦ, ЕКАТТЕ 78135, ОБЩ. МЕЗДРА, ОБЛ. ВРАЦА", register.FormatTitle("78135", ""));
        }
    }
}
