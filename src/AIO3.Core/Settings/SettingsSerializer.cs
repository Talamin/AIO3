using System.Collections.Generic;
using System.Text;

namespace AIO3.Core.Settings
{
    /// <summary>
    /// Converts a list of settings to/from a flat "key=value" text format. Pure (no I/O), so the
    /// App layer can persist it however it likes while this stays unit-testable.
    /// </summary>
    public static class SettingsSerializer
    {
        public static string ToText(IEnumerable<Setting> settings)
        {
            var sb = new StringBuilder();
            foreach (Setting s in settings)
                sb.Append(s.Key).Append('=').Append(s.Serialize()).Append('\n');
            return sb.ToString();
        }

        public static void ApplyText(IEnumerable<Setting> settings, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var map = new Dictionary<string, string>();
            foreach (string line in text.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                map[t.Substring(0, eq)] = t.Substring(eq + 1);
            }

            foreach (Setting s in settings)
                if (map.TryGetValue(s.Key, out string raw))
                    s.Deserialize(raw);
        }
    }
}
