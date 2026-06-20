using System.Linq;
using AIO3.Core.Game;

namespace AIO3.Core.Combat
{
    /// <summary>
    /// Picks the player's combat target when target selection is enabled. This is about managing ADDS
    /// once a fight is already underway — the FC must never pull or start a fight on its own (the WRobot
    /// product owns the opener), so the only candidates are enemies already attacking us, and it only
    /// acts when there is more than one of them.
    ///
    /// Among those attackers it prefers the one that will stop hurting us SOONEST — the lowest estimated
    /// time-to-kill (TTK). TTK = time to close to melee (you deal no damage while running) + time to kill
    /// from the target's current health. So low-health targets win (they die fastest and a dead target
    /// deals no more damage), but a nearer target can beat a slightly-lower-health far one because the
    /// run-up costs damage time. A hysteresis margin avoids thrashing between similar targets.
    ///
    /// Returns null to mean "leave the current target alone". It never returns an enemy that isn't already
    /// attacking us, so it can never initiate a pull.
    /// </summary>
    public static class TargetSelector
    {
        // Heuristics for the TTK estimate (seconds). Deliberately rough; a future DamageTracker can
        // replace FullKillSeconds with a learned per-target kill time.
        private const float MeleeRange = 5f;
        private const float MoveSpeedYards = 7f;    // ~base run speed
        private const float FullKillSeconds = 6f;   // ~time to kill a full-health target while leveling
        private const float SwitchMargin = 0.8f;    // only switch if the new TTK is < 80% of the current one

        public static IWowUnit Pick(CombatContext ctx)
        {
            // Only enemies already attacking us are candidates — this is what stops the FC ever pulling.
            var attackers = ctx.Enemies
                .Where(e => e.IsAlive && e.IsAttackable && e.IsTargetingMe)
                .ToList();

            // Nothing to manage unless we're fighting more than one enemy.
            if (attackers.Count < 2) return null;

            IWowUnit best = null;
            float bestTtk = float.MaxValue;
            foreach (IWowUnit e in attackers)
            {
                float ttk = Ttk(e);
                if (ttk < bestTtk) { bestTtk = ttk; best = e; }
            }

            // Keep the current target unless another attacker dies clearly sooner (hysteresis).
            IWowUnit current = (ctx.HasEnemyTarget && ctx.Target.IsTargetingMe) ? ctx.Target : null;
            if (current == null) return best;                       // not on an attacker → take the best
            if (best != null && bestTtk < Ttk(current) * SwitchMargin) return best;
            return null;                                            // keep current
        }

        /// <summary>Estimated seconds to remove an enemy: run-up to melee + kill time from its health.</summary>
        private static float Ttk(IWowUnit e)
        {
            float travel = System.Math.Max(0f, e.Distance - MeleeRange) / MoveSpeedYards;
            float kill = (float)(e.HealthPercent / 100.0) * FullKillSeconds;
            return travel + kill;
        }
    }
}
