using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarlockAfflictionTests
    {
        private const long Fresh = 60000; // a DoT with plenty of duration left → upkeep slot stays quiet

        // An Affliction warlock at range on a full-health dummy, full mana and health, armor up, all DoTs
        // freshly applied (so the upkeep slots stay quiet), with an alive Voidwalker already on the target —
        // so each test isolates the rule it cares about. Pass withPet:false to test the summon.
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
            g.TargetUnit.WithAura("Haunt", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Curse of Agony", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Corruption", mine: true, timeLeftMs: Fresh);
            g.TargetUnit.WithAura("Unstable Affliction", mine: true, timeLeftMs: Fresh);
            if (withPet)
                g.PetUnit = new FakeUnit { Guid = 99, Name = "Voidwalker", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new WarlockSettings());

        private static RotationStep Fire(FakeGameClient g, WarlockSettings s) =>
            new RotationEngine(new SoloAffliction(s).BuildSteps()).Tick(CombatContext.Capture(g));

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

        [Fact]
        public void Shadow_Trance_proc_fires_an_instant_Shadow_Bolt_even_moving()
        {
            FakeGameClient g = LockGame();
            g.Moving = true;
            g.MeUnit.WithAura("Shadow Trance"); // Nightfall proc → instant Shadow Bolt
            Assert.Equal("Shadow Bolt", Fire(g)?.Name);
        }

        // --- DoT upkeep (priority order) ---

        [Fact]
        public void Applies_Haunt_when_missing()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Haunt");
            Assert.Equal("Haunt", Fire(g)?.Name);
        }

        [Fact]
        public void Applies_the_curse_after_Haunt()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Curse of Agony");
            Assert.Equal("Curse", Fire(g)?.Name);
            Assert.Contains("Curse of Agony", g.CastLog); // the chosen curse (Agony default)
        }

        [Fact]
        public void The_curse_follows_the_setting()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Curse of Agony");
            var s = new WarlockSettings();
            s.Curse.Value = "Doom";
            Assert.Equal("Curse", Fire(g, s)?.Name);
            Assert.Contains("Curse of Doom", g.CastLog);
        }

        [Fact]
        public void Maintains_Corruption()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Corruption");
            Assert.Equal("Corruption", Fire(g)?.Name);
        }

        [Fact]
        public void Maintains_Unstable_Affliction()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.Auras.Remove("Unstable Affliction");
            Assert.Equal("Unstable Affliction", Fire(g)?.Name);
        }

        [Fact]
        public void Refreshes_a_DoT_about_to_expire()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.WithAura("Corruption", mine: true, timeLeftMs: 500); // about to fall off
            Assert.Equal("Corruption", Fire(g)?.Name);
        }

        // --- Immolate vs Unstable Affliction (level gating) ---

        [Fact]
        public void Uses_Immolate_only_when_Unstable_Affliction_is_unknown()
        {
            FakeGameClient g = LockGame();
            g.UnknownSpells.Add("Unstable Affliction"); // low-level warlock without UA
            g.TargetUnit.Auras.Remove("Unstable Affliction");
            // Immolate becomes the cast-time DoT in UA's slot.
            Assert.Equal("Immolate", Fire(g)?.Name);
        }

        [Fact]
        public void Skips_Immolate_once_Unstable_Affliction_is_known()
        {
            FakeGameClient g = LockGame();
            // UA known + up; Immolate must NOT be cast (they share the slot).
            RotationStep fired = Fire(g);
            Assert.NotEqual("Immolate", fired?.Name);
        }

        // --- mana / sustain ---

        [Fact]
        public void Life_Tap_when_mana_low_and_health_high()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 20; // below the Life Tap mana threshold (40)
            // health 100 > floor 50
            Assert.Equal("Life Tap", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Life_Tap_when_health_is_too_low()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 20; // mana low...
            g.MeUnit.HealthPercent = 30; // ...but HP below the floor (50) → can't afford it
            Assert.NotEqual("Life Tap", Fire(g)?.Name);
        }

        [Fact]
        public void Maintains_the_Life_Tap_buff_with_the_glyph()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 100; // mana full → the mana tap is quiet
            var s = new WarlockSettings();
            s.GlyphLifeTap.Value = true; // glyph uptime tap enabled, buff missing
            Assert.Equal("Life Tap", Fire(g, s)?.Name);
        }

        [Fact]
        public void Wands_when_very_low_on_mana()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.PowerPercent = 15; // below the wand threshold (20)
            // Wand sits above Life Tap, so it wins when both could fire.
            Assert.Equal("Shoot", Fire(g)?.Name);
        }

        // --- self-heal ---

        [Fact]
        public void Drain_Life_when_low_and_solo()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 30; // below the Drain Life threshold (40), solo (party empty)
            Assert.Equal("Drain Life", Fire(g)?.Name);
        }

        [Fact]
        public void Drain_Life_holds_while_moving()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 30;
            g.Moving = true; // a channel can't start on the move
            Assert.NotEqual("Drain Life", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Drain_Life_in_a_group()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 30;
            g.PartyList.Add(g.MeUnit);
            g.PartyList.Add(new FakeUnit { Guid = 50, Name = "Friend", Reaction = Reaction.Friendly });
            Assert.NotEqual("Drain Life", Fire(g)?.Name); // a healer covers it in a group
        }

        // --- buffs ---

        [Fact]
        public void Keeps_the_armor_up()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.Auras.Remove("Fel Armor");
            Assert.Equal("Armor", Fire(g)?.Name);
            Assert.Contains("Fel Armor", g.CastLog); // Auto picks the best known (Fel Armor)
        }

        [Fact]
        public void Falls_back_to_Demon_Armor_when_Fel_Armor_is_unknown()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.Auras.Remove("Fel Armor");
            g.UnknownSpells.Add("Fel Armor");
            Assert.Equal("Armor", Fire(g)?.Name);
            Assert.Contains("Demon Armor", g.CastLog);
        }

        [Fact]
        public void Casts_Unending_Breath_out_of_combat_when_missing()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.Auras.Remove("Unending Breath"); // buff dropped; OOC + not mounted → re-apply it
            Assert.Equal("Unending Breath", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_cast_Unending_Breath_in_combat()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.Auras.Remove("Unending Breath");
            g.InCombatFlag = true; // mid-fight → don't waste a GCD on the breathing buff
            Assert.NotEqual("Unending Breath", Fire(g)?.Name);
        }

        // --- pet ---

        [Fact]
        public void Summons_the_voidwalker_when_petless_out_of_combat()
        {
            FakeGameClient g = LockGame(withPet: false);
            g.ItemCounts["Soul Shard"] = 1; // a shard to pay the Voidwalker summon (else it falls back to the Imp)
            Assert.Equal("Pet summon", Fire(g)?.Name);
            Assert.Contains("Summon Voidwalker", g.CastLog);
        }

        [Fact]
        public void Summons_the_chosen_demon()
        {
            FakeGameClient g = LockGame(withPet: false);
            g.ItemCounts["Soul Shard"] = 1; // a shard to pay the Felhunter summon
            var s = new WarlockSettings();
            s.Pet.Value = "Felhunter";
            Assert.Equal("Pet summon", Fire(g, s)?.Name);
            Assert.Contains("Summon Felhunter", g.CastLog);
        }

        [Fact]
        public void Sends_the_pet_to_a_target_it_is_not_on()
        {
            FakeGameClient g = LockGame();
            g.PetUnit.TargetGuid = 0; // pet not yet on our target
            Assert.Equal("Pet attack", Fire(g)?.Name);
            Assert.Contains(1ul, g.PetAttackLog);
        }

        [Fact]
        public void Pet_management_can_be_turned_off()
        {
            FakeGameClient g = LockGame(withPet: false);
            var s = new WarlockSettings();
            s.ManagePet.Value = false;
            RotationStep fired = Fire(g, s);
            Assert.NotEqual("Pet summon", fired?.Name);
            Assert.DoesNotContain("Summon Voidwalker", g.CastLog);
        }

        // --- low-level skip ---

        [Fact]
        public void Low_level_warlock_skips_unknown_spells_and_still_casts()
        {
            // A fresh warlock only knows Shadow Bolt — every DoT / proc / Life Tap / armor is unknown, so it
            // must skip them cleanly and fall through to the Shadow Bolt filler.
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

        [Fact]
        public void Uses_an_emergency_item_below_threshold()
        {
            FakeGameClient g = LockGame();
            g.MeUnit.HealthPercent = 20;
            g.ReadyItems.Add("Healthstone");
            Assert.Equal("Emergency heal", Fire(g)?.Name);
        }

        // --- let the DoTs finish a dying mob (stop overkilling with the filler) ---

        [Fact]
        public void Stops_nuking_when_the_DoTs_will_finish_a_low_mob()
        {
            FakeGameClient g = LockGame();   // full DoT coverage, mana/HP full
            g.TargetUnit.HealthPercent = 15; // below the default 20% floor
            Assert.NotEqual("Shadow Bolt", Fire(g)?.Name); // let the DoTs finish it — no filler nuke
        }

        [Fact]
        public void Holds_the_Shadow_Trance_proc_when_the_DoTs_will_finish_the_mob()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            g.MeUnit.WithAura("Shadow Trance"); // a proc we'd normally spend — save it for the next pull
            Assert.NotEqual("Shadow Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void Still_nukes_a_low_elite()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            g.TargetUnit.IsElite = true; // big HP pool → DoTs won't finish it → keep nuking
            Assert.Equal("Shadow Bolt", Fire(g)?.Name);
        }

        [Fact]
        public void DotsWillFinishTarget_true_for_a_low_normal_mob_with_coverage()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            Assert.True(WarlockCommon.DotsWillFinishTarget(CombatContext.Capture(g), new WarlockSettings()));
        }

        [Fact]
        public void DotsWillFinishTarget_false_with_only_one_dot()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            g.TargetUnit.Auras.Remove("Haunt");
            g.TargetUnit.Auras.Remove("Curse of Agony");
            g.TargetUnit.Auras.Remove("Unstable Affliction"); // only Corruption left → 1 DoT < 2
            Assert.False(WarlockCommon.DotsWillFinishTarget(CombatContext.Capture(g), new WarlockSettings()));
        }

        [Fact]
        public void DotsWillFinishTarget_false_when_the_dots_are_about_to_expire()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            // All but Corruption are about to fall off → they don't count toward finishing the mob.
            g.TargetUnit.WithAura("Haunt", mine: true, timeLeftMs: 500);
            g.TargetUnit.WithAura("Curse of Agony", mine: true, timeLeftMs: 500);
            g.TargetUnit.WithAura("Unstable Affliction", mine: true, timeLeftMs: 500);
            Assert.False(WarlockCommon.DotsWillFinishTarget(CombatContext.Capture(g), new WarlockSettings()));
        }

        [Fact]
        public void DotsWillFinishTarget_false_above_the_floor()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 25; // above the default 20% floor
            Assert.False(WarlockCommon.DotsWillFinishTarget(CombatContext.Capture(g), new WarlockSettings()));
        }

        [Fact]
        public void DotsWillFinishTarget_false_when_disabled()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            var s = new WarlockSettings();
            s.LetDotsFinishHealthPercent.Value = 0; // disabled → always nuke
            Assert.False(WarlockCommon.DotsWillFinishTarget(CombatContext.Capture(g), s));
        }

        [Fact]
        public void Does_not_re_apply_Corruption_when_the_DoTs_will_finish_the_mob()
        {
            // Dying-mob fix: at 15% HP with full DoT coverage, DotsWillFinishTarget is true, so the Corruption
            // maintain must NOT re-apply even though the aura is missing (Haunt/Curse/UA still cover the kill).
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 15;
            g.TargetUnit.Auras.Remove("Corruption"); // missing → would normally be re-applied
            // Don't let Drain Soul (the dying-mob harvest) confuse the assertion — stock shards so it stays quiet.
            g.ItemCounts["Soul Shard"] = 4;
            g.ItemCounts["Healthstone"] = 1;
            Assert.NotEqual("Corruption", Fire(g)?.Name);
            Assert.DoesNotContain("Corruption", g.CastLog);
        }

        [Fact]
        public void Still_re_applies_Corruption_above_the_finish_floor()
        {
            // Above the floor DotsWillFinishTarget is false, so the maintain works exactly as before (regression guard
            // that the new gate didn't break ordinary upkeep).
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 100;
            g.TargetUnit.Auras.Remove("Corruption");
            Assert.Equal("Corruption", Fire(g)?.Name);
        }

        // --- Soul Shard economy (Drain Soul harvest + Create Healthstone) ---

        [Fact]
        public void Drains_Soul_on_a_dying_mob_when_shards_are_low()
        {
            FakeGameClient g = LockGame();    // DoTs up, full health/mana
            g.TargetUnit.HealthPercent = 20;  // dying (≤ 25 default), no shards held → harvest one
            Assert.Equal("Drain Soul", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Drain_Soul_when_shards_are_stocked()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 20;
            g.ItemCounts["Soul Shard"] = 4;   // above the keep count (3) → no need to harvest
            g.ItemCounts["Healthstone"] = 1;  // and don't create one either (isolate the Drain Soul check)
            Assert.NotEqual("Drain Soul", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Drain_Soul_on_a_healthy_mob()
        {
            FakeGameClient g = LockGame();    // target at full health → not a harvest target
            Assert.NotEqual("Drain Soul", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_Drain_Soul_on_an_elite()
        {
            FakeGameClient g = LockGame();
            g.TargetUnit.HealthPercent = 20;
            g.TargetUnit.IsElite = true;      // a channel on a tough mob isn't worth it
            Assert.NotEqual("Drain Soul", Fire(g)?.Name);
        }

        [Fact]
        public void Creates_a_Healthstone_when_missing_and_a_shard_is_available()
        {
            FakeGameClient g = LockGame();
            g.ItemCounts["Soul Shard"] = 1;   // a shard to spend, no Healthstone in bags → create one (OOC)
            Assert.Equal("Create Healthstone", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_create_a_Healthstone_without_a_shard()
        {
            FakeGameClient g = LockGame();    // no shard → can't create (no reagent)
            Assert.NotEqual("Create Healthstone", Fire(g)?.Name);
        }

        [Fact]
        public void Does_not_create_a_Healthstone_when_one_is_already_in_the_bags()
        {
            FakeGameClient g = LockGame();
            g.ItemCounts["Soul Shard"] = 1;
            g.ItemCounts["Healthstone"] = 1;  // already stocked
            Assert.NotEqual("Create Healthstone", Fire(g)?.Name);
        }
    }
}
