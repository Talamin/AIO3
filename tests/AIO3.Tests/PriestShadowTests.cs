using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Priest;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // End-to-end priority tests for the Solo Shadow priest + the shared PriestCommon blocks. The base game is a
    // Shadow priest IN SHADOWFORM at full HP / full mana, casting at one full-health non-elite enemy in range, with
    // all DoTs already applied (long durations) and the OOC buffs pre-applied, so each test isolates the rule under
    // test unless it opts in. PlayerInCombat is left false so the in-combat offensive racials don't preempt the
    // rule under test (same convention as the other spec tests); nothing in the shadow DPS core gates on
    // PlayerInCombat. The Shadow Weaving talent is NOT taken by default (so SW:Pain refreshes freely); a dedicated
    // test takes it.
    public class PriestShadowTests
    {
        private const long Long = 60_000; // a DoT/buff that won't need refreshing this tick

        private static FakeGameClient ShadowGame()
        {
            var g = new FakeGameClient { Class = WowClass.Priest };
            g.MeUnit.HealthPercent = 100;
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.WithAura("Shadowform");      // in the DPS form by default
            g.MeUnit.WithAura("Inner Fire");      // upkeep buffs already up so they don't preempt
            g.MeUnit.WithAura("Vampiric Embrace");
            g.MeUnit.WithAura("Power Word: Fortitude");
            g.MeUnit.WithAura("Divine Spirit");
            g.MeUnit.WithAura("Shadow Protection");
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 20,
                HealthPercent = 100,
                IsAttackable = true
            };
            // All single-target DoTs already up (long) so the DoT-maintain steps don't preempt non-DoT tests.
            g.TargetUnit.WithAura("Vampiric Touch", mine: true, timeLeftMs: Long);
            g.TargetUnit.WithAura("Devouring Plague", mine: true, timeLeftMs: Long);
            g.TargetUnit.WithAura("Shadow Word: Pain", mine: true, timeLeftMs: Long);
            g.EnemyList.Add(g.TargetUnit);
            // Mind Blast on cooldown by default so it doesn't win every "what's the filler" test.
            g.SpellsOnCooldown.Add("Mind Blast");
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloShadow().BuildSteps()).Tick(CombatContext.Capture(g));

        private static RotationStep Fire(FakeGameClient g, PriestSettings s) =>
            new RotationEngine(new SoloShadow(s).BuildSteps()).Tick(CombatContext.Capture(g));

        // --- seams read through in Shadowform (the in-game verification proxy) ---

        [Fact]
        public void Mana_and_shadowform_aura_read_through()
        {
            var g = ShadowGame();
            g.MeUnit.PowerPercent = 55;
            CombatContext ctx = CombatContext.Capture(g);
            Assert.Equal(55, ctx.Me.PowerPercent);
            Assert.True(PriestCommon.InShadowform(ctx));
        }

        // --- the shadow DPS core (single-target) ---

        [Fact]
        public void Mind_Blast_is_the_cooldown_nuke_when_DoTs_are_up()
        {
            var g = ShadowGame();
            g.SpellsOnCooldown.Remove("Mind Blast"); // off cooldown → it's the nuke
            Assert.Equal("Mind Blast", Fire(g)?.Name);
        }

        [Fact]
        public void Vampiric_Touch_leads_the_DoTs_when_it_falls_off()
        {
            var g = ShadowGame();
            g.TargetUnit.Auras.Remove("Vampiric Touch"); // VT missing → it's the priority DoT
            Assert.Equal("Vampiric Touch", Fire(g)?.Name);
        }

        [Fact]
        public void Devouring_Plague_maintained_when_missing()
        {
            var g = ShadowGame();
            g.TargetUnit.Auras.Remove("Devouring Plague"); // VT still up → DP is next
            Assert.Equal("Devouring Plague", Fire(g)?.Name);
        }

        [Fact]
        public void Devouring_Plague_respects_its_toggle()
        {
            var s = new PriestSettings();
            s.UseDevouringPlague.Value = false;
            var g = ShadowGame();
            g.TargetUnit.Auras.Remove("Devouring Plague"); // would maintain, but toggle off → fall through
            Assert.NotEqual("Devouring Plague", Fire(g, s)?.Name);
        }

        [Fact]
        public void Devouring_Plague_skipped_on_a_dying_normal_mob()
        {
            var g = ShadowGame();
            g.TargetUnit.Auras.Remove("Devouring Plague");
            g.TargetUnit.HealthPercent = 35; // below the normal floor (40) → don't refresh a fresh self-heal DoT
            Assert.NotEqual("Devouring Plague", Fire(g)?.Name);
        }

        [Fact]
        public void Shadow_Word_Pain_maintained_when_missing_and_untalented()
        {
            var g = ShadowGame();
            g.TargetUnit.Auras.Remove("Shadow Word: Pain"); // SW:Pain missing, Shadow Weaving NOT taken → refresh
            Assert.Equal("Shadow Word: Pain", Fire(g)?.Name);
        }

        // --- Shadow Weaving gate on SW:Pain (the HasTalent(3,12) interlock) ---

        [Fact]
        public void Shadow_Word_Pain_held_until_five_weaving_stacks_when_talented()
        {
            var g = ShadowGame();
            g.Talents.Add((PriestCommon.ShadowWeavingTalentTab, PriestCommon.ShadowWeavingTalentIndex)); // talented
            g.TargetUnit.Auras.Remove("Shadow Word: Pain");
            g.MeUnit.WithAura("Shadow Weaving", stacks: 3); // not yet 5 → hold SW:Pain
            Assert.NotEqual("Shadow Word: Pain", Fire(g)?.Name);
        }

        [Fact]
        public void Shadow_Word_Pain_applied_at_five_weaving_stacks_when_talented()
        {
            var g = ShadowGame();
            g.Talents.Add((PriestCommon.ShadowWeavingTalentTab, PriestCommon.ShadowWeavingTalentIndex));
            g.TargetUnit.Auras.Remove("Shadow Word: Pain");
            g.MeUnit.WithAura("Shadow Weaving", stacks: 5); // full stacks → snapshot it now
            Assert.Equal("Shadow Word: Pain", Fire(g)?.Name);
        }

        // --- Mind Flay channelled filler (opt-in) ---

        [Fact]
        public void Mind_Flay_fills_when_enabled_and_a_DoT_is_up()
        {
            var s = new PriestSettings();
            s.UseMindFlay.Value = true;
            var g = ShadowGame(); // SW:Pain + DP up by default → Mind Flay is eligible
            Assert.Equal("Mind Flay", Fire(g, s)?.Name);
        }

        [Fact]
        public void Mind_Flay_held_when_moving()
        {
            var s = new PriestSettings();
            s.UseMindFlay.Value = true;
            var g = ShadowGame();
            g.Moving = true; // channel → can't start on the move
            Assert.NotEqual("Mind Flay", Fire(g, s)?.Name);
        }

        [Fact]
        public void Mind_Flay_off_by_default()
        {
            var g = ShadowGame(); // default UseMindFlay = false
            Assert.NotEqual("Mind Flay", Fire(g)?.Name);
        }

        // --- Mind Sear AoE (target-anchored cluster) ---

        [Fact]
        public void Mind_Sear_fires_when_a_pack_clusters_on_the_target()
        {
            var g = ShadowGame();
            // Two extra enemies at the target's position (within 11y of the target) → 3 around the target.
            g.TargetUnit.X = 0; g.TargetUnit.Y = 0;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 20, IsAttackable = true, X = 1, Y = 1 });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 20, IsAttackable = true, X = 2, Y = 0 });
            Assert.Equal("Mind Sear", Fire(g)?.Name);
        }

        [Fact]
        public void Mind_Sear_held_when_mana_too_low()
        {
            var g = ShadowGame();
            g.TargetUnit.X = 0; g.TargetUnit.Y = 0;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 20, IsAttackable = true, X = 1, Y = 1 });
            g.MeUnit.PowerPercent = 50; // below the 65 Mind Sear mana gate (but above heal/dispersion) → no Mind Sear
            Assert.NotEqual("Mind Sear", Fire(g)?.Name);
        }

        // --- in-form survival (castable in Shadowform; no form drop) ---

        [Fact]
        public void Power_Word_Shield_fires_low_without_dropping_form()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 50; // below the 60 shield threshold; shield/Weakened Soul not up
            Assert.Equal("Power Word: Shield", Fire(g)?.Name);
        }

        [Fact]
        public void Power_Word_Shield_skipped_while_weakened_soul_is_up()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 50;
            g.MeUnit.WithAura("Weakened Soul"); // the shield's cooldown debuff → don't re-shield
            Assert.NotEqual("Power Word: Shield", Fire(g)?.Name);
        }

        [Fact]
        public void Dispersion_fires_low_on_mana_and_is_off_gcd()
        {
            var g = ShadowGame();
            g.MeUnit.PowerPercent = 25; // below the 30 dispersion threshold
            RotationStep step = Fire(g);
            Assert.Equal("Dispersion", step?.Name);
        }

        [Fact]
        public void Psychic_Scream_panics_when_surrounded_and_low_solo()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 70; // below the 80 scream threshold
            // Two enemies in melee on us (within 6y).
            g.TargetUnit.IsTargetingMe = true; g.TargetUnit.Distance = 5;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 5, IsTargetingMe = true, IsAttackable = true });
            Assert.Equal("Psychic Scream", Fire(g)?.Name);
        }

        [Fact]
        public void Psychic_Scream_not_used_in_a_group()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 70;
            g.TargetUnit.IsTargetingMe = true; g.TargetUnit.Distance = 5;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 5, IsTargetingMe = true, IsAttackable = true });
            g.PartyList.Add(g.MeUnit);
            g.PartyList.Add(new FakeUnit { Guid = 9, Reaction = Reaction.Friendly }); // 2 members → in a group
            Assert.NotEqual("Psychic Scream", Fire(g)?.Name);
        }

        // --- THE SHADOWFORM HEAL INTERLOCK (drop form → heal → re-enter form) ---

        [Fact]
        public void Drops_shadowform_first_when_a_hard_heal_is_wanted()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 35; // below the 40 hard-heal threshold, mana fine
            g.MeUnit.WithAura("Power Word: Shield"); // shield already up → isolate the heal interlock from it
            // In form → the form-drop leads (casting "Shadowform" toggles it off); the heal fires next tick.
            RotationStep step = Fire(g);
            Assert.Equal("Shadowform", step?.Name);
            Assert.Contains("Shadowform", g.CastLog); // the toggle-off cast was issued
        }

        [Fact]
        public void Best_heal_fires_once_out_of_form()
        {
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform"); // already dropped (the next beat)
            g.MeUnit.WithAura("Power Word: Shield"); // shield already up → isolate the heal from it
            g.MeUnit.HealthPercent = 35; // below the hard-heal threshold but above flash? flash default is 60...
            // Flash Heal (60) outranks Heal (40) and both apply at 35; Flash leads.
            Assert.Equal("Flash Heal", Fire(g)?.Name);
        }

        [Fact]
        public void Best_heal_resolves_to_the_best_known_heal_when_flash_is_off()
        {
            var s = new PriestSettings();
            s.FlashHealHealthPercent.Value = 0; // disable Flash so the hard-heal resolver shows
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform");
            g.MeUnit.WithAura("Power Word: Shield"); // shield already up → isolate the heal resolver from it
            g.MeUnit.HealthPercent = 35;
            g.UnknownSpells.Add("Greater Heal"); // not learned → falls to Heal
            RotationStep step = Fire(g, s);
            Assert.Equal("Heal", step?.Name);   // step name is the generic "Heal"
            Assert.Contains("Heal", g.CastLog); // resolved + cast "Heal" (Greater Heal unknown)
        }

        [Fact]
        public void Hard_heal_held_when_mana_too_low_so_it_does_not_thrash_out_of_form()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 35;  // would want a heal...
            g.MeUnit.PowerPercent = 35;   // ...but below the 40 heal mana floor → don't drop form for a heal we can't afford
            Assert.NotEqual("Shadowform", Fire(g)?.Name); // no form drop
        }

        [Fact]
        public void Reenters_shadowform_when_healed_and_out_of_form()
        {
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform"); // out of form, healthy now → re-enter
            // healthy (100) so no heal is wanted; ShadowformUpkeep should re-enter the form.
            Assert.Equal("Shadowform", Fire(g)?.Name);
            Assert.Contains("Shadowform", g.CastLog);
        }

        [Fact]
        public void Does_not_reenter_form_while_a_heal_is_still_wanted()
        {
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform"); // out of form
            g.MeUnit.HealthPercent = 35;         // still hurt → heal, don't re-enter form yet
            Assert.NotEqual("Shadowform", Fire(g)?.Name); // the upkeep is held; a heal fires instead
        }

        [Fact]
        public void Renew_tops_off_out_of_form_without_dropping_form_for_it()
        {
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform"); // out of form already
            g.MeUnit.HealthPercent = 85;         // below Renew (90) but above Flash/Heal → Renew the HoT
            // But ShadowformUpkeep (re-enter form) sits BELOW the heals; Renew (1.3) beats the upkeep (1.9).
            Assert.Equal("Renew", Fire(g)?.Name);
        }

        [Fact]
        public void Renew_does_not_drop_form_on_its_own()
        {
            var g = ShadowGame(); // in form
            g.MeUnit.HealthPercent = 85; // below Renew threshold but NOT the hard-heal threshold (40)
            // No hard heal wanted (HP above flash/heal), so no form drop; Renew can't fire in form → fall through to DPS.
            Assert.NotEqual("Shadowform", Fire(g)?.Name);
            Assert.NotEqual("Renew", Fire(g)?.Name);
        }

        // --- mana tools ---

        [Fact]
        public void Shadowfiend_fires_below_its_mana_threshold()
        {
            var g = ShadowGame();
            g.MeUnit.PowerPercent = 25; // below the 30 shadowfiend threshold
            g.SpellsOnCooldown.Add("Dispersion"); // isolate from Dispersion (also 30) — fiend should still show
            // Dispersion (0.25) outranks Shadowfiend (1.6); with Dispersion on CD, Shadowfiend wins.
            Assert.Equal("Shadowfiend", Fire(g)?.Name);
        }

        [Fact]
        public void Wand_finishes_a_low_target_off_the_gcd()
        {
            var g = ShadowGame();
            g.TargetUnit.HealthPercent = 15; // at/below the 20 wand-target threshold → wand it
            Assert.Equal("Shoot", Fire(g)?.Name);
        }

        [Fact]
        public void Wand_respects_its_toggle()
        {
            var s = new PriestSettings();
            s.UseWand.Value = false;
            var g = ShadowGame();
            g.TargetUnit.HealthPercent = 15;
            Assert.NotEqual("Shoot", Fire(g, s)?.Name);
        }

        // --- emergency item (off the GCD; the top of the list) ---

        [Fact]
        public void Emergency_item_leads_when_critically_low()
        {
            var g = ShadowGame();
            g.MeUnit.HealthPercent = 25; // below the 30 emergency threshold
            g.ReadyItems.Add("Healthstone");
            RotationStep step = Fire(g);
            Assert.Equal("Emergency heal", step?.Name);
        }

        // --- pre-Shadowform (low level): Smite carries, and the heals work without a form drop ---

        [Fact]
        public void Pre_form_priest_fills_with_Smite()
        {
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform");   // not in form
            g.UnknownSpells.Add("Shadowform");     // ...and can't enter it (pre-Shadowform)
            // No DoTs known yet at low level — strip them so the shadow core is inert.
            g.UnknownSpells.Add("Vampiric Touch");
            g.UnknownSpells.Add("Devouring Plague");
            g.UnknownSpells.Add("Shadow Word: Pain");
            g.UnknownSpells.Add("Mind Blast");
            g.TargetUnit.HealthPercent = 80; // above the wand threshold so the wand doesn't preempt
            Assert.Equal("Smite", Fire(g)?.Name);
        }

        [Fact]
        public void Pre_form_priest_heals_without_a_form_drop()
        {
            var g = ShadowGame();
            g.MeUnit.Auras.Remove("Shadowform");
            g.UnknownSpells.Add("Shadowform");
            g.MeUnit.WithAura("Power Word: Shield"); // shield already up → isolate the heal from it
            g.MeUnit.HealthPercent = 35; // hard-heal threshold — out of form already → heal directly (Flash leads)
            Assert.Equal("Flash Heal", Fire(g)?.Name);
        }

        [Fact]
        public void Smite_goes_quiet_in_shadowform()
        {
            var g = ShadowGame(); // in form, all DoTs up, Mind Blast on CD, Mind Flay off
            // Nothing in the shadow core is eligible (DoTs up, MB on CD), so the engine returns null rather than
            // casting Smite (which is locked out in form).
            Assert.NotEqual("Smite", Fire(g)?.Name);
        }
    }
}
