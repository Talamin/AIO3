using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Shared warrior building blocks reused by every spec, so cross-cutting behaviour is written
    /// once and stays consistent (no drift). Each returns a ready RotationStep; the spec just lists
    /// them in priority order alongside its signature abilities.
    /// </summary>
    public static class WarriorCommon
    {
        // Charge's usable range is 8–25y. The stance dance only *starts* the switch when the target is
        // still this far out (ChargeDanceMinRange), so the gap WRobot keeps closing during the switch
        // doesn't push us inside the 8y minimum before we can Charge (which would waste the dance). Once
        // already in Battle Stance there is no switch delay, so a plain Charge uses the full 8–25y.
        private const float ChargeMinRange = 8f;
        private const float ChargeMaxRange = 25f;
        private const float ChargeDanceMinRange = 18f;

        // A stance change zeroes our rage. Don't dance (and lose it) while we still have this much — we'd
        // reach melee and spend it first. Charge itself, once in Battle Stance, costs no rage.
        private const int ChargeDanceMaxRage = 10;

        /// <summary>Switch to the spec's stance when not already in it (no-op until the stance is learned).</summary>
        public static RotationStep EnsureStance(string stanceName, float priority) =>
            Skill.Spell(stanceName).Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Game.ActiveStanceName != stanceName).OffGcd();

        /// <summary>Build rage when an attackable target is in melee (off the GCD).</summary>
        public static RotationStep Bloodrage(float priority) =>
            Skill.Spell("Bloodrage").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.Distance < 8f).OffGcd();

        /// <summary>Break fear (and enrage) with Berserker Rage.</summary>
        public static RotationStep BerserkerRage(float priority) =>
            Skill.Spell("Berserker Rage").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Me.HasAura("Fear")).OffGcd();

        /// <summary>Major DPS cooldown (Recklessness): on a boss/elite or a pack, when enabled.</summary>
        public static RotationStep Recklessness(WarriorSettings s, float priority) =>
            Skill.Spell("Recklessness").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && ctx.HasEnemyTarget
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite || ctx.EnemiesWithin(8f) >= 3)).OffGcd();

        /// <summary>In-combat gap-closer (Berserker stance). The host only runs the rotation while the
        /// product is fighting, so this closes distance during a committed fight, not during navigation.</summary>
        public static RotationStep Intercept(WarriorSettings s, float priority) =>
            Skill.Spell("Intercept").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseGapClosers.Value
                              && ctx.Me.Rage > 10 && ctx.Target.Distance > 8f && ctx.Target.Distance <= 25f)
                 .RecastDelay(1000); // don't re-issue mid-leap (mirrors the old AIO's forcedTimerMS)

        /// <summary>
        /// Opener gap-closer (Battle stance), used only until Intercept is learned — with a stance dance:
        /// Charge requires Battle Stance, so if we're in another stance we switch to Battle first, then
        /// Charge; EnsureStance restores our home stance on the next tick (once Charge is on cooldown /
        /// we're in combat, this step goes quiet). Stateless: the dance ends on its own via the range /
        /// in-combat / cooldown gates. MUST sit at a HIGHER priority than EnsureStance (smaller number)
        /// so EnsureStance doesn't revert the stance mid-dance.
        ///
        /// The stance switch costs time and rage, so it is gated tighter than a plain Charge: we only
        /// START the switch when (a) the target is still near the top of Charge's range — switching takes
        /// a beat during which WRobot keeps closing the gap, so starting too close means we overrun the 8y
        /// minimum mid-switch and waste the dance — and (b) we have no meaningful rage to lose (a stance
        /// change zeroes rage; if we have some we'd rather just walk in and spend it). Once we are already
        /// in Battle Stance neither applies — Charge is instant and free — so we charge anywhere in range.
        /// </summary>
        public static RotationStep ChargeWithStanceDance(WarriorSettings s, float priority) =>
            new RotationStep(
                name: "Charge",
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) =>
                {
                    if (!s.UseGapClosers.Value
                        || ctx.Game.PlayerInCombat                 // Charge is an out-of-combat opener
                        || ctx.Game.IsSpellKnown("Intercept")      // Intercept replaces Charge once learned
                        || !ctx.Game.IsSpellReady("Charge")        // also ends the dance once Charge is on cooldown
                        || t.Distance > ChargeMaxRange)
                        return false;

                    // Already in Battle Stance → no switch delay or rage loss; charge anywhere in range.
                    if (ctx.Game.ActiveStanceName == "Battle Stance")
                        return t.Distance > ChargeMinRange;

                    // Must switch first → only worth it far out (so we don't overrun the minimum while
                    // switching) and only when we have no rage worth keeping.
                    return t.Distance >= ChargeDanceMinRange && ctx.Me.Rage <= ChargeDanceMaxRage;
                },
                action: (ctx, t) =>
                    ctx.Game.ActiveStanceName != "Battle Stance"
                        ? ctx.Game.Cast("Battle Stance", ctx.Me)   // stance dance: switch to Battle first...
                        : ctx.Game.Cast("Charge", t),               // ...then Charge
                ignoreGcd: true);

        /// <summary>Slow a fleeing humanoid below 40% (snare; humanoids are the ones that flee).</summary>
        public static RotationStep Hamstring(WarriorSettings s, float priority) =>
            Skill.Spell("Hamstring").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseHamstring.Value
                              && ctx.Target.HealthPercent < 40
                              && ctx.Target.CreatureType == "Humanoid"
                              && !ctx.Target.IsBoss()
                              && !ctx.Target.HasAura("Hamstring"));

        public static RotationStep Execute(float priority) =>
            Skill.Spell("Execute").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.HealthPercent < 20);

        /// <summary>
        /// Demoralizing Shout: enemy attack-power reduction (survival). Self-cast AoE debuff, so it is
        /// gated on the current target to avoid spamming: only refresh when the target is missing it
        /// (neither Demoralizing Shout nor the druid Demoralizing Roar) and is durable enough to be
        /// worth a global — a tougher fight (elite/boss) or a pack. Trash that dies in a few swings
        /// isn't worth the rage/GCD.
        /// </summary>
        public static RotationStep DemoralizingShout(WarriorSettings s, float priority) =>
            Skill.Spell("Demoralizing Shout").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget
                              && !ctx.Target.HasAura("Demoralizing Shout")
                              && !ctx.Target.HasAura("Demoralizing Roar")
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite
                                  || ctx.EnemiesWithin(10f) >= s.AoeThreshold.Value));

        /// <summary>Usable only after a killing blow; IsSpellReady gates the proc window.</summary>
        public static RotationStep VictoryRush(float priority) =>
            Skill.Spell("Victory Rush").Priority(priority).On(Targets.CurrentEnemy);

        /// <summary>Keep Rend up (refresh when under ~3s left) — but not on bleed-immune creatures.</summary>
        public static RotationStep Rend(float priority) =>
            Skill.Spell("Rend").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => (!ctx.Target.HasMyAura("Rend") || ctx.Target.MyAuraTimeLeftMs("Rend") < 3000)
                              && ctx.Target.CreatureType != "Elemental"
                              && ctx.Target.CreatureType != "Mechanical");

        /// <summary>
        /// Off-GCD rage dump; lowest-priority "leftover" once spare rage is above the reserve.
        /// Guarded so we don't re-queue the on-next-swing attack every tick.
        /// </summary>
        public static RotationStep HeroicStrike(WarriorSettings s, float priority) =>
            Skill.Spell("Heroic Strike").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > s.HeroicStrikeRageReserve.Value
                              && !ctx.Game.IsCurrentSpell("Heroic Strike")).OffGcd();

        /// <summary>Off-GCD AoE rage dump (preferred over Heroic Strike with several enemies in range).</summary>
        public static RotationStep Cleave(WarriorSettings s, float priority) =>
            Skill.Spell("Cleave").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= s.AoeThreshold.Value
                              && !ctx.Game.IsCurrentSpell("Cleave")).OffGcd();
    }
}
