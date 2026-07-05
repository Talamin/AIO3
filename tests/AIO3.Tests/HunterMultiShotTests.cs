using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Hunter;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // The shared HunterCommon.MultiShot block. Covers the AoE gate AND the throttle that fixes the bug Talamin saw:
    // Multi-Shot's short (~0.5s, less with haste) cast falls within the ~400ms spell-queue window, so without a
    // RecastDelay the engine re-issued it every tick and RESTARTED the shot before it completed — it only toggled
    // the spell on/off and never fired.
    public class HunterMultiShotTests
    {
        // A ranged hunter at the origin; the target + one add clustered ~28yd downrange (far from the player, near
        // each other) — the case the target-relative pack gate is built for.
        private static FakeGameClient PackGame()
        {
            var g = new FakeGameClient { Class = WowClass.Hunter };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Reaction = Reaction.Hostile, Distance = 28, X = 28, Y = 0, HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(new FakeUnit // one add 1yd from the target (so EnemiesNearTarget = 2)
            {
                Guid = 2, Reaction = Reaction.Hostile, Distance = 28, X = 29, Y = 0, IsAttackable = true
            });
            return g;
        }

        private static HunterSettings AoeAt(int threshold)
        {
            var s = new HunterSettings();
            s.AoeThreshold.Value = threshold; // mirror Talamin's config (AoE at 2)
            return s;
        }

        [Fact]
        public void Fires_on_a_cluster_around_the_target()
        {
            var g = PackGame();
            RotationStep step = HunterCommon.MultiShot(AoeAt(2), priority: 1f);
            Assert.Equal("Multi-Shot", new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g))?.Name);
        }

        [Fact]
        public void Does_not_re_issue_during_its_own_cast()
        {
            // The regression: the SAME step fired twice in quick succession must NOT fire the second time — the
            // RecastDelay throttle lets the ~0.5s cast complete instead of restarting it every tick.
            var g = PackGame();
            RotationStep step = HunterCommon.MultiShot(AoeAt(2), priority: 1f);
            var engine = new RotationEngine(new[] { step });

            Assert.Equal("Multi-Shot", engine.Tick(CombatContext.Capture(g))?.Name); // first tick fires
            Assert.Null(engine.Tick(CombatContext.Capture(g)));                       // immediately after → throttled
        }

        [Fact]
        public void Holds_while_moving()
        {
            var g = PackGame();
            g.Moving = true; // a cast-time shot can't start on the move
            RotationStep step = HunterCommon.MultiShot(AoeAt(2), priority: 1f);
            Assert.Null(new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g)));
        }

        [Fact]
        public void Respects_the_AoE_toggle()
        {
            var g = PackGame();
            var s = AoeAt(2);
            s.UseAoe.Value = false;
            RotationStep step = HunterCommon.MultiShot(s, priority: 1f);
            Assert.Null(new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g)));
        }

        [Fact]
        public void Held_below_the_AoE_threshold()
        {
            var g = PackGame();
            g.EnemyList.RemoveAt(1); // remove the add → only the target near the target (count 1)
            RotationStep step = HunterCommon.MultiShot(AoeAt(2), priority: 1f);
            Assert.Null(new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g)));
        }
    }
}
