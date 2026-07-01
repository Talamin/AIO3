using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Shaman
{
    /// <summary>
    /// Solo Enhancement shaman (melee, range ~5; leveling/grinding). Composes the shared <see cref="ShamanCommon"/>
    /// baseline — the four-school totem upkeep, the situational totems, weapon imbues (Windfury main + Flametongue
    /// off), the self-shield, the self-heal, Wind Shear, Flame Shock, the Maelstrom proc, Bloodlust — and adds the
    /// Enhancement filler in priority order: Maelstrom-instant lightning, Fire Nova off a fire totem, Shamanistic
    /// Rage, Feral Spirit, Stormstrike, Earth Shock, Lava Lash. Thin: the step list is built ONCE in the ctor;
    /// unknown spells auto-skip so the same list scales by level.
    /// </summary>
    public sealed class SoloEnhancement : IRotation
    {
        public string Name => "Shaman - Solo Enhancement";

        private readonly ShamanSettings _settings;
        private readonly IReadOnlyList<RotationStep> _steps;

        public SoloEnhancement() : this(new ShamanSettings()) { }

        public SoloEnhancement(ShamanSettings settings)
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
                // Self-heal (Healing Wave; skip if the mob's nearly dead). Highest "real" rotation priority.
                ShamanCommon.SelfHeal(s, "Healing Wave", priority: 0.2f),

                // --- buffs (shield + imbues; kept up like out-of-combat self-buffs) ---
                ShamanCommon.Shield(s, ShamanSpec.Enhancement, priority: 0.5f),
                ShamanCommon.WeaponImbue(s, ShamanSpec.Enhancement, priority: 0.6f),

                // --- melee auto-attack: ensure we're actually swinging (off the GCD). Every other melee spec has
                // this; Enhancement was missing it, so a low-level shaman with no strikes yet just stood there. ---
                CombatBlocks.AutoAttack(priority: 0.7f),

                // --- interrupt ---
                ShamanCommon.WindShear(s, priority: 1.0f),

                // --- Maelstrom Weapon proc: at 5 stacks an instant Lightning Bolt (1) / Chain Lightning (>=2) ---
                Skill.Spell("Chain Lightning").Priority(1.10f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.MaelstromReady(ctx) && ShamanCommon.ManaForOffense(ctx, s)
                                  && ctx.EnemiesNearTarget(ShamanCommon.PackRadius) >= 2),
                Skill.Spell("Lightning Bolt").Priority(1.15f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.MaelstromReady(ctx) && ShamanCommon.ManaForOffense(ctx, s)),

                // --- Fire Nova: a fire totem up + a pack near the target → instant AoE off the totem ---
                Skill.Spell("Fire Nova").Priority(1.20f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s)
                                  && ShamanCommon.TotemUpAndUseful(ctx, ShamanCommon.FireTotemNames)
                                  && ctx.EnemiesNearTarget(ShamanCommon.PackRadius) >= s.FireNovaCount.Value),

                // --- Shamanistic Rage: mana/AP cooldown when low on mana ---
                Skill.Spell("Shamanistic Rage").Priority(1.30f).On(Targets.Self)
                     .When(ctx => ctx.Me.PowerPercent <= s.ShamanisticRageManaPercent.Value),

                // --- Feral Spirit: the wolves, per the criteria setting (or an elite) ---
                Skill.Spell("Feral Spirit").Priority(1.35f).On(Targets.CurrentEnemy)
                     .When(ctx => s.UseCooldowns.Value && FeralSpiritWanted(ctx, s)),
            };

            // The standard four-school totem drops + the situational/temporary totems sit in the ~1.4-2.75 band.
            ShamanCommon.WithSituationalTotems(s, core);
            ShamanCommon.WithSchoolTotems(s, ShamanSpec.Enhancement, core);

            core.AddRange(new[]
            {
                // --- burst cooldown ---
                ShamanCommon.Bloodlust(s, "Bloodlust", priority: 3.0f),
                ShamanCommon.Bloodlust(s, "Heroism", priority: 3.01f),

                // --- melee strikes ---
                // Stormstrike: the Enhancement signature nuke (also boosts our nature damage on the target).
                Skill.Spell("Stormstrike").Priority(4.0f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s)),
                // Flame Shock: maintain the DoT (DyingFloor + mana reserve).
                ShamanCommon.FlameShock(s, priority: 4.5f, extraGate: ctx => ShamanCommon.ManaForOffense(ctx, s)),
                // Earth Shock: instant nuke filler (mana-gated; skipped if Flame Shock already covers the shock CD).
                Skill.Spell("Earth Shock").Priority(5.0f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s)),
                // Lava Lash: off-hand fire strike (auto-skips until learned).
                Skill.Spell("Lava Lash").Priority(5.5f).On(Targets.CurrentEnemy)
                     .When(ctx => ShamanCommon.ManaForOffense(ctx, s)),
                // Low-level filler: hard-cast Lightning Bolt only while OUT of melee (target > ~8yd) — a pull / a poke
                // while the mob closes — so a pre-40 shaman OPENS with a spell then goes melee, instead of standing at
                // range trading Lightning Bolt and casting itself OOM against a caster (Daniel). It STOPS the moment
                // it's in melee → auto-attack + Earth Shock finish it. Paired with the module's dynamic Range (caster
                // to pull, melee once engaged). Inert once Stormstrike (L40) is learned (the Maelstrom-proc instant LB
                // owns high level). Stands still to cast.
                Skill.Spell("Lightning Bolt").Priority(6.0f).On(Targets.CurrentEnemy)
                     .When(ctx => !ctx.Game.IsSpellKnown("Stormstrike") && ShamanCommon.Fighting(ctx)
                                  && ShamanCommon.ManaForOffense(ctx, s) && !ctx.Game.PlayerIsMoving
                                  && ctx.HasEnemyTarget && ctx.Target.Distance > 8f),
            });

            // Racials append at the 2.5-band default; keep the band below the school totems so survival wins.
            return Racials.With(core, ctx => s.UseRacials.Value, basePriority: 2.4f);
        }

        /// <summary>Whether Feral Spirit should fire under the criteria dropdown: "+2 and Elite" → 2 attackers near
        /// the target OR an elite; "+3 and Elite" → 3 or elite; "only Elite" → elite; "None" → never. Mirrors the
        /// old SoloEnhancement Feral Spirit ladder, collapsed into one gate.</summary>
        private static bool FeralSpiritWanted(CombatContext ctx, ShamanSettings s)
        {
            if (!ctx.HasEnemyTarget) return false;
            bool elite = ctx.Target.IsElite || ctx.Target.IsBoss();
            int near = ctx.EnemiesNearTarget(ShamanCommon.SurroundRadius);
            switch (s.FeralSpirit.Value)
            {
                case "+2 and Elite": return elite || near >= 2;
                case "+3 and Elite": return elite || near >= 3;
                case "only Elite": return elite;
                default: return false; // "None"
            }
        }
    }
}
