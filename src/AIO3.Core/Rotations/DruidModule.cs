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
        private readonly IGameClient _game; // to read whether a melee form is learned (drives the dynamic Range)

        private DruidSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public DruidModule(IGameClient game = null)
        {
            _game = game;
            // The Spec selector sits first; the shared druid tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Druid;
        public string DisplayName => "Druid";
        public IReadOnlyList<Setting> Settings => _all;

        /// <summary>Combat distance reported to WRobot. Balance is always a caster. Feral reports MELEE only once a
        /// shapeshift form is learned (Bear Form at ~level 10, then Cat) — before that a formless leveling druid
        /// nukes with Wrath, so it reports CASTER range, otherwise WRobot drags the still-caster low-level druid
        /// into melee (the level-1 behaviour Daniel saw). Mirrors the old AIO's DruidBehavior range logic
        /// (melee if it knows a form, else caster). Re-read live, so it switches to melee the moment a form is
        /// learned. A null game client (tests) keeps the melee default.</summary>
        public float Range
        {
            get
            {
                if (_activeSpec == DruidSpec.Balance) return _settings.CasterRange.Value;
                bool hasMeleeForm = _game == null
                    || _game.IsSpellKnown("Bear Form") || _game.IsSpellKnown("Cat Form")
                    || _game.IsSpellKnown("Dire Bear Form");
                return hasMeleeForm ? _settings.MeleeRange.Value : _settings.CasterRange.Value;
            }
        }

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
