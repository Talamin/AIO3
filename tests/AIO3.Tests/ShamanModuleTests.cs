using AIO3.Core.Game;
using AIO3.Core.Rotations;
using AIO3.Core.Rotations.Shaman;
using Xunit;

namespace AIO3.Tests
{
    public class ShamanModuleTests
    {
        [Fact]
        public void Registered_for_the_shaman_class()
        {
            IClassModule m = ClassModules.For(WowClass.Shaman);
            Assert.NotNull(m);
            Assert.Equal(WowClass.Shaman, m.Class);
        }

        [Fact]
        public void Enhancement_is_melee_range_elemental_is_caster_range()
        {
            var m = new ShamanModule();
            m.ResolveRotation(highestTalentTab: 2); // Enhancement
            Assert.True(m.Range <= 10, $"Enhancement should report melee range, got {m.Range}");

            m.ResolveRotation(highestTalentTab: 1); // Elemental
            Assert.True(m.Range >= 20, $"Elemental should report caster range, got {m.Range}");
        }

        [Fact]
        public void Enhancement_resolves_to_the_enhancement_rotation()
        {
            var m = new ShamanModule();
            var r = m.ResolveRotation(highestTalentTab: 2);
            Assert.IsType<SoloEnhancement>(r);
            Assert.Equal("Solo Enhancement", m.ActiveLabel);
        }

        [Fact]
        public void Elemental_resolves_to_the_elemental_rotation()
        {
            var m = new ShamanModule();
            var r = m.ResolveRotation(highestTalentTab: 1);
            Assert.IsType<SoloElemental>(r);
            Assert.Equal("Solo Elemental", m.ActiveLabel);
        }

        [Fact]
        public void Restoration_falls_back_to_elemental_with_a_label_note()
        {
            var m = new ShamanModule();
            var r = m.ResolveRotation(highestTalentTab: 3); // Restoration → no rotation yet
            Assert.IsType<SoloElemental>(r);                // ...so it runs Elemental
            Assert.Equal("Solo Restoration→Elemental", m.ActiveLabel);
        }

        [Fact]
        public void Rotation_is_stable_across_resolves_with_the_same_spec()
        {
            var m = new ShamanModule();
            var a = m.ResolveRotation(2);
            var b = m.ResolveRotation(2);
            Assert.Same(a, b); // rebuilt only on a spec/mode change → host can compare by reference
        }

        [Fact]
        public void Talent_build_follows_the_active_spec()
        {
            var m = new ShamanModule();
            m.ResolveRotation(highestTalentTab: 1); // Elemental
            Assert.Same(ShamanTalents.For(ShamanSpec.Elemental), m.DesiredTalentBuild());
        }

        [Fact]
        public void Does_not_manage_bag_food()
        {
            Assert.False(new ShamanModule().ManageBagFoodDrink);
        }
    }
}
