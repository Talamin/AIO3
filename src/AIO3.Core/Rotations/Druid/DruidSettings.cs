using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Druid
{
    /// <summary>
    /// Live-tunable settings shared by all druid specs (Feral cat/bear and Balance share this one instance, like
    /// the rogue shares Combat/Assassination). One instance is edited by the in-game overlay and read by the active
    /// rotation every tick; thresholds are read at eval time so overlay edits apply live.
    ///
    /// The druid is a hybrid: Feral is a melee energy/combo (cat) + rage (bear) shapeshifter, Balance is an Eclipse
    /// caster. So the General/Survival tab holds the shared shapeshifter knobs (in-combat self-heal thresholds,
    /// Barkskin, Innervate), while the Feral-only knobs (Bear count, finisher CP, Tiger's Fury, the stealth opener)
    /// and the Balance-only knobs (DoTs, AoE, Force of Nature, heal thresholds) live in the Rotation tab but tag
    /// <c>Setting.Spec</c>, so the overlay shows each ONLY while its spec is active — the same pattern the Rogue and
    /// Warlock use for spec-only knobs (Spec strings match <see cref="DruidSpec"/>.ToString(): "Feral" / "Balance").
    /// </summary>
    public sealed class DruidSettings
    {
        // --- Buffs (out of combat) ---

        /// <summary>Keep Mark of the Wild (or Gift of the Wild in a group) up on yourself out of combat.</summary>
        public readonly ToggleSetting UseMarkOfTheWild =
            new ToggleSetting("markOfWild", "Keep Mark of the Wild up", value: true);

        /// <summary>Keep Thorns up on yourself out of combat (reflects melee damage).</summary>
        public readonly ToggleSetting UseThorns =
            new ToggleSetting("thorns", "Keep Thorns up", value: true);

        // --- Rotation (shared) ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). Feral fights in melee once Cat/Bear is
        /// learned; pre-form (and Balance) it's a caster range. The module picks the actual value (it knows the
        /// spec); this is the melee number Feral uses once shifted.</summary>
        public readonly IntSetting MeleeRange =
            new IntSetting("meleeRange", "Melee range (yd)", value: 5, min: 3, max: 8, step: 1);

        /// <summary>Caster distance reported to WRobot for Balance (and a pre-form Feral that nukes with Wrath).</summary>
        public readonly IntSetting CasterRange =
            new IntSetting("casterRange", "Caster range (yd)", value: 29, min: 5, max: 36, step: 1);

        /// <summary>Use racials (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the Naaru, per race)
        /// in combat. Gates the shared <see cref="Library.Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>Use the major offensive cooldowns on elites/bosses/packs (Feral: Berserk; Balance: Force of
        /// Nature). The label names them so it isn't a mystery toggle.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns (Berserk / Force of Nature)", value: true);

        // --- Rotation: Feral-only (shown only while Feral is the active spec) ---

        /// <summary>Shift to (Dire) Bear Form when at least this many enemies are meleeing us — bear is the tank/AoE
        /// form when surrounded. Below it, stay in Cat Form for single-target DPS. Feral-only.</summary>
        public readonly IntSetting BearCount =
            new IntSetting("bearCount", "Bear form: min attackers", value: 2, min: 2, max: 6, step: 1);

        /// <summary>Combo points required before a Cat finisher (Rip / Ferocious Bite) is spent. Feral-only.</summary>
        public readonly IntSetting FinisherComboPoints =
            new IntSetting("finisherCp", "Finisher at combo points", value: 5, min: 1, max: 5, step: 1);

        /// <summary>Don't apply/refresh Rip (the Cat bleed finisher) below this target HP% — a fresh bleed won't tick
        /// out before the mob dies, so the combo points go to Ferocious Bite instead. Feral-only.</summary>
        public readonly IntSetting RipHealth =
            new IntSetting("ripHealth", "Rip only above enemy HP%", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Use Tiger's Fury (an instant energy + damage cooldown) on cooldown while in Cat Form. Feral-only.</summary>
        public readonly ToggleSetting UseTigersFury =
            new ToggleSetting("tigersFury", "Use Tiger's Fury on cooldown", value: true);

        /// <summary>Keep Faerie Fire (Feral) up (the -armor debuff). Feral-only.</summary>
        public readonly ToggleSetting UseFaerieFire =
            new ToggleSetting("faerieFire", "Use Faerie Fire (Feral)", value: true);

        /// <summary>Open from Prowl (stealth) before the fight — Ravage from behind / Pounce from the front. Off by
        /// default: WRobot products usually own the pull, and a stealthed pull can desync with them. Feral-only.</summary>
        public readonly ToggleSetting UseProwl =
            new ToggleSetting("prowl", "Open from Prowl (stealth)", value: false);

        /// <summary>Which opener starts a Prowl-opened fight (only used when "Open from Prowl" is on). Auto (the
        /// default) lets the FC pick by position: Ravage when we're BEHIND the target (a big positional hit) else
        /// Pounce from the FRONT (positional-free stun) — so Ravage is only chosen when it will actually land. Force
        /// "Ravage" or "Pounce" to override the positional pick. Feral-only.</summary>
        public readonly ChoiceSetting ProwlOpener =
            new ChoiceSetting("prowlOpener", "Prowl opener", "Auto", new[] { "Auto", "Ravage", "Pounce" });

        /// <summary>Use Maul (the Bear rage dump) when rage is above this reserve. Feral-only.</summary>
        public readonly IntSetting MaulRageReserve =
            new IntSetting("maulRage", "Maul above rage", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Interrupt enemy casts with Bash (Bear) while tanking. Smart learns what's interruptible; Never
        /// disables it (e.g. a product owns interrupts). Feral-only.</summary>
        public readonly ChoiceSetting InterruptMode =
            new ChoiceSetting("interrupt", "Interrupt (Bash)", InterruptModes.Smart, InterruptModes.All);

        // --- Rotation: Balance-only (shown only while Balance is the active spec) ---

        /// <summary>Keep Moonfire up as a DoT (besides Insect Swarm). Off keeps the Eclipse rotation cleaner on
        /// trash; the DoT still fires on bosses. Balance-only.</summary>
        public readonly ToggleSetting UseMoonfire =
            new ToggleSetting("moonfire", "Use Moonfire DoT", value: true);

        /// <summary>Keep Insect Swarm up as a DoT. Balance-only.</summary>
        public readonly ToggleSetting UseInsectSwarm =
            new ToggleSetting("insectSwarm", "Use Insect Swarm DoT", value: true);

        /// <summary>Don't apply/refresh a DoT (Moonfire / Insect Swarm) below this target HP% — it won't tick out
        /// before the mob dies, so the GCD is better spent on a nuke. Balance-only.</summary>
        public readonly IntSetting DotHealth =
            new IntSetting("dotHealth", "DoTs only above enemy HP%", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Use the Balance AoE spells (Hurricane / Typhoon) on packs. Balance-only.</summary>
        public readonly ToggleSetting UseAoe =
            new ToggleSetting("useAoe", "Use AoE (Hurricane / Typhoon)", value: true);

        /// <summary>Use Starfall (the channelled star AoE cooldown) on packs / bosses. Balance-only.</summary>
        public readonly ToggleSetting UseStarfall =
            new ToggleSetting("starfall", "Use Starfall", value: true);

        /// <summary>Minimum nearby enemies before the Balance AoE spells (Starfall / Hurricane / Typhoon) fire.
        /// Balance-only.</summary>
        public readonly IntSetting AoeTargets =
            new IntSetting("aoeCount", "AoE: min enemies", value: 3, min: 2, max: 10, step: 1);

        /// <summary>Summon Force of Nature (treants) on a boss/elite. Balance-only.</summary>
        public readonly ToggleSetting UseForceOfNature =
            new ToggleSetting("forceOfNature", "Use Force of Nature (treants)", value: true);

        // --- Survival (shared shapeshifter self-heal / defensives) ---

        /// <summary>Cast Barkskin (off-GCD damage reduction; usable in any form) below this health %. 0 disables it.</summary>
        public readonly IntSetting BarkskinHealthPercent =
            new IntSetting("barkskinHp", "Barkskin below HP%", value: 35, min: 0, max: 90, step: 5);

        /// <summary>In-combat self-heal below this health %. Prefer an INSTANT via the Predator's Swiftness proc so
        /// it doesn't drop form; otherwise shift out to heal (gated on enough mana). The druid's survival edge.</summary>
        public readonly IntSetting InCombatHealHealthPercent =
            new IntSetting("icHealHp", "In-combat heal below HP%", value: 35, min: 0, max: 90, step: 5);

        /// <summary>Minimum mana % before an in-combat heal that shifts out of form is allowed (the simple mana gate
        /// — we deliberately drop the old GetSpellCost arithmetic). A free Predator's Swiftness instant ignores this.</summary>
        public readonly IntSetting HealManaPercent =
            new IntSetting("healMana", "Self-heal needs mana above %", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Use Regrowth as the in-combat self-heal (instant via Predator's Swiftness, else a shift-out cast).</summary>
        public readonly ToggleSetting UseRegrowthIC =
            new ToggleSetting("regrowthIC", "In-combat Regrowth", value: true);

        /// <summary>Use Rejuvenation as an in-combat HoT (a shift-out cast).</summary>
        public readonly ToggleSetting UseRejuvenationIC =
            new ToggleSetting("rejuvIC", "In-combat Rejuvenation", value: true);

        /// <summary>Use Healing Touch as the in-combat big heal (instant via Predator's Swiftness, else a shift-out
        /// cast).</summary>
        public readonly ToggleSetting UseHealingTouchIC =
            new ToggleSetting("healingTouchIC", "In-combat Healing Touch", value: true);

        /// <summary>Use Innervate (the mana cooldown) on yourself below this mana %. 0 disables it.</summary>
        public readonly IntSetting InnervateManaPercent =
            new IntSetting("innervate", "Innervate below mana%", value: 25, min: 0, max: 90, step: 5);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 25, min: 0, max: 90, step: 5);

        // --- Spec ---

        /// <summary>Which rotation set to run. Only Solo exists today; Group is a placeholder (falls back to Solo).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        // --- General (meta only) ---

        /// <summary>Auto target switching among attackers (never pulls). Off by default so it can't fight a product
        /// that owns targeting.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: false);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps and learned damage.</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public DruidSettings()
        {
            UseMarkOfTheWild.Category = "Buffs";
            UseMarkOfTheWild.Description = "Keep Mark of the Wild (or Gift of the Wild in a group) up on yourself out of combat.";
            UseThorns.Category = "Buffs";
            UseThorns.Description = "Keep Thorns up on yourself out of combat (reflects melee damage back at attackers).";

            MeleeRange.Category = "Rotation";
            MeleeRange.Description = "Melee combat distance Feral uses once shifted into Cat/Bear form.";
            CasterRange.Category = "Rotation";
            CasterRange.Description = "Caster combat distance used by Balance (and a pre-form Feral nuking with Wrath).";
            UseRacials.Category = "Rotation";
            UseRacials.Description = "Use your race's combat racial (Blood Fury, Berserking, Arcane Torrent, War Stomp, etc.).";
            UseCooldowns.Category = "Rotation";
            UseCooldowns.Description = "Use major offensive cooldowns on elites/bosses/packs (Feral: Berserk; Balance: Force of Nature).";

            // Feral-only knobs live in the Rotation tab but tag their spec, so the overlay shows them ONLY while
            // Feral is active (Spec strings match DruidSpec.ToString()).
            BearCount.Category = "Rotation";            BearCount.Spec = "Feral";
            BearCount.Description = "Shift to (Dire) Bear Form when at least this many enemies are meleeing you; below it, stay in Cat.";
            FinisherComboPoints.Category = "Rotation";  FinisherComboPoints.Spec = "Feral";
            FinisherComboPoints.Description = "Combo points required before spending a Cat finisher (Rip / Ferocious Bite).";
            RipHealth.Category = "Rotation";            RipHealth.Spec = "Feral";
            RipHealth.Description = "Only apply/refresh Rip above this enemy HP%; below it, spend combo points on Ferocious Bite instead.";
            UseTigersFury.Category = "Rotation";        UseTigersFury.Spec = "Feral";
            UseTigersFury.Description = "Use Tiger's Fury (instant energy + damage cooldown) on cooldown while in Cat Form.";
            UseFaerieFire.Category = "Rotation";        UseFaerieFire.Spec = "Feral";
            UseFaerieFire.Description = "Keep Faerie Fire (Feral) up on the target for the -armor debuff.";
            UseProwl.Category = "Rotation";             UseProwl.Spec = "Feral";
            UseProwl.Description = "Open from Prowl stealth before the fight (off by default, since a stealthed pull can desync with WRobot).";
            ProwlOpener.Category = "Rotation";          ProwlOpener.Spec = "Feral";
            ProwlOpener.Description = "Which Prowl opener to use: Auto picks Ravage from behind else Pounce from the front; or force one.";
            MaulRageReserve.Category = "Rotation";      MaulRageReserve.Spec = "Feral";
            MaulRageReserve.Description = "Use Maul (the Bear rage dump) only when rage is above this reserve.";
            InterruptMode.Category = "Rotation";        InterruptMode.Spec = "Feral";
            InterruptMode.Description = "Interrupt enemy casts with Bash while tanking: Smart learns what's interruptible; Never disables it.";

            // Balance-only knobs, same pattern (shown only while Balance is the active spec).
            UseMoonfire.Category = "Rotation";          UseMoonfire.Spec = "Balance";
            UseMoonfire.Description = "Keep Moonfire up as a DoT; off keeps the Eclipse rotation cleaner on trash (still fires on bosses).";
            UseInsectSwarm.Category = "Rotation";       UseInsectSwarm.Spec = "Balance";
            UseInsectSwarm.Description = "Keep Insect Swarm up on the target as a DoT.";
            DotHealth.Category = "Rotation";            DotHealth.Spec = "Balance";
            DotHealth.Description = "Only apply/refresh DoTs (Moonfire / Insect Swarm) above this enemy HP%; below it, just nuke.";
            UseAoe.Category = "Rotation";               UseAoe.Spec = "Balance";
            UseAoe.Description = "Use the Balance AoE spells (Hurricane / Typhoon) on packs.";
            UseStarfall.Category = "Rotation";          UseStarfall.Spec = "Balance";
            UseStarfall.Description = "Use Starfall (the channelled star AoE cooldown) on packs and bosses.";
            AoeTargets.Category = "Rotation";           AoeTargets.Spec = "Balance";
            AoeTargets.Description = "Minimum nearby enemies before the Balance AoE spells (Starfall / Hurricane / Typhoon) fire.";
            UseForceOfNature.Category = "Rotation";     UseForceOfNature.Spec = "Balance";
            UseForceOfNature.Description = "Summon Force of Nature (treants) on a boss or elite.";

            BarkskinHealthPercent.Category = "Survival";
            BarkskinHealthPercent.Description = "Cast Barkskin (off-GCD damage reduction, usable in any form) below this health %. 0 disables it.";
            InCombatHealHealthPercent.Category = "Survival";
            InCombatHealHealthPercent.Description = "Self-heal in combat below this health %, preferring an instant proc so you don't drop form.";
            HealManaPercent.Category = "Survival";
            HealManaPercent.Description = "Minimum mana % before an in-combat heal that shifts out of form is allowed (a free instant ignores this).";
            UseRegrowthIC.Category = "Survival";
            UseRegrowthIC.Description = "Use Regrowth as the in-combat self-heal (instant via Predator's Swiftness, else a shift-out cast).";
            UseRejuvenationIC.Category = "Survival";
            UseRejuvenationIC.Description = "Use Rejuvenation as an in-combat HoT (a shift-out cast).";
            UseHealingTouchIC.Category = "Survival";
            UseHealingTouchIC.Description = "Use Healing Touch as the in-combat big heal (instant via Predator's Swiftness, else a shift-out cast).";
            InnervateManaPercent.Category = "Survival";
            InnervateManaPercent.Description = "Use Innervate (the mana cooldown) on yourself below this mana %. 0 disables it.";
            EmergencyHealthPercent.Category = "Survival";
            EmergencyHealthPercent.Description = "Use an emergency healthstone/potion below this health %. 0 disables it.";

            ContentMode.Category = "Spec";
            ContentMode.Description = "Which rotation set to run. Only Solo exists today; Group is a placeholder that falls back to Solo.";
            AutoAssignTalents.Category = "Spec";
            AutoAssignTalents.Description = "Automatically spend talent points using the active spec's default build.";

            AutoSwitchTarget.Category = "General";
            AutoSwitchTarget.Description = "Auto-switch among attackers (never pulls). Off by default so it can't fight a product that owns targeting.";
            DebugProfiling.Category = "General";
            DebugProfiling.Description = "Dev aid: periodically log rotation tick time, the most expensive steps, and learned damage.";

            _all = new Setting[]
            {
                // Buffs
                UseMarkOfTheWild, UseThorns,
                // Rotation (general, then the Feral-only and Balance-only knobs that show only in their spec)
                MeleeRange, CasterRange, UseRacials, UseCooldowns,
                BearCount, FinisherComboPoints, RipHealth, UseTigersFury, UseFaerieFire, UseProwl, ProwlOpener,
                MaulRageReserve, InterruptMode, // Feral-only
                UseMoonfire, UseInsectSwarm, DotHealth, UseAoe, UseStarfall, AoeTargets, UseForceOfNature, // Balance-only
                // Survival
                BarkskinHealthPercent, InCombatHealHealthPercent, HealManaPercent,
                UseRegrowthIC, UseRejuvenationIC, UseHealingTouchIC, InnervateManaPercent, EmergencyHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
