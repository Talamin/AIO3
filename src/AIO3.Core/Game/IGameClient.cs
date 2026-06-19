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

        /// <summary>True if the player is currently auto-attacking (melee swing toggle on).</summary>
        bool PlayerIsAutoAttacking { get; }

        CastResult Cast(string spell, IWowUnit target, bool force = false);

        /// <summary>Use the first of these items that is in the bags and off cooldown. Returns true if one was used.</summary>
        bool UseFirstReadyItem(System.Collections.Generic.IReadOnlyList<string> names);

        /// <summary>Set the player's current target (so WRobot's facing/movement follows it).</summary>
        void SetTarget(IWowUnit unit);

        /// <summary>Run an action under the WoW frame lock (consistent memory reads).</summary>
        void RunLocked(Action action);
    }
}
