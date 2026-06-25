using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Shared rogue building blocks — the melee / energy / combo-point baseline every rogue spec composes, so the
    /// cross-cutting behaviour (Slice and Dice uptime, the Eviscerate / Rupture finishers, the defensive
    /// cooldowns, the Sprint gap-closer, the stealth opener) is written ONCE and stays consistent. Each returns a
    /// ready RotationStep; the spec just lists them in priority order alongside its signature abilities.
    ///
    /// Finishers spend combo points (read via <see cref="CombatContext.ComboPoints"/>); builders cost energy
    /// (<c>ctx.Me.Energy</c>) and generate them. Known/ready/GCD/range gating is added automatically by the DSL,
    /// so these declare only the interesting condition; an unknown spell auto-skips, so the same list runs from
    /// level 10 up. Assassination (a later phase) reuses SnD / the finishers / the defensives / the stealth
    /// helper unchanged — only its filler differs.
    /// </summary>
    public static class RogueCommon
    {
        /// <summary>Below this range an enemy that is on us counts as "in melee" — the trigger for the surrounded
        /// defensives (Evasion) and the AoE / cooldown gates. One named constant so every melee gate uses the same
        /// radius (mirrors the old AIO's 10y enemy-count radius for these checks; deliberately wider than the
        /// 8y <see cref="Library.Racials.MeleePackRadius"/>, which keeps the old AIO's racial-specific radius).</summary>
        public const float MeleeRange = 10f;

        // --- shared "fight shape" gates (so the cooldowns and the spec read one definition of pack / lone elite) ---

        /// <summary>At least <paramref name="count"/> enemies within <see cref="MeleeRange"/> — the pack trigger for
        /// the AoE / cooldown gates. One source so Blade Flurry, Adrenaline Rush, and Killing Spree agree on what a
        /// "pack" is (and a future Group Combat rotation inherits the same definition).</summary>
        public static bool Pack(CombatContext ctx, int count) => ctx.EnemiesWithin(MeleeRange) >= count;

        /// <summary>A solo fight against an elite/boss — a long, dangerous fight worth a major cooldown even at a
        /// single target. Shared so the cooldowns and Evasion's lone-elite trigger use the same definition.</summary>
        public static bool LoneElite(CombatContext ctx) =>
            !ctx.IsInGroup && ctx.HasEnemyTarget && (ctx.Target.IsElite || ctx.Target.IsBoss());

        /// <summary>The "not while stealthed" opener guard, in one place — out-of-stealth abilities (the builder,
        /// the finishers, the cooldowns) wait until the stealth opener has fired.</summary>
        public static bool NotStealthed(CombatContext ctx) => !ctx.Game.PlayerIsStealthed;

        // --- burst / AoE cooldowns (gated on UseCooldowns; on a pack or a lone elite/boss, mirroring
        // WarriorCommon.Recklessness — written once here so a future Group Combat rotation composes them too) ---

        /// <summary>Adrenaline Rush — an energy-regen burst. Pop it on a pack (>= AdrenalineRushEnemies in melee) or
        /// a lone elite/boss. Self-cast, off the GCD. Auto-skips when unknown / on cooldown.</summary>
        public static RotationStep AdrenalineRush(RogueSettings s, float priority) =>
            Skill.Spell("Adrenaline Rush").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCooldowns.Value && NotStealthed(ctx)
                              && (Pack(ctx, s.AdrenalineRushEnemies.Value) || LoneElite(ctx)))
                 .OffGcd();

        /// <summary>Killing Spree — a melee-leap burst. Use on a pack (>= KillingSpreeEnemies) or a lone elite/boss,
        /// but not while Adrenaline Rush / Blade Flurry is already up (don't stack the bursts — the old AIO gate).</summary>
        public static RotationStep KillingSpree(RogueSettings s, float priority) =>
            Skill.Spell("Killing Spree").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseCooldowns.Value && NotStealthed(ctx)
                              && !ctx.Me.HasAura("Adrenaline Rush") && !ctx.Me.HasAura("Blade Flurry")
                              && (Pack(ctx, s.KillingSpreeEnemies.Value) || LoneElite(ctx)));

        /// <summary>Blade Flurry — cleave when a pack is in melee (>= BladeFlurryEnemies); its cleave is wasted on a
        /// single target. Self-cast, off the GCD. Not while stealthed.</summary>
        public static RotationStep BladeFlurry(RogueSettings s, float priority) =>
            Skill.Spell("Blade Flurry").Priority(priority).On(Targets.Self)
                 .When(ctx => NotStealthed(ctx) && Pack(ctx, s.BladeFlurryEnemies.Value))
                 .OffGcd();

        /// <summary>Slice and Dice cannot drop in a long fight — it is the rogue's core attack-speed buff. Keep it
        /// up: cast when the buff is missing AND we have at least <paramref name="minComboPoints"/> combo points to
        /// spend (1 is enough — even a 1-CP SnD is worth refreshing rather than letting it fall). It is a finisher,
        /// so it consumes combo points; the CP gate is the interesting condition (known/ready is automatic).</summary>
        public static RotationStep SliceAndDice(int minComboPoints, float priority) =>
            Skill.Spell("Slice and Dice").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Me.HasAura("Slice and Dice") && ctx.ComboPoints >= minComboPoints);

        /// <summary>Eviscerate — the direct-damage finisher. Spend it at the configured combo-point threshold (read
        /// each tick so an overlay edit applies live). Not while stealthed (openers come first).</summary>
        public static RotationStep Eviscerate(RogueSettings s, float priority) =>
            Skill.Spell("Eviscerate").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => NotStealthed(ctx) && ctx.ComboPoints >= s.FinisherComboPoints.Value);

        /// <summary>Rupture — the bleed finisher (opt-in via UseRupture). Spend it at the finisher CP threshold when
        /// the bleed is missing/expiring and the target is worth a damage-over-time (durable: an elite/boss, since
        /// trash dies before a full bleed pays off). Not on bleed-immune creatures, not while stealthed.</summary>
        public static RotationStep Rupture(RogueSettings s, float priority) =>
            Skill.Spell("Rupture").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseRupture.Value
                              && NotStealthed(ctx)
                              && ctx.ComboPoints >= s.FinisherComboPoints.Value
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite)
                              && ctx.Target.CreatureType != "Elemental"
                              && ctx.Target.CreatureType != "Mechanical"
                              && (!ctx.Target.HasMyAura("Rupture") || ctx.Target.MyAuraTimeLeftMs("Rupture") < 3000));

        /// <summary>Evasion — the dodge defensive. Fire when low on health, when surrounded (>= the configured
        /// attacker count meleeing us), or on a solo elite/boss fight (a long, dangerous fight). Self-cast, off the
        /// GCD. Mirrors the old SoloCombat's three Evasion triggers (HP, enemy count, lone elite).</summary>
        public static RotationStep Evasion(RogueSettings s, float priority) =>
            Skill.Spell("Evasion").Priority(priority).On(Targets.Self)
                 .When(ctx => (s.EvasionHealthPercent.Value > 0 && ctx.Me.HealthPercent < s.EvasionHealthPercent.Value)
                              || ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= MeleeRange) >= s.EvasionEnemies.Value
                              || LoneElite(ctx))
                 .OffGcd();

        /// <summary>Cloak of Shadows — wipe magic effects + spell resistance. Fire when we carry a Magic debuff
        /// (auto-skips when Cloak is unknown). Self-cast, off the GCD.</summary>
        public static RotationStep CloakOfShadows(RogueSettings s, float priority) =>
            Skill.Spell("Cloak of Shadows").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseCloakOfShadows.Value && ctx.Game.PlayerHasDebuffType("Magic"))
                 .OffGcd();

        /// <summary>Sprint — close the gap to a target out of melee range during a committed fight (the host only
        /// runs the rotation while the product is fighting, so this never fires during navigation). Off the GCD;
        /// throttled so it isn't re-issued every tick while the speed buff carries us in.</summary>
        public static RotationStep Sprint(RogueSettings s, float priority) =>
            Skill.Spell("Sprint").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseSprint.Value && ctx.HasEnemyTarget
                              && ctx.Target.Distance > MeleeRange && !ctx.Me.HasAura("Sprint"))
                 .OffGcd()
                 .RecastDelay(2000);

        /// <summary>Stealth — open from stealth out of combat (opt-in). Cast "Stealth" when not already stealthed
        /// and not in combat, so the spec's opener (Ambush / Garrote) can fire. A normal cast — no special
        /// handling. Self-cast. Gated tightly so it never fights a product that owns the pull.</summary>
        public static RotationStep Stealth(RogueSettings s, float priority) =>
            Skill.Spell("Stealth").Priority(priority).On(Targets.Self)
                 .When(ctx => s.UseStealth.Value
                              && !ctx.Game.PlayerInCombat
                              && !ctx.Game.PlayerIsStealthed
                              && !ctx.Game.PlayerIsMounted
                              && !ctx.Game.PlayerIsResting        // don't break off eating/drinking to recover
                              && !ctx.Game.PlayerHasHarmfulAura()); // a DoT tick would instantly break stealth

        /// <summary>Stealth opener — the first strike of a stealth-opened fight, the spell chosen by the
        /// <see cref="RogueSettings.StealthOpener"/> dropdown: Cheap Shot (positional-free, 4s stun + 2 combo
        /// points) or Garrote (bleed + silence, but must be cast from BEHIND the target). Fires only while
        /// stealthed and in melee range (the engine's range gate), so it breaks stealth to start the fight and
        /// the normal build-and-finish loop takes over. Sits above auto-attack so it lands before the swing
        /// breaks stealth. The RecastDelay is a safety net: if the chosen opener can't land (e.g. Garrote when
        /// not behind, or out of energy) it isn't re-issued every tick — auto-attack then opens instead.
        /// Unknown/unusable spell auto-skips (IsSpellKnown), so a low-level rogue just opens with auto-attack.</summary>
        public static RotationStep Opener(RogueSettings s, string spell, float priority) =>
            Skill.Spell(spell).Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseStealth.Value
                              && s.StealthOpener.Value == spell
                              && ctx.Game.PlayerIsStealthed)
                 .RecastDelay(2000);
    }
}
