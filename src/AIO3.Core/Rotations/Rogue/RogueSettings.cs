using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Live-tunable settings shared by all rogue specs (Combat now; Assassination will reuse the same instance
    /// when it lands). One instance is edited by the in-game overlay and read by the active rotation every tick;
    /// thresholds are read at eval time so overlay edits take effect live. The rogue is a melee, energy +
    /// combo-point class, so it brings the melee baseline knobs (combat range, Kick interrupt, finisher CP
    /// threshold) plus a Survival tab for the defensive cooldowns (Evasion / Cloak of Shadows).
    ///
    /// Combat-only knobs (Blade Flurry / Adrenaline Rush / Killing Spree enemy counts) live in the Rotation tab
    /// but tag <c>Setting.Spec = "Combat"</c>, so the overlay shows them ONLY while Combat is the active spec —
    /// the same pattern the Warlock uses for its spec-only knobs.
    /// </summary>
    public sealed class RogueSettings
    {
        // --- Rotation ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). A rogue fights in melee; ~5y keeps
        /// it behind the target without standing on top of it.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 5, min: 3, max: 8, step: 1);

        /// <summary>How to interrupt enemy casts with Kick: Smart (learns what's interruptible) / Always / Never
        /// (set Never if a WRobot product already handles interrupts).</summary>
        public readonly ChoiceSetting InterruptMode =
            new ChoiceSetting("interrupt", "Interrupt (Kick)", InterruptModes.Smart, InterruptModes.All);

        /// <summary>Combo points required before a finisher (Eviscerate / Rupture) is spent. Slice and Dice has
        /// its own lower threshold (it only needs 1 CP to be worth keeping up).</summary>
        public readonly IntSetting FinisherComboPoints =
            new IntSetting("finisherCp", "Finisher at combo points", value: 3, min: 1, max: 5, step: 1);

        /// <summary>Keep Rupture (a bleed finisher) up on durable targets. Off by default while leveling — on
        /// trash that dies fast, dumping combo points into Eviscerate is better than a slow bleed.</summary>
        public readonly ToggleSetting UseRupture =
            new ToggleSetting("rupture", "Use Rupture (bleed finisher)", value: false);

        /// <summary>Open from Stealth with the dropdown's opener before the fight (cast "Stealth" out of combat).
        /// Off by default: WRobot products usually own the pull, and a stealthed pull can desync with them.</summary>
        public readonly ToggleSetting UseStealth =
            new ToggleSetting("stealth", "Open from Stealth", value: false);

        /// <summary>Sprint to close the gap to a target out of melee range. A movement tool, so it's a toggle —
        /// turn off if a product owns movement (it only fires while the product is committed to a fight).</summary>
        public readonly ToggleSetting UseSprint =
            new ToggleSetting("sprint", "Sprint to close distance", value: true);

        /// <summary>Use racials in combat (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the
        /// Naaru, per race). Gates the shared <see cref="Library.Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>Use the major offensive cooldowns on elites/bosses/packs. For Combat this is Adrenaline Rush
        /// + Killing Spree — the label names them so it isn't a mystery toggle.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns (Adrenaline Rush / Killing Spree)", value: true);

        // --- Rotation: Combat-only (shown only while Combat is the active spec) ---

        /// <summary>Use Blade Flurry when at least this many enemies are within melee (its cleave is wasted on a
        /// single target). Combat-only.</summary>
        public readonly IntSetting BladeFlurryEnemies =
            new IntSetting("bladeFlurry", "Blade Flurry: min enemies", value: 2, min: 2, max: 6, step: 1);

        /// <summary>Use Adrenaline Rush when at least this many enemies are within melee (also fires on an elite
        /// regardless of count). Combat-only.</summary>
        public readonly IntSetting AdrenalineRushEnemies =
            new IntSetting("adrenalineRush", "Adrenaline Rush: min enemies", value: 3, min: 1, max: 6, step: 1);

        /// <summary>Use Killing Spree when at least this many enemies are within melee. Combat-only.</summary>
        public readonly IntSetting KillingSpreeEnemies =
            new IntSetting("killingSpree", "Killing Spree: min enemies", value: 2, min: 1, max: 6, step: 1);

        // --- Survival ---

        /// <summary>Use Evasion (dodge cooldown) below this health %. 0 disables the HP trigger (Evasion still
        /// fires on the surrounded/elite triggers below).</summary>
        public readonly IntSetting EvasionHealthPercent =
            new IntSetting("evasionHp", "Evasion below HP%", value: 35, min: 0, max: 90, step: 5);

        /// <summary>Use Evasion when at least this many enemies are meleeing you (the dodge wall is most valuable
        /// when surrounded). Also fires on a solo elite fight.</summary>
        public readonly IntSetting EvasionEnemies =
            new IntSetting("evasionCount", "Evasion: min attackers", value: 2, min: 1, max: 6, step: 1);

        /// <summary>Use Cloak of Shadows when carrying a Magic debuff (it wipes magic effects and gives spell
        /// resistance). Auto-skips when Cloak is unknown.</summary>
        public readonly ToggleSetting UseCloakOfShadows =
            new ToggleSetting("cloak", "Cloak of Shadows on magic debuff", value: true);

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

        /// <summary>Auto target switching among attackers (never pulls). Off by default so it can't fight a
        /// product that owns targeting.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: false);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public RogueSettings()
        {
            CombatRange.Category = "Rotation";
            InterruptMode.Category = "Rotation";
            FinisherComboPoints.Category = "Rotation";
            UseRupture.Category = "Rotation";
            UseStealth.Category = "Rotation";
            UseSprint.Category = "Rotation";
            UseRacials.Category = "Rotation";
            UseCooldowns.Category = "Rotation";

            // Combat-only knobs live in the Rotation tab but tag their spec, so the overlay shows them ONLY while
            // Combat is active (Spec strings match RogueSpec.ToString()).
            BladeFlurryEnemies.Category = "Rotation";    BladeFlurryEnemies.Spec = "Combat";
            AdrenalineRushEnemies.Category = "Rotation"; AdrenalineRushEnemies.Spec = "Combat";
            KillingSpreeEnemies.Category = "Rotation";   KillingSpreeEnemies.Spec = "Combat";

            EvasionHealthPercent.Category = "Survival";
            EvasionEnemies.Category = "Survival";
            UseCloakOfShadows.Category = "Survival";
            EmergencyHealthPercent.Category = "Survival";

            ContentMode.Category = "Spec";
            AutoAssignTalents.Category = "Spec";

            AutoSwitchTarget.Category = "General";
            DebugProfiling.Category = "General";

            _all = new Setting[]
            {
                // Rotation (general, then the Combat-only knobs that show only in Combat)
                CombatRange, InterruptMode, FinisherComboPoints, UseRupture, UseStealth, UseSprint,
                UseRacials, UseCooldowns,
                BladeFlurryEnemies, AdrenalineRushEnemies, KillingSpreeEnemies, // Combat-only
                // Survival
                EvasionHealthPercent, EvasionEnemies, UseCloakOfShadows, EmergencyHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
