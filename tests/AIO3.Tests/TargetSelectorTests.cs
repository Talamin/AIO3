using AIO3.Core.Combat;
using AIO3.Core.Game;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class TargetSelectorTests
    {
        private static FakeUnit Enemy(ulong guid, float distance, bool targetingMe = false) => new FakeUnit
        {
            Guid = guid,
            Reaction = Reaction.Hostile,
            IsAttackable = true,
            HealthPercent = 100,
            Distance = distance,
            IsTargetingMe = targetingMe
        };

        private static IWowUnit Pick(FakeGameClient g) => TargetSelector.Pick(CombatContext.Capture(g));

        [Fact]
        public void Never_pulls_when_no_enemy_is_attacking_us()
        {
            // Enemies are nearby but none is on us and we have no target — the FC must NOT acquire one
            // (the product owns the opener).
            var g = new FakeGameClient();
            g.EnemyList.Add(Enemy(1, distance: 8));
            g.EnemyList.Add(Enemy(2, distance: 12));

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Does_nothing_with_a_single_attacker()
        {
            // One enemy engaged = a normal solo fight; nothing to manage, keep the product's target.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: true);
            g.EnemyList.Add(g.TargetUnit);

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Keeps_the_current_target_when_it_is_one_of_several_attackers()
        {
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: true);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(Enemy(2, distance: 3, targetingMe: true)); // a closer add, but no thrashing

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Switches_to_an_attacker_when_the_current_target_is_not_one_of_them()
        {
            // We're on something that isn't fighting us (e.g. it fled / mis-tag) while two adds are on us.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: false);
            FakeUnit nearAttacker = Enemy(2, distance: 6, targetingMe: true);
            FakeUnit farAttacker = Enemy(3, distance: 20, targetingMe: true);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(farAttacker);
            g.EnemyList.Add(nearAttacker);

            Assert.Same(nearAttacker, Pick(g)); // nearest attacker
        }

        [Fact]
        public void Switches_when_we_have_no_target_but_several_enemies_are_on_us()
        {
            // The product's target died mid-fight; two adds remain on us → re-acquire among the attackers.
            var g = new FakeGameClient();
            FakeUnit a = Enemy(2, distance: 4, targetingMe: true);
            FakeUnit b = Enemy(3, distance: 9, targetingMe: true);
            g.EnemyList.Add(b);
            g.EnemyList.Add(a);

            Assert.Same(a, Pick(g));
        }

        [Fact]
        public void Picks_nothing_when_there_are_no_enemies()
        {
            Assert.Null(Pick(new FakeGameClient()));
        }

        [Fact]
        public void Fake_records_the_set_target_guid()
        {
            var g = new FakeGameClient();
            g.SetTarget(Enemy(42, distance: 5));
            Assert.Equal(42ul, g.LastSetTargetGuid);
        }
    }
}
