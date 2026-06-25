using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarlockDemonologyTests
    {
        private const long Fresh = 60000; // a DoT/buff with plenty of duration left → its upkeep slot stays quiet

        // A Demonology warlock at range on a full-health dummy, full mana/health, armor up, curse + DoTs fresh,
        // Demonic Empowerment already up on an alive Felguard on the target — so each test isolates one rule.
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
            g.MeUnit.WithAura("Unending Breath"); // underwater-breathing buff up, so the OOC self-cast stays quiet
            // DoTs already up with plenty of time left → maintenance quiet, rotation falls to the filler.
            g.TargetUnit.WithAura("Curse of Agony", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Corruption", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Immolate", mine: true, timeLeftMs: Fresh);
            if (withPet)
            {
                g.PetUnit = new FakeUnit { Guid = 99, Name = "Felguard", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
                g.PetUnit.WithAura("Demonic Empowerment"); // spec buff already up
            }
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new WarlockSettings());

        private static RotationStep Fire(FakeGameClient g, WarlockSettings s) =>
            new RotationEngine(new SoloDemonology(s).BuildSteps()).Tick(CombatContext.Capture(g));

        // --- filler / steady state ---

        [Fact]
        public void Shadow_Bolt_is_the_filler()
        {
            Assert.Equal("Shadow Bolt", Fire(LockGame())?.Name);
        }

        [Fact]
        public void Shadow_Bolt_holds_while_moving()
        {
            FakeGameClient g = LockGame();
            g.Moving = true; // the filler is a cast-time spell
            Assert.NotEqual("Shadow Bolt", Fire(g)?.Name);
        }

        // --- Soul Fire proc spender ---

        [Fact]
        public void Soul_Fire_on_a_Decimation_proc_beats_the_filler()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.WithAura("Decimation"); // proc up → Soul Fire spends it
            Assert.Equal("Soul Fire", Fire(g)?.Name);
        }

        [Fact]
        public void Soul_Fire_stays_quiet_without_a_proc()
        {
            // No proc → Soul Fire must not fire; the filler wins.
            Assert.Equal("Shadow Bolt", Fire(LockGame())?.Name);
        }

        [Fact]
        public void Soul_Fire_can_be_disabled()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.WithAura("Molten Core"); // proc up...
            var s = new WarlockSettings();
            s.UseSoulFire.Value = false; // ...but the toggle is off
            Assert.NotEqual("Soul Fire", Fire(g, s)?.Name);
        }

        // --- Demonic Empowerment ---

        [Fact]
        public void Demonic_Empowerment_when_missing_on_the_pet()
        {
            FakeGameClient g = LockGame();
            g.PetUnit.Auras.Remove("Demonic Empowerment");
            Assert.Equal("Demonic Empowerment", Fire(g)?.Name);
        }

        [Fact]
        public void No_Demonic_Empowerment_when_petless()
        {
            FakeGameClient g = LockGame(withPet: false);
            // Petless → the pet-targeted buff has no target and is skipped; the summon comes first anyway.
            RotationStep fired = Fire(g);
            Assert.NotEqual("Demonic Empowerment", fired?.Name);
        }

        [Fact]
        public void Demonic_Empowerment_can_be_disabled()
        {
            FakeGameClient g = LockGame();
            g.PetUnit.Auras.Remove("Demonic Empowerment");
            var s = new WarlockSettings();
            s.DemonicEmpowerment.Value = false;
            Assert.NotEqual("Demonic Empowerment", Fire(g, s)?.Name);
        }

        // --- DoT upkeep (priority order) ---

        [Fact]
        public void Applies_the_curse()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Curse of Agony");
            Assert.Equal("Curse", Fire(g)?.Name);
            Assert.Contains("Curse of Agony", g.CastLog);
        }

        [Fact]
        public void Maintains_Corruption()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Corruption");
            Assert.Equal("Corruption", Fire(g)?.Name);
        }

        [Fact]
        public void Maintains_Immolate()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Immolate");
            Assert.Equal("Immolate", Fire(g)?.Name);
        }

        [Fact]
        public void Immolate_holds_while_moving()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Immolate");
            g.Moving = true; // Immolate is a cast-time DoT
            Assert.NotEqual("Immolate", Fire(g)?.Name);
        }

        // --- order: curse before Corruption before Immolate ---

        [Fact]
        public void Curse_beats_Corruption_and_Immolate_when_several_are_down()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Curse of Agony");
            g.TargetUnit.Auras.Remove("Corruption");
            g.TargetUnit.Auras.Remove("Immolate");
            Assert.Equal("Curse", Fire(g)?.Name);
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
        public void Does_not_Life_Tap_when_health_is_too_low()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 20;
            g.MeUnit.HealthPercent = 30; // below the floor (50) → can't afford it
            Assert.NotEqual("Life Tap", Fire(g)?.Name);
        }

        [Fact]
        public void Wands_when_very_low_on_mana()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 15; // below the wand threshold (20)
            Assert.Equal("Shoot", Fire(g)?.Name);
        }

        // --- self-heal ---

        [Fact]
        public void Drain_Life_when_low_and_solo()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 30; // below 40, solo
            Assert.Equal("Drain Life", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Drain_Life_in_a_group()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 30;
            g.PartyList.Add(g.MeUnit);
            g.PartyList.Add(new FakeUnit { Guid = 50, Name = "Friend", Reaction = Reaction.Friendly });
            Assert.NotEqual("Drain Life", Fire(g)?.Name);
        }

        // --- pet (Auto = Felguard for Demonology) ---

        [Fact]
        public void Summons_the_Felguard_by_default_for_Demonology()
        {
            FakeGameClient g = LockGame(withPet: false);
            g.ItemCounts["Soul Shard"] = 1; // a shard to pay the Felguard summon (else it falls back to the Imp)
            Assert.Equal("Pet summon", Fire(g)?.Name);
            Assert.Contains("Summon Felguard", g.CastLog); // Auto → Felguard
        }

        [Fact]
        public void Falls_back_to_Voidwalker_when_the_Felguard_is_unlearned()
        {
            FakeGameClient g = LockGame(withPet: false);
            g.ItemCounts["Soul Shard"] = 1;          // a shard to pay the Voidwalker summon
            g.UnknownSpells.Add("Summon Felguard");  // low-level demo without the Felguard yet
            Assert.Equal("Pet summon", Fire(g)?.Name);
            Assert.Contains("Summon Voidwalker", g.CastLog);
        }

        [Fact]
        public void Manual_pet_choice_overrides_Auto()
        {
            FakeGameClient g = LockGame(withPet: false);
            g.ItemCounts["Soul Shard"] = 1; // a shard to pay the Succubus summon
            var s = new WarlockSettings();
            s.Pet.Value = "Succubus";
            Assert.Equal("Pet summon", Fire(g, s)?.Name);
            Assert.Contains("Summon Succubus", g.CastLog);
        }

        [Fact]
        public void Sends_the_pet_to_a_target_it_is_not_on()
        {
            FakeGameClient g = LockGame();
            g.PetUnit.TargetGuid = 0;
            Assert.Equal("Pet attack", Fire(g)?.Name);
            Assert.Contains(1ul, g.PetAttackLog);
        }

        // --- low-level skip ---

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
