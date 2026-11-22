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

        now = now.Add(TheostoneAlerts.ShowFor);
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
