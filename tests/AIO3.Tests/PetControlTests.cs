using System;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class PetControlTests
    {
        private static readonly Func<CombatContext, bool> On = ctx => true;
        private static readonly Func<CombatContext, bool> Off = ctx => false;

        private static FakeGameClient Game(FakeUnit pet)
        {
            var g = new FakeGameClient { Class = WowClass.Hunter };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 28,
                HealthPercent = 100,
                IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.PetUnit = pet;
            return g;
        }

        private static FakeUnit Pet(bool alive = true, double hp = 100, ulong target = 0) =>
            new FakeUnit { Guid = 99, Name = "Pet", IsAlive = alive, HealthPercent = hp, TargetGuid = target, Distance = 5 };

        private static RotationStep Fire(FakeGameClient g, RotationStep step) =>
            new RotationEngine(new[] { step }).Tick(CombatContext.Capture(g));

        // --- Summon (call / revive) ---

        [Fact]
        public void Summon_calls_pet_when_absent()
        {
            FakeGameClient g = Game(pet: null);
            Fire(g, PetControl.Summon(On, "Call Pet", "Revive Pet", 1f));
            Assert.Contains("Call Pet", g.CastLog);
        }

        [Fact]
        public void Summon_revives_a_dead_pet()
        {
            FakeGameClient g = Game(Pet(alive: false));
            Fire(g, PetControl.Summon(On, "Call Pet", "Revive Pet", 1f));
            Assert.Contains("Revive Pet", g.CastLog);
        }

        [Fact]
        public void Summon_idle_when_the_pet_is_alive()
        {
            FakeGameClient g = Game(Pet());
            Assert.Null(Fire(g, PetControl.Summon(On, "Call Pet", "Revive Pet", 1f)));
        }

        [Fact]
        public void Summon_skipped_in_combat()
        {
            FakeGameClient g = Game(pet: null);
            g.InCombatFlag = true; // Call/Revive are slow casts — out of combat only
            Assert.Null(Fire(g, PetControl.Summon(On, "Call Pet", "Revive Pet", 1f)));
        }

        [Fact]
        public void Summon_skipped_when_disabled()
        {
            FakeGameClient g = Game(pet: null);
            Assert.Null(Fire(g, PetControl.Summon(Off, "Call Pet", "Revive Pet", 1f)));
        }

        // --- Heal ---

        [Fact]
        public void Heal_mends_a_hurt_pet()
        {
            FakeGameClient g = Game(Pet(hp: 50));
            Fire(g, PetControl.Heal(On, "Mend Pet", ctx => 60, 1f));
            Assert.Contains("Mend Pet", g.CastLog);
        }

        [Fact]
        public void Heal_idle_when_pet_is_healthy()
        {
            FakeGameClient g = Game(Pet(hp: 100));
            Assert.Null(Fire(g, PetControl.Heal(On, "Mend Pet", ctx => 60, 1f)));
        }

        [Fact]
        public void Heal_skipped_when_petless()
        {
            FakeGameClient g = Game(pet: null);
            Assert.Null(Fire(g, PetControl.Heal(On, "Mend Pet", ctx => 60, 1f)));
        }

        // --- Attack ---

        [Fact]
        public void Attack_sends_the_pet_to_the_target()
        {
            FakeGameClient g = Game(Pet(target: 0)); // pet not on our target
            Fire(g, PetControl.Attack(On, 1f));
            Assert.Contains(1ul, g.PetAttackLog);
        }

        [Fact]
        public void Attack_idle_when_pet_already_on_target()
        {
            FakeGameClient g = Game(Pet(target: 1)); // already attacking our target
            Assert.Null(Fire(g, PetControl.Attack(On, 1f)));
            Assert.Empty(g.PetAttackLog);
        }

        [Fact]
        public void Attack_skipped_when_petless()
        {
            FakeGameClient g = Game(pet: null);
            Assert.Null(Fire(g, PetControl.Attack(On, 1f)));
            Assert.Empty(g.PetAttackLog);
        }

        [Fact]
        public void Attack_skipped_when_disabled()
        {
            FakeGameClient g = Game(Pet(target: 0));
            Assert.Null(Fire(g, PetControl.Attack(Off, 1f)));
            Assert.Empty(g.PetAttackLog);
        }

        // --- Taunt (pet-ability management) ---

        [Fact]
        public void Taunt_pulls_aggro_when_a_mob_is_on_me()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Growl");      // the pet has a taunt
            g.TargetUnit.IsTargetingMe = true; // and something is on us

            Fire(g, PetControl.Taunt(On, "Growl", 1f));
            Assert.Contains("Growl", g.PetCastLog);
        }

        [Fact]
        public void Taunt_auto_skips_a_pet_without_the_taunt()
        {
            FakeGameClient g = Game(Pet());
            // No "Growl" on the bar (e.g. an Imp) — must be handled automatically, not attempted.
            g.TargetUnit.IsTargetingMe = true;

            Assert.Null(Fire(g, PetControl.Taunt(On, "Growl", 1f)));
            Assert.Empty(g.PetCastLog);
        }

        [Fact]
        public void Taunt_idle_when_nothing_is_on_me()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Growl");
            // Nothing targeting us (pet is holding aggro) → no need to taunt.

            Assert.Null(Fire(g, PetControl.Taunt(On, "Growl", 1f)));
            Assert.Empty(g.PetCastLog);
        }

        [Fact]
        public void Taunt_skipped_when_petless()
        {
            FakeGameClient g = Game(pet: null);
            g.TargetUnit.IsTargetingMe = true;
            Assert.Null(Fire(g, PetControl.Taunt(On, "Growl", 1f)));
        }

        // --- Attack target priority (peel adds off the owner) ---

        [Fact]
        public void Attack_peels_a_mob_that_is_attacking_the_owner()
        {
            FakeGameClient g = Game(Pet(target: 1)); // pet currently on the main target (guid 1)
            g.EnemyList.Add(new FakeUnit
            {
                Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, HealthPercent = 80,
                IsTargetingMe = true // an add slipped onto us
            });

            Fire(g, PetControl.Attack(On, 1f));
            Assert.Contains(2ul, g.PetAttackLog); // pet redirected to the add on the owner, not the main target
        }

        [Fact]
        public void Attack_keeps_holding_a_mob_that_is_on_the_pet()
        {
            // Pet is on the add (guid 2) which now attacks the pet (already peeled). Nothing on the owner.
            FakeGameClient g = Game(Pet(target: 2));
            g.EnemyList.Add(new FakeUnit
            {
                Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, HealthPercent = 80,
                IsTargetingMyPet = true
            });

            // Must NOT thrash back to the main target — the pet keeps the add it is holding.
            Assert.Null(Fire(g, PetControl.Attack(On, 1f)));
            Assert.Empty(g.PetAttackLog);
        }
    }
}
