using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Hunter
{
    /// <summary>
    /// Live-tunable settings for the hunter specs (Beast Mastery today). One shared instance is edited by
    /// the in-game overlay and read by the active rotation every tick. The aspect thresholds are read at
    /// eval time so overlay edits take effect live.
    /// </summary>
    public sealed class HunterSettings
    {
        // --- Pet ---

        /// <summary>Keep the pet summoned / revived / healed and pointed at the target. Turn off if a WRobot
        /// product manages the pet. (Everything pet-related is also automatically skipped when petless —
        /// below the taming level / untamed — since it keys on the pet actually existing, not on level.)</summary>
        public readonly ToggleSetting ManagePet =
            new ToggleSetting("managePet", "Manage pet (summon / heal / attack)", value: true);

        /// <summary>Mend Pet the pet when it drops below this health %. 0 disables it.</summary>
        public readonly IntSetting PetHealPercent =
            new IntSetting("petHeal", "Mend Pet below HP%", value: 60, min: 0, max: 100, step: 5);

        /// <summary>Cast Misdirection on the pet to hand it threat (solo) so it keeps the mobs.</summary>
        public readonly ToggleSetting UseMisdirection =
            new ToggleSetting("misdirect", "Misdirection to pet", value: true);

        /// <summary>Step back to ranged distance when a mob closes to melee but is on the pet (the adapter
        /// refuses to step over a ledge, so it never walks off a cliff). Turn off if a product owns movement.</summary>
        public readonly ToggleSetting UseBackpedal =
            new ToggleSetting("backpedal", "Step back to range (pet tanking)", value: true);

        /// <summary>How far to step back when regaining ranged distance — a short hop, not a run. Defaults
        /// to 7 so it clears mobs with a big hitbox (4 isn't always enough). The rotation pauses for the step
        /// so it doesn't slide-cast.</summary>
        public readonly IntSetting BackpedalYards =
            new IntSetting("backpedalYards", "Step back distance (yd)", value: 7, min: 3, max: 12, step: 1);

        // --- Rotation ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). A hunter stands at range and
        /// shoots, so this defaults high; the melee fallback (Raptor Strike) covers mobs that close in.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 28, min: 5, max: 41, step: 1);

        /// <summary>Minimum nearby enemies before the AoE shots fire.</summary>
        public readonly IntSetting AoeThreshold =
            new IntSetting("aoeCount", "AoE: min enemies", value: 3, min: 2, max: 10, step: 1);

        /// <summary>Use Multi-Shot on packs.</summary>
        public readonly ToggleSetting UseAoe =
            new ToggleSetting("useAoe", "Use AoE (Multi-Shot)", value: true);

        /// <summary>Swap to Aspect of the Viper to regen when mana drops below this %.</summary>
        public readonly IntSetting AspectViperManaPercent =
            new IntSetting("viperMana", "Aspect of the Viper below mana%", value: 20, min: 0, max: 100, step: 5);

        /// <summary>Swap back to the damage aspect (Dragonhawk / Hawk) when mana is above this %. The gap
        /// above the Viper threshold gives hysteresis so the aspect doesn't flap at the boundary.</summary>
        public readonly IntSetting AspectHawkManaPercent =
            new IntSetting("hawkMana", "Aspect of the Hawk above mana%", value: 30, min: 0, max: 100, step: 5);

        /// <summary>Feign Death to drop aggro when low and the pet is alive to keep the mobs.</summary>
        public readonly ToggleSetting UseFeignDeath =
            new ToggleSetting("feign", "Feign Death when low", value: true);

        /// <summary>Disengage (leap back) when a mob reaches melee.</summary>
        public readonly ToggleSetting UseDisengage =
            new ToggleSetting("disengage", "Disengage from melee", value: false);

        /// <summary>Interrupt enemy casts with Intimidation (the pet's stun; needs an alive pet).</summary>
        public readonly ToggleSetting InterruptCasts =
            new ToggleSetting("interrupt", "Interrupt casts (Intimidation)", value: true);

        /// <summary>Use offensive racials (Blood Fury / Berserking) in combat.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>Use major cooldowns (Bestial Wrath / Rapid Fire) on elites/bosses/packs.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns (Bestial Wrath / Rapid Fire)", value: true);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        // --- Spec ---

        /// <summary>Which rotation set to run. Only Solo exists today; Group is a placeholder (falls back to Solo).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        // --- General (meta only) ---

        /// <summary>Auto target switching among attackers (never pulls). On by default; turn off if a
        /// product owns targeting and the two fight over the target.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: true);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public HunterSettings()
        {
            // Tab assignment for the in-game overlay. Pet = pet management; Rotation = how we fight;
            // Spec = spec/mode/talents; General = meta toggles only.
            ManagePet.Category = "Pet";
            PetHealPercent.Category = "Pet";
            UseMisdirection.Category = "Pet";
            UseBackpedal.Category = "Pet";
            BackpedalYards.Category = "Pet";

            CombatRange.Category = "Rotation";
            AoeThreshold.Category = "Rotation";
            UseAoe.Category = "Rotation";
            AspectViperManaPercent.Category = "Rotation";
            AspectHawkManaPercent.Category = "Rotation";
            UseFeignDeath.Category = "Rotation";
            UseDisengage.Category = "Rotation";
            InterruptCasts.Category = "Rotation";
            UseRacials.Category = "Rotation";
            UseCooldowns.Category = "Rotation";
            EmergencyHealthPercent.Category = "Rotation";

            ContentMode.Category = "Spec";
            AutoAssignTalents.Category = "Spec";

            AutoSwitchTarget.Category = "General";
            DebugProfiling.Category = "General";

            _all = new Setting[]
            {
                // Pet
                ManagePet, PetHealPercent, UseMisdirection, UseBackpedal, BackpedalYards,
                // Rotation
                CombatRange, AoeThreshold, UseAoe, AspectViperManaPercent, AspectHawkManaPercent,
                UseFeignDeath, UseDisengage, InterruptCasts, UseRacials, UseCooldowns, EmergencyHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
