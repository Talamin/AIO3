using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Mage
{
    /// <summary>
    /// Live-tunable settings shared by the mage specs (Frost / Fire / Arcane). One instance is edited by the
    /// in-game overlay and read by the active rotation every tick. Thresholds are read at eval time so overlay
    /// edits take effect live. This is the first pure caster, so it brings the "caster baseline" knobs: armor
    /// upkeep, mana management (Evocation / mana gem / wand), and kiting/survival.
    /// </summary>
    public sealed class MageSettings
    {
        // --- Buffs ---

        /// <summary>Which armor to keep up. Auto picks a spec-appropriate one (Fire→Molten, Arcane→Mage,
        /// Frost→Ice/Frost) and falls back to whatever is known.</summary>
        public readonly ChoiceSetting ArmorChoice =
            new ChoiceSetting("armor", "Armor", "Auto",
                new[] { "Auto", "Molten Armor", "Mage Armor", "Ice Armor", "Frost Armor" });

        /// <summary>Keep Arcane Intellect up on yourself (skipped if Arcane Brilliance is already on).</summary>
        public readonly ToggleSetting UseArcaneIntellect =
            new ToggleSetting("arcaneInt", "Keep Arcane Intellect up", value: true);

        /// <summary>Summon the Water Elemental as a cooldown (Frost). Auto-skips if not known; directed via the
        /// shared pet controller. Turn off if a product manages the pet.</summary>
        public readonly ToggleSetting UseWaterElemental =
            new ToggleSetting("waterElemental", "Use Water Elemental (Frost)", value: true);

        /// <summary>Out of combat, automatically conjure food / water / a mana gem when the stock runs low —
        /// the hallmark mage self-sufficiency. Auto-skips ranks/spells not yet known.</summary>
        public readonly ToggleSetting UseConjure =
            new ToggleSetting("conjure", "Auto-conjure food / water / gem", value: true);

        /// <summary>Conjure more food/water when fewer than this many are in the bags.</summary>
        public readonly IntSetting ConjureCount =
            new IntSetting("conjureCount", "Conjure below stacks", value: 20, min: 5, max: 60, step: 5);

        /// <summary>Tell WRobot to eat/drink the BEST food/water in our bags — so it actually consumes the food we
        /// conjure (and drinks for mana), instead of a named vendor item we may not carry. Turn off if a vendor
        /// plugin owns food/drink. (Sets WRobot's "use best bag food/drink" + "drink for mana".)</summary>
        public readonly ToggleSetting ManageFood =
            new ToggleSetting("manageFood", "Eat/drink best bag food (conjured)", value: true);

        // --- Rotation ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). A mage casts at range; the kite
        /// + wand cover mobs that close in.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 30, min: 5, max: 41, step: 1);

        /// <summary>Use AoE spells (Blizzard / Flamestrike / Arcane Explosion …) on packs.</summary>
        public readonly ToggleSetting UseAoe =
            new ToggleSetting("useAoe", "Use AoE", value: true);

        /// <summary>Minimum nearby enemies before the AoE spells fire.</summary>
        public readonly IntSetting AoeThreshold =
            new IntSetting("aoeCount", "AoE: min enemies", value: 3, min: 2, max: 10, step: 1);

        /// <summary>Interrupt enemy casts with Counterspell.</summary>
        public readonly ToggleSetting InterruptCasts =
            new ToggleSetting("interrupt", "Interrupt casts (Counterspell)", value: true);

        /// <summary>Polymorph (sheep) an extra attacker when several mobs are on you. Off by default — it can
        /// fight AoE / a product that cleaves.</summary>
        public readonly ToggleSetting UsePolymorph =
            new ToggleSetting("sheep", "Polymorph extra attackers", value: false);

        /// <summary>Use racials (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the Naaru, per
        /// race) in combat. Gates the shared <see cref="Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>Use major cooldowns (Icy Veins / Combustion / Arcane Power / Mirror Image …) on
        /// elites / bosses / packs.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns (Icy Veins / Combustion / Arcane Power)", value: true);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        // --- Survival / kite ---

        /// <summary>Kite a mob that reaches melee: Frost Nova to root it, then step back to regain range. The
        /// adapter refuses to step over a ledge, so it never walks off a cliff. Turn off if a product owns
        /// movement.</summary>
        public readonly ToggleSetting UseKiting =
            new ToggleSetting("kite", "Kite melee (Frost Nova + step back)", value: true);

        /// <summary>How far to step back when kiting — a hop, not a run. The rotation pauses for the step so it
        /// doesn't slide-cast.</summary>
        public readonly IntSetting KiteYards =
            new IntSetting("kiteYards", "Kite step distance (yd)", value: 10, min: 5, max: 18, step: 1);

        /// <summary>Only kite (Frost Nova + Blink + step back) an attacker still ABOVE this health %. A mob that
        /// dies in a cast or two isn't worth a root and a hop — just finish it. 0 = always kite. (The old AIO used
        /// 30.)</summary>
        public readonly IntSetting KiteMinTargetHealth =
            new IntSetting("kiteMinHp", "Kite only above enemy HP%", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Don't kite (Frost Nova / Blink / step back) a mob this many levels — or more — BELOW you. A
        /// "grey", trivial mob dies in a hit or two, so we just nuke it down instead of wasting a root + hop on it.
        /// 0 = kite regardless of level. (Default 5 = WoW's "grey con" rule of thumb.)</summary>
        public readonly IntSetting KiteSkipGreyLevels =
            new IntSetting("kiteSkipGrey", "Skip kite vs mobs N lvls below", value: 5, min: 0, max: 15, step: 1);

        /// <summary>Blink-escape when a mob reaches melee: the FC turns to face away, Blinks (so it teleports
        /// AWAY, not into the mob), then faces back. Pairs with Frost Nova (root → blink away → cast from range).
        /// Turn off if a product owns movement.</summary>
        public readonly ToggleSetting UseBlink =
            new ToggleSetting("blink", "Blink to escape melee", value: true);

        /// <summary>Ice Block (full immunity, clears debuffs) when health drops this low and you're being hit.
        /// A panic button — you can't act during it.</summary>
        public readonly ToggleSetting UseIceBlock =
            new ToggleSetting("iceBlock", "Ice Block when low", value: true);

        public readonly IntSetting IceBlockHealthPercent =
            new IntSetting("iceBlockHp", "Ice Block below HP%", value: 15, min: 0, max: 60, step: 5);

        /// <summary>Keep Ice Barrier up in combat (Frost; auto-skips if not known).</summary>
        public readonly ToggleSetting UseIceBarrier =
            new ToggleSetting("iceBarrier", "Keep Ice Barrier up", value: true);

        /// <summary>Mana Shield (mana → damage absorb) when health drops low and you're being hit.</summary>
        public readonly ToggleSetting UseManaShield =
            new ToggleSetting("manaShield", "Mana Shield when low", value: true);

        public readonly IntSetting ManaShieldHealthPercent =
            new IntSetting("manaShieldHp", "Mana Shield below HP%", value: 50, min: 0, max: 90, step: 5);

        // --- Mana ---

        /// <summary>Channel Evocation to refill mana below this %, when nothing is meleeing you.</summary>
        public readonly IntSetting EvocationManaPercent =
            new IntSetting("evocation", "Evocation below mana%", value: 20, min: 0, max: 100, step: 5);

        /// <summary>Use a conjured Mana Gem (or mana potion) below this mana %.</summary>
        public readonly ToggleSetting UseManaGem =
            new ToggleSetting("manaGem", "Use mana gem / potion", value: true);

        public readonly IntSetting ManaGemManaPercent =
            new IntSetting("manaGemPct", "Mana gem below mana%", value: 25, min: 0, max: 100, step: 5);

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

        /// <summary>Auto target switching among attackers (never pulls). On by default; turn off if a product
        /// owns targeting and the two fight over the target.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: true);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public MageSettings()
        {
            ArmorChoice.Category = "Buffs";
            UseArcaneIntellect.Category = "Buffs";
            UseWaterElemental.Category = "Buffs";
            UseConjure.Category = "Buffs";
            ConjureCount.Category = "Buffs";
            ManageFood.Category = "Buffs";

            CombatRange.Category = "Rotation";
            UseAoe.Category = "Rotation";
            AoeThreshold.Category = "Rotation";
            InterruptCasts.Category = "Rotation";
            UsePolymorph.Category = "Rotation";
            UseRacials.Category = "Rotation";
            UseCooldowns.Category = "Rotation";
            EmergencyHealthPercent.Category = "Rotation";

            UseKiting.Category = "Survival";
            KiteYards.Category = "Survival";
            KiteMinTargetHealth.Category = "Survival";
            KiteSkipGreyLevels.Category = "Survival";
            UseBlink.Category = "Survival";
            UseIceBlock.Category = "Survival";
            IceBlockHealthPercent.Category = "Survival";
            UseIceBarrier.Category = "Survival";
            UseManaShield.Category = "Survival";
            ManaShieldHealthPercent.Category = "Survival";

            EvocationManaPercent.Category = "Mana";
            UseManaGem.Category = "Mana";
            ManaGemManaPercent.Category = "Mana";
            UseWand.Category = "Mana";
            WandManaPercent.Category = "Mana";

            ContentMode.Category = "Spec";
            AutoAssignTalents.Category = "Spec";

            AutoSwitchTarget.Category = "General";
            DebugProfiling.Category = "General";

            _all = new Setting[]
            {
                // Buffs
                ArmorChoice, UseArcaneIntellect, UseWaterElemental, UseConjure, ConjureCount, ManageFood,
                // Rotation
                CombatRange, UseAoe, AoeThreshold, InterruptCasts, UsePolymorph, UseRacials, UseCooldowns, EmergencyHealthPercent,
                // Survival
                UseKiting, KiteYards, KiteMinTargetHealth, KiteSkipGreyLevels, UseBlink, UseIceBlock, IceBlockHealthPercent, UseIceBarrier, UseManaShield, ManaShieldHealthPercent,
                // Mana
                EvocationManaPercent, UseManaGem, ManaGemManaPercent, UseWand, WandManaPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
