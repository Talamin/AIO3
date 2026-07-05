using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using AIO3.Core.Combat;
using AIO3.Core.Game;
using robotManager.Helpful;
using wManager.Events;
using wManager.Wow.Class;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;
using WReaction = wManager.Wow.Enums.Reaction;
using HitFlags = wManager.Wow.Enums.CGWorldFrameHitFlags;
using InventorySlot = wManager.Wow.Enums.InventorySlot;

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

        // Absolute spell mana cost by name, from Lua GetSpellInfo (4th return = cost at the highest known rank).
        // Cost rises on level-up, so cache with a TTL rather than forever (a rotation may gate on it every tick).
        private readonly Dictionary<string, (int cost, int at)> _spellCostCache = new Dictionary<string, (int, int)>();
        private const int SpellCostTtlMs = 30000;
        private readonly WRobotCooldowns _cooldowns = new WRobotCooldowns();

        // Enemy/party lists are rebuilt on WRobot's ObjectManager pulse (~100ms) rather than every tick:
        // the underlying unit data only refreshes on that pulse, so per-tick rebuilds were wasted work.
        // The reference swap is atomic and the field is volatile, so the tick thread always reads a
        // complete list. Wrappers are live, so unit properties (distance/auras) stay current between pulses.
        private volatile IReadOnlyList<IWowUnit> _enemiesCache = Array.Empty<IWowUnit>();
        private volatile IReadOnlyList<IWowUnit> _partyCache = Array.Empty<IWowUnit>();
        // Totems are read live via Lua GetTotemInfo in the Totems getter (cached), NOT the object-manager pulse — the
        // object scan (UnitFlags.Totem + owner GUID) is unreliable on servers where the totem is the "pet".
        private IReadOnlyList<IWowUnit> _totemsCache = Array.Empty<IWowUnit>();
        private int _totemsAt;
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

        // HoldPosition window: while Now < _holdUntil we cancel the product's travel pulses so a long cast (the
        // pet summon) isn't broken by re-pathing. Set by HoldPosition; the movement-pulse handlers read it.
        private volatile int _holdUntil;

        private static int Now => Environment.TickCount;

        public WRobotGameClient()
        {
            ObjectManagerEvents.OnObjectManagerPulsed += OnObjectManagerPulsed;
            wManager.Events.FightEvents.OnFightLoop += OnFightLoop; // backpedal runs here (see OnFightLoop)
            // Hold-position-during-summon: cancel travel re-pathing while _holdUntil is in the future. Both hooks,
            // like the old AIO's AutoPartyResurrect — OnMovementPulse pins path-following, OnMoveToPulse pins a
            // direct MoveTo. They fire on WRobot's movement thread, so keep the handlers tiny and never throw.
            wManager.Events.MovementEvents.OnMovementPulse += OnMovementPulseHold;
            wManager.Events.MovementEvents.OnMoveToPulse += OnMoveToPulseHold;
        }

        public void Dispose()
        {
            ObjectManagerEvents.OnObjectManagerPulsed -= OnObjectManagerPulsed;
            wManager.Events.FightEvents.OnFightLoop -= OnFightLoop;
            wManager.Events.MovementEvents.OnMovementPulse -= OnMovementPulseHold;
            wManager.Events.MovementEvents.OnMoveToPulse -= OnMoveToPulseHold;
        }

        // True while the pin window is still in the future AND we're out of combat: the moment a fight starts (an
        // add aggros mid-summon/-channel) the pin releases so the product can move us and we defend ourselves
        // instead of standing pinned. A hold only ever protects an out-of-combat cast (pet summon, Cannibalize).
        private bool Holding => unchecked(Now - _holdUntil) < 0 && !ObjectManager.Me.InCombat;

        private void OnMovementPulseHold(System.Collections.Generic.List<robotManager.Helpful.Vector3> points, System.ComponentModel.CancelEventArgs cancelable)
        {
            try { if (Holding) cancelable.Cancel = true; } catch { /* never throw on the movement thread */ }
        }

        private void OnMoveToPulseHold(robotManager.Helpful.Vector3 point, System.ComponentModel.CancelEventArgs cancelable)
        {
            try { if (Holding) cancelable.Cancel = true; } catch { /* never throw on the movement thread */ }
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
                var me = ObjectManager.Me;

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
                // Totems are read live via GetTotemInfo in the Totems getter (the object scan was unreliable — see there).

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

        // Whether a talent has >=1 point. GetTalentInfo's 5th return is the current rank (same call TalentTrainer
        // reads). Cached per (tab,index) ~10s — talents change only on respec/level-up, so a rotation can gate on a
        // talent every tick without a per-tick Lua round-trip.
        private const int TalentTtlMs = 10000;
        private readonly Dictionary<(int, int), (bool has, int at)> _talentCache = new Dictionary<(int, int), (bool, int)>();

        public bool HasTalent(int talentTab, int talentIndex)
        {
            (int, int) key = (talentTab, talentIndex);
            if (_talentCache.TryGetValue(key, out (bool has, int at) c) && unchecked(Now - c.at) < TalentTtlMs)
                return c.has;
            bool has = false;
            try
            {
                string s = Lua.LuaDoString<string>(
                    $"local _,_,_,_,cur = GetTalentInfo({talentTab},{talentIndex}); return tostring(cur or 0)");
                has = int.TryParse(s, out int cur) && cur > 0;
            }
            catch { }
            _talentCache[key] = (has, Now);
            return has;
        }

        // Native WRobot rune API (Lua-backed wrapper) — counts READY runes of a kind. Maps the game-agnostic Core
        // RuneType to wManager's own RuneTypes enum (Blood=1/Unholy=2/Frost=3/Death=4). 0 for non-DK / on error.
        public int RunesReady(AIO3.Core.Game.RuneType type)
        {
            try
            {
                wManager.Wow.Enums.RuneTypes mapped;
                switch (type)
                {
                    case AIO3.Core.Game.RuneType.Blood: mapped = wManager.Wow.Enums.RuneTypes.Blood; break;
                    case AIO3.Core.Game.RuneType.Frost: mapped = wManager.Wow.Enums.RuneTypes.Frost; break;
                    case AIO3.Core.Game.RuneType.Unholy: mapped = wManager.Wow.Enums.RuneTypes.Unholy; break;
                    default: mapped = wManager.Wow.Enums.RuneTypes.Death; break;
                }
                return ObjectManager.Me.RunesReadyCount(mapped);
            }
            catch { return 0; }
        }

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

        // WoWPlayer.ComboPoint is an Int32 (0..5), already relative to the current target — no Lua needed.
        public int ComboPoints => ObjectManager.Me.ComboPoint;

        // Stealth is a normal player buff; HaveBuff is a cheap memory read.
        public bool PlayerIsStealthed => ObjectManager.Me.HaveBuff("Stealth");

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

        // Own active totems via Lua GetTotemInfo(1..4) — reliable across servers. The object-manager scan (UnitFlags.
        // Totem + owner GUID) FAILS where the dropped totem is the "pet" and carries no totem flag (verified in GREZ's
        // log: no totem-flagged units, totem shows as ObjectManager.Pet, tiny char GUID=3). GetTotemInfo reads the four
        // totem slots' names directly. No position is exposed, so an active totem is treated as at-our-feet (Distance
        // 0) → the school reads "up", which also ends the re-drop-on-move churn (a totem is re-dropped only when it
        // actually leaves its slot / expires). Cached 250ms so the Lua read isn't per-tick.
        public IReadOnlyList<IWowUnit> Totems
        {
            get
            {
                if (unchecked(Now - _totemsAt) < 250) return _totemsCache;
                _totemsAt = Now;
                var list = new List<IWowUnit>();
                try
                {
                    string raw = Lua.LuaDoString<string>(
                        "local r='' for i=1,4 do local h,n=GetTotemInfo(i) if h and n and n~='' then r=r..n..'|' end end return r") ?? "";
                    var names = raw.Split('|').Where(n => n.Length > 0).ToList();
                    if (names.Count > 0)
                    {
                        // Real distance per active totem: GetTotemInfo says a slot is active but not WHERE. Without the
                        // distance the school reads "up" forever and we never re-drop after walking away from the totem —
                        // it stays active in the slot but its buff is out of range (Talamin: totem up, no buff, no re-cast).
                        // The totem is a unit (the "pet" and/or a summoned unit), so match it by name for GetDistance.
                        var dist = new Dictionary<string, float>();
                        foreach (WoWUnit u in ObjectManager.GetObjectWoWUnit())
                            if (u != null && u.IsValid && u.Name != null && names.Contains(u.Name)
                                && (!dist.TryGetValue(u.Name, out float cur) || u.GetDistance < cur))
                                dist[u.Name] = u.GetDistance;
                        WoWUnit pet = ObjectManager.Pet;
                        if (pet != null && pet.IsValid && pet.Name != null && names.Contains(pet.Name))
                            dist[pet.Name] = pet.GetDistance; // the pet is authoritative for its own totem
                        foreach (string name in names)
                            list.Add(new TotemUnit(name, dist.TryGetValue(name, out float d) ? d : 0f));
                    }
                }
                catch { }
                _totemsCache = list;
                return _totemsCache;
            }
        }

        // A minimal own-totem unit for the Totems list: a name + its real distance. GetTotemInfo gives the name; the
        // distance comes from the matching totem unit (see the getter). The school-upkeep only reads Name + Distance,
        // so the rest are inert defaults.
        private sealed class TotemUnit : IWowUnit
        {
            public TotemUnit(string name, float distance) { Name = name; Distance = distance; }
            public string Name { get; }
            public float Distance { get; }
            public ulong Guid => 0;
            public int Entry => 0;
            public bool IsAlive => true;
            public int Level => 0;
            public double HealthPercent => 100;
            public double PowerPercent => 0;
            public int Rage => 0;
            public int Energy => 0;
            public int RunicPower => 0;
            public int Mana => 0;
            public float DistanceTo(IWowUnit other) => 0f;
            public bool IsCasting => false;
            public int CastingSpellId => 0;
            public AIO3.Core.Game.Reaction Reaction => AIO3.Core.Game.Reaction.Friendly;
            public bool IsTargetingMe => false;
            public bool IsTargetingMyPet => false;
            public ulong TargetGuid => 0;
            public ulong PetOwnerGuid => 0;
            public bool IsAttackable => false;
            public bool IsElite => false;
            public bool IsCaster => false;
            public string CreatureType => "";
            public bool HasAura(string name) => false;
            public int AuraStacks(string name) => 0;
            public bool HasMyAura(string name) => false;
            public long MyAuraTimeLeftMs(string name) => 0;
        }

        public bool IsSpellKnown(string spell) => GetSpell(spell).KnownSpell;

        public float SpellRange(string spell) => GetSpell(spell).MaxRange;

        // No managed WRobot API exposes a spell's mana cost (scout-verified), so read it via Lua GetSpellInfo — its
        // 4th return is the cost at the character's HIGHEST KNOWN rank, so it tracks level-ups automatically; nil → 0
        // for an unknown/free spell. Cached ~30s so a per-tick gate (the druid shift-heal affordability check) doesn't
        // spam a Lua round-trip. Mirrors the old AIO's RotationSpell.GetSpellCost, but re-queried instead of once-at-init.
        public int SpellManaCost(string spell)
        {
            if (_spellCostCache.TryGetValue(spell, out var c) && unchecked(Now - c.at) < SpellCostTtlMs) return c.cost;
            int cost = 0;
            try
            {
                cost = Lua.LuaDoString<int>(
                    "local _,_,_,c = GetSpellInfo('" + spell.Replace("'", "\\'") + "'); return c or 0");
            }
            catch { }
            _spellCostCache[spell] = (cost, Now);
            return cost;
        }

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

        // A ground mount is configured iff WRobot's GroundMountName is non-empty (same field the old AIO checked).
        public bool HasGroundMount => !string.IsNullOrWhiteSpace(wManager.wManagerSetting.CurrentSetting.GroundMountName);

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

        // True movement-root bit. GetMovementFlag dereferences the MovementInfo block at [BaseAddress+0xD8] and reads
        // the flags DWORD at +0x44 (scout-verified from WRobot's own WoWUnit.IsFlying = +0x44 & 0x2000000). The earlier
        // +0x38 was WRONG — it landed in the positional area of the block, so `& 0x800` flipped true spuriously, which
        // made Hand of Freedom (and the Gnome Escape Artist racial) fire on cooldown. MOVEMENTFLAG_ROOT = 0x800. NOT
        // WoWUnit.Rooted — that aliases UnitFlags.Influenced (0x4), which does NOT flip for Frost Nova / Entangling
        // Roots / a net, so it under-reports. Cheap memory read, no Lua.
        public bool PlayerIsRooted => ObjectManager.Me.GetMovementFlag(0x44, 0x800);

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

        // Corpse scan shared by Cannibalize and Raise Dead. GetObjectWoWUnit() includes dead units (scout-verified);
        // IsDead/Entry/GetDistance all resolve on them. The creature type comes from our per-entry cache (populated
        // when the mob was alive + our target), so a fresh unfought corpse simply won't match.
        private bool AnyCorpseNearby(float range, params string[] creatureTypes) =>
            ObjectManager.GetObjectWoWUnit().Any(u => u.IsDead && u.GetDistance <= range
                && WRobotUnit.CachedCreatureTypeIs(u.Entry, creatureTypes));

        // A Humanoid/Undead corpse within 8yd for Cannibalize.
        public bool HasCannibalizeCorpseNearby() => AnyCorpseNearby(8f, "Humanoid", "Undead");

        // A Humanoid corpse within reach for Raise Dead (which reanimates a humanoid corpse, else consumes Corpse
        // Dust). Humanoid-only (Undead corpses don't feed Raise Dead) and a conservative range so a false positive
        // can't make the summon fire when the corpse is actually out of reach — if none matches, the caller falls
        // back to the Corpse Dust reagent gate.
        private const float RaiseDeadCorpseRange = 10f;
        public bool HasRaiseableCorpseNearby() => AnyCorpseNearby(RaiseDeadCorpseRange, "Humanoid");

        // Rest/regen phase: WRobot exposes no readable "currently regenerating" engine state to a FightClass
        // (scout-verified), so infer it from the Food/Drink auras — the clear, reliable signal that the bot is
        // eating/drinking to recover. Cheap memory reads; only consulted in the out-of-combat stealth-opener window.
        public bool PlayerIsResting
        {
            get
            {
                WoWUnit me = ObjectManager.Me;
                return me.HaveBuff("Food") || me.HaveBuff("Drink");
            }
        }

        private int _harmfulAuraAt;
        private bool _harmfulAura;

        // Any harmful aura (debuff) on the player, including physical bleeds. WRobot's Aura type can't classify
        // auras as harmful (scout-verified), but Blizzard's UnitDebuff lists debuffs only — slot 1 is occupied iff
        // the player carries at least one debuff. Cached briefly; only consulted in the stealth-opener window.
        public bool PlayerHasHarmfulAura()
        {
            if (unchecked(Now - _harmfulAuraAt) >= 250)
            {
                _harmfulAuraAt = Now;
                _harmfulAura = Lua.LuaDoString<bool>("return UnitDebuff('player', 1) ~= nil");
            }
            return _harmfulAura;
        }

        // ~150° rear cone (full cone width) — a generous "behind" with margin, so a positional ability (Garrote/
        // Shred) is only chosen when we're comfortably behind, not at the very edge where the cast would fail.
        // The rear cone (full width) that counts as "behind" for positional abilities (Shred / Ravage / Garrote).
        // TIGHTENED 150°->80° after the feral Shred-spam bug. The geometry itself is correct (scout-verified: the
        // TargetFacingToRadian heading is start->faceTarget, and WoWUnit.Rotation is a LIVE memory read), but a WRobot
        // bot fights FACE-ON and is repositioned by the product mid-fight, so the mob's facing sweeps past the player.
        // The old 150° cone tripped at |offset| > 105° — a mob only ~75° off-facing counted as "behind" — so transient
        // turns produced false "behind=true" -> Shred fired and the SERVER rejected it (the client cast "succeeds", so
        // it never fell through to Claw = a dead GCD). 80° trips at |offset| > 140° (within 40° of dead-behind), a ~50°
        // margin over WoW's own ~90° behind-line, so we only commit when CONFIDENTLY, squarely behind. In normal
        // face-on combat this never trips -> the builder correctly falls through to Mangle/Claw. Tune here.
        private const float BehindArcRadians = 1.40f; // ~80° rear cone (full width)

        // Anti-transient + anti-latency debounce: the raw "behind" geometry must hold CONTINUOUSLY this long before
        // PlayerIsBehindTarget commits. One tick caught while the mob spins past isn't enough, and the window outlasts
        // the client/server facing-sync latency, so a Shred we commit actually lands. A genuine behind position (stealth
        // approach, pet-tanked mob) clears it easily; an incidental melee turn does not.
        private const int BehindStableMs = 300;
        private int _behindRawFalseAt; // last Now the raw geometry said NOT behind (the debounce anchor)

        // Outcome-based positional guard: the behind GEOMETRY can false-positive on a stale mob facing, so we also
        // judge whether a positional ability (Shred/…) actually LANDED, and back it off when the server keeps
        // rejecting it. Fed by the shared DamageTracker (wired at startup); read by PositionalFailing().
        private static readonly HashSet<string> Positionals = new HashSet<string> { "Shred", "Backstab", "Ambush", "Garrote" };
        private readonly PositionalGuard _posGuard = new PositionalGuard();
        private DamageTracker _damage;

        // Signed offset (radians, [-π,π]) between the target's facing and the direction TO the player: ~0 = player
        // in FRONT of the target, ~±π = directly BEHIND it. NaN if there's no target. robotManager.Helpful.Math.
        // TargetFacingToRadian is atan2 in WRobot's Rotation convention (0=east, CCW — scout-verified live), so
        // subtracting the target's Rotation gives the front/back offset directly (no axis swap). This REPLACES
        // WoWUnit.IsBehind, which is control-flow-obfuscated and reported "front" while we were visibly behind the
        // mob. Pure memory reads, no Lua. Class-agnostic (rogue Garrote opener now; feral Shred/Mangle later).
        private double TargetFacingOffset()
        {
            WoWUnit t = ObjectManager.Target;
            if (t == null || !t.IsValid) return double.NaN;
            double a = robotManager.Helpful.Math.TargetFacingToRadian(t.Position, ObjectManager.Me.Position);
            double diff = a - t.Rotation;
            while (diff > System.Math.PI) diff -= 2 * System.Math.PI;
            while (diff < -System.Math.PI) diff += 2 * System.Math.PI;
            return diff;
        }

        public bool PlayerIsBehindTarget()
        {
            double diff = TargetFacingOffset();
            bool rawBehind = !double.IsNaN(diff)
                             && System.Math.Abs(diff) > System.Math.PI - BehindArcRadians / 2.0;
            if (!rawBehind) { _behindRawFalseAt = Now; return false; }
            return unchecked(Now - _behindRawFalseAt) >= BehindStableMs; // stably behind long enough to commit
        }

        public void AttachDamageTracker(DamageTracker damage) => _damage = damage;

        // A positional is "failing" when its recent casts dealt no damage (the server rejected them despite the
        // behind-geometry saying go). No tracker wired (or never cast) → false, so the gate is a no-op by default.
        public bool PositionalFailing(string spell) =>
            _damage != null && _posGuard.Suppressed(spell, _damage.HitCount(spell), Now);

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
            // Positional outcome: the client cast "succeeds" even when the server will reject it for position, so note
            // it here and let the DamageTracker tell us (later) whether it actually landed — see PositionalFailing().
            if (_damage != null && Positionals.Contains(spell)) _posGuard.OnCast(spell, _damage.HitCount(spell), Now);
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

        public void HoldPosition(int ms)
        {
            _holdUntil = Now + ms;       // pin window — the movement-pulse handlers cancel re-pathing until then
            MovementManager.StopMove();  // and kill any motion already in progress
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
                // Resource readout is class-aware: mana% is meaningless for a rogue (runs on energy + combo points)
                // or a warrior (rage), so show the resource the rotation actually reads — plus stealth for the rogue.
                string pwr;
                switch (meUnit.WowClass.ToString())
                {
                    case "Rogue":
                        // behind = the positional check that drives the "Auto" stealth opener (Garrote behind /
                        // Cheap Shot in front) — logged so we can verify the front/back detection in-game.
                        pwr = $"energy={meUnit.Energy} cp={ObjectManager.Me.ComboPoint} stealth={(meUnit.HaveBuff("Stealth") ? "Y" : "N")} behind={(PlayerIsBehindTarget() ? "Y" : "N")}@{System.Math.Abs(TargetFacingOffset()) * 57.2958:0}deg";
                        break;
                    case "Druid":
                        // behind@deg + shredFail = the positional readout: confirms whether the behind-geometry is
                        // false-positiving (says behind while we're visibly in front) and whether the Shred outcome
                        // guard has backed off. Lets us nail the Shred-spam root cause live.
                        pwr = $"mp={meUnit.ManaPercentage:0}% energy={meUnit.Energy} rage={meUnit.Rage} cp={ObjectManager.Me.ComboPoint}"
                            + $" behind={(PlayerIsBehindTarget() ? "Y" : "N")}@{System.Math.Abs(TargetFacingOffset()) * 57.2958:0}deg shredFail={(PositionalFailing("Shred") ? "Y" : "N")}";
                        break;
                    case "Warrior":
                        pwr = $"rage={meUnit.Rage}";
                        break;
                    default:
                        pwr = $"mp={meUnit.ManaPercentage:0}%";
                        break;
                }
                // Shaman totem diagnostic: WHY a totem gets re-cast. Shows what the own-totem detection (_totemsCache,
                // = ctx.Totems) actually sees, then EVERY totem-flagged unit nearby with its owner GUIDs + coords — so a
                // re-drop loop reveals whether the totem is simply undetected (owner-GUID mismatch / flag / not-alive)
                // rather than out of range. me= is our GUID to compare against each totem's sum(SummonedBy)/cre(CreatedBy).
                string totemDump = "";
                if (meUnit.WowClass.ToString() == "Shaman")
                {
                    string detected = string.Join(", ", Totems.Select(x => $"{x.Name}@{x.Distance:0.#}"));
                    string flagged = string.Join(", ", ObjectManager.GetObjectWoWUnit()
                        .Where(o => o != null && o.IsValid
                                    && (o.UnitFlags & wManager.Wow.Enums.UnitFlags.Totem) != 0 && o.GetDistance <= 60)
                        .Select(o => $"{o.Name}@{o.GetDistance:0.#}({o.Position.X:0.#},{o.Position.Y:0.#},{o.Position.Z:0.#})"
                                     + $" alive={o.IsAlive} sum={o.SummonedBy} cre={o.CreatedBy} mine={(o.SummonedBy == me || o.CreatedBy == me ? "Y" : "N")}"));
                    totemDump = $" | TOTEMS detected=[{detected}] | flagged nearby (me={me}): [{flagged}]";
                }
                DebugLog.Write($"pos me=({mp.X:0.#},{mp.Y:0.#},{mp.Z:0.#}) hp={meUnit.HealthPercent:0}% {pwr} "
                    + $"| tgt {unit.Name}@{unit.GetDistance:0.#} hp={unit.HealthPercent:0}% onMe={unit.IsTargetingMe} ({tp.X:0.#},{tp.Y:0.#},{tp.Z:0.#}) "
                    + $"| auras: {auras}{petStr}"
                    + (self.Length > 0 ? $" | self: {self}" : "")
                    + (others.Length > 0 ? $" | others: {others}" : "")
                    + totemDump);
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

        // Weapon temp-enchant (poison) state, cached ~1s so the rogue's OOC poison upkeep can read it every tick
        // without a Lua round-trip each time. Reapplication is ~hourly, so a 1s cache is plenty fresh; applying a
        // poison invalidates it (_weaponEnchantFresh=false) so the next read reflects the new enchant immediately.
        private const int WeaponEnchantTtlMs = 1000;
        private WeaponEnchant _weaponEnchant;
        private int _weaponEnchantAt;
        private bool _weaponEnchantFresh;
        private int _weaponEnchantLogAt;

        public WeaponEnchant GetWeaponEnchant()
        {
            if (_weaponEnchantFresh && unchecked(Now - _weaponEnchantAt) < WeaponEnchantTtlMs) return _weaponEnchant;

            bool mainEquipped = ObjectManager.Me.GetEquipedItemBySlot(InventorySlot.INVSLOT_MAINHAND) != 0;
            bool offEquipped = ObjectManager.Me.GetEquipedItemBySlot(InventorySlot.INVSLOT_OFFHAND) != 0;

            // GetWeaponEnchantInfo() -> hasMain, mainExpirationMs, mainCharges, hasOff, offExpirationMs, offCharges.
            // Do the has-enchant truthy test IN Lua and return the remaining ms (or 0 when a hand has no enchant).
            // The has-flag is 1/nil on 3.3.5a (NOT true/false), so a C# `tostring(hm)=="true"` test reads EVERY hand
            // as un-enchanted and re-poisons it forever (the bug Talamin saw — Instant Poison re-applied to the main
            // hand every ~5s). `(hm and me) or 0` is truthy for 1 or true and yields 0 when not enchanted.
            string[] r = Lua.LuaDoString<string[]>(
                "local hm, me, _, ho, oe = GetWeaponEnchantInfo(); " +
                "return tostring((hm and me) or 0), tostring((ho and oe) or 0);");
            int mainMs = (r != null && r.Length > 0) ? ParseIntSafe(r[0]) : 0;
            int offMs = (r != null && r.Length > 1) ? ParseIntSafe(r[1]) : 0;

            _weaponEnchant = new WeaponEnchant(mainEquipped, mainMs, offEquipped, offMs);
            _weaponEnchantAt = Now;
            _weaponEnchantFresh = true;
            if (unchecked(Now - _weaponEnchantLogAt) >= 5000) // throttled diagnostic (this runs ~1/s OOC otherwise)
            {
                _weaponEnchantLogAt = Now;
                DebugLog.Write($"weaponEnchant main={mainMs}ms(eq={mainEquipped}) off={offMs}ms(eq={offEquipped})");
            }
            return _weaponEnchant;
        }

        private static int ParseIntSafe(string s) => int.TryParse(s, out int v) ? v : 0;

        private bool _offhandWeapon;
        private int _offhandWeaponAt;
        // OffhandHasWeapon() = 1/true when a WEAPON (not a shield/held item) is in the off-hand. Cached ~1s (it only
        // changes on an equip). Lets the shaman off-hand imbue skip a shield instead of re-casting Rockbiter forever.
        public bool OffHandHasWeapon
        {
            get
            {
                if (unchecked(Now - _offhandWeaponAt) < WeaponEnchantTtlMs) return _offhandWeapon;
                _offhandWeaponAt = Now;
                try { _offhandWeapon = Lua.LuaDoString<bool>("return (OffhandHasWeapon() or 0) ~= 0"); }
                catch { _offhandWeapon = false; }
                return _offhandWeapon;
            }
        }

        // Cast a shaman weapon imbue AND confirm the "replace your weapon enchant" popup WoW raises — without that the
        // enchant never lands and WeaponImbue re-casts forever (Talamin's Rockbiter spam). The popup appears async, so
        // click it ~100ms later off the tick thread (mirrors the old AIO ApplyEnchant). Guarded on IsVisible so we only
        // click when a popup is actually up.
        public CastResult ImbueWeapon(string spell)
        {
            CastResult r = Cast(spell, Me);
            if (r == CastResult.Success)
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                        Lua.LuaDoString("if StaticPopup1Button1 and StaticPopup1Button1:IsVisible() then StaticPopup1Button1:Click() end");
                    }
                    catch { }
                });
            return r;
        }

        public bool HasItemById(uint itemId) => ItemsManager.HasItemById(itemId);

        // Apply a poison item to a weapon: plant the character so the apply lands, USE the poison (which enters the
        // apply-to-weapon CURSOR mode), then PickupInventoryItem on the chosen slot applies it and clicking the static
        // popup confirms replacing any existing enchant. Off-hand's Lua slot token is "SecondaryHandSlot".
        //
        // CRITICAL: use the poison BY NAME (resolved from the id), NOT ItemsManager.UseItem(uint). The by-id overload
        // does a plain "use", which for a poison lands on the MAIN hand regardless of the slot we then pick — so every
        // poison overwrote the main weapon and the off hand never got one (Talamin's bug). UseItem(name) is the proven
        // mechanic from the old AIO ApplyPoison: it puts the poison on the cursor so PickupInventoryItem(slot) targets
        // the hand we chose. GetItemInfo returns the client-localized name, which UseItem(name) matches — so this is
        // still locale-safe. The Core step's RecastDelay throttles re-issue.
        public void ApplyPoisonToWeapon(uint poisonItemId, bool mainHand)
        {
            if (!ItemsManager.HasItemById(poisonItemId)) return; // not carried — nothing to apply

            string poisonName = Lua.LuaDoString<string>("local n = GetItemInfo(" + poisonItemId + "); return n or '';");
            if (string.IsNullOrEmpty(poisonName)) return; // name not in the client cache yet → skip, retry next tick

            string slot = mainHand ? "MainHandSlot" : "SecondaryHandSlot";

            MovementManager.StopMove();
            MovementManager.StopMoveTo(false, 1000);
            ItemsManager.UseItem(poisonName);
            Lua.LuaDoString("PickupInventoryItem(GetInventorySlotInfo(\"" + slot + "\")); StaticPopup1Button1:Click();");

            _weaponEnchantFresh = false; // force a fresh read next tick (the enchant just changed)
            DebugLog.Write($"Applied poison {poisonName} ({poisonItemId}) on {slot}");
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

        // Pet happiness: GetPetHappiness()'s first return (1 unhappy / 2 content / 3 happy). 0 when there's no pet
        // (defensive — the Lua returns nil, which LuaDoString<int> reads back as 0). Mirrors the old PetHelper.
        public int PetHappiness
        {
            get
            {
                WoWUnit pet = ObjectManager.Pet;
                if (pet == null || !pet.IsValid || pet.Guid == 0) return 0; // no pet → no upkeep
                try { return Lua.LuaDoString<int>("local h = GetPetHappiness() return h or 0"); }
                catch { return 0; }
            }
        }

        // Brief cache of the pet's accepted food TYPES (GetPetFoodTypes, e.g. "Meat" or "Meat,Fish"). It's static
        // per pet, and FeedPet runs rarely (only while unhappy, on a 5s recast), so a short TTL keyed on the pet
        // keeps the Lua read off the hot path without going stale across a re-tame (GUID change clears it).
        private ulong _petFoodTypesGuid;
        private int _petFoodTypesAt;
        private string _petFoodTypes = "";

        private string PetFoodTypes(WoWUnit pet)
        {
            ulong guid = (pet != null && pet.IsValid) ? pet.Guid : 0;
            if (guid != 0 && guid == _petFoodTypesGuid && unchecked(Now - _petFoodTypesAt) < 10000) return _petFoodTypes;
            string types;
            try { types = Lua.LuaDoString<string>("return GetPetFoodTypes() or ''") ?? ""; }
            catch { types = ""; }
            _petFoodTypes = types;
            _petFoodTypesGuid = guid;
            _petFoodTypesAt = Now;
            return types;
        }

        // Feed the pet the first in-bag food whose type the pet accepts. Mirrors the old PetHelper.Feed exactly:
        // read the accepted food types, walk the type→names map, find the first food we carry
        // (GetItemCountByNameLUA > 0), then CastSpellByName('Feed Pet') + UseItemByName(food). Returns false when no
        // matching food is in the bags (the rotation step then falls through). Defensive: never throws.
        public bool FeedPet(IReadOnlyDictionary<string, IReadOnlyList<string>> foodByType)
        {
            if (foodByType == null) return false;
            WoWUnit pet = ObjectManager.Pet;
            if (pet == null || !pet.IsValid || pet.Guid == 0) return false; // no pet → nothing to feed

            string types = PetFoodTypes(pet);
            if (string.IsNullOrEmpty(types)) return false;

            try
            {
                // GetPetFoodTypes() returns the accepted types as a comma-joined string (e.g. "Meat,Fish"); a
                // substring match is safe because the keys (Meat/Fungus/Fish/Fruit/Bread) don't overlap one another.
                foreach (KeyValuePair<string, IReadOnlyList<string>> entry in foodByType)
                {
                    if (!types.Contains(entry.Key)) continue; // pet doesn't accept this food type
                    foreach (string food in entry.Value)
                    {
                        if (ItemsManager.GetItemCountByNameLUA(food) <= 0) continue; // not in the bags
                        Lua.LuaDoString("CastSpellByName('Feed Pet')", false);
                        Lua.LuaDoString($"UseItemByName('{food}')", false);
                        DebugLog.Write($"FeedPet: feeding {pet.Name} a '{food}'");
                        return true;
                    }
                }
            }
            catch { return false; }
            return false; // accepted a type but carry none of its foods
        }

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
            // The pet's action bar is static per demon (a warlock pet never learns new abilities; a re-tamed
            // hunter pet changes its GUID, invalidating the cache immediately above), so cache it for a long TTL.
            // The old 5s TTL re-ran the ~14ms GetPetActionInfo Lua scan every 5s even OOC/idle, which showed up
            // as "Pet autocast Firebolt 14ms" in the perf log (the first PetHasAbility caller after each expiry).
            if (guid == _petBarGuid && unchecked(Now - _petBarAt) < 60000) return _petAbilities;

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
