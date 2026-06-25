using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Paladin;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class PaladinRetributionTests
    {
        // A retribution paladin in melee on a full-health dummy, already auto-attacking, full mana/health
        // so the survival/upkeep slots above the offensive core stay quiet unless a test opens them.
        private static FakeGameClient RetGame()
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

        // Seal/aura/blessing already up (the "Auto" choices for Retribution) so the rotation reaches the
        // offensive core below the upkeep block.
        private static FakeGameClient BuffsUp(FakeGameClient game)
        {
            game.MeUnit.WithAura("Seal of Command");
            game.MeUnit.WithAura("Retribution Aura");
            game.MeUnit.WithAura("Blessing of Kings");
            return game;
        }

        private static RotationStep Fire(FakeGameClient game) =>
            Fire(game, new PaladinSettings());

        private static RotationStep Fire(FakeGameClient game, PaladinSettings settings) =>
            new RotationEngine(new SoloRetribution(settings).BuildSteps()).Tick(CombatContext.Capture(game));

        [Fact]
        public void Keeps_the_auto_seal_up()
        {
            FakeGameClient game = RetGame();
            Assert.Equal("Seal", Fire(game)?.Name);
            Assert.Contains("Seal of Command", game.CastLog); // Auto → Command when known
        }

        [Fact]
        public void Falls_back_to_Seal_of_Righteousness_when_Command_unknown()
        {
            FakeGameClient game = RetGame();
            game.KnownSpells.Add("Seal of Righteousness"); // restrict the known set (Command not in it)

            Fire(game);
            Assert.Contains("Seal of Righteousness", game.CastLog);
        }

        [Fact]
        public void Keeps_the_aura_up_after_the_seal()
        {
            FakeGameClient game = RetGame();
            game.MeUnit.WithAura("Seal of Command"); // seal already up → aura is next

            Assert.Equal("Aura", Fire(game)?.Name);
            Assert.Contains("Retribution Aura", game.CastLog);
        }

        [Fact]
        public void Keeps_the_blessing_up_after_seal_and_aura()
        {
            FakeGameClient game = RetGame();
            game.MeUnit.WithAura("Seal of Command");
            game.MeUnit.WithAura("Retribution Aura");

            Assert.Equal("Blessing", Fire(game)?.Name);
            Assert.Contains("Blessing of Kings", game.CastLog);
        }

        [Fact]
        public void Judges_on_cooldown_with_the_auto_judgement()
        {
            FakeGameClient game = BuffsUp(RetGame());

            Assert.Equal("Judgement", Fire(game)?.Name);
            Assert.Contains("Judgement of Wisdom", game.CastLog); // Auto → Wisdom when known
        }

        [Fact]
        public void Crusader_Strike_is_the_single_target_filler()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Divine Storm");

            Assert.Equal("Crusader Strike", Fire(game)?.Name);
        }

        [Fact]
        public void Exorcism_fires_on_an_Art_of_War_proc()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.MeUnit.WithAura("The Art of War");
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");

            Assert.Equal("Exorcism", Fire(game)?.Name); // free + instant; above Divine Storm / Crusader Strike
        }

        [Fact]
        public void Hammer_of_Wrath_executes_below_20_percent()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.TargetUnit.HealthPercent = 15;

            Assert.Equal("Hammer of Wrath", Fire(game)?.Name);
        }

        [Fact]
        public void Consecration_fires_on_a_pack()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Divine Storm");
            game.SpellsOnCooldown.Add("Crusader Strike");
            game.SpellsOnCooldown.Add("Exorcism");        // leveling-Ret filler would otherwise win at this priority
            game.SpellsOnCooldown.Add("Avenging Wrath"); // pack cooldown would otherwise win at this count
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });

            Assert.Equal("Consecration", Fire(game)?.Name); // 2 enemies == default AoE threshold
        }

        [Fact]
        public void Consecration_skips_a_dying_pack()
        {
            // Dying-mob fix: even with the pack count met, don't drop an 8-tick ground AoE when the (current) target
            // is already about to die (below ConsecrationMinTargetHealth = 25).
            FakeGameClient game = BuffsUp(RetGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Divine Storm");
            game.SpellsOnCooldown.Add("Crusader Strike");
            game.SpellsOnCooldown.Add("Avenging Wrath");
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });
            game.TargetUnit.HealthPercent = PaladinCommon.ConsecrationMinTargetHealth - 1; // 24%

            Assert.NotEqual("Consecration", Fire(game)?.Name);
            Assert.DoesNotContain("Consecration", game.CastLog);
        }

        [Fact]
        public void Lay_on_Hands_when_critically_low()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.MeUnit.HealthPercent = 10; // below the default 15

            Assert.Equal("Lay on Hands", Fire(game)?.Name);
        }

        [Fact]
        public void Lay_on_Hands_does_not_fire_while_Forbearance_is_up()
        {
            // LoH applies (and is blocked by) Forbearance — so at low HP with Forbearance up it must NOT take the panic
            // slot every tick. With nothing else to do here it should fall through and not cast LoH at all.
            FakeGameClient game = BuffsUp(RetGame());
            game.MeUnit.HealthPercent = 10; // below the default 15
            game.MeUnit.WithAura("Forbearance");

            Assert.NotEqual("Lay on Hands", Fire(game)?.Name);
            Assert.DoesNotContain("Lay on Hands", game.CastLog);
        }

        [Fact]
        public void Hand_of_Freedom_fires_when_rooted()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.RootedFlag = true;

            Assert.Equal("Hand of Freedom", Fire(game)?.Name);
            Assert.Contains("Hand of Freedom", game.CastLog);
        }

        [Fact]
        public void Exorcism_is_a_filler_without_an_Art_of_War_proc()
        {
            // Leveling Ret without the Art of War talent never procs the instant arm — Exorcism must still fire as a
            // normal hard-cast filler below Crusader Strike. No proc aura here.
            FakeGameClient game = BuffsUp(RetGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Divine Storm");
            game.SpellsOnCooldown.Add("Crusader Strike");

            Assert.Equal("Exorcism", Fire(game)?.Name);
        }

        [Fact]
        public void Art_of_War_proc_arm_wins_over_the_Exorcism_filler()
        {
            // With the proc up the instant arm (7.5) must win over the plain filler (9.5) and over Divine Storm /
            // Crusader Strike — both arms are "Exorcism", so confirm it fires above the melee strikes.
            FakeGameClient game = BuffsUp(RetGame());
            game.MeUnit.WithAura("The Art of War");
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");

            Assert.Equal("Exorcism", Fire(game)?.Name);
        }

        [Fact]
        public void Consecration_fires_on_a_pack_spread_inside_the_wider_radius()
        {
            // A second enemy at 12y is outside the old 8y count but inside the 15y Consecration decision radius — the
            // wider gate must still see the pack and fire Consecration (old AIO sized this at 15y).
            FakeGameClient game = BuffsUp(RetGame());
            game.SpellsOnCooldown.Add("Judgement of Wisdom");
            game.SpellsOnCooldown.Add("Judgement of Light");
            game.SpellsOnCooldown.Add("Divine Storm");
            game.SpellsOnCooldown.Add("Crusader Strike");
            game.SpellsOnCooldown.Add("Exorcism");        // filler would otherwise win at this priority
            game.SpellsOnCooldown.Add("Avenging Wrath");  // pack cooldown would otherwise win at this count
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 12 });

            Assert.Equal("Consecration", Fire(game)?.Name);
        }

        [Fact]
        public void Divine_Protection_against_several_attackers()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.TargetUnit.IsTargetingMe = true;
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 5, IsTargetingMe = true });

            Assert.Equal("Divine Protection", Fire(game)?.Name);
        }

        [Fact]
        public void Divine_Plea_when_low_on_mana()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.MeUnit.PowerPercent = 40; // below the default 60

            Assert.Equal("Divine Plea", Fire(game)?.Name);
        }

        [Fact]
        public void Interrupts_a_casting_enemy_with_Hammer_of_Justice()
        {
            FakeGameClient game = RetGame();
            game.TargetUnit.IsCasting = true;
            game.TargetUnit.CastingSpellId = 42;

            Assert.Equal("Hammer of Justice", Fire(game)?.Name);
        }

        [Fact]
        public void Emergency_item_used_below_threshold()
        {
            FakeGameClient game = BuffsUp(RetGame());
            game.MeUnit.HealthPercent = 20; // below the default 30
            game.ReadyItems.Add("Healthstone");

            Assert.Equal("Emergency heal", Fire(game)?.Name);
            Assert.Contains("Healthstone", game.UsedItems);
        }
    }
}
