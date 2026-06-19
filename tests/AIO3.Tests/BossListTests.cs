using AIO3.Core.Data;
using AIO3.Core.Game;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class BossListTests
    {
        [Fact]
        public void Known_boss_entries_are_detected()
        {
            Assert.True(BossList.Contains(31146)); // Heroic Training dummy (first entry)
            Assert.False(BossList.Contains(999999)); // above any real creature entry
        }

        [Fact]
        public void IsBoss_extension_uses_the_list()
        {
            Assert.True(new FakeUnit { Entry = 31146 }.IsBoss());
            Assert.False(new FakeUnit { Entry = 999999 }.IsBoss());
        }
    }
}
