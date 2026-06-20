using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AIO3.Core.Combat;

namespace AIO3.Core.Engine
{
    /// <summary>
    /// Layer 2 — the priority-list runner. Sorts steps once by priority, then on each
    /// tick walks them in order and fires the first one that wants to. Honours the
    /// global cooldown unless a step opts out.
    /// </summary>
    public sealed class RotationEngine
    {
        private readonly RotationStep[] _steps;

        // Always-on, near-free timing (a couple of timestamp reads per step). The host drains and logs it
        // only when the debug toggle is on, so we can watch rotation timing during development without
        // paying for it in normal runs.
        private static readonly double MsPerStopwatchTick = 1000.0 / Stopwatch.Frequency;
        private readonly Dictionary<string, double> _stepMs = new Dictionary<string, double>();
        private double _tickMs;
        private int _tickCount;

        public RotationEngine(IEnumerable<RotationStep> steps)
        {
            _steps = steps.OrderBy(s => s.Priority).ToArray();
        }

        public IReadOnlyList<RotationStep> Steps => _steps;

        /// <summary>
        /// Evaluate the priority list once. Returns the step that fired, or null if none did.
        /// </summary>
        public RotationStep Tick(CombatContext ctx)
        {
            long tickStart = Stopwatch.GetTimestamp();
            var exclusives = new ExclusiveSet();
            bool gcdActive = ctx.Game.GlobalCooldownRemainingMs > 0;

            RotationStep fired = null;
            foreach (RotationStep step in _steps)
            {
                if (gcdActive && !step.IgnoreGcd) continue;

                long stepStart = Stopwatch.GetTimestamp();
                bool executed = step.TryExecute(ctx, exclusives);
                Record(step.Name, (Stopwatch.GetTimestamp() - stepStart) * MsPerStopwatchTick);

                if (executed) { fired = step; break; }
            }

            _tickMs += (Stopwatch.GetTimestamp() - tickStart) * MsPerStopwatchTick;
            _tickCount++;
            return fired;
        }

        private void Record(string name, double ms) =>
            _stepMs[name] = _stepMs.TryGetValue(name, out double v) ? v + ms : ms;

        /// <summary>
        /// Average tick time and the most expensive steps (avg per tick) since the last call, then reset.
        /// Returns null if no ticks ran. The host logs this when the debug profiling toggle is on.
        /// </summary>
        public string DrainProfile()
        {
            if (_tickCount == 0) return null;

            var sb = new StringBuilder();
            sb.Append($"tick ~{_tickMs / _tickCount:0.0}ms x{_tickCount}");
            foreach (KeyValuePair<string, double> kv in _stepMs.OrderByDescending(k => k.Value).Take(4))
                sb.Append($" | {kv.Key} {kv.Value / _tickCount:0.0}ms");

            _stepMs.Clear();
            _tickMs = 0;
            _tickCount = 0;
            return sb.ToString();
        }
    }
}
