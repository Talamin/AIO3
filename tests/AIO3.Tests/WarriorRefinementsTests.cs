using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // Tests the shared WarriorCommon blocks in isolation (one step per engine).
    public class WarriorRefinementsTests
    {
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Warrior };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100,
                IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g, RotationStep step) =>
            new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g));

        [Fact]
        public void Hamstring_fires_on_a_fleeing_humanoid()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 30;
            g.TargetUnit.CreatureType = "Humanoid";

            Assert.Equal("Hamstring", Fire(g, WarriorCommon.Hamstring(new WarriorSettings(), 1f))?.Name);
        }

        [Fact]
        public void Hamstring_skips_non_humanoids()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 30;
            g.TargetUnit.CreatureType = "Beast";

            Assert.Null(Fire(g, WarriorCommon.Hamstring(new WarriorSettings(), 1f)));
        }

        [Fact]
        public void Hamstring_skips_bosses()
        {
            FakeGameClient g = Game();
            g.TargetUnit.HealthPercent = 30;
            g.TargetUnit.CreatureType = "Humanoid";
            g.TargetUnit.Entry = 31146; // a boss entry

            Assert.Null(Fire(g, WarriorCommon.Hamstring(new WarriorSettings(), 1f)));
        }

        [Fact]
        public void Rend_skips_bleed_immune_creatures()
        {
            FakeGameClient g = Game();

            g.TargetUnit.CreatureType = "Elemental";
            Assert.Null(Fire(g, WarriorCommon.Rend(1f)));

            g.TargetUnit.CreatureType = "Beast";
            Assert.Equal("Rend", Fire(g, WarriorCommon.Rend(1f))?.Name);
        }

        [Fact]
        public void Rend_refreshes_before_expiry_but_not_while_fresh()
        {
            FakeGameClient g = Game();
            g.TargetUnit.CreatureType = "Beast";

            // Fresh (plenty of time left) → don't refresh.
            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 8000);
            Assert.Null(Fire(g, WarriorCommon.Rend(1f)));

            // About to expire → refresh.
            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 1500);
            Assert.Equal("Rend", Fire(g, WarriorCommon.Rend(1f))?.Name);
        }

        [Fact]
        public void Heroic_Strike_is_not_requeued_while_already_pending()
        {
            FakeGameClient g = Game();
            g.MeUnit.Rage = 50;
            RotationStep hs = WarriorCommon.HeroicStrike(new WarriorSettings(), 1f);

            Assert.Equal("Heroic Strike", Fire(g, hs)?.Name);

            g.CurrentSpells.Add("Heroic Strike"); // already queued on next swing
            Assert.Null(Fire(g, hs));
        }

        [Fact]
        public void MaintainMyDebuff_casts_when_missing_or_expiring_but_not_while_fresh()
        {
            FakeGameClient g = Game();
            RotationStep step = CombatBlocks.MaintainMyDebuff("Rend", minMsLeft: 3000, priority: 1f);

            Assert.Equal("Rend", Fire(g, step)?.Name); // missing

            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 1000);
            Assert.Equal("Rend", Fire(g, step)?.Name); // expiring

            g.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 9000);
            Assert.Null(Fire(g, step)); // fresh
        }

        [Fact]
        public void Berserker_Rage_breaks_fear()
        {
            FakeGameClient g = Game();

            Assert.Null(Fire(g, WarriorCommon.BerserkerRage(1f)));

            g.MeUnit.WithAura("Fear");
            Assert.Equal("Berserker Rage", Fire(g, WarriorCommon.BerserkerRage(1f))?.Name);
        }
    }
}
