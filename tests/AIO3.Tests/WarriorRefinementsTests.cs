using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // Tests the shared WarriorCommon blocks in isolation (one step per engine).
    public class WarriorRefinementsTests
    {
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Warrior };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100,
                IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g, RotationStep step) =>
            new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g));

        [Fact]
        public void Hamstring_fires_on_a_fleeing_humanoid()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 30;
            g.TargetUnit.CreatureType = "Humanoid";

            Assert.Equal("Hamstring", Fire(g, WarriorCommon.Hamstring(new WarriorSettings(), 1f))?.Name);
        }

        [Fact]
        public void Hamstring_skips_non_humanoids()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 30;
            g.TargetUnit.CreatureType = "Beast";

            Assert.Null(Fire(g, WarriorCommon.Hamstring(new WarriorSettings(), 1f)));
        }

        [Fact]
        public void Hamstring_skips_bosses()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 30;
            g.TargetUnit.CreatureType = "Humanoid";
            g.TargetUnit.Entry = 31146; // a boss entry

            Assert.Null(Fire(g, WarriorCommon.Hamstring(new WarriorSettings(), 1f)));
        }

        [Fact]
        public void Rend_skips_bleed_immune_creatures()
        {
            FakeGameClient g = Game();

            g.TargetUnit.CreatureType = "Elemental";
            Assert.Null(Fire(g, WarriorCommon.Rend(1f)));

            g.TargetUnit.CreatureType = "Beast";
            Assert.Equal("Rend", Fire(g, WarriorCommon.Rend(1f))?.Name);
        }

        [Fact]
        public void Rend_refreshes_before_expiry_but_not_while_fresh()
        {
            FakeGameClient g = Game();
            g.TargetUnit.CreatureType = "Beast";

            // Fresh (plenty of time left) → don't refresh.
            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 8000);
            Assert.Null(Fire(g, WarriorCommon.Rend(1f)));

            // About to expire → refresh.
            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 1500);
            Assert.Equal("Rend", Fire(g, WarriorCommon.Rend(1f))?.Name);
        }

        [Fact]
        public void Heroic_Strike_is_not_requeued_while_already_pending()
        {
            FakeGameClient g = Game();
            g.MeUnit.Rage = 50;
            RotationStep hs = WarriorCommon.HeroicStrike(new WarriorSettings(), 1f);

            Assert.Equal("Heroic Strike", Fire(g, hs)?.Name);

            g.CurrentSpells.Add("Heroic Strike"); // already queued on next swing
            Assert.Null(Fire(g, hs));
        }

        [Fact]
        public void MaintainMyDebuff_casts_when_missing_or_expiring_but_not_while_fresh()
        {
            // A fresh step per scenario: the new post-cast apply-grace only matters on a rapid SECOND fire of the
            // SAME step instance, but in real play "missing" and "expiring" are ~15s apart so it never interferes.
            // Reusing one instance here (all three fires in the same millisecond) would hit the throttle and mask
            // the condition under test. The throttle itself is covered by CombatBlocksTests.
            FakeGameClient g = Game();

            Assert.Equal("Rend", Fire(g, CombatBlocks.MaintainMyDebuff("Rend", minMsLeft: 3000, priority: 1f))?.Name); // missing

            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 1000);
            Assert.Equal("Rend", Fire(g, CombatBlocks.MaintainMyDebuff("Rend", minMsLeft: 3000, priority: 1f))?.Name); // expiring

            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 9000);
            Assert.Null(Fire(g, CombatBlocks.MaintainMyDebuff("Rend", minMsLeft: 3000, priority: 1f))); // fresh
        }

        [Fact]
        public void Emergency_item_used_when_low_and_one_is_ready()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 20;
            g.ReadyItems.Add("Healthstone");
            RotationStep step = CombatBlocks.UseItems("Emergency heal",
                new[] { "Healthstone", "Major Healing Potion" }, ctx => ctx.Me.HealthPercent < 30, 1f);

            Assert.Equal("Emergency heal", Fire(g, step)?.Name);
            Assert.Contains("Healthstone", g.UsedItems);
        }

        [Fact]
        public void Emergency_item_skipped_when_none_ready()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 20; // low, but no item available
            RotationStep step = CombatBlocks.UseItems("Emergency heal",
                new[] { "Healthstone" }, ctx => ctx.Me.HealthPercent < 30, 1f);

            Assert.Null(Fire(g, step));
            Assert.Empty(g.UsedItems);
        }

        [Fact]
        public void Offensive_racial_fires_only_in_combat_with_an_enemy()
        {
            FakeGameClient g = Game();
            RotationStep step = CombatBlocks.OffensiveRacial("Blood Fury", 1f, ctx => true);

            Assert.Null(Fire(g, step)); // not in combat yet

            g.InCombatFlag = true;
            Assert.Equal("Blood Fury", Fire(g, step)?.Name);
        }

        [Fact]
        public void Interrupt_mode_never_disables_and_always_enables()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 123;

            Assert.Null(Fire(g, CombatBlocks.Interrupt("Pummel", 1f, mode: ctx => InterruptModes.Never)));
            Assert.Equal("Pummel", Fire(g, CombatBlocks.Interrupt("Pummel", 1f, mode: ctx => InterruptModes.Always))?.Name);
        }

        [Fact]
        public void Smart_interrupt_skips_spells_learned_to_be_non_interruptible()
        {
            var tracker = new InterruptTracker();
            tracker.RecordAttempt(1, 100);
            tracker.OnCastCompleted(1, 100); // 100 completed despite our attempt → non-interruptible

            FakeGameClient g = Game();
            g.TargetUnit.Guid = 1;
            g.TargetUnit.IsCasting = true;
            RotationStep step = CombatBlocks.Interrupt("Pummel", 1f, ctx => InterruptModes.Smart);

            g.TargetUnit.CastingSpellId = 100; // blacklisted → skip
            Assert.Null(new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g, tracker)));

            g.TargetUnit.CastingSpellId = 200; // unknown → interrupt
            Assert.Equal("Pummel", new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g, tracker))?.Name);
        }

        [Fact]
        public void Recklessness_bursts_on_an_elite_but_not_lone_trash()
        {
            var s = new WarriorSettings();

            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true;
            Assert.Equal("Recklessness", Fire(g, WarriorCommon.Recklessness(s, 1f))?.Name);

            FakeGameClient trash = Game(); // single, non-elite, not a boss
            Assert.Null(Fire(trash, WarriorCommon.Recklessness(s, 1f)));
        }

        [Fact]
        public void Recklessness_respects_the_cooldowns_toggle()
        {
            var s = new WarriorSettings();
            s.UseCooldowns.Value = false;

            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true;
            Assert.Null(Fire(g, WarriorCommon.Recklessness(s, 1f)));
        }

        [Fact]
        public void Berserker_Rage_breaks_fear()
        {
            FakeGameClient g = Game();

            Assert.Null(Fire(g, WarriorCommon.BerserkerRage(1f)));

            g.MeUnit.WithAura("Fear");
            Assert.Equal("Berserker Rage", Fire(g, WarriorCommon.BerserkerRage(1f))?.Name);
        }
    }
}
