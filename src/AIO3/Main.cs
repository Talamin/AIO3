using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIO3.Adapter;
using AIO3.Combat;
using AIO3.Core.Combat;
using AIO3.Core.Engine;
using AIO3.Core.Game;
using AIO3.Core.Rotations.Warrior;
using AIO3.Core.Settings;
using AIO3.Overlay;
using AIO3.Persistence;
using AIO3.Talents;
using robotManager.Helpful;
using wManager.Events;
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
    private TalentTrainer _talentTrainer;
    private InterruptTracker _interrupts;
    private InterruptLearner _interruptLearner;
    private CancellationTokenSource _cts;
    private volatile bool _applyingTalents; // talents run off-thread so they never freeze the rotation

    // Warrior wiring (only class implemented so far).
    private bool _isWarrior;
    private WarriorSettings _warriorSettings;
    private ChoiceSetting _specSetting;
    private WarriorSpec? _activeSpec;

    // Combat distance reported to WRobot. Tunable live via the warrior settings so we can dial in the
    // melee stop distance (default 5, same as the old AIO). Falls back to 5 before settings exist.
    public float Range => _warriorSettings?.CombatRange.Value ?? 5f;

    public void Initialize()
    {
        _isWarrior = _game.PlayerClass == WowClass.Warrior;

        // An earlier build set this global WRobot setting to true; it persists in WRobot's saved config,
        // so just removing that line didn't undo it. With our ObjectManager.Locker the Lua mover can
        // contend and freeze the rotation during combat, so force it back to the default (off).
        wManager.wManagerSetting.CurrentSetting.UseLuaToMove = false;

        if (_isWarrior)
        {
            _warriorSettings = new WarriorSettings();
            _specSetting = new ChoiceSetting("spec", "Spec", WarriorSpecs.Auto, WarriorSpecs.Choices) { Category = "Spec" };

            // Panel/persistence cover the spec selector plus the shared warrior tunables.
            var list = new List<Setting> { _specSetting };
            list.AddRange(_warriorSettings.All);

            string profile = ObjectManager.Me.Name;
            _store = new SettingsStore(string.IsNullOrEmpty(profile) ? "default" : profile, list);
            _store.Load(); // apply persisted values (incl. the saved spec override) before running

            _overlay = new SettingsOverlay("Warrior", list);
            _talentTrainer = new TalentTrainer();

            // Empirical interrupt learner: feeds the tracker from the combat log (the API's
            // interruptible flag is unreliable). Blacklist persists per character.
            _interrupts = new InterruptTracker();
            _interruptLearner = new InterruptLearner(_interrupts, string.IsNullOrEmpty(profile) ? "default" : profile);
            EventsLuaWithArgs.OnEventsLuaStringWithArgs += _interruptLearner.OnCombatLog;

            // Initial engine; Reconcile() in the loop swaps to the actually-resolved spec.
            _activeSpec = WarriorSpec.Fury;
            _engine = new RotationEngine(new SoloFury(_warriorSettings).BuildSteps());
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
            case WarriorSpec.Arms: return new SoloArms(_warriorSettings);
            case WarriorSpec.Protection: return new SoloProtection(_warriorSettings);
            default: return new SoloFury(_warriorSettings);
        }
    }

    private void Loop(CancellationToken token)
    {
        var idleHeartbeat = Stopwatch.StartNew();
        string lastFired = null;
        var sinceLastLog = Stopwatch.StartNew();
        var overlayPoll = Stopwatch.StartNew();
        var reconcile = Stopwatch.StartNew();
        var talentTimer = Stopwatch.StartNew();
        var profileTimer = Stopwatch.StartNew();

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

                // Overlay polling and spec reconcile use Lua but DON'T need the frame lock — running them
                // under it pauses the game's frame (stutter) for ~12 Lua reads. Keep them out of the lock.
                _overlay?.EnsureCreated();
                if (overlayPoll.ElapsedMilliseconds > 400)
                {
                    overlayPoll.Restart();
                    settingsChanged = _overlay != null && _overlay.Poll();
                }
                if (settingsChanged || reconcile.ElapsedMilliseconds > 2000)
                {
                    reconcile.Restart();
                    Reconcile();
                }
                if (settingsChanged)
                    _store?.Save(); // file I/O, outside the lock

                // Hold the frame lock ONLY for the unit snapshot (consistent reads while iterating the
                // object manager). The rotation's per-spell cooldown/known queries and the cast itself
                // don't need it, so they run UNLOCKED — otherwise those slow queries pause the game's
                // frame every tick (the stutter). Don't run while mounted/travelling.
                if (!mounted)
                {
                    CombatContext ctx = null;
                    _game.RunLocked(() => ctx = CombatContext.Capture(_game, _interrupts));

                    if (ctx != null)
                    {
                        // Optional auto target-switching only (off by default; never pulls — product owns the opener).
                        if (_isWarrior && _warriorSettings.AutoSwitchTarget.Value)
                        {
                            IWowUnit desired = TargetSelector.Pick(ctx);
                            if (desired != null && (ctx.Target == null || desired.Guid != ctx.Target.Guid))
                                _game.SetTarget(desired);
                        }

                        fired = _engine.Tick(ctx);
                    }
                }

                // Auto-assign talents out of combat. Runs on a background task because TalentTrainer
                // sleeps between LearnTalent calls — doing it inline froze the whole rotation loop while
                // spending points. Guarded so only one application runs at a time.
                if (_isWarrior && _talentTrainer != null && _activeSpec.HasValue
                    && _warriorSettings.AutoAssignTalents.Value
                    && !_applyingTalents
                    && !_game.PlayerInCombat
                    && talentTimer.ElapsedMilliseconds > 15000)
                {
                    talentTimer.Restart();
                    _applyingTalents = true;
                    WarriorSpec spec = _activeSpec.Value;
                    Task.Factory.StartNew(() =>
                    {
                        try { _talentTrainer.Apply(WarriorTalents.For(spec)); }
                        catch (Exception ex) { Logging.WriteError($"[AIO3] talents: {ex.Message}"); }
                        finally { _applyingTalents = false; }
                    });
                }

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

            // Drain the engine's rolling timing every few seconds (resets the window even when not
            // logging) and log it while the debug toggle is on — a dev aid to spot timing regressions.
            if (profileTimer.ElapsedMilliseconds > 3000)
            {
                profileTimer.Restart();
                string profile = _engine?.DrainProfile();
                if (profile != null && _isWarrior && _warriorSettings.DebugProfiling.Value)
                    Logging.Write($"[AIO3] perf: {profile}");
            }

            // Sleep is at the end but ALWAYS reached (the old OOC spin-loop burned a core here).
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (_interruptLearner != null)
        {
            EventsLuaWithArgs.OnEventsLuaStringWithArgs -= _interruptLearner.OnCombatLog;
            _interruptLearner.Save();
        }
        (_game as IDisposable)?.Dispose(); // unsubscribe the object-manager pulse handler
        Logging.Write("[AIO3] Disposed.");
    }

    public void ShowConfiguration()
    {
        Logging.Write("[AIO3] Settings are configured in-game via the /aio3 overlay.");
    }
}
