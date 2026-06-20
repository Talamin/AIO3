using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarriorArmsTests
    {
        // Arms warrior in Battle stance, auto-attacking, upkeep neutralised. Overpower is
        // proc-gated in reality, so it is put on cooldown here to isolate the steady-state strike.
        private static FakeGameClient ArmsGame()
        {
            var game = new FakeGameClient
            {
                Class = WowClass.Warrior,
                StanceName = "Battle Stance",
                AutoAttacking = true
            };
            game.SpellsOnCooldown.Add("Bloodrage");
            game.SpellsOnCooldown.Add("Overpower");
            game.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100
            };
            game.EnemyList.Add(game.TargetUnit);
            game.MeUnit.WithAura("Battle Shout", mine: true);
            return game;
        }

        private static RotationStep Fire(FakeGameClient game) =>
            new RotationEngine(new SoloArms().BuildSteps()).Tick(CombatContext.Capture(game));

        [Fact]
        public void Switches_to_Battle_Stance_when_not_in_it()
        {
            FakeGameClient game = ArmsGame();
            game.StanceName = "Berserker Stance";

            Assert.Equal("Battle Stance", Fire(game)?.Name);
        }

        [Fact]
        public void Mortal_Strike_is_the_core_strike()
        {
            FakeGameClient game = ArmsGame();

            Assert.Equal("Mortal Strike", Fire(game)?.Name);
        }

        [Fact]
        public void Execute_fires_below_20_percent()
        {
            FakeGameClient game = ArmsGame();
            game.TargetUnit.HealthPercent = 15;

            Assert.Equal("Execute", Fire(game)?.Name);
        }

        [Fact]
        public void Overpower_fires_in_its_proc_window()
        {
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Remove("Overpower"); // proc available → off-cooldown
            game.SpellsOnCooldown.Add("Mortal Strike"); // Mortal Strike sits above Overpower; isolate the proc

            Assert.Equal("Overpower", Fire(game)?.Name);
        }

        [Fact]
        public void Mortal_Strike_wins_over_an_available_Overpower()
        {
            // Both off cooldown → the core strike (prio 7) is chosen ahead of the Overpower proc (prio 8).
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Remove("Overpower"); // proc available too

            Assert.Equal("Mortal Strike", Fire(game)?.Name);
        }

        [Fact]
        public void Rend_is_kept_up()
        {
            // Strikes on cooldown and Rend missing → the bleed is (re)applied as upkeep.
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Add("Mortal Strike");
            game.TargetUnit.CreatureType = "Humanoid"; // bleeds (not Elemental/Mechanical)

            Assert.Equal("Rend", Fire(game)?.Name);
        }

        [Fact]
        public void Slam_fills_with_spare_rage()
        {
            // Nothing else up, Rend already applied, plenty of rage → Slam is the filler.
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Add("Mortal Strike");
            game.SpellsOnCooldown.Add("Victory Rush");
            game.MeUnit.Rage = 50;
            game.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 99999);

            Assert.Equal("Slam", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_fires_on_an_elite_when_missing()
        {
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Add("Recklessness"); // elite would otherwise trigger the DPS cooldown first
            game.TargetUnit.IsElite = true;

            Assert.Equal("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_is_skipped_on_lone_trash()
        {
            // Single non-elite, non-boss below the AoE threshold → not worth a global.
            FakeGameClient game = ArmsGame();

            Assert.NotEqual("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Uses_Whirlwind_AoE_when_threshold_met()
        {
            // Two enemies (default AoeThreshold) → the Sweeping Strikes + Whirlwind cleave fires.
            // Strikes on cooldown so the AoE block is reached; Sweeping Strikes is off the GCD and
            // resolves first, leaving Whirlwind as the next AoE step.
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Add("Mortal Strike");
            game.SpellsOnCooldown.Add("Victory Rush");
            game.SpellsOnCooldown.Add("Sweeping Strikes");
            game.SpellsOnCooldown.Add("Bladestorm");
            game.MeUnit.Rage = 10; // below Slam's rage gate so it doesn't pre-empt the AoE block
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });
            game.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 99999);
            game.TargetUnit.WithAura("Demoralizing Shout"); // already debuffed → don't pre-empt with the shout

            Assert.Equal("Whirlwind", Fire(game)?.Name);
        }

        [Fact]
        public void AoE_threshold_setting_is_respected()
        {
            var fs = new WarriorSettings();
            fs.AoeThreshold.Value = 3; // require 3 enemies
            FakeGameClient game = ArmsGame();
            game.SpellsOnCooldown.Add("Mortal Strike");
            game.SpellsOnCooldown.Add("Victory Rush");
            game.MeUnit.Rage = 10; // below Slam's rage gate
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });
            game.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 99999);

            RotationStep fired = new RotationEngine(new SoloArms(fs).BuildSteps()).Tick(CombatContext.Capture(game));
            Assert.NotEqual("Sweeping Strikes", fired?.Name);
            Assert.NotEqual("Whirlwind", fired?.Name);
            Assert.NotEqual("Thunder Clap", fired?.Name);
        }
    }
}
