using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightUnholyTests
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
            s.UseRaiseDead.Value = false; // tested separately in DeathKnightCommonTests
            return s;
        }

        private static RotationStep Fire(FakeGameClient g, DeathKnightSettings s = null) =>
            new RotationEngine(new SoloUnholy(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Scourge_strike_is_the_main_strike_with_diseases_up()
        {
            Assert.Equal("Scourge Strike", Fire(Game())?.Name);
        }

        [Fact]
        public void Scourge_strike_holds_without_both_diseases()
        {
            FakeGameClient g = Game();
            g.TargetUnit.Auras.Remove("Blood Plague");
            Assert.NotEqual("Scourge Strike", Fire(g)?.Name);
        }

        [Fact]
        public void Summon_gargoyle_on_a_boss_fight()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsElite = true; // elite → a "big fight" → gargoyle (prio 2.0) above the strikes
            Assert.Equal("Summon Gargoyle", Fire(g)?.Name);
        }

        [Fact]
        public void Death_strike_self_heals_below_the_threshold()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 40; // below the 50% default → Death Strike (prio 2.5) above the strikes
            Assert.Equal("Death Strike", Fire(g)?.Name);
        }

        [Fact]
        public void Death_coil_dumps_runic_power_at_80()
        {
            FakeGameClient g = Game();
            // Make Scourge Strike + Blood-rune fillers unaffordable so the 0-rune Death Coil is reached.
            g.RunesReadyByType[RuneType.Blood] = 0;
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.MeUnit.RunicPower = 90; // >= the 80 default
            var s = Defaults();
            s.UseCooldowns.Value = false; // silence Empower Rune Weapon (correctly fires when rune-starved)
            Assert.Equal("Death Coil", Fire(g, s)?.Name);
        }

        [Fact]
        public void Death_coil_holds_below_the_runic_power_threshold()
        {
            FakeGameClient g = Game();
            g.RunesReadyByType[RuneType.Blood] = 0;
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.MeUnit.RunicPower = 50; // below 80 → no dump
            var s = Defaults();
            s.UseCooldowns.Value = false;
            Assert.NotEqual("Death Coil", Fire(g, s)?.Name);
        }

        [Fact]
        public void Single_target_filler_is_blood_strike()
        {
            FakeGameClient g = Game();
            // Make Scourge Strike unaffordable (no Frost/Unholy/Death) so the Blood-rune filler is reached.
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.RunesReadyByType[RuneType.Blood] = 2;
            var s = Defaults();
            s.UseCooldowns.Value = false; // 2 total runes <= the Empower threshold; silence it to isolate the filler
            Assert.Equal("Blood Strike", Fire(g, s)?.Name); // 1 enemy → Blood Strike (==1), Heart Strike skips
        }
    }
}
