using AIO3.Core.Combat;
using Xunit;

namespace AIO3.Tests
{
    // The outcome-based back-off for behind-only abilities (feral Shred). The behind GEOMETRY can read "behind" while
    // the player is in front (a stale mob facing), so the server rejects the cast and it deals no damage; the guard
    // judges by that outcome (a later hit count above the count at cast = it landed) and holds the ability off when it
    // keeps missing. Pure + clock-injected, so these run offline. Grace = 800ms, back-off = 2s, 4s, 6s … capped 8s.
    public class PositionalGuardTests
    {
        [Fact]
        public void Not_suppressed_before_anything_is_cast()
        {
            var g = new PositionalGuard();
            Assert.False(g.Suppressed("Shred", hitsNow: 0, nowMs: 1000));
        }

        [Fact]
        public void A_cast_is_not_judged_until_the_grace_elapses()
        {
            var g = new PositionalGuard();
            g.OnCast("Shred", hitsNow: 0, nowMs: 0);
            Assert.False(g.Suppressed("Shred", hitsNow: 0, nowMs: 500)); // 500ms < 800ms grace → still pending, no verdict
        }

        [Fact]
        public void Suppressed_after_a_miss_once_the_grace_has_elapsed()
        {
            var g = new PositionalGuard();
            g.OnCast("Shred", hitsNow: 0, nowMs: 0);
            // 900ms later, still 0 hits → the cast dealt no damage (server-rejected) → back off.
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 900));
        }

        [Fact]
        public void Not_suppressed_when_the_cast_actually_landed()
        {
            var g = new PositionalGuard();
            g.OnCast("Shred", hitsNow: 0, nowMs: 0);
            // a Shred hit was recorded in the meantime (count 0 → 1) → it landed → no back-off.
            Assert.False(g.Suppressed("Shred", hitsNow: 1, nowMs: 900));
        }

        [Fact]
        public void Back_off_clears_after_its_window_then_a_second_miss_holds_longer()
        {
            var g = new PositionalGuard();
            // First miss → ~2s back-off from t=900.
            g.OnCast("Shred", hitsNow: 0, nowMs: 0);
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 900));   // suppressed until ~2900
            Assert.False(g.Suppressed("Shred", hitsNow: 0, nowMs: 3000)); // window elapsed → allowed to retry

            // Retry at 3000 also misses → second back-off is longer (~4s), so still suppressed past where the first ended.
            g.OnCast("Shred", hitsNow: 0, nowMs: 3000);
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 3900));  // resolves the 2nd miss → ~4s hold (until ~7900)
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 6000));  // past the 2s window (5900) but inside the 4s one
        }

        [Fact]
        public void A_landed_cast_resets_the_streak_so_the_next_miss_is_short_again()
        {
            var g = new PositionalGuard();
            g.OnCast("Shred", hitsNow: 0, nowMs: 0);
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 900));   // miss → failStreak 1
            g.OnCast("Shred", hitsNow: 0, nowMs: 3000);
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 3900));  // miss → failStreak 2 (~4s)

            // Now a Shred lands (hit count climbs) → streak resets.
            g.OnCast("Shred", hitsNow: 1, nowMs: 8000);
            Assert.False(g.Suppressed("Shred", hitsNow: 2, nowMs: 8900)); // landed → not suppressed

            // The streak is back to 0, so a fresh miss only costs the short (~2s) back-off, not the grown one.
            g.OnCast("Shred", hitsNow: 2, nowMs: 12000);
            Assert.True(g.Suppressed("Shred", hitsNow: 2, nowMs: 12900)); // suppressed until ~14900
            Assert.False(g.Suppressed("Shred", hitsNow: 2, nowMs: 15000)); // ~2s window already over → reset confirmed
        }

        [Fact]
        public void Abilities_are_tracked_independently()
        {
            var g = new PositionalGuard();
            g.OnCast("Shred", hitsNow: 0, nowMs: 0);
            Assert.True(g.Suppressed("Shred", hitsNow: 0, nowMs: 900));
            // Backstab was never cast → its own state is clean.
            Assert.False(g.Suppressed("Backstab", hitsNow: 0, nowMs: 900));
        }
    }
}
