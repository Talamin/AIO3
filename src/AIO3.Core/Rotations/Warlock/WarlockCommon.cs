using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;

namespace AIO3.Core.Rotations.Warlock
{
    /// <summary>
    /// Shared warlock building blocks — the "caster baseline" every warlock spec composes: armor upkeep, the
    /// Life Tap mana engine (the signature warlock trade of health for mana), the Drain Life self-heal, and the
    /// low-mana wand. Spell names that have a player choice (the armor, the curse, which demon to summon) are
    /// resolved at EVAL TIME so overlay edits apply live and an "Auto"/best-known pick fills in as the player
    /// levels.
    ///
    /// Cast-time / channelled spells (Drain Life) gate on <c>!PlayerIsMoving</c> in the spec; instants
    /// (Life Tap, Corruption, the curses) do not. The wand is off the GCD.
    /// </summary>
    public static class WarlockCommon
    {
        /// <summary>Keep the right armor up. The chosen armor (or the best-known via Auto) is resolved at eval
        /// time and cast when missing.</summary>
        public static RotationStep Armor(WarlockSettings s, float priority) =>
            new RotationStep(
                name: "Armor",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    string armor = ResolveArmor(ctx, s);
                    return armor != null && !ctx.Me.HasAura(armor);
                },
                action: (ctx, t) =>
                {
                    string armor = ResolveArmor(ctx, s);
                    return armor != null ? ctx.Game.Cast(armor, ctx.Me) : CastResult.Failed;
                });

        /// <summary>Life Tap — trade health for mana when mana is low, but only while health is above the floor
        /// (so we never tap ourselves into danger). Instant, so it does not gate on movement.</summary>
        public static RotationStep LifeTap(WarlockSettings s, float priority) =>
            Skill.Spell("Life Tap").Priority(priority).On(Targets.Self)
                 .When(ctx => ctx.Me.PowerPercent < s.LifeTapManaPercent.Value
                              && ctx.Me.HealthPercent > s.LifeTapHealthFloor.Value);

        /// <summary>Keep the Glyph of Life Tap spell-power buff up: re-tap when the buff is missing and health is
        /// safe (a higher floor than the mana tap — this one is purely for uptime, never worth risking HP).
        /// Opt-in via the GlyphLifeTap toggle.</summary>
        public static RotationStep GlyphLifeTap(WarlockSettings s, float priority) =>
            Skill.Spell("Life Tap").Priority(priority).On(Targets.Self)
                 .When(ctx => s.GlyphLifeTap.Value
                              && !ctx.Me.HasAura("Life Tap")
                              && ctx.Me.HealthPercent > s.LifeTapHealthFloor.Value);

        /// <summary>Channel Drain Life to self-heal when low and solo. It is a channel, so it gates on
        /// <c>!PlayerIsMoving</c> (you cannot start it on the move) right here in the shared block, so every
        /// spec that composes it gets the same correct behaviour with one call.</summary>
        public static RotationStep DrainLife(WarlockSettings s, float priority) =>
            Skill.Spell("Drain Life").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.DrainLifeHealthPercent.Value > 0
                              && !ctx.IsInGroup
                              && ctx.Me.HealthPercent < s.DrainLifeHealthPercent.Value
                              && !ctx.Game.PlayerIsMoving);

        /// <summary>Wand (Shoot) the target to conserve mana when low — needs a wand equipped (auto-skips
        /// otherwise) and only when not already wanding. Off the GCD.</summary>
        public static RotationStep Wand(WarlockSettings s, float priority) =>
            Skill.Spell("Shoot").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseWand.Value
                              && ctx.Me.PowerPercent < s.WandManaPercent.Value
                              && !ctx.Game.IsCurrentSpell("Shoot")).OffGcd();

        /// <summary>The summon spell for the chosen demon (e.g. "Summon Voidwalker"). Resolved at eval time so
        /// an overlay edit swaps the demon live and an "Auto" pick fills in per spec. Used by the shared
        /// PetControl.Summon call so the spec keeps the right pet up. The <paramref name="spec"/> only matters
        /// for Auto: Demonology → Felguard, Destruction → Imp, everything else → Voidwalker; a known-spell
        /// fallback drops to the tanky Voidwalker when the spec demon is not learned yet.</summary>
        public static string SummonSpell(WarlockSettings s, CombatContext ctx, WarlockSpec spec) =>
            "Summon " + ResolvePet(s, ctx, spec);

        /// <summary>Resolve which demon to summon: a manual choice wins; "Auto" picks the spec-appropriate demon,
        /// falling back to the Voidwalker when that demon's summon spell is not known yet (low level).</summary>
        public static string ResolvePet(WarlockSettings s, CombatContext ctx, WarlockSpec spec)
        {
            string choice = s.Pet.Value;
            if (choice != "Auto") return choice;

            string preferred;
            switch (spec)
            {
                case WarlockSpec.Demonology: preferred = "Felguard"; break;
                case WarlockSpec.Destruction: preferred = "Imp"; break;
                default: preferred = "Voidwalker"; break;
            }
            if (ctx != null && !ctx.Game.IsSpellKnown("Summon " + preferred)) return "Voidwalker";
            return preferred;
        }

        /// <summary>The curse spell to maintain, from the chosen curse setting. Resolved at eval time so an
        /// overlay edit swaps the curse live.</summary>
        public static string CurseSpell(WarlockSettings s)
        {
            switch (s.Curse.Value)
            {
                case "Doom": return "Curse of Doom";
                case "Elements": return "Curse of the Elements";
                case "Tongues": return "Curse of Tongues";
                case "Weakness": return "Curse of Weakness";
                default: return "Curse of Agony";
            }
        }

        /// <summary>Maintain the chosen curse on the current target — cast when missing. The curse name is
        /// resolved each tick (<see cref="CurseSpell"/>), so swapping the curse setting takes effect live; the
        /// known/ready gate then auto-skips a curse the warlock has not learned yet. (Only one curse can be up
        /// at a time, so we just check it is missing rather than a refresh window.)</summary>
        public static RotationStep MaintainCurse(WarlockSettings s, float priority) =>
            new RotationStep(
                name: "Curse",
                priority: priority,
                targets: Targets.CurrentEnemy,
                condition: (ctx, t) =>
                {
                    string curse = CurseSpell(s);
                    if (!ctx.Game.IsSpellKnown(curse) || !ctx.Game.IsSpellReady(curse)) return false;
                    float range = ctx.Game.SpellRange(curse);
                    if (range > 0f && t.Distance > range) return false;
                    return !t.HasMyAura(curse);
                },
                action: (ctx, t) => ctx.Game.Cast(CurseSpell(s), t));

        private static string ResolveArmor(CombatContext ctx, WarlockSettings s)
        {
            string choice = s.ArmorChoice.Value;
            if (choice != "Auto" && ctx.Game.IsSpellKnown(choice)) return choice;

            // Auto: best known. Fel Armor supersedes Demon Armor supersedes Demon Skin.
            string[] prefs = { "Fel Armor", "Demon Armor", "Demon Skin" };
            foreach (string a in prefs)
                if (ctx.Game.IsSpellKnown(a)) return a;
            return null;
        }
    }
}
