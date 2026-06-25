using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;

namespace AIO3.Core.Rotations.Mage
{
    /// <summary>
    /// Shared mage building blocks — the "caster baseline" every spec composes: armor + Arcane Intellect
    /// upkeep, the interrupt, mana management (Evocation / mana gem / wand), and survival/kiting (Ice Block,
    /// Mana Shield, Ice Barrier, Frost Nova + cliff-safe step back, Blink, Polymorph). Each returns a ready
    /// RotationStep; the spec lists them in priority order alongside its signature nukes. Spell names resolve
    /// at eval time where a choice exists, so overlay edits apply live.
    ///
    /// Cast-time nukes (Frostbolt, Fireball, Arcane Blast …) gate on <c>!PlayerIsMoving</c> in the specs — a
    /// caster must stand still — while instants (procs, Ice Lance, Fire Blast, Arcane Barrage) do not.
    /// </summary>
    public static class MageCommon
    {
        /// <summary>Below this the target is "in melee" — used for the Blink escape and the Evocation interrupt
        /// guard (a mob this close beats a channel off).</summary>
        public const float MeleeRange = 8f;

        /// <summary>Frost Nova's PBAoE root radius. It roots everything within ~10yd of us, so we don't wait for a
        /// mob to reach true melee — we root it as it crosses this radius (more kite distance, no wasted nova).</summary>
        public const float FrostNovaRadius = 10f;

        /// <summary>Radius that defines a "pack" for AoE / cooldown gates.</summary>
        public const float AoeRadius = 10f;

        /// <summary>"Several enemies" — at this many in the pack a major cooldown is worth pressing.</summary>
        public const int PackSize = 2;

        /// <summary>Instant-execute finisher threshold (Fire Blast etc.).</summary>
        public const int ExecutePercent = 10;

        /// <summary>Don't re-apply Living Bomb (a 12s DoT that detonates at the end) to a mob already in execute range
        /// — it dies before the DoT pays off. Mirrors the execute floor (reuses <see cref="ExecutePercent"/>) so the
        /// DoT and the Fire Blast execute share one source. HP-floor heuristic (no time-to-die seam exists).</summary>
        public const int LivingBombMinTargetHealth = ExecutePercent;

        /// <summary>The shields (Ice Barrier / Mana Shield) don't re-cast on a lone target this low (HP%): a mob about
        /// to die isn't worth a fresh shield's mana/GCD. Relaxed when more than one enemy is in the fight (the others
        /// keep the shield earning), so only the "last mob is dying" case is skipped. HP-floor heuristic.</summary>
        public const int ShieldMinTargetHealth = 20;

        /// <summary>True when a shield (Ice Barrier / Mana Shield) is still worth (re-)casting: the lone current target
        /// is above <see cref="ShieldMinTargetHealth"/>, OR there is more than one enemy (a pack keeps it earning even
        /// if the current target is dying). Stops the shield refreshing as the last mob of a fight dies.</summary>
        public static bool ShieldWorthwhile(CombatContext ctx) =>
            ctx.EnemyCount > 1 || (ctx.HasEnemyTarget && ctx.Target.HealthPercent > ShieldMinTargetHealth);

        /// <summary>A fight worth spending a major cooldown on: a boss, an elite, or a pack. Includes the
        /// HasEnemyTarget check, so a Self-cast cooldown step gated on this never dereferences a null target
        /// (the NRE that bit the hunter's Bestial Wrath).</summary>
        public static bool IsBigFight(CombatContext ctx) =>
            ctx.HasEnemyTarget && (ctx.Target.IsBoss() || ctx.Target.IsElite || ctx.EnemiesWithin(AoeRadius) >= PackSize);

        /// <summary>A self-cast major cooldown (Icy Veins / Arcane Power / Mirror Image / Presence of Mind),
        /// fired on a boss/elite/pack when cooldowns are enabled. One place for the gate the specs share.</summary>
        public static RotationStep MajorCooldown(MageSettings s, string spell, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && IsBigFight(ctx));

        // --- buffs ---

        /// <summary>Keep the right armor up. The chosen armor (or a spec-appropriate Auto pick) is resolved at
        /// eval time and cast when missing.</summary>
        public static RotationStep Armor(MageSettings s, MageSpec spec, float priority) =>
            new RotationStep(
                name: "Armor",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    string armor = ResolveArmor(ctx, s, spec);
                    return armor != null && !ctx.Me.HasAura(armor);
                },
                action: (ctx, t) =>
                {
                    string armor = ResolveArmor(ctx, s, spec);
                    return armor != null ? ctx.Game.Cast(armor, ctx.Me) : CastResult.Failed;
                });

        /// <summary>Keep Arcane Intellect up (skipped when Arcane Brilliance is already on, e.g. from a group).</summary>
        public static RotationStep ArcaneIntellect(MageSettings s, float priority) =>
            Skill.Spell("Arcane Intellect").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseArcaneIntellect.Value
                              && !ctx.Me.HasAura("Arcane Intellect")
                              && !ctx.Me.HasAura("Arcane Brilliance"));

        // --- interrupt ---

        /// <summary>Counterspell an enemy cast (Smart mode learns what's interruptible; Never when disabled).
        /// Arcane Torrent (the Blood Elf AoE-silence backup) now lives in the shared <see cref="Racials"/> bundle.</summary>
        public static RotationStep Counterspell(MageSettings s, float priority) =>
            CombatBlocks.Interrupt("Counterspell", priority,
                ctx => s.InterruptCasts.Value ? InterruptModes.Smart : InterruptModes.Never);

        // --- survival ---

        /// <summary>Ice Block (full immunity, clears debuffs) as a last resort when low and being hit.</summary>
        public static RotationStep IceBlock(MageSettings s, float priority) =>
            Skill.Spell("Ice Block").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseIceBlock.Value
                              && ctx.Me.HealthPercent < s.IceBlockHealthPercent.Value
                              && ctx.EnemiesTargetingMe >= 1);

        /// <summary>Mana Shield (mana → damage absorb) when low and being hit, with mana to spare. Skips a dying lone
        /// target (<see cref="ShieldWorthwhile"/>) so it doesn't burn mana shielding against a mob about to die.</summary>
        public static RotationStep ManaShield(MageSettings s, float priority) =>
            Skill.Spell("Mana Shield").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseManaShield.Value
                              && ctx.Me.HealthPercent < s.ManaShieldHealthPercent.Value
                              && ctx.Me.PowerPercent >= 25
                              && !ctx.Me.HasAura("Mana Shield")
                              && ctx.EnemiesTargetingMe >= 1
                              && ShieldWorthwhile(ctx));

        /// <summary>Keep Ice Barrier up in combat (Frost; auto-skips if not known). Needs some mana to be worth it,
        /// and skips a dying lone target (<see cref="ShieldWorthwhile"/>) so it doesn't refresh as the last mob dies.</summary>
        public static RotationStep IceBarrier(MageSettings s, float priority) =>
            Skill.Spell("Ice Barrier").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseIceBarrier.Value && ctx.HasEnemyTarget
                              && !ctx.Me.HasAura("Ice Barrier")
                              && ctx.Me.PowerPercent > 20
                              && ShieldWorthwhile(ctx));

        /// <summary>Frost Nova: root everything around us when a worthwhile mob is on US and within the nova's
        /// radius — we root it as it crosses ~10yd, not only once it's in true melee, so we get more kite distance.
        /// Skips a mob that's about to die (not worth a root + hop). Self-cast PBAoE; its cooldown gates the recast.
        /// Part of the kite — pairs with <see cref="KiteBack"/>. Held while one of our sheeps is up (Frost Nova's
        /// damage would break it) — Blink/step-back still kite.</summary>
        public static RotationStep FrostNova(MageSettings s, float priority) =>
            Skill.Spell("Frost Nova").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseKiting.Value && ctx.Game.PlayerInCombat
                              && KiteWorthy(ctx, FrostNovaRadius, s.KiteMinTargetHealth.Value, s.KiteSkipGreyLevels.Value)
                              && !AnySheeped(ctx));

        /// <summary>Step back to regain ranged distance when a mob is meleeing us (after Frost Nova roots it).
        /// Cliff-safe (the adapter refuses to step over a ledge). The rotation pauses for the hop. Only runs when
        /// a mob we have ACTUALLY rooted with Frost Nova is on us — without an active root, backing up just
        /// drags the mob along forever (the "runs backwards endlessly" bug). So once Frost Nova is on cooldown
        /// (CD 25s, root only ~8s) and a fresh mob reaches melee unrooted, we don't backpedal — we tank/cast or
        /// Blink. After a step the rooted mob is out of melee range, so it naturally stops after one hop.</summary>
        public static RotationStep KiteBack(MageSettings s, float priority) =>
            new RotationStep(
                name: "Kite back",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => s.UseKiting.Value && ctx.Game.PlayerInCombat && ctx.HasEnemyTarget
                                       && RootedKiteWorthy(ctx, FrostNovaRadius, s.KiteMinTargetHealth.Value, s.KiteSkipGreyLevels.Value),
                action: (ctx, t) => ctx.Game.StepBack(s.KiteYards.Value) ? CastResult.Success : CastResult.Failed,
                ignoreGcd: true,
                recastDelayMs: 1500);

        /// <summary>Blink-escape after the root: the adapter turns to face away, Blinks (teleports in the facing
        /// direction), then faces back toward the target — so it gains distance instead of blinking into the mob.
        /// This is the PRIMARY escape: it triggers at the same <see cref="FrostNovaRadius"/> as the root, so once
        /// Frost Nova roots a mob as it crosses ~10yd, Blink (priority above <see cref="KiteBack"/>) fires the next
        /// tick → blink away → cast from range. The cliff-safe step-back is only the fallback for when Blink isn't
        /// known yet or is on cooldown (then this condition fails and the engine falls through to KiteBack).</summary>
        public static RotationStep Blink(MageSettings s, float priority) =>
            new RotationStep(
                name: "Blink away",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => s.UseBlink.Value && ctx.Game.PlayerInCombat
                                       && KiteWorthy(ctx, FrostNovaRadius, s.KiteMinTargetHealth.Value, s.KiteSkipGreyLevels.Value, meleeOnly: true)
                                       && ctx.Game.IsSpellKnown("Blink") && ctx.Game.IsSpellReady("Blink"),
                action: (ctx, t) => ctx.Game.BlinkAway() ? CastResult.Success : CastResult.Failed,
                recastDelayMs: 2000);

        /// <summary>Polymorph an extra attacker (not the current target) when several mobs are on us. Only
        /// sheepable types, and not one already sheeped. While the sheep is up the specs hold Frost Nova + all
        /// AoE (see <see cref="AnySheeped"/>) so we don't break our own CC; single-target + Blink/step-back run on.</summary>
        public static RotationStep Polymorph(MageSettings s, float priority) =>
            new RotationStep(
                name: "Polymorph",
                priority: priority,
                targets: ctx => ctx.Enemies.Where(e =>
                    e.IsTargetingMe
                    && (ctx.Target == null || e.Guid != ctx.Target.Guid)
                    && !e.HasAura("Polymorph")),
                // The sheepable-type check + facing happen in the adapter (it parks the add on focus): a
                // non-target add's CreatureType can't be read up here, and casting needs us to face it.
                condition: (ctx, t) => s.UsePolymorph.Value
                                       && ctx.Game.IsSpellKnown("Polymorph") && ctx.Game.IsSpellReady("Polymorph")
                                       && ctx.EnemiesTargetingMe >= 2,
                action: (ctx, t) => ctx.Game.Polymorph(t) ? CastResult.Success : CastResult.Failed);

        /// <summary>Finish OUR sheeped add once the main target is dead. When we have no live target but a mob we
        /// Polymorphed is still up, target it — the next tick's nukes break the sheep and kill it. Without this the
        /// product wanders off to a fresh pull, the Polymorph expires unattended, and the add wakes up on us. Only
        /// acts in the post-kill gap (no live target), so it doesn't fight the product over a real multi-mob pull.</summary>
        public static RotationStep FinishSheepedAdd(MageSettings s, float priority) =>
            new RotationStep(
                name: "Finish sheeped add",
                priority: priority,
                targets: ctx => ctx.Enemies.Where(e => e.IsAlive && e.IsAttackable && e.HasMyAura("Polymorph")),
                condition: (ctx, t) => s.UsePolymorph.Value && !ctx.HasEnemyTarget,
                action: (ctx, t) => { ctx.Game.SetTarget(t); return CastResult.Success; });

        // --- mana ---

        /// <summary>Channel Evocation to refill mana when low — but only when nothing is meleeing us (so the
        /// channel isn't beaten off; kite first).</summary>
        public static RotationStep Evocation(MageSettings s, float priority) =>
            Skill.Spell("Evocation").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Me.PowerPercent < s.EvocationManaPercent.Value && !MeleeOnMe(ctx));

        /// <summary>Use a conjured Mana Gem (or mana potion) below the mana threshold. Off the GCD.</summary>
        public static RotationStep ManaGem(MageSettings s, float priority) =>
            CombatBlocks.UseItems("Mana gem", Consumables.ManaItems,
                ctx => s.UseManaGem.Value && ctx.Me.PowerPercent < s.ManaGemManaPercent.Value,
                priority);

        /// <summary>Wand (Shoot) the target to conserve mana when low — needs a wand equipped (auto-skips
        /// otherwise) and only when not already wanding. Off the GCD.</summary>
        public static RotationStep Wand(MageSettings s, float priority) =>
            Skill.Spell("Shoot").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseWand.Value
                              && ctx.Me.PowerPercent < s.WandManaPercent.Value
                              && !ctx.Game.IsCurrentSpell("Shoot")).OffGcd();

        // --- auto-conjure (out of combat) ---

        /// <summary>Append the out-of-combat conjure steps (food / water / mana gem) to a spec's step list — so
        /// every mage spec auto-stocks its own consumables with one call. Mirrors the hunter's WithPetSpecials.</summary>
        public static List<RotationStep> WithConjure(MageSettings s, List<RotationStep> coreSteps)
        {
            coreSteps.Add(ConjureFood(s, priority: 0.40f));
            coreSteps.Add(ConjureWater(s, priority: 0.41f));
            coreSteps.Add(ConjureManaGem(s, priority: 0.42f));
            return coreSteps;
        }

        /// <summary>Conjure food when the stock runs low (out of combat). Uses Conjure Refreshment if known
        /// (its item restores both food and water, so the water step then sits idle), else Conjure Food.</summary>
        public static RotationStep ConjureFood(MageSettings s, float priority) =>
            new RotationStep(
                name: "Conjure food",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (!CanConjure(ctx, s)) return false;
                    string spell = FoodSpell(ctx);
                    return ctx.Game.IsSpellKnown(spell) && ctx.Game.IsSpellReady(spell)
                           && ctx.Game.CountItems(Consumables.ConjuredFood) < s.ConjureCount.Value;
                },
                action: (ctx, t) => ctx.Game.Cast(FoodSpell(ctx), ctx.Me));

        /// <summary>Conjure water when low (out of combat) — skipped when Conjure Refreshment is known (its
        /// combined item already covers water via the food count).</summary>
        public static RotationStep ConjureWater(MageSettings s, float priority) =>
            new RotationStep(
                name: "Conjure water",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => CanConjure(ctx, s)
                                       && !ctx.Game.IsSpellKnown("Conjure Refreshment")
                                       && ctx.Game.IsSpellKnown("Conjure Water") && ctx.Game.IsSpellReady("Conjure Water")
                                       && ctx.Game.CountItems(Consumables.ConjuredWater) < s.ConjureCount.Value,
                action: (ctx, t) => ctx.Game.Cast("Conjure Water", ctx.Me));

        /// <summary>Conjure a Mana Gem when we have none (out of combat). The gem is rechargeable, so one is enough.</summary>
        public static RotationStep ConjureManaGem(MageSettings s, float priority) =>
            new RotationStep(
                name: "Conjure mana gem",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => CanConjure(ctx, s)
                                       && ctx.Game.IsSpellKnown("Conjure Mana Gem") && ctx.Game.IsSpellReady("Conjure Mana Gem")
                                       && ctx.Game.CountItems(Consumables.ManaGems) == 0,
                action: (ctx, t) => ctx.Game.Cast("Conjure Mana Gem", ctx.Me));

        private static bool CanConjure(CombatContext ctx, MageSettings s) =>
            s.UseConjure.Value && !ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMounted;

        private static string FoodSpell(CombatContext ctx) =>
            ctx.Game.IsSpellKnown("Conjure Refreshment") ? "Conjure Refreshment" : "Conjure Food";

        /// <summary>True if any enemy is meleeing the player (within <see cref="MeleeRange"/> and on us).</summary>
        public static bool MeleeOnMe(CombatContext ctx) =>
            ctx.Enemies.Any(e => e.IsTargetingMe && e.Distance <= MeleeRange);

        /// <summary>True if a mob worth kiting is on us and within <paramref name="range"/>: it's targeting us and
        /// still above <paramref name="minHealthPct"/> (a mob about to die isn't worth the root + hop). When
        /// <paramref name="meleeOnly"/>, only MELEE mobs count (a caster has a mana pool and casts from range, so
        /// stepping back doesn't escape it — we burst it instead; Frost Nova still freezes it for a shatter). Used
        /// to trigger Frost Nova (any mob) / Blink (melee only). Never while SWIMMING — in water the player swims at
        /// half speed and the product re-approaches the rooted mob between hops (it undoes the kite), so the kite
        /// just oscillates at melee and wastes Frost Nova; we stand and nuke instead.</summary>
        private static bool KiteWorthy(CombatContext ctx, float range, int minHealthPct, int skipGreyLevels, bool meleeOnly = false) =>
            // Cheap enemy check FIRST so the Lua-backed swimming read is only paid when a mob is actually on us
            // (most ticks we're nuking from range with nothing in melee → this short-circuits and skips the Lua).
            ctx.Enemies.Any(e => e.IsTargetingMe && e.Distance <= range && e.HealthPercent > minHealthPct
                                 && (!meleeOnly || !e.IsCaster) && !IsGrey(ctx, e, skipGreyLevels))
            && !ctx.Game.PlayerIsSwimming;

        /// <summary>Like <see cref="KiteWorthy"/> but the mob must also be rooted by OUR Frost Nova AND be a MELEE
        /// mob (not a caster). The step-back only runs while this holds: backing off a rooted melee mob gains real
        /// distance (it can't follow); backing off a caster is pointless (it keeps casting from range) and backing
        /// off an unrooted mob just drags it along. Also suppressed while swimming.</summary>
        private static bool RootedKiteWorthy(CombatContext ctx, float range, int minHealthPct, int skipGreyLevels) =>
            // Enemy check first (incl. the per-unit aura read) so the Lua swimming read is only paid when a rooted
            // mob is on us — otherwise this short-circuits before either.
            ctx.Enemies.Any(e => e.IsTargetingMe && e.Distance <= range && e.HealthPercent > minHealthPct
                                 && !e.IsCaster && !IsGrey(ctx, e, skipGreyLevels) && e.HasMyAura("Frost Nova"))
            && !ctx.Game.PlayerIsSwimming;

        /// <summary>True if <paramref name="e"/> is a "grey", trivial mob — at least <paramref name="skipGreyLevels"/>
        /// levels BELOW us — so it isn't worth kiting (it dies in a hit or two; just nuke it). Guards on both
        /// levels being known (0 = unread) so an unknown level never counts as grey, and on the toggle (0 = off).</summary>
        private static bool IsGrey(CombatContext ctx, IWowUnit e, int skipGreyLevels) =>
            skipGreyLevels > 0 && e.Level > 0 && ctx.Me.Level > 0
            && e.Level <= ctx.Me.Level - skipGreyLevels;

        /// <summary>True if one of OUR Polymorphs is active on an enemy. While it is, the specs hold Frost Nova
        /// and all AoE (they deal damage and would break the sheep); single-target on the main mob + Blink /
        /// step-back kiting don't break it, so they keep running.</summary>
        public static bool AnySheeped(CombatContext ctx) =>
            ctx.Enemies.Any(e => e.HasMyAura("Polymorph"));

        private static string ResolveArmor(CombatContext ctx, MageSettings s, MageSpec spec)
        {
            string choice = s.ArmorChoice.Value;
            if (choice != "Auto" && ctx.Game.IsSpellKnown(choice)) return choice;

            // Auto: spec preference, then fall back to anything known. Ice Armor supersedes Frost Armor.
            string[] prefs;
            switch (spec)
            {
                case MageSpec.Fire: prefs = new[] { "Molten Armor", "Mage Armor", "Ice Armor", "Frost Armor" }; break;
                case MageSpec.Arcane: prefs = new[] { "Mage Armor", "Molten Armor", "Ice Armor", "Frost Armor" }; break;
                default: prefs = new[] { "Ice Armor", "Frost Armor", "Mage Armor", "Molten Armor" }; break; // Frost
            }
            foreach (string a in prefs)
                if (ctx.Game.IsSpellKnown(a)) return a;
            return null;
        }
    }
}
