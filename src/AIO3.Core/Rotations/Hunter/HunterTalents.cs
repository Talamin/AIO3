namespace AIO3.Core.Rotations.Hunter
{
    /// <summary>
    /// Default talent build per hunter spec, ported verbatim from the old AIO TalentsManager. The trainer
    /// applies the progression in order, spending points as they become available while leveling. Only the
    /// Beast Mastery build is wired in today; Marksmanship / Survival land with their specs.
    /// </summary>
    public static class HunterTalents
    {
        public static string[] For(HunterSpec spec) => BeastMastery;

        // Beast Mastery (old Hunter_SoloBeastMastery)
        private static readonly string[] BeastMastery =
        {
            "050002000000000000000000000000000000000000000000000000000000000000000000000000000",
            "052012015250120501000000000000000000000000000000000000000000000000000000000000000",
            "052012015250120531005010000000000000000000000000000000000000000000000000000000000",
            "152012015250120531005310510050052000000000000000000000000000000000000000000000000",
            "152012015250120531305310510052052300000000000000000000000000000000000000000000000"
        };
    }
}
