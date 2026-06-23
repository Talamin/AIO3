using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using AIO3.Core.Game;
using robotManager.Helpful;
using wManager.Events;
using wManager.Wow.Class;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;
using WReaction = wManager.Wow.Enums.Reaction;
using HitFlags = wManager.Wow.Enums.CGWorldFrameHitFlags;

namespace AIO3.Adapter
{
    /// <summary>
    /// Layer 0 — the only class that talks to wManager. Implements <see cref="IGameClient"/>
    /// so everything above it stays WRobot-agnostic and testable.
    /// </summary>
    internal sealed class WRobotGameClient : IGameClient, IDisposable
    {
        private const float ScanRange = 50f;

        private readonly Dictionary<string, Spell> _spellCache = new Dictionary<string, Spell>();
        private readonly WRobotCooldowns _cooldowns = new WRobotCooldowns();

        // Enemy/party lists are rebuilt on WRobot's ObjectManager pulse (~100ms) rather than every tick:
        // the underlying unit data only refreshes on that pulse, so per-tick rebuilds were wasted work.
        // The reference swap is atomic and the field is volatile, so the tick thread always reads a
        // complete list. Wrappers are live, so unit properties (distance/auras) stay current between pulses.
        private volatile IReadOnlyList<IWowUnit> _enemiesCache = Array.Empty<IWowUnit>();
        private volatile IReadOnlyList<IWowUnit> _partyCache = Array.Empty<IWowUnit>();
        private readonly Stopwatch _sinceRebuild = Stopwatch.StartNew();
        private bool _rebuiltOnce;

        // Cheap caches for the handful of per-tick Lua reads — WRobot's Lua.LuaDoString costs ~15-40ms
        // each and the rotation calls these every tick (stance, auto-attack, IsCurrentSpell, IsSpellUsable
        // on fire-and-fail). Short TTLs keep them correct; only the rotation loop touches them (no locks).
        private const int LuaCacheMs = 150;
        private string _stance;
        private int _stanceAt;
        private bool _autoAttacking, _autoAttackingKnown;
        private int _autoAttackingAt;
        private int _itemScanAt;
        private readonly Dictionary<string, (bool value, int at)> _usableCache = new Dictionary<string, (bool, int)>();
        private readonly Dictionary<string, (bool value, int at)> _currentSpellCache = new Dictionary<string, (bool, int)>();

        // Pet action-bar cache. The bar is stable per pet, so we scan it once per pet (refreshed on a pet
        // change or a short TTL) and answer PetHasAbility from the set — keeping the "pet lacks this
        // ability" path cheap (a HashSet lookup, no per-tick Lua) so an absent taunt is handled for free.
        private ulong _petBarGuid;
        private int _petBarAt;
        private HashSet<string> _petAbilities = new HashSet<string>();

        // Pet abilities currently OFF cooldown — one Lua scan per ~500ms (not per ability) so a step can gate
        // a pet-ability cast on readiness without spamming Lua while it recharges.
        private int _petReadyAt;
        private HashSet<string> _petReady = new HashSet<string>();

        // A backpedal hop runs on its own worker thread (see StepBack); true while it holds the back key.
        private volatile bool _repositioning;

        private static int Now => Environment.TickCount;

        public WRobotGameClient()
        {
            ObjectManagerEvents.OnObjectManagerPulsed += OnObjectManagerPulsed;
            wManager.Events.FightEvents.OnFightLoop += OnFightLoop; // backpedal runs here (see OnFightLoop)
        }

        public void Dispose()
        {
            ObjectManagerEvents.OnObjectManagerPulsed -= OnObjectManagerPulsed;
            wManager.Events.FightEvents.OnFightLoop -= OnFightLoop;
        }

        // Runs on WRobot's object-manager thread after each pulse. Must never throw (would break the OM).
        private void OnObjectManagerPulsed()
        {
            // Cap the rebuild rate in case the pulse is faster than ~100ms; no point going quicker.
            if (_rebuiltOnce && _sinceRebuild.ElapsedMilliseconds < 80) return;
            _sinceRebuild.Restart();
            _rebuiltOnce = true;

            try
            {
                var enemies = new List<IWowUnit>();
                foreach (WoWUnit u in ObjectManager.GetObjectWoWUnit())
                {
                    if (u.IsValid && u.IsAlive && u.IsAttackable
                        && u.Reaction <= WReaction.Neutral
                        && u.GetDistance <= ScanRange)
                    {
                        enemies.Add(new WRobotUnit(u));
                    }
                }
                _enemiesCache = enemies;

                var me = ObjectManager.Me;
                var party = new List<IWowUnit> { new WRobotUnit(me) };
                // Raid-aware: GetParty() is 5-man only; use the raid roster when in a raid.
                List<WoWPlayer> group = wManager.Wow.Helpers.Party.GetRaidMemberCount() > 0
                    ? wManager.Wow.Helpers.Party.GetRaidMembers()
                    : wManager.Wow.Helpers.Party.GetParty();
                foreach (WoWPlayer p in group)
                {
                    if (p.Guid != me.Guid)
                        party.Add(new WRobotUnit(p));
                }
                _partyCache = party;
            }
            catch
            {
                // Ignore a bad pulse (e.g. mid zone transition); the next pulse refreshes the lists.
            }
        }

        private Spell GetSpell(string name)
        {
            if (!_spellCache.TryGetValue(name, out Spell spell))
            {
                spell = new Spell(name);
                _spellCache[name] = spell;
            }
            return spell;
        }

        public IWowUnit Me => new WRobotUnit(ObjectManager.Me);

        public WowClass PlayerClass =>
            System.Enum.TryParse(ObjectManager.Me.WowClass.ToString(), out WowClass c) ? c : WowClass.None;

        public int HighestTalentTab =>
            Lua.LuaDoString<int>(
                "local best,bp=0,-1 " +
                "for i=1,GetNumTalentTabs() do local _,_,p=GetTalentTabInfo(i) if p and p>bp then bp=p best=i end end " +
                "if bp<=0 then return 0 end return best");

        public string ActiveStanceName
        {
            get
            {
                // Stance changes only on a (deliberate) shapeshift — cache it; Cast invalidates this when
                // it casts a stance, so a stance dance still reacts immediately.
                if (_stance != null && unchecked(Now - _stanceAt) < 250) return _stance;
                _stance = Lua.LuaDoString<string>(
                    "for i=1,10 do local _, name, isActive = GetShapeshiftFormInfo(i); " +
                    "if name ~= nil and isActive then return name; end end return '';") ?? "";
                _stanceAt = Now;
                return _stance;
            }
        }

        public IWowUnit Target
        {
            get
            {
                WoWUnit t = ObjectManager.Target;
                return (t != null && t.IsValid && t.Guid != 0) ? new WRobotUnit(t) : null;
            }
        }

        public IWowUnit Pet
        {
            get
            {
                // IsValid distinguishes "have a pet" (even when dead) from "no pet" — key on existence,
                // never on level. A dead pet returns a (non-alive) wrapper so the controller can revive it.
                WoWUnit p = ObjectManager.Pet;
                return (p != null && p.IsValid && p.Guid != 0) ? new WRobotUnit(p) : null;
            }
        }

        // Rebuilt on the object-manager pulse (see OnObjectManagerPulsed), not per tick.
        public IReadOnlyList<IWowUnit> Enemies => _enemiesCache;

        public IReadOnlyList<IWowUnit> Party => _partyCache;

        public bool IsSpellKnown(string spell) => GetSpell(spell).KnownSpell;

        public float SpellRange(string spell) => GetSpell(spell).MaxRange;

        public bool IsCurrentSpell(string spell)
        {
            if (_currentSpellCache.TryGetValue(spell, out var c) && unchecked(Now - c.at) < LuaCacheMs) return c.value;
            bool v = Lua.LuaDoString<bool>($"return IsCurrentSpell('{spell}') == 1 or IsCurrentSpell('{spell}') == true");
            _currentSpellCache[spell] = (v, Now);
            return v;
        }

        public bool IsSpellReady(string spell)
        {
            Spell s = GetSpell(spell);
            if (!s.KnownSpell) return false;
            // Cooldown comes from one per-tick memory walk (WRobotCooldowns) instead of a slow ~30ms
            // SpellManager call per spell. Usability (rage/stance/range) is handled by the step's When
            // predicate, the DSL range gate, and Cast itself — so it is not re-checked here.
            return _cooldowns.CooldownLeftMs((uint)s.Id) <= 0;
        }

        public int GlobalCooldownRemainingMs => _cooldowns.GcdRemainingMs;

        public bool PlayerIsCasting => ObjectManager.Me.IsCast;

        public bool PlayerIsMoving => ObjectManager.Me.GetMove;

        public bool PlayerIsMounted => ObjectManager.Me.IsMounted;

        public bool PlayerInCombat => ObjectManager.Me.InCombat;

        private bool _deadCached;
        private int _deadCheckedAt;
        // Me.IsDead catches the corpse; a GHOST (released, running to the corpse) still has health, so IsDead is
        // false there — UnitIsDeadOrGhost covers both. Cache 250ms so this Lua read doesn't run every 50ms tick
        // (death state changes slowly; a quarter-second of latency on revive is irrelevant).
        public bool PlayerIsDeadOrGhost
        {
            get
            {
                if (unchecked(Now - _deadCheckedAt) < 250) return _deadCached;
                _deadCheckedAt = Now;
                _deadCached = ObjectManager.Me.IsDead
                    || Lua.LuaDoString<bool>("return UnitIsDeadOrGhost('player') == 1");
                return _deadCached;
            }
        }

        private bool _swimCached;
        private int _swimCheckedAt;
        // Swimming is read via the IsSwimming() Lua API (no reliable memory flag in this build), cached 250ms so
        // it doesn't run every 50ms tick. Entering/leaving water a quarter-second late doesn't matter for the kite.
        public bool PlayerIsSwimming
        {
            get
            {
                if (unchecked(Now - _swimCheckedAt) < 250) return _swimCached;
                _swimCheckedAt = Now;
                _swimCached = Lua.LuaDoString<bool>("return IsSwimming() == 1");
                return _swimCached;
            }
        }

        // True movement-root bit. GetMovementFlag reads the MovementInfo block (scout-verified: flags field at
        // +0x38, MOVEMENTFLAG_ROOT = 0x800). NOT WoWUnit.Rooted — that aliases UnitFlags.Influenced (0x4), which
        // does NOT flip for Frost Nova / Entangling Roots / a net, so it's useless here. Cheap memory read, no Lua.
        public bool PlayerIsRooted => ObjectManager.Me.GetMovementFlag(0x38, 0x800);

        private HashSet<string> _debuffTypes = new HashSet<string>();
        private int _debuffTypesAt;
        // One Lua scan of the player's debuffs per ~250ms, caching the set of dispel types present. UnitDebuff's
        // 5th return is the debuffType ("Poison"/"Disease"/"Magic"/"Curse") — scout-verified for 3.3.5a.
        public bool PlayerHasDebuffType(string dispelType)
        {
            if (unchecked(Now - _debuffTypesAt) >= 250)
            {
                _debuffTypesAt = Now;
                string joined = Lua.LuaDoString<string>(
                    "local t='' for i=1,40 do local _,_,_,_,d=UnitDebuff('player',i) " +
                    "if d and d~='' then t=t..d..';' end end return t");
                var set = new HashSet<string>();
                if (!string.IsNullOrEmpty(joined))
                    foreach (string p in joined.Split(';'))
                        if (p.Length > 0) set.Add(p);
                _debuffTypes = set;
            }
            return _debuffTypes.Contains(dispelType);
        }

        // A Humanoid/Undead corpse within 8yd for Cannibalize. GetObjectWoWUnit() includes dead units (scout-
        // verified); IsDead/Entry/GetDistance all resolve on them. The creature type comes from our per-entry
        // cache (populated when the mob was alive + our target), so a fresh unfought corpse simply won't match.
        public bool HasCannibalizeCorpseNearby() =>
            ObjectManager.GetObjectWoWUnit().Any(u => u.IsDead && u.GetDistance <= 8f
                && WRobotUnit.CachedCreatureTypeIs(u.Entry, "Humanoid", "Undead"));

        // WRobot's own fight state — true throughout a fight including the approach. Mirrors how the old
        // AIO gated its combat rotation, so we only act when the product has committed to a target.
        public bool ProductIsFighting => Fight.InFight;

        public bool PlayerIsAutoAttacking
        {
            get
            {
                if (_autoAttackingKnown && unchecked(Now - _autoAttackingAt) < 400) return _autoAttacking;
                _autoAttacking = Lua.LuaDoString<bool>(
                    "return IsCurrentSpell('Auto Attack') == 1 or IsCurrentSpell('Auto Attack') == true");
                _autoAttackingKnown = true;
                _autoAttackingAt = Now;
                return _autoAttacking;
            }
        }

        // Cached IsSpellUsable (Lua). Proc abilities (Victory Rush/Revenge/Overpower) reach Cast every
        // tick and fail here when not usable, so caching this avoids a per-tick Lua call per such step.
        private bool IsUsable(Spell s)
        {
            if (_usableCache.TryGetValue(s.Name, out var c) && unchecked(Now - c.at) < LuaCacheMs) return c.value;
            bool v = s.IsSpellUsable;
            _usableCache[s.Name] = (v, Now);
            return v;
        }

        // WoW's spell-queue window (ms) — casts issued within this much of a cast's end get queued for seamless
        // chaining. Read once from the CVar (rarely changes); default 400. We cast inside it, see Cast().
        private int _queueWindowMs = -1;
        private int QueueWindowMs
        {
            get
            {
                if (_queueWindowMs < 0)
                {
                    try { _queueWindowMs = Lua.LuaDoString<int>("return tonumber(GetCVar('SpellQueueWindow')) or 400"); }
                    catch { _queueWindowMs = 400; }
                    if (_queueWindowMs <= 0) _queueWindowMs = 400; // a 0/disabled CVar -> still chain at the default
                }
                return _queueWindowMs;
            }
        }

        public CastResult Cast(string spell, IWowUnit target, bool force = false)
        {
            if (!(target is WRobotUnit wTarget)) return CastResult.NoTarget;
            WoWUnit unit = wTarget.Inner;
            Spell s = GetSpell(spell);

            if (unit == null || !unit.IsValid || unit.IsDead) return CastResult.NoTarget;
            if (!s.KnownSpell) return CastResult.NotKnown;
            if (!IsUsable(s)) return CastResult.NotUsable;
            if (s.CastTime > 0.0 && ObjectManager.Me.GetMove) return CastResult.Moving;
            if (!force && ObjectManager.Me.IsCast)
            {
                // Don't restart an in-progress cast — BUT allow the next cast inside the spell-queue window (the
                // last ~SpellQueueWindow ms): WoW then QUEUES it so casts chain seamlessly, exactly what manual
                // spamming does. Without this we waited for the cast to fully finish and lost the loop-cadence gap
                // (~80-125ms) every cast. CastingTimeLeft is 0 during a CHANNEL (not covered by it), so a channel
                // (Evocation / Arcane Missiles / Drain Life) stays blocked here and we never clip it.
                int castLeft = (int)ObjectManager.Me.CastingTimeLeft;
                if (!(castLeft > 0 && castLeft <= QueueWindowMs)) return CastResult.Busy;
                DebugLog.Write($"queue {s.Name} ({castLeft}ms left on the current cast)"); // queue-window cast → WoW chains it
            }

            if (force) Lua.LuaDoString("SpellStopCasting();");

            // A self target casts with NO unit (plain "/cast X"): self-buffs (armor, Arcane Intellect, Icy
            // Veins, Evocation) auto-apply to the player, and no-target PBAoE/instants (Frost Nova, Arcane
            // Explosion, Blizzard) only cast this way — casting them "on player" fails (they have no friendly
            // target type), which is why Frost Nova silently never fired. Other units still use the targeted cast.
            string unitId = LuaUnitId(unit);
            if (unitId == "player") SpellManager.CastSpellByNameLUA(s.Name);
            else SpellManager.CastSpellByNameOn(s.Name, unitId);

            // Keep the caches honest right after we change state ourselves.
            if (spell == "Auto Attack") { _autoAttacking = true; _autoAttackingKnown = true; _autoAttackingAt = Now; }
            if (spell.EndsWith("Stance")) { _stance = null; _usableCache.Clear(); } // stance affects usability
            if (spell.StartsWith("Conjure")) _itemCountCache.Clear(); // we just changed a bag count
            return CastResult.Success;
        }

        public bool UseFirstReadyItem(IReadOnlyList<string> names)
        {
            // Throttle the bag scan: it runs every tick while low on HP (the emergency-item step keeps
            // firing) and Bag.GetBagItem() is ~15-20ms. Items have long cooldowns, so ~750ms is plenty.
            if (unchecked(Now - _itemScanAt) < 750) return false;
            _itemScanAt = Now;

            foreach (WoWItem item in Bag.GetBagItem())
            {
                if (!names.Contains(item.Name)) continue;
                if (IsItemOnCooldown(item.Entry)) continue;
                ItemsManager.UseItemByNameOrId(item.Name);
                return true;
            }
            return false;
        }

        private static bool IsItemOnCooldown(int entry) =>
            Lua.LuaDoString<bool>(
                $"local s,d = GetItemCooldown({entry}); if d and d > 0 and (GetTime()-s) < d then return true else return false end");

        private static string LuaUnitId(WoWUnit unit)
        {
            if (unit.Guid == ObjectManager.Me.Guid) return "player";
            WoWUnit pet = ObjectManager.Pet;
            if (pet != null && pet.IsValid && unit.Guid == pet.Guid) return "pet"; // e.g. Mend Pet
            WoWUnit target = ObjectManager.Target;
            if (target != null && unit.Guid == target.Guid) return "target";
            ObjectManager.Me.FocusGuid = unit.Guid;
            return "focus";
        }

        public void SetTarget(IWowUnit unit)
        {
            // Verified API: WoWUnit.Target is a settable ulong (GUID) on the local player that performs
            // the actual in-game target switch, so WRobot's facing/movement follows the new target.
            if (unit != null && unit.Guid != 0) ObjectManager.Me.Target = unit.Guid;
        }

        public void SetManageBagFoodDrink(bool on)
        {
            // TryToUseBestBagFoodDrink makes WRobot eat/drink the best food/water it finds in the bags (the items
            // we conjure), instead of the named vendor item in FoodName/DrinkName. RestingMana makes it drink to
            // refill mana. Set in memory only (no Save) — same as UseLuaToMove in Initialize; we re-assert on load.
            var s = wManager.wManagerSetting.CurrentSetting;
            s.TryToUseBestBagFoodDrink = on;
            if (on)
            {
                s.RestingMana = true; // a caster drinks to refill mana, not just to heal

                // Wipe any food/drink NAME WRobot still has configured. The Wholesome Vendors plugin likes to write
                // a vendor food name here (e.g. "Freshly Baked Bread") that a mage doesn't carry, which overrides
                // the conjured food. Clearing it once on start removes that conflict so WRobot falls back cleanly to
                // the best conjured food/water in the bags (TryToUseBestBagFoodDrink). Memory-only, like the rest.
                s.FoodName = "";
                s.DrinkName = "";
            }
            DebugLog.Write($"Regen: TryToUseBestBagFoodDrink={on}"
                + (on ? " + RestingMana=true + cleared Food/DrinkName" : ""));
        }

        private volatile float _pendingBackYards; // hand-off to the fight-loop thread (set here, consumed there)
        private int _backpedalReadyAt;            // cooldown so we don't backpedal-spam (casting resumes between hops)
        private const float MaxSafeBackstepDrop = 12f; // refuse a backstep only if the floor behind drops more than
                                                       // this (a real cliff); gentle slopes are fine to back down

        /// <summary>Request a backpedal of <paramref name="yards"/>. This does NOT move here — it validates
        /// (cliff guard + cooldown) on the rotation thread and hands the request to the fight-loop thread, which
        /// is the ONLY place a backpedal works: WRobot's fight loop keeps re-pathing us into combat range and its
        /// StopMove brake releases our key every ~80ms, so a keypress on any other thread is stomped (the char
        /// moved ~1yd despite the key being held the full duration). Returns true when the request is accepted
        /// (the step "fired"); the actual hold happens in OnFightLoop.</summary>
        public bool StepBack(float yards)
        {
            if (_repositioning) { StepSkipLog("hop in progress"); return false; }            // a hop is already running
            if (unchecked(Now - _backpedalReadyAt) < 0) { StepSkipLog("throttled"); return false; } // let other steps cast

            var me = ObjectManager.Me;
            Vector3 pos = me.Position;
            // The spot we'd back into (Rotation is the facing in radians; +PI = directly behind).
            Vector3 dest = pos.InFrontOf(me.Rotation + (float)System.Math.PI, yards);

            // Cliff guard: find the FLOOR behind us and refuse only a genuine drop/void, never ordinary ground.
            // TraceLineGo returns true when it HITS (scout-verified), and the out point is the floor's real world
            // position. Use the floor-only flag (HitTestGround — walls/WMOs don't fool it) and a generous window
            // (well above to well below, so any normal slope is caught; WRobot also biases both endpoints +1.5yd
            // Z internally). The old tight ±(2..5) window mislabeled flat/slightly-sloped ground as a "cliff" and
            // killed the kite in-game. Now: refuse only if there's NO floor in the whole window (a real void) or
            // the floor is a big drop below us (a real cliff); a gentle downhill is fine to back down.
            var top = new Vector3(dest.X, dest.Y, pos.Z + 5f, "None");
            var bottom = new Vector3(dest.X, dest.Y, pos.Z - 25f, "None");
            if (!TraceLine.TraceLineGo(top, bottom, HitFlags.HitTestGround, out Vector3 ground))
            {
                StepSkipLog($"no floor behind: posZ={pos.Z:0.#} dest=({dest.X:0.#},{dest.Y:0.#}) "
                    + $"hdist={pos.DistanceTo2D(dest):0.#} rot={me.Rotation:0.00}");
                return false;
            }
            float drop = pos.Z - ground.Z; // + = downhill behind us, - = uphill
            if (drop > MaxSafeBackstepDrop)
            {
                StepSkipLog($"drop {drop:0.#}yd behind (> {MaxSafeBackstepDrop:0})");
                return false;
            }
            if (DebugLog.Enabled) DebugLog.Write($"StepBack ok: back-drop={drop:0.#}yd"); // diagnostic

            _pendingBackYards = yards; // picked up by OnFightLoop within ~80ms
            return true;
        }

        private int _lastStepSkipLogAt;
        // Diagnostic only (Debug logging, throttled): why a requested step-back was refused.
        private void StepSkipLog(string reason)
        {
            if (!DebugLog.Enabled || unchecked(Now - _lastStepSkipLogAt) < 1000) return;
            _lastStepSkipLogAt = Now;
            DebugLog.Write($"StepBack refused: {reason}");
        }

        private Vector3 _backStart;
        private int _backStartTick;

        /// <summary>True while a backpedal hop is playing out (the fight loop is blocked holding the back key).
        /// The host calls this each loop and pauses casting while it returns true.</summary>
        public bool ServiceReposition() => _repositioning;

        /// <summary>Runs SYNCHRONOUSLY on WRobot's fight-loop thread (~every 80ms in combat). When a backpedal is
        /// pending we set <c>cancelable.Cancel = true</c> (so WRobot exits this fight iteration instead of MoveTo-ing
        /// us straight back into range) and then BLOCK this thread with one continuous Move.Backward(PressKey). Because
        /// it's the same thread that does WRobot's range correction, nothing competes with the keypress — that's the
        /// crux that makes the backpedal smooth (verified: off-thread keypresses get stomped to ~1yd). The rotation
        /// is frozen for the hold, so the duration is short and a cooldown throttles re-triggers. Must never throw.</summary>
        private void OnFightLoop(WoWUnit unit, CancelEventArgs cancelable)
        {
            DumpTargetAuras(unit); // diagnostic only (Debug logging) — shows the real id/name/owner of each aura

            if (_pendingBlink)
            {
                _pendingBlink = false;
                DoBlinkAway(cancelable);
                return; // don't also backpedal the same iteration
            }

            float yards = _pendingBackYards;
            if (yards <= 0f) return;
            _pendingBackYards = 0f;
            if (_repositioning || unchecked(Now - _backpedalReadyAt) < 0) return;

            cancelable.Cancel = true; // skip WRobot's own move-to-range for this iteration so it doesn't undo us

            int durationMs = (int)System.Math.Min(3500f, System.Math.Max(400f, yards / 4.0f * 1000f));
            _backStart = ObjectManager.Me.Position;
            _backStartTick = Now;
            _repositioning = true;
            DebugLog.Write($"StepBack: yards={yards:0.#}  dur={durationMs}ms (fightloop)");
            try { Move.Backward(Move.MoveAction.PressKey, durationMs); }
            catch { }
            finally
            {
                float moved = ObjectManager.Me.Position.DistanceTo2D(_backStart);
                int held = unchecked(Now - _backStartTick);
                DebugLog.Write($"Backpedal end: held={held}ms  moved={moved:0.0}yd  speed={(held > 0 ? moved / held * 1000f : 0f):0.0}yd/s");
                _backpedalReadyAt = unchecked(Now + 1200); // brief cooldown; cast between hops
                _repositioning = false;
            }
        }

        private int _lastAuraDumpAt; // throttle for the diagnostic aura dump below

        /// <summary>Diagnostic (Debug logging only, throttled): logs OUR position + health/mana + our own buffs
        /// (procs like Shadow Trance / Brain Freeze, armor, Life Tap glyph), the target's position + auras (DoT
        /// uptime), the PET's state, and every other nearby hostile — so we can see WHO moves between ticks, the
        /// water Z-offset, DoT/proc/pet state, and adds. Fight thread; never throws.</summary>
        private void DumpTargetAuras(WoWUnit unit)
        {
            if (!DebugLog.Enabled || unit == null || !unit.IsValid) return;
            if (unchecked(Now - _lastAuraDumpAt) < 1000) return; // at most one dump per second
            _lastAuraDumpAt = Now;
            try
            {
                WoWUnit meUnit = ObjectManager.Me;
                ulong me = meUnit.Guid;
                Vector3 mp = meUnit.Position, tp = unit.Position;
                string auras = string.Join(", ", BuffManager.GetAuras(unit.GetBaseAddress)
                    .Select(a => $"{a.SpellId}:{a.GetSpell?.Name}(o={(a.Owner == me ? "me" : a.Owner.ToString())})"));
                // Our own buffs (by name) — armor, Life Tap glyph, procs (Shadow Trance / Brain Freeze / Hot Streak).
                string self = string.Join(",", BuffManager.GetAuras(meUnit.GetBaseAddress)
                    .Select(a => a.GetSpell?.Name).Where(n => !string.IsNullOrEmpty(n)));
                // Pet state (pet casters: Voidwalker / Imp / Water Elemental) — alive, health, distance.
                WoWUnit pet = ObjectManager.Pet;
                string petStr = (pet != null && pet.IsValid && pet.IsAlive)
                    ? $" | pet {pet.Name} hp={pet.HealthPercent:0}% @{pet.GetDistance:0.#}" : "";
                // Other nearby hostiles (adds), '*' = targeting me — so a second add and who it's on is visible.
                string others = string.Join(", ", ObjectManager.GetObjectWoWUnit()
                    .Where(o => o != null && o.IsValid && o.IsAlive && o.IsAttackable
                                && o.Reaction <= WReaction.Neutral && o.Guid != unit.Guid && o.GetDistance <= 40)
                    .Select(o => $"{o.Name}@{o.GetDistance:0.#}({o.Position.X:0.#},{o.Position.Y:0.#},{o.Position.Z:0.#}){(o.IsTargetingMe ? "*" : "")}"));
                DebugLog.Write($"pos me=({mp.X:0.#},{mp.Y:0.#},{mp.Z:0.#}) hp={meUnit.HealthPercent:0}% mp={meUnit.ManaPercentage:0}% "
                    + $"| tgt {unit.Name}@{unit.GetDistance:0.#} hp={unit.HealthPercent:0}% onMe={unit.IsTargetingMe} ({tp.X:0.#},{tp.Y:0.#},{tp.Z:0.#}) "
                    + $"| auras: {auras}{petStr}"
                    + (self.Length > 0 ? $" | self: {self}" : "")
                    + (others.Length > 0 ? $" | others: {others}" : ""));
            }
            catch { }
        }

        private volatile bool _pendingBlink; // set by BlinkAway, consumed on the fight-loop thread
        private float _blinkAwayHeading;     // published with _pendingBlink; read on the fight thread
        private float _blinkBackHeading;
        private const float BlinkDistance = 20f;   // Blink teleports ~20yd
        private const float BlinkLandClearance = 8f; // don't blink within this of another mob

        /// <summary>Request a Blink-escape away from the current target. Validates HERE (rotation thread) that the
        /// landing spot ~20yd behind us is SAFE — solid ground (no cliff), a clear path (no wall), and no other
        /// mob standing there — so blinking blind off a ledge or into adds can't happen. If it isn't safe we
        /// return false and the rotation falls through to the cliff-safe step-back instead. The actual
        /// face→Blink→face runs on the fight-loop thread (so the product can't re-orient us mid-cast).</summary>
        public bool BlinkAway()
        {
            Spell blink = GetSpell("Blink");
            if (!blink.KnownSpell || !IsUsable(blink)) return false;
            WoWUnit me = ObjectManager.Me;
            WoWUnit tgt = ObjectManager.Target;
            if (me == null || tgt == null || !tgt.IsValid) return false;

            Vector3 mp = me.Position, tp = tgt.Position;
            float toTarget = (float)System.Math.Atan2(tp.Y - mp.Y, tp.X - mp.X);
            float away = Norm(toTarget + (float)System.Math.PI);
            Vector3 dest = mp.InFrontOf(away, BlinkDistance);

            if (!CanBlinkTo(mp, dest, tgt)) return false; // unsafe landing → let the step-back handle it

            _blinkAwayHeading = away;
            _blinkBackHeading = Norm(toTarget);
            _pendingBlink = true; // volatile publish AFTER the headings are written
            return true;
        }

        /// <summary>Is the Blink landing spot safe? Checks (1) a cliff — solid ground within a safe drop at the
        /// destination; (2) a wall — a clear path from us to it (Blink slams into geometry otherwise); (3) adds —
        /// no hostile (other than the mob we're escaping) standing at the landing spot.</summary>
        private static bool CanBlinkTo(Vector3 from, Vector3 dest, WoWUnit target)
        {
            // Cliff: require ground within a safe drop where we'd land, else it's a ledge → refuse.
            var top = new Vector3(dest.X, dest.Y, from.Z + 2f, "None");
            var bottom = new Vector3(dest.X, dest.Y, from.Z - 5f, "None"); // 5y = max tolerated drop
            if (!TraceLine.TraceLineGo(top, bottom, HitFlags.HitTestGroundAndStructures, out _)) return false;

            // Wall: a clear path at body height (true = blocked → Blink would slam into it and barely move).
            var eyeFrom = new Vector3(from.X, from.Y, from.Z + 1.5f, "None");
            var eyeTo = new Vector3(dest.X, dest.Y, from.Z + 1.5f, "None");
            if (TraceLine.TraceLineGo(eyeFrom, eyeTo, HitFlags.HitTestGroundAndStructures, out _)) return false;

            // Adds: don't blink INTO another mob (the one we're escaping is in front, so it's excluded).
            ulong targetGuid = target != null ? target.Guid : 0;
            foreach (WoWUnit u in ObjectManager.GetObjectWoWUnit())
            {
                if (u == null || !u.IsValid || !u.IsAlive || !u.IsAttackable) continue;
                if (u.Reaction > WReaction.Neutral) continue; // friendly → ignore
                if (u.Guid == targetGuid) continue;
                if (u.Position.DistanceTo2D(dest) <= BlinkLandClearance) return false;
            }
            return true;
        }

        /// <summary>On the fight thread: turn to face the precomputed AWAY heading (instant — a direct orientation
        /// write, NOT the animated Face/CTM), cast Blink (teleports in the facing direction), then face back toward
        /// the target so casting can resume. StopMove first so Blink follows facing, not a movement vector. Cancels
        /// the fight iteration so the product doesn't re-path us mid-escape. Safety was already checked in BlinkAway.</summary>
        private void DoBlinkAway(CancelEventArgs cancelable)
        {
            WoWUnit me = ObjectManager.Me;
            if (me == null) return;
            Spell blink = GetSpell("Blink");
            if (!blink.KnownSpell || !IsUsable(blink)) return;

            cancelable.Cancel = true;
            MovementManager.StopMove();
            me.Rotation = _blinkAwayHeading; // face away
            SpellManager.CastSpellByNameLUA("Blink");
            me.Rotation = _blinkBackHeading;  // face back to the target
            DebugLog.Write("BlinkAway");
        }

        private static float Norm(float radians)
        {
            const float TwoPi = (float)(2 * System.Math.PI);
            while (radians < 0f) radians += TwoPi;
            while (radians >= TwoPi) radians -= TwoPi;
            return radians;
        }

        // Cached bag counts for CountItems: one Lua round-trip per name-set, refreshed on a short TTL (the
        // conjure checks run out of combat where a few-second staleness is fine, and this avoids a Lua call
        // per tick). Keyed on the joined names.
        private const int ItemCountTtlMs = 6000; // conjure stock changes slowly OOC; a conjure cast clears the cache
        private readonly Dictionary<string, (int count, int at)> _itemCountCache = new Dictionary<string, (int, int)>();

        public int CountItems(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return 0;
            string key = string.Join("", names);
            if (_itemCountCache.TryGetValue(key, out var cached) && unchecked(Now - cached.at) < ItemCountTtlMs)
                return cached.count;

            // ONE Lua round-trip for the WHOLE list. The cost is the round-trip itself (~15-40ms) plus the per-name
            // bag scan inside GetItemCountByNameLUA, so one call per name scaled with BOTH the list length and the
            // number of items in the bags (~170ms for the 10-name conjure lists, dominating the OOC tick). Native
            // GetItemCount is O(1) per name, so summing them all in a single call is cheap and size-independent.
            string sum = string.Join("+", names.Select(n => "GetItemCount('" + n.Replace("'", "\\'") + "')"));
            int total = Lua.LuaDoString<int>("return " + sum);
            _itemCountCache[key] = (total, Now);
            return total;
        }

        // Whether a creature entry is a sheepable type (Beast/Humanoid/Critter). Cached so we only pay one Lua
        // read per entry — we resolve it for a NON-target add by parking it on focus (UnitCreatureType only
        // works for a unit token, and the add isn't our target).
        private readonly Dictionary<int, bool> _sheepableByEntry = new Dictionary<int, bool>();

        public bool Polymorph(IWowUnit add)
        {
            if (!(add is WRobotUnit w)) return false;
            WoWUnit u = w.Inner;
            if (u == null || !u.IsValid || !u.IsAlive) return false;
            Spell poly = GetSpell("Polymorph");
            if (!poly.KnownSpell || !IsUsable(poly)) return false;
            if (ObjectManager.Me.IsCast) return false; // already casting (likely the Polymorph itself)

            // Resolve sheepability from the per-entry cache FIRST — so an add we've already learned can't be
            // sheeped (Undead/Elemental "Apparition" mobs are NOT polymorph-able) costs nothing per tick: no
            // focus, no Lua. Only an UNKNOWN entry pays the one-time focus-park + type read.
            if (_sheepableByEntry.TryGetValue(u.Entry, out bool sheepable))
            {
                if (!sheepable) return false; // known un-sheepable — decline for free (focus untouched)
            }
            else
            {
                // Unknown entry: park it on focus once (UnitCreatureType needs a unit token and the add isn't
                // our target), read + cache the type, and log it so it's visible WHY a mob won't sheep.
                ObjectManager.Me.FocusGuid = u.Guid;
                string ct = Lua.LuaDoString<string>("return UnitCreatureType('focus') or ''");
                if (string.IsNullOrEmpty(ct)) { Lua.LuaDoString("ClearFocus();"); return false; } // unresolved — retry later
                sheepable = ct == "Humanoid" || ct == "Beast" || ct == "Critter";
                _sheepableByEntry[u.Entry] = sheepable;
                DebugLog.Write($"Polymorph: {u.Name} type='{ct}' sheepable={sheepable}");
                if (!sheepable) { Lua.LuaDoString("ClearFocus();"); return false; }
            }

            // Sheepable: park on focus (the cast targets a non-target add), face it (instant orientation write,
            // like BlinkAway, so the cast isn't refused for facing), cast, then drop focus immediately — the cast
            // is target-locked at start, and a stale focus would interfere with later focus-token casts.
            ObjectManager.Me.FocusGuid = u.Guid;
            Vector3 mp = ObjectManager.Me.Position, ap = u.Position;
            ObjectManager.Me.Rotation = Norm((float)System.Math.Atan2(ap.Y - mp.Y, ap.X - mp.X));
            SpellManager.CastSpellByNameOn("Polymorph", "focus");
            Lua.LuaDoString("ClearFocus();");
            DebugLog.Write($"Polymorph cast on {u.Name}");
            return true;
        }

        public void PetAttack(IWowUnit target)
        {
            if (!(target is WRobotUnit wTarget)) return;
            WoWUnit unit = wTarget.Inner;
            WoWUnit pet = ObjectManager.Pet;
            if (unit == null || !unit.IsValid || pet == null || !pet.IsValid) return;

            // If the unit is our current target the simple form works; otherwise park its GUID on focus
            // and use the macro conditional (the pattern the old AIO uses), then clear focus again.
            WoWUnit current = ObjectManager.Target;
            if (current != null && current.IsValid && current.Guid == unit.Guid)
            {
                Lua.LuaDoString("PetAttack('target');");
            }
            else
            {
                ObjectManager.Me.FocusGuid = unit.Guid;
                Lua.RunMacroText("/petattack [@focus]");
                Lua.LuaDoString("ClearFocus();");
            }
        }

        public bool PetHasAbility(string name) => PetAbilities().Contains(name);

        public bool PetAbilityReady(string name) => PetReadySet().Contains(name);

        // Names of pet abilities that are on the bar AND off cooldown, scanned in one pass (500ms TTL).
        private HashSet<string> PetReadySet()
        {
            WoWUnit pet = ObjectManager.Pet;
            if (pet == null || !pet.IsValid)
            {
                if (_petReady.Count != 0) _petReady = new HashSet<string>();
                return _petReady;
            }
            if (unchecked(Now - _petReadyAt) < 500) return _petReady;

            string raw = Lua.LuaDoString<string>(
                "local t='' for i=1,10 do local n=GetPetActionInfo(i) if n then local s,d=GetPetActionCooldown(i) " +
                "if (d-(GetTime()-s))<=0 then t=t..n..'|' end end end return t");
            var set = new HashSet<string>();
            if (!string.IsNullOrEmpty(raw))
                foreach (string n in raw.Split('|'))
                    if (n.Length > 0) set.Add(n);
            _petReady = set;
            _petReadyAt = Now;
            return _petReady;
        }

        public bool CastPetAbility(string name)
        {
            if (!PetHasAbility(name)) return false; // cheap (cached) — no Lua when the pet doesn't have it
            // Proven pattern from the old PetManager: find the action slot by name, cast it if off cooldown.
            return Lua.LuaDoString<bool>(
                "local idx=0 for i=1,10 do local n=GetPetActionInfo(i) if n=='" + name + "' then idx=i end end " +
                "if idx>0 then local s,d=GetPetActionCooldown(idx) if (d-(GetTime()-s))<=0 then CastPetAction(idx) return true end end return false");
        }

        public void SetPetAutocast(string ability, bool on)
        {
            if (!PetHasAbility(ability)) return; // cheap (cached) — no Lua when the pet doesn't have it
            // Proven pattern from the old PetManager: GetSpellAutocast(name,'pet') -> (autocastable, autostate);
            // ToggleSpellAutocast flips it, so only toggle when the current state differs from what we want.
            string want = on ? "true" : "false";
            Lua.LuaDoString(
                "local want=" + want + " local allowed,state=GetSpellAutocast('" + ability + "','pet') " +
                "if allowed and state ~= want then ToggleSpellAutocast('" + ability + "','pet') end");
        }

        // The current pet's action-bar ability names, scanned once per pet (5s TTL) via Lua.
        private HashSet<string> PetAbilities()
        {
            WoWUnit pet = ObjectManager.Pet;
            ulong guid = (pet != null && pet.IsValid) ? pet.Guid : 0;
            if (guid == 0)
            {
                if (_petAbilities.Count != 0) _petAbilities = new HashSet<string>();
                _petBarGuid = 0;
                return _petAbilities;
            }
            if (guid == _petBarGuid && unchecked(Now - _petBarAt) < 5000) return _petAbilities;

            string raw = Lua.LuaDoString<string>(
                "local t='' for i=1,10 do local n=GetPetActionInfo(i) if n then t=t..n..'|' end end return t");
            var set = new HashSet<string>();
            if (!string.IsNullOrEmpty(raw))
                foreach (string n in raw.Split('|'))
                    if (n.Length > 0) set.Add(n);
            _petAbilities = set;
            _petBarGuid = guid;
            _petBarAt = Now;
            return _petAbilities;
        }

        public void RunLocked(Action action)
        {
            // Use WRobot's frame lock (the mechanism the old AIO uses around its rotation). Taking
            // ObjectManager.Locker instead contended with the product's own use of it during combat,
            // intermittently stalling our tick for seconds (the "reacts too late" delay).
            lock (wManager.Wow.Memory.WowMemory.LockFrameLocker)
            {
                try
                {
                    wManager.Wow.Memory.WowMemory.LockFrame();
                    action();
                }
                finally
                {
                    wManager.Wow.Memory.WowMemory.UnlockFrame();
                }
            }
        }
    }
}
