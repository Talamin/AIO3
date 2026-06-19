using System;
using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Library
{
    /// <summary>
    /// Layer 3 — the shared step library. Parameterised, reusable combat behaviours that
    /// every spec composes instead of re-implementing. This is the mechanism that keeps
    /// the "all-in-one" consistent: improve a block here and every spec that uses it
    /// improves at once, with no edit to the spec. Each block returns a ready RotationStep.
    /// </summary>
    public static class CombatBlocks
    {
        /// <summary>
        /// Interrupt an enemy that is currently casting, using <paramref name="spell"/>.
        /// The DSL's automatic range gate ensures the caster is within interrupt range.
        /// (Detecting whether a cast is actually interruptible is a later refinement.)
        /// </summary>
        /// <summary>
        /// Interrupt an enemy cast. <paramref name="mode"/> returns Always / Smart / Never:
        /// Always tries every cast; Smart skips spells the InterruptTracker learned are non-interruptible;
        /// Never disables it (e.g. a product handles interrupts). Records each attempt so the tracker can learn.
        /// </summary>
        public static RotationStep Interrupt(string spell, float priority = 1f, Func<CombatContext, string> mode = null) =>
            new RotationStep(
                name: spell,
                priority: priority,
                targets: Targets.EnemiesCasting,
                condition: (ctx, t) =>
                {
                    if (!ctx.Game.IsSpellKnown(spell) || !ctx.Game.IsSpellReady(spell)) return false;
                    float range = ctx.Game.SpellRange(spell);
                    if (range > 0f && t.Distance > range) return false;

                    string m = mode == null ? InterruptModes.Always : mode(ctx);
                    if (m == InterruptModes.Never) return false;
                    if (m == InterruptModes.Smart && !ctx.Interrupts.ShouldInterrupt(t.CastingSpellId)) return false;
                    return true;
                },
                action: (ctx, t) =>
                {
                    ctx.Interrupts.RecordAttempt(t.Guid, t.CastingSpellId);
                    return ctx.Game.Cast(spell, t);
                },
                ignoreGcd: true);

        /// <summary>
        /// Ensure melee auto-attack is running on the current target (off the GCD). WRobot's own
        /// fight engine usually does this, but the old rotations keep it as a safety net.
        /// </summary>
        public static RotationStep AutoAttack(float priority = 0.5f) =>
            Skill.Spell("Auto Attack")
                 .Priority(priority)
                 .On(Targets.CurrentEnemy) // never start swinging at a friendly NPC
                 .When(ctx => !ctx.Game.PlayerIsCasting && !ctx.Game.PlayerIsAutoAttacking)
                 .OffGcd()
                 .Build();

        /// <summary>
        /// Cast a self-defensive when the player's health drops below
        /// <paramref name="healthPercent"/>.
        /// </summary>
        public static RotationStep DefensiveBelow(string spell, double healthPercent, float priority = 2f) =>
            Skill.Spell(spell)
                 .Priority(priority)
                 .On(Targets.Self)
                 .When(ctx => ctx.Me.HealthPercent < healthPercent)
                 .Build();

        /// <summary>
        /// Keep a self-buff up (cast when missing). Useful for shouts, armors, auras, etc.
        /// <paramref name="supersededBy"/> lists better/exclusive buffs that make this one
        /// unnecessary (e.g. don't cast Battle Shout if a paladin's Greater Blessing of Might
        /// is already up).
        /// </summary>
        public static RotationStep SelfBuff(string spell, float priority = 3f, params string[] supersededBy) =>
            Skill.Spell(spell)
                 .Priority(priority)
                 .On(Targets.Self)
                 .When(ctx => !ctx.Me.HasAura(spell) && !HasAnyAura(ctx, supersededBy))
                 .Build();

        /// <summary>
        /// Keep our own debuff up on the current enemy: cast when it is missing OR about to expire
        /// (less than <paramref name="minMsLeft"/> remaining). Reusable by any DoT/debuff spec.
        /// </summary>
        public static RotationStep MaintainMyDebuff(string spell, int minMsLeft, float priority) =>
            Skill.Spell(spell)
                 .Priority(priority)
                 .On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Target.HasMyAura(spell) || ctx.Target.MyAuraTimeLeftMs(spell) < minMsLeft);

        /// <summary>
        /// Use the first ready item from <paramref name="names"/> when <paramref name="when"/> holds
        /// (e.g. an emergency healthstone/potion below a health threshold). Off the GCD.
        /// </summary>
        public static RotationStep UseItems(string label, IReadOnlyList<string> names, Func<CombatContext, bool> when, float priority) =>
            new RotationStep(
                name: label,
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => when(ctx),
                action: (ctx, t) => ctx.Game.UseFirstReadyItem(names) ? CastResult.Success : CastResult.Failed,
                ignoreGcd: true);

        /// <summary>An offensive racial (used in combat with an enemy, off the GCD; IsSpellKnown gates by race).</summary>
        public static RotationStep OffensiveRacial(string spell, float priority, Func<CombatContext, bool> enabled) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat && ctx.HasEnemyTarget).OffGcd();

        private static bool HasAnyAura(CombatContext ctx, string[] names)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Length; i++)
                if (ctx.Me.HasAura(names[i])) return true;
            return false;
        }
    }
}
