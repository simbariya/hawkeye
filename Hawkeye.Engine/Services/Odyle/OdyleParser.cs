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
    ///
    /// Spending: the game sends a one-entry update, opcode 0x610C: [0x00][one entry, same layout][0x03].
    /// Proven 2026-11-21: cleric looted a double cube, 760 (+40) became 680 (+40), and 0x610C carried
    /// exactly that. 0x610C also updates OTHER counters with entry no. 1 (one had key 0), so an update only
    /// counts when its key is Odyle's key, learned from 0x610B (51591 on all the owner's characters).
    /// </summary>
    public static class OdyleParser
    {
        public const int Cap = 840;
        public const int PoolCap = 2000;

        /// <summary>Odyle's key as seen on all four of the owner's characters; used until a login teaches the real one.</summary>
        public const int DefaultKey = 51591;

        public static (int Current, int Pool)? Parse(byte[] data) => ParseWithKey(data) is { } r ? (r.Current, r.Pool) : null;

        /// <summary>The login/switch message (0x610B): Odyle and its key.</summary>
        public static (int Current, int Pool, int Key)? ParseWithKey(byte[] data)
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
                return (current, pool, key);
            }
            return null;
        }

        /// <summary>A one-counter update (0x610C): Odyle only if it is entry 1 with Odyle's key.</summary>
        public static (int Current, int Pool)? ParseUpdate(byte[] data, int odyleKey)
        {
            int p = data.Length > 0 && data[0] >= 0x80 ? 2 : 1;   // length varint (1 or 2 bytes)
            p += 2;                                               // opcode
            if (p + 3 >= data.Length || data[p] != 0x00) return null;
            p++;
            byte flags = data[p];
            if (flags is not (0x04 or 0x08 or 0x0C) || data[p + 1] != 0x01) return null;
            p += 2;
            if (!TryVarint(data, ref p, out int key) || key != odyleKey) return null;
            int current = 0, pool = 0;
            if ((flags & 0x04) != 0 && !TryVarint(data, ref p, out current)) return null;
            if ((flags & 0x08) != 0 && !TryVarint(data, ref p, out pool)) return null;
            return (current, pool);
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
