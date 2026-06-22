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

        /// <summary>
        /// Mana-restoring items, best first. The bot uses the first present + off cooldown. Covers the mage's
        /// conjured Mana Gem ranks (Conjure Mana Gem) and the common mana potions, so the same UseItems block
        /// works for a mage that conjured a gem and for any caster carrying potions.
        /// </summary>
        public static readonly string[] ManaItems =
        {
            // Conjured mana gems (mage), highest rank first
            "Mana Sapphire", "Mana Emerald", "Mana Ruby", "Mana Citrine", "Mana Jade", "Mana Agate",

            // Mana potions (classic -> WotLK)
            "Runic Mana Potion", "Super Mana Potion", "Crystal Mana Potion", "Major Mana Potion",
            "Superior Mana Potion", "Greater Mana Potion", "Mana Potion", "Lesser Mana Potion",
            "Minor Mana Potion", "Endless Mana Potion", "Powerful Rejuvenation Potion"
        };

        /// <summary>Mage conjured mana gems, all ranks (count to decide whether to Conjure Mana Gem). Subset of
        /// <see cref="ManaItems"/>; the gem is rechargeable, so the mage only carries one.</summary>
        public static readonly string[] ManaGems =
        {
            "Mana Agate", "Mana Jade", "Mana Citrine", "Mana Ruby", "Mana Emerald", "Mana Sapphire"
        };

        /// <summary>Mage conjured FOOD, all ranks (lowest→highest). The Conjure Refreshment items (Mana Pie /
        /// Mana Strudel) live here too — they restore both, so counting them satisfies the food check and the
        /// water check is skipped when Conjure Refreshment is known. Ported from the old AIO MageFoodManager.</summary>
        public static readonly string[] ConjuredFood =
        {
            "Conjured Muffin", "Conjured Bread", "Conjured Rye", "Conjured Pumpernickel", "Conjured Sourdough",
            "Conjured Sweet Roll", "Conjured Cinnamon Roll", "Conjured Croissant",
            "Conjured Mana Pie", "Conjured Mana Strudel"
        };

        /// <summary>Mage conjured WATER, all ranks (lowest→highest). Ported from the old AIO MageFoodManager.</summary>
        public static readonly string[] ConjuredWater =
        {
            "Conjured Water", "Conjured Fresh Water", "Conjured Purified Water", "Conjured Spring Water",
            "Conjured Mineral Water", "Conjured Sparkling Water", "Conjured Crystal Water",
            "Conjured Mountain Spring Water", "Conjured Glacier Water", "Conjured Mana Biscuit"
        };
    }
}
