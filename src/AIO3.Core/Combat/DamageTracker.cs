using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AIO3.Core.Combat
{
    /// <summary>
    /// Learns how much damage each of our abilities actually does, by tallying combat-log damage events
    /// (the same source we use for interrupt learning). MEASURE-ONLY for now: it records and reports, it
    /// does NOT yet influence the rotation. Per ability it keeps the running total, the number of hits,
    /// and a decaying average per hit (EMA) so it tracks the current level/gear instead of all-time data.
    ///
    /// One tracker = one "notebook" for one source. A pet class will use a second instance for the pet's
    /// own GUID, so player and pet damage never mix. The host feeds it via a combat-log learner (Layer 0);
    /// this class is pure and thread-safe (combat-log events arrive on another thread).
    /// </summary>
    public sealed class DamageTracker
    {
        private const double Alpha = 0.15; // EMA weight of each new hit (higher = adapts faster, noisier)

        private sealed class Stat { public long Total; public int Count; public double AvgPerHit; }

        private readonly object _lock = new object();
        private readonly Dictionary<string, Stat> _byAbility = new Dictionary<string, Stat>();

        /// <summary>Record one damage event (a single hit or DoT tick) for an ability.</summary>
        public void Record(string ability, long amount)
        {
            if (string.IsNullOrEmpty(ability) || amount <= 0) return;
            lock (_lock)
            {
                if (!_byAbility.TryGetValue(ability, out Stat s)) { s = new Stat(); _byAbility[ability] = s; }
                s.Total += amount;
                s.Count++;
                s.AvgPerHit = s.Count == 1 ? amount : s.AvgPerHit * (1 - Alpha) + amount * Alpha;
            }
        }

        /// <summary>Decaying average damage per hit for an ability (0 if never seen).</summary>
        public double AveragePerHit(string ability)
        {
            lock (_lock) return _byAbility.TryGetValue(ability, out Stat s) ? s.AvgPerHit : 0.0;
        }

        public int HitCount(string ability)
        {
            lock (_lock) return _byAbility.TryGetValue(ability, out Stat s) ? s.Count : 0;
        }

        /// <summary>Top abilities by total damage so far, for the development log. Null if nothing recorded.</summary>
        public string Report(int top = 6)
        {
            lock (_lock)
            {
                if (_byAbility.Count == 0) return null;
                var sb = new StringBuilder("dmg:");
                foreach (KeyValuePair<string, Stat> kv in _byAbility.OrderByDescending(k => k.Value.Total).Take(top))
                    sb.Append($" {kv.Key}={kv.Value.Total}(n{kv.Value.Count},~{kv.Value.AvgPerHit:0})");
                return sb.ToString();
            }
        }
    }
}
