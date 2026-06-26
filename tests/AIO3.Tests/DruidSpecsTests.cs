using AIO3.Core.Rotations.Druid;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class DruidSpecsTests
    {
        [Theory]
        [InlineData(1, DruidSpec.Balance)]
        [InlineData(2, DruidSpec.Feral)]
        [InlineData(3, DruidSpec.Restoration)]
        [InlineData(0, DruidSpec.Feral)] // no points yet (pre-10) → leveling default = Feral
        public void Auto_detects_from_highest_talent_tab(int tab, DruidSpec expected)
        {
            Assert.Equal(expected, DruidSpecs.Resolve(DruidSpecs.Auto, tab));
        }

        [Theory]
        [InlineData("Balance", DruidSpec.Balance)]
        [InlineData("Feral", DruidSpec.Feral)]
        [InlineData("Restoration", DruidSpec.Restoration)]
        public void Manual_override_wins_over_talents(string choice, DruidSpec expected)
        {
            // Talents say Balance (tab 1), but the manual override takes precedence.
            Assert.Equal(expected, DruidSpecs.Resolve(choice, highestTalentTab: 1));
        }

        [Fact]
        public void Choice_setting_round_trips_and_rejects_unknown_values()
        {
            var s = new ChoiceSetting("spec", "Spec", DruidSpecs.Auto, DruidSpecs.Choices);

            s.Deserialize("Feral");
            Assert.Equal("Feral", s.Value);
            Assert.Equal("Feral", s.Serialize());

            s.Deserialize("Bogus"); // not an option → keep current
            Assert.Equal("Feral", s.Value);
        }

        [Fact]
        public void Talent_build_is_provided_for_each_spec()
        {
            // Feral and Balance ship their own builds; Restoration ships the ported group-resto build even though
            // its rotation falls back to Feral.
            Assert.NotEmpty(DruidTalents.For(DruidSpec.Feral));
            Assert.NotEmpty(DruidTalents.For(DruidSpec.Balance));
            Assert.NotEmpty(DruidTalents.For(DruidSpec.Restoration));
            // Balance and Feral are distinct progressions.
            Assert.NotSame(DruidTalents.For(DruidSpec.Feral), DruidTalents.For(DruidSpec.Balance));
        }
    }
}
