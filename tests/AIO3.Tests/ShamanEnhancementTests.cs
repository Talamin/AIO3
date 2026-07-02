using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Shaman;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class ShamanEnhancementTests
    {
        // An Enhancement shaman in melee, in combat, full mana, with all four totems already up + close, imbues +
        // shield up, so the upkeep/totem bands stay quiet and each test isolates the melee rule it cares about.
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Shaman };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 5,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.InCombatFlag = true;
            g.ProductFightingFlag = true;
            g.AutoAttacking = true; // already swinging → the off-GCD AutoAttack step stays inert so tests isolate the rotation
            g.MeUnit.WithAura("Water Shield");
            g.MeUnit.WithAura("Lightning Shield");
            g.WeaponEnchantState = new WeaponEnchant(true, 600000, true, 600000); // both hands imbued
            // all four schools up + close → totem band quiet
            g.TotemList.Add(new FakeUnit { Name = "Magma Totem", Distance = 5, Reaction = Reaction.Friendly });
            g.TotemList.Add(new FakeUnit { Name = "Strength of Earth Totem", Distance = 5, Reaction = Reaction.Friendly });
            g.TotemList.Add(new FakeUnit { Name = "Healing Stream Totem", Distance = 5, Reaction = Reaction.Friendly });
            g.TotemList.Add(new FakeUnit { Name = "Windfury Totem", Distance = 5, Reaction = Reaction.Friendly });
            return g;
        }

        // Racials disabled by default: they fire off-GCD in combat (Blood Fury at the ~2.4 band) and would mask the
        // strike/shock priorities these tests isolate. RacialsTests covers the racial bundle itself.
        private static ShamanSettings Defaults()
        {
            var s = new ShamanSettings();
            s.UseRacials.Value = false;
            return s;
        }

        private static RotationStep Fire(FakeGameClient g, ShamanSettings s = null) =>
            new RotationEngine(new SoloEnhancement(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Stormstrike_is_the_signature_strike()
        {
            // Nothing special up → Stormstrike (prio 4.0) is the highest melee strike.
            Assert.Equal("Stormstrike", Fire(Game())?.Name);
        }

        [Fact]
        public void Enhancement_auto_attacks_off_the_gcd()
        {
            // Every melee spec must ensure the melee swing is on — Enhancement was missing the step, so a low-level
            // shaman with no strikes just stood there. When not yet swinging, the off-GCD AutoAttack step fires.
            FakeGameClient g = Game();
            g.AutoAttacking = false; // not yet swinging
            Assert.Equal("Auto Attack", Fire(g)?.Name);
        }

        [Fact]
        public void Low_level_opener_pulls_with_Lightning_Bolt_out_of_combat()
        {
            // Pre-40 Enhancement, OUT OF COMBAT and out of melee (target > 8yd) → open with a single Lightning Bolt pull.
            FakeGameClient g = Game();
            g.InCombatFlag = false;         // opener: not engaged yet
            g.ProductFightingFlag = false;
            g.TargetUnit.Distance = 20;     // out of melee
            g.UnknownSpells.Add("Stormstrike");
            g.UnknownSpells.Add("Lava Lash");
            g.UnknownSpells.Add("Flame Shock");
            g.UnknownSpells.Add("Earth Shock"); // strip the shocks so the Lightning Bolt opener is isolated
            Assert.Equal("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Low_level_stops_Lightning_Bolt_once_in_combat_and_goes_melee()
        {
            // The bug Daniel hit at lvl 15: in combat at range it kept spamming Lightning Bolt instead of closing.
            // The opener is out-of-combat ONLY → once engaged, NO more Lightning Bolt (it closes + finishes in melee).
            FakeGameClient g = Game();      // Game() is already in combat
            g.TargetUnit.Distance = 20;     // still at range, but already engaged
            g.UnknownSpells.Add("Stormstrike");
            g.UnknownSpells.Add("Lava Lash");
            g.UnknownSpells.Add("Flame Shock");
            g.UnknownSpells.Add("Earth Shock");
            Assert.NotEqual("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Low_level_no_Lightning_Bolt_opener_when_already_in_melee()
        {
            // Out of combat but the target is already in melee (<= 8yd) → no ranged opener, just walk in and swing.
            FakeGameClient g = Game();
            g.InCombatFlag = false;
            g.ProductFightingFlag = false;
            g.TargetUnit.Distance = 5;      // already in melee
            g.UnknownSpells.Add("Stormstrike");
            g.UnknownSpells.Add("Lava Lash");
            g.UnknownSpells.Add("Flame Shock");
            g.UnknownSpells.Add("Earth Shock");
            Assert.NotEqual("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Low_level_opener_holds_when_it_cant_afford_the_lightning_bolt()
        {
            // Daniel: if there isn't enough mana, DON'T wait on the opener — no Lightning Bolt. (The module range also
            // drops to melee so the bot walks straight in; see ShamanModuleTests.)
            FakeGameClient g = Game();
            g.InCombatFlag = false;
            g.ProductFightingFlag = false;
            g.TargetUnit.Distance = 20;
            g.UnknownSpells.Add("Stormstrike");
            g.UnknownSpells.Add("Lava Lash");
            g.UnknownSpells.Add("Flame Shock");
            g.UnknownSpells.Add("Earth Shock");
            g.SpellManaCosts["Lightning Bolt"] = 100; // costs 100...
            g.MeUnit.Mana = 50;                        // ...but we only have 50 → can't afford the opener
            Assert.NotEqual("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Low_level_Lightning_Bolt_is_inert_once_Stormstrike_is_learned()
        {
            // Once the melee strike exists, the hard-cast filler stops (the Maelstrom-proc instant LB owns high level).
            FakeGameClient g = Game(); // Stormstrike known → the strike wins, not the filler
            Assert.Equal("Stormstrike", Fire(g)?.Name);
        }

        [Fact]
        public void No_off_hand_imbue_loop_on_a_shield_user()
        {
            // Main imbued, off-hand slot filled by a SHIELD (no weapon, 0ms enchant) → the off-hand imbue must NOT
            // fire (a weapon imbue can't enchant a shield → the Rockbiter-spam loop). The strike fires instead.
            FakeGameClient g = Game();
            g.WeaponEnchantState = new WeaponEnchant(true, 600000, true, 0); // main imbued; off-hand present, unenchanted
            g.OffHandHasWeaponFlag = false;                                  // …but it's a shield
            Assert.NotEqual("Weapon imbue", Fire(g)?.Name);
        }

        [Fact]
        public void Main_hand_imbue_fires_when_the_main_hand_is_unenchanted()
        {
            // Regression: the main-hand imbue still fires when the main hand has a weapon and no enchant.
            FakeGameClient g = Game();
            g.WeaponEnchantState = new WeaponEnchant(true, 0, false, 0); // main equipped + unenchanted; no off-hand
            Assert.Equal("Weapon imbue", Fire(g)?.Name);
        }

        [Fact]
        public void Maelstrom_proc_fires_an_instant_lightning_bolt_single_target()
        {
            FakeGameClient g = Game();
            g.MeUnit.WithAura("Maelstrom Weapon", stacks: 5); // full stacks → instant LB
            Assert.Equal("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Maelstrom_proc_chain_lightnings_a_pack()
        {
            FakeGameClient g = Game();
            g.MeUnit.WithAura("Maelstrom Weapon", stacks: 5);
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            Assert.Equal("Chain Lightning", Fire(g)?.Name); // 2 near the target → Chain Lightning wins
        }

        [Fact]
        public void Maelstrom_below_five_stacks_does_not_fire_the_instant()
        {
            FakeGameClient g = Game();
            g.MeUnit.WithAura("Maelstrom Weapon", stacks: 4); // not full → no instant
            Assert.NotEqual("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Fire_Nova_on_a_pack_with_a_fire_totem_up()
        {
            FakeGameClient g = Game();
            // a pack near the target (>= FireNovaCount default 3), fire totem already up (Magma in the helper)
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            Assert.Equal("Fire Nova", Fire(g)?.Name);
        }

        [Fact]
        public void Fire_Nova_holds_without_a_fire_totem()
        {
            FakeGameClient g = Game();
            g.TotemList.RemoveAll(t => t.Name == "Magma Totem"); // no fire totem...
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            // ...so Fire Nova can't fire; the now-missing fire school re-drops instead (a totem step, not Fire Nova).
            Assert.NotEqual("Fire Nova", Fire(g)?.Name);
        }

        [Fact]
        public void Shamanistic_rage_when_low_on_mana()
        {
            FakeGameClient g = Game();
            g.MeUnit.PowerPercent = 20; // below the 25% default
            Assert.Equal("Shamanistic Rage", Fire(g)?.Name);
        }

        [Fact]
        public void Flame_Shock_is_maintained_on_a_healthy_target()
        {
            FakeGameClient g = Game();
            // No Flame Shock on target, healthy → it gets applied (above Earth Shock / Lava Lash, below Stormstrike).
            var s = Defaults();
            g.SpellsOnCooldown.Add("Stormstrike"); // isolate the shock from the higher-priority strike
            Assert.Equal("Flame Shock", Fire(g, s)?.Name);
        }

        [Fact]
        public void Flame_Shock_is_not_refreshed_on_a_dying_mob()
        {
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Stormstrike");
            g.TargetUnit.HealthPercent = 10; // below the 15% DyingFloor → don't apply a fresh DoT
            Assert.NotEqual("Flame Shock", Fire(g)?.Name);
        }

        [Fact]
        public void Self_heal_when_low_and_target_not_dying()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 40; // below the 50% default
            Assert.Equal("Healing Wave", Fire(g)?.Name);
        }

        [Fact]
        public void Self_heal_is_skipped_when_the_target_is_about_to_die()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 40;
            g.TargetUnit.HealthPercent = 5; // below the skip-enemy-HP floor → finish the mob, don't heal
            Assert.NotEqual("Healing Wave", Fire(g)?.Name);
        }

        [Fact]
        public void Mana_reserve_holds_offensive_strikes()
        {
            FakeGameClient g = Game();
            var s = Defaults();
            s.ManaSavedForHeals.Value = 60; // reserve 60% for heals
            g.MeUnit.PowerPercent = 50;     // below the reserve → offense holds
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Stormstrike", fired?.Name);
            Assert.NotEqual("Earth Shock", fired?.Name);
        }

        [Fact]
        public void Reapplies_a_missing_main_hand_imbue()
        {
            FakeGameClient g = Game();
            // main hand has a weapon but NO temp-enchant (RemainingMs 0); off hand imbued
            g.WeaponEnchantState = new WeaponEnchant(true, 0, true, 600000);
            Assert.Equal("Weapon imbue", Fire(g)?.Name);
            Assert.Contains("Windfury Weapon", g.CastLog); // Enhancement main = Windfury
        }

        [Fact]
        public void Reapplies_a_missing_off_hand_imbue()
        {
            FakeGameClient g = Game();
            // main hand imbued; off hand has a WEAPON but NO temp-enchant
            g.WeaponEnchantState = new WeaponEnchant(true, 600000, true, 0);
            g.OffHandHasWeaponFlag = true; // a weapon (not a shield) → the off-hand imbue is due
            Assert.Equal("Weapon imbue", Fire(g)?.Name);
            Assert.Contains("Flametongue Weapon", g.CastLog); // Enhancement off = Flametongue
        }

        [Fact]
        public void Wind_Shear_interrupts_a_casting_enemy()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 100;
            Assert.Equal("Wind Shear", Fire(g)?.Name);
        }

        [Fact]
        public void Feral_Spirit_on_an_elite_under_the_default_setting()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true; // "+2 and Elite" default → fires on an elite
            Assert.Equal("Feral Spirit", Fire(g)?.Name);
        }

        [Fact]
        public void No_target_does_not_throw()
        {
            var g = new FakeGameClient { Class = WowClass.Shaman };
            g.MeUnit.PowerPercent = 100;
            Assert.Null(Record.Exception(() => Fire(g)));
        }
    }
}
