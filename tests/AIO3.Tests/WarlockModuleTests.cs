using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Rotations;
using AIO3.Core.Game;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class WarlockModuleTests
    {
        private static ChoiceSetting Spec(WarlockModule m) => (ChoiceSetting)m.Settings.First(s => s.Key == "spec");

        [Fact]
        public void Factory_maps_the_warlock_class()
        {
            IClassModule m = ClassModules.For(WowClass.Warlock);
            Assert.NotNull(m);
            Assert.Equal("Warlock", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new WarlockModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Theory]
        [InlineData(1, "Affliction")]
        [InlineData(2, "Demonology")]
        [InlineData(3, "Destruction")]
        [InlineData(0, "Affliction")] // no points → leveling default
        public void Auto_resolves_the_spec_from_talents(int tab, string expectedLabel)
        {
            var m = new WarlockModule();
            m.ResolveRotation(tab);
            Assert.Contains(expectedLabel, m.ActiveLabel);
        }

        [Fact]
        public void Resolves_each_tab_to_its_own_spec_rotation()
        {
            var m = new WarlockModule();
            Assert.Contains("Affliction", m.ResolveRotation(1).Name);
            Assert.Contains("Demonology", m.ResolveRotation(2).Name);
            Assert.Contains("Destruction", m.ResolveRotation(3).Name);
        }

        [Fact]
        public void Manual_override_picks_the_spec_label()
        {
            var m = new WarlockModule();
            Spec(m).Value = "Destruction";
            m.ResolveRotation(1); // talents say Affliction, override says Destruction
            Assert.Contains("Destruction", m.ActiveLabel);
        }

        [Fact]
        public void Same_spec_returns_the_same_rotation_instance()
        {
            var m = new WarlockModule();
            IRotation a = m.ResolveRotation(1);
            IRotation b = m.ResolveRotation(1);
            Assert.Same(a, b); // host swaps the engine only when the instance changes
        }

        [Fact]
        public void Range_reports_the_combat_range()
        {
            var m = new WarlockModule();
            Assert.Equal(30f, m.Range); // warlocks cast at range
        }

        [Fact]
        public void Talent_build_follows_the_active_spec()
        {
            var m = new WarlockModule();
            Spec(m).Value = "Destruction";
            m.ResolveRotation(0);
            Assert.NotNull(m.DesiredTalentBuild()); // destruction build codes exist
        }

        [Fact]
        public void Talent_build_is_null_when_auto_assign_is_off()
        {
            var m = new WarlockModule();
            ((ToggleSetting)m.Settings.First(s => s.Key == "autoTalents")).Value = false;
            m.ResolveRotation(1);
            Assert.Null(m.DesiredTalentBuild());
        }

        [Fact]
        public void Warlock_leaves_food_to_the_vendor_plugin()
        {
            // It conjures Healthstone / Soulstone, not food — like Warrior / Paladin / Hunter.
            Assert.False(ClassModules.For(WowClass.Warlock).ManageBagFoodDrink);
        }

        [Fact]
        public void Auto_target_switch_is_off_by_default()
        {
            // A permanent-pet DoT class commits to its target so DoTs tick out.
            Assert.False(new WarlockModule().AutoSwitchTargetEnabled);
        }
    }
}
