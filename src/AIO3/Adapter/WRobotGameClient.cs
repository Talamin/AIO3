using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AIO3.Core.Game;
using wManager.Events;
using wManager.Wow.Class;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;
using WReaction = wManager.Wow.Enums.Reaction;

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
            if (unit.Guid == ObjectManager.Target.Guid) return "target";
            ObjectManager.Me.FocusGuid = unit.Guid;
            return "focus";
        }

        public void SetTarget(IWowUnit unit)
        {
            // Verified API: WoWUnit.Target is a settable ulong (GUID) on the local player that performs
            // the actual in-game target switch, so WRobot's facing/movement follows the new target.
            if (unit != null && unit.Guid != 0) ObjectManager.Me.Target = unit.Guid;
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
