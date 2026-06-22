namespace AIO3.Core.Rotations.Mage
{
    public enum MageSpec
    {
        Arcane,
        Fire,
        Frost
    }

    /// <summary>
    /// Resolves which mage spec to run, combining a manual override with talent auto-detection.
    /// WoW's mage talent tab order is Arcane (1), Fire (2), Frost (3); 0 = none spent yet. All three are
    /// solo leveling specs, so all are selectable. With no points yet we default to Frost — the safest
    /// leveling spec (kiting + survivability).
    /// </summary>
    public static class MageSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Frost", "Fire", "Arcane" };

        public static MageSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Frost": return MageSpec.Frost;
                case "Fire": return MageSpec.Fire;
                case "Arcane": return MageSpec.Arcane;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return MageSpec.Arcane;
                        case 2: return MageSpec.Fire;
                        case 3: return MageSpec.Frost;
                        default: return MageSpec.Frost; // no points yet → leveling default
                    }
            }
        }
    }
}
