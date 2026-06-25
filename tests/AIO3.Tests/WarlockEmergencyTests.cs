using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>
    /// The emergency panic buttons spliced into every spec by WarlockCommon.Fear / HowlOfTerror. A warlock has
    /// no Frost Nova, so when it is low AND a mob is on it in melee it Fears (single) or Howls (surrounded) to
    /// break melee for a brief heal window. These fire ONLY under the low-HP + meleed emergency, so they don't
    /// disrupt normal play — proven here.
    /// </summary>
    public class WarlockEmergencyTests
    {
        private const long Fresh = 60000;

        // A warlock at full health by default, DoTs fresh (rotation otherwise quiet), pet on the target. Tests
        // tune health + add meleeing mobs to trigger the emergency. No emergency item is set, so the 0.05f item
        // step stays quiet and the Fear/Howl gates are what's being exercised.
        private static FakeGameClient LockGame()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock, InCombatFlag = true };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 30,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.HealthPercent = 100;
            g.MeUnit.WithAura("Fel Armor");
            g.TargetUnit.WithAura("Haunt", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Curse of Agony", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Corruption", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Unstable Affliction", mine: true, timeLeftMs: Fresh);
            g.PetUnit = new FakeUnit { Guid = 99, Name = "Voidwalker", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            return g;
        }

        /// <summary>Add a mob meleeing us (targeting us, within melee range). Also points the pet at it so the
        /// pet-attack peel step (which would otherwise win on priority) stays quiet and the test isolates the
        /// Fear/Howl emergency. The first attacker added becomes the pet's hold target.</summary>
        private static FakeUnit AddMeleeAttacker(FakeGameClient g, ulong guid, double hp = 100)
        {
            var add = new FakeUnit
            {
                Guid = guid, Name = "Add" + guid, Reaction = Reaction.Hostile,
                Distance = 4, IsAttackable = true, IsTargetingMe = true, IsTargetingMyPet = true, HealthPercent = hp
            };
            g.EnemyList.Add(add);
            // Keep the pet "already peeling" the lowest-HP attacker so PetControl.Attack doesn't re-issue and
            // outrank the emergency. BestPetTarget prefers the lowest-HP enemy targeting us; the first attacker
            // added is given the lowest HP below, so pin the pet to it.
            if (g.PetUnit.TargetGuid == 1) g.PetUnit.TargetGuid = guid;
            return add;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new WarlockSettings());

        private static RotationStep Fire(FakeGameClient g, WarlockSettings s) =>
            new RotationEngine(new SoloAffliction(s).BuildSteps()).Tick(CombatContext.Capture(g));

        // Death Coil now outranks Fear/Howl by default (it also heals), so the Fear/Howl tests below disable it to
        // isolate the spell they exercise.
        private static WarlockSettings NoDeathCoil()
        {
            var s = new WarlockSettings();
            s.UseDeathCoil.Value = false;
            return s;
        }

        // --- Death Coil (panic heal; wins over Fear/Howl) ---

        [Fact]
        public void Death_Coils_the_meleeing_mob_when_low()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20; // below FearHealthPercent (25)
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            Assert.Equal("Death Coil", Fire(g)?.Name);
            Assert.Contains("Death Coil", g.CastLog);
        }

        [Fact]
        public void Death_Coil_beats_single_Fear_when_both_are_eligible()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            // Both Fear and Death Coil are eligible (low + meleed) — Death Coil wins (it also heals).
            Assert.Equal("Death Coil", Fire(g)?.Name);
        }

        [Fact]
        public void Death_Coil_beats_Howl_when_surrounded()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            AddMeleeAttacker(g, 2);
            AddMeleeAttacker(g, 3); // surrounded → Howl is eligible, but Death Coil still wins
            Assert.Equal("Death Coil", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Death_Coil_at_full_health_even_when_meleed()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            RotationStep fired = Fire(g);
            Assert.NotEqual("Death Coil", fired?.Name);
            Assert.DoesNotContain("Death Coil", g.CastLog);
        }

        [Fact]
        public void Does_not_Death_Coil_when_low_but_nothing_is_meleeing_us()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20; // low, but the target is at 30yd and not on us
            RotationStep fired = Fire(g);
            Assert.NotEqual("Death Coil", fired?.Name);
            Assert.DoesNotContain("Death Coil", g.CastLog);
        }

        [Fact]
        public void UseDeathCoil_toggle_off_falls_back_to_Fear()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            var s = new WarlockSettings();
            s.UseDeathCoil.Value = false; // off → the Fear panic still breaks melee
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Death Coil", fired?.Name);
            Assert.Equal("Fear", fired?.Name);
        }

        [Fact]
        public void Low_level_lock_without_Death_Coil_falls_back_to_Fear()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            g.UnknownSpells.Add("Death Coil"); // not learned yet → skips cleanly, Fear takes over
            RotationStep fired = Fire(g);
            Assert.NotEqual("Death Coil", fired?.Name);
            Assert.Equal("Fear", fired?.Name);
        }

        // --- Fear (single) ---

        [Fact]
        public void Fears_the_meleeing_mob_when_low()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20; // below FearHealthPercent (25)
            // The current target is the one in melee on us → Fear it.
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            Assert.Equal("Fear", Fire(g, NoDeathCoil())?.Name);
            Assert.Contains("Fear", g.CastLog);
        }

        [Fact]
        public void Fears_an_add_in_melee_on_us_not_the_ranged_target()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            // The kill target is at range and not on us; a separate add is meleeing us → Fear fires for the add
            // (the FearTarget picker selects the meleeing-on-us enemy, since the ranged target isn't on us).
            AddMeleeAttacker(g, 2);
            Assert.Equal("Fear", Fire(g, NoDeathCoil())?.Name);
            Assert.Contains("Fear", g.CastLog);
        }

        [Fact]
        public void Does_not_Fear_at_full_health_even_when_meleed()
        {
            FakeGameClient g = LockGame();
            // Healthy: a mob is on us but it's not an emergency → no Fear.
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            RotationStep fired = Fire(g);
            Assert.NotEqual("Fear", fired?.Name);
            Assert.DoesNotContain("Fear", g.CastLog);
        }

        [Fact]
        public void Does_not_Fear_when_low_but_nothing_is_meleeing_us()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20; // low...
            // ...but the target is at 30yd and not targeting us → no melee to break.
            RotationStep fired = Fire(g);
            Assert.NotEqual("Fear", fired?.Name);
            Assert.DoesNotContain("Fear", g.CastLog);
        }

        [Fact]
        public void UseFear_toggle_off_stops_Fear()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            var s = new WarlockSettings();
            s.UseFear.Value = false;
            Assert.NotEqual("Fear", Fire(g, s)?.Name);
            Assert.DoesNotContain("Fear", g.CastLog);
        }

        [Fact]
        public void FearHealthPercent_zero_disables_the_emergency()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 5;
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            var s = new WarlockSettings();
            s.FearHealthPercent.Value = 0; // 0 = off
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Fear", fired?.Name);
            Assert.NotEqual("Howl of Terror", fired?.Name);
        }

        [Fact]
        public void Low_level_lock_without_Fear_skips_it_cleanly()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            g.TargetUnit.Distance = 4;
            g.TargetUnit.IsTargetingMe = true;
            g.UnknownSpells.Add("Fear"); // not learned yet
            Assert.NotEqual("Fear", Fire(g)?.Name);
            Assert.DoesNotContain("Fear", g.CastLog);
        }

        // --- Howl of Terror (AoE, surrounded) ---

        [Fact]
        public void Howls_when_low_and_surrounded()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            // Two adds meleeing us = surrounded → Howl beats single Fear.
            AddMeleeAttacker(g, 2);
            AddMeleeAttacker(g, 3);
            Assert.Equal("Howl of Terror", Fire(g, NoDeathCoil())?.Name);
            Assert.Contains("Howl of Terror", g.CastLog);
        }

        [Fact]
        public void Howl_does_not_fire_with_only_one_attacker()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            AddMeleeAttacker(g, 2); // only one mob on us → single Fear handles it, not Howl
            RotationStep fired = Fire(g, NoDeathCoil());
            Assert.NotEqual("Howl of Terror", fired?.Name);
            Assert.DoesNotContain("Howl of Terror", g.CastLog);
            Assert.Equal("Fear", fired?.Name); // the single-target panic instead
        }

        [Fact]
        public void Howl_does_not_fire_at_full_health_when_surrounded()
        {
            FakeGameClient g = LockGame();
            AddMeleeAttacker(g, 2);
            AddMeleeAttacker(g, 3);
            // Healthy → no emergency.
            RotationStep fired = Fire(g);
            Assert.NotEqual("Howl of Terror", fired?.Name);
            Assert.DoesNotContain("Howl of Terror", g.CastLog);
        }

        [Fact]
        public void UseHowl_toggle_off_falls_back_to_single_Fear()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            AddMeleeAttacker(g, 2);
            AddMeleeAttacker(g, 3);
            var s = NoDeathCoil(); // isolate Fear/Howl from Death Coil
            s.UseHowl.Value = false; // no AoE fear, but the single Fear still breaks one mob
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Howl of Terror", fired?.Name);
            Assert.Equal("Fear", fired?.Name);
        }
    }
}
