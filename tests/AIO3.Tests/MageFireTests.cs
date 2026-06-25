using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Mage;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class MageFireTests
    {
        // A fire mage at range on a full-health dummy, full mana, upkeep buffs up and Living Bomb already
        // ticking so the rotation reaches the steady-state nuke.
        private static FakeGameClient MageGame()
        {
            var g = new FakeGameClient { Class = WowClass.Mage };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 30,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.WithAura("Molten Armor");
            g.MeUnit.WithAura("Arcane Intellect");
            g.TargetUnit.WithAura("Living Bomb", mine: true, timeLeftMs: 8000); // DoT up → reach the filler
            // Bags stocked so out-of-combat auto-conjure stays idle (would otherwise preempt at prio 0.4).
            g.ItemCounts["Conjured Mana Strudel"] = 20;
            g.ItemCounts["Conjured Mana Biscuit"] = 20;
            g.ItemCounts["Mana Sapphire"] = 1;
            return g;
        }

        private static RotationStep Fire(FakeGameClient g, MageSettings s = null) =>
            new RotationEngine(new SoloFire(s ?? new MageSettings()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Fireball_is_the_filler()
        {
            Assert.Equal("Fireball", Fire(MageGame())?.Name);
        }

        [Fact]
        public void Pyroblast_on_a_Hot_Streak_proc()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Hot Streak");
            Assert.Equal("Pyroblast", Fire(g)?.Name);
        }

        [Fact]
        public void Maintains_living_bomb_when_it_falls_off()
        {
            FakeGameClient g = MageGame();
            g.TargetUnit.Auras.Remove("Living Bomb");
            Assert.Equal("Living Bomb", Fire(g)?.Name);
        }

        [Fact]
        public void Living_bomb_skips_a_dying_target()
        {
            // Dying-mob fix: don't re-apply the 12s Living Bomb DoT to a mob already in execute range — it dies first.
            // Below the execute floor the Fire Blast execute (priority 6) takes the slot instead of Living Bomb (4.5).
            FakeGameClient g = MageGame();
            g.TargetUnit.Auras.Remove("Living Bomb");
            g.TargetUnit.HealthPercent = MageCommon.LivingBombMinTargetHealth - 1; // in execute range
            RotationStep fired = Fire(g);
            Assert.NotEqual("Living Bomb", fired?.Name);
            Assert.Equal("Fire Blast", fired?.Name);
        }

        [Fact]
        public void Combustion_on_an_elite()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            var s = new MageSettings();
            s.UseRacials.Value = false; // racials sit just above the cooldowns; isolate Combustion
            Assert.Equal("Combustion", Fire(g, s)?.Name);
        }

        [Fact]
        public void Combustion_waits_for_living_bomb_to_be_up()
        {
            // F3: Combustion only after Living Bomb is ticking, so its Ignite/crit value compounds an active DoT.
            // With the DoT NOT up, Combustion holds and the rotation applies Living Bomb first.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            g.TargetUnit.Auras.Remove("Living Bomb"); // no DoT yet → too early for Combustion
            var s = new MageSettings();
            s.UseRacials.Value = false;
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Combustion", fired?.Name);
            Assert.Equal("Living Bomb", fired?.Name); // applies the DoT first

            // Once Living Bomb is ticking, Combustion fires.
            g.TargetUnit.WithAura("Living Bomb", mine: true, timeLeftMs: 8000);
            Assert.Equal("Combustion", Fire(g, s)?.Name);
        }

        [Fact]
        public void Flamestrike_on_a_pack()
        {
            FakeGameClient g = MageGame();
            // Three enemies within AoE radius (incl. target) → AoE threshold met.
            g.TargetUnit.Distance = 8;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 7, HealthPercent = 100 });
            var s = new MageSettings();
            s.UseCooldowns.Value = false; // a pack also triggers Combustion (a cooldown); isolate the AoE nuke
            Assert.Equal("Flamestrike", Fire(g, s)?.Name);
        }

        [Fact]
        public void Keeps_molten_armor_up()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.Auras.Remove("Molten Armor");
            Assert.Equal("Armor", Fire(g)?.Name);
            Assert.Contains("Molten Armor", g.CastLog); // auto picks Molten for fire
        }

        [Fact]
        public void No_target_does_not_throw()
        {
            var g = new FakeGameClient { Class = WowClass.Mage };
            g.MeUnit.PowerPercent = 100;
            Assert.Null(Record.Exception(() => Fire(g)));
        }

        [Fact]
        public void Mana_shield_casts_when_low_and_meleed_by_a_healthy_target()
        {
            // Baseline: hurt (below the 50% Mana Shield threshold, above the 15% Ice Block panic), an enemy on us,
            // mana to spare, the meleeing target still healthy → Mana Shield goes up.
            FakeGameClient g = MageGame();
            g.MeUnit.HealthPercent = 40;
            g.TargetUnit.IsTargetingMe = true;
            g.TargetUnit.Distance = 5;
            Assert.Equal("Mana Shield", Fire(g)?.Name);
        }

        [Fact]
        public void Mana_shield_does_not_recast_on_a_dying_lone_target()
        {
            // Dying-mob fix: same low-HP + meleed setup, but the lone target is about to die (below
            // ShieldMinTargetHealth) → don't burn mana shielding against a mob with seconds to live.
            FakeGameClient g = MageGame();
            g.MeUnit.HealthPercent = 40;
            g.TargetUnit.IsTargetingMe = true;
            g.TargetUnit.Distance = 5;
            g.TargetUnit.HealthPercent = MageCommon.ShieldMinTargetHealth - 1; // 19% → dying lone target
            Assert.NotEqual("Mana Shield", Fire(g)?.Name);
            Assert.DoesNotContain("Mana Shield", g.CastLog);
        }
    }
}
