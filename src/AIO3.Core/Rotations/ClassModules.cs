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
        public static IClassModule For(WowClass playerClass)
        {
            switch (playerClass)
            {
                case WowClass.Warrior: return new WarriorModule();
                case WowClass.Paladin: return new PaladinModule();
                case WowClass.Hunter: return new HunterModule();
                default: return null;
            }
        }
    }
}
