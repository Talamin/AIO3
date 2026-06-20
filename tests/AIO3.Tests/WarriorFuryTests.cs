using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarriorFuryTests
    {
        // A warrior in Berserker stance, already auto-attacking, Bloodrage on cooldown,
        // facing one full-health enemy in melee. Upkeep steps are neutralised so each test
        // isolates the rule it cares about.
        private static FakeGameClient WarriorGame()
        {
            var game = new FakeGameClient
            {
                Class = WowClass.Warrior,
                StanceName = "Berserker Stance",
                AutoAttacking = true
            };
            game.SpellsOnCooldown.Add("Bloodrage");
            game.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100
            };
            game.EnemyList.Add(game.TargetUnit);
            // Pre-buff Battle Shout so the prio-3 buff step doesn't preempt the rules under test.
            game.MeUnit.WithAura("Battle Shout", mine: true);
            return game;
        }

        private static RotationStep Fire(FakeGameClient game) =>
            new RotationEngine(new SoloFury().BuildSteps()).Tick(CombatContext.Capture(game));

        private static RotationStep Fire(FakeGameClient game, SoloFury rotation) =>
            new RotationEngine(rotation.BuildSteps()).Tick(CombatContext.Capture(game));

        [Fact]
        public void Heroic_Strike_reserve_setting_is_respected()
        {
            var fs = new WarriorSettings();
            fs.HeroicStrikeRageReserve.Value = 40;
            FakeGameClient game = WarriorGame();
            game.Gcd = 1000;        // only off-GCD steps eligible
            game.MeUnit.Rage = 30;  // above the default 20 but below the configured 40

            Assert.Null(Fire(game, new SoloFury(fs))); // keeps the rage, no HS
        }

        [Fact]
        public void AoE_threshold_setting_is_respected()
        {
            var fs = new WarriorSettings();
            fs.AoeThreshold.Value = 3; // require 3 enemies
            FakeGameClient game = WarriorGame();
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });
            game.SpellsOnCooldown.Add("Victory Rush");
            game.SpellsOnCooldown.Add("Whirlwind"); // Whirlwind is also a single-target filler; isolate the AoE-gated steps
            game.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 99999);

            RotationStep fired = Fire(game, new SoloFury(fs)); // only 2 enemies → below threshold
            Assert.NotEqual("Thunder Clap", fired?.Name);
            Assert.NotEqual("Piercing Howl", fired?.Name);
            Assert.NotEqual("Cleave", fired?.Name);
        }

        [Fact]
        public void Gap_closers_can_be_disabled_via_setting()
        {
            var fs = new WarriorSettings();
            fs.UseGapClosers.Value = false;
            FakeGameClient game = WarriorGame();
            game.TargetUnit.Distance = 15;
            game.KnownSpells.Add("Charge"); // would Charge if gap-closers were enabled

            RotationStep fired = Fire(game, new SoloFury(fs));
            Assert.NotEqual("Charge", fired?.Name);
            Assert.NotEqual("Intercept", fired?.Name);
        }

        [Fact]
        public void Never_attacks_a_friendly_target()
        {
            FakeGameClient game = WarriorGame();
            game.TargetUnit.Reaction = Reaction.Friendly;
            game.TargetUnit.IsAttackable = false; // friendly NPC (e.g. quest giver)
            game.MeUnit.Rage = 100;               // plenty of rage — still must not attack
            game.EnemyList.Clear();

            RotationStep fired = Fire(game);

            Assert.Null(fired);
            Assert.Empty(game.CastLog);
        }

        [Fact]
        public void Switches_to_Berserker_Stance_when_not_in_it()
        {
            FakeGameClient game = WarriorGame();
            game.StanceName = "Battle Stance";

            Assert.Equal("Berserker Stance", Fire(game)?.Name);
        }

        [Fact]
        public void Ensures_auto_attack_when_not_attacking()
        {
            FakeGameClient game = WarriorGame();
            game.AutoAttacking = false;

            Assert.Equal("Auto Attack", Fire(game)?.Name);
        }

        [Fact]
        public void Interrupts_a_casting_enemy_with_Pummel()
        {
            FakeGameClient game = WarriorGame();
            game.TargetUnit.IsCasting = true;

            Assert.Equal("Pummel", Fire(game)?.Name);
        }

        [Fact]
        public void Slam_fires_on_Bloodsurge_proc()
        {
            FakeGameClient game = WarriorGame();
            game.MeUnit.WithAura("Slam!");

            Assert.Equal("Slam", Fire(game)?.Name);
        }

        [Fact]
        public void Bloodthirst_fires_with_rage_when_wounded()
        {
            FakeGameClient game = WarriorGame();
            game.MeUnit.Rage = 50;
            game.MeUnit.HealthPercent = 75;

            Assert.Equal("Bloodthirst", Fire(game)?.Name);
        }

        [Fact]
        public void Bloodthirst_fires_at_full_hp_with_rage()
        {
            // The old rotation gated Bloodthirst on HealthPercent <= 80 (a carried-over quirk that
            // cost DPS at full health). With the gate removed it now fires on cooldown with rage,
            // even at full health — it is Fury's top DPS strike and feeds the Bloodsurge proc.
            FakeGameClient game = WarriorGame();
            game.MeUnit.Rage = 50;
            game.MeUnit.HealthPercent = 100;

            Assert.Equal("Bloodthirst", Fire(game)?.Name);
        }

        [Fact]
        public void Bloodthirst_is_skipped_without_rage()
        {
            // Below the rage gate (> 30) Bloodthirst is held, so Death Wish (rage > 10) wins instead.
            FakeGameClient game = WarriorGame();
            game.MeUnit.Rage = 25;
            game.MeUnit.HealthPercent = 100;

            Assert.Equal("Death Wish", Fire(game)?.Name);
        }

        [Fact]
        public void Charge_opener_dances_to_battle_stance_then_charges()
        {
            var fs = new WarriorSettings();
            fs.UseGapClosers.Value = true; // opt-in (off by default)
            FakeGameClient game = WarriorGame(); // Berserker stance (Fury home)
            game.TargetUnit.Distance = 15;
            game.KnownSpells.Add("Charge"); // Intercept unlearned

            var engine = new RotationEngine(new SoloFury(fs).BuildSteps());

            // Not in Battle Stance yet → the dance switches to Battle Stance first (it doesn't Charge).
            Assert.Equal("Charge", engine.Tick(CombatContext.Capture(game))?.Name);
            Assert.Contains("Battle Stance", game.CastLog);
            Assert.DoesNotContain("Charge", game.CastLog);

            // Now in Battle Stance → it actually Charges (and out-prioritises EnsureStance reverting).
            game.StanceName = "Battle Stance";
            game.CastLog.Clear();
            engine.Tick(CombatContext.Capture(game));
            Assert.Contains("Charge", game.CastLog);
        }

        [Fact]
        public void Intercepts_a_distant_target_when_learned_and_rage_available()
        {
            var fs = new WarriorSettings();
            fs.UseGapClosers.Value = true;
            FakeGameClient game = WarriorGame();
            game.TargetUnit.Distance = 15;
            game.MeUnit.Rage = 50;
            game.KnownSpells.Add("Intercept"); // learned → Intercept wins over Charge

            Assert.Equal("Intercept", Fire(game, new SoloFury(fs))?.Name);
        }

        [Fact]
        public void Intercept_is_throttled_against_an_immediate_refire()
        {
            var fs = new WarriorSettings();
            fs.UseGapClosers.Value = true;
            FakeGameClient game = WarriorGame();
            game.TargetUnit.Distance = 15;
            game.MeUnit.Rage = 50;
            game.KnownSpells.Add("Intercept");

            // Same engine across ticks so the step keeps its recast-throttle state.
            var engine = new RotationEngine(new SoloFury(fs).BuildSteps());
            Assert.Equal("Intercept", engine.Tick(CombatContext.Capture(game))?.Name); // first fires
            Assert.Null(engine.Tick(CombatContext.Capture(game)));                      // throttled within 1s
        }

        [Fact]
        public void Execute_fires_below_20_percent()
        {
            FakeGameClient game = WarriorGame();
            game.TargetUnit.HealthPercent = 15;

            Assert.Equal("Execute", Fire(game)?.Name);
        }

        [Fact]
        public void Uses_AoE_with_two_enemies_in_range()
        {
            FakeGameClient game = WarriorGame();
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });
            // Neutralise the higher-priority single-target catches (incl. Whirlwind filler) so AoE is reached.
            game.SpellsOnCooldown.Add("Victory Rush");
            game.SpellsOnCooldown.Add("Whirlwind");
            game.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 99999);
            game.TargetUnit.WithAura("Demoralizing Shout"); // already debuffed → don't pre-empt with the shout

            Assert.Equal("Thunder Clap", Fire(game)?.Name);
        }

        [Fact]
        public void Whirlwind_is_a_single_target_filler()
        {
            // No proc/strike up and no rage for Bloodthirst/Death Wish → Whirlwind fills the gap,
            // even against a lone target (it is a solid instant on its own cooldown).
            FakeGameClient game = WarriorGame();
            game.SpellsOnCooldown.Add("Bloodthirst");
            game.SpellsOnCooldown.Add("Victory Rush");
            game.TargetUnit.WithAura("Rend", mine: true, timeLeftMs: 99999);

            Assert.Equal("Whirlwind", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_fires_on_an_elite_when_missing()
        {
            // Free the higher-priority strikes/cooldowns so the survival-debuff slot is reached.
            FakeGameClient game = WarriorGame();
            game.SpellsOnCooldown.Add("Bloodthirst");
            game.SpellsOnCooldown.Add("Recklessness"); // elite would otherwise trigger the DPS cooldown first
            game.TargetUnit.IsElite = true;

            Assert.Equal("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_is_skipped_on_lone_trash()
        {
            // Single non-elite, non-boss below the AoE threshold → not worth a global.
            FakeGameClient game = WarriorGame();
            game.SpellsOnCooldown.Add("Bloodthirst");

            Assert.NotEqual("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Casts_Battle_Shout_when_missing()
        {
            FakeGameClient game = WarriorGame();
            game.MeUnit.Auras.Remove("Battle Shout");

            Assert.Equal("Battle Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Skips_Battle_Shout_when_Greater_Blessing_of_Might_is_up()
        {
            FakeGameClient game = WarriorGame();
            game.MeUnit.Auras.Remove("Battle Shout");
            game.MeUnit.WithAura("Greater Blessing of Might");

            Assert.NotEqual("Battle Shout", Fire(game)?.Name);
        }

        [Theory]
        [InlineData(10, false)] // below the reserve → keep the rage
        [InlineData(25, true)]  // spare rage → dump it (off the GCD, even during the GCD)
        public void Heroic_Strike_dumps_spare_rage_off_gcd(int rage, bool shouldFire)
        {
            FakeGameClient game = WarriorGame();
            game.Gcd = 1000;           // GCD active → only off-GCD steps may fire
            game.MeUnit.Rage = rage;

            RotationStep fired = Fire(game);
            Assert.Equal(shouldFire ? "Heroic Strike" : null, fired?.Name);
        }
    }
}
