namespace Hawkeye.Engine.Services.Odyle
{
    /// <summary>
    /// Reads Odyle from the game's counters message (opcode 0x610B), sent when you log in or switch character.
    ///
    /// Found 2026-11-21 from four characters whose in-game numbers the owner read out (cleric 760 (+40),
    /// AltB 250 (+345), alta 480 (+295), Main 60 (+360)); all four matched. The message holds a list of entries:
    ///     [flags][entry no.][key varint][value varint if flags&amp;0x04][extra varint if flags&amp;0x08]
    /// Odyle is entry 1: value = current Odyle, extra = the refill pool shown as "(+N)". The cap (840) and the
    /// pool cap (2000) are not sent; they are fixed. Entry 2 follows with the same key, which is how the
    /// right spot is recognised (the list does not start at a fixed offset).
    /// Not yet seen: a character with 0 current (flags 0x08 expected, value then 0).
    /// </summary>
    public static class OdyleParser
    {
        public const int Cap = 840;
        public const int PoolCap = 2000;

        public static (int Current, int Pool)? Parse(byte[] data)
        {
            for (int i = 4; i + 4 < data.Length; i++)
            {
                byte flags = data[i];
                if (flags is not (0x04 or 0x08 or 0x0C) || data[i + 1] != 0x01) continue;

                int p = i + 2;
                if (!TryVarint(data, ref p, out int key)) continue;
                int current = 0, pool = 0;
                if ((flags & 0x04) != 0 && !TryVarint(data, ref p, out current)) continue;
                if ((flags & 0x08) != 0 && !TryVarint(data, ref p, out pool)) continue;

                // Entry 2 must follow with the same key.
                if (p + 2 >= data.Length || data[p + 1] != 0x02) continue;
                int q = p + 2;
                if (!TryVarint(data, ref q, out int key2) || key2 != key) continue;

                if (current < 0 || current > 100_000 || pool < 0 || pool > 100_000) continue;
                return (current, pool);
            }
            return null;
        }

        private static bool TryVarint(byte[] d, ref int p, out int value)
        {
            value = 0;
            int shift = 0;
            for (int n = 0; n < 5 && p < d.Length; n++)
            {
                byte b = d[p++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
                shift += 7;
            }
            return false;
        }
    }
}
