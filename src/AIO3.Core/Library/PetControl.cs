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

        // After we ISSUE a summon, wait at most this long for the pet to actually appear before allowing another
        // cast. The summon is a multi-second cast (≈10s on some servers) and the pet only spawns a beat AFTER the
        // cast ends — so we can't size a fixed throttle to the cast time. Instead we hold off until the pet is up
        // (cleared the moment it appears) and only fall through after this generous timeout, i.e. when the summon
        // genuinely failed/was interrupted. This is what kills the double-cast across any cast length.
        private const int SummonWaitMs = 15000;

        // How long to PIN the character (cancel the product's travel re-pathing) once we start a summon, so the
        // long ~10s cast finishes. A single StopMove isn't enough — the product re-issues a move on its next pulse
        // and breaks the cast. Generous enough to cover the cast on slow servers; auto-expires after.
        private const int SummonHoldMs = 12000;

        /// <summary>Keep a pet up: summon it with <paramref name="callSpell"/> when none exists, or revive it
        /// with <paramref name="reviveSpell"/> when it is dead. Out of combat / not mounted. If a pet we
        /// already had suddenly disappears (almost always because we just mounted), wait out a short grace
        /// before re-summoning — otherwise we'd cast Call Pet into the mount-up and dismount ourselves on a
        /// loop. A pet we never had (fresh login) is summoned immediately.</summary>
        public static RotationStep Summon(Func<CombatContext, bool> enabled, string callSpell, string reviveSpell, float priority) =>
            Summon(enabled, ctx => callSpell, ctx => reviveSpell, priority);

        /// <summary>Same as <see cref="Summon(Func{CombatContext,bool},string,string,float)"/> but resolves the
        /// call / revive spell names at EVAL TIME — so a runtime pet choice (e.g. the warlock's per-spec "Auto"
        /// demon with a known-spell fallback) picks the right summon each tick.
        ///
        /// <paramref name="desiredPetName"/> (optional) enables SWAP-BACK: when a healthy pet is up but it is not
        /// the demon this returns (e.g. the free Imp we fell back to while out of Soul Shards), re-summon the
        /// desired one once it's affordable again. The warlock passes its resolved demon (which already accounts
        /// for shards, so "desired" == current while broke → no swap); a hunter leaves it null → never swaps.</summary>
        public static RotationStep Summon(Func<CombatContext, bool> enabled, Func<CombatContext, string> callSpell,
            Func<CombatContext, string> reviveSpell, float priority, Func<CombatContext, string> desiredPetName = null)
        {
            bool petSeen = false;
            int vanishedAt = 0;
            int summonedAt = 0; // when we last issued a summon/revive; 0 = not waiting on one
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
                        if (ctx.Pet.IsAlive)
                        {
                            // Healthy pet → done, UNLESS it's the wrong demon and the desired one is now affordable
                            // (swap-back). When swapping we deliberately do NOT clear summonedAt, so the wait below
                            // still throttles the re-cast across the swap's cast → spawn gap.
                            if (desiredPetName == null || WantedPetIsUp(ctx, desiredPetName)) { summonedAt = 0; return false; }
                        }
                        // exists but dead → fall through to revive
                    }
                    else if (petSeen)
                    {
                        if (vanishedAt == 0) vanishedAt = Environment.TickCount;
                        if (unchecked(Environment.TickCount - vanishedAt) < SummonGraceMs) return false;
                    }

                    // A summon/revive we already cast hasn't taken effect yet — the cast is multi-second and the
                    // pet spawns a beat after it ends. Don't cast again until the pet is up (cleared above) or this
                    // generous timeout passes (the summon failed / was interrupted), which is what stops the
                    // double-cast regardless of how long the summon cast actually takes. The PlayerIsCasting gate
                    // below ALSO blocks re-entry while the cast runs.
                    if (summonedAt != 0 && unchecked(Environment.TickCount - summonedAt) < SummonWaitMs) return false;
                    summonedAt = 0;

                    if (!enabled(ctx) || ctx.Game.PlayerInCombat || ctx.Game.PlayerIsMounted
                        || ctx.Game.PlayerIsCasting) return false; // never start a summon on top of a running cast
                    string spell = SummonSpell(ctx, callSpell, reviveSpell);
                    return spell != null && ctx.Game.IsSpellKnown(spell) && ctx.Game.IsSpellReady(spell);
                },
                action: (ctx, t) =>
                {
                    // Pin the character for the whole cast: the adapter refuses a cast-time spell while moving, and
                    // the product's travel movement would otherwise re-path and break the ~10s summon. HoldPosition
                    // stops current motion AND cancels the product's move pulses for SummonHoldMs (the single
                    // StopMove the old code used was not enough — the product just re-issued a move). The condition's
                    // PlayerIsCasting gate keeps this action from re-running mid-cast, so the pin is set once.
                    ctx.Game.HoldPosition(SummonHoldMs);

                    string spell = SummonSpell(ctx, callSpell, reviveSpell);
                    CastResult r = spell != null ? ctx.Game.Cast(spell, ctx.Me) : CastResult.Failed;
                    if (r == CastResult.Success) summonedAt = Environment.TickCount; // start the wait-for-pet window
                    return r;
                });
        }

        /// <summary>True if we should KEEP the current pet: the desired summon resolves to no opinion, or to the
        /// demon we already have. Used by the swap-back. The "desired" already accounts for affordability (the
        /// warlock's resolver drops to the free Imp while out of shards), so a mismatch means a real, payable swap.</summary>
        private static bool WantedPetIsUp(CombatContext ctx, Func<CombatContext, string> desiredPetName)
        {
            string want = desiredPetName(ctx);
            return string.IsNullOrEmpty(want)
                   || (ctx.Pet != null && string.Equals(ctx.Pet.Name, want, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Heal the pet with <paramref name="mendSpell"/> when it is alive and below the
        /// <paramref name="belowPercent"/> threshold (and not already being mended). The threshold is read
        /// each tick (a Func, not a captured value) so an overlay slider edit takes effect live.</summary>
        public static RotationStep Heal(Func<CombatContext, bool> enabled, string mendSpell, Func<CombatContext, int> belowPercent, float priority) =>
            Skill.Spell(mendSpell).Priority(priority).On(Targets.Pet)
                 .When((ctx, t) => enabled(ctx) && t.IsAlive
                                   && t.HealthPercent < belowPercent(ctx)
                                   && !t.HasAura(mendSpell));

        /// <summary>Keep the current target on the pet with its taunt (e.g. "Growl" for a hunter pet, "Torment"
        /// for a Voidwalker). PROACTIVE: taunts as soon as the engaged target is NOT already on the pet — it's on
        /// the owner or still approaching — instead of waiting for the mob to reach the squishy owner first, so
        /// the pet snaps it onto itself early (pair with <see cref="Attack"/>, prio just above this, which sends
        /// the pet to the target so the taunt lands from melee — the taunt's range is measured from the PET).
        /// Idles once the mob IS on the pet (it's being tanked). Gated on <c>PetAbilityReady</c> (on the bar AND
        /// off cooldown — a cached read), so an Imp without the taunt skips for free AND a short re-taunt throttle
        /// can't spam Lua while the ability recovers; it simply re-taunts the moment the taunt is ready again.</summary>
        public static RotationStep Taunt(Func<CombatContext, bool> enabled, string tauntSpell, float priority) =>
            new RotationStep(
                name: "Pet taunt",
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) => enabled(ctx) && ctx.Pet != null && ctx.Pet.IsAlive
                                       && ctx.HasEnemyTarget && ctx.Target.TargetGuid != ctx.Pet.Guid
                                       && ctx.Game.PetAbilityReady(tauntSpell),
                action: (ctx, t) => ctx.Game.CastPetAbility(tauntSpell) ? CastResult.Success : CastResult.Failed,
                ignoreGcd: true,
                // Re-taunt quickly while the mob isn't on the pet. PetAbilityReady is the real cooldown gate, so
                // this short throttle just covers the cast → cooldown-register gap without re-issuing every tick.
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

        // Short re-taunt throttle: the real limiter is PetAbilityReady (the taunt's actual cooldown, cached), so
        // this only covers the brief cast → cooldown-register gap. Kept low so the pet snaps the mob back quickly
        // when it slips to the owner (the old AIO re-taunted every ~500ms in combat).
        private const int TauntReuseMs = 1500;

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

        // How often we re-assert a pet ability's autocast state, so it stays correct even if it gets turned off
        // externally (a manual right-click, a UI reset). SetPetAutocast is a no-op when already correct, so these
        // re-checks are cheap; the interval keeps the Lua scans rare.
        private const int AutocastRecheckMs = 10000;

        /// <summary>Keep a pet ability on AUTOCAST (or off) to match <paramref name="enabled"/> — the right model
        /// for a cast-time, no-cooldown nuke like the Imp's Firebolt: the pet fires it itself, so we don't fight its
        /// cast time by re-triggering every tick. Sets it immediately on a new pet or a toggle change, and
        /// RE-ASSERTS it every <see cref="AutocastRecheckMs"/> so it stays on even if something turned it off
        /// externally. Auto-skips a pet that doesn't have the ability. Off the GCD.</summary>
        public static RotationStep Autocast(Func<CombatContext, bool> enabled, string ability, float priority)
        {
            ulong syncedFor = 0;     // pet guid we last synced
            bool syncedState = false;
            int nextRecheck = 0;     // Environment.TickCount at which we re-assert even if nothing changed
            return new RotationStep(
                name: "Pet autocast " + ability,
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => ctx.Pet != null && ctx.Pet.IsAlive && ctx.Game.PetHasAbility(ability)
                                       && (ctx.Pet.Guid != syncedFor                       // new pet
                                           || syncedState != enabled(ctx)                  // toggle changed
                                           || unchecked(Environment.TickCount - nextRecheck) >= 0), // periodic re-assert
                action: (ctx, t) =>
                {
                    bool on = enabled(ctx);
                    ctx.Game.SetPetAutocast(ability, on);
                    syncedFor = ctx.Pet.Guid;
                    syncedState = on;
                    nextRecheck = Environment.TickCount + AutocastRecheckMs;
                    // Return Failed so this background maintenance NEVER consumes a rotation tick — the engine
                    // falls through and casts the real spell the same tick. (The throttle lives in the condition
                    // via nextRecheck, so it still only re-asserts once per interval despite the Failed result.)
                    return CastResult.Failed;
                },
                ignoreGcd: true);
        }

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

        // A DEAD pet is revived; otherwise (no pet, OR a live pet we only reach here to SWAP — the not-swapping
        // live-pet case already returned false in the condition) we cast the call/summon spell.
        private static string SummonSpell(CombatContext ctx, Func<CombatContext, string> callSpell, Func<CombatContext, string> reviveSpell) =>
            ctx.Pet != null && !ctx.Pet.IsAlive ? reviveSpell(ctx) : callSpell(ctx);
    }
}
