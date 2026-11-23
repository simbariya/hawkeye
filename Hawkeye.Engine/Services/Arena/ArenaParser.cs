using System.Text;

namespace Hawkeye.Engine.Services.Arena
{
    /// <summary>
    /// The arena's own messages, read from the owner's recorded 1v1 match (2026-11-23, won 3-0, rating 1500 to 1525).
    /// A player in them is an 8-byte key: [u32 character id][u16 0][u16 server id]. Layouts:
    ///
    /// Roster 0xE319, as the match loads: per player [u8 team][u32 rating]...(17 bytes)...[key 8][u8 name length][name].
    ///   Found by looking for the key + name shape; the team byte is 22 bytes before the key, the rating 21.
    /// Kill 0xE325: [len][op][killer key][victim key], once per kill (three for a 3-0).
    /// Result 0xE328: [len][op][u8][u32 your new rating][..][your key]..., at the end of the match.
    /// Standings 0xE32A, at every zone change: [len][op][u8 count] then 14 bytes per arena mode:
    ///   [u8 mode][u32 rating][u32 ?][u8 wins][u8 losses][u16 ?][u8 ?]. Mode 1 went from 1500 / 0 wins to
    ///   1525 / 1 win after the match; the owner's mode 3 read 1532, 9 and 7.
    /// Score 0xE326 (rounds per player) is not needed: the kills give the same score.
    /// </summary>
    public static class ArenaParser
    {
        public readonly record struct Player(ulong Key, string Name, int Server, int Rating, int Team);
        public readonly record struct Kill(ulong Killer, ulong Victim);
        public readonly record struct Standing(int Mode, int Rating, int Wins, int Losses);

        private static int HeaderLength(byte[] d)
        {
            int p = 0;
            while (p < d.Length && (d[p] & 0x80) != 0) p++;
            return p + 1 + 2;   // varint length, then the 2-byte opcode
        }

        public static List<Player>? ParseRoster(byte[] d)
        {
            try
            {
                var players = new List<Player>();
                for (int i = Math.Max(HeaderLength(d), 22); i + 10 <= d.Length; i++)
                {
                    uint id = BitConverter.ToUInt32(d, i);
                    if (id == 0 || d[i + 4] != 0 || d[i + 5] != 0) continue;
                    int len = d[i + 8];
                    if (len < 2 || len > 32 || i + 9 + len > d.Length) continue;
                    if (!IsName(d, i + 9, len)) continue;
                    int rating = (int)BitConverter.ToUInt32(d, i - 21);
                    if (rating < 100 || rating > 10_000) continue;
                    players.Add(new Player(BitConverter.ToUInt64(d, i), Encoding.UTF8.GetString(d, i + 9, len),
                        d[i + 6] | (d[i + 7] << 8), rating, d[i - 22]));
                    i += 8 + len;
                }
                return players.Count > 0 ? players : null;
            }
            catch (Exception) { return null; }
        }

        private static bool IsName(byte[] d, int start, int len)
        {
            for (int k = start; k < start + len; k++)
                if (d[k] < 0x20 || d[k] == 0x7F) return false;
            try { _ = new UTF8Encoding(false, true).GetString(d, start, len); return true; }
            catch (Exception) { return false; }
        }

        public static Kill? ParseKill(byte[] d)
        {
            int p = HeaderLength(d);
            if (p + 16 > d.Length) return null;
            return new Kill(BitConverter.ToUInt64(d, p), BitConverter.ToUInt64(d, p + 8));
        }

        /// <summary>Your new rating at the end of a match.</summary>
        public static int? ParseResultRating(byte[] d)
        {
            int p = HeaderLength(d) + 1;
            if (p + 4 > d.Length) return null;
            int rating = (int)BitConverter.ToUInt32(d, p);
            return rating is >= 100 and <= 10_000 ? rating : null;
        }

        /// <summary>Your key in the result: [u8][u32 rating][u8][u8][key].</summary>
        public static ulong? ParseResultKey(byte[] d)
        {
            int p = HeaderLength(d) + 1 + 4 + 2;
            return p + 8 <= d.Length ? BitConverter.ToUInt64(d, p) : null;
        }

        /// <summary>
        /// Your key from 0xE343, sent as you enter (and leave) the arena: [u8 count] then per entry [key][u8 mode]
        /// [u32 0][u64 time], your key in every entry (seen in both recordings).
        /// </summary>
        public static ulong? ParseOwnKey(byte[] d)
        {
            int p = HeaderLength(d);
            if (p + 1 + 8 > d.Length || d[p] < 1) return null;
            ulong key = BitConverter.ToUInt64(d, p + 1);
            return key != 0 && d[p + 5] == 0 && d[p + 6] == 0 ? key : null;
        }

        public static List<Standing>? ParseStandings(byte[] d)
        {
            int p = HeaderLength(d);
            if (p >= d.Length) return null;
            int count = d[p++];
            if (count < 1 || count > 8 || p + count * 14 > d.Length) return null;
            var list = new List<Standing>(count);
            for (int i = 0; i < count; i++, p += 14)
                list.Add(new Standing(d[p], (int)BitConverter.ToUInt32(d, p + 1), d[p + 9], d[p + 10]));
            return list;
        }
    }
}
