using System;
using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Game;

namespace AIO3.Core.Engine
{
    /// <summary>
    /// One entry in a priority list: a named action with a priority, a set of candidate
    /// targets, a condition, and the action itself. Lower <see cref="Priority"/> wins.
    /// Steps are plain data — built once, never reallocated per access (a bug in the old
    /// code where <c>Rotation =&gt; new List&lt;...&gt;()</c> rebuilt every read).
    /// </summary>
    public sealed class RotationStep
    {
        private readonly Func<CombatContext, IEnumerable<IWowUnit>> _targets;
        private readonly Func<CombatContext, IWowUnit, bool> _condition;
        private readonly Func<CombatContext, IWowUnit, CastResult> _action;

        public string Name { get; }
        public float Priority { get; }
        public Exclusive Exclusive { get; }

        /// <summary>If true, the step may still fire while the global cooldown is active.</summary>
        public bool IgnoreGcd { get; }

        public RotationStep(
            string name,
            float priority,
            Func<CombatContext, IEnumerable<IWowUnit>> targets,
            Func<CombatContext, IWowUnit, bool> condition,
            Func<CombatContext, IWowUnit, CastResult> action,
            Exclusive exclusive = null,
            bool ignoreGcd = false)
        {
            Name = name;
            Priority = priority;
            _targets = targets;
            _condition = condition;
            _action = action;
            Exclusive = exclusive;
            IgnoreGcd = ignoreGcd;
        }

        /// <summary>
        /// Evaluate against each candidate target; fire on the first one whose condition
        /// holds and whose action succeeds. Returns true if the step fired.
        /// </summary>
        public bool TryExecute(CombatContext ctx, ExclusiveSet exclusives)
        {
            foreach (IWowUnit target in _targets(ctx))
            {
                if (target == null) continue;

                // Another step already claimed this target's exclusive slot.
                if (Exclusive != null && exclusives.Contains(target, Exclusive)) continue;

                if (!_condition(ctx, target)) continue;

                // Reserve the slot before acting so concurrent same-pass steps see it.
                if (Exclusive != null) exclusives.Add(target, Exclusive);

                CastResult result = _action(ctx, target);
                if (result == CastResult.Success) return true;

                // Cast did not go through — release the slot for other steps.
                if (Exclusive != null) exclusives.Remove(target, Exclusive);
            }

            return false;
        }

        public override string ToString() => $"[{Priority}] {Name}";
    }
}
