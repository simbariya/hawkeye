namespace Hawkeye.Engine.Services.Theostone
{
    /// <summary>
    /// What the game's general effects on you are, for the debuff tile (owner, 2026-11-27: "can debuff on you be
    /// classified? instead of just debuff"). These effects (ids 10000001 to 10000099) have no name in Hawkeye's game
    /// data, or a wrong one ("HP Absorption effect"), so the tile said "Debuff".
    ///
    /// How each was worked out: the buff message carries, after the caster, [u8][u32 the skill that caused it]. Every
    /// general effect on the owner in the PvP and arena recordings (2026-11-21 to 2026-11-23) was grouped by that skill,
    /// and the skills' effects were read from metabot.gg (data for the 2026-11-04 patch). The lengths match:
    /// - 10000004, 10000006, 10000012 Slow: Snare Shot (Slow 5 s), Blade Toss (Slow 5 s), Dimensional Control (Slow 3 s,
    ///   seen 3000 ms), Jointstrike: Corrode (water spirit Slow), Smoke Bomb.
    /// - 10000020 Root: Poach (Root 2 s, seen 2000 ms), Grapple (Root 3 s, seen 3000 ms), Shackling Arrow.
    /// - 10000030 Stun: Shadowstrike (Stun 3 s, seen 3000 ms three times).
    /// - 10000031 Stun: Marchutan's Wrath (Stun 3 s), Suppressing Arrow (Stun 3 s).
    /// - 10000032 Knockdown: Doom Shield (Knockdown 3 s), Rush Strike (Knockdown 3 s), seen 3000 ms five times.
    /// - 10000042, 10000046 Knockback: Melee Fire, Marking Shot, Ambush Kick (100 to 400 ms, shorter than tiles show).
    /// - 10000070 Blind: Smoke Bomb (Blind 5 s, seen 5000 ms), Infiltrate (Blind 3 s, seen 3000 ms).
    /// - 10000080 Silence (LIKELY, not certain): Shadowstrike, 5000 ms, twice, right after its stun. Shadowstrike's
    ///   specialisations add Seal 5 s or Root 5 s; Root is 10000020, so this is taken to be the Seal. The game calls
    ///   Silence "Seal"; the owner says Silence (see <see cref="TheostoneKinds"/>).
    /// Ids not seen keep the old behaviour (their name, or "Debuff").
    /// </summary>
    public static class StatusKinds
    {
        private static readonly Dictionary<int, string> Kinds = new()
        {
            [10000004] = "Slow", [10000006] = "Slow", [10000012] = "Slow",
            [10000020] = "Root",
            [10000030] = "Stun", [10000031] = "Stun",
            [10000032] = "Knockdown",
            [10000042] = "Knockback", [10000046] = "Knockback",
            [10000070] = "Blind",
            [10000080] = "Silence",
        };

        /// <summary>The kind of a general effect ("Stun", "Root"...), or null when it is not one we know.</summary>
        public static string? Kind(int buffId) => Kinds.GetValueOrDefault(buffId);
    }
}
