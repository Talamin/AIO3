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
            g.MeUnit.WithAura("Unending Breath"); // underwater-breathing buff up, so the OOC self-cast stays quiet
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

        // --- Shadowburn (sub-20% execute; costs a shard, guarded by SoulShardKeep) ---

        [Fact]
        public void Shadowburn_executes_below_20_percent_with_shards_above_the_keep()
        {
            FakeGameClient g = LockGame();
            g.InCombatFlag = true;                 // execute happens in combat (else CreateHealthstone fires OOC)
            g.TargetUnit.HealthPercent = 15;       // execute window
            g.ItemCounts["Soul Shard"] = 4;        // above SoulShardKeep (3) → spending one is safe
            var s = NoBurst();
            s.UseRacials.Value = false;            // isolate Shadowburn from the racial bundle (2.5 band)
            // Above the fillers; fires even though the filler would otherwise be suppressed by "let DoTs finish".
            Assert.Equal("Shadowburn", Fire(g, s)?.Name);
            Assert.Contains("Shadowburn", g.CastLog);
        }

        [Fact]
        public void Shadowburn_beats_the_Chaos_Bolt_filler_in_the_execute_window()
        {
            FakeGameClient g = LockGame();
            g.InCombatFlag = true;
            g.TargetUnit.HealthPercent = 15;
            g.ItemCounts["Soul Shard"] = 4;
            var s = new WarlockSettings();
            s.UseConflagrate.Value = false; // Conflagrate (8f) would outrank Shadowburn (10f) while Immolate is up
            s.UseRacials.Value = false;     // isolate from the racial bundle (2.5 band)
            // Chaos Bolt is ON (12f) — Shadowburn (10f) still wins, and it fires even though DotsWillFinishTarget
            // would suppress the Chaos Bolt/Incinerate fillers on this dying mob.
            Assert.Equal("Shadowburn", Fire(g, s)?.Name);
        }

        [Fact]
        public void No_Shadowburn_without_shards_above_the_keep()
        {
            FakeGameClient g = LockGame();
            g.InCombatFlag = true;
            g.TargetUnit.HealthPercent = 15;
            g.ItemCounts["Soul Shard"] = 3; // only the keep amount → don't drain the pet/healthstone supply
            RotationStep fired = Fire(g, NoBurst());
            Assert.NotEqual("Shadowburn", fired?.Name);
            Assert.DoesNotContain("Shadowburn", g.CastLog);
        }

        [Fact]
        public void No_Shadowburn_above_20_percent()
        {
            FakeGameClient g = LockGame();
            g.InCombatFlag = true;
            g.TargetUnit.HealthPercent = 25; // outside the execute window
            g.ItemCounts["Soul Shard"] = 4;
            RotationStep fired = Fire(g, NoBurst());
            Assert.NotEqual("Shadowburn", fired?.Name);
            Assert.DoesNotContain("Shadowburn", g.CastLog);
        }

        [Fact]
        public void Shadowburn_toggle_off_stops_it()
        {
            FakeGameClient g = LockGame();
            g.InCombatFlag = true;
            g.TargetUnit.HealthPercent = 15;
            g.ItemCounts["Soul Shard"] = 4;
            var s = NoBurst();
            s.UseShadowburn.Value = false;
            Assert.NotEqual("Shadowburn", Fire(g, s)?.Name);
            Assert.DoesNotContain("Shadowburn", g.CastLog);
        }

        [Fact]
        public void Shadowburn_skips_cleanly_when_unlearned()
        {
            FakeGameClient g = LockGame();
            g.InCombatFlag = true;
            g.TargetUnit.HealthPercent = 15;
            g.ItemCounts["Soul Shard"] = 4;
            g.UnknownSpells.Add("Shadowburn"); // low-level destro without it yet
            Assert.NotEqual("Shadowburn", Fire(g, NoBurst())?.Name);
            Assert.DoesNotContain("Shadowburn", g.CastLog);
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
