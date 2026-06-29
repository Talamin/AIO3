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
        /// <summary>An attacker within this range counts as "in melee" on us — the trigger for the bear AoE gates
        /// (Swipe / Demoralizing Roar) and the surrounded count. One named constant so every TIGHT melee gate uses
        /// the same radius. Named distinctly from <see cref="DruidSettings.MeleeRange"/> (the WRobot
        /// ICustomClass.Range), which is an unrelated concept.</summary>
        public const float SurroundRadius = 8f;

        /// <summary>The WIDER, target-anchored radius that drives the Cat/Bear FORM decision (mirrors the old AIO's
        /// 12-20y enemy-count radius for the form switch — SoloFeral.cs:38,48). A pack about to land on us is
        /// counted around the TARGET (not the tight 8y player-melee radius), so the bear switch fires a beat before
        /// every mob is already on top of us. Kept separate from <see cref="SurroundRadius"/> so the Swipe/AoE gates
        /// stay tight while the form decision sees the whole approaching pack.</summary>
        public const float FormDecisionRadius = 18f;

        /// <summary>Below this distance the target is essentially in melee, so the Growl pull is pointless — just hit
        /// it. Above it, Growl is worth casting to drag the mob in (WRobot enforces Growl's actual ~30y cap, so an
        /// out-of-range cast just fails harmlessly and retries as the bot closes).</summary>
        private const float GrowlPullMinRange = 8f;

        /// <summary>After a Growl pull lands, don't re-taunt for this long (a taunt-immune mob would otherwise be
        /// spammed while the bot closes the gap). Only set on a successful cast, so an out-of-range miss still retries.</summary>
        private const int GrowlPullRecastMs = 3000;

        // --- form facts (one definition each, so every step agrees) ---

        /// <summary>True only when we're actually engaging a fight — the product has committed (its fight state is
        /// set during the APPROACH too, before the combat flag) or we're already in combat. The form-entry steps gate
        /// on this so the druid shapeshifts ONLY to fight: walking to a flight master / vendor / quest NPC it stays in
        /// caster form instead of re-shifting into Bear/Cat every tick (which blocks mounting, boarding a taxi, and
        /// NPC gossip). The rotation otherwise ticks every pulse out of combat (for the OOC buffs), so without this
        /// gate the form steps fire whenever idle — e.g. Cat fires at 0 enemies (<c>0 &lt; BearCount</c>).</summary>
        public static bool Fighting(CombatContext ctx) => ctx.Game.ProductIsFighting || ctx.Game.PlayerInCombat;

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

        /// <summary>True once the feral druid has learned a melee combat form (Cat / Bear / Dire Bear). The pre-form
        /// caster fallback (Moonfire / Wrath) is gated on the NEGATION of this: only a druid that CAN'T yet shapeshift
        /// fills with caster nukes. A form-capable druid must always shift into its form instead of standing and
        /// casting Wrath (which is what the bare <c>!InAnyForm</c> gate wrongly allowed during a form-held window).</summary>
        public static bool KnowsCombatForm(CombatContext ctx) =>
            ctx.Game.IsSpellKnown("Cat Form") || ctx.Game.IsSpellKnown("Bear Form")
            || ctx.Game.IsSpellKnown("Dire Bear Form");

        /// <summary>Number of enemies meleeing us within the TIGHT <see cref="SurroundRadius"/> — the "is a pack
        /// actually on me?" count that drives the bear AoE gates (Swipe / Demoralizing Roar). Shared so the AoE
        /// abilities agree on what a "pack on me" is.</summary>
        public static int Surrounding(CombatContext ctx) =>
            ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= SurroundRadius);

        /// <summary>Number of enemies ACTUALLY ATTACKING US (targeting the player) within the wider
        /// <see cref="FormDecisionRadius"/> — the count that drives the Cat/Bear FORM switch. Requiring
        /// <c>IsTargetingMe</c> (not merely "near the target") is what matches the <see cref="DruidSettings.BearCount"/>
        /// setting's documented meaning ("min attackers meleeing you") AND stops the mana-bleeding form thrash: in a
        /// mob-dense grind/quest area the old target-anchored count (<c>EnemiesNearTarget</c>) counted UNRELATED ambient
        /// mobs near the target, so a single-target fight saw >= BearCount and flip-flopped Cat&lt;-&gt;Bear every time the
        /// ambient count crossed the threshold — and every shift costs a feral mana it can't regen in combat. The radius
        /// stays wide (18y) so a pack that has AGGROED us and is running in still counts proactively (switch a beat before
        /// they all land), but a mob the bot isn't fighting no longer drags us into Bear. Same shape as
        /// <see cref="Surrounding"/>, at the wider form-decision radius.</summary>
        public static int FormDecisionCount(CombatContext ctx) =>
            ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= FormDecisionRadius);

        // --- out-of-combat buffs (CombatBlocks.SelfBuff pattern; Gift of the Wild supersedes Mark of the Wild) ---

        /// <summary>Single-stat buffs (from scrolls like Scroll of Protection, or another player) that share Mark of
        /// the Wild's stack group on 3.3.5a cores: MotW can't be applied while one of these is up, so casting it would
        /// loop forever. Mirrors the old AIO's OOCBuffs gate — skip MotW when any of these (or Gift of the Wild) is on.
        /// "Armor" in particular is the Scroll of Protection buff that triggered the endless re-cast.</summary>
        private static readonly string[] MotwSupersedingBuffs =
            { "Gift of the Wild", "Stamina", "Armor", "Agility", "Strength", "Spirit", "Intellect" };

        /// <summary>Keep Mark of the Wild up. Skipped when MotW/Gift of the Wild is already on, OR when a conflicting
        /// single-stat buff (<see cref="MotwSupersedingBuffs"/>, e.g. an "Armor" scroll) occupies the same stack group
        /// — on those cores MotW won't land over them, so re-casting would loop. Out-of-combat only — like the old
        /// AIO's OOCBuffs addon (RunInCombat=false), we don't break the rotation to re-buff mid-fight; the buffs are
        /// long and applied before the pull. Opt-out via the toggle.</summary>
        public static RotationStep MarkOfTheWild(DruidSettings s, float priority) =>
            Skill.Spell("Mark of the Wild").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseMarkOfTheWild.Value && !ctx.Game.PlayerInCombat
                              && !ctx.Me.HasAura("Mark of the Wild")
                              && System.Array.TrueForAll(MotwSupersedingBuffs, b => !ctx.Me.HasAura(b)));

        /// <summary>Keep Thorns up (reflects melee damage). Out-of-combat only (see <see cref="MarkOfTheWild"/>).
        /// Opt-out via the toggle.</summary>
        public static RotationStep Thorns(DruidSettings s, float priority) =>
            Skill.Spell("Thorns").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseThorns.Value && !ctx.Game.PlayerInCombat && !ctx.Me.HasAura("Thorns"));

        /// <summary>Below this distance to the target the druid stops travelling and engages — Travel Form drops so a
        /// single clean shift into Bear/Cat (and the ~30y Faerie Fire / Growl pull) takes over. Above it the target is
        /// still a run-up away, where a mount (here Travel Form) belongs. Mirrors the old shaman FC's MountDistance
        /// gate; keeping it ABOVE melee but within ranged-pull range avoids the Travel↔Bear GCD thrash at the engage.</summary>
        public const float TravelFormDropRange = 20f;

        /// <summary>Travel Form as a ground-mount substitute — the druid's equivalent of the old shaman FC's Ghost
        /// Wolf. While moving on foot, NOT in real combat, with no mount configured, and the target still a run-up away
        /// (no target, or it's beyond <see cref="TravelFormDropRange"/>), shift into Travel Form for the +40% outdoor
        /// speed. The distance gate (not a bare <c>!PlayerInCombat</c>) is what stops the Travel↔Bear thrash: Travel
        /// Form covers the FAR portion of every grind run-up and drops ONCE at ~20y, where the combat-form step makes a
        /// single direct Travel→Bear/Cat switch (no GCD collision). Only while MOVING, so a stop to loot/interact drops
        /// it. Suppressed when a ground mount exists (let WRobot mount it), while mounted, or while resting. Auto-skips
        /// below level 16.</summary>
        public static RotationStep TravelForm(DruidSettings s, float priority) =>
            Skill.Spell("Travel Form").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseTravelForm.Value
                              && ctx.Game.PlayerIsMoving && !ctx.Game.PlayerInCombat
                              && !ctx.Game.PlayerIsMounted && !ctx.Game.HasGroundMount
                              && !ctx.Game.PlayerIsResting
                              && (ctx.Target == null || ctx.Target.Distance > TravelFormDropRange)
                              && !ctx.Me.HasAura("Travel Form"));

        // --- survival (form-agnostic) ---

        /// <summary>Barkskin — off-GCD damage reduction usable in ANY form (it doesn't break shapeshift). Fire it
        /// below the configured health %. The druid's cheap, always-available panic button.</summary>
        public static RotationStep Barkskin(DruidSettings s, float priority) =>
            Skill.Spell("Barkskin").Priority(priority).On(Targets.Self)
                 .When(ctx => s.BarkskinHealthPercent.Value > 0
                              && ctx.Me.HealthPercent < s.BarkskinHealthPercent.Value)
                 .OffGcd();

        /// <summary>Survival Instincts — the Feral max-health emergency cooldown (off-GCD). Only in a combat form
        /// (cat or bear; it's a feral talent), fired below the configured health %. Auto-skips until the talent is
        /// learned, so a non-feral or untalented druid never sees it. The old GroupFeralTank popped it at 35% in
        /// bear; here it covers cat too.</summary>
        public static RotationStep SurvivalInstincts(DruidSettings s, float priority) =>
            Skill.Spell("Survival Instincts").Priority(priority).On(Targets.Self)
                 .When(ctx => s.SurvivalInstinctsHealthPercent.Value > 0
                              && (InCatForm(ctx) || InBearForm(ctx))
                              && ctx.Me.HealthPercent < s.SurvivalInstinctsHealthPercent.Value)
                 .OffGcd();

        /// <summary>Innervate — the mana cooldown, on yourself below the configured mana %. NOT while in cat/bear
        /// form: a feral has no mana to regen mid-fight and casting Innervate would shift it OUT of form for nothing
        /// (the old SoloFeral gated Innervate on being formless — SoloFeral.cs:17). Balance is a caster, never in
        /// cat/bear, so it is unaffected. Solo only (the group "innervate the healer" case is a Group-mode concern,
        /// deferred).</summary>
        public static RotationStep Innervate(DruidSettings s, float priority) =>
            Skill.Spell("Innervate").Priority(priority).On(Targets.Self)
                 .When(ctx => s.InnervateManaPercent.Value > 0
                              && !InCatForm(ctx) && !InBearForm(ctx)
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

        /// <summary>Grace after a shift-out heal lands before its step may re-issue. A cast-time heal (Regrowth ~1.5s)
        /// applies its aura only at cast END + server latency, so without this the maintain check still reads the HoT
        /// as "missing" and casts it 2-3× in a row (the in-game triple-Regrowth). Same idea as the DoT apply-grace.</summary>
        private const int ShiftHealApplyGraceMs = 3000;

        /// <summary>True while an in-combat shift-out heal is WANTED this tick: hurt below the IC-heal threshold, with
        /// the mana to afford it, no instant proc up (that heals in-form instead), AND a chosen HoT (Regrowth /
        /// Rejuvenation) still missing. The last clause is what lets us REFORM once both HoTs are up — we only leave
        /// form to APPLY the missing HoTs, then return to fighting with them ticking. Drives the form-drop step AND
        /// holds the form re-entry, so they don't fight over the GCD (mirrors the priest's WantsHardHeal interlock).</summary>
        public static bool WantsShiftHeal(CombatContext ctx, DruidSettings s)
        {
            if (s.InCombatHealHealthPercent.Value <= 0) return false;
            if (ctx.Me.HealthPercent >= s.InCombatHealHealthPercent.Value) return false;
            if (ctx.Me.PowerPercent <= s.HealManaPercent.Value) return false;
            if (HasInstantHealProc(ctx)) return false; // the instant proc heal keeps form — no shift-out needed
            bool wantRegrowth = s.UseRegrowthIC.Value && !ctx.Me.HasAura("Regrowth");
            bool wantRejuv = s.UseRejuvenationIC.Value && !ctx.Me.HasAura("Rejuvenation");
            return wantRegrowth || wantRejuv;
        }

        /// <summary>The spell to toggle the CURRENT form off (cast a form's own spell while in it → back to caster).</summary>
        private static string ActiveFormSpell(CombatContext ctx)
        {
            if (InCatForm(ctx)) return "Cat Form";
            if (ctx.Me.HasAura("Dire Bear Form")) return "Dire Bear Form";
            if (ctx.Me.HasAura("Bear Form")) return "Bear Form";
            if (InMoonkinForm(ctx)) return "Moonkin Form";
            return null;
        }

        /// <summary>Drop the combat form so a cast-time heal can actually be cast (forms block Regrowth/Rejuvenation —
        /// an instant heal would auto-drop the form, but a cast-time Regrowth started in form spam-fails). Fires when
        /// in a form and a shift-out heal is wanted; one beat, then the formless heals below fire in priority order
        /// (Regrowth, then Rejuvenation), then <see cref="CatForm"/>/<see cref="BearForm"/> re-enter once both HoTs are
        /// up (held meanwhile by <see cref="WantsShiftHeal"/>). The toggle is instant so its aura drops within the
        /// GCD that follows — the form is gone before this step is eligible again, so it can't re-toggle.</summary>
        public static RotationStep DropFormToHeal(DruidSettings s, float priority) =>
            new RotationStep(
                name: "Cancel Form (heal)",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => InAnyForm(ctx) && WantsShiftHeal(ctx, s),
                action: (ctx, t) => ctx.Game.Cast(ActiveFormSpell(ctx), ctx.Me));

        /// <summary>A shift-out in-combat heal, cast ONLY while formless (the <see cref="DropFormToHeal"/> step drops
        /// the form first — a cast-time heal started in form spam-fails). Fires below the IC-heal threshold, gated on
        /// a SIMPLE mana % floor (we deliberately drop the old GetSpellCost arithmetic), and skipped while the
        /// Predator's Swiftness proc is up (the instant version keeps form). Not re-stacked if already on us (a HoT),
        /// and RecastDelay-graced so the cast→aura latency can't double-cast it.</summary>
        public static RotationStep ShiftOutHeal(DruidSettings s, string spell, System.Func<DruidSettings, bool> enabled, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(s)
                              && !InAnyForm(ctx)            // formless only — DropFormToHeal got us here
                              && ctx.Me.HealthPercent < s.InCombatHealHealthPercent.Value
                              && ctx.Me.PowerPercent > s.HealManaPercent.Value
                              && !HasInstantHealProc(ctx)   // prefer the instant proc heal when it's available
                              && !ctx.Me.HasAura(spell))
                 .RecastDelay(ShiftHealApplyGraceMs);

        // --- Cat Form: builders / finishers / cooldowns (energy + combo points) ---

        /// <summary>The bear-switch hysteresis: enter Bear at <c>>= BearCount</c> around the target, but once in
        /// bear DON'T return to cat until the pack drops below <c>BearCount - 1</c> (one fewer than the entry gate).
        /// This makes bear HARDER to leave than to enter, so a pack hovering right at the threshold can't thrash the
        /// druid in and out of form every tick (the old AIO did the same with its -1/-2 cat-switch counts —
        /// SoloFeral.cs:48-49). The cat return floor never goes below 1, so a single straggler can't re-trigger it.</summary>
        public static int BearReturnCount(DruidSettings s) => System.Math.Max(1, s.BearCount.Value - 1);

        /// <summary>Switch to Cat Form for single-target DPS. From a non-bear state, shift to cat whenever the pack
        /// around the target is below the bear ENTRY count (<c>BearCount</c>); from bear, only once it drops below
        /// the lower RETURN count (<see cref="BearReturnCount"/>) — the hysteresis that stops form thrash. Auto-skips
        /// until Cat Form is learned (a low-level druid stays a caster). Sits ABOVE the bear switch in the spec so
        /// the form decision is consistent.</summary>
        public static RotationStep CatForm(DruidSettings s, float priority) =>
            Skill.Spell("Cat Form").Priority(priority).On(Targets.Self)
                 .When(ctx => Fighting(ctx) && !InCatForm(ctx) && !WantsShiftHeal(ctx, s)
                              && FormDecisionCount(ctx) < (InBearForm(ctx) ? BearReturnCount(s) : s.BearCount.Value));

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
        /// prowling (don't break the stealth opener), but ONLY below <see cref="TigersFuryEnergyMax"/> energy so the
        /// instant +energy burst isn't wasted overcapping (the old GroupFeral gated the same way — only when energy
        /// has room to absorb the burst). Off the GCD (instant). Opt-out via the toggle.</summary>
        public static RotationStep TigersFury(DruidSettings s, float priority) =>
            Skill.Spell("Tiger's Fury").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseTigersFury.Value && InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && ctx.Me.Energy < TigersFuryEnergyMax)
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

        /// <summary>Grace after a debuff is applied before its maintain step may re-issue. The debuff lands only at
        /// cast END + server latency, so a bare <c>!HasMyAura</c> maintain re-casts it several times in the gap before
        /// the aura registers (the in-game Faerie Fire applied ~5× in half a second). One cast, then left alone for
        /// the debuff's duration — exactly the "cast it once and leave it" behaviour these long debuffs want.</summary>
        private const int DebuffApplyGraceMs = 1500;

        /// <summary>Faerie Fire (Feral) — the -armor debuff and the bear's ranged threat/"taunt" opener. Apply ONCE
        /// when missing, then leave it (it lasts ~40s); the RecastDelay grace bridges the cast→debuff-apply latency so
        /// it isn't spam-reapplied. Not while prowling (it would break stealth before the opener). No HP floor — the
        /// armor debuff (and the threat) helps from the first hit, like the old AIO.</summary>
        public static RotationStep FaerieFireFeral(DruidSettings s, float priority) =>
            Skill.Spell("Faerie Fire (Feral)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseFaerieFire.Value && !ctx.Me.HasAura("Prowl")
                              && !ctx.Target.HasMyAura("Faerie Fire (Feral)"))
                 .RecastDelay(DebuffApplyGraceMs);

        /// <summary>Growl pull — the bear's ranged opener. A bear has no ranged attack, so until Faerie Fire (Feral)
        /// is learned (or with it toggled off) the bot can only engage in melee. Growl is a ranged taunt: cast on an
        /// out-of-combat target that isn't attacking us yet and is past melee, it aggros the mob and drags it in with
        /// an instant threat lead. Suppressed when Faerie Fire (Feral) is available and on (it's the better pull —
        /// damage + armor debuff, no cooldown), so Growl only fills the gap. Bear Form only (Growl needs a form).
        /// RecastDelay keeps a taunt-immune mob from being re-taunted while the bot closes the distance.</summary>
        public static RotationStep GrowlPull(DruidSettings s, float priority) =>
            Skill.Spell("Growl").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseGrowlPull.Value && InBearForm(ctx)
                              && ctx.Game.ProductIsFighting && !ctx.Game.PlayerInCombat // engaging this target, not yet in melee combat
                              && !ctx.Target.IsTargetingMe && ctx.Target.Distance > GrowlPullMinRange
                              && !(s.UseFaerieFire.Value && ctx.Game.IsSpellKnown("Faerie Fire (Feral)")))
                 .RecastDelay(GrowlPullRecastMs);

        /// <summary>Savage Roar — the cat's "Slice and Dice": a self-buff finisher that boosts melee (white) damage
        /// by ~30-40% while in Cat Form. Spend combo points to KEEP IT UP whenever it's missing at the low
        /// <see cref="SavageRoarComboPoints"/> threshold — it's a near-permanent buff, so we refresh it cheaply
        /// rather than wait for a full bar (the old GroupFeral refreshed it at the finisher CP count, but a low floor
        /// keeps the buff up far more reliably). Highest-priority cat finisher (above Rip/Ferocious Bite) so the buff
        /// never falls off. Opt-out via the toggle; auto-skips until learned. Not while prowling.</summary>
        public static RotationStep SavageRoar(DruidSettings s, float priority) =>
            Skill.Spell("Savage Roar").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseSavageRoar.Value && InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && !ctx.Me.HasAura("Savage Roar")
                              && ctx.ComboPoints >= SavageRoarComboPoints);

        /// <summary>Rake's OWN dying-mob HP-floor (distinct from Rip's). Rake lasts ~9s — much shorter than Rip's
        /// ~16s — so it pays off on lower-HP targets, hence a LOWER floor than Rip. On a boss it pays off down to
        /// almost nothing, so the floor drops further. Mirrors the old AIO's Rake gate
        /// (<c>HealthPercent >= 35 || (>= 20 &amp;&amp; boss)</c> — SoloFeral.cs:63).</summary>
        public static double RakeFloor(CombatContext ctx) =>
            ctx.Target.IsBoss() ? RakeBossFloor : RakeNormalFloor;

        /// <summary>Rake — the Cat bleed, applied when missing on a healthy target (its OWN HP-floor —
        /// <see cref="RakeFloor"/> — is lower than Rip's, and lower still on a boss, since Rake's short duration pays
        /// off on lower-HP mobs). Not on bleed-immune creatures, not while prowling (the opener goes first), and only
        /// below the finisher CP threshold (no point building Rake when a finisher should fire). Routes through
        /// MaintainMyDebuff for the shared post-cast grace (so the freshly applied bleed isn't double-cast before it
        /// becomes visible).</summary>
        public static RotationStep Rake(DruidSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Rake", RakeRefreshMs, priority,
                extraGate: ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                                  && ctx.ComboPoints < s.FinisherComboPoints.Value
                                  && ctx.Target.HealthPercent > RakeFloor(ctx)
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

        /// <summary>Mangle (Cat) maintaining the +30% bleed-damage DEBUFF — the cat counterpart to
        /// <see cref="MangleBear"/>. Casts when the Mangle debuff is missing on the target, so the bleeds Rake/Rip
        /// rely on are amplified. Uses the SAME aura name "Mangle" the bear maintainer keys on — in 3.3.5a both
        /// Mangle (Cat) and Mangle (Bear) apply the same shared "Mangle" debuff (we deliberately DROP the old FC's
        /// <c>"Mangle (Cat)"</c> aura-name check from GroupFeral.cs:66, which was drift). Sits ABOVE the Shred /
        /// Mangle-builder block so the debuff is refreshed first; the builder Mangle below still spams as the energy
        /// filler. Build below the finisher threshold. Not while prowling.</summary>
        public static RotationStep MangleCatDebuff(DruidSettings s, float priority) =>
            Skill.Spell("Mangle (Cat)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && ctx.ComboPoints < s.FinisherComboPoints.Value
                              && !ctx.Target.HasMyAura("Mangle"))
                 .RecastDelay(DebuffApplyGraceMs);

        /// <summary>Shred — the highest-damage Cat combo-point builder, but it can ONLY be used from BEHIND the
        /// target (a positional). Gated on <see cref="IGameClient.PlayerIsBehindTarget"/> (the same seam the rogue's
        /// Garrote opener uses), so it's chosen when behind and the front-fallback builder (Mangle/Claw) takes over
        /// otherwise. Build below the finisher threshold so we don't overbuild past a finisher-worthy bar. Not while
        /// prowling. Auto-skips until learned (a low-level cat builds with Mangle/Claw until Shred is trained).</summary>
        public static RotationStep Shred(DruidSettings s, float priority) =>
            Skill.Spell("Shred").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InCatForm(ctx) && !ctx.Me.HasAura("Prowl")
                              && ctx.ComboPoints < s.FinisherComboPoints.Value
                              && ctx.Game.PlayerIsBehindTarget());

        /// <summary>Mangle (Cat) — the primary front-fallback combo-point builder (also applies the bleed-damage
        /// debuff). Used when Shred can't land (we're in front) or as the steady builder. Build below the finisher
        /// threshold so we don't overbuild past a finisher-worthy bar. Not while prowling.</summary>
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

        /// <summary>Switch to (Dire) Bear Form — the tank/AoE form. Fires when the pack around the target reaches
        /// the bear ENTRY count (<c>>= BearCount</c>, measured at the wider <see cref="FormDecisionRadius"/>), AND
        /// also as the SINGLE-TARGET form while Cat Form isn't learned yet (level 10-19: the druid has only Bear, so
        /// it fights in bear instead of dropping to the caster filler — Cat takes over single-target once it's
        /// trained at ~20). Prefers Dire Bear Form (the upgrade) when known; Bear Form is the auto-skip fallback for
        /// a lower-level druid (below ~10 neither is known, so it stays a caster). Sits ABOVE the cat switch so the
        /// form decision is consistent. The matching CatForm return is hysteretic (see <see cref="BearReturnCount"/>),
        /// so a pack hovering at the threshold can't thrash the form every tick.</summary>
        public static RotationStep BearForm(DruidSettings s, float priority) =>
            new RotationStep(
                name: "Bear Form",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (InBearForm(ctx)) return false;
                    if (!Fighting(ctx)) return false; // only shift to fight — don't block taxis/mounting while idle
                    if (WantsShiftHeal(ctx, s)) return false; // hold the re-shift while we shift out to heal
                    // Below the bear ENTRY count AND Cat is available → let the Cat switch handle single-target.
                    // Otherwise (pack at the entry count, or no Cat Form yet) the bear is the form to be in.
                    if (FormDecisionCount(ctx) < s.BearCount.Value && ctx.Game.IsSpellKnown("Cat Form")) return false;
                    string form = ctx.Game.IsSpellKnown("Dire Bear Form") ? "Dire Bear Form" : "Bear Form";
                    return ctx.Game.IsSpellKnown(form) && ctx.Game.IsSpellReady(form);
                },
                action: (ctx, t) =>
                    ctx.Game.Cast(ctx.Game.IsSpellKnown("Dire Bear Form") ? "Dire Bear Form" : "Bear Form", ctx.Me));

        /// <summary>Mangle (Bear) — the Bear builder/debuff: apply/maintain the Mangle bleed-damage debuff and deal
        /// threat. Only in bear form. (The Mangle debuff shows as "Mangle"; the old AIO maintained it the same way.)</summary>
        public static RotationStep MangleBear(DruidSettings s, float priority) =>
            Skill.Spell("Mangle (Bear)").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => InBearForm(ctx) && !ctx.Target.HasMyAura("Mangle"))
                 .RecastDelay(DebuffApplyGraceMs);

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
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite || Surrounding(ctx) >= s.BearCount.Value))
                 .RecastDelay(DebuffApplyGraceMs);

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

        /// <summary>Rake's dying-mob HP-floor on a NORMAL target: don't apply a fresh ~9s bleed below this (it won't
        /// pay off before the mob dies). Lower than Rip's floor — Rake's short duration pays off on lower-HP targets.</summary>
        private const double RakeNormalFloor = 35;

        /// <summary>Rake's HP-floor on a BOSS: a boss lives long enough that the bleed pays off down to almost
        /// nothing, so keep applying it far lower than on trash (old AIO: 20% on a boss).</summary>
        private const double RakeBossFloor = 20;

        /// <summary>Tiger's Fury only fires below this energy: its instant +energy burst is wasted if we're already
        /// near full, so hold it until energy has room to absorb the gain (the old GroupFeral gated the same way).</summary>
        private const int TigersFuryEnergyMax = 35;

        /// <summary>Savage Roar is refreshed once we have at least this many combo points — a LOW floor (1) so the
        /// melee-damage buff is kept up near-permanently rather than waiting for a full finisher bar.</summary>
        private const int SavageRoarComboPoints = 1;

        /// <summary>Don't pop Enrage on a mob about to die (HP%) — the rage isn't worth it. Mirrors the old AIO's
        /// <c>t.HealthPercent >= 35</c> Enrage gate.</summary>
        private const int EnrageMinTargetHealth = 35;

        /// <summary>Frenzied Regeneration fires below this health % (the old AIO's bear self-heal trigger).</summary>
        private const int FrenziedRegenHealthPercent = 60;

        /// <summary>Frenzied Regeneration needs at least this much rage to convert into health.</summary>
        private const int FrenziedRegenMinRage = 25;
    }
}
