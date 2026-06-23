using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Hunter;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Hunter class implementation: the first pet class. Beast Mastery solo leveling APL today, with the
    /// shared <see cref="HunterSettings"/> driving it and the pet kept up by the class-agnostic
    /// <see cref="Library.PetControl"/> blocks (gated on the pet actually existing, never on level).
    /// Marksmanship / Survival resolve but fall back to Beast Mastery until their specs land.
    /// </summary>
    public sealed class HunterModule : IClassModule
    {
        private readonly HunterSettings _settings = new HunterSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", HunterSpecs.Auto, HunterSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private HunterSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public HunterModule()
        {
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Hunter;
        public string DisplayName => "Hunter";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Beast Mastery";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // hunter eats vendor food (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            HunterSpec desired = HunterSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + Display(desired);
            return _rotation;
        }

        private IRotation Build(HunterSpec spec)
        {
            switch (spec)
            {
                case HunterSpec.Marksmanship: return new SoloMarksmanship(_settings);
                case HunterSpec.Survival: return new SoloSurvival(_settings);
                default: return new SoloBeastMastery(_settings);
            }
        }

        private static string Display(HunterSpec spec) =>
            spec == HunterSpec.BeastMastery ? "Beast Mastery" : spec.ToString();

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? HunterTalents.For(_activeSpec.Value)
                : null;
    }
}
