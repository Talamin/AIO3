using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Shaman
{
    /// <summary>
    /// Solo Elemental shaman (caster, range ~27; leveling/grinding). Composes the shared <see cref="ShamanCommon"/>
    /// baseline — the four-school totem upkeep, the situational totems, the single Flametongue imbue, the
    /// self-shield (Lightning Shield), the self-heal, Wind Shear, Flame Shock, Bloodlust — and adds the Elemental
    /// nuke core in priority order: Flame Shock (the Lava Burst enabler), Lava Burst when the target carries our
    /// Flame Shock (the synergy), Elemental Mastery, Chain Lightning on a pack (fallback Lightning Bolt), Lightning
    /// Bolt single, Earth Shock as the instant/moving filler. Cast-time spells gate <c>!PlayerIsMoving</c> (a caster
    /// must stand still); instants (the shocks, Lava Burst under Elemental Mastery is still a cast) are listed
    /// accordingly. Thin: the step list is built ONCE in the ctor; unknown spells auto-skip so it scales by level.
    /// </summary>
    public sealed class SoloElemental : IRotation
    {
        public string Name => "Shaman - Solo Elemental";

        private readonly ShamanSettings _settings;
        private readonly IReadOnlyList<RotationStep> _steps;

        public SoloElemental() : this(new ShamanSettings()) { }

        public SoloElemental(ShamanSettings settings)
        {
            _settings = settings;
            _steps = BuildList();
        }

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => _steps;

        private IReadOnlyList<RotationStep> BuildList()
        {
            ShamanSettings s = _settings;

            var core = new List<RotationStep>
            {
                // --- emergency survival ---
                CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                    ctx => s.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < s.EmergencyHealthPercent.Value,
                    priority: 0.05f),
                // Self-heal (Healing Wave; cast-time, so hold while moving — skip if the mob's nearly dead).
                ShamanCommon.SelfHeal(s, "Healing Wave", priority: 0.2f, requireStationary: true),

                // --- buffs (Lightning Shield + Flametongue imbue) ---
                ShamanCommon.Shield(s, ShamanSpec.Elemental, priority: 0.5f),
                ShamanCommon.WeaponImbue(s, ShamanSpec.Elemental, priority: 0.6f),

                // --- interrupt ---
                ShamanCommon.WindShear(s, priority: 1.0f),

                // --- Flame Shock: maintain the DoT (instant — fine while moving). The Lava Burst enabler. ---
                ShamanCommon.FlameShock(s, priority: 1.10f, extraGate: ctx => ShamanCommon.ManaForOffense(ctx, s)),

                // --- Lava Burst: only when the target carries OUR Flame Shock (the synergy — guaranteed crit). ---
                Skill.Spell("Lava Burst").Priority(1.20f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s) && !ctx.Game.PlayerIsMoving
                                  && ctx.HasEnemyTarget && ctx.Target.HasMyAura("Flame Shock")),

                // --- Elemental Mastery: the next-cast instant + crit cooldown ---
                Skill.Spell("Elemental Mastery").Priority(1.25f).On(Targets.Self)
                     .When(ctx => s.UseCooldowns.Value && ShamanCommon.IsBigFight(ctx) && !ctx.Me.HasAura("Elemental Mastery")),
            };

            // The standard four-school totem drops + the situational/temporary totems sit in the ~1.4-2.75 band.
            ShamanCommon.WithSituationalTotems(s, core);
            ShamanCommon.WithSchoolTotems(s, ShamanSpec.Elemental, core);

            core.AddRange(new[]
            {
                // --- burst cooldown ---
                ShamanCommon.Bloodlust(s, "Bloodlust", priority: 3.0f),
                ShamanCommon.Bloodlust(s, "Heroism", priority: 3.01f),

                // --- Earth Shock: instant filler / when moving (a caster's only damage on the run). ---
                Skill.Spell("Earth Shock").Priority(4.0f).On(Targets.CurrentEnemy)
                     .When(ctx => s.ElementalEarthShock.Value && ShamanCommon.ManaForOffense(ctx, s)
                                  && (ctx.Game.PlayerIsMoving || !ctx.Game.IsSpellKnown("Lava Burst"))),

                // --- Chain Lightning: a pack near the target (cast-time → stand still). Fallback Lightning Bolt
                //     when Chain Lightning isn't known yet. ---
                Skill.Spell("Chain Lightning").Priority(5.0f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s) && !ctx.Game.PlayerIsMoving
                                  && ctx.EnemiesNearTarget(ShamanCommon.PackRadius) >= s.ChainLightningCount.Value),

                // --- Lightning Bolt: the single-target filler (cast-time → stand still). ---
                Skill.Spell("Lightning Bolt").Priority(6.0f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s) && !ctx.Game.PlayerIsMoving),
            });

            // Racials append below the school totems so survival/totems win.
            return Racials.With(core, ctx => s.UseRacials.Value, basePriority: 2.4f);
        }
    }
}
