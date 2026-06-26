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
            var s = new RogueSettings();
            RotationStep step = RogueCommon.SliceAndDice(s, minComboPoints: 1, priority: 1f);

            g.ComboPointCount = 0;
            Assert.Null(Fire(g, step)); // no combo points

            g.ComboPointCount = 1;
            Assert.Equal("Slice and Dice", Fire(g, step)?.Name); // missing buff + a cheap CP → refresh

            g.ComboPointCount = s.FinisherComboPoints.Value;
            Assert.Null(Fire(g, step)); // a finisher-worthy bar belongs in the finisher, not an SnD refresh

            g.ComboPointCount = 1;
            g.TargetUnit.HealthPercent = 30;
            Assert.Null(Fire(g, step)); // dying mob → don't waste a CP refreshing SnD

            g.TargetUnit.HealthPercent = 100;
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
            g.ProductFightingFlag = true; // the product has committed to a fight (approaching)

            g.InCombatFlag = true; // in combat → no stealth
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));

            g.InCombatFlag = false; // out of combat, not stealthed → open from stealth
            Assert.Equal("Stealth", Fire(g, RogueCommon.Stealth(s, 1f))?.Name);

            g.Stealthed = true; // already stealthed → done
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));
        }

        [Fact]
        public void Stealth_waits_until_the_product_commits_to_a_fight()
        {
            var s = new RogueSettings();
            s.UseStealth.Value = true;
            var g = Game();
            g.InCombatFlag = false; // out of combat, not stealthed, not resting/debuffed...

            g.ProductFightingFlag = false; // ...but the product is idle/travelling → DON'T stealth (no permanent stealth)
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));

            g.ProductFightingFlag = true;  // product commits to a fight (the approach) → now open from stealth
            Assert.Equal("Stealth", Fire(g, RogueCommon.Stealth(s, 1f))?.Name);
        }

        [Fact]
        public void Stealth_waits_through_the_regeneration_phase()
        {
            var s = new RogueSettings();
            s.UseStealth.Value = true;
            var g = Game();
            g.ProductFightingFlag = true;
            g.InCombatFlag = false; // out of combat, not stealthed...

            g.RestingFlag = true;   // ...but sitting to eat/drink → don't break off the rest to stealth
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));

            g.RestingFlag = false;  // rest finished → open from stealth
            Assert.Equal("Stealth", Fire(g, RogueCommon.Stealth(s, 1f))?.Name);
        }

        [Fact]
        public void Stealth_holds_while_a_dot_is_on_the_rogue()
        {
            var s = new RogueSettings();
            s.UseStealth.Value = true;
            var g = Game();
            g.ProductFightingFlag = true;
            g.InCombatFlag = false;

            g.HarmfulAuraFlag = true; // a lingering DoT/bleed would instantly break stealth → don't bother
            Assert.Null(Fire(g, RogueCommon.Stealth(s, 1f)));

            g.HarmfulAuraFlag = false; // debuff gone → safe to open from stealth
            Assert.Equal("Stealth", Fire(g, RogueCommon.Stealth(s, 1f))?.Name);
        }

        // --- Sinister Strike caps the build at the finisher threshold (no overbuild) ---

        [Fact]
        public void Sinister_Strike_builds_below_the_finisher_threshold()
        {
            var s = new RogueSettings();
            var g = Game();
            g.ComboPointCount = s.FinisherComboPoints.Value - 1; // below the cap → keep building
            Assert.Equal("Sinister Strike", Fire(g, RogueCommon.SinisterStrike(s, 1f))?.Name);
        }

        [Fact]
        public void Sinister_Strike_caps_at_the_finisher_threshold()
        {
            var s = new RogueSettings();
            var g = Game();
            g.ComboPointCount = s.FinisherComboPoints.Value; // at the threshold → a finisher fires, don't overbuild
            Assert.Null(Fire(g, RogueCommon.SinisterStrike(s, 1f)));
        }

        // --- Rupture TTK/execute HP-floor: skip a dying target ---

        [Fact]
        public void Rupture_skips_a_dying_target_below_the_HP_floor()
        {
            var s = new RogueSettings();
            s.UseRupture.Value = true;
            var g = Game();
            g.ComboPointCount = 5;
            g.TargetUnit.IsElite = true;

            g.TargetUnit.HealthPercent = RogueCommon.RuptureMinTargetHealth - 1; // below the floor → bleed won't tick out
            Assert.Null(Fire(g, RogueCommon.Rupture(s, 1f)));

            g.TargetUnit.HealthPercent = RogueCommon.RuptureMinTargetHealth + 1; // above the floor → apply the bleed
            Assert.Equal("Rupture", Fire(g, RogueCommon.Rupture(s, 1f))?.Name);
        }

        // --- Blade Flurry fresh-fight gate: skip a half-dead pack ---

        [Fact]
        public void Blade_Flurry_skips_a_half_dead_pack()
        {
            var s = new RogueSettings();
            var g = Game();
            // A pack in melee (default min 2).
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });

            g.TargetUnit.HealthPercent = RogueCommon.BladeFlurryMinTargetHealth - 1; // below the floor → don't pop it
            Assert.Null(Fire(g, RogueCommon.BladeFlurry(s, 1f)));

            g.TargetUnit.HealthPercent = RogueCommon.BladeFlurryMinTargetHealth + 1; // fresh enough → cleave
            Assert.Equal("Blade Flurry", Fire(g, RogueCommon.BladeFlurry(s, 1f))?.Name);
        }

        // --- Evasion: the target-healthy qualifier gates ONLY the low-HP trigger ---

        [Fact]
        public void Evasion_HP_trigger_requires_a_healthy_target()
        {
            var s = new RogueSettings();
            s.EvasionEnemies.Value = 99; // disable the surrounded trigger; isolate the HP trigger
            var g = Game();
            // MODERATE low HP (below the 35 trigger, above the 25 panic) so the dying-mob qualifier applies.
            g.MeUnit.HealthPercent = 30;

            g.TargetUnit.HealthPercent = RogueCommon.EvasionMinTargetHealth - 1; // mob is dying → don't burn Evasion
            Assert.Null(Fire(g, RogueCommon.Evasion(s, 1f)));

            g.TargetUnit.HealthPercent = RogueCommon.EvasionMinTargetHealth + 1; // healthy target → fire
            Assert.Equal("Evasion", Fire(g, RogueCommon.Evasion(s, 1f))?.Name);
        }

        [Fact]
        public void Evasion_panics_at_critical_HP_even_on_a_dying_target()
        {
            var s = new RogueSettings();
            s.EvasionEnemies.Value = 99; // isolate the HP trigger
            var g = Game();
            g.MeUnit.HealthPercent = RogueCommon.EvasionPanicHealthPercent - 1; // critically low — losing the race
            g.TargetUnit.HealthPercent = 26; // dying mob, but survival wins: Evasion fires anyway (the 1%-HP-Kodo case)
            Assert.Equal("Evasion", Fire(g, RogueCommon.Evasion(s, 1f))?.Name);
        }

        [Fact]
        public void Evasion_surrounded_trigger_fires_regardless_of_target_HP()
        {
            var s = new RogueSettings();
            s.EvasionHealthPercent.Value = 0; // disable the HP trigger; isolate the surrounded trigger
            var g = Game();
            g.MeUnit.HealthPercent = 100;
            g.TargetUnit.HealthPercent = 5; // a dying target must NOT suppress the surrounded trigger
            g.TargetUnit.IsTargetingMe = true;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsTargetingMe = true });
            Assert.Equal("Evasion", Fire(g, RogueCommon.Evasion(s, 1f))?.Name);
        }

        [Fact]
        public void Evasion_lone_elite_trigger_fires_regardless_of_target_HP()
        {
            var s = new RogueSettings();
            s.EvasionHealthPercent.Value = 0; // HP trigger off
            s.EvasionEnemies.Value = 99;      // surrounded trigger off → isolate the lone-elite trigger
            var g = Game();
            g.MeUnit.HealthPercent = 100;
            g.TargetUnit.IsElite = true;
            g.TargetUnit.HealthPercent = 5; // a dying elite must NOT suppress the lone-elite trigger
            Assert.Equal("Evasion", Fire(g, RogueCommon.Evasion(s, 1f))?.Name);
        }

        // --- Fan of Knives: Assassination pack AoE ---

        [Fact]
        public void Fan_of_Knives_fires_on_a_pack()
        {
            var s = new RogueSettings();
            var g = Game();
            // Default FanOfKnivesEnemies is 3 → need three enemies in melee.
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            Assert.Equal("Fan of Knives", Fire(g, RogueCommon.FanOfKnives(s, 1f))?.Name);
        }

        [Fact]
        public void Fan_of_Knives_skipped_below_the_pack_size()
        {
            var s = new RogueSettings();
            var g = Game();
            // Two enemies in melee → below the default min of 3.
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            Assert.Null(Fire(g, RogueCommon.FanOfKnives(s, 1f)));
        }

        [Fact]
        public void Fan_of_Knives_skipped_while_stealthed()
        {
            var s = new RogueSettings();
            var g = Game();
            g.Stealthed = true; // opener territory — the AoE waits
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 6, IsAttackable = true });
            Assert.Null(Fire(g, RogueCommon.FanOfKnives(s, 1f)));
        }
    }
}
