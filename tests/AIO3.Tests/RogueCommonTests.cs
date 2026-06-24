using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // Tests the shared RogueCommon blocks in isolation (one step per engine), so Assassination inherits proven
    // behaviour when it reuses them.
    public class RogueCommonTests
    {
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Rogue, InCombatFlag = true };
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
        public void Slice_and_Dice_block_needs_a_combo_point_and_a_missing_buff()
        {
            var g = Game();
            RotationStep step = RogueCommon.SliceAndDice(minComboPoints: 1, priority: 1f);

            g.ComboPointCount = 0;
            Assert.Null(Fire(g, step)); // no combo points

            g.ComboPointCount = 1;
            Assert.Equal("Slice and Dice", Fire(g, step)?.Name); // missing buff + a CP → refresh

            g.MeUnit.WithAura("Slice and Dice");
            Assert.Null(Fire(g, step)); // already up → no refresh
        }

        [Fact]
        public void Sprint_closes_a_gap_to_an_out_of_range_target()
        {
            var s = new RogueSettings();
            var g = Game();
            RotationStep step = RogueCommon.Sprint(s, priority: 1f);

            g.TargetUnit.Distance = 5; // already in melee
            Assert.Null(Fire(g, step));

            g.TargetUnit.Distance = 20; // out of melee → close it
            Assert.Equal("Sprint", Fire(g, step)?.Name);

            g.MeUnit.WithAura("Sprint"); // already sprinting → don't re-cast
            Assert.Null(Fire(g, step));
        }

        [Fact]
        public void Sprint_respects_the_toggle()
        {
            var s = new RogueSettings();
            s.UseSprint.Value = false;
            var g = Game();
            g.TargetUnit.Distance = 20;
            Assert.Null(Fire(g, RogueCommon.Sprint(s, 1f)));
        }

        [Fact]
        public void Evasion_fires_when_surrounded()
        {
            var s = new RogueSettings();
            s.EvasionHealthPercent.Value = 0; // disable the HP trigger; isolate the surrounded trigger
            var g = Game();
            g.MeUnit.HealthPercent = 100;

            // One attacker → below the default count of 2.
            g.TargetUnit.IsTargetingMe = true;
            Assert.Null(Fire(g, RogueCommon.Evasion(s, 1f)));

            // Two attackers in melee → Evasion.
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsTargetingMe = true });
            Assert.Equal("Evasion", Fire(g, RogueCommon.Evasion(s, 1f))?.Name);
        }

        [Fact]
        public void Rupture_is_opt_in_and_only_on_durable_targets()
        {
            var s = new RogueSettings();
            var g = Game();
            g.ComboPointCount = 5;
            g.TargetUnit.IsElite = true;

            // Off by default.
            Assert.Null(Fire(g, RogueCommon.Rupture(s, 1f)));

            s.UseRupture.Value = true;
            Assert.Equal("Rupture", Fire(g, RogueCommon.Rupture(s, 1f))?.Name); // elite + CP + missing bleed

            // Not on a normal mob (dies before a bleed pays off).
            g.TargetUnit.IsElite = false;
            Assert.Null(Fire(g, RogueCommon.Rupture(s, 1f)));
        }

        [Fact]
        public void Rupture_skips_bleed_immune_creatures()
        {
            var s = new RogueSettings();
            s.UseRupture.Value = true;
            var g = Game();
            g.ComboPointCount = 5;
            g.TargetUnit.IsElite = true;
            g.TargetUnit.CreatureType = "Mechanical";
            Assert.Null(Fire(g, RogueCommon.Rupture(s, 1f)));
        }

        [Fact]
        public void Stealth_opens_out_of_combat_only()
        {
            var s = new RogueSettings();
            s.UseStealth.Value = true;
            var g = Game();

            g.InCombatFlag = true; // in combat → no stealth
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));

            g.InCombatFlag = false; // out of combat, not stealthed → open from stealth
            Assert.Equal("Stealth", Fire(g, RogueCommon.Stealth(s, 1f))?.Name);

            g.Stealthed = true; // already stealthed → done
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));
        }
    }
}
