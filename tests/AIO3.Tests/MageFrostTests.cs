using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Mage;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class MageFrostTests
    {
        // A frost mage at range on a full-health dummy, full mana, with the upkeep buffs already up so the
        // maintenance slots stay quiet and each test isolates the rule it cares about. A pet (Water Elemental)
        // is present by default so the summon step is quiet; pass withPet:false to test the summon.
        private static FakeGameClient MageGame(bool withPet = true)
        {
            var g = new FakeGameClient { Class = WowClass.Mage };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 30,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.WithAura("Ice Armor");          // armor up
            g.MeUnit.WithAura("Arcane Intellect");   // AI up
            g.MeUnit.WithAura("Ice Barrier");        // shield up → upkeep quiet
            // Bags stocked so the out-of-combat auto-conjure stays idle (it would otherwise preempt at prio 0.4).
            g.ItemCounts["Conjured Mana Strudel"] = 20;
            g.ItemCounts["Conjured Mana Biscuit"] = 20;
            g.ItemCounts["Mana Sapphire"] = 1;
            if (withPet)
                g.PetUnit = new FakeUnit { Guid = 99, Name = "Water Elemental", IsAlive = true, TargetGuid = 1, Distance = 8 };
            return g;
        }

        // Add an enemy that has closed to melee and is on the player (the kite trigger).
        private static FakeUnit AddMeleeAttacker(FakeGameClient g, ulong guid = 2, string type = "Beast")
        {
            var add = new FakeUnit
            {
                Guid = guid, Name = "Add", Reaction = Reaction.Hostile, Distance = 4,
                HealthPercent = 100, IsAttackable = true, IsTargetingMe = true, CreatureType = type
            };
            g.EnemyList.Add(add);
            return add;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new MageSettings());

        private static RotationStep Fire(FakeGameClient g, MageSettings s) =>
            new RotationEngine(new SoloFrost(s).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Frostbolt_is_the_filler()
        {
            Assert.Equal("Frostbolt", Fire(MageGame())?.Name);
        }

        [Fact]
        public void Frostbolt_holds_while_moving()
        {
            FakeGameClient g = MageGame();
            g.Moving = true; // a cast-time spell can't be cast on the move
            Assert.NotEqual("Frostbolt", Fire(g)?.Name);
        }

        [Fact]
        public void Summons_the_water_elemental_when_none()
        {
            FakeGameClient g = MageGame(withPet: false);
            Assert.Equal("Summon Water Elemental", Fire(g)?.Name);
        }

        [Fact]
        public void Ice_Lance_on_a_frozen_target()
        {
            FakeGameClient g = MageGame();
            g.TargetUnit.WithAura("Frost Nova"); // rooted → shatter
            // Deep Freeze (prio 4.0, also a shatter) now fires on Frozen regardless of UseCooldowns (F8), so it would
            // take this slot first — make it unavailable to isolate Ice Lance (the lower-priority shatter filler).
            g.SpellsOnCooldown.Add("Deep Freeze");
            Assert.Equal("Ice Lance", Fire(g)?.Name);
        }

        [Fact]
        public void Deep_Freeze_fires_on_a_frozen_target_even_with_cooldowns_off()
        {
            // F8: Deep Freeze is a core shatter nuke, not a big-cooldown button — it must fire on the frozen
            // condition alone, NOT gated on UseCooldowns. With cooldowns OFF and a frozen target it still goes.
            FakeGameClient g = MageGame();
            g.TargetUnit.WithAura("Frost Nova"); // rooted/frozen → shatter window
            var s = new MageSettings();
            s.UseCooldowns.Value = false;        // big cooldowns off...
            Assert.Equal("Deep Freeze", Fire(g, s)?.Name); // ...Deep Freeze still fires (it's not a "cooldown")
        }

        [Fact]
        public void Cold_Snap_refreshes_ice_barrier_when_down_and_low_in_a_big_fight()
        {
            // F6: spend Cold Snap to bring Ice Barrier back when it has dropped in a real fight and we're hurt.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;        // a big fight worth the cooldown
            g.MeUnit.Auras.Remove("Ice Barrier"); // barrier dropped
            g.MeUnit.HealthPercent = 45;          // below the ~50% floor
            var s = new MageSettings();
            s.UseRacials.Value = false;           // racials sit just above; isolate Cold Snap
            // Ice Barrier (prio 0.8) would re-cast first, so it never reaches Cold Snap — make it unknown to isolate.
            g.UnknownSpells.Add("Ice Barrier");
            // Icy Veins / Mirror Image (prio 2.6/2.65) also fire on the same big fight and sit above Cold Snap (2.7);
            // take them out so the test isolates Cold Snap itself.
            g.UnknownSpells.Add("Icy Veins");
            g.UnknownSpells.Add("Mirror Image");
            Assert.Equal("Cold Snap", Fire(g, s)?.Name);
        }

        [Fact]
        public void Cold_Snap_is_not_burned_trivially()
        {
            // It must NOT fire when the barrier is up, when we're healthy, or in a trivial (non-big) fight.
            // Barrier up + healthy single target → Cold Snap stays quiet.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            // Ice Barrier still up, HP full → no reason to reset cooldowns
            Assert.NotEqual("Cold Snap", Fire(g)?.Name);

            // Barrier down + hurt but only a lone trivial mob (not a big fight) → still no Cold Snap.
            FakeGameClient g2 = MageGame();
            g2.InCombatFlag = true;
            g2.MeUnit.Auras.Remove("Ice Barrier");
            g2.MeUnit.HealthPercent = 45;
            g2.UnknownSpells.Add("Ice Barrier"); // so the barrier re-cast doesn't mask the comparison
            Assert.NotEqual("Cold Snap", Fire(g2)?.Name); // single non-elite mob → IsBigFight false
        }

        [Fact]
        public void Frostfire_Bolt_on_a_Brain_Freeze_proc()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Fireball!"); // Brain Freeze → instant Frostfire Bolt
            Assert.Equal("Frostfire Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Fire_Blast_executes_a_low_target()
        {
            FakeGameClient g = MageGame();
            g.TargetUnit.HealthPercent = 8;
            Assert.Equal("Fire Blast", Fire(g)?.Name);
        }

        [Fact]
        public void Keeps_the_armor_up()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.Auras.Remove("Ice Armor");
            Assert.Equal("Armor", Fire(g)?.Name);
            Assert.Contains("Ice Armor", g.CastLog); // auto picks Ice Armor for frost
        }

        [Fact]
        public void Keeps_ice_barrier_up()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.Auras.Remove("Ice Barrier");
            Assert.Equal("Ice Barrier", Fire(g)?.Name);
        }

        [Fact]
        public void Ice_barrier_does_not_recast_on_a_dying_lone_target()
        {
            // Dying-mob fix: don't refresh the shield as the last mob of a fight dies (HP below ShieldMinTargetHealth
            // and no other enemy). The shield step goes quiet; the rotation falls through to something else.
            FakeGameClient g = MageGame();
            g.MeUnit.Auras.Remove("Ice Barrier");
            g.TargetUnit.HealthPercent = MageCommon.ShieldMinTargetHealth - 1; // 19% → dying lone target
            Assert.NotEqual("Ice Barrier", Fire(g)?.Name);
            Assert.DoesNotContain("Ice Barrier", g.CastLog);
        }

        [Fact]
        public void Ice_barrier_still_recasts_with_more_than_one_enemy()
        {
            // Relaxation: a pack keeps the shield earning even if the current target is dying, so it still casts.
            FakeGameClient g = MageGame();
            g.MeUnit.Auras.Remove("Ice Barrier");
            g.TargetUnit.HealthPercent = 5; // current target dying...
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 30, HealthPercent = 100 });
            Assert.Equal("Ice Barrier", Fire(g)?.Name);
        }

        [Fact]
        public void Counterspell_interrupts_a_casting_enemy()
        {
            FakeGameClient g = MageGame();
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 133; // some cast; Smart defaults to interrupt until it learns otherwise
            Assert.Equal("Counterspell", Fire(g)?.Name);
        }

        [Fact]
        public void Frost_Nova_when_a_mob_reaches_melee()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            AddMeleeAttacker(g);
            Assert.Equal("Frost Nova", Fire(g)?.Name);
        }

        [Fact]
        public void Blinks_away_after_the_root()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.WithAura("Frost Nova", mine: true); // we rooted it
            g.SpellsOnCooldown.Add("Frost Nova");   // ...and Nova is now down → escape with Blink
            Assert.Equal("Blink away", Fire(g)?.Name);
            Assert.Equal(1, g.BlinkAwayCount);
        }

        [Fact]
        public void Falls_back_to_step_back_when_the_blink_spot_is_unsafe()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.WithAura("Frost Nova", mine: true); // rooted → stepping back is safe/useful
            g.SpellsOnCooldown.Add("Frost Nova");
            g.BlinkAwayResult = false;              // adapter judged the landing unsafe (cliff / wall / adds)

            Assert.Equal("Kite back", Fire(g)?.Name); // ...so we step back instead
            Assert.Equal(1, g.BlinkAwayCount);        // but it did try the blink first
            Assert.Contains(10f, g.StepBackLog);
        }

        [Fact]
        public void Kites_back_when_blink_is_unavailable()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.WithAura("Frost Nova", mine: true); // rooted
            g.SpellsOnCooldown.Add("Frost Nova");
            g.SpellsOnCooldown.Add("Blink");        // no blink → fall back to the step-back
            Assert.Equal("Kite back", Fire(g)?.Name);
            Assert.Contains(10f, g.StepBackLog);    // default kite distance
        }

        [Fact]
        public void Blink_is_preferred_over_step_back_for_a_mob_rooted_at_nova_range()
        {
            // Frost Nova roots a mob as it crosses ~10yd, so the rooted mob sits at ~10yd — OUTSIDE true melee (8).
            // Blink is the primary escape: it now triggers at the nova radius too, so it fires there (not the
            // step-back). KiteBack only takes over when Blink is unavailable (covered by Kites_back_when_blink...).
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.Distance = 9;                        // rooted just inside the nova radius (10), beyond melee (8)
            add.WithAura("Frost Nova", mine: true);  // we rooted it
            g.SpellsOnCooldown.Add("Frost Nova");    // nova just cast → on CD, so it doesn't preempt Blink
            Assert.Equal("Blink away", Fire(g)?.Name);
            Assert.Empty(g.StepBackLog);             // and we did NOT step back
        }

        [Fact]
        public void Does_not_step_back_when_the_mob_is_not_rooted()
        {
            // The "runs backwards endlessly" bug: an unrooted mob in melee must NOT trigger a step-back (it would
            // just follow forever). With Frost Nova on cooldown (can't root) and no Blink, we hold ground.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            AddMeleeAttacker(g);                  // in melee, NOT rooted
            g.SpellsOnCooldown.Add("Frost Nova"); // can't root right now
            g.SpellsOnCooldown.Add("Blink");
            RotationStep fired = Fire(g);
            Assert.NotEqual("Kite back", fired?.Name);
            Assert.Empty(g.StepBackLog);
        }

        [Fact]
        public void Roots_at_the_nova_radius_before_true_melee()
        {
            // Frost Nova roots within ~10yd, so we don't wait for the mob to reach melee (8) — root it as it
            // crosses the radius, buying more kite distance.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.Distance = 10; // inside Frost Nova's radius, but not yet in true melee
            Assert.Equal("Frost Nova", Fire(g)?.Name);
        }

        [Fact]
        public void Holds_Frost_Nova_until_the_mob_enters_its_radius()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.Distance = 14; // still outside the radius → don't waste the nova
            Assert.NotEqual("Frost Nova", Fire(g)?.Name);
            Assert.Empty(g.StepBackLog);
        }

        [Fact]
        public void Skips_the_kite_when_the_attacker_is_almost_dead()
        {
            // If the mob on us dies in a cast or two, finishing it beats spending Frost Nova + a step back.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.HealthPercent = 20; // below the default 30% kite floor
            RotationStep fired = Fire(g);
            Assert.NotEqual("Frost Nova", fired?.Name);
            Assert.NotEqual("Kite back", fired?.Name);
            Assert.Empty(g.StepBackLog);
        }

        [Fact]
        public void Skips_the_kite_for_a_grey_low_level_mob()
        {
            // A trivial mob 5+ levels below us dies in a hit or two — not worth a root + step back. Just nuke it.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.MeUnit.Level = 30;
            FakeUnit add = AddMeleeAttacker(g);
            add.Level = 25;                          // exactly 5 below → grey at the default threshold
            add.WithAura("Frost Nova", mine: true);  // even already rooted, we don't bother stepping back
            g.SpellsOnCooldown.Add("Frost Nova");

            RotationStep fired = Fire(g);
            Assert.NotEqual("Frost Nova", fired?.Name);
            Assert.NotEqual("Kite back", fired?.Name);
            Assert.Empty(g.StepBackLog);
        }

        [Fact]
        public void Still_kites_a_mob_only_a_few_levels_below()
        {
            // 4 levels below is still yellow/green (not grey) at the default 5 → kite it normally.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.MeUnit.Level = 30;
            FakeUnit add = AddMeleeAttacker(g);
            add.Level = 26;                          // only 4 below → not grey
            Assert.Equal("Frost Nova", Fire(g)?.Name);
        }

        [Fact]
        public void Grey_skip_is_off_when_the_threshold_is_zero()
        {
            // KiteSkipGreyLevels = 0 disables the grey rule entirely (kite regardless of level difference).
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.MeUnit.Level = 80;
            FakeUnit add = AddMeleeAttacker(g);
            add.Level = 1;                           // hopelessly grey...
            var s = new MageSettings();
            s.KiteSkipGreyLevels.Value = 0;          // ...but the rule is off
            Assert.Equal("Frost Nova", Fire(g, s)?.Name);
        }

        [Fact]
        public void Does_not_kite_while_swimming()
        {
            // In water the kite is futile (half-speed swim + the product re-approaches the rooted mob), so the
            // kite is suppressed and we just nuke. Covers Frost Nova and the step-back, even with the mob rooted.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.SwimmingFlag = true;
            FakeUnit add = AddMeleeAttacker(g);    // a healthy mob in melee on us
            add.WithAura("Frost Nova", mine: true); // ...and already rooted
            g.SpellsOnCooldown.Add("Frost Nova");

            RotationStep fired = Fire(g);
            Assert.NotEqual("Frost Nova", fired?.Name);
            Assert.NotEqual("Kite back", fired?.Name);
            Assert.Empty(g.StepBackLog);
        }

        [Fact]
        public void Does_not_step_back_from_a_caster()
        {
            // A caster keeps casting from range, so backing off is pointless — burst it instead (no step-back).
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.IsCaster = true;                    // a mana-using caster
            add.WithAura("Frost Nova", mine: true); // rooted/frozen
            g.SpellsOnCooldown.Add("Frost Nova");
            g.SpellsOnCooldown.Add("Blink");

            RotationStep fired = Fire(g);
            Assert.NotEqual("Kite back", fired?.Name);
            Assert.Empty(g.StepBackLog);
        }

        [Fact]
        public void Frost_Nova_still_freezes_a_caster_for_the_shatter()
        {
            // We don't kite casters, but Frost Nova still freezes one in melee so the shatter (Ice Lance / Deep
            // Freeze) hits hard while we burst it down.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            FakeUnit add = AddMeleeAttacker(g);
            add.IsCaster = true; // a caster in melee
            Assert.Equal("Frost Nova", Fire(g)?.Name);
        }

        [Fact]
        public void Conjures_food_when_low_out_of_combat()
        {
            FakeGameClient g = MageGame();
            g.ItemCounts["Conjured Mana Strudel"] = 0; // bags empty of food (out of combat by default)
            Assert.Equal("Conjure food", Fire(g)?.Name);
            Assert.Contains("Conjure Refreshment", g.CastLog); // prefers Refreshment when known
        }

        [Fact]
        public void Conjures_a_mana_gem_when_none()
        {
            FakeGameClient g = MageGame();
            g.ItemCounts["Mana Sapphire"] = 0; // no gem
            var s = new MageSettings();
            s.UseConjure.Value = true;
            // food/water still stocked from the helper, so the gem is what's missing
            Assert.Equal("Conjure mana gem", Fire(g, s)?.Name);
        }

        [Fact]
        public void Auto_conjure_can_be_turned_off()
        {
            FakeGameClient g = MageGame();
            g.ItemCounts["Conjured Mana Strudel"] = 0;
            g.ItemCounts["Mana Sapphire"] = 0;
            var s = new MageSettings();
            s.UseConjure.Value = false;
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Conjure food", fired?.Name);
            Assert.NotEqual("Conjure mana gem", fired?.Name);
        }

        [Fact]
        public void Does_not_step_back_without_Frost_Nova()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            AddMeleeAttacker(g);
            g.UnknownSpells.Add("Frost Nova"); // low-level mage hasn't learned it yet
            g.UnknownSpells.Add("Blink");      // isolate the step-back from the blink escape
            RotationStep fired = Fire(g);
            Assert.NotEqual("Kite back", fired?.Name);
            Assert.Empty(g.StepBackLog); // didn't even ask to step back
        }

        [Fact]
        public void Kiting_can_be_turned_off()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            AddMeleeAttacker(g);
            var s = new MageSettings();
            s.UseKiting.Value = false;
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Frost Nova", fired?.Name);
            Assert.NotEqual("Kite back", fired?.Name);
        }

        [Fact]
        public void Polymorph_sheeps_an_extra_attacker()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsTargetingMe = true; // the mob we're nuking is on us...
            AddMeleeAttacker(g, guid: 2);      // ...and a second add is too → 2 attackers
            var s = new MageSettings();
            s.UsePolymorph.Value = true;

            // Polymorph now sits above the kite, so it fires first; the adapter handles type + facing.
            Assert.Equal("Polymorph", Fire(g, s)?.Name);
            Assert.Contains(2ul, g.PolymorphLog); // sheeps the add, not the current target
        }

        [Fact]
        public void Polymorph_needs_two_attackers()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            AddMeleeAttacker(g, guid: 2); // only one mob on us (the main target isn't targeting us)
            var s = new MageSettings();
            s.UsePolymorph.Value = true;
            s.UseKiting.Value = false;
            s.UseBlink.Value = false;

            Assert.NotEqual("Polymorph", Fire(g, s)?.Name);
            Assert.Empty(g.PolymorphLog);
        }

        [Fact]
        public void Finishes_our_sheeped_add_after_the_target_dies()
        {
            // Main target dead, our Polymorphed add still up + no live target → grab it so we finish it instead of
            // letting the product pull a fresh mob while the sheep expires and the add wakes on us.
            FakeGameClient g = MageGame();
            g.TargetUnit.IsAlive = false; // we just killed the main target
            var sheeped = new FakeUnit { Guid = 7, Reaction = Reaction.Hostile, IsAttackable = true, IsAlive = true, Distance = 8, HealthPercent = 100 };
            sheeped.WithAura("Polymorph", mine: true);
            g.EnemyList.Add(sheeped);
            var s = new MageSettings();
            s.UsePolymorph.Value = true;

            Assert.Equal("Finish sheeped add", Fire(g, s)?.Name);
            Assert.Equal(7ul, g.LastSetTargetGuid); // we re-targeted the sheeped add
        }

        [Fact]
        public void Does_not_grab_the_sheeped_add_while_a_live_target_remains()
        {
            // A live target is still up → keep fighting it; only finish the sheeped add in the post-kill gap.
            FakeGameClient g = MageGame();
            var sheeped = new FakeUnit { Guid = 7, Reaction = Reaction.Hostile, IsAttackable = true, IsAlive = true, Distance = 8, HealthPercent = 100 };
            sheeped.WithAura("Polymorph", mine: true);
            g.EnemyList.Add(sheeped);
            var s = new MageSettings();
            s.UsePolymorph.Value = true;

            Assert.NotEqual("Finish sheeped add", Fire(g, s)?.Name);
        }

        [Fact]
        public void Holds_AoE_while_our_sheep_is_up()
        {
            FakeGameClient g = MageGame();
            g.TargetUnit.Distance = 8; // a pack within AoE radius (would normally Blizzard/Cone)
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            var sheeped = new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 7, HealthPercent = 100 };
            sheeped.WithAura("Polymorph", mine: true); // we have a sheep up
            g.EnemyList.Add(sheeped);
            var s = new MageSettings();
            s.UseKiting.Value = false;    // isolate AoE-hold from the kite
            s.UseBlink.Value = false;
            s.UseCooldowns.Value = false; // ...and from the big-fight major cooldown (Icy Veins on a 3-pack)

            // 3 enemies within AoE radius, but a sheep is up → AoE must hold; fall to single-target.
            Assert.Equal("Frostbolt", Fire(g, s)?.Name);
        }

        [Fact]
        public void Wands_when_low_on_mana()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.PowerPercent = 15; // below the wand threshold (20)
            var s = new MageSettings();
            s.EvocationManaPercent.Value = 0; // isolate the wand from Evocation (both trigger at low mana)
            Assert.Equal("Shoot", Fire(g, s)?.Name);
        }

        [Fact]
        public void Evocation_when_low_on_mana_and_safe()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.PowerPercent = 15; // below evocation threshold; nothing meleeing us
            var s = new MageSettings();
            s.UseWand.Value = false; // so we see Evocation rather than the wand
            Assert.Equal("Evocation", Fire(g, s)?.Name);
        }

        [Fact]
        public void Uses_an_emergency_item_below_threshold()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.HealthPercent = 20;
            g.ReadyItems.Add("Runic Healing Potion");
            Assert.Equal("Emergency heal", Fire(g)?.Name);
        }

        [Fact]
        public void No_target_does_not_throw()
        {
            var g = new FakeGameClient { Class = WowClass.Mage };
            g.MeUnit.PowerPercent = 100;
            Assert.Null(Record.Exception(() => Fire(g)));
        }
    }
}
