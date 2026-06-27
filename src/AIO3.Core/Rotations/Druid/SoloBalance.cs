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
    /// Solo Balance druid (leveling/grinding, 10-80). An Eclipse caster on the shared caster baseline: keep
    /// Moonkin Form up, maintain the Moonfire + Insect Swarm DoTs (with the dying-mob HP-floor), and ride the
    /// Eclipse cycle — Starfire under Lunar (or while Nature's Grace is up), Wrath under Solar, Wrath as the
    /// default filler. Starfire opens on a full-HP, not-yet-attacking target. AoE (Starfall / Hurricane /
    /// Typhoon) and Force of Nature gate on enemy count / boss. Survival: shift-out self-heal, Barkskin,
    /// Innervate, Faerie Fire on bosses.
    ///
    /// Thin: composes the shared <see cref="DruidCommon"/> survival/buff baseline + the Layer 3 blocks; only the
    /// Eclipse / DoT / AoE filler lives here, in priority order. Cast-time nukes gate on <c>!PlayerIsMoving</c>
    /// (a caster stands still), like the mage nukes. Ported from the old Combat/Druid/SoloBalance.cs.
    /// </summary>
    public sealed class SoloBalance : IRotation
    {
        public string Name => "Druid - Solo Balance";

        // The Balance AoE / Force of Nature anchor on the TARGET's cluster (a ranged caster stands away from the
        // pack, so a player-relative count would rarely trip). 33y mirrors the old AIO's Starfall radius.
        private const float AoeRadius = 33f;

        /// <summary>The opener (Starfire) only fires on an essentially-fresh target — at or above this HP%. Not a
        /// hard 100% (a pre-pull DoT / pet / a stray tick can chip it to 99% and the opener would never fire); this
        /// is the "first hit of a fresh pull" threshold paired with the not-yet-attacking check.</summary>
        private const int OpenerHealthPercent = 90;

        private readonly DruidSettings _settings;
        private readonly List<RotationStep> _steps;

        public SoloBalance() : this(new DruidSettings()) { }

        public SoloBalance(DruidSettings settings)
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
            DruidCommon.Barkskin(_settings, priority: 0.1f),

            // --- in-combat self-heal (shift out of Moonkin; mana-gated) ---
            DruidCommon.ShiftOutHeal(_settings, "Regrowth", s => s.UseRegrowthIC.Value, priority: 0.3f),
            DruidCommon.ShiftOutHeal(_settings, "Rejuvenation", s => s.UseRejuvenationIC.Value, priority: 0.31f),
            DruidCommon.ShiftOutHeal(_settings, "Healing Touch", s => s.UseHealingTouchIC.Value, priority: 0.32f),
            DruidCommon.Innervate(_settings, priority: 0.5f),

            // --- out-of-combat buffs ---
            DruidCommon.MarkOfTheWild(_settings, priority: 0.6f),
            DruidCommon.Thorns(_settings, priority: 0.61f),

            // --- Moonkin Form upkeep (the caster form; auto-skips until learned, so a pre-form druid just nukes) ---
            Skill.Spell("Moonkin Form").Priority(0.8f).On(Targets.Self)
                 .When(ctx => !DruidCommon.InMoonkinForm(ctx)),

            // --- baseline / interrupt ---
            CombatBlocks.AutoAttack(priority: 1f),

            // (racials are appended by the shared Racials bundle at the 2.5 band)

            // --- AoE (anchored on the target's cluster; held nothing extra) ---
            // Starfall: a channelled cooldown — on a boss, or a big pack when enabled.
            Skill.Spell("Starfall").Priority(3.0f).On(Targets.Self)
                 .When(ctx => _settings.UseStarfall.Value && _settings.UseCooldowns.Value && ctx.HasEnemyTarget
                              && (ctx.Target.IsBoss()
                                  || ctx.EnemiesNearTarget(AoeRadius) >= _settings.AoeTargets.Value)),
            // Typhoon: an instant cone knockback/AoE on a pack.
            Skill.Spell("Typhoon").Priority(3.1f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value
                              && ctx.EnemiesNearTarget(AoeRadius) >= _settings.AoeTargets.Value),
            // Hurricane: a channelled ground AoE on a pack (cast-time → stand still).
            Skill.Spell("Hurricane").Priority(3.2f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseAoe.Value && !ctx.Game.PlayerIsMoving
                              && ctx.EnemiesNearTarget(AoeRadius) >= _settings.AoeTargets.Value),

            // --- Force of Nature (treants) on a boss/elite, when enabled ---
            Skill.Spell("Force of Nature").Priority(3.5f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseForceOfNature.Value && _settings.UseCooldowns.Value
                              && ctx.HasEnemyTarget && (ctx.Target.IsBoss() || ctx.Target.IsElite)),

            // --- armor debuff on bosses (Faerie Fire — the caster version) ---
            Skill.Spell("Faerie Fire").Priority(3.8f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.UseFaerieFire.Value && ctx.Target.IsBoss()
                              && !ctx.Target.HasAura("Faerie Fire")),

            // --- opener: Starfire on a full-HP, not-yet-attacking target (a big front-loaded nuke) ---
            // Sits ABOVE the DoT maintenance so a fresh pull LEADS with Starfire (its long cast lands as the mob
            // closes) instead of front-loading a DoT — then the DoTs go up on the next GCDs.
            Skill.Spell("Starfire").Priority(3.9f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving
                              && ctx.Target.HealthPercent >= OpenerHealthPercent && !ctx.Target.IsTargetingMe),

            // --- DoTs (maintain when missing/expiring, with the dying-mob HP-floor) ---
            // Suppressed DURING an Eclipse window on trash: re-applying a DoT mid-Eclipse clips a GCD out of the
            // damage burst the proc exists for. On a BOSS the fight is long enough that the DoT uptime is worth more
            // than one clipped Eclipse GCD, so the suppression lifts (mirrors the old FC keeping DoTs up on bosses).
            CombatBlocks.MaintainMyDebuff("Insect Swarm", DotRefreshMs, priority: 4.0f,
                extraGate: ctx => _settings.UseInsectSwarm.Value
                                  && ctx.Target.HealthPercent > _settings.DotHealth.Value
                                  && (!InEclipse(ctx) || ctx.Target.IsBoss())),
            CombatBlocks.MaintainMyDebuff("Moonfire", DotRefreshMs, priority: 4.1f,
                extraGate: ctx => _settings.UseMoonfire.Value
                                  && ctx.Target.HealthPercent > _settings.DotHealth.Value
                                  && (!InEclipse(ctx) || ctx.Target.IsBoss())),

            // --- Eclipse rotation (cast-time → stand still) ---
            // Starfire under Lunar eclipse / Nature's Grace (the arcane side of the cycle). Guarded so a Solar
            // eclipse always routes to Wrath (never Starfire), even if Nature's Grace happens to overlap.
            Skill.Spell("Starfire").Priority(5.0f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !ctx.Me.HasAura("Eclipse (Solar)")
                              && (ctx.Me.HasAura("Eclipse (Lunar)") || ctx.Me.HasAura("Nature's Grace"))),
            // Wrath under Solar eclipse (the nature side of the cycle).
            Skill.Spell("Wrath").Priority(5.1f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && ctx.Me.HasAura("Eclipse (Solar)")),

            // --- default filler: Wrath when no eclipse is up (cast-time → stand still) ---
            Skill.Spell("Wrath").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving),

        }, ctx => _settings.UseRacials.Value, basePriority: 2.5f);

        /// <summary>True while either Eclipse proc is up (Lunar or Solar) — the window during which a DoT refresh on
        /// trash is suppressed so it doesn't clip the burst. Read once here so both DoT gates agree.</summary>
        private static bool InEclipse(CombatContext ctx) =>
            ctx.Me.HasAura("Eclipse (Lunar)") || ctx.Me.HasAura("Eclipse (Solar)");

        // DoT refresh window (re-apply when under this many ms remain). Routes through MaintainMyDebuff so the
        // shared post-cast grace stops the apply-latency double-cast (Moonfire/Insect Swarm last ~12s).
        private const int DotRefreshMs = 2000;
    }
}
