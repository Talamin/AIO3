using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Warlock class implementation: a caster + permanent demon + DoTs. Ships the three solo leveling specs
    /// (Affliction / Demonology / Destruction). Shares the <see cref="WarlockSettings"/> and the
    /// <see cref="WarlockCommon"/> caster baseline (armor, Life Tap mana engine, Drain Life, wand) and keeps
    /// the demon up via the class-agnostic <see cref="Library.PetControl"/> (the per-spec "Auto" demon resolves
    /// at eval time: Affliction → Voidwalker, Demonology → Felguard, Destruction → Imp). The rotation is rebuilt
    /// only when the spec or mode changes, so the host can swap the engine by reference comparison.
    /// </summary>
    public sealed class WarlockModule : IClassModule
    {
        private readonly WarlockSettings _settings = new WarlockSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", WarlockSpecs.Auto, WarlockSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private WarlockSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public WarlockModule()
        {
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Warlock;
        public string DisplayName => "Warlock";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Affliction";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        // The warlock eats vendor food and conjures Healthstone/Soulstone (not food), so leave food to the
        // vendor plugin — like Warrior / Paladin / Hunter.
        public bool ManageBagFoodDrink => false;

        public IRotation ResolveRotation(int highestTalentTab)
        {
            WarlockSpec desired = WarlockSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + desired;
            return _rotation;
        }

        private IRotation Build(WarlockSpec spec)
        {
            switch (spec)
            {
                case WarlockSpec.Demonology: return new SoloDemonology(_settings);
                case WarlockSpec.Destruction: return new SoloDestruction(_settings);
                default: return new SoloAffliction(_settings);
            }
        }

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? WarlockTalents.For(_activeSpec.Value)
                : null;
    }
}
