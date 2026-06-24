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
    /// Solo Demonology warlock (leveling/grinding). Single-target caster carried by a beefy Felguard: keep the
    /// demon summoned and on the target, keep Demonic Empowerment up on it, sustain mana with Life Tap (and the
    /// wand at low mana), self-heal with Drain Life when low and solo, maintain curse + Corruption + Immolate,
    /// spend a Decimation/Molten-Core-style proc as a Soul Fire, and fill with Shadow Bolt. Everything
    /// pet-related is gated on the pet actually existing (<c>ctx.Pet</c>), and every spell auto-skips when
    /// unknown, so a low-level demo lock plays as a clean caster and fills in as it levels.
    ///
    /// Thin: composes the shared <see cref="WarlockCommon"/> caster baseline, <see cref="PetControl"/>, and the
    /// Layer 3 <see cref="CombatBlocks"/>. Multi-target (Seed of Corruption / Rain of Fire), Metamorphosis, and
    /// the pet's special abilities are deferred.
    /// </summary>
    public sealed class SoloDemonology : IRotation
    {
        public string Name => "Warlock - Solo Demonology";

        // DoT refresh window: re-apply when fewer than this many ms remain (avoid clipping ticks).
        private const int DotRefreshMs = 2000;

        private readonly WarlockSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloDemonology() : this(new WarlockSettings()) { }

        public SoloDemonology(WarlockSettings settings)
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
            // Keep a Healthstone stocked (OOC) so the emergency-heal item above always has one to use.
            WarlockCommon.CreateHealthstone(_settings, priority: 0.45f),

            // --- pet upkeep (all skip when petless; Auto resolves to Felguard, else Voidwalker if unlearned) ---
            PetControl.Summon(ctx => _settings.ManagePet.Value,
                ctx => WarlockCommon.SummonSpell(_settings, ctx, WarlockSpec.Demonology),
                ctx => WarlockCommon.SummonSpell(_settings, ctx, WarlockSpec.Demonology), priority: 0.6f),
            PetControl.Attack(ctx => _settings.ManagePet.Value, priority: 0.7f),
            // Health Funnel the demon when low (opt-in; channelled, so only while standing still and we have HP).
            PetControl.Heal(
                ctx => _settings.ManagePet.Value && _settings.PetHealPercent.Value > 0
                       && !ctx.Game.PlayerIsMoving && ctx.Me.HealthPercent > _settings.LifeTapHealthFloor.Value,
                "Health Funnel", ctx => _settings.PetHealPercent.Value, priority: 0.8f),

            // --- Demonic Empowerment: spec buff on the demon (instant; auto-skips if unknown / petless) ---
            Skill.Spell("Demonic Empowerment").Priority(0.9f).On(Targets.Pet)
                 .When((ctx, t) => _settings.DemonicEmpowerment.Value && t.IsAlive && !t.HasAura("Demonic Empowerment")),

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

            // --- DoTs (single-target upkeep, priority order) ---
            // The chosen curse (instant; resolved live from the setting).
            WarlockCommon.MaintainCurse(_settings, priority: 6f),
            // Corruption is instant.
            CombatBlocks.MaintainMyDebuff("Corruption", DotRefreshMs, priority: 8f),
            // Immolate is cast-time — stand still; the block also guards against re-queueing it mid-cast.
            CombatBlocks.MaintainCastDebuff("Immolate", DotRefreshMs, priority: 9f),

            // --- Soul Shard harvest (on a dying mob when shards are low; sits above the Soul Fire / filler) ---
            WarlockCommon.DrainSoul(_settings, priority: 9.5f),

            // --- proc spender ---
            // Soul Fire is a hard-hitting cast normally made instant/cheap by a Demonology proc (Decimation /
            // Molten Core). Gate on the proc buff so we only spend it when worth it; off the proc the filler
            // Shadow Bolt below wins. Cast-time, so stand still.
            Skill.Spell("Soul Fire").Priority(15f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseSoulFire.Value
                              && (ctx.Me.HasAura("Decimation") || ctx.Me.HasAura("Molten Core"))
                              && !ctx.Game.PlayerIsMoving
                              && !WarlockCommon.DotsWillFinishTarget(ctx, _settings)),

            // --- filler (cast-time → stand still) ---
            // Skip the filler once the DoTs will finish a low, normal mob on their own — saves mana / Life-Tap.
            Skill.Spell("Shadow Bolt").Priority(20f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !WarlockCommon.DotsWillFinishTarget(ctx, _settings)),
        }), ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
