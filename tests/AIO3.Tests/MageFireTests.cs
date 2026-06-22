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
    }
}
