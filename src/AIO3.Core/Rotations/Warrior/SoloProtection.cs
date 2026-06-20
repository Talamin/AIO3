using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Protection warrior (leveling/grinding, 10-80). Defensive-stance survival + fast kills:
    /// emergency defensives, Shield Slam / Revenge / Devastate threat-damage core, AoE via Thunder
    /// Clap / Shockwave, with Sunder Armor and Demoralizing Shout upkeep. Composes the shared
    /// WarriorCommon / CombatBlocks blocks. Group-tanking tools (Taunt, Challenging Shout, Mocking
    /// Blow, Spell Reflection-for-others, threat-for-others) are intentionally omitted — solo threat
    /// doesn't matter and a separate dungeon-farm botbase owns tanking.
    ///
    /// Unknown/unusable spells are skipped automatically (IsSpellKnown), so this single priority list
    /// fills in as the player levels — list high-level abilities freely; they simply don't fire yet.
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
            // Charge opener with a stance dance — Protection's home is Defensive Stance, so this switches
            // to Battle Stance, Charges, and lets EnsureStance restore Defensive afterwards. Above
            // EnsureStance so it can hold Battle long enough; only active out of combat at range when
            // gap-closers are enabled.
            WarriorCommon.ChargeWithStanceDance(_settings, priority: 0.08f),
            WarriorCommon.EnsureStance("Defensive Stance", priority: 0.1f),
            CombatBlocks.AutoAttack(priority: 1f),

            // --- emergency defensives (lower HP = higher prio) ---
            CombatBlocks.DefensiveBelow("Last Stand", healthPercent: 15, priority: 1.5f),
            CombatBlocks.DefensiveBelow("Shield Wall", healthPercent: 35, priority: 1.6f),

            // Interrupt (Protection's interrupt is Shield Bash). Respects InterruptMode.
            CombatBlocks.Interrupt("Shield Bash", priority: 2f, mode: ctx => _settings.InterruptMode.Value),
            WarriorCommon.BerserkerRage(priority: 2.5f),

            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),

            // Shield Block while a target is in melee (off the GCD mitigation; also feeds Revenge/Shield Slam).
            Skill.Spell("Shield Block").Priority(3.5f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.Distance < 8f).OffGcd(),

            WarriorCommon.Bloodrage(priority: 5f),
            WarriorCommon.Intercept(_settings, priority: 6f), // Charge opener handled high up via the stance dance

            // --- survival debuff: enemy attack-power reduction on tougher fights / packs ---
            WarriorCommon.DemoralizingShout(_settings, priority: 6.8f),

            // --- threat / damage core (fills in with level) ---
            // Shield Slam and Revenge are both cooldown strikes. With damage learning on, cast whichever
            // hits harder on this server; otherwise the hand order (Shield Slam first) applies. Both are
            // attempted off-cooldown and fail through harmlessly when not usable (e.g. Revenge's proc).
            CombatBlocks.BestDamage(7f, ctx => _settings.UseDamageLearning.Value, "Shield Slam", "Revenge"),
            // Execute as a sub-20% finisher (cheap HP gate avoids wasted attempts).
            WarriorCommon.Execute(priority: 8.5f),

            // --- AoE / control (>= AoeThreshold enemies within 10y) ---
            Skill.Spell("Thunder Clap").Priority(9f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Shockwave").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Concussion Blow").Priority(11f).On(Targets.CurrentEnemy),

            // --- single-target armor/threat filler ---
            // Devastate REPLACES Sunder Armor once learned, so it is the primary filler...
            Skill.Spell("Devastate").Priority(12f).On(Targets.CurrentEnemy),
            // ...and Sunder Armor only runs before Devastate is learned (else they'd double-apply the
            // same debuff and waste a global). Refresh when missing or about to drop below 5 stacks.
            Skill.Spell("Sunder Armor").Priority(12.5f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.IsSpellKnown("Devastate")
                              && (!ctx.Target.HasMyAura("Sunder Armor") || ctx.Target.AuraStacks("Sunder Armor") < 5)),

            WarriorCommon.Rend(priority: 13f),
            WarriorCommon.VictoryRush(priority: 14f),
            WarriorCommon.Hamstring(_settings, priority: 14.5f),

            // --- rage dump (off the GCD) ---
            WarriorCommon.Cleave(_settings, priority: 15f),
            WarriorCommon.HeroicStrike(_settings, priority: 16f),
        };
    }
}
