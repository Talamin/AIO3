using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Shared rogue building blocks — the melee / energy / combo-point baseline every rogue spec composes, so the
    /// cross-cutting behaviour (Slice and Dice uptime, the Eviscerate / Rupture finishers, the defensive
    /// cooldowns, the Sprint gap-closer, the stealth opener) is written ONCE and stays consistent. Each returns a
    /// ready RotationStep; the spec just lists them in priority order alongside its signature abilities.
    ///
    /// Finishers spend combo points (read via <see cref="CombatContext.ComboPoints"/>); builders cost energy
    /// (<c>ctx.Me.Energy</c>) and generate them. Known/ready/GCD/range gating is added automatically by the DSL,
    /// so these declare only the interesting condition; an unknown spell auto-skips, so the same list runs from
    /// level 10 up. Assassination (a later phase) reuses SnD / the finishers / the defensives / the stealth
    /// helper unchanged — only its filler differs.
    /// </summary>
    public static class RogueCommon
    {
        /// <summary>Below this range an enemy that is on us counts as "in melee" — the trigger for the surrounded
        /// defensives (Evasion) and the AoE / cooldown gates. One named constant so every melee gate uses the same
        /// radius (mirrors the old AIO's 10y enemy-count radius for these checks; deliberately wider than the
        /// 8y <see cref="Library.Racials.MeleePackRadius"/>, which keeps the old AIO's racial-specific radius).</summary>
        public const float MeleeRange = 10f;

        // --- shared "fight shape" gates (so the cooldowns and the spec read one definition of pack / lone elite) ---

        /// <summary>At least <paramref name="count"/> enemies within <see cref="MeleeRange"/> — the pack trigger for
        /// the AoE / cooldown gates. One source so Blade Flurry, Adrenaline Rush, and Killing Spree agree on what a
        /// "pack" is (and a future Group Combat rotation inherits the same definition).</summary>
        public static bool Pack(CombatContext ctx, int count) => ctx.EnemiesWithin(MeleeRange) >= count;

        /// <summary>A solo fight against an elite/boss — a long, dangerous fight worth a major cooldown even at a
        /// single target. Shared so the cooldowns and Evasion's lone-elite trigger use the same definition.</summary>
        public static bool LoneElite(CombatContext ctx) =>
            !ctx.IsInGroup && ctx.HasEnemyTarget && (ctx.Target.IsElite || ctx.Target.IsBoss());

        /// <summary>The "not while stealthed" opener guard, in one place — out-of-stealth abilities (the builder,
        /// the finishers, the cooldowns) wait until the stealth opener has fired.</summary>
        public static bool NotStealthed(CombatContext ctx) => !ctx.Game.PlayerIsStealthed;

        // --- burst / AoE cooldowns (gated on UseCooldowns; on a pack or a lone elite/boss, mirroring
        // WarriorCommon.Recklessness — written once here so a future Group Combat rotation composes them too) ---

        /// <summary>Adrenaline Rush — an energy-regen burst. Pop it on a pack (>= AdrenalineRushEnemies in melee) or
        /// a lone elite/boss. Self-cast, off the GCD. Auto-skips when unknown / on cooldown.</summary>
        public static RotationStep AdrenalineRush(RogueSettings s, float priority) =>
            Skill.Spell("Adrenaline Rush").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && NotStealthed(ctx)
                              && (Pack(ctx, s.AdrenalineRushEnemies.Value) || LoneElite(ctx)))
                 .OffGcd();

        /// <summary>Killing Spree — a melee-leap burst. Use on a pack (>= KillingSpreeEnemies) or a lone elite/boss,
        /// but not while Adrenaline Rush / Blade Flurry is already up (don't stack the bursts — the old AIO gate).</summary>
        public static RotationStep KillingSpree(RogueSettings s, float priority) =>
            Skill.Spell("Killing Spree").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseCooldowns.Value && NotStealthed(ctx)
                              && !ctx.Me.HasAura("Adrenaline Rush") && !ctx.Me.HasAura("Blade Flurry")
                              && (Pack(ctx, s.KillingSpreeEnemies.Value) || LoneElite(ctx)));

        /// <summary>Don't pop the Blade Flurry cleave cooldown on a half-dead pack — above this target HP% the fight
        /// has enough left for the cleave to pay off. Mirrors the old SoloCombat's `HealthPercent > 70` gate.</summary>
        public const int BladeFlurryMinTargetHealth = 70;

        /// <summary>Blade Flurry — cleave when a pack is in melee (>= BladeFlurryEnemies); its cleave is wasted on a
        /// single target. Also a fresh-fight gate (target HP above <see cref="BladeFlurryMinTargetHealth"/>) so we
        /// don't burn the cooldown on a pack that's about to die. Self-cast, off the GCD. Not while stealthed.</summary>
        public static RotationStep BladeFlurry(RogueSettings s, float priority) =>
            Skill.Spell("Blade Flurry").Priority(priority).On(Targets.Self)
                 .When(ctx => NotStealthed(ctx) && Pack(ctx, s.BladeFlurryEnemies.Value)
                              && ctx.Target.HealthPercent > BladeFlurryMinTargetHealth)
                 .OffGcd();

        /// <summary>A mob this healthy (HP%) is worth refreshing Slice and Dice for; below it the mob dies before the
        /// attack-speed buff pays off, so the combo points go to the finisher instead. Mirrors the old AIO's
        /// `HealthPercent > 50` Slice and Dice gate.</summary>
        public const int SliceAndDiceMinTargetHealth = 50;

        /// <summary>Slice and Dice — the rogue's core attack-speed buff. Refresh it when it's down, but spend only
        /// CHEAP combo points on it: at least <paramref name="minComboPoints"/> (1 is enough) and BELOW the finisher
        /// threshold, so a finisher-worthy bar goes to Eviscerate instead of being wasted on a buff refresh (the
        /// thing Talamin saw — 3 CP burned re-applying SnD). Also skip a dying target (HP below
        /// <see cref="SliceAndDiceMinTargetHealth"/>): it'll be dead before the buff matters, so dump the points into
        /// the finisher. So SnD stays up by refreshing early at 1-2 CP while building, and never eats a full bar.</summary>
        public static RotationStep SliceAndDice(RogueSettings s, int minComboPoints, float priority) =>
            Skill.Spell("Slice and Dice").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Me.HasAura("Slice and Dice")
                              && ctx.ComboPoints >= minComboPoints
                              && ctx.ComboPoints < s.FinisherComboPoints.Value
                              && ctx.Target.HealthPercent > SliceAndDiceMinTargetHealth);

        /// <summary>Eviscerate — the direct-damage finisher. Spend it at the configured combo-point threshold (read
        /// each tick so an overlay edit applies live). Not while stealthed (openers come first).</summary>
        public static RotationStep Eviscerate(RogueSettings s, float priority) =>
            Skill.Spell("Eviscerate").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => NotStealthed(ctx) && ctx.ComboPoints >= s.FinisherComboPoints.Value);

        /// <summary>Refresh Rupture once under this many ms remain — re-applies the bleed before it falls off rather
        /// than letting a gap open. One source so Combat and Assassination agree on the window.</summary>
        public const int RuptureRefreshMs = 3000;

        /// <summary>Don't dump a bleed into a target below this HP% — a fresh Rupture won't tick out before the mob
        /// dies, so the combo points are better spent on a direct-damage finisher (an HP%-floor execute heuristic, no
        /// TTK seam exists yet — same shape as WarlockCommon.DotsWillFinishTarget).</summary>
        public const int RuptureMinTargetHealth = 30;

        /// <summary>A target a bleed can't stick to (Rupture/Garrote are physical bleeds, immune on these types) —
        /// one definition so every bleed step skips the same creatures.</summary>
        public static bool IsBleedImmune(IWowUnit unit) =>
            unit.CreatureType == "Elemental" || unit.CreatureType == "Mechanical";

        /// <summary>Rupture — the bleed finisher (opt-in via UseRupture). Spend it at the finisher CP threshold when
        /// the bleed is missing/expiring and the target is worth a damage-over-time (durable: an elite/boss, since
        /// trash dies before a full bleed pays off). Not on bleed-immune creatures, not while stealthed.</summary>
        public static RotationStep Rupture(RogueSettings s, float priority) =>
            RuptureCore(s, () => s.UseRupture.Value, priority);

        /// <summary>The shared Rupture body — Combat and Assassination differ only in WHICH toggle enables it (Combat
        /// defaults OFF, Assassination ON), so the durable-target / bleed-immune / refresh-window logic is written
        /// once here. <paramref name="enabled"/> reads each spec's toggle live.</summary>
        private static RotationStep RuptureCore(RogueSettings s, System.Func<bool> enabled, float priority) =>
            Skill.Spell("Rupture").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => enabled()
                              && NotStealthed(ctx)
                              && ctx.ComboPoints >= s.FinisherComboPoints.Value
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite)
                              && ctx.Target.HealthPercent > RuptureMinTargetHealth
                              && !IsBleedImmune(ctx.Target)
                              && (!ctx.Target.HasMyAura("Rupture") || ctx.Target.MyAuraTimeLeftMs("Rupture") < RuptureRefreshMs));

        /// <summary>On the low-HP Evasion trigger only, require the target to be above this HP% — don't burn the dodge
        /// cooldown when the mob is about to die anyway. Mirrors the old SoloCombat's `t.HealthPercent > 70` on the
        /// HP-based Evasion (the surrounded / lone-elite triggers carry no such qualifier).</summary>
        public const int EvasionMinTargetHealth = 70;

        /// <summary>Below this health %, Evasion is a PANIC button — fire it regardless of the target's health. The
        /// "don't burn it on a dying mob" qualifier is right at moderate HP (you'll win the race), but when we're this
        /// low we're LOSING the race and need the dodge wall now, even against a near-dead mob (the 1%-HP-vs-26%-Kodo
        /// near-death that prompted this).</summary>
        public const int EvasionPanicHealthPercent = 25;

        /// <summary>Evasion — the dodge defensive. Fire when low on health (AND the target is still healthy so we don't
        /// burn it on a dying mob — UNLESS we're below <see cref="EvasionPanicHealthPercent"/>, where survival wins and
        /// it fires regardless), when surrounded (>= the configured attacker count meleeing us), or on a solo elite/boss
        /// fight. Self-cast, off the GCD. Mirrors the old SoloCombat's three Evasion triggers (HP, enemy count, lone
        /// elite) plus the panic override.</summary>
        public static RotationStep Evasion(RogueSettings s, float priority) =>
            Skill.Spell("Evasion").Priority(priority).On(Targets.Self)
                 .When(ctx => (s.EvasionHealthPercent.Value > 0 && ctx.Me.HealthPercent < s.EvasionHealthPercent.Value
                               && ctx.HasEnemyTarget
                               && (ctx.Target.HealthPercent > EvasionMinTargetHealth
                                   || ctx.Me.HealthPercent < EvasionPanicHealthPercent))
                              || ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= MeleeRange) >= s.EvasionEnemies.Value
                              || LoneElite(ctx))
                 .OffGcd();

        /// <summary>Cloak of Shadows — wipe magic effects + spell resistance. Fire when we carry a Magic debuff
        /// (auto-skips when Cloak is unknown). Self-cast, off the GCD.</summary>
        public static RotationStep CloakOfShadows(RogueSettings s, float priority) =>
            Skill.Spell("Cloak of Shadows").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCloakOfShadows.Value && ctx.Game.PlayerHasDebuffType("Magic"))
                 .OffGcd();

        /// <summary>Sprint — close the gap to a target out of melee range during a committed fight (the host only
        /// runs the rotation while the product is fighting, so this never fires during navigation). Off the GCD;
        /// throttled so it isn't re-issued every tick while the speed buff carries us in.</summary>
        public static RotationStep Sprint(RogueSettings s, float priority) =>
            Skill.Spell("Sprint").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseSprint.Value && ctx.HasEnemyTarget
                              && ctx.Target.Distance > MeleeRange && !ctx.Me.HasAura("Sprint"))
                 .OffGcd()
                 .RecastDelay(2000);

        /// <summary>Stealth — open from stealth out of combat (opt-in). Cast "Stealth" when not already stealthed
        /// and not in combat, so the spec's opener (Ambush / Garrote) can fire. A normal cast — no special
        /// handling. Self-cast. Gated tightly so it never fights a product that owns the pull.</summary>
        public static RotationStep Stealth(RogueSettings s, float priority) =>
            Skill.Spell("Stealth").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseStealth.Value
                              && ctx.Game.ProductIsFighting       // only when the product commits to a fight (the
                                                                  // approach) — NOT idle/travelling, or we'd run
                                                                  // around permanently stealthed between pulls
                              && !ctx.Game.PlayerInCombat
                              && !ctx.Game.PlayerIsStealthed
                              && !ctx.Game.PlayerIsMounted
                              && !ctx.Game.PlayerIsResting        // don't break off eating/drinking to recover
                              && !ctx.Game.PlayerHasHarmfulAura()); // a DoT tick would instantly break stealth

        // --- Assassination filler (parameterised here so a future Group Assassination composes them too; the spec
        // just lists them in priority order). Mutilate / Envenom / Hunger for Blood / Cold Blood are the
        // Assassination-tree abilities; Combat never lists them, so they live here but cost nothing if unused. ---

        /// <summary>True when one of our bleeds is ticking on the target — the precondition Hunger for Blood needs and
        /// the reason Assassination keeps Rupture up. Checks the bleeds an Assassination rogue actually applies
        /// (Rupture, Garrote); read as "my bleed", so another player's DoT doesn't satisfy it.</summary>
        public static bool MyBleedUp(CombatContext ctx) =>
            ctx.HasEnemyTarget && (ctx.Target.HasMyAura("Rupture") || ctx.Target.HasMyAura("Garrote"));

        /// <summary>Sinister Strike — the baseline combo-point builder, shared by every spec (Combat's main builder,
        /// Assassination's dagger-less / pre-Mutilate fallback). Lowest-priority filler: it builds every GCD nothing
        /// better wants — but only BELOW the finisher threshold (like Mutilate), so it doesn't keep burning energy
        /// overbuilding past the point a finisher fires. Not while stealthed (the opener goes first). Energy/known
        /// gating is automatic.</summary>
        public static RotationStep SinisterStrike(RogueSettings s, float priority) =>
            Skill.Spell("Sinister Strike").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => NotStealthed(ctx) && ctx.ComboPoints < s.FinisherComboPoints.Value);

        /// <summary>Mutilate — the Assassination builder (a dual-dagger strike that grants 2 combo points). Requires
        /// daggers equipped, which "known" can't tell us, so a cast can fail without them; the engine then falls
        /// through to the Sinister Strike fallback the spec lists below it. Build until we are at the finisher
        /// threshold (no point overcapping combo points). Not while stealthed (the opener goes first).</summary>
        public static RotationStep Mutilate(RogueSettings s, float priority) =>
            Skill.Spell("Mutilate").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => NotStealthed(ctx) && ctx.ComboPoints < s.FinisherComboPoints.Value);

        /// <summary>Fan of Knives — Assassination's instant melee AoE (no combo points, hits everything around us).
        /// Cleave when a pack is in melee (>= FanOfKnivesEnemies); wasted on a single target. Combat cleaves with
        /// Blade Flurry instead, so this is the Assassination pack tool. Not while stealthed (the opener goes first);
        /// auto-skips until learned (IsSpellKnown).</summary>
        public static RotationStep FanOfKnives(RogueSettings s, float priority) =>
            Skill.Spell("Fan of Knives").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => Pack(ctx, s.FanOfKnivesEnemies.Value) && NotStealthed(ctx));

        /// <summary>Rupture for Assassination — the bleed the spec leans on (it enables Hunger for Blood and is core
        /// to the tree's damage), so it has its own toggle that defaults ON (Combat's <see cref="Rupture"/> defaults
        /// OFF). Same durable-target / bleed-immune / refresh logic as Combat's Rupture (shared via RuptureCore) —
        /// only the enabling toggle differs.</summary>
        public static RotationStep AssassinationRupture(RogueSettings s, float priority) =>
            RuptureCore(s, () => s.AssassinationUseRupture.Value, priority);

        /// <summary>Hunger for Blood — the maintainable damage buff that REQUIRES a bleed on the target. Refresh it
        /// when it's down and one of our bleeds is up (the old GroupAssassination gate, minus the wManager talent
        /// check — IsSpellKnown auto-skips when the talent isn't taken). Opt-in via UseHungerForBlood. Not while
        /// stealthed.</summary>
        public static RotationStep HungerForBlood(RogueSettings s, float priority) =>
            Skill.Spell("Hunger For Blood").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseHungerForBlood.Value && NotStealthed(ctx)
                              && !ctx.Me.HasAura("Hunger For Blood") && MyBleedUp(ctx));

        /// <summary>Cold Blood — the next finisher/ability crits. A cooldown: pair it with a finisher (only worth it
        /// when we have the combo points to spend immediately) and gate it like the other cooldowns (UseCooldowns,
        /// on a pack or a lone elite/boss), mirroring how Combat gates Adrenaline Rush. Off the GCD so it doesn't
        /// eat the finisher's GCD. Auto-skips when the talent is untaken / on cooldown. Not while stealthed.
        /// Note: it intentionally borrows <c>AdrenalineRushEnemies</c> as the shared "pack size" threshold (Cold
        /// Blood has no separate enemy-count knob by design), so the cooldowns share one pack setting.</summary>
        public static RotationStep ColdBlood(RogueSettings s, float priority) =>
            Skill.Spell("Cold Blood").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && s.UseColdBlood.Value && NotStealthed(ctx)
                              && ctx.ComboPoints >= s.FinisherComboPoints.Value
                              && (Pack(ctx, s.AdrenalineRushEnemies.Value) || LoneElite(ctx)))
                 .OffGcd();

        /// <summary>Envenom — the Assassination finisher (consumes Deadly Poison stacks for a burst of nature
        /// damage). Spend it at the finisher CP threshold. Poisons are deferred, so this is gated by the spec's
        /// finisher choice (Envenom unless the player picked Eviscerate); when chosen it is preferred over the
        /// shared Eviscerate. Not while stealthed.</summary>
        public static RotationStep Envenom(RogueSettings s, float priority) =>
            Skill.Spell("Envenom").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseEnvenomFinisher && NotStealthed(ctx)
                              && ctx.ComboPoints >= s.FinisherComboPoints.Value);

        /// <summary>Stealth opener — the first strike of a stealth-opened fight, the spell chosen by the
        /// <see cref="RogueSettings.StealthOpener"/> dropdown: Cheap Shot (positional-free, 4s stun + 2 combo
        /// points) or Garrote (bleed + silence, but must be cast from BEHIND the target). Fires only while
        /// stealthed and in melee range (the engine's range gate), so it breaks stealth to start the fight and
        /// the normal build-and-finish loop takes over. Sits above auto-attack so it lands before the swing
        /// breaks stealth. The RecastDelay is a safety net: if the chosen opener can't land (e.g. Garrote when
        /// not behind, or out of energy) it isn't re-issued every tick — auto-attack then opens instead.
        /// Unknown/unusable spell auto-skips (IsSpellKnown), so a low-level rogue just opens with auto-attack.</summary>
        public static RotationStep Opener(RogueSettings s, string spell, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseStealth.Value
                              && ctx.Game.PlayerIsStealthed
                              && OpenerSelected(s, spell, ctx))
                 .RecastDelay(2000);

        /// <summary>Which opener wins for <paramref name="spell"/> under the StealthOpener setting. An explicit
        /// "Cheap Shot"/"Garrote" forces that spell; "Auto" lets the FC pick by position — Garrote when we're
        /// behind the target (where it can land), Cheap Shot from the front (positional-free). The behind check is
        /// the shared, class-agnostic ctx.Game.PlayerIsBehindTarget() seam (the feral druid will reuse it for
        /// Shred vs Mangle).</summary>
        private static bool OpenerSelected(RogueSettings s, string spell, CombatContext ctx)
        {
            string mode = s.StealthOpener.Value;
            if (mode != "Auto") return mode == spell;
            return ctx.Game.PlayerIsBehindTarget() ? spell == "Garrote" : spell == "Cheap Shot";
        }

        // --- Weapon poisons (out-of-combat upkeep) ---

        /// <summary>Don't re-issue the poison apply for this long after one fires — the apply is near-instant, but
        /// this covers the use→enchant-registers gap so the step doesn't spam it (mirrors the old AIO ApplyPoison's
        /// 5000ms step timer).</summary>
        public const int PoisonApplyThrottleMs = 5000;

        /// <summary>Out-of-combat weapon-poison upkeep: keep Instant Poison on the MAIN hand and Deadly Poison (else
        /// Instant) on the OFF hand, reapplying when a hand's poison drops under the configured minutes — or is
        /// missing (a fresh/expired hand reads 0 ms and reapplies). Picks the highest poison rank the rogue's level
        /// allows AND carries; if none is carried the step falls through (no spin). OOC only — applying briefly stops
        /// the character, so it never interrupts a live fight — and not mounted. Opt-out via UsePoisons. Mirrors the
        /// old AIO ApplyPoison addon (minus its mis-ordered Deadly table; see <see cref="RoguePoisons"/>).</summary>
        public static RotationStep MaintainPoisons(RogueSettings s, float priority) =>
            new RotationStep(
                name: "Apply poisons",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => s.UsePoisons.Value
                                       && !ctx.Game.PlayerInCombat
                                       && !ctx.Game.PlayerIsMounted
                                       && ChoosePoison(ctx, s).HasValue,
                action: (ctx, t) =>
                {
                    PoisonChoice? choice = ChoosePoison(ctx, s);
                    if (choice == null) return CastResult.Failed;
                    ctx.Game.ApplyPoisonToWeapon(choice.Value.PoisonId, choice.Value.MainHand);
                    return CastResult.Success;
                },
                ignoreGcd: true,
                recastDelayMs: PoisonApplyThrottleMs);

        /// <summary>A pending poison application: which poison id goes on which hand.</summary>
        private readonly struct PoisonChoice
        {
            public readonly uint PoisonId;
            public readonly bool MainHand;
            public PoisonChoice(uint poisonId, bool mainHand) { PoisonId = poisonId; MainHand = mainHand; }
        }

        /// <summary>Which poison to apply to which hand right now, or null if nothing needs it. Reads the weapon
        /// enchant state once plus the highest usable Instant/Deadly ranks (by level + carried), then applies the
        /// strategy: Instant on the main hand; Deadly (else Instant) on the off hand — each only when that hand is
        /// equipped and its poison is under the refresh window. Main hand is checked first, so a fight-approach tick
        /// tops up at most one hand per <see cref="PoisonApplyThrottleMs"/>; the next eligible tick does the other.</summary>
        private static PoisonChoice? ChoosePoison(CombatContext ctx, RogueSettings s)
        {
            WeaponEnchant we = ctx.Game.GetWeaponEnchant();
            int thresholdMs = s.PoisonRefreshMinutes.Value * 60000;
            int level = ctx.Me.Level;

            uint instant = RoguePoisons.BestUsableInstant(level, ctx.Game.HasItemById);
            if (we.MainHandEquipped && we.MainHandRemainingMs < thresholdMs && instant != 0)
                return new PoisonChoice(instant, mainHand: true);

            if (we.OffHandEquipped && we.OffHandRemainingMs < thresholdMs)
            {
                uint deadly = RoguePoisons.BestUsableDeadly(level, ctx.Game.HasItemById);
                if (deadly != 0) return new PoisonChoice(deadly, mainHand: false);
                if (instant != 0) return new PoisonChoice(instant, mainHand: false);
            }
            return null;
        }
    }
}
