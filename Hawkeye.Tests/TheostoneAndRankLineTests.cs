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
        Assert.Equal(("Khrudgelmir's Silence", "Silence", 1383, TimeSpan.FromSeconds(4)), (d.Name, d.Word, d.Instance, d.Duration));

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

    // ---- short words on the tiles (owner, 2026-11-23: "just call it Silence/Blind/Paralyze etc., damage ones Damage 1%, 2%") ----

    // Every stone in the public tables (metabot.gg, timesaver.gg, expcarry.com, aion.xtopolis.com; 2026-11-23), by the
    // game's own names, with what the tables say it does. The label comes from the skill id alone, so this checks
    // the id pattern against all 51.
    private static readonly Dictionary<string, string> Published = new()
    {
        // damage: Fire 1%, Water 2%, Earth 5%, Wind 10% single target; area 2%, 4%, 10%, 20%
        ["Zikel's Vestige"] = "Damage 1%", ["Traufnir's Courage"] = "Damage 1%", ["Tiamat's Rage"] = "Damage 1%",
        ["Tassin's Command"] = "Damage 2%", ["Sif's Knowledge"] = "Damage 2%", ["Triniel's Rationality"] = "Damage 2%",
        ["Ulgorn's Will"] = "Damage 5%", ["Freyr's Wisdom"] = "Damage 5%", ["Vaizel's Oath"] = "Damage 5%",
        ["Aultross's Promise"] = "Damage 10%", ["Sigyn's Brilliance"] = "Damage 10%", ["Meslamtaeda's Regret"] = "Damage 10%",
        ["Bargott's Ember"] = "Damage 2%", ["Boreas's Encouragement"] = "Damage 2%", ["Tiamat's Counterstrike"] = "Damage 2%",
        ["Aulvicar's Hex"] = "Damage 4%", ["Charna's Acumen"] = "Damage 4%", ["Marchutan's Justice"] = "Damage 4%",
        ["Gartua's Strength"] = "Damage 10%", ["Thrasymedes's Wisdom"] = "Damage 10%", ["Yustiel's Compassion"] = "Damage 10%",
        ["Rathman's Greed"] = "Damage 20%", ["Jumentis's Dexterity"] = "Damage 20%", ["Beritra's Selfishness"] = "Damage 20%",
        // debuffs
        ["Kaisinel's Vestige"] = "Blind", ["Bollvig's Tragedy"] = "Blind", ["Fregion's Schemes"] = "Blind",
        ["Perion's Tragedy"] = "Silence", ["Khrudgelmir's Silence"] = "Silence", ["Beritra's Conspiracy"] = "Silence",
        ["Heladrir's Chains"] = "Root", ["Charna's Root"] = "Root", ["Nezekan's March"] = "Root",
        ["Aulak's Roar"] = "Stun", ["Fasimedes's Dignity"] = "Stun", ["Meslamtaeda's Greed"] = "Stun",
        ["Nua's Scheme"] = "Paralyze", ["Vidar's Dignity"] = "Paralyze", ["Ereshkigal's Honor"] = "Paralyze",
        ["Reda's Tenacity"] = "Slow", ["Deltras's Loyalty"] = "Slow", ["Kaisinel's Illusion"] = "Slow",
        ["Notun's Craft"] = "Attack Slow", ["Io's Sadness"] = "Attack Slow", ["Lumiel's Interference"] = "Attack Slow",
        ["Santras's Secret"] = "Poison", ["Orissan's Blood"] = "Poison", ["Zikel's Arrogance"] = "Poison",
        ["Feruk's Instinct"] = "Bleed", ["Mahisha's Wrath"] = "Bleed", ["Fregion's Strategy"] = "Bleed",
    };

    [Fact]
    public void EveryPublishedStone_GetsItsWord()
    {
        var skills = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GameData", "Assets", "skills.json")))!;
        var byName = skills.Where(kv => kv.Value.StartsWith("Theostone: ")).ToDictionary(kv => kv.Value["Theostone: ".Length..], kv => int.Parse(kv.Key));
        Assert.Equal(51, Published.Count);
        foreach (var (name, word) in Published)
        {
            int id = byName[name];
            Assert.Equal((name, word), (name, TheostoneKinds.Label(id, name)));
            Assert.Equal((name, word), (name, TheostoneKinds.Label(id / 10, name)));   // the 7-digit form damage messages carry
        }
        Assert.Equal("Gerod's Curse", TheostoneKinds.Label(byName["Gerod's Curse"], "Gerod's Curse"));   // not in the tables: own name
        Assert.Equal("Odd", TheostoneKinds.Label(0, "Odd"));
    }

    [Fact]
    public void Hits_ShowTheWord()
    {
        alerts.OnHit(Me, Mob, "Theostone: Traufnir's Courage", 10_000, 3000117);
        alerts.OnHit(Enemy, Me, "Theostone: Vidar's Dignity", 0, 3001112);
        Assert.Equal(["Damage 1%", "Paralyze"], alerts.Showing.Select(p => p.Word));
    }

    // Santras's Secret (Poison, 20 s) on the owner, instance 1221 (C5 09); the game's removal 9.4 s later when he used a
    // potion: reason 7 for instance 1232 (D0 09) and 1221 (C5 09). Real bytes from the 2026-11-21 recording.
    private const string PoisonCleansed = "252C38EE610207D0090BEE6106D31E00636E0A0C07C5090BEE6106D31E00636E0A0C";

    [Fact]
    public void Poison_TicksAddUp_AndAPotionEndsIt()
    {
        alerts.OnDebuff(Me, 1221, "Theostone: Santras's Secret", 20_000, Enemy, 3001015);
        now = now.AddSeconds(2);
        alerts.OnTick(Enemy, Me, "Theostone: Santras's Secret", 300);
        now = now.AddSeconds(2);
        alerts.OnTick(Enemy, Me, "Theostone: Santras's Secret", 300);
        var p = Assert.Single(alerts.Showing);
        Assert.Equal(("Poison", 600L, false), (p.Word, p.Damage, p.Yours));

        now = now.AddSeconds(5.4);
        new Hawkeye.Engine.PacketProcessing.Processors.BuffRemovedProcessor(alerts).Process(Msg(PoisonCleansed));
        Assert.Empty(alerts.Showing);
    }

    [Fact]
    public void Ticks_NeverMakeATileOfTheirOwn()
    {
        alerts.OnTick(Enemy, Me, "Theostone: Santras's Secret", 300);
        Assert.Empty(alerts.Showing);
    }

    [Fact]
    public void YourDebuffOnAnEnemy_CountsDown_AndEndsWithIt()
    {
        alerts.OnHit(Me, Enemy, "Theostone: Bollvig's Tragedy", 0, 3001107);
        alerts.OnDebuff(Enemy, 1221, "Theostone: Bollvig's Tragedy", 6_000, Me, 3001107);
        var p = Assert.Single(alerts.Showing);
        Assert.Equal(("Blind", true, true, Enemy), (p.Word, p.Yours, p.IsDebuff, p.Target));

        // The same instance number removed from YOU says nothing about the enemy's debuff.
        new Hawkeye.Engine.PacketProcessing.Processors.BuffRemovedProcessor(alerts).Process(Msg(PoisonCleansed));
        Assert.Single(alerts.Showing);

        var removal = Convert.FromHexString("0B2C38BF150107C509");    // [len][op][entity 2751][count 1][reason 7][instance 1221]
        alerts.OnRemoved(Bystander, removal, 6);                       // someone else's: still there
        Assert.Single(alerts.Showing);
        alerts.OnRemoved(Enemy, removal, 6);
        Assert.Empty(alerts.Showing);
    }

    [Fact]
    public void SomeoneElsesDebuff_OnSomeoneElse_IsNotShown()
    {
        alerts.OnDebuff(Enemy, 77, "Theostone: Bollvig's Tragedy", 6_000, Bystander, 3001107);
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
