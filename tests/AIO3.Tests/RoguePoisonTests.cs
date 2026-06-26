using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // The rogue weapon-poison rank tables + selection (pure, no game state).
    public class RoguePoisonsDataTests
    {
        [Fact]
        public void Picks_the_highest_instant_rank_the_level_allows_and_carries()
        {
            var carried = new HashSet<uint> { 6947, 6949, 6950 }; // Instant Poison ranks I, II, III

            Assert.Equal(6950u, RoguePoisons.BestUsableInstant(40, carried.Contains)); // rank III (L36) <= 40
            Assert.Equal(6949u, RoguePoisons.BestUsableInstant(30, carried.Contains)); // rank III needs L36 → rank II (L28)
            Assert.Equal(0u, RoguePoisons.BestUsableInstant(19, carried.Contains));    // rank I needs L20 → nothing usable
        }

        [Fact]
        public void Skips_a_rank_the_level_allows_but_the_bags_do_not_carry()
        {
            var carried = new HashSet<uint> { 6947 }; // only rank I in the bags
            Assert.Equal(6947u, RoguePoisons.BestUsableInstant(80, carried.Contains)); // level allows all, only I carried
        }

        [Fact]
        public void Deadly_table_is_ordered_so_a_level_60_gets_rank_five_not_rank_one()
        {
            // The old AIO's mis-ordered dict made a level-60 rogue pick rank-1 Deadly (id 2892, L30). Ordered
            // strictly high→low, a level-60 must pick rank V (id 20844, L60).
            var all = new HashSet<uint> { 43233, 43232, 22054, 22053, 20844, 8985, 8984, 2893, 2892 };
            Assert.Equal(20844u, RoguePoisons.BestUsableDeadly(60, all.Contains));
            Assert.Equal(22053u, RoguePoisons.BestUsableDeadly(62, all.Contains)); // rank VI (L62)
        }
    }

    // The out-of-combat MaintainPoisons upkeep block via the engine.
    public class RoguePoisonBlockTests
    {
        private const uint InstantR1 = 6947; // L20
        private const uint DeadlyR1 = 2892;  // L30
        private const int OneHourMs = 60 * 60000;

        private static FakeGameClient OocRogue(int level)
        {
            var g = new FakeGameClient { Class = WowClass.Rogue, InCombatFlag = false };
            g.MeUnit.Level = level;
            return g;
        }

        private static string Fire(FakeGameClient g, RotationStep step) =>
            new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g))?.Name;

        private static RotationStep Step(RogueSettings s) => RogueCommon.MaintainPoisons(s, priority: 1f);

        [Fact]
        public void Applies_instant_poison_to_the_main_hand_when_its_enchant_is_low()
        {
            var s = new RogueSettings();
            var g = OocRogue(20);
            g.ItemIdsInBags.Add(InstantR1);
            g.WeaponEnchantState = new WeaponEnchant(mainHandEquipped: true, mainHandRemainingMs: 0,
                                                     offHandEquipped: false, offHandRemainingMs: 0);

            Assert.Equal("Apply poisons", Fire(g, Step(s)));
            PoisonApplication applied = Assert.Single(g.AppliedPoisons);
            Assert.Equal(InstantR1, applied.PoisonId);
            Assert.True(applied.MainHand);
        }

        [Fact]
        public void Prefers_deadly_poison_on_the_off_hand()
        {
            var s = new RogueSettings();
            var g = OocRogue(30);
            g.ItemIdsInBags.Add(InstantR1);
            g.ItemIdsInBags.Add(DeadlyR1);
            g.WeaponEnchantState = new WeaponEnchant(true, OneHourMs, true, 0); // main fresh, off low

            Assert.Equal("Apply poisons", Fire(g, Step(s)));
            PoisonApplication applied = Assert.Single(g.AppliedPoisons);
            Assert.Equal(DeadlyR1, applied.PoisonId);
            Assert.False(applied.MainHand); // off hand
        }

        [Fact]
        public void Falls_back_to_instant_on_the_off_hand_when_no_deadly_is_carried()
        {
            var s = new RogueSettings();
            var g = OocRogue(30);
            g.ItemIdsInBags.Add(InstantR1); // no deadly carried
            g.WeaponEnchantState = new WeaponEnchant(true, OneHourMs, true, 0);

            Assert.Equal("Apply poisons", Fire(g, Step(s)));
            PoisonApplication applied = Assert.Single(g.AppliedPoisons);
            Assert.Equal(InstantR1, applied.PoisonId);
            Assert.False(applied.MainHand);
        }

        [Fact]
        public void Does_nothing_when_both_hands_are_freshly_poisoned()
        {
            var s = new RogueSettings();
            var g = OocRogue(60);
            g.ItemIdsInBags.Add(InstantR1);
            g.ItemIdsInBags.Add(DeadlyR1);
            g.WeaponEnchantState = new WeaponEnchant(true, OneHourMs, true, OneHourMs); // both well above threshold

            Assert.Null(Fire(g, Step(s)));
            Assert.Empty(g.AppliedPoisons);
        }

        [Fact]
        public void Does_not_apply_poisons_in_combat()
        {
            var s = new RogueSettings();
            var g = OocRogue(20);
            g.InCombatFlag = true;
            g.ItemIdsInBags.Add(InstantR1);
            g.WeaponEnchantState = new WeaponEnchant(true, 0, false, 0);

            Assert.Null(Fire(g, Step(s)));
            Assert.Empty(g.AppliedPoisons);
        }

        [Fact]
        public void Respects_the_use_poisons_toggle()
        {
            var s = new RogueSettings();
            s.UsePoisons.Value = false;
            var g = OocRogue(20);
            g.ItemIdsInBags.Add(InstantR1);
            g.WeaponEnchantState = new WeaponEnchant(true, 0, false, 0);

            Assert.Null(Fire(g, Step(s)));
            Assert.Empty(g.AppliedPoisons);
        }

        [Fact]
        public void Falls_through_when_no_poison_is_carried()
        {
            var s = new RogueSettings();
            var g = OocRogue(20); // a hand needs poison but the bags are empty
            g.WeaponEnchantState = new WeaponEnchant(true, 0, true, 0);

            Assert.Null(Fire(g, Step(s)));
            Assert.Empty(g.AppliedPoisons);
        }

        [Fact]
        public void Tops_up_the_main_hand_first_when_both_hands_are_low()
        {
            var s = new RogueSettings();
            var g = OocRogue(30);
            g.ItemIdsInBags.Add(InstantR1);
            g.ItemIdsInBags.Add(DeadlyR1);
            g.WeaponEnchantState = new WeaponEnchant(true, 0, true, 0); // both low

            Assert.Equal("Apply poisons", Fire(g, Step(s)));
            PoisonApplication applied = Assert.Single(g.AppliedPoisons);
            Assert.Equal(InstantR1, applied.PoisonId); // main hand Instant goes first
            Assert.True(applied.MainHand);
        }
    }
}
