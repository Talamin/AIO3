using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class CombatBlocksTests
    {
        private static FakeGameClient Game(out FakeUnit enemy)
        {
            var game = new FakeGameClient();
            enemy = new FakeUnit { Guid = 1, Name = "Caster Mob", Reaction = Reaction.Hostile, Distance = 5 };
            game.EnemyList.Add(enemy);
            game.TargetUnit = enemy;
            return game;
        }

        [Fact]
        public void Interrupt_fires_when_an_enemy_is_casting_in_range()
        {
            FakeGameClient game = Game(out FakeUnit enemy);
            enemy.IsCasting = true;

            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.Interrupt("Pummel") });
            RotationStep fired = engine.Tick(CombatContext.Capture(game));

            Assert.Equal("Pummel", fired?.Name);
            Assert.Equal(new[] { "Pummel" }, game.CastLog.ToArray());
        }

        [Fact]
        public void Interrupt_silent_when_no_enemy_is_casting()
        {
            FakeGameClient game = Game(out FakeUnit enemy);
            enemy.IsCasting = false;

            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.Interrupt("Pummel") });
            RotationStep fired = engine.Tick(CombatContext.Capture(game));

            Assert.Null(fired);
            Assert.Empty(game.CastLog);
        }

        [Fact]
        public void Interrupt_respects_range()
        {
            FakeGameClient game = Game(out FakeUnit enemy);
            enemy.IsCasting = true;
            enemy.Distance = 30;                 // casting, but far away
            game.SpellRanges["Pummel"] = 5f;     // melee interrupt range

            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.Interrupt("Pummel") });
            RotationStep fired = engine.Tick(CombatContext.Capture(game));

            Assert.Null(fired);
            Assert.Empty(game.CastLog);
        }

        [Theory]
        [InlineData(100, false)] // healthy → no defensive
        [InlineData(25, true)]   // hurt → defensive
        public void DefensiveBelow_fires_only_under_threshold(double hp, bool shouldFire)
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.HealthPercent = hp;

            var engine = new RotationEngine(new List<RotationStep>
            {
                CombatBlocks.DefensiveBelow("Shield Wall", healthPercent: 30)
            });
            RotationStep fired = engine.Tick(CombatContext.Capture(game));

            Assert.Equal(shouldFire, fired != null);
            Assert.Equal(shouldFire, game.CastLog.Contains("Shield Wall"));
        }

        [Fact]
        public void SelfBuff_casts_only_when_missing()
        {
            FakeGameClient game = Game(out _);

            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.SelfBuff("Battle Shout") });

            // Missing → casts.
            Assert.Equal("Battle Shout", engine.Tick(CombatContext.Capture(game))?.Name);

            // Present (mine) → silent.
            game.MeUnit.WithAura("Battle Shout", mine: true);
            game.CastLog.Clear();
            Assert.Null(engine.Tick(CombatContext.Capture(game)));
        }

        [Fact]
        public void MaintainCastDebuff_casts_when_missing_and_stationary()
        {
            FakeGameClient game = Game(out _);
            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.MaintainCastDebuff("Immolate", 3000, 1f) });
            Assert.Equal("Immolate", engine.Tick(CombatContext.Capture(game))?.Name);
        }

        [Fact]
        public void MaintainCastDebuff_holds_while_already_casting_it()
        {
            // The double-cast guard: the cast-time DoT's debuff only lands when the cast finishes, so while it IS
            // the current cast the bare missing-aura check is still true — without this guard the cast-queue window
            // would queue a second cast.
            FakeGameClient game = Game(out _);
            game.CurrentSpells.Add("Immolate"); // mid-cast
            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.MaintainCastDebuff("Immolate", 3000, 1f) });
            Assert.Null(engine.Tick(CombatContext.Capture(game)));
        }

        [Fact]
        public void MaintainCastDebuff_holds_while_moving()
        {
            FakeGameClient game = Game(out _);
            game.Moving = true; // cast-time DoT → stand still
            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.MaintainCastDebuff("Immolate", 3000, 1f) });
            Assert.Null(engine.Tick(CombatContext.Capture(game)));
        }

        [Fact]
        public void MaintainCastDebuff_does_not_recast_in_the_apply_window()
        {
            // After the cast ends, the debuff isn't visible for ~2.5s (server latency) AND we're no longer "casting
            // it", so the IsCurrentSpell guard no longer applies. The post-cast grace must still stop a second cast
            // in that window (the Immolate double-cast from the logs). The fake never lands the aura, so a second
            // tick would re-cast without the throttle.
            FakeGameClient game = Game(out _);
            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.MaintainCastDebuff("Immolate", 3000, 1f) });
            CombatContext ctx = CombatContext.Capture(game);

            Assert.Equal("Immolate", engine.Tick(ctx)?.Name);                  // first application
            Assert.Null(engine.Tick(ctx));                                     // within the grace → no second cast
            Assert.Single(game.CastLog.FindAll(c => c == "Immolate"));
        }

        [Fact]
        public void MaintainMyDebuff_does_not_recast_in_the_apply_window()
        {
            // Instant DoT: the aura still takes ~0.6-1.1s to become visible, so the same post-cast grace stops the
            // Corruption double-cast.
            FakeGameClient game = Game(out _);
            var engine = new RotationEngine(new List<RotationStep> { CombatBlocks.MaintainMyDebuff("Corruption", 2000, 1f) });
            CombatContext ctx = CombatContext.Capture(game);

            Assert.Equal("Corruption", engine.Tick(ctx)?.Name);               // first application
            Assert.Null(engine.Tick(ctx));                                    // within the grace → no double-cast
            Assert.Single(game.CastLog.FindAll(c => c == "Corruption"));
        }
    }
}
