namespace AIO3.Core.Rotations.Warrior
{
    public enum WarriorSpec
    {
        Fury,
        Arms,
        Protection
    }

    /// <summary>
    /// Resolves which warrior spec to run, combining a manual override choice with talent
    /// auto-detection. Pure and unit-tested. The talent tab indices follow WoW's order
    /// (1 = Arms, 2 = Fury, 3 = Protection; 0 = no points spent yet, e.g. before level 10).
    /// </summary>
    public static class WarriorSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Fury", "Arms", "Protection" };

        public static WarriorSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Fury": return WarriorSpec.Fury;
                case "Arms": return WarriorSpec.Arms;
                case "Protection": return WarriorSpec.Protection;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return WarriorSpec.Arms;
                        case 2: return WarriorSpec.Fury;
                        case 3: return WarriorSpec.Protection;
                        default: return WarriorSpec.Fury; // no points yet → sensible leveling default
                    }
            }
        }
    }
}
