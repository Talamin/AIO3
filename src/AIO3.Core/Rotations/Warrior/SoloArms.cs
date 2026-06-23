using System.Collections.Generic;
using AIO3.Core.Data;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Library;
using AIO3.Core.Settings;

namespace AIO3.Core.Rotations.Warrior
{
    /// <summary>
    /// Solo Arms warrior (leveling/grinding, 10-80). Battle-stance burst: Mortal Strike is the
    /// signature strike, Overpower fires in its proc window, Rend keeps the bleed up and Slam fills
    /// the gaps; Sweeping Strikes + Whirlwind / Bladestorm cleave a pack. Thin: composes the shared
    /// <see cref="WarriorCommon"/> / Layer 3 blocks and adds only the Arms-specific filler in priority
    /// order. Ported from the old Combat/Warrior/SoloArms.cs (boss/spread-Rend specifics deferred).
    ///
    /// Unknown/unusable spells are skipped automatically (IsSpellKnown), so this single priority list
    /// fills in as the player levels — list high-level abilities freely; they simply don't fire yet.
    /// </summary>
    public sealed class SoloArms : IRotation
    {
        public string Name => "Warrior - Solo Arms";

        private readonly WarriorSettings _settings;

        public SoloArms() : this(new WarriorSettings()) { }

        public SoloArms(WarriorSettings settings) => _settings = settings;

        public IReadOnlyList<Setting> Settings => _settings.All;

        public IReadOnlyList<RotationStep> BuildSteps() => Racials.With(new List<RotationStep>
        {
            // --- survival + burst (racials are appended by the shared Racials bundle at the 4.2 band) ---
            CombatBlocks.UseItems("Emergency heal", Consumables.HealthItems,
                ctx => _settings.EmergencyHealthPercent.Value > 0 && ctx.Me.HealthPercent < _settings.EmergencyHealthPercent.Value,
                priority: 0.05f),
            WarriorCommon.Recklessness(_settings, priority: 4.3f),

            // --- baseline / upkeep ---
            // Charge opener with a stance dance (Arms' home is already Battle Stance, so it charges
            // directly; only active out of combat at range while gap-closers are enabled).
            WarriorCommon.ChargeWithStanceDance(_settings, priority: 0.08f),
            // Tank-only fallback (fires only in Defensive Stance, so inert for Arms/Battle).
            WarriorCommon.TauntPull(_settings, priority: 0.09f),
            WarriorCommon.EnsureStance("Battle Stance", priority: 0.1f),
            CombatBlocks.AutoAttack(priority: 1f),
            CombatBlocks.Interrupt("Pummel", priority: 2f, mode: ctx => _settings.InterruptMode.Value),
            CombatBlocks.SelfBuff("Battle Shout", priority: 3f, supersededBy: "Greater Blessing of Might"),
            WarriorCommon.BerserkerRage(priority: 4f),
            CombatBlocks.DefensiveBelow("Enraged Regeneration", healthPercent: 50, priority: 4.5f),
            WarriorCommon.Bloodrage(priority: 5f),

            // --- survival debuff: enemy attack-power reduction on tougher fights / packs ---
            WarriorCommon.DemoralizingShout(_settings, priority: 5.5f),

            // --- single target ---
            // Execute: cheap sub-20% finisher (HP gate avoids wasted attempts).
            WarriorCommon.Execute(priority: 6f),
            // Mortal Strike: the Arms core strike, on cooldown.
            Skill.Spell("Mortal Strike").Priority(7f).On(Targets.CurrentEnemy),
            // Overpower: usable only in its proc window (after a dodge / Taste for Blood). Attempted
            // off-cooldown and fails through harmlessly when the proc isn't up — IsSpellReady checks
            // known + off-cooldown only, not usability, so no Lua/usable gate is needed here.
            Skill.Spell("Overpower").Priority(8f).On(Targets.CurrentEnemy),
            WarriorCommon.Rend(priority: 9f),
            WarriorCommon.VictoryRush(priority: 10f),
            // Slam filler when we have spare rage (cast-while-moving is blocked by the adapter).
            Skill.Spell("Slam").Priority(11f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.Me.Rage > 20),

            // --- gap-closers + utility (Charge opener handled high up via the stance dance) ---
            WarriorCommon.Intercept(_settings, priority: 12f),
            WarriorCommon.Hamstring(_settings, priority: 13.5f),

            // --- AoE (>= AoeThreshold enemies within 10y) ---
            // Sweeping Strikes (off the GCD) so the next strikes/Whirlwind cleave a second target.
            Skill.Spell("Sweeping Strikes").Priority(14f).On(Targets.Self)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value).OffGcd(),
            Skill.Spell("Bladestorm").Priority(15f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            // Whirlwind: Arms' instant cleave, paired with Sweeping Strikes for an extra target.
            Skill.Spell("Whirlwind").Priority(15.5f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            Skill.Spell("Thunder Clap").Priority(16f).On(Targets.CurrentEnemy)
                 .When(ctx => ctx.EnemiesWithin(10f) >= _settings.AoeThreshold.Value),
            WarriorCommon.Cleave(_settings, priority: 16.5f),

            // --- rage dump ---
            WarriorCommon.HeroicStrike(_settings, priority: 17f),
        }, ctx => _settings.UseRacials.Value, basePriority: 4.2f);
    }
}
