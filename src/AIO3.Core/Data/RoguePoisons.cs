using System;
using System.Collections.Generic;

namespace AIO3.Core.Data
{
    /// <summary>
    /// Rogue weapon-poison item ranks (WotLK 3.3.5a), each a (minimum level, item id), ordered HIGHEST rank first.
    /// The poison upkeep picks the highest rank the rogue's level allows AND actually carries. Item IDs (not names)
    /// keep the lookup locale-independent — poison names differ per client language, the ids don't.
    ///
    /// Ported from the old AIO ApplyPoison dictionaries, but ordered strictly high→low so the "first usable" pick
    /// really is the best rank: the old Deadly table was mis-ordered (rank 1 sat before rank 5), so e.g. a level-60
    /// rogue wrongly got rank-1 Deadly Poison. Sorting fixes that.
    ///
    /// Strategy (the conventional leveling setup, matching the old FC): Instant Poison on the MAIN hand, Deadly
    /// Poison on the OFF hand (its per-hit stacks build fastest from the faster off-hand swings); Instant Poison is
    /// the off-hand fallback when no Deadly is carried.
    /// </summary>
    public static class RoguePoisons
    {
        /// <summary>One poison rank: usable from <see cref="Level"/> up, the bag item <see cref="ItemId"/>.</summary>
        public readonly struct Rank
        {
            public readonly int Level;
            public readonly uint ItemId;
            public Rank(int level, uint itemId) { Level = level; ItemId = itemId; }
        }

        /// <summary>Instant Poison ranks, highest first (rank IX..I).</summary>
        public static readonly IReadOnlyList<Rank> Instant = new[]
        {
            new Rank(79, 43231), new Rank(73, 43230), new Rank(68, 21927), new Rank(60, 8928),
            new Rank(52, 8927),  new Rank(44, 8926),  new Rank(36, 6950),  new Rank(28, 6949),
            new Rank(20, 6947)
        };

        /// <summary>Deadly Poison ranks, highest first (rank IX..I).</summary>
        public static readonly IReadOnlyList<Rank> Deadly = new[]
        {
            new Rank(80, 43233), new Rank(76, 43232), new Rank(70, 22054), new Rank(62, 22053),
            new Rank(60, 20844), new Rank(54, 8985),  new Rank(46, 8984),  new Rank(38, 2893),
            new Rank(30, 2892)
        };

        /// <summary>The highest Instant Poison the level allows AND the bags carry (per <paramref name="have"/>), or 0.</summary>
        public static uint BestUsableInstant(int level, Func<uint, bool> have) => BestUsable(Instant, level, have);

        /// <summary>The highest Deadly Poison the level allows AND the bags carry (per <paramref name="have"/>), or 0.</summary>
        public static uint BestUsableDeadly(int level, Func<uint, bool> have) => BestUsable(Deadly, level, have);

        private static uint BestUsable(IReadOnlyList<Rank> ranks, int level, Func<uint, bool> have)
        {
            foreach (Rank rank in ranks)
                if (rank.Level <= level && have(rank.ItemId))
                    return rank.ItemId;
            return 0;
        }
    }
}
