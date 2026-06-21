using System;
using System.IO;
using robotManager.Helpful;

/// <summary>
/// Appends timestamped diagnostics to a file on disk (WRobot/Settings/AIO3/debug.log) so the fightclass'
/// behaviour — casts, the backpedal's real movement, etc. — can be inspected directly off disk instead of
/// scraped out of the in-game log window. Gated by the "Debug logging" toggle (Main sets <see cref="Enabled"/>);
/// a cheap no-op when off, and never throws (logging must never break the rotation).
/// </summary>
internal static class DebugLog
{
    public static volatile bool Enabled;

    private static readonly object Gate = new object();
    private static string _path;

    private static string Path_
    {
        get
        {
            if (_path == null)
                _path = Path.Combine(Others.GetCurrentDirectory, "Settings", "AIO3", "debug.log");
            return _path;
        }
    }

    /// <summary>Start a fresh log for the session (truncates the file) and writes a header.</summary>
    public static void StartSession(string header)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path_));
                File.WriteAllText(Path_, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  === " + header + " ===\n");
            }
        }
        catch { }
    }

    public static void Write(string line)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path_));
                File.AppendAllText(Path_, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + "\n");
            }
        }
        catch { }
    }
}
