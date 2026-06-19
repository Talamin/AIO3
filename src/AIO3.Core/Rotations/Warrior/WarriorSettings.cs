using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Live-tunable settings shared by all warrior specs (Fury/Arms/Protection read the ones they
    /// care about). One shared instance is edited by the in-game overlay and read by the active
    /// rotation every tick, so the settings panel stays stable when the spec is switched.
    /// </summary>
    public sealed class WarriorSettings
    {
        /// <summary>Rage kept in reserve before Heroic Strike dumps spare rage. Lower = HS fires sooner.</summary>
        public readonly IntSetting HeroicStrikeRageReserve =
            new IntSetting("hsReserve", "Heroic Strike rage reserve", value: 20, min: 0, max: 100, step: 5);

        /// <summary>Minimum nearby enemies before the AoE abilities fire.</summary>
        public readonly IntSetting AoeThreshold =
            new IntSetting("aoeCount", "AoE: min enemies", value: 2, min: 2, max: 10, step: 1);

        /// <summary>Use the Charge / Intercept gap-closers.</summary>
        public readonly ToggleSetting UseGapClosers =
            new ToggleSetting("useGapClosers", "Use Charge / Intercept", value: true);

        /// <summary>Hamstring fleeing targets below 40% health.</summary>
        public readonly ToggleSetting UseHamstring =
            new ToggleSetting("hamstring", "Hamstring fleeing targets", value: true);

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        private readonly Setting[] _all;

        public WarriorSettings()
        {
            _all = new Setting[] { HeroicStrikeRageReserve, AoeThreshold, UseGapClosers, UseHamstring, AutoAssignTalents };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
