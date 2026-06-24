namespace AIO3.Core.Rotations.Rogue
{
    /// <summary>
    /// Default talent build per rogue spec, ported from the old AIO TalentsManager. The trainer applies the
    /// progression in order, spending points as they become available while leveling.
    ///
    /// Combat is the only spec wired to a rotation in this phase; the build is the old <c>Rogue_SoloCombat</c>
    /// progression (identical to <c>Rogue_GroupCombat</c> there). Assassination uses the old
    /// <c>Rogue_GroupAssassination</c> codes — the old <c>Rogue_SoloAssassination</c> block was commented out as
    /// "Seems incorrect?", so the group build is the trustworthy Assassination progression to ship now (so a
    /// player on that tree still auto-assigns a sane build even though the rotation falls back to Combat until
    /// SoloAssassination lands). Subtlety is not built and has no curated code in the old project, so it falls
    /// back to the Combat build for now.
    ///
    /// TODO: add a real Subtlety leveling build when Subtlety is implemented (the old AIO had no Subtlety rogue
    /// codes to port).
    /// </summary>
    public static class RogueTalents
    {
        public static string[] For(RogueSpec spec)
        {
            switch (spec)
            {
                case RogueSpec.Assassination: return Assassination;
                case RogueSpec.Subtlety: return Combat; // TODO: dedicated Subtlety build (none in the old AIO)
                default: return Combat;
            }
        }

        // Combat (old Rogue_SoloCombat / Rogue_GroupCombat — identical there).
        private static readonly string[] Combat =
        {
            "00000000000000000000000000002303201000000000000000000000000000000000000000000000000",
            "00000000000000000000000000002303521000040100000000000000000000000000000000000000000",
            "00000000000000000000000000002513521000050100000000000000000000000000000000000000000",
            "00000000000000000000000000002513521000050102200000000000000000000000000000000000000",
            "00000000000000000000000000002523521000050102201000000000000000000000000000000000000",
            "00000000000000000000000000002523521000050102231000000000000000000000000000000000000",
            "00000000000000000000000000002523521000150102231005010000000000000000000000000000000",
            "00000000000000000000000000002523521000150102231005212510000000000000000000000000000",
            "02000000000000000000000000002523521000150102231005212510000000000000000000000000000",
            "32500010504000000000000000002523521000150102231005212510000000000000000000000000000"
        };

        // Assassination (old Rogue_GroupAssassination — the old SoloAssassination build was commented out as
        // "Seems incorrect?", so this curated group build is the trustworthy Assassination progression to ship).
        private static readonly string[] Assassination =
        {
            "00530200505000000000000000000000000000000000000000000000000000000000000000000000000",
            "00530200535010252010000000000000000000000000000000000000000000000000000000000000000",
            "00530300535010252010320100000000000000000000000000000000000000000000000000000000000",
            "00530300535010252010333105100500500300000000000000000005020000000000000000000000000"
        };
    }
}
