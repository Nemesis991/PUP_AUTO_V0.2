using PUP_AUTO.Semantics;
using Xunit;

namespace PUP_AUTO.Tests
{
    /// <summary>Characterisation tests: pin the CURRENT behaviour of ParcelData.</summary>
    public class ParcelDataTests
    {
        [Fact]
        public void Defaults_AreEmptyAndZero()
        {
            var p = new ParcelData();

            Assert.Equal(string.Empty, p.ParcelId);
            Assert.Equal(0.0, p.TotalAreaSqm);
            Assert.Equal(0.0, p.ServitudeGrossAreaSqm);
            Assert.Equal(0.0, p.ServitudeNetAreaSqm);
            Assert.Equal(0.0, p.PoleAreaSqm);
            Assert.Empty(p.AssignedPoleNumbers);
            Assert.Empty(p.IndividualPoleAreas);
        }

        [Fact]
        public void RemainderAreaSqm_IsTotalMinusNetMinusPole()
        {
            var p = new ParcelData { TotalAreaSqm = 10000.0, ServitudeNetAreaSqm = 4000.0, PoleAreaSqm = 36.0 };

            Assert.Equal(5964.0, p.RemainderAreaSqm);
        }

        [Fact]
        public void RemainderAreaSqm_UsesNetNotGrossServitude()
        {
            var p = new ParcelData
            {
                TotalAreaSqm = 100.0,
                ServitudeGrossAreaSqm = 90.0,
                ServitudeNetAreaSqm = 10.0,
                PoleAreaSqm = 0.0
            };

            Assert.Equal(90.0, p.RemainderAreaSqm);
        }

        [Fact]
        public void RemainderAreaSqm_IsClampedAtZero()
        {
            var p = new ParcelData { TotalAreaSqm = 100.0, ServitudeNetAreaSqm = 90.0, PoleAreaSqm = 20.0 };

            Assert.Equal(0.0, p.RemainderAreaSqm);
        }

        [Fact]
        public void RemainderAreaSqm_IsRawAndNotRounded()
        {
            var p = new ParcelData { TotalAreaSqm = 10.1234567, ServitudeNetAreaSqm = 0.0, PoleAreaSqm = 0.0 };

            Assert.Equal(10.1234567, p.RemainderAreaSqm);
        }
    }
}
