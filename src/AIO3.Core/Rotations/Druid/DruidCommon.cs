using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;

namespace AIO3.Core.Rotations.Druid
{
    /// <summary>
    /// Shared druid building blocks — the hybrid baseline every druid spec composes, so the cross-cutting
    /// behaviour (form switching, Mark of the Wild / Thorns upkeep, the Cat energy/combo ladder, the Bear
    /// rage/tank ladder, the in-combat self-heal, Barkskin / Innervate) is written ONCE and stays consistent.
    /// Each returns a ready RotationStep; the spec just lists them in priority order alongside its signature
    /// abilities.
    ///
    /// Cat finishers spend combo points (read via <see cref="CombatContext.ComboPoints"/> — the SAME seam the
    /// rogue uses); cat builders cost energy (<c>ctx.Me.Energy</c>); bear abilities cost rage (<c>ctx.Me.Rage</c>);
    /// forms / procs / eclipse are auras (<c>ctx.Me.HasAura("Cat Form")</c> …). Known/ready/GCD/range gating is
    /// added automatically by the DSL, so these declare only the interesting condition; an unknown spell
    /// auto-skips, so the same list runs from level 10 (pre-form) up as the player learns the forms.
    /// </summary>
    public static class DruidCommon
    {
        /// <summary>An attacker within this range counts as "in melee" on us — the trigger for the Bear-form
        /// switch and the surrounded gates. One named constant so every melee gate uses the same radius (mirrors
        /// the old AIO's enemy-count radius for the form decision). Named distinctly from
        /// <see cref="DruidSettings.MeleeRange"/> (the WRobot ICustomClass.Range), which is an unrelated concept.</summary>
        public const float SurroundRadius = 8f;

        // --- form facts (one definition each, so every step agrees) ---

        /// <summary>True while in Cat Form (the single-target DPS form: energy + combo points).</summary>
        public static bool InCatForm(CombatContext ctx) => ctx.Me.HasAura("Cat Form");

        /// <summary>True while in a bear form — Bear or Dire Bear (the tank/AoE form: rage).</summary>
        public static bool InBearForm(CombatContext ctx) =>
            ctx.Me.HasAura("Bear Form") || ctx.Me.HasAura("Dire Bear Form");

        /// <summary>True while in Moonkin Form (the Balance caster form).</summary>
        public static bool InMoonkinForm(CombatContext ctx) => ctx.Me.HasAura("Moonkin Form");

        /// <summary>True while in any shapeshift combat form (Cat / Bear / Dire Bear / Moonkin). A shift-out heal
        /// must drop the form first, so the "needs to shift out" gate reads this.</summary>
        public static bool InAnyForm(CombatContext ctx) => InCatForm(ctx) || InBearForm(ctx) || InMoonkinForm(ctx);

        /// <summary>Number of enemies meleeing us within <see cref="SurroundRadius"/> — the "am I surrounded?"
        /// count that drives the Bear-form switch and the bear AoE gate. Shared so the form decision and the AoE
        /// abilities agree on what a "pack on me" is.</summary>
        public static int Surrounding(CombatContext ctx) =>
            ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= SurroundRadius);

        // --- out-of-combat buffs (CombatBlocks.SelfBuff pattern; Gift of the Wild supersedes Mark of the Wild) ---

        /// <summary>Keep Mark of the Wild up (skipped when the raid-wide Gift of the Wild is already on, e.g. from a
        /// group). Out-of-combat only — like the old AIO's OOCBuffs addon (RunInCombat=false), we don't break the
        /// rotation to re-buff mid-fight; the buffs are long and applied before the pull. Opt-out via the toggle.</summary>
        public static RotationStep MarkOfTheWild(DruidSettings s, float priority) =>
            Skill.Spell("Mark of the Wild").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseMarkOfTheWild.Value && !ctx.Game.PlayerInCombat
                              && !ctx.Me.HasAura("Mark of the Wild") && !ctx.Me.HasAura("Gift of the Wild"));

        /// <summary>Keep Thorns up (reflects melee damage). Out-of-combat only (see <see cref="MarkOfTheWild"/>).
        /// Opt-out via the toggle.</summary>
        public static RotationStep Thorns(DruidSettings s, float priority) =>
            Skill.Spell("Thorns").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseThorns.Value && !ctx.Game.PlayerInCombat && !ctx.Me.HasAura("Thorns"));

        // --- survival (form-agnostic) ---

        /// <summary>Barkskin — off-GCD damage reduction usable in ANY form (it doesn't break shapeshift). Fire it
        /// below the configured health %. The druid's cheap, always-available panic button.</summary>
        public static RotationStep Barkskin(DruidSettings s, float priority) =>
            Skill.Spell("Barkskin").Priority(priority).On(Targets.Self)
                 .When(ctx => s.BarkskinHealthPercent.Value > 0
                              && ctx.Me.HealthPercent < s.BarkskinHealthPercent.Value)
                 .OffGcd();

        /// <summary>Innervate — the mana cooldown, on yourself below the configured mana %. Solo only (the group
        /// "innervate the healer" case is a Group-mode concern, deferred).</summary>
        public static RotationStep Innervate(DruidSettings s, float priority) =>
            Skill.Spell("Innervate").Priority(priority).On(Targets.Self)
                 .When(ctx => s.InnervateManaPercent.Value > 0
                              && ctx.Me.PowerPercent <= s.InnervateManaPercent.Value);

        // --- in-combat self-heal (the druid's edge) ---

        // The Predator's Swiftness proc makes the next Regrowth / Healing Touch / Nature's Grasp INSTANT, so it can
        // be cast WITHOUT shifting out of cat/bear form — the cheap, form-preserving heal. The old AIO used the
        // "Predator's Swiftness" buff for the same instant Regrowth (SoloFeral.cs:37). We prefer that over a
        // shift-out heal: an instant keeps us attacking and costs no form re-shift.

        /// <summary>True when the free instant heal proc (Predator's Swiftness, the resto/feral "next nature heal is
        /// instant") is up — so Regrowth / Healing Touch can be cast in-form without dropping it.</summary>
        public static bool HasInstantHealProc(CombatContext ctx) => ctx.Me.HasAura("Predator's Swiftness");

        /// <summary>An instant in-combat heal via the Predator's Swiftness proc — fires below the IC-heal threshold
        /// while the proc is up, in ANY form (the instant cast doesn't break shapeshift). No mana gate beyond the
        /// spell's own cost: the proc makes it free/instant, so it's always worth taking when hurt. Sits above the
        /// shift-out heals so the form-preserving option wins when the proc is available.</summary>
        public static RotationStep InstantProcHeal(DruidSettings s, string spell, System.Func<DruidSettings, bool> enabled, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(s)
                              && ctx.Me.HealthPercent < s.InCombatHealHealthPercent.Value
                              && HasInstantHealProc(ctx)
                              && !ctx.Me.HasAura(spell)); // don't re-stack a HoT/Regrowth that's already on us

        /// <summary>A shift-out in-combat heal: drop form and cast the heal when hurt below the IC-heal threshold,
        /// gated on a SIMPLE mana % floor (we deliberately drop the old GetSpellCost arithmetic). Skipped while the
        /// Predator's Swiftness proc is up (the instant version above is strictly better — keep form). Casting the
        /// heal itself drops the form automatically in WoW, so we don't issue a separate shapeshift cast; the
        /// engine just casts the heal. Not re-stacked if already on us (a HoT). The mana gate keeps a low-mana
        /// druid from thrashing in and out of form for a heal it can't afford.</summary>
        public static RotationStep ShiftOutHeal(DruidSettings s, string spell, System.Func<DruidSettings, bool> enabled, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(s)
                              && ctx.Me.HealthPercent < s.InCombatHealHealthPercent.Value
                              && ctx.Me.PowerPercent > s.HealManaPercent.Value
                              && !HasInstantHealProc(ctx)   // prefer the instant proc heal when it's available
                              && !ctx.Me.HasAura(spell));

        // --- Cat Form: builders / finishers / cooldowns (energy + combo points) ---

        /// <summary>Switch to Cat Form for single-target DPS — when not surrounded (fewer than BearCount attackers
        /// in melee) and not already in cat. The form switch itself; the cat ladder runs once shifted. Auto-skips
        /// until Cat Form is learned (a low-level druid stays a caster). Sits ABOVE the bear switch in the spec so
        /// the surrounded check there wins when a pack is on us.</summary>
        public static RotationStep CatForm(DruidSettings s, float priority) =>
            Skill.Spell("Cat Form").Priority(priority).On(Targets.Self)
                 .When(ctx => !InCatForm(ctx) && Surrounding(ctx) < s.BearCount.Value);

        /// <summary>Prowl — enter stealth out of combat (opt-in) so the spec's positional opener (Ravage/Pounce) can
        /// fire. Gated like the rogue's Stealth: only while the product commits to a fight, not idle / mounted /
        /// resting, and never with a debuff that would break stealth. Requires Cat Form (Prowl is a cat ability).</summary>
        public static RotationStep Prowl(DruidSettings s, float priority) =>
            Skill.Spell("Prowl").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseProwl.Value
                              && InCatForm(ctx)
                              && !ctx.Me.HasAura("Prowl")
                              && ctx.Game.ProductIsFighting
                              && !ctx.Game.PlayerInCombat
                              && !ctx.Game.PlayerIsMounted
                              && !ctx.Game.PlayerIsResting
                              && !ctx.Game.PlayerHasHarmfulAura());

        /// <summary>Prowl opener — the first strike of a stealth-opened cat fight, the ability chosen by the
        /// <see cref="DruidSettings.ProwlOpener"/> dropdown: Pounce (positional-free front stun) or Ravage (a big
        /// hit, but must be cast from BEHIND the target). Fires only while prowling and in melee (the engine's range
        /// gate), so it breaks stealth to start the fight and the normal build-and-finish loop takes over. The
        /// RecastDelay is a safety net: if the chosen opener can't land (e.g. Ravage when not behind) it isn't
        /// re-issued every tick. Unknown/unusable spell auto-skips. Mirrors RogueCommon.Opener.</summary>
        public static RotationStep ProwlOpener(DruidSettings s, string spell, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseProwl.Value && ctx.Me.HasAura("Prowl") && OpenerSelected(s, spell, ctx))
                 .RecastDelay(2000);

        /// <summary>Which opener wins for <paramref name="spell"/> under the ProwlOpener setting. An explicit
        /// "Ravage"/"Pounce" forces that spell; "Auto" lets the FC pick by position — Ravage when we're behind the
        /// target (where it can land), Pounce from the front. Reuses the shared, class-agnostic
        /// <see cref="IGameClient.PlayerIsBehindTarget"/> seam (the same one the rogue's Garrote opener uses).</summary>
        private static bool OpenerSelected(DruidSettings s, string spell, CombatContext ctx)
        {
            string mode = s.ProwlOpener.Value;
            if (mode != "Auto") return mode == spell;
            return ctx.Game.PlayerIsBehindTarget() ? spell == "Ravage" : spell == "Pounce";
        }

        /// <summary>Tiger's Fury — an instant energy + damage cooldown. Pop it on cooldown while in Cat Form and not
        /// prowling (don't break the stealth opener). Off the GCD (instant). Opt-out via the toggle.</summary>
        public static RotationStep TigersFury(DruidSettings s, float priority) =>
            Skill.Spell("Tiger's Fury").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseTigersFury.Value && InCatForm(ctx) && !ctx.Me.HasAura("Prowl"))
                 .OffGcd();

        /// <summary>Berserk — the Feral burst cooldown (cat: free combo-point spam; bear: removes Maul/Mangle CD).
        /// Gated like the other major cooldowns: on a boss/elite or a pack, when cooldowns are enabled, and only in
        /// a combat form. Off the GCD. Auto-skips until learned.</summary>
        public static RotationStep Berserk(DruidSettings s, float priority) =>
            Skill.Spell("Berserk").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && (InCatForm(ctx) || InBearForm(ctx))
                              && ctx.HasEnemyTarget
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite || Surrounding(ctx) >= s.BearCount.Value))
                 .OffGcd();

        /// <summary>A target a bleed can't stick to (Rake / Rip are physical bleeds, immune on these types) — one
        /// definition so every cat bleed step skips the same creatures. Mirrors the old AIO's Elemental skip and
        /// RogueCommon.IsBleedImmune.</summary>
        public static bool IsBleedImmune(IWowUnit unit) =>
            unit.CreatureType == "Elemental" || unit.CreatureType == "Mechanical";

        /// <summary>Faerie Fire (Feral) — the -armor debuff, usable in cat or bear. Apply when missing; opt-out via
        /// the toggle. Not while prowling (it would break stealth before the opener). Reuses the boss/elite-aware
        /// MaintainMyDebuff so it isn't re-applied to a dying mob? No — armor debuff helps from the first hit, so we
        /// apply it whenever it's missing, like the old AIO (no HP floor).</summary>
        public static RotationStep FaerieFireFeral(DruidSettings s, float priority) =>
            Skill.Spell("Faerie Fire (Feral)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseFaerieFire.Value && !ctx.Me.HasAura("Prowl")
                              && !ctx.Target.HasMyAura("Faerie Fire (Feral)"));

        /// <summary>Rake — the Cat bleed, applied when missing on a healthy target (the dying-mob HP-floor reuses
        /// the Rip-health setting so a fresh bleed isn't wasted on a mob about to die). Not on bleed-immune
        /// creatures, not while prowling (the opener goes first), and only below the finisher CP threshold (no point
        /// building Rake when a finisher should fire). Routes through MaintainMyDebuff for the shared post-cast
        /// grace (so the freshly applied bleed isn't double-cast before it becomes visible).</summary>
        public static RotationStep Rake(DruidSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Rake", RakeRefreshMs, priority,
                extraGate: ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                                  && ctx.ComboPoints < s.FinisherComboPoints.Value
                                  && ctx.Target.HealthPercent > s.RipHealth.Value
                                  && !IsBleedImmune(ctx.Target));

        /// <summary>Rip — the Cat bleed FINISHER, spent at the finisher CP threshold on a durable target (above the
        /// Rip-health floor — a fresh bleed won't tick out before a low mob dies, so Ferocious Bite gets the points
        /// instead). Not on bleed-immune creatures, not while prowling. Routes through MaintainMyDebuff so it
        /// re-applies when missing/expiring with the shared post-cast grace.</summary>
        public static RotationStep Rip(DruidSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Rip", RipRefreshMs, priority,
                extraGate: ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                                  && ctx.ComboPoints >= s.FinisherComboPoints.Value
                                  && ctx.Target.HealthPercent > s.RipHealth.Value
                                  && !IsBleedImmune(ctx.Target));

        /// <summary>Ferocious Bite — the Cat direct-damage finisher, spent at the finisher CP threshold. Lower
        /// priority than Rip in the spec, so Rip (the bleed) takes the points on durable targets and Ferocious Bite
        /// dumps them otherwise (a dying mob below the Rip floor, or once Rip is already up). Not while prowling.</summary>
        public static RotationStep FerociousBite(DruidSettings s, float priority) =>
            Skill.Spell("Ferocious Bite").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && ctx.ComboPoints >= s.FinisherComboPoints.Value);

        /// <summary>Mangle (Cat) — the primary combo-point builder (also a bleed-damage debuff). Build below the
        /// finisher threshold so we don't overbuild past a finisher-worthy bar. Not while prowling.</summary>
        public static RotationStep MangleCat(DruidSettings s, float priority) =>
            Skill.Spell("Mangle (Cat)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && ctx.ComboPoints < s.FinisherComboPoints.Value);

        /// <summary>Claw — the fallback Cat builder (pre-Mangle, or when Mangle is on cooldown). Lowest cat-builder
        /// priority so Mangle wins; fills the GCD when nothing better wants it. Build below the finisher threshold.
        /// Not while prowling.</summary>
        public static RotationStep Claw(DruidSettings s, float priority) =>
            Skill.Spell("Claw").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && ctx.ComboPoints < s.FinisherComboPoints.Value);

        // --- Bear Form: tank / AoE (rage) ---

        /// <summary>Switch to (Dire) Bear Form — the tank/AoE form. Fires when surrounded (>= BearCount attackers in
        /// melee), AND also as the SINGLE-TARGET form while Cat Form isn't learned yet (level 10-19: the druid has
        /// only Bear, so it fights in bear instead of dropping to the caster filler — Cat takes over single-target
        /// once it's trained at ~20). Prefers Dire Bear Form (the upgrade) when known; Bear Form is the auto-skip
        /// fallback for a lower-level druid (below ~10 neither is known, so it stays a caster). Sits ABOVE the cat
        /// switch so the surrounded check wins when a pack is on us.</summary>
        public static RotationStep BearForm(DruidSettings s, float priority) =>
            new RotationStep(
                name: "Bear Form",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (InBearForm(ctx)) return false;
                    // Not surrounded AND Cat is available → let the Cat switch handle single-target. Otherwise
                    // (surrounded, or no Cat Form yet) the bear is the form to be in.
                    if (Surrounding(ctx) < s.BearCount.Value && ctx.Game.IsSpellKnown("Cat Form")) return false;
                    string form = ctx.Game.IsSpellKnown("Dire Bear Form") ? "Dire Bear Form" : "Bear Form";
                    return ctx.Game.IsSpellKnown(form) && ctx.Game.IsSpellReady(form);
                },
                action: (ctx, t) =>
                    ctx.Game.Cast(ctx.Game.IsSpellKnown("Dire Bear Form") ? "Dire Bear Form" : "Bear Form", ctx.Me));

        /// <summary>Mangle (Bear) — the Bear builder/debuff: apply/maintain the Mangle bleed-damage debuff and deal
        /// threat. Only in bear form. (The Mangle debuff shows as "Mangle"; the old AIO maintained it the same way.)</summary>
        public static RotationStep MangleBear(DruidSettings s, float priority) =>
            Skill.Spell("Mangle (Bear)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InBearForm(ctx) && !ctx.Target.HasMyAura("Mangle"));

        /// <summary>Lacerate — the Bear bleed, stacked and maintained (refresh when missing/expiring). Only in bear
        /// form; auto-skips until learned. Routes through MaintainMyDebuff for the shared post-cast grace.</summary>
        public static RotationStep Lacerate(DruidSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Lacerate", LacerateRefreshMs, priority,
                extraGate: ctx => InBearForm(ctx) && ctx.Target.HealthPercent > s.RipHealth.Value
                                  && !IsBleedImmune(ctx.Target));

        /// <summary>Swipe (Bear) — the Bear AoE, when a pack is in melee (>= BearCount). Only in bear form.</summary>
        public static RotationStep SwipeBear(DruidSettings s, float priority) =>
            Skill.Spell("Swipe (Bear)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InBearForm(ctx) && Surrounding(ctx) >= s.BearCount.Value);

        /// <summary>Maul — the Bear rage dump (an on-next-swing attack). Spend rage above the configured reserve;
        /// guarded so we don't re-queue it every tick. Off the GCD (it's an on-next-hit attack). Only in bear form.</summary>
        public static RotationStep Maul(DruidSettings s, float priority) =>
            Skill.Spell("Maul").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InBearForm(ctx) && ctx.Me.Rage > s.MaulRageReserve.Value
                              && !ctx.Game.IsCurrentSpell("Maul"))
                 .OffGcd();

        /// <summary>Demoralizing Roar — the Bear attack-power debuff (survival). Apply when missing on a target
        /// worth a global (boss/elite or a pack on us), like the warrior's Demoralizing Shout — trash that dies in a
        /// few swings isn't worth it. Also skips a target already carrying Demoralizing Shout (a warrior's). Only in
        /// bear form.</summary>
        public static RotationStep DemoralizingRoar(DruidSettings s, float priority) =>
            Skill.Spell("Demoralizing Roar").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InBearForm(ctx)
                              && !ctx.Target.HasMyAura("Demoralizing Roar")
                              && !ctx.Target.HasAura("Demoralizing Shout")
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite || Surrounding(ctx) >= s.BearCount.Value));

        /// <summary>Enrage — an instant Bear rage generator. Pop it to fuel the rotation when not already enraged
        /// and the target isn't about to die. Off the GCD. Only in bear form.</summary>
        public static RotationStep Enrage(float priority) =>
            Skill.Spell("Enrage").Priority(priority).On(Targets.Self)
                 .When(ctx => InBearForm(ctx) && !ctx.Me.HasAura("Enrage")
                              && ctx.HasEnemyTarget && ctx.Target.HealthPercent >= EnrageMinTargetHealth)
                 .OffGcd();

        /// <summary>Frenzied Regeneration — the Bear survival cooldown (converts rage to health). Fire below the
        /// configured bear-survival health % when we have rage to convert. Off the GCD. Only in bear form.</summary>
        public static RotationStep FrenziedRegeneration(DruidSettings s, float priority) =>
            Skill.Spell("Frenzied Regeneration").Priority(priority).On(Targets.Self)
                 .When(ctx => InBearForm(ctx)
                              && ctx.Me.HealthPercent < FrenziedRegenHealthPercent
                              && ctx.Me.Rage > FrenziedRegenMinRage)
                 .OffGcd();

        // --- named constants (no magic numbers) ---

        // Bleed/debuff refresh windows: re-apply when under this many ms remain. Routed through MaintainMyDebuff,
        // which adds the shared post-cast grace so the apply-latency double-cast can't happen.
        private const int RakeRefreshMs = 2000;     // Rake lasts ~9s
        private const int RipRefreshMs = 2000;      // Rip lasts ~12-16s
        private const int LacerateRefreshMs = 3000; // Lacerate lasts ~15s (refresh the stack)

        /// <summary>Don't pop Enrage on a mob about to die (HP%) — the rage isn't worth it. Mirrors the old AIO's
        /// <c>t.HealthPercent >= 35</c> Enrage gate.</summary>
        private const int EnrageMinTargetHealth = 35;

        /// <summary>Frenzied Regeneration fires below this health % (the old AIO's bear self-heal trigger).</summary>
        private const int FrenziedRegenHealthPercent = 60;

        /// <summary>Frenzied Regeneration needs at least this much rage to convert into health.</summary>
        private const int FrenziedRegenMinRage = 25;
    }
}
