using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Paladin
{
    /// <summary>
    /// Solo Protection paladin (leveling/grinding, 10-80). Shield tank: keep Righteous Fury + Holy Shield +
    /// the seal/aura/blessing up, then build threat with Hammer of the Righteous / Shield of Righteousness,
    /// Judgement and Consecration. Survives via the shared self-heal / Divine Protection / Lay on Hands
    /// blocks. Avenger's Shield is used only in combat (the product owns the opener — we never pull).
    ///
    /// Thin: composes the shared <see cref="PaladinCommon"/> / Layer 3 blocks and adds only the
    /// Prot-specific filler in priority order. Unknown/unusable spells are skipped automatically, so this
    /// single list fills in as the player levels.
    /// </summary>
    public sealed class SoloProtection : IRotation
    {
        public string Name => "Paladin - Solo Protection";

        private readonly PaladinSettings _settings;

        public SoloProtection() : this(new PaladinSettings()) { }

        public SoloProtection(PaladinSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => Racials.With(new List<RotationStep>
        {
            // (racials are appended by the shared Racials bundle at the 4.0 band)
            // --- emergency survival ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            PaladinCommon.LayOnHands(_settings, priority: 0.1f),
            PaladinCommon.DivineProtection(_settings, priority: 0.2f),
            PaladinCommon.ArtOfWarFlash(_settings, priority: 0.4f),   // harmless if the proc/talent is absent
            PaladinCommon.HolyLightSelf(_settings, priority: 0.5f),
            PaladinCommon.DivinePlea(_settings, priority: 0.7f),

            // --- baseline / upkeep (also maintained out of combat) ---
            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Hammer of Justice", priority: 2f, mode: ctx => _settings.InterruptMode.Value),
            PaladinCommon.Seal(PaladinSpec.Protection, _settings, priority: 3f),
            PaladinCommon.Aura(PaladinSpec.Protection, _settings, priority: 3.1f),
            PaladinCommon.Blessing(PaladinSpec.Protection, _settings, priority: 3.2f),
            CombatBlocks.SelfBuff("Righteous Fury", priority: 3.5f),  // holy threat multiplier (toggle buff)
            // Holy Shield: short block/damage buff — only worth its mana when actually fighting.
            Skill.Spell("Holy Shield").Priority(3.6f).On(Targets.Self)
                 .When(ctx => ctx.HasEnemyTarget && !ctx.Me.HasAura("Holy Shield")),

            // --- major cooldown ---
            PaladinCommon.AvengingWrath(_settings, priority: 4.3f),

            // --- threat priority ---
            PaladinCommon.HammerOfWrath(priority: 6f),                 // ranged execute < 20%
            PaladinCommon.Judgement(_settings, priority: 7f),         // on cooldown: mana return + threat
            Skill.Spell("Shield of Righteousness").Priority(8f).On(Targets.CurrentEnemy),
            Skill.Spell("Hammer of the Righteous").Priority(9f).On(Targets.CurrentEnemy),
            // Avenger's Shield: ranged threat + silence. In combat only — never as an opener pull.
            Skill.Spell("Avenger's Shield").Priority(10f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Game.PlayerInCombat),

            // --- AoE / sustained threat (pack, elite or boss; gated to save mana on lone trash) ---
            Skill.Spell("Consecration").Priority(12f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(8f) >= _settings.AoeThreshold.Value
                              || ctx.Target.IsElite || ctx.Target.IsBoss()),
            PaladinCommon.HolyWrath(_settings, priority: 13f),
        }, ctx => _settings.UseRacials.Value, basePriority: 4f);
    }
}
