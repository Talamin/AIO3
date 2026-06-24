using System;
using System.Collections.Generic;

namespace AIO3.Core.Game
{
    /// <summary>Outcome of a cast attempt.</summary>
    public enum CastResult
    {
        Success,
        NotKnown,
        NotUsable,
        OnCooldown,
        NoTarget,
        Moving,
        Busy,
        Failed
    }

    /// <summary>
    /// Layer 0 seam — the single boundary between our logic and WRobot.
    /// The concrete implementation (WRobotGameClient) is the ONLY code allowed to
    /// touch wManager. Tests use FakeGameClient. The frame lock lives behind
    /// <see cref="RunLocked"/> and nowhere else.
    /// </summary>
    public interface IGameClient
    {
        IWowUnit Me { get; }
        IWowUnit Target { get; }

        /// <summary>The player's pet, or null if none exists. Keyed on real existence (the pet object is
        /// valid), NEVER on level — an untamed / dismissed / below-taming-level hunter reads null and plays
        /// petless, and a pet present early on an unusual server just works. Captured into CombatContext.</summary>
        IWowUnit Pet { get; }

        /// <summary>Order the pet to attack the given unit. No-op if there is no pet. Instant; the pet then
        /// auto-casts its own abilities. Callers throttle / only re-issue on a target change.</summary>
        void PetAttack(IWowUnit target);

        /// <summary>Whether the current pet has the named ability on its action bar (e.g. "Growl"). Cheap
        /// (cached per pet) so a step can gate on it without a per-tick scan — and so a pet that simply
        /// doesn't have the ability (an Imp has no taunt) is handled automatically: the step never fires.</summary>
        bool PetHasAbility(string name);

        /// <summary>Cast a pet ability by name if the pet has it and it is off cooldown. Returns true if it
        /// was cast. No-op (false) when the pet lacks the ability — so the same call is safe for any pet.</summary>
        bool CastPetAbility(string name);

        /// <summary>Turn a pet ability's AUTOCAST on/off (e.g. the Imp's Firebolt — a cast-time nuke with no
        /// cooldown that's best left to the pet rather than re-triggered every tick). Only toggles when the
        /// current state differs; no-op when the pet lacks the ability. Persists per pet.</summary>
        void SetPetAutocast(string ability, bool on);

        /// <summary>Whether the named pet ability is on the bar AND off cooldown. Cheap (cached per pet), so a
        /// step can gate on it without a per-tick scan and won't try to cast an ability that's recharging.</summary>
        bool PetAbilityReady(string name);

        /// <summary>The local player's class (drives which rotation is selected).</summary>
        WowClass PlayerClass { get; }

        /// <summary>Index of the talent tab with the most points (1-based), or 0 if none spent yet.</summary>
        int HighestTalentTab { get; }

        /// <summary>Name of the player's active stance/shapeshift form (e.g. "Berserker Stance"), or "".</summary>
        string ActiveStanceName { get; }

        /// <summary>The player's combo points on the current target (0..5). Target-relative — WoW tracks combo
        /// points per target, so this is already the count that the finishers (Eviscerate / Slice and Dice /
        /// Rupture) would consume. 0 for non-rogue/feral classes.</summary>
        int ComboPoints { get; }

        /// <summary>True while the player is stealthed (the "Stealth" buff is up). Lets rogue steps skip the
        /// out-of-stealth abilities while opening from stealth (casting "Stealth" itself is a normal cast).</summary>
        bool PlayerIsStealthed { get; }

        /// <summary>Enemies near the player (already range-filtered by the adapter).</summary>
        IReadOnlyList<IWowUnit> Enemies { get; }

        /// <summary>Party/raid members (including the player).</summary>
        IReadOnlyList<IWowUnit> Party { get; }

        bool IsSpellKnown(string spell);
        bool IsSpellReady(string spell);

        /// <summary>True if the named spell is the current/queued cast (e.g. an on-next-swing ability).</summary>
        bool IsCurrentSpell(string spell);

        /// <summary>Max cast range of a spell in yards (0 or less = no range gate / self/melee-handled).</summary>
        float SpellRange(string spell);

        /// <summary>Remaining global cooldown in ms (0 = off the GCD).</summary>
        int GlobalCooldownRemainingMs { get; }

        bool PlayerIsCasting { get; }
        bool PlayerIsMoving { get; }
        bool PlayerIsMounted { get; }
        bool PlayerInCombat { get; }

        /// <summary>True while the player is dead OR a ghost (corpse-running). The host skips the whole rotation
        /// tick then — the product owns the corpse run, and ticking would just burn time (e.g. the conjure bag
        /// scans) and spam the perf log while we can do nothing.</summary>
        bool PlayerIsDeadOrGhost { get; }

        /// <summary>True while the player is swimming (in water). The kite is futile here — swimming is half speed
        /// and the mobs aren't held by Frost Nova's ground root, so a caster just stands and nukes instead.</summary>
        bool PlayerIsSwimming { get; }

        /// <summary>True while the player is rooted/snared in place (Frost Nova / Entangling Roots / a net). Read
        /// from the real movement-flag bit (not the misleading WoWUnit.Rooted, which is a different unit flag).
        /// Lets the Gnome racial Escape Artist break a root.</summary>
        bool PlayerIsRooted { get; }

        /// <summary>True if the player has any debuff of the given dispel type ("Poison" / "Disease" / "Magic" /
        /// "Curse"). Lets the Dwarf racial Stoneform fire on a poison/disease, and serves future cleanse logic.</summary>
        bool PlayerHasDebuffType(string dispelType);

        /// <summary>True if a Humanoid or Undead corpse is within ~8yd — the targets the Undead racial Cannibalize
        /// can feed on (an out-of-combat heal). Creature type comes from the per-entry cache (mobs we fought).</summary>
        bool HasCannibalizeCorpseNearby();

        /// <summary>True when the WRobot product is engaged in a fight (its own fight state, set during
        /// the approach too). The rotation only runs while this (or actual combat) holds, so the FC
        /// never acts — or moves (Charge) — while the product is merely navigating.</summary>
        bool ProductIsFighting { get; }

        /// <summary>True if the player is currently auto-attacking (melee swing toggle on).</summary>
        bool PlayerIsAutoAttacking { get; }

        CastResult Cast(string spell, IWowUnit target, bool force = false);

        /// <summary>Use the first of these items that is in the bags and off cooldown. Returns true if one was used.</summary>
        bool UseFirstReadyItem(System.Collections.Generic.IReadOnlyList<string> names);

        /// <summary>Configure WRobot's regen: when <paramref name="on"/>, eat/drink the BEST food/water found in
        /// the bags (so a conjuring class consumes what it conjured) and drink to restore mana. When off, leave
        /// WRobot's named-food settings alone. The host calls this only when the class module's preference changes.</summary>
        void SetManageBagFoodDrink(bool on);

        /// <summary>Set the player's current target (so WRobot's facing/movement follows it).</summary>
        void SetTarget(IWowUnit unit);

        /// <summary>Pin the character in place for <paramref name="ms"/> milliseconds: stop any current movement
        /// AND cancel the product's travel re-pathing (the move-to / path pulses) for that window. Used so a long
        /// cast-time spell completes out of combat — chiefly the pet summon (~10s cast), which the product would
        /// otherwise break by re-pathing mid-cast (a single StopMove isn't enough; it re-issues a move on its next
        /// pulse). The hold auto-expires after <paramref name="ms"/>.</summary>
        void HoldPosition(int ms);

        /// <summary>Step back roughly <paramref name="yards"/> yards (to regain ranged distance). Refuses and
        /// returns false if the spot is over a ledge/cliff — so it never walks the player off an edge. Returns
        /// true if the step was started; it plays out in the background while <see cref="IsRepositioning"/> holds.</summary>
        bool StepBack(float yards);

        /// <summary>Drive an in-progress <see cref="StepBack"/> hop: the host calls this every loop. It holds
        /// the backpedal key down for the hop's window (releasing it once at the end) and returns true while
        /// the hop is in progress, so the host pauses casting without blocking the loop or starving movement.</summary>
        bool ServiceReposition();

        /// <summary>Blink-escape away from the current target: turn to face directly away, cast Blink (which
        /// teleports in the facing direction), then face back toward the target so casting can resume. Runs on
        /// WRobot's fight-loop thread (so the product can't re-orient us mid-cast). Returns true if started;
        /// no-op (false) if Blink is unknown/on cooldown or there is no target.</summary>
        bool BlinkAway();

        /// <summary>Total count of the given items in the player's bags (summed across all the names — e.g. all
        /// ranks of conjured food). Used by the mage's auto-conjure to decide when to make more.</summary>
        int CountItems(IReadOnlyList<string> names);

        /// <summary>Polymorph (sheep) the given add — which is NOT the current target. The adapter parks it on
        /// focus to read its creature type (only sheepable types: Beast / Humanoid / Critter), turns to face it
        /// so the cast lands, and casts on focus. Returns false (no-op) if it's unknown/on cooldown or the add
        /// isn't a sheepable type, so the rotation falls through. The caller picks WHICH add and gates the rest.</summary>
        bool Polymorph(IWowUnit add);

        /// <summary>Run an action under the WoW frame lock (consistent memory reads).</summary>
        void RunLocked(Action action);
    }
}
