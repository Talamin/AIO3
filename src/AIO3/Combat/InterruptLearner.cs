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
                string text = _tracker.Serialize();
                if (string.IsNullOrWhiteSpace(text)) return; // nothing learned yet → don't write an empty file
                Directory.CreateDirectory(Path.GetDirectoryName(_path));

                // ATOMIC write (same as SettingsStore): temp file + File.Replace so a kill mid-save can't truncate
                // the real file to 0 bytes. Keeps a .bak the load falls back to.
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, text);
                if (File.Exists(_path))
                    File.Replace(tmp, _path, _path + ".bak");
                else
                    File.Move(tmp, _path);
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
                string text = ReadIfNonEmpty(_path) ?? ReadIfNonEmpty(_path + ".bak");
                if (text != null) _tracker.Load(text);
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] interrupt blacklist load failed: " + e.Message);
            }
        }

        private static string ReadIfNonEmpty(string path)
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : text;
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
