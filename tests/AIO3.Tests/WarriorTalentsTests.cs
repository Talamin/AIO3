using AIO3.Core.Rotations.Warrior;
using Xunit;

namespace AIO3.Tests
{
    public class WarriorTalentsTests
    {
        [Theory]
        [InlineData(WarriorSpec.Fury)]
        [InlineData(WarriorSpec.Arms)]
        [InlineData(WarriorSpec.Protection)]
        public void Each_spec_has_a_progression_of_uniform_length(WarriorSpec spec)
        {
            string[] codes = WarriorTalents.For(spec);

            Assert.NotEmpty(codes);

            int length = codes[0].Length;
            Assert.All(codes, c => Assert.Equal(length, c.Length));

            // The final build must actually spend points (contains a non-zero rank).
            Assert.Contains(codes[codes.Length - 1], ch => ch != '0');
        }
    }
}
