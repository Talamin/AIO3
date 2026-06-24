using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // End-to-end priority tests for the Solo Combat rogue, plus the shared RogueCommon blocks and the new
    // Energy / ComboPoints / Stealthed seams. The base game is a rogue already in melee on one full-health,
    // non-elite enemy, auto-attacking, with the burst cooldowns + Kick on cooldown so the tests isolate the
    // finisher/builder rules unless they opt in. PlayerInCombat is left false so the in-combat offensive
    // racials don't preempt the rule under test (same convention as the Warrior spec tests); nothing in the
    // rogue rotation gates on PlayerInCombat, so this doesn't change which rotation step is eligible.
    public class RogueCombatTests
    {
        private static FakeGameClient RogueGame()
        {
            var g = new FakeGameClient
            {
                Class = WowClass.Rogue,
                AutoAttacking = true
            };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100,
                IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            // Neutralise the burst cooldowns + Kick so they don't preempt the rules under test.
            g.SpellsOnCooldown.Add("Adrenaline Rush");
            g.SpellsOnCooldown.Add("Killing Spree");
            g.SpellsOnCooldown.Add("Kick");
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloCombat().BuildSteps()).Tick(CombatContext.Capture(g));

        private static RotationStep Fire(FakeGameClient g, SoloCombat rotation) =>
            new RotationEngine(rotation.BuildSteps()).Tick(CombatContext.Capture(g));

        // --- new Layer-0 seams read through ---

        [Fact]
        public void Seams_read_through_the_fake_client()
        {
            var g = RogueGame();
            g.MeUnit.Energy = 75;
            g.ComboPointCount = 4;
            g.Stealthed = true;

            CombatContext ctx = CombatContext.Capture(g);
            Assert.Equal(75, ctx.Me.Energy);
            Assert.Equal(4, ctx.ComboPoints);     // convenience accessor
            Assert.Equal(4, ctx.Game.ComboPoints);
            Assert.True(ctx.Game.PlayerIsStealthed);
        }

        // --- Slice and Dice upkeep ---

        [Fact]
        public void Slice_and_Dice_refreshes_when_missing_with_a_combo_point()
        {
            var g = RogueGame();
            g.ComboPointCount = 1; // enough for SnD upkeep
            Assert.Equal("Slice and Dice", Fire(g)?.Name);
        }

        [Fact]
        public void Slice_and_Dice_not_cast_without_a_combo_point()
        {
            var g = RogueGame();
            g.ComboPointCount = 0; // no points → fall through to the builder
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }

        [Fact]
        public void Slice_and_Dice_not_recast_while_the_buff_is_up()
        {
            var g = RogueGame();
            g.ComboPointCount = 5;
            g.MeUnit.WithAura("Slice and Dice"); // already up → SnD upkeep skips
            // With SnD up and 5 CP (>= finisher threshold 3), Eviscerate is the next finisher.
            Assert.Equal("Eviscerate", Fire(g)?.Name);
        }

        // --- Eviscerate finisher at the CP threshold ---

        [Fact]
        public void Eviscerate_fires_at_the_finisher_threshold()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice"); // keep SnD upkeep out of the way
            g.ComboPointCount = 3;               // default finisher threshold
            Assert.Equal("Eviscerate", Fire(g)?.Name);
        }

        [Fact]
        public void Eviscerate_held_below_the_finisher_threshold()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 2; // below the threshold → build instead
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }

        [Fact]
        public void Finisher_threshold_setting_is_respected()
        {
            var s = new RogueSettings();
            s.FinisherComboPoints.Value = 5; // require a full 5 before Eviscerate
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 4; // below the raised threshold

            Assert.Equal("Sinister Strike", Fire(g, new SoloCombat(s))?.Name);
        }

        // --- Sinister Strike as the builder / filler ---

        [Fact]
        public void Sinister_Strike_is_the_builder_with_no_combo_points()
        {
            var g = RogueGame();
            g.ComboPointCount = 0;
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }

        [Fact]
        public void Sinister_Strike_is_skipped_while_stealthed()
        {
            var g = RogueGame();
            g.ComboPointCount = 0;
            g.Stealthed = true; // opener territory — the builder waits
            Assert.Null(Fire(g));
        }

        // --- Blade Flurry on a pack ---

        [Fact]
        public void Blade_Flurry_fires_on_two_enemies_in_melee()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice"); // keep finishers/upkeep out of the way
            g.ComboPointCount = 0;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            Assert.Equal("Blade Flurry", Fire(g)?.Name);
        }

        [Fact]
        public void Blade_Flurry_skipped_on_a_single_target()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0; // single enemy → no Blade Flurry, build instead
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }

        // --- burst cooldowns ---

        [Fact]
        public void Adrenaline_Rush_pops_on_a_lone_elite_when_cooldowns_enabled()
        {
            var g = RogueGame();
            g.SpellsOnCooldown.Remove("Adrenaline Rush");
            g.SpellsOnCooldown.Add("Evasion"); // Evasion also triggers on a lone elite (higher priority) — isolate AR
            g.TargetUnit.IsElite = true;
            Assert.Equal("Adrenaline Rush", Fire(g)?.Name);
        }

        [Fact]
        public void Cooldowns_respect_the_toggle()
        {
            var s = new RogueSettings();
            s.UseCooldowns.Value = false;
            var g = RogueGame();
            g.SpellsOnCooldown.Remove("Adrenaline Rush");
            g.SpellsOnCooldown.Remove("Killing Spree");
            g.SpellsOnCooldown.Add("Evasion"); // Evasion's lone-elite trigger is a separate survival rule — isolate
            g.TargetUnit.IsElite = true;
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0;

            Assert.Equal("Sinister Strike", Fire(g, new SoloCombat(s))?.Name); // no burst, just build
        }

        // --- Kick interrupt gating ---

        [Fact]
        public void Kick_interrupts_a_casting_enemy_when_mode_is_not_never()
        {
            var g = RogueGame();
            g.SpellsOnCooldown.Remove("Kick");
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 123;
            Assert.Equal("Kick", Fire(g)?.Name);
        }

        [Fact]
        public void Kick_disabled_when_interrupt_mode_is_never()
        {
            var s = new RogueSettings();
            s.InterruptMode.Value = InterruptModes.Never;
            var g = RogueGame();
            g.SpellsOnCooldown.Remove("Kick");
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 123;
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0;

            Assert.Equal("Sinister Strike", Fire(g, new SoloCombat(s))?.Name); // no Kick, just build
        }

        // --- defensives ---

        [Fact]
        public void Evasion_fires_below_the_health_threshold()
        {
            var g = RogueGame();
            g.MeUnit.HealthPercent = 20; // below the default 35
            Assert.Equal("Evasion", Fire(g)?.Name);
        }

        [Fact]
        public void Cloak_of_Shadows_fires_on_a_magic_debuff()
        {
            var g = RogueGame();
            g.DebuffTypes.Add("Magic");
            // Evasion is off the GCD too but needs a low-HP / surrounded / elite trigger, none of which hold here.
            Assert.Equal("Cloak of Shadows", Fire(g)?.Name);
        }

        // --- low-level rogue: only Sinister Strike known → falls through cleanly ---

        [Fact]
        public void Low_level_rogue_with_only_Sinister_Strike_falls_through_cleanly()
        {
            var g = RogueGame();
            g.KnownSpells.Add("Sinister Strike");
            g.KnownSpells.Add("Auto Attack"); // already auto-attacking, so AutoAttack step won't fire
            g.ComboPointCount = 3; // even at a "finisher" count, Eviscerate is unknown → skip to the builder
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }
    }
}
