using System.Collections.Generic;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Priest;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Priest class implementation: a caster + DoTs + heals. AIO3 is solo-only for now, so it ships ONE rotation —
    /// the solo Shadow DPS spec — shared via the <see cref="PriestSettings"/> and the <see cref="PriestCommon"/>
    /// caster baseline (the buffs, the survival incl. the Shadowform shift-out-to-heal, the mana tools, the DoT/
    /// nuke core, the wand). Shadow is the leveling default. Discipline and Holy are deferred healers (like the
    /// Paladin's Holy / the Druid's Restoration): their talent build still auto-applies, but they fall back to the
    /// Shadow rotation with a label note. The rotation is rebuilt only when the spec or mode changes, so the host
    /// can swap the engine by reference comparison.
    ///
    /// TODO (later phases): Discipline / Holy healing rotations and the Group modes (Group Discipline / Holy /
    /// Shadow) the old AIO carried.
    /// </summary>
    public sealed class PriestModule : IClassModule
    {
        private readonly PriestSettings _settings = new PriestSettings();
        private readonly ChoiceSetting _spec =
            new ChoiceSetting("spec", "Spec", PriestSpecs.Auto, PriestSpecs.Choices) { Category = "Spec" };
        private readonly List<Setting> _all;

        private PriestSpec? _activeSpec;
        private string _activeMode;
        private IRotation _rotation;

        public PriestModule()
        {
            // The Spec selector sits first; the shared priest tunables follow.
            _all = new List<Setting> { _spec };
            _all.AddRange(_settings.All);
        }

        public WowClass Class => WowClass.Priest;
        public string DisplayName => "Priest";
        public IReadOnlyList<Setting> Settings => _all;
        public float Range => _settings.CombatRange.Value;
        public string ActiveLabel { get; private set; } = "Solo Shadow";
        public string ActiveSpec => _activeSpec?.ToString();
        public bool AutoSwitchTargetEnabled => _settings.AutoSwitchTarget.Value;
        public bool DebugLoggingEnabled => _settings.DebugProfiling.Value;
        // The priest eats vendor food / water (left to the vendor plugin) — like Warrior / Paladin / Hunter /
        // Warlock / Druid.
        public bool ManageBagFoodDrink => false;

        public IRotation ResolveRotation(int highestTalentTab)
        {
            PriestSpec desired = PriestSpecs.Resolve(_spec.Value, highestTalentTab);
            string mode = _settings.ContentMode.Value;
            if (_rotation != null && _activeSpec == desired && _activeMode == mode)
                return _rotation;

            _activeSpec = desired;
            _activeMode = mode;
            _rotation = Build(desired);
            // Shadow ships a rotation; Discipline / Holy fall back to Shadow (label reflects the fallback, like the
            // Druid's Restoration→Feral / the Paladin's Holy→Ret).
            string specLabel = desired == PriestSpec.Shadow ? "Shadow" : desired + "→Shadow";
            ActiveLabel = (mode == "Group" ? "Group→Solo " : "Solo ") + specLabel;
            return _rotation;
        }

        // Shadow runs SoloShadow; Discipline / Holy (no rotation yet) also run SoloShadow. Every spec shares the
        // one PriestSettings instance, so the Shadow-only knobs show via Setting.Spec.
        private IRotation Build(PriestSpec spec) => new SoloShadow(_settings);

        public string[] DesiredTalentBuild() =>
            _settings.AutoAssignTalents.Value && _activeSpec.HasValue
                ? PriestTalents.For(_activeSpec.Value)
                : null;
    }
}
