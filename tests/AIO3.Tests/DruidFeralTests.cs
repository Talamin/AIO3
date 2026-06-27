using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Druid;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // End-to-end priority tests for the Solo Feral druid, plus the shared DruidCommon cat/bear ladders and the
    // form-switch glue. The base game is a feral druid in CAT FORM, in melee on one full-health, non-elite enemy,
    // auto-attacking, with the burst cooldowns + Bash on cooldown so the tests isolate the ladder rules unless
    // they opt in. PlayerInCombat is left false so the in-combat offensive racials don't preempt the rule under
    // test (same convention as the Rogue/Warrior spec tests); nothing in the feral cat ladder gates on
    // PlayerInCombat.
    public class DruidFeralTests
    {
        private static FakeGameClient CatGame()
        {
            var g = new FakeGameClient
            {
                Class = WowClass.Druid,
                AutoAttacking = true
            };
            g.MeUnit.WithAura("Cat Form"); // in cat form by default
            g.MeUnit.WithAura("Mark of the Wild"); // pre-buffed (OOC buffs are up before the pull) so they don't preempt
            g.MeUnit.WithAura("Thorns");
            g.MeUnit.WithAura("Savage Roar"); // kept up by default so the finisher-order tests isolate Rip/FB (a
                                              // dedicated test removes it to prove Savage Roar leads the finishers)
            g.MeUnit.Energy = 100;
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100,
                IsAttackable = true
            };
            g.TargetUnit.WithAura("Faerie Fire (Feral)", mine: true); // armor debuff already applied (it leads the fight)
            g.EnemyList.Add(g.TargetUnit);
            // Neutralise the burst cooldown + interrupt + Tiger's Fury so they don't preempt the rules under test.
            g.SpellsOnCooldown.Add("Berserk");
            g.SpellsOnCooldown.Add("Bash");
            g.SpellsOnCooldown.Add("Tiger's Fury");
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloFeral().BuildSteps()).Tick(CombatContext.Capture(g));

        private static RotationStep Fire(FakeGameClient g, SoloFeral rotation) =>
            new RotationEngine(rotation.BuildSteps()).Tick(CombatContext.Capture(g));

        // --- seams read through in cat form (the in-game verification proxy) ---

        [Fact]
        public void Energy_and_combo_points_read_through_in_cat_form()
        {
            var g = CatGame();
            g.MeUnit.Energy = 80;
            g.ComboPointCount = 3;
            CombatContext ctx = CombatContext.Capture(g);
            Assert.Equal(80, ctx.Me.Energy);
            Assert.Equal(3, ctx.ComboPoints);
        }

        // --- cat builder ladder ---

        [Fact]
        public void Mangle_Cat_is_the_primary_builder_below_the_finisher_threshold()
        {
            var g = CatGame();
            g.ComboPointCount = 0;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000); // Rake already up → builder is next
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        [Fact]
        public void Claw_is_the_builder_fallback_when_Mangle_is_unavailable()
        {
            var g = CatGame();
            g.ComboPointCount = 0;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.SpellsOnCooldown.Add("Mangle (Cat)"); // Mangle down → Claw fills in
            Assert.Equal("Claw", Fire(g)?.Name);
        }

        [Fact]
        public void Builder_stops_at_the_finisher_threshold()
        {
            var s = new DruidSettings(); // FinisherComboPoints default 5
            var g = CatGame();
            g.ComboPointCount = 5;                                   // at the threshold → a finisher fires, not a builder
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.TargetUnit.WithAura("Rip", mine: true, timeLeftMs: 12000); // Rip up → Ferocious Bite dumps
            Assert.Equal("Ferocious Bite", Fire(g, new SoloFeral(s))?.Name);
        }

        // --- Rake (bleed, apply when missing on a healthy target, HP-floored) ---

        [Fact]
        public void Rake_applied_when_missing_on_a_healthy_target()
        {
            var g = CatGame();
            g.ComboPointCount = 0; // missing Rake outranks the builder
            Assert.Equal("Rake", Fire(g)?.Name);
        }

        [Fact]
        public void Rake_skipped_on_a_dying_target()
        {
            var g = CatGame();
            g.ComboPointCount = 0;
            g.TargetUnit.HealthPercent = 20; // below the Rip-health floor (30) → don't waste a bleed; build instead
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        [Fact]
        public void Rake_skipped_on_a_bleed_immune_creature()
        {
            var g = CatGame();
            g.ComboPointCount = 0;
            g.TargetUnit.CreatureType = "Elemental"; // bleed-immune → no Rake; build instead
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        // --- finishers at the CP threshold ---

        [Fact]
        public void Rip_is_the_bleed_finisher_on_a_durable_target()
        {
            var g = CatGame();
            g.ComboPointCount = 5;                                   // at the finisher threshold
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000); // Rake up so it doesn't preempt
            // Rip missing on a healthy durable target → it wins over Ferocious Bite.
            Assert.Equal("Rip", Fire(g)?.Name);
        }

        [Fact]
        public void Ferocious_Bite_dumps_when_Rip_is_already_up()
        {
            var g = CatGame();
            g.ComboPointCount = 5;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.TargetUnit.WithAura("Rip", mine: true, timeLeftMs: 12000); // Rip up → Ferocious Bite gets the points
            Assert.Equal("Ferocious Bite", Fire(g)?.Name);
        }

        [Fact]
        public void Rip_skipped_on_a_dying_target_so_Ferocious_Bite_dumps()
        {
            var g = CatGame();
            g.ComboPointCount = 5;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.TargetUnit.HealthPercent = 20; // below the Rip-health floor → no fresh bleed; dump with Ferocious Bite
            Assert.Equal("Ferocious Bite", Fire(g)?.Name);
        }

        [Fact]
        public void Finisher_threshold_setting_is_respected()
        {
            var s = new DruidSettings();
            s.FinisherComboPoints.Value = 5;
            var g = CatGame();
            g.ComboPointCount = 4; // below the raised threshold → build, no finisher
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            Assert.Equal("Mangle (Cat)", Fire(g, new SoloFeral(s))?.Name);
        }

        // --- Tiger's Fury (off-GCD energy/damage CD) ---

        [Fact]
        public void Tigers_Fury_pops_on_cooldown_in_cat_form()
        {
            var g = CatGame();
            g.SpellsOnCooldown.Remove("Tiger's Fury");
            g.MeUnit.Energy = 20; // below the energy cap → the +energy burst isn't wasted
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000); // keep the ladder out of the way
            Assert.Equal("Tiger's Fury", Fire(g)?.Name);
        }

        [Fact]
        public void Tigers_Fury_held_at_high_energy()
        {
            var g = CatGame();
            g.SpellsOnCooldown.Remove("Tiger's Fury");
            g.MeUnit.Energy = 90; // near full → popping the +energy burst now would overcap, so hold it
            g.ComboPointCount = 0;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000); // Rake up → the builder is next, not TF
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name); // Mangle debuff is missing → debuff-maintain Mangle fires
        }

        [Fact]
        public void Tigers_Fury_respects_its_toggle()
        {
            var s = new DruidSettings();
            s.UseTigersFury.Value = false;
            var g = CatGame();
            g.SpellsOnCooldown.Remove("Tiger's Fury");
            g.ComboPointCount = 0;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            Assert.Equal("Mangle (Cat)", Fire(g, new SoloFeral(s))?.Name);
        }

        // --- Faerie Fire (Feral) armor debuff ---

        [Fact]
        public void Faerie_Fire_applied_when_missing()
        {
            var g = CatGame();
            g.TargetUnit.Auras.Remove("Faerie Fire (Feral)"); // debuff missing → FF (priority 4) leads
            Assert.Equal("Faerie Fire (Feral)", Fire(g)?.Name);
        }

        // --- form switching ---

        [Fact]
        public void Bear_Form_switch_when_surrounded()
        {
            var g = CatGame();
            // Two attackers in melee on us (default BearCount 2) → switch to bear.
            g.TargetUnit.IsTargetingMe = true;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsTargetingMe = true, IsAttackable = true });
            // Already in cat form (CatGame), but surrounded → Bear/Dire Bear switch wins. The step name is the
            // generic "Bear Form"; the actual cast (Dire Bear preferred, known by default) shows in the CastLog.
            Assert.Equal("Bear Form", Fire(g)?.Name);
            Assert.Contains("Dire Bear Form", g.CastLog);
        }

        [Fact]
        public void Cat_Form_switch_when_not_in_a_form_and_not_surrounded()
        {
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form"); // not in any form, single target → switch to cat
            Assert.Equal("Cat Form", Fire(g)?.Name);
        }

        [Fact]
        public void Bear_Form_single_target_while_Cat_Form_is_not_learned_yet()
        {
            // Level 10-19: only Bear Form is known. Single-target, the druid fights in BEAR (not the caster filler),
            // since Cat Form is trained later (~20). Bear takes single-target until then.
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form");     // not in any form
            g.UnknownSpells.Add("Cat Form");       // not learned yet
            g.UnknownSpells.Add("Dire Bear Form"); // plain Bear at this level
            Assert.Equal("Bear Form", Fire(g)?.Name); // single target, no Cat → shift to Bear, not stay a caster
            Assert.Contains("Bear Form", g.CastLog);
        }

        [Fact]
        public void Bear_Form_falls_back_to_Bear_when_Dire_Bear_is_unknown()
        {
            var g = CatGame();
            g.UnknownSpells.Add("Dire Bear Form"); // low-level druid → plain Bear Form
            g.TargetUnit.IsTargetingMe = true;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsTargetingMe = true, IsAttackable = true });
            Assert.Equal("Bear Form", Fire(g)?.Name);
            Assert.Contains("Bear Form", g.CastLog); // Dire Bear unknown → plain Bear Form cast
        }

        // --- bear ladder ---

        private static FakeGameClient BearGame()
        {
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form");
            g.MeUnit.WithAura("Dire Bear Form"); // in bear form
            g.MeUnit.Rage = 100;
            // Surround so the form-switch steps don't fire and the bear ladder is eligible.
            g.TargetUnit.IsTargetingMe = true;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsTargetingMe = true, IsAttackable = true });
            return g;
        }

        [Fact]
        public void Mangle_Bear_maintained_when_missing()
        {
            var g = BearGame();
            // Demoralizing Roar (priority 5) would fire first on a pack; give the target the roar so Mangle shows.
            g.TargetUnit.WithAura("Demoralizing Roar", mine: true);
            g.MeUnit.WithAura("Enrage"); // keep Enrage out of the way
            Assert.Equal("Mangle (Bear)", Fire(g)?.Name);
        }

        [Fact]
        public void Swipe_Bear_cleaves_a_pack()
        {
            var g = BearGame();
            g.TargetUnit.WithAura("Demoralizing Roar", mine: true);
            g.MeUnit.WithAura("Enrage");
            g.TargetUnit.WithAura("Mangle", mine: true);   // Mangle up
            g.TargetUnit.WithAura("Lacerate", mine: true, timeLeftMs: 15000); // Lacerate up → Swipe is next
            Assert.Equal("Swipe (Bear)", Fire(g)?.Name);
        }

        [Fact]
        public void Frenzied_Regeneration_fires_low_in_bear_form()
        {
            var g = BearGame();
            g.MeUnit.HealthPercent = 50; // below 60, with rage → bear survival
            Assert.Equal("Frenzied Regeneration", Fire(g)?.Name);
        }

        [Fact]
        public void Demoralizing_Roar_applied_on_a_pack_when_missing()
        {
            var g = BearGame();
            g.MeUnit.WithAura("Enrage"); // keep Enrage out of the way
            // Pack on us, roar missing → Demoralizing Roar (priority 5) wins.
            Assert.Equal("Demoralizing Roar", Fire(g)?.Name);
        }

        // --- survival: Barkskin + in-combat heals ---

        [Fact]
        public void Barkskin_fires_below_the_threshold()
        {
            var g = CatGame();
            g.MeUnit.HealthPercent = 30; // below the default 35
            Assert.Equal("Barkskin", Fire(g)?.Name);
        }

        [Fact]
        public void Instant_proc_heal_preferred_over_shift_out_in_cat_form()
        {
            var g = CatGame();
            g.MeUnit.HealthPercent = 30;            // below the IC-heal threshold (35)
            g.SpellsOnCooldown.Add("Barkskin");     // isolate the heal (Barkskin would win otherwise)
            g.SpellsOnCooldown.Add("Survival Instincts"); // ...and the new low-HP defensive
            g.MeUnit.WithAura("Predator's Swiftness"); // free instant proc up → form-preserving Regrowth
            Assert.Equal("Regrowth", Fire(g)?.Name);
        }

        [Fact]
        public void Shift_out_heal_used_when_no_proc_and_mana_allows()
        {
            var g = CatGame();
            g.MeUnit.HealthPercent = 30;
            g.SpellsOnCooldown.Add("Barkskin");
            g.SpellsOnCooldown.Add("Survival Instincts");
            g.MeUnit.PowerPercent = 80; // plenty of mana, no proc → shift-out Regrowth
            Assert.Equal("Regrowth", Fire(g)?.Name);
        }

        [Fact]
        public void Shift_out_heal_held_when_mana_too_low()
        {
            var g = CatGame();
            g.MeUnit.HealthPercent = 30;
            g.SpellsOnCooldown.Add("Barkskin");
            g.SpellsOnCooldown.Add("Survival Instincts");
            // Between the Innervate threshold (25) and the heal mana gate (30): no Innervate, and the shift-out
            // heal is held by the mana gate (no proc) → don't thrash out of form; keep fighting.
            g.MeUnit.PowerPercent = 28;
            g.ComboPointCount = 0;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        [Fact]
        public void Innervate_suppressed_in_cat_form()
        {
            // A feral shouldn't shift OUT of cat just to Innervate (it has no mana to regen mid-fight). Low mana in
            // cat form → no Innervate; the rotation keeps fighting instead (item 13).
            var g = CatGame();
            g.MeUnit.PowerPercent = 20; // below the default 25
            g.SpellsOnCooldown.Add("Barkskin");
            g.ComboPointCount = 0;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        [Fact]
        public void Innervate_fires_low_on_mana_when_formless()
        {
            // A pre-form (or shifted-out) druid at low mana still gets Innervate — the form gate only blocks it
            // while in cat/bear.
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form"); // formless
            g.UnknownSpells.Add("Cat Form");   // and can't re-shift, so it stays formless this tick
            g.UnknownSpells.Add("Bear Form");
            g.UnknownSpells.Add("Dire Bear Form");
            g.MeUnit.PowerPercent = 20;
            g.SpellsOnCooldown.Add("Barkskin");
            Assert.Equal("Innervate", Fire(g)?.Name);
        }

        // --- prowl opener (opt-in) ---

        [Fact]
        public void Auto_prowl_opener_uses_Ravage_when_behind()
        {
            var s = new DruidSettings();
            s.UseProwl.Value = true;
            var g = CatGame();
            g.MeUnit.WithAura("Prowl");
            g.BehindTargetFlag = true; // behind → Auto picks Ravage
            Assert.Equal("Ravage", Fire(g, new SoloFeral(s))?.Name);
        }

        [Fact]
        public void Auto_prowl_opener_uses_Pounce_from_the_front()
        {
            var s = new DruidSettings();
            s.UseProwl.Value = true;
            var g = CatGame();
            g.MeUnit.WithAura("Prowl");
            g.BehindTargetFlag = false; // not behind → Auto picks the positional-free Pounce
            Assert.Equal("Pounce", Fire(g, new SoloFeral(s))?.Name);
        }

        // --- pre-form caster fallback (a low-level druid, no Cat/Bear yet) ---

        [Fact]
        public void Pre_form_druid_applies_Moonfire_then_Wraths()
        {
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form"); // no form
            g.UnknownSpells.Add("Cat Form");   // and can't shift (pre-form)
            g.UnknownSpells.Add("Bear Form");
            g.UnknownSpells.Add("Dire Bear Form");
            // Moonfire missing on a healthy target → it leads the caster fallback.
            Assert.Equal("Moonfire", Fire(g)?.Name);
        }

        [Fact]
        public void Pre_form_druid_casts_Wrath_when_Moonfire_is_up()
        {
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form");
            g.UnknownSpells.Add("Cat Form");
            g.UnknownSpells.Add("Bear Form");
            g.UnknownSpells.Add("Dire Bear Form");
            g.TargetUnit.WithAura("Moonfire", mine: true); // Moonfire up → Wrath fills
            Assert.Equal("Wrath", Fire(g)?.Name);
        }

        // --- Shred (best cat builder, behind-only) ---

        [Fact]
        public void Shred_is_the_builder_when_behind_the_target()
        {
            var g = CatGame();
            g.ComboPointCount = 0;
            g.BehindTargetFlag = true; // behind → Shred is usable and outranks Mangle/Claw
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000); // Rake up
            g.TargetUnit.WithAura("Mangle", mine: true); // +30% debuff up → the Mangle debuff-maintain stays quiet
            Assert.Equal("Shred", Fire(g)?.Name);
        }

        [Fact]
        public void Mangle_Cat_is_the_front_fallback_when_not_behind()
        {
            var g = CatGame();
            g.ComboPointCount = 0;
            g.BehindTargetFlag = false; // in front → Shred can't land; the front builder fills
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.TargetUnit.WithAura("Mangle", mine: true); // debuff up so the debuff-maintain doesn't preempt
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        [Fact]
        public void Shred_skipped_in_front_even_with_Mangle_on_cooldown()
        {
            // In front with Mangle down, Shred still can't land (positional) → Claw is the floor.
            var g = CatGame();
            g.ComboPointCount = 0;
            g.BehindTargetFlag = false;
            g.SpellsOnCooldown.Add("Mangle (Cat)"); // no Mangle (so no debuff-maintain and no front builder)
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.TargetUnit.WithAura("Mangle", mine: true);
            Assert.Equal("Claw", Fire(g)?.Name);
        }

        // --- Savage Roar (the cat's Slice and Dice; highest finisher, kept up) ---

        [Fact]
        public void Savage_Roar_leads_the_finishers_when_missing()
        {
            var g = CatGame();
            g.MeUnit.Auras.Remove("Savage Roar"); // buff missing → refresh it first
            g.ComboPointCount = 1;                 // at the low keep-up threshold
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000); // Rake up so it doesn't preempt
            Assert.Equal("Savage Roar", Fire(g)?.Name);
        }

        [Fact]
        public void Savage_Roar_held_below_its_combo_point_floor()
        {
            var g = CatGame();
            g.MeUnit.Auras.Remove("Savage Roar");
            g.ComboPointCount = 0; // below the keep-up floor (1) → don't cast it yet; build instead
            g.BehindTargetFlag = false;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            g.TargetUnit.WithAura("Mangle", mine: true);
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        [Fact]
        public void Savage_Roar_respects_its_toggle()
        {
            var s = new DruidSettings();
            s.UseSavageRoar.Value = false;
            var g = CatGame();
            g.MeUnit.Auras.Remove("Savage Roar");
            g.ComboPointCount = 5; // would refresh Savage Roar, but the toggle is off → fall through to Rip
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            Assert.Equal("Rip", Fire(g, new SoloFeral(s))?.Name);
        }

        [Fact]
        public void Cat_finisher_order_is_Savage_Roar_then_Rip_then_Ferocious_Bite()
        {
            // Savage Roar missing, Rip missing, CP at the threshold → Savage Roar wins.
            var g = CatGame();
            g.MeUnit.Auras.Remove("Savage Roar");
            g.ComboPointCount = 5;
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            Assert.Equal("Savage Roar", Fire(g)?.Name);

            // With Savage Roar up, Rip is next (the bleed finisher on a durable target).
            g.MeUnit.WithAura("Savage Roar");
            Assert.Equal("Rip", Fire(g)?.Name);

            // With Rip up too, Ferocious Bite dumps the points.
            g.TargetUnit.WithAura("Rip", mine: true, timeLeftMs: 12000);
            Assert.Equal("Ferocious Bite", Fire(g)?.Name);
        }

        // --- Mangle (Cat) maintains the +30% bleed debuff ---

        [Fact]
        public void Mangle_Cat_refreshes_the_missing_bleed_debuff_before_building()
        {
            // Mangle "+30%" debuff missing → the debuff-maintain Mangle fires even from BEHIND (where Shred would
            // otherwise be the builder), so the bleeds stay amplified.
            var g = CatGame();
            g.ComboPointCount = 0;
            g.BehindTargetFlag = true; // behind, so this proves the debuff-maintain outranks Shred
            g.TargetUnit.WithAura("Rake", mine: true, timeLeftMs: 9000);
            // Mangle debuff is NOT on the target → maintain it.
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
            Assert.Contains("Mangle (Cat)", g.CastLog);
        }

        // --- Rake's boss-aware floor (its own, lower than Rip's) ---

        [Fact]
        public void Rake_applied_below_the_normal_floor_on_a_boss()
        {
            // 25% HP is below Rake's normal floor (35) but above its boss floor (20) → on a boss Rake still applies.
            var g = CatGame();
            g.ComboPointCount = 0;
            g.TargetUnit.Entry = 31146; // a BossList entry (Heroic Training dummy)
            g.TargetUnit.HealthPercent = 25;
            Assert.Equal("Rake", Fire(g)?.Name);
        }

        [Fact]
        public void Rake_skipped_at_the_same_HP_on_a_normal_mob()
        {
            // Same 25% HP, but a normal mob is below Rake's normal floor (35) → no fresh bleed; build instead.
            var g = CatGame();
            g.ComboPointCount = 0;
            g.BehindTargetFlag = false;
            g.TargetUnit.HealthPercent = 25; // normal mob (no boss entry)
            g.TargetUnit.WithAura("Mangle", mine: true); // debuff up so the maintain doesn't preempt the assertion
            Assert.Equal("Mangle (Cat)", Fire(g)?.Name);
        }

        // --- Survival Instincts (off-GCD emergency cooldown) ---

        [Fact]
        public void Survival_Instincts_fires_low_in_a_form()
        {
            var g = CatGame();
            g.MeUnit.HealthPercent = 30;        // below the default 35
            g.SpellsOnCooldown.Add("Barkskin"); // isolate it from Barkskin (which would win at 0.1)
            Assert.Equal("Survival Instincts", Fire(g)?.Name);
        }

        [Fact]
        public void Survival_Instincts_skipped_when_not_learned()
        {
            var g = CatGame();
            g.MeUnit.HealthPercent = 30;
            g.SpellsOnCooldown.Add("Barkskin");
            g.UnknownSpells.Add("Survival Instincts"); // untalented → auto-skip; the heal path takes over
            g.MeUnit.WithAura("Predator's Swiftness"); // instant proc → form-preserving Regrowth
            Assert.Equal("Regrowth", Fire(g)?.Name);
        }

        // --- bear-switch: wider form radius + enter/return hysteresis ---

        // A pack clustered around the TARGET (target-anchored, wider radius) — n enemies all at the target's
        // position so EnemiesNearTarget(18) == n. The current target counts too, so pass the EXTRA adds.
        private static void PackAroundTarget(FakeGameClient g, int extraAdds)
        {
            g.TargetUnit.IsTargetingMe = true;
            for (int i = 0; i < extraAdds; i++)
                g.EnemyList.Add(new FakeUnit { Guid = (ulong)(100 + i), Reaction = Reaction.Hostile, Distance = 30, IsAttackable = true });
        }

        [Fact]
        public void Form_decision_uses_the_wider_target_anchored_radius()
        {
            // Two enemies clustered on the target but 30y from the PLAYER (outside the tight 8y melee radius). The
            // OLD player-relative 8y count would see zero and stay in cat; the wider target-anchored radius sees the
            // pack → bear.
            var g = CatGame();
            PackAroundTarget(g, extraAdds: 1); // target + 1 add = 2 around the target (default BearCount 2)
            Assert.Equal("Bear Form", Fire(g)?.Name);
            Assert.Contains("Dire Bear Form", g.CastLog);
        }

        [Fact]
        public void Bear_entered_at_the_bear_count()
        {
            var g = CatGame(); // in cat
            PackAroundTarget(g, extraAdds: 1); // 2 around the target == BearCount(2) → enter bear
            Assert.Equal("Bear Form", Fire(g)?.Name);
        }

        [Fact]
        public void Bear_retained_at_the_return_threshold_hysteresis()
        {
            // In bear with exactly BearCount-1 (1) enemy around the target: that's below the ENTRY count (2) but NOT
            // below the RETURN count (max(1, 2-1)=1), so we DON'T flip back to cat — harder to leave bear than to
            // enter it. The bear ladder runs instead.
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form");
            g.MeUnit.WithAura("Dire Bear Form");
            g.MeUnit.Rage = 100;
            PackAroundTarget(g, extraAdds: 0); // just the target == 1 around the target
            RotationStep step = Fire(g);
            Assert.NotEqual("Cat Form", step?.Name); // did NOT return to cat
        }

        [Fact]
        public void Bear_returns_to_cat_below_the_return_threshold()
        {
            // With a higher BearCount the return floor is well above 1, so a single straggler is below it and the
            // druid DOES return to cat. BearCount 4 → return floor max(1,3)=3; 1 enemy around the target < 3 → cat.
            var s = new DruidSettings();
            s.BearCount.Value = 4;
            var g = CatGame();
            g.MeUnit.Auras.Remove("Cat Form");
            g.MeUnit.WithAura("Dire Bear Form");
            g.MeUnit.Rage = 100;
            PackAroundTarget(g, extraAdds: 0); // 1 around the target < return floor (3) → back to cat
            Assert.Equal("Cat Form", Fire(g, new SoloFeral(s))?.Name);
        }
    }
}
