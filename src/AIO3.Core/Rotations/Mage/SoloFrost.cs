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
    /// Solo Frost mage (leveling/grinding, 10-80). The safest solo spec: kite with Frost Nova + a cliff-safe
    /// step back, shatter frozen targets with Ice Lance / Deep Freeze, and weave the Brain Freeze instant.
    /// Keeps armor / Arcane Intellect / Ice Barrier up and manages mana via Evocation / mana gem / wand. The
    /// Water Elemental is summoned and directed by the shared <see cref="PetControl"/> (auto-skips if unknown
    /// or product-managed). Thin: composes <see cref="MageCommon"/> + Layer 3 blocks; unknown spells auto-skip
    /// so it fills in as the player levels.
    /// </summary>
    public sealed class SoloFrost : IRotation
    {
        public string Name => "Mage - Solo Frost";

        private readonly MageSettings _settings;

        public SoloFrost() : this(new MageSettings()) { }

        public SoloFrost(MageSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => Racials.With(MageCommon.WithConjure(_settings, new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            MageCommon.IceBlock(_settings, priority: 0.1f),

            // --- buffs / shields ---
            MageCommon.Armor(_settings, MageSpec.Frost, priority: 0.5f),
            MageCommon.ArcaneIntellect(_settings, priority: 0.6f),
            MageCommon.ManaShield(_settings, priority: 0.7f),
            MageCommon.IceBarrier(_settings, priority: 0.8f),

            // --- interrupt ---
            MageCommon.Counterspell(_settings, priority: 1.0f),

            // --- CC an extra attacker first (the sheep holds: Frost Nova + AoE are suppressed while it's up) ---
            MageCommon.Polymorph(_settings, priority: 1.1f),
            // After a kill, grab our own sheeped add (no live target) so we finish it instead of leaving it to wake.
            MageCommon.FinishSheepedAdd(_settings, priority: 0.1f),

            // --- kite (Frost Nova root → Blink away, else cliff-safe step back) ---
            MageCommon.FrostNova(_settings, priority: 1.2f),
            MageCommon.Blink(_settings, priority: 1.3f),
            MageCommon.KiteBack(_settings, priority: 1.4f),

            // --- Water Elemental (summon + direct; auto-skips if unknown / permanent already up) ---
            Skill.Spell("Summon Water Elemental").Priority(1.7f).On(Targets.Self)
                 .When(ctx => _settings.UseWaterElemental.Value && ctx.HasEnemyTarget && ctx.Pet == null),
            PetControl.Attack(ctx => _settings.UseWaterElemental.Value, priority: 1.75f),

            // --- mana ---
            MageCommon.Evocation(_settings, priority: 2.0f),
            MageCommon.ManaGem(_settings, priority: 2.1f),

            // --- cooldowns (racials are appended by the shared Racials bundle at the 2.5 band) ---
            MageCommon.MajorCooldown(_settings, "Icy Veins", priority: 2.6f),
            MageCommon.MajorCooldown(_settings, "Mirror Image", priority: 2.65f),

            // --- AoE (held while our sheep is up so we don't break it) ---
            Skill.Spell("Blizzard").Priority(3.0f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !ctx.Game.PlayerIsMoving && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.AoeRadius) >= _settings.AoeThreshold.Value),
            Skill.Spell("Cone of Cold").Priority(3.1f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.MeleeRange) >= _settings.AoeThreshold.Value),

            // --- procs / instants (shatter combo) ---
            Skill.Spell("Deep Freeze").Priority(4.0f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseCooldowns.Value && Frozen(ctx)),
            Skill.Spell("Frostfire Bolt").Priority(4.5f).On(Targets.CurrentEnemy)
                 .When(ctx => BrainFreeze(ctx)),
            Skill.Spell("Ice Lance").Priority(5.0f).On(Targets.CurrentEnemy)
                 .When(ctx => Frozen(ctx)),
            Skill.Spell("Fire Blast").Priority(6.0f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.HealthPercent < MageCommon.ExecutePercent), // instant execute filler

            // --- wand when low on mana ---
            MageCommon.Wand(_settings, priority: 8.0f),

            // --- fillers (cast-time → stand still) ---
            Skill.Spell("Frostbolt").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving),
            Skill.Spell("Fireball").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !ctx.Game.IsSpellKnown("Frostbolt")),
        }), ctx => _settings.UseRacials.Value, basePriority: 2.5f);

        // Target is "frozen" (rooted by Frost Nova or we have a Fingers of Frost charge) → Ice Lance / Deep Freeze
        // shatter. HasAura (name-based HaveBuff) already detects the root cheaply; no need for the costlier HasMyAura.
        private static bool Frozen(CombatContext ctx) =>
            ctx.Me.HasAura("Fingers of Frost") || ctx.Target.HasAura("Frost Nova");

        // Brain Freeze proc makes the next Frostfire Bolt instant + free (buff shows as "Fireball!").
        private static bool BrainFreeze(CombatContext ctx) =>
            ctx.Me.HasAura("Fireball!") || ctx.Me.HasAura("Brain Freeze");
    }
}
