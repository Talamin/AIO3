using System.Collections.Generic;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Fury warrior. Thin: composes shared <see cref="WarriorCommon"/> / Layer 3 blocks and adds
    /// the Fury-specific filler in priority order. Ported from the old Combat/Warrior/SoloFury.cs.
    /// Unknown/unusable spells are skipped automatically, so it runs fine while leveling.
    /// </summary>
    public sealed class SoloFury : IRotation
    {
        public string Name => "Warrior - Solo Fury";

        private readonly WarriorSettings _settings;

        public SoloFury() : this(new WarriorSettings()) { }

        public SoloFury(WarriorSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => new List<RotationStep>
        {
            // --- baseline / upkeep ---
            WarriorCommon.EnsureStance("Berserker Stance", priority: 0.1f),
            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Pummel", priority: 2f),
            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),
            WarriorCommon.BerserkerRage(priority: 4f),
            CombatBlocks.DefensiveBelow("Enraged Regeneration", healthPercent: 50, priority: 4.5f),
            WarriorCommon.Bloodrage(priority: 5f),

            // --- single target ---
            // Instant Slam from the Bloodsurge proc ("Slam!").
            Skill.Spell("Slam").Priority(6f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Slam!")),

            // NOTE: old rotation gated Bloodthirst on HealthPercent <= 80 (a quirk). Kept on request.
            Skill.Spell("Bloodthirst").Priority(7f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > 30 && ctx.Me.HealthPercent <= 80),

            Skill.Spell("Death Wish").Priority(8f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Me.Rage > 10),

            WarriorCommon.Execute(priority: 9f),
            WarriorCommon.VictoryRush(priority: 10f),
            WarriorCommon.Rend(priority: 11f),

            // --- gap-closers + utility ---
            WarriorCommon.Intercept(_settings, priority: 12f),
            WarriorCommon.Charge(_settings, priority: 13f),
            WarriorCommon.Hamstring(_settings, priority: 13.5f),

            // --- AoE (>= AoeThreshold enemies within 10y) ---
            // Piercing Howl: AoE slow for a fleeing pack (mirrors the old <40% + pack gating).
            Skill.Spell("Piercing Howl").Priority(13.8f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.HealthPercent < 40
                              && ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Thunder Clap").Priority(14f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Whirlwind").Priority(15f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            WarriorCommon.Cleave(_settings, priority: 16f),

            // --- rage dump ---
            WarriorCommon.HeroicStrike(_settings, priority: 17f),
        };
    }
}
