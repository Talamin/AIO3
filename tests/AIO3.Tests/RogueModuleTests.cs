using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class RogueModuleTests
    {
        private static ChoiceSetting Spec(RogueModule m) => (ChoiceSetting)m.Settings.First(s => s.Key == "spec");

        [Fact]
        public void Factory_maps_the_rogue_class()
        {
            IClassModule m = ClassModules.For(WowClass.Rogue);
            Assert.NotNull(m);
            Assert.Equal("Rogue", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new RogueModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Theory]
        [InlineData(2, "Combat")]    // Combat tab → Combat label
        [InlineData(0, "Combat")]    // no points → leveling default = Combat
        public void Auto_resolves_combat_from_talents(int tab, string expectedLabel)
        {
            var m = new RogueModule();
            m.ResolveRotation(tab);
            Assert.Contains(expectedLabel, m.ActiveLabel);
        }

        [Fact]
        public void Phase1_runs_the_combat_rotation_for_every_spec()
        {
            // Only the Combat rotation ships now; Assassination/Subtlety fall back to it (the talent build still
            // tracks the detected spec — see Talent_build_follows_the_active_spec).
            var m = new RogueModule();
            Assert.Contains("Combat", m.ResolveRotation(1).Name); // Assassination tab
            Assert.Contains("Combat", m.ResolveRotation(2).Name); // Combat tab
            Assert.Contains("Combat", m.ResolveRotation(3).Name); // Subtlety tab
        }

        [Fact]
        public void Assassination_label_shows_the_fallback_to_combat()
        {
            var m = new RogueModule();
            m.ResolveRotation(1); // Assassination tab
            Assert.Contains("Assassination", m.ActiveLabel);
            Assert.Contains("Combat", m.ActiveLabel); // "...→Combat" makes the fallback explicit
        }

        [Fact]
        public void Same_spec_returns_the_same_rotation_instance()
        {
            var m = new RogueModule();
            IRotation a = m.ResolveRotation(2);
            IRotation b = m.ResolveRotation(2);
            Assert.Same(a, b); // host swaps the engine only when the instance changes
        }

        [Fact]
        public void Range_reports_the_melee_combat_range()
        {
            var m = new RogueModule();
            Assert.Equal(5f, m.Range); // rogue fights in melee
        }

        [Fact]
        public void Talent_build_follows_the_active_spec()
        {
            var m = new RogueModule();
            Spec(m).Value = "Assassination";
            m.ResolveRotation(0);
            Assert.NotNull(m.DesiredTalentBuild()); // assassination build codes exist even though the rotation is Combat
        }

        [Fact]
        public void Talent_build_is_null_when_auto_assign_is_off()
        {
            var m = new RogueModule();
            ((ToggleSetting)m.Settings.First(s => s.Key == "autoTalents")).Value = false;
            m.ResolveRotation(2);
            Assert.Null(m.DesiredTalentBuild());
        }

        [Fact]
        public void Rogue_leaves_food_to_the_vendor_plugin()
        {
            Assert.False(ClassModules.For(WowClass.Rogue).ManageBagFoodDrink);
        }

        [Fact]
        public void Auto_target_switch_is_off_by_default()
        {
            Assert.False(new RogueModule().AutoSwitchTargetEnabled);
        }
    }
}
