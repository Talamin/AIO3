using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Rotations;
using AIO3.Core.Game;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class MageModuleTests
    {
        private static ChoiceSetting Spec(MageModule m) => (ChoiceSetting)m.Settings.First(s => s.Key == "spec");

        [Fact]
        public void Factory_maps_the_mage_class()
        {
            IClassModule m = ClassModules.For(WowClass.Mage);
            Assert.NotNull(m);
            Assert.Equal("Mage", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new MageModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Theory]
        [InlineData(3, "Frost")]
        [InlineData(2, "Fire")]
        [InlineData(1, "Arcane")]
        public void Auto_resolves_the_spec_from_talents(int tab, string expected)
        {
            var m = new MageModule();
            IRotation r = m.ResolveRotation(tab);
            Assert.Contains(expected, r.Name);
        }

        [Fact]
        public void Manual_override_picks_the_spec()
        {
            var m = new MageModule();
            Spec(m).Value = "Fire";
            Assert.Contains("Fire", m.ResolveRotation(3).Name); // tab says Frost, override says Fire
        }

        [Fact]
        public void Same_spec_returns_the_same_rotation_instance()
        {
            var m = new MageModule();
            IRotation a = m.ResolveRotation(3);
            IRotation b = m.ResolveRotation(3);
            Assert.Same(a, b); // host swaps the engine only when the instance changes
        }

        [Fact]
        public void Talent_build_follows_the_active_spec()
        {
            var m = new MageModule();
            Spec(m).Value = "Arcane";
            m.ResolveRotation(0);
            Assert.NotNull(m.DesiredTalentBuild()); // arcane build codes exist
        }

        [Fact]
        public void Talent_build_is_null_when_auto_assign_is_off()
        {
            var m = new MageModule();
            ((ToggleSetting)m.Settings.First(s => s.Key == "autoTalents")).Value = false;
            m.ResolveRotation(3);
            Assert.Null(m.DesiredTalentBuild());
        }

        [Fact]
        public void Manages_bag_food_by_default_and_follows_the_toggle()
        {
            var m = new MageModule();
            Assert.True(m.ManageBagFoodDrink); // on by default — the mage eats what it conjures
            ((ToggleSetting)m.Settings.First(s => s.Key == "manageFood")).Value = false;
            Assert.False(m.ManageBagFoodDrink);
        }

        [Fact]
        public void Non_conjuring_classes_leave_food_to_the_vendor_plugin()
        {
            Assert.False(ClassModules.For(WowClass.Warrior).ManageBagFoodDrink);
            Assert.False(ClassModules.For(WowClass.Hunter).ManageBagFoodDrink);
        }
    }
}
