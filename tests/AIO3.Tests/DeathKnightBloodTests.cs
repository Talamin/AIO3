using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightBloodTests
    {
        // A Blood DK in melee, in combat, full runes (2 of each), full health, presence + Horn up, both diseases
        // already on the target — so the upkeep/disease bands stay quiet and each test isolates the rule it cares
        // about. Racials off (they'd fire off-GCD and mask the strike priorities; RacialsTests covers them).
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
            FullRunes(g);
            return g;
        }

        private static void FullRunes(FakeGameClient g)
        {
            g.RunesReadyByType[RuneType.Blood] = 2;
            g.RunesReadyByType[RuneType.Frost] = 2;
            g.RunesReadyByType[RuneType.Unholy] = 2;
            g.RunesReadyByType[RuneType.Death] = 0;
        }

        private static DeathKnightSettings Defaults()
        {
            var s = new DeathKnightSettings();
            s.UseRacials.Value = false;
            s.UseRaiseDead.Value = false; // no ghoul noise in the strike tests
            return s;
        }

        private static RotationStep Fire(FakeGameClient g, DeathKnightSettings s = null) =>
            new RotationEngine(new SoloBlood(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Single_target_filler_is_heart_or_blood_strike()
        {
            // Diseases up, nothing special → with 1 enemy, Blood Strike (==1) is the builder Heart Strike (>=2) skips.
            Assert.Equal("Blood Strike", Fire(Game())?.Name);
        }

        [Fact]
        public void Heart_strike_cleaves_a_pack()
        {
            FakeGameClient g = Game();
            // 2nd enemy already carries the diseases so Pestilence stays quiet; isolate the builder choice.
            var e = new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 6, HealthPercent = 100 };
            e.WithAura("Frost Fever", mine: true, timeLeftMs: 20000);
            e.WithAura("Blood Plague", mine: true, timeLeftMs: 20000);
            g.EnemyList.Add(e);
            var s = Defaults();
            s.UseCooldowns.Value = false; // a 2-pack is a "big fight" → Dancing Rune Weapon; silence it for this test
            // 2 enemies near the target → Heart Strike (>=2) wins over Blood Strike (==1, now false).
            Assert.Equal("Heart Strike", Fire(g, s)?.Name);
        }

        [Fact]
        public void Vampiric_blood_when_very_low()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 25; // below the 30% default
            Assert.Equal("Vampiric Blood", Fire(g)?.Name);
        }

        [Fact]
        public void Rune_tap_at_the_configured_health()
        {
            FakeGameClient g = Game();
            g.MeUnit.HealthPercent = 45; // below 50% Rune Tap, above 30% Vampiric Blood
            Assert.Equal("Rune Tap", Fire(g)?.Name);
        }

        [Fact]
        public void Anti_magic_shell_vs_a_caster_on_me()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.IsTargetingMe = true;
            Assert.Equal("Anti-Magic Shell", Fire(g)?.Name);
        }

        [Fact]
        public void Death_grip_pulls_a_distant_engaged_mob()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = false;          // not yet in melee combat, product is committing
            g.ProductFightingFlag = true;
            g.TargetUnit.Distance = 25;      // past melee
            g.TargetUnit.IsTargetingMe = false;
            Assert.Equal("Death Grip", Fire(g)?.Name);
        }

        [Fact]
        public void Diseases_are_applied_when_missing()
        {
            FakeGameClient g = Game();
            g.TargetUnit.Auras.Remove("Frost Fever"); // Frost Fever missing → Icy Touch re-applies it
            Assert.Equal("Icy Touch", Fire(g)?.Name);
        }

        [Fact]
        public void Death_coil_dumps_runic_power()
        {
            FakeGameClient g = Game();
            // Make the strikes unaffordable so the 0-rune RP dump is reached, with RP to spend.
            g.RunesReadyByType[RuneType.Blood] = 0;
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.MeUnit.RunicPower = 60;
            var s = Defaults();
            s.UseCooldowns.Value = false; // silence Empower Rune Weapon (which correctly fires when rune-starved)
            Assert.Equal("Death Coil", Fire(g, s)?.Name);
        }

        [Fact]
        public void Rune_starved_rotation_does_not_jam_on_an_unaffordable_strike()
        {
            FakeGameClient g = Game();
            // All runes spent, no runic power → nothing rune-costed and no RP dump should fire. The engine must
            // pick a 0-rune step or nothing — crucially NOT a rune-costed strike it can't pay for.
            g.RunesReadyByType[RuneType.Blood] = 0;
            g.RunesReadyByType[RuneType.Frost] = 0;
            g.RunesReadyByType[RuneType.Unholy] = 0;
            g.RunesReadyByType[RuneType.Death] = 0;
            g.MeUnit.RunicPower = 0;
            var s = Defaults();
            s.UseCooldowns.Value = false; // silence Empower Rune Weapon so we test ONLY that no rune strike fires
            RotationStep fired = Fire(g, s);
            // Whatever fires (likely nothing) must not be a rune-costed ability.
            if (fired != null)
                Assert.False(DeathKnightCommon.RuneCost.ContainsKey(fired.Name),
                    $"Rune-starved rotation fired the rune-costed {fired.Name} — it would silently fail and jam.");
        }

        [Fact]
        public void Presence_is_kept_up_when_missing()
        {
            FakeGameClient g = Game();
            g.MeUnit.Auras.Remove("Blood Presence");
            // The step is named "Presence" (it resolves the configured presence at cast time); assert it casts the one.
            Assert.Equal("Presence", Fire(g)?.Name);
            Assert.Contains("Blood Presence", g.CastLog);
        }

        [Fact]
        public void Horn_of_winter_is_kept_up_when_missing()
        {
            FakeGameClient g = Game();
            g.MeUnit.Auras.Remove("Horn of Winter");
            Assert.Equal("Horn of Winter", Fire(g)?.Name);
        }

        [Fact]
        public void Mind_freeze_interrupts_a_casting_target()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCasting = true;
            g.TargetUnit.CastingSpellId = 100;
            // Interrupt sits above the strikes; Anti-Magic Shell only fires when the caster targets me (it doesn't).
            Assert.Equal("Mind Freeze", Fire(g)?.Name);
        }

        [Fact]
        public void No_target_does_not_throw()
        {
            var g = new FakeGameClient { Class = WowClass.DeathKnight };
            FullRunes(g);
            Assert.Null(Record.Exception(() => Fire(g)));
        }
    }
}
