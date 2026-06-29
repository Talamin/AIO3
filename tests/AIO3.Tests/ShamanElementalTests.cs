using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Shaman;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class ShamanElementalTests
    {
        // An Elemental shaman at range, in combat, full mana, standing still, with all four totems already up +
        // close, the Flametongue imbue + Lightning Shield up, so the upkeep/totem bands stay quiet and each test
        // isolates the caster rule it cares about.
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Shaman };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 27,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.InCombatFlag = true;
            g.ProductFightingFlag = true;
            g.MeUnit.WithAura("Lightning Shield");
            g.WeaponEnchantState = new WeaponEnchant(true, 600000, false, 0); // main imbued; no off-hand needed for Ele
            g.TotemList.Add(new FakeUnit { Name = "Totem of Wrath", Distance = 5, Reaction = Reaction.Friendly });
            g.TotemList.Add(new FakeUnit { Name = "Stoneskin Totem", Distance = 5, Reaction = Reaction.Friendly });
            g.TotemList.Add(new FakeUnit { Name = "Mana Spring Totem", Distance = 5, Reaction = Reaction.Friendly });
            g.TotemList.Add(new FakeUnit { Name = "Wrath of Air Totem", Distance = 5, Reaction = Reaction.Friendly });
            return g;
        }

        // Racials disabled by default (see ShamanEnhancementTests for the rationale).
        private static ShamanSettings Defaults()
        {
            var s = new ShamanSettings();
            s.UseRacials.Value = false;
            return s;
        }

        private static RotationStep Fire(FakeGameClient g, ShamanSettings s = null) =>
            new RotationEngine(new SoloElemental(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Lightning_Bolt_is_the_single_target_filler()
        {
            // Flame Shock already up (so the maintain doesn't fire), standing still, single target → Lightning Bolt.
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000);
            g.SpellsOnCooldown.Add("Lava Burst"); // isolate the filler from the Flame-Shock synergy nuke
            Assert.Equal("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Lightning_Bolt_holds_while_moving()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000);
            g.SpellsOnCooldown.Add("Lava Burst");
            g.Moving = true; // cast-time spell can't be cast on the move
            Assert.NotEqual("Lightning Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Flame_Shock_is_applied_when_missing()
        {
            FakeGameClient g = Game(); // no Flame Shock on the target → maintain fires (prio 1.10)
            Assert.Equal("Flame Shock", Fire(g)?.Name);
        }

        [Fact]
        public void Lava_Burst_only_when_flame_shock_is_on_the_target()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000); // synergy condition met
            Assert.Equal("Lava Burst", Fire(g)?.Name); // beats Lightning Bolt
        }

        [Fact]
        public void Lava_Burst_holds_without_flame_shock()
        {
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Flame Shock"); // can't apply it this tick...
            RotationStep fired = Fire(g);
            Assert.NotEqual("Lava Burst", fired?.Name); // ...and without it on the target, Lava Burst holds
        }

        [Fact]
        public void Chain_Lightning_on_a_pack()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000); // FS up so it doesn't preempt
            g.SpellsOnCooldown.Add("Lava Burst");
            // three enemies near the target (>= ChainLightningCount default 3)
            for (ulong i = 2; i <= 3; i++)
                g.EnemyList.Add(new FakeUnit { Guid = i, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 27, HealthPercent = 100 });
            Assert.Equal("Chain Lightning", Fire(g)?.Name);
        }

        [Fact]
        public void Chain_Lightning_falls_back_to_lightning_bolt_when_unknown()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000);
            g.SpellsOnCooldown.Add("Lava Burst");
            g.UnknownSpells.Add("Chain Lightning"); // not learned yet
            for (ulong i = 2; i <= 3; i++)
                g.EnemyList.Add(new FakeUnit { Guid = i, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 27, HealthPercent = 100 });
            Assert.Equal("Lightning Bolt", Fire(g)?.Name); // the single-target filler covers the pack
        }

        [Fact]
        public void Earth_Shock_is_the_instant_filler_while_moving()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000); // FS up so its maintain is quiet
            g.Moving = true; // moving → cast-time nukes hold; Earth Shock (instant) fills
            Assert.Equal("Earth Shock", Fire(g)?.Name);
        }

        [Fact]
        public void Elemental_Mastery_on_a_big_fight()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000); // FS up so EM is the next thing
            g.SpellsOnCooldown.Add("Lava Burst"); // isolate EM from the Lava Burst synergy
            Assert.Equal("Elemental Mastery", Fire(g)?.Name);
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
        public void Reapplies_a_missing_flametongue_imbue()
        {
            FakeGameClient g = Game();
            // main hand has a weapon but NO temp-enchant (RemainingMs 0); off hand irrelevant for Elemental
            g.WeaponEnchantState = new WeaponEnchant(true, 0, false, 0);
            Assert.Equal("Weapon imbue", Fire(g)?.Name);
            Assert.Contains("Flametongue Weapon", g.CastLog); // Elemental main = Flametongue
        }

        [Fact]
        public void Bloodlust_can_be_enabled_on_a_big_fight()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000);
            g.SpellsOnCooldown.Add("Lava Burst");
            g.SpellsOnCooldown.Add("Elemental Mastery");
            g.UnknownSpells.Add("Heroism"); // Horde character → only Bloodlust known
            var s = Defaults();
            s.UseBloodlust.Value = true;
            Assert.Equal("Bloodlust", Fire(g, s)?.Name);
        }

        [Fact]
        public void Bloodlust_is_off_by_default()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true;
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000);
            g.SpellsOnCooldown.Add("Lava Burst");
            g.SpellsOnCooldown.Add("Elemental Mastery");
            RotationStep fired = Fire(g); // default settings → UseBloodlust false
            Assert.NotEqual("Bloodlust", fired?.Name);
            Assert.NotEqual("Heroism", fired?.Name);
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
