using System.Linq;
using AIO3.Core.Engine;
using AIO3.Core.Rotations;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class PaladinModuleTests
    {
        private static ChoiceSetting Choice(IClassModule m, string key) =>
            (ChoiceSetting)m.Settings.First(s => s.Key == key);

        private static ToggleSetting Toggle(IClassModule m, string key) =>
            (ToggleSetting)m.Settings.First(s => s.Key == key);

        [Fact]
        public void Factory_maps_paladin_class()
        {
            IClassModule m = ClassModules.For(AIO3.Core.Game.WowClass.Paladin);
            Assert.NotNull(m);
            Assert.Equal("Paladin", m.DisplayName);
        }

        [Fact]
        public void Spec_selector_is_the_first_setting()
        {
            var m = new PaladinModule();
            Assert.Equal("spec", m.Settings[0].Key);
        }

        [Fact]
        public void Auto_resolves_the_retribution_rotation()
        {
            var m = new PaladinModule();
            IRotation r = m.ResolveRotation(3);
            Assert.Contains("Retribution", r.Name);
        }

        [Fact]
        public void Override_resolves_the_protection_rotation()
        {
            var m = new PaladinModule();
            Choice(m, "spec").Value = "Protection";
            IRotation r = m.ResolveRotation(3);
            Assert.Contains("Protection", r.Name);
        }

        [Fact]
        public void Rotation_instance_is_stable_until_the_spec_changes()
        {
            var m = new PaladinModule();
            IRotation a = m.ResolveRotation(3);
            IRotation b = m.ResolveRotation(3);
            Assert.Same(a, b); // unchanged → same instance (host won't rebuild the engine)

            Choice(m, "spec").Value = "Protection";
            IRotation c = m.ResolveRotation(3);
            Assert.NotSame(a, c); // changed → new instance
        }

        [Fact]
        public void Group_mode_falls_back_to_solo_and_says_so()
        {
            var m = new PaladinModule();
            Choice(m, "mode").Value = "Group";
            m.ResolveRotation(3);
            Assert.StartsWith("Group→Solo", m.ActiveLabel);
        }

        [Fact]
        public void Talent_build_follows_the_auto_assign_toggle()
        {
            var m = new PaladinModule();
            m.ResolveRotation(3); // resolve a spec first

            Assert.NotNull(m.DesiredTalentBuild()); // auto-assign on by default

            Toggle(m, "autoTalents").Value = false;
            Assert.Null(m.DesiredTalentBuild());
        }
    }
}
