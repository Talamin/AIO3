namespace AIO3.Core.Rotations.Warlock
{
    /// <summary>
    /// Default talent build per warlock spec, ported verbatim from the old AIO TalentsManager
    /// (Warlock_SoloAffliction / Warlock_SoloDemonology / Warlock_SoloDestruction). The trainer applies the
    /// progression in order, spending points as they become available while leveling.
    ///
    /// Only Affliction is wired into a spec in Phase 1; the Demonology / Destruction codes are kept here so
    /// the data is ready when those specs land (and so a player on those trees auto-assigns the right build
    /// even though the rotation falls back to Affliction for now).
    /// </summary>
    public static class WarlockTalents
    {
        public static string[] For(WarlockSpec spec)
        {
            switch (spec)
            {
                case WarlockSpec.Demonology: return Demonology;
                case WarlockSpec.Destruction: return Destruction;
                default: return Affliction;
            }
        }

        // Affliction (old Warlock_SoloAffliction)
        private static readonly string[] Affliction =
        {
            "033002200100000000000000000000000000000000000000000000000000000000000000000000000",
            "235002200100300000000000000000000000000000000000000000000000000000000000000000000",
            "235002200102301000000000000000000000000000000000000000000000000000000000000000000",
            "235002200102341023001000000000000000000000000000000000000000000000000000000000000",
            "235002200102341023301000000000000000000000000000000000000000000000000000000000000",
            "235002200102341023351010110000000000000000000000000000000000000000000000000000000",
            "235002200102341023351013115100000000000000000000000000000000000000000000000000000",
            "235002200102341023351013115100020000000000000000000000000000000000000000000000000",
            "235002200102351023351033115100322030113020000000000000000000000000000000000000000"
        };

        // Demonology (old Warlock_SoloDemonology)
        private static readonly string[] Demonology =
        {
            "000000000000000000000000000000320330113520253013300100000000000000000000000000000",
            "000000000000000000000000000000320330113520253013523134100000000000000000000000000",
            "000000000000000000000000000000320330113520253013523134155000005000000000000000000"
        };

        // Destruction (old Warlock_SoloDestruction)
        private static readonly string[] Destruction =
        {
            "000000000000000000000000000000000000000000000000000000005000000000000000000000000",
            "000000000000000000000000000000020000000000000000000000005000000000000000000000000",
            "000000000000000000000000000000020000000000000000000000005203205210331051335230351",
            "000000000000000000000000000003320030002000000000000000005203205210331051335230351"
        };
    }
}
