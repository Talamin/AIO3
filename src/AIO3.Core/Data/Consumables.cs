namespace AIO3.Core.Data
{
    /// <summary>
    /// Consumable item names used by the emergency-heal logic. The bot uses the first one that is
    /// present in bags and off cooldown. Names are enUS; add localized names if needed.
    /// Healthstone list ported from the old AIO Consumables; common health potions added.
    /// </summary>
    public static class Consumables
    {
        public static readonly string[] HealthItems =
        {
            // Healthstones (warlock)
            "Minor Healthstone", "Lesser Healthstone", "Healthstone", "Greater Healthstone",
            "Major Healthstone", "Master Healthstone", "Fel Healthstone", "Demonic Healthstone",

            // Health potions (classic -> WotLK)
            "Minor Healing Potion", "Lesser Healing Potion", "Healing Potion", "Greater Healing Potion",
            "Superior Healing Potion", "Major Healing Potion", "Super Healing Potion",
            "Crystal Healing Potion", "Runic Healing Potion", "Endless Healing Potion",
            "Powerful Rejuvenation Potion"
        };
    }
}
