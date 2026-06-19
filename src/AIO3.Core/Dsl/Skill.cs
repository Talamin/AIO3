using System;
using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Dsl
{
    /// <summary>Entry point for the fluent step DSL: <c>Skill.Spell("Ice Lance").Priority(15)...</c></summary>
    public static class Skill
    {
        public static SpellStep Spell(string name) => new SpellStep(name);
    }

    /// <summary>
    /// Fluent builder for a spell step. Known/ready/GCD gating is added automatically so
    /// a spec only declares the *interesting* condition, not the boilerplate.
    /// </summary>
    public sealed class SpellStep
    {
        private readonly string _spell;
        private float _priority = 100f;
        private Func<CombatContext, IEnumerable<IWowUnit>> _targets = Targets.Current;
        private Func<CombatContext, IWowUnit, bool> _when = (ctx, t) => true;
        private Exclusive _exclusive;
        private bool _ignoreGcd;
        private int _recastDelayMs;

        public SpellStep(string spell) => _spell = spell;

        /// <summary>Lower wins. Mirrors the old float-priority convention.</summary>
        public SpellStep Priority(float priority) { _priority = priority; return this; }

        public SpellStep On(Func<CombatContext, IEnumerable<IWowUnit>> targets) { _targets = targets; return this; }

        public SpellStep When(Func<CombatContext, IWowUnit, bool> condition) { _when = condition; return this; }

        public SpellStep When(Func<CombatContext, bool> condition) { _when = (ctx, t) => condition(ctx); return this; }

        public SpellStep WithToken(Exclusive exclusive) { _exclusive = exclusive; return this; }

        public SpellStep OffGcd() { _ignoreGcd = true; return this; }

        /// <summary>Throttle: once fired, don't fire again for <paramref name="ms"/> (e.g. Charge, so it
        /// isn't re-issued every tick during its leap before the cooldown registers).</summary>
        public SpellStep RecastDelay(int ms) { _recastDelayMs = ms; return this; }

        public RotationStep Build()
        {
            string spell = _spell;
            Func<CombatContext, IWowUnit, bool> when = _when;

            return new RotationStep(
                name: spell,
                priority: _priority,
                targets: _targets,
                condition: (ctx, t) =>
                {
                    if (!ctx.Game.IsSpellKnown(spell) || !ctx.Game.IsSpellReady(spell)) return false;
                    float range = ctx.Game.SpellRange(spell);
                    if (range > 0f && t.Distance > range) return false; // out of cast range
                    return when(ctx, t);
                },
                action: (ctx, t) => ctx.Game.Cast(spell, t),
                exclusive: _exclusive,
                ignoreGcd: _ignoreGcd,
                recastDelayMs: _recastDelayMs);
        }

        /// <summary>Implicit build so a SpellStep can be dropped straight into a step list.</summary>
        public static implicit operator RotationStep(SpellStep step) => step.Build();
    }
}
