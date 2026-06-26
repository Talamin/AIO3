namespace AIO3.Core.Rotations.Druid
{
    public enum DruidSpec
    {
        Balance,
        Feral,
        Restoration
    }

    /// <summary>
    /// Resolves which druid spec to run, combining a manual override with talent auto-detection. WoW's druid
    /// talent tab order is Balance (1), Feral Combat (2), Restoration (3); 0 = no points spent yet.
    ///
    /// Feral is the solo leveling spec, so it is the Auto default (and the fallback for "no points yet" or any
    /// unrecognised value). Feral and Balance both ship rotations; Restoration is a healer (deferred, like the
    /// Paladin's Holy) — it resolves here so its talent build still auto-applies, but the module maps it to the
    /// Feral rotation with a label note (the same Subtlety→Combat pattern the rogue uses).
    /// </summary>
    public static class DruidSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Balance", "Feral", "Restoration" };

        public static DruidSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Balance": return DruidSpec.Balance;
                case "Feral": return DruidSpec.Feral;
                case "Restoration": return DruidSpec.Restoration;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return DruidSpec.Balance;
                        case 2: return DruidSpec.Feral;
                        case 3: return DruidSpec.Restoration;
                        default: return DruidSpec.Feral; // no points yet → leveling default
                    }
            }
        }
    }
}
