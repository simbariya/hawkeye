using System.Text;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Pvp;

namespace Hawkeye.Tests;

/// <summary>
/// The PvP ranking messages and the login AP total (found 2026-11-22 in a recorded login). Built here with the real
/// layout and made-up players; the owner's real numbers (rank 78, 5,008,920 points, 21 points behind rank 77, AP
/// 362,815 at login) were checked against the recording, which stays off git.
/// </summary>
public sealed class PvpRankingTests
{
    private static readonly DateTime NextUpdate = new(2026, 11, 22, 13, 28, 5, DateTimeKind.Utc);

    private static byte[] Row(int rank, string name, string legion, long points, int k = 10, int d = 5, int a = 7, int server = 1304)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes((uint)rank));
        b.AddRange(BitConverter.GetBytes(0x00032A89u));
        b.AddRange([0, 0]);
        b.AddRange(BitConverter.GetBytes((ushort)server));
        foreach (var s in new[] { name, legion }) { var t = Encoding.UTF8.GetBytes(s); b.Add((byte)t.Length); b.AddRange(t); }
        b.AddRange([legion.Length > 0 ? (byte)1 : (byte)0, 0, 0x2D, 0, 0, 0, 0, 0, 0, 0]);
        b.Add(4);                                                       // class
        b.AddRange(BitConverter.GetBytes((ulong)points));
        var json = Encoding.UTF8.GetBytes($"{{\"kill_count\":{k},\"death_count\":{d},\"assist_count\":{a}}}");
        b.Add((byte)json.Length); b.AddRange(json);
        b.AddRange(new byte[8]);
        b.AddRange(BitConverter.GetBytes(5u)); b.AddRange(BitConverter.GetBytes(5u));
        return b.ToArray();
    }

    private static byte[] Time() => BitConverter.GetBytes(new DateTimeOffset(NextUpdate).ToUnixTimeMilliseconds());

    private static byte[] Own(int category, byte[] row, byte type = 1) =>
        [0x8E, 0x01, 0x57, 0x8D, 0, 0, type, (byte)category, .. row, .. BitConverter.GetBytes(10000u), .. Time()];

    private static byte[] Table(int category, params byte[][] rows) =>
        [0x9A, 0xD1, 0x03, 0x55, 0x8D, 0, 0, 1, (byte)category, 0xF4, 0x03, .. rows.SelectMany(r => r), .. Time(), .. BitConverter.GetBytes(3650u)];

    [Fact]
    public void OwnLine_IsRead()
    {
        var list = PvpRankingParser.ParseOwn(Own(4, Row(78, "Alpha", "Guildone", 5_008_920, 1459, 947, 1730)))!;
        Assert.Equal(4, list.Category);
        var r = Assert.Single(list.Rows);
        Assert.Equal((78, "Alpha", "Guildone", 5_008_920L, 1459, 947, 1730, 1304), (r.Rank, r.Name, r.Legion, r.Points, r.Kills, r.Deaths, r.Assists, r.ServerId));
        Assert.Equal(NextUpdate.ToLocalTime(), list.NextUpdate);
    }

    [Fact]
    public void Table_IsRead_NonAsciiAndNoLegionToo()
    {
        var list = PvpRankingParser.ParseTable(Table(4,
            Row(1, "Bravo", "", 9_000_000),
            Row(2, "Чарли", "Звезда", 6_000_000),
            Row(3, "Alpha", "Guildone", 5_000_000)))!;
        Assert.Equal(["Bravo", "Чарли", "Alpha"], list.Rows.Select(r => r.Name));
        Assert.Equal("", list.Rows[0].Legion);
        Assert.Equal(6_000_000, list.Rows[1].Points);
        Assert.Equal(NextUpdate.ToLocalTime(), list.NextUpdate);
    }

    /// <summary>The same messages carry the dungeon and boss rankings (type 0x15, 0x03): not PvP, skipped.</summary>
    [Fact]
    public void OtherRankings_AreSkipped()
    {
        Assert.Null(PvpRankingParser.ParseOwn(Own(4, Row(1, "Alpha", "", 1), type: 0x15)));
        Assert.Null(PvpRankingParser.ParseOwn([0x10, 0x57, 0x8D, 0, 0, 1, 4, 1, 0]));
    }

    private static RankList Owners() => PvpRankingParser.ParseTable(Table(4,
        Row(1, "Top", "", 6_000_000),
        Row(2, "Close", "", 5_008_940),
        Row(3, "Alpha", "Guildone", 5_008_920),
        Row(4, "Below", "", 4_971_110)))!;

    private static readonly RankRow Me = new(3, 1304, "Alpha", "Guildone", 4, 5_008_920, 1459, 947, 1730);

    [Fact]
    public void PointsToNext_LikeTheOwnersLogin()
    {
        var s = RankMath.Standing(Me, 4, 5_008_920, Owners(), 342, null);
        Assert.Equal((3, 2, 21L), (s.Projected, s.NextRank, s.PointsToNext));
        Assert.Equal("Ranger rank 3", RankWords.Label(s, "Ranger"));
        Assert.Equal("21 RP to 2", RankWords.Next(s));
        Assert.Contains("Overall rank 342", RankWords.Tip(s));

        var tie = RankMath.Standing(Me, 4, 5_008_940, Owners(), null, null);      // a tie is not a pass
        Assert.Equal((3, 1L), (tie.Projected, tie.PointsToNext));

        var passed = RankMath.Standing(Me, 4, 5_008_941, Owners(), null, null);   // passed rank 2 since the update
        Assert.Equal((2, 1, 991_060L), (passed.Projected, passed.NextRank, passed.PointsToNext));
        Assert.Equal("Ranger rank 3 (next 2)", RankWords.Label(passed, "Ranger"));

        var top = RankMath.Standing(Me, 4, 7_000_000, Owners(), null, null);
        Assert.Equal("Top spot", RankWords.Next(top));
    }

    [Fact]
    public void NoTable_ShowsJustTheRank()
    {
        var s = RankMath.Standing(Me, 4, 5_008_920, null, null, null);
        Assert.Equal(("Class rank 3", ""), (RankWords.Label(s, ""), RankWords.Next(s)));
    }

    private static PvpTracker Tracker(out EntityTracker entities)
    {
        entities = new EntityTracker();
        entities.SetSessionPlayerName(12526, "Alpha", 0, isUser: true);
        return new PvpTracker(entities);
    }

    /// <summary>First kill exact (owner, 2026-11-21): AP from the login inventory, rank points from your ranking line.</summary>
    [Fact]
    public void FirstKill_CountsFromTheLoginTotals()
    {
        var pvp = Tracker(out _);
        const uint key = 0x37703323;
        // Inventory records: [..][key][u64 count]; another item first so the search has to skip it.
        byte[] inventory = [0x00, 0x2B, 0x33, 0x70, 0x37, 0xA9, 0x15, 0, 0, 0, 0, 0, 0, 0x01, 0x02, 0x02, 0x00,
            .. BitConverter.GetBytes(key), .. BitConverter.GetBytes(362_815UL), 0x01, 0x01, 0, 0];
        pvp.OnInventory(inventory);
        pvp.OnOwnRank(PvpRankingParser.ParseOwn(Own(4, Row(78, "Alpha", "Guildone", 5_008_920)))!);
        pvp.SetActive(true);

        pvp.OnApTotal(366_595, key);     // the first kill: +3780 AP
        pvp.OnRankTotal(5_014_520);      // +5600
        Assert.Equal((3780L, 5600L), (pvp.ApGained, pvp.RankGained));
        Assert.Equal(78, pvp.Standing!.Rank);
    }

    [Fact]
    public void AnotherCharacter_StartsItsTotalsOver()
    {
        var pvp = Tracker(out var entities);
        pvp.OnOwnRank(PvpRankingParser.ParseOwn(Own(4, Row(78, "Alpha", "", 5_008_920)))!);
        Assert.NotNull(pvp.Standing);
        entities.SetSessionPlayerName(30001, "Zulu", 0, isUser: true);
        Assert.Null(pvp.Standing);
        pvp.SetActive(true);
        pvp.OnRankTotal(100);            // Zulu's first total: nothing to compare with, so no gain
        Assert.Equal(0, pvp.RankGained);
    }
}
