using System.Collections.Generic;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Arms warrior. Mortal Strike is the signature ability; Overpower fires on its proc
    /// window (gated by IsSpellUsable). Composes the shared WarriorCommon blocks. Ported from the
    /// old Combat/Warrior/SoloArms.cs (boss/spread-Rend specifics deferred).
    /// </summary>
    public sealed class SoloArms : IRotation
    {
        public string Name => "Warrior - Solo Arms";

        private readonly WarriorSettings _settings;

        public SoloArms() : this(new WarriorSettings()) { }

        public SoloArms(WarriorSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => new List<RotationStep>
        {
            // --- baseline / upkeep ---
            WarriorCommon.EnsureStance("Battle Stance", priority: 0.1f),
            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Pummel", priority: 2f),
            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),
            WarriorCommon.BerserkerRage(priority: 4f),
            CombatBlocks.DefensiveBelow("Enraged Regeneration", healthPercent: 50, priority: 4.5f),
            WarriorCommon.Bloodrage(priority: 5f),

            // --- single target ---
            // Overpower: usable only in its proc window (after a dodge / Taste for Blood).
            Skill.Spell("Overpower").Priority(6f).On(Targets.CurrentEnemy),
            WarriorCommon.Execute(priority: 7f),
            // Mortal Strike: the Arms core strike, on cooldown.
            Skill.Spell("Mortal Strike").Priority(8f).On(Targets.CurrentEnemy),
            WarriorCommon.Rend(priority: 9f),
            WarriorCommon.VictoryRush(priority: 10f),
            // Slam filler when we have spare rage (cast-while-moving is blocked by the adapter).
            Skill.Spell("Slam").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > 20),

            // --- gap-closers + utility ---
            WarriorCommon.Intercept(_settings, priority: 12f),
            WarriorCommon.Charge(_settings, priority: 13f),
            WarriorCommon.Hamstring(_settings, priority: 13.5f),

            // --- AoE ---
            Skill.Spell("Sweeping Strikes").Priority(14f).On(Targets.Self)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value).OffGcd(),
            Skill.Spell("Bladestorm").Priority(15f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Thunder Clap").Priority(16f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            WarriorCommon.Cleave(_settings, priority: 16.5f),

            // --- rage dump ---
            WarriorCommon.HeroicStrike(_settings, priority: 17f),
        };
    }
}
