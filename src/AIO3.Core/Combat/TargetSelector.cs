using System.Linq;
using AIO3.Core.Game;

namespace AIO3.Core.Combat
{
    /// <summary>
    /// Picks the player's combat target when target selection is enabled. This is purely about managing
    /// ADDS once a fight is already underway — the FC must never pull or start a fight on its own. The
    /// WRobot product always owns the opener (initial target + engage), and we must not fight it.
    ///
    /// Therefore the selector only ever considers enemies that are already attacking us, and only acts
    /// when there is more than one of them:
    ///
    ///   - 0 or 1 attacker  → do nothing (nothing to manage; keep whatever the product chose).
    ///   - 2+ attackers and the current target is one of them → keep it (no thrashing between adds).
    ///   - 2+ attackers and the current target is NOT one of them (dead, fled, or a mis-tag)
    ///                      → switch to the nearest enemy that is actually on us.
    ///
    /// Returns null to mean "leave the current target alone". It never returns an enemy that is not
    /// already attacking us, so it can never initiate a pull.
    /// </summary>
    public static class TargetSelector
    {
        public static IWowUnit Pick(CombatContext ctx)
        {
            // Only enemies already attacking us are candidates — this is what prevents the FC from
            // ever pulling or acquiring a fresh target on its own.
            var attackers = ctx.Enemies
                .Where(e => e.IsAlive && e.IsAttackable && e.IsTargetingMe)
                .OrderBy(e => e.Distance)
                .ToList();

            // Nothing to manage unless we're fighting more than one enemy.
            if (attackers.Count < 2) return null;

            // Already locked onto one of the attackers → keep it (no thrashing between adds).
            if (ctx.HasEnemyTarget && ctx.Target.IsTargetingMe) return null;

            // Our current target isn't one of the attackers → switch to the nearest one that is.
            return attackers[0];
        }
    }
}
