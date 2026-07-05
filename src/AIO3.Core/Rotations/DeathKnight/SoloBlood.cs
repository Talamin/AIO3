using System.Collections.Generic;
using AIO3.Core.Combat;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.DeathKnight
{
    /// <summary>
    /// Solo Blood Death Knight (melee, range ~5; leveling/grinding). The survival-heavy tree: composes the shared
    /// <see cref="DeathKnightCommon"/> baseline (the rune-affordability gate, disease upkeep, Presence + Horn of
    /// Winter, Mind Freeze / Death Grip) and adds the Blood survival cooldowns + strike priority. No ghoul band —
    /// Raise Dead's ghoul is permanent only for Unholy (Master of Ghouls); a 60s temp minion isn't worth it here. Thin:
    /// the step list is built ONCE in the ctor; unknown spells auto-skip so the same list scales by level (a low
    /// DK without Heart Strike just uses Blood Strike). EVERY rune-costed step gates on CanAffordRunes via the
    /// DeathKnightCommon helpers, so a rune-starved step never jams the rotation.
    /// </summary>
    public sealed class SoloBlood : IRotation
    {
        public string Name => "Death Knight - Solo Blood";

        private readonly DeathKnightSettings _settings;
        private readonly IReadOnlyList<RotationStep> _steps;

        public SoloBlood() : this(new DeathKnightSettings()) { }

        public SoloBlood(DeathKnightSettings settings)
        {
            _settings = settings;
            _steps = BuildList();
        }

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => _steps;

        private IReadOnlyList<RotationStep> BuildList()
        {
            DeathKnightSettings s = _settings;

            var core = new List<RotationStep>
            {
                // --- emergency item ---
                CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                    ctx => s.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < s.EmergencyHealthPercent.Value,
                    priority: 0.05f),

                // --- survival (Blood is the survival tree) ---
                // Vampiric Blood: the big HP/heal cooldown when low (0-rune).
                Skill.Spell("Vampiric Blood").Priority(0.2f).On(Targets.Self)
                     .When(ctx => s.VampiricBloodPercent.Value > 0 && ctx.Me.HealthPercent <= s.VampiricBloodPercent.Value),
                // Anti-Magic Shell vs an enemy casting at me (0-rune, off-GCD).
                DeathKnightCommon.AntiMagicShell(s, priority: 0.25f),
                // Icebound Fortitude: damage-reduction below HP% with a pack on me (0-rune, off-GCD).
                DeathKnightCommon.IceboundFortitude(s, priority: 0.3f),
                // Rune Tap: a Blood-rune self-heal at/below the configured HP% (rune-gated, 1 Blood).
                DeathKnightCommon.Strike2Self("Rune Tap", priority: 0.35f,
                    ctx => s.RuneTapPercent.Value > 0 && ctx.Me.HealthPercent <= s.RuneTapPercent.Value),
            };

            // No ghoul band: Raise Dead's ghoul is permanent only for Unholy (Master of Ghouls). See SoloUnholy.

            core.AddRange(new[]
            {
                // --- presence + Horn of Winter upkeep ---
                DeathKnightCommon.Presence(s, priority: 0.95f),
                DeathKnightCommon.HornOfWinter(s, priority: 0.96f),

                // --- pull / interrupt ---
                DeathKnightCommon.DeathGripPull(s, priority: 1.0f),
                DeathKnightCommon.MindFreeze(s, priority: 1.1f),
                DeathKnightCommon.DeathGripInterrupt(s, priority: 1.2f),

                // --- rune-economy cooldown ---
                DeathKnightCommon.EmpowerRuneWeapon(s, priority: 1.5f),

                // --- elite/boss cooldowns ---
                // Mark of Blood: a healing debuff on a boss/elite (rune-gated, 1 Blood).
                DeathKnightCommon.Strike("Mark of Blood", priority: 2.0f,
                    ctx => s.UseCooldowns.Value && ctx.HasEnemyTarget && (ctx.Target.IsBoss() || ctx.Target.IsElite)),
                // Dancing Rune Weapon: the Blood burst cooldown on a boss/elite/pack (0-rune).
                Skill.Spell("Dancing Rune Weapon").Priority(2.1f).On(Targets.CurrentEnemy)
                     .When(ctx => s.UseCooldowns.Value && DeathKnightCommon.IsBigFight(ctx)),

                // --- diseases ---
                DeathKnightCommon.IcyTouch(s, priority: 3.0f),
                DeathKnightCommon.PlagueStrike(s, priority: 3.1f),
                DeathKnightCommon.Pestilence(s, priority: 3.2f),

                // --- AoE / builders by enemy count (rune-gated) ---
                DeathKnightCommon.DeathAndDecay(ctx => s.BloodDeathAndDecayCount.Value, priority: 4.0f),
                DeathKnightCommon.BloodBoil(ctx => s.BloodBloodBoilCount.Value, priority: 4.5f),
                DeathKnightCommon.HeartStrike(ctx => s.BloodHeartStrikeCount.Value, priority: 5.0f),
                DeathKnightCommon.BloodStrike(ctx => s.BloodBloodStrikeCount.Value, priority: 5.5f),

                // --- heal-filler: Death Strike (rune-gated, 1F+1U) heals while it strikes ---
                DeathKnightCommon.Strike("Death Strike", priority: 6.0f),

                // --- RP dump: Death Coil at >= the configured RP (0-rune) ---
                DeathKnightCommon.DeathCoil(ctx => s.BloodDeathCoilRunicPower.Value, priority: 7.0f),

                // --- last-resort filler: Blood Strike with no count gate (rune-gated) ---
                DeathKnightCommon.Strike("Blood Strike", priority: 8.0f),
            });

            // Racials append at the ~9-band so survival/strikes win; off-GCD racials still fire alongside.
            return Racials.With(core, ctx => s.UseRacials.Value, basePriority: 9.0f);
        }
    }
}
