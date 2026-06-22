using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Mage;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class MageArcaneTests
    {
        // An arcane mage at range on a full-health dummy, full mana, upkeep buffs up.
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
            g.MeUnit.WithAura("Mage Armor");
            g.MeUnit.WithAura("Arcane Intellect");
            // Bags stocked so out-of-combat auto-conjure stays idle (would otherwise preempt at prio 0.4).
            g.ItemCounts["Conjured Mana Strudel"] = 20;
            g.ItemCounts["Conjured Mana Biscuit"] = 20;
            g.ItemCounts["Mana Sapphire"] = 1;
            return g;
        }

        private static RotationStep Fire(FakeGameClient g, MageSettings s = null) =>
            new RotationEngine(new SoloArcane(s ?? new MageSettings()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Arcane_Blast_is_the_filler()
        {
            Assert.Equal("Arcane Blast", Fire(MageGame())?.Name);
        }

        [Fact]
        public void Arcane_Missiles_on_a_Missile_Barrage_proc()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Missile Barrage");
            Assert.Equal("Arcane Missiles", Fire(g)?.Name);
        }

        [Fact]
        public void Arcane_Barrage_dumps_at_three_stacks()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Arcane Blast", stacks: 3); // ramped → instant dump
            Assert.Equal("Arcane Barrage", Fire(g)?.Name);
        }

        [Fact]
        public void Arcane_Power_on_an_elite()
        {
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            var s = new MageSettings();
            s.UseRacials.Value = false; // racials sit just above the cooldowns; isolate Arcane Power
            Assert.Equal("Arcane Power", Fire(g, s)?.Name);
        }

        [Fact]
        public void Keeps_mage_armor_up()
        {
            FakeGameClient g = MageGame();
            g.MeUnit.Auras.Remove("Mage Armor");
            Assert.Equal("Armor", Fire(g)?.Name);
            Assert.Contains("Mage Armor", g.CastLog); // auto picks Mage Armor for arcane
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
