using AIO3.Core.Rotations.Hunter;
using Xunit;

namespace AIO3.Tests
{
    public class HunterSpecsTests
    {
        [Fact]
        public void Auto_with_no_talents_defaults_to_Beast_Mastery() =>
            Assert.Equal(HunterSpec.BeastMastery, HunterSpecs.Resolve("Auto", 0));

        [Fact]
        public void Auto_detects_marksmanship_from_tab_2() =>
            Assert.Equal(HunterSpec.Marksmanship, HunterSpecs.Resolve("Auto", 2));

        [Fact]
        public void Auto_detects_survival_from_tab_3() =>
            Assert.Equal(HunterSpec.Survival, HunterSpecs.Resolve("Auto", 3));

        [Fact]
        public void Manual_override_wins_over_talents() =>
            Assert.Equal(HunterSpec.BeastMastery, HunterSpecs.Resolve("Beast Mastery", 3));
    }
}
