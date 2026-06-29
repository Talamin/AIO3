namespace AIO3.Core.Rotations.Priest
{
    public enum PriestSpec
    {
        Discipline,
        Holy,
        Shadow
    }

    /// <summary>
    /// Resolves which priest spec to run, combining a manual override with talent auto-detection. WoW's priest
    /// talent tab order is Discipline (1), Holy (2), Shadow (3); 0 = no points spent yet.
    ///
    /// AIO3 is solo-only for now, so Shadow — the solo DPS spec — is the leveling default and the Auto pick. Both
    /// Discipline and Holy are deferred healers (like the Paladin's Holy / the Druid's Restoration): they resolve
    /// here so their talent build still auto-applies, but the module maps them to the Shadow rotation with a label
    /// note. With no points spent yet we default to Shadow (the solo leveling default).
    /// </summary>
    public static class PriestSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Discipline", "Holy", "Shadow" };

        public static PriestSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Discipline": return PriestSpec.Discipline;
                case "Holy": return PriestSpec.Holy;
                case "Shadow": return PriestSpec.Shadow;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return PriestSpec.Discipline;
                        case 2: return PriestSpec.Holy;
                        case 3: return PriestSpec.Shadow;
                        default: return PriestSpec.Shadow; // no points yet → solo leveling default
                    }
            }
        }
    }
}
