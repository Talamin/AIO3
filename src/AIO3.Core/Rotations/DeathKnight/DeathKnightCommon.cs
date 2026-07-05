using System;
using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;

namespace AIO3.Core.Rotations.DeathKnight
{
    /// <summary>
    /// Shared Death Knight building blocks — the baseline all three solo specs compose, so the cross-cutting
    /// behaviour (the rune-affordability gate, disease upkeep, survival cooldowns, the Presence + Horn of Winter
    /// upkeep, the Death Grip pull, the Mind Freeze interrupt, the ghoul, and the runic-power dumps) is written
    /// ONCE and stays consistent. Each block returns a ready RotationStep; the spec lists them in priority order
    /// alongside its signature strikes.
    ///
    /// THE RUNE GATE (mandatory): AIO3's engine fires ONE step per tick with NO fall-through, and WRobot's
    /// IsSpellReady / IsSpellUsable do NOT reflect rune availability (scout-verified). So a rune-costed ability
    /// that's the highest-priority-eligible step but has no runes would be PICKED and SILENTLY FAIL every tick —
    /// the rotation gets stuck. Therefore EVERY rune-costed step gates its When on <see cref="CanAffordRunes"/>
    /// against its entry in <see cref="RuneCost"/>. A spent Blood/Frost/Unholy rune refreshes into a DEATH rune
    /// that pays for ANY cost, so affordability counts the specific type PLUS the Death pool.
    /// </summary>
    public static class DeathKnightCommon
    {
        // --- rune-cost model (3.3.5a). (blood, frost, unholy) per ability; Death runes cover any deficit. ---

        public readonly struct Cost
        {
            public readonly int Blood, Frost, Unholy;
            public Cost(int blood, int frost, int unholy) { Blood = blood; Frost = frost; Unholy = unholy; }
        }

        /// <summary>Per-ability rune cost (3.3.5a, verified against TrinityCore / Wowpedia). The two formerly
        /// uncertain values are confirmed for WotLK: Scourge Strike = 1 Frost + 1 Unholy (the 1-Unholy-only change
        /// is 4.0.1, post-WotLK); Death and Decay = 1 Blood + 1 Frost + 1 Unholy (one of each). 0-rune abilities
        /// (Frost Strike, Death Coil, Rune Strike, Empower Rune Weapon, Blood Tap, Icebound Fortitude,
        /// Anti-Magic Shell, Vampiric Blood, Summon Gargoyle, Dancing Rune Weapon, Death Grip, Horn of Winter,
        /// presences, Raise Dead) are NOT in this map — they carry no rune gate (runic-power / cooldown only).</summary>
        public static readonly IReadOnlyDictionary<string, Cost> RuneCost = new Dictionary<string, Cost>
        {
            // 1 Frost
            ["Icy Touch"] = new Cost(0, 1, 0),
            ["Howling Blast"] = new Cost(0, 1, 0),
            ["Chains of Ice"] = new Cost(0, 1, 0),
            // 1 Unholy
            ["Plague Strike"] = new Cost(0, 0, 1),
            // 1 Blood
            ["Blood Strike"] = new Cost(1, 0, 0),
            ["Heart Strike"] = new Cost(1, 0, 0),
            ["Pestilence"] = new Cost(1, 0, 0),
            ["Blood Boil"] = new Cost(1, 0, 0),
            ["Rune Tap"] = new Cost(1, 0, 0),
            ["Mark of Blood"] = new Cost(1, 0, 0),
            // 1 Frost + 1 Unholy
            ["Death Strike"] = new Cost(0, 1, 1),
            ["Obliterate"] = new Cost(0, 1, 1),
            ["Scourge Strike"] = new Cost(0, 1, 1),   // 3.3.5a: 1F+1U (verified)
            // 1 Blood + 1 Frost + 1 Unholy
            ["Death and Decay"] = new Cost(1, 1, 1),  // 3.3.5a: one of each (verified)
        };

        /// <summary>True if the player can pay <paramref name="blood"/>/<paramref name="frost"/>/<paramref name="unholy"/>
        /// rune costs right now: each specific type is covered by its ready runes, and any combined shortfall is paid
        /// from the Death-rune pool (a Death rune pays for any cost). The mandatory gate behind every rune ability —
        /// see the class summary for why a missing gate would jam the rotation.</summary>
        public static bool CanAffordRunes(CombatContext ctx, int blood, int frost, int unholy)
        {
            int death = ctx.Game.RunesReady(RuneType.Death);
            int deficit = Math.Max(0, blood - ctx.Game.RunesReady(RuneType.Blood))
                        + Math.Max(0, frost - ctx.Game.RunesReady(RuneType.Frost))
                        + Math.Max(0, unholy - ctx.Game.RunesReady(RuneType.Unholy));
            return deficit <= death; // Death runes cover the combined deficit
        }

        /// <summary>Affordability for a NAMED ability via <see cref="RuneCost"/>. A 0-rune ability (not in the map)
        /// is always affordable. This is the gate every rune-costed step ANDs into its When.</summary>
        public static bool CanAfford(CombatContext ctx, string spell) =>
            !RuneCost.TryGetValue(spell, out Cost c) || CanAffordRunes(ctx, c.Blood, c.Frost, c.Unholy);

        /// <summary>How many runes (of any kind) are ready right now — Blood + Frost + Unholy + Death. Drives
        /// Empower Rune Weapon ("≤2 runes ready → refresh them all").</summary>
        public static int RunesReadyTotal(CombatContext ctx) =>
            ctx.Game.RunesReady(RuneType.Blood) + ctx.Game.RunesReady(RuneType.Frost)
            + ctx.Game.RunesReady(RuneType.Unholy) + ctx.Game.RunesReady(RuneType.Death);

        // --- shared facts ---

        /// <summary>True only when we're actually engaging a fight — the product has committed (its fight state is
        /// set during the APPROACH too) or we're already in combat. The Death Grip pull gates on this (mirroring the
        /// druid/shaman <c>Fighting</c> helper) so the DK pulls ONLY to fight, not while idle / travelling.</summary>
        public static bool Fighting(CombatContext ctx) => ctx.Game.ProductIsFighting || ctx.Game.PlayerInCombat;

        // --- a thin rune-gated offensive strike (the spec's bread-and-butter wrapper) ---

        /// <summary>A rune-costed offensive strike on the current enemy: the DSL's known/ready/range gating PLUS the
        /// mandatory rune-affordability gate PLUS the caller's <paramref name="extra"/> condition. Use this for every
        /// rune ability so the cost gate can never be forgotten (a forgotten gate jams the rotation — see the class
        /// summary). 0-rune abilities pass <see cref="CanAfford"/> trivially, so this wrapper is safe for them too.</summary>
        public static RotationStep Strike(string spell, float priority, Func<CombatContext, bool> extra = null) =>
            Skill.Spell(spell).Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => CanAfford(ctx, spell) && (extra == null || extra(ctx)));

        /// <summary>A rune-costed SELF-targeted ability (e.g. Rune Tap — a Blood-rune self-heal): the same mandatory
        /// rune gate as <see cref="Strike"/>, but cast on the player. The caller's <paramref name="extra"/> condition
        /// (e.g. an HP floor) is ANDed in. A 0-rune self ability passes <see cref="CanAfford"/> trivially.</summary>
        public static RotationStep Strike2Self(string spell, float priority, Func<CombatContext, bool> extra = null) =>
            Skill.Spell(spell).Priority(priority).On(Targets.Self)
                 .When(ctx => CanAfford(ctx, spell) && (extra == null || extra(ctx)));

        // --- diseases (Icy Touch → Frost Fever; Plague Strike → Blood Plague; Pestilence spreads both) ---

        /// <summary>Icy Touch — applies/maintains Frost Fever. The CAST spell (Icy Touch) differs from the AURA it
        /// applies (Frost Fever), so this can't route through MaintainMyDebuff (which assumes one name for both) —
        /// it's a plain maintain on the aura. Re-cast when the disease is missing or under
        /// <see cref="DiseaseRefreshMs"/> remaining, gated on the rune cost (1 Frost) and the dying-mob floor. A
        /// post-cast grace stops the apply-latency double-cast (the disease isn't visible in the snapshot for a
        /// beat after the cast).</summary>
        public static RotationStep IcyTouch(DeathKnightSettings s, float priority) =>
            MaintainDisease("Icy Touch", "Frost Fever", s, priority);

        /// <summary>Plague Strike — applies/maintains Blood Plague (1 Unholy). DyingFloor + rune gate as Icy Touch;
        /// same cast-name ≠ aura-name maintain.</summary>
        public static RotationStep PlagueStrike(DeathKnightSettings s, float priority) =>
            MaintainDisease("Plague Strike", "Blood Plague", s, priority);

        /// <summary>Maintain a DK disease where the cast spell (<paramref name="castSpell"/>, e.g. Icy Touch) applies
        /// a differently-named aura (<paramref name="auraName"/>, e.g. Frost Fever) on the target. Re-cast when the
        /// aura is missing or expiring, gated on the diseases toggle, the rune cost of the cast spell, the dying-mob
        /// floor, and known-spell. A post-cast grace (<see cref="DiseaseApplyGraceMs"/>) suppresses the apply-latency
        /// double-cast — the same mechanism CombatBlocks.MaintainMyDebuff uses, but keyed on the distinct aura.</summary>
        public static RotationStep MaintainDisease(string castSpell, string auraName, DeathKnightSettings s, float priority) =>
            Skill.Spell(castSpell).Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseDiseases.Value && CanAfford(ctx, castSpell)
                              && ctx.Target.HealthPercent > s.DiseaseMinTargetHealth.Value
                              && (!ctx.Target.HasMyAura(auraName)
                                  || ctx.Target.MyAuraTimeLeftMs(auraName) < DiseaseRefreshMs))
                 .RecastDelay(DiseaseApplyGraceMs);

        /// <summary>Pestilence — spread both diseases (1 Blood) when 2+ (configurable) nearby enemies LACK them, and
        /// the current target already carries both (so there's something to spread). DyingFloor on the target.</summary>
        public static RotationStep Pestilence(DeathKnightSettings s, float priority) =>
            Skill.Spell("Pestilence").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseDiseases.Value && CanAfford(ctx, "Pestilence")
                              && ctx.Target.HasMyAura("Frost Fever") && ctx.Target.HasMyAura("Blood Plague")
                              && ctx.Target.HealthPercent > s.DiseaseMinTargetHealth.Value
                              && EnemiesNearLackingDiseases(ctx) >= s.PestilenceCount.Value);

        private static int EnemiesNearLackingDiseases(CombatContext ctx)
        {
            int n = 0;
            foreach (IWowUnit e in ctx.Enemies)
                if (e.IsAlive && e.IsAttackable && e.Distance <= PestilenceRadius
                    && (!e.HasMyAura("Frost Fever") || !e.HasMyAura("Blood Plague")))
                    n++;
            return n;
        }

        // --- presence + Horn of Winter (upkeep) ---

        /// <summary>Keep the configured Presence up (the old DK "Presence" setting): cast it when not already in it.
        /// One Exclusive token so a presence swap never double-fires. Not while mounted.</summary>
        public static RotationStep Presence(DeathKnightSettings s, float priority)
        {
            var slot = new Exclusive("DKPresence");
            return new RotationStep(
                name: "Presence",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (ctx.Game.PlayerIsMounted) return false;
                    string presence = s.Presence.Value;
                    return !ctx.Me.HasAura(presence)
                           && ctx.Game.IsSpellKnown(presence) && ctx.Game.IsSpellReady(presence);
                },
                action: (ctx, t) => ctx.Game.Cast(s.Presence.Value, ctx.Me),
                exclusive: slot);
        }

        /// <summary>Horn of Winter — the Strength/Agility buff (and free runic power). Re-cast when the buff is
        /// missing and not mounted. 0-rune, so no rune gate. Throttled so it isn't re-issued before the buff lands.</summary>
        public static RotationStep HornOfWinter(DeathKnightSettings s, float priority) =>
            Skill.Spell("Horn of Winter").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseHornOfWinter.Value && !ctx.Game.PlayerIsMounted
                              && !ctx.Me.HasAura("Horn of Winter"))
                 .RecastDelay(BuffApplyGraceMs);

        // --- survival (shared; Blood adds its own Rune Tap / Vampiric Blood / Mark of Blood) ---

        /// <summary>Anti-Magic Shell — absorbs magic damage when an enemy is casting at me. 0-rune, off the GCD.</summary>
        public static RotationStep AntiMagicShell(DeathKnightSettings s, float priority) =>
            Skill.Spell("Anti-Magic Shell").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseAntiMagicShell.Value
                              && EnemyCastingAtMe(ctx))
                 .OffGcd();

        /// <summary>Icebound Fortitude — the universal DK damage-reduction cooldown: below the configured HP% with a
        /// pack (2+) on me. 0-rune, off the GCD.</summary>
        public static RotationStep IceboundFortitude(DeathKnightSettings s, float priority) =>
            Skill.Spell("Icebound Fortitude").Priority(priority).On(Targets.Self)
                 .When(ctx => s.IceboundFortitudeHealthPercent.Value > 0
                              && ctx.Me.HealthPercent < s.IceboundFortitudeHealthPercent.Value
                              && EnemiesOnMe(ctx) >= IceboundFortitudePackSize)
                 .OffGcd();

        /// <summary>Empower Rune Weapon — instantly refreshes ALL runes + grants runic power. The DK "panic button"
        /// for rune starvation: fire when at most 2 runes are ready (the old DK gate), on a cooldowns-worthy fight,
        /// in combat. 0-rune. Off the GCD.</summary>
        public static RotationStep EmpowerRuneWeapon(DeathKnightSettings s, float priority) =>
            Skill.Spell("Empower Rune Weapon").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && ctx.Game.PlayerInCombat
                              && RunesReadyTotal(ctx) <= EmpowerRuneWeaponRuneThreshold)
                 .OffGcd();

        // --- interrupt: Mind Freeze (melee) + Death Grip (pull a caster in) ---

        /// <summary>Mind Freeze — the DK melee interrupt. Smart mode learns what's interruptible; off when the
        /// interrupt toggle is disabled. 0-rune (it costs runic power, not runes — no rune gate). Off the GCD.</summary>
        public static RotationStep MindFreeze(DeathKnightSettings s, float priority) =>
            CombatBlocks.Interrupt("Mind Freeze", priority,
                ctx => s.InterruptCasts.Value ? InterruptModes.Smart : InterruptModes.Never);

        /// <summary>Death Grip as a pull-INTERRUPT: yank a casting enemy that's targeting me into melee (which
        /// interrupts the cast and closes the gap). A backup to Mind Freeze when the caster is at range. 0-rune.
        /// Gated on BOTH the interrupt toggle and the Death-Grip-interrupt toggle. The DSL range gate handles the
        /// 30y reach.</summary>
        public static RotationStep DeathGripInterrupt(DeathKnightSettings s, float priority) =>
            Skill.Spell("Death Grip").Priority(priority).On(Targets.EnemiesCasting)
                 .When((ctx, t) => s.InterruptCasts.Value && s.UseDeathGripInterrupt.Value
                                   && t.IsTargetingMe)
                 .RecastDelay(DeathGripRecastMs);

        /// <summary>Death Grip ranged PULL — the DK's proper opener (like the bear's Growl pull, but it yanks the
        /// mob to you). Cast on the engaged target while the product is committing to the fight but we're not yet in
        /// combat, the target is past melee, and it isn't already targeting us (pull the engaged mob in). 0-rune.
        /// RecastDelay keeps it from being re-issued every tick while the pull resolves / on a grip-immune mob. The
        /// DSL range gate enforces Death Grip's ~30y cap.</summary>
        public static RotationStep DeathGripPull(DeathKnightSettings s, float priority) =>
            Skill.Spell("Death Grip").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseDeathGripPull.Value
                              && Fighting(ctx) && !ctx.Game.PlayerInCombat
                              && ctx.Target.Distance > MeleeRange && !ctx.Target.IsTargetingMe)
                 .RecastDelay(DeathGripRecastMs);

        // --- runic-power dumps (0-rune RP spenders) ---

        /// <summary>Frost Strike — the Frost runic-power dump (0-rune). Spend at/above the configured RP. RunicPower
        /// is read directly (PowerPercent reads MANA for a DK, which is wrong).</summary>
        public static RotationStep FrostStrike(DeathKnightSettings s, float priority) =>
            Skill.Spell("Frost Strike").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.RunicPower >= s.FrostStrikeRunicPower.Value);

        /// <summary>Death Coil — a runic-power dump (0-rune; also a ranged poke). Spend at/above
        /// <paramref name="rpThreshold"/> runic power. Blood dumps at ~40, Unholy at ~80 (it'd rather feed the
        /// gargoyle) — the spec passes the threshold from its own setting.</summary>
        public static RotationStep DeathCoil(Func<CombatContext, int> rpThreshold, float priority) =>
            Skill.Spell("Death Coil").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.RunicPower >= rpThreshold(ctx));

        // --- AoE/builder strike helpers driven by the per-spec enemy-count settings ---

        /// <summary>Blood Strike — single-target builder (1 Blood): fire when EXACTLY <paramref name="count"/>
        /// enemies are in melee (the old DK gate: <c>== count</c>, so it yields to Heart Strike when a cleave wants
        /// it). Rune-gated.</summary>
        public static RotationStep BloodStrike(Func<CombatContext, int> count, float priority) =>
            Strike("Blood Strike", priority, ctx => EnemiesInMelee(ctx) == count(ctx));

        /// <summary>Heart Strike — the cleave builder (1 Blood): fire when at least <paramref name="count"/> enemies
        /// are in melee. Auto-skips until learned (level ~55). Rune-gated.</summary>
        public static RotationStep HeartStrike(Func<CombatContext, int> count, float priority) =>
            Strike("Heart Strike", priority, ctx => EnemiesInMelee(ctx) >= count(ctx));

        /// <summary>Blood Boil — the Blood AoE (1 Blood): fire when MORE than <paramref name="count"/> enemies are in
        /// melee. Rune-gated.</summary>
        public static RotationStep BloodBoil(Func<CombatContext, int> count, float priority) =>
            Strike("Blood Boil", priority, ctx => EnemiesInMelee(ctx) > count(ctx));

        /// <summary>Death and Decay — the ground AoE (1 Blood + 1 Frost + 1 Unholy): fire when at least
        /// <paramref name="count"/> enemies are near the target. Rune-gated on all three (a Death-rune-covered
        /// shortfall counts), so it never fires unaffordable.</summary>
        public static RotationStep DeathAndDecay(Func<CombatContext, int> count, float priority) =>
            Strike("Death and Decay", priority, ctx => EnemiesNear(ctx) >= count(ctx));

        // --- the ghoul (reuses PetControl; gated on UseRaiseDead + reagent/corpse availability) ---

        /// <summary>Corpse Dust — the Raise Dead reagent (3.3.5a item 37201). Consumed when no nearby humanoid corpse
        /// is available; without a corpse AND without this in the bags, Raise Dead just fails, so the summon gates on
        /// it (see <see cref="WithGhoul"/>).</summary>
        public const uint CorpseDustItemId = 37201;

        /// <summary>Append the ghoul management band to a spec's step list (one call, like the hunter's pet band).
        /// UNHOLY ONLY: Raise Dead's ghoul is a permanent pet only with the Master of Ghouls talent (Unholy); for
        /// Blood/Frost it's a 60s temp minion that isn't worth a GCD + reagent, so those specs don't compose this.
        /// Reuses the class-agnostic <see cref="PetControl"/> blocks: Raise Dead to summon (no revive spell — a DK
        /// re-Raises rather than revives), target-sync the ghoul to the player's target, and the two ghoul abilities
        /// Gnaw (interrupt when the target is casting + the ghoul is in range) and Leap (gap-close when the ghoul is
        /// far). Every block is gated on the ghoul EXISTING (ctx.Pet) + the UseRaiseDead toggle, so a petless /
        /// product-owned-pet DK skips them cleanly. The SUMMON additionally requires a raisable corpse nearby OR
        /// Corpse Dust in the bags — otherwise Raise Dead just fails and would spam a dead cast every tick.
        /// Priorities sit in the pet band (~0.5-0.9), above the rotation.</summary>
        public static List<RotationStep> WithGhoul(DeathKnightSettings s, List<RotationStep> steps)
        {
            Func<CombatContext, bool> enabled = ctx => s.UseRaiseDead.Value;
            // Raise Dead needs a nearby humanoid corpse OR its Corpse Dust reagent; with neither the cast just fails,
            // so gate the SUMMON (not the target-sync / Gnaw / Leap, which only need the ghoul to exist) on it.
            Func<CombatContext, bool> canSummon = ctx => enabled(ctx)
                && (ctx.Game.HasRaiseableCorpseNearby() || ctx.Game.HasItemById(CorpseDustItemId));
            // Raise Dead summons; there is no revive spell (a dead/expired ghoul is re-Raised), so callSpell ==
            // reviveSpell == "Raise Dead" and the PetControl revive path simply re-casts it.
            steps.Add(PetControl.Summon(canSummon, "Raise Dead", "Raise Dead", priority: 0.5f));
            steps.Add(PetControl.Attack(enabled, priority: 0.7f));
            // Gnaw: the ghoul's interrupt — only when the target is casting (and the ghoul is in range, which the
            // adapter's PetAbilityReady + the pet's own positioning handle). Mirrors the old DeathKnightBehavior.
            steps.Add(PetControl.UseAbility(enabled, "Gnaw", priority: 0.75f,
                when: ctx => ctx.HasEnemyTarget && ctx.Target.IsCasting));
            // Leap: gap-close the ghoul to a distant target (old DeathKnightBehavior fired Leap when the ghoul was
            // >=7y from the target). We approximate "ghoul is far from the fight" with the ghoul's distance to us.
            steps.Add(PetControl.UseAbility(enabled, "Leap", priority: 0.8f,
                when: ctx => ctx.Pet != null && ctx.Pet.Distance >= GhoulLeapRange));
            return steps;
        }

        // --- shared melee/AoE counts ---

        /// <summary>Enemies in melee on the TARGET cluster (the old DK's "GetDistance <= 10" pack count for the
        /// Blood/Heart/Boil builders). Target-anchored so a clump around the mob counts even if it's at our melee
        /// edge.</summary>
        public static int EnemiesInMelee(CombatContext ctx) => ctx.EnemiesNearTarget(MeleeCountRadius);

        /// <summary>Enemies near the target for Death and Decay placement (the old DK's "GetDistance < 15").</summary>
        public static int EnemiesNear(CombatContext ctx) => ctx.EnemiesNearTarget(DeathAndDecayRadius);

        /// <summary>Enemies meleeing US within 8y — the "is a pack on me?" count for Icebound Fortitude.</summary>
        public static int EnemiesOnMe(CombatContext ctx)
        {
            int n = 0;
            foreach (IWowUnit e in ctx.Enemies)
                if (e.IsTargetingMe && e.Distance <= OnMeRadius) n++;
            return n;
        }

        private static bool EnemyCastingAtMe(CombatContext ctx)
        {
            foreach (IWowUnit e in ctx.Enemies)
                if (e.IsCasting && e.IsTargetingMe) return true;
            return false;
        }

        /// <summary>A fight worth a major cooldown: a boss, an elite, or a pack near the target.</summary>
        public static bool IsBigFight(CombatContext ctx) =>
            ctx.HasEnemyTarget && (ctx.Target.IsBoss() || ctx.Target.IsElite || EnemiesNear(ctx) >= BigFightPackSize);

        // --- named constants (no magic numbers) ---

        /// <summary>Diseases last ~21s (Frost Fever) / ~15s base; refresh a few seconds before expiry.</summary>
        private const int DiseaseRefreshMs = 3000;

        /// <summary>Post-cast grace before a disease maintain may re-fire — covers the server round-trip in which the
        /// freshly applied disease isn't yet visible in the snapshot, so the maintain doesn't double-cast it. Mirrors
        /// CombatBlocks' InstantDebuffApplyGraceMs.</summary>
        private const int DiseaseApplyGraceMs = 1500;

        /// <summary>Pestilence spread radius (old DK: enemies within 15y of the target that lack the diseases).</summary>
        private const float PestilenceRadius = 15f;

        /// <summary>Melee-pack radius for the Blood/Heart/Boil enemy counts (old DK: <= 10y).</summary>
        private const float MeleeCountRadius = 10f;

        /// <summary>Death and Decay placement radius (old DK: < 15y).</summary>
        private const float DeathAndDecayRadius = 15f;

        /// <summary>"A pack is on me" radius for Icebound Fortitude (old DK: enemies on me within 8y).</summary>
        private const float OnMeRadius = 8f;

        /// <summary>Below this distance the target is essentially in melee, so the Death Grip pull is pointless.</summary>
        private const float MeleeRange = 8f;

        /// <summary>Don't re-issue Death Grip for this long after firing (a grip-immune mob would otherwise be
        /// spammed while the bot closes; the pull also takes a beat to register the cooldown).</summary>
        private const int DeathGripRecastMs = 3000;

        /// <summary>A buff (Horn of Winter / Presence) takes a server round-trip to show; throttle the re-cast so it
        /// isn't issued twice before the aura lands.</summary>
        private const int BuffApplyGraceMs = 1500;

        /// <summary>Empower Rune Weapon fires at or below this many ready runes (the old DK's <c>&lt;= 2</c> gate).</summary>
        private const int EmpowerRuneWeaponRuneThreshold = 2;

        /// <summary>Icebound Fortitude wants at least this many enemies on us (old DK: <c>&gt;= 2</c>).</summary>
        private const int IceboundFortitudePackSize = 2;

        /// <summary>The ghoul's Leap gap-closer fires when it's at least this far from us (old DeathKnightBehavior
        /// fired Leap at >=7y from the target; we approximate with the ghoul's distance).</summary>
        private const float GhoulLeapRange = 7f;

        /// <summary>A "pack" worth a major cooldown.</summary>
        private const int BigFightPackSize = 2;
    }
}
