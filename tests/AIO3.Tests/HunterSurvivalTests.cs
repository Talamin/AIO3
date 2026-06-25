using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Hunter;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class HunterSurvivalTests
    {
        // Survival hunter, steady state at range: pet on the target, aspect up, Auto Shot running,
        // Hunter's Mark + Serpent Sting already applied so the rotation reaches the signature shots.
        private static FakeGameClient Game()
        {
            var g = new FakeGameClient { Class = WowClass.Hunter };
            g.TargetUnit = new FakeUnit
            {
                Guid = 1, Name = "Dummy", Reaction = Reaction.Hostile, Distance = 28, HealthPercent = 100, IsAttackable = true
            };
            g.EnemyList.Add(g.TargetUnit);
            g.MeUnit.PowerPercent = 100;
            g.MeUnit.WithAura("Aspect of the Dragonhawk");
            g.MeUnit.WithAura("Trueshot Aura"); // shared AP buff up → the upkeep step stays idle
            g.CurrentSpells.Add("Auto Shot");
            g.PetUnit = new FakeUnit { Guid = 99, Name = "Pet", IsAlive = true, HealthPercent = 100, TargetGuid = 1, Distance = 5 };
            g.TargetUnit.WithAura("Hunter's Mark", mine: true);
            // Fresh duration so the maintain (now via MaintainMyDebuff with a 1500ms refresh window) stays quiet.
            g.TargetUnit.WithAura("Serpent Sting", mine: true, timeLeftMs: 15000);
            return g;
        }

        private static RotationStep Fire(FakeGameClient g) => Fire(g, new HunterSettings());

        private static RotationStep Fire(FakeGameClient g, HunterSettings s) =>
            new RotationEngine(new SoloSurvival(s).BuildSteps()).Tick(CombatContext.Capture(g));

        [Fact]
        public void Kill_Command_fires_with_a_pet()
        {
            Assert.Equal("Kill Command", Fire(Game())?.Name);
        }

        [Fact]
        public void Black_Arrow_leads_the_Lock_and_Load_engine()
        {
            // After the audit reorder, Black Arrow (which procs Lock and Load) is ranked just above Explosive
            // Shot, mirroring the old SV. With Kill Command down it's the next shot.
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Kill Command");
            Assert.Equal("Black Arrow", Fire(g)?.Name);
        }

        [Fact]
        public void Explosive_Shot_after_black_arrow()
        {
            FakeGameClient g = Game();
            g.SpellsOnCooldown.Add("Kill Command");
            g.SpellsOnCooldown.Add("Black Arrow");
            Assert.Equal("Explosive Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Lock_and_Load_proc_spams_Explosive_Shot_at_top_priority()
        {
            // The LnL window: Black Arrow / trap crits reset Explosive Shot. While the aura is up Explosive Shot
            // jumps to TOP filler priority — ahead of Kill Command (the normal lead filler) and Black Arrow.
            FakeGameClient g = Game();
            g.MeUnit.WithAura("Lock and Load", stacks: 2);
            Assert.Equal("Explosive Shot", Fire(g)?.Name);
        }

        [Fact]
        public void Without_Lock_and_Load_Kill_Command_still_leads()
        {
            // Sanity: the high-priority LnL Explosive step only fires during the proc. With no LnL aura the
            // normal lead filler (Kill Command) wins, not Explosive Shot.
            Assert.Equal("Kill Command", Fire(Game())?.Name);
        }

        [Fact]
        public void Trueshot_Aura_is_kept_up_on_survival()
        {
            // S3: Trueshot Aura is a shared AP buff now applied on every spec (not just MM). A SV hunter that
            // learned it keeps it up.
            FakeGameClient g = Game();
            g.MeUnit.Auras.Remove("Trueshot Aura"); // it dropped → SV should re-apply it
            Assert.False(g.MeUnit.HasAura("Trueshot Aura"));
            Assert.Equal("Trueshot Aura", Fire(g)?.Name);
            Assert.Contains("Trueshot Aura", g.CastLog);
        }

        [Fact]
        public void Volley_fires_on_a_pack_and_holds_while_moving()
        {
            // BM1/AoE: Volley is a shared channelled AoE wired into every spec; it gates on the same
            // player-relative pack gate as Multi-Shot and on standing still.
            FakeGameClient g = Game();
            for (ulong i = 2; i <= 4; i++) // three extra mobs in 10yd → pack of 4 (>= default AoE threshold 3)
                g.EnemyList.Add(new FakeUnit { Guid = i, Distance = 6, IsAttackable = true, Reaction = Reaction.Hostile });
            g.SpellsOnCooldown.Add("Kill Command"); // clear the lead filler so we reach the AoE band
            var s = new HunterSettings();
            s.UseCooldowns.Value = false; // Rapid Fire fires on a pack; isolate the AoE shot

            Assert.Equal("Volley", Fire(g, s)?.Name);

            g.Moving = true; // channelled → can't start on the move
            Assert.NotEqual("Volley", Fire(g, s)?.Name);
        }
    }
}
