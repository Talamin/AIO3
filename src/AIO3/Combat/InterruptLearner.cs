using System;
using System.Collections.Generic;
using System.IO;
using AIO3.Core.Combat;
using robotManager.Helpful;
using wManager.Wow.ObjectManager;

namespace AIO3.Combat
{
    /// <summary>
    /// Feeds the InterruptTracker from the combat log so it can learn which casts are really
    /// (non-)interruptible — the API flag can't be trusted. Subscribe OnCombatLog to
    /// EventsLuaWithArgs.OnEventsLuaStringWithArgs. The learned blacklist is persisted per character.
    ///
    /// COMBAT_LOG_EVENT_UNFILTERED args: [1]=subevent, [2]=sourceGUID(hex), [8]=spellId,
    /// and for SPELL_INTERRUPT the interrupted ("extra") spell id is at [11].
    /// </summary>
    internal sealed class InterruptLearner
    {
        private readonly InterruptTracker _tracker;
        private readonly string _path;

        public InterruptLearner(InterruptTracker tracker, string profile)
        {
            _tracker = tracker;
            string dir = Path.Combine(Others.GetCurrentDirectory, "Settings", "AIO3");
            _path = Path.Combine(dir, Sanitize(profile) + ".interrupts");
            Load();
        }

        public void OnCombatLog(string eventId, List<string> args)
        {
            try
            {
                if (eventId != "COMBAT_LOG_EVENT_UNFILTERED" || args == null || args.Count < 9) return;

                switch (args[1])
                {
                    case "SPELL_INTERRUPT":
                        // We interrupted a cast → the interrupted spell (index 11) IS interruptible.
                        if (args.Count > 11 && ParseGuid(args[2]) == ObjectManager.Me.Guid
                            && int.TryParse(args[11], out int interrupted))
                            _tracker.OnInterruptSucceeded(interrupted);
                        break;

                    case "SPELL_CAST_SUCCESS":
                        // A cast completed; if we had just tried to interrupt it, it is non-interruptible.
                        if (int.TryParse(args[8], out int spellId)
                            && _tracker.OnCastCompleted(ParseGuid(args[2]), spellId))
                            Save(); // a newly learned non-interruptible spell
                        break;
                }
            }
            catch
            {
                // Combat-log parsing must never crash the event thread.
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, _tracker.Serialize());
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] interrupt blacklist save failed: " + e.Message);
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_path)) _tracker.Load(File.ReadAllText(_path));
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] interrupt blacklist load failed: " + e.Message);
            }
        }

        private static ulong ParseGuid(string hex)
        {
            try { return Convert.ToUInt64(hex, 16); }
            catch { return 0; }
        }

        private static string Sanitize(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return "default";
            foreach (char c in Path.GetInvalidFileNameChars()) profile = profile.Replace(c, '_');
            return profile;
        }
    }
}
