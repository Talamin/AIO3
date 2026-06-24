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

        [Fact]
        public void Summon_waits_before_re_summoning_a_pet_that_suddenly_vanished()
        {
            // A pet we already had disappears (we just mounted) — the grace must stop us from instantly
            // casting Call Pet into the mount-up (which would dismount us on a loop).
            RotationStep step = PetControl.Summon(On, "Call Pet", "Revive Pet", 1f);
            FakeGameClient g = Game(Pet()); // pet present
            Fire(g, step);                  // the step sees the pet
            g.PetUnit = null;               // pet vanished (mounted)

            Assert.Null(Fire(g, step));     // grace → no immediate re-summon
            Assert.DoesNotContain("Call Pet", g.CastLog);
        }

        [Fact]
        public void Summon_skipped_while_mounted()
        {
            FakeGameClient g = Game(pet: null);
            g.Mounted = true;
            Assert.Null(Fire(g, PetControl.Summon(On, "Call Pet", "Revive Pet", 1f)));
        }

        [Fact]
        public void Summon_plants_the_character_for_the_long_cast()
        {
            // The summon is a long cast: the adapter refuses a cast-time spell while moving, and the product
            // re-paths during travel — so a moving bot never completed it and the pet only appeared at engage.
            // The summon must STOP movement first so it can plant for the cast and summon proactively, OOC.
            FakeGameClient g = Game(pet: null);
            g.Moving = true; // traveling between mobs
            Fire(g, PetControl.Summon(On, "Call Pet", "Revive Pet", 1f));
            Assert.Contains("Call Pet", g.CastLog);  // no longer blocked by movement at this layer
            Assert.True(g.StopMovementCalls > 0);     // planted the character first
        }

        [Fact]
        public void Summon_does_not_restop_or_recast_while_the_cast_is_in_progress()
        {
            // The summon plants the char with one StopMovement, then the cast runs. It must NOT keep calling
            // StopMovement each tick during the cast: StopMove() cancels the in-progress summon, and the action
            // would then re-cast it — the Imp double-cast. While casting, the step stays fully quiet.
            RotationStep step = PetControl.Summon(On, "Call Pet", "Revive Pet", 1f);
            FakeGameClient g = Game(pet: null);

            Fire(g, step);                          // summon issued (one cast, one stop)
            int stopsAfterIssue = g.StopMovementCalls;
            g.Casting = true;                       // the long summon cast is now in progress; pet not up yet
            Fire(g, step);
            Fire(g, step);

            Assert.Single(g.CastLog.FindAll(c => c == "Call Pet")); // exactly one cast — no double-cast
            Assert.Equal(stopsAfterIssue, g.StopMovementCalls);     // no StopMove during the cast (would cancel it)
        }

        [Fact]
        public void Summon_does_not_double_cast_before_the_pet_appears()
        {
            // The summon is a multi-second cast and the pet only spawns a beat AFTER it finishes. The FC must not
            // re-cast Summon into that "cast done, pet not here yet" gap. With the pet still absent, the recast
            // throttle blocks the follow-up ticks, so exactly one summon is cast.
            RotationStep step = PetControl.Summon(On, "Call Pet", "Revive Pet", 1f);
            FakeGameClient g = Game(pet: null);

            Fire(g, step);   // summon issued
            Fire(g, step);   // pet still absent → throttled
            Fire(g, step);   // still throttled

            Assert.Single(g.CastLog.FindAll(c => c == "Call Pet"));
        }

        // --- Autocast (e.g. Imp Firebolt) ---

        [Fact]
        public void Autocast_enables_an_ability_the_pet_has()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Firebolt");
            Fire(g, PetControl.Autocast(On, "Firebolt", 0.96f));
            Assert.True(g.PetAutocast["Firebolt"]);
        }

        [Fact]
        public void Autocast_disables_when_the_toggle_is_off()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Firebolt");
            Fire(g, PetControl.Autocast(Off, "Firebolt", 0.96f));
            Assert.False(g.PetAutocast["Firebolt"]);
        }

        [Fact]
        public void Autocast_skips_a_pet_without_the_ability()
        {
            FakeGameClient g = Game(Pet()); // e.g. a Voidwalker — no Firebolt on its bar
            Assert.Null(Fire(g, PetControl.Autocast(On, "Firebolt", 0.96f)));
            Assert.DoesNotContain("Firebolt", g.PetAutocast.Keys);
        }

        [Fact]
        public void Autocast_syncs_once_then_throttles_and_never_preempts()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Firebolt");
            RotationStep step = PetControl.Autocast(On, "Firebolt", 0.96f);
            // It returns Failed (background maintenance), so it never "fires" — and the throttle means it applies
            // the autocast just once across several quick ticks, not every tick.
            Assert.Null(Fire(g, step));
            Assert.Null(Fire(g, step));
            Assert.Null(Fire(g, step));
            Assert.Equal(1, g.PetAutocastCalls);
            Assert.True(g.PetAutocast["Firebolt"]);
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

        // --- UseAbility (pet special abilities) ---

        [Fact]
        public void UseAbility_casts_a_ready_pet_ability()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Furious Howl"); // on the bar and off cooldown
            Fire(g, PetControl.UseAbility(On, "Furious Howl", 1f));
            Assert.Contains("Furious Howl", g.PetCastLog);
        }

        [Fact]
        public void UseAbility_skips_an_ability_on_cooldown()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Furious Howl");
            g.PetAbilitiesOnCooldown.Add("Furious Howl");
            Assert.Null(Fire(g, PetControl.UseAbility(On, "Furious Howl", 1f)));
            Assert.Empty(g.PetCastLog);
        }

        [Fact]
        public void UseAbility_auto_skips_a_pet_without_the_ability()
        {
            FakeGameClient g = Game(Pet()); // no Furious Howl on this pet's bar
            Assert.Null(Fire(g, PetControl.UseAbility(On, "Furious Howl", 1f)));
        }

        [Fact]
        public void UseAbility_respects_the_when_gate()
        {
            FakeGameClient g = Game(Pet());
            g.PetAbilities.Add("Dash");
            Assert.Null(Fire(g, PetControl.UseAbility(On, "Dash", 1f, when: ctx => false)));
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
