namespace AIO3.Core.Rotations.Rogue
{
    public enum RogueSpec
    {
        Assassination,
        Combat,
        Subtlety
    }

    /// <summary>
    /// Resolves which rogue spec to run, combining a manual override with talent auto-detection. WoW's rogue
    /// talent tab order is Assassination (1), Combat (2), Subtlety (3); 0 = no points spent yet.
    ///
    /// Combat is the solo leveling spec, so it is the Auto default (and the fallback for "no points yet" or any
    /// unrecognised value). Combat and Assassination both ship rotations; Subtlety resolves here so its talent
    /// build still auto-applies, but the module maps it to the Combat rotation (it is intentionally not built).
    /// </summary>
    public static class RogueSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Assassination", "Combat", "Subtlety" };

        public static RogueSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Assassination": return RogueSpec.Assassination;
                case "Combat": return RogueSpec.Combat;
                case "Subtlety": return RogueSpec.Subtlety;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return RogueSpec.Assassination;
                        case 2: return RogueSpec.Combat;
                        case 3: return RogueSpec.Subtlety;
                        default: return RogueSpec.Combat; // no points yet → leveling default
                    }
            }
        }
    }
}
