using AIO3.Core.Rotations.Paladin;
using Xunit;

namespace AIO3.Tests
{
    public class PaladinSpecsTests
    {
        [Fact]
        public void Auto_with_no_talents_defaults_to_Retribution()
        {
            Assert.Equal(PaladinSpec.Retribution, PaladinSpecs.Resolve("Auto", 0));
        }

        [Fact]
        public void Auto_detects_protection_from_tab_2()
        {
            Assert.Equal(PaladinSpec.Protection, PaladinSpecs.Resolve("Auto", 2));
        }

        [Fact]
        public void Auto_detects_retribution_from_tab_3()
        {
            Assert.Equal(PaladinSpec.Retribution, PaladinSpecs.Resolve("Auto", 3));
        }

        [Fact]
        public void Holy_talents_fall_back_to_Retribution()
        {
            // Holy (tab 1) is not a solo leveling spec here — default to Retribution.
            Assert.Equal(PaladinSpec.Retribution, PaladinSpecs.Resolve("Auto", 1));
        }

        [Fact]
        public void Manual_override_wins_over_talents()
        {
            Assert.Equal(PaladinSpec.Protection, PaladinSpecs.Resolve("Protection", 3));
            Assert.Equal(PaladinSpec.Retribution, PaladinSpecs.Resolve("Retribution", 2));
        }
    }
}
