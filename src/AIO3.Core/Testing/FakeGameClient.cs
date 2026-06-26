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

        /// <summary>The player's pet, or null for petless (no pet tamed / below taming level).</summary>
        public FakeUnit PetUnit;

        public readonly List<IWowUnit> EnemyList = new List<IWowUnit>();
        public readonly List<IWowUnit> PartyList = new List<IWowUnit>();

        /// <summary>GUIDs the pet was told to attack, in order (assert pet commands in tests).</summary>
        public readonly List<ulong> PetAttackLog = new List<ulong>();

        /// <summary>Abilities on the pet's action bar (e.g. "Growl"). A pet without an entry simply lacks it.</summary>
        public readonly HashSet<string> PetAbilities = new HashSet<string>();

        /// <summary>Pet abilities that were cast, in order.</summary>
        public readonly List<string> PetCastLog = new List<string>();

        /// <summary>If empty, every spell counts as known (convenient default).</summary>
        public readonly HashSet<string> KnownSpells = new HashSet<string>();

        /// <summary>Spells explicitly NOT known — overrides the all-known default, so a single spell can be made
        /// unknown (e.g. a low-level mage without Frost Nova) without whitelisting everything else.</summary>
        public readonly HashSet<string> UnknownSpells = new HashSet<string>();

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
        public bool DeadOrGhostFlag;
        public bool SwimmingFlag;
        public bool RootedFlag;
        public bool CannibalizeCorpseFlag;
        public bool RestingFlag;     // sitting/eating to recover, for PlayerIsResting
        public bool HarmfulAuraFlag; // a debuff/DoT on the player, for PlayerHasHarmfulAura
        public bool BehindTargetFlag; // player is in the target's rear arc, for PlayerIsBehindTarget

        /// <summary>Dispel types currently on the player ("Poison"/"Disease"/…) for PlayerHasDebuffType.</summary>
        public readonly HashSet<string> DebuffTypes = new HashSet<string>();
        public bool ProductFightingFlag;
        public bool AutoAttacking;
        public WowClass Class = WowClass.None;
        public int TalentTab; // highest talent tab (0 = none)
        public string StanceName = "";

        /// <summary>The player's combo points on the current target (0..5), returned by <see cref="ComboPoints"/>.</summary>
        public int ComboPointCount;

        /// <summary>Whether the player is stealthed, returned by <see cref="PlayerIsStealthed"/>.</summary>
        public bool Stealthed;

        /// <summary>Names of spells that were cast, in order.</summary>
        public readonly List<string> CastLog = new List<string>();

        /// <summary>Items considered present + off cooldown; UseFirstReadyItem picks from these.</summary>
        public readonly HashSet<string> ReadyItems = new HashSet<string>();
        public readonly List<string> UsedItems = new List<string>();

        public IWowUnit Me => MeUnit;
        public IWowUnit Target => TargetUnit;
        public IWowUnit Pet => PetUnit;
        public WowClass PlayerClass => Class;
        public int HighestTalentTab => TalentTab;
        public string ActiveStanceName => StanceName;
        public int ComboPoints => ComboPointCount;
        public bool PlayerIsStealthed => Stealthed;
        public IReadOnlyList<IWowUnit> Enemies => EnemyList;
        public IReadOnlyList<IWowUnit> Party => PartyList;

        public bool IsSpellKnown(string spell) => !UnknownSpells.Contains(spell) && (KnownSpells.Count == 0 || KnownSpells.Contains(spell));
        public bool IsSpellReady(string spell) => !SpellsOnCooldown.Contains(spell);
        public bool IsCurrentSpell(string spell) => CurrentSpells.Contains(spell);
        public float SpellRange(string spell) => SpellRanges.TryGetValue(spell, out float r) ? r : DefaultSpellRange;
        public int GlobalCooldownRemainingMs => Gcd;
        public bool PlayerIsCasting => Casting;
        public bool PlayerIsMoving => Moving;
        public bool PlayerIsMounted => Mounted;
        public bool PlayerInCombat => InCombatFlag;
        public bool PlayerIsDeadOrGhost => DeadOrGhostFlag;
        public bool PlayerIsSwimming => SwimmingFlag;
        public bool PlayerIsRooted => RootedFlag;
        public bool PlayerHasDebuffType(string dispelType) => DebuffTypes.Contains(dispelType);
        public bool HasCannibalizeCorpseNearby() => CannibalizeCorpseFlag;
        public bool PlayerIsResting => RestingFlag;
        public bool PlayerHasHarmfulAura() => HarmfulAuraFlag;
        public bool PlayerIsBehindTarget() => BehindTargetFlag;
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

        /// <summary>How many times <see cref="HoldPosition"/> was called (the summon pins the char for its long
        /// cast) and the last hold duration requested.</summary>
        public int HoldPositionCalls;
        public int LastHoldMs;

        public void HoldPosition(int ms) { HoldPositionCalls++; LastHoldMs = ms; }

        /// <summary>Last value passed to <see cref="SetManageBagFoodDrink"/> (null = never called).</summary>
        public bool? ManageBagFoodDrinkSet;

        public void SetManageBagFoodDrink(bool on) => ManageBagFoodDrinkSet = on;

        /// <summary>Yard amounts StepBack was asked for; StepBackResult controls whether it "succeeds"
        /// (false simulates a refused move — blocked / cliff).</summary>
        public readonly List<float> StepBackLog = new List<float>();
        public bool StepBackResult = true;

        public bool StepBack(float yards)
        {
            StepBackLog.Add(yards);
            return StepBackResult;
        }

        public void PetAttack(IWowUnit target)
        {
            if (target != null) PetAttackLog.Add(target.Guid);
        }

        public bool PetHasAbility(string name) => PetAbilities.Contains(name);

        /// <summary>Pet abilities currently on cooldown (so PetAbilityReady returns false for them).</summary>
        public readonly HashSet<string> PetAbilitiesOnCooldown = new HashSet<string>();

        public bool PetAbilityReady(string name) => PetAbilities.Contains(name) && !PetAbilitiesOnCooldown.Contains(name);

        /// <summary>Pet happiness (1 unhappy / 2 content / 3 happy; 0 = no pet), returned by <see cref="PetHappiness"/>.</summary>
        public int PetHappinessValue;
        public int PetHappiness => PetHappinessValue;

        /// <summary>Whether <see cref="FeedPet"/> "succeeds" (true = it found + used a food). Set false to simulate
        /// no matching food in the bags so the rotation step falls through.</summary>
        public bool FeedPetResult = true;

        /// <summary>How many times <see cref="FeedPet"/> was invoked (assert the feed step actually ran).</summary>
        public int FeedPetCalls;

        public bool FeedPet(IReadOnlyDictionary<string, IReadOnlyList<string>> foodByType)
        {
            FeedPetCalls++;
            return FeedPetResult;
        }

        public bool CastPetAbility(string name)
        {
            if (!PetAbilities.Contains(name)) return false;
            PetCastLog.Add(name);
            return true;
        }

        /// <summary>Current autocast state set per ability via SetPetAutocast (assert in tests). Missing = unset.</summary>
        public readonly Dictionary<string, bool> PetAutocast = new Dictionary<string, bool>();

        /// <summary>How many times SetPetAutocast actually applied (the pet had the ability) — to assert throttling.</summary>
        public int PetAutocastCalls;

        public void SetPetAutocast(string ability, bool on)
        {
            if (!PetAbilities.Contains(ability)) return; // mirror the adapter's "only if known" guard
            PetAutocast[ability] = on;
            PetAutocastCalls++;
        }

        /// <summary>Set by tests to exercise the host's "pause while repositioning" gate.</summary>
        public bool Repositioning;
        public bool ServiceReposition() => Repositioning;

        /// <summary>Number of times BlinkAway was invoked; BlinkAwayResult controls whether it "succeeds".</summary>
        public int BlinkAwayCount;
        public bool BlinkAwayResult = true;
        public bool BlinkAway()
        {
            BlinkAwayCount++;
            return BlinkAwayResult;
        }

        /// <summary>Per-item bag counts for CountItems (e.g. conjured food). Missing = 0.</summary>
        public readonly Dictionary<string, int> ItemCounts = new Dictionary<string, int>();
        public int CountItems(IReadOnlyList<string> names)
        {
            int total = 0;
            foreach (string n in names)
                if (ItemCounts.TryGetValue(n, out int c)) total += c;
            return total;
        }

        /// <summary>Weapon-enchant state returned by <see cref="GetWeaponEnchant"/>. Default = nothing equipped /
        /// 0 ms; a poison test opts in by setting the equipped hands + remaining time.</summary>
        public WeaponEnchant WeaponEnchantState;
        public WeaponEnchant GetWeaponEnchant() => WeaponEnchantState;

        /// <summary>Item ids considered present in the bags, for <see cref="HasItemById"/> (e.g. poison ranks).</summary>
        public readonly HashSet<uint> ItemIdsInBags = new HashSet<uint>();
        public bool HasItemById(uint itemId) => ItemIdsInBags.Contains(itemId);

        /// <summary>Poisons applied via <see cref="ApplyPoisonToWeapon"/>, in order (id + which hand).</summary>
        public readonly List<PoisonApplication> AppliedPoisons = new List<PoisonApplication>();
        public void ApplyPoisonToWeapon(uint poisonItemId, bool mainHand)
            => AppliedPoisons.Add(new PoisonApplication(poisonItemId, mainHand));

        /// <summary>GUIDs Polymorph was cast on, in order; PolymorphResult controls whether it "succeeds"
        /// (false simulates the add not being a sheepable type / unknown).</summary>
        public readonly List<ulong> PolymorphLog = new List<ulong>();
        public bool PolymorphResult = true;
        public bool Polymorph(IWowUnit add)
        {
            if (add == null) return false;
            PolymorphLog.Add(add.Guid);
            return PolymorphResult;
        }

        public void RunLocked(Action action) => action();
    }

    /// <summary>A recorded poison application (which item id went on which hand) for asserting in tests.</summary>
    public readonly struct PoisonApplication
    {
        public readonly uint PoisonId;
        public readonly bool MainHand;
        public PoisonApplication(uint poisonId, bool mainHand) { PoisonId = poisonId; MainHand = mainHand; }
    }
}
