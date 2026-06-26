namespace AIO3.Core.Game
{
    /// <summary>
    /// Temporary weapon-enchant (poison) state for both weapon slots, read in one shot from the WoW
    /// GetWeaponEnchantInfo() API: whether each hand holds a weapon and how much of its temp enchant (poison)
    /// remains, in milliseconds (0 = none / expired). The rogue's poison upkeep uses this to decide which hand
    /// needs reapplying.
    /// </summary>
    public readonly struct WeaponEnchant
    {
        public bool MainHandEquipped { get; }
        public int MainHandRemainingMs { get; }
        public bool OffHandEquipped { get; }
        public int OffHandRemainingMs { get; }

        public WeaponEnchant(bool mainHandEquipped, int mainHandRemainingMs, bool offHandEquipped, int offHandRemainingMs)
        {
            MainHandEquipped = mainHandEquipped;
            MainHandRemainingMs = mainHandRemainingMs;
            OffHandEquipped = offHandEquipped;
            OffHandRemainingMs = offHandRemainingMs;
        }
    }
}
