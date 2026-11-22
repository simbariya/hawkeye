using System.Text;
using System.Text.RegularExpressions;

namespace Hawkeye.Engine.Services.Pvp
{
    /// <summary>One player's line in the game's PvP ranking.</summary>
    public sealed record RankRow(int Rank, int ServerId, string Name, string Legion, int ClassId, long Points,
        int Kills, int Deaths, int Assists);

    /// <summary>
    /// A ranking the game sent: your own line (0x8D57, one row) or a whole table (0x8D55, the top 500).
    /// Category 0 is the overall ranking; any other value is a class ranking (4 on the owner's Ranger).
    /// <see cref="NextUpdate"/> is when the game recalculates ranks (hourly), local time.
    /// </summary>
    public sealed record RankList(int Category, IReadOnlyList<RankRow> Rows, DateTime? NextUpdate);

    /// <summary>
    /// Reads the two ranking messages. Found 2026-11-22 in a recorded login, checked against the owner's screen
    /// (5,008,920 rank points, lifetime 1459 / 947 / 1730, Ranger rank 78; the table's row 78 is him).
    ///
    /// Own line 0x8D57: [len][op][00 00][01][category][row][...][u64 next update, ms since 1970 UTC]
    /// Table   0x8D55: [len][op][00 00][01][category][u16 ?][row x 500][u64 next update][u32 ?]
    /// Row: [u32 rank][u32 ?][u16 0][u16 server id][name: len + UTF-8][legion: len + UTF-8][10 bytes][u8 class]
    ///      [u64 rank points][text: len + {"kill_count":N,"death_count":N,"assist_count":N}][8 zero bytes][u32 ?][u32 ?]
    /// </summary>
    public static class PvpRankingParser
    {
        private static readonly Regex Counts = new(@"""kill_count"":(\d+),""death_count"":(\d+),""assist_count"":(\d+)", RegexOptions.Compiled);

        public static RankList? ParseOwn(byte[] d)
        {
            try
            {
                int p = Start(d, out int category);
                if (p < 0) return null;
                var row = Row(d, ref p);
                return row is null ? null : new RankList(category, [row], FindTime(d, p));
            }
            catch (Exception) { return null; }
        }

        public static RankList? ParseTable(byte[] d)
        {
            int category = 0, p;
            var rows = new List<RankRow>();
            try
            {
                p = Start(d, out category);
                if (p < 0) return null;
                p += 2;
                while (d.Length - p > 16)
                {
                    int at = p;
                    var row = Row(d, ref p);
                    if (row is null || (rows.Count > 0 && row.Rank != rows[^1].Rank + 1)) { p = at; break; }
                    rows.Add(row);
                }
            }
            catch (Exception) { return rows.Count > 0 ? new RankList(category, rows, null) : null; }
            return rows.Count == 0 ? null : new RankList(category, rows, FindTime(d, p));
        }

        // Skips the length and opcode, then [00 00][01]; returns where the category byte ends.
        private static int Start(byte[] d, out int category)
        {
            category = 0;
            int p = 0;
            while (p < d.Length && p < 5 && (d[p] & 0x80) != 0) p++;
            p += 1 + 2;                         // last length byte, opcode
            if (p + 4 > d.Length || d[p + 2] != 1) return -1;
            category = d[p + 3];
            return p + 4;
        }

        private static RankRow? Row(byte[] d, ref int p)
        {
            int rank = (int)BitConverter.ToUInt32(d, p);
            p += 8;                             // rank, unknown u32
            p += 2;
            int server = d[p] | (d[p + 1] << 8);
            p += 2;
            string name = Text(d, ref p);
            string legion = Text(d, ref p);
            p += 10;
            int cls = d[p++];
            long points = (long)BitConverter.ToUInt64(d, p);
            p += 8;
            var m = Counts.Match(Text(d, ref p));
            p += 16;
            if (!m.Success || rank <= 0 || name.Length == 0 || points < 0) return null;
            return new RankRow(rank, server, name, legion, cls, points,
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        }

        private static string Text(byte[] d, ref int p)
        {
            int len = d[p++];
            if (len == 0) return "";
            if (p + len > d.Length) throw new FormatException("text runs past the end");
            var s = Encoding.UTF8.GetString(d, p, len);
            p += len;
            return s;
        }

        // The next-update time: the first u64 after the rows that reads as a date between 2025 and 2040.
        private static DateTime? FindTime(byte[] d, int from)
        {
            const long min = 1_735_689_600_000, max = 2_208_988_800_000;   // 2025-01-01, 2040-01-01 in ms
            for (int i = Math.Max(0, from); i + 8 <= d.Length; i++)
            {
                long v = BitConverter.ToInt64(d, i);
                if (v >= min && v < max) return DateTimeOffset.FromUnixTimeMilliseconds(v).LocalDateTime;
            }
            return null;
        }
    }

    /// <summary>Where you stand, for the PvP panel.</summary>
    /// <param name="Rank">Your rank as of the last update.</param>
    /// <param name="Projected">Where your live points would put you in the last table (null without a table or outside it).</param>
    /// <param name="NextRank">The rank above you to aim for; null at the top or without a table.</param>
    /// <param name="PointsToNext">Rank points still needed to pass the player at <paramref name="NextRank"/>.</param>
    public sealed record RankStanding(int Category, int Rank, long Points, int? Projected, int? NextRank, long? PointsToNext,
        int? OverallRank, int LifetimeKills, int LifetimeDeaths, int LifetimeAssists, DateTime? NextUpdate, int TableSize);

    /// <summary>The rank line's words, shared by the meter and the stream page.</summary>
    public static class RankWords
    {
        private static string N(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Left side: "Ranger rank 78", plus "(next 77)" when your live points already beat the one above.</summary>
        public static string Label(RankStanding s, string className)
        {
            string what = s.Category == 0 ? "Overall rank" : className.Length > 0 ? $"{className} rank" : "Class rank";
            string label = $"{what} {s.Rank}";
            if (s.Projected is { } p && p < s.Rank) label += $" (next {p})";
            return label;
        }

        /// <summary>Right side: "21 RP to 77", "Top spot", or empty without a table.</summary>
        public static string Next(RankStanding s)
        {
            if (s.PointsToNext is { } need && s.NextRank is { } next) return $"{N(need)} RP to {next}";
            return s.Projected == 1 ? "Top spot" : "";
        }

        public static string Tip(RankStanding s)
        {
            var parts = new List<string>();
            if (s.OverallRank is { } o) parts.Add($"Overall rank {o}.");
            parts.Add($"Lifetime: {N(s.LifetimeKills)} kills, {N(s.LifetimeDeaths)} deaths, {N(s.LifetimeAssists)} assists.");
            parts.Add(s.NextUpdate is { } at ? $"Ranks update hourly, next at {at:HH:mm}." : "Ranks update hourly.");
            if (s.TableSize > 0) parts.Add($"Points to the next rank are counted against the last update (top {s.TableSize}).");
            return string.Join(" ", parts);
        }
    }

    public static class RankMath
    {
        /// <summary>
        /// Your standing from your own line, your live points and the last table of that ranking. The table is the
        /// hourly snapshot, so "points to next" means: this many more by the next update and you take that rank
        /// (unless they gain too). Ties count as not passed.
        /// </summary>
        public static RankStanding Standing(RankRow own, int category, long livePoints, RankList? table, int? overall, DateTime? nextUpdate)
        {
            int? projected = null, next = null;
            long? need = null;
            if (table is { Rows.Count: > 0 })
            {
                var others = table.Rows.Where(r => !(r.Name == own.Name && r.ServerId == own.ServerId))
                                       .OrderByDescending(r => r.Points).ToList();
                int ahead = others.Count(r => r.Points >= livePoints);
                bool full = table.Rows.Count >= 500;
                bool youAreInIt = others.Count < table.Rows.Count;
                if (!full || youAreInIt || ahead < others.Count)
                {
                    projected = ahead + 1;
                    if (ahead > 0)
                    {
                        next = ahead;                            // you would take the place of the last one ahead
                        need = others[ahead - 1].Points - livePoints + 1;
                    }
                }
                else
                {
                    next = table.Rows[^1].Rank;                  // below the table: aim for its last place
                    need = others[^1].Points - livePoints + 1;
                }
            }
            return new RankStanding(category, own.Rank, livePoints, projected, next, need, overall,
                own.Kills, own.Deaths, own.Assists, nextUpdate ?? table?.NextUpdate, table?.Rows.Count ?? 0);
        }
    }
}
