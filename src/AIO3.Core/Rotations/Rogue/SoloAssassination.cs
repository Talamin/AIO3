using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Solo Assassination rogue (leveling/grinding, 10-80). Energy + combo-point melee built on the same shared
    /// baseline as Combat: keep Slice and Dice up, maintain the bleed + Hunger for Blood, and spend combo points on
    /// Envenom (the signature finisher) — built with Mutilate (daggers) and Sinister Strike as the dagger-less /
    /// pre-Mutilate fallback. Positional-FREE: it opens from the FRONT with Cheap Shot (Garrote, which needs to be
    /// behind, stays the opt-in opener). Poisons are deferred to the player/product, so this functions with no
    /// poison managed (Envenom is still chosen, but auto-falls back to Eviscerate when unknown / unwanted).
    ///
    /// Thin: composes the shared <see cref="RogueCommon"/> melee baseline and the Layer 3 <see cref="CombatBlocks"/>
    /// / racials, and adds only the Assassination-specific filler in priority order. It deliberately omits the
    /// Combat-tree cooldowns (Adrenaline Rush / Killing Spree / Blade Flurry); its only major cooldown is Cold Blood.
    ///
    /// Unknown/unusable spells are skipped automatically (IsSpellKnown), so this single priority list fills in as the
    /// player levels — at level 10 with only Sinister Strike + Eviscerate known it runs cleanly as a build-and-finish
    /// loop; Mutilate / Envenom / Rupture / Hunger for Blood / Cold Blood light up as they're learned.
    /// </summary>
    public sealed class SoloAssassination : IRotation
    {
        public string Name => "Rogue - Solo Assassination";

        // Slice and Dice is worth keeping up off even a single combo point — refresh it whenever it's down.
        private const int SnDMinComboPoints = 1;

        private readonly RogueSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloAssassination() : this(new RogueSettings()) { }

        public SoloAssassination(RogueSettings settings)
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

            // --- defensives (off the GCD) — shared with Combat ---
            RogueCommon.Evasion(_settings, priority: 0.2f),
            RogueCommon.CloakOfShadows(_settings, priority: 0.3f),

            // --- opener (positional-free by default) ---
            // Stealth out of combat (opt-in) so the fight can start from stealth; never fights the product's pull.
            RogueCommon.Stealth(_settings, priority: 0.4f),
            // Cheap Shot opens from the FRONT (Assassination's default); Garrote (behind-only) is the opt-in choice.
            // Above auto-attack (1f) so it lands before the swing breaks stealth; only the chosen opener passes.
            RogueCommon.Opener(_settings, "Cheap Shot", priority: 0.6f),
            RogueCommon.Opener(_settings, "Garrote", priority: 0.6f),

            // --- baseline / upkeep ---
            CombatBlocks.AutoAttack(priority: 1f),
            // Sprint to close the gap (off the GCD, throttled); sits high so we reach melee before spending energy.
            RogueCommon.Sprint(_settings, priority: 1.5f),
            CombatBlocks.Interrupt("Kick", priority: 2f, mode: ctx => _settings.InterruptMode.Value),

            // (racials are appended by the shared Racials bundle at the 2.5 band)

            // --- survival finisher (shared block) ---
            // Recuperate: when low on HP, spend a finisher-worthy combo bar on the self-heal HoT instead of damage.
            // At 2.9f it sits above Cold Blood (3f) and every offensive CP-spender (SnD 5 / Rupture 6 / Envenom 7 /
            // Eviscerate 7.5) — so when low, survival wins the bar instead of crit-buffing a damage finisher.
            RogueCommon.Recuperate(_settings, priority: 2.9f),

            // --- cooldown: Cold Blood pairs with the next finisher (guaranteed crit). Sits just above the finishers
            // so the crit lands on the Envenom/Eviscerate that follows. Off the GCD; gated on cooldowns + a pack /
            // lone elite, like Combat's Adrenaline Rush (Adrenaline Rush / Killing Spree / Blade Flurry are COMBAT
            // talents and are deliberately absent here). ---
            RogueCommon.ColdBlood(_settings, priority: 3f),

            // --- AoE: Fan of Knives cleaves a pack (Assassination's instant AoE; Combat uses Blade Flurry instead).
            // Mirrors where Blade Flurry sits in SoloCombat — above the builders so it pre-empts single-target filler. ---
            RogueCommon.FanOfKnives(_settings, priority: 4f),

            // --- finishers (spend combo points) ---
            // Slice and Dice upkeep: the core attack-speed buff — refresh whenever it's down and we have a CP.
            RogueCommon.SliceAndDice(_settings, SnDMinComboPoints, priority: 5f),
            // Hunger for Blood: refresh the damage buff once a bleed is up — before we dump the combo points it needs.
            RogueCommon.HungerForBlood(_settings, priority: 5.5f),
            // Rupture (defaults ON for Assassination): apply the bleed on durable targets — it feeds Hunger for Blood
            // and is core to the tree's damage. Then the damage finisher: Envenom (preferred) else Eviscerate.
            RogueCommon.AssassinationRupture(_settings, priority: 6f),
            RogueCommon.Envenom(_settings, priority: 7f),
            RogueCommon.Eviscerate(_settings, priority: 7.5f),

            // --- builders / filler ---
            // Mutilate: the signature builder (needs daggers). A cast fails without daggers, so Sinister Strike sits
            // below it as the dagger-less / pre-Mutilate fallback — the engine falls through on a failed/unknown cast.
            RogueCommon.Mutilate(_settings, priority: 9f),
            // Sinister Strike: the fallback combo-point builder (shared block). Lowest priority so finishers/upkeep
            // win when ready; it fills every other GCD.
            RogueCommon.SinisterStrike(_settings, priority: 10f),

        }, ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
