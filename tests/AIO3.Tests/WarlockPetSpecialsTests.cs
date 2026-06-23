using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>
    /// The demon's own special abilities spliced into every spec by WarlockCommon.PetSpecials: the Voidwalker
    /// Torment tank-taunt, the Felhunter Spell Lock interrupt, and the Imp Firebolt. Each is gated on the
    /// current pet HAVING the ability (PetHasAbility / PetAbilityReady), so the same set is safe for any demon —
    /// these tests prove it fires on the right pet and auto-skips on the wrong one / behind its toggle.
    /// </summary>
    public class WarlockPetSpecialsTests
    {
        private const long Fresh = 60000;

        // A warlock in combat with a mob on us (so the taunt/interrupt gates have something to react to), all
        // DoTs fresh so the rotation is otherwise quiet, and a named pet WITHOUT any pet abilities by default —
        // each test grants exactly the ability it exercises. petName tags the demon for readability.
        private static FakeGameClient LockGame(string petName = "Voidwalker")
        {
            var g = new FakeGameClient { Class = WowClass.Warlock, InCombatFlag = true };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 30,
                HealthPercent = 100, IsAttackable = true, IsTargetingMe = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.HealthPercent = 100;
            g.MeUnit.WithAura("Fel Armor");
            g.TargetUnit.WithAura("Haunt", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Curse of Agony", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Corruption", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Unstable Affliction", mine: true, timeLeftMs: Fresh);
            // Pet alive and already on our target, so the Pet-summon / Pet-attack upkeep stays quiet.
            g.PetUnit = new FakeUnit { Guid = 99, Name = petName, IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new WarlockSettings());

        private static RotationStep Fire(FakeGameClient g, WarlockSettings s) =>
            new RotationEngine(new SoloAffliction(s).BuildSteps()).Tick(CombatContext.Capture(g));

        // --- Torment (Voidwalker tank-taunt) ---

        [Fact]
        public void Voidwalker_Torments_to_tank_a_mob_off_us()
        {
            FakeGameClient g = LockGame("Voidwalker");
            g.PetAbilities.Add("Torment"); // a Voidwalker has Torment
            // A mob is on us (IsTargetingMe) → the pet should taunt it onto itself.
            Assert.Equal("Pet taunt", Fire(g)?.Name);
            Assert.Contains("Torment", g.PetCastLog);
        }

        [Fact]
        public void Torment_skips_when_no_mob_is_on_us()
        {
            FakeGameClient g = LockGame("Voidwalker");
            g.PetAbilities.Add("Torment");
            g.TargetUnit.IsTargetingMe = false; // nothing on the owner → no need to taunt
            RotationStep fired = Fire(g);
            Assert.NotEqual("Pet taunt", fired?.Name);
            Assert.DoesNotContain("Torment", g.PetCastLog);
        }

        [Fact]
        public void Torment_auto_skips_on_an_Imp_that_lacks_it()
        {
            FakeGameClient g = LockGame("Imp"); // an Imp has no Torment on its bar (PetAbilities empty)
            RotationStep fired = Fire(g);
            Assert.NotEqual("Pet taunt", fired?.Name);
            Assert.DoesNotContain("Torment", g.PetCastLog);
        }

        [Fact]
        public void PetTank_toggle_off_stops_the_taunt()
        {
            FakeGameClient g = LockGame("Voidwalker");
            g.PetAbilities.Add("Torment");
            var s = new WarlockSettings();
            s.PetTank.Value = false; // owner doesn't want the demon tanking
            Assert.NotEqual("Pet taunt", Fire(g, s)?.Name);
            Assert.DoesNotContain("Torment", g.PetCastLog);
        }

        // --- Spell Lock (Felhunter interrupt) ---

        [Fact]
        public void Felhunter_Spell_Locks_a_casting_target()
        {
            FakeGameClient g = LockGame("Felhunter");
            g.PetAbilities.Add("Spell Lock");
            g.TargetUnit.IsCasting = true; // the target is mid-cast → interrupt it
            Assert.Equal("Pet Spell Lock", Fire(g)?.Name);
            Assert.Contains("Spell Lock", g.PetCastLog);
        }

        [Fact]
        public void Spell_Lock_skips_when_the_target_is_not_casting()
        {
            FakeGameClient g = LockGame("Felhunter");
            g.PetAbilities.Add("Spell Lock");
            g.TargetUnit.IsCasting = false; // nothing to interrupt
            RotationStep fired = Fire(g);
            Assert.NotEqual("Pet Spell Lock", fired?.Name);
            Assert.DoesNotContain("Spell Lock", g.PetCastLog);
        }

        [Fact]
        public void Spell_Lock_does_not_fire_in_Never_mode()
        {
            FakeGameClient g = LockGame("Felhunter");
            g.PetAbilities.Add("Spell Lock");
            g.TargetUnit.IsCasting = true;
            var s = new WarlockSettings();
            s.InterruptCasts.Value = InterruptModes.Never; // a product owns interrupts
            Assert.NotEqual("Pet Spell Lock", Fire(g, s)?.Name);
            Assert.DoesNotContain("Spell Lock", g.PetCastLog);
        }

        [Fact]
        public void Spell_Lock_fires_in_Always_mode()
        {
            FakeGameClient g = LockGame("Felhunter");
            g.PetAbilities.Add("Spell Lock");
            g.TargetUnit.IsCasting = true;
            var s = new WarlockSettings();
            s.InterruptCasts.Value = InterruptModes.Always;
            Assert.Equal("Pet Spell Lock", Fire(g, s)?.Name);
            Assert.Contains("Spell Lock", g.PetCastLog);
        }

        [Fact]
        public void Spell_Lock_auto_skips_on_a_Voidwalker_that_lacks_it()
        {
            FakeGameClient g = LockGame("Voidwalker"); // Voidwalker has no Spell Lock
            g.TargetUnit.IsCasting = true;
            RotationStep fired = Fire(g);
            Assert.NotEqual("Pet Spell Lock", fired?.Name);
            Assert.DoesNotContain("Spell Lock", g.PetCastLog);
        }

        // --- Firebolt (Imp nuke) — kept on AUTOCAST, not re-triggered each tick ---

        [Fact]
        public void Imp_keeps_Firebolt_on_autocast()
        {
            FakeGameClient g = LockGame("Imp");
            g.PetAbilities.Add("Firebolt");
            Fire(g);
            Assert.True(g.PetAutocast["Firebolt"]);   // the Imp fires it itself; we don't actively cast it
            Assert.DoesNotContain("Firebolt", g.PetCastLog);
        }

        [Fact]
        public void ImpFirebolt_toggle_off_disables_the_autocast()
        {
            FakeGameClient g = LockGame("Imp");
            g.PetAbilities.Add("Firebolt");
            var s = new WarlockSettings();
            s.ImpFirebolt.Value = false;
            Fire(g, s);
            Assert.False(g.PetAutocast["Firebolt"]);  // autocast is turned OFF, not left running
        }

        [Fact]
        public void Imp_keeps_Blood_Pact_on_autocast()
        {
            // The Imp's party stamina buff is kept on autocast too (gated on managing the pet, no separate toggle).
            FakeGameClient g = LockGame("Imp");
            g.PetAbilities.Add("Blood Pact");
            Fire(g);
            Assert.True(g.PetAutocast["Blood Pact"]);
        }

        [Fact]
        public void Pet_specials_all_skip_when_pet_management_is_off()
        {
            FakeGameClient g = LockGame("Felhunter");
            g.PetAbilities.Add("Spell Lock");
            g.TargetUnit.IsCasting = true;
            var s = new WarlockSettings();
            s.ManagePet.Value = false; // a product manages the pet entirely
            Assert.DoesNotContain("Spell Lock", g.PetCastLog);
            Assert.NotEqual("Pet Spell Lock", Fire(g, s)?.Name);
        }
    }
}
