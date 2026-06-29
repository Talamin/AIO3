using AIO3.Core.Rotations.Priest;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class PriestSpecsTests
    {
        [Theory]
        [InlineData(1, PriestSpec.Discipline)]
        [InlineData(2, PriestSpec.Holy)]
        [InlineData(3, PriestSpec.Shadow)]
        [InlineData(0, PriestSpec.Shadow)] // no points yet (pre-10) → solo leveling default = Shadow
        public void Auto_detects_from_highest_talent_tab(int tab, PriestSpec expected)
        {
            Assert.Equal(expected, PriestSpecs.Resolve(PriestSpecs.Auto, tab));
        }

        [Theory]
        [InlineData("Discipline", PriestSpec.Discipline)]
        [InlineData("Holy", PriestSpec.Holy)]
        [InlineData("Shadow", PriestSpec.Shadow)]
        public void Manual_override_wins_over_talents(string choice, PriestSpec expected)
        {
            // Talents say Shadow (tab 3), but the manual override takes precedence.
            Assert.Equal(expected, PriestSpecs.Resolve(choice, highestTalentTab: 3));
        }

        [Fact]
        public void Choice_setting_round_trips_and_rejects_unknown_values()
        {
            var s = new ChoiceSetting("spec", "Spec", PriestSpecs.Auto, PriestSpecs.Choices);

            s.Deserialize("Shadow");
            Assert.Equal("Shadow", s.Value);
            Assert.Equal("Shadow", s.Serialize());

            s.Deserialize("Bogus"); // not an option → keep current
            Assert.Equal("Shadow", s.Value);
        }

        [Fact]
        public void Talent_build_is_provided_for_each_spec()
        {
            // Shadow ships the solo leveling build; Disc/Holy ship the ported group builds even though their
            // rotation falls back to Shadow.
            Assert.NotEmpty(PriestTalents.For(PriestSpec.Shadow));
            Assert.NotEmpty(PriestTalents.For(PriestSpec.Discipline));
            Assert.NotEmpty(PriestTalents.For(PriestSpec.Holy));
            // Shadow and Holy are distinct progressions.
            Assert.NotSame(PriestTalents.For(PriestSpec.Shadow), PriestTalents.For(PriestSpec.Holy));
        }
    }
}
