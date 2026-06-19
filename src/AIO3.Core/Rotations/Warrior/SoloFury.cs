using System.Collections.Generic;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Fury warrior (Layer 4). Thin: it composes Layer 3 baseline blocks and adds the
    /// class-specific filler in priority order (lower priority value = evaluated first).
    /// Ported from the old AIO Combat/Warrior/SoloFury.cs, which is the authoritative
    /// reference for what the fightclass must do (everything the WRobot product does not).
    ///
    /// Unknown/unusable spells are skipped automatically (IsSpellKnown/IsSpellReady gate),
    /// so this runs fine on a low-level warrior — only learned abilities fire.
    ///
    /// DEFERRED (next iterations): settings-gated Hamstring / Piercing Howl, and creature-type
    /// checks (e.g. Rend not on Elementals) once a creature-type capability exists.
    /// </summary>
    public sealed class SoloFury : IRotation
    {
        public string Name => "Warrior - Solo Fury";

        private const string BerserkerStance = "Berserker Stance";

        // Live settings shared with the in-game overlay (edits take effect immediately).
        private readonly FurySettings _settings;

        public SoloFury() : this(new FurySettings()) { }

        public SoloFury(FurySettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => new List<RotationStep>
        {
            // --- baseline / upkeep (off the GCD) ---

            // Ensure Berserker Stance once it is learned (no-op before level 30 / if unknown).
            Skill.Spell(BerserkerStance).Priority(0.1f).On(Targets.Self)
                 .When(ctx => ctx.Game.ActiveStanceName != BerserkerStance).OffGcd(),

            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Pummel", priority: 2f),

            // Keep Battle Shout up — but not if a paladin's Greater Blessing of Might already covers it.
            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),

            // Bloodrage to build rage when we have an attackable target in melee (off the GCD).
            Skill.Spell("Bloodrage").Priority(5f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.Distance < 8f).OffGcd(),

            // --- single target ---

            // Instant Slam from the Bloodsurge proc ("Slam!").
            Skill.Spell("Slam").Priority(6f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Slam!")),

            // NOTE: the old rotation gated Bloodthirst on HealthPercent <= 80, which looks like a
            // quirk (it would skip BT at full health). Mirrored faithfully for now — flag to revisit.
            Skill.Spell("Bloodthirst").Priority(7f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > 30 && ctx.Me.HealthPercent <= 80),

            Skill.Spell("Death Wish").Priority(8f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Me.Rage > 10),

            Skill.Spell("Execute").Priority(9f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.HealthPercent < 20),

            // Usable only after a killing blow; IsSpellReady gates the proc window.
            Skill.Spell("Victory Rush").Priority(10f).On(Targets.CurrentEnemy),

            Skill.Spell("Rend").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Target.HasMyAura("Rend")),

            // --- gap-closers (target out of melee, 8–25y) ---

            // Intercept: in-combat closer (Berserker stance). Costs rage.
            Skill.Spell("Intercept").Priority(12f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseGapClosers.Value
                              && ctx.Me.Rage > 10 && ctx.Target.Distance > 8f && ctx.Target.Distance <= 25f),

            // Charge: opener / closer used when Intercept isn't learned yet (low level / Battle stance).
            Skill.Spell("Charge").Priority(13f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseGapClosers.Value
                              && ctx.Target.Distance > 8f && ctx.Target.Distance <= 25f
                              && !ctx.Game.IsSpellKnown("Intercept")),

            // --- AoE: configurable enemy threshold within 10 yards ---

            Skill.Spell("Thunder Clap").Priority(14f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Whirlwind").Priority(15f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Cleave").Priority(16f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value).OffGcd(),

            // Rage dump (off the GCD, on next swing). Fires as soon as we have spare rage above
            // the reserve — at low level that means promptly, since nothing else needs the rage.
            Skill.Spell("Heroic Strike").Priority(17f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > _settings.HeroicStrikeRageReserve.Value).OffGcd(),
        };
    }
}
