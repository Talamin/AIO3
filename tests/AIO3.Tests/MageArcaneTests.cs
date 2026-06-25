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
        public void Arcane_Missiles_is_the_standing_still_dump_at_the_cap()
        {
            // F10: Arcane Missiles is now the standing-still dump at the stack cap, even WITHOUT a Missile Barrage
            // proc (it's free on a proc and cheaper than another escalating Arcane Blast).
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Arcane Blast", stacks: 4); // default cap reached, standing still, no proc
            Assert.Equal("Arcane Missiles", Fire(g)?.Name);
        }

        [Fact]
        public void Arcane_Barrage_is_the_moving_dump_at_the_cap()
        {
            // Moving → can't channel Missiles, so Arcane Barrage (instant) is the dump at the cap.
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Arcane Blast", stacks: 4);
            g.Moving = true;
            Assert.Equal("Arcane Barrage", Fire(g)?.Name);
        }

        [Fact]
        public void Keeps_ramping_arcane_blast_below_the_cap()
        {
            // Below the configured cap (default 4) we keep ramping with Arcane Blast — the dump doesn't fire yet.
            FakeGameClient g = MageGame();
            g.MeUnit.WithAura("Arcane Blast", stacks: 3); // one below the default cap
            Assert.Equal("Arcane Blast", Fire(g)?.Name);
        }

        [Fact]
        public void Conserves_mana_by_capping_at_two_stacks()
        {
            // F10: below the conserve mana% the stack cap drops to 2 → at 2 stacks we dump (Missiles standing still)
            // instead of ramping Arcane Blast to 4 and draining the pool on its escalating cost.
            FakeGameClient g = MageGame();
            g.MeUnit.PowerPercent = 26;                   // below the 30% conserve threshold, above wand/gem/evoc floors
            g.MeUnit.WithAura("Arcane Blast", stacks: 2); // conserve cap → already at the dump point
            Assert.Equal("Arcane Missiles", Fire(g)?.Name);
        }

        [Fact]
        public void Arcane_Power_pairs_with_at_least_one_stack()
        {
            // F11: the burst is gated on a minimum Arcane Blast stack. At >= 1 stack Arcane Power fires.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            g.MeUnit.WithAura("Arcane Blast", stacks: 1); // ramp started
            var s = new MageSettings();
            s.UseRacials.Value = false; // racials sit just above the cooldowns; isolate Arcane Power
            Assert.Equal("Arcane Power", Fire(g, s)?.Name);
        }

        [Fact]
        public void Burst_does_not_fire_at_zero_stacks()
        {
            // F11: with no Arcane Blast stacks the burst holds — it shouldn't pop AP/PoM blind at fight start.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            var s = new MageSettings();
            s.UseRacials.Value = false;
            RotationStep fired = Fire(g, s); // 0 stacks
            Assert.NotEqual("Arcane Power", fired?.Name);
            Assert.NotEqual("Presence of Mind", fired?.Name);
            Assert.Equal("Arcane Blast", fired?.Name); // ramp first
        }

        [Fact]
        public void Presence_of_Mind_needs_two_stacks()
        {
            // F11: PoM wants its free instant on a real Arcane Blast → it only fires at >= 2 stacks. At 1 stack the
            // other burst pieces (AP at >= 1) take priority; PoM itself must not fire yet.
            FakeGameClient g = MageGame();
            g.InCombatFlag = true;
            g.TargetUnit.IsElite = true;
            var s = new MageSettings();
            s.UseRacials.Value = false;
            // Put AP / Icy Veins / Mirror Image out of the way so PoM (lowest of the burst) is what we'd see if it fired.
            g.UnknownSpells.Add("Arcane Power");
            g.UnknownSpells.Add("Icy Veins");
            g.UnknownSpells.Add("Mirror Image");

            g.MeUnit.WithAura("Arcane Blast", stacks: 1);   // one stack → PoM not yet
            Assert.NotEqual("Presence of Mind", Fire(g, s)?.Name);

            g.MeUnit.WithAura("Arcane Blast", stacks: 2);   // two stacks → PoM fires
            Assert.Equal("Presence of Mind", Fire(g, s)?.Name);
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
