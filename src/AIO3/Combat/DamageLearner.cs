using System;
using System.Collections.Generic;
using AIO3.Core.Combat;
using wManager.Wow.ObjectManager;

namespace AIO3.Combat
{
    /// <summary>
    /// Feeds the <see cref="DamageTracker"/> from the combat log: every time WE deal damage, record the
    /// ability and amount. Subscribe OnCombatLog to EventsLuaWithArgs.OnEventsLuaStringWithArgs.
    /// MEASURE-ONLY — nothing reads the tracker to make decisions yet.
    ///
    /// COMBAT_LOG_EVENT_UNFILTERED args: [1]=subevent, [2]=sourceGUID(hex). For SPELL_*: [8]=spellId,
    /// [9]=spellName, [11]=amount. For SWING_DAMAGE (melee): [8]=amount (no spell prefix).
    /// </summary>
    internal sealed class DamageLearner
    {
        private readonly DamageTracker _tracker;

        public DamageLearner(DamageTracker tracker) => _tracker = tracker;

        public void OnCombatLog(string eventId, List<string> args)
        {
            try
            {
                if (eventId != "COMBAT_LOG_EVENT_UNFILTERED" || args == null || args.Count < 9) return;

                // Identify a damage subevent FIRST (cheap string compare) and skip everything else before
                // touching the GUID. Most combat-log events aren't damage, so during a pack this keeps the
                // per-event cost tiny instead of hex-parsing a GUID thousands of times a fight.
                string ability;
                long amount;
                switch (args[1])
                {
                    case "SWING_DAMAGE": // white melee hit
                        ability = "Auto Attack";
                        if (!long.TryParse(args[8], out amount)) return;
                        break;

                    case "SPELL_DAMAGE":
                    case "SPELL_PERIODIC_DAMAGE": // DoT tick (attributed to the spell by name)
                    case "RANGE_DAMAGE":
                        if (args.Count < 12) return;
                        ability = args[9];
                        if (!long.TryParse(args[11], out amount)) return;
                        break;

                    default:
                        return;
                }

                // Only our own damage (a pet class will add a second learner for the pet's GUID).
                if (ParseGuid(args[2]) != MyGuid) return;

                _tracker.Record(ability, amount);
            }
            catch
            {
                // Combat-log parsing must never crash the event thread.
            }
        }

        // Our GUID is constant for this fightclass load, so read it once instead of per event.
        private ulong _myGuid;
        private ulong MyGuid
        {
            get
            {
                if (_myGuid == 0) _myGuid = ObjectManager.Me.Guid;
                return _myGuid;
            }
        }

        private static ulong ParseGuid(string hex)
        {
            try { return Convert.ToUInt64(hex, 16); }
            catch { return 0; }
        }
    }
}
