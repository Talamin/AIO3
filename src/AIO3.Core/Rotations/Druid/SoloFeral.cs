using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Druid
{
    /// <summary>
    /// Solo Feral druid (leveling/grinding, 10-80). The leveling default: a single priority list with form gates
    /// that scales from level 10 because every form-specific ability auto-skips (IsSpellKnown) until learned. Cat
    /// Form is the single-target DPS form (energy + combo points → builders Mangle/Claw, bleeds Rake/Rip, dumps
    /// Ferocious Bite); (Dire) Bear Form is the tank/AoE form when surrounded (rage → Mangle/Maul/Swipe, survival
    /// Frenzied Regeneration). Pre-form low levels fall back to a caster filler (Wrath/Moonfire) + Auto Attack.
    ///
    /// Thin: composes the shared <see cref="DruidCommon"/> hybrid baseline, the Layer 3 <see cref="CombatBlocks"/>
    /// (interrupt / emergency item) and the racials, and adds only the form-switch glue + the pre-form caster
    /// fallback in priority order. Ported from the old Combat/Druid/SoloFeral.cs (+ its LowLevel pre-form steps).
    /// </summary>
    public sealed class SoloFeral : IRotation
    {
        public string Name => "Druid - Solo Feral";

        private readonly DruidSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloFeral() : this(new DruidSettings()) { }

        public SoloFeral(DruidSettings settings)
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
            DruidCommon.Barkskin(_settings, priority: 0.1f),
            // Survival Instincts (off the GCD) — the Feral max-health emergency cooldown (cat or bear); auto-skips
            // until the talent is learned. Sits beside Barkskin so both panic buttons fire when low.
            DruidCommon.SurvivalInstincts(_settings, priority: 0.15f),
            // Bear survival (off the GCD) — Frenzied Regeneration converts rage to health when low in bear form.
            DruidCommon.FrenziedRegeneration(_settings, priority: 0.2f),

            // --- in-combat self-heal (the druid's edge) ---
            // Prefer the form-preserving instant via Predator's Swiftness, then the shift-out heals (mana-gated).
            DruidCommon.InstantProcHeal(_settings, "Regrowth", s => s.UseRegrowthIC.Value, priority: 0.3f),
            DruidCommon.InstantProcHeal(_settings, "Healing Touch", s => s.UseHealingTouchIC.Value, priority: 0.31f),
            DruidCommon.ShiftOutHeal(_settings, "Regrowth", s => s.UseRegrowthIC.Value, priority: 0.4f),
            DruidCommon.ShiftOutHeal(_settings, "Rejuvenation", s => s.UseRejuvenationIC.Value, priority: 0.41f),
            DruidCommon.ShiftOutHeal(_settings, "Healing Touch", s => s.UseHealingTouchIC.Value, priority: 0.42f),
            // Innervate when low on mana (so the self-heals stay affordable).
            DruidCommon.Innervate(_settings, priority: 0.5f),

            // --- out-of-combat buffs (Mark of the Wild / Thorns) ---
            DruidCommon.MarkOfTheWild(_settings, priority: 0.6f),
            DruidCommon.Thorns(_settings, priority: 0.61f),

            // --- form switching (Bear when surrounded wins; else Cat for single-target) ---
            // Bear sits ABOVE Cat so the surrounded check decides the form; both auto-skip until learned.
            DruidCommon.BearForm(_settings, priority: 0.8f),
            DruidCommon.CatForm(_settings, priority: 0.85f),

            // --- baseline / upkeep ---
            CombatBlocks.AutoAttack(priority: 1f),
            // Bash interrupt (Bear) — shared Layer 3 interrupt; only lands in bear form (range/known gate it).
            CombatBlocks.Interrupt("Bash", priority: 2f, mode: ctx => _settings.InterruptMode.Value),

            // (racials are appended by the shared Racials bundle at the 2.5 band)

            // --- Feral burst cooldown (Berserk on a boss/elite/pack, when enabled) ---
            DruidCommon.Berserk(_settings, priority: 3f),

            // --- stealth opener (opt-in): Prowl out of combat, then Ravage (behind) / Pounce (front) ---
            DruidCommon.Prowl(_settings, priority: 3.5f),
            DruidCommon.ProwlOpener(_settings, "Ravage", priority: 3.6f),
            DruidCommon.ProwlOpener(_settings, "Pounce", priority: 3.6f),

            // --- armor debuff (cat or bear) ---
            DruidCommon.FaerieFireFeral(_settings, priority: 4f),

            // --- Bear ladder (tank/AoE; each step gates on bear form so it's inert in cat) ---
            DruidCommon.DemoralizingRoar(_settings, priority: 5f),
            DruidCommon.Enrage(priority: 5.1f),
            DruidCommon.MangleBear(_settings, priority: 5.2f),
            DruidCommon.Lacerate(_settings, priority: 5.3f),
            DruidCommon.SwipeBear(_settings, priority: 5.4f),
            DruidCommon.Maul(_settings, priority: 5.5f),         // off-GCD rage dump

            // --- Cat ladder (single-target DPS; each step gates on cat form so it's inert in bear) ---
            // Order: off-GCD CD → bleed apply → finishers (Savage Roar keeps the melee buff up, then Rip, then the
            // Ferocious Bite dump) → debuff-maintain → builders (Shred behind, else Mangle, else Claw).
            DruidCommon.TigersFury(_settings, priority: 6f),        // off-GCD energy/damage CD (held near energy cap)
            DruidCommon.Rake(_settings, priority: 6.2f),            // bleed (apply when missing, boss-aware HP-floor)
            DruidCommon.SavageRoar(_settings, priority: 6.25f),     // highest finisher: keep the +melee-damage buff up
            DruidCommon.Rip(_settings, priority: 6.3f),             // bleed finisher (durable targets)
            DruidCommon.FerociousBite(_settings, priority: 6.4f),   // direct-damage dump finisher
            DruidCommon.MangleCatDebuff(_settings, priority: 6.5f), // maintain the +30% bleed debuff (cat)
            DruidCommon.Shred(_settings, priority: 6.6f),           // best builder, behind-only
            DruidCommon.MangleCat(_settings, priority: 6.7f),       // front-fallback builder
            DruidCommon.Claw(_settings, priority: 6.8f),            // fallback builder

            // --- pre-form caster fallback (only while NOT in any combat form — a low-level druid before Cat/Bear) ---
            // These auto-skip once shifted (the form gate) AND once unlearned spells are replaced by the forms, so
            // the one list scales: at L10 it's Moonfire + Wrath, and the moment Cat/Bear is learned the form steps
            // take over and these go quiet.
            Skill.Spell("Moonfire").Priority(8f).On(Targets.CurrentEnemy)
                 .When(ctx => !DruidCommon.InAnyForm(ctx)
                              && !ctx.Target.HasMyAura("Moonfire")
                              && ctx.Target.HealthPercent > _settings.RipHealth.Value),
            Skill.Spell("Wrath").Priority(9f).On(Targets.CurrentEnemy)
                 .When(ctx => !DruidCommon.InAnyForm(ctx) && !ctx.Game.PlayerIsMoving),

        }, ctx => _settings.UseRacials.Value, basePriority: 2.5f);
    }
}
