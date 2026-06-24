using System;
using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;

namespace AIO3.Core.Rotations.Warlock
{
    /// <summary>
    /// Shared warlock building blocks — the "caster baseline" every warlock spec composes: armor upkeep, the
    /// Life Tap mana engine (the signature warlock trade of health for mana), the Drain Life self-heal, and the
    /// low-mana wand. Spell names that have a player choice (the armor, the curse, which demon to summon) are
    /// resolved at EVAL TIME so overlay edits apply live and an "Auto"/best-known pick fills in as the player
    /// levels.
    ///
    /// Cast-time / channelled spells (Drain Life) gate on <c>!PlayerIsMoving</c> in the spec; instants
    /// (Life Tap, Corruption, the curses) do not. The wand is off the GCD.
    /// </summary>
    public static class WarlockCommon
    {
        /// <summary>Keep the right armor up. The chosen armor (or the best-known via Auto) is resolved at eval
        /// time and cast when missing.</summary>
        public static RotationStep Armor(WarlockSettings s, float priority) =>
            new RotationStep(
                name: "Armor",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    string armor = ResolveArmor(ctx, s);
                    return armor != null && !ctx.Me.HasAura(armor);
                },
                action: (ctx, t) =>
                {
                    string armor = ResolveArmor(ctx, s);
                    return armor != null ? ctx.Game.Cast(armor, ctx.Me) : CastResult.Failed;
                });

        /// <summary>Life Tap — trade health for mana when mana is low, but only while health is above the floor
        /// (so we never tap ourselves into danger). Instant, so it does not gate on movement.</summary>
        public static RotationStep LifeTap(WarlockSettings s, float priority) =>
            Skill.Spell("Life Tap").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Me.PowerPercent < s.LifeTapManaPercent.Value
                              && ctx.Me.HealthPercent > s.LifeTapHealthFloor.Value);

        /// <summary>Keep the Glyph of Life Tap spell-power buff up: re-tap when the buff is missing and health is
        /// safe (a higher floor than the mana tap — this one is purely for uptime, never worth risking HP).
        /// Opt-in via the GlyphLifeTap toggle.</summary>
        public static RotationStep GlyphLifeTap(WarlockSettings s, float priority) =>
            Skill.Spell("Life Tap").Priority(priority).On(Targets.Self)
                 .When(ctx => s.GlyphLifeTap.Value
                              && !ctx.Me.HasAura("Life Tap")
                              && ctx.Me.HealthPercent > s.LifeTapHealthFloor.Value);

        /// <summary>Channel Drain Life to self-heal when low and solo. It is a channel, so it gates on
        /// <c>!PlayerIsMoving</c> (you cannot start it on the move) right here in the shared block, so every
        /// spec that composes it gets the same correct behaviour with one call.</summary>
        public static RotationStep DrainLife(WarlockSettings s, float priority) =>
            Skill.Spell("Drain Life").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.DrainLifeHealthPercent.Value > 0
                              && !ctx.IsInGroup
                              && ctx.Me.HealthPercent < s.DrainLifeHealthPercent.Value
                              && !ctx.Game.PlayerIsMoving);

        /// <summary>Wand (Shoot) the target to conserve mana when low — needs a wand equipped (auto-skips
        /// otherwise) and only when not already wanding. Off the GCD.</summary>
        public static RotationStep Wand(WarlockSettings s, float priority) =>
            Skill.Spell("Shoot").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseWand.Value
                              && ctx.Me.PowerPercent < s.WandManaPercent.Value
                              && !ctx.Game.IsCurrentSpell("Shoot")).OffGcd();

        /// <summary>The summon spell for the chosen demon (e.g. "Summon Voidwalker"). Resolved at eval time so
        /// an overlay edit swaps the demon live and an "Auto" pick fills in per spec. Used by the shared
        /// PetControl.Summon call so the spec keeps the right pet up. The <paramref name="spec"/> only matters
        /// for Auto: Demonology → Felguard, Destruction → Imp, everything else → Voidwalker; a known-spell
        /// fallback drops to the best demon actually learned, ending at the Imp (the level-1 pet).</summary>
        public static string SummonSpell(WarlockSettings s, CombatContext ctx, WarlockSpec spec) =>
            "Summon " + ResolvePet(s, ctx, spec);

        /// <summary>Resolve which demon to summon: a manual choice wins; "Auto" picks the spec-appropriate demon,
        /// then falls back to the best demon actually LEARNED — ending at the Imp, every warlock's level-1 pet —
        /// so a low-level lock that hasn't tamed the spec demon (or even the Voidwalker) yet still summons one.</summary>
        public static string ResolvePet(WarlockSettings s, CombatContext ctx, WarlockSpec spec)
        {
            string choice = s.Pet.Value;
            if (choice != "Auto") return choice;

            string preferred;
            switch (spec)
            {
                case WarlockSpec.Demonology: preferred = "Felguard"; break;
                case WarlockSpec.Destruction: preferred = "Imp"; break;
                default: preferred = "Voidwalker"; break;
            }
            if (ctx == null) return preferred;

            // The spec demon may not be learned yet at low level — fall through to the best one we DO know.
            // The Imp is learned at level 1, so it's the guaranteed final fallback (no more "summons nothing").
            foreach (string demon in new[] { preferred, "Voidwalker", "Imp" })
                if (ctx.Game.IsSpellKnown("Summon " + demon)) return demon;
            return preferred;
        }

        /// <summary>The curse spell to maintain, from the chosen curse setting. Resolved at eval time so an
        /// overlay edit swaps the curse live.</summary>
        public static string CurseSpell(WarlockSettings s)
        {
            switch (s.Curse.Value)
            {
                case "Doom": return "Curse of Doom";
                case "Elements": return "Curse of the Elements";
                case "Tongues": return "Curse of Tongues";
                case "Weakness": return "Curse of Weakness";
                default: return "Curse of Agony";
            }
        }

        /// <summary>Maintain the chosen curse on the current target — cast when missing. The curse name is
        /// resolved each tick (<see cref="CurseSpell"/>), so swapping the curse setting takes effect live; the
        /// known/ready gate then auto-skips a curse the warlock has not learned yet. (Only one curse can be up
        /// at a time, so we just check it is missing rather than a refresh window.)</summary>
        public static RotationStep MaintainCurse(WarlockSettings s, float priority) =>
            new RotationStep(
                name: "Curse",
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) =>
                {
                    string curse = CurseSpell(s);
                    if (!ctx.Game.IsSpellKnown(curse) || !ctx.Game.IsSpellReady(curse)) return false;
                    float range = ctx.Game.SpellRange(curse);
                    if (range > 0f && t.Distance > range) return false;
                    return !t.HasMyAura(curse);
                },
                action: (ctx, t) => ctx.Game.Cast(CurseSpell(s), t));

        /// <summary>Below this range an enemy that is on us counts as "in melee" — the trigger for the Fear /
        /// Howl panic buttons (a cloth caster with no Frost Nova has to break melee to survive). One named
        /// constant so every emergency gate uses the same radius. 8yd deliberately mirrors
        /// <c>MageCommon.MeleeRange</c> (the other cloth caster), not the hunter's 5yd ranged floor.</summary>
        public const float MeleeRange = 8f;

        /// <summary>Throttle shared by both emergency panic buttons (Fear / Howl) so they can't drift apart and
        /// so a feared mob's brief window isn't re-feared every tick.</summary>
        private const int FearRecastMs = 3000;

        /// <summary>True if any enemy is meleeing the player (targeting us and within <see cref="MeleeRange"/>).</summary>
        public static bool MeleeOnMe(CombatContext ctx) =>
            ctx.Enemies.Any(e => e.IsAlive && e.IsTargetingMe && e.Distance <= MeleeRange);

        /// <summary>Count of enemies meleeing the player (used to decide single Fear vs the AoE Howl).</summary>
        public static int MeleeingMe(CombatContext ctx) =>
            ctx.Enemies.Count(e => e.IsAlive && e.IsTargetingMe && e.Distance <= MeleeRange);

        // --- "let the DoTs finish it" (stop nuking a dying trash mob; save mana / GCDs while leveling) ---

        /// <summary>How many of OUR ticking DoTs the target must carry before we trust them to finish it off.</summary>
        private const int DotsToFinishCount = 2;

        /// <summary>A DoT must have at least this long left to count toward finishing the mob (else it falls off first).</summary>
        private const int DotsFinishMinTimeLeftMs = 2000;

        /// <summary>The damaging single-target DoTs a leveling warlock lays down — counted as "coverage" on a dying
        /// mob. Curse of Doom is a single delayed nuke, not a finishing DoT, so it is intentionally excluded; the
        /// leveling curse is Agony (the ramping ticking DoT).</summary>
        private static readonly string[] FinishingDots =
            { "Corruption", "Immolate", "Unstable Affliction", "Haunt", "Curse of Agony" };

        /// <summary>
        /// True when the current target is a low, normal mob carrying enough of OUR ticking DoTs that they will
        /// finish it without another hard cast — so the filler nuke (Shadow Bolt / Incinerate / Soul Fire) can be
        /// skipped. While leveling this saves mana and Life-Tap (health) pressure and avoids overkill GCDs on a mob
        /// that is already dead; the freed time goes to the next pull. Gated by
        /// <see cref="WarlockSettings.LetDotsFinishHealthPercent"/> (0 = off, always nuke).
        ///
        /// Deliberately a simple HP% + DoT-coverage heuristic, not a precise time-to-die model: the only cost of a
        /// false "let it die" is the mob dying a moment slower (never a survival risk), and a precise model needs
        /// absolute target HP + per-DoT tick damage we don't track yet. Bosses/elites are excluded — their HP pool
        /// dwarfs DoT damage, so a % floor there would drop the nuke far too early.
        /// </summary>
        public static bool DotsWillFinishTarget(CombatContext ctx, WarlockSettings s)
        {
            int floor = s.LetDotsFinishHealthPercent.Value;
            if (floor <= 0 || !ctx.HasEnemyTarget) return false;

            IWowUnit target = ctx.Target;
            if (target.HealthPercent > floor) return false;
            if (target.IsElite || target.IsBoss()) return false; // huge HP pool — DoTs won't finish it; keep nuking

            int active = 0;
            foreach (string dot in FinishingDots)
                if (target.MyAuraTimeLeftMs(dot) >= DotsFinishMinTimeLeftMs && ++active >= DotsToFinishCount)
                    return true;
            return false;
        }

        // --- pet specials (mirror HunterCommon.PetSpecials) ---

        /// <summary>
        /// The demon's own special abilities, cast via its action bar — each auto-skips when THIS demon
        /// doesn't have it (a Voidwalker has Torment but no Spell Lock, a Felhunter has Spell Lock, an Imp has
        /// Firebolt). So every spec can splice the same set in: <see cref="PetControl.Taunt"/> /
        /// <see cref="PetControl.UseAbility"/> gate on <c>PetHasAbility</c> / <c>PetAbilityReady</c> for the
        /// current pet. Returned as a list so every warlock spec composes it with one call.
        ///
        /// Included: Torment (Voidwalker tank-taunt — the big solo survival win, gated on the new PetTank toggle),
        /// Spell Lock (Felhunter — the warlock's ONLY interrupt, gated on the InterruptCasts mode), and the Imp's
        /// Firebolt (ranged nuke) + Blood Pact (party stamina buff) kept on the Imp's AUTOCAST. Deferred: Devour
        /// Magic, Sacrifice (Voidwalker), Seduction (Succubus), Felguard Cleave.
        /// </summary>
        public static List<RotationStep> PetSpecials(WarlockSettings s)
        {
            Func<CombatContext, bool> manage = ctx => s.ManagePet.Value;

            return new List<RotationStep>
            {
                // Voidwalker Torment: taunt mobs off the cloth caster so the Voidwalker TANKS them. Auto-skips
                // for Imp / Felhunter (no Torment) via PetHasAbility. Gated on ManagePet + the PetTank toggle.
                PetControl.Taunt(ctx => s.ManagePet.Value && s.PetTank.Value, "Torment", 0.91f),

                // Felhunter Spell Lock: the warlock's only interrupt. Fire when the target is casting and the mode
                // isn't Never. For now Smart == Always (fire on any target cast); the empirical InterruptTracker
                // integration is a later refinement. Auto-skips for non-Felhunter pets via PetAbilityReady.
                // TODO: route through ctx.Interrupts (Smart should skip proven-uninterruptible casts) like
                // CombatBlocks.Interrupt does — needs a pet-ability interrupt path that records attempts.
                PetControl.UseAbility(manage, "Spell Lock", 0.92f,
                    when: ctx => s.InterruptCasts.Value != InterruptModes.Never
                                 && ctx.HasEnemyTarget && ctx.Target.IsCasting),

                // Imp Firebolt: a cast-time ranged nuke with NO cooldown — leave it on the Imp's AUTOCAST rather
                // than re-triggering it every tick (which fights its cast time). The Imp then fires it itself.
                // Auto-skips for non-Imps. Turn ImpFirebolt off to disable the autocast.
                PetControl.Autocast(ctx => s.ManagePet.Value && s.ImpFirebolt.Value, "Firebolt", 0.96f),

                // Imp Blood Pact: the party stamina buff — keep it on the Imp's autocast so it stays up. Pure
                // benefit, so no separate toggle (just gated on managing the pet). Auto-skips for non-Imps.
                PetControl.Autocast(manage, "Blood Pact", 0.97f),
            };
        }

        /// <summary>Append <see cref="PetSpecials"/> to a spec's core step list (so every warlock spec gets the
        /// demon's own abilities with one call — mirrors <c>HunterCommon.WithPetSpecials</c>).</summary>
        public static IReadOnlyList<RotationStep> WithPetSpecials(WarlockSettings s, List<RotationStep> coreSteps)
        {
            coreSteps.AddRange(PetSpecials(s));
            return coreSteps;
        }

        // --- emergency Fear / Howl (panic buttons; a warlock has no Frost Nova) ---

        /// <summary>
        /// EMERGENCY single-target Fear: when health is below <c>FearHealthPercent</c> AND a mob is meleeing us,
        /// Fear the attacker that's on us to break melee for a brief heal window (Drain Life / Healthstone). A
        /// feared mob runs uncontrolled and DoTs break Fear, so this is a panic button, not a sustained kite —
        /// hence the tight low-HP + meleed gate and the recast throttle so it doesn't spam. Instant. Targets the
        /// meleeing enemy on us (prefers the current target when it is the one meleeing us). Known/ready gating
        /// is automatic, so a low-level lock without Fear skips cleanly.
        /// </summary>
        public static RotationStep Fear(WarlockSettings s, float priority)
        {
            const string fear = "Fear";
            return new RotationStep(
                name: fear,
                priority: priority,
                targets: ctx => FearTarget(ctx) is IWowUnit u ? new[] { u } : Array.Empty<IWowUnit>(),
                condition: (ctx, t) =>
                {
                    if (!s.UseFear.Value || s.FearHealthPercent.Value <= 0) return false;
                    if (ctx.Me.HealthPercent >= s.FearHealthPercent.Value) return false;
                    if (!ctx.Game.IsSpellKnown(fear) || !ctx.Game.IsSpellReady(fear)) return false;
                    float range = ctx.Game.SpellRange(fear);
                    if (range > 0f && t.Distance > range) return false;
                    // Only while actually meleed: t is already a meleeing-on-us enemy (see FearTarget).
                    return true;
                },
                action: (ctx, t) => ctx.Game.Cast(fear, t),
                recastDelayMs: FearRecastMs);
        }

        /// <summary>
        /// EMERGENCY AoE Howl of Terror: when health is below <c>FearHealthPercent</c> AND we are SURROUNDED
        /// (>= 2 enemies meleeing us), Howl to fear everything nearby — a self-cast PBAoE, like Frost Nova. Same
        /// panic-button rationale as <see cref="Fear"/> (brief heal window, not a kite). Self-cast (no target
        /// token), instant. Known/ready gating is automatic.
        /// </summary>
        public static RotationStep HowlOfTerror(WarlockSettings s, float priority) =>
            Skill.Spell("Howl of Terror").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseHowl.Value && s.FearHealthPercent.Value > 0
                              && ctx.Me.HealthPercent < s.FearHealthPercent.Value
                              && MeleeingMe(ctx) >= 2)
                 .RecastDelay(FearRecastMs);

        /// <summary>The enemy to Fear: prefer the current target when it is the one meleeing us (so we don't drop
        /// our DoT'd kill target's threat onto a random add), else the lowest-HP enemy meleeing us. Null when no
        /// enemy is in melee on us — which keeps the Fear step quiet outside the emergency.</summary>
        private static IWowUnit FearTarget(CombatContext ctx)
        {
            bool OnMe(IWowUnit e) => e != null && e.IsAlive && e.IsTargetingMe && e.Distance <= MeleeRange;

            if (OnMe(ctx.Target)) return ctx.Target;

            IWowUnit best = null;
            double bestHp = double.MaxValue;
            foreach (IWowUnit e in ctx.Enemies)
                if (OnMe(e) && e.HealthPercent < bestHp)
                {
                    best = e;
                    bestHp = e.HealthPercent;
                }
            return best;
        }

        private static string ResolveArmor(CombatContext ctx, WarlockSettings s)
        {
            string choice = s.ArmorChoice.Value;
            if (choice != "Auto" && ctx.Game.IsSpellKnown(choice)) return choice;

            // Auto: best known. Fel Armor supersedes Demon Armor supersedes Demon Skin.
            string[] prefs = { "Fel Armor", "Demon Armor", "Demon Skin" };
            foreach (string a in prefs)
                if (ctx.Game.IsSpellKnown(a)) return a;
            return null;
        }
    }
}
