using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Druid;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Druid class implementation: a hybrid shapeshifter. Ships the Solo Feral (cat + bear) and Solo Balance
    /// leveling specs — both share the <see cref="DruidSettings"/> and the <see cref="DruidCommon"/> hybrid
    /// baseline (form switching, Mark of the Wild / Thorns, the Cat energy/combo ladder, the Bear rage/tank
    /// ladder, the in-combat self-heal, Barkskin / Innervate). Feral is the leveling default. Restoration is a
    /// deferred healer (like the Paladin's Holy): its talent build still auto-applies, but it falls back to the
    /// Feral rotation with a label note. The rotation is rebuilt only when the spec or mode changes, so the host
    /// can swap the engine by reference comparison.
    ///
    /// TODO (later phases): Restoration (a healing rotation), Group modes (Group Feral Tank / Group Feral /
    /// Group Restoration), and the OOC heal-up addon the old AIO carried.
    /// </summary>
    public sealed class DruidModule : IClassModule
    {
        private readonly DruidSettings _settings = new DruidSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", DruidSpecs.Auto, DruidSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private DruidSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public DruidModule()
        {
            // The Spec selector sits first; the shared druid tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Druid;
        public string DisplayName => "Druid";
        public IReadOnlyList<Setting> Settings => _all;

        /// <summary>Combat distance reported to WRobot. Feral is melee in its steady state (once Cat/Bear is
        /// learned), Balance is a caster. NOTE: a pre-form leveling Feral (no Cat/Bear yet) nukes with Wrath at
        /// caster range, but the module can't see "form known" without a game client; it reports the melee range
        /// for Feral. The pre-form window is brief and WRobot closes to melee anyway — flagged as the one range
        /// nuance to revisit if a low-level Feral struggles to reach its caster filler.</summary>
        public float Range =>
            _activeSpec == DruidSpec.Balance ? _settings.CasterRange.Value : _settings.MeleeRange.Value;

        public string ActiveLabel { get; private set; } = "Solo Feral";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // druid eats vendor food (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            DruidSpec desired = DruidSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            // Feral and Balance ship rotations; Restoration falls back to Feral (label reflects the fallback).
            string specLabel = desired == DruidSpec.Balance ? "Balance"
                             : desired == DruidSpec.Feral ? "Feral"
                             : desired + "→Feral";
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + specLabel;
            return _rotation;
        }

        // Balance runs SoloBalance; Feral (and Restoration, which has no rotation yet) runs SoloFeral. Every spec
        // shares the one DruidSettings instance, so spec-only knobs show via Setting.Spec.
        private IRotation Build(DruidSpec spec) =>
            spec == DruidSpec.Balance ? new SoloBalance(_settings) : (IRotation)new SoloFeral(_settings);

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? DruidTalents.For(_activeSpec.Value)
                : null;
    }
}
