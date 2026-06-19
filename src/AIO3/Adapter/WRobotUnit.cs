using System.Linq;
using AIO3.Core.Game;
using wManager.Wow.Class;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;
using WReaction = wManager.Wow.Enums.Reaction;

namespace AIO3.Adapter
{
    /// <summary>
    /// Adapts a wManager <see cref="WoWUnit"/> to the engine-facing <see cref="IWowUnit"/>.
    /// Reads are live; with the single-threaded tick loop this is safe. (When we later
    /// need a true frozen snapshot for parallel work, this is where we'd copy fields.)
    /// </summary>
    internal sealed class WRobotUnit : IWowUnit
    {
        private readonly WoWUnit _unit;

        public WRobotUnit(WoWUnit unit) => _unit = unit;

        public WoWUnit Inner => _unit;

        public ulong Guid => _unit.Guid;
        public string Name => _unit.Name;
        public bool IsAlive => _unit.IsAlive;
        public double HealthPercent => _unit.HealthPercent;
        public double PowerPercent => _unit.ManaPercentage;
        public int Rage => (int)_unit.Rage;
        public float Distance => _unit.GetDistance;
        public bool IsCasting => _unit.IsCast;
        public bool IsTargetingMe => _unit.IsTargetingMe;
        public bool IsAttackable => _unit.IsAttackable;

        public Reaction Reaction =>
            _unit.Reaction > WReaction.Neutral ? Reaction.Friendly :
            _unit.Reaction == WReaction.Neutral ? Reaction.Neutral :
            Reaction.Hostile;

        public bool HasAura(string name) => _unit.HaveBuff(name);

        public int AuraStacks(string name) => (int)_unit.BuffStack(name);

        public bool HasMyAura(string name)
        {
            ulong me = ObjectManager.Me.Guid;
            var ids = SpellListManager.SpellIdByName(name);
            return BuffManager.GetAuras(_unit.GetBaseAddress)
                .Any(a => a.Owner == me && ids.Contains(a.SpellId));
        }

        public long MyAuraTimeLeftMs(string name)
        {
            ulong me = ObjectManager.Me.Guid;
            var ids = SpellListManager.SpellIdByName(name);
            var aura = BuffManager.GetAuras(_unit.GetBaseAddress)
                .FirstOrDefault(a => a.Owner == me && ids.Contains(a.SpellId));
            // Aura.TimeLeft is already computed/clamped by WRobot (no manual frame-time math).
            return aura != null && aura.TimeLeft > 0 ? aura.TimeLeft : 0;
        }
    }
}
