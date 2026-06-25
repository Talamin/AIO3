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

        [Fact]
        public void Viper_Sting_drains_a_caster_mob_when_low_on_mana()
        {
            // MM1: Viper Sting is MM's mana sustain vs caster mobs — fires when the target is a caster and our
            // mana is at/below the threshold (default 45). It outranks Serpent Sting / Chimera here.
            FakeGameClient g = Game();
            g.TargetUnit.IsCaster = true;
            g.MeUnit.PowerPercent = 40; // at/below the 45 threshold
            Assert.Equal("Viper Sting", Fire(g)?.Name);
        }

        [Fact]
        public void Viper_Sting_is_skipped_on_a_non_caster()
        {
            // The mana drain is pointless against a melee mob (no mana pool) → never fires; the rotation
            // continues to its normal signature shot.
            FakeGameClient g = Game();
            g.TargetUnit.IsCaster = false;
            g.MeUnit.PowerPercent = 40;
            Assert.NotEqual("Viper Sting", Fire(g)?.Name);
            Assert.DoesNotContain("Viper Sting", g.CastLog);
        }

        [Fact]
        public void Viper_Sting_is_skipped_with_full_mana()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCaster = true;
            g.MeUnit.PowerPercent = 100; // above the threshold → no need to drain
            Assert.NotEqual("Viper Sting", Fire(g)?.Name);
        }

        [Fact]
        public void Viper_Sting_is_not_reapplied_while_up()
        {
            FakeGameClient g = Game();
            g.TargetUnit.IsCaster = true;
            g.MeUnit.PowerPercent = 40;
            g.TargetUnit.WithAura("Viper Sting", mine: true);
            Assert.NotEqual("Viper Sting", Fire(g)?.Name);
        }

        [Fact]
        public void Chimera_fires_with_only_Viper_Sting_up()
        {
            // The Chimera gate was widened from "Serpent Sting up" to "Serpent OR Viper Sting up" (they're
            // mutually exclusive), so a Viper turn on a caster doesn't stall the signature nuke.
            FakeGameClient g = Game();
            g.TargetUnit.Auras.Remove("Serpent Sting");       // Serpent down...
            g.TargetUnit.WithAura("Viper Sting", mine: true); // ...but Viper up (mutually exclusive)
            // Drop the target below the Serpent-Sting HP floor so the Serpent maintain step stays quiet and we
            // genuinely test the widened Chimera gate (not just "Serpent re-applies first").
            g.TargetUnit.HealthPercent = HunterCommon.SerpentStingMinTargetHealth - 1; // 69%
            Assert.Equal("Chimera Shot", Fire(g)?.Name);
        }
    }
}
