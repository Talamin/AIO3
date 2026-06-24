using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Rogue class implementation: an energy + combo-point melee striker. Phase 1 ships the Solo Combat leveling
    /// spec only — it shares the <see cref="RogueSettings"/> and the <see cref="RogueCommon"/> melee baseline
    /// (Slice and Dice / Eviscerate / Evasion / Cloak / Sprint / Stealth). Assassination resolves here (so its
    /// talent build still auto-applies) but maps to the Combat rotation until SoloAssassination lands; Subtlety
    /// is not built. The rotation is rebuilt only when the spec or mode changes, so the host can swap the engine
    /// by reference comparison.
    ///
    /// TODO (later phases): SoloAssassination spec, poisons (ApplyPoison), Subtlety.
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
            // Only the Combat rotation exists in Phase 1; Assassination/Subtlety fall back to it (label reflects it).
            string specLabel = desired == RogueSpec.Combat ? "Combat" : desired + "→Combat";
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + specLabel;
            return _rotation;
        }

        // Phase 1 builds only the Combat rotation; every spec falls back to it (the talent build still tracks the
        // detected spec via DesiredTalentBuild). Replace the Assassination case when SoloAssassination lands.
        private IRotation Build(RogueSpec spec) => new SoloCombat(_settings);

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? RogueTalents.For(_activeSpec.Value)
                : null;
    }
}
