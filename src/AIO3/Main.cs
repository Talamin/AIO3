using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIO3.Adapter;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Settings;
using AIO3.Overlay;
using AIO3.Persistence;
using robotManager.Helpful;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;

/// <summary>
/// WRobot entry point (ICustomClass). Resolves the active spec (auto-detected from talents, with a
/// manual override via the overlay) and runs a single-threaded loop that builds one CombatContext per
/// tick and lets the engine cast one action. The active rotation is swapped at runtime when the spec
/// changes (e.g. picking a spec at level 10, or respeccing).
/// </summary>
public class Main : ICustomClass
{
    private readonly IGameClient _game = new WRobotGameClient();
    private RotationEngine _engine;
    private SettingsOverlay _overlay;
    private SettingsStore _store;
    private CancellationTokenSource _cts;

    // Warrior wiring (only class implemented so far).
    private bool _isWarrior;
    private FurySettings _furySettings;
    private ChoiceSetting _specSetting;
    private WarriorSpec? _activeSpec;

    public float Range => 5f; // melee; per-spec range refinement comes later

    public void Initialize()
    {
        _isWarrior = _game.PlayerClass == WowClass.Warrior;

        if (_isWarrior)
        {
            _furySettings = new FurySettings();
            _specSetting = new ChoiceSetting("spec", "Spec", WarriorSpecs.Auto, WarriorSpecs.Choices);

            // Panel/persistence cover the spec selector plus the active spec's tunables.
            var list = new List<Setting> { _specSetting };
            list.AddRange(_furySettings.All);

            string profile = ObjectManager.Me.Name;
            _store = new SettingsStore(string.IsNullOrEmpty(profile) ? "default" : profile, list);
            _store.Load(); // apply persisted values (incl. the saved spec override) before running

            _overlay = new SettingsOverlay("Warrior", list);

            // Initial engine; Reconcile() in the loop swaps to the actually-resolved spec.
            _activeSpec = WarriorSpec.Fury;
            _engine = new RotationEngine(new SoloFury(_furySettings).BuildSteps());
            Logging.Write("[AIO3] Loaded: Warrior (spec auto-selected from talents; /aio3 to override).");
        }
        else
        {
            _engine = new RotationEngine(Array.Empty<RotationStep>());
            Logging.Write($"[AIO3] No rotation for class {_game.PlayerClass} yet — running idle.");
        }

        _cts = new CancellationTokenSource();
        Task.Factory.StartNew(() => Loop(_cts.Token), _cts.Token);
    }

    /// <summary>Resolve the desired warrior spec (override or talent auto-detect) and swap the engine if it changed.</summary>
    private void Reconcile()
    {
        if (!_isWarrior) return;

        WarriorSpec desired = WarriorSpecs.Resolve(_specSetting.Value, _game.HighestTalentTab);
        if (_activeSpec == desired) return;
        _activeSpec = desired;

        IRotation rotation = BuildWarriorRotation(desired);
        _engine = new RotationEngine(rotation.BuildSteps());
        Logging.Write($"[AIO3] Active spec: {desired} ({rotation.Name})");
    }

    private IRotation BuildWarriorRotation(WarriorSpec spec)
    {
        switch (spec)
        {
            case WarriorSpec.Fury:
                return new SoloFury(_furySettings);
            default:
                // Arms / Protection not implemented yet — fall back so the bot still functions.
                Logging.Write($"[AIO3] {spec} rotation not implemented yet — using Solo Fury as fallback.");
                return new SoloFury(_furySettings);
        }
    }

    private void Loop(CancellationToken token)
    {
        var idleHeartbeat = Stopwatch.StartNew();
        string lastFired = null;
        var sinceLastLog = Stopwatch.StartNew();
        var overlayPoll = Stopwatch.StartNew();
        var reconcile = Stopwatch.StartNew();

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

                    // Re-resolve the spec immediately after a manual change, and periodically for
                    // talent auto-detect (e.g. choosing a spec at level 10 / respeccing).
                    if (settingsChanged || reconcile.ElapsedMilliseconds > 2000)
                    {
                        reconcile.Restart();
                        Reconcile();
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
        Logging.Write("[AIO3] Settings are configured in-game via the /aio3 overlay.");
    }
}
