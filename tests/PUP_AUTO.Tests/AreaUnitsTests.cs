using System.Globalization;
using PUP_AUTO.Core;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Characterisation tests: pin the CURRENT behaviour of AreaUnits.</summary>
    public class AreaUnitsTests
    {
        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(1000.0, 1.0)]
        [InlineData(1234.5678, 1.235)]
        [InlineData(1234.5674, 1.235)]
        [InlineData(1234.4, 1.234)]
        public void SqmToDka_RoundsToThreeDecimals(double sqm, double expectedDka)
        {
            Assert.Equal(expectedDka, AreaUnits.SqmToDka(sqm));
        }

        [Theory]
        [InlineData(0.5, 0.001)]     // midpoint rounds away from zero, not to even
        [InlineData(1.5, 0.002)]
        [InlineData(2.5, 0.003)]
        [InlineData(3.5, 0.004)]
        [InlineData(-1.5, -0.002)]
        [InlineData(-2.5, -0.003)]
        public void SqmToDka_MidpointRoundsAwayFromZero(double sqm, double expectedDka)
        {
            Assert.Equal(expectedDka, AreaUnits.SqmToDka(sqm));
        }

        [Fact]
        public void SqmToDka_BelowHalfOfLastDecimalRoundsToZero()
        {
            Assert.Equal(0.0, AreaUnits.SqmToDka(0.4));
        }

        [Theory]
        [InlineData(0.0, "0.000")]
        [InlineData(1000.0, "1.000")]
        [InlineData(1234567.0, "1234.567")]
        [InlineData(2.5, "0.003")]
        [InlineData(0.4, "0.000")]
        public void FormatDka_HasExactlyThreeDecimals(double sqm, string expected)
        {
            Assert.Equal(expected, AreaUnits.FormatDka(sqm));
        }

        [Fact]
        public void FormatDka_IsInvariantUnderBulgarianCurrentCulture()
        {
            CultureInfo savedCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo savedUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var bg = new CultureInfo("bg-BG", false); // false: ignore the machine's decimal-symbol override
                Assert.Equal(",", bg.NumberFormat.NumberDecimalSeparator); // precondition of this test

                Thread.CurrentThread.CurrentCulture = bg;
                Thread.CurrentThread.CurrentUICulture = bg;

                Assert.Equal("1234.567", AreaUnits.FormatDka(1234567.0));
                Assert.Equal("0.036", AreaUnits.FormatDka(36.0));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = savedCulture;
                Thread.CurrentThread.CurrentUICulture = savedUiCulture;
            }
        }

        [Fact]
        public void DkaDecimals_IsThree()
        {
            Assert.Equal(3, AreaUnits.DkaDecimals);
        }

        [Fact]
        public void OutputCulture_DefaultsToInvariant()
        {
            Assert.Same(CultureInfo.InvariantCulture, AreaUnits.OutputCulture);
        }
    }
}
