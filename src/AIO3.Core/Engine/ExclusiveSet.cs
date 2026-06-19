using System.Collections.Generic;
using AIO3.Core.Game;

namespace AIO3.Core.Engine
{
    /// <summary>
    /// Tracks which exclusive tokens have been consumed on which units during a single
    /// rotation pass. A fresh instance is created each tick by the engine.
    /// </summary>
    public sealed class ExclusiveSet
    {
        private readonly Dictionary<ulong, HashSet<Exclusive>> _tokens = new Dictionary<ulong, HashSet<Exclusive>>();

        public bool Add(IWowUnit unit, Exclusive exclusive)
        {
            if (exclusive == null || unit == null) return false;
            if (!_tokens.TryGetValue(unit.Guid, out HashSet<Exclusive> set))
            {
                set = new HashSet<Exclusive>();
                _tokens[unit.Guid] = set;
            }
            return set.Add(exclusive);
        }

        public bool Contains(IWowUnit unit, Exclusive exclusive)
        {
            if (exclusive == null || unit == null) return false;
            return _tokens.TryGetValue(unit.Guid, out HashSet<Exclusive> set) && set.Contains(exclusive);
        }

        public bool Remove(IWowUnit unit, Exclusive exclusive)
        {
            if (exclusive == null || unit == null) return false;
            return _tokens.TryGetValue(unit.Guid, out HashSet<Exclusive> set) && set.Remove(exclusive);
        }
    }
}
