using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class BestDamageTests
    {
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Warrior };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Reaction = Reaction.Hostile,
                IsAttackable = true,
                HealthPercent = 100,
                Distance = 5
            };
            g.EnemyList.Add(g.TargetUnit);
            return g;
        }

        private static string Fire(FakeGameClient g, DamageTracker dmg, RotationStep step)
        {
            g.CastLog.Clear();
            new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g, null, dmg));
            return g.CastLog.LastOrDefault();
        }

        private static void Seed(DamageTracker t, string ability, int hits, long perHit)
        {
            for (int i = 0; i < hits; i++) t.Record(ability, perHit);
        }

        [Fact]
        public void Off_uses_the_hand_order()
        {
            var dmg = new DamageTracker();
            Seed(dmg, "Revenge", 10, 999); // even though Revenge hits harder...
            var step = CombatBlocks.BestDamage(1f, ctx => false, "Shield Slam", "Revenge");

            Assert.Equal("Shield Slam", Fire(Game(), dmg, step)); // ...learning off → first in hand order
        }

        [Fact]
        public void On_with_data_casts_the_higher_damage_strike()
        {
            var dmg = new DamageTracker();
            Seed(dmg, "Shield Slam", 10, 100);
            Seed(dmg, "Revenge", 10, 300);
            var step = CombatBlocks.BestDamage(1f, ctx => true, "Shield Slam", "Revenge");

            Assert.Equal("Revenge", Fire(Game(), dmg, step));
        }

        [Fact]
        public void On_explores_an_unmeasured_candidate_first()
        {
            var dmg = new DamageTracker();
            Seed(dmg, "Shield Slam", 10, 500); // well measured and strong...
            // ...but Revenge has no data yet, so it is explored first to get measured.
            var step = CombatBlocks.BestDamage(1f, ctx => true, "Shield Slam", "Revenge");

            Assert.Equal("Revenge", Fire(Game(), dmg, step));
        }

        [Fact]
        public void Skips_unknown_or_unready_candidates()
        {
            var g = Game();
            g.KnownSpells.Add("Revenge"); // Shield Slam unknown → not a candidate
            var step = CombatBlocks.BestDamage(1f, ctx => true, "Shield Slam", "Revenge");

            Assert.Equal("Revenge", Fire(g, new DamageTracker(), step));
        }
    }
}
