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

        // After we apply a DoT, the debuff isn't visible in our per-tick snapshot until the server round-trips —
        // ~0.6-1.1s for an instant cast, ~2.5s measured from the START of a cast-time spell. Without a throttle the
        // maintain step sees the aura "missing" in that gap and casts a SECOND time (the Immolate / Corruption
        // double-cast seen in the debug logs). A short post-cast throttle (the recast delay timer only starts on a
        // SUCCESSFUL cast) suppresses the re-cast until the aura has had time to land. DoTs last 12-24s, so this
        // never delays a genuine refresh (which only happens when <minMsLeft remains).
        private const int InstantDebuffApplyGraceMs = 1500; // instant DoTs (Corruption, Rend) — apply latency
        private const int CastDebuffApplyGraceMs = 3000;    // cast-time DoTs (Immolate, UA, Haunt) — cast time + latency

        /// <summary>
        /// Keep our own debuff up on the current enemy: cast when it is missing OR about to expire
        /// (less than <paramref name="minMsLeft"/> remaining). Reusable by any DoT/debuff spec. A post-cast grace
        /// (<see cref="InstantDebuffApplyGraceMs"/>) stops a second cast in the window before the freshly applied
        /// debuff becomes visible (the instant-DoT double-cast).
        /// <paramref name="extraGate"/> adds an optional extra condition AND-ed into the When (mirrors
        /// <see cref="MaintainCastDebuff"/>) — e.g. an HP-floor so the debuff isn't re-applied to a mob that dies
        /// before it pays off, or a "the DoTs already finish it" skip. Null (the default) leaves behaviour unchanged.
        /// </summary>
        public static RotationStep MaintainMyDebuff(string spell, int minMsLeft, float priority,
            Func<CombatContext, bool> extraGate = null) =>
            Skill.Spell(spell)
                 .Priority(priority)
                 .On(Targets.CurrentEnemy)
                 .When(ctx => (extraGate == null || extraGate(ctx))
                              && (!ctx.Target.HasMyAura(spell) || ctx.Target.MyAuraTimeLeftMs(spell) < minMsLeft))
                 .RecastDelay(InstantDebuffApplyGraceMs);

        /// <summary>
        /// Maintain a CAST-TIME self-debuff (a DoT like Immolate / Unstable Affliction / Haunt): refresh it when
        /// missing or under <paramref name="minMsLeft"/> remaining, but only while standing still AND not already
        /// casting it. Two guards stop the double-cast: <see cref="IGameClient.IsCurrentSpell"/> blocks a queued
        /// second cast DURING the cast, and the post-cast grace (<see cref="CastDebuffApplyGraceMs"/>) covers the
        /// gap AFTER the cast ends but before the debuff becomes visible (server latency) — without it the bare
        /// missing-aura check re-cast a second Immolate ~2.5s after the first (seen in the logs).
        /// <paramref name="extraGate"/> adds an optional extra condition (e.g. "only when UA isn't known").
        /// </summary>
        public static RotationStep MaintainCastDebuff(string spell, int minMsLeft, float priority,
            Func<CombatContext, bool> extraGate = null) =>
            Skill.Spell(spell)
                 .Priority(priority)
                 .On(Targets.CurrentEnemy)
                 .When(ctx => (extraGate == null || extraGate(ctx))
                              && (!ctx.Target.HasMyAura(spell) || ctx.Target.MyAuraTimeLeftMs(spell) < minMsLeft)
                              && !ctx.Game.PlayerIsMoving
                              && !ctx.Game.IsCurrentSpell(spell))
                 .RecastDelay(CastDebuffApplyGraceMs);

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

        /// <summary>An offensive racial (used in combat with an enemy, off the GCD; IsSpellKnown gates by race).
        /// Used by the shared <see cref="Racials"/> bundle.</summary>
        public static RotationStep OffensiveRacial(string spell, float priority, Func<CombatContext, bool> enabled) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat && ctx.HasEnemyTarget).OffGcd();

        // Minimum recorded hits before a candidate's learned damage is trusted (else it's explored).
        private const int MinDamageSamples = 5;

        /// <summary>
        /// Cast the highest-damage ready strike among interchangeable <paramref name="candidates"/>, using
        /// what the <see cref="DamageTracker"/> has learned. With <paramref name="useLearning"/> off it
        /// falls back to the given (hand-tuned) order, so it is safe before any data exists. With learning
        /// on, candidates with fewer than a handful of recorded hits are explored first (so every option
        /// gets measured), then the best learned average per hit wins. List candidates highest-priority
        /// first so the fallback order matches the intended hand priority.
        /// </summary>
        public static RotationStep BestDamage(float priority, Func<CombatContext, bool> useLearning, params string[] candidates) =>
            new RotationStep(
                name: "Best damage",
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) => Choose(ctx, useLearning, candidates) != null,
                action: (ctx, t) =>
                {
                    string spell = Choose(ctx, useLearning, candidates);
                    return spell != null ? ctx.Game.Cast(spell, t) : CastResult.Failed;
                });

        private static string Choose(CombatContext ctx, Func<CombatContext, bool> useLearning, string[] candidates)
        {
            bool learn = useLearning != null && useLearning(ctx);
            string firstReady = null, best = null;
            double bestDamage = -1;
            foreach (string c in candidates)
            {
                if (!ctx.Game.IsSpellKnown(c) || !ctx.Game.IsSpellReady(c)) continue;
                if (firstReady == null) firstReady = c;
                if (!learn) continue;
                if (ctx.Damage.HitCount(c) < MinDamageSamples) return c; // explore an unmeasured option
                double d = ctx.Damage.AveragePerHit(c);
                if (d > bestDamage) { bestDamage = d; best = c; }
            }
            return learn ? (best ?? firstReady) : firstReady;
        }

        private static bool HasAnyAura(CombatContext ctx, string[] names)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Length; i++)
                if (ctx.Me.HasAura(names[i])) return true;
            return false;
        }
    }
}
