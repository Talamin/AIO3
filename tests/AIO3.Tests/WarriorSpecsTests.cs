using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class WarriorSpecsTests
    {
        [Theory]
        [InlineData(1, WarriorSpec.Arms)]
        [InlineData(2, WarriorSpec.Fury)]
        [InlineData(3, WarriorSpec.Protection)]
        [InlineData(0, WarriorSpec.Fury)] // no points yet (pre-10) → leveling default
        public void Auto_detects_from_highest_talent_tab(int tab, WarriorSpec expected)
        {
            Assert.Equal(expected, WarriorSpecs.Resolve(WarriorSpecs.Auto, tab));
        }

        [Theory]
        [InlineData("Fury", WarriorSpec.Fury)]
        [InlineData("Arms", WarriorSpec.Arms)]
        [InlineData("Protection", WarriorSpec.Protection)]
        public void Manual_override_wins_over_talents(string choice, WarriorSpec expected)
        {
            // Talents say Arms (tab 1), but the manual override takes precedence.
            Assert.Equal(expected, WarriorSpecs.Resolve(choice, highestTalentTab: 1));
        }

        [Fact]
        public void Choice_setting_round_trips_and_rejects_unknown_values()
        {
            var s = new ChoiceSetting("spec", "Spec", WarriorSpecs.Auto, WarriorSpecs.Choices);

            s.Deserialize("Fury");
            Assert.Equal("Fury", s.Value);
            Assert.Equal("Fury", s.Serialize());

            s.Deserialize("Bogus"); // not an option → keep current
            Assert.Equal("Fury", s.Value);
        }
    }
}
