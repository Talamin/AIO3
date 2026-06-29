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
    /// Solo Frost Death Knight (melee, range ~5; leveling/grinding). Composes the shared <see cref="DeathKnightCommon"/>
    /// baseline (rune gate, diseases, Presence + Horn of Winter, Mind Freeze / Death Grip, the ghoul) and adds the
    /// Frost core: Howling Blast on the Rime/Freezing Fog or Killing Machine proc, Obliterate as the main strike
    /// (needs both diseases up; 1F+1U), Frost Strike as the RP dump (0-rune), then the Blood-rune fillers + the AoE.
    /// Thin: the step list is built ONCE in the ctor; unknown spells auto-skip so it scales by level. EVERY
    /// rune-costed step gates on CanAffordRunes via the DeathKnightCommon helpers.
    /// </summary>
    public sealed class SoloFrost : IRotation
    {
        public string Name => "Death Knight - Solo Frost";

        private readonly DeathKnightSettings _settings;
        private readonly IReadOnlyList<RotationStep> _steps;

        public SoloFrost() : this(new DeathKnightSettings()) { }

        public SoloFrost(DeathKnightSettings settings)
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

            // --- ghoul (optional temp pet for Frost) ---
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

                // --- proc: Howling Blast on Rime/Freezing Fog (free) or Killing Machine. Rune-gated (1 Frost) so a
                //     non-free proc cast still can't fire unaffordable; the proc just makes it the priority. ---
                DeathKnightCommon.Strike("Howling Blast", priority: 2.0f,
                    ctx => ctx.Me.HasAura("Freezing Fog") || ctx.Me.HasAura("Rime") || ctx.Me.HasAura("Killing Machine")),

                // --- diseases ---
                DeathKnightCommon.IcyTouch(s, priority: 3.0f),
                DeathKnightCommon.PlagueStrike(s, priority: 3.1f),
                DeathKnightCommon.Pestilence(s, priority: 3.2f),

                // --- AoE by enemy count (rune-gated) ---
                DeathKnightCommon.DeathAndDecay(ctx => s.FrostDeathAndDecayCount.Value, priority: 4.0f),
                DeathKnightCommon.BloodBoil(ctx => s.FrostBloodBoilCount.Value, priority: 4.5f),

                // --- main strike: Obliterate needs BOTH diseases up (1F+1U, rune-gated) ---
                DeathKnightCommon.Strike("Obliterate", priority: 5.0f,
                    ctx => ctx.Target.HasMyAura("Frost Fever") && ctx.Target.HasMyAura("Blood Plague")),

                // --- RP dump: Frost Strike at >= the configured RP (0-rune) ---
                DeathKnightCommon.FrostStrike(s, priority: 5.5f),

                // --- Blood-rune fillers by enemy count (rune-gated) ---
                DeathKnightCommon.HeartStrike(ctx => s.FrostHeartStrikeCount.Value, priority: 6.0f),
                DeathKnightCommon.BloodStrike(ctx => s.FrostBloodStrikeCount.Value, priority: 6.5f),

                // --- heal-filler: Death Strike (rune-gated, 1F+1U) ---
                DeathKnightCommon.Strike("Death Strike", priority: 7.0f),

                // --- last-resort filler: Blood Strike with no count gate (rune-gated) ---
                DeathKnightCommon.Strike("Blood Strike", priority: 8.0f),
            });

            return Racials.With(core, ctx => s.UseRacials.Value, basePriority: 9.0f);
        }
    }
}
