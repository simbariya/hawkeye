namespace Hawkeye.Engine.Services.Theostone
{
    /// <summary>
    /// What a Theostone does, in one or two words for its alert tile (owner, 2026-11-23: "instead of those big names,
    /// just call it Silence/Blind/Paralyze etc. Damage ones should be Damage 1%, Damage 2% depending on the type").
    ///
    /// The skill id says it all: id / 10 = 300 F G SS (the 7-digit form damage messages carry), where
    /// F = family (0 damage, 1 debuff), G = grade (0 Rare, 1 Epic, 2 Unique) and SS = the slot below. Checked
    /// against all 51 stones in the public tables (metabot.gg, timesaver.gg, expcarry.com, aion.xtopolis.com,
    /// 2026-11-23) and the owner's PvP recording (Paralyze 3 s, Poison 20 s, Silence 4 s, Silence 3 s: all match).
    /// The game calls Silence "Seal"; the owner says Silence. Damage "N%" is the stone's proc chance, which is what
    /// tells the damage stones apart (Fire 1%, Water 2%, Earth 5%, Wind 10%; their area versions 2%, 4%, 10%, 20%).
    /// Slots not in the public tables (08, 25 and up) keep the stone's own name.
    /// </summary>
    public static class TheostoneKinds
    {
        private static readonly Dictionary<int, string> DebuffSlots = new()
        {
            [7] = "Blind", [9] = "Silence", [10] = "Root", [11] = "Stun", [12] = "Paralyze",
            [13] = "Slow", [14] = "Attack Slow", [15] = "Poison", [16] = "Bleed",
        };

        private static readonly Dictionary<int, int> DamageChance = new()
        {
            [17] = 1, [18] = 2, [19] = 5, [20] = 10,     // single target: Fire, Water, Earth, Wind
            [21] = 2, [22] = 4, [23] = 10, [24] = 20,    // area (up to 4): Fire, Water, Earth, Wind
        };

        /// <summary>
        /// The tile's word for a Theostone: "Silence", "Damage 2%"... <paramref name="code"/> may be the 8-digit skill
        /// id or the 7-digit form; 0 or an unknown slot falls back to <paramref name="name"/>.
        /// </summary>
        public static string Label(int code, string name)
        {
            int n = code >= 30_000_000 ? code / 10 : code;
            if (n is < 3_000_000 or > 3_099_999) return name;
            int family = n / 1000 % 10, slot = n % 100;
            if (family == 1 && DebuffSlots.TryGetValue(slot, out var word)) return word;
            if (family == 0 && DamageChance.TryGetValue(slot, out var chance)) return $"Damage {chance}%";
            return name;
        }
    }
}
