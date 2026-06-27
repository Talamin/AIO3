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
using AIO3.Core.Rotations;
using AIO3.Core.Settings;
using AIO3.Overlay;
using AIO3.Persistence;
using AIO3.Talents;
using robotManager.Helpful;
using wManager.Events;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;

/// <summary>
/// WRobot entry point (ICustomClass). Picks the class module for the player's class (Warrior, Paladin, …),
/// wires its settings into the overlay/persistence, and runs a single-threaded loop that builds one
/// CombatContext per tick and lets the engine cast one action. The active rotation is swapped at runtime
/// when the module re-resolves it (e.g. picking a spec at level 10, respeccing, or changing the mode).
/// Main itself is class-agnostic — all class-specific logic lives behind <see cref="IClassModule"/>.
/// </summary>
public class Main : ICustomClass
{
    private readonly IGameClient _game = new WRobotGameClient();
    private RotationEngine _engine;
    private SettingsOverlay _overlay;
    private NativeOverlay _nativeOverlay; // native window over the game; the Lua panel is the fallback
    private int _overlayStart;            // when the overlays were created (for the native-vs-Lua fallback grace)
    private const int OverlayFallbackMs = 2000; // if the native overlay isn't up by now, fall back to the Lua panel
    private SettingsStore _store;
    private TalentTrainer _talentTrainer;
    private bool _lastManageFood;             // last applied "use best bag food/drink" state (apply only on change)
    private int _lastCastTick;                // Environment.TickCount of the previous cast (for the debug cast-gap)
    private InterruptTracker _interrupts;
    private InterruptLearner _interruptLearner;
    private DamageTracker _damageTracker;     // measure-only for now: learns per-ability damage from the log
    private DamageLearner _damageLearner;
    private CancellationTokenSource _cts;
    private volatile bool _applyingTalents; // talents run off-thread so they never freeze the rotation

    // The active class implementation (null when the player's class isn't implemented yet → idle).
    private IClassModule _class;
    private IRotation _activeRotation;

    // Combat distance reported to WRobot. Tunable live via the class module (e.g. the warrior Combat range
    // slider) so we can dial in the melee stop distance. Falls back to 5 before a module exists.
    public float Range => _class?.Range ?? 5f;

    public void Initialize()
    {
        // An earlier build set this global WRobot setting to true; it persists in WRobot's saved config,
        // so just removing that line didn't undo it. With our ObjectManager.Locker the Lua mover can
        // contend and freeze the rotation during combat, so force it back to the default (off).
        wManager.wManagerSetting.CurrentSetting.UseLuaToMove = false;

        // Damage learning (measure-only): record per-ability damage from the combat log for every class.
        _damageTracker = new DamageTracker();
        _damageLearner = new DamageLearner(_damageTracker);
        EventsLuaWithArgs.OnEventsLuaStringWithArgs += _damageLearner.OnCombatLog;

        _class = ClassModules.For(_game.PlayerClass, _game);
        if (_class != null)
        {
            // Panel/persistence cover the module's full setting set (spec/mode selectors first).
            IReadOnlyList<Setting> list = _class.Settings;

            string profile = ObjectManager.Me.Name;
            if (string.IsNullOrEmpty(profile)) profile = "default";
            _store = new SettingsStore(profile, list);
            _store.Load(); // apply persisted values (incl. the saved spec override) before running

            // Pass the live active spec so the overlay hides settings tagged for other specs (and rebuilds when
            // the spec changes). _class is captured; ActiveSpec updates as the module resolves the spec.
            _overlay = new SettingsOverlay(_class.DisplayName, list, () => _class.ActiveSpec);
            // Native over-the-game overlay (Phase 1, in PARALLEL with the Lua panel as a fallback). Self-guards on
            // its own STA thread; if WPF can't start in this byte[]-loaded fightclass it logs and we keep the panel.
            try { _nativeOverlay = new NativeOverlay(_class.DisplayName, list, () => _class.ActiveSpec); }
            catch (Exception e) { Logging.WriteError("[AIO3] native overlay init failed: " + e.Message); }
            _overlayStart = Environment.TickCount;
            _talentTrainer = new TalentTrainer();

            // Empirical interrupt learner: feeds the tracker from the combat log (the API's
            // interruptible flag is unreliable). Blacklist persists per character.
            _interrupts = new InterruptTracker();
            _interruptLearner = new InterruptLearner(_interrupts, profile);
            EventsLuaWithArgs.OnEventsLuaStringWithArgs += _interruptLearner.OnCombatLog;

            // Initial engine; Reconcile() in the loop swaps to the actually-resolved spec/mode.
            _activeRotation = _class.ResolveRotation(_game.HighestTalentTab);
            _engine = new RotationEngine(_activeRotation.BuildSteps());
            Logging.Write($"[AIO3] Loaded: {_class.DisplayName} (spec auto-selected from talents; /aio3 to override).");
        }
        else
        {
            _engine = new RotationEngine(Array.Empty<RotationStep>());
            Logging.Write($"[AIO3] No rotation for class {_game.PlayerClass} yet — running idle.");
        }

        DebugLog.StartSession(_class != null ? _class.DisplayName : ("idle (" + _game.PlayerClass + ")"));

        _cts = new CancellationTokenSource();
        Task.Factory.StartNew(() => Loop(_cts.Token), _cts.Token);
    }

    /// <summary>Re-resolve the rotation (spec + mode) via the class module and swap the engine if it changed.</summary>
    private void Reconcile()
    {
        if (_class == null) return;

        IRotation rotation = _class.ResolveRotation(_game.HighestTalentTab);
        if (ReferenceEquals(rotation, _activeRotation)) return;
        _activeRotation = rotation;
        _engine = new RotationEngine(rotation.BuildSteps());
        Logging.Write($"[AIO3] Active: {_class.ActiveLabel} ({rotation.Name})");
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
                bool dead = _game.PlayerIsDeadOrGhost; // dead/ghost: the product runs the corpse, we do nothing
                RotationStep fired = null;
                bool settingsChanged = false;

                // Mirror the "Debug logging" toggle to the on-disk debug log (read directly off disk for
                // diagnosing behaviour like the backpedal, instead of scraping the in-game log window).
                DebugLog.Enabled = _class != null && _class.DebugLoggingEnabled;

                // Apply the class's food/drink preference to WRobot, but only when it changes (it writes a
                // wManager setting). A conjuring class (mage) turns on "use best bag food/drink" so WRobot eats
                // what it conjured; other classes leave it to the vendor plugin.
                bool manageFood = _class != null && _class.ManageBagFoodDrink;
                if (manageFood != _lastManageFood)
                {
                    _game.SetManageBagFoodDrink(manageFood);
                    _lastManageFood = manageFood;
                }

                // Overlay polling and spec reconcile use Lua but DON'T need the frame lock — running them
                // under it pauses the game's frame (stutter) for ~12 Lua reads. Keep them out of the lock.
                // ONE overlay at a time: while the native overlay is up, suppress the Lua panel — running both
                // lets the Lua Poll() write its (stale) bridge value back over a native edit, which reverted edits
                // and re-saved the old value. Fall back to the Lua panel only if native isn't up (with a short
                // startup grace so the panel doesn't flash before the window appears).
                bool nativeUp = _nativeOverlay != null && _nativeOverlay.IsActive;
                bool useLuaPanel = !nativeUp &&
                                   (_nativeOverlay == null || unchecked(Environment.TickCount - _overlayStart) > OverlayFallbackMs);
                if (useLuaPanel)
                {
                    _overlay?.EnsureCreated();
                    if (overlayPoll.ElapsedMilliseconds > 400)
                    {
                        overlayPoll.Restart();
                        settingsChanged = _overlay != null && _overlay.Poll();
                    }
                }
                // Native overlay edits bind straight into the Setting objects; this just picks up "something
                // changed" so we Reconcile (spec) + Save the same way a Lua-panel edit does. Cheap (a bool read).
                if (_nativeOverlay != null && _nativeOverlay.TakeDirty())
                    settingsChanged = true;
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
                // Drive any in-progress backpedal hop (holds the key down smoothly, releases at the end) and
                // pause casting while it plays out — no per-tick re-pressing (which jerked) and no blocking.
                bool repositioning = _game.ServiceReposition();

                if (!mounted && !repositioning && !dead)
                {
                    CombatContext ctx = null;
                    _game.RunLocked(() =>
                    {
                        ctx = CombatContext.Capture(_game, _interrupts, _damageTracker);
                        // Warm the adapter's by-entry creature-type cache for the current target while we hold the
                        // frame lock. The type is read live only for the WoW target, so without this it's cached
                        // only when some ability's condition reads it mid-fight -- which a Combat rogue never does
                        // (its only reader, Rupture, is off by default). An empty cache makes corpse creature-type
                        // lookups fail, so the Undead racial Cannibalize could never find a Humanoid/Undead corpse.
                        if (ctx?.Target != null) { _ = ctx.Target.CreatureType; }
                    });

                    if (ctx != null)
                    {
                        // Optional auto target-switching only (off by default; never pulls — product owns the opener).
                        if (_class != null && _class.AutoSwitchTargetEnabled)
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
                if (_class != null && _talentTrainer != null
                    && !_applyingTalents
                    && !_game.PlayerInCombat
                    && !dead
                    && talentTimer.ElapsedMilliseconds > 15000)
                {
                    string[] build = _class.DesiredTalentBuild();
                    if (build != null)
                    {
                        talentTimer.Restart();
                        _applyingTalents = true;
                        Task.Factory.StartNew(() =>
                        {
                            try { _talentTrainer.Apply(build); }
                            catch (Exception ex) { Logging.WriteError($"[AIO3] talents: {ex.Message}"); }
                            finally { _applyingTalents = false; }
                        });
                    }
                }

                if (fired != null)
                {
                    int gap = unchecked(Environment.TickCount - _lastCastTick); // ms since the previous cast
                    _lastCastTick = Environment.TickCount;
                    DebugLog.Write($"cast {fired.Name} (+{gap}ms)"); // full trace on disk (not deduped); +gap = cast interval

                    // Log each distinct cast (dedupe consecutive identical within 2s to avoid spam).
                    if (fired.Name != lastFired || sinceLastLog.ElapsedMilliseconds > 2000)
                    {
                        Logging.Write($"[AIO3] cast {fired.Name}");
                        lastFired = fired.Name;
                        sinceLastLog.Restart();
                    }
                    idleHeartbeat.Restart();
                }
                else if (!dead && idleHeartbeat.ElapsedMilliseconds > 10000)
                {
                    idleHeartbeat.Restart();
                    Logging.Write("[AIO3] idle (no action chosen)");
                }
            }
            catch (Exception e)
            {
                Logging.WriteError($"[AIO3] {e.Message}\n{e.StackTrace}");
            }

            // Every few seconds drain the engine's rolling timing (resets the window even when not
            // logging); while the debug toggle is on, log it plus the learned per-ability damage.
            if (profileTimer.ElapsedMilliseconds > 3000)
            {
                profileTimer.Restart();
                string profile = _engine?.DrainProfile();
                if (_class != null && _class.DebugLoggingEnabled)
                {
                    if (profile != null) Logging.Write($"[AIO3] perf: {profile}");
                    string dmg = _damageTracker?.Report();
                    if (dmg != null) Logging.Write($"[AIO3] {dmg}");
                }
            }

            // Sleep is at the end but ALWAYS reached (the old OOC spin-loop burned a core here).
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (_damageLearner != null)
            EventsLuaWithArgs.OnEventsLuaStringWithArgs -= _damageLearner.OnCombatLog;
        if (_interruptLearner != null)
        {
            EventsLuaWithArgs.OnEventsLuaStringWithArgs -= _interruptLearner.OnCombatLog;
            _interruptLearner.Save();
        }
        _nativeOverlay?.Dispose(); // close the overlay window + end its STA thread
        (_game as IDisposable)?.Dispose(); // unsubscribe the object-manager pulse handler
        Logging.Write("[AIO3] Disposed.");
    }

    public void ShowConfiguration()
    {
        Logging.Write("[AIO3] Settings are configured in-game via the /aio3 overlay.");
    }
}
