using System.Collections.Generic;
using AIO3.Core.Game;

namespace AIO3.Core.Testing
{
    /// <summary>A pure-data unit for offline tests and simulation.</summary>
    public sealed class FakeUnit : IWowUnit
    {
        public ulong Guid { get; set; }
        public int Entry { get; set; }
        public string Name { get; set; } = "";
        public bool IsAlive { get; set; } = true;
        public int Level { get; set; } // 0 by default → never treated as grey in tests unless set
        public double HealthPercent { get; set; } = 100;
        public double PowerPercent { get; set; } = 100;
        public int Rage { get; set; }
        public int Energy { get; set; }
        public int RunicPower { get; set; }
        public float Distance { get; set; }

        // 2D position for DistanceTo tests. The player is conceptually at the origin; a unit's Distance is its
        // player-distance and is independent of X/Y. Tests that exercise DistanceTo set X/Y to place a cluster.
        public float X, Y;

        public float DistanceTo(IWowUnit other)
        {
            if (other is FakeUnit f)
            {
                float dx = X - f.X, dy = Y - f.Y;
                return (float)System.Math.Sqrt((dx * dx) + (dy * dy));
            }
            return System.Math.Abs(Distance - (other?.Distance ?? 0f));
        }
        public bool IsCasting { get; set; }
        public int CastingSpellId { get; set; }
        public Reaction Reaction { get; set; } = Reaction.Hostile;
        public bool IsTargetingMe { get; set; }
        public bool IsTargetingMyPet { get; set; }
        public ulong TargetGuid { get; set; }
        public ulong PetOwnerGuid { get; set; } // 0 = not a pet; set to an owner's Guid to make this a pet
        public bool IsAttackable { get; set; } = true;
        public bool IsElite { get; set; }
        public bool IsCaster { get; set; } // has a mana pool → kite logic treats it as a caster (burst, don't kite)
        public string CreatureType { get; set; } = "";

        public sealed class Aura
        {
            public int Stacks = 1;
            public bool Mine;
            public long TimeLeftMs;
        }

        public readonly Dictionary<string, Aura> Auras = new Dictionary<string, Aura>();

        public FakeUnit WithAura(string name, int stacks = 1, bool mine = false, long timeLeftMs = 0)
        {
            Auras[name] = new Aura { Stacks = stacks, Mine = mine, TimeLeftMs = timeLeftMs };
            return this;
        }

        public bool HasAura(string name) => Auras.ContainsKey(name);
        public int AuraStacks(string name) => Auras.TryGetValue(name, out Aura a) ? a.Stacks : 0;
        public bool HasMyAura(string name) => Auras.TryGetValue(name, out Aura a) && a.Mine;
        public long MyAuraTimeLeftMs(string name) => Auras.TryGetValue(name, out Aura a) && a.Mine ? a.TimeLeftMs : 0;
    }
}
