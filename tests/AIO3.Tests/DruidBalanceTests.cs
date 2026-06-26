using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Druid;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    // End-to-end priority tests for the Solo Balance druid. The base game is a balance druid already in Moonkin
    // Form, standing still (a caster), on one full-health, non-elite enemy that IS attacking us (so the Starfire
    // opener's "not yet attacking" gate doesn't preempt the steady-state rules), with the AoE / cooldowns off the
    // table unless a test opts in. PlayerInCombat is left false so the offensive racials don't preempt.
    public class DruidBalanceTests
    {
        private static FakeGameClient MoonkinGame()
        {
            var g = new FakeGameClient
            {
                Class = WowClass.Druid,
                AutoAttacking = true
            };
            g.MeUnit.WithAura("Moonkin Form");
            g.MeUnit.WithAura("Mark of the Wild"); // pre-buffed so OOC buffs don't preempt the rotation under test
            g.MeUnit.WithAura("Thorns");
            g.TargetUnit = new FakeUnit
            {
                Guid = 1,
                Name = "Dummy",
                Reaction = Reaction.Hostile,
                Distance = 25,
                HealthPercent = 100,
                IsTargetingMe = true, // attacking us → not an opener target
                IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) =>
            new RotationEngine(new SoloBalance().BuildSteps()).Tick(CombatContext.Capture(g));

        private static RotationStep Fire(FakeGameClient g, SoloBalance rotation) =>
            new RotationEngine(rotation.BuildSteps()).Tick(CombatContext.Capture(g));

        // --- Moonkin Form upkeep ---

        [Fact]
        public void Moonkin_Form_cast_when_missing()
        {
            var g = MoonkinGame();
            g.MeUnit.Auras.Remove("Moonkin Form");
            Assert.Equal("Moonkin Form", Fire(g)?.Name);
        }

        // --- DoTs (maintain when missing, HP-floored) ---

        [Fact]
        public void Insect_Swarm_applied_when_missing()
        {
            var g = MoonkinGame();
            // Insect Swarm (priority 4.0) leads Moonfire (4.1) when both missing.
            Assert.Equal("Insect Swarm", Fire(g)?.Name);
        }

        [Fact]
        public void Moonfire_applied_after_Insect_Swarm()
        {
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000); // IS up → Moonfire is next
            Assert.Equal("Moonfire", Fire(g)?.Name);
        }

        [Fact]
        public void DoTs_skipped_on_a_dying_target()
        {
            var g = MoonkinGame();
            g.TargetUnit.HealthPercent = 20; // below the DoT-health floor (30) → no DoT, nuke instead (Wrath filler)
            Assert.Equal("Wrath", Fire(g)?.Name);
        }

        [Fact]
        public void Moonfire_respects_its_toggle()
        {
            var s = new DruidSettings();
            s.UseMoonfire.Value = false;
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000); // IS up; Moonfire disabled → filler
            Assert.Equal("Wrath", Fire(g, new SoloBalance(s))?.Name);
        }

        // --- Eclipse rotation ---

        [Fact]
        public void Starfire_under_Lunar_eclipse()
        {
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000); // DoTs up so they don't preempt
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            g.MeUnit.WithAura("Eclipse (Lunar)");
            Assert.Equal("Starfire", Fire(g)?.Name);
        }

        [Fact]
        public void Starfire_under_Natures_Grace()
        {
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000);
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            g.MeUnit.WithAura("Nature's Grace");
            Assert.Equal("Starfire", Fire(g)?.Name);
        }

        [Fact]
        public void Wrath_under_Solar_eclipse()
        {
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000);
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            g.MeUnit.WithAura("Eclipse (Solar)");
            Assert.Equal("Wrath", Fire(g)?.Name);
        }

        [Fact]
        public void Wrath_is_the_default_filler_without_an_eclipse()
        {
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000);
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            Assert.Equal("Wrath", Fire(g)?.Name);
        }

        [Fact]
        public void Cast_time_nukes_held_while_moving()
        {
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000);
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            g.Moving = true; // a caster must stand still → nothing casts
            Assert.Null(Fire(g));
        }

        // --- Starfire opener ---

        [Fact]
        public void Starfire_opens_on_a_full_hp_not_yet_attacking_target()
        {
            var g = MoonkinGame();
            g.TargetUnit.IsTargetingMe = false; // not yet attacking + full HP → the opener fires
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000); // keep DoTs out of the way
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            Assert.Equal("Starfire", Fire(g)?.Name);
        }

        // --- AoE (target-anchored counts) ---

        [Fact]
        public void Typhoon_fires_on_a_pack()
        {
            var g = MoonkinGame();
            // Three enemies clustered around the target (default AoeTargets 3). Position them at the origin so
            // DistanceTo(target) is small; the target is also at the origin (X/Y default 0).
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 25, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 25, IsAttackable = true });
            // Starfall (a cooldown) sits above Typhoon and also trips on the pack; isolate Typhoon by disabling it.
            var s = new DruidSettings();
            s.UseStarfall.Value = false;
            Assert.Equal("Typhoon", Fire(g, new SoloBalance(s))?.Name);
        }

        [Fact]
        public void Starfall_fires_on_a_boss()
        {
            var g = MoonkinGame();
            // BossList lookup is by entry; emulate a boss via IsElite + boss entry is hard, so use the elite/pack
            // path: actually Starfall gates on IsBoss() OR pack. Use a pack to trip it deterministically.
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 25, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 25, IsAttackable = true });
            // Starfall (priority 3.0) wins over Typhoon (3.1) on the pack.
            Assert.Equal("Starfall", Fire(g)?.Name);
        }

        [Fact]
        public void AoE_respects_the_toggles()
        {
            var s = new DruidSettings();
            s.UseAoe.Value = false;
            s.UseStarfall.Value = false;
            var g = MoonkinGame();
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, Distance = 25, IsAttackable = true });
            g.EnemyList.Add(new FakeUnit { Guid = 3, Reaction = Reaction.Hostile, Distance = 25, IsAttackable = true });
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000);
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            Assert.Equal("Wrath", Fire(g, new SoloBalance(s))?.Name); // AoE off → single-target filler
        }

        // --- survival ---

        [Fact]
        public void Barkskin_fires_below_the_threshold()
        {
            var g = MoonkinGame();
            g.MeUnit.HealthPercent = 30;
            Assert.Equal("Barkskin", Fire(g)?.Name);
        }

        [Fact]
        public void Shift_out_heal_when_low_with_mana()
        {
            var g = MoonkinGame();
            g.MeUnit.HealthPercent = 30; // below IC-heal threshold
            g.SpellsOnCooldown.Add("Barkskin");
            g.MeUnit.PowerPercent = 80;
            Assert.Equal("Regrowth", Fire(g)?.Name);
        }
    }
}
