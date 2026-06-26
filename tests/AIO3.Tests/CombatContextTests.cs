using AIO3.Core.Combat;
using AIO3.Core.Game;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>
    /// CombatContext world-fact helpers. Focus here is the target-relative pack gate (X1): a ranged class
    /// stands far from the cluster it is AoE-ing, so a player-relative count is the wrong question for
    /// placing a target-anchored AoE. FakeUnit carries a 2D (X,Y) position used by DistanceTo; its
    /// player-relative Distance is independent of X/Y, so each test can place a cluster freely.
    /// </summary>
    public class CombatContextTests
    {
        // A hostile enemy at world position (x,y). Distance (player-relative) defaults to far so it never
        // counts toward EnemiesWithin unless a test sets it.
        private static FakeUnit Enemy(ulong guid, float x, float y, float distance = 28f) =>
            new FakeUnit { Guid = guid, X = x, Y = y, Distance = distance, IsAttackable = true, Reaction = Reaction.Hostile };

        private static CombatContext Ctx(FakeGameClient g) => CombatContext.Capture(g);

        [Fact]
        public void FakeUnit_DistanceTo_is_euclidean_between_positions()
        {
            var a = new FakeUnit { X = 0, Y = 0 };
            var b = new FakeUnit { X = 3, Y = 4 };
            Assert.Equal(5f, a.DistanceTo(b), 3);
            Assert.Equal(5f, b.DistanceTo(a), 3); // symmetric
            Assert.Equal(0f, a.DistanceTo(a), 3);
        }

        [Fact]
        public void EnemiesNearTarget_counts_the_cluster_around_the_target()
        {
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, x: 28, y: 0);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(Enemy(2, x: 29, y: 0)); // ~1yd from target
            g.EnemyList.Add(Enemy(3, x: 28, y: 3)); // ~3yd from target
            g.EnemyList.Add(Enemy(4, x: 50, y: 0)); // ~22yd from target → outside 10yd

            // target itself + the two near adds = 3 within 10yd of the target; the far one is excluded.
            Assert.Equal(3, Ctx(g).EnemiesNearTarget(10f));
        }

        [Fact]
        public void EnemiesNearTarget_excludes_an_enemy_near_the_player_but_far_from_the_target()
        {
            // The crux of X1: an add hugging the player (player-relative Distance 2) but 28yd from the distant
            // target must NOT count toward a target-anchored AoE. The old EnemiesWithin(10) would have counted it.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, x: 28, y: 0);
            g.EnemyList.Add(g.TargetUnit);
            var stragglerOnPlayer = Enemy(2, x: 0, y: 0, distance: 2f); // right on the player, far from the target
            g.EnemyList.Add(stragglerOnPlayer);

            // Old gate counts by player-Distance: the target (28) is excluded, the straggler (2) counts → a
            // bogus "pack of 1" near the player.
            Assert.Equal(1, Ctx(g).EnemiesWithin(10f));
            // New gate counts by distance to the target: only the target itself is near the target; the
            // straggler 28yd away is excluded → 1, and crucially it does NOT include the player-hugging add.
            Assert.Equal(1, Ctx(g).EnemiesNearTarget(10f));
        }

        [Fact]
        public void EnemiesNearTarget_is_zero_with_no_target()
        {
            var g = new FakeGameClient();
            g.EnemyList.Add(Enemy(2, x: 5, y: 5));
            Assert.Null(g.Target);
            Assert.Equal(0, Ctx(g).EnemiesNearTarget(50f));
        }

        [Fact]
        public void EnemiesNear_general_form_counts_around_any_center()
        {
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, x: 0, y: 0);
            g.EnemyList.Add(g.TargetUnit);
            var add = Enemy(2, x: 100, y: 0);
            g.EnemyList.Add(add);

            // Around the target (origin): only the target. Around the distant add: only the add.
            Assert.Equal(1, Ctx(g).EnemiesNear(g.TargetUnit, 10f));
            Assert.Equal(1, Ctx(g).EnemiesNear(add, 10f));
            Assert.Equal(0, Ctx(g).EnemiesNear(null, 10f)); // null center → 0, never throws
        }
    }
}
