using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Live-tunable settings for the Fury rotation. The Setting objects are the single shared
    /// storage: the rotation reads their <c>Value</c> each tick, and the in-game overlay writes
    /// to them, so edits take effect immediately. This is the Layer-5 settings system in miniature.
    /// </summary>
    public sealed class FurySettings
    {
        /// <summary>Rage kept in reserve before Heroic Strike dumps spare rage. Lower = HS fires sooner.</summary>
        public readonly IntSetting HeroicStrikeRageReserve =
            new IntSetting("hsReserve", "Heroic Strike rage reserve", value: 20, min: 0, max: 100, step: 5);

        /// <summary>Minimum nearby enemies before the AoE abilities (Thunder Clap/Whirlwind/Cleave) fire.</summary>
        public readonly IntSetting AoeThreshold =
            new IntSetting("aoeCount", "AoE: min enemies", value: 2, min: 2, max: 10, step: 1);

        /// <summary>Whether to use the Charge / Intercept gap-closers.</summary>
        public readonly ToggleSetting UseGapClosers =
            new ToggleSetting("useGapClosers", "Use Charge / Intercept", value: true);

        private readonly Setting[] _all;

        public FurySettings()
        {
            _all = new Setting[] { HeroicStrikeRageReserve, AoeThreshold, UseGapClosers };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
