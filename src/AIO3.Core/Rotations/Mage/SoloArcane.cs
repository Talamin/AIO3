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
    /// Solo Arcane mage (leveling/grinding, 10-80). Ramp Arcane Blast, dump with Arcane Missiles on the Missile
    /// Barrage proc (or Arcane Barrage while moving), and lean on the shared mana management since Arcane is
    /// mana-hungry. Same caster baseline as the other specs (armor / Arcane Intellect / kite / interrupt).
    /// Thin: composes <see cref="MageCommon"/> + Layer 3 blocks; unknown spells auto-skip while leveling.
    /// </summary>
    public sealed class SoloArcane : IRotation
    {
        public string Name => "Mage - Solo Arcane";

        private readonly MageSettings _settings;

        public SoloArcane() : this(new MageSettings()) { }

        public SoloArcane(MageSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => Racials.With(MageCommon.WithConjure(_settings, new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            MageCommon.IceBlock(_settings, priority: 0.1f),

            // --- buffs / shields ---
            MageCommon.Armor(_settings, MageSpec.Arcane, priority: 0.5f),
            MageCommon.ArcaneIntellect(_settings, priority: 0.6f),
            MageCommon.ManaShield(_settings, priority: 0.7f),

            // --- interrupt ---
            MageCommon.Counterspell(_settings, priority: 1.0f),

            // --- CC an extra attacker first (the sheep holds: Frost Nova + AoE are suppressed while it's up) ---
            MageCommon.Polymorph(_settings, priority: 1.1f),
            // After a kill, grab our own sheeped add (no live target) so we finish it instead of leaving it to wake.
            MageCommon.FinishSheepedAdd(_settings, priority: 0.1f),

            // --- kite ---
            MageCommon.FrostNova(_settings, priority: 1.2f),
            MageCommon.Blink(_settings, priority: 1.3f),
            MageCommon.KiteBack(_settings, priority: 1.4f),

            // --- mana (Arcane is mana-hungry → keep these high) ---
            MageCommon.Evocation(_settings, priority: 2.0f),
            MageCommon.ManaGem(_settings, priority: 2.1f),

            // --- cooldowns (racials are appended by the shared Racials bundle at the 2.5 band) ---
            MageCommon.MajorCooldown(_settings, "Arcane Power", priority: 2.6f),
            MageCommon.MajorCooldown(_settings, "Icy Veins", priority: 2.65f),
            MageCommon.MajorCooldown(_settings, "Mirror Image", priority: 2.7f),
            MageCommon.MajorCooldown(_settings, "Presence of Mind", priority: 2.75f),

            // --- AoE (held while our sheep is up so we don't break it) ---
            Skill.Spell("Arcane Explosion").Priority(3.0f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.MeleeRange) >= _settings.AoeThreshold.Value),

            // --- proc / instant dump ---
            Skill.Spell("Arcane Missiles").Priority(4.0f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Missile Barrage") && !ctx.Game.PlayerIsMoving), // channel
            Skill.Spell("Arcane Barrage").Priority(4.5f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.AuraStacks("Arcane Blast") >= 3), // instant dump (resets the stack growth)

            // --- wand when low on mana ---
            MageCommon.Wand(_settings, priority: 8.0f),

            // --- fillers (cast-time → stand still) ---
            Skill.Spell("Arcane Blast").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving),
            Skill.Spell("Frostbolt").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !ctx.Game.IsSpellKnown("Arcane Blast")),
        }), ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
