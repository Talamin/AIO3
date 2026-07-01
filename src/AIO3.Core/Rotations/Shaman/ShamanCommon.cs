using System;
using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;

namespace AIO3.Core.Rotations.Shaman
{
    /// <summary>
    /// Shared shaman building blocks — the baseline both solo specs compose, so the cross-cutting behaviour
    /// (the four-school totem upkeep + the situational/temporary totems, weapon imbues, the self-shield, the
    /// self-heal, the shocks, Wind Shear, the Maelstrom proc, Bloodlust) is written ONCE and stays consistent.
    /// Each block returns a ready RotationStep; the spec lists them in priority order alongside its signature
    /// abilities.
    ///
    /// Totems are modelled as INDIVIDUAL rotation steps on top of the one totem seam, <see cref="CombatContext.Totems"/>
    /// (the player's own active totems, each with Name + Distance). A school's totem is "up and useful" iff one of
    /// its totems is in that list within <see cref="TotemUsefulRange"/> (a totem left behind past that is gone — the
    /// game enforces one totem per school slot, so dropping a fresh one replaces the distant one). The drop step
    /// fires when the chosen school totem is NOT up-and-useful, gated on <see cref="Fighting"/> + stationary +
    /// not mounted (totems are stationary — never drop mid-run). No new seam is needed: weapon imbues read the
    /// existing <see cref="IGameClient.GetWeaponEnchant"/> presence.
    /// </summary>
    public static class ShamanCommon
    {
        // --- named constants (no magic numbers) ---

        /// <summary>A totem within this many yards of us still counts as "up and useful" for its school. A totem
        /// left behind past this is effectively gone (its aura no longer reaches us / its damage no longer reaches
        /// the fight), so the drop step fires to plant a fresh one — which the game slots into the same school,
        /// replacing the distant totem.</summary>
        public const float TotemUsefulRange = 30f;

        /// <summary>Beyond this distance a totem is "left behind" enough to bother with Totemic Recall (cleanup).
        /// Wider than <see cref="TotemUsefulRange"/> so recall is a last resort, not a churn.</summary>
        public const float TotemRecallRange = 40f;

        /// <summary>The redeploy fire totem (Magma/Searing) and the situational fire drops only matter when
        /// the target is within this range — there's no point planting a stationary damage totem if the mob is far.</summary>
        public const float FireTotemTargetRange = 15f;

        /// <summary>Radius around the player that counts as "an enemy is on me" for the defensive totems
        /// (Earth Elemental / Stoneclaw) and the surrounded count. Also the radius the Enhancement Feral-Spirit
        /// attacker count uses (the old FC counted enemies within 20y of the target).</summary>
        public const float SurroundRadius = 20f;

        /// <summary>The target-anchored "pack" radius for the splash/AoE nukes (Chain Lightning, Fire Nova) — the
        /// old FC's 10y enemy count around the target. One source so both specs agree on what a "pack" is.</summary>
        public const float PackRadius = 10f;

        /// <summary>Earthbind snares runners within this range (humanoid that may flee).</summary>
        public const float EarthbindRange = 10f;

        /// <summary>An enemy caster within this range is worth a Grounding Totem (it absorbs the next spell).</summary>
        public const float GroundingRange = 30f;

        /// <summary>Maelstrom Weapon at this many stacks → the instant Lightning Bolt / Chain Lightning is free.
        /// The aura is buff id 53817 in the old FC; we key on the NAME here — VERIFY IN GAME that "Maelstrom Weapon"
        /// is the readable aura name (it is on 3.3.5a) before trusting this in production.</summary>
        public const int MaelstromFullStacks = 5;

        /// <summary>Don't re-issue a totem drop for this long after a successful one — totems take a tick to show up
        /// in the snapshot (server round-trip), so without this throttle the same school would be dropped twice.</summary>
        private const int TotemDropGraceMs = 1500;

        // --- school → ordered totem-name preference, per spec (from the old Totems.cs DefaultTotems) ---

        private static readonly string[] FireEnh = { "Magma Totem", "Searing Totem", "Flametongue Totem" };
        private static readonly string[] FireEle = { "Totem of Wrath", "Flametongue Totem" };
        private static readonly string[] EarthEnh = { "Strength of Earth Totem", "Stoneskin Totem" };
        private static readonly string[] EarthEle = { "Stoneskin Totem", "Strength of Earth Totem" };
        private static readonly string[] WaterEnh = { "Healing Stream Totem", "Mana Spring Totem" };
        private static readonly string[] WaterEle = { "Mana Spring Totem", "Healing Stream Totem" };
        private static readonly string[] AirEnh = { "Windfury Totem", "Wrath of Air Totem" };
        private static readonly string[] AirEle = { "Wrath of Air Totem", "Windfury Totem" };

        /// <summary>The four schools, used to test "is ANY totem of this school up" so a forced/specific choice and
        /// the auto pick share one definition of the school. <see cref="FireTotemNames"/> exposes the fire roster so
        /// the spec's Fire Nova gate reuses it instead of keeping a second copy.</summary>
        private static readonly string[] AllFire = { "Magma Totem", "Searing Totem", "Flametongue Totem", "Totem of Wrath" };

        /// <summary>Every fire totem name (the school the Enhancement Fire Nova fires off). One source for the
        /// roster so adding a fire totem is a single edit. Returns the shared array (do not mutate).</summary>
        public static string[] FireTotemNames => AllFire;
        private static readonly string[] AllEarth = { "Strength of Earth Totem", "Stoneskin Totem", "Tremor Totem" };
        private static readonly string[] AllWater = { "Healing Stream Totem", "Mana Spring Totem", "Cleansing Totem" };
        private static readonly string[] AllAir = { "Windfury Totem", "Wrath of Air Totem", "Nature Resistance Totem" };

        public enum TotemSchool { Fire, Earth, Water, Air }

        // --- shared facts ---

        /// <summary>True only when we're actually engaging a fight — the product has committed (its fight state is
        /// set during the APPROACH too) or we're already in combat. The totem-drop steps gate on this (mirroring
        /// the old <c>Fight.InFight</c>) so the shaman drops totems ONLY to fight, not while idle / travelling.
        /// Same helper the druid uses to gate form entry.</summary>
        public static bool Fighting(CombatContext ctx) => ctx.Game.ProductIsFighting || ctx.Game.PlayerInCombat;

        /// <summary>True when offensive spells may fire: we're not reserving mana for heals, or we're already above
        /// the reserve. One definition so every nuke step agrees on the mana gate (the old SoloEnhancement gated
        /// every offensive step on SoloEnhancementManaSavedForHeals).</summary>
        public static bool ManaForOffense(CombatContext ctx, ShamanSettings s) =>
            s.ManaSavedForHeals.Value <= 0 || ctx.Me.PowerPercent >= s.ManaSavedForHeals.Value;

        /// <summary>True when at least <see cref="MaelstromFullStacks"/> Maelstrom Weapon stacks are up — the instant
        /// Lightning Bolt / Chain Lightning window for Enhancement.</summary>
        public static bool MaelstromReady(CombatContext ctx) =>
            ctx.Me.AuraStacks("Maelstrom Weapon") >= MaelstromFullStacks;

        /// <summary>Enemies on us within <see cref="SurroundRadius"/> (drives the defensive totems).</summary>
        public static int Surrounding(CombatContext ctx) =>
            ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= SurroundRadius);

        /// <summary>True if a totem from <paramref name="names"/> is up AND within <see cref="TotemUsefulRange"/>.
        /// The "up and useful" check the drop steps invert.</summary>
        public static bool TotemUpAndUseful(CombatContext ctx, params string[] names) =>
            ctx.Totems.Any(t => t.Distance <= TotemUsefulRange && names.Any(n => t.Name.Contains(n)));

        /// <summary>True if a totem from <paramref name="names"/> is present at ANY distance (used by recall to
        /// know a school slot is occupied, and by the temporary-totem "don't recall while one is up" guard).</summary>
        public static bool TotemPresent(CombatContext ctx, params string[] names) =>
            ctx.Totems.Any(t => names.Any(n => t.Name.Contains(n)));

        // --- totem school resolution (Auto = spec default, None = skip, a specific name = force it) ---

        private static string[] SchoolNames(TotemSchool school) =>
            school == TotemSchool.Fire ? AllFire
            : school == TotemSchool.Earth ? AllEarth
            : school == TotemSchool.Water ? AllWater
            : AllAir;

        private static string[] SchoolPreference(ShamanSpec spec, TotemSchool school)
        {
            bool enh = spec == ShamanSpec.Enhancement;
            switch (school)
            {
                case TotemSchool.Fire: return enh ? FireEnh : FireEle;
                case TotemSchool.Earth: return enh ? EarthEnh : EarthEle;
                case TotemSchool.Water: return enh ? WaterEnh : WaterEle;
                default: return enh ? AirEnh : AirEle;
            }
        }

        private static string SchoolChoice(ShamanSettings s, TotemSchool school) =>
            school == TotemSchool.Fire ? s.FireTotem.Value
            : school == TotemSchool.Earth ? s.EarthTotem.Value
            : school == TotemSchool.Water ? s.WaterTotem.Value
            : s.AirTotem.Value;

        /// <summary>The totem to drop for a school given the per-school setting and the spec default: the chosen
        /// totem when forced (and known), else the first KNOWN totem in the spec's preference order (so the choice
        /// scales by level). Returns null for "None" or when nothing in the school is known yet.</summary>
        public static string ResolveSchoolTotem(CombatContext ctx, ShamanSettings s, ShamanSpec spec, TotemSchool school)
        {
            string choice = SchoolChoice(s, school);
            if (choice == "None") return null;
            if (choice != "Auto")
                return ctx.Game.IsSpellKnown(choice) ? choice : null;

            foreach (string name in SchoolPreference(spec, school))
                if (ctx.Game.IsSpellKnown(name)) return name;
            return null;
        }

        /// <summary>The school totem-drop step: drop the chosen school totem when it isn't up-and-useful. Gated on
        /// engaging a fight + stationary + not mounted (totems are stationary; never drop mid-run). A successful
        /// drop is throttled so the same school isn't planted twice before it appears in the snapshot.</summary>
        public static RotationStep DropSchoolTotem(ShamanSettings s, ShamanSpec spec, TotemSchool school, float priority) =>
            new RotationStep(
                name: "Totem: " + school,
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (!Fighting(ctx) || ctx.Game.PlayerIsMoving || ctx.Game.PlayerIsMounted) return false;
                    string totem = ResolveSchoolTotem(ctx, s, spec, school);
                    if (totem == null) return false;
                    if (!ctx.Game.IsSpellKnown(totem) || !ctx.Game.IsSpellReady(totem)) return false;
                    // Up-and-useful if ANY totem of this school is in range — a forced choice that differs from
                    // what's planted still re-drops, but an Auto pick won't churn when the school slot is filled.
                    return !TotemUpAndUseful(ctx, SchoolNames(school));
                },
                action: (ctx, t) =>
                {
                    string totem = ResolveSchoolTotem(ctx, s, spec, school);
                    return totem != null ? ctx.Game.Cast(totem, ctx.Me) : CastResult.Failed;
                },
                recastDelayMs: TotemDropGraceMs);

        /// <summary>Totemic Recall — cleanup: recall when a totem is left behind beyond <see cref="TotemRecallRange"/>
        /// and no temporary/situational totem is up (recalling would pull those too). Out of combat / not moving so
        /// it doesn't fight the re-drop. Individual re-drops already handle the functional re-set, so this is purely
        /// tidy-up of a distant totem the game would otherwise leave standing.</summary>
        public static RotationStep TotemicRecall(ShamanSettings s, float priority) =>
            Skill.Spell("Totemic Recall").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseTotemicRecall.Value && !ctx.Game.PlayerIsMounted && !ctx.Game.PlayerInCombat
                              && ctx.Totems.Any(t => t.Distance > TotemRecallRange)
                              && !HasTemporaryTotem(ctx))
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>True if any temporary/situational totem is up (the old HasTemporary). Recall holds while one is
        /// up so it doesn't pull the Earth Elemental / Mana Tide / Stoneclaw the situational steps just dropped.</summary>
        private static bool HasTemporaryTotem(CombatContext ctx) =>
            TotemPresent(ctx, "Mana Tide Totem", "Earth Elemental Totem", "Stoneclaw Totem",
                              "Grounding Totem", "Earthbind Totem", "Tremor Totem");

        // --- situational / temporary totems (port of the old CombatBuffs.cs conditions) ---

        /// <summary>Mana Tide Totem when mana is low (in combat). A big burst-regen cooldown.</summary>
        public static RotationStep ManaTide(ShamanSettings s, float priority) =>
            Skill.Spell("Mana Tide Totem").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseManaTide.Value && ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMounted
                              && ctx.Me.PowerPercent <= s.ManaTideManaPercent.Value)
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>Earth Elemental Totem — the solo defensive cooldown: several enemies on us and Stoneclaw isn't
        /// up. A big add-tank. Gated on cooldowns + the toggle.</summary>
        public static RotationStep EarthElemental(ShamanSettings s, float priority) =>
            Skill.Spell("Earth Elemental Totem").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseEarthElemental.Value && s.UseCooldowns.Value
                              && ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMoving && !ctx.Game.PlayerIsMounted
                              && !TotemPresent(ctx, "Stoneclaw Totem")
                              && Surrounding(ctx) >= EarthElementalPackSize)
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>Stoneclaw Totem — a cheap absorb/taunt totem when 2+ enemies are on us and the Earth Elemental
        /// isn't up. The lighter defensive than the Earth Elemental.</summary>
        public static RotationStep Stoneclaw(ShamanSettings s, float priority) =>
            Skill.Spell("Stoneclaw Totem").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseStoneclaw.Value
                              && ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMoving && !ctx.Game.PlayerIsMounted
                              && !TotemPresent(ctx, "Earth Elemental Totem")
                              && Surrounding(ctx) >= StoneclawPackSize)
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>Grounding Totem — absorbs the next spell from a nearby enemy caster.</summary>
        public static RotationStep Grounding(ShamanSettings s, float priority) =>
            Skill.Spell("Grounding Totem").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseGroundingTotem.Value && ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMounted
                              && !TotemPresent(ctx, "Grounding Totem")
                              && ctx.Enemies.Any(e => e.IsCasting && e.Distance <= GroundingRange))
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>Earthbind Totem — snares a humanoid runner near us (kiting prevention). Off by default.</summary>
        public static RotationStep Earthbind(ShamanSettings s, float priority) =>
            Skill.Spell("Earthbind Totem").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseEarthbindTotem.Value && ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMounted
                              && !TotemPresent(ctx, "Earthbind Totem")
                              && ctx.Enemies.Any(e => e.Distance <= EarthbindRange && e.CreatureType == "Humanoid"))
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>Cleansing Totem — pulses a poison/disease cleanse on the party. Only when a party member carries
        /// one (the player's own debuffs use PlayerHasDebuffType; the party case is the totem's reason to exist).</summary>
        public static RotationStep Cleansing(ShamanSettings s, float priority) =>
            Skill.Spell("Cleansing Totem").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCleansingTotem.Value && ctx.Game.PlayerInCombat && !ctx.Game.PlayerIsMounted
                              && !TotemPresent(ctx, "Cleansing Totem")
                              && (ctx.Game.PlayerHasDebuffType("Poison") || ctx.Game.PlayerHasDebuffType("Disease")))
                 .RecastDelay(TotemDropGraceMs);

        /// <summary>Redeploy a fire totem in combat: target in range and no fire totem up. Re-drops Magma if known
        /// (its pulsing AoE is the Enhancement filler), else Searing. Mirrors the old Magma/Searing redeploy steps.
        /// Distinct from the standard school drop because it gates on the TARGET being close (a damage totem is
        /// wasted if the mob is far) and on its own toggle. Auto-skips until a fire totem is learned.</summary>
        public static RotationStep RedeployFire(ShamanSettings s, float priority) =>
            new RotationStep(
                name: "Redeploy fire totem",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (!s.RedeployFireTotem.Value || !ctx.Game.PlayerInCombat
                        || ctx.Game.PlayerIsMoving || ctx.Game.PlayerIsMounted) return false;
                    if (!ctx.HasEnemyTarget || ctx.Target.Distance > FireTotemTargetRange) return false;
                    if (TotemUpAndUseful(ctx, AllFire)) return false;
                    return ctx.Game.IsSpellKnown(RedeployFireTotemName(ctx));
                },
                action: (ctx, t) => ctx.Game.Cast(RedeployFireTotemName(ctx), ctx.Me),
                recastDelayMs: TotemDropGraceMs);

        private static string RedeployFireTotemName(CombatContext ctx) =>
            ctx.Game.IsSpellKnown("Magma Totem") ? "Magma Totem" : "Searing Totem";

        // --- self-shield (Water / Lightning) ---

        /// <summary>Keep a self-shield up. Auto resolves to Lightning Shield for Elemental and, for Enhancement,
        /// Water Shield while low on mana (for the regen) else Lightning Shield. A forced choice (or "None") wins.
        /// One Exclusive token so the two shields never fight for the slot.</summary>
        public static RotationStep Shield(ShamanSettings s, ShamanSpec spec, float priority)
        {
            var shieldSlot = new Exclusive("ShamanShield");
            return new RotationStep(
                name: "Self shield",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (ctx.Game.PlayerIsMounted) return false;
                    string shield = ResolveShield(ctx, s, spec);
                    return shield != null && !ctx.Me.HasAura(shield)
                           && ctx.Game.IsSpellKnown(shield) && ctx.Game.IsSpellReady(shield);
                },
                action: (ctx, t) =>
                {
                    string shield = ResolveShield(ctx, s, spec);
                    return shield != null ? ctx.Game.Cast(shield, ctx.Me) : CastResult.Failed;
                },
                exclusive: shieldSlot);
        }

        private static string ResolveShield(CombatContext ctx, ShamanSettings s, ShamanSpec spec)
        {
            string choice = s.ShieldChoice.Value;
            if (choice == "None") return null;
            if (choice == "Water Shield" || choice == "Lightning Shield")
                return ctx.Game.IsSpellKnown(choice) ? choice : Fallback(ctx, choice);

            // Auto: Enhancement prefers Water Shield when low on mana (its regen), else Lightning Shield for the
            // melee damage; Elemental wants Lightning Shield (the caster damage on hit) and falls back to Water.
            if (spec == ShamanSpec.Enhancement)
            {
                bool wantWater = ctx.Me.PowerPercent <= s.EnhancementWaterShieldManaPercent.Value
                                 || !ctx.Game.IsSpellKnown("Lightning Shield");
                string preferred = wantWater ? "Water Shield" : "Lightning Shield";
                return ctx.Game.IsSpellKnown(preferred) ? preferred : Fallback(ctx, preferred);
            }
            return ctx.Game.IsSpellKnown("Lightning Shield") ? "Lightning Shield" : Fallback(ctx, "Lightning Shield");
        }

        private static string Fallback(CombatContext ctx, string preferred)
        {
            string other = preferred == "Water Shield" ? "Lightning Shield" : "Water Shield";
            return ctx.Game.IsSpellKnown(other) ? other : null;
        }

        // --- weapon imbues (read via the existing GetWeaponEnchant presence seam) ---

        /// <summary>Keep weapon imbues up: re-cast the spec's main-hand imbue when the main hand is unenchanted, and
        /// (Enhancement) the off-hand imbue when an off-hand weapon is equipped but unenchanted. Reads only the
        /// PRESENCE of a temp-enchant per hand (the GetWeaponEnchant seam) — "is this hand imbued" — which is all we
        /// need. One step; resolves the imbue per spec/level so a typo lives in one place.</summary>
        public static RotationStep WeaponImbue(ShamanSettings s, ShamanSpec spec, float priority) =>
            new RotationStep(
                name: "Weapon imbue",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) => s.UseWeaponImbues.Value && !ctx.Game.PlayerIsMounted && NextImbue(ctx, spec) != null,
                action: (ctx, t) =>
                {
                    string imbue = NextImbue(ctx, spec);
                    // ImbueWeapon (not Cast): it also confirms the "replace weapon enchant" popup — without that the
                    // imbue never lands and this step re-casts forever (the Rockbiter-spam bug).
                    return imbue != null ? ctx.Game.ImbueWeapon(imbue) : CastResult.Failed;
                },
                recastDelayMs: TotemDropGraceMs);

        /// <summary>The imbue to (re-)apply this tick, or null when both needed hands are imbued. Main-hand first
        /// (the priority hand), then the Enhancement off-hand. Main-hand needs the imbue when it holds a weapon
        /// (<c>MainHandEquipped</c>) whose enchant has lapsed (<c>MainHandRemainingMs == 0</c>). The OFF-hand is gated
        /// on <see cref="IGameClient.OffHandHasWeapon"/> (a real weapon, NOT a shield) — a weapon imbue can't enchant a
        /// shield, so a shield off-hand would read "unenchanted" forever and re-cast the imbue in a loop. Mirrors the
        /// old EnchantStep: Enhancement = Windfury main (Rockbiter fallback) + Flametongue off; Elemental = Flametongue
        /// main only.</summary>
        private static string NextImbue(CombatContext ctx, ShamanSpec spec)
        {
            WeaponEnchant w = ctx.Game.GetWeaponEnchant();

            if (w.MainHandEquipped && w.MainHandRemainingMs == 0)
            {
                string main = MainImbue(ctx, spec);
                if (main != null && ctx.Game.IsSpellKnown(main)) return main;
            }
            // Off-hand: only imbue an actual WEAPON (OffHandHasWeapon) — never a shield / held item, else the off-hand
            // reads "unenchanted" forever and we re-cast the imbue in a loop (the Rockbiter-spam bug on a shield user).
            if (spec == ShamanSpec.Enhancement && ctx.Game.OffHandHasWeapon && w.OffHandRemainingMs == 0)
            {
                string off = OffImbue(ctx);
                if (off != null && ctx.Game.IsSpellKnown(off)) return off;
            }
            return null;
        }

        private static string MainImbue(CombatContext ctx, ShamanSpec spec)
        {
            if (spec == ShamanSpec.Enhancement)
                return ctx.Game.IsSpellKnown("Windfury Weapon") ? "Windfury Weapon"
                     : ctx.Game.IsSpellKnown("Rockbiter Weapon") ? "Rockbiter Weapon"
                     : ctx.Game.IsSpellKnown("Flametongue Weapon") ? "Flametongue Weapon" : null;
            // Elemental (and the fallback): Flametongue main-hand (spell power), Rockbiter pre-Flametongue.
            return ctx.Game.IsSpellKnown("Flametongue Weapon") ? "Flametongue Weapon"
                 : ctx.Game.IsSpellKnown("Rockbiter Weapon") ? "Rockbiter Weapon" : null;
        }

        private static string OffImbue(CombatContext ctx) =>
            ctx.Game.IsSpellKnown("Flametongue Weapon") ? "Flametongue Weapon"
            : ctx.Game.IsSpellKnown("Rockbiter Weapon") ? "Rockbiter Weapon" : null;

        // --- self-heal ---

        /// <summary>In-combat / out self-heal: Healing Wave (or Lesser Healing Wave) below the configured health %,
        /// skipped when the current target is nearly dead (finish it instead). Solo: never re-heal at full. When
        /// <paramref name="requireStationary"/> the heal also gates on <c>!PlayerIsMoving</c> — Healing Wave is a
        /// cast-time spell, so the caster Elemental must stand still; Enhancement (in melee anyway) passes false.</summary>
        public static RotationStep SelfHeal(ShamanSettings s, string spell, float priority, bool requireStationary = false) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => s.SelfHealHealthPercent.Value > 0
                              && ctx.Me.HealthPercent < s.SelfHealHealthPercent.Value
                              && (!requireStationary || !ctx.Game.PlayerIsMoving)
                              && (!ctx.HasEnemyTarget
                                  || ctx.Target.HealthPercent > s.SelfHealSkipEnemyHealthPercent.Value));

        // --- interrupt ---

        /// <summary>Wind Shear interrupt (Smart mode learns what's interruptible; Never when disabled).</summary>
        public static RotationStep WindShear(ShamanSettings s, float priority) =>
            CombatBlocks.Interrupt("Wind Shear", priority,
                ctx => s.InterruptCasts.Value ? InterruptModes.Smart : InterruptModes.Never);

        // --- shocks (shared by both specs; the spec picks the priority) ---

        /// <summary>Flame Shock — maintain the DoT on a durable target (above the dying-mob floor — the DyingFloor —
        /// so we don't refresh on a mob about to die). Routes through MaintainMyDebuff for the shared post-cast
        /// grace. The mana-reserve gate is added by the spec via <paramref name="extraGate"/>.</summary>
        public static RotationStep FlameShock(ShamanSettings s, float priority, Func<CombatContext, bool> extraGate = null) =>
            CombatBlocks.MaintainMyDebuff("Flame Shock", FlameShockRefreshMs, priority,
                extraGate: ctx => (extraGate == null || extraGate(ctx))
                                  && ctx.HasEnemyTarget
                                  && ctx.Target.HealthPercent > s.FlameShockMinTargetHealth.Value);

        // --- Bloodlust / Heroism (burst cooldown, faction-correct name via IsSpellKnown) ---

        /// <summary>Bloodlust (Horde) / Heroism (Alliance) — the burst haste cooldown. We list BOTH names; each is
        /// gated by IsSpellKnown, so only the one the character actually has fires (faction-correct without reading
        /// faction). On a boss/elite/pack when cooldowns + the Bloodlust toggle are on. Shares the long
        /// Sated/Exhaustion debuff, so it's off by default and only on a real fight.</summary>
        public static RotationStep Bloodlust(ShamanSettings s, string spell, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseBloodlust.Value && s.UseCooldowns.Value && IsBigFight(ctx)
                              && !ctx.Me.HasAura("Bloodlust") && !ctx.Me.HasAura("Heroism")
                              && !ctx.Me.HasAura("Sated") && !ctx.Me.HasAura("Exhaustion"));

        /// <summary>A fight worth a major cooldown: a boss, an elite, or a pack on/near us. Includes HasEnemyTarget
        /// so a Self-cast cooldown gated on this never dereferences a null target.</summary>
        public static bool IsBigFight(CombatContext ctx) =>
            ctx.HasEnemyTarget && (ctx.Target.IsBoss() || ctx.Target.IsElite || Surrounding(ctx) >= BigFightPackSize);

        // --- WithSituationalTotems: append the situational totem band to a spec's list in one call ---

        /// <summary>Append the situational/temporary totems + Totemic Recall to a spec's step list (one call, like
        /// the mage's WithConjure). Mana Tide and the defensives sit highest (survival/mana), then the toggleable
        /// utility totems, then the fire redeploy, then recall last. Priorities sit in the totem band (~1.4-1.6),
        /// below survival/interrupt but able to fire alongside the rotation.</summary>
        public static List<RotationStep> WithSituationalTotems(ShamanSettings s, List<RotationStep> steps)
        {
            steps.Add(ManaTide(s, priority: 1.40f));
            steps.Add(EarthElemental(s, priority: 1.42f));
            steps.Add(Stoneclaw(s, priority: 1.44f));
            steps.Add(Grounding(s, priority: 1.46f));
            steps.Add(Earthbind(s, priority: 1.48f));
            steps.Add(Cleansing(s, priority: 1.50f));
            steps.Add(RedeployFire(s, priority: 1.55f));
            steps.Add(TotemicRecall(s, priority: 1.60f));
            return steps;
        }

        /// <summary>Append the four standard school totem drops to a spec's list in the totem band (fire early —
        /// it's damage — then earth/water/air). Below survival/interrupt; tuned per the brief.</summary>
        public static List<RotationStep> WithSchoolTotems(ShamanSettings s, ShamanSpec spec, List<RotationStep> steps)
        {
            steps.Add(DropSchoolTotem(s, spec, TotemSchool.Fire, priority: 2.50f));
            steps.Add(DropSchoolTotem(s, spec, TotemSchool.Earth, priority: 2.60f));
            steps.Add(DropSchoolTotem(s, spec, TotemSchool.Water, priority: 2.70f));
            steps.Add(DropSchoolTotem(s, spec, TotemSchool.Air, priority: 2.75f));
            return steps;
        }

        // --- named constants (counts / refresh windows) ---

        private const int FlameShockRefreshMs = 2000;   // Flame Shock lasts ~18-21s
        private const int EarthElementalPackSize = 3;   // old: >= 3 enemies on us
        private const int StoneclawPackSize = 2;        // old: >= 2 enemies on us
        private const int BigFightPackSize = 2;         // a "pack" worth a major cooldown
    }
}
