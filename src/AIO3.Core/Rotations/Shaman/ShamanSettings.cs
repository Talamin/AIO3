using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Shaman
{
    /// <summary>
    /// Live-tunable settings shared by the two solo shaman specs (Elemental / Enhancement). One instance is edited
    /// by the in-game overlay and read by the active rotation every tick (thresholds resolve at eval time so an
    /// overlay edit takes effect live). The shaman brings the totem layer's knobs on top of the usual baseline: a
    /// per-school Auto/None/specific totem choice (×4), the situational-totem toggles, and the spec rotation
    /// tunables. Spec-only knobs are tagged via <see cref="Setting.Spec"/> so the panel hides what doesn't apply.
    /// </summary>
    public sealed class ShamanSettings
    {
        // --- Totems (per-school choice: Auto = spec default, None = skip, or a specific totem name) ---

        public readonly ChoiceSetting FireTotem =
            new ChoiceSetting("fireTotem", "Fire totem", "Auto",
                new[] { "Auto", "None", "Totem of Wrath", "Magma Totem", "Searing Totem", "Flametongue Totem" });

        public readonly ChoiceSetting EarthTotem =
            new ChoiceSetting("earthTotem", "Earth totem", "Auto",
                new[] { "Auto", "None", "Strength of Earth Totem", "Stoneskin Totem" });

        public readonly ChoiceSetting WaterTotem =
            new ChoiceSetting("waterTotem", "Water totem", "Auto",
                new[] { "Auto", "None", "Mana Spring Totem", "Healing Stream Totem" });

        public readonly ChoiceSetting AirTotem =
            new ChoiceSetting("airTotem", "Air totem", "Auto",
                new[] { "Auto", "None", "Windfury Totem", "Wrath of Air Totem" });

        /// <summary>Recall the totems (Totemic Recall) when one is left behind out of range and no temporary totem
        /// is up. Cleanup only — the per-school re-drop already re-sets a left-behind totem.</summary>
        public readonly ToggleSetting UseTotemicRecall =
            new ToggleSetting("totemicRecall", "Recall left-behind totems", value: true);

        // --- Situational / temporary totems (each its own toggle; auto-skips until learned) ---

        public readonly ToggleSetting UseManaTide =
            new ToggleSetting("manaTide", "Mana Tide Totem when low mana", value: true);

        public readonly IntSetting ManaTideManaPercent =
            new IntSetting("manaTidePct", "Mana Tide below mana%", value: 30, min: 0, max: 90, step: 5);

        public readonly ToggleSetting UseEarthElemental =
            new ToggleSetting("earthElemental", "Earth Elemental on a pack", value: true);

        public readonly ToggleSetting UseStoneclaw =
            new ToggleSetting("stoneclaw", "Stoneclaw Totem on a pack", value: true);

        public readonly ToggleSetting RedeployFireTotem =
            new ToggleSetting("redeployFire", "Redeploy fire totem in combat", value: true);

        public readonly ToggleSetting UseGroundingTotem =
            new ToggleSetting("grounding", "Grounding Totem vs casters", value: true);

        public readonly ToggleSetting UseEarthbindTotem =
            new ToggleSetting("earthbind", "Earthbind Totem vs runners", value: false);

        public readonly ToggleSetting UseCleansingTotem =
            new ToggleSetting("cleansing", "Cleansing Totem (party poison/disease)", value: true);

        // --- Buffs (shields + weapon imbues) ---

        /// <summary>Which self-shield to keep up. Auto = Lightning Shield for Elemental, Water Shield for Enhancement
        /// (Water Shield gives mana regen the melee leans on). "Water Shield" / "Lightning Shield" force one.</summary>
        public readonly ChoiceSetting ShieldChoice =
            new ChoiceSetting("shield", "Self shield", "Auto",
                new[] { "Auto", "Water Shield", "Lightning Shield", "None" });

        /// <summary>For Enhancement, the mana % at/below which we prefer Water Shield (for its mana regen) over
        /// Lightning Shield. Above it, keep Lightning Shield up for the extra damage on melee swings.</summary>
        public readonly IntSetting EnhancementWaterShieldManaPercent =
            new IntSetting("ehWaterShieldMana", "Enh: Water Shield below mana%", value: 50, min: 0, max: 100, step: 5);

        /// <summary>Keep weapon imbues up (Windfury main-hand + Flametongue off-hand for Enhancement; Flametongue
        /// for Elemental). Read via the GetWeaponEnchant seam; re-applied when a hand's enchant is missing.</summary>
        public readonly ToggleSetting UseWeaponImbues =
            new ToggleSetting("imbues", "Keep weapon imbues up", value: true);

        // --- Survival / self-heal ---

        /// <summary>Self-heal (Healing Wave / Lesser Healing Wave) below this health %.</summary>
        public readonly IntSetting SelfHealHealthPercent =
            new IntSetting("selfHealHp", "Self heal below HP%", value: 50, min: 0, max: 90, step: 5);

        /// <summary>Don't self-heal when the current target is below this HP% (it's about to die — finish it instead
        /// of spending a heal/GCD). Mirrors the old SoloEnhancementEnemyHPSkipHealing.</summary>
        public readonly IntSetting SelfHealSkipEnemyHealthPercent =
            new IntSetting("selfHealSkipEnemyHp", "Skip self heal above enemy HP%", value: 10, min: 0, max: 100, step: 5);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        // --- Rotation ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). Set per spec at resolve time
        /// (Enhancement melee ~5, Elemental caster ~27); shown here for visibility / override.</summary>
        public readonly IntSetting EnhancementRange =
            new IntSetting("ehRange", "Enh: combat range (yd)", value: 5, min: 5, max: 10, step: 1) { Spec = "Enhancement" };

        public readonly IntSetting ElementalRange =
            new IntSetting("eleRange", "Ele: combat range (yd)", value: 27, min: 5, max: 35, step: 1) { Spec = "Elemental" };

        /// <summary>Reserve this much mana % for heals: below it, offensive spells hold. 0 = never reserve.</summary>
        public readonly IntSetting ManaSavedForHeals =
            new IntSetting("manaForHeals", "Reserve mana for heals %", value: 0, min: 0, max: 90, step: 5);

        /// <summary>Interrupt enemy casts with Wind Shear.</summary>
        public readonly ToggleSetting InterruptCasts =
            new ToggleSetting("interrupt", "Interrupt casts (Wind Shear)", value: true);

        /// <summary>Use racials (Blood Fury / Berserking / War Stomp / …) in combat.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>Use major cooldowns (Bloodlust/Heroism, Elemental Mastery, Feral Spirit) on elites/bosses/packs.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns", value: true);

        /// <summary>Cast Bloodlust (Horde) / Heroism (Alliance) as a burst cooldown on a big fight. Off by default —
        /// it shares the long Sated/Exhaustion debuff, so leveling players often save it.</summary>
        public readonly ToggleSetting UseBloodlust =
            new ToggleSetting("bloodlust", "Use Bloodlust / Heroism", value: false);

        // --- Enhancement-specific ---

        /// <summary>Enhancement: minimum enemies near the target before Fire Nova fires (off a fire totem).</summary>
        public readonly IntSetting FireNovaCount =
            new IntSetting("fireNovaCount", "Enh: Fire Nova min enemies", value: 3, min: 1, max: 10, step: 1) { Spec = "Enhancement" };

        /// <summary>Enhancement: at/below this mana %, pop Shamanistic Rage (restores mana, reduces damage).</summary>
        public readonly IntSetting ShamanisticRageManaPercent =
            new IntSetting("shamRageMana", "Enh: Shamanistic Rage below mana%", value: 25, min: 0, max: 90, step: 5) { Spec = "Enhancement" };

        /// <summary>Enhancement: when to summon Feral Spirit (the wolves cooldown).</summary>
        public readonly ChoiceSetting FeralSpirit =
            new ChoiceSetting("feralSpirit", "Enh: Feral Spirit", "+2 and Elite",
                new[] { "+2 and Elite", "+3 and Elite", "only Elite", "None" }) { Spec = "Enhancement" };

        /// <summary>Maintain Flame Shock on a target above this HP% (don't refresh a ~15% dying mob — the DyingFloor).</summary>
        public readonly IntSetting FlameShockMinTargetHealth =
            new IntSetting("flameShockMinHp", "Flame Shock above enemy HP%", value: 15, min: 0, max: 90, step: 5);

        // --- Elemental-specific ---

        /// <summary>Elemental: minimum enemies near the target before Chain Lightning fires (else Lightning Bolt).</summary>
        public readonly IntSetting ChainLightningCount =
            new IntSetting("chainLightningCount", "Ele: Chain Lightning min enemies", value: 3, min: 2, max: 10, step: 1) { Spec = "Elemental" };

        /// <summary>Elemental: use Earth Shock as an instant filler (and while moving). Off uses only Flame Shock /
        /// the lightning nukes.</summary>
        public readonly ToggleSetting ElementalEarthShock =
            new ToggleSetting("eleEarthShock", "Ele: use Earth Shock filler", value: true) { Spec = "Elemental" };

        // --- Spec ---

        /// <summary>Which rotation set to run. Only Solo exists today; Group is a placeholder (falls back to Solo).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        // --- General (meta only) ---

        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: true);

        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public ShamanSettings()
        {
            FireTotem.Category = "Totems";
            FireTotem.Description = "Which fire totem to keep up. Auto picks your spec's best known one (Enh: Magma/Searing; Ele: Totem of Wrath). None skips the school.";
            EarthTotem.Category = "Totems";
            EarthTotem.Description = "Which earth totem to keep up. Auto picks your spec's default. None skips the school.";
            WaterTotem.Category = "Totems";
            WaterTotem.Description = "Which water totem to keep up. Auto picks your spec's default. None skips the school.";
            AirTotem.Category = "Totems";
            AirTotem.Description = "Which air totem to keep up. Auto picks your spec's default. None skips the school.";
            UseTotemicRecall.Category = "Totems";
            UseTotemicRecall.Description = "Recall (Totemic Recall) totems left behind out of range when no temporary totem is up — cleanup; the per-school re-drop already re-sets them.";

            UseManaTide.Category = "SituationalTotems";
            UseManaTide.Description = "Drop Mana Tide Totem when mana is low.";
            ManaTideManaPercent.Category = "SituationalTotems";
            ManaTideManaPercent.Description = "Mana % at/below which Mana Tide Totem is dropped.";
            UseEarthElemental.Category = "SituationalTotems";
            UseEarthElemental.Description = "Summon the Earth Elemental (defensive cooldown) when several enemies are on you and Stoneclaw isn't up.";
            UseStoneclaw.Category = "SituationalTotems";
            UseStoneclaw.Description = "Drop Stoneclaw Totem (absorb taunt) when 2+ enemies are on you and the Earth Elemental isn't up.";
            RedeployFireTotem.Category = "SituationalTotems";
            RedeployFireTotem.Description = "In combat, re-drop a fire totem (Magma/Searing) when the target is in range and no fire totem is up.";
            UseGroundingTotem.Category = "SituationalTotems";
            UseGroundingTotem.Description = "Drop Grounding Totem when an enemy caster is near (absorbs one spell).";
            UseEarthbindTotem.Category = "SituationalTotems";
            UseEarthbindTotem.Description = "Drop Earthbind Totem when a humanoid runner is close (snares it). Off by default.";
            UseCleansingTotem.Category = "SituationalTotems";
            UseCleansingTotem.Description = "Drop Cleansing Totem when a party member is poisoned/diseased.";

            ShieldChoice.Category = "Buffs";
            ShieldChoice.Description = "Which self-shield to keep up. Auto = Lightning Shield (Ele) / Water Shield when low mana (Enh).";
            EnhancementWaterShieldManaPercent.Category = "Buffs"; EnhancementWaterShieldManaPercent.Spec = "Enhancement";
            EnhancementWaterShieldManaPercent.Description = "Enhancement, Auto shield: below this mana % prefer Water Shield (mana regen); above it keep Lightning Shield up.";
            UseWeaponImbues.Category = "Buffs";
            UseWeaponImbues.Description = "Keep weapon imbues up (Enh: Windfury main-hand + Flametongue off-hand; Ele: Flametongue). Re-applied when a hand's enchant is missing.";

            SelfHealHealthPercent.Category = "Survival";
            SelfHealHealthPercent.Description = "Self-heal (Healing Wave / Lesser Healing Wave) below this health %.";
            SelfHealSkipEnemyHealthPercent.Category = "Survival";
            SelfHealSkipEnemyHealthPercent.Description = "Don't self-heal when the target is below this HP% — it's about to die, so finish it instead.";
            EmergencyHealthPercent.Category = "Survival";
            EmergencyHealthPercent.Description = "Use an emergency healthstone/potion below this health %. 0 disables it.";

            EnhancementRange.Category = "Rotation";
            EnhancementRange.Description = "Combat distance reported to WRobot for Enhancement (melee).";
            ElementalRange.Category = "Rotation";
            ElementalRange.Description = "Combat distance reported to WRobot for Elemental (caster).";
            ManaSavedForHeals.Category = "Rotation";
            ManaSavedForHeals.Description = "Reserve this much mana % for heals: offensive spells hold below it. 0 = never reserve.";
            InterruptCasts.Category = "Rotation";
            InterruptCasts.Description = "Interrupt enemy casts with Wind Shear.";
            UseRacials.Category = "Rotation";
            UseRacials.Description = "Use your race's combat racial (Blood Fury / Berserking / War Stomp / …).";
            UseCooldowns.Category = "Rotation";
            UseCooldowns.Description = "Use major cooldowns (Elemental Mastery / Feral Spirit) on elites / bosses / packs.";
            UseBloodlust.Category = "Rotation";
            UseBloodlust.Description = "Cast Bloodlust (Horde) / Heroism (Alliance) on a big fight. Off by default — it shares the long Sated/Exhaustion debuff.";

            FireNovaCount.Category = "Rotation";
            FireNovaCount.Description = "Enhancement: minimum enemies near the target before Fire Nova fires (off a fire totem).";
            ShamanisticRageManaPercent.Category = "Rotation";
            ShamanisticRageManaPercent.Description = "Enhancement: pop Shamanistic Rage at/below this mana %.";
            FeralSpirit.Category = "Rotation";
            FeralSpirit.Description = "Enhancement: when to summon Feral Spirit (the wolves).";
            FlameShockMinTargetHealth.Category = "Rotation";
            FlameShockMinTargetHealth.Description = "Maintain Flame Shock only on a target above this HP% (don't refresh a dying mob).";

            ChainLightningCount.Category = "Rotation";
            ChainLightningCount.Description = "Elemental: minimum enemies near the target before Chain Lightning fires (else Lightning Bolt).";
            ElementalEarthShock.Category = "Rotation";
            ElementalEarthShock.Description = "Elemental: use Earth Shock as an instant filler and while moving.";

            ContentMode.Category = "Spec";
            ContentMode.Description = "Which rotation set to run. Only Solo exists today; Group is a placeholder that falls back to Solo.";
            AutoAssignTalents.Category = "Spec";
            AutoAssignTalents.Description = "Automatically spend talent points using the active spec's default build.";

            AutoSwitchTarget.Category = "General";
            AutoSwitchTarget.Description = "Auto-switch targets among attackers (never pulls). Off if a product owns targeting.";
            DebugProfiling.Category = "General";
            DebugProfiling.Description = "Dev aid: periodically log rotation tick time, the most expensive steps, and learned damage.";

            _all = new Setting[]
            {
                // Totems
                FireTotem, EarthTotem, WaterTotem, AirTotem, UseTotemicRecall,
                // Situational totems
                UseManaTide, ManaTideManaPercent, UseEarthElemental, UseStoneclaw, RedeployFireTotem,
                UseGroundingTotem, UseEarthbindTotem, UseCleansingTotem,
                // Buffs
                ShieldChoice, EnhancementWaterShieldManaPercent, UseWeaponImbues,
                // Survival
                SelfHealHealthPercent, SelfHealSkipEnemyHealthPercent, EmergencyHealthPercent,
                // Rotation
                EnhancementRange, ElementalRange, ManaSavedForHeals, InterruptCasts, UseRacials, UseCooldowns, UseBloodlust,
                FireNovaCount, ShamanisticRageManaPercent, FeralSpirit, FlameShockMinTargetHealth,
                ChainLightningCount, ElementalEarthShock,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
