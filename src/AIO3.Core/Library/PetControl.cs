using System;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Library
{
    /// <summary>
    /// Layer 3 — shared pet-management blocks for every pet class (Hunter first; Warlock / Death Knight
    /// reuse them). Every block is gated on the pet actually EXISTING in the world (<c>ctx.Pet</c>), never
    /// on level — so a petless owner (untamed / dismissed / below the taming level / an unusual server)
    /// simply skips them and plays petless.
    ///
    /// Pets auto-cast their own special abilities, so this controller v1 only keeps the pet
    /// summoned / alive / healed and pointed at the right target. Each block also takes an
    /// <paramref name="enabled"/> predicate so the owner can switch the whole thing off when a WRobot
    /// product manages the pet (product coexistence). A fuller "pet as its own rotation with pet-bar
    /// cooldowns and target selection" is a later refinement.
    /// </summary>
    public static class PetControl
    {
        // Grace after a pet we had suddenly vanishes before we re-summon it — long enough to outlast a
        // mount-up cast (mounting dismisses the pet, and casting Call Pet into it cancels the mount).
        private const int SummonGraceMs = 2000;

        /// <summary>Keep a pet up: summon it with <paramref name="callSpell"/> when none exists, or revive it
        /// with <paramref name="reviveSpell"/> when it is dead. Out of combat / not mounted. If a pet we
        /// already had suddenly disappears (almost always because we just mounted), wait out a short grace
        /// before re-summoning — otherwise we'd cast Call Pet into the mount-up and dismount ourselves on a
        /// loop. A pet we never had (fresh login) is summoned immediately.</summary>
        public static RotationStep Summon(Func<CombatContext, bool> enabled, string callSpell, string reviveSpell, float priority)
        {
            bool petSeen = false;
            int vanishedAt = 0;
            return new RotationStep(
                name: "Pet summon",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (ctx.Pet != null)
                    {
                        petSeen = true;
                        vanishedAt = 0;
                        if (ctx.Pet.IsAlive) return false; // alive → nothing to do
                        // dead → fall through to revive (no grace)
                    }
                    else if (petSeen)
                    {
                        if (vanishedAt == 0) vanishedAt = Environment.TickCount;
                        if (unchecked(Environment.TickCount - vanishedAt) < SummonGraceMs) return false;
                    }

                    if (!enabled(ctx) || ctx.Game.PlayerInCombat || ctx.Game.PlayerIsMounted) return false;
                    string spell = SummonSpell(ctx, callSpell, reviveSpell);
                    return spell != null && ctx.Game.IsSpellKnown(spell) && ctx.Game.IsSpellReady(spell);
                },
                action: (ctx, t) =>
                {
                    string spell = SummonSpell(ctx, callSpell, reviveSpell);
                    return spell != null ? ctx.Game.Cast(spell, ctx.Me) : CastResult.Failed;
                });
        }

        /// <summary>Heal the pet with <paramref name="mendSpell"/> when it is alive and below the
        /// <paramref name="belowPercent"/> threshold (and not already being mended). The threshold is read
        /// each tick (a Func, not a captured value) so an overlay slider edit takes effect live.</summary>
        public static RotationStep Heal(Func<CombatContext, bool> enabled, string mendSpell, Func<CombatContext, int> belowPercent, float priority) =>
            Skill.Spell(mendSpell).Priority(priority).On(Targets.Pet)
                 .When((ctx, t) => enabled(ctx) && t.IsAlive
                                   && t.HealthPercent < belowPercent(ctx)
                                   && !t.HasAura(mendSpell));

        /// <summary>Pull aggro back to the pet with its taunt (e.g. "Growl" for a hunter pet, "Torment" for
        /// a Voidwalker) when something is attacking the owner. Auto-managed: if the pet doesn't have the
        /// named taunt on its bar (an Imp has none), <c>PetHasAbility</c> is false and the step never fires —
        /// so the same call is safe for every pet. The pet taunts whatever it is currently on, so pair this
        /// with <see cref="Attack"/> which keeps it on the owner's target. Throttled to the taunt's reuse.</summary>
        public static RotationStep Taunt(Func<CombatContext, bool> enabled, string tauntSpell, float priority) =>
            new RotationStep(
                name: "Pet taunt",
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) => enabled(ctx) && ctx.Pet != null && ctx.Pet.IsAlive
                                       && ctx.EnemiesTargetingMe >= 1
                                       && ctx.Game.PetHasAbility(tauntSpell),
                action: (ctx, t) => ctx.Game.CastPetAbility(tauntSpell) ? CastResult.Success : CastResult.Failed,
                ignoreGcd: true,
                // Re-taunt whenever a mob is back on the owner. The real limiter is the taunt's own cooldown
                // (CastPetAbility checks it), so this throttle just matches it (~5s for Growl) to keep re-taunts
                // coming without scanning the pet bar every tick while the ability is recovering.
                recastDelayMs: TauntReuseMs);

        /// <summary>Keep the pet on the right target: prefer a mob attacking the owner (peel it onto the
        /// pet), then a mob attacking the pet (keep holding it — no thrashing back to the main target),
        /// then the owner's current target. Re-issues only on a target change (throttled).</summary>
        public static RotationStep Attack(Func<CombatContext, bool> enabled, float priority) =>
            new RotationStep(
                name: "Pet attack",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (!enabled(ctx) || ctx.Pet == null || !ctx.Pet.IsAlive) return false;
                    IWowUnit want = BestPetTarget(ctx);
                    return want != null && want.Guid != ctx.Pet.TargetGuid;
                },
                action: (ctx, t) =>
                {
                    IWowUnit want = BestPetTarget(ctx);
                    if (want == null) return CastResult.Failed;
                    ctx.Game.PetAttack(want);
                    return CastResult.Success;
                },
                ignoreGcd: true,
                recastDelayMs: 500);

        private const int TauntReuseMs = 5000;

        /// <summary>Cast a named pet ability (e.g. "Bite", "Furious Howl", "Dash") when it's on the bar and
        /// off cooldown — auto-skipping any pet that doesn't have it. <paramref name="when"/> adds the
        /// situational gate (in combat, has focus, …). The throttle limits cast-spam for no-cooldown
        /// abilities (e.g. the focus-dump Bite); cooldown abilities are already gated by readiness.</summary>
        public static RotationStep UseAbility(Func<CombatContext, bool> enabled, string ability, float priority,
            Func<CombatContext, bool> when = null, int recastDelayMs = 500) =>
            new RotationStep(
                name: "Pet " + ability,
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => enabled(ctx) && ctx.Pet != null && ctx.Pet.IsAlive
                                       && (when == null || when(ctx))
                                       && ctx.Game.PetAbilityReady(ability),
                action: (ctx, t) => ctx.Game.CastPetAbility(ability) ? CastResult.Success : CastResult.Failed,
                ignoreGcd: true,
                recastDelayMs: recastDelayMs);

        /// <summary>The unit the pet should be on: lowest-HP mob attacking the owner (peel), else lowest-HP
        /// mob attacking the pet (hold), else the owner's current target.</summary>
        private static IWowUnit BestPetTarget(CombatContext ctx)
        {
            IWowUnit onOwner = LowestHp(ctx, e => e.IsTargetingMe);
            if (onOwner != null) return onOwner;
            IWowUnit onPet = LowestHp(ctx, e => e.IsTargetingMyPet);
            if (onPet != null) return onPet;
            IWowUnit main = ctx.Target;
            return (main != null && main.IsAlive && main.IsAttackable) ? main : null;
        }

        private static IWowUnit LowestHp(CombatContext ctx, Func<IWowUnit, bool> pick)
        {
            IWowUnit best = null;
            double bestHp = double.MaxValue;
            foreach (IWowUnit e in ctx.Enemies)
                if (e.IsAlive && e.IsAttackable && pick(e) && e.HealthPercent < bestHp)
                {
                    best = e;
                    bestHp = e.HealthPercent;
                }
            return best;
        }

        // null = nothing to do (the pet exists and is alive).
        private static string SummonSpell(CombatContext ctx, string callSpell, string reviveSpell) =>
            ctx.Pet == null ? callSpell : (!ctx.Pet.IsAlive ? reviveSpell : null);
    }
}
