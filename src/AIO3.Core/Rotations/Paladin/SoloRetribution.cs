using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Paladin
{
    /// <summary>
    /// Solo Retribution paladin (leveling/grinding, 10-80). Melee hybrid: keep the seal/aura/blessing up,
    /// judge on cooldown for mana + damage, and cycle Crusader Strike / Divine Storm with free Exorcism
    /// procs, finishing sub-20% with Hammer of Wrath. Self-heals (Holy Light / Art of War Flash of Light)
    /// and Divine Plea keep it sustainable while leveling without a healer.
    ///
    /// Thin: composes the shared <see cref="PaladinCommon"/> / Layer 3 blocks and adds only the
    /// Ret-specific filler in priority order. Unknown/unusable spells are skipped automatically
    /// (IsSpellKnown), so this single priority list fills in as the player levels.
    /// </summary>
    public sealed class SoloRetribution : IRotation
    {
        public string Name => "Paladin - Solo Retribution";

        private readonly PaladinSettings _settings;

        public SoloRetribution() : this(new PaladinSettings()) { }

        public SoloRetribution(PaladinSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => Racials.With(new List<RotationStep>
        {
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            PaladinCommon.LayOnHands(_settings, priority: 0.1f),
            PaladinCommon.DivineProtection(_settings, priority: 0.2f),
            PaladinCommon.ArtOfWarFlash(_settings, priority: 0.4f),   // free instant heal from the proc
            PaladinCommon.HolyLightSelf(_settings, priority: 0.5f),
            PaladinCommon.HandOfFreedom(priority: 0.6f),             // off-GCD: break a root/snare so we can keep meleeing
            PaladinCommon.DivinePlea(_settings, priority: 0.7f),

            // --- baseline / upkeep (also maintained out of combat) ---
            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Hammer of Justice", priority: 2f, mode: ctx => _settings.InterruptMode.Value),
            PaladinCommon.Seal(PaladinSpec.Retribution, _settings, priority: 3f),
            PaladinCommon.Aura(PaladinSpec.Retribution, _settings, priority: 3.1f),
            PaladinCommon.Blessing(PaladinSpec.Retribution, _settings, priority: 3.2f),

            // --- major cooldown (racials are appended by the shared Racials bundle at the 4.0 band) ---
            PaladinCommon.AvengingWrath(_settings, priority: 4.3f),

            // --- single target priority ---
            PaladinCommon.HammerOfWrath(priority: 6f),                  // ranged execute < 20%
            PaladinCommon.Judgement(_settings, priority: 7f),          // on cooldown: mana return + damage
            // Exorcism is free and instant during a "The Art of War" proc — use it promptly (no melee downtime).
            Skill.Spell("Exorcism").Priority(7.5f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.HasAura("The Art of War")),
            Skill.Spell("Divine Storm").Priority(8f).On(Targets.CurrentEnemy),
            Skill.Spell("Crusader Strike").Priority(9f).On(Targets.CurrentEnemy),
            // Leveling-Ret filler (no Art of War talent → never procs the instant arm above). In 3.3.5a Exorcism
            // hits every creature type, so it's a valid hard-cast nuke (let IsSpellReady/range gate it). Below
            // Crusader Strike, above the auto-attack/wand floor; the proc arm at 7.5 still wins when up.
            Skill.Spell("Exorcism").Priority(9.5f).On(Targets.CurrentEnemy),

            // --- AoE (>= AoeThreshold enemies within 10y) ---
            // HP floor: don't drop an 8-tick ground AoE on a pack already about to die (old AIO's HealthPercent > 25).
            Skill.Spell("Consecration").Priority(12f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(PaladinCommon.ConsecrationPackRadius) >= _settings.AoeThreshold.Value
                              && ctx.Target.HealthPercent > PaladinCommon.ConsecrationMinTargetHealth),
            PaladinCommon.HolyWrath(_settings, priority: 13f),
        }, ctx => _settings.UseRacials.Value, basePriority: 4f);
    }
}
