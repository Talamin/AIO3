using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warlock
{
    /// <summary>
    /// Solo Destruction warlock (leveling/grinding). Single-target direct-damage caster backed by the ranged
    /// Imp: keep the demon up and on the target, sustain mana with Life Tap (and the wand at low mana),
    /// self-heal with Drain Life when low and solo, then drive the Destruction engine — keep Immolate up (its
    /// key DoT; Conflagrate and Incinerate both key off it), fire Conflagrate while Immolate is up, maintain
    /// curse + Corruption, and fill with Incinerate (Chaos Bolt when ready, Shadow Bolt when Incinerate is not
    /// learned yet). Everything pet-related is gated on the pet actually existing (<c>ctx.Pet</c>), and every
    /// spell auto-skips when unknown, so a low-level destro lock plays as a clean Shadow Bolt caster and fills
    /// in as it levels.
    ///
    /// Thin: composes the shared <see cref="WarlockCommon"/> caster baseline, <see cref="PetControl"/>, and the
    /// Layer 3 <see cref="CombatBlocks"/>. Multi-target (Rain of Fire), Shadowfury, and Death Coil are deferred.
    /// </summary>
    public sealed class SoloDestruction : IRotation
    {
        public string Name => "Warlock - Solo Destruction";

        // DoT refresh window: re-apply when fewer than this many ms remain (avoid clipping ticks).
        private const int DotRefreshMs = 2000;
        // The Destruction filler nuke — referenced twice (the cast, and the Shadow-Bolt-until-learned gate).
        private const string Incinerate = "Incinerate";

        private readonly WarlockSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloDestruction() : this(new WarlockSettings()) { }

        public SoloDestruction(WarlockSettings settings)
        {
            _settings = settings;
            _steps = Build(); // build the step list ONCE (a field, never a per-tick property)
        }

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => _steps;

        // Build the core list once, then splice the demon's own special abilities (Torment / Spell Lock /
        // Firebolt) onto the end with the shared WarlockCommon.WithPetSpecials (mirrors the hunter).
        private List<RotationStep> Build() => Racials.With(WarlockCommon.WithPetSpecials(_settings, new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),

            // --- buffs ---
            WarlockCommon.Armor(_settings, priority: 0.5f),

            // --- pet upkeep (all skip when petless; Auto resolves to Imp, else Voidwalker if unlearned) ---
            PetControl.Summon(ctx => _settings.ManagePet.Value,
                ctx => WarlockCommon.SummonSpell(_settings, ctx, WarlockSpec.Destruction),
                ctx => WarlockCommon.SummonSpell(_settings, ctx, WarlockSpec.Destruction), priority: 0.6f),
            PetControl.Attack(ctx => _settings.ManagePet.Value, priority: 0.7f),
            // Health Funnel the demon when low (opt-in; channelled, so only while standing still and we have HP).
            PetControl.Heal(
                ctx => _settings.ManagePet.Value && _settings.PetHealPercent.Value > 0
                       && !ctx.Game.PlayerIsMoving && ctx.Me.HealthPercent > _settings.LifeTapHealthFloor.Value,
                "Health Funnel", ctx => _settings.PetHealPercent.Value, priority: 0.8f),

            // --- mana / sustain ---
            WarlockCommon.Wand(_settings, priority: 1.0f),
            WarlockCommon.LifeTap(_settings, priority: 1.5f),
            WarlockCommon.GlyphLifeTap(_settings, priority: 1.6f),

            // --- EMERGENCY break-melee (no Frost Nova; panic buttons, gated tightly on low HP) ---
            // Above Drain Life: you can't safely channel a heal while a mob beats on you — break melee first.
            // Howl (surrounded) outranks single Fear; both skip cleanly when unknown / not low / not meleed.
            WarlockCommon.HowlOfTerror(_settings, priority: 1.85f),
            WarlockCommon.Fear(_settings, priority: 1.9f),

            // --- self-heal (channel → stand still; shared block owns the gate) ---
            WarlockCommon.DrainLife(_settings, priority: 2.0f),

            // (racials are appended by the shared Racials bundle at the 2.5 band)

            // --- DoTs / curse (priority order) ---
            // The chosen curse (instant; resolved live from the setting).
            WarlockCommon.MaintainCurse(_settings, priority: 6f),
            // Immolate is Destruction's key DoT (Conflagrate / Incinerate key off it). Cast-time → stand still.
            Skill.Spell("Immolate").Priority(7f).On(Targets.CurrentEnemy)
                 .When(ctx => (!ctx.Target.HasMyAura("Immolate") || ctx.Target.MyAuraTimeLeftMs("Immolate") < DotRefreshMs)
                              && !ctx.Game.PlayerIsMoving),
            // Conflagrate consumes Immolate for a burst — only fire it while Immolate is actually on the target
            // (instant, so it does not gate on movement). Gated so we never cast it on a target without Immolate.
            Skill.Spell("Conflagrate").Priority(8f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseConflagrate.Value && ctx.Target.HasMyAura("Immolate")),
            // Corruption is instant.
            CombatBlocks.MaintainMyDebuff("Corruption", DotRefreshMs, priority: 9f),

            // --- filler nukes (cast-time → stand still) ---
            // Chaos Bolt is a hard-hitting cooldown nuke — fire when ready (its readiness gate throttles it).
            Skill.Spell("Chaos Bolt").Priority(12f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseChaosBolt.Value && !ctx.Game.PlayerIsMoving),
            // Incinerate is the Destruction filler (bonus on an Immolate'd target). Replaces Shadow Bolt once known.
            Skill.Spell(Incinerate).Priority(18f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving),
            // Shadow Bolt is the fallback filler before Incinerate is learned (auto-skips once Incinerate wins).
            Skill.Spell("Shadow Bolt").Priority(20f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.IsSpellKnown(Incinerate) && !ctx.Game.PlayerIsMoving),
        }), ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
