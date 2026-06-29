using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Priest
{
    /// <summary>
    /// Solo Shadow priest (leveling/grinding, 10-80). One priority list that scales from level 10 because every
    /// spec-specific ability auto-skips (IsSpellKnown) until learned: a pre-Shadowform priest is a Smite caster
    /// (Power Word: Shield + heals carry survival), and Shadowform / Mind Flay / Vampiric Touch / Devouring Plague
    /// / Mind Sear / Dispersion light up as they're trained. Survival + upkeep first, then the shadow DoT/nuke
    /// core.
    ///
    /// THE SHADOWFORM HEAL INTERLOCK: in 3.3.5a a priest can't cast Holy heals (Heal / Flash Heal / Renew) while in
    /// Shadowform. The in-combat hard heal therefore runs in two beats — <see cref="PriestCommon.DropShadowformToHeal"/>
    /// cancels the form (priority 1.0, above the heals), then the heal steps (gated on being OUT of form) fire the
    /// next tick, and <see cref="PriestCommon.ShadowformUpkeep"/> re-enters form once healed (held while a heal is
    /// still wanted, so it doesn't fight the heal). In-form survival (Power Word: Shield, Dispersion, Vampiric
    /// Embrace) stays available throughout.
    ///
    /// Thin: composes the shared <see cref="PriestCommon"/> baseline, the Layer 3 <see cref="CombatBlocks"/>, and
    /// the racials. The only spec-local thing is the priority ordering.
    /// </summary>
    public sealed class SoloShadow : IRotation
    {
        public string Name => "Priest - Solo Shadow";

        private readonly PriestSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloShadow() : this(new PriestSettings()) { }

        public SoloShadow(PriestSettings settings)
        {
            _settings = settings;
            _steps = Build(); // build the step list ONCE (a field, never a per-tick property)
        }

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => _steps;

        private List<RotationStep> Build() => Racials.With(new List<RotationStep>
        {
            // --- emergency survival (off the GCD / item) ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),

            // --- in-form survival (all castable in Shadowform; no form drop needed) ---
            // Power Word: Shield (Discipline — works in form). Dispersion (Shadow capstone — emergency mana +
            // damage reduction, off-GCD). Psychic Scream (panic AoE fear when surrounded + low, solo).
            PriestCommon.PowerWordShield(_settings, priority: 0.2f),
            PriestCommon.Dispersion(_settings, priority: 0.25f),
            PriestCommon.PsychicScream(_settings, priority: 0.3f),

            // --- the in-combat hard heal (drop Shadowform → heal → re-enter form) ---
            // Drop form FIRST (above the heals) so the next tick can cast a Holy heal; the heal steps are gated on
            // being OUT of form, so they fire once the drop has landed. ShadowformUpkeep (below) re-enters after.
            PriestCommon.DropShadowformToHeal(_settings, priority: 1.0f),
            PriestCommon.FlashHeal(_settings, priority: 1.1f),   // fast emergency heal (out of form)
            PriestCommon.BestHeal(_settings, priority: 1.2f),    // best known hard heal (Greater>Heal>Lesser)
            PriestCommon.Renew(_settings, priority: 1.3f),       // cheap HoT top-off (out of form)

            // --- mana ---
            // Wand first when finishing a low target / nearly out of mana (off the GCD, conserves mana).
            PriestCommon.Wand(_settings, priority: 1.5f),
            // Shadowfiend — the mana cooldown (cast on the enemy; it auto-attacks + returns mana).
            PriestCommon.Shadowfiend(_settings, priority: 1.6f),

            // --- combat buffs / form upkeep ---
            // Inner Fire (castable in form). Shadowform upkeep re-enters the DPS form when it's down (held while a
            // heal is wanted so it doesn't fight the heal). Vampiric Embrace (self-heal buff, castable in form).
            PriestCommon.InnerFire(_settings, priority: 1.8f),
            PriestCommon.ShadowformUpkeep(_settings, priority: 1.9f),
            PriestCommon.VampiricEmbrace(_settings, priority: 2.0f),

            // --- OOC buffs (long self-buffs, applied before the pull; superseded by the raid-wide Prayers) ---
            PriestCommon.OocBuff("Power Word: Fortitude", s => s.PowerWordFortitude.Value, _settings, priority: 2.1f,
                "Prayer of Fortitude"),
            PriestCommon.OocBuff("Divine Spirit", s => s.DivineSpirit.Value, _settings, priority: 2.15f,
                "Prayer of Spirit"),
            PriestCommon.OocBuff("Shadow Protection", s => s.ShadowProtection.Value, _settings, priority: 2.2f,
                "Prayer of Shadow Protection"),

            // (racials are appended by the shared Racials bundle at the 2.5 band)

            // --- AoE (channelled; replaces the single-target core when a pack is on the target) ---
            PriestCommon.MindSear(_settings, priority: 3.0f),

            // --- single-target shadow DoT / nuke core (in form; each auto-skips until learned) ---
            // Order mirrors the old SoloShadow: VT (priority DoT) → Devouring Plague → Mind Blast (CD nuke) →
            // SW:Pain (Shadow-Weaving-gated) → Mind Flay (channel filler) → Smite (pre-form filler).
            PriestCommon.VampiricTouch(_settings, priority: 4.0f),
            PriestCommon.DevouringPlague(_settings, priority: 4.1f),
            PriestCommon.MindBlast(priority: 4.2f),
            PriestCommon.ShadowWordPain(_settings, priority: 4.3f),
            PriestCommon.MindFlay(_settings, priority: 4.4f),
            // Pre-Shadowform filler — carries the early game; goes quiet once Shadowform is up (it locks out Holy,
            // and Smite's gate already skips while in form) and the shadow nukes above take over.
            PriestCommon.Smite(priority: 5.0f),

        }, ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
