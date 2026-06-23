using System;
using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;

namespace AIO3.Core.Library
{
    /// <summary>
    /// Shared, class-agnostic racials — the AIO3 equivalent of the old AIO RacialManager. Every class appends
    /// this bundle once (via <see cref="With"/>) instead of wiring racials per spec. Each step is gated by
    /// <c>IsSpellKnown</c>, which encodes the race, so a step only fires for a character that actually has the
    /// racial: an Orc gets Blood Fury, a Troll Berserking, a Blood Elf Arcane Torrent, a Tauren War Stomp, a
    /// Draenei Gift of the Naaru — on any class that race can be. The whole bundle is gated by the class's
    /// "Use racials" toggle, and every step is off the GCD.
    ///
    /// Covers the reliable PvE racials. The CC-break / cleanse racials are deferred until the data/seams they
    /// need exist: Stoneform (needs poison/disease debuff detection), Escape Artist (needs a "rooted" seam),
    /// Will of the Forsaken / Every Man for Himself (fear/charm/sleep aura names are unreliable), Shadowmeld and
    /// Cannibalize (situational out-of-combat / corpse handling).
    /// </summary>
    public static class Racials
    {
        /// <summary>Arcane Torrent's 8-yard PBAoE silence radius.</summary>
        public const float ArcaneTorrentRadius = 8f;

        /// <summary>Pop a resource racial for the resource when a mana user drops below this (the free ~8%).</summary>
        public const int LowResourcePercent = 20;

        /// <summary>"Several enemies in melee" for the AoE racials (War Stomp). Radius in yards.</summary>
        public const float MeleePackRadius = 8f;
        public const int MeleePackSize = 2;

        /// <summary>Use Gift of the Naaru (a heal-over-time) when hurt at least this much in combat.</summary>
        public const int GiftOfNaaruHealthPercent = 80;

        /// <summary>Shadowmeld (Night Elf): a last-ditch vanish below this health % in combat.</summary>
        public const int ShadowmeldHealthPercent = 5;

        /// <summary>Cannibalize (Undead): out-of-combat corpse heal below this health %.</summary>
        public const int CannibalizeHealthPercent = 50;

        /// <summary>Priority gap between racials in the bundle, so the 11 racials ladder cleanly above
        /// <c>basePriority</c> (band width ~0.05) without colliding with each other or adjacent spec steps.</summary>
        public const float RacialSpacing = 0.005f;

        /// <summary>
        /// Append the shared racial steps to a spec's step list and return the combined list. <paramref name="useRacials"/>
        /// is the class's master toggle; <paramref name="basePriority"/> places the racial band (each racial gets a small
        /// offset above it). A new list is returned, so callers can pass either a List or an IReadOnlyList.
        /// </summary>
        public static List<RotationStep> With(IReadOnlyList<RotationStep> steps, Func<CombatContext, bool> useRacials, float basePriority)
        {
            // While feared/charmed/asleep we can't act at all, so HOLD the offensive racials — otherwise, if the
            // CC-break racial is on cooldown, the engine would fall through and waste e.g. Blood Fury during the
            // fear. (A root doesn't stop casting, so this only suppresses on fear/charm/sleep, not Escape Artist's
            // root case.) The CC-break / cleanse / panic racials are exempt — they're the way OUT.
            Func<CombatContext, bool> offensive = ctx => useRacials(ctx) && !HasCrowdControl(ctx);

            var list = new List<RotationStep>(steps)
            {
                // CC-breaks / cleanse / panic come FIRST in the band so they win over the offensive racials when
                // both could fire (e.g. break the fear rather than waste Blood Fury while feared — the adapter
                // can't tell a feared cast failed, so ordering, not fall-through, must decide it).
                EscapeArtist(useRacials, basePriority + 0 * RacialSpacing),
                WillOfTheForsaken(useRacials, basePriority + 1 * RacialSpacing),
                EveryManForHimself(useRacials, basePriority + 2 * RacialSpacing),
                Stoneform(useRacials, basePriority + 3 * RacialSpacing),
                Shadowmeld(useRacials, basePriority + 4 * RacialSpacing),
                // offensive / utility racials (held while feared/charmed/asleep)
                CombatBlocks.OffensiveRacial("Blood Fury", basePriority + 5 * RacialSpacing, offensive),
                CombatBlocks.OffensiveRacial("Berserking", basePriority + 6 * RacialSpacing, offensive),
                ArcaneTorrent(offensive, basePriority + 7 * RacialSpacing),
                WarStomp(offensive, basePriority + 8 * RacialSpacing),
                GiftOfTheNaaru(offensive, basePriority + 9 * RacialSpacing),
                // out-of-combat corpse heal last
                Cannibalize(useRacials, basePriority + 10 * RacialSpacing),
            };
            return list;
        }

        /// <summary>Arcane Torrent (Blood Elf): an 8-yard PBAoE silence that also restores a little resource. Fires
        /// to silence an enemy casting within 8yd (a backup to a real interrupt), or — for a mana user — to grab the
        /// free resource when low. Off the GCD; IsSpellKnown auto-skips every non-Blood-Elf.</summary>
        public static RotationStep ArcaneTorrent(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Arcane Torrent").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat
                              && (ctx.Enemies.Any(e => e.IsCasting && e.Distance <= ArcaneTorrentRadius)
                                  || (ctx.Me.IsCaster && ctx.Me.PowerPercent < LowResourcePercent)))
                 .OffGcd();

        /// <summary>War Stomp (Tauren): a short AoE stun when at least two enemies are in melee on us — buys a
        /// caster a free cast or a melee a breather. Off the GCD; auto-skips for non-Tauren.</summary>
        public static RotationStep WarStomp(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("War Stomp").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat
                              && ctx.Enemies.Count(e => e.IsTargetingMe && e.Distance <= MeleePackRadius) >= MeleePackSize)
                 .OffGcd();

        /// <summary>Gift of the Naaru (Draenei): a heal-over-time when hurt in combat. Off the GCD; auto-skips
        /// for non-Draenei.</summary>
        public static RotationStep GiftOfTheNaaru(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Gift of the Naaru").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat && ctx.Me.HealthPercent < GiftOfNaaruHealthPercent).OffGcd();

        /// <summary>Will of the Forsaken (Undead): break a Fear / Charm / Sleep. Off the GCD; auto-skips for
        /// non-Undead.</summary>
        public static RotationStep WillOfTheForsaken(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Will of the Forsaken").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat && HasCrowdControl(ctx)).OffGcd();

        /// <summary>Every Man for Himself (Human): the Human "PvP trinket" — break a Fear / Charm / Sleep. Off the
        /// GCD; auto-skips for non-Human.</summary>
        public static RotationStep EveryManForHimself(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Every Man for Himself").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat && HasCrowdControl(ctx)).OffGcd();

        /// <summary>Escape Artist (Gnome): break a root/snare (Frost Nova / Entangling Roots / a net). Off the GCD;
        /// auto-skips for non-Gnome.</summary>
        public static RotationStep EscapeArtist(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Escape Artist").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat
                              && (ctx.Game.PlayerIsRooted || ctx.Me.HasAura("Frost Nova"))).OffGcd();

        /// <summary>Stoneform (Dwarf): cleanse a poison/disease (and gain armor). Off the GCD; auto-skips for
        /// non-Dwarf.</summary>
        public static RotationStep Stoneform(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Stoneform").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat
                              && (ctx.Game.PlayerHasDebuffType("Poison") || ctx.Game.PlayerHasDebuffType("Disease"))).OffGcd();

        /// <summary>Shadowmeld (Night Elf): a last-ditch vanish when nearly dead in combat (drops aggro if nothing
        /// holds a hard lock on us). Off the GCD; auto-skips for non-Night-Elf.</summary>
        public static RotationStep Shadowmeld(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Shadowmeld").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && ctx.Game.PlayerInCombat && ctx.Me.HealthPercent < ShadowmeldHealthPercent).OffGcd();

        /// <summary>Cannibalize (Undead): an out-of-combat heal channelled on a nearby Humanoid/Undead corpse. Off
        /// the GCD; auto-skips for non-Undead or when no suitable corpse is in range.</summary>
        public static RotationStep Cannibalize(Func<CombatContext, bool> enabled, float priority) =>
            Skill.Spell("Cannibalize").Priority(priority).On(Targets.Self)
                 .When(ctx => enabled(ctx) && !ctx.Game.PlayerInCombat
                              && ctx.Me.HealthPercent < CannibalizeHealthPercent && ctx.Game.HasCannibalizeCorpseNearby()).OffGcd();

        /// <summary>The player has a Fear / Charm / Sleep effect (the CC the Undead/Human racials break). Name-based,
        /// mirroring the old AIO — fires only when one of those exact aura names is present.</summary>
        private static bool HasCrowdControl(CombatContext ctx) =>
            ctx.Me.HasAura("Fear") || ctx.Me.HasAura("Charm") || ctx.Me.HasAura("Sleep");
    }
}
