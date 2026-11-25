namespace Hawkeye.Engine.Services.Skills
{
    /// <summary>
    /// Your character's skill list (0x5100), sent at login, at every zone change and teleport, and when you switch
    /// skill setups (found 2026-11-25 in the owner's recordings; all 59 lists in them parse to the last byte).
    ///
    /// Layout after the header: <c>[varint count]</c>, then per skill
    /// <c>[u8 flags][u32 skill id][u8 level][u8 learned][u8 bonus][u8 ?][u8 bonus][u8 bonus][u8 ?]</c>
    /// <c>(flags &amp; 2: varint cooldown ms left)[u32 ?](flags &amp; 4: u8 ?)(flags &amp; 8: varint ?)</c>.
    /// The level is the one the game shows: learned + the three bonuses, true for every class skill outside the arena
    /// (the arena sets every skill to 20). The id carries the chosen specialisations in its low digits
    /// (14012450 = Deadshot 14010000 with 2, 4 and 5), the same as in damage messages.
    /// </summary>
    public static class SkillListParser
    {
        public sealed record Entry(int SkillId, int Level, int Learned);

        public static List<Entry>? Parse(byte[] d)
        {
            try
            {
                int p = 0;
                while (p < d.Length && (d[p] & 0x80) != 0) p++;
                p += 1 + 2;                                   // varint length, then the 2-byte opcode
                int count = (int)ReadVarint(d, ref p);
                if (count < 1 || count > 400) return null;

                var list = new List<Entry>(count);
                for (int n = 0; n < count; n++)
                {
                    if (p + 16 > d.Length) return null;
                    byte flags = d[p];
                    int id = BitConverter.ToInt32(d, p + 1);
                    int level = d[p + 5], learned = d[p + 6];
                    p += 12;
                    if ((flags & 2) != 0) ReadVarint(d, ref p);
                    p += 4;
                    if ((flags & 4) != 0) p += 1;
                    if ((flags & 8) != 0) ReadVarint(d, ref p);
                    if (p > d.Length) return null;
                    list.Add(new Entry(id, level, learned));
                }
                return p == d.Length ? list : null;           // a list that does not end exactly was misread
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static long ReadVarint(byte[] d, ref int p)
        {
            long v = 0;
            int shift = 0;
            while (true)
            {
                byte b = d[p++];
                v |= (long)(b & 0x7F) << shift;
                if (b < 0x80) return v;
                shift += 7;
                if (shift > 35) throw new IndexOutOfRangeException();
            }
        }
    }
}
