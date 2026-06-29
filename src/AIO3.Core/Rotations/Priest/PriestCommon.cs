using System;
using AIO3.Core.Combat;
using AIO3.Core.Dsl;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Library;

namespace AIO3.Core.Rotations.Priest
{
    /// <summary>
    /// Shared priest building blocks — the caster baseline every priest spec composes: the self-buffs (Inner Fire
    /// in form, the OOC Fortitude / Shadow Protection / Divine Spirit), the survival kit (Power Word: Shield,
    /// Dispersion, Psychic Scream, and the in-combat HARD heal that DROPS Shadowform first), the mana tools
    /// (Shadowfiend, the wand), and the Shadow DoT/nuke core (Vampiric Touch / Devouring Plague / Shadow Word:
    /// Pain / Mind Blast / Mind Sear / Mind Flay, plus the pre-form Smite filler). Spell choices that depend on
    /// what is learned (the hard heal: Greater Heal &gt; Heal &gt; Lesser Heal) resolve at EVAL TIME, like the
    /// paladin's Auto seal, so they fill in as the player levels.
    ///
    /// THE SHADOWFORM HEAL NUANCE (3.3.5a): a priest in Shadowform CANNOT cast Holy spells (Heal / Flash Heal /
    /// Greater Heal / Renew) — the cast is blocked, not auto-dropping the form like a druid's heal does. So the
    /// in-combat hard heal works in two beats, like the druid's shift-out heal: <see cref="DropShadowformToHeal"/>
    /// cancels Shadowform (casting "Shadowform" is a toggle) when a heal is wanted and we're in form, then the
    /// heal steps (<see cref="BestHeal"/> / <see cref="FlashHeal"/> / <see cref="Renew"/>) — all gated on
    /// <c>!InShadowform</c> — fire the next tick; the <see cref="ShadowformUpkeep"/> step re-enters form once we're
    /// healed. In-form survival stays available throughout: Power Word: Shield, Dispersion, Vampiric Embrace.
    ///
    /// Cast-time / channelled spells (the heals, Mind Sear, Mind Flay, Smite) gate on <c>!PlayerIsMoving</c> where
    /// it matters; instants (the DoTs, Mind Blast, Shadowform, Inner Fire) do not. The wand is off the GCD.
    /// </summary>
    public static class PriestCommon
    {
        /// <summary>"Surrounded" for the Psychic Scream panic button: this many enemies in melee on us.</summary>
        public const int ScreamPackSize = 2;

        /// <summary>Melee radius for the Psychic Scream surround check (an AoE fear is only worth it if mobs are
        /// actually on us). Mirrors the old SoloShadow 6yd gate.</summary>
        public const float ScreamMeleeRange = 6f;

        /// <summary>Mind Sear's AoE radius around the TARGET — it's a channelled cone/PBAoE anchored on the focus.
        /// Mirrors the old SoloShadow 11yd enemy-cluster check.</summary>
        public const float MindSearRadius = 11f;

        /// <summary>Mind Sear needs at least this much mana to be worth channelling (it's expensive). Old FC: 65.</summary>
        public const int MindSearMinManaPercent = 65;

        /// <summary>Below this mana% the wand fires regardless of target HP — out of mana, conserve by wanding.</summary>
        public const int WandManaFloorPercent = 5;

        /// <summary>Shadow Word: Pain's "refresh at 5 Shadow Weaving stacks" gate only matters when the Shadow
        /// Weaving talent is taken. (3, 12) is the old FC's tab/index for Shadow Weaving — VERIFY IN GAME (the
        /// 1-based GetTalentInfo order can drift between client builds). When NOT talented we refresh SW:Pain at
        /// any time; when talented we hold until the stacks are built so the DoT snapshots full Shadow Weaving.</summary>
        public const int ShadowWeavingTalentTab = 3;
        public const int ShadowWeavingTalentIndex = 12;

        /// <summary>Shadow Weaving caps at 5 stacks; we wait for the full stack before snapshotting SW:Pain.</summary>
        public const int ShadowWeavingMaxStacks = 5;

        // DoT refresh windows (re-apply when fewer than this many ms remain) — ported from the old SoloShadow.
        public const int VampiricTouchRefreshMs = 1300; // VT lasts ~15s; refreshed late (it's the priority DoT)
        public const int DevouringPlagueRefreshMs = 2590; // DP lasts ~24s
        public const int ShadowWordPainRefreshMs = 2800;  // SW:Pain lasts ~18-24s

        /// <summary>Devouring Plague's dying-mob HP floor on a NORMAL target: don't re-apply a fresh self-heal DoT
        /// below this — it won't tick out before the mob dies. Old FC: HealthPercent &gt; 40.</summary>
        public const int DevouringPlagueNormalFloor = 40;

        /// <summary>Devouring Plague's HP floor on a BOSS — a boss lives long enough that the DoT pays off far
        /// lower. Old FC: boss &amp;&amp; HealthPercent &gt; 15.</summary>
        public const int DevouringPlagueBossFloor = 15;

        // --- form / state facts ---

        /// <summary>True while in Shadowform (the Shadow DPS form). A hard heal must drop it first (Holy spells are
        /// locked out in form), so the heal gates read this.</summary>
        public static bool InShadowform(CombatContext ctx) => ctx.Me.HasAura("Shadowform");

        /// <summary>True when an in-combat hard heal (Flash Heal / Greater Heal / …) is WANTED this tick: HP is
        /// below the configured threshold AND we have the mana to afford it. Drives both the form-drop and the heal
        /// steps so they agree. The mana floor stops a low-mana priest from thrashing out of form for a heal it
        /// can't pay for. (Renew has its own, separate gate — it's a cheap HoT.)</summary>
        public static bool WantsHardHeal(CombatContext ctx, PriestSettings s)
        {
            if (ctx.Me.PowerPercent <= s.HealManaPercent.Value) return false;
            int flash = s.FlashHealHealthPercent.Value;
            int heal = s.HealHealthPercent.Value;
            return (flash > 0 && ctx.Me.HealthPercent < flash) || (heal > 0 && ctx.Me.HealthPercent < heal);
        }

        // --- buffs ---

        /// <summary>Keep Inner Fire up (a spell-power / armor self-buff). Castable in Shadowform, so no form gate;
        /// opt-out via the toggle.</summary>
        public static RotationStep InnerFire(PriestSettings s, float priority) =>
            Skill.Spell("Inner Fire").Priority(priority).On(Targets.Self)
                 .When(ctx => s.InnerFire.Value && !ctx.Me.HasAura("Inner Fire") && !ctx.Game.PlayerIsMounted);

        /// <summary>An out-of-combat self-buff (Power Word: Fortitude / Shadow Protection / Divine Spirit), kept up
        /// when missing and not mounted. Mirrors the old OOCBuffs addon (RunInCombat=false): long buffs applied
        /// before the pull, not re-cast mid-fight. <paramref name="enabled"/> is the per-buff toggle;
        /// <paramref name="supersededBy"/> lists the raid-wide Prayer that makes the single-target version
        /// unnecessary (e.g. Prayer of Fortitude over Power Word: Fortitude).</summary>
        public static RotationStep OocBuff(string spell, Func<PriestSettings, bool> enabled, PriestSettings s,
            float priority, params string[] supersededBy) =>
            new RotationStep(
                name: spell,
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (!enabled(s) || ctx.Game.PlayerInCombat || ctx.Game.PlayerIsMounted) return false;
                    if (ctx.Me.HasAura(spell)) return false;
                    if (supersededBy != null)
                        foreach (string b in supersededBy)
                            if (ctx.Me.HasAura(b)) return false;
                    return true;
                },
                action: (ctx, t) => ctx.Game.Cast(spell, ctx.Me));

        /// <summary>Maintain Shadowform — the Shadow DPS form. Re-enters when it's down (whether we never had it, or
        /// we dropped it for a heal). Gated on the toggle and on NOT currently wanting a heal, so it doesn't fight
        /// the heal step over the GCD the tick we just dropped form to heal (the heal fires first; once healed, this
        /// re-enters next). Not while mounted. Instant. Auto-skips until learned (a pre-Shadowform priest is a Smite
        /// caster).</summary>
        public static RotationStep ShadowformUpkeep(PriestSettings s, float priority) =>
            Skill.Spell("Shadowform").Priority(priority).On(Targets.Self)
                 .When(ctx => s.Shadowform.Value && !InShadowform(ctx)
                              && !ctx.Game.PlayerIsMounted
                              && !WantsHardHeal(ctx, s));

        /// <summary>Maintain Vampiric Embrace — the self-heal-from-shadow-damage buff (castable in form, so no form
        /// gate). Re-cast when missing; opt-out via the toggle. Auto-skips until learned.</summary>
        public static RotationStep VampiricEmbrace(PriestSettings s, float priority) =>
            Skill.Spell("Vampiric Embrace").Priority(priority).On(Targets.Self)
                 .When(ctx => s.VampiricEmbrace.Value && !ctx.Me.HasAura("Vampiric Embrace"));

        // --- survival (castable in Shadowform) ---

        /// <summary>Power Word: Shield below the configured HP% — a damage-absorb that is CASTABLE IN SHADOWFORM
        /// (it's a Discipline spell, not Holy). Won't re-cast while the shield itself or Weakened Soul (its cooldown
        /// debuff) is up. Self-cast.</summary>
        public static RotationStep PowerWordShield(PriestSettings s, float priority) =>
            Skill.Spell("Power Word: Shield").Priority(priority).On(Targets.Self)
                 .When(ctx => s.ShieldHealthPercent.Value > 0
                              && ctx.Me.HealthPercent <= s.ShieldHealthPercent.Value
                              && !ctx.Me.HasAura("Power Word: Shield")
                              && !ctx.Me.HasAura("Weakened Soul"));

        /// <summary>Dispersion — the Shadow capstone emergency button: 90% damage reduction + restores mana over
        /// its channel. Fire below the configured mana % when it isn't already up. CASTABLE IN SHADOWFORM. Off the
        /// GCD. Auto-skips until the talent is learned (so a non-Shadow / low-level priest never sees it). 0
        /// disables it.</summary>
        public static RotationStep Dispersion(PriestSettings s, float priority) =>
            Skill.Spell("Dispersion").Priority(priority).On(Targets.Self)
                 .When(ctx => s.DispersionManaPercent.Value > 0
                              && ctx.Me.PowerPercent <= s.DispersionManaPercent.Value
                              && !ctx.Me.HasAura("Dispersion"))
                 .OffGcd();

        /// <summary>Psychic Scream — a panic AoE fear when SURROUNDED (>= <see cref="ScreamPackSize"/> enemies in
        /// melee on us) and below the configured HP%, SOLO. Buys a brief heal/escape window. Self-cast PBAoE.
        /// Mirrors the old SoloShadow gate (solo, HP &lt; 80, 2+ within 6yd).</summary>
        public static RotationStep PsychicScream(PriestSettings s, float priority) =>
            Skill.Spell("Psychic Scream").Priority(priority).On(Targets.Self)
                 .When(ctx => s.PsychicScreamHealthPercent.Value > 0 && !ctx.IsInGroup
                              && ctx.Me.HealthPercent < s.PsychicScreamHealthPercent.Value
                              && MeleeingMe(ctx) >= ScreamPackSize);

        // --- the in-combat hard heal (drop Shadowform → heal → re-enter form) ---

        /// <summary>Drop Shadowform so a Holy heal can be cast. In 3.3.5a a priest can't cast Heal / Flash Heal /
        /// Greater Heal / Renew while in Shadowform, so when a hard heal is wanted (<see cref="WantsHardHeal"/>) AND
        /// we're in form, this cancels the form by casting "Shadowform" (a toggle). The next tick — now out of form
        /// — the heal step fires, then <see cref="ShadowformUpkeep"/> re-enters form once we're topped off. Sits at
        /// the SAME urgency as the heals (just above them) so it leads them while in form. Mirrors the Druid's
        /// in-combat shift-out-to-heal, adapted for the priest's hard form lockout (the form does NOT auto-drop on
        /// the heal cast here, so we issue the cancel explicitly).</summary>
        public static RotationStep DropShadowformToHeal(PriestSettings s, float priority) =>
            Skill.Spell("Shadowform").Priority(priority).On(Targets.Self)
                 .When(ctx => InShadowform(ctx) && WantsHardHeal(ctx, s));

        /// <summary>The best hard heal the priest has learned, resolved AT EVAL TIME (Greater Heal &gt; Heal &gt;
        /// Lesser Heal), like the paladin's Auto seal. Fires below the hard-heal threshold, only when OUT of
        /// Shadowform (the form blocks it — <see cref="DropShadowformToHeal"/> drops it first) and only while
        /// standing still (it's a cast). The shared mana floor is folded into <see cref="WantsHardHeal"/>.</summary>
        public static RotationStep BestHeal(PriestSettings s, float priority) =>
            new RotationStep(
                name: "Heal",
                priority: priority,
                targets: Targets.Self,
                condition: (ctx, t) =>
                {
                    if (s.HealHealthPercent.Value <= 0) return false;
                    if (InShadowform(ctx) || ctx.Game.PlayerIsMoving) return false;
                    if (ctx.Me.HealthPercent >= s.HealHealthPercent.Value) return false;
                    if (ctx.Me.PowerPercent <= s.HealManaPercent.Value) return false;
                    return ResolveHeal(ctx) != null;
                },
                action: (ctx, t) =>
                {
                    string heal = ResolveHeal(ctx);
                    return heal != null ? ctx.Game.Cast(heal, ctx.Me) : CastResult.Failed;
                });

        /// <summary>Flash Heal — the fast (mana-hungry) emergency heal. Below its threshold, OUT of form, standing
        /// still, mana above the floor. Sits ABOVE <see cref="BestHeal"/> so a sharp HP drop gets the quick cast.
        /// Auto-skips until learned.</summary>
        public static RotationStep FlashHeal(PriestSettings s, float priority) =>
            Skill.Spell("Flash Heal").Priority(priority).On(Targets.Self)
                 .When(ctx => s.FlashHealHealthPercent.Value > 0
                              && !InShadowform(ctx) && !ctx.Game.PlayerIsMoving
                              && ctx.Me.HealthPercent < s.FlashHealHealthPercent.Value
                              && ctx.Me.PowerPercent > s.HealManaPercent.Value);

        /// <summary>Renew — a cheap HoT to top off (Holy, so only out of form). Cast when missing below its
        /// threshold, mana above the floor. We do NOT drop form just for Renew (it's not urgent enough to justify
        /// the form thrash); it only applies when already out of form (e.g. just after a hard heal, or pre-form).
        /// Mirrors the old SoloShadow Renew (not in form, mana &gt; 40, refresh when missing).</summary>
        public static RotationStep Renew(PriestSettings s, float priority) =>
            Skill.Spell("Renew").Priority(priority).On(Targets.Self)
                 .When(ctx => s.RenewHealthPercent.Value > 0
                              && !InShadowform(ctx)
                              && ctx.Me.HealthPercent < s.RenewHealthPercent.Value
                              && ctx.Me.PowerPercent > s.HealManaPercent.Value
                              && !ctx.Me.HasAura("Renew"));

        // --- mana ---

        /// <summary>Shadowfiend — cast ON the current enemy below the configured mana %. It's a cooldown (not a
        /// managed pet): the fiend auto-attacks the target and returns mana per hit. Auto-skips until learned. 0
        /// disables it.</summary>
        public static RotationStep Shadowfiend(PriestSettings s, float priority) =>
            Skill.Spell("Shadowfiend").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.ShadowfiendManaPercent.Value > 0
                              && ctx.Me.PowerPercent < s.ShadowfiendManaPercent.Value);

        /// <summary>Wand (Shoot) the target to conserve mana: fires when the target is at/below the wand HP% (finish
        /// it with the wand instead of spending mana) OR mana is nearly empty. Needs a wand equipped (auto-skips
        /// otherwise) and only when not already wanding. Off the GCD. Mirrors the old SoloShadow Shoot gate.</summary>
        public static RotationStep Wand(PriestSettings s, float priority) =>
            Skill.Spell("Shoot").Priority(priority).On(Targets.CurrentEnemy)
                 .When((ctx, t) => s.UseWand.Value
                                   && (t.HealthPercent <= s.WandTargetHealthPercent.Value || ctx.Me.PowerPercent < WandManaFloorPercent)
                                   && !ctx.Game.IsCurrentSpell("Shoot")).OffGcd();

        // --- Shadow DoT / nuke core (in form; each auto-skips until learned) ---

        /// <summary>Vampiric Touch — the priority shadow DoT (also fuels mana/replenishment). Maintain it,
        /// refreshing late (<see cref="VampiricTouchRefreshMs"/>) since it's the lead DoT. Floors on the target's
        /// HP so a fresh DoT isn't wasted on a dying mob.</summary>
        public static RotationStep VampiricTouch(PriestSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Vampiric Touch", VampiricTouchRefreshMs, priority,
                extraGate: ctx => ctx.Target.HealthPercent > DyingFloor(ctx, DevouringPlagueNormalFloor, DevouringPlagueBossFloor));

        /// <summary>Devouring Plague — a self-heal shadow DoT. Maintain it on a durable target (its own boss-aware
        /// HP floor: normal &gt; 40, boss &gt; 15), gated on the toggle. Refresh when under
        /// <see cref="DevouringPlagueRefreshMs"/> remain.</summary>
        public static RotationStep DevouringPlague(PriestSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Devouring Plague", DevouringPlagueRefreshMs, priority,
                extraGate: ctx => s.UseDevouringPlague.Value
                                  && ctx.Target.HealthPercent > DyingFloor(ctx, DevouringPlagueNormalFloor, DevouringPlagueBossFloor));

        /// <summary>Shadow Word: Pain — the cheap, long shadow DoT. Maintained, but the refresh is GATED on Shadow
        /// Weaving: when the Shadow Weaving talent is taken (<see cref="ShadowWeavingTalentTab"/>,
        /// <see cref="ShadowWeavingTalentIndex"/>), only (re)apply at 5 Shadow Weaving stacks so the DoT snapshots
        /// the full +shadow-damage debuff; when NOT talented, refresh any time. Floors on the target's HP so a
        /// fresh DoT isn't wasted on a dying mob. (The talent index is the old FC's value — VERIFY IN GAME.)</summary>
        public static RotationStep ShadowWordPain(PriestSettings s, float priority) =>
            CombatBlocks.MaintainMyDebuff("Shadow Word: Pain", ShadowWordPainRefreshMs, priority,
                extraGate: ctx => (!ctx.Game.HasTalent(ShadowWeavingTalentTab, ShadowWeavingTalentIndex)
                                   || ctx.Me.AuraStacks("Shadow Weaving") >= ShadowWeavingMaxStacks)
                                  && ctx.Target.HealthPercent > DyingFloor(ctx, DevouringPlagueNormalFloor, DevouringPlagueBossFloor));

        /// <summary>Mind Blast — the instant-cooldown shadow nuke. Cast on cooldown; the auto known/ready/GCD gate
        /// handles the rest.</summary>
        public static RotationStep MindBlast(float priority) =>
            Skill.Spell("Mind Blast").Priority(priority).On(Targets.CurrentEnemy);

        /// <summary>Mind Sear — the channelled AoE: fire when >= <see cref="ScreamPackSize"/> enemies cluster within
        /// <see cref="MindSearRadius"/> of the TARGET (target-anchored, like the hunter's Volley), mana is above
        /// <see cref="MindSearMinManaPercent"/>, and we're standing still (it's a channel). Opt-out via the toggle;
        /// auto-skips until learned.</summary>
        public static RotationStep MindSear(PriestSettings s, float priority) =>
            Skill.Spell("Mind Sear").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => s.UseMindSear.Value
                              && ctx.EnemiesNearTarget(MindSearRadius) >= ScreamPackSize
                              && ctx.Me.PowerPercent > MindSearMinManaPercent
                              && !ctx.Game.PlayerIsMoving);

        /// <summary>Mind Flay — the channelled single-target filler. Fire while SW:Pain or Devouring Plague is up
        /// (so it pairs with a DoT, like the old FC), standing still. Opt-out via the toggle (off by default);
        /// auto-skips until learned.</summary>
        public static RotationStep MindFlay(PriestSettings s, float priority) =>
            Skill.Spell("Mind Flay").Priority(priority).On(Targets.CurrentEnemy)
                 .When((ctx, t) => s.UseMindFlay.Value && !ctx.Game.PlayerIsMoving
                                   && (t.HasMyAura("Shadow Word: Pain") || t.HasMyAura("Devouring Plague")));

        /// <summary>Smite — the non-shadow nuke that carries the low levels until Shadowform is learned (a priest
        /// can't cast it in form anyway, so it naturally goes quiet once Shadowform is up and locks out Holy). The
        /// pre-Shadowform filler. Cast-time → stand still. Auto-skips when unknown.</summary>
        public static RotationStep Smite(float priority) =>
            Skill.Spell("Smite").Priority(priority).On(Targets.CurrentEnemy)
                 .When(ctx => !ctx.Game.PlayerIsMoving && !InShadowform(ctx));

        // --- helpers ---

        /// <summary>The dying-mob HP floor for the target: the boss floor on a boss, else the normal floor. One
        /// place so every DoT's floor agrees (and drops lower on a boss, which lives long enough for the DoT to
        /// pay off).</summary>
        private static int DyingFloor(CombatContext ctx, int normalFloor, int bossFloor) =>
            ctx.HasEnemyTarget && ctx.Target.IsBoss() ? bossFloor : normalFloor;

        /// <summary>The best hard heal known right now (Greater Heal &gt; Heal &gt; Lesser Heal), resolved at eval
        /// time so it fills in as the priest levels — like the paladin's Auto seal pick. Null if none are known
        /// (so the heal step skips cleanly).</summary>
        private static string ResolveHeal(CombatContext ctx)
        {
            if (ctx.Game.IsSpellKnown("Greater Heal")) return "Greater Heal";
            if (ctx.Game.IsSpellKnown("Heal")) return "Heal";
            if (ctx.Game.IsSpellKnown("Lesser Heal")) return "Lesser Heal";
            return null;
        }

        /// <summary>Count of enemies meleeing the player within <see cref="ScreamMeleeRange"/> — the surround count
        /// that arms Psychic Scream.</summary>
        private static int MeleeingMe(CombatContext ctx)
        {
            int n = 0;
            foreach (IWowUnit e in ctx.Enemies)
                if (e.IsAlive && e.IsTargetingMe && e.Distance <= ScreamMeleeRange) n++;
            return n;
        }
    }
}
