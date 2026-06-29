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
                case WowClass.Shaman: return new ShamanModule();
                case WowClass.DeathKnight: return new DeathKnightModule();
                // Druid takes the game client so its Range can switch caster↔melee on whether a form is learned.
                case WowClass.Druid: return new DruidModule(game);
                default: return null;
            }
        }
    }
}
