using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIO3.Adapter;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Overlay;
using AIO3.Persistence;
using robotManager.Helpful;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;

/// <summary>
/// WRobot entry point (ICustomClass). Selects a rotation by class, then runs a single-threaded
/// loop that builds one CombatContext per tick and lets the engine pick and cast one action.
/// </summary>
public class Main : ICustomClass
{
    private readonly IGameClient _game = new WRobotGameClient();
    private RotationEngine _engine;
    private SettingsOverlay _overlay;
    private SettingsStore _store;
    private CancellationTokenSource _cts;

    public float Range => 5f; // melee; per-spec range refinement comes later

    public void Initialize()
    {
        IRotation rotation = null;

        // Warrior is the only class wired so far. It gets live settings, persistence + an overlay.
        if (_game.PlayerClass == WowClass.Warrior)
        {
            var fury = new SoloFury();
            string profile = ObjectManager.Me.Name;
            _store = new SettingsStore(string.IsNullOrEmpty(profile) ? "default" : profile, fury.Settings);
            _store.Load(); // apply persisted values before the rotation runs
            rotation = fury;
            _overlay = new SettingsOverlay(fury.Name, fury.Settings);
        }

        _engine = new RotationEngine(rotation?.BuildSteps() ?? Array.Empty<RotationStep>());

        if (rotation != null)
            Logging.Write($"[AIO3] Loaded rotation: {rotation.Name}");
        else
            Logging.Write($"[AIO3] No rotation for class {_game.PlayerClass} yet — running idle.");

        _cts = new CancellationTokenSource();
        Task.Factory.StartNew(() => Loop(_cts.Token), _cts.Token);
    }

    private void Loop(CancellationToken token)
    {
        var idleHeartbeat = Stopwatch.StartNew();
        string lastFired = null;
        var sinceLastLog = Stopwatch.StartNew();
        var overlayPoll = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!Conditions.InGameAndConnectedAndProductStartedNotInPause)
                {
                    Thread.Sleep(250);
                    continue;
                }

                bool mounted = _game.PlayerIsMounted;
                RotationStep fired = null;
                bool settingsChanged = false;

                _game.RunLocked(() =>
                {
                    // Build the in-game overlay once, and poll its edits periodically. Runs even
                    // while mounted so settings can be tweaked any time in-world.
                    _overlay?.EnsureCreated();
                    if (overlayPoll.ElapsedMilliseconds > 400)
                    {
                        overlayPoll.Restart();
                        settingsChanged = _overlay != null && _overlay.Poll();
                    }

                    // Don't run the rotation while mounted/travelling (casting would dismount;
                    // WRobot dismounts itself to fight). Mirrors the old code's pervasive !IsMounted.
                    if (!mounted)
                        fired = _engine.Tick(CombatContext.Capture(_game));
                });

                // Persist outside the frame lock (file I/O) when the player changed a setting.
                if (settingsChanged)
                    _store?.Save();

                if (fired != null)
                {
                    // Log each distinct cast (dedupe consecutive identical within 2s to avoid spam).
                    if (fired.Name != lastFired || sinceLastLog.ElapsedMilliseconds > 2000)
                    {
                        Logging.Write($"[AIO3] cast {fired.Name}");
                        lastFired = fired.Name;
                        sinceLastLog.Restart();
                    }
                    idleHeartbeat.Restart();
                }
                else if (idleHeartbeat.ElapsedMilliseconds > 10000)
                {
                    idleHeartbeat.Restart();
                    Logging.Write("[AIO3] idle (no action chosen)");
                }
            }
            catch (Exception e)
            {
                Logging.WriteError($"[AIO3] {e.Message}\n{e.StackTrace}");
            }

            // Sleep is at the end but ALWAYS reached (the old OOC spin-loop burned a core here).
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        Logging.Write("[AIO3] Disposed.");
    }

    public void ShowConfiguration()
    {
        Logging.Write("[AIO3] No settings UI yet.");
    }
}
