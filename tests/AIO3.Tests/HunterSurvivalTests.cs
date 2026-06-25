using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Hunter;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class HunterSurvivalTests
    {
        // Survival hunter, steady state at range: pet on the target, aspect up, Auto Shot running,
        // Hunter's Mark + Serpent Sting already applied so the rotation reaches the signature shots.
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Hunter };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 28, HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.WithAura("Aspect of the Dragonhawk");
            g.CurrentSpells.Add("Auto Shot");
            g.PetUnit = new FakeUnit { Guid = 99, Name = "Pet", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            // Fresh duration so the maintain (now via MaintainMyDebuff with a 1500ms refresh window) stays quiet.
            g.TargetUnit.WithAura("Serpent Sting", mine: true, timeLeftMs: 15000);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloSurvival(new HunterSettings()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Kill_Command_fires_with_a_pet()
        {
            Assert.Equal("Kill Command", Fire(Game())?.Name);
        }

        [Fact]
        public void Explosive_Shot_when_kill_command_is_down()
        {
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Kill Command");
            Assert.Equal("Explosive Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Black_Arrow_after_explosive_shot()
        {
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Kill Command");
            g.SpellsOnCooldown.Add("Explosive Shot");
            Assert.Equal("Black Arrow", Fire(g)?.Name);
        }
    }
}
