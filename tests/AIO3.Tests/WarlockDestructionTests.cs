using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarlockDestructionTests
    {
        private const long Fresh = 60000; // a DoT with plenty of time left → its upkeep slot stays quiet

        // A Destruction warlock at range on a full dummy, full mana/health, armor up, curse + DoTs fresh, with an
        // Imp on the target — so each test isolates one rule. Conflagrate + Chaos Bolt are ON by default.
        private static FakeGameClient LockGame(bool withPet = true)
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 30,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.HealthPercent = 100;
            g.MeUnit.WithAura("Fel Armor"); // armor up
            g.TargetUnit.WithAura("Curse of Agony", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Corruption", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Immolate", mine: true, timeLeftMs: Fresh);
            if (withPet)
                g.PetUnit = new FakeUnit { Guid = 99, Name = "Imp", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 20 };
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new WarlockSettings());

        private static RotationStep Fire(FakeGameClient g, WarlockSettings s) =>
            new RotationEngine(new SoloDestruction(s).BuildSteps()).Tick(CombatContext.Capture(g));

        // Bursts off, to isolate the DoT ladder + the Incinerate filler from Conflagrate/Chaos Bolt.
        private static WarlockSettings NoBurst()
        {
            var s = new WarlockSettings();
            s.UseConflagrate.Value = false;
            s.UseChaosBolt.Value = false;
            return s;
        }

        // --- Conflagrate / Chaos Bolt (default on) ---

        [Fact]
        public void Conflagrate_fires_while_Immolate_is_up()
        {
            Assert.Equal("Conflagrate", Fire(LockGame())?.Name);
        }

        [Fact]
        public void Conflagrate_needs_Immolate_on_the_target()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Immolate");
            // No Immolate → Conflagrate can't fire; Immolate re-applies instead (it outprioritizes Conflagrate).
            Assert.Equal("Immolate", Fire(g)?.Name);
        }

        [Fact]
        public void Chaos_Bolt_when_Conflagrate_is_off()
        {
            var s = new WarlockSettings();
            s.UseConflagrate.Value = false;
            Assert.Equal("Chaos Bolt", Fire(LockGame(), s)?.Name);
        }

        [Fact]
        public void Chaos_Bolt_can_be_disabled()
        {
            // Both bursts off → the rotation falls through to the Incinerate filler.
            Assert.NotEqual("Chaos Bolt", Fire(LockGame(), NoBurst())?.Name);
        }

        // --- filler ---

        [Fact]
        public void Incinerate_is_the_filler_without_the_bursts()
        {
            Assert.Equal("Incinerate", Fire(LockGame(), NoBurst())?.Name);
        }

        [Fact]
        public void Incinerate_holds_while_moving()
        {
            FakeGameClient g = LockGame();
            g.Moving = true; // Incinerate is a cast-time nuke
            Assert.NotEqual("Incinerate", Fire(g, NoBurst())?.Name);
        }

        [Fact]
        public void Shadow_Bolt_is_the_filler_before_Incinerate_is_learned()
        {
            FakeGameClient g = LockGame();
            g.UnknownSpells.Add("Incinerate"); // low-level destro without Incinerate yet
            Assert.Equal("Shadow Bolt", Fire(g, NoBurst())?.Name);
        }

        // --- DoT upkeep (bursts off to isolate the ladder) ---

        [Fact]
        public void Maintains_Immolate()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Immolate");
            Assert.Equal("Immolate", Fire(g, NoBurst())?.Name);
        }

        [Fact]
        public void Immolate_holds_while_moving()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Immolate");
            g.Moving = true; // Immolate is a cast-time DoT
            Assert.NotEqual("Immolate", Fire(g, NoBurst())?.Name);
        }

        [Fact]
        public void Maintains_Corruption()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Corruption");
            Assert.Equal("Corruption", Fire(g, NoBurst())?.Name);
        }

        [Fact]
        public void Applies_the_curse()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Curse of Agony");
            Assert.Equal("Curse", Fire(g, NoBurst())?.Name);
            Assert.Contains("Curse of Agony", g.CastLog);
        }

        // --- mana / sustain ---

        [Fact]
        public void Life_Tap_when_mana_low_and_health_high()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 20; // below the Life Tap mana threshold (40); HP 100 > floor 50
            Assert.Equal("Life Tap", Fire(g)?.Name);
        }

        [Fact]
        public void Wands_when_very_low_on_mana()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 15; // below the wand threshold (20)
            Assert.Equal("Shoot", Fire(g)?.Name);
        }

        [Fact]
        public void Drain_Life_when_low_and_solo()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 30; // below 40, solo
            Assert.Equal("Drain Life", Fire(g)?.Name);
        }

        // --- pet (Auto = Imp for Destruction) ---

        [Fact]
        public void Summons_the_Imp_by_default_for_Destruction()
        {
            FakeGameClient g = LockGame(withPet: false);
            Assert.Equal("Pet summon", Fire(g)?.Name);
            Assert.Contains("Summon Imp", g.CastLog); // Auto → Imp
        }

        [Fact]
        public void Sends_the_pet_to_a_target_it_is_not_on()
        {
            FakeGameClient g = LockGame();
            g.PetUnit.TargetGuid = 0;
            Assert.Equal("Pet attack", Fire(g)?.Name);
            Assert.Contains(1ul, g.PetAttackLog);
        }

        // --- low-level / safety ---

        [Fact]
        public void Low_level_warlock_skips_unknown_spells_and_still_casts()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            g.TargetUnit = new FakeUnit { Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 30, HealthPercent = 100, IsAttackable = true };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.KnownSpells.Add("Shadow Bolt"); // only this is known
            Assert.Equal("Shadow Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void No_target_does_not_throw()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.WithAura("Fel Armor");
            Assert.Null(Record.Exception(() => Fire(g)));
        }
    }
}
