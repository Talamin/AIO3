using System;
using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Game;

namespace AIO3.Core.Dsl
{
    /// <summary>Common target selectors for steps.</summary>
    public static class Targets
    {
        public static readonly Func<CombatContext, IEnumerable<IWowUnit>> Self =
            ctx => ctx.Me != null ? new[] { ctx.Me } : Array.Empty<IWowUnit>();

        public static readonly Func<CombatContext, IEnumerable<IWowUnit>> Current =
            ctx => ctx.Target != null ? new[] { ctx.Target } : Array.Empty<IWowUnit>();

        /// <summary>The current target, but only if it is alive and actually attackable
        /// (excludes friendly NPCs). Use this for all offensive steps.</summary>
        public static readonly Func<CombatContext, IEnumerable<IWowUnit>> CurrentEnemy =
            ctx => ctx.Target != null && ctx.Target.IsAlive && ctx.Target.IsAttackable
                ? new[] { ctx.Target } : Array.Empty<IWowUnit>();

        public static readonly Func<CombatContext, IEnumerable<IWowUnit>> Enemies =
            ctx => ctx.Enemies;

        public static readonly Func<CombatContext, IEnumerable<IWowUnit>> Party =
            ctx => ctx.Party;

        /// <summary>Enemies currently casting — the candidate set for interrupts.</summary>
        public static readonly Func<CombatContext, IEnumerable<IWowUnit>> EnemiesCasting =
            ctx => ctx.Enemies.Where(e => e.IsCasting);
    }
}
