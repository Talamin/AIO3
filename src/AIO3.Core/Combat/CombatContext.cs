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

        /// <summary>The player's pet snapshot, or null if none exists (petless / below taming level /
        /// dismissed). Captured under the frame lock like the other units; pet steps key on this.</summary>
        public IWowUnit Pet { get; }

        public IReadOnlyList<IWowUnit> Enemies { get; }
        public IReadOnlyList<IWowUnit> Party { get; }

        /// <summary>The local player's own active totems (shaman), each carrying its Name and Distance. Empty for
        /// every other class. Drives the totem upkeep: a totem is "down" for a school when none of its totems is in
        /// the list, and "left behind" when its only totem is past the re-drop / recall range.</summary>
        public IReadOnlyList<IWowUnit> Totems { get; }

        /// <summary>Shared interrupt learner (lives across ticks; supplied by the host).</summary>
        public InterruptTracker Interrupts { get; }

        /// <summary>Shared damage learner (lives across ticks; supplied by the host). Used by the
        /// BestDamage block to rank interchangeable strikes by learned damage.</summary>
        public DamageTracker Damage { get; }

        public CombatContext(
            IGameClient game,
            IWowUnit me,
            IWowUnit target,
            IReadOnlyList<IWowUnit> enemies,
            IReadOnlyList<IWowUnit> party,
            InterruptTracker interrupts = null,
            DamageTracker damage = null,
            IWowUnit pet = null,
            IReadOnlyList<IWowUnit> totems = null)
        {
            Game = game;
            Me = me;
            Target = target;
            Enemies = enemies;
            Party = party;
            Pet = pet;
            Totems = totems ?? System.Array.Empty<IWowUnit>();
            Interrupts = interrupts ?? new InterruptTracker();
            Damage = damage ?? new DamageTracker();
        }

        /// <summary>Take a snapshot of the current game state.</summary>
        public static CombatContext Capture(IGameClient game, InterruptTracker interrupts = null, DamageTracker damage = null) => new CombatContext(
            game,
            game.Me,
            game.Target,
            game.Enemies.ToArray(),
            game.Party.ToArray(),
            interrupts,
            damage,
            game.Pet,
            game.Totems.ToArray());

        // --- shared, computed-once world facts ---

        public int EnemyCount => Enemies.Count;

        /// <summary>The player's combo points on the current target (0..5) — convenience over <see cref="Game"/>
        /// so rogue/feral finisher gates read <c>ctx.ComboPoints</c> like they read <c>ctx.Me.Energy</c>.</summary>
        public int ComboPoints => Game.ComboPoints;

        public int EnemiesTargetingMe => Enemies.Count(e => e.IsTargetingMe);

        /// <summary>Number of enemies within <paramref name="yards"/> of the player (AoE sizing).
        /// Use this for threats ON the player (e.g. "am I surrounded?"); for placing a ground/cone AoE that
        /// lands around the TARGET, use <see cref="EnemiesNearTarget"/> instead.</summary>
        public int EnemiesWithin(float yards) => Enemies.Count(e => e.Distance <= yards);

        /// <summary>Number of enemies within <paramref name="yards"/> of <paramref name="center"/> (3D).
        /// The general form behind <see cref="EnemiesNearTarget"/>: a ranged AoE hits the cluster around the
        /// spell's anchor, which is rarely the player.</summary>
        public int EnemiesNear(IWowUnit center, float yards) =>
            center == null ? 0 : Enemies.Count(e => e.DistanceTo(center) <= yards);

        /// <summary>Number of enemies within <paramref name="yards"/> of the CURRENT TARGET (0 if no target).
        /// The correct pack gate for target-anchored AoE shots (Multi-Shot, Volley): a ranged hunter stands far
        /// from the pack, so the player-relative <see cref="EnemiesWithin"/> would almost never trip.</summary>
        public int EnemiesNearTarget(float yards) => EnemiesNear(Target, yards);

        public bool HasTarget => Target != null && Target.IsAlive;

        /// <summary>True only if the current target is alive AND attackable (not a friendly NPC).</summary>
        public bool HasEnemyTarget => Target != null && Target.IsAlive && Target.IsAttackable;

        public bool InCombat => Enemies.Any(e => e.IsTargetingMe);

        /// <summary>Party includes the player, so a group means more than one member.</summary>
        public bool IsInGroup => Party.Count > 1;
    }
}
