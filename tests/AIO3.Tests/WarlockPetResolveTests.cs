using AIO3.Core.Combat;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warlock;
using AIO3.Core.Testing;
using Xunit;

namespace AIO3.Tests
{
    /// <summary>Auto pet resolution must always land on a demon the warlock has actually LEARNED — ending at the
    /// Imp (the level-1 pet). The bug this guards: a low-level lock defaulted to Affliction, whose Auto pick is the
    /// Voidwalker, but Summon Voidwalker isn't learned yet, so the FC summoned nothing instead of the Imp.</summary>
    public class WarlockPetResolveTests
    {
        private static CombatContext Ctx(FakeGameClient g) => CombatContext.Capture(g);

        [Fact]
        public void Auto_low_level_affliction_falls_back_to_the_Imp()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            g.UnknownSpells.Add("Summon Voidwalker"); // not learned yet at low level
            g.UnknownSpells.Add("Summon Felguard");
            var s = new WarlockSettings();             // Pet = "Auto"
            Assert.Equal("Imp", WarlockCommon.ResolvePet(s, Ctx(g), WarlockSpec.Affliction));
        }

        [Fact]
        public void Auto_affliction_uses_the_Voidwalker_once_learned()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock }; // all spells known
            Assert.Equal("Voidwalker", WarlockCommon.ResolvePet(new WarlockSettings(), Ctx(g), WarlockSpec.Affliction));
        }

        [Fact]
        public void Auto_destruction_uses_the_Imp()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            Assert.Equal("Imp", WarlockCommon.ResolvePet(new WarlockSettings(), Ctx(g), WarlockSpec.Destruction));
        }

        [Fact]
        public void Auto_low_level_demonology_falls_back_to_the_Imp()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            g.UnknownSpells.Add("Summon Felguard");
            g.UnknownSpells.Add("Summon Voidwalker"); // neither learned → Imp
            Assert.Equal("Imp", WarlockCommon.ResolvePet(new WarlockSettings(), Ctx(g), WarlockSpec.Demonology));
        }

        [Fact]
        public void A_manual_choice_is_respected()
        {
            var g = new FakeGameClient { Class = WowClass.Warlock };
            var s = new WarlockSettings();
            s.Pet.Value = "Felhunter";
            Assert.Equal("Felhunter", WarlockCommon.ResolvePet(s, Ctx(g), WarlockSpec.Affliction));
        }
    }
}
