using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Hunter;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class HunterBeastMasteryTests
    {
        // A BM hunter at range on a full-health dummy, full mana, aspect up and Auto Shot already running,
        // with an alive pet that is already on the target — so the upkeep slots stay quiet and each test
        // isolates the rule it cares about.
        private static FakeGameClient HunterGame(bool withPet = true)
        {
            var g = new FakeGameClient { Class = WowClass.Hunter };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 28,
                HealthPercent = 100,
                IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;                   // full mana → damage aspect
            g.MeUnit.WithAura("Aspect of the Dragonhawk"); // all spells known → Dragonhawk is the resolved aspect
            g.MeUnit.WithAura("Trueshot Aura");            // shared AP buff up → the upkeep step stays idle
            g.CurrentSpells.Add("Auto Shot");              // already shooting → Auto Shot step idle
            if (withPet)
                g.PetUnit = new FakeUnit { Guid = 99, Name = "Pet", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            return g;
        }

        // Hunter's Mark + Serpent Sting already up so the rotation reaches the shots.
        private static FakeGameClient Marked(FakeGameClient g)
        {
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            // Fresh duration so the maintain (now via MaintainMyDebuff with a 1500ms refresh window) stays quiet.
            g.TargetUnit.WithAura("Serpent Sting", mine: true, timeLeftMs: 15000);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new HunterSettings());

        private static RotationStep Fire(FakeGameClient g, HunterSettings s) =>
            new RotationEngine(new SoloBeastMastery(s).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Kill_Command_is_the_pet_filler()
        {
            FakeGameClient g = Marked(HunterGame());
            Assert.Equal("Kill Command", Fire(g)?.Name);
        }

        [Fact]
        public void Arcane_Shot_when_kill_command_is_down()
        {
            FakeGameClient g = Marked(HunterGame());
            g.SpellsOnCooldown.Add("Kill Command");
            Assert.Equal("Arcane Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Steady_Shot_is_the_stationary_filler()
        {
            FakeGameClient g = Marked(HunterGame());
            g.SpellsOnCooldown.Add("Kill Command");
            g.SpellsOnCooldown.Add("Arcane Shot");
            Assert.Equal("Steady Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Kill_Shot_executes_low_targets()
        {
            FakeGameClient g = Marked(HunterGame());
            g.TargetUnit.HealthPercent = 15;
            Assert.Equal("Kill Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Applies_Hunters_Mark_when_missing()
        {
            FakeGameClient g = HunterGame(); // not marked
            Assert.Equal("Hunter's Mark", Fire(g)?.Name);
        }

        [Fact]
        public void Applies_Serpent_Sting_after_the_mark()
        {
            FakeGameClient g = HunterGame();
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            Assert.Equal("Serpent Sting", Fire(g)?.Name);
        }

        [Fact]
        public void Serpent_Sting_skips_a_normal_mob_below_70_percent()
        {
            // Dying-mob fix (a): the floor was raised from 30 to 70 for normal mobs — a fresh 15s DoT is wasted on a
            // mob with seconds to live. Marked, Serpent Sting missing, but below the 70% floor → it does NOT apply.
            FakeGameClient g = HunterGame();
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            g.TargetUnit.HealthPercent = HunterCommon.SerpentStingMinTargetHealth - 1; // 69%
            Assert.NotEqual("Serpent Sting", Fire(g)?.Name);
            Assert.DoesNotContain("Serpent Sting", g.CastLog);
        }

        [Fact]
        public void Serpent_Sting_still_applies_to_a_low_elite()
        {
            // Dying-mob fix (a): on an elite the floor relaxes to 20% (a long fight outlives the DoT) — so at 50% the
            // normal floor would have skipped, but the elite floor keeps it stinging.
            FakeGameClient g = HunterGame();
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            g.TargetUnit.IsElite = true;
            g.TargetUnit.HealthPercent = 50; // below 70 (normal floor) but above 20 (elite floor)
            var s = new HunterSettings();
            s.UseCooldowns.Value = false; // Bestial Wrath / Rapid Fire fire on an elite; isolate the sting
            Assert.Equal("Serpent Sting", Fire(g, s)?.Name);
        }

        [Fact]
        public void Serpent_Sting_skips_an_elite_below_20_percent()
        {
            FakeGameClient g = HunterGame();
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            g.TargetUnit.IsElite = true;
            g.TargetUnit.HealthPercent = HunterCommon.SerpentStingMinEliteHealth - 1; // 19%
            Assert.NotEqual("Serpent Sting", Fire(g)?.Name);
            Assert.DoesNotContain("Serpent Sting", g.CastLog);
        }

        [Fact]
        public void Serpent_Sting_does_not_double_apply_in_the_apply_window()
        {
            // Dying-mob fix (b): routing through MaintainMyDebuff added a post-cast grace, so the apply-latency
            // double-cast (the documented Immolate/Corruption bug) can't happen — a second tick in the window is
            // suppressed even though the fake never lands the aura.
            FakeGameClient g = HunterGame();
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            var engine = new RotationEngine(new SoloBeastMastery().BuildSteps());
            CombatContext ctx = CombatContext.Capture(g);

            Assert.Equal("Serpent Sting", engine.Tick(ctx)?.Name); // first application
            Assert.NotEqual("Serpent Sting", engine.Tick(ctx)?.Name); // within the grace → no second cast
            Assert.Single(g.CastLog.FindAll(c => c == "Serpent Sting"));
        }

        [Fact]
        public void Swaps_to_Aspect_of_the_Viper_when_low_on_mana()
        {
            FakeGameClient g = HunterGame();
            g.MeUnit.PowerPercent = 10; // below the Viper threshold (20)

            Assert.Equal("Aspect", Fire(g)?.Name);
            Assert.Contains("Aspect of the Viper", g.CastLog);
        }

        [Fact]
        public void Growl_pulls_a_mob_back_onto_the_pet()
        {
            FakeGameClient g = Marked(HunterGame());
            g.PetAbilities.Add("Growl");
            g.TargetUnit.IsTargetingMe = true; // a mob slipped onto us

            Assert.Equal("Pet taunt", Fire(g)?.Name);
            Assert.Contains("Growl", g.PetCastLog);
        }

        [Fact]
        public void Sends_the_pet_to_a_target_it_is_not_on()
        {
            FakeGameClient g = Marked(HunterGame());
            g.PetUnit.TargetGuid = 0; // pet not yet on our target

            Assert.Equal("Pet attack", Fire(g)?.Name);
            Assert.Contains(1ul, g.PetAttackLog);
        }

        [Fact]
        public void Backpedal_steps_back_when_a_melee_mob_is_on_the_pet()
        {
            FakeGameClient g = Marked(HunterGame());
            g.InCombatFlag = true;
            g.TargetUnit.Distance = 3;             // too close for ranged
            g.TargetUnit.IsTargetingMyPet = true;  // but the pet is tanking it → safe to back up

            Assert.Equal("Backpedal", Fire(g)?.Name);
            Assert.Contains(7f, g.StepBackLog);
        }

        [Fact]
        public void Backpedal_does_not_fire_when_the_mob_is_on_us()
        {
            FakeGameClient g = Marked(HunterGame());
            g.InCombatFlag = true;
            g.TargetUnit.Distance = 3;
            g.TargetUnit.IsTargetingMe = true; // on us, not the pet → backing up wouldn't shake it

            Assert.NotEqual("Backpedal", Fire(g)?.Name);
        }

        [Fact]
        public void Backpedal_refuses_over_a_cliff_and_falls_through()
        {
            FakeGameClient g = Marked(HunterGame());
            g.InCombatFlag = true;
            g.TargetUnit.Distance = 3;
            g.TargetUnit.IsTargetingMyPet = true;
            g.StepBackResult = false; // adapter found no safe spot (ledge) → must refuse

            Assert.NotEqual("Backpedal", Fire(g)?.Name); // doesn't claim the move...
            Assert.Contains(7f, g.StepBackLog);          // ...but it did ask (and was refused)
        }

        [Fact]
        public void Backpedal_respects_its_toggle()
        {
            FakeGameClient g = Marked(HunterGame());
            g.InCombatFlag = true;
            g.TargetUnit.Distance = 3;
            g.TargetUnit.IsTargetingMyPet = true;
            var s = new HunterSettings();
            s.UseBackpedal.Value = false;

            Assert.NotEqual("Backpedal", Fire(g, s)?.Name);
        }

        [Fact]
        public void Uses_a_ready_pet_cooldown_in_combat()
        {
            FakeGameClient g = Marked(HunterGame());
            g.InCombatFlag = true;
            g.PetAbilities.Add("Furious Howl"); // on the bar and ready

            Assert.Equal("Pet Furious Howl", Fire(g)?.Name);
            Assert.Contains("Furious Howl", g.PetCastLog);
        }

        [Fact]
        public void No_target_does_not_throw()
        {
            // With Bestial Wrath / Rapid Fire known (level 50+), the Self-cast cooldown steps are evaluated
            // every tick even with no target — they must guard ctx.Target before touching it.
            var g = new FakeGameClient { Class = WowClass.Hunter };
            g.PetUnit = new FakeUnit { Guid = 99, Name = "Pet", IsAlive = true, HealthPercent = 100 };
            g.MeUnit.PowerPercent = 100;
            // No TargetUnit, no enemies.

            Assert.Null(Record.Exception(() => Fire(g)));
        }

        [Fact]
        public void Petless_still_shoots_and_skips_every_pet_step()
        {
            FakeGameClient g = Marked(HunterGame(withPet: false));
            g.InCombatFlag = true; // so the out-of-combat Call Pet doesn't fire
            var s = new HunterSettings();
            s.UseRacials.Value = false; // keep racials out of the way of the steady-state shot

            Assert.Equal("Arcane Shot", Fire(g, s)?.Name);
            Assert.DoesNotContain("Kill Command", g.CastLog); // pet abilities are gated out when petless
        }

        [Fact]
        public void Trueshot_Aura_is_kept_up_on_beast_mastery()
        {
            // S3: Trueshot Aura is now applied on BM too (not just MM). A BM hunter that learned it keeps it up.
            FakeGameClient g = Marked(HunterGame());
            g.MeUnit.Auras.Remove("Trueshot Aura"); // it dropped → BM should re-apply it
            Assert.False(g.MeUnit.HasAura("Trueshot Aura"));
            Assert.Equal("Trueshot Aura", Fire(g)?.Name);
            Assert.Contains("Trueshot Aura", g.CastLog);
        }

        [Fact]
        public void Volley_is_BM_primary_AoE_on_a_pack()
        {
            // BM1: Volley is BM's primary grind-AoE (channelled), ranked above Kill Shot / Multi-Shot.
            // Positional model (X1): the pack is measured around the TARGET, not the player. Place the target
            // and the adds at the same spot (~28yd downrange of the player at the origin) so they cluster.
            FakeGameClient g = Marked(HunterGame());
            g.MeUnit.WithAura("Trueshot Aura"); // keep the upkeep band quiet
            g.TargetUnit.X = 28; g.TargetUnit.Y = 0; // the target stands 28yd downrange of the player
            for (ulong i = 2; i <= 4; i++) // pack of 4 within 10yd of the target (>= AoE threshold 3)
                g.EnemyList.Add(new FakeUnit { Guid = i, Distance = 28, X = 29, Y = 0, IsAttackable = true, Reaction = Reaction.Hostile });
            var s = new HunterSettings();
            s.UseCooldowns.Value = false; // Bestial Wrath / Rapid Fire fire on a pack; isolate the AoE shot

            Assert.Equal("Volley", Fire(g, s)?.Name);
        }

        [Fact]
        public void Volley_holds_while_moving()
        {
            FakeGameClient g = Marked(HunterGame());
            g.MeUnit.WithAura("Trueshot Aura");
            g.TargetUnit.X = 28; g.TargetUnit.Y = 0;
            for (ulong i = 2; i <= 4; i++)
                g.EnemyList.Add(new FakeUnit { Guid = i, Distance = 28, X = 29, Y = 0, IsAttackable = true, Reaction = Reaction.Hostile });
            g.Moving = true; // channelled → can't start on the move
            var s = new HunterSettings();
            s.UseCooldowns.Value = false;

            Assert.NotEqual("Volley", Fire(g, s)?.Name);
        }

        [Fact]
        public void Volley_fires_on_a_distant_pack_the_old_player_relative_gate_would_have_missed()
        {
            // X1: the regression the seam fixes. A ranged BM hunter stands ~28yd back; the pack is clustered on
            // the distant target. Every add is FAR from the player (player-relative Distance = 28 > AoeRadius 10),
            // so the old EnemiesWithin(AoeRadius) gate counted ZERO and Volley/Multi-Shot never fired. The new
            // target-relative gate counts the cluster around the target and triggers the AoE.
            FakeGameClient g = Marked(HunterGame());
            g.MeUnit.WithAura("Trueshot Aura");
            g.TargetUnit.X = 28; g.TargetUnit.Y = 0; // 28yd downrange
            for (ulong i = 2; i <= 4; i++) // adds packed on the target, all 28yd from the player
                g.EnemyList.Add(new FakeUnit { Guid = i, Distance = 28, X = 30, Y = 1, IsAttackable = true, Reaction = Reaction.Hostile });
            var s = new HunterSettings();
            s.UseCooldowns.Value = false;

            // Sanity: the old player-relative count is zero (no add within AoeRadius of the player) ...
            Assert.Equal(0, CombatContext.Capture(g).EnemiesWithin(HunterCommon.AoeRadius));
            // ... yet the target-relative count sees the full pack, so Volley fires.
            Assert.True(CombatContext.Capture(g).EnemiesNearTarget(HunterCommon.AoeRadius) >= s.AoeThreshold.Value);
            Assert.Equal("Volley", Fire(g, s)?.Name);
        }

        [Fact]
        public void Multi_Shot_does_not_fire_on_an_add_near_the_player_but_far_from_the_target()
        {
            // X1 (negative): adds hugging the player but nowhere near the distant target are NOT a pack for a
            // target-anchored AoE. The old player-relative gate would have counted them and tripped the AoE; the
            // new gate (correctly) sees only the target itself near the target, so the AoE band stays quiet.
            FakeGameClient g = Marked(HunterGame());
            g.MeUnit.WithAura("Trueshot Aura");
            g.SpellsOnCooldown.Add("Kill Command"); // clear the lead filler so we'd reach the AoE band if it tripped
            g.TargetUnit.X = 28; g.TargetUnit.Y = 0;
            // Three adds right on top of the player (Distance ~2) but 28yd from the target — under the OLD gate
            // this is a "pack of 3 near me" and would have tripped the default AoE threshold (3).
            for (ulong i = 5; i <= 7; i++)
                g.EnemyList.Add(new FakeUnit { Guid = i, Distance = 2, X = 0, Y = 0, IsAttackable = true, Reaction = Reaction.Hostile });
            var s = new HunterSettings(); // default AoE threshold (3)
            s.UseCooldowns.Value = false;

            // Old gate would have seen 3 near the player; the new gate sees only the target itself (1) near the target.
            Assert.True(CombatContext.Capture(g).EnemiesWithin(HunterCommon.AoeRadius) >= s.AoeThreshold.Value);
            Assert.Equal(1, CombatContext.Capture(g).EnemiesNearTarget(HunterCommon.AoeRadius));
            Assert.NotEqual("Multi-Shot", Fire(g, s)?.Name);
            Assert.NotEqual("Volley", Fire(g, s)?.Name);
        }

        [Fact]
        public void Aspect_returns_to_Hawk_only_above_the_raised_default()
        {
            // S4: the Hawk-return default was raised from 30 to 55 so the hunter doesn't leave Viper while nearly
            // OOM. Sitting in Viper at 40% mana stays in Viper (40 < 55); it only returns above 55.
            var s = new HunterSettings();
            Assert.Equal(55, s.AspectHawkManaPercent.Value);

            FakeGameClient g = HunterGame();
            g.MeUnit.Auras.Remove("Aspect of the Dragonhawk");
            g.MeUnit.WithAura("Aspect of the Viper"); // currently regenerating
            g.MeUnit.PowerPercent = 40;               // recovered past the OLD 30 default but not the new 55

            // In the hysteresis band → keeps Viper, does NOT swap back to the damage aspect yet.
            Assert.DoesNotContain("Aspect of the Dragonhawk", g.CastLog);
            RotationStep step = Fire(g, s);
            Assert.True(step == null || step.Name != "Aspect" || !g.CastLog.Contains("Aspect of the Dragonhawk"));

            // Once mana genuinely recovers past 55, it returns to the damage aspect.
            g.MeUnit.PowerPercent = 60;
            Assert.Equal("Aspect", Fire(g, s)?.Name);
            Assert.Contains("Aspect of the Dragonhawk", g.CastLog);
        }
    }
}
