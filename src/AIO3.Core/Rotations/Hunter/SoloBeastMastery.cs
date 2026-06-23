using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Hunter
{
    /// <summary>
    /// Solo Beast Mastery hunter (leveling/grinding, 10-80). Ranged + pet: keep the pet summoned and on the
    /// target, keep Auto Shot / the right aspect / Hunter's Mark / Serpent Sting up, then cycle Kill
    /// Command, Arcane / Steady Shot, finishing sub-20% with Kill Shot. Everything pet-related is gated on
    /// the pet actually existing (<c>ctx.Pet</c>), so a petless hunter (below the taming level / untamed /
    /// a product that owns the pet) plays as a clean ranged DPS with the pet steps simply skipped.
    ///
    /// Thin: composes the shared <see cref="HunterCommon"/> / <see cref="PetControl"/> / Layer 3 blocks and
    /// adds only the BM-specific shots. Unknown spells auto-skip, so it fills in as the player levels.
    /// </summary>
    public sealed class SoloBeastMastery : IRotation
    {
        public string Name => "Hunter - Solo Beast Mastery";

        private readonly HunterSettings _settings;

        public SoloBeastMastery() : this(new HunterSettings()) { }

        public SoloBeastMastery(HunterSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => Racials.With(HunterCommon.WithPetSpecials(_settings, new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            HunterCommon.FeignDeath(_settings, priority: 0.2f),
            Skill.Spell("Deterrence").Priority(0.3f).On(Targets.Self)
                 .When(ctx => ctx.Me.HealthPercent < 40 && ctx.EnemiesTargetingMe >= 1),

            // --- pet upkeep (all skip when petless) ---
            PetControl.Summon(ctx => _settings.ManagePet.Value, "Call Pet", "Revive Pet", priority: 0.5f),
            PetControl.Heal(ctx => _settings.ManagePet.Value && _settings.PetHealPercent.Value > 0,
                "Mend Pet", ctx => _settings.PetHealPercent.Value, priority: 0.6f),
            PetControl.Attack(ctx => _settings.ManagePet.Value, priority: 0.7f),
            // Pull mobs off us onto the pet with Growl (auto-skips if the pet has no taunt).
            PetControl.Taunt(ctx => _settings.ManagePet.Value, "Growl", priority: 0.8f),
            // Regain ranged distance when a mob closed to melee but is on the pet (cliff-safe).
            HunterCommon.Backpedal(_settings, priority: 0.9f),

            // --- baseline / upkeep (also maintained out of combat) ---
            HunterCommon.AutoShot(priority: 1f),
            HunterCommon.Aspect(_settings, priority: 1.5f),
            HunterCommon.Misdirection(_settings, priority: 1.8f),

            // Intimidation interrupt: the pet's stun, so only with an alive pet and when enabled.
            Skill.Spell("Intimidation").Priority(2f).On(Targets.CurrentEnemy)
                 .When(ctx => _settings.InterruptCasts.Value
                              && ctx.Pet != null && ctx.Pet.IsAlive && ctx.Target.IsCasting),

            // --- cooldowns (racials are appended by the shared Racials bundle at the 3.0 band) ---
            // These are Self-cast cooldowns, so the engine evaluates them every tick even with no target.
            // HasEnemyTarget MUST come before any ctx.Target.* access (IsElite is an instance property and
            // would NRE on a null target) — and they're pointless without an enemy anyway.
            Skill.Spell("Bestial Wrath").Priority(3.5f).On(Targets.Self)
                 .When(ctx => _settings.UseCooldowns.Value && ctx.HasEnemyTarget && ctx.Pet != null && ctx.Pet.IsAlive
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite || ctx.EnemiesWithin(HunterCommon.AoeRadius) >= 2)),
            Skill.Spell("Rapid Fire").Priority(3.6f).On(Targets.Self)
                 .When(ctx => _settings.UseCooldowns.Value && ctx.HasEnemyTarget
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite
                                  || ctx.EnemiesWithin(HunterCommon.AoeRadius) >= _settings.AoeThreshold.Value)),

            // --- debuffs ---
            HunterCommon.HuntersMark(priority: 5f),
            HunterCommon.SerpentSting(priority: 6f),

            // --- shots ---
            HunterCommon.KillShot(priority: 7f),
            HunterCommon.KillCommand(priority: 8f),
            Skill.Spell("Multi-Shot").Priority(9f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin
                              && _settings.UseAoe.Value && ctx.EnemiesWithin(HunterCommon.AoeRadius) >= _settings.AoeThreshold.Value),
            Skill.Spell("Arcane Shot").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin),
            // Steady Shot is the filler — a cast-time shot, so only while standing still.
            Skill.Spell("Steady Shot").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin && !ctx.Game.PlayerIsMoving),

            // --- slows + melee fallback ---
            HunterCommon.ConcussiveShot(priority: 12f),
            Skill.Spell("Raptor Strike").Priority(13f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance < HunterCommon.RangedMin),
            HunterCommon.Disengage(_settings, priority: 13.5f),
        }), ctx => _settings.UseRacials.Value, basePriority: 3f);
    }
}
