using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Rogue class implementation: an energy + combo-point melee striker. Ships the Solo Combat and Solo
    /// Assassination leveling specs — both share the <see cref="RogueSettings"/> and the <see cref="RogueCommon"/>
    /// melee baseline (Slice and Dice / Eviscerate / Rupture / Evasion / Cloak / Sprint / Stealth / openers).
    /// Assassination adds Mutilate / Envenom / Hunger for Blood / Cold Blood on top; Subtlety is not built and
    /// falls back to Combat. The rotation is rebuilt only when the spec or mode changes, so the host can swap the
    /// engine by reference comparison.
    ///
    /// TODO (later phases): poisons (ApplyPoison), Subtlety.
    /// </summary>
    public sealed class RogueModule : IClassModule
    {
        private readonly RogueSettings _settings = new RogueSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", RogueSpecs.Auto, RogueSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private RogueSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public RogueModule()
        {
            // The Spec selector sits first; the shared rogue tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Rogue;
        public string DisplayName => "Rogue";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Combat";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        public bool ManageBagFoodDrink => false; // rogue eats vendor food (left to the vendor plugin)

        public IRotation ResolveRotation(int highestTalentTab)
        {
            RogueSpec desired = RogueSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            // Combat and Assassination ship rotations; Subtlety falls back to Combat (label reflects the fallback).
            string specLabel = desired == RogueSpec.Assassination ? "Assassination"
                             : desired == RogueSpec.Combat ? "Combat"
                             : desired + "→Combat";
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + specLabel;
            return _rotation;
        }

        // Assassination runs SoloAssassination; Combat (and Subtlety, which has no rotation yet) runs SoloCombat.
        // Every spec shares the one RogueSettings instance, so spec-only knobs show via Setting.Spec.
        private IRotation Build(RogueSpec spec) =>
            spec == RogueSpec.Assassination ? new SoloAssassination(_settings) : (IRotation)new SoloCombat(_settings);

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? RogueTalents.For(_activeSpec.Value)
                : null;
    }
}
