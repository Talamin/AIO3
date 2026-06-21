using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// A self-contained class implementation (Warrior, Paladin, …). It owns its settings (including the
    /// Spec + Mode selectors), resolves which rotation to run from the player's talents and the chosen
    /// mode, and supplies the per-spec talent build. The host (Main) stays class-agnostic: it wires the
    /// settings into the overlay/persistence, ticks the resolved rotation, applies the talent build out
    /// of combat, and reads the host-relevant toggles below.
    ///
    /// Pure Core — no WRobot types and no logging; the host owns all game I/O and writes the log lines.
    /// </summary>
    public interface IClassModule
    {
        /// <summary>The WoW class this module implements.</summary>
        WowClass Class { get; }

        /// <summary>Human label for the overlay title and the load log (e.g. "Warrior").</summary>
        string DisplayName { get; }

        /// <summary>All settings exposed to the overlay + persistence (the Spec/Mode selectors first).</summary>
        IReadOnlyList<Setting> Settings { get; }

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range).</summary>
        float Range { get; }

        /// <summary>A short label for the active spec + mode, set by the latest <see cref="ResolveRotation"/>
        /// (e.g. "Solo Fury"). The host uses it for the "Active: …" log line.</summary>
        string ActiveLabel { get; }

        /// <summary>Optional auto target-switching among attackers (never pulls). Read each tick by the host;
        /// off by default so it can't fight a product that owns targeting.</summary>
        bool AutoSwitchTargetEnabled { get; }

        /// <summary>Dev toggle: the host periodically logs per-tick timing + learned per-ability damage.</summary>
        bool DebugLoggingEnabled { get; }

        /// <summary>Resolve the rotation to run from the manual override + talent auto-detection and the
        /// Solo/Group mode. Returns the current rotation instance, rebuilt internally only when the spec or
        /// mode changed — so the host can compare instances (ReferenceEquals) to know when to swap the engine.</summary>
        IRotation ResolveRotation(int highestTalentTab);

        /// <summary>The talent build (progression codes) to auto-apply for the active spec, or null when
        /// auto-assign is disabled or no spec has been resolved yet.</summary>
        string[] DesiredTalentBuild();
    }
}
