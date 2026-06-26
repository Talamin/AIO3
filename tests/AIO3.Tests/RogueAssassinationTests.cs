using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // End-to-end priority tests for the Solo Assassination rogue, exercising the Assassination-specific filler
    // (Mutilate / Envenom / Hunger for Blood / Cold Blood / Rupture) layered on the shared RogueCommon baseline.
    // The base game is a rogue already in melee on one full-health, non-elite enemy, auto-attacking, with Kick on
    // cooldown so the tests isolate the rule under test unless they opt in. Cold Blood / Hunger for Blood don't fire
    // in the base (no pack / lone-elite, no bleed up), so they don't preempt the builder/finisher rules.
    // PlayerInCombat is left false so the in-combat racials don't preempt the rule under test (same convention as
    // the other spec tests); nothing in the rogue rotation gates on PlayerInCombat.
    public class RogueAssassinationTests
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
            g.SpellsOnCooldown.Add("Kick");
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloAssassination().BuildSteps()).Tick(CombatContext.Capture(g));

        private static RotationStep Fire(FakeGameClient g, SoloAssassination rotation) =>
            new RotationEngine(rotation.BuildSteps()).Tick(CombatContext.Capture(g));

        // --- Mutilate is the builder ---

        [Fact]
        public void Mutilate_is_the_builder_when_known()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice"); // keep upkeep out of the way
            g.ComboPointCount = 0;               // build
            Assert.Equal("Mutilate", Fire(g)?.Name);
        }

        [Fact]
        public void Mutilate_held_at_the_finisher_threshold()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 3; // at the finisher threshold → don't keep building, finish instead
            Assert.NotEqual("Mutilate", Fire(g)?.Name);
        }

        [Fact]
        public void Mutilate_is_skipped_while_stealthed()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0;
            g.Stealthed = true; // opener territory — the builder waits
            Assert.Null(Fire(g));
        }

        // --- Sinister Strike is the fallback builder when Mutilate is unknown (dagger-less / pre-Mutilate) ---

        [Fact]
        public void Sinister_Strike_is_the_fallback_builder_when_Mutilate_unknown()
        {
            var g = RogueGame();
            g.UnknownSpells.Add("Mutilate"); // no daggers / not learned yet → fall through to Sinister Strike
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0;
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }

        // --- Slice and Dice upkeep (shared block) ---

        [Fact]
        public void Slice_and_Dice_refreshes_when_missing_with_a_combo_point()
        {
            var g = RogueGame();
            g.ComboPointCount = 1; // enough for SnD upkeep, and it wins over the builder
            Assert.Equal("Slice and Dice", Fire(g)?.Name);
        }

        [Fact]
        public void Slice_and_Dice_not_recast_while_the_buff_is_up()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0; // up already → fall through to the builder
            Assert.Equal("Mutilate", Fire(g)?.Name);
        }

        // --- Eviscerate is the DEFAULT finisher now (poisons are deferred, so Envenom at 0 Deadly Poison stacks is
        //     weaker than Eviscerate); Envenom is chosen only when the player switches the finisher to Envenom/Auto ---

        [Fact]
        public void Eviscerate_is_the_default_finisher_at_the_threshold()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice"); // keep SnD upkeep out of the way
            g.ComboPointCount = 3;               // default finisher threshold
            Assert.Equal("Eviscerate", Fire(g)?.Name); // default finisher is Eviscerate (poisons deferred)
        }

        [Fact]
        public void Envenom_is_used_when_the_finisher_is_set_to_Envenom()
        {
            var s = new RogueSettings();
            s.AssassinationFinisher.Value = "Envenom"; // player poisoned their weapons → switch to Envenom
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 3;
            Assert.Equal("Envenom", Fire(g, new SoloAssassination(s))?.Name);
        }

        [Fact]
        public void Finisher_held_below_the_finisher_threshold()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 2; // below the threshold → keep building
            Assert.Equal("Mutilate", Fire(g)?.Name);
        }

        // --- Eviscerate is the finisher fallback when Envenom is unknown, or when forced ---

        [Fact]
        public void Eviscerate_is_the_finisher_when_Envenom_unknown()
        {
            var g = RogueGame();
            g.UnknownSpells.Add("Envenom"); // pre-Envenom / no poison build → Auto falls back to Eviscerate
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 3;
            Assert.Equal("Eviscerate", Fire(g)?.Name);
        }

        [Fact]
        public void Finisher_choice_can_force_Eviscerate_over_Envenom()
        {
            var s = new RogueSettings();
            s.AssassinationFinisher.Value = "Eviscerate"; // force Eviscerate even though Envenom is known
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 3;
            Assert.Equal("Eviscerate", Fire(g, new SoloAssassination(s))?.Name);
        }

        [Fact]
        public void Finisher_threshold_setting_is_respected()
        {
            var s = new RogueSettings();
            s.FinisherComboPoints.Value = 5; // require a full 5 before the finisher
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 4; // below the raised threshold → keep building
            Assert.Equal("Mutilate", Fire(g, new SoloAssassination(s))?.Name);
        }

        // --- Rupture defaults ON for Assassination (Combat defaults it off) ---

        [Fact]
        public void Rupture_is_on_by_default_for_Assassination_on_a_durable_target()
        {
            var g = RogueGame();
            // Evasion + Cold Blood also fire on a lone elite (both above Rupture's 6f) — isolate Rupture.
            g.SpellsOnCooldown.Add("Evasion");
            g.SpellsOnCooldown.Add("Cold Blood");
            g.MeUnit.WithAura("Slice and Dice");
            g.TargetUnit.IsElite = true; // durable target, missing bleed
            g.ComboPointCount = 5;       // enough to finish
            // Rupture (6f) sits above Envenom (7f), so the missing bleed is applied first.
            Assert.Equal("Rupture", Fire(g)?.Name);
        }

        [Fact]
        public void Rupture_can_be_disabled_for_Assassination()
        {
            var s = new RogueSettings();
            s.AssassinationUseRupture.Value = false; // off → straight to the damage finisher (default Eviscerate)
            var g = RogueGame();
            g.SpellsOnCooldown.Add("Evasion");      // isolate from Evasion's lone-elite trigger
            g.SpellsOnCooldown.Add("Cold Blood");   // ...and Cold Blood's
            g.MeUnit.WithAura("Slice and Dice");
            g.MeUnit.WithAura("Hunger For Blood"); // up already so HfB upkeep doesn't preempt the finisher
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000); // a bleed is up (enables HfB precondition)
            g.ComboPointCount = 5;
            Assert.Equal("Eviscerate", Fire(g, new SoloAssassination(s))?.Name); // default finisher (poisons deferred)
        }

        [Fact]
        public void Rupture_is_not_recast_while_the_bleed_is_up()
        {
            var g = RogueGame();
            g.SpellsOnCooldown.Add("Evasion");    // isolate from Evasion's lone-elite trigger
            g.SpellsOnCooldown.Add("Cold Blood"); // ...and Cold Blood's
            g.MeUnit.WithAura("Slice and Dice");
            g.MeUnit.WithAura("Hunger For Blood"); // up already so HfB upkeep doesn't preempt the finisher
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000); // bleed already up, plenty left
            g.ComboPointCount = 5;
            Assert.Equal("Eviscerate", Fire(g)?.Name); // Rupture skipped → the default damage finisher
        }

        // --- Hunger for Blood upkeep (needs a bleed up) ---

        [Fact]
        public void Hunger_for_Blood_refreshes_when_a_bleed_is_up_and_the_buff_is_missing()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000); // bleed present → HfB precondition met
            g.ComboPointCount = 0; // no finisher competing; HfB (5.5f) beats the builder
            Assert.Equal("Hunger For Blood", Fire(g)?.Name);
        }

        [Fact]
        public void Hunger_for_Blood_holds_without_a_bleed_on_the_target()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0; // no bleed up → HfB can't fire, fall through to the builder
            Assert.Equal("Mutilate", Fire(g)?.Name);
        }

        [Fact]
        public void Hunger_for_Blood_not_recast_while_up()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.MeUnit.WithAura("Hunger For Blood");                         // already up
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000);
            g.ComboPointCount = 0;
            Assert.Equal("Mutilate", Fire(g)?.Name); // HfB skipped → build
        }

        [Fact]
        public void Hunger_for_Blood_respects_its_toggle()
        {
            var s = new RogueSettings();
            s.UseHungerForBlood.Value = false;
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000);
            g.ComboPointCount = 0;
            Assert.Equal("Mutilate", Fire(g, new SoloAssassination(s))?.Name); // off → no HfB
        }

        // --- Cold Blood: cooldown gated like the burst cooldowns (UseCooldowns + pack / lone elite + the CP it needs) ---

        [Fact]
        public void Cold_Blood_pops_on_a_lone_elite_with_finisher_combo_points()
        {
            var g = RogueGame();
            g.SpellsOnCooldown.Add("Evasion"); // Evasion also triggers on a lone elite (higher priority) — isolate
            g.MeUnit.WithAura("Slice and Dice");
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000); // bleed up so Rupture doesn't preempt
            g.ComboPointCount = 5; // ready to finish → pair Cold Blood with it
            Assert.Equal("Cold Blood", Fire(g)?.Name);
        }

        [Fact]
        public void Cold_Blood_held_without_finisher_combo_points()
        {
            var g = RogueGame();
            g.SpellsOnCooldown.Add("Evasion");
            g.MeUnit.WithAura("Slice and Dice");
            g.TargetUnit.IsElite = true;
            g.ComboPointCount = 2; // not enough to finish → don't waste Cold Blood; build instead
            Assert.Equal("Mutilate", Fire(g)?.Name);
        }

        [Fact]
        public void Cold_Blood_respects_the_cooldowns_toggle()
        {
            var s = new RogueSettings();
            s.UseCooldowns.Value = false;
            var g = RogueGame();
            g.SpellsOnCooldown.Add("Evasion");
            g.MeUnit.WithAura("Slice and Dice");
            g.MeUnit.WithAura("Hunger For Blood"); // up so HfB upkeep doesn't preempt the finisher
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000);
            g.ComboPointCount = 5;
            // No Cold Blood; with the bleed up and 5 CP, the default finisher (Eviscerate) fires.
            Assert.Equal("Eviscerate", Fire(g, new SoloAssassination(s))?.Name);
        }

        [Fact]
        public void Cold_Blood_respects_its_own_toggle()
        {
            var s = new RogueSettings();
            s.UseColdBlood.Value = false; // cooldowns on, but Cold Blood specifically disabled
            var g = RogueGame();
            g.SpellsOnCooldown.Add("Evasion");
            g.MeUnit.WithAura("Slice and Dice");
            g.MeUnit.WithAura("Hunger For Blood"); // up so HfB upkeep doesn't preempt the finisher
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000);
            g.ComboPointCount = 5;
            Assert.Equal("Eviscerate", Fire(g, new SoloAssassination(s))?.Name);
        }

        [Fact]
        public void Cold_Blood_skipped_on_a_lone_normal_mob()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.MeUnit.WithAura("Hunger For Blood"); // up so HfB upkeep doesn't preempt the finisher
            g.TargetUnit.WithAura("Rupture", mine: true, timeLeftMs: 10000);
            g.ComboPointCount = 5; // single non-elite → no Cold Blood, just finish (default Eviscerate)
            Assert.Equal("Eviscerate", Fire(g)?.Name);
        }

        // --- stealth opener: positional-free Cheap Shot is the default ---

        [Fact]
        public void Cheap_Shot_opens_from_stealth_by_default()
        {
            var s = new RogueSettings();
            s.UseStealth.Value = true; // stealth opening on; opener defaults to Cheap Shot (front-facing)
            var g = RogueGame();
            g.Stealthed = true;
            Assert.Equal("Cheap Shot", Fire(g, new SoloAssassination(s))?.Name);
        }

        [Fact]
        public void Garrote_opens_from_stealth_when_selected()
        {
            var s = new RogueSettings();
            s.UseStealth.Value = true;
            s.StealthOpener.Value = "Garrote"; // the behind-only alternative
            var g = RogueGame();
            g.Stealthed = true;
            Assert.Equal("Garrote", Fire(g, new SoloAssassination(s))?.Name);
        }

        // --- defensives + interrupt come from the shared baseline (smoke-test they still fire under this spec) ---

        [Fact]
        public void Evasion_fires_below_the_health_threshold()
        {
            var g = RogueGame();
            g.MeUnit.HealthPercent = 20; // below the default 35
            Assert.Equal("Evasion", Fire(g)?.Name);
        }

        [Fact]
        public void Kick_interrupts_a_casting_enemy()
        {
            var g = RogueGame();
            g.SpellsOnCooldown.Remove("Kick");
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 123;
            Assert.Equal("Kick", Fire(g)?.Name);
        }

        // --- the Combat-tree cooldowns are deliberately absent ---

        [Fact]
        public void Combat_cooldowns_are_not_used_by_Assassination()
        {
            var g = RogueGame();
            // A 3-enemy pack would trigger Adrenaline Rush / Blade Flurry in Combat; Assassination never lists those —
            // its pack tool is Fan of Knives (so the cleave is Fan of Knives, NOT the Combat-tree cooldowns).
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0;
            RotationStep fired = Fire(g);
            Assert.NotEqual("Adrenaline Rush", fired?.Name);
            Assert.NotEqual("Killing Spree", fired?.Name);
            Assert.NotEqual("Blade Flurry", fired?.Name);
            Assert.Equal("Fan of Knives", fired?.Name); // the Assassination pack tool cleaves, not a Combat cooldown
        }

        // --- low-level rogue: only Sinister Strike + Eviscerate known → build-and-finish loop still runs ---

        [Fact]
        public void Low_level_rogue_falls_through_to_a_build_and_finish_loop()
        {
            var g = RogueGame();
            g.KnownSpells.Add("Sinister Strike");
            g.KnownSpells.Add("Eviscerate");
            g.KnownSpells.Add("Auto Attack"); // already auto-attacking, so the AutoAttack step won't fire
            g.ComboPointCount = 0;            // Mutilate/SnD unknown → build with Sinister Strike
            Assert.Equal("Sinister Strike", Fire(g)?.Name);
        }

        [Fact]
        public void Low_level_rogue_finishes_with_Eviscerate_when_Envenom_unknown()
        {
            var g = RogueGame();
            g.KnownSpells.Add("Sinister Strike");
            g.KnownSpells.Add("Eviscerate");
            g.KnownSpells.Add("Auto Attack");
            g.ComboPointCount = 3; // at the threshold → the default finisher (Eviscerate) fires
            Assert.Equal("Eviscerate", Fire(g)?.Name);
        }

        // --- Fan of Knives: the Assassination pack AoE (Combat uses Blade Flurry) ---

        [Fact]
        public void Fan_of_Knives_cleaves_a_three_enemy_pack()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice"); // keep finishers/upkeep out of the way
            g.ComboPointCount = 0;
            // Default FanOfKnivesEnemies is 3 → add two more enemies in melee for a 3-pack.
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            Assert.Equal("Fan of Knives", Fire(g)?.Name);
        }

        [Fact]
        public void Fan_of_Knives_skipped_below_the_pack_size()
        {
            var g = RogueGame();
            g.MeUnit.WithAura("Slice and Dice");
            g.ComboPointCount = 0;
            // Two enemies → below the default min of 3 → build instead of cleaving.
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            Assert.Equal("Mutilate", Fire(g)?.Name);
        }
    }
}
