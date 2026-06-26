using System;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>
    /// PetControl.FeedWhenUnhappy — out-of-combat Feed Pet upkeep (audit item S5). Mirrors the old AIO PetHelper:
    /// feed an unhappy pet (happiness 1 or 2) out of combat so it stops dealing -25% damage / running off.
    /// </summary>
    public class PetFeedTests
    {
        private static readonly Func<CombatContext, bool> On = ctx => true;
        private static readonly Func<CombatContext, bool> Off = ctx => false;

        // A fresh FeedWhenUnhappy step per call: the recast throttle persists per-step, so reusing one instance
        // across tests would leak state (a feed in one test would suppress the next within the 5s window).
        private static RotationStep Step(Func<CombatContext, bool> enabled) => PetControl.FeedWhenUnhappy(enabled, 0.55f);

        // Out of combat + a happiness 1/2 pet by default; tests override the interesting field.
        private static FakeGameClient Game(FakeUnit pet, int happiness = 1, bool inCombat = false)
        {
            var g = new FakeGameClient { Class = WowClass.Hunter, InCombatFlag = inCombat };
            g.PetUnit = pet;
            g.PetHappinessValue = happiness;
            return g;
        }

        private static FakeUnit Pet(bool alive = true) =>
            new FakeUnit { Guid = 99, Name = "Pet", IsAlive = alive, HealthPercent = 100, Distance = 5 };

        private static RotationStep Fire(FakeGameClient g, RotationStep step) =>
            new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g));

        [Fact]
        public void Feeds_an_unhappy_pet_out_of_combat()
        {
            FakeGameClient g = Game(Pet(), happiness: 1);
            Assert.Equal("Pet feed", Fire(g, Step(On))?.Name);
            Assert.Equal(1, g.FeedPetCalls); // it actually invoked FeedPet
        }

        [Fact]
        public void Feeds_a_content_pet_too()
        {
            // Happiness 2 (content) is still below 3 (happy) — the old FC fed on < 3, so content is fed.
            FakeGameClient g = Game(Pet(), happiness: 2);
            Assert.Equal("Pet feed", Fire(g, Step(On))?.Name);
            Assert.Equal(1, g.FeedPetCalls);
        }

        [Fact]
        public void Does_not_feed_a_happy_pet()
        {
            FakeGameClient g = Game(Pet(), happiness: 3);
            Assert.Null(Fire(g, Step(On)));
            Assert.Equal(0, g.FeedPetCalls);
        }

        [Fact]
        public void Does_not_feed_when_there_is_no_pet()
        {
            // No pet → PetHappiness reads 0 anyway, but the pet-existence gate is what protects us.
            FakeGameClient g = Game(pet: null, happiness: 0);
            Assert.Null(Fire(g, Step(On)));
            Assert.Equal(0, g.FeedPetCalls);
        }

        [Fact]
        public void Does_not_feed_a_dead_pet()
        {
            FakeGameClient g = Game(Pet(alive: false), happiness: 1);
            Assert.Null(Fire(g, Step(On)));
            Assert.Equal(0, g.FeedPetCalls);
        }

        [Fact]
        public void Does_not_feed_in_combat()
        {
            // Feed Pet is interrupted by damage — it's an OUT-of-combat upkeep only.
            FakeGameClient g = Game(Pet(), happiness: 1, inCombat: true);
            Assert.Null(Fire(g, Step(On)));
            Assert.Equal(0, g.FeedPetCalls);
        }

        [Fact]
        public void Does_not_feed_when_disabled()
        {
            // Petfeed off (or Manage pet off) → the enabled predicate is false and we never feed.
            FakeGameClient g = Game(Pet(), happiness: 1);
            Assert.Null(Fire(g, Step(Off)));
            Assert.Equal(0, g.FeedPetCalls);
        }

        [Fact]
        public void Falls_through_when_no_matching_food_in_bags()
        {
            // FeedPet returns false (the adapter found no in-bag food of an accepted type) → the step does NOT
            // claim the tick, so a lower-priority step could run. It still invoked FeedPet (one attempt).
            FakeGameClient g = Game(Pet(), happiness: 1);
            g.FeedPetResult = false;
            Assert.Null(Fire(g, Step(On))); // Failed result → engine reports no fire
            Assert.Equal(1, g.FeedPetCalls);
        }
    }
}
