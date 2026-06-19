using System.Collections.Generic;
using System.Diagnostics;

namespace AIO3.Adapter
{
    /// <summary>
    /// Reads the player's spell-cooldown list straight from game memory in a single pass, instead of one
    /// slow <c>SpellManager.GetSpellCooldownTimeLeftBySpellName</c> call per spell (~30ms each — the
    /// rotation tick's dominant cost). Mirrors the old AIO's memory-based cooldown/GCD reading. The
    /// snapshot is cached with a short TTL so we walk the list at most once per tick.
    /// Addresses/offsets are the WotLK 3.3.5a values the old AIO uses against the same client.
    /// </summary>
    internal sealed class WRobotCooldowns
    {
        private const uint NowAddr = 0xCD76ACu;
        private const uint ListHeadAddr = 0xD3F5ACu + 8u;

        private readonly Dictionary<uint, int> _cooldownMs = new Dictionary<uint, int>();
        private readonly Stopwatch _age = Stopwatch.StartNew();
        private bool _primed;
        private int _gcdMs;

        public int GcdRemainingMs { get { EnsureFresh(); return _gcdMs; } }

        /// <summary>Remaining cooldown for a spell id in ms (0 = ready / not on cooldown).</summary>
        public int CooldownLeftMs(uint spellId)
        {
            EnsureFresh();
            return _cooldownMs.TryGetValue(spellId, out int v) ? v : 0;
        }

        private void EnsureFresh()
        {
            // One memory walk per tick is plenty; sub-tick callers reuse the snapshot.
            if (_primed && _age.ElapsedMilliseconds < 30) return;
            Refresh();
            _age.Restart();
            _primed = true;
        }

        private void Refresh()
        {
            _cooldownMs.Clear();
            _gcdMs = 0;
            try
            {
                var mem = wManager.Wow.Memory.WowMemory.Memory;
                int now = mem.ReadInt32(NowAddr);
                uint obj = mem.ReadPtr(ListHeadAddr);
                int guard = 0;
                while (obj != 0u && (obj & 1u) == 0u && guard++ < 1000)
                {
                    uint spellId = mem.ReadPtr(obj + 8u);
                    int start = mem.ReadInt32(obj + 0x10u);
                    int cd1 = mem.ReadInt32(obj + 0x14u);
                    int cd2 = mem.ReadInt32(obj + 0x20u);
                    int globalLength = mem.ReadInt32(obj + 0x2Cu);
                    int length = cd1 + cd2;
                    int cdleft = System.Math.Max(System.Math.Max(length, globalLength) - (now - start), 0);
                    if (cdleft > 0)
                    {
                        if (spellId != 0u) _cooldownMs[spellId] = cdleft;
                        // The GCD shows up as a cooldown entry with a ~1.0-1.5s "global" length.
                        if (globalLength >= 1000 && globalLength <= 1500 && cdleft <= 1500 && cdleft > _gcdMs)
                            _gcdMs = cdleft;
                    }
                    obj = mem.ReadPtr(obj + 4u);
                }
            }
            catch
            {
                // Bad read (rare, unlocked) — fail open: treat everything as ready for this tick.
                _cooldownMs.Clear();
                _gcdMs = 0;
            }
        }
    }
}
