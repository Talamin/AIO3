using System.Linq;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>Spec-aware setting visibility: a setting tagged with a <see cref="Setting.Spec"/> only shows while
    /// that spec is active; untagged settings always show. This is what lets the overlay hide a spec's knobs in
    /// the other specs (e.g. Demonology toggles don't appear while playing Affliction).</summary>
    public class SettingVisibilityTests
    {
        [Fact]
        public void Untagged_setting_applies_to_every_spec()
        {
            var s = new ToggleSetting("x", "X", value: true); // no Spec tag
            Assert.True(s.AppliesTo("Demonology"));
            Assert.True(s.AppliesTo("Affliction"));
            Assert.True(s.AppliesTo(null));
        }

        [Fact]
        public void Tagged_setting_applies_only_to_its_spec()
        {
            var s = new ToggleSetting("x", "X", value: true) { Spec = "Demonology" };
            Assert.True(s.AppliesTo("Demonology"));
            Assert.False(s.AppliesTo("Destruction"));
            Assert.False(s.AppliesTo("Affliction"));
        }

        [Fact]
        public void A_null_active_spec_disables_filtering()
        {
            // Before the module has resolved a spec, ActiveSpec is null — show everything rather than hide things.
            var s = new ToggleSetting("x", "X", value: true) { Spec = "Demonology" };
            Assert.True(s.AppliesTo(null));
        }

        [Fact]
        public void Warlock_spec_knobs_are_tagged_and_consolidated_into_rotation()
        {
            var w = new WarlockSettings();

            // The four spec-only knobs are tagged with their spec...
            Assert.Equal("Demonology", w.DemonicEmpowerment.Spec);
            Assert.Equal("Demonology", w.UseSoulFire.Spec);
            Assert.Equal("Destruction", w.UseConflagrate.Spec);
            Assert.Equal("Destruction", w.UseChaosBolt.Spec);

            // ...and consolidated into the Rotation tab (no standalone Demonology/Destruction tabs).
            foreach (var s in new[] { w.DemonicEmpowerment, w.UseSoulFire, w.UseConflagrate, w.UseChaosBolt })
                Assert.Equal("Rotation", s.Category);
            Assert.DoesNotContain(w.All, s => s.Category == "Demonology" || s.Category == "Destruction");

            // General knobs stay untagged (always visible).
            Assert.Null(w.ArmorChoice.Spec);
            Assert.Null(w.Curse.Spec);
        }

        [Fact]
        public void Affliction_hides_the_other_specs_knobs()
        {
            var w = new WarlockSettings();
            var visible = w.All.Where(s => s.AppliesTo("Affliction")).ToList();

            Assert.DoesNotContain(w.DemonicEmpowerment, visible);
            Assert.DoesNotContain(w.UseConflagrate, visible);
            Assert.Contains(w.Curse, visible);        // general knob still shows
            Assert.Contains(w.ArmorChoice, visible);
        }
    }
}
