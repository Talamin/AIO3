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
    /// Solo Survival hunter (leveling/grinding, 10-80). Ranged + pet, sharing the <see cref="PetControl"/>
    /// upkeep and the <see cref="HunterCommon"/> baseline; the signature is Explosive Shot + Black Arrow
    /// (the Lock and Load engine) on top of the usual sting/shots. Everything pet-related is gated on the
    /// pet existing, so it degrades to ranged-only when petless.
    /// </summary>
    public sealed class SoloSurvival : IRotation
    {
        public string Name => "Hunter - Solo Survival";

        private readonly HunterSettings _settings;

        public SoloSurvival() : this(new HunterSettings()) { }

        public SoloSurvival(HunterSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => HunterCommon.WithPetSpecials(_settings, new List<RotationStep>
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
            PetControl.Taunt(ctx => _settings.ManagePet.Value, "Growl", priority: 0.8f),
            HunterCommon.Backpedal(_settings, priority: 0.9f),

            // --- baseline / upkeep ---
            HunterCommon.AutoShot(priority: 1f),
            HunterCommon.Aspect(_settings, priority: 1.5f),
            HunterCommon.Misdirection(_settings, priority: 1.8f),

            // --- offensive racials + cooldown ---
            CombatBlocks.OffensiveRacial("Blood Fury", 3f, ctx => _settings.UseRacials.Value),
            CombatBlocks.OffensiveRacial("Berserking", 3.01f, ctx => _settings.UseRacials.Value),
            Skill.Spell("Rapid Fire").Priority(3.5f).On(Targets.Self)
                 .When(ctx => _settings.UseCooldowns.Value && ctx.HasEnemyTarget
                              && (ctx.Target.IsBoss() || ctx.Target.IsElite
                                  || ctx.EnemiesWithin(HunterCommon.AoeRadius) >= _settings.AoeThreshold.Value)),

            // --- debuffs ---
            HunterCommon.HuntersMark(priority: 5f),
            HunterCommon.SerpentSting(priority: 6f),

            // --- shots ---
            HunterCommon.KillShot(priority: 7f),
            HunterCommon.KillCommand(priority: 7.5f),
            // Explosive Shot + Black Arrow: the Lock and Load engine — fire on cooldown.
            Skill.Spell("Explosive Shot").Priority(8f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin),
            Skill.Spell("Black Arrow").Priority(8.5f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin),
            Skill.Spell("Aimed Shot").Priority(9f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin),
            Skill.Spell("Multi-Shot").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin
                              && _settings.UseAoe.Value && ctx.EnemiesWithin(HunterCommon.AoeRadius) >= _settings.AoeThreshold.Value),
            Skill.Spell("Arcane Shot").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin),
            Skill.Spell("Steady Shot").Priority(12f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance >= HunterCommon.RangedMin && !ctx.Game.PlayerIsMoving),

            // --- slows + melee fallback ---
            HunterCommon.ConcussiveShot(priority: 13f),
            Skill.Spell("Raptor Strike").Priority(14f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Target.Distance < HunterCommon.RangedMin),
            HunterCommon.Disengage(_settings, priority: 14.5f),
        });
    }
}
