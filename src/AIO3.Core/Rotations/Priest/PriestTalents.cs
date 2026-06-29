namespace AIO3.Core.Rotations.Priest
{
    /// <summary>
    /// Default talent build per priest spec, ported verbatim from the old AIO TalentsManager
    /// (Priest_SoloShadow / Priest_GroupDiscipline / Priest_GroupHoly). The trainer applies the progression in
    /// order, spending points as they become available while leveling.
    ///
    /// Only Shadow is wired into a spec (the solo DPS leveling rotation). Discipline / Holy are deferred healers
    /// with no shipped rotation, but the old project curated sane builds for both, so we ship them here — a player
    /// on the Disc/Holy tree still auto-assigns the right healing build even though the rotation falls back to
    /// Shadow until those specs land. (The old FC stored Disc/Holy as Group* builds; reused as the sane default.)
    /// </summary>
    public static class PriestTalents
    {
        public static string[] For(PriestSpec spec)
        {
            switch (spec)
            {
                case PriestSpec.Discipline: return Discipline;
                case PriestSpec.Holy: return Holy;
                default: return Shadow;
            }
        }

        // Shadow (old Priest_SoloShadow) — the solo leveling DPS build.
        private static readonly string[] Shadow =
        {
            "0000000000000000000000000000000000000000000000000000000300000000000000000000000000",
            "0000000000000000000000000000000000000000000000000000000302023001000000000000000000",
            "0000000000000000000000000000000000000000000000000000000302023041003000000000000000",
            "0000000000000000000000000000000000000000000000000000000302023051013010000000000000",
            "0000000000000000000000000000000000000000000000000000000304023051013012023100000000",
            "0000000000000000000000000000000000000000000000000000000304023051013012023140300000",
            "0000000000000000000000000000000000000000000000000000000304023051013012023151301000",
            "0000000000000000000000000000000000000000000000000000000304023051013012023152301000",
            "0000000000000000000000000000000000000000000000000000000305023051213012023152301051",
            "0503200100000000000000000000000000000000000000000000000305023051213012023152301051",
            "0503203100000000000000000000000000000000000000000000000325023051223012323152301051"
        };

        // Discipline (old Priest_GroupDiscipline) — deferred healer; shipped so a Disc-tree player auto-assigns a
        // sane healing build even though the rotation falls back to Shadow until a Discipline spec is built.
        private static readonly string[] Discipline =
        {
            "0503203130300512331323231251205310030000000000000000000000000000000000000000000000"
        };

        // Holy (old Priest_GroupHoly) — deferred healer; shipped so a Holy-tree player auto-assigns a sane healing
        // build even though the rotation falls back to Shadow until a Holy spec is built.
        private static readonly string[] Holy =
        {
            "0000000000000000000000000000032050030000000000000000000000000000000000000000000000",
            "0000000000000000000000000000034050032302122330000000000000000000000000000000000000",
            "0000000000000000000000000000034050032302142330000300000000000000000000000000000000",
            "0000000000000000000000000000034050032302152430000331351000000000000000000000000000",
            "0000000000000000000000000000235050032302152530000331351000000000000000000000000000",
            "0503203100000000000000000000235050032302152530000331351000000000000000000000000000"
        };
    }
}
