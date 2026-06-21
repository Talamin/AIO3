using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Rotations.Hunter
{
    /// <summary>
    /// Shared hunter building blocks reused by every spec: aspect management, the maintained debuffs,
    /// auto-shot, and the ranged-class survival/utility. Each returns a ready RotationStep; the spec just
    /// lists them in priority order alongside its signature shots. The aspect is resolved at eval time so
    /// overlay edits apply live.
    ///
    /// All ranged shots gate on the target being at least <see cref="RangedMin"/> yards away: a hunter
    /// can't shoot point-blank, and (unlike a melee miss) a "successful" out-of-range cast would stall the
    /// engine. Below that range the spec's melee fallback (Raptor Strike) takes over.
    /// </summary>
    public static class HunterCommon
    {
        /// <summary>Minimum range for ranged shots (no shooting inside melee).</summary>
        public const float RangedMin = 5f;

        /// <summary>Radius that defines a "pack" for the AoE / cooldown gates (one source for the BM spec).</summary>
        public const float AoeRadius = 10f;

        /// <summary>Keep Auto Shot running on the target (off the GCD; only re-toggled when not already shooting).</summary>
        public static RotationStep AutoShot(float priority) =>
            Skill.Spell("Auto Shot").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= RangedMin
                              && !ctx.Game.PlayerIsCasting
                              && !ctx.Game.IsCurrentSpell("Auto Shot")).OffGcd();

        /// <summary>Keep the right aspect up: Aspect of the Viper to regen below the mana floor, otherwise
        /// the best damage aspect we know (Dragonhawk, else Hawk). In the band between the two thresholds we
        /// keep whatever is up (hysteresis), but never sit aspect-less.</summary>
        public static RotationStep Aspect(HunterSettings s, float priority) =>
            new RotationStep(
                name: "Aspect",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    string aspect = ResolveAspect(ctx, s);
                    return aspect != null && !ctx.Me.HasAura(aspect);
                },
                action: (ctx, t) =>
                {
                    string aspect = ResolveAspect(ctx, s);
                    return aspect != null ? ctx.Game.Cast(aspect, ctx.Me) : CastResult.Failed;
                });

        /// <summary>Apply Hunter's Mark when missing and the target is still healthy enough to be worth it.</summary>
        public static RotationStep HuntersMark(float priority) =>
            Skill.Spell("Hunter's Mark").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Target.HasMyAura("Hunter's Mark") && ctx.Target.HealthPercent > 50);

        /// <summary>Apply Serpent Sting when missing on a target healthy enough to tick it out.</summary>
        public static RotationStep SerpentSting(float priority) =>
            Skill.Spell("Serpent Sting").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= RangedMin
                              && !ctx.Target.HasMyAura("Serpent Sting")
                              && ctx.Target.HealthPercent > 30);

        /// <summary>Ranged execute: Kill Shot under 20%.</summary>
        public static RotationStep KillShot(float priority) =>
            Skill.Spell("Kill Shot").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= RangedMin && ctx.Target.HealthPercent < 20);

        /// <summary>Kill Command: a focus dump that makes the pet hit harder — needs an alive pet.
        /// Baseline, so Beast Mastery and Survival both use it.</summary>
        public static RotationStep KillCommand(float priority) =>
            Skill.Spell("Kill Command").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Pet != null && ctx.Pet.IsAlive);

        /// <summary>Hand the pet threat with Misdirection (solo) when something is on us and the pet is up.</summary>
        public static RotationStep Misdirection(HunterSettings s, float priority) =>
            Skill.Spell("Misdirection").Priority(priority).On(Targets.Pet)
                 .When((ctx, t) => s.UseMisdirection.Value && t.IsAlive
                                   && !ctx.Me.HasAura("Misdirection")
                                   && ctx.EnemiesTargetingMe >= 1);

        /// <summary>Feign Death to shed aggro when low — only while the pet is alive to keep the mobs.</summary>
        public static RotationStep FeignDeath(HunterSettings s, float priority) =>
            Skill.Spell("Feign Death").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseFeignDeath.Value
                              && ctx.Pet != null && ctx.Pet.IsAlive
                              && ctx.Me.HealthPercent < 50 && ctx.EnemiesTargetingMe >= 1);

        /// <summary>Step back to ranged distance when a mob has closed to melee but is on the PET, not us —
        /// so backing up regains ranged uptime without kiting the mob around (it stays on the pet). The
        /// adapter refuses the move over a ledge, so this never walks us off a cliff. Only while the pet is
        /// alive and tanking; if the mob is on us instead, Feign Death / Disengage handle it.</summary>
        public static RotationStep Backpedal(HunterSettings s, float priority) =>
            new RotationStep(
                name: "Backpedal",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => s.UseBackpedal.Value
                                       && ctx.Game.PlayerInCombat
                                       && ctx.Pet != null && ctx.Pet.IsAlive
                                       && ctx.Target != null && ctx.Target.IsAlive
                                       && ctx.Target.Distance < RangedMin
                                       && ctx.Target.IsTargetingMyPet,
                action: (ctx, t) => ctx.Game.StepBack(s.BackpedalYards.Value) ? CastResult.Success : CastResult.Failed,
                ignoreGcd: true,
                recastDelayMs: 1500);

        /// <summary>Disengage (leap back) when a mob is on us in melee, if enabled.</summary>
        public static RotationStep Disengage(HunterSettings s, float priority) =>
            Skill.Spell("Disengage").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseDisengage.Value && ctx.Target.Distance < RangedMin && ctx.Target.IsTargetingMe);

        /// <summary>Slow a fleeing humanoid below 40% with Concussive Shot.</summary>
        public static RotationStep ConcussiveShot(float priority) =>
            Skill.Spell("Concussive Shot").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= RangedMin
                              && ctx.Target.HealthPercent < 40
                              && ctx.Target.CreatureType == "Humanoid"
                              && !ctx.Target.HasAura("Concussive Shot"));

        private static string ResolveAspect(CombatContext ctx, HunterSettings s)
        {
            string hawk = ctx.Game.IsSpellKnown("Aspect of the Dragonhawk") ? "Aspect of the Dragonhawk"
                        : ctx.Game.IsSpellKnown("Aspect of the Hawk") ? "Aspect of the Hawk"
                        : null;
            bool knowViper = ctx.Game.IsSpellKnown("Aspect of the Viper");

            double mana = ctx.Me.PowerPercent;
            if (mana < s.AspectViperManaPercent.Value && knowViper) return "Aspect of the Viper";
            if (mana > s.AspectHawkManaPercent.Value) return hawk;

            // In the hysteresis band: keep whatever is up; if somehow aspect-less, apply the damage aspect.
            if (knowViper && ctx.Me.HasAura("Aspect of the Viper")) return "Aspect of the Viper";
            if (hawk != null && ctx.Me.HasAura(hawk)) return hawk;
            return hawk;
        }
    }
}
