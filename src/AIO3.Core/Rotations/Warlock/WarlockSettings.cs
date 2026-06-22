using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warlock
{
    /// <summary>
    /// Live-tunable settings shared by the warlock specs (only Affliction in Phase 1). One instance is edited
    /// by the in-game overlay and read by the active rotation every tick; thresholds are read at eval time so
    /// overlay edits take effect live. The warlock is a caster + permanent pet + DoTs, so it brings the caster
    /// baseline knobs (armor, wand, the signature Life Tap mana engine) plus a Pet category like the hunter.
    /// </summary>
    public sealed class WarlockSettings
    {
        // --- Buffs ---

        /// <summary>Which armor to keep up. Auto picks the best known: Fel Armor → Demon Armor → Demon Skin.</summary>
        public readonly ChoiceSetting ArmorChoice =
            new ChoiceSetting("armor", "Armor", "Auto",
                new[] { "Auto", "Fel Armor", "Demon Armor", "Demon Skin" });

        // --- Pet ---

        /// <summary>Which demon to summon. Auto picks per spec at eval time (Demonology → Felguard, Destruction
        /// → Imp, Affliction/other → Voidwalker), with a known-spell fallback so a low-level lock that has not
        /// learned the spec demon yet drops to the tanky Voidwalker. A manual choice overrides Auto.
        /// Everything pet-related is also automatically skipped when petless (key is the pet existing, not level).</summary>
        public readonly ChoiceSetting Pet =
            new ChoiceSetting("pet", "Pet", "Auto",
                new[] { "Auto", "Voidwalker", "Imp", "Felhunter", "Succubus", "Felguard" });

        /// <summary>Keep the demon summoned and pointed at the target. Turn off if a WRobot product manages the
        /// pet. (Pet steps also auto-skip when petless, since they key on the pet actually existing.)</summary>
        public readonly ToggleSetting ManagePet =
            new ToggleSetting("managePet", "Manage pet (summon / attack)", value: true);

        /// <summary>Health Funnel the pet when it drops below this health % (and we have HP to spare). 0 disables it.</summary>
        public readonly IntSetting PetHealPercent =
            new IntSetting("petHeal", "Health Funnel pet below HP%", value: 0, min: 0, max: 100, step: 5);

        // --- Rotation ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). A warlock casts at range; the wand
        /// covers low mana.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 30, min: 5, max: 41, step: 1);

        /// <summary>Which curse to keep on the target. Agony is the leveling default (ramping DoT).</summary>
        public readonly ChoiceSetting Curse =
            new ChoiceSetting("curse", "Curse", "Agony",
                new[] { "Agony", "Doom", "Elements", "Tongues", "Weakness" });

        /// <summary>Use offensive racials (Blood Fury / Berserking) in combat.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        // --- Demonology ---

        /// <summary>Keep Demonic Empowerment up on the demon (spec buff; auto-skips if unknown / petless).</summary>
        public readonly ToggleSetting DemonicEmpowerment =
            new ToggleSetting("demonicEmpowerment", "Demonology: Demonic Empowerment on pet", value: true);

        /// <summary>Cast Soul Fire when a Decimation/Molten-Core-style proc is up (gated on the buff; auto-skips
        /// if Soul Fire is unknown). Off the proc it stays behind Shadow Bolt, so leaving this on is harmless.</summary>
        public readonly ToggleSetting UseSoulFire =
            new ToggleSetting("soulFire", "Demonology: Soul Fire on proc", value: true);

        // --- Destruction ---

        /// <summary>Use Conflagrate (consumes Immolate for a burst). Gated so it only fires while Immolate is up
        /// on the target; auto-skips if unknown.</summary>
        public readonly ToggleSetting UseConflagrate =
            new ToggleSetting("conflagrate", "Destruction: use Conflagrate", value: true);

        /// <summary>Use Chaos Bolt as a nuke when known (sits between Incinerate and the Shadow Bolt fallback).</summary>
        public readonly ToggleSetting UseChaosBolt =
            new ToggleSetting("chaosBolt", "Destruction: use Chaos Bolt", value: true);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        // --- Survival ---

        /// <summary>Channel Drain Life to self-heal when low and solo (no healer to rely on). 0 disables it.</summary>
        public readonly IntSetting DrainLifeHealthPercent =
            new IntSetting("drainLifeHp", "Drain Life below HP%", value: 40, min: 0, max: 90, step: 5);

        // --- Mana ---

        /// <summary>Life Tap (mana from health — the signature warlock engine) when mana drops below this %.</summary>
        public readonly IntSetting LifeTapManaPercent =
            new IntSetting("lifeTapMana", "Life Tap below mana%", value: 40, min: 0, max: 100, step: 5);

        /// <summary>Only Life Tap while health is above this % (don't trade HP we can't afford).</summary>
        public readonly IntSetting LifeTapHealthFloor =
            new IntSetting("lifeTapHpFloor", "Life Tap only above HP%", value: 50, min: 10, max: 95, step: 5);

        /// <summary>Keep the Glyph of Life Tap spell-power buff up: re-tap when the buff is missing and HP is safe.
        /// Off by default (only worth it with the glyph; harmless otherwise but spends a GCD).</summary>
        public readonly ToggleSetting GlyphLifeTap =
            new ToggleSetting("glyphLifeTap", "Maintain Life Tap buff (glyph)", value: false);

        /// <summary>Wand (Shoot) the target to conserve mana when low (needs a wand equipped).</summary>
        public readonly ToggleSetting UseWand =
            new ToggleSetting("wand", "Wand when low mana", value: true);

        public readonly IntSetting WandManaPercent =
            new IntSetting("wandPct", "Wand below mana%", value: 20, min: 0, max: 100, step: 5);

        // --- Spec ---

        /// <summary>Which rotation set to run. Only Solo exists today; Group is a placeholder (falls back to Solo).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        // --- General (meta only) ---

        /// <summary>Auto target switching among attackers (never pulls). Off by default for the warlock — a
        /// permanent-pet DoT class works better when it commits to a target and lets DoTs tick out.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: false);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public WarlockSettings()
        {
            ArmorChoice.Category = "Buffs";

            Pet.Category = "Pet";
            ManagePet.Category = "Pet";
            PetHealPercent.Category = "Pet";

            CombatRange.Category = "Rotation";
            Curse.Category = "Rotation";
            UseRacials.Category = "Rotation";
            EmergencyHealthPercent.Category = "Rotation";

            DemonicEmpowerment.Category = "Demonology";
            UseSoulFire.Category = "Demonology";

            UseConflagrate.Category = "Destruction";
            UseChaosBolt.Category = "Destruction";

            DrainLifeHealthPercent.Category = "Survival";

            LifeTapManaPercent.Category = "Mana";
            LifeTapHealthFloor.Category = "Mana";
            GlyphLifeTap.Category = "Mana";
            UseWand.Category = "Mana";
            WandManaPercent.Category = "Mana";

            ContentMode.Category = "Spec";
            AutoAssignTalents.Category = "Spec";

            AutoSwitchTarget.Category = "General";
            DebugProfiling.Category = "General";

            _all = new Setting[]
            {
                // Buffs
                ArmorChoice,
                // Pet
                Pet, ManagePet, PetHealPercent,
                // Rotation
                CombatRange, Curse, UseRacials, EmergencyHealthPercent,
                // Demonology
                DemonicEmpowerment, UseSoulFire,
                // Destruction
                UseConflagrate, UseChaosBolt,
                // Survival
                DrainLifeHealthPercent,
                // Mana
                LifeTapManaPercent, LifeTapHealthFloor, GlyphLifeTap, UseWand, WandManaPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
