using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightCommonTests
    {
        // A baseline DK in melee, in combat, full runes, presence + Horn up. Diseases NOT on the target by default
        // so the disease tests can opt in. Uses the Blood spec as the host for the shared-block tests (it composes
        // the same DeathKnightCommon blocks every spec does).
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.DeathKnight };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 5,
                HealthPercent = 100, IsAttackable = true
            };
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

        private static RotationStep Fire(FakeGameClient g, DeathKnightSettings s) =>
            new RotationEngine(new SoloBlood(s).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Icy_touch_applies_frost_fever_first_when_both_diseases_missing()
        {
            // Both missing → Icy Touch (Frost Fever, prio 3.0) wins over Plague Strike (3.1).
            Assert.Equal("Icy Touch", Fire(Game(), Defaults())?.Name);
        }

        [Fact]
        public void Plague_strike_applies_blood_plague_when_frost_fever_is_already_up()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Frost Fever", mine: true, timeLeftMs: 20000); // FF up, BP missing → Plague Strike
            Assert.Equal("Plague Strike", Fire(g, Defaults())?.Name);
        }

        [Fact]
        public void Diseases_are_not_applied_to_a_dying_mob()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 10; // below the 15% DyingFloor → no fresh disease
            RotationStep fired = Fire(g, Defaults());
            Assert.NotEqual("Icy Touch", fired?.Name);
            Assert.NotEqual("Plague Strike", fired?.Name);
        }

        [Fact]
        public void Pestilence_spreads_to_a_pack_lacking_diseases()
        {
            FakeGameClient g = Game();
            // Target carries both diseases; two more nearby enemies lack them → Pestilence (prio 3.2).
            g.TargetUnit.WithAura("Frost Fever", mine: true, timeLeftMs: 20000);
            g.TargetUnit.WithAura("Blood Plague", mine: true, timeLeftMs: 20000);
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 8, HealthPercent = 100 });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 8, HealthPercent = 100 });
            var s = Defaults();
            s.UseCooldowns.Value = false; // a 3-pack is a "big fight" → Dancing Rune Weapon; silence it for this test
            Assert.Equal("Pestilence", Fire(g, s)?.Name);
        }

        [Fact]
        public void Pestilence_holds_when_the_pack_already_has_diseases()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Frost Fever", mine: true, timeLeftMs: 20000);
            g.TargetUnit.WithAura("Blood Plague", mine: true, timeLeftMs: 20000);
            for (ulong i = 2; i <= 3; i++)
            {
                var e = new FakeUnit { Guid = i, Reaction = Reaction.Hostile, IsAttackable = true, Distance = 8, HealthPercent = 100 };
                e.WithAura("Frost Fever", mine: true, timeLeftMs: 20000);
                e.WithAura("Blood Plague", mine: true, timeLeftMs: 20000);
                g.EnemyList.Add(e);
            }
            Assert.NotEqual("Pestilence", Fire(g, Defaults())?.Name);
        }

        // --- ghoul (PetControl reuse) ---

        [Fact]
        public void Raise_dead_summons_the_ghoul_out_of_combat_when_petless()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = false; // summon fires out of combat
            g.PetUnit = null;       // petless
            var s = Defaults();
            s.UseRaiseDead.Value = true;
            Assert.Equal("Pet summon", Fire(g, s)?.Name);
            Assert.Contains("Raise Dead", g.CastLog);
        }

        [Fact]
        public void Ghoul_syncs_to_the_players_target()
        {
            FakeGameClient g = Game();
            g.PetUnit = new FakeUnit { Guid = 50, Name = "Ghoul", IsAlive = true, TargetGuid = 0 };
            var s = Defaults();
            s.UseRaiseDead.Value = true;
            Fire(g, s);
            Assert.Contains(g.TargetUnit.Guid, g.PetAttackLog); // the pet was told to attack our target
        }

        [Fact]
        public void Ghoul_gnaws_a_casting_target()
        {
            FakeGameClient g = Game();
            g.PetUnit = new FakeUnit { Guid = 50, Name = "Ghoul", IsAlive = true, TargetGuid = g.TargetUnit.Guid };
            g.PetAbilities.Add("Gnaw");
            g.TargetUnit.IsCasting = true;
            var s = Defaults();
            s.UseRaiseDead.Value = true;
            s.InterruptCasts.Value = false; // silence Mind Freeze so the ghoul's Gnaw is the interrupt seen
            Fire(g, s);
            Assert.Contains("Gnaw", g.PetCastLog);
        }

        [Fact]
        public void Ghoul_leaps_to_a_distant_fight()
        {
            FakeGameClient g = Game();
            g.PetUnit = new FakeUnit { Guid = 50, Name = "Ghoul", IsAlive = true, TargetGuid = g.TargetUnit.Guid, Distance = 15 };
            g.PetAbilities.Add("Leap");
            var s = Defaults();
            s.UseRaiseDead.Value = true;
            Fire(g, s);
            Assert.Contains("Leap", g.PetCastLog);
        }

        [Fact]
        public void Ghoul_band_is_silent_when_raise_dead_is_off()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = false;
            g.PetUnit = null;
            var s = Defaults();
            s.UseRaiseDead.Value = false;
            Assert.NotEqual("Pet summon", Fire(g, s)?.Name);
            Assert.DoesNotContain("Raise Dead", g.CastLog);
        }
    }
}
