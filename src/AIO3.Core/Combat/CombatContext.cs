using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Game;

namespace AIO3.Core.Combat
{
    /// <summary>
    /// Layer 1 — one immutable snapshot of the world per tick. Built once at the start
    /// of a tick, read by every step. Because it is immutable and lives only for the
    /// tick, the per-tick caching that plagued the old code (static dictionary, raced
    /// across threads) becomes trivially correct here.
    ///
    /// Shared, computed-once world facts (enemy counts, "am I being focused", later:
    /// time-to-die, incoming damage, add clumps) live here so that every predicate
    /// reads the same numbers instead of recomputing them.
    /// </summary>
    public sealed class CombatContext
    {
        public IGameClient Game { get; }
        public IWowUnit Me { get; }
        public IWowUnit Target { get; }
        public IReadOnlyList<IWowUnit> Enemies { get; }
        public IReadOnlyList<IWowUnit> Party { get; }

        public CombatContext(
            IGameClient game,
            IWowUnit me,
            IWowUnit target,
            IReadOnlyList<IWowUnit> enemies,
            IReadOnlyList<IWowUnit> party)
        {
            Game = game;
            Me = me;
            Target = target;
            Enemies = enemies;
            Party = party;
        }

        /// <summary>Take a snapshot of the current game state.</summary>
        public static CombatContext Capture(IGameClient game) => new CombatContext(
            game,
            game.Me,
            game.Target,
            game.Enemies.ToArray(),
            game.Party.ToArray());

        // --- shared, computed-once world facts ---

        public int EnemyCount => Enemies.Count;

        public int EnemiesTargetingMe => Enemies.Count(e => e.IsTargetingMe);

        /// <summary>Number of enemies within <paramref name="yards"/> of the player (AoE sizing).</summary>
        public int EnemiesWithin(float yards) => Enemies.Count(e => e.Distance <= yards);

        public bool HasTarget => Target != null && Target.IsAlive;

        /// <summary>True only if the current target is alive AND attackable (not a friendly NPC).</summary>
        public bool HasEnemyTarget => Target != null && Target.IsAlive && Target.IsAttackable;

        public bool InCombat => Enemies.Any(e => e.IsTargetingMe);

        /// <summary>Party includes the player, so a group means more than one member.</summary>
        public bool IsInGroup => Party.Count > 1;
    }
}
