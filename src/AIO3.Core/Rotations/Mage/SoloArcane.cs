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
            // Pair the burst with the Arcane Blast ramp instead of firing it blind: AP / Icy Veins / Mirror Image once
            // at least one stack is up, Presence of Mind once we've ramped a little (>= 2) so its free instant lands on
            // a real Arcane Blast — not at 0 stacks. Old FC sequenced these the same way (AIO-Public-clone
            // .../Mage/SoloArcane.cs:29-32: AP/Icy Veins/Mirror Image at BuffStack >= 1, Presence of Mind at >= 2).
            ArcaneBurst("Arcane Power", priority: 2.6f, minStacks: 1),
            ArcaneBurst("Icy Veins", priority: 2.65f, minStacks: 1),
            ArcaneBurst("Mirror Image", priority: 2.7f, minStacks: 1),
            ArcaneBurst("Presence of Mind", priority: 2.75f, minStacks: 2),

            // --- AoE (held while our sheep is up so we don't break it) ---
            Skill.Spell("Arcane Explosion").Priority(3.0f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !MageCommon.AnySheeped(ctx)
                              && ctx.EnemiesWithin(MageCommon.MeleeRange) >= _settings.AoeThreshold.Value),

            // --- dump at the stack cap (resets Arcane Blast's escalating cost) ---
            // Standing still → channel Arcane Missiles (also our conserve dump: free on a proc, cheaper than another
            // Arcane Blast). Moving / instant → Arcane Barrage. Both also fire on a Missile Barrage proc. Old FC
            // dumped at the cap the same way (AIO-Public-clone .../Mage/SoloArcane.cs:33-34, BuffStack >= 3).
            Skill.Spell("Arcane Missiles").Priority(4.0f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving
                              && (ctx.Me.HasAura("Missile Barrage") || AtStackCap(ctx))), // channel
            Skill.Spell("Arcane Barrage").Priority(4.5f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Missile Barrage") || AtStackCap(ctx)), // instant dump (moving-safe)

            // --- wand when low on mana ---
            MageCommon.Wand(_settings, priority: 8.0f),

            // --- fillers (cast-time → stand still) ---
            // Ramp Arcane Blast only up to the (possibly conserve-lowered) cap; at/over it the dump above takes over.
            Skill.Spell("Arcane Blast").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !AtStackCap(ctx)),
            Skill.Spell("Frostbolt").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !ctx.Game.IsSpellKnown("Arcane Blast")),
        }), ctx => _settings.UseRacials.Value, basePriority: 2.5f);

        // When conserving mana, cap Arcane Blast at 2 stacks; we conserve.
        private const int ConserveStackCap = 2;

        /// <summary>A self-cast Arcane burst cooldown (Arcane Power / Icy Veins / Mirror Image / Presence of Mind),
        /// gated on the shared big-fight rule AND a minimum Arcane Blast stack so it pairs with the ramp instead of
        /// firing at 0 stacks. PoM in particular wants a stack up so its free instant lands on a real Arcane Blast.</summary>
        private RotationStep ArcaneBurst(string spell, float priority, int minStacks) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => _settings.UseCooldowns.Value && MageCommon.IsBigFight(ctx)
                              && ctx.Me.AuraStacks("Arcane Blast") >= minStacks);

        /// <summary>The Arcane Blast stack cap to ramp to before dumping: the configured <c>ArcaneBlastStacks</c>,
        /// lowered to <see cref="ConserveStackCap"/> while mana is below <c>ArcaneConserveManaPercent</c> so the
        /// escalating per-stack cost doesn't drain the pool (we lean on Missiles / wand / Evocation instead).</summary>
        private int EffectiveStackCap(CombatContext ctx) =>
            ctx.Me.PowerPercent < _settings.ArcaneConserveManaPercent.Value
                ? System.Math.Min(ConserveStackCap, _settings.ArcaneBlastStacks.Value)
                : _settings.ArcaneBlastStacks.Value;

        /// <summary>True once Arcane Blast has reached the effective cap → stop ramping and dump.</summary>
        private bool AtStackCap(CombatContext ctx) =>
            ctx.Me.AuraStacks("Arcane Blast") >= EffectiveStackCap(ctx);
    }
}
