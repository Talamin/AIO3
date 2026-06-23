using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Live-tunable settings shared by all warrior specs (Fury/Arms/Protection read the ones they
    /// care about). One shared instance is edited by the in-game overlay and read by the active
    /// rotation every tick, so the settings panel stays stable when the spec is switched.
    /// </summary>
    public sealed class WarriorSettings
    {
        /// <summary>Rage kept in reserve before Heroic Strike dumps spare rage. Lower = HS fires sooner.</summary>
        public readonly IntSetting HeroicStrikeRageReserve =
            new IntSetting("hsReserve", "Heroic Strike rage reserve", value: 20, min: 0, max: 100, step: 5);

        /// <summary>Minimum nearby enemies before the AoE abilities fire.</summary>
        public readonly IntSetting AoeThreshold =
            new IntSetting("aoeCount", "AoE: min enemies", value: 2, min: 2, max: 10, step: 1);

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range) — how close the bot stands to
        /// the target. Tune if the bot ends up inside the mob instead of in front of it.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 5, min: 3, max: 15, step: 1);

        /// <summary>Use the Charge / Intercept gap-closers. Off by default: Charge can interfere with the
        /// product's own movement on engage (it aborts the fight on some setups). Enable to try it — it's
        /// throttled so it isn't re-issued mid-leap.</summary>
        public readonly ToggleSetting UseGapClosers =
            new ToggleSetting("useGapClosers", "Use Charge / Intercept", value: false);

        /// <summary>Tank opener: in Defensive Stance, Taunt a target at range to pull it to you when you
        /// can't Charge, so the fight starts sooner. Only acts out of combat on the product's current target
        /// (never selects one) and only in Defensive Stance, so it is a Protection tool — Fury/Arms keep
        /// Charge/Intercept. It pulls, so it is a toggle (turn off if a product owns pulling).</summary>
        public readonly ToggleSetting UseTauntPull =
            new ToggleSetting("tauntPull", "Taunt to pull (tank opener)", value: true);

        /// <summary>Hamstring fleeing targets below 40% health.</summary>
        public readonly ToggleSetting UseHamstring =
            new ToggleSetting("hamstring", "Hamstring fleeing targets", value: true);

        /// <summary>Which rotation set to run. Only Solo (questing/grinding) exists today; Group is here so
        /// the selector is ready when group/dungeon rotations are added (it falls back to Solo until then).</summary>
        public readonly ChoiceSetting ContentMode =
            new ChoiceSetting("mode", "Mode", "Solo", new[] { "Solo", "Group" });

        /// <summary>Automatically spend talent points using the active spec's default build.</summary>
        public readonly ToggleSetting AutoAssignTalents =
            new ToggleSetting("autoTalents", "Auto-assign talents", value: true);

        /// <summary>Use racials in combat (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the
        /// Naaru, per race). Gates the shared <see cref="Library.Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>How to interrupt enemy casts: Smart (learns what's interruptible) / Always / Never
        /// (set Never if a WRobot product already handles interrupts, to avoid fighting it).</summary>
        public readonly ChoiceSetting InterruptMode =
            new ChoiceSetting("interrupt", "Interrupt", InterruptModes.Smart, InterruptModes.All);

        /// <summary>Auto target switching: when several enemies are attacking, switch the current target
        /// to an attacker if it isn't already one. This is ONLY about which target we hit — it never
        /// pulls or starts a fight (the product owns the opener). On by default per Daniel's preference;
        /// turn it off if a WRobot product owns targeting and the two start fighting over the target.
        /// NOTE: other multi-enemy concerns (multi-target damage, defending against several attackers)
        /// will become their own separate settings later.</summary>
        public readonly ToggleSetting AutoSwitchTarget =
            new ToggleSetting("targetSelect", "Auto target switching", value: true);

        /// <summary>Let the learned per-ability damage (DamageTracker) choose between interchangeable
        /// strikes (advisory). Off = use the hand-tuned order. Safe either way.</summary>
        public readonly ToggleSetting UseDamageLearning =
            new ToggleSetting("dmgLearn", "Use damage learning", value: false);

        /// <summary>Use the major offensive cooldown on elites/bosses/packs. For the warrior this is
        /// Recklessness (Fury/Arms) — the label names it so it isn't a mystery toggle.</summary>
        public readonly ToggleSetting UseCooldowns =
            new ToggleSetting("cooldowns", "Use cooldowns (Recklessness)", value: true);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        /// <summary>Dev aid: periodically log the rotation tick time + most expensive steps, and the
        /// learned per-ability damage (the DamageTracker, measure-only for now).</summary>
        public readonly ToggleSetting DebugProfiling =
            new ToggleSetting("debugPerf", "Debug logging", value: false);

        private readonly Setting[] _all;

        public WarriorSettings()
        {
            // Tab assignment for the in-game overlay. Rotation = anything that changes what/how the
            // rotation fights; Spec = spec/mode/talents; General = meta toggles only (targeting policy,
            // damage-learning advisory, dev logging) that don't change which abilities the rotation casts.
            HeroicStrikeRageReserve.Category = "Rotation";
            AoeThreshold.Category = "Rotation";
            CombatRange.Category = "Rotation";
            UseGapClosers.Category = "Rotation";
            UseTauntPull.Category = "Rotation";
            UseHamstring.Category = "Rotation";
            UseRacials.Category = "Rotation";
            InterruptMode.Category = "Rotation";
            UseCooldowns.Category = "Rotation";
            EmergencyHealthPercent.Category = "Rotation";

            ContentMode.Category = "Spec";
            AutoAssignTalents.Category = "Spec";

            AutoSwitchTarget.Category = "General";
            UseDamageLearning.Category = "General";
            DebugProfiling.Category = "General";

            _all = new Setting[]
            {
                // Rotation
                HeroicStrikeRageReserve, AoeThreshold, CombatRange, UseGapClosers, UseTauntPull, UseHamstring,
                UseRacials, InterruptMode, UseCooldowns, EmergencyHealthPercent,
                // Spec
                ContentMode, AutoAssignTalents,
                // General
                AutoSwitchTarget, UseDamageLearning, DebugProfiling
            };
        }

        public IReadOnlyList<Setting> All => _all;
    }
}
