using AIO3.Core.Combat;
using AIO3.Core.Game;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightRuneTests
    {
        private static CombatContext Ctx(FakeGameClient g) => CombatContext.Capture(g);

        private static FakeGameClient WithRunes(int blood, int frost, int unholy, int death)
        {
            var g = new FakeGameClient { Class = WowClass.DeathKnight };
            g.RunesReadyByType[RuneType.Blood] = blood;
            g.RunesReadyByType[RuneType.Frost] = frost;
            g.RunesReadyByType[RuneType.Unholy] = unholy;
            g.RunesReadyByType[RuneType.Death] = death;
            return g;
        }

        [Fact]
        public void Exact_type_runes_pay_their_cost()
        {
            var ctx = Ctx(WithRunes(blood: 1, frost: 1, unholy: 1, death: 0));
            Assert.True(DeathKnightCommon.CanAffordRunes(ctx, blood: 1, frost: 0, unholy: 0));
            Assert.True(DeathKnightCommon.CanAffordRunes(ctx, blood: 0, frost: 1, unholy: 1)); // Death Strike
        }

        [Fact]
        public void Missing_specific_rune_is_unaffordable_without_death_runes()
        {
            var ctx = Ctx(WithRunes(blood: 0, frost: 1, unholy: 1, death: 0));
            Assert.False(DeathKnightCommon.CanAffordRunes(ctx, blood: 1, frost: 0, unholy: 0)); // no Blood, no Death
        }

        [Fact]
        public void Death_runes_cover_any_deficit()
        {
            // No Blood ready, but one Death rune pays for the Blood Strike.
            var ctx = Ctx(WithRunes(blood: 0, frost: 0, unholy: 0, death: 1));
            Assert.True(DeathKnightCommon.CanAffordRunes(ctx, blood: 1, frost: 0, unholy: 0));
        }

        [Fact]
        public void Death_runes_cover_a_combined_multi_type_deficit()
        {
            // Death and Decay wants 1B+1F+1U. We have none of those, but 3 Death runes cover all three.
            var ctx = Ctx(WithRunes(blood: 0, frost: 0, unholy: 0, death: 3));
            Assert.True(DeathKnightCommon.CanAffordRunes(ctx, blood: 1, frost: 1, unholy: 1));

            // ...but only 2 Death runes can't cover a 3-rune deficit.
            var ctx2 = Ctx(WithRunes(blood: 0, frost: 0, unholy: 0, death: 2));
            Assert.False(DeathKnightCommon.CanAffordRunes(ctx2, blood: 1, frost: 1, unholy: 1));
        }

        [Fact]
        public void Named_cost_lookup_matches_verified_3_3_5a_costs()
        {
            // The two formerly-uncertain values, verified for WotLK 3.3.5a:
            Assert.Equal(new DeathKnightCommon.Cost(0, 1, 1), DeathKnightCommon.RuneCost["Scourge Strike"]);   // 1F+1U
            Assert.Equal(new DeathKnightCommon.Cost(1, 1, 1), DeathKnightCommon.RuneCost["Death and Decay"]);  // 1B+1F+1U
            // Spot-check the simple ones.
            Assert.Equal(new DeathKnightCommon.Cost(0, 1, 0), DeathKnightCommon.RuneCost["Icy Touch"]);
            Assert.Equal(new DeathKnightCommon.Cost(0, 0, 1), DeathKnightCommon.RuneCost["Plague Strike"]);
            Assert.Equal(new DeathKnightCommon.Cost(0, 1, 1), DeathKnightCommon.RuneCost["Obliterate"]);
            Assert.Equal(new DeathKnightCommon.Cost(1, 0, 0), DeathKnightCommon.RuneCost["Heart Strike"]);
        }

        [Fact]
        public void Zero_rune_abilities_are_always_affordable()
        {
            var ctx = Ctx(WithRunes(0, 0, 0, 0)); // completely rune-starved
            // Frost Strike / Death Coil / Death Grip carry no rune cost → always affordable.
            Assert.True(DeathKnightCommon.CanAfford(ctx, "Frost Strike"));
            Assert.True(DeathKnightCommon.CanAfford(ctx, "Death Coil"));
            Assert.True(DeathKnightCommon.CanAfford(ctx, "Death Grip"));
        }

        [Fact]
        public void CanAfford_named_uses_the_cost_map()
        {
            var ctx = Ctx(WithRunes(blood: 0, frost: 1, unholy: 1, death: 0));
            Assert.True(DeathKnightCommon.CanAfford(ctx, "Obliterate"));      // 1F+1U → affordable
            Assert.False(DeathKnightCommon.CanAfford(ctx, "Death and Decay")); // needs 1B too, none ready
        }

        [Fact]
        public void Runes_ready_total_sums_all_kinds()
        {
            var ctx = Ctx(WithRunes(blood: 2, frost: 1, unholy: 0, death: 1));
            Assert.Equal(4, DeathKnightCommon.RunesReadyTotal(ctx));
        }
    }
}
