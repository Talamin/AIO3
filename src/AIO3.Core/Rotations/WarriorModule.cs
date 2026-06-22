using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Warrior class implementation: Fury / Arms / Protection solo leveling APLs, with the shared
    /// <see cref="WarriorSettings"/> driving them. Resolves the spec from talents (manual override via the
    /// Spec dropdown) and the Solo/Group mode; only Solo rotations exist today, so Group falls back to Solo.
    /// </summary>
    public sealed class WarriorModule : IClassModule
    {
        private readonly WarriorSettings _settings = new WarriorSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", WarriorSpecs.Auto, WarriorSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private WarriorSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public WarriorModule()
        {
            // The Spec selector sits first; the shared warrior tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Warrior;
        public string DisplayName => "Warrior";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Fury";
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // warrior eats vendor food (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            WarriorSpec desired = WarriorSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            // Only Solo rotations exist; Group falls back to Solo (reflected in the label).
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + desired;
            return _rotation;
        }

        private IRotation Build(WarriorSpec spec)
        {
            switch (spec)
            {
                case WarriorSpec.Arms: return new SoloArms(_settings);
                case WarriorSpec.Protection: return new SoloProtection(_settings);
                default: return new SoloFury(_settings);
            }
        }

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? WarriorTalents.For(_activeSpec.Value)
                : null;
    }
}
