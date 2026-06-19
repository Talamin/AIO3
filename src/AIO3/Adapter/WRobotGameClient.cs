using System;
using System.Collections.Generic;
using System.Linq;
using AIO3.Core.Game;
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
    internal sealed class WRobotGameClient : IGameClient
    {
        private const float ScanRange = 50f;

        private readonly Dictionary<string, Spell> _spellCache = new Dictionary<string, Spell>();

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

        public string ActiveStanceName =>
            Lua.LuaDoString<string>(
                "for i=1,10 do local _, name, isActive = GetShapeshiftFormInfo(i); " +
                "if name ~= nil and isActive then return name; end end return '';");

        public IWowUnit Target
        {
            get
            {
                WoWUnit t = ObjectManager.Target;
                return (t != null && t.IsValid && t.Guid != 0) ? new WRobotUnit(t) : null;
            }
        }

        public IReadOnlyList<IWowUnit> Enemies
        {
            get
            {
                var result = new List<IWowUnit>();
                foreach (WoWUnit u in ObjectManager.GetObjectWoWUnit())
                {
                    if (u.IsValid && u.IsAlive && u.IsAttackable
                        && u.Reaction <= WReaction.Neutral
                        && u.GetDistance <= ScanRange)
                    {
                        result.Add(new WRobotUnit(u));
                    }
                }
                return result;
            }
        }

        public IReadOnlyList<IWowUnit> Party
        {
            get
            {
                var me = ObjectManager.Me;
                var result = new List<IWowUnit> { new WRobotUnit(me) };
                // Raid-aware: GetParty() is 5-man only; use the raid roster when in a raid.
                List<WoWPlayer> group = wManager.Wow.Helpers.Party.GetRaidMemberCount() > 0
                    ? wManager.Wow.Helpers.Party.GetRaidMembers()
                    : wManager.Wow.Helpers.Party.GetParty();
                foreach (WoWPlayer p in group)
                {
                    if (p.Guid != me.Guid)
                        result.Add(new WRobotUnit(p));
                }
                return result;
            }
        }

        public bool IsSpellKnown(string spell) => GetSpell(spell).KnownSpell;

        public float SpellRange(string spell) => GetSpell(spell).MaxRange;

        public bool IsCurrentSpell(string spell) =>
            Lua.LuaDoString<bool>($"return IsCurrentSpell('{spell}') == 1 or IsCurrentSpell('{spell}') == true");

        public bool IsSpellReady(string spell)
        {
            Spell s = GetSpell(spell);
            if (!s.KnownSpell || !s.IsSpellUsable) return false;
            // IsSpellUsable does not gate on cooldown, so check the cooldown separately via the
            // supported API (no raw memory). Same units the engine expects: <= 0 means ready.
            return SpellManager.GetSpellCooldownTimeLeftBySpellName(s.Name) <= 0;
        }

        public int GlobalCooldownRemainingMs => SpellManager.GlobalCooldownTimeLeft();

        public bool PlayerIsCasting => ObjectManager.Me.IsCast;

        public bool PlayerIsMoving => ObjectManager.Me.GetMove;

        public bool PlayerIsMounted => ObjectManager.Me.IsMounted;

        public bool PlayerInCombat => ObjectManager.Me.InCombat;

        // WRobot's own fight state — true throughout a fight including the approach. Mirrors how the old
        // AIO gated its combat rotation, so we only act when the product has committed to a target.
        public bool ProductIsFighting => Fight.InFight;

        public bool PlayerIsAutoAttacking =>
            Lua.LuaDoString<bool>("return IsCurrentSpell('Auto Attack') == 1 or IsCurrentSpell('Auto Attack') == true");

        public CastResult Cast(string spell, IWowUnit target, bool force = false)
        {
            if (!(target is WRobotUnit wTarget)) return CastResult.NoTarget;
            WoWUnit unit = wTarget.Inner;
            Spell s = GetSpell(spell);

            if (unit == null || !unit.IsValid || unit.IsDead) return CastResult.NoTarget;
            if (!s.KnownSpell) return CastResult.NotKnown;
            if (!s.IsSpellUsable) return CastResult.NotUsable;
            if (s.CastTime > 0.0 && ObjectManager.Me.GetMove) return CastResult.Moving;
            if (!force && ObjectManager.Me.IsCast) return CastResult.Busy;

            if (force) Lua.LuaDoString("SpellStopCasting();");

            SpellManager.CastSpellByNameOn(s.Name, LuaUnitId(unit));
            return CastResult.Success;
        }

        public bool UseFirstReadyItem(IReadOnlyList<string> names)
        {
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
