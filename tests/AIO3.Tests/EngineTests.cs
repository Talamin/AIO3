using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class EngineTests
    {
        private static FakeGameClient FrostMageVsLowHpTarget(int fingersOfFrostStacks)
        {
            var game = new FakeGameClient();
            game.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Target Dummy",
                HealthPercent = 18,
                Reaction = Reaction.Hostile,
                Distance = 20
            };
            game.EnemyList.Add(game.TargetUnit);
            if (fingersOfFrostStacks > 0)
                game.MeUnit.WithAura("Fingers of Frost", stacks: fingersOfFrostStacks, mine: true, timeLeftMs: 8000);
            return game;
        }

        // A tiny two-step Frost filler: prefer Ice Lance when Fingers of Frost is up,
        // otherwise Frostbolt. Ice Lance has the lower (= higher) priority.
        private static RotationEngine FrostFiller() => new RotationEngine(new List<RotationStep>
        {
            Skill.Spell("Frostbolt").Priority(18).On(Targets.Current),
            Skill.Spell("Ice Lance").Priority(15).On(Targets.Current)
                 .When((ctx, t) => ctx.Me.AuraStacks("Fingers of Frost") > 0),
        });

        [Fact]
        public void Picks_IceLance_when_FingersOfFrost_is_active()
        {
            FakeGameClient game = FrostMageVsLowHpTarget(fingersOfFrostStacks: 2);

            RotationStep fired = FrostFiller().Tick(CombatContext.Capture(game));

            Assert.Equal("Ice Lance", fired?.Name);
            Assert.Equal("Ice Lance", game.CastLog.Single());
        }

        [Fact]
        public void Falls_back_to_Frostbolt_without_FingersOfFrost()
        {
            FakeGameClient game = FrostMageVsLowHpTarget(fingersOfFrostStacks: 0);

            RotationStep fired = FrostFiller().Tick(CombatContext.Capture(game));

            Assert.Equal("Frostbolt", fired?.Name);
            Assert.Equal("Frostbolt", game.CastLog.Single());
        }

        [Fact]
        public void Skips_gcd_bound_steps_while_global_cooldown_active()
        {
            FakeGameClient game = FrostMageVsLowHpTarget(fingersOfFrostStacks: 2);
            game.Gcd = 800; // global cooldown running

            RotationStep fired = FrostFiller().Tick(CombatContext.Capture(game));

            Assert.Null(fired);
            Assert.Empty(game.CastLog);
        }

        [Fact]
        public void Respects_spell_cooldown()
        {
            FakeGameClient game = FrostMageVsLowHpTarget(fingersOfFrostStacks: 2);
            game.SpellsOnCooldown.Add("Ice Lance");

            RotationStep fired = FrostFiller().Tick(CombatContext.Capture(game));

            // Ice Lance is on cooldown, so the engine falls through to Frostbolt.
            Assert.Equal("Frostbolt", fired?.Name);
        }

        [Fact]
        public void Exclusive_token_prevents_two_steps_hitting_the_same_target()
        {
            var game = new FakeGameClient();
            var enemy = new FakeUnit { Guid = 7, Name = "Caster", Reaction = Reaction.Hostile, Distance = 15 };
            game.EnemyList.Add(enemy);
            game.TargetUnit = enemy;

            var cc = new Exclusive("crowd-control");
            var engine = new RotationEngine(new List<RotationStep>
            {
                Skill.Spell("Polymorph").Priority(1).On(Targets.Enemies).WithToken(cc),
                Skill.Spell("Repentance").Priority(2).On(Targets.Enemies).WithToken(cc),
            });

            engine.Tick(CombatContext.Capture(game));

            // Only the higher-priority CC fires; the token blocks the second on the same unit.
            Assert.Equal(new[] { "Polymorph" }, game.CastLog.ToArray());
        }
    }
}
