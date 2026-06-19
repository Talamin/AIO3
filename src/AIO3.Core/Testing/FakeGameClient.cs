using System;
using System.Collections.Generic;
using AIO3.Core.Game;

namespace AIO3.Core.Testing
{
    /// <summary>
    /// In-memory IGameClient for offline tests. Set up the world, run the engine,
    /// then assert on <see cref="CastLog"/>. This is the capability the old code
    /// completely lacked.
    /// </summary>
    public sealed class FakeGameClient : IGameClient
    {
        public FakeUnit MeUnit = new FakeUnit { Name = "Me", Reaction = Reaction.Friendly };
        public FakeUnit TargetUnit;
        public readonly List<IWowUnit> EnemyList = new List<IWowUnit>();
        public readonly List<IWowUnit> PartyList = new List<IWowUnit>();

        /// <summary>If empty, every spell counts as known (convenient default).</summary>
        public readonly HashSet<string> KnownSpells = new HashSet<string>();
        public readonly HashSet<string> SpellsOnCooldown = new HashSet<string>();

        /// <summary>Spells currently queued/casting (e.g. on-next-swing abilities).</summary>
        public readonly HashSet<string> CurrentSpells = new HashSet<string>();

        /// <summary>Per-spell range; unset spells use <see cref="DefaultSpellRange"/> (large = never gated).</summary>
        public readonly Dictionary<string, float> SpellRanges = new Dictionary<string, float>();
        public float DefaultSpellRange = 100f;

        public int Gcd;
        public bool Casting;
        public bool Moving;
        public bool Mounted;
        public bool InCombatFlag;
        public bool ProductFightingFlag;
        public bool AutoAttacking;
        public WowClass Class = WowClass.None;
        public int TalentTab; // highest talent tab (0 = none)
        public string StanceName = "";

        /// <summary>Names of spells that were cast, in order.</summary>
        public readonly List<string> CastLog = new List<string>();

        /// <summary>Items considered present + off cooldown; UseFirstReadyItem picks from these.</summary>
        public readonly HashSet<string> ReadyItems = new HashSet<string>();
        public readonly List<string> UsedItems = new List<string>();

        public IWowUnit Me => MeUnit;
        public IWowUnit Target => TargetUnit;
        public WowClass PlayerClass => Class;
        public int HighestTalentTab => TalentTab;
        public string ActiveStanceName => StanceName;
        public IReadOnlyList<IWowUnit> Enemies => EnemyList;
        public IReadOnlyList<IWowUnit> Party => PartyList;

        public bool IsSpellKnown(string spell) => KnownSpells.Count == 0 || KnownSpells.Contains(spell);
        public bool IsSpellReady(string spell) => !SpellsOnCooldown.Contains(spell);
        public bool IsCurrentSpell(string spell) => CurrentSpells.Contains(spell);
        public float SpellRange(string spell) => SpellRanges.TryGetValue(spell, out float r) ? r : DefaultSpellRange;
        public int GlobalCooldownRemainingMs => Gcd;
        public bool PlayerIsCasting => Casting;
        public bool PlayerIsMoving => Moving;
        public bool PlayerIsMounted => Mounted;
        public bool PlayerInCombat => InCombatFlag;
        public bool ProductIsFighting => ProductFightingFlag;
        public bool PlayerIsAutoAttacking => AutoAttacking;

        public CastResult Cast(string spell, IWowUnit target, bool force = false)
        {
            CastLog.Add(spell);
            return CastResult.Success;
        }

        public bool UseFirstReadyItem(IReadOnlyList<string> names)
        {
            foreach (string n in names)
            {
                if (ReadyItems.Contains(n))
                {
                    UsedItems.Add(n);
                    return true;
                }
            }
            return false;
        }

        /// <summary>GUID passed to the most recent <see cref="SetTarget"/> call (0 = never called).</summary>
        public ulong LastSetTargetGuid;

        public void SetTarget(IWowUnit unit)
        {
            if (unit != null) LastSetTargetGuid = unit.Guid;
        }

        public void RunLocked(Action action) => action();
    }
}
