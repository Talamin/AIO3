namespace AIO3.Core.Rotations.Paladin
{
    /// <summary>
    /// The paladin specs we run for solo leveling/grinding. Holy is intentionally absent: it is a pure
    /// healer and not a solo leveling spec in this context, so a Holy-talented paladin defaults to the
    /// Retribution rotation.
    /// </summary>
    public enum PaladinSpec
    {
        Protection,
        Retribution
    }

    /// <summary>
    /// Resolves which paladin spec to run, combining a manual override choice with talent auto-detection.
    /// Pure and unit-tested. The talent tab indices follow WoW's order (1 = Holy, 2 = Protection,
    /// 3 = Retribution; 0 = no points spent yet, e.g. before level 10). Holy talents (tab 1) and an empty
    /// tree both default to Retribution — the natural solo leveling spec.
    /// </summary>
    public static class PaladinSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Retribution", "Protection" };

        public static PaladinSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Retribution": return PaladinSpec.Retribution;
                case "Protection": return PaladinSpec.Protection;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 2: return PaladinSpec.Protection;
                        case 3: return PaladinSpec.Retribution;
                        default: return PaladinSpec.Retribution; // Holy (1) / none (0) → solo leveling default
                    }
            }
        }
    }
}
