namespace AIO3.Core.Rotations.DeathKnight
{
    public enum DeathKnightSpec
    {
        Blood,
        Frost,
        Unholy
    }

    /// <summary>
    /// Resolves which Death Knight spec to run, combining a manual override with talent auto-detection. WoW's DK
    /// talent tab order is Blood (1), Frost (2), Unholy (3); 0 = no points spent yet.
    ///
    /// AIO3 is solo-only for now, so all three DPS trees ship rotations (a DK has no caster/healer tree — every
    /// tree is a melee DPS/tank spec). With no points spent yet we default to Blood — the standard solo leveling
    /// spec (Blood's self-heals via Death Strike / Rune Tap / Vampiric Blood make it the safest grind tree before
    /// talents tip the scales).
    /// </summary>
    public static class DeathKnightSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Blood", "Frost", "Unholy" };

        public static DeathKnightSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Blood": return DeathKnightSpec.Blood;
                case "Frost": return DeathKnightSpec.Frost;
                case "Unholy": return DeathKnightSpec.Unholy;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return DeathKnightSpec.Blood;
                        case 2: return DeathKnightSpec.Frost;
                        case 3: return DeathKnightSpec.Unholy;
                        default: return DeathKnightSpec.Blood; // no points yet → solo leveling default
                    }
            }
        }
    }
}
