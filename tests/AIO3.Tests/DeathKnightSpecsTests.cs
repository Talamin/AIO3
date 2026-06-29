using AIO3.Core.Rotations.DeathKnight;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightSpecsTests
    {
        [Theory]
        [InlineData("Blood", DeathKnightSpec.Blood)]
        [InlineData("Frost", DeathKnightSpec.Frost)]
        [InlineData("Unholy", DeathKnightSpec.Unholy)]
        public void Manual_override_wins(string choice, DeathKnightSpec expected)
        {
            // Talents say Unholy (tab 3), but the manual choice overrides.
            Assert.Equal(expected, DeathKnightSpecs.Resolve(choice, highestTalentTab: 3));
        }

        [Theory]
        [InlineData(1, DeathKnightSpec.Blood)]
        [InlineData(2, DeathKnightSpec.Frost)]
        [InlineData(3, DeathKnightSpec.Unholy)]
        public void Auto_detects_from_the_talent_tab(int tab, DeathKnightSpec expected)
        {
            Assert.Equal(expected, DeathKnightSpecs.Resolve("Auto", tab));
        }

        [Fact]
        public void No_points_defaults_to_blood()
        {
            Assert.Equal(DeathKnightSpec.Blood, DeathKnightSpecs.Resolve("Auto", highestTalentTab: 0));
        }
    }
}
