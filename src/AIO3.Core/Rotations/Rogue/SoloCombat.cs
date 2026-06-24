using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Solo Combat rogue (leveling/grinding, 10-80). Energy + combo-point melee: keep Slice and Dice up, spend
    /// combo points on Eviscerate (and optional Rupture on durable targets), and build them with Sinister Strike;
    /// Blade Flurry cleaves a pack, Adrenaline Rush / Killing Spree are the burst cooldowns. Thin: composes the
    /// shared <see cref="RogueCommon"/> melee baseline and the Layer 3 <see cref="CombatBlocks"/> / racials, and
    /// adds only the Combat-specific filler in priority order. Ported from the old Combat/Rogue/SoloCombat.cs.
    ///
    /// Unknown/unusable spells are skipped automatically (IsSpellKnown), so this single priority list fills in as
    /// the player levels — at level 10 with only Sinister Strike + Eviscerate known it runs cleanly as a
    /// build-and-finish loop; Slice and Dice / Blade Flurry / the cooldowns light up as they're learned.
    /// </summary>
    public sealed class SoloCombat : IRotation
    {
        public string Name => "Rogue - Solo Combat";

        // Slice and Dice is worth keeping up off even a single combo point — refresh it whenever it's down.
        private const int SnDMinComboPoints = 1;

        private readonly RogueSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloCombat() : this(new RogueSettings()) { }

        public SoloCombat(RogueSettings settings)
        {
            _settings = settings;
            _steps = Build(); // build the step list ONCE (a field, never a per-tick property)
        }

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => _steps;

        private List<RotationStep> Build() => Racials.With(new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),

            // --- defensives (off the GCD) ---
            RogueCommon.Evasion(_settings, priority: 0.2f),
            RogueCommon.CloakOfShadows(_settings, priority: 0.3f),

            // --- opener ---
            // Stealth out of combat (opt-in) so the fight can start from stealth; never fights the product's pull.
            RogueCommon.Stealth(_settings, priority: 0.4f),

            // --- baseline / upkeep ---
            CombatBlocks.AutoAttack(priority: 1f),
            // Sprint to close the gap (off the GCD, throttled); sits high so we reach melee before spending energy.
            RogueCommon.Sprint(_settings, priority: 1.5f),
            CombatBlocks.Interrupt("Kick", priority: 2f, mode: ctx => _settings.InterruptMode.Value),

            // (racials are appended by the shared Racials bundle at the 2.5 band)

            // --- burst cooldowns (shared RogueCommon blocks; gated on UseCooldowns, on a pack or a lone
            // elite/boss, mirroring WarriorCommon.Recklessness — so a future Group Combat rotation reuses them) ---
            RogueCommon.AdrenalineRush(_settings, priority: 3f),
            RogueCommon.KillingSpree(_settings, priority: 3.2f),

            // --- AoE: Blade Flurry cleaves when a pack is in melee ---
            RogueCommon.BladeFlurry(_settings, priority: 4f),

            // --- finishers (spend combo points) ---
            // Slice and Dice upkeep: the core attack-speed buff — refresh whenever it's down and we have a CP.
            RogueCommon.SliceAndDice(SnDMinComboPoints, priority: 5f),
            // Rupture (opt-in) bleed on durable targets, then Eviscerate as the damage finisher at the CP threshold.
            RogueCommon.Rupture(_settings, priority: 6f),
            RogueCommon.Eviscerate(_settings, priority: 7f),

            // --- builder / filler ---
            // Sinister Strike: the combo-point builder. Lowest priority so finishers/upkeep win when ready; it
            // fills every other GCD. Not while stealthed (the opener goes first). Energy/known gating is automatic.
            Skill.Spell("Sinister Strike").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => RogueCommon.NotStealthed(ctx)),

        }, ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
