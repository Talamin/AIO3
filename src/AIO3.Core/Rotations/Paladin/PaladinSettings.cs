using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Paladin
{
    /// <summary>
    /// Live-tunable settings shared by the paladin specs (Retribution today). One shared instance is edited
    /// by the in-game overlay and read by the active rotation every tick, so the settings panel stays stable
    /// when the spec is switched. Buff choices (seal / aura / blessing / judgement) are read at eval time —
    /// not baked into the steps — so changing them in the overlay takes effect live.
    /// </summary>
    public sealed class PaladinSettings
    {
        // --- Buffs (seal / aura / blessing / judgement upkeep) ---

        /// <summary>Which seal to keep up. "Auto" = Seal of Command if learned, else Seal of Righteousness.</summary>
        public readonly ChoiceSetting Seal = new ChoiceSetting("seal", "Seal", "Auto",
            new[] { "Auto", "Seal of Command", "Seal of Vengeance", "Seal of Corruption", "Seal of Righteousness", "Seal of Justice" });

        /// <summary>Which aura to keep up. "Auto" = Retribution Aura for Ret, Devotion Aura for Prot.</summary>
        public readonly ChoiceSetting Aura = new ChoiceSetting("aura", "Aura", "Auto",
            new[] { "Auto", "Retribution Aura", "Devotion Aura", "Concentration Aura", "Fire Resistance Aura", "Frost Resistance Aura", "Shadow Resistance Aura" });

        /// <summary>Self-blessing to keep up (solo). "Auto" = Blessing of Kings if learned, else Blessing of Might.</summary>
        public readonly ChoiceSetting Blessing = new ChoiceSetting("blessing", "Blessing", "Auto",
            new[] { "Auto", "Blessing of Kings", "Blessing of Might", "Blessing of Wisdom", "Blessing of Sanctuary" });

        /// <summary>Which judgement to apply on cooldown. "Auto" = Judgement of Wisdom (mana) if learned, else Light.</summary>
        public readonly ChoiceSetting Judgement = new ChoiceSetting("judgement", "Judgement", "Auto",
            new[] { "Auto", "Judgement of Wisdom", "Judgement of Light", "Judgement of Justice" });

        // --- Rotation ---

        /// <summary>Minimum nearby enemies before the AoE abilities (Consecration / Holy Wrath / Avenging Wrath) fire.</summary>
        public readonly IntSetting AoeThreshold =
            new IntSetting("aoeCount", "AoE: min enemies", value: 2, min: 2, max: 10, step: 1);

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range) — how close the bot stands to the
        /// target. Tune if the bot ends up inside the mob instead of in front of it.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 5, min: 3, max: 15, step: 1);

        // --- Survival ---

        /// <summary>Hard-cast Holy Light on yourself below this health %. 0 disables it.</summary>
        public readonly IntSetting SelfHealPercent =
            new IntSetting("selfHeal", "Holy Light below HP%", value: 40, min: 0, max: 90, step: 5);

        /// <summary>Use a free instant Flash of Light from "The Art of War" procs below this health %. 0 disables.</summary>
        public readonly IntSetting ArtOfWarHealPercent =
            new IntSetting("aowHeal", "Art of War heal below HP%", value: 75, min: 0, max: 100, step: 5);

        /// <summary>Emergency Lay on Hands (full heal, long cooldown) below this health %. 0 disables it.</summary>
        public readonly IntSetting LayOnHandsPercent =
            new IntSetting("loh", "Lay on Hands below HP%", value: 15, min: 0, max: 60, step: 5);

        /// <summary>Use Divine Protection (damage reduction) when several enemies are attacking you.</summary>
        public readonly ToggleSetting UseDivineProtection =
            new ToggleSetting("divineProt", "Divine Protection vs adds", value: true);

        /// <summary>Cast Divine Plea below this mana % to refill (out of / in combat). 0 disables it.</summary>
        public readonly IntSetting DivinePleaManaPercent =
            new IntSetting("divinePlea", "Divine Plea below mana%", value: 60, min: 0, max: 100, step: 5);

        // --- General (shared, mirrors the warrior set) ---

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        /// <summary>Which rotation set to run. Only Solo exists today; Group is a placeholder (falls back to Solo).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Use racials in combat (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the
        /// Naaru, per race). Gates the shared <see cref="Library.Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>How to interrupt enemy casts with Hammer of Justice: Smart / Always / Never.</summary>
        public readonly ChoiceSetting InterruptMode =
            new ChoiceSetting("interrupt", "Interrupt", InterruptModes.Smart, InterruptModes.All);

        /// <summary>Auto target switching among attackers (never pulls — only re-targets when several enemies
        /// are already attacking, with hysteresis). On by default for the paladin per Daniel's preference.
        /// Turn it off if a WRobot product owns targeting and the two start fighting over the target.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: true);

        // NOTE: there is no "Use damage learning" toggle here (unlike the warrior). The DamageTracker still
        // MEASURES every paladin ability for the debug log, but it only becomes ADVISORY through a BestDamage
        // block — a "pick the bigger of two interchangeable strikes" slot. The paladin APLs are clean
        // first-come-first-served on distinct cooldowns (Judgement / Crusader Strike / Divine Storm / …), so
        // there is no such slot and the toggle would do nothing. Re-add it only if an interchangeable choice
        // appears.

        /// <summary>Use major offensive cooldowns on elites/bosses/packs. For the paladin this is
        /// Avenging Wrath ("Wings") — the label spells it out so it isn't a mystery toggle.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns (Avenging Wrath)", value: true);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public PaladinSettings()
        {
            // Tab assignment for the in-game overlay. Rotation = anything that changes what/how the
            // rotation fights; General = meta toggles only (targeting policy, dev logging) that don't
            // change which abilities we cast.
            Seal.Category = "Buffs";
            Seal.Description = "Which seal to keep up. Auto picks Seal of Command if learned, otherwise Seal of Righteousness.";
            Aura.Category = "Buffs";
            Aura.Description = "Which aura to keep up. Auto uses Retribution Aura for Ret and Devotion Aura for Prot.";
            Blessing.Category = "Buffs";
            Blessing.Description = "Self-blessing to keep up when solo. Auto picks Blessing of Kings if learned, otherwise Blessing of Might.";
            Judgement.Category = "Buffs";
            Judgement.Description = "Which judgement to apply on cooldown. Auto picks Judgement of Wisdom (mana) if learned, otherwise Light.";

            AoeThreshold.Category = "Rotation";
            AoeThreshold.Description = "Minimum nearby enemies before AoE abilities (Consecration / Holy Wrath / Avenging Wrath) fire.";
            CombatRange.Category = "Rotation";
            CombatRange.Description = "How close the bot stands to the target. Lower it if the bot ends up inside the mob instead of in front.";
            UseRacials.Category = "Rotation";
            UseRacials.Description = "Use your race's combat racial (Blood Fury, Berserking, Arcane Torrent, War Stomp, Gift of the Naaru).";
            InterruptMode.Category = "Rotation";
            InterruptMode.Description = "How to interrupt enemy casts with Hammer of Justice: Smart, Always or Never.";
            UseCooldowns.Category = "Rotation";
            UseCooldowns.Description = "Use Avenging Wrath (Wings) on elites, bosses and packs for a burst of damage.";

            SelfHealPercent.Category = "Survival";
            SelfHealPercent.Description = "Hard-cast Holy Light on yourself below this health %. 0 disables it.";
            ArtOfWarHealPercent.Category = "Survival";
            ArtOfWarHealPercent.Description = "Use a free instant Flash of Light from Art of War procs below this health %. 0 disables it.";
            LayOnHandsPercent.Category = "Survival";
            LayOnHandsPercent.Description = "Emergency Lay on Hands (full heal, long cooldown) below this health %. 0 disables it.";
            UseDivineProtection.Category = "Survival";
            UseDivineProtection.Description = "Cast Divine Protection for damage reduction when several enemies are attacking you.";
            DivinePleaManaPercent.Category = "Survival";
            DivinePleaManaPercent.Description = "Cast Divine Plea to refill below this mana %. 0 disables it.";
            EmergencyHealthPercent.Category = "Survival";
            EmergencyHealthPercent.Description = "Use an emergency healthstone or potion below this health %. 0 disables it.";

            ContentMode.Category = "Spec";
            ContentMode.Description = "Which rotation set to run. Only Solo exists today; Group falls back to Solo.";
            AutoAssignTalents.Category = "Spec";
            AutoAssignTalents.Description = "Automatically spend talent points using the active spec's default build.";

            AutoSwitchTarget.Category = "General";
            AutoSwitchTarget.Description = "Re-target among attackers when several enemies are on you (never pulls). Off if a product owns targeting.";
            DebugProfiling.Category = "General";
            DebugProfiling.Description = "Dev aid: periodically log rotation tick time, the most expensive steps and learned damage.";

            _all = new Setting[]
            {
                // Buffs
                Seal, Aura, Blessing, Judgement,
                // Rotation
                AoeThreshold, CombatRange, UseRacials, InterruptMode, UseCooldowns,
                // Survival
                SelfHealPercent, ArtOfWarHealPercent, LayOnHandsPercent, UseDivineProtection,
                DivinePleaManaPercent, EmergencyHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
