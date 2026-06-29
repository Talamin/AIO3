namespace AIO3.Core.Rotations.Shaman
{
    public enum ShamanSpec
    {
        Elemental,
        Enhancement,
        Restoration
    }

    /// <summary>
    /// Resolves which shaman spec to run, combining a manual override with talent auto-detection. WoW's shaman
    /// talent tab order is Elemental (1), Enhancement (2), Restoration (3); 0 = no points spent yet.
    ///
    /// AIO3 is solo-only for now, so the two DPS specs ship rotations: Elemental (caster) and Enhancement (melee).
    /// Restoration is a deferred healer (like the Priest's Discipline/Holy, the Druid's Restoration): it resolves
    /// here so its talent build still auto-applies, but the module maps it to the Elemental rotation with a label
    /// note. With no points spent yet we default to Enhancement — the standard solo leveling spec (a shaman levels
    /// fastest in melee with weapon imbues + Stormstrike before Elemental scaling kicks in).
    /// </summary>
    public static class ShamanSpecs
    {
        public const string Auto = "Auto";

        public static readonly string[] Choices = { Auto, "Elemental", "Enhancement", "Restoration" };

        public static ShamanSpec Resolve(string choice, int highestTalentTab)
        {
            switch (choice)
            {
                case "Elemental": return ShamanSpec.Elemental;
                case "Enhancement": return ShamanSpec.Enhancement;
                case "Restoration": return ShamanSpec.Restoration;
                default: // Auto → detect from talents
                    switch (highestTalentTab)
                    {
                        case 1: return ShamanSpec.Elemental;
                        case 2: return ShamanSpec.Enhancement;
                        case 3: return ShamanSpec.Restoration;
                        default: return ShamanSpec.Enhancement; // no points yet → solo leveling default
                    }
            }
        }
    }
}
