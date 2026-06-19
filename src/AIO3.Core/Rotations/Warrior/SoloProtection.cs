using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Protection warrior. Defensive-stance survival + threat: Shield Slam (Sword and Board),
    /// Revenge, Devastate, with emergency defensives. Adapted from the old GroupProtection.cs for
    /// solo play (group threat tools like Taunt/Challenging Shout omitted).
    /// </summary>
    public sealed class SoloProtection : IRotation
    {
        public string Name => "Warrior - Solo Protection";

        private readonly WarriorSettings _settings;

        public SoloProtection() : this(new WarriorSettings()) { }

        public SoloProtection(WarriorSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => new List<RotationStep>
        {
            // --- survival + burst ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            CombatBlocks.OffensiveRacial("Blood Fury", 4.2f, ctx => _settings.UseRacials.Value),
            CombatBlocks.OffensiveRacial("Berserking", 4.21f, ctx => _settings.UseRacials.Value),

            // --- baseline / upkeep ---
            WarriorCommon.EnsureStance("Defensive Stance", priority: 0.1f),
            CombatBlocks.AutoAttack(priority: 1f),

            // --- emergency defensives ---
            CombatBlocks.DefensiveBelow("Last Stand", healthPercent: 15, priority: 1.5f),
            CombatBlocks.DefensiveBelow("Shield Wall", healthPercent: 35, priority: 1.6f),

            // Interrupt (Protection uses Shield Bash).
            CombatBlocks.Interrupt("Shield Bash", priority: 2f, mode: ctx => _settings.InterruptMode.Value),
            WarriorCommon.BerserkerRage(priority: 2.5f),

            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),

            // Shield Block while actively tanking (off the GCD).
            Skill.Spell("Shield Block").Priority(3.5f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.Distance < 8f).OffGcd(),

            WarriorCommon.Bloodrage(priority: 5f),
            WarriorCommon.Intercept(_settings, priority: 6f),
            WarriorCommon.Charge(_settings, priority: 6.5f),

            // --- threat core ---
            // Shield Slam: fires on the Sword and Board proc and on cooldown (IsSpellReady gates it).
            Skill.Spell("Shield Slam").Priority(7f).On(Targets.CurrentEnemy),
            // Revenge: usable in its proc window (after block/dodge/parry).
            Skill.Spell("Revenge").Priority(8f).On(Targets.CurrentEnemy),

            // --- AoE / control ---
            Skill.Spell("Thunder Clap").Priority(9f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Shockwave").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Concussion Blow").Priority(11f).On(Targets.CurrentEnemy),

            // Devastate: Sunder Armor upkeep / threat filler.
            Skill.Spell("Devastate").Priority(12f).On(Targets.CurrentEnemy),
            WarriorCommon.Rend(priority: 13f),
            WarriorCommon.VictoryRush(priority: 14f),
            WarriorCommon.Hamstring(_settings, priority: 14.5f),

            // --- rage dump ---
            WarriorCommon.Cleave(_settings, priority: 15f),
            WarriorCommon.HeroicStrike(_settings, priority: 16f),
        };
    }
}
