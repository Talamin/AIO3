using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Rotations;
using AIO3.Core.Rotations.Priest;
using AIO3.Core.Game;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class PriestModuleTests
    {
        private static ChoiceSetting Spec(PriestModule m) => (ChoiceSetting)m.Settings.First(s => s.Key == "spec");

        [Fact]
        public void Factory_maps_the_priest_class()
        {
            IClassModule m = ClassModules.For(WowClass.Priest);
            Assert.NotNull(m);
            Assert.Equal("Priest", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new PriestModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Theory]
        [InlineData(3, "Shadow")]
        [InlineData(0, "Shadow")] // no points → solo leveling default
        public void Auto_resolves_shadow_for_the_shadow_tab_and_the_default(int tab, string expectedLabel)
        {
            var m = new PriestModule();
            m.ResolveRotation(tab);
            Assert.Contains(expectedLabel, m.ActiveLabel);
        }

        [Theory]
        [InlineData(1, "Discipline")]
        [InlineData(2, "Holy")]
        public void Disc_and_holy_resolve_but_fall_back_to_shadow_with_a_label_note(int tab, string spec)
        {
            // The deferred healers resolve (so their talent build applies) but map to the Shadow rotation; the
            // label shows the fallback (e.g. "Solo Discipline→Shadow"), like the Druid's Restoration→Feral.
            var m = new PriestModule();
            m.ResolveRotation(tab);
            Assert.Contains(spec, m.ActiveLabel);
            Assert.Contains("Shadow", m.ActiveLabel);
            Assert.Contains("Shadow", m.ResolveRotation(tab).Name); // the rotation IS the Shadow rotation
        }

        [Fact]
        public void Manual_override_picks_the_spec_label()
        {
            var m = new PriestModule();
            Spec(m).Value = "Holy";
            m.ResolveRotation(3); // talents say Shadow, override says Holy
            Assert.Contains("Holy", m.ActiveLabel);
        }

        [Fact]
        public void Same_spec_returns_the_same_rotation_instance()
        {
            var m = new PriestModule();
            IRotation a = m.ResolveRotation(3);
            IRotation b = m.ResolveRotation(3);
            Assert.Same(a, b); // host swaps the engine only when the instance changes
        }

        [Fact]
        public void Range_reports_the_caster_combat_range()
        {
            var m = new PriestModule();
            Assert.Equal(27f, m.Range); // mirrors the old PriestBehavior.Range
        }

        [Theory]
        [InlineData("Shadow")]
        [InlineData("Discipline")]
        [InlineData("Holy")]
        public void Talent_build_follows_the_active_spec(string spec)
        {
            var m = new PriestModule();
            Spec(m).Value = spec;
            m.ResolveRotation(0);
            Assert.NotNull(m.DesiredTalentBuild()); // every spec ships build codes
        }

        [Fact]
        public void Talent_build_is_null_when_auto_assign_is_off()
        {
            var m = new PriestModule();
            ((ToggleSetting)m.Settings.First(s => s.Key == "autoTalents")).Value = false;
            m.ResolveRotation(3);
            Assert.Null(m.DesiredTalentBuild());
        }

        [Fact]
        public void Priest_leaves_food_to_the_vendor_plugin()
        {
            Assert.False(ClassModules.For(WowClass.Priest).ManageBagFoodDrink);
        }

        [Fact]
        public void Auto_target_switch_is_off_by_default()
        {
            // A DoT caster commits to its target so the DoTs tick out.
            Assert.False(new PriestModule().AutoSwitchTargetEnabled);
        }

        [Fact]
        public void Shadow_only_knobs_hide_outside_shadow()
        {
            // Shadowform/Dispersion/etc. are tagged Spec="Shadow" → shown for Shadow, hidden for Holy.
            var m = new PriestModule();
            Setting shadowform = m.Settings.First(s => s.Key == "shadowform");
            Assert.True(shadowform.AppliesTo("Shadow"));
            Assert.False(shadowform.AppliesTo("Holy"));
            // A shared knob (the wand) applies to every spec.
            Assert.True(m.Settings.First(s => s.Key == "wand").AppliesTo("Holy"));
        }
    }
}
