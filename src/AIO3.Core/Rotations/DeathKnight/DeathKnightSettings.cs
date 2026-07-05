using System.Collections.Generic;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.DeathKnight
{
    /// <summary>
    /// Live-tunable settings shared by the three solo Death Knight specs (Blood / Frost / Unholy). One instance is
    /// edited by the in-game overlay and read by the active rotation every tick (thresholds resolve at eval time so
    /// an overlay edit takes effect live). On top of the usual baseline the DK brings: the combat Presence choice,
    /// the disease + survival knobs (Rune Tap %, Vampiric Blood %), the per-spec AoE enemy counts (Blood Boil /
    /// Heart Strike / Blood Strike / Death and Decay), the ghoul toggle, and the Death Grip pull + interrupt
    /// toggles. Spec-only knobs are tagged via <see cref="Setting.Spec"/> so the panel hides what doesn't apply.
    /// </summary>
    public sealed class DeathKnightSettings
    {
        // --- Presence + buffs (shared) ---

        /// <summary>Which Presence to fight in (kept up by the Presence upkeep step). Default Blood — the survival
        /// presence (extra armor + health), the safest choice while leveling. Mirrors the old DK "Presence" setting.</summary>
        public readonly ChoiceSetting Presence =
            new ChoiceSetting("presence", "Presence", "Blood Presence",
                new[] { "Blood Presence", "Frost Presence", "Unholy Presence" });

        /// <summary>Keep Horn of Winter up (the Strength/Agility buff + free runic power). Re-cast when missing.</summary>
        public readonly ToggleSetting UseHornOfWinter =
            new ToggleSetting("hornOfWinter", "Keep Horn of Winter up", value: true);

        // --- Diseases (shared) ---

        /// <summary>Maintain Frost Fever / Blood Plague (Icy Touch + Plague Strike) and spread with Pestilence.</summary>
        public readonly ToggleSetting UseDiseases =
            new ToggleSetting("diseases", "Maintain diseases", value: true);

        /// <summary>Don't refresh a disease on a target below this HP% (it dies before the ~21s disease pays off —
        /// the DyingFloor). Mirrors the shaman's Flame Shock dying-mob floor.</summary>
        public readonly IntSetting DiseaseMinTargetHealth =
            new IntSetting("diseaseMinHp", "Maintain diseases above enemy HP%", value: 15, min: 0, max: 90, step: 5);

        /// <summary>Spread diseases with Pestilence when at least this many nearby enemies LACK them.</summary>
        public readonly IntSetting PestilenceCount =
            new IntSetting("pestilenceCount", "Pestilence spread min enemies", value: 2, min: 1, max: 10, step: 1);

        // --- Survival (shared) ---

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Anti-Magic Shell when an enemy is casting at me (absorbs magic damage). Off the GCD.</summary>
        public readonly ToggleSetting UseAntiMagicShell =
            new ToggleSetting("antiMagicShell", "Anti-Magic Shell vs casters", value: true);

        /// <summary>Icebound Fortitude below this HP% when a pack is on me (the universal DK damage-reduction CD).</summary>
        public readonly IntSetting IceboundFortitudeHealthPercent =
            new IntSetting("iceboundFortHp", "Icebound Fortitude below HP%", value: 80, min: 0, max: 100, step: 5);

        // --- Death Grip + interrupt (shared) ---

        /// <summary>Interrupt enemy casts with Mind Freeze (the DK's melee interrupt).</summary>
        public readonly ToggleSetting InterruptCasts =
            new ToggleSetting("interrupt", "Interrupt casts (Mind Freeze)", value: true);

        /// <summary>Use Death Grip as the ranged pull (drag the engaged mob into melee, like a bear's Growl pull).</summary>
        public readonly ToggleSetting UseDeathGripPull =
            new ToggleSetting("deathGripPull", "Death Grip ranged pull", value: true);

        /// <summary>Also use Death Grip to yank a casting enemy out of its cast (a pull-interrupt backup to Mind
        /// Freeze, for a caster standing at range). Gated on the interrupt toggle too.</summary>
        public readonly ToggleSetting UseDeathGripInterrupt =
            new ToggleSetting("deathGripInterrupt", "Death Grip pull casters in", value: true);

        // --- Ghoul (Unholy permanent; Blood/Frost optional temp) ---

        /// <summary>Summon + manage the ghoul (Raise Dead) — UNHOLY only, where Master of Ghouls makes it permanent.
        /// Blood/Frost never run the ghoul band (the 60s temp minion isn't worth a GCD + reagent). Off lets a product
        /// own the pet (or play petless). The summon also needs a raisable corpse nearby OR Corpse Dust in the bags.</summary>
        public readonly ToggleSetting UseRaiseDead =
            new ToggleSetting("raiseDead", "Raise Dead (ghoul, Unholy)", value: true);

        /// <summary>Use major cooldowns (Dancing Rune Weapon, Summon Gargoyle, Empower Rune Weapon, Mark of Blood)
        /// on elites / bosses / packs.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns", value: true);

        // --- Blood-specific ---

        /// <summary>Blood: Rune Tap (a Blood-rune self-heal) at/below this HP%.</summary>
        public readonly IntSetting RuneTapPercent =
            new IntSetting("runeTapHp", "Blood: Rune Tap below HP%", value: 50, min: 0, max: 90, step: 5) { Spec = "Blood" };

        /// <summary>Blood: Vampiric Blood (a big self-heal/HP cooldown) at/below this HP%.</summary>
        public readonly IntSetting VampiricBloodPercent =
            new IntSetting("vampBloodHp", "Blood: Vampiric Blood below HP%", value: 30, min: 0, max: 90, step: 5) { Spec = "Blood" };

        /// <summary>Blood: use Blood Strike when EXACTLY this many enemies are in melee (single-target builder).</summary>
        public readonly IntSetting BloodBloodStrikeCount =
            new IntSetting("bloodBloodStrike", "Blood: Blood Strike at enemies ==", value: 1, min: 1, max: 5, step: 1) { Spec = "Blood" };

        /// <summary>Blood: use Heart Strike when at least this many enemies are in melee (the cleave builder).</summary>
        public readonly IntSetting BloodHeartStrikeCount =
            new IntSetting("bloodHeartStrike", "Blood: Heart Strike at enemies >=", value: 2, min: 1, max: 5, step: 1) { Spec = "Blood" };

        /// <summary>Blood: use Blood Boil when MORE than this many enemies are in melee (the AoE).</summary>
        public readonly IntSetting BloodBloodBoilCount =
            new IntSetting("bloodBloodBoil", "Blood: Blood Boil at enemies >", value: 2, min: 1, max: 10, step: 1) { Spec = "Blood" };

        /// <summary>Blood: use Death and Decay when at least this many enemies are near (the ground AoE).</summary>
        public readonly IntSetting BloodDeathAndDecayCount =
            new IntSetting("bloodDnD", "Blood: Death and Decay at enemies >=", value: 3, min: 1, max: 10, step: 1) { Spec = "Blood" };

        /// <summary>Blood: Runic Power at/above which Death Coil dumps it (0-rune RP spender; Blood dumps eagerly).</summary>
        public readonly IntSetting BloodDeathCoilRunicPower =
            new IntSetting("bloodDeathCoilRp", "Blood: Death Coil at RP >=", value: 40, min: 0, max: 130, step: 5) { Spec = "Blood" };

        // --- Frost-specific ---

        /// <summary>Frost: use Blood Strike when EXACTLY this many enemies are in melee.</summary>
        public readonly IntSetting FrostBloodStrikeCount =
            new IntSetting("frostBloodStrike", "Frost: Blood Strike at enemies ==", value: 1, min: 1, max: 5, step: 1) { Spec = "Frost" };

        /// <summary>Frost: use Heart Strike when at least this many enemies are in melee.</summary>
        public readonly IntSetting FrostHeartStrikeCount =
            new IntSetting("frostHeartStrike", "Frost: Heart Strike at enemies >=", value: 2, min: 1, max: 5, step: 1) { Spec = "Frost" };

        /// <summary>Frost: use Blood Boil when MORE than this many enemies are in melee.</summary>
        public readonly IntSetting FrostBloodBoilCount =
            new IntSetting("frostBloodBoil", "Frost: Blood Boil at enemies >", value: 2, min: 1, max: 10, step: 1) { Spec = "Frost" };

        /// <summary>Frost: use Death and Decay when at least this many enemies are near.</summary>
        public readonly IntSetting FrostDeathAndDecayCount =
            new IntSetting("frostDnD", "Frost: Death and Decay at enemies >=", value: 3, min: 1, max: 10, step: 1) { Spec = "Frost" };

        /// <summary>Frost: Runic Power at/above which Frost Strike dumps it (0-rune RP spender).</summary>
        public readonly IntSetting FrostStrikeRunicPower =
            new IntSetting("frostStrikeRp", "Frost: Frost Strike at RP >=", value: 40, min: 0, max: 130, step: 5) { Spec = "Frost" };

        // --- Unholy-specific ---

        /// <summary>Unholy: use Blood Strike when EXACTLY this many enemies are in melee.</summary>
        public readonly IntSetting UnholyBloodStrikeCount =
            new IntSetting("unholyBloodStrike", "Unholy: Blood Strike at enemies ==", value: 1, min: 1, max: 5, step: 1) { Spec = "Unholy" };

        /// <summary>Unholy: use Heart Strike when at least this many enemies are in melee.</summary>
        public readonly IntSetting UnholyHeartStrikeCount =
            new IntSetting("unholyHeartStrike", "Unholy: Heart Strike at enemies >=", value: 2, min: 1, max: 5, step: 1) { Spec = "Unholy" };

        /// <summary>Unholy: use Blood Boil when MORE than this many enemies are in melee.</summary>
        public readonly IntSetting UnholyBloodBoilCount =
            new IntSetting("unholyBloodBoil", "Unholy: Blood Boil at enemies >", value: 2, min: 1, max: 10, step: 1) { Spec = "Unholy" };

        /// <summary>Unholy: use Death and Decay when at least this many enemies are near.</summary>
        public readonly IntSetting UnholyDeathAndDecayCount =
            new IntSetting("unholyDnD", "Unholy: Death and Decay at enemies >=", value: 3, min: 1, max: 10, step: 1) { Spec = "Unholy" };

        /// <summary>Unholy: Death Strike (HP-heal) at/below this HP%.</summary>
        public readonly IntSetting UnholyDeathStrikeHealthPercent =
            new IntSetting("unholyDeathStrikeHp", "Unholy: Death Strike below HP%", value: 50, min: 0, max: 90, step: 5) { Spec = "Unholy" };

        /// <summary>Unholy: Runic Power at/above which Death Coil dumps it (0-rune RP spender).</summary>
        public readonly IntSetting UnholyDeathCoilRunicPower =
            new IntSetting("unholyDeathCoilRp", "Unholy: Death Coil at RP >=", value: 80, min: 0, max: 130, step: 5) { Spec = "Unholy" };

        // --- Rotation (shared) ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). Melee (~5); Death Grip/Death Coil reach
        /// further but the engine range-gates per-spell, so the base stays melee.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("range", "Combat range (yd)", value: 5, min: 5, max: 10, step: 1);

        /// <summary>Use racials (Blood Fury / Berserking / War Stomp / …) in combat.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        // --- Spec / meta ---

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

        public DeathKnightSettings()
        {
            Presence.Category = "Buffs";
            Presence.Description = "Which Presence to fight in. Blood = survival (armor + health, the leveling default); Frost = threat/defence; Unholy = haste/movement.";
            UseHornOfWinter.Category = "Buffs";
            UseHornOfWinter.Description = "Keep Horn of Winter up (Strength/Agility buff + free runic power). Re-cast when missing.";

            UseDiseases.Category = "Diseases";
            UseDiseases.Description = "Maintain Frost Fever (Icy Touch) and Blood Plague (Plague Strike), and spread them with Pestilence.";
            DiseaseMinTargetHealth.Category = "Diseases";
            DiseaseMinTargetHealth.Description = "Don't apply/refresh a disease on a target below this HP% (it dies before the disease pays off).";
            PestilenceCount.Category = "Diseases";
            PestilenceCount.Description = "Cast Pestilence to spread diseases when at least this many nearby enemies lack them.";

            EmergencyHealthPercent.Category = "Survival";
            EmergencyHealthPercent.Description = "Use an emergency healthstone/potion below this health %. 0 disables it.";
            UseAntiMagicShell.Category = "Survival";
            UseAntiMagicShell.Description = "Cast Anti-Magic Shell when an enemy is casting at you (absorbs magic damage).";
            IceboundFortitudeHealthPercent.Category = "Survival";
            IceboundFortitudeHealthPercent.Description = "Icebound Fortitude (damage reduction) below this HP% when a pack is on you.";

            InterruptCasts.Category = "Rotation";
            InterruptCasts.Description = "Interrupt enemy casts with Mind Freeze (and, if enabled, Death Grip).";
            UseDeathGripPull.Category = "Rotation";
            UseDeathGripPull.Description = "Use Death Grip as the ranged pull — yank the engaged mob into melee (like a bear's Growl pull).";
            UseDeathGripInterrupt.Category = "Rotation";
            UseDeathGripInterrupt.Description = "Also Death Grip a casting enemy in (a pull-interrupt backup to Mind Freeze). Needs the interrupt toggle too.";
            UseRaiseDead.Category = "Rotation"; UseRaiseDead.Spec = "Unholy";
            UseRaiseDead.Description = "Summon + control the ghoul (Raise Dead). Unholy only (Master of Ghouls keeps it permanent). Needs a raisable corpse nearby or Corpse Dust.";
            UseCooldowns.Category = "Rotation";
            UseCooldowns.Description = "Use major cooldowns (Dancing Rune Weapon / Summon Gargoyle / Empower Rune Weapon / Mark of Blood) on elites/bosses/packs.";
            UseRacials.Category = "Rotation";
            UseRacials.Description = "Use your race's combat racial (Blood Fury / Berserking / War Stomp / …).";

            RuneTapPercent.Category = "Rotation"; RuneTapPercent.Spec = "Blood";
            RuneTapPercent.Description = "Blood: Rune Tap (a Blood-rune self-heal) at/below this HP%.";
            VampiricBloodPercent.Category = "Rotation"; VampiricBloodPercent.Spec = "Blood";
            VampiricBloodPercent.Description = "Blood: Vampiric Blood (a big HP/heal cooldown) at/below this HP%.";
            BloodBloodStrikeCount.Category = "Rotation"; BloodBloodStrikeCount.Spec = "Blood";
            BloodBloodStrikeCount.Description = "Blood: Blood Strike when exactly this many enemies are in melee (single-target builder).";
            BloodHeartStrikeCount.Category = "Rotation"; BloodHeartStrikeCount.Spec = "Blood";
            BloodHeartStrikeCount.Description = "Blood: Heart Strike when at least this many enemies are in melee (cleave builder).";
            BloodBloodBoilCount.Category = "Rotation"; BloodBloodBoilCount.Spec = "Blood";
            BloodBloodBoilCount.Description = "Blood: Blood Boil when more than this many enemies are in melee (AoE).";
            BloodDeathAndDecayCount.Category = "Rotation"; BloodDeathAndDecayCount.Spec = "Blood";
            BloodDeathAndDecayCount.Description = "Blood: Death and Decay when at least this many enemies are near (ground AoE).";
            BloodDeathCoilRunicPower.Category = "Rotation"; BloodDeathCoilRunicPower.Spec = "Blood";
            BloodDeathCoilRunicPower.Description = "Blood: dump runic power with Death Coil at/above this RP.";

            FrostBloodStrikeCount.Category = "Rotation"; FrostBloodStrikeCount.Spec = "Frost";
            FrostBloodStrikeCount.Description = "Frost: Blood Strike when exactly this many enemies are in melee.";
            FrostHeartStrikeCount.Category = "Rotation"; FrostHeartStrikeCount.Spec = "Frost";
            FrostHeartStrikeCount.Description = "Frost: Heart Strike when at least this many enemies are in melee.";
            FrostBloodBoilCount.Category = "Rotation"; FrostBloodBoilCount.Spec = "Frost";
            FrostBloodBoilCount.Description = "Frost: Blood Boil when more than this many enemies are in melee.";
            FrostDeathAndDecayCount.Category = "Rotation"; FrostDeathAndDecayCount.Spec = "Frost";
            FrostDeathAndDecayCount.Description = "Frost: Death and Decay when at least this many enemies are near.";
            FrostStrikeRunicPower.Category = "Rotation"; FrostStrikeRunicPower.Spec = "Frost";
            FrostStrikeRunicPower.Description = "Frost: dump runic power with Frost Strike at/above this RP.";

            UnholyBloodStrikeCount.Category = "Rotation"; UnholyBloodStrikeCount.Spec = "Unholy";
            UnholyBloodStrikeCount.Description = "Unholy: Blood Strike when exactly this many enemies are in melee.";
            UnholyHeartStrikeCount.Category = "Rotation"; UnholyHeartStrikeCount.Spec = "Unholy";
            UnholyHeartStrikeCount.Description = "Unholy: Heart Strike when at least this many enemies are in melee.";
            UnholyBloodBoilCount.Category = "Rotation"; UnholyBloodBoilCount.Spec = "Unholy";
            UnholyBloodBoilCount.Description = "Unholy: Blood Boil when more than this many enemies are in melee.";
            UnholyDeathAndDecayCount.Category = "Rotation"; UnholyDeathAndDecayCount.Spec = "Unholy";
            UnholyDeathAndDecayCount.Description = "Unholy: Death and Decay when at least this many enemies are near.";
            UnholyDeathStrikeHealthPercent.Category = "Rotation"; UnholyDeathStrikeHealthPercent.Spec = "Unholy";
            UnholyDeathStrikeHealthPercent.Description = "Unholy: Death Strike (self-heal) at/below this HP%.";
            UnholyDeathCoilRunicPower.Category = "Rotation"; UnholyDeathCoilRunicPower.Spec = "Unholy";
            UnholyDeathCoilRunicPower.Description = "Unholy: dump runic power with Death Coil at/above this RP.";

            CombatRange.Category = "Rotation";
            CombatRange.Description = "Combat distance reported to WRobot (melee). Death Grip/Death Coil reach further; the engine range-gates per spell.";

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
                // Buffs
                Presence, UseHornOfWinter,
                // Diseases
                UseDiseases, DiseaseMinTargetHealth, PestilenceCount,
                // Survival
                EmergencyHealthPercent, UseAntiMagicShell, IceboundFortitudeHealthPercent,
                // Rotation (shared)
                InterruptCasts, UseDeathGripPull, UseDeathGripInterrupt, UseRaiseDead, UseCooldowns, UseRacials, CombatRange,
                // Blood
                RuneTapPercent, VampiricBloodPercent, BloodBloodStrikeCount, BloodHeartStrikeCount, BloodBloodBoilCount,
                BloodDeathAndDecayCount, BloodDeathCoilRunicPower,
                // Frost
                FrostBloodStrikeCount, FrostHeartStrikeCount, FrostBloodBoilCount, FrostDeathAndDecayCount, FrostStrikeRunicPower,
                // Unholy
                UnholyBloodStrikeCount, UnholyHeartStrikeCount, UnholyBloodBoilCount, UnholyDeathAndDecayCount,
                UnholyDeathStrikeHealthPercent, UnholyDeathCoilRunicPower,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
