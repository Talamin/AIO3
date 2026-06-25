using AIO3.Core.Rotations.Rogue;
using AIO3.Core.Settings;
using Xunit;

namespace AIO3.Tests
{
    public class RogueSpecsTests
    {
        [Theory]
        [InlineData(1, RogueSpec.Assassination)]
        [InlineData(2, RogueSpec.Combat)]
        [InlineData(3, RogueSpec.Subtlety)]
        [InlineData(0, RogueSpec.Combat)] // no points yet (pre-10) → leveling default = Combat
        public void Auto_detects_from_highest_talent_tab(int tab, RogueSpec expected)
        {
            Assert.Equal(expected, RogueSpecs.Resolve(RogueSpecs.Auto, tab));
        }

        [Theory]
        [InlineData("Assassination", RogueSpec.Assassination)]
        [InlineData("Combat", RogueSpec.Combat)]
        [InlineData("Subtlety", RogueSpec.Subtlety)]
        public void Manual_override_wins_over_talents(string choice, RogueSpec expected)
        {
            // Talents say Assassination (tab 1), but the manual override takes precedence.
            Assert.Equal(expected, RogueSpecs.Resolve(choice, highestTalentTab: 1));
        }

        [Fact]
        public void Choice_setting_round_trips_and_rejects_unknown_values()
        {
            var s = new ChoiceSetting("spec", "Spec", RogueSpecs.Auto, RogueSpecs.Choices);

            s.Deserialize("Combat");
            Assert.Equal("Combat", s.Value);
            Assert.Equal("Combat", s.Serialize());

            s.Deserialize("Bogus"); // not an option → keep current
            Assert.Equal("Combat", s.Value);
        }

        [Fact]
        public void Talent_build_is_provided_for_each_spec()
        {
            // Combat ships its own build; Assassination has the ported group build; Subtlety falls back to Combat.
            Assert.NotEmpty(RogueTalents.For(RogueSpec.Combat));
            Assert.NotEmpty(RogueTalents.For(RogueSpec.Assassination));
            Assert.Same(RogueTalents.For(RogueSpec.Combat), RogueTalents.For(RogueSpec.Subtlety));
        }

        [Fact]
        public void Assassination_finisher_defaults_to_Eviscerate_and_suppresses_Envenom()
        {
            var s = new RogueSettings();
            // Poisons are deferred, so the default dump is Eviscerate, not Envenom.
            Assert.Equal("Eviscerate", s.AssassinationFinisher.Value);
            Assert.False(s.UseEnvenomFinisher); // "Eviscerate" → Envenom suppressed

            s.AssassinationFinisher.Value = "Envenom";
            Assert.True(s.UseEnvenomFinisher);  // explicit Envenom

            s.AssassinationFinisher.Value = "Auto";
            Assert.True(s.UseEnvenomFinisher);  // Auto still allows Envenom (auto-skips via IsSpellKnown when unlearned)
        }
    }
}
