using AIO3.Core.Rotations.Shaman;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class ShamanSpecsTests
    {
        [Theory]
        [InlineData(1, ShamanSpec.Elemental)]
        [InlineData(2, ShamanSpec.Enhancement)]
        [InlineData(3, ShamanSpec.Restoration)]
        [InlineData(0, ShamanSpec.Enhancement)] // no points yet (pre-10) → solo leveling default = Enhancement
        public void Auto_detects_from_highest_talent_tab(int tab, ShamanSpec expected)
        {
            Assert.Equal(expected, ShamanSpecs.Resolve(ShamanSpecs.Auto, tab));
        }

        [Theory]
        [InlineData("Elemental", ShamanSpec.Elemental)]
        [InlineData("Enhancement", ShamanSpec.Enhancement)]
        [InlineData("Restoration", ShamanSpec.Restoration)]
        public void Manual_override_wins_over_talents(string choice, ShamanSpec expected)
        {
            // Talents say Restoration (tab 3), but the manual override takes precedence.
            Assert.Equal(expected, ShamanSpecs.Resolve(choice, highestTalentTab: 3));
        }

        [Fact]
        public void Choice_setting_round_trips_and_rejects_unknown_values()
        {
            var s = new ChoiceSetting("spec", "Spec", ShamanSpecs.Auto, ShamanSpecs.Choices);

            s.Deserialize("Enhancement");
            Assert.Equal("Enhancement", s.Value);
            Assert.Equal("Enhancement", s.Serialize());

            s.Deserialize("Bogus"); // not an option → keep current
            Assert.Equal("Enhancement", s.Value);
        }

        [Fact]
        public void Talent_build_is_provided_for_each_spec()
        {
            // Enhancement and Elemental ship the solo leveling builds; Restoration ships the ported group build even
            // though its rotation falls back to Elemental.
            Assert.NotEmpty(ShamanTalents.For(ShamanSpec.Enhancement));
            Assert.NotEmpty(ShamanTalents.For(ShamanSpec.Elemental));
            Assert.NotEmpty(ShamanTalents.For(ShamanSpec.Restoration));
            Assert.NotSame(ShamanTalents.For(ShamanSpec.Enhancement), ShamanTalents.For(ShamanSpec.Elemental));
        }
    }
}
