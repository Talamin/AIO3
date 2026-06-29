using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Priest
{
    /// <summary>
    /// Live-tunable settings for the priest. One instance is edited by the in-game overlay and read by the active
    /// rotation every tick; thresholds are read at eval time so overlay edits take effect live. AIO3 ships only
    /// the solo Shadow DPS spec for now, so the spec-specific knobs are tagged <c>Spec = "Shadow"</c> and show in
    /// the overlay only while Shadow is the active spec (Discipline / Holy are deferred healers that fall back to
    /// the Shadow rotation). The shared caster baseline knobs (wand, racials, buffs) apply to every spec.
    /// </summary>
    public sealed class PriestSettings
    {
        // --- Buffs (OOC self-buffs + the in-form Inner Fire upkeep) ---

        /// <summary>Keep Inner Fire up (a spell-power / armor self-buff; castable in Shadowform).</summary>
        public readonly ToggleSetting InnerFire =
            new ToggleSetting("innerFire", "Keep Inner Fire up", value: true);

        /// <summary>Keep Power Word: Fortitude up out of combat (stamina buff).</summary>
        public readonly ToggleSetting PowerWordFortitude =
            new ToggleSetting("pwFort", "Power Word: Fortitude (OOC)", value: true);

        /// <summary>Keep Shadow Protection up out of combat (shadow-resistance buff).</summary>
        public readonly ToggleSetting ShadowProtection =
            new ToggleSetting("shadowProt", "Shadow Protection (OOC)", value: true);

        /// <summary>Keep Divine Spirit up out of combat (spirit buff; auto-skips until learned).</summary>
        public readonly ToggleSetting DivineSpirit =
            new ToggleSetting("divineSpirit", "Divine Spirit (OOC)", value: true);

        // --- Rotation (general) ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). A priest casts at range; the wand
        /// covers low mana. Mirrors the old PriestBehavior.Range (27).</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 27, min: 5, max: 41, step: 1);

        /// <summary>Use racials in combat (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the
        /// Naaru, per race). Gates the shared <see cref="Library.Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        // --- Rotation: Shadow-only (shown only while Shadow is the active spec) ---

        /// <summary>Keep Shadowform up (the Shadow DPS form). Off plays as a Smite caster.</summary>
        public readonly ToggleSetting Shadowform =
            new ToggleSetting("shadowform", "Maintain Shadowform", value: true);

        /// <summary>Maintain Vampiric Embrace (self-heal from shadow damage; castable in form).</summary>
        public readonly ToggleSetting VampiricEmbrace =
            new ToggleSetting("vampEmbrace", "Maintain Vampiric Embrace", value: true);

        /// <summary>Maintain Devouring Plague (a self-heal DoT). Off skips it entirely.</summary>
        public readonly ToggleSetting UseDevouringPlague =
            new ToggleSetting("devouringPlague", "Use Devouring Plague", value: true);

        /// <summary>Use Mind Flay as the channelled filler (when SW:Pain or Devouring Plague is up). Off by default
        /// (mirrors the old SoloShadow default) — the priest fills with Mind Blast / wand otherwise.</summary>
        public readonly ToggleSetting UseMindFlay =
            new ToggleSetting("mindFlay", "Use Mind Flay filler", value: false);

        /// <summary>Use Mind Sear (the channelled AoE) when 2+ enemies cluster around the target. Auto-skips until
        /// learned.</summary>
        public readonly ToggleSetting UseMindSear =
            new ToggleSetting("mindSear", "Use Mind Sear (AoE)", value: true);

        /// <summary>Cast Shadowfiend below this mana % to refill mana (a cooldown that auto-attacks the target and
        /// returns mana). Auto-skips until learned.</summary>
        public readonly IntSetting ShadowfiendManaPercent =
            new IntSetting("shadowfiend", "Shadowfiend below mana%", value: 30, min: 0, max: 100, step: 5);

        /// <summary>Dispersion below this mana % (emergency mana regen + 90% damage reduction). Auto-skips until the
        /// Shadow capstone is learned. 0 disables it.</summary>
        public readonly IntSetting DispersionManaPercent =
            new IntSetting("dispersion", "Dispersion below mana%", value: 30, min: 0, max: 100, step: 5);

        // --- Survival ---

        /// <summary>Power Word: Shield below this health % (a damage-absorb; castable in form). It won't re-cast
        /// while the shield or Weakened Soul is up. 0 disables it.</summary>
        public readonly IntSetting ShieldHealthPercent =
            new IntSetting("shieldHp", "Power Word: Shield below HP%", value: 60, min: 0, max: 100, step: 5);

        /// <summary>The in-combat HARD heal threshold: below this HP%, DROP Shadowform (a priest can't cast Holy
        /// heals in form) and cast the best known heal (Greater Heal &gt; Heal &gt; Lesser Heal). 0 disables it.</summary>
        public readonly IntSetting HealHealthPercent =
            new IntSetting("healHp", "Hard heal below HP%", value: 40, min: 0, max: 100, step: 5);

        /// <summary>Flash Heal below this HP% — the fast (but mana-hungry) emergency heal. Also drops Shadowform
        /// first. Sits above the slower Heal so a sharper drop gets the fast cast. 0 disables it.</summary>
        public readonly IntSetting FlashHealHealthPercent =
            new IntSetting("flashHp", "Flash Heal below HP%", value: 60, min: 0, max: 100, step: 5);

        /// <summary>Renew below this HP% (a HoT; Holy, so only out of form). Cheap topping-off between the harder
        /// heals. 0 disables it.</summary>
        public readonly IntSetting RenewHealthPercent =
            new IntSetting("renewHp", "Renew below HP%", value: 90, min: 0, max: 100, step: 5);

        /// <summary>Only cast a hard heal / Renew while mana is above this % (so we don't shift out of form for a
        /// heal we can't afford and then thrash trying to re-enter). Mirrors the old SoloShadow Renew mana gate.</summary>
        public readonly IntSetting HealManaPercent =
            new IntSetting("healMana", "Heal only above mana%", value: 40, min: 0, max: 100, step: 5);

        /// <summary>Psychic Scream (a panic AoE fear) when surrounded (2+ enemies in melee) and below this HP%,
        /// solo. Buys a breather. 0 disables it.</summary>
        public readonly IntSetting PsychicScreamHealthPercent =
            new IntSetting("screamHp", "Psychic Scream below HP%", value: 80, min: 0, max: 100, step: 5);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        // --- Mana ---

        /// <summary>Wand (Shoot) the target to conserve mana — fires when the target is low or mana is nearly empty
        /// (needs a wand equipped).</summary>
        public readonly ToggleSetting UseWand =
            new ToggleSetting("wand", "Wand low targets / low mana", value: true);

        /// <summary>Wand once the target drops to/below this HP% (finish it with the wand instead of spending mana),
        /// or whenever mana is nearly empty.</summary>
        public readonly IntSetting WandTargetHealthPercent =
            new IntSetting("wandTargetHp", "Wand at/below target HP%", value: 20, min: 0, max: 100, step: 5);

        // --- Spec ---

        /// <summary>Which rotation set to run. Only Solo exists today; Group is a placeholder (falls back to Solo).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        // --- General (meta only) ---

        /// <summary>Auto target switching among attackers (never pulls). Off by default for the priest — a DoT
        /// caster works better committing to one target so the DoTs tick out.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: false);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public PriestSettings()
        {
            InnerFire.Category = "Buffs";
            InnerFire.Description = "Keep Inner Fire up (spell-power / armor self-buff); it's castable in Shadowform.";
            PowerWordFortitude.Category = "Buffs";
            PowerWordFortitude.Description = "Keep Power Word: Fortitude (stamina) up out of combat.";
            ShadowProtection.Category = "Buffs";
            ShadowProtection.Description = "Keep Shadow Protection (shadow resistance) up out of combat.";
            DivineSpirit.Category = "Buffs";
            DivineSpirit.Description = "Keep Divine Spirit (spirit) up out of combat; auto-skips until learned.";

            CombatRange.Category = "Rotation";
            CombatRange.Description = "Combat distance reported to WRobot; the priest casts at range and wands at low mana.";
            UseRacials.Category = "Rotation";
            UseRacials.Description = "Use your race's combat racial (Blood Fury, Berserking, Arcane Torrent, War Stomp, Gift of the Naaru).";

            // Shadow-only knobs live in the Rotation tab but tag their spec, so the overlay shows them ONLY while
            // Shadow is active (Spec string matches PriestSpec.ToString()).
            Shadowform.Category = "Rotation";        Shadowform.Spec = "Shadow";
            Shadowform.Description = "Keep Shadowform (the Shadow DPS form) up; off plays as a Smite caster.";
            VampiricEmbrace.Category = "Rotation";   VampiricEmbrace.Spec = "Shadow";
            VampiricEmbrace.Description = "Maintain Vampiric Embrace (self-heal from your shadow damage); castable in form.";
            UseDevouringPlague.Category = "Rotation"; UseDevouringPlague.Spec = "Shadow";
            UseDevouringPlague.Description = "Maintain Devouring Plague (a self-heal DoT); off skips it entirely.";
            UseMindFlay.Category = "Rotation";       UseMindFlay.Spec = "Shadow";
            UseMindFlay.Description = "Use Mind Flay as the channelled filler while SW:Pain or Devouring Plague is up.";
            UseMindSear.Category = "Rotation";       UseMindSear.Spec = "Shadow";
            UseMindSear.Description = "Use Mind Sear (channelled AoE) when 2+ enemies cluster around the target; auto-skips until learned.";
            ShadowfiendManaPercent.Category = "Rotation"; ShadowfiendManaPercent.Spec = "Shadow";
            ShadowfiendManaPercent.Description = "Cast Shadowfiend below this mana % to refill mana; auto-skips until learned.";
            DispersionManaPercent.Category = "Rotation";  DispersionManaPercent.Spec = "Shadow";
            DispersionManaPercent.Description = "Dispersion below this mana % (emergency regen + damage reduction); auto-skips until the Shadow capstone is learned; 0 disables it.";

            ShieldHealthPercent.Category = "Survival";
            ShieldHealthPercent.Description = "Power Word: Shield below this HP% (a damage-absorb, castable in form); won't recast while the shield or Weakened Soul is up; 0 disables it.";
            HealHealthPercent.Category = "Survival";
            HealHealthPercent.Description = "Below this HP% drop Shadowform (you can't cast Holy heals in form) and cast the best known heal; 0 disables it.";
            FlashHealHealthPercent.Category = "Survival";
            FlashHealHealthPercent.Description = "Flash Heal below this HP% (fast but mana-hungry); also drops Shadowform first; 0 disables it.";
            RenewHealthPercent.Category = "Survival";
            RenewHealthPercent.Description = "Renew below this HP% (a HoT; Holy, so only out of form); 0 disables it.";
            HealManaPercent.Category = "Survival";
            HealManaPercent.Description = "Only cast a hard heal / Renew while mana is above this %, so you don't shift out of form for a heal you can't afford.";
            PsychicScreamHealthPercent.Category = "Survival";
            PsychicScreamHealthPercent.Description = "Psychic Scream (panic AoE fear) when 2+ enemies are in melee and below this HP%, solo; 0 disables it.";
            EmergencyHealthPercent.Category = "Survival";
            EmergencyHealthPercent.Description = "Use an emergency healthstone or potion below this health %; 0 disables it.";

            UseWand.Category = "Mana";
            UseWand.Description = "Wand (Shoot) low targets or when nearly out of mana; needs a wand equipped.";
            WandTargetHealthPercent.Category = "Mana";
            WandTargetHealthPercent.Description = "Start wanding once the target drops to/below this HP% (or whenever mana is nearly empty).";

            ContentMode.Category = "Spec";
            ContentMode.Description = "Which rotation set to run; only Solo exists today (Group is a placeholder that falls back to Solo).";
            AutoAssignTalents.Category = "Spec";
            AutoAssignTalents.Description = "Automatically spend talent points using the active spec's default build.";

            AutoSwitchTarget.Category = "General";
            AutoSwitchTarget.Description = "Auto-switch among attackers (never pulls); off by default since a DoT caster prefers committing to one target.";
            DebugProfiling.Category = "General";
            DebugProfiling.Description = "Dev aid: periodically log rotation tick time, the most expensive steps, and learned damage.";

            _all = new Setting[]
            {
                // Buffs
                InnerFire, PowerWordFortitude, ShadowProtection, DivineSpirit,
                // Rotation (general, then the Shadow-only knobs that show only in Shadow)
                CombatRange, UseRacials,
                Shadowform, VampiricEmbrace, UseDevouringPlague, UseMindFlay, UseMindSear,
                ShadowfiendManaPercent, DispersionManaPercent,
                // Survival
                ShieldHealthPercent, HealHealthPercent, FlashHealHealthPercent, RenewHealthPercent,
                HealManaPercent, PsychicScreamHealthPercent, EmergencyHealthPercent,
                // Mana
                UseWand, WandTargetHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
