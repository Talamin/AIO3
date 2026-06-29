using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightFrostTests
    {
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.DeathKnight };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 5,
                HealthPercent = 100, IsAttackable = true
            };
            g.TargetUnit.WithAura("Frost Fever", mine: true, timeLeftMs: 20000);
            g.TargetUnit.WithAura("Blood Plague", mine: true, timeLeftMs: 20000);
            g.EnemyList.Add(g.TargetUnit);
            g.InCombatFlag = true;
            g.ProductFightingFlag = true;
            g.MeUnit.WithAura("Blood Presence");
            g.MeUnit.WithAura("Horn of Winter");
            g.RunesReadyByType[RuneType.Blood] = 2;
            g.RunesReadyByType[RuneType.Frost] = 2;
            g.RunesReadyByType[RuneType.Unholy] = 2;
            return g;
        }

        private static DeathKnightSettings Defaults()
        {
            var s = new DeathKnightSettings();
            s.UseRacials.Value = false;
            s.UseRaiseDead.Value = false;
            return s;
        }

        private static RotationStep Fire(FakeGameClient g, DeathKnightSettings s = null) =>
            new RotationEngine(new SoloFrost(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Obliterate_is_the_main_strike_with_diseases_up()
        {
            // Diseases up, full runes, 1 enemy → Obliterate (prio 5.0) beats the Blood-rune fillers below it.
            Assert.Equal("Obliterate", Fire(Game())?.Name);
        }

        [Fact]
        public void Obliterate_holds_without_both_diseases()
        {
            FakeGameClient g = Game();
            g.TargetUnit.Auras.Remove("Frost Fever"); // a disease missing → Icy Touch re-applies (above Obliterate)
            Assert.NotEqual("Obliterate", Fire(g)?.Name);
        }

        [Fact]
        public void Howling_blast_on_the_rime_proc()
        {
            FakeGameClient g = Game();
            g.MeUnit.WithAura("Freezing Fog"); // the Rime proc aura
            Assert.Equal("Howling Blast", Fire(g)?.Name);
        }

        [Fact]
        public void Howling_blast_on_killing_machine()
        {
            FakeGameClient g = Game();
            g.MeUnit.WithAura("Killing Machine");
            Assert.Equal("Howling Blast", Fire(g)?.Name);
        }

        [Fact]
        public void Frost_strike_dumps_runic_power()
        {
            FakeGameClient g = Game();
            // Make Obliterate unaffordable (no Frost/Unholy/Death) so the 0-rune Frost Strike is reached with RP up.
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.MeUnit.RunicPower = 60; // >= the 40 default
            var s = Defaults();
            s.UseCooldowns.Value = false; // silence Empower Rune Weapon (correctly fires when rune-starved)
            Assert.Equal("Frost Strike", Fire(g, s)?.Name);
        }

        [Fact]
        public void Death_and_decay_on_a_pack()
        {
            FakeGameClient g = Game();
            for (ulong i = 2; i <= 4; i++) // 3 more → 4 near the target (>= DnD count 3)
            {
                var e = new FakeUnit { Guid = i, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 };
                // Give them the diseases too, so Pestilence (which spreads to enemies LACKING diseases) stays quiet
                // and this isolates the AoE choice.
                e.WithAura("Frost Fever", mine: true, timeLeftMs: 20000);
                e.WithAura("Blood Plague", mine: true, timeLeftMs: 20000);
                g.EnemyList.Add(e);
            }
            Assert.Equal("Death and Decay", Fire(g)?.Name);
        }

        [Fact]
        public void Rune_starved_does_not_fire_an_unaffordable_obliterate()
        {
            FakeGameClient g = Game();
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.RunesReadyByType[RuneType.Blood] = 0;
            g.MeUnit.RunicPower = 0;
            var s = Defaults();
            s.UseCooldowns.Value = false;
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Obliterate", fired?.Name);
            if (fired != null)
                Assert.False(DeathKnightCommon.RuneCost.ContainsKey(fired.Name));
        }
    }
}
