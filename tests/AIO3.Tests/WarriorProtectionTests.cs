using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class WarriorProtectionTests
    {
        // Protection warrior in Defensive stance, auto-attacking, upkeep neutralised. Shield Block is
        // an off-GCD mitigation that would otherwise win; put on cooldown to isolate threat abilities.
        private static FakeGameClient ProtGame()
        {
            var game = new FakeGameClient
            {
                Class = WowClass.Warrior,
                StanceName = "Defensive Stance",
                AutoAttacking = true
            };
            game.SpellsOnCooldown.Add("Bloodrage");
            game.SpellsOnCooldown.Add("Shield Block");
            game.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 5,
                HealthPercent = 100
            };
            game.EnemyList.Add(game.TargetUnit);
            game.MeUnit.WithAura("Battle Shout", mine: true);
            return game;
        }

        private static RotationStep Fire(FakeGameClient game) =>
            new RotationEngine(new SoloProtection().BuildSteps()).Tick(CombatContext.Capture(game));

        private static RotationStep Fire(FakeGameClient game, WarriorSettings settings) =>
            new RotationEngine(new SoloProtection(settings).BuildSteps()).Tick(CombatContext.Capture(game));

        [Fact]
        public void Switches_to_Defensive_Stance_when_not_in_it()
        {
            FakeGameClient game = ProtGame();
            game.StanceName = "Battle Stance";

            Assert.Equal("Defensive Stance", Fire(game)?.Name);
        }

        [Fact]
        public void Shield_Slam_is_the_threat_core()
        {
            FakeGameClient game = ProtGame();

            Assert.Equal("Shield Slam", Fire(game)?.Name);
        }

        [Fact]
        public void Last_Stand_fires_when_critically_low()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.HealthPercent = 10;

            Assert.Equal("Last Stand", Fire(game)?.Name);
        }

        [Fact]
        public void Shield_Wall_fires_when_low()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.HealthPercent = 30; // below 35, above Last Stand's 15

            Assert.Equal("Shield Wall", Fire(game)?.Name);
        }

        [Fact]
        public void Interrupts_a_casting_enemy_with_Shield_Bash()
        {
            FakeGameClient game = ProtGame();
            game.TargetUnit.IsCasting = true;
            game.TargetUnit.CastingSpellId = 42;

            Assert.Equal("Shield Bash", Fire(game)?.Name);
        }

        [Fact]
        public void Devastate_is_the_filler_when_learned()
        {
            // Default whitelist (all known) → Devastate is learned, so it is the armor/threat filler
            // and Sunder Armor must not also fire. Shield Slam/Revenge are put on cooldown to expose it.
            FakeGameClient game = ProtGame();
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.SpellsOnCooldown.Add("Concussion Blow"); // sits above the filler when off cooldown

            Assert.Equal("Devastate", Fire(game)?.Name);
        }

        [Fact]
        public void Sunder_Armor_only_runs_before_Devastate_is_learned()
        {
            FakeGameClient game = ProtGame();
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            // Whitelist that excludes Devastate (and includes the upkeep/baseline spells already up).
            game.KnownSpells.Add("Sunder Armor");
            game.KnownSpells.Add("Auto Attack");

            // Devastate unknown → Sunder Armor takes over the filler slot.
            Assert.Equal("Sunder Armor", Fire(game)?.Name);
        }

        [Fact]
        public void Sunder_Armor_is_suppressed_once_Devastate_is_known()
        {
            FakeGameClient game = ProtGame();
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.SpellsOnCooldown.Add("Devastate"); // learned but on cooldown
            // Both known; the !IsSpellKnown("Devastate") gate must keep Sunder Armor silent.
            game.KnownSpells.Add("Sunder Armor");
            game.KnownSpells.Add("Devastate");
            game.KnownSpells.Add("Auto Attack");

            Assert.NotEqual("Sunder Armor", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_fires_on_an_elite_when_missing()
        {
            FakeGameClient game = ProtGame();
            // Free the higher-priority threat core so the debuff slot is reached.
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.SpellsOnCooldown.Add("Devastate");
            game.SpellsOnCooldown.Add("Concussion Blow");
            game.TargetUnit.IsElite = true;

            Assert.Equal("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_is_skipped_on_lone_trash()
        {
            // Single non-elite, non-boss target below the AoE threshold → not worth a global.
            FakeGameClient game = ProtGame();
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.SpellsOnCooldown.Add("Devastate");
            game.SpellsOnCooldown.Add("Concussion Blow");

            Assert.NotEqual("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Demoralizing_Shout_is_not_reapplied_while_up()
        {
            FakeGameClient game = ProtGame();
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.SpellsOnCooldown.Add("Devastate");
            game.SpellsOnCooldown.Add("Concussion Blow");
            game.TargetUnit.IsElite = true;
            game.TargetUnit.WithAura("Demoralizing Shout"); // already debuffed

            Assert.NotEqual("Demoralizing Shout", Fire(game)?.Name);
        }

        [Fact]
        public void Uses_Thunder_Clap_AoE_when_threshold_met()
        {
            FakeGameClient game = ProtGame();
            // Free the single-target core above the AoE block.
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });
            game.TargetUnit.WithAura("Demoralizing Shout"); // already debuffed → don't pre-empt with the shout

            Assert.Equal("Thunder Clap", Fire(game)?.Name); // 2 enemies == default AoeThreshold
        }

        [Fact]
        public void AoE_threshold_setting_is_respected()
        {
            var fs = new WarriorSettings();
            fs.AoeThreshold.Value = 3; // require 3 enemies
            FakeGameClient game = ProtGame();
            game.SpellsOnCooldown.Add("Shield Slam");
            game.SpellsOnCooldown.Add("Revenge");
            game.SpellsOnCooldown.Add("Devastate"); // so a missed AoE doesn't just fall to the filler
            game.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 6 });

            RotationStep fired = Fire(game, fs); // only 2 enemies → below threshold
            Assert.NotEqual("Thunder Clap", fired?.Name);
            Assert.NotEqual("Shockwave", fired?.Name);
        }

        [Fact]
        public void Emergency_item_used_below_threshold()
        {
            FakeGameClient game = ProtGame();
            game.MeUnit.HealthPercent = 20; // below the default 30%
            game.ReadyItems.Add("Healthstone");

            Assert.Equal("Emergency heal", Fire(game)?.Name);
            Assert.Contains("Healthstone", game.UsedItems);
        }
    }
}
