using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Hunter;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class HunterMarksmanshipTests
    {
        // MM hunter, steady state at range: pet on the target, aspect + Trueshot Aura up, Auto Shot running,
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
            g.MeUnit.WithAura("Trueshot Aura");
            g.CurrentSpells.Add("Auto Shot");
            g.PetUnit = new FakeUnit { Guid = 99, Name = "Pet", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            // Fresh duration so the maintain (now via MaintainMyDebuff with a 1500ms refresh window) stays quiet.
            g.TargetUnit.WithAura("Serpent Sting", mine: true, timeLeftMs: 15000);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloMarksmanship(new HunterSettings()).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Chimera_Shot_is_the_signature_with_serpent_sting_up()
        {
            Assert.Equal("Chimera Shot", Fire(Game())?.Name);
        }

        [Fact]
        public void Aimed_Shot_when_chimera_is_down()
        {
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Chimera Shot");
            Assert.Equal("Aimed Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Silencing_Shot_interrupts_a_cast()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCasting = true;
            Assert.Equal("Silencing Shot", Fire(g)?.Name);
        }
    }
}
