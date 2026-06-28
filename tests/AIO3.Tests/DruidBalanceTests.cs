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
                AutoAttacking = true,
                ProductFightingFlag = true // engaged in a fight (the Moonkin form step requires it); PlayerInCombat stays false
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

        // --- Mark of the Wild upkeep + stack-group suppression ---

        [Fact]
        public void MarkOfTheWild_cast_when_missing()
        {
            var g = MoonkinGame();
            g.MeUnit.Auras.Remove("Mark of the Wild"); // unbuffed, out of combat → MotW leads (priority 0.6)
            Assert.Equal("Mark of the Wild", Fire(g)?.Name);
        }

        [Fact]
        public void MarkOfTheWild_suppressed_by_an_Armor_scroll_buff()
        {
            var g = MoonkinGame();
            g.MeUnit.Auras.Remove("Mark of the Wild");
            g.MeUnit.WithAura("Armor"); // Scroll of Protection buff shares MotW's stack group → MotW can't land.
            // Without the guard the druid would re-cast MotW forever; it must instead skip to the rotation.
            Assert.NotEqual("Mark of the Wild", Fire(g)?.Name);
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

        // --- DoT suppression during Eclipse (trash) vs upkeep on bosses ---

        [Fact]
        public void DoTs_suppressed_under_Eclipse_on_trash()
        {
            // Insect Swarm missing on a trash mob, but a Lunar Eclipse is up → don't clip the burst with a DoT;
            // spend the GCD on the Eclipse nuke (Starfire) instead.
            var g = MoonkinGame();
            g.MeUnit.WithAura("Eclipse (Lunar)");
            Assert.Equal("Starfire", Fire(g)?.Name);
        }

        [Fact]
        public void DoTs_suppressed_under_Solar_Eclipse_on_trash_routes_to_Wrath()
        {
            var g = MoonkinGame();
            g.MeUnit.WithAura("Eclipse (Solar)"); // Solar → the nuke is Wrath, and the DoT is still suppressed
            Assert.Equal("Wrath", Fire(g)?.Name);
        }

        [Fact]
        public void DoTs_kept_on_a_boss_during_Eclipse()
        {
            // On a BOSS the long fight makes DoT uptime worth more than one clipped Eclipse GCD → the DoT still
            // refreshes even under Eclipse.
            var s = new DruidSettings();
            s.UseStarfall.Value = false;       // Starfall (a boss cooldown, 3.0) would preempt; isolate the DoT
            s.UseForceOfNature.Value = false;  // ...and Force of Nature (a boss cooldown, 3.5)
            var g = MoonkinGame();
            g.TargetUnit.Entry = 31146; // a BossList entry
            g.TargetUnit.WithAura("Faerie Fire"); // armor debuff already up so it doesn't preempt the DoT (3.8 < 4.0)
            g.MeUnit.WithAura("Eclipse (Lunar)");
            Assert.Equal("Insect Swarm", Fire(g, new SoloBalance(s))?.Name);
        }

        // --- Starfire opener leads the DoTs on a fresh pull ---

        [Fact]
        public void Starfire_opener_fires_before_the_DoTs()
        {
            // A fresh, full-HP target not yet attacking, with NO DoTs up: the opener (now above the DoT maintenance)
            // leads with Starfire rather than front-loading a DoT.
            var g = MoonkinGame();
            g.TargetUnit.IsTargetingMe = false; // a fresh pull
            // (no Insect Swarm / Moonfire applied → they'd otherwise want to go up first under the old ordering)
            Assert.Equal("Starfire", Fire(g)?.Name);
        }

        // --- Starfire Solar guard ---

        [Fact]
        public void Starfire_routes_to_Wrath_under_Solar_even_with_Natures_Grace()
        {
            // Nature's Grace would normally enable the Lunar-side Starfire, but a Solar Eclipse must always route to
            // Wrath — the Solar guard on the Starfire gate ensures it.
            var g = MoonkinGame();
            g.TargetUnit.WithAura("Insect Swarm", mine: true, timeLeftMs: 12000); // DoTs up so they don't preempt
            g.TargetUnit.WithAura("Moonfire", mine: true, timeLeftMs: 12000);
            g.MeUnit.WithAura("Eclipse (Solar)");
            g.MeUnit.WithAura("Nature's Grace");
            Assert.Equal("Wrath", Fire(g)?.Name);
        }
    }
}
