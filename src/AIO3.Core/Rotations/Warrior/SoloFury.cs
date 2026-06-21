using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Fury warrior (leveling/grinding, 10-80). Berserker-stance burst: Bloodthirst is the top
    /// DPS strike, the Bloodsurge "Slam!" proc and Whirlwind fill the gaps, Execute finishes sub-20%.
    /// Thin: composes the shared <see cref="WarriorCommon"/> / Layer 3 blocks and adds only the
    /// Fury-specific filler in priority order. Ported from the old Combat/Warrior/SoloFury.cs.
    ///
    /// Unknown/unusable spells are skipped automatically (IsSpellKnown), so this single priority list
    /// fills in as the player levels — list high-level abilities freely; they simply don't fire yet.
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
            // --- survival + burst ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            CombatBlocks.OffensiveRacial("Blood Fury", 4.2f, ctx => _settings.UseRacials.Value),
            CombatBlocks.OffensiveRacial("Berserking", 4.21f, ctx => _settings.UseRacials.Value),
            WarriorCommon.Recklessness(_settings, priority: 4.3f),

            // --- baseline / upkeep ---
            // Charge opener with a stance dance — sits above EnsureStance so it can hold Battle Stance
            // long enough to Charge; only active out of combat at range while gap-closers are enabled.
            WarriorCommon.ChargeWithStanceDance(_settings, priority: 0.08f),
            // Tank-only fallback (fires only in Defensive Stance, so inert for Fury/Berserker).
            WarriorCommon.TauntPull(_settings, priority: 0.09f),
            WarriorCommon.EnsureStance("Berserker Stance", priority: 0.1f),
            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Pummel", priority: 2f, mode: ctx => _settings.InterruptMode.Value),
            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),
            WarriorCommon.BerserkerRage(priority: 4f),
            CombatBlocks.DefensiveBelow("Enraged Regeneration", healthPercent: 50, priority: 4.5f),
            WarriorCommon.Bloodrage(priority: 5f),

            // --- survival debuff: enemy attack-power reduction on tougher fights / packs ---
            WarriorCommon.DemoralizingShout(_settings, priority: 5.5f),

            // --- single target ---
            // Bloodthirst: Fury's top DPS strike and the Bloodsurge feeder — fire on cooldown whenever
            // there is rage to spend. (The old rotation gated it on HealthPercent <= 80, a carried-over
            // quirk that cost DPS at full health; removed.)
            Skill.Spell("Bloodthirst").Priority(6f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > 30),

            // Instant, free Slam from the Bloodsurge proc ("Slam!") — consume it right after Bloodthirst.
            Skill.Spell("Slam").Priority(6.5f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Slam!")),

            Skill.Spell("Death Wish").Priority(7f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Me.Rage > 10),

            WarriorCommon.Execute(priority: 8f),
            WarriorCommon.VictoryRush(priority: 9f),
            WarriorCommon.Rend(priority: 10f),
            // Whirlwind doubles as single-target filler when no proc/strike is up (it hits the target too).
            Skill.Spell("Whirlwind").Priority(11f).On(Targets.CurrentEnemy),

            // --- gap-closers + utility (Charge opener handled high up via the stance dance) ---
            WarriorCommon.Intercept(_settings, priority: 12f),
            WarriorCommon.Hamstring(_settings, priority: 13.5f),

            // --- AoE (>= AoeThreshold enemies within 10y) ---
            // NOTE: Whirlwind itself isn't repeated here — its cleave is already covered by the
            // single-target filler step above (priority 11), which hits up to 4 nearby enemies.
            // Piercing Howl: AoE slow for a fleeing pack (mirrors the old <40% + pack gating).
            Skill.Spell("Piercing Howl").Priority(13.8f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.HealthPercent < 40
                              && ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Thunder Clap").Priority(14f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            WarriorCommon.Cleave(_settings, priority: 16f),

            // --- rage dump ---
            WarriorCommon.HeroicStrike(_settings, priority: 17f),
        };
    }
}
