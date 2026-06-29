using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations;
using AIO3.Core.Rotations.DeathKnight;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class DeathKnightModuleTests
    {
        private static ChoiceSetting Choice(IClassModule m, string key) =>
            (ChoiceSetting)m.Settings.First(s => s.Key == key);

        private static ToggleSetting Toggle(IClassModule m, string key) =>
            (ToggleSetting)m.Settings.First(s => s.Key == key);

        [Fact]
        public void Factory_maps_the_death_knight_class()
        {
            IClassModule m = ClassModules.For(WowClass.DeathKnight);
            Assert.NotNull(m);
            Assert.Equal(WowClass.DeathKnight, m.Class);
            Assert.Equal("Death Knight", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new DeathKnightModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Fact]
        public void Auto_resolves_each_tree_from_the_talent_tab()
        {
            Assert.IsType<SoloBlood>(new DeathKnightModule().ResolveRotation(1));
            Assert.IsType<SoloFrost>(new DeathKnightModule().ResolveRotation(2));
            Assert.IsType<SoloUnholy>(new DeathKnightModule().ResolveRotation(3));
        }

        [Fact]
        public void No_points_resolves_blood()
        {
            var m = new DeathKnightModule();
            Assert.IsType<SoloBlood>(m.ResolveRotation(0));
            Assert.Equal("Solo Blood", m.ActiveLabel);
        }

        [Fact]
        public void Override_resolves_the_chosen_spec()
        {
            var m = new DeathKnightModule();
            Choice(m, "spec").Value = "Unholy";
            Assert.IsType<SoloUnholy>(m.ResolveRotation(1)); // talents say Blood, override wins
        }

        [Fact]
        public void Range_reports_melee()
        {
            var m = new DeathKnightModule();
            Assert.True(m.Range <= 10, $"DK should report melee range, got {m.Range}");
        }

        [Fact]
        public void Rotation_is_stable_across_resolves_with_the_same_spec()
        {
            var m = new DeathKnightModule();
            var a = m.ResolveRotation(2);
            var b = m.ResolveRotation(2);
            Assert.Same(a, b); // rebuilt only on a spec/mode change
        }

        [Fact]
        public void Talent_build_follows_the_active_spec_and_toggle()
        {
            var m = new DeathKnightModule();
            m.ResolveRotation(3); // Unholy
            Assert.Same(DeathKnightTalents.For(DeathKnightSpec.Unholy), m.DesiredTalentBuild());

            Toggle(m, "autoTalents").Value = false;
            Assert.Null(m.DesiredTalentBuild());
        }

        [Fact]
        public void Does_not_manage_bag_food()
        {
            Assert.False(new DeathKnightModule().ManageBagFoodDrink);
        }

        [Fact]
        public void Every_setting_is_in_the_all_list()
        {
            // Catches a knob that's declared but forgotten in _all (it would never persist or show).
            var s = new DeathKnightSettings();
            Assert.Contains(s.RuneTapPercent, s.All);
            Assert.Contains(s.UnholyDeathCoilRunicPower, s.All);
            Assert.Contains(s.UseRaiseDead, s.All);
            Assert.Contains(s.Presence, s.All);
        }
    }
}
