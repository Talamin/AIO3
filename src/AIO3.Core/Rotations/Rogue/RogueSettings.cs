using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Live-tunable settings shared by all rogue specs (Combat and Assassination share this one instance). One
    /// instance is edited by the in-game overlay and read by the active rotation every tick;
    /// thresholds are read at eval time so overlay edits take effect live. The rogue is a melee, energy +
    /// combo-point class, so it brings the melee baseline knobs (combat range, Kick interrupt, finisher CP
    /// threshold) plus a Survival tab for the defensive cooldowns (Evasion / Cloak of Shadows).
    ///
    /// Combat-only knobs (Blade Flurry / Adrenaline Rush / Killing Spree enemy counts) and Assassination-only knobs
    /// (Rupture / Hunger for Blood / Cold Blood toggles, finisher choice) live in the Rotation tab but tag
    /// <c>Setting.Spec</c>, so the overlay shows each ONLY while its spec is active — the same pattern the Warlock
    /// uses for its spec-only knobs.
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

        /// <summary>Which strike opens a stealth-opened fight (only used when "Open from Stealth" is on). Auto (the
        /// default) lets the FC pick by position: Garrote when we're BEHIND the target (a bleed + silence) else
        /// Cheap Shot from the FRONT (positional-free, 4s stun, 2 combo points) — so Garrote is only chosen when it
        /// will actually land. Force "Cheap Shot" or "Garrote" to override the positional pick.</summary>
        public readonly ChoiceSetting StealthOpener =
            new ChoiceSetting("stealthOpener", "Stealth opener", "Auto", new[] { "Auto", "Cheap Shot", "Garrote" });

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

        // --- Rotation: Assassination-only (shown only while Assassination is the active spec) ---

        /// <summary>Use Fan of Knives when at least this many enemies are within melee (its instant AoE is wasted on
        /// a single target). Assassination-only — Combat cleaves with Blade Flurry instead.</summary>
        public readonly IntSetting FanOfKnivesEnemies =
            new IntSetting("fanOfKnives", "Fan of Knives: min enemies", value: 3, min: 2, max: 6, step: 1);

        /// <summary>Keep Rupture up on durable targets — Assassination leans on the bleed (it enables Hunger for
        /// Blood and is core to the tree's damage), so this defaults ON, unlike Combat's UseRupture. Still gated to
        /// elites/bosses (trash dies before a bleed pays off) and skips bleed-immune creatures.</summary>
        public readonly ToggleSetting AssassinationUseRupture =
            new ToggleSetting("assassRupture", "Use Rupture (bleed finisher)", value: true);

        /// <summary>Maintain Hunger for Blood (a damage buff that needs a bleed on the target). Auto-skips when the
        /// talent isn't taken. Assassination-only.</summary>
        public readonly ToggleSetting UseHungerForBlood =
            new ToggleSetting("hungerForBlood", "Use Hunger for Blood", value: true);

        /// <summary>Pair Cold Blood (guaranteed-crit cooldown) with a finisher on elites/bosses/packs. Also gated by
        /// the shared "Use cooldowns" toggle. Auto-skips when the talent isn't taken. Assassination-only.</summary>
        public readonly ToggleSetting UseColdBlood =
            new ToggleSetting("coldBlood", "Use Cold Blood with finishers", value: true);

        /// <summary>Which finisher Assassination spends combo points on. Defaults to Eviscerate because poisons are
        /// deferred to the player/product — Envenom with 0 Deadly Poison stacks hits weaker than Eviscerate, so until
        /// you poison your weapons Eviscerate is the better dump. Switch to Envenom (the signature finisher, which
        /// scales with Deadly Poison stacks) or Auto (Envenom when it's known, else Eviscerate) once you apply
        /// poisons. Assassination-only.</summary>
        public readonly ChoiceSetting AssassinationFinisher =
            new ChoiceSetting("assassFinisher", "Finisher", "Eviscerate", new[] { "Auto", "Envenom", "Eviscerate" });

        /// <summary>True when the rotation should use Envenom as the finisher: explicitly chosen, or "Auto" — the
        /// Envenom step still auto-skips via IsSpellKnown when it isn't learned, so Auto falls back to Eviscerate
        /// for a low-level rogue. "Eviscerate" suppresses Envenom outright.</summary>
        public bool UseEnvenomFinisher =>
            AssassinationFinisher.Value == "Envenom" || AssassinationFinisher.Value == "Auto";

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

        /// <summary>Use Recuperate (a combo-point finisher that heals over time) as a low-HP self-heal. A survival
        /// spend: when low it takes priority over the offensive finishers, so a finisher-worthy bar goes into the
        /// HoT instead of damage. Auto-skips until the talent is learned.</summary>
        public readonly ToggleSetting UseRecuperate =
            new ToggleSetting("recuperate", "Use Recuperate (self-heal finisher)", value: true);

        /// <summary>Spend a finisher on Recuperate (the healing-over-time HoT) below this health %. Reads at eval
        /// time so an overlay edit applies live.</summary>
        public readonly IntSetting RecuperateHealthPercent =
            new IntSetting("recuperateHp", "Recuperate below HP%", value: 50, min: 0, max: 90, step: 5);

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
            StealthOpener.Category = "Rotation";
            UseSprint.Category = "Rotation";
            UseRacials.Category = "Rotation";
            UseCooldowns.Category = "Rotation";

            // Combat-only knobs live in the Rotation tab but tag their spec, so the overlay shows them ONLY while
            // Combat is active (Spec strings match RogueSpec.ToString()).
            BladeFlurryEnemies.Category = "Rotation";    BladeFlurryEnemies.Spec = "Combat";
            AdrenalineRushEnemies.Category = "Rotation"; AdrenalineRushEnemies.Spec = "Combat";
            KillingSpreeEnemies.Category = "Rotation";   KillingSpreeEnemies.Spec = "Combat";

            // Assassination-only knobs, same pattern (shown only while Assassination is the active spec).
            AssassinationUseRupture.Category = "Rotation"; AssassinationUseRupture.Spec = "Assassination";
            UseHungerForBlood.Category = "Rotation";       UseHungerForBlood.Spec = "Assassination";
            UseColdBlood.Category = "Rotation";            UseColdBlood.Spec = "Assassination";
            AssassinationFinisher.Category = "Rotation";   AssassinationFinisher.Spec = "Assassination";
            FanOfKnivesEnemies.Category = "Rotation";      FanOfKnivesEnemies.Spec = "Assassination";

            EvasionHealthPercent.Category = "Survival";
            EvasionEnemies.Category = "Survival";
            UseCloakOfShadows.Category = "Survival";
            UseRecuperate.Category = "Survival";
            RecuperateHealthPercent.Category = "Survival";
            EmergencyHealthPercent.Category = "Survival";

            ContentMode.Category = "Spec";
            AutoAssignTalents.Category = "Spec";

            AutoSwitchTarget.Category = "General";
            DebugProfiling.Category = "General";

            _all = new Setting[]
            {
                // Rotation (general, then the Combat-only knobs that show only in Combat)
                CombatRange, InterruptMode, FinisherComboPoints, UseRupture, UseStealth, StealthOpener, UseSprint,
                UseRacials, UseCooldowns,
                BladeFlurryEnemies, AdrenalineRushEnemies, KillingSpreeEnemies, // Combat-only
                AssassinationUseRupture, UseHungerForBlood, UseColdBlood, AssassinationFinisher,
                FanOfKnivesEnemies, // Assassination-only
                // Survival
                EvasionHealthPercent, EvasionEnemies, UseCloakOfShadows,
                UseRecuperate, RecuperateHealthPercent, EmergencyHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
