using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Paladin;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class PaladinProtectionTests
    {
        private static FakeGameClient ProtGame()
        {
            var game = new FakeGameClient { Class = WowClass.Paladin, AutoAttacking = true };
            game.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100,
                IsAttackable = true
            };
            game.EnemyList.Add(game.TargetUnit);
            return game;
        }

        // Full upkeep up (the "Auto" choices for Protection) so the rotation reaches the threat core.
        private static FakeGameClient BuffsUp(FakeGameClient game)
        {
            game.MeUnit.WithAura("Seal of Vengeance");
            game.MeUnit.WithAura("Devotion Aura");
            game.MeUnit.WithAura("Blessing of Sanctuary");
            game.MeUnit.WithAura("Righteous Fury");
            game.MeUnit.WithAura("Holy Shield");
            return game;
        }

        private static RotationStep Fire(FakeGameClient game) =>
            new RotationEngine(new SoloProtection(new PaladinSettings()).BuildSteps()).Tick(CombatContext.Capture(game));

        [Fact]
        public void Keeps_Righteous_Fury_up()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.WithAura("Seal of Vengeance");
            game.MeUnit.WithAura("Devotion Aura");
            game.MeUnit.WithAura("Blessing of Sanctuary");
            // Righteous Fury missing → it is the next upkeep step.

            Assert.Equal("Righteous Fury", Fire(game)?.Name);
        }

        [Fact]
        public void Maintains_Holy_Shield_in_combat()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.WithAura("Seal of Vengeance");
            game.MeUnit.WithAura("Devotion Aura");
            game.MeUnit.WithAura("Blessing of Sanctuary");
            game.MeUnit.WithAura("Righteous Fury");
            // Holy Shield missing, an enemy target present → maintain it.

            Assert.Equal("Holy Shield", Fire(game)?.Name);
        }

        [Fact]
        public void Shield_of_Righteousness_is_the_threat_filler()
        {
            FakeGameClient game = BuffsUp(ProtGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");

            Assert.Equal("Shield of Righteousness", Fire(game)?.Name);
        }

        [Fact]
        public void Hammer_of_the_Righteous_after_Shield_of_Righteousness()
        {
            FakeGameClient game = BuffsUp(ProtGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Shield of Righteousness");

            Assert.Equal("Hammer of the Righteous", Fire(game)?.Name);
        }

        [Fact]
        public void Avengers_Shield_is_not_used_as_an_opener()
        {
            FakeGameClient game = BuffsUp(ProtGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Shield of Righteousness");
            game.SpellsOnCooldown.Add("Hammer of the Righteous");
            game.InCombatFlag = false; // product hasn't engaged yet → never pull with Avenger's Shield

            Assert.NotEqual("Avenger's Shield", Fire(game)?.Name);
        }

        [Fact]
        public void Avengers_Shield_fires_in_combat()
        {
            FakeGameClient game = BuffsUp(ProtGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Shield of Righteousness");
            game.SpellsOnCooldown.Add("Hammer of the Righteous");
            game.InCombatFlag = true;

            Assert.Equal("Avenger's Shield", Fire(game)?.Name);
        }

        [Fact]
        public void Consecration_fires_on_an_elite()
        {
            FakeGameClient game = BuffsUp(ProtGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Shield of Righteousness");
            game.SpellsOnCooldown.Add("Hammer of the Righteous");
            game.SpellsOnCooldown.Add("Avenging Wrath"); // would otherwise fire on the elite
            game.TargetUnit.IsElite = true;

            Assert.Equal("Consecration", Fire(game)?.Name);
        }

        [Fact]
        public void Interrupts_a_casting_enemy_with_Hammer_of_Justice()
        {
            FakeGameClient game = ProtGame();
            game.TargetUnit.IsCasting = true;
            game.TargetUnit.CastingSpellId = 42;

            Assert.Equal("Hammer of Justice", Fire(game)?.Name);
        }
    }
}
