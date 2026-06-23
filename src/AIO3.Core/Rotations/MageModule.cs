using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Mage;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Mage class implementation: the first pure caster. Frost / Fire / Arcane solo leveling APLs sharing the
    /// <see cref="MageSettings"/> and the <see cref="MageCommon"/> caster baseline (armor, mana management,
    /// kiting/survival). Frost is the leveling default (kiting + Water Elemental). The rotation is rebuilt only
    /// when the spec or mode changes, so the host can swap the engine by reference comparison.
    /// </summary>
    public sealed class MageModule : IClassModule
    {
        private readonly MageSettings _settings = new MageSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", MageSpecs.Auto, MageSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private MageSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public MageModule()
        {
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Mage;
        public string DisplayName => "Mage";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Frost";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => _settings.ManageFood.Value; // eat/drink the conjured food we make

        public IRotation ResolveRotation(int highestTalentTab)
        {
            MageSpec desired = MageSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + desired;
            return _rotation;
        }

        private IRotation Build(MageSpec spec)
        {
            switch (spec)
            {
                case MageSpec.Fire: return new SoloFire(_settings);
                case MageSpec.Arcane: return new SoloArcane(_settings);
                default: return new SoloFrost(_settings);
            }
        }

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? MageTalents.For(_activeSpec.Value)
                : null;
    }
}
