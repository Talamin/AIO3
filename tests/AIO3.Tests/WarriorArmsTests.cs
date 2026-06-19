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
            game.SpellsOnCooldown.Remove("Overpower"); // proc available → usable

            Assert.Equal("Overpower", Fire(game)?.Name);
        }
    }
}
