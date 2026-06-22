using AIO3.Core.Rotations.Mage;
using Xunit;

namespace AIO3.Tests
{
    public class MageSpecsTests
    {
        [Theory]
        [InlineData("Frost", MageSpec.Frost)]
        [InlineData("Fire", MageSpec.Fire)]
        [InlineData("Arcane", MageSpec.Arcane)]
        public void Manual_override_wins(string choice, MageSpec expected)
        {
            Assert.Equal(expected, MageSpecs.Resolve(choice, highestTalentTab: 0));
        }

        [Theory]
        [InlineData(1, MageSpec.Arcane)] // WoW mage tab order: Arcane / Fire / Frost
        [InlineData(2, MageSpec.Fire)]
        [InlineData(3, MageSpec.Frost)]
        public void Auto_detects_from_the_highest_talent_tab(int tab, MageSpec expected)
        {
            Assert.Equal(expected, MageSpecs.Resolve(MageSpecs.Auto, tab));
        }

        [Fact]
        public void Auto_defaults_to_frost_before_any_points()
        {
            Assert.Equal(MageSpec.Frost, MageSpecs.Resolve(MageSpecs.Auto, highestTalentTab: 0));
        }

        [Fact]
        public void Auto_is_the_first_choice()
        {
            Assert.Equal(MageSpecs.Auto, MageSpecs.Choices[0]);
        }
    }
}
