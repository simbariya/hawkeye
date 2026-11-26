namespace Hawkeye.Engine.Services.Timed
{
    /// <summary>
    /// Potion cooldowns for the skill tracker (v0.2.38; owner, 2026-11-26: "GREATER HEALING POTION COOLDOWN CHECK").
    ///
    /// Drinking a potion is a cast (0x3802) like a skill, but its cooldown field is 0 and the game's cooldown list
    /// (0x3847) never holds potions: the client knows the cooldown from its own data. So the length comes from here,
    /// measured on the owner's recordings (2026-11-26, every player's potions, shortest gap between two uses):
    /// - Healing Potion / Superior Healing Potion (the cleanse: removes 1 / 2 status effects): 10.7 to 11.1 s across
    ///   six players, so 10 s (one guide's table says 10 s too). The owner's own "greater" one is the Superior
    ///   (id 2020102: 15 uses in his PvP recording).
    /// - Life Potion / Superior Life Potion (a 10 s heal over time): 29.9 to 30.1 s, dozens of times, so 30 s.
    /// Mana potions: too few uses to tell, left out.
    /// A family shares one cooldown (assumed, as in the first game): drinking one starts the timer for all of them.
    /// </summary>
    public static class PotionCooldowns
    {
        private static readonly (int[] Ids, TimeSpan Cooldown)[] Families =
        [
            ([2020101, 2020102], TimeSpan.FromSeconds(10)),   // Healing Potion, Superior Healing Potion
            ([2011101, 2011102], TimeSpan.FromSeconds(30)),   // Life Potion, Superior Life Potion
        ];

        /// <summary>The cooldown of a potion, or null when it is not one with a known cooldown.</summary>
        public static TimeSpan? Cooldown(int id) => Find(id)?.Cooldown;

        /// <summary>Every potion that shares <paramref name="id"/>'s cooldown, itself included (empty when unknown).</summary>
        public static IReadOnlyList<int> SharingWith(int id) => Find(id)?.Ids ?? [];

        public static bool IsKnown(int id) => Find(id) is not null;

        private static (int[] Ids, TimeSpan Cooldown)? Find(int id)
        {
            foreach (var f in Families)
                if (Array.IndexOf(f.Ids, id) >= 0) return f;
            return null;
        }
    }
}
