using System;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Rotations.Paladin
{
    /// <summary>
    /// Shared paladin building blocks reused by every spec, so cross-cutting behaviour (the seal / aura /
    /// blessing / judgement system, cooldowns, self-healing) is written once and stays consistent. Each
    /// returns a ready RotationStep; the spec just lists them in priority order alongside its signature
    /// abilities.
    ///
    /// The buff blocks resolve their spell name at eval time (not baked into the step) so that changing the
    /// choice in the overlay takes effect live, and an "Auto" choice can pick a spec-appropriate default and
    /// fall back as the player levels and learns better options.
    /// </summary>
    public static class PaladinCommon
    {
        /// <summary>Consecration is an 8-tick (~8s) ground AoE — don't drop a fresh one on a pack already this low
        /// (HP%); it dies before the ground effect pays off, wasting the mana/GCD. Restores the old AIO's
        /// <c>HealthPercent > 25</c> floor. HP-floor heuristic (no time-to-die seam). Shared so Prot and Ret agree.</summary>
        public const int ConsecrationMinTargetHealth = 25;

        /// <summary>Radius (yards) used to count the pack when deciding whether Consecration is worth dropping. The old
        /// AIO sized this decision at 15y (<c>Enemies.Count(GetDistance &lt;= 15) &gt;= ProtConsecration</c>); an 8y
        /// count under-reports a spread pull and skips a legitimately large pack. Shared so Prot/Ret agree.</summary>
        public const float ConsecrationPackRadius = 15f;

        // --- buff upkeep (seal / aura / blessing) -------------------------------------------------

        /// <summary>Keep the chosen seal up (instant; safe to maintain out of combat). "Auto" picks a
        /// spec-appropriate seal, falling back to the always-available Seal of Righteousness.</summary>
        public static RotationStep Seal(PaladinSpec spec, PaladinSettings s, float priority) =>
            SelfBuffDynamic("Seal", priority, ctx => ResolveSeal(ctx, spec, s.Seal.Value));

        /// <summary>Keep the chosen aura up. "Auto" = Retribution Aura (Ret) / Devotion Aura (Prot).</summary>
        public static RotationStep Aura(PaladinSpec spec, PaladinSettings s, float priority) =>
            SelfBuffDynamic("Aura", priority, ctx => ResolveAura(ctx, spec, s.Aura.Value));

        /// <summary>Keep the chosen self-blessing up. "Auto" = Sanctuary (Prot) / Kings, else Might.</summary>
        public static RotationStep Blessing(PaladinSpec spec, PaladinSettings s, float priority) =>
            SelfBuffDynamic("Blessing", priority, ctx => ResolveBlessing(ctx, spec, s.Blessing.Value));

        /// <summary>A self-buff whose spell name is resolved each tick: cast the resolved buff when it is
        /// known and not already up. A null resolved name leaves the step idle.</summary>
        private static RotationStep SelfBuffDynamic(string label, float priority, Func<CombatContext, string> resolve) =>
            new RotationStep(
                name: label,
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    string spell = resolve(ctx);
                    return spell != null && !ctx.Me.HasAura(spell);
                },
                action: (ctx, t) =>
                {
                    string spell = resolve(ctx);
                    return spell != null ? ctx.Game.Cast(spell, ctx.Me) : CastResult.Failed;
                });

        // --- judgement (on-cooldown debuff + seal trigger) ----------------------------------------

        /// <summary>Judge the active seal on cooldown. "Auto" = Judgement of Wisdom (mana return) if known,
        /// else Judgement of Light. Resolved at eval time with the known/ready/range gating the DSL adds.</summary>
        public static RotationStep Judgement(PaladinSettings s, float priority) =>
            CastOnEnemyDynamic("Judgement", priority, ctx => ResolveJudgement(ctx, s.Judgement.Value));

        /// <summary>An offensive cast whose spell name is resolved each tick, on the current enemy, with the
        /// known / ready / range gating the DSL normally adds for a fixed spell.</summary>
        private static RotationStep CastOnEnemyDynamic(string label, float priority, Func<CombatContext, string> resolve) =>
            new RotationStep(
                name: label,
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) =>
                {
                    string spell = resolve(ctx);
                    if (spell == null || !ctx.Game.IsSpellKnown(spell) || !ctx.Game.IsSpellReady(spell)) return false;
                    float range = ctx.Game.SpellRange(spell);
                    return !(range > 0f && t.Distance > range);
                },
                action: (ctx, t) =>
                {
                    string spell = resolve(ctx);
                    return spell != null ? ctx.Game.Cast(spell, t) : CastResult.Failed;
                });

        // --- cooldowns / survival -----------------------------------------------------------------

        /// <summary>Avenging Wrath: major offensive cooldown on a boss/elite or a pack, when enabled.</summary>
        public static RotationStep AvengingWrath(PaladinSettings s, float priority) =>
            Skill.Spell("Avenging Wrath").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && ctx.HasEnemyTarget
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite
                                  || ctx.EnemiesWithin(10f) >= s.AoeThreshold.Value));

        /// <summary>Divine Protection (damage reduction) when several enemies are attacking us.</summary>
        public static RotationStep DivineProtection(PaladinSettings s, float priority) =>
            Skill.Spell("Divine Protection").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseDivineProtection.Value && ctx.EnemiesTargetingMe >= 2);

        /// <summary>Emergency full heal; long cooldown, used only when critically low (0 disables it). Lay on Hands
        /// itself applies <c>Forbearance</c> and is BLOCKED by it, so gate on its absence — otherwise at low HP with
        /// Forbearance up the step keeps returning Failed every tick, burning the panic slot while a real heal could
        /// land (old AIO: <c>!Me.HaveBuff("Forbearance")</c>, SoloProtection.cs:20).</summary>
        public static RotationStep LayOnHands(PaladinSettings s, float priority) =>
            Skill.Spell("Lay on Hands").Priority(priority).On(Targets.Self)
                 .When(ctx => s.LayOnHandsPercent.Value > 0
                              && ctx.Me.HealthPercent < s.LayOnHandsPercent.Value
                              && !ctx.Me.HasAura("Forbearance"));

        /// <summary>Hand of Freedom: break a root/snare so a rooted paladin doesn't just stand there. Off the GCD, so
        /// it fires even mid-rotation. Gated on the real movement-flag bit via <c>ctx.Game.PlayerIsRooted</c> (old AIO:
        /// <c>Me.Rooted</c>, SoloRetribution.cs:30 / SoloProtection.cs:37). IsSpellKnown auto-skips until learned.</summary>
        public static RotationStep HandOfFreedom(float priority) =>
            Skill.Spell("Hand of Freedom").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Game.PlayerIsRooted).OffGcd();

        /// <summary>Hard-cast Holy Light on yourself below the self-heal threshold (0 disables it).</summary>
        public static RotationStep HolyLightSelf(PaladinSettings s, float priority) =>
            Skill.Spell("Holy Light").Priority(priority).On(Targets.Self)
                 .When(ctx => s.SelfHealPercent.Value > 0 && ctx.Me.HealthPercent <= s.SelfHealPercent.Value);

        /// <summary>Free instant Flash of Light from a "The Art of War" proc, below its threshold (0 disables).</summary>
        public static RotationStep ArtOfWarFlash(PaladinSettings s, float priority) =>
            Skill.Spell("Flash of Light").Priority(priority).On(Targets.Self)
                 .When(ctx => s.ArtOfWarHealPercent.Value > 0
                              && ctx.Me.HasAura("The Art of War")
                              && ctx.Me.HealthPercent <= s.ArtOfWarHealPercent.Value);

        /// <summary>Refill mana with Divine Plea below the mana threshold (0 disables it).</summary>
        public static RotationStep DivinePlea(PaladinSettings s, float priority) =>
            Skill.Spell("Divine Plea").Priority(priority).On(Targets.Self)
                 .When(ctx => s.DivinePleaManaPercent.Value > 0 && ctx.Me.PowerPercent < s.DivinePleaManaPercent.Value);

        /// <summary>Hammer of Wrath: ranged execute, only usable below 20% (IsSpellReady gates the window).</summary>
        public static RotationStep HammerOfWrath(float priority) =>
            Skill.Spell("Hammer of Wrath").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.HealthPercent < 20);

        /// <summary>Holy Wrath: AoE Holy damage + stun, only worthwhile against an undead/demon pack
        /// (it only hits those creature types). Shared so the gate lives in one place for every spec.</summary>
        public static RotationStep HolyWrath(PaladinSettings s, float priority) =>
            Skill.Spell("Holy Wrath").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= s.AoeThreshold.Value
                              && (ctx.Target.CreatureType == "Undead" || ctx.Target.CreatureType == "Demon"));

        // --- resolvers ----------------------------------------------------------------------------

        private static string ResolveSeal(CombatContext ctx, PaladinSpec spec, string choice)
        {
            if (choice != "Auto")
                return ctx.Game.IsSpellKnown(choice) ? choice : Known(ctx, "Seal of Righteousness");

            // Auto: spec-appropriate, then fall back to the always-available Seal of Righteousness.
            if (spec == PaladinSpec.Protection)
            {
                if (ctx.Game.IsSpellKnown("Seal of Vengeance")) return "Seal of Vengeance";   // Alliance threat seal
                if (ctx.Game.IsSpellKnown("Seal of Corruption")) return "Seal of Corruption"; // Horde threat seal
            }
            if (ctx.Game.IsSpellKnown("Seal of Command")) return "Seal of Command";
            return Known(ctx, "Seal of Righteousness");
        }

        private static string ResolveAura(CombatContext ctx, PaladinSpec spec, string choice)
        {
            if (choice != "Auto")
                return ctx.Game.IsSpellKnown(choice) ? choice : null;

            string preferred = spec == PaladinSpec.Protection ? "Devotion Aura" : "Retribution Aura";
            if (ctx.Game.IsSpellKnown(preferred)) return preferred;
            return Known(ctx, "Devotion Aura"); // the baseline aura every paladin has
        }

        private static string ResolveBlessing(CombatContext ctx, PaladinSpec spec, string choice)
        {
            if (choice != "Auto")
                return ctx.Game.IsSpellKnown(choice) ? choice : Known(ctx, "Blessing of Might");

            if (spec == PaladinSpec.Protection && ctx.Game.IsSpellKnown("Blessing of Sanctuary")) return "Blessing of Sanctuary";
            if (ctx.Game.IsSpellKnown("Blessing of Kings")) return "Blessing of Kings";
            return Known(ctx, "Blessing of Might");
        }

        private static string ResolveJudgement(CombatContext ctx, string choice)
        {
            if (choice != "Auto")
                return choice;
            return ctx.Game.IsSpellKnown("Judgement of Wisdom") ? "Judgement of Wisdom" : "Judgement of Light";
        }

        private static string Known(CombatContext ctx, string spell) =>
            ctx.Game.IsSpellKnown(spell) ? spell : null;
    }
}
