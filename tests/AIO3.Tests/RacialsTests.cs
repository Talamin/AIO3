using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>
    /// The shared, class-agnostic <see cref="Racials"/> bundle. Each racial is tested in isolation (one step per
    /// engine) so cross-racial priority doesn't interfere. In the FakeGameClient every spell counts as known, so
    /// "auto-skips by race" is simulated by adding the racial to <see cref="FakeGameClient.UnknownSpells"/>.
    /// </summary>
    public class RacialsTests
    {
        private static FakeGameClient Game(out FakeUnit enemy)
        {
            var game = new FakeGameClient { InCombatFlag = true };
            enemy = new FakeUnit { Guid = 1, Name = "Mob", Reaction = Reaction.Hostile, Distance = 5, IsTargetingMe = true };
            game.EnemyList.Add(enemy);
            game.TargetUnit = enemy;
            return game;
        }

        private static string Fire(FakeGameClient game, RotationStep step) =>
            new RotationEngine(new List<RotationStep> { step }).Tick(CombatContext.Capture(game))?.Name;

        // --- Arcane Torrent (Blood Elf): silence + resource ---

        [Fact]
        public void Arcane_Torrent_silences_an_enemy_casting_within_8yd()
        {
            FakeGameClient game = Game(out FakeUnit enemy);
            enemy.IsCasting = true;
            enemy.Distance = 8;
            Assert.Equal("Arcane Torrent", Fire(game, Racials.ArcaneTorrent(ctx => true, 1f)));
        }

        [Fact]
        public void Arcane_Torrent_ignores_a_caster_beyond_8yd()
        {
            FakeGameClient game = Game(out FakeUnit enemy);
            enemy.IsCasting = true;
            enemy.Distance = 12;             // outside the 8yd silence radius
            game.MeUnit.IsCaster = false;    // and not a mana user → no resource trigger either
            Assert.Null(Fire(game, Racials.ArcaneTorrent(ctx => true, 1f)));
        }

        [Fact]
        public void Arcane_Torrent_grabs_the_resource_when_a_mana_user_is_low()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.IsCaster = true;     // has a mana pool
            game.MeUnit.PowerPercent = 15;   // below the low-resource threshold (20)
            Assert.Equal("Arcane Torrent", Fire(game, Racials.ArcaneTorrent(ctx => true, 1f)));
        }

        [Fact]
        public void Arcane_Torrent_does_not_fire_on_low_resource_for_a_non_mana_user()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.IsCaster = false;    // rage/energy user → nothing to restore
            game.MeUnit.PowerPercent = 5;
            Assert.Null(Fire(game, Racials.ArcaneTorrent(ctx => true, 1f)));
        }

        [Fact]
        public void Arcane_Torrent_auto_skips_for_a_non_blood_elf()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.IsCaster = true;
            game.MeUnit.PowerPercent = 10;
            game.UnknownSpells.Add("Arcane Torrent");   // not a Blood Elf
            Assert.Null(Fire(game, Racials.ArcaneTorrent(ctx => true, 1f)));
        }

        [Fact]
        public void Racial_respects_the_use_racials_toggle()
        {
            FakeGameClient game = Game(out FakeUnit enemy);
            enemy.IsCasting = true;
            Assert.Null(Fire(game, Racials.ArcaneTorrent(ctx => false, 1f)));   // toggle off
        }

        // --- War Stomp (Tauren): AoE stun when swarmed ---

        [Fact]
        public void War_Stomp_when_two_enemies_are_in_melee()
        {
            FakeGameClient game = Game(out _);
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6, IsTargetingMe = true });
            Assert.Equal("War Stomp", Fire(game, Racials.WarStomp(ctx => true, 1f)));
        }

        [Fact]
        public void War_Stomp_holds_for_a_single_attacker()
        {
            FakeGameClient game = Game(out _);   // only one enemy in melee on us
            Assert.Null(Fire(game, Racials.WarStomp(ctx => true, 1f)));
        }

        // --- Gift of the Naaru (Draenei): HoT when hurt ---

        [Fact]
        public void Gift_of_the_Naaru_when_hurt_in_combat()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.HealthPercent = 60;
            Assert.Equal("Gift of the Naaru", Fire(game, Racials.GiftOfTheNaaru(ctx => true, 1f)));
        }

        [Fact]
        public void Gift_of_the_Naaru_holds_at_full_health()
        {
            FakeGameClient game = Game(out _);   // health 100 by default
            Assert.Null(Fire(game, Racials.GiftOfTheNaaru(ctx => true, 1f)));
        }

        // --- CC-break / cleanse / panic racials ---

        [Fact]
        public void Will_of_the_Forsaken_breaks_a_fear()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.WithAura("Fear");
            Assert.Equal("Will of the Forsaken", Fire(game, Racials.WillOfTheForsaken(ctx => true, 1f)));
        }

        [Fact]
        public void Every_Man_for_Himself_breaks_a_sleep()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.WithAura("Sleep");
            Assert.Equal("Every Man for Himself", Fire(game, Racials.EveryManForHimself(ctx => true, 1f)));
        }

        [Fact]
        public void CC_break_holds_without_crowd_control()
        {
            FakeGameClient game = Game(out _);   // no Fear/Charm/Sleep on us
            Assert.Null(Fire(game, Racials.WillOfTheForsaken(ctx => true, 1f)));
        }

        [Fact]
        public void Escape_Artist_breaks_a_root()
        {
            FakeGameClient game = Game(out _);
            game.RootedFlag = true;
            Assert.Equal("Escape Artist", Fire(game, Racials.EscapeArtist(ctx => true, 1f)));
        }

        [Fact]
        public void Escape_Artist_also_breaks_a_Frost_Nova_root()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.WithAura("Frost Nova");   // rooted by a mage's nova even if the movement flag lags
            Assert.Equal("Escape Artist", Fire(game, Racials.EscapeArtist(ctx => true, 1f)));
        }

        [Fact]
        public void Stoneform_cleanses_a_poison()
        {
            FakeGameClient game = Game(out _);
            game.DebuffTypes.Add("Poison");
            Assert.Equal("Stoneform", Fire(game, Racials.Stoneform(ctx => true, 1f)));
        }

        [Fact]
        public void Stoneform_cleanses_a_disease()
        {
            FakeGameClient game = Game(out _);
            game.DebuffTypes.Add("Disease");
            Assert.Equal("Stoneform", Fire(game, Racials.Stoneform(ctx => true, 1f)));
        }

        [Fact]
        public void Stoneform_holds_for_a_magic_debuff()
        {
            FakeGameClient game = Game(out _);
            game.DebuffTypes.Add("Magic");   // Stoneform doesn't cleanse magic
            Assert.Null(Fire(game, Racials.Stoneform(ctx => true, 1f)));
        }

        [Fact]
        public void Shadowmeld_when_nearly_dead_in_combat()
        {
            FakeGameClient game = Game(out _);
            game.MeUnit.HealthPercent = 4;   // below the 5% panic floor
            Assert.Equal("Shadowmeld", Fire(game, Racials.Shadowmeld(ctx => true, 1f)));
        }

        [Fact]
        public void Cannibalize_out_of_combat_on_a_nearby_corpse()
        {
            FakeGameClient game = Game(out _);
            game.InCombatFlag = false;            // Cannibalize is an out-of-combat heal
            game.MeUnit.HealthPercent = 40;       // below the 50% floor
            game.CannibalizeCorpseFlag = true;    // a Humanoid/Undead corpse is in range
            Assert.Equal("Cannibalize", Fire(game, Racials.Cannibalize(ctx => true, 1f)));
        }

        [Fact]
        public void Cannibalize_holds_in_combat()
        {
            FakeGameClient game = Game(out _);    // in combat
            game.MeUnit.HealthPercent = 40;
            game.CannibalizeCorpseFlag = true;
            Assert.Null(Fire(game, Racials.Cannibalize(ctx => true, 1f)));
        }

        [Fact]
        public void Cannibalize_pins_position_through_the_channel()
        {
            FakeGameClient game = Game(out _);
            game.InCombatFlag = false;
            game.MeUnit.HealthPercent = 40;
            game.CannibalizeCorpseFlag = true;
            // It fires AND pins the bot in place for the channel, so the product can't drag it off the corpse.
            Assert.Equal("Cannibalize", Fire(game, Racials.Cannibalize(ctx => true, 1f)));
            Assert.Equal(1, game.HoldPositionCalls);
            Assert.Equal(Racials.CannibalizeChannelMs, game.LastHoldMs);
        }

        // --- the bundle: offensive racial wins the high slot ---

        [Fact]
        public void Bundle_fires_Blood_Fury_in_combat_with_an_enemy()
        {
            FakeGameClient game = Game(out _);
            List<RotationStep> steps = Racials.With(new List<RotationStep>(), ctx => true, basePriority: 4f);
            string fired = new RotationEngine(steps).Tick(CombatContext.Capture(game))?.Name;
            Assert.Equal("Blood Fury", fired);   // highest-priority racial in the bundle
        }

        [Fact]
        public void Bundle_is_silent_when_racials_are_disabled()
        {
            FakeGameClient game = Game(out _);
            List<RotationStep> steps = Racials.With(new List<RotationStep>(), ctx => false, basePriority: 4f);
            Assert.Null(new RotationEngine(steps).Tick(CombatContext.Capture(game)));
        }
    }
}
