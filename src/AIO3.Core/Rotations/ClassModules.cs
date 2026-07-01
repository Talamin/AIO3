using AIO3.Core.Game;

namespace AIO3.Core.Rotations
{
    /// <summary>
    /// Maps the player's class to its <see cref="IClassModule"/> implementation. Returns null for classes
    /// not implemented yet, in which case the host runs idle. This is the single place a new class is
    /// registered once its module exists.
    /// </summary>
    public static class ClassModules
    {
        public static IClassModule For(WowClass playerClass, IGameClient game = null)
        {
            switch (playerClass)
            {
                case WowClass.Warrior: return new WarriorModule();
                case WowClass.Paladin: return new PaladinModule();
                case WowClass.Hunter: return new HunterModule();
                case WowClass.Rogue: return new RogueModule();
                case WowClass.Mage: return new MageModule();
                case WowClass.Warlock: return new WarlockModule();
                case WowClass.Priest: return new PriestModule();
                // Shaman + Druid take the game client so their Range can switch caster↔melee on whether the melee
                // strike / form is learned (a pre-Stormstrike shaman / pre-form druid levels at caster range).
                case WowClass.Shaman: return new ShamanModule(game);
                case WowClass.DeathKnight: return new DeathKnightModule();
                case WowClass.Druid: return new DruidModule(game);
                default: return null;
            }
        }
    }
}
