using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Paladin;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Paladin class implementation: Retribution / Protection solo leveling APLs, with the shared
    /// <see cref="PaladinSettings"/> driving them and the seal / aura / blessing / judgement system in
    /// <see cref="PaladinCommon"/>. Resolves the spec from talents (manual override via the Spec dropdown)
    /// and the Solo/Group mode; only Solo rotations exist today, so Group falls back to Solo.
    /// </summary>
    public sealed class PaladinModule : IClassModule
    {
        private readonly PaladinSettings _settings = new PaladinSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", PaladinSpecs.Auto, PaladinSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private PaladinSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public PaladinModule()
        {
            // The Spec selector sits first; the shared paladin tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Paladin;
        public string DisplayName => "Paladin";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Retribution";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // paladin eats vendor food (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            PaladinSpec desired = PaladinSpecs.Resolve(_spec.Value, highestTalentTab);
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

        private IRotation Build(PaladinSpec spec)
        {
            switch (spec)
            {
                case PaladinSpec.Protection: return new SoloProtection(_settings);
                default: return new SoloRetribution(_settings);
            }
        }

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? PaladinTalents.For(_activeSpec.Value)
                : null;
    }
}
