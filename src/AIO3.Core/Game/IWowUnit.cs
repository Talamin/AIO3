namespace AIO3.Core.Game
{
    /// <summary>
    /// Read-only view of a unit. Upper layers only ever see this interface, never a
    /// concrete wManager type. The WRobot adapter and the test fakes both implement it.
    /// </summary>
    public interface IWowUnit
    {
        ulong Guid { get; }

        /// <summary>Creature template entry ID (used for boss detection, etc.).</summary>
        int Entry { get; }

        string Name { get; }
        bool IsAlive { get; }

        /// <summary>0..100.</summary>
        double HealthPercent { get; }

        /// <summary>Primary power (mana/rage/energy) as 0..100.</summary>
        double PowerPercent { get; }

        /// <summary>Current rage (0..100ish). Warrior/Druid; 0 for other classes.</summary>
        int Rage { get; }

        /// <summary>Distance to the local player ("Me"), in yards.</summary>
        float Distance { get; }

        bool IsCasting { get; }

        /// <summary>Spell id the unit is currently casting, or 0.</summary>
        int CastingSpellId { get; }

        Reaction Reaction { get; }
        bool IsTargetingMe { get; }

        /// <summary>True if this unit is currently targeting the player's pet. Lets the pet controller keep
        /// holding a mob it already peeled (so it doesn't thrash back to the main target).</summary>
        bool IsTargetingMyPet { get; }

        /// <summary>GUID of the unit this unit is currently targeting, or 0. Used for pet/target
        /// coordination (e.g. "is my pet already attacking my target?").</summary>
        ulong TargetGuid { get; }

        /// <summary>Whether the player may actually attack this unit (false for friendly NPCs).</summary>
        bool IsAttackable { get; }

        /// <summary>Elite / rare-elite / world-boss classification (tougher than normal mobs).</summary>
        bool IsElite { get; }

        /// <summary>True if the unit has a mana pool — a heuristic for "this is a caster" (it casts from range,
        /// so kiting it is futile; burst it / freeze-shatter instead). Pure melee mobs use rage/energy/no power.</summary>
        bool IsCaster { get; }

        /// <summary>Localized creature type (e.g. "Humanoid", "Elemental"); "" if unknown.</summary>
        string CreatureType { get; }

        /// <summary>True if the unit has the named aura from any caster.</summary>
        bool HasAura(string name);

        /// <summary>Stack count of the named aura (any caster), or 0.</summary>
        int AuraStacks(string name);

        /// <summary>True if the unit has the named aura applied by the local player.</summary>
        bool HasMyAura(string name);

        /// <summary>Remaining duration of my aura on the unit, in ms, or 0.</summary>
        long MyAuraTimeLeftMs(string name);
    }

    public static class WowUnitExtensions
    {
        /// <summary>Hostile or neutral counts as "enemy" (matches the old AIO semantics).</summary>
        public static bool IsEnemy(this IWowUnit unit) => unit != null && unit.Reaction != Reaction.Friendly;

        /// <summary>True if the unit's entry is in the ported boss list.</summary>
        public static bool IsBoss(this IWowUnit unit) => unit != null && Data.BossList.Contains(unit.Entry);
    }
}
