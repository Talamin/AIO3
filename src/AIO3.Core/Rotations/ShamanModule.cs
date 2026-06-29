using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Shaman;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Shaman class implementation: a totem-dropping hybrid. Ships the Solo Enhancement (melee) and Solo Elemental
    /// (caster) leveling specs — both share the <see cref="ShamanSettings"/> and the <see cref="ShamanCommon"/>
    /// baseline (the four-school totem upkeep, the situational totems, weapon imbues, the self-shield, the
    /// self-heal, Wind Shear, the shocks, the Maelstrom proc, Bloodlust). Enhancement is the leveling default.
    /// Restoration is a deferred healer (like the Priest's Discipline/Holy, the Druid's Restoration): its talent
    /// build still auto-applies, but it falls back to the Elemental rotation with a label note. The rotation is
    /// rebuilt only when the spec or mode changes, so the host can swap the engine by reference comparison.
    ///
    /// TODO (later phases): Restoration (a healing rotation) and the Group modes the old AIO carried.
    /// </summary>
    public sealed class ShamanModule : IClassModule
    {
        private readonly ShamanSettings _settings = new ShamanSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", ShamanSpecs.Auto, ShamanSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private ShamanSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public ShamanModule()
        {
            // The Spec selector sits first; the shared shaman tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Shaman;
        public string DisplayName => "Shaman";
        public IReadOnlyList<Setting> Settings => _all;

        /// <summary>Combat distance reported to WRobot. Enhancement is melee (~5); Elemental — and the
        /// Restoration→Elemental fallback — is a caster (~27). Re-read live so it switches with the resolved spec.</summary>
        public float Range =>
            _activeSpec == ShamanSpec.Enhancement ? _settings.EnhancementRange.Value : _settings.ElementalRange.Value;

        public string ActiveLabel { get; private set; } = "Solo Enhancement";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // shaman eats vendor food (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            ShamanSpec desired = ShamanSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            // Enhancement and Elemental ship rotations; Restoration falls back to Elemental (label reflects it).
            string specLabel = desired == ShamanSpec.Enhancement ? "Enhancement"
                             : desired == ShamanSpec.Elemental ? "Elemental"
                             : desired + "→Elemental";
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + specLabel;
            return _rotation;
        }

        // Enhancement runs SoloEnhancement; Elemental (and Restoration, which has no rotation yet) runs
        // SoloElemental. Every spec shares the one ShamanSettings instance, so spec-only knobs show via Setting.Spec.
        private IRotation Build(ShamanSpec spec) =>
            spec == ShamanSpec.Enhancement ? new SoloEnhancement(_settings) : (IRotation)new SoloElemental(_settings);

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? ShamanTalents.For(_activeSpec.Value)
                : null;
    }
}
