using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class SettingsSerializerTests
    {
        [Fact]
        public void Round_trips_int_and_toggle_values()
        {
            var source = new Setting[]
            {
                new IntSetting("hs", "HS", value: 35, min: 0, max: 100, step: 5),
                new ToggleSetting("aoe", "AoE", value: true),
            };
            string text = SettingsSerializer.ToText(source);

            var target = new Setting[]
            {
                new IntSetting("hs", "HS", value: 0, min: 0, max: 100, step: 5),
                new ToggleSetting("aoe", "AoE", value: false),
            };
            SettingsSerializer.ApplyText(target, text);

            Assert.Equal(35, ((IntSetting)target[0]).Value);
            Assert.True(((ToggleSetting)target[1]).Value);
        }

        [Fact]
        public void Deserialize_clamps_int_to_bounds()
        {
            var s = new IntSetting("hs", "HS", value: 20, min: 0, max: 50, step: 5);
            s.Deserialize("999");
            Assert.Equal(50, s.Value);
        }

        [Fact]
        public void Unknown_keys_are_ignored()
        {
            var s = new IntSetting("hs", "HS", value: 20, min: 0, max: 100, step: 5);
            SettingsSerializer.ApplyText(new Setting[] { s }, "somethingElse=7\n\n");
            Assert.Equal(20, s.Value);
        }

        [Fact]
        public void Garbage_value_leaves_setting_unchanged()
        {
            var s = new IntSetting("hs", "HS", value: 20, min: 0, max: 100, step: 5);
            s.Deserialize("not-a-number");
            Assert.Equal(20, s.Value);
        }
    }
}
