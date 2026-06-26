using System;
using System.Collections.Generic;
using System.IO;
using AIO3.Core.Settings;
using robotManager.Helpful;

namespace AIO3.Persistence
{
    /// <summary>
    /// Persists a rotation's settings to a small text file under the WRobot Settings folder,
    /// one file per profile (character). Core does the (testable) serialization; this only does I/O.
    /// </summary>
    internal sealed class SettingsStore
    {
        private readonly IReadOnlyList<Setting> _settings;
        private readonly string _path;

        public SettingsStore(string profile, IReadOnlyList<Setting> settings)
        {
            _settings = settings;
            string dir = Path.Combine(Others.GetCurrentDirectory, "Settings", "AIO3");
            _path = Path.Combine(dir, Sanitize(profile) + ".conf");
        }

        public void Load()
        {
            try
            {
                // Prefer the live file; fall back to the .bak that Save() keeps, so a crash that truncated the
                // main file (the disconnect-then-WRobot-killed data loss) still recovers the last good settings.
                string text = ReadIfNonEmpty(_path) ?? ReadIfNonEmpty(_path + ".bak");
                if (text != null)
                {
                    SettingsSerializer.ApplyText(_settings, text);
                    Logging.Write($"[AIO3] Loaded settings from {_path}");
                }
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] settings load failed: " + e.Message);
            }
        }

        public void Save()
        {
            try
            {
                string text = SettingsSerializer.ToText(_settings);
                // Never overwrite a good file with nothing (guards against a serializer returning empty).
                if (string.IsNullOrWhiteSpace(text)) return;

                Directory.CreateDirectory(Path.GetDirectoryName(_path));

                // ATOMIC save: write a temp file, then swap it in. File.WriteAllText alone truncates the real file
                // to 0 bytes BEFORE writing, so a kill mid-write (a server disconnect closing WRobot) left the
                // .conf empty and every setting reset to default on the next load. File.Replace is atomic on NTFS
                // and keeps a .bak; if anything throws, the previous file stays intact (no data loss).
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, text);
                if (File.Exists(_path))
                    File.Replace(tmp, _path, _path + ".bak");
                else
                    File.Move(tmp, _path);
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] settings save failed: " + e.Message);
            }
        }

        private static string ReadIfNonEmpty(string path)
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static string Sanitize(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return "default";
            foreach (char c in Path.GetInvalidFileNameChars())
                profile = profile.Replace(c, '_');
            return profile;
        }
    }
}
