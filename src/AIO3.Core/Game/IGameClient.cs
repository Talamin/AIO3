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

        /// <summary>Whether the named pet ability is on the bar AND off cooldown. Cheap (cached per pet), so a
        /// step can gate on it without a per-tick scan and won't try to cast an ability that's recharging.</summary>
        bool PetAbilityReady(string name);

        /// <summary>The local player's class (drives which rotation is selected).</summary>
        WowClass PlayerClass { get; }

        /// <summary>Index of the talent tab with the most points (1-based), or 0 if none spent yet.</summary>
        int HighestTalentTab { get; }

        /// <summary>Name of the player's active stance/shapeshift form (e.g. "Berserker Stance"), or "".</summary>
        string ActiveStanceName { get; }

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

        /// <summary>True when the WRobot product is engaged in a fight (its own fight state, set during
        /// the approach too). The rotation only runs while this (or actual combat) holds, so the FC
        /// never acts — or moves (Charge) — while the product is merely navigating.</summary>
        bool ProductIsFighting { get; }

        /// <summary>True if the player is currently auto-attacking (melee swing toggle on).</summary>
        bool PlayerIsAutoAttacking { get; }

        CastResult Cast(string spell, IWowUnit target, bool force = false);

        /// <summary>Use the first of these items that is in the bags and off cooldown. Returns true if one was used.</summary>
        bool UseFirstReadyItem(System.Collections.Generic.IReadOnlyList<string> names);

        /// <summary>Set the player's current target (so WRobot's facing/movement follows it).</summary>
        void SetTarget(IWowUnit unit);

        /// <summary>Step back roughly <paramref name="yards"/> yards (to regain ranged distance). Refuses and
        /// returns false if the spot is over a ledge/cliff — so it never walks the player off an edge. Returns
        /// true if the step was started; it plays out in the background while <see cref="IsRepositioning"/> holds.</summary>
        bool StepBack(float yards);

        /// <summary>Drive an in-progress <see cref="StepBack"/> hop: the host calls this every loop. It holds
        /// the backpedal key down for the hop's window (releasing it once at the end) and returns true while
        /// the hop is in progress, so the host pauses casting without blocking the loop or starving movement.</summary>
        bool ServiceReposition();

        /// <summary>Run an action under the WoW frame lock (consistent memory reads).</summary>
        void RunLocked(Action action);
    }
}
