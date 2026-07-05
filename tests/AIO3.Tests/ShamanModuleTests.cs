using AIO3.Core.Game;
using AIO3.Core.Rotations;
using AIO3.Core.Rotations.Shaman;
using AIO3.Core.Testing;
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
        public void Low_level_enhancement_pulls_at_caster_range_then_closes_in_combat()
        {
            // Pre-Stormstrike (L40) Enhancement opens with a spell then melees: caster range OUT of combat (pull with
            // Lightning Bolt), melee range IN combat (close + finish) — so it doesn't stand at range and go OOM.
            var g = new FakeGameClient { Class = WowClass.Shaman };
            g.UnknownSpells.Add("Stormstrike");
            var m = new ShamanModule(g);
            m.ResolveRotation(highestTalentTab: 2); // Enhancement

            g.InCombatFlag = false;
            Assert.True(m.Range >= 20, $"pre-Stormstrike out of combat should pull at caster range, got {m.Range}");

            g.InCombatFlag = true;
            Assert.True(m.Range <= 10, $"pre-Stormstrike in combat should close to melee range, got {m.Range}");

            g.UnknownSpells.Remove("Stormstrike"); // learned the melee strike → melee regardless
            g.InCombatFlag = false;
            Assert.True(m.Range <= 10, $"with Stormstrike should be melee range, got {m.Range}");
        }

        [Fact]
        public void Low_level_enhancement_goes_melee_when_it_cant_afford_the_opener()
        {
            // No mana for the Lightning Bolt opener → don't stand at caster range waiting for a spell we can't cast;
            // report MELEE range so the bot walks straight in (Talamin).
            var g = new FakeGameClient { Class = WowClass.Shaman };
            g.UnknownSpells.Add("Stormstrike");     // pre-Stormstrike
            g.SpellManaCosts["Lightning Bolt"] = 100;
            g.MeUnit.Mana = 50;                     // can't afford one Lightning Bolt
            var m = new ShamanModule(g);
            m.ResolveRotation(highestTalentTab: 2); // Enhancement
            g.InCombatFlag = false;
            Assert.True(m.Range <= 10, $"can't afford opener out of combat → melee range, got {m.Range}");
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
