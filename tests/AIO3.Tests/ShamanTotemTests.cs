using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Shaman;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>
    /// The totem layer is the hard part: the school→name map, the "up and useful within range" check, the drop
    /// gate (engaging a fight + stationary + not mounted), and the situational/temporary totems. Tests run the
    /// real engine on a FakeGameClient and assert which step fires (lower priority wins).
    /// </summary>
    public class ShamanTotemTests
    {
        // A shaman engaging a fight, stationary, in melee on a full-health dummy. Imbues + shield are up so the
        // buff slots stay quiet, leaving the totem steps to win. Caller adds totems via TotemList.
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Shaman };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 5,
                HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.ProductFightingFlag = true; // engaging a fight (Fighting(ctx) true) but not yet "in combat"
            g.AutoAttacking = true; // already swinging → the off-GCD Enhancement AutoAttack step stays inert
            // Buffs up so they don't preempt the totem band.
            g.MeUnit.WithAura("Water Shield");
            g.MeUnit.WithAura("Lightning Shield");
            g.WeaponEnchantState = new WeaponEnchant(true, 600000, true, 600000); // both hands imbued
            return g;
        }

        private static FakeUnit Totem(string name, float distance) =>
            new FakeUnit { Name = name, Distance = distance, Reaction = Reaction.Friendly };

        // The totem tests are about the totem layer: switch off the offensive racials, Feral Spirit, and the
        // interrupt so they don't mask which totem step fires. Each test re-enables what it specifically needs.
        private static ShamanSettings Defaults()
        {
            var s = new ShamanSettings();
            s.UseRacials.Value = false;
            s.InterruptCasts.Value = false;
            s.FeralSpirit.Value = "None";
            return s;
        }

        private static RotationStep Enh(FakeGameClient g, ShamanSettings s = null) =>
            new RotationEngine(new SoloEnhancement(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        private static RotationStep Ele(FakeGameClient g, ShamanSettings s = null) =>
            new RotationEngine(new SoloElemental(s ?? Defaults()).BuildSteps()).Tick(CombatContext.Capture(g));

        // --- school map: the auto pick scales by what's known ---

        [Fact]
        public void Enhancement_fire_school_prefers_magma_then_searing()
        {
            FakeGameClient g = Game();
            // No fire totem up → the fire-school drop fires. Magma known → it's the pick.
            Assert.Equal("Totem: Fire", Enh(g)?.Name);
            Assert.Contains("Magma Totem", g.CastLog);
        }

        [Fact]
        public void Enhancement_fire_falls_back_to_searing_when_magma_unknown()
        {
            FakeGameClient g = Game();
            g.UnknownSpells.Add("Magma Totem");
            Assert.Equal("Totem: Fire", Enh(g)?.Name);
            Assert.Contains("Searing Totem", g.CastLog);
        }

        [Fact]
        public void Elemental_fire_school_prefers_totem_of_wrath()
        {
            FakeGameClient g = Game();
            g.TargetUnit.WithAura("Flame Shock", mine: true, timeLeftMs: 60000); // FS up so its maintain doesn't preempt
            g.SpellsOnCooldown.Add("Lava Burst"); // ...and the FS synergy nuke is down → isolate the totem drop
            Assert.Equal("Totem: Fire", Ele(g)?.Name);
            Assert.Contains("Totem of Wrath", g.CastLog);
        }

        [Fact]
        public void Per_school_choice_can_force_a_specific_totem()
        {
            FakeGameClient g = Game();
            var s = Defaults();
            s.FireTotem.Value = "Searing Totem"; // force Searing even though Magma is known
            Assert.Equal("Totem: Fire", Enh(g, s)?.Name);
            Assert.Contains("Searing Totem", g.CastLog);
            Assert.DoesNotContain("Magma Totem", g.CastLog);
        }

        [Fact]
        public void Per_school_choice_none_skips_the_school()
        {
            FakeGameClient g = Game();
            var s = Defaults();
            s.FireTotem.Value = "None";
            // Fire skipped → the next school (earth) drops instead.
            Assert.Equal("Totem: Earth", Enh(g, s)?.Name);
            Assert.DoesNotContain("Magma Totem", g.CastLog);
        }

        // --- up-and-useful within range ---

        [Fact]
        public void Does_not_redrop_a_school_whose_totem_is_up_and_in_range()
        {
            FakeGameClient g = Game();
            g.TotemList.Add(Totem("Magma Totem", 5f)); // fire up and close → fire school satisfied
            // Earth is now the first unsatisfied school.
            Assert.Equal("Totem: Earth", Enh(g)?.Name);
        }

        [Fact]
        public void Redrops_a_school_whose_only_totem_is_left_behind_out_of_range()
        {
            FakeGameClient g = Game();
            g.TotemList.Add(Totem("Magma Totem", ShamanCommon.TotemUsefulRange + 5f)); // left behind → not useful
            Assert.Equal("Totem: Fire", Enh(g)?.Name);
            Assert.Contains("Magma Totem", g.CastLog); // re-drops a fresh one (game replaces the distant fire slot)
        }

        // --- the drop gate ---

        [Fact]
        public void Does_not_drop_totems_while_moving()
        {
            FakeGameClient g = Game();
            g.Moving = true; // totems are stationary — never plant mid-run
            RotationStep fired = Enh(g);
            Assert.DoesNotContain("Totem:", fired?.Name ?? "");
        }

        [Fact]
        public void Does_not_drop_totems_while_mounted()
        {
            FakeGameClient g = Game();
            g.Mounted = true;
            RotationStep fired = Enh(g);
            Assert.DoesNotContain("Totem:", fired?.Name ?? "");
        }

        [Fact]
        public void Does_not_drop_totems_when_not_engaging_a_fight()
        {
            FakeGameClient g = Game();
            g.ProductFightingFlag = false; // idle: not fighting and not in combat
            g.InCombatFlag = false;
            RotationStep fired = Enh(g);
            Assert.DoesNotContain("Totem:", fired?.Name ?? "");
        }

        [Fact]
        public void Drops_totems_when_already_in_combat_even_if_product_flag_is_off()
        {
            FakeGameClient g = Game();
            g.ProductFightingFlag = false;
            g.InCombatFlag = true;        // Fighting(ctx) holds via PlayerInCombat (product flag off)
            g.TargetUnit.Distance = 5;    // in melee → the near-target totem gate is satisfied
            // A fire totem still goes down purely off the PlayerInCombat path (at melee the offensive redeploy wins).
            Assert.Equal("Redeploy fire totem", Enh(g)?.Name);
            Assert.Contains("Magma Totem", g.CastLog);
        }

        [Fact]
        public void Enhancement_holds_school_totems_until_near_the_target()
        {
            // Talamin: don't plant totems mid-run. In combat but still far from the target (>15y) → NO school totem
            // (else it plants far, then re-drops at melee = mana loss); once near the target it plants.
            FakeGameClient g = Game();
            g.InCombatFlag = true;
            g.TotemList.Add(Totem("Magma Totem", 5)); // fire up → isolate a non-fire school drop from the fire redeploy
            g.TargetUnit.Distance = 30;   // still sprinting in
            Assert.DoesNotContain("Totem:", Enh(g)?.Name ?? "");

            g.TargetUnit.Distance = 8;    // arrived near the mob
            Assert.Contains("Totem:", Enh(g)?.Name ?? "");
        }

        // --- situational / temporary totems ---

        [Fact]
        public void Mana_tide_when_low_on_mana_in_combat()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = true;
            g.MeUnit.PowerPercent = 28; // below Mana Tide's 30% but above Shamanistic Rage's 25% → Mana Tide wins
            Assert.Equal("Mana Tide Totem", Enh(g)?.Name);
        }

        [Fact]
        public void Earth_elemental_on_a_pure_pack_on_us()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = true;
            g.TargetUnit.IsTargetingMe = true;
            // three attackers on us within the surround radius → Earth Elemental (defensive CD)
            for (ulong i = 2; i <= 3; i++)
                g.EnemyList.Add(new FakeUnit { Guid = i, Reaction = Reaction.Hostile, IsAttackable = true, IsTargetingMe = true, Distance = 5, HealthPercent = 100 });
            Assert.Equal("Earth Elemental Totem", Enh(g)?.Name);
        }

        [Fact]
        public void Stoneclaw_on_a_smaller_pack_when_no_earth_elemental()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = true;
            g.TargetUnit.IsTargetingMe = true;
            g.EnemyList.Add(new FakeUnit { Guid = 2, Reaction = Reaction.Hostile, IsAttackable = true, IsTargetingMe = true, Distance = 5, HealthPercent = 100 });
            var s = Defaults();
            s.UseEarthElemental.Value = false; // isolate Stoneclaw (2 attackers, no Earth Elemental)
            Assert.Equal("Stoneclaw Totem", Enh(g, s)?.Name);
        }

        [Fact]
        public void Grounding_against_a_nearby_caster()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = true;
            g.TargetUnit.IsCasting = true; // an enemy caster near us
            var s = Defaults();            // interrupt already off in Defaults → Wind Shear won't take the cast first
            Assert.Equal("Grounding Totem", Enh(g, s)?.Name);
        }

        [Fact]
        public void Redeploy_fire_totem_in_combat_when_target_close_and_no_fire_up()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = true;
            g.TargetUnit.Distance = 10; // within the fire-totem target range
            var s = Defaults();
            // No fire totem present → the redeploy (prio 1.55) beats the standard fire-school drop (prio 2.5).
            Assert.Equal("Redeploy fire totem", Enh(g, s)?.Name);
            Assert.Contains("Magma Totem", g.CastLog);
        }

        [Fact]
        public void Totemic_recall_when_a_totem_is_left_behind_far_and_no_temporary_up()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = false;       // recall is out-of-combat cleanup
            g.ProductFightingFlag = false; // ...and not engaging (so the school drops don't preempt)
            g.TotemList.Add(Totem("Magma Totem", ShamanCommon.TotemRecallRange + 5f)); // a totem far away
            Assert.Equal("Totemic Recall", Enh(g)?.Name);
        }

        [Fact]
        public void Totemic_recall_holds_while_a_temporary_totem_is_up()
        {
            FakeGameClient g = Game();
            g.InCombatFlag = false;
            g.ProductFightingFlag = false;
            g.TotemList.Add(Totem("Magma Totem", ShamanCommon.TotemRecallRange + 5f));
            g.TotemList.Add(Totem("Mana Tide Totem", 3f)); // a temporary totem is up → don't recall (would pull it)
            Assert.NotEqual("Totemic Recall", Enh(g)?.Name);
        }
    }
}
