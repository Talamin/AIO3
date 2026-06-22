using System.Collections.Concurrent;
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
        public int Entry => _unit.Entry;
        public string Name => _unit.Name;
        public bool IsAlive => _unit.IsAlive;
        public double HealthPercent => _unit.HealthPercent;
        public double PowerPercent => _unit.ManaPercentage;
        public int Rage => (int)_unit.Rage;
        public float Distance => _unit.GetDistance;
        public bool IsCasting => _unit.IsCast;
        public int CastingSpellId => _unit.CastingSpellId;
        public bool IsTargetingMe => _unit.IsTargetingMe;
        public bool IsTargetingMyPet => _unit.IsTargetingMyPet;
        public ulong TargetGuid => _unit.Target; // WoWUnit.Target is the GUID of this unit's current target
        public bool IsAttackable => _unit.IsAttackable;
        public bool IsElite => _unit.IsElite;

        // Creature type is read via Lua and cached per creature entry. We can only query it reliably
        // for the current target, so we resolve it when this unit is the target and reuse it after.
        private static readonly ConcurrentDictionary<int, string> CreatureTypeByEntry = new ConcurrentDictionary<int, string>();

        public string CreatureType
        {
            get
            {
                if (CreatureTypeByEntry.TryGetValue(_unit.Entry, out string cached) && !string.IsNullOrEmpty(cached))
                    return cached;

                WoWUnit target = ObjectManager.Target;
                if (target != null && target.Guid == _unit.Guid)
                {
                    string ct = Lua.LuaDoString<string>("return UnitCreatureType('target') or ''");
                    if (!string.IsNullOrEmpty(ct))
                        CreatureTypeByEntry[_unit.Entry] = ct;
                    return ct;
                }

                return cached ?? "";
            }
        }

        public Reaction Reaction =>
            _unit.Reaction > WReaction.Neutral ? Reaction.Friendly :
            _unit.Reaction == WReaction.Neutral ? Reaction.Neutral :
            Reaction.Hostile;

        public bool HasAura(string name) => _unit.HaveBuff(name);

        public int AuraStacks(string name) => (int)_unit.BuffStack(name);

        public bool HasMyAura(string name)
        {
            // SpellIdByName resolves only the CAST ranks of a name (Frost Nova -> 122,865,...). Some roots/CC
            // land as a separately-named "Effect" aura with a different spell id (Frost Nova's root is the
            // 56135 "Frost Nova Effect" aura) and sometimes no recorded caster (Owner == 0). So: accept the
            // fast cast-rank id match OR a prefix match on the resolved aura name (catches "<name> Effect"),
            // and treat an unowned aura as ours too (solo play — no other caster of these spells is around).
            ulong me = ObjectManager.Me.Guid;
            var ids = SpellListManager.SpellIdByName(name);
            foreach (var a in BuffManager.GetAuras(_unit.GetBaseAddress))
            {
                if (a.Owner != me && a.Owner != 0UL) continue;            // ours, or an unowned effect aura
                if (ids.Contains(a.SpellId)) return true;                 // fast path: a cast-rank id
                string auraName = a.GetSpell?.Name;                       // fallback: the resolved aura name
                if (!string.IsNullOrEmpty(auraName)
                    && auraName.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
