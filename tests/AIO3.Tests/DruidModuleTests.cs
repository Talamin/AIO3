using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations;
using AIO3.Core.Rotations.Druid;
using AIO3.Core.Settings;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    public class DruidModuleTests
    {
        private static ChoiceSetting Spec(DruidModule m) => (ChoiceSetting)m.Settings.First(s => s.Key == "spec");

        [Fact]
        public void Factory_maps_the_druid_class()
        {
            IClassModule m = ClassModules.For(WowClass.Druid);
            Assert.NotNull(m);
            Assert.Equal("Druid", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new DruidModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Theory]
        [InlineData(2, "Feral")]   // Feral tab → Feral label
        [InlineData(0, "Feral")]   // no points → leveling default = Feral
        [InlineData(1, "Balance")] // Balance tab → Balance label
        public void Auto_resolves_from_talents(int tab, string expectedLabel)
        {
            var m = new DruidModule();
            m.ResolveRotation(tab);
            Assert.Contains(expectedLabel, m.ActiveLabel);
        }

        [Fact]
        public void Each_tab_resolves_to_its_shipped_rotation()
        {
            // Balance and Feral each ship a rotation; Restoration has none yet and falls back to Feral.
            var m = new DruidModule();
            Assert.Contains("Balance", m.ResolveRotation(1).Name); // Balance tab → SoloBalance
            Assert.Contains("Feral", m.ResolveRotation(2).Name);   // Feral tab → SoloFeral
            Assert.Contains("Feral", m.ResolveRotation(3).Name);   // Restoration tab → falls back to SoloFeral
        }

        [Fact]
        public void Restoration_label_shows_the_fallback_to_feral()
        {
            var m = new DruidModule();
            m.ResolveRotation(3); // Restoration tab — not built
            Assert.Contains("Restoration", m.ActiveLabel);
            Assert.Contains("Feral", m.ActiveLabel); // "...→Feral" makes the fallback explicit
        }

        [Fact]
        public void Balance_label_names_the_balance_spec()
        {
            var m = new DruidModule();
            m.ResolveRotation(1); // Balance tab
            Assert.Contains("Balance", m.ActiveLabel);
            Assert.DoesNotContain("→Feral", m.ActiveLabel); // it ships its own rotation, not a fallback
        }

        [Fact]
        public void Same_spec_returns_the_same_rotation_instance()
        {
            var m = new DruidModule();
            IRotation a = m.ResolveRotation(2);
            IRotation b = m.ResolveRotation(2);
            Assert.Same(a, b); // host swaps the engine only when the instance changes
        }

        [Fact]
        public void Range_is_melee_for_feral_and_caster_for_balance()
        {
            var m = new DruidModule();
            m.ResolveRotation(2); // Feral
            Assert.Equal(5f, m.Range);
            m.ResolveRotation(1); // Balance
            Assert.Equal(29f, m.Range);
        }

        [Fact]
        public void Feral_range_is_caster_until_a_form_is_learned()
        {
            var game = new FakeGameClient { Class = WowClass.Druid };
            var m = new DruidModule(game);
            m.ResolveRotation(2); // Feral

            // A formless low-level druid nukes with Wrath → caster range (so WRobot doesn't drag it into melee).
            game.UnknownSpells.Add("Bear Form");
            game.UnknownSpells.Add("Cat Form");
            game.UnknownSpells.Add("Dire Bear Form");
            Assert.Equal(29f, m.Range);

            // Bear Form learned (~level 10) → melee range, switched live.
            game.UnknownSpells.Remove("Bear Form");
            Assert.Equal(5f, m.Range);
        }

        [Fact]
        public void Talent_build_follows_the_active_spec()
        {
            var m = new DruidModule();
            Spec(m).Value = "Restoration";
            m.ResolveRotation(0);
            Assert.NotNull(m.DesiredTalentBuild()); // resto build codes exist even though the rotation is Feral
        }

        [Fact]
        public void Talent_build_is_null_when_auto_assign_is_off()
        {
            var m = new DruidModule();
            ((ToggleSetting)m.Settings.First(s => s.Key == "autoTalents")).Value = false;
            m.ResolveRotation(2);
            Assert.Null(m.DesiredTalentBuild());
        }

        [Fact]
        public void Druid_leaves_food_to_the_vendor_plugin()
        {
            Assert.False(ClassModules.For(WowClass.Druid).ManageBagFoodDrink);
        }

        [Fact]
        public void Auto_target_switch_is_off_by_default()
        {
            Assert.False(new DruidModule().AutoSwitchTargetEnabled);
        }
    }
}
