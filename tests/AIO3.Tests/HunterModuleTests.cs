using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class HunterModuleTests
    {
        private static ChoiceSetting Choice(IClassModule m, string key) =>
            (ChoiceSetting)m.Settings.First(s => s.Key == key);

        private static ToggleSetting Toggle(IClassModule m, string key) =>
            (ToggleSetting)m.Settings.First(s => s.Key == key);

        [Fact]
        public void Factory_maps_hunter_class()
        {
            IClassModule m = ClassModules.For(WowClass.Hunter);
            Assert.NotNull(m);
            Assert.Equal("Hunter", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new HunterModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Fact]
        public void Auto_resolves_the_beast_mastery_rotation()
        {
            var m = new HunterModule();
            IRotation r = m.ResolveRotation(1); // tab 1 = Beast Mastery
            Assert.Contains("Beast Mastery", r.Name);
        }

        [Fact]
        public void Auto_resolves_marksmanship_and_survival_from_talents()
        {
            Assert.Contains("Marksmanship", new HunterModule().ResolveRotation(2).Name); // tab 2
            Assert.Contains("Survival", new HunterModule().ResolveRotation(3).Name);     // tab 3
        }

        [Fact]
        public void Override_resolves_the_chosen_spec()
        {
            var m = new HunterModule();
            Choice(m, "spec").Value = "Survival";
            Assert.Contains("Survival", m.ResolveRotation(1).Name); // talents say BM, override wins
        }

        [Fact]
        public void Range_reports_the_combat_range()
        {
            var m = new HunterModule();
            Assert.Equal(28f, m.Range); // hunters stand at range
        }

        [Fact]
        public void Talent_build_follows_the_auto_assign_toggle()
        {
            var m = new HunterModule();
            m.ResolveRotation(1);

            Assert.NotNull(m.DesiredTalentBuild());

            Toggle(m, "autoTalents").Value = false;
            Assert.Null(m.DesiredTalentBuild());
        }
    }
}
