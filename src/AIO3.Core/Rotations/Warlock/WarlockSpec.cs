namespace AIO3.Core.Rotations.Warlock
{
    public enum WarlockSpec
    {
        Affliction,
        Demonology,
        Destruction
    }

    /// <summary>
    /// Resolves which warlock spec to run, combining a manual override with talent auto-detection.
    /// WoW's warlock talent tab order is Affliction (1), Demonology (2), Destruction (3); 0 = none spent yet.
    /// All three solo specs ship, so each tab maps to its own rotation; with no points yet we default to
    /// Affliction (the DoT leveling default).
    /// </summary>
    public static class WarlockSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Affliction", "Demonology", "Destruction" };

        public static WarlockSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Affliction": return WarlockSpec.Affliction;
                case "Demonology": return WarlockSpec.Demonology;
                case "Destruction": return WarlockSpec.Destruction;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return WarlockSpec.Affliction;
                        case 2: return WarlockSpec.Demonology;
                        case 3: return WarlockSpec.Destruction;
                        default: return WarlockSpec.Affliction; // no points yet → leveling default
                    }
            }
        }
    }
}
