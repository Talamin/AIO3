using System.Collections.Generic;
using AIO3.Core.Combat;
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

        /// <summary>Create a Healthstone out of combat when we carry none and have a Soul Shard to spend — the
        /// emergency-heal step uses it, so without this the supply runs dry.</summary>
        public readonly ToggleSetting CreateHealthstone =
            new ToggleSetting("createHs", "Create a Healthstone when missing", value: true);

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

        /// <summary>Let the Voidwalker TANK mobs off you: it taunts (Torment) whatever is attacking the cloth
        /// caster — the big solo survival win. Auto-skips for Imp / Felhunter (no Torment).</summary>
        public readonly ToggleSetting PetTank =
            new ToggleSetting("petTank", "Pet tanks (taunt off you)", value: true);

        /// <summary>Keep the Imp's Firebolt on AUTOCAST (the Imp fires its ranged nuke itself — the right model for
        /// a cast-time, no-cooldown ability). Off turns the autocast back off. Auto-skips for non-Imps.</summary>
        public readonly ToggleSetting ImpFirebolt =
            new ToggleSetting("impFirebolt", "Imp autocasts Firebolt", value: true);

        /// <summary>Keep the Imp's Phase Shift on AUTOCAST — the Imp phases out of harm's way on its own (a survival
        /// ability). Off turns the autocast back off; turn it off if you'd rather the Imp never interrupt its DPS to
        /// phase. Auto-skips for non-Imps.</summary>
        public readonly ToggleSetting ImpPhaseShift =
            new ToggleSetting("impPhaseShift", "Imp autocasts Phase Shift", value: true);

        // --- Rotation ---

        /// <summary>Combat distance reported to WRobot (ICustomClass.Range). A warlock casts at range; the wand
        /// covers low mana.</summary>
        public readonly IntSetting CombatRange =
            new IntSetting("combatRange", "Combat range (yd)", value: 30, min: 5, max: 41, step: 1);

        /// <summary>Which curse to keep on the target. Agony is the leveling default (ramping DoT).</summary>
        public readonly ChoiceSetting Curse =
            new ChoiceSetting("curse", "Curse", "Agony",
                new[] { "Agony", "Doom", "Elements", "Tongues", "Weakness" });

        /// <summary>Use racials in combat (Blood Fury / Berserking / Arcane Torrent / War Stomp / Gift of the
        /// Naaru, per race). Gates the shared <see cref="Library.Racials"/> bundle.</summary>
        public readonly ToggleSetting UseRacials =
            new ToggleSetting("racials", "Use racials", value: true);

        /// <summary>How to interrupt enemy casts with the Felhunter's Spell Lock (the warlock's only interrupt):
        /// Smart / Always / Never. Smart currently behaves like Always (fire on any target cast); the empirical
        /// InterruptTracker integration is a later refinement. Auto-skips when the pet isn't a Felhunter.</summary>
        public readonly ChoiceSetting InterruptCasts =
            new ChoiceSetting("interrupt", "Interrupt casts (Spell Lock)", InterruptModes.Smart, InterruptModes.All);

        /// <summary>Stop casting the filler nuke once a NORMAL mob is below this HP% AND carries at least two of our
        /// ticking DoTs — let the DoTs finish it instead of overkilling with another Shadow Bolt / Incinerate. Saves
        /// mana and Life-Tap (health) pressure while leveling. Bosses/elites are never affected. 0 disables it.</summary>
        public readonly IntSetting LetDotsFinishHealthPercent =
            new IntSetting("dotsFinishHp", "Let DoTs finish below HP%", value: 20, min: 0, max: 60, step: 5);

        /// <summary>Harvest a Soul Shard with Drain Soul on a low, dying mob when we're short on shards. Shards are
        /// the reagent for Healthstones / Soulstones, so without this the emergency Healthstone has no supply. The
        /// channel also deals damage and fits the same window as "let the DoTs finish".</summary>
        public readonly ToggleSetting UseDrainSoul =
            new ToggleSetting("drainSoul", "Drain Soul for shards", value: true);

        /// <summary>Only Drain Soul a target at or below this HP% — you only keep the shard if the mob dies, so this
        /// targets a dying mob.</summary>
        public readonly IntSetting DrainSoulHealthPercent =
            new IntSetting("drainSoulHp", "Drain Soul at/below HP%", value: 25, min: 0, max: 60, step: 5);

        /// <summary>Only harvest while we hold this many Soul Shards or fewer (stop draining once stocked).</summary>
        public readonly IntSetting SoulShardKeep =
            new IntSetting("shardKeep", "Keep up to N Soul Shards", value: 3, min: 0, max: 20, step: 1);

        // --- Rotation: Demonology-only (shown only while Demonology is the active spec) ---

        /// <summary>When to pop Metamorphosis (the Demonology capstone — a big damage/survival form): "On cooldown"
        /// uses it whenever it's ready, "On bosses" saves it for elites/bosses, "Off" never casts it. Auto-skips
        /// until learned. Mirrors the old FC's SoloDemonologyMetamorphosis (OnCooldown / OnBosses / None).</summary>
        public readonly ChoiceSetting Metamorphosis =
            new ChoiceSetting("metamorphosis", "Metamorphosis", "On cooldown",
                new[] { "On cooldown", "On bosses", "Off" });

        /// <summary>Keep Demonic Empowerment up on the demon (spec buff; auto-skips if unknown / petless).</summary>
        public readonly ToggleSetting DemonicEmpowerment =
            new ToggleSetting("demonicEmpowerment", "Demonic Empowerment on pet", value: true);

        /// <summary>Cast Soul Fire when a Decimation/Molten-Core-style proc is up (gated on the buff; auto-skips
        /// if Soul Fire is unknown). Off the proc it stays behind Shadow Bolt, so leaving this on is harmless.</summary>
        public readonly ToggleSetting UseSoulFire =
            new ToggleSetting("soulFire", "Soul Fire on proc", value: true);

        // --- Rotation: Destruction-only (shown only while Destruction is the active spec) ---

        /// <summary>Use Conflagrate (consumes Immolate for a burst). Gated so it only fires while Immolate is up
        /// on the target; auto-skips if unknown.</summary>
        public readonly ToggleSetting UseConflagrate =
            new ToggleSetting("conflagrate", "Use Conflagrate", value: true);

        /// <summary>Use Chaos Bolt as a nuke when known (sits between Incinerate and the Shadow Bolt fallback).</summary>
        public readonly ToggleSetting UseChaosBolt =
            new ToggleSetting("chaosBolt", "Use Chaos Bolt", value: true);

        /// <summary>Use Shadowburn (the instant sub-20% execute) to finish a low target — costs a Soul Shard, so it
        /// only fires when we hold MORE than <see cref="SoulShardKeep"/> shards (never draining the pet/healthstone
        /// supply). Fills the window where the normal filler is suppressed by "let DoTs finish".</summary>
        public readonly ToggleSetting UseShadowburn =
            new ToggleSetting("shadowburn", "Shadowburn execute below 20% HP", value: true);

        /// <summary>Use an emergency healthstone/potion below this health %. 0 disables it.</summary>
        public readonly IntSetting EmergencyHealthPercent =
            new IntSetting("emergencyHp", "Emergency item below HP%", value: 30, min: 0, max: 90, step: 5);

        // --- Survival ---

        /// <summary>Channel Drain Life to self-heal when low and solo (no healer to rely on). 0 disables it.</summary>
        public readonly IntSetting DrainLifeHealthPercent =
            new IntSetting("drainLifeHp", "Drain Life below HP%", value: 40, min: 0, max: 90, step: 5);

        /// <summary>EMERGENCY Death Coil: when low AND meleed, Death Coil the attacker — an instant horror (1.5s
        /// flee) that ALSO heals us for the damage dealt. Strictly better than the Fear panic (Fear only scatters;
        /// Death Coil scatters AND heals), so it wins over Fear/Howl. Shares the Fear/Howl low-HP threshold.</summary>
        public readonly ToggleSetting UseDeathCoil =
            new ToggleSetting("useDeathCoil", "Emergency Death Coil when meleed + low HP", value: true);

        /// <summary>EMERGENCY Fear: with no Frost Nova, Fear the mob meleeing you to break melee for a brief heal
        /// window when low. A panic button, not a kite (DoTs break Fear).</summary>
        public readonly ToggleSetting UseFear =
            new ToggleSetting("useFear", "Emergency Fear when meleed + low HP", value: true);

        /// <summary>Fire the emergency Fear / Howl only below this health %. 0 disables both.</summary>
        public readonly IntSetting FearHealthPercent =
            new IntSetting("fearHp", "Emergency Fear/Howl below HP%", value: 25, min: 0, max: 90, step: 5);

        /// <summary>EMERGENCY Howl of Terror: when SURROUNDED (>= 2 mobs meleeing you) and low, fear everything
        /// nearby (self-cast PBAoE, like Frost Nova).</summary>
        public readonly ToggleSetting UseHowl =
            new ToggleSetting("useHowl", "Emergency Howl when surrounded + low HP", value: true);

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
            ArmorChoice.Description = "Which armor buff to keep up; Auto picks the best you know (Fel > Demon Armor > Demon Skin).";
            CreateHealthstone.Category = "Buffs";
            CreateHealthstone.Description = "Out of combat, spend a Soul Shard to make a Healthstone when you carry none, so the emergency heal has supply.";

            Pet.Category = "Pet";
            Pet.Description = "Which demon to summon; Auto picks per spec (Felguard/Imp/Voidwalker) with a fallback to the tanky Voidwalker.";
            ManagePet.Category = "Pet";
            ManagePet.Description = "Keep the demon summoned and attacking the target; turn off if a WRobot product manages the pet.";
            PetHealPercent.Category = "Pet";
            PetHealPercent.Description = "Health Funnel the pet when its health drops below this %, if you have HP to spare; 0 disables it.";
            PetTank.Category = "Pet";
            PetTank.Description = "Let the Voidwalker taunt mobs off you (Torment) to tank for the cloth caster; auto-skips for Imp/Felhunter.";
            ImpFirebolt.Category = "Pet";
            ImpFirebolt.Description = "Keep the Imp's Firebolt on autocast so it fires its ranged nuke itself; auto-skips for non-Imps.";
            ImpPhaseShift.Category = "Pet";
            ImpPhaseShift.Description = "Keep the Imp's Phase Shift on autocast so it phases out of danger; turn off to never interrupt its DPS.";

            CombatRange.Category = "Rotation";
            CombatRange.Description = "Combat distance reported to WRobot; the warlock casts at range and wands at low mana.";
            Curse.Category = "Rotation";
            Curse.Description = "Which curse to keep on the target; Agony is the leveling default (a ramping DoT).";
            UseRacials.Category = "Rotation";
            UseRacials.Description = "Use your race's combat racial (Blood Fury, Berserking, Arcane Torrent, War Stomp, Gift of the Naaru).";
            InterruptCasts.Category = "Rotation";
            InterruptCasts.Description = "How to interrupt enemy casts with the Felhunter's Spell Lock (Smart/Always/Never); auto-skips without a Felhunter.";
            LetDotsFinishHealthPercent.Category = "Rotation";
            LetDotsFinishHealthPercent.Description = "Stop nuking a normal mob below this HP% if 2+ of your DoTs tick, letting them finish it; 0 disables it.";
            UseDrainSoul.Category = "Rotation";
            UseDrainSoul.Description = "Drain Soul a low, dying mob to harvest a Soul Shard when short, supplying Healthstones and Soulstones.";
            DrainSoulHealthPercent.Category = "Rotation";
            DrainSoulHealthPercent.Description = "Only Drain Soul a target at or below this HP%, since you only keep the shard if the mob dies.";
            SoulShardKeep.Category = "Rotation";
            SoulShardKeep.Description = "Only harvest shards while you hold this many or fewer; stop draining once stocked to this count.";
            EmergencyHealthPercent.Category = "Rotation";
            EmergencyHealthPercent.Description = "Use an emergency Healthstone or potion when your health drops below this %; 0 disables it.";

            // Spec-only knobs live in the Rotation tab but tag their spec, so the overlay shows them ONLY while
            // that spec is active (Spec strings match WarlockSpec.ToString()). No more standalone spec tabs.
            Metamorphosis.Category = "Rotation";      Metamorphosis.Spec = "Demonology";
            Metamorphosis.Description = "When to pop Metamorphosis (the Demonology damage/survival form): on cooldown, only on bosses, or off.";
            DemonicEmpowerment.Category = "Rotation"; DemonicEmpowerment.Spec = "Demonology";
            DemonicEmpowerment.Description = "Keep Demonic Empowerment up on the demon; auto-skips if unknown or petless.";
            UseSoulFire.Category = "Rotation";        UseSoulFire.Spec = "Demonology";
            UseSoulFire.Description = "Cast Soul Fire when a Decimation/Molten-Core-style proc is up; off the proc it stays behind Shadow Bolt.";

            UseConflagrate.Category = "Rotation"; UseConflagrate.Spec = "Destruction";
            UseConflagrate.Description = "Use Conflagrate for a burst; only fires while Immolate is up on the target, which it consumes.";
            UseChaosBolt.Category = "Rotation";   UseChaosBolt.Spec = "Destruction";
            UseChaosBolt.Description = "Use Chaos Bolt as a nuke when known; sits between Incinerate and the Shadow Bolt fallback.";
            UseShadowburn.Category = "Rotation";  UseShadowburn.Spec = "Destruction";
            UseShadowburn.Description = "Use Shadowburn to instantly execute a sub-20% target; only when you hold more than your kept shards.";

            DrainLifeHealthPercent.Category = "Survival";
            DrainLifeHealthPercent.Description = "Channel Drain Life to self-heal when low and solo (no healer to rely on); 0 disables it.";
            UseDeathCoil.Category = "Survival";
            UseDeathCoil.Description = "When low and meleed, Death Coil the attacker: an instant 1.5s horror that also heals you for the damage.";
            UseFear.Category = "Survival";
            UseFear.Description = "When low with no Frost Nova, Fear the mob meleeing you to buy a brief heal window; a panic button, not a kite.";
            FearHealthPercent.Category = "Survival";
            FearHealthPercent.Description = "Fire the emergency Fear and Howl only below this health %; 0 disables both.";
            UseHowl.Category = "Survival";
            UseHowl.Description = "When low and surrounded (2+ mobs meleeing you), Howl of Terror to fear everything nearby (self-cast PBAoE).";

            LifeTapManaPercent.Category = "Mana";
            LifeTapManaPercent.Description = "Life Tap (trade health for mana, the signature warlock engine) when mana drops below this %.";
            LifeTapHealthFloor.Category = "Mana";
            LifeTapHealthFloor.Description = "Only Life Tap while health is above this %, so you never trade HP you can't afford.";
            GlyphLifeTap.Category = "Mana";
            GlyphLifeTap.Description = "Re-tap to keep the Glyph of Life Tap spell-power buff up; only worth enabling if you have that glyph.";
            UseWand.Category = "Mana";
            UseWand.Description = "Wand (Shoot) the target to conserve mana when low; needs a wand equipped.";
            WandManaPercent.Category = "Mana";
            WandManaPercent.Description = "Start wanding to conserve mana once your mana drops below this %.";

            ContentMode.Category = "Spec";
            ContentMode.Description = "Which rotation set to run; only Solo exists today (Group is a placeholder that falls back to Solo).";
            AutoAssignTalents.Category = "Spec";
            AutoAssignTalents.Description = "Automatically spend talent points using the active spec's default build.";

            AutoSwitchTarget.Category = "General";
            AutoSwitchTarget.Description = "Auto-switch among attackers (never pulls); off by default since a DoT/pet class prefers committing to one target.";
            DebugProfiling.Category = "General";
            DebugProfiling.Description = "Dev aid: periodically log rotation tick time, the most expensive steps, and learned damage.";

            _all = new Setting[]
            {
                // Buffs
                ArmorChoice, CreateHealthstone,
                // Pet
                Pet, ManagePet, PetHealPercent, PetTank, ImpFirebolt, ImpPhaseShift,
                // Rotation (general, then the spec-only knobs that show only in their spec)
                CombatRange, Curse, UseRacials, InterruptCasts, LetDotsFinishHealthPercent,
                UseDrainSoul, DrainSoulHealthPercent, SoulShardKeep, EmergencyHealthPercent,
                Metamorphosis, DemonicEmpowerment, UseSoulFire,   // Demonology-only
                UseConflagrate, UseChaosBolt, UseShadowburn,      // Destruction-only
                // Survival
                DrainLifeHealthPercent, UseDeathCoil, UseFear, FearHealthPercent, UseHowl,
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
