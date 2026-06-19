using System.Collections.Generic;

namespace AIO3.Core.Combat
{
    public static class InterruptModes
    {
        public const string Smart = "Smart";   // empirically learn what is interruptible
        public const string Always = "Always"; // try to interrupt every cast
        public const string Never = "Never";   // do not interrupt (e.g. a product handles it)

        public static readonly string[] All = { Smart, Always, Never };
    }

    /// <summary>
    /// Learns which casts are actually (non-)interruptible by observing outcomes, because the API's
    /// "interruptible" flag is unreliable (especially on private servers). The rotation calls
    /// <see cref="RecordAttempt"/> when it fires an interrupt; the adapter feeds back combat-log
    /// results via <see cref="OnInterruptSucceeded"/> / <see cref="OnCastCompleted"/>. In Smart mode
    /// the rotation skips spells proven non-interruptible.
    /// </summary>
    public sealed class InterruptTracker
    {
        // Accessed from both the rotation loop and the combat-log event thread, so guard it.
        private readonly object _lock = new object();
        private readonly HashSet<int> _nonInterruptible = new HashSet<int>();
        private ulong _pendingGuid;
        private int _pendingSpellId;

        // Interrupt unless we've learned this spell is non-interruptible. spellId 0 (unreadable) can
        // never be blacklisted (OnCastCompleted ignores it), so an unknown-id cast is still attempted.
        public bool ShouldInterrupt(int spellId)
        {
            lock (_lock) return !_nonInterruptible.Contains(spellId);
        }

        public bool IsBlacklisted(int spellId)
        {
            lock (_lock) return _nonInterruptible.Contains(spellId);
        }

        public int BlacklistCount { get { lock (_lock) return _nonInterruptible.Count; } }

        /// <summary>The rotation just tried to interrupt this cast.</summary>
        public void RecordAttempt(ulong targetGuid, int spellId)
        {
            lock (_lock) { _pendingGuid = targetGuid; _pendingSpellId = spellId; }
        }

        /// <summary>Combat log confirms we interrupted this spell — it IS interruptible.</summary>
        public void OnInterruptSucceeded(int spellId)
        {
            lock (_lock) { _nonInterruptible.Remove(spellId); ClearPendingIf(spellId); }
        }

        /// <summary>
        /// A unit finished a cast. If we had just tried to interrupt that same cast and it completed
        /// anyway, the spell is non-interruptible. Returns true if a new spell was blacklisted.
        /// </summary>
        public bool OnCastCompleted(ulong sourceGuid, int spellId)
        {
            lock (_lock)
            {
                bool added = false;
                if (spellId != 0 && _pendingSpellId == spellId && _pendingGuid == sourceGuid)
                    added = _nonInterruptible.Add(spellId);
                ClearPendingIf(spellId);
                return added;
            }
        }

        private void ClearPendingIf(int spellId)
        {
            if (_pendingSpellId == spellId) { _pendingGuid = 0; _pendingSpellId = 0; }
        }

        public string Serialize()
        {
            lock (_lock) return string.Join(",", _nonInterruptible);
        }

        public void Load(string csv)
        {
            lock (_lock)
            {
                _nonInterruptible.Clear();
                if (string.IsNullOrEmpty(csv)) return;
                foreach (string part in csv.Split(','))
                    if (int.TryParse(part, out int id)) _nonInterruptible.Add(id);
            }
        }
    }
}
