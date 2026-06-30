using System.Collections.Generic;

namespace AIO3.Core.Combat
{
    /// <summary>
    /// Self-correcting suppressor for POSITIONAL abilities (behind-only: the feral's Shred, the rogue's
    /// Backstab/Ambush/Garrote). The behind-detection geometry (<see cref="Game.IGameClient.PlayerIsBehindTarget"/>)
    /// reads the target's facing from memory, which LAGS while a mob continuously turns to face a strafing melee bot —
    /// so it can report "behind" for longer than the debounce while the player is squarely in FRONT. The client cast
    /// then "succeeds" but the SERVER rejects it ("must be behind"), so the ability deals ZERO damage and the GCD is
    /// wasted (observed live: 31 Shred casts, 0 Shred damage all session). Geometry tuning can't fix a stale memory
    /// read, so we judge by OUTCOME instead: after casting a positional we wait a short grace for its combat-log
    /// damage event to arrive; if nothing landed, we back off (exponential, capped) and the spec's front-fallback
    /// builder (Mangle / Claw) takes the GCDs. A positional that genuinely lands (a real behind position) resets the
    /// streak and flows normally. Pure + clock-injected so it unit-tests offline with no WRobot.
    /// </summary>
    public sealed class PositionalGuard
    {
        private const int GraceMs = 800;        // wait for the async combat-log damage event before judging a cast
        private const int BackoffStepMs = 2000; // suppression grows 2s, 4s, 6s … per consecutive miss
        private const int BackoffCapMs = 8000;

        private sealed class S
        {
            public int CastAtMs;
            public int HitsAtCast;
            public bool Pending;
            public int FailStreak;
            public int SuppressUntilMs;
        }

        private readonly Dictionary<string, S> _byAbility = new Dictionary<string, S>();

        private S Get(string ability)
        {
            if (!_byAbility.TryGetValue(ability, out S s)) { s = new S(); _byAbility[ability] = s; }
            return s;
        }

        /// <summary>Note that we just cast <paramref name="ability"/> at <paramref name="nowMs"/>. <paramref
        /// name="hitsNow"/> is the ability's current landed-hit count (from the <see cref="DamageTracker"/>); a later
        /// hit count above this is how we know the cast actually landed.</summary>
        public void OnCast(string ability, int hitsNow, int nowMs)
        {
            S s = Get(ability);
            s.CastAtMs = nowMs;
            s.HitsAtCast = hitsNow;
            s.Pending = true;
        }

        /// <summary>True while <paramref name="ability"/> is backed off after a missed (server-rejected) cast. Resolves
        /// a pending cast once the grace has elapsed — landed → reset the streak; missed → grow the back-off. Call it
        /// every tick with the live hit count + clock (it both judges the last cast and reports the current state).</summary>
        public bool Suppressed(string ability, int hitsNow, int nowMs)
        {
            S s = Get(ability);
            if (s.Pending && unchecked(nowMs - s.CastAtMs) >= GraceMs)
            {
                if (hitsNow > s.HitsAtCast)
                {
                    s.FailStreak = 0;
                    s.SuppressUntilMs = nowMs; // it landed — clear any back-off
                }
                else
                {
                    s.FailStreak++;
                    int backoff = System.Math.Min(BackoffCapMs, BackoffStepMs * s.FailStreak);
                    s.SuppressUntilMs = nowMs + backoff; // it missed — hold off, longer each consecutive miss
                }
                s.Pending = false;
            }
            return unchecked(nowMs - s.SuppressUntilMs) < 0;
        }
    }
}
