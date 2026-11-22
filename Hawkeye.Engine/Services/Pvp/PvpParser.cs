using System.Text;

namespace Hawkeye.Engine.Services.Pvp
{
    /// <summary>
    /// The three messages PvP mode reads. Found 2026-11-21 in live PvP with the owner calling out his numbers:
    /// two kills (+5600 / +2200 rank points, +3780 / +2100 AP) and one death (-280 rank points), every change at
    /// exactly the moment of the kill or death.
    ///
    /// Death (0x8D04): [len][op][victim varint][u32 unknown][killer varint][u16 killer's server id][killer name: len + UTF-8][legion: len + UTF-8]...
    ///   killer 0 and no names = not a player kill (mobs, falls).
    /// Item count (0x561B, 44 bytes): item id u32 at offset 6, new count u32 at offset 18. AP is an item with id
    ///   <see cref="ApItemId"/> (the same message counts arrows going down while you shoot).
    /// Rank points (0xE273, 12 bytes): total u32 at offset 4.
    /// </summary>
    public static class PvpParser
    {
        /// <summary>Abyss Points, as the game counts them (an item). Seen on the owner's main.</summary>
        public const uint ApItemId = 0x001AC7C1;

        public readonly record struct Death(int Victim, int Killer, string KillerName, string KillerLegion, int KillerServer = 0);

        public static Death? ParseDeath(byte[] d)
        {
            try
            {
                int p = d.Length > 0 && d[0] >= 0x80 ? 2 : 1;
                p += 2;
                int victim = Varint(d, ref p);
                if (p + 4 > d.Length) return new Death(victim, 0, "", "");
                p += 4;                                   // unknown u32
                int killer = Varint(d, ref p);
                if (killer == 0 || p + 3 > d.Length) return new Death(victim, 0, "", "");
                int server = d[p] | (d[p + 1] << 8);      // e.g. 1304 / 2303 on global: not in ServerMap (Korean ids)
                p += 2;
                string name = Text(d, ref p);
                string legion = p < d.Length ? Text(d, ref p) : "";
                return new Death(victim, killer, name, legion, server);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The new AP total if this item-count message is about AP; else null.</summary>
        public static long? ParseApTotal(byte[] d)
        {
            if (d.Length != 44) return null;
            return BitConverter.ToUInt32(d, 6) == ApItemId ? BitConverter.ToUInt32(d, 18) : null;
        }

        /// <summary>
        /// The key your inventory files AP under (u32 at 14 of an AP item-count message, right before the count).
        /// The login inventory (0x5611) has no item ids, only these keys: [key][u64 count]. Found 2026-11-22:
        /// key 0x37703323 on the owner's main in both days' AP messages, and in the login inventory followed by his
        /// exact AP (362,815).
        /// </summary>
        public static uint? ParseApKey(byte[] d)
        {
            if (d.Length != 44 || BitConverter.ToUInt32(d, 6) != ApItemId) return null;
            return BitConverter.ToUInt32(d, 14);
        }

        /// <summary>The count filed under this key in an inventory message, or null if the key is not in it.</summary>
        public static long? FindCount(byte[] inventory, uint key)
        {
            for (int i = 0; i + 12 <= inventory.Length; i++)
            {
                if (BitConverter.ToUInt32(inventory, i) != key) continue;
                ulong count = BitConverter.ToUInt64(inventory, i + 4);
                if (count < 1_000_000_000_000) return (long)count;
            }
            return null;
        }

        /// <summary>The rank points total.</summary>
        public static long? ParseRankTotal(byte[] d) => d.Length == 12 ? BitConverter.ToUInt32(d, 4) : null;

        private static int Varint(byte[] d, ref int p)
        {
            int v = 0, shift = 0;
            for (int n = 0; n < 5; n++)
            {
                byte b = d[p++];
                v |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return v;
                shift += 7;
            }
            throw new FormatException("varint too long");
        }

        private static string Text(byte[] d, ref int p)
        {
            int len = d[p++];
            if (len == 0 || p + len > d.Length) return "";
            var s = Encoding.UTF8.GetString(d, p, len);
            p += len;
            return s;
        }
    }
}
