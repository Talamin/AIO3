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
    /// Solo Unholy Death Knight (melee, range ~5; leveling/grinding). The permanent-ghoul tree: composes the shared
    /// <see cref="DeathKnightCommon"/> baseline (rune gate, diseases, Presence + Horn of Winter, Mind Freeze /
    /// Death Grip, the ghoul — which Master of Ghouls keeps permanent here) and adds the Unholy core: Summon
    /// Gargoyle (boss CD), Scourge Strike as the main strike (needs both diseases; 1F+1U), Death and Decay AoE,
    /// the Blood-rune fillers, Death Strike as a self-heal below HP%, and Death Coil as the RP dump (~80). Thin:
    /// the step list is built ONCE in the ctor; unknown spells auto-skip so it scales by level. EVERY rune-costed
    /// step gates on CanAffordRunes via the DeathKnightCommon helpers.
    /// </summary>
    public sealed class SoloUnholy : IRotation
    {
        public string Name => "Death Knight - Solo Unholy";

        private readonly DeathKnightSettings _settings;
        private readonly IReadOnlyList<RotationStep> _steps;

        public SoloUnholy() : this(new DeathKnightSettings()) { }

        public SoloUnholy(DeathKnightSettings settings)
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

                // --- survival (shared) ---
                DeathKnightCommon.AntiMagicShell(s, priority: 0.25f),
                DeathKnightCommon.IceboundFortitude(s, priority: 0.3f),
            };

            // --- ghoul: Unholy's permanent pet (Master of Ghouls). Highest-value of the pet band. ---
            DeathKnightCommon.WithGhoul(s, core);

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

                // --- Summon Gargoyle: the Unholy burst cooldown on a boss/elite/pack (0-rune; runic power fuels it) ---
                Skill.Spell("Summon Gargoyle").Priority(2.0f).On(Targets.CurrentEnemy)
                     .When(ctx => s.UseCooldowns.Value && DeathKnightCommon.IsBigFight(ctx)),

                // --- self-heal: Death Strike below HP% (rune-gated, 1F+1U) ---
                DeathKnightCommon.Strike("Death Strike", priority: 2.5f,
                    ctx => s.UnholyDeathStrikeHealthPercent.Value > 0
                           && ctx.Me.HealthPercent < s.UnholyDeathStrikeHealthPercent.Value),

                // --- diseases ---
                DeathKnightCommon.IcyTouch(s, priority: 3.0f),
                DeathKnightCommon.PlagueStrike(s, priority: 3.1f),
                DeathKnightCommon.Pestilence(s, priority: 3.2f),

                // --- AoE by enemy count (rune-gated) ---
                DeathKnightCommon.DeathAndDecay(ctx => s.UnholyDeathAndDecayCount.Value, priority: 4.0f),
                DeathKnightCommon.BloodBoil(ctx => s.UnholyBloodBoilCount.Value, priority: 4.5f),

                // --- main strike: Scourge Strike needs BOTH diseases up (1F+1U, rune-gated) ---
                DeathKnightCommon.Strike("Scourge Strike", priority: 5.0f,
                    ctx => ctx.Target.HasMyAura("Frost Fever") && ctx.Target.HasMyAura("Blood Plague")),

                // --- Blood-rune fillers by enemy count (rune-gated) ---
                DeathKnightCommon.HeartStrike(ctx => s.UnholyHeartStrikeCount.Value, priority: 6.0f),
                DeathKnightCommon.BloodStrike(ctx => s.UnholyBloodStrikeCount.Value, priority: 6.5f),

                // --- RP dump: Death Coil at >= the configured RP (0-rune; ~80 so it'd rather feed the gargoyle) ---
                DeathKnightCommon.DeathCoil(ctx => s.UnholyDeathCoilRunicPower.Value, priority: 7.0f),

                // --- last-resort filler: Blood Strike with no count gate (rune-gated) ---
                DeathKnightCommon.Strike("Blood Strike", priority: 8.0f),
            });

            return Racials.With(core, ctx => s.UseRacials.Value, basePriority: 9.0f);
        }
    }
}
