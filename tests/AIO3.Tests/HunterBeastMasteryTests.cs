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
    }
}
