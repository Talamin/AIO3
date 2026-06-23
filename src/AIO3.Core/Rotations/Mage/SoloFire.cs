using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Mage
{
    /// <summary>
    /// Solo Fire mage (leveling/grinding, 10-80). Maintain Living Bomb, weave the Hot Streak instant Pyroblast,
    /// and fill with Fireball; Fire Blast finishes. Shares the same caster baseline as the other specs (armor /
    /// Arcane Intellect / kite / mana / interrupt). Thin: composes <see cref="MageCommon"/> + Layer 3 blocks;
    /// unknown spells auto-skip so it fills in as the player levels.
    /// </summary>
    public sealed class SoloFire : IRotation
    {
        public string Name => "Mage - Solo Fire";

        private readonly MageSettings _settings;

        public SoloFire() : this(new MageSettings()) { }

        public SoloFire(MageSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => MageCommon.WithConjure(_settings, new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            MageCommon.IceBlock(_settings, priority: 0.1f),

            // --- buffs / shields ---
            MageCommon.Armor(_settings, MageSpec.Fire, priority: 0.5f),
            MageCommon.ArcaneIntellect(_settings, priority: 0.6f),
            MageCommon.ManaShield(_settings, priority: 0.7f),

            // --- interrupt ---
            MageCommon.Counterspell(_settings, priority: 1.0f),
            MageCommon.ArcaneTorrent(_settings, priority: 1.05f),

            // --- CC an extra attacker first (the sheep holds: Frost Nova + AoE are suppressed while it's up) ---
            MageCommon.Polymorph(_settings, priority: 1.1f),
            // After a kill, grab our own sheeped add (no live target) so we finish it instead of leaving it to wake.
            MageCommon.FinishSheepedAdd(_settings, priority: 0.1f),

            // --- kite ---
            MageCommon.FrostNova(_settings, priority: 1.2f),
            MageCommon.Blink(_settings, priority: 1.3f),
            MageCommon.KiteBack(_settings, priority: 1.4f),

            // --- mana ---
            MageCommon.Evocation(_settings, priority: 2.0f),
            MageCommon.ManaGem(_settings, priority: 2.1f),

            // --- offensive racials + cooldowns ---
            CombatBlocks.OffensiveRacial("Blood Fury", 2.5f, ctx => _settings.UseRacials.Value),
            CombatBlocks.OffensiveRacial("Berserking", 2.51f, ctx => _settings.UseRacials.Value),
            Skill.Spell("Combustion").Priority(2.6f).On(Targets.Self)
                 .When(ctx => _settings.UseCooldowns.Value && MageCommon.IsBigFight(ctx) && !ctx.Me.HasAura("Combustion")),

            // --- AoE (held while our sheep is up so we don't break it) ---
            Skill.Spell("Flamestrike").Priority(3.0f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !ctx.Game.PlayerIsMoving && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.AoeRadius) >= _settings.AoeThreshold.Value),
            Skill.Spell("Dragon's Breath").Priority(3.1f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.MeleeRange) >= _settings.AoeThreshold.Value),
            Skill.Spell("Blast Wave").Priority(3.2f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.MeleeRange) >= _settings.AoeThreshold.Value),

            // --- procs / DoT / instants ---
            Skill.Spell("Pyroblast").Priority(4.0f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Hot Streak")), // instant from the proc
            CombatBlocks.MaintainMyDebuff("Living Bomb", minMsLeft: 1500, priority: 4.5f),
            Skill.Spell("Fire Blast").Priority(6.0f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.HealthPercent < MageCommon.ExecutePercent), // instant execute filler

            // --- wand when low on mana ---
            MageCommon.Wand(_settings, priority: 8.0f),

            // --- fillers (cast-time → stand still) ---
            Skill.Spell("Fireball").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving),
            Skill.Spell("Scorch").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !ctx.Game.IsSpellKnown("Fireball")),
        });
    }
}
