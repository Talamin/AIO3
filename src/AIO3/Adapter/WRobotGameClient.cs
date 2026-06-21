using System;
using System.Collections.Generic;
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

        // End tick of the current backpedal hop (see StepBack / IsRepositioning).
        private int _repositionUntil;

        private static int Now => Environment.TickCount;

        public WRobotGameClient()
        {
            ObjectManagerEvents.OnObjectManagerPulsed += OnObjectManagerPulsed;
        }

        public void Dispose()
        {
            ObjectManagerEvents.OnObjectManagerPulsed -= OnObjectManagerPulsed;
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

        public CastResult Cast(string spell, IWowUnit target, bool force = false)
        {
            if (!(target is WRobotUnit wTarget)) return CastResult.NoTarget;
            WoWUnit unit = wTarget.Inner;
            Spell s = GetSpell(spell);

            if (unit == null || !unit.IsValid || unit.IsDead) return CastResult.NoTarget;
            if (!s.KnownSpell) return CastResult.NotKnown;
            if (!IsUsable(s)) return CastResult.NotUsable;
            if (s.CastTime > 0.0 && ObjectManager.Me.GetMove) return CastResult.Moving;
            if (!force && ObjectManager.Me.IsCast) return CastResult.Busy;

            if (force) Lua.LuaDoString("SpellStopCasting();");

            SpellManager.CastSpellByNameOn(s.Name, LuaUnitId(unit));

            // Keep the caches honest right after we change state ourselves.
            if (spell == "Auto Attack") { _autoAttacking = true; _autoAttackingKnown = true; _autoAttackingAt = Now; }
            if (spell.EndsWith("Stance")) { _stance = null; _usableCache.Clear(); } // stance affects usability
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

        public bool StepBack(float yards)
        {
            var me = ObjectManager.Me;
            Vector3 pos = me.Position;
            // The spot we'd back into (Rotation is the facing in radians; +PI = directly behind).
            Vector3 dest = pos.InFrontOf(me.Rotation + (float)System.Math.PI, yards);

            // Cliff guard: probe straight DOWN at the destination — require solid ground within a safe drop,
            // else it's a ledge and we refuse. This is what actually prevents walking off an edge (the old
            // AIO only checked horizontally and could fall). One cheap trace; no PathFinder (it was the 20ms+
            // tick spike) — a short straight backstep's real danger is a cliff right behind, which this catches.
            var top = new Vector3(dest.X, dest.Y, pos.Z + 2f, "None");
            var bottom = new Vector3(dest.X, dest.Y, pos.Z - 5f, "None"); // 5y = max tolerated drop
            if (!TraceLine.TraceLineGo(top, bottom, HitFlags.HitTestGroundAndStructures, out _)) return false;

            // Open a reposition window; the host drives the key via ServiceReposition each tick. We HOLD the
            // back key down for the window rather than tapping it: a single tap fizzles (the product re-issues
            // its own movement each frame and overrides ours), and re-tapping every tick is the jerky stop-go
            // the user saw — so instead we keep the key DOWN (re-affirmed, never released mid-hop) for smooth,
            // continuous backpedal, then release once at the end. Keeps us facing the mob. Backpedal ~4.5 yd/s.
            _repositionUntil = unchecked(Now + System.Math.Min(2500, (int)(yards / 4.5f * 1000f)));
            return true;
        }

        private bool _backKeyDown;

        /// <summary>Drive an in-progress backpedal each tick (the host calls this every loop). While the hop's
        /// window is open it holds the back key DOWN (re-affirmed so the product can't steal the movement, but
        /// never released mid-hop → smooth, not the jerky tap-tap of repeated presses) and returns true so the
        /// host pauses casting; when the window ends it releases the key once and returns false.</summary>
        public bool ServiceReposition()
        {
            if (unchecked(Now - _repositionUntil) < 0)
            {
                Move.Backward(Move.MoveAction.DownKey); // press/hold (idempotent while already down)
                _backKeyDown = true;
                return true;
            }
            if (_backKeyDown)
            {
                Move.Backward(Move.MoveAction.UpKey); // release at the end of the hop
                _backKeyDown = false;
            }
            return false;
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
