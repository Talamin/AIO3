namespace AIO3.Core.Rotations.Hunter
{
    public enum HunterSpec
    {
        BeastMastery,
        Marksmanship,
        Survival
    }

    /// <summary>
    /// Resolves which hunter spec to run, combining a manual override with talent auto-detection.
    /// Talent tab order follows WoW's (1 = Beast Mastery, 2 = Marksmanship, 3 = Survival; 0 = none yet).
    /// All three are solo leveling specs (each levels with a pet), so all are selectable.
    /// </summary>
    public static class HunterSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Beast Mastery", "Marksmanship", "Survival" };

        public static HunterSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Beast Mastery": return HunterSpec.BeastMastery;
                case "Marksmanship": return HunterSpec.Marksmanship;
                case "Survival": return HunterSpec.Survival;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return HunterSpec.BeastMastery;
                        case 2: return HunterSpec.Marksmanship;
                        case 3: return HunterSpec.Survival;
                        default: return HunterSpec.BeastMastery; // no points yet → leveling default
                    }
            }
        }
    }
}
