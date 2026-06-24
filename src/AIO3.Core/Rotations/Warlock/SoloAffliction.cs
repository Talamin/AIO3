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
    /// Solo Affliction warlock (leveling/grinding, 10-80). Single-target DoT caster + permanent demon:
    /// keep the Voidwalker summoned and on the target, keep armor up, sustain mana with Life Tap (and the
    /// wand at low mana), self-heal with Drain Life when low and solo, then stack the DoTs in priority order
    /// (Haunt → curse → Immolate-if-no-UA → Corruption → Unstable Affliction), spend the Shadow Trance proc
    /// as an instant Shadow Bolt, and fill with Shadow Bolt. Everything pet-related is gated on the pet
    /// actually existing (<c>ctx.Pet</c>), so a low-level / petless warlock plays as a clean DoT caster with
    /// the pet steps skipped.
    ///
    /// Thin: composes the shared <see cref="WarlockCommon"/> caster baseline, <see cref="PetControl"/>, and
    /// the Layer 3 <see cref="CombatBlocks"/>. Unknown spells auto-skip, so it fills in as the player levels.
    /// Multi-target DoT spreading (Seed of Corruption / Rain of Fire), Fear/Howl kiting, and the pet's special
    /// abilities (Spell Lock interrupt, Torment, Firebolt) are deferred to a later phase.
    /// </summary>
    public sealed class SoloAffliction : IRotation
    {
        public string Name => "Warlock - Solo Affliction";

        // DoT refresh window: re-apply when fewer than this many ms remain (avoid clipping ticks).
        private const int DotRefreshMs = 2000;
        // Haunt is short (12s) and a damage-multiplier debuff — refresh it more eagerly.
        private const int HauntRefreshMs = 3000;

        private readonly WarlockSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloAffliction() : this(new WarlockSettings()) { }

        public SoloAffliction(WarlockSettings settings)
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
            // Keep a Healthstone stocked (OOC). Priority sits BELOW the pet summon (0.6) so summoning the demon
            // gets first claim on a Soul Shard — a Healthstone is only made from a surplus shard, never the pet's.
            WarlockCommon.CreateHealthstone(_settings, priority: 0.85f),

            // --- pet upkeep (all skip when petless) ---
            // Summon the chosen demon when none exists; "Revive Pet" never exists for a warlock (a dead demon is
            // re-summoned), so the call spell is used for both — PetControl re-summons whenever ctx.Pet is gone.
            PetControl.Summon(ctx => _settings.ManagePet.Value,
                ctx => WarlockCommon.SummonSpell(_settings, ctx, WarlockSpec.Affliction),
                ctx => WarlockCommon.SummonSpell(_settings, ctx, WarlockSpec.Affliction), priority: 0.6f,
                desiredPetName: ctx => WarlockCommon.DesiredPetForSwap(_settings, ctx, WarlockSpec.Affliction)),
            PetControl.Attack(ctx => _settings.ManagePet.Value, priority: 0.7f),
            // Health Funnel the demon when low (opt-in; channelled, so only while standing still and we have HP).
            PetControl.Heal(
                ctx => _settings.ManagePet.Value && _settings.PetHealPercent.Value > 0
                       && !ctx.Game.PlayerIsMoving && ctx.Me.HealthPercent > _settings.LifeTapHealthFloor.Value,
                "Health Funnel", ctx => _settings.PetHealPercent.Value, priority: 0.8f),

            // --- mana / sustain ---
            // Wand first when very low on mana (off the GCD, conserves mana).
            WarlockCommon.Wand(_settings, priority: 1.0f),
            // Life Tap is the signature mana engine; the glyph-uptime tap sits just behind it.
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

            // --- DoTs (single-target upkeep, priority order) ---
            // Haunt: short damage-multiplier debuff — keep it up (cast-time, so stand still; no re-queue mid-cast).
            CombatBlocks.MaintainCastDebuff("Haunt", HauntRefreshMs, priority: 5f),
            // The chosen curse (instant; resolved live from the setting).
            WarlockCommon.MaintainCurse(_settings, priority: 6f),
            // Immolate only when Unstable Affliction is NOT known (they share the slot at higher levels). Cast-time.
            CombatBlocks.MaintainCastDebuff("Immolate", DotRefreshMs, priority: 7f,
                extraGate: ctx => !ctx.Game.IsSpellKnown("Unstable Affliction")),
            // Corruption is instant.
            CombatBlocks.MaintainMyDebuff("Corruption", DotRefreshMs, priority: 8f),
            // Unstable Affliction is cast-time — stand still.
            CombatBlocks.MaintainCastDebuff("Unstable Affliction", DotRefreshMs, priority: 9f),

            // --- Soul Shard harvest (on a dying mob when shards are low; replaces the filler nuke here) ---
            WarlockCommon.DrainSoul(_settings, priority: 9.5f),

            // --- procs ---
            // Shadow Trance (Nightfall) makes the next Shadow Bolt instant — spend it on the move too. Hold it
            // when the DoTs will finish a dying mob (the proc carries to the next pull instead of overkilling).
            Skill.Spell("Shadow Bolt").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("Shadow Trance")
                              && !WarlockCommon.DotsWillFinishTarget(ctx, _settings)),

            // --- filler (cast-time → stand still) ---
            // Skip the filler once the DoTs will finish a low, normal mob on their own — saves mana / Life-Tap.
            Skill.Spell("Shadow Bolt").Priority(20f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !WarlockCommon.DotsWillFinishTarget(ctx, _settings)),
        }), ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
