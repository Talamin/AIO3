using AIO3.Core.Combat;
using Xunit;

namespace AIO3.Tests
{
    public class InterruptTrackerTests
    {
        [Fact]
        public void Unknown_spell_is_interruptible_until_proven_otherwise()
        {
            var t = new InterruptTracker();
            Assert.True(t.ShouldInterrupt(100));
        }

        [Fact]
        public void Cast_that_completes_after_an_attempt_is_blacklisted()
        {
            var t = new InterruptTracker();
            t.RecordAttempt(targetGuid: 7, spellId: 100);

            bool added = t.OnCastCompleted(sourceGuid: 7, spellId: 100);

            Assert.True(added);
            Assert.False(t.ShouldInterrupt(100));
        }

        [Fact]
        public void Successful_interrupt_keeps_the_spell_interruptible()
        {
            var t = new InterruptTracker();
            t.RecordAttempt(7, 100);
            t.OnInterruptSucceeded(100);

            Assert.True(t.ShouldInterrupt(100));
            // pending was cleared, so a later completion does not blacklist it
            Assert.False(t.OnCastCompleted(7, 100));
            Assert.True(t.ShouldInterrupt(100));
        }

        [Fact]
        public void Completion_without_a_matching_attempt_does_not_blacklist()
        {
            var t = new InterruptTracker();
            Assert.False(t.OnCastCompleted(7, 100));          // no attempt
            t.RecordAttempt(7, 100);
            Assert.False(t.OnCastCompleted(9, 100));          // different source
            Assert.True(t.ShouldInterrupt(100));
        }

        [Fact]
        public void Blacklist_round_trips_through_serialization()
        {
            var t = new InterruptTracker();
            t.RecordAttempt(1, 55);
            t.OnCastCompleted(1, 55);

            var restored = new InterruptTracker();
            restored.Load(t.Serialize());

            Assert.False(restored.ShouldInterrupt(55));
        }
    }
}
