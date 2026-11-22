using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Pvp;
using Hawkeye.Engine.Services.Theostone;
using Hawkeye.Engine.Settings;

namespace Hawkeye.Tests;

/// <summary>
/// Theostone alerts and the always-on rank line (owner, 2026-11-22). In the owner's recorded PvP session his own
/// Theostone procced 10 times and enemies' Theostones hit him 16 times: both arrive as damage hits.
/// </summary>
public sealed class TheostoneAndRankLineTests : IDisposable
{
    private const int Me = 12526, Enemy = 2751, Bystander = 4000, Mob = 90001;
    private readonly string settingsPath = Path.Combine(Path.GetTempPath(), $"hawkeye-theo-{Guid.NewGuid():N}.json");
    private readonly SettingsService settings;
    private readonly TheostoneAlerts alerts;
    private DateTime now = new(2026, 11, 22, 15, 0, 0);

    public TheostoneAndRankLineTests()
    {
        settings = new SettingsService(settingsPath);
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
        entities.SetSessionPlayerName(Enemy, "Bravo", 0);
        alerts = new TheostoneAlerts(entities, settings) { Clock = () => now };
    }

    public void Dispose() => File.Delete(settingsPath);

    [Fact]
    public void YoursAndOnYou_Alert_OthersDoNot()
    {
        alerts.OnHit(Me, Mob, "Theostone: Tassin's Command", 12_000);
        alerts.OnHit(Enemy, Me, "Theostone: Mahisha's Wrath", 8_000);
        alerts.OnHit(Bystander, Enemy, "Theostone: Aulvicar's Hex", 5_000);    // not about you

        var shown = alerts.Showing;
        Assert.Equal(2, shown.Count);
        Assert.Equal((true, "Tassin's Command", ""), (shown[0].Yours, shown[0].Name, shown[0].From));
        Assert.Equal((false, "Mahisha's Wrath", "Bravo"), (shown[1].Yours, shown[1].Name, shown[1].From));
    }

    [Fact]
    public void MultiHit_IsOneAlert_AndItGoesAway()
    {
        alerts.OnHit(Me, Mob, "Theostone: Tassin's Command", 1_000);
        now = now.AddSeconds(0.3);
        alerts.OnHit(Me, Mob, "Theostone: Tassin's Command", 1_500);
        var one = Assert.Single(alerts.Showing);
        Assert.Equal((2, 2_500L), (one.Hits, one.Damage));

        now = now.Add(alerts.ShowFor);
        Assert.Empty(alerts.Showing);
    }

    [Fact]
    public void SwitchedOff_InSettings()
    {
        settings.Update(s => { s.TheostoneAlertsYours = false; s.TheostoneAlertsOnYou = false; });
        alerts.OnHit(Me, Mob, "Theostone: Tassin's Command", 1_000);
        alerts.OnHit(Enemy, Me, "Theostone: Mahisha's Wrath", 1_000);
        Assert.Empty(alerts.Showing);
    }

    // ---- debuffs on you: stay until they end or the game removes them (owner: "until it's gone, naturally or by potting") ----

    // Real messages from the owner's PvP recording (2026-11-21); he was entity 12526 (EE 61). No names in them.
    // Khrudgelmir's Silence on him for 4 s, instance 1383 (E7 0A), from entity 11274:
    private const string Silence4s = "342A38EE610113E70AD3EEC901A00F0000000000005F2496FFA10100008A580115CB2D0000238BC0C5927002C678D32447";
    // ... cleansed 1.41 s later (reason 7), together with instance 1384:
    private const string SilenceCleansed = "252C38EE610207E70A0BEE6106D31E00636E0A0C07E80A0BEE6106D31E00636E0A0C";
    // Vidar's Dignity 3 s, instance 952 (B8 07), ran out after 3.03 s (reason 0):
    private const string DignityRanOut = "0D2C38EE610100B80701";

    private Hawkeye.Engine.PacketProcessing.Processors.BuffPacketProcessor BuffReader()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
        var sessions = new Hawkeye.Engine.Services.Session.CombatSessionManager(entities,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, settings,
            new Hawkeye.Engine.Services.Timed.TimedEventTracker(), new Hawkeye.Engine.Services.Timed.TimedEventTracker());
        return new(Microsoft.Extensions.Logging.Abstractions.NullLogger<Hawkeye.Engine.PacketProcessing.Processors.BuffPacketProcessor>.Instance,
            sessions, alerts);
    }

    private static Hawkeye.Engine.PacketProcessing.Routing.Packet Msg(string hex) => new() { Data = Convert.FromHexString(hex) };

    [Fact]
    public void Debuff_StaysUntilCleansed()
    {
        BuffReader().Process(Msg(Silence4s));
        var d = Assert.Single(alerts.Showing);
        Assert.Equal(("Khrudgelmir's Silence", 1383, TimeSpan.FromSeconds(4)), (d.Name, d.Instance, d.Duration));

        now = now.AddSeconds(3.9);                                  // longer than a plain alert, still on
        Assert.Single(alerts.Showing);
        Assert.InRange(alerts.TimeLeft(d).TotalSeconds, 0.05, 0.15);

        new Hawkeye.Engine.PacketProcessing.Processors.BuffRemovedProcessor(alerts).Process(Msg(SilenceCleansed));
        Assert.Empty(alerts.Showing);
    }

    [Fact]
    public void Debuff_EndsOnItsOwn_AndOtherRemovalsLeaveItAlone()
    {
        BuffReader().Process(Msg(Silence4s));
        new Hawkeye.Engine.PacketProcessing.Processors.BuffRemovedProcessor(alerts).Process(Msg(DignityRanOut));   // another debuff
        Assert.Single(alerts.Showing);
        now = now.AddSeconds(4);
        Assert.Empty(alerts.Showing);
    }

    [Fact]
    public void TheHitThatBringsADebuff_IsOneAlert()
    {
        alerts.OnHit(Enemy, Me, "Theostone: Khrudgelmir's Silence", 3_000);
        alerts.OnDebuff(Me, 1383, "Theostone: Khrudgelmir's Silence", 4_000, Enemy);
        var one = Assert.Single(alerts.Showing);
        Assert.Equal((3_000L, true, "Bravo"), (one.Damage, one.IsDebuff, one.From));
    }

    // ---- rank line: always there, nudges you to open Abyss Rank ----

    private static readonly RankList Table = new(4, [
        new RankRow(1, 1304, "Top", "", 4, 6_000_000, 0, 0, 0),
        new RankRow(2, 1304, "Close", "", 4, 5_008_940, 0, 0, 0),
        new RankRow(3, 1304, "Alpha", "", 4, 5_008_920, 0, 0, 0)], null);
    private static readonly RankRow Own = new(3, 1304, "Alpha", "", 4, 5_008_920, 1459, 947, 1730);
    private static readonly DateTime Update = new(2026, 11, 22, 15, 28, 5);

    /// <summary>
    /// The game sends the ranking once per hourly update (owner, 2026-11-22), so a restarted Hawkeye (e.g. after
    /// installing an update) must show the remembered one instead of "Rank unknown" for the rest of the hour.
    /// </summary>
    [Fact]
    public void Ranking_SurvivesARestart()
    {
        var file = Path.Combine(Path.GetTempPath(), $"hawkeye-totals-{Guid.NewGuid():N}.json");
        try
        {
            var entities = new EntityTracker();
            entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
            var store = new PvpTotalsStore(file);
            var pvp = new PvpTracker(entities, null, store);
            pvp.OnOwnRank(new RankList(4, [Own], Update));
            pvp.OnRankTable(Table with { NextUpdate = Update });
            store.FlushAsync().Wait();

            var again = new PvpTracker(new EntityTracker(), null, new PvpTotalsStore(file));   // name not known yet
            var s = again.Standing!;
            Assert.Equal((3, 21L, Update), (s.Rank, s.PointsToNext!.Value, s.NextUpdate!.Value));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void RankLine_Unknown_Live_Reached_Old()
    {
        var unknown = RankWords.Line(null, "Ranger", Update);
        Assert.Equal((RankWords.LineState.Unknown, RankWords.OpenRankPage), (unknown.State, unknown.Right));

        var live = RankWords.Line(RankMath.Standing(Own, 4, 5_008_920, Table, null, Update), "Ranger", Update.AddMinutes(-5));
        Assert.Equal(("Ranger rank 3", "21 RP to 2", RankWords.LineState.Live), (live.Left, live.Right, live.State));

        var reached = RankWords.Line(RankMath.Standing(Own, 4, 5_009_000, Table, null, Update), "Ranger", Update.AddMinutes(-5));
        Assert.Equal(("Ranger rank 2 reached", "official at 15:28", RankWords.LineState.Reached), (reached.Left, reached.Right, reached.State));
        var reachedLater = RankWords.Line(RankMath.Standing(Own, 4, 5_009_000, Table, null, Update), "Ranger", Update.AddMinutes(1));
        Assert.Equal(RankWords.OpenRankPage, reachedLater.Right);

        var old = RankWords.Line(RankMath.Standing(Own, 4, 5_008_920, Table, null, Update), "Ranger", Update.AddMinutes(1));
        Assert.Equal(("Ranger rank 3", RankWords.OpenRankPage, RankWords.LineState.Old), (old.Left, old.Right, old.State));
        Assert.Contains("21 RP to 2", old.Tip);
    }
}
