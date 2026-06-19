using AIO3.Core.Dsl;
using AIO3.Core.Engine;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Shared warrior building blocks reused by every spec, so cross-cutting behaviour is written
    /// once and stays consistent (no drift). Each returns a ready RotationStep; the spec just lists
    /// them in priority order alongside its signature abilities.
    /// </summary>
    public static class WarriorCommon
    {
        /// <summary>Switch to the spec's stance when not already in it (no-op until the stance is learned).</summary>
        public static RotationStep EnsureStance(string stanceName, float priority) =>
            Skill.Spell(stanceName).Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Game.ActiveStanceName != stanceName).OffGcd();

        /// <summary>Build rage when an attackable target is in melee (off the GCD).</summary>
        public static RotationStep Bloodrage(float priority) =>
            Skill.Spell("Bloodrage").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && ctx.Target.Distance < 8f).OffGcd();

        public static RotationStep Intercept(WarriorSettings s, float priority) =>
            Skill.Spell("Intercept").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseGapClosers.Value
                              && ctx.Me.Rage > 10 && ctx.Target.Distance > 8f && ctx.Target.Distance <= 25f);

        public static RotationStep Charge(WarriorSettings s, float priority) =>
            Skill.Spell("Charge").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseGapClosers.Value
                              && ctx.Target.Distance > 8f && ctx.Target.Distance <= 25f
                              && !ctx.Game.IsSpellKnown("Intercept"));

        /// <summary>Slow a fleeing target below 40% (creature-type filtering is a later refinement).</summary>
        public static RotationStep Hamstring(WarriorSettings s, float priority) =>
            Skill.Spell("Hamstring").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseHamstring.Value && ctx.Target.HealthPercent < 40 && !ctx.Target.HasAura("Hamstring"));

        public static RotationStep Execute(float priority) =>
            Skill.Spell("Execute").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.HealthPercent < 20);

        /// <summary>Usable only after a killing blow; IsSpellReady gates the proc window.</summary>
        public static RotationStep VictoryRush(float priority) =>
            Skill.Spell("Victory Rush").Priority(priority).On(Targets.CurrentEnemy);

        public static RotationStep Rend(float priority) =>
            Skill.Spell("Rend").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Target.HasMyAura("Rend"));

        /// <summary>Off-GCD rage dump; lowest-priority "leftover" once spare rage is above the reserve.</summary>
        public static RotationStep HeroicStrike(WarriorSettings s, float priority) =>
            Skill.Spell("Heroic Strike").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > s.HeroicStrikeRageReserve.Value).OffGcd();

        /// <summary>Off-GCD AoE rage dump (use over Heroic Strike when several enemies are in range).</summary>
        public static RotationStep Cleave(WarriorSettings s, float priority) =>
            Skill.Spell("Cleave").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= s.AoeThreshold.Value).OffGcd();
    }
}
