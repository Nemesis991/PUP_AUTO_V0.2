using System.Globalization;
using PUP_AUTO.Core;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Pins the string keys that are part of the output / lookup contract.</summary>
    public class NamesTests
    {
        [Fact]
        public void FileNames_AreByteIdentical()
        {
            Assert.Equal("PUP_AUTO_Logs.txt", FileNames.LogFile);
            Assert.Equal("_TestFiles", FileNames.TestFilesFolder);
            Assert.Equal("_Templates", FileNames.TemplatesFolder);
            Assert.Equal("TemplateC.cad", FileNames.CadLibraryFile);
            Assert.Equal("PUP_Report.xls", FileNames.ReportXlsFile);
            Assert.Equal("MVP_Math_Test_Parcels.xlsx", FileNames.MvpMathTestFile);
            Assert.Equal("Контролна_справка_cad.xlsx", FileNames.CadControlReportFile);
            Assert.Equal("Регистър_на_стъпките_на_стълбовете.xlsx", FileNames.PoleStepsRegisterFile);
            Assert.Equal("Баланси_на_територията.xlsx", FileNames.TerritoryBalanceFile);
            Assert.Equal("Общ_баланс_за_общината.xlsx", FileNames.MunicipalityBalanceFile);
            Assert.Equal("Номенклатури", FileNames.NomenclaturesFolder);
            Assert.Equal("EKATTE.csv", FileNames.EkatteRegisterFile);
            Assert.Equal("Координатен_регистър_на_стъпките.xlsx", FileNames.CoordinateRegisterFile);
            Assert.Equal("Координатен_регистър_на_сервитута.xlsx", FileNames.ServitudeRegisterFile);
        }

        [Fact]
        public void ServitudePointBlockNames_AreByteIdentical()
        {
            Assert.Equal("SERV_TOCHKA", ServitudePointBlockNames.BlockName);
            Assert.Equal("S-Trass-сервитут", ServitudePointBlockNames.Layer);
            Assert.Equal("NOMER", ServitudePointBlockNames.NumberTag);
            Assert.Equal("Номер на точка", ServitudePointBlockNames.NumberPrompt);
            Assert.Equal("NUM_Align", ServitudePointBlockNames.TextStyle);
            Assert.Equal("simplex.shx", ServitudePointBlockNames.TextStyleFont);
            Assert.Equal(0.65, ServitudePointBlockNames.TextStyleWidthFactor);
            Assert.Equal(3.0, ServitudePointBlockNames.TextHeight);
            Assert.Equal("PUP_AUTO", ServitudePointBlockNames.XDataApp);
            Assert.Equal("SERV_POINT", ServitudePointBlockNames.XDataValue);
        }

        [Theory]
        [InlineData("SERV_TOCHKA", true)]
        [InlineData("serv_tochka", true)]
        [InlineData("  SERV_TOCHKA  ", true)]
        [InlineData("GBP032", false)]
        [InlineData("SERV_TOCHKA_2", false)]
        [InlineData("", false)]
        public void AServitudePointBlock_IsRecognisedWhateverItsCaseOrPadding(string name, bool expected) =>
            Assert.Equal(expected, ServitudePointBlockNames.IsServitudePointBlock(name));

        [Fact]
        public void TheTwoMarkerBlocks_DoNotRecogniseEachOther()
        {
            Assert.False(PoleCornerBlockNames.IsCornerBlock(ServitudePointBlockNames.BlockName));
            Assert.False(ServitudePointBlockNames.IsServitudePointBlock(PoleCornerBlockNames.BlockName));
        }

        [Fact]
        public void XDataNames_AreByteIdentical()
        {
            Assert.Equal("TransCAD", XDataNames.RegApp);
            Assert.Equal("Неизвестен_Имот", XDataNames.UnknownParcel);
            Assert.Equal("Грешка_XData", XDataNames.XDataError);
        }

        [Fact]
        public void PluginLayers_AreByteIdentical()
        {
            Assert.Equal("POLE_STEPS", PluginLayers.PoleSteps);
            Assert.Equal("diagonali", PluginLayers.Diagonals);
            Assert.Equal("Текст", PluginLayers.Text);
            Assert.Equal("segmented SERV", PluginLayers.SegmentedServitude);
        }

        [Fact]
        public void PluginLayers_MatchIgnoringCase_AndOnlyExactNames()
        {
            Assert.True(PluginLayers.IsPluginLayer("pole_steps"));
            Assert.True(PluginLayers.IsPluginLayer("ТЕКСТ"));
            Assert.False(PluginLayers.IsPluginLayer("0"));
            Assert.False(PluginLayers.IsPluginLayer("POLE_STEPS2"));
        }

        [Fact]
        public void PoleAttributeTags_ListsAreByteIdentical()
        {
            Assert.Equal(new[] { "НОМЕР_НА_СТЪЛБА", "СТЪЛБ_№", "NOMER" }, PoleAttributeTags.PoleNumberTags);
            Assert.Equal(new[] { "P1", "P2", "P3", "P4" }, PoleAttributeTags.PointTags);
            Assert.Equal(new[] { "Visibility", "Visibility1", "Видимост", "Видимост1" }, PoleAttributeTags.VisibilityPropertyNames);
        }

        [Theory]
        [InlineData("НОМЕР_НА_СТЪЛБА")]
        [InlineData("номер_на_стълба")]
        [InlineData("СТЪЛБ_№")]
        [InlineData("стълб_№")]
        [InlineData("NOMER")]
        [InlineData("nomer")]
        [InlineData("Nomer")]
        public void IsPoleNumberTag_AcceptsRealTagsInAnyCase(string tag)
        {
            Assert.True(PoleAttributeTags.IsPoleNumberTag(tag));
        }

        [Theory]
        [InlineData("P1")]
        [InlineData("TP1")]
        [InlineData("НОМЕР")]
        [InlineData("")]
        public void IsPoleNumberTag_RejectsOtherTags(string tag)
        {
            Assert.False(PoleAttributeTags.IsPoleNumberTag(tag));
        }

        // The pole-selection code used ToUpper() (current culture) while the footprint extractor used
        // ToUpperInvariant(). Both now go through PoleAttributeTags; this proves the merge is safe
        // for the real tags under the cultures the plugin can run in.
        [Theory]
        [InlineData("bg-BG")]
        [InlineData("tr-TR")]
        [InlineData("en-US")]
        [InlineData("")]
        public void OldToUpper_And_InvariantAgree_ForRealTags(string cultureName)
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName, false);

                string[] realTags =
                {
                    "НОМЕР_НА_СТЪЛБА", "номер_на_стълба", "СТЪЛБ_№", "стълб_№", "NOMER", "nomer",
                    "P1", "p2", "P3", "P4", "P1-SOMETHING", "TP1", "tp2"
                };

                foreach (string tag in realTags)
                {
                    string oldSelection = tag.ToUpper();
                    Assert.Equal(oldSelection, PoleAttributeTags.Normalize(tag));

                    bool oldMatch = oldSelection == "НОМЕР_НА_СТЪЛБА" || oldSelection == "СТЪЛБ_№" || oldSelection == "NOMER";
                    Assert.Equal(oldMatch, PoleAttributeTags.IsPoleNumberTag(tag));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Theory]
        [InlineData("Visibility", true)]
        [InlineData("VISIBILITY1", true)]
        [InlineData("видимост", true)]
        [InlineData("Видимост1", true)]
        [InlineData("Visibility2", false)]
        [InlineData("Angle", false)]
        public void IsVisibilityProperty_MatchesIgnoringCase(string name, bool expected)
        {
            Assert.Equal(expected, PoleAttributeTags.IsVisibilityProperty(name));
        }
    }
}
