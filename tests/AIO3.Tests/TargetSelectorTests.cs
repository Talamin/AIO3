using AIO3.Core.Combat;
using AIO3.Core.Game;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class TargetSelectorTests
    {
        private static FakeUnit Enemy(ulong guid, float distance, bool targetingMe = false, double hp = 100) => new FakeUnit
        {
            Guid = guid,
            Reaction = Reaction.Hostile,
            IsAttackable = true,
            HealthPercent = hp,
            Distance = distance,
            IsTargetingMe = targetingMe
        };

        private static FakeUnit Pet(ulong guid, ulong ownerGuid, float distance, bool targetingMe = false, double hp = 100) => new FakeUnit
        {
            Guid = guid,
            PetOwnerGuid = ownerGuid, // non-zero → IsPet()
            Reaction = Reaction.Hostile,
            IsAttackable = true,
            HealthPercent = hp,
            Distance = distance,
            IsTargetingMe = targetingMe
        };

        private static IWowUnit Pick(FakeGameClient g) => TargetSelector.Pick(CombatContext.Capture(g));

        [Fact]
        public void Never_pulls_when_no_enemy_is_attacking_us()
        {
            // Enemies are nearby but none is on us and we have no target — the FC must NOT acquire one
            // (the product owns the opener).
            var g = new FakeGameClient();
            g.EnemyList.Add(Enemy(1, distance: 8));
            g.EnemyList.Add(Enemy(2, distance: 12));

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Does_nothing_with_a_single_attacker()
        {
            // One enemy engaged = a normal solo fight; nothing to manage, keep the product's target.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: true);
            g.EnemyList.Add(g.TargetUnit);

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Keeps_the_current_target_when_it_is_one_of_several_attackers()
        {
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: true);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(Enemy(2, distance: 3, targetingMe: true)); // a closer add, but no thrashing

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Switches_to_an_attacker_when_the_current_target_is_not_one_of_them()
        {
            // We're on something that isn't fighting us (e.g. it fled / mis-tag) while two adds are on us.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: false);
            FakeUnit nearAttacker = Enemy(2, distance: 6, targetingMe: true);
            FakeUnit farAttacker = Enemy(3, distance: 20, targetingMe: true);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(farAttacker);
            g.EnemyList.Add(nearAttacker);

            Assert.Same(nearAttacker, Pick(g)); // nearest attacker
        }

        [Fact]
        public void Switches_when_we_have_no_target_but_several_enemies_are_on_us()
        {
            // The product's target died mid-fight; two adds remain on us → re-acquire among the attackers.
            var g = new FakeGameClient();
            FakeUnit a = Enemy(2, distance: 4, targetingMe: true);
            FakeUnit b = Enemy(3, distance: 9, targetingMe: true);
            g.EnemyList.Add(b);
            g.EnemyList.Add(a);

            Assert.Same(a, Pick(g));
        }

        [Fact]
        public void Switches_to_a_much_lower_health_attacker_in_melee()
        {
            // Both in melee (no travel cost) → the one that dies sooner (low health) wins.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: true, hp: 80);
            FakeUnit dying = Enemy(2, distance: 5, targetingMe: true, hp: 20);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(dying);

            Assert.Same(dying, Pick(g));
        }

        [Fact]
        public void Does_not_thrash_for_a_marginally_lower_health_target()
        {
            // 50% vs 45% in melee — too close to be worth switching (hysteresis), so keep the current.
            var g = new FakeGameClient();
            g.TargetUnit = Enemy(1, distance: 5, targetingMe: true, hp: 50);
            g.EnemyList.Add(g.TargetUnit);
            g.EnemyList.Add(Enemy(2, distance: 5, targetingMe: true, hp: 45));

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Keeps_a_nearer_target_when_the_run_up_to_a_low_health_one_costs_more()
        {
            // No current target; a 60%-health attacker in melee vs a 20%-health one 30y away. The run-up
            // (no damage dealt while closing) makes the near one die sooner overall → pick it.
            var g = new FakeGameClient();
            FakeUnit near = Enemy(1, distance: 5, targetingMe: true, hp: 60);
            FakeUnit farDying = Enemy(2, distance: 30, targetingMe: true, hp: 20);
            g.EnemyList.Add(near);
            g.EnemyList.Add(farDying);

            Assert.Same(near, Pick(g));
        }

        // --- pet → owner redirect (a caster/hunter fighting us through its pet) ---

        [Fact]
        public void Switches_from_an_enemy_pet_to_its_owner()
        {
            // We're on the pet (the product tagged it); its owner — the real threat — is up and on us. Kill the owner.
            var g = new FakeGameClient();
            FakeUnit owner = Enemy(1, distance: 25, targetingMe: true);
            FakeUnit pet = Pet(2, ownerGuid: 1, distance: 5, targetingMe: true);
            g.TargetUnit = pet;
            g.EnemyList.Add(owner);
            g.EnemyList.Add(pet);

            Assert.Same(owner, Pick(g));
        }

        [Fact]
        public void Redirects_to_the_owner_even_when_the_pet_is_the_only_thing_on_us()
        {
            // Only the pet is meleeing us, but its owner is present (it summoned the pet attacking us), so
            // going for the owner is not a pull.
            var g = new FakeGameClient();
            FakeUnit owner = Enemy(1, distance: 28, targetingMe: false);
            FakeUnit pet = Pet(2, ownerGuid: 1, distance: 5, targetingMe: true);
            g.TargetUnit = pet;
            g.EnemyList.Add(owner);
            g.EnemyList.Add(pet);

            Assert.Same(owner, Pick(g));
        }

        [Fact]
        public void Does_not_thrash_between_an_owner_and_its_pet()
        {
            // Already on the owner, with both the owner and its (lower-health) pet on us — they collapse to one
            // candidate, so there's nothing to switch to (no oscillation back to the pet on a TTK whim).
            var g = new FakeGameClient();
            FakeUnit owner = Enemy(1, distance: 25, targetingMe: true);
            FakeUnit pet = Pet(2, ownerGuid: 1, distance: 5, targetingMe: true, hp: 20);
            g.TargetUnit = owner;
            g.EnemyList.Add(owner);
            g.EnemyList.Add(pet);

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Stays_on_the_pet_when_its_owner_is_not_in_range()
        {
            // The pet's owner isn't in the scan (off-screen / already dead) → nothing better to switch to.
            var g = new FakeGameClient();
            FakeUnit pet = Pet(2, ownerGuid: 99, distance: 5, targetingMe: true); // owner 99 not present
            g.TargetUnit = pet;
            g.EnemyList.Add(pet);

            Assert.Null(Pick(g));
        }

        [Fact]
        public void Picks_nothing_when_there_are_no_enemies()
        {
            Assert.Null(Pick(new FakeGameClient()));
        }

        [Fact]
        public void Fake_records_the_set_target_guid()
        {
            var g = new FakeGameClient();
            g.SetTarget(Enemy(42, distance: 5));
            Assert.Equal(42ul, g.LastSetTargetGuid);
        }
    }
}
