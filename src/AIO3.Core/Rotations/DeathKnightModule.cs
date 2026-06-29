using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Death Knight class implementation: a rune/runic-power melee class. Ships the three solo DPS leveling specs —
    /// Blood (survival), Frost (Obliterate/Howling Blast), Unholy (permanent ghoul / Scourge Strike) — all sharing
    /// the one <see cref="DeathKnightSettings"/> and the <see cref="DeathKnightCommon"/> baseline (the mandatory
    /// rune-affordability gate, disease upkeep, Presence + Horn of Winter, Mind Freeze / Death Grip, the ghoul, the
    /// runic-power dumps). Blood is the leveling default (no points spent yet). The rotation is rebuilt only when
    /// the spec or mode changes, so the host can swap the engine by reference comparison.
    ///
    /// TODO (later phases): the Group / Tank / PvP modes the old AIO carried (deferred — solo-only for now).
    /// </summary>
    public sealed class DeathKnightModule : IClassModule
    {
        private readonly DeathKnightSettings _settings = new DeathKnightSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", DeathKnightSpecs.Auto, DeathKnightSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private DeathKnightSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public DeathKnightModule()
        {
            // The Spec selector sits first; the shared DK tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.DeathKnight;
        public string DisplayName => "Death Knight";
        public IReadOnlyList<Setting> Settings => _all;

        /// <summary>Combat distance reported to WRobot — melee (~5). Death Grip / Death Coil reach further, but the
        /// engine range-gates per spell, so the base stays melee.</summary>
        public float Range => _settings.CombatRange.Value;

        public string ActiveLabel { get; private set; } = "Solo Blood";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // DK has no food synergy (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            DeathKnightSpec desired = DeathKnightSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + desired;
            return _rotation;
        }

        private IRotation Build(DeathKnightSpec spec)
        {
            switch (spec)
            {
                case DeathKnightSpec.Frost: return new SoloFrost(_settings);
                case DeathKnightSpec.Unholy: return new SoloUnholy(_settings);
                default: return new SoloBlood(_settings);
            }
        }

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? DeathKnightTalents.For(_activeSpec.Value)
                : null;
    }
}
