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
                if (File.Exists(_path))
                {
                    SettingsSerializer.ApplyText(_settings, File.ReadAllText(_path));
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
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, SettingsSerializer.ToText(_settings));
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] settings save failed: " + e.Message);
            }
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
