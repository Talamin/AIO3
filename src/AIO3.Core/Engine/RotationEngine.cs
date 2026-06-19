using System.Collections.Generic;
using System.Linq;
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
            var exclusives = new ExclusiveSet();
            bool gcdActive = ctx.Game.GlobalCooldownRemainingMs > 0;

            foreach (RotationStep step in _steps)
            {
                if (gcdActive && !step.IgnoreGcd) continue;
                if (step.TryExecute(ctx, exclusives)) return step;
            }

            return null;
        }
    }
}
