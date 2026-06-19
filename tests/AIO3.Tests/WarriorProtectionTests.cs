using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarriorProtectionTests
    {
        // Protection warrior in Defensive stance, auto-attacking, upkeep neutralised. Shield Block is
        // an off-GCD mitigation that would otherwise win; put on cooldown to isolate threat abilities.
        private static FakeGameClient ProtGame()
        {
            var game = new FakeGameClient
            {
                Class = WowClass.Warrior,
                StanceName = "Defensive Stance",
                AutoAttacking = true
            };
            game.SpellsOnCooldown.Add("Bloodrage");
            game.SpellsOnCooldown.Add("Shield Block");
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
            new RotationEngine(new SoloProtection().BuildSteps()).Tick(CombatContext.Capture(game));

        [Fact]
        public void Switches_to_Defensive_Stance_when_not_in_it()
        {
            FakeGameClient game = ProtGame();
            game.StanceName = "Battle Stance";

            Assert.Equal("Defensive Stance", Fire(game)?.Name);
        }

        [Fact]
        public void Shield_Slam_is_the_threat_core()
        {
            FakeGameClient game = ProtGame();

            Assert.Equal("Shield Slam", Fire(game)?.Name);
        }

        [Fact]
        public void Last_Stand_fires_when_critically_low()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.HealthPercent = 10;

            Assert.Equal("Last Stand", Fire(game)?.Name);
        }

        [Fact]
        public void Shield_Wall_fires_when_low()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.HealthPercent = 30; // below 35, above Last Stand's 15

            Assert.Equal("Shield Wall", Fire(game)?.Name);
        }
    }
}
