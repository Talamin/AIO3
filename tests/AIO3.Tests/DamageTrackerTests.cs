using AIO3.Core.Combat;
using Xunit;

namespace AIO3.Tests
{
    public class DamageTrackerTests
    {
        [Fact]
        public void Accumulates_total_and_count_per_ability()
        {
            var t = new DamageTracker();
            t.Record("Mortal Strike", 100);
            t.Record("Mortal Strike", 200);
            t.Record("Rend", 30);

            Assert.Equal(2, t.HitCount("Mortal Strike"));
            Assert.Equal(1, t.HitCount("Rend"));
        }

        [Fact]
        public void First_hit_seeds_the_average_then_it_decays_toward_new_hits()
        {
            var t = new DamageTracker();
            t.Record("Bloodthirst", 100);
            Assert.Equal(100.0, t.AveragePerHit("Bloodthirst"), 3); // first hit = the value itself

            t.Record("Bloodthirst", 300);
            // EMA: 100*(1-0.15) + 300*0.15 = 85 + 45 = 130
            Assert.Equal(130.0, t.AveragePerHit("Bloodthirst"), 3);
        }

        [Fact]
        public void Ignores_zero_negative_and_empty()
        {
            var t = new DamageTracker();
            t.Record("X", 0);
            t.Record("X", -5);
            t.Record("", 100);
            t.Record(null, 100);

            Assert.Equal(0, t.HitCount("X"));
            Assert.Null(t.Report());
        }

        [Fact]
        public void Unseen_ability_reports_zero()
        {
            var t = new DamageTracker();
            Assert.Equal(0.0, t.AveragePerHit("Nope"));
            Assert.Equal(0, t.HitCount("Nope"));
        }

        [Fact]
        public void Report_orders_by_total_damage()
        {
            var t = new DamageTracker();
            t.Record("Weak", 10);
            t.Record("Strong", 500);
            t.Record("Strong", 500);

            string report = t.Report();
            Assert.NotNull(report);
            Assert.True(report.IndexOf("Strong") < report.IndexOf("Weak"), "Strong should be listed before Weak");
        }
    }
}
