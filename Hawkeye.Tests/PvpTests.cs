using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Pvp;

namespace Hawkeye.Tests;

/// <summary>
/// PvP mode, from real messages of the owner's PvP session (2026-11-21); player names replaced with neutral ones
/// of the same length. You are entity 12526 ("Alpha").
/// </summary>
public sealed class PvpTests
{
    private const int Me = 12526;
    // You kill entity 13200: victim 13200, killer 12526 "Alpha" of "Guildone".
    private const string YourKill = "28048D9067FED0DA00EE611805" + "05" + "416C706861" + "08" + "4775696C646F6E65" + "010000000000000101";
    // You die: victim 12526, killed by "Bravo" of "XY".
    private const string YourDeath = "22048DEE6186B10501847CFF08" + "05" + "427261766F" + "02" + "5859" + "020000000000000101";
    // Someone else kills entity 7999 ("Other" of "XY").
    private const string OthersKill = "21048DBF3E2E64D600E7071805" + "05" + "4F74686572" + "02" + "5859" + "010000000000000101";
    // AP item count 149139, and rank points 4729192.
    private const string Ap149139 = "2F1B56010100C1C71A00006000002333703793460200000000000101000000000000000000000000000B0000";
    private const string Rank4729192 = "0F73E2006829480000000000";

    private static byte[] Hex(string h) => Convert.FromHexString(h);

    /// <summary>The app builds it through its service setup; two fillable constructors broke 0.2.9 (I15).</summary>
    [Fact]
    public void AppServiceSetup_CanBuildIt()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<EntityTracker>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<PvpTracker>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<PvpHistory>(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        Assert.NotNull(provider.GetService(typeof(PvpTracker)));
        // PvpHistory: only the logger constructor is fillable (the other needs a path), so no tie. Building it
        // would read the real %AppData% file, so only check that the setup can choose.
        var ctors = typeof(PvpHistory).GetConstructors().Where(c => c.GetParameters().All(p =>
            p.ParameterType != typeof(string))).ToList();
        Assert.Single(ctors);
    }

    [Fact]
    public void Messages_AreRead()
    {
        var kill = PvpParser.ParseDeath(Hex(YourKill))!.Value;
        Assert.Equal((13200, Me, "Alpha", "Guildone"), (kill.Victim, kill.Killer, kill.KillerName, kill.KillerLegion));
        Assert.Equal(149139, PvpParser.ParseApTotal(Hex(Ap149139)));
        Assert.Equal(4729192, PvpParser.ParseRankTotal(Hex(Rank4729192)));
        var mobDeath = PvpParser.ParseDeath(Hex("1B048D86930200000000000000000000000000000000000000"))!.Value;
        Assert.Equal(0, mobDeath.Killer);
    }

    // Owner, 2026-11-23, farming Abyss mobs on his Chanter: AP did not count. The AP item's own number (offset 6) is
    // different on that character (0x08E6C5, not 0x1AC7C1); the key at 14 (0x37703323) is the same.
    [Fact]
    public void Ap_IsFoundByItsKey_OnEveryCharacter()
    {
        const string ChanterAp20949 = "2F1B56010100C5E608000094000023337037D5510000000000000101000000000000000000000000000B0000";
        const string NotAp = "2F1B56010100418D090000C2000021F4CB1F5812000000000000012200000000000000000000000000001600";
        Assert.Equal(20949, PvpParser.ParseApTotal(Hex(ChanterAp20949)));
        Assert.Equal(PvpParser.ApKey, PvpParser.ParseApKey(Hex(ChanterAp20949)));
        Assert.Equal(149139, PvpParser.ParseApTotal(Hex(Ap149139)));   // the main's, as before
        Assert.Null(PvpParser.ParseApTotal(Hex(NotAp)));
    }

    private static (EntityTracker, PvpTracker, Func<DateTime>, Action<double>) Setup()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
        foreach (var enemy in new[] { 13200, 7999 }) entities.SetSessionPlayerName(enemy, "Enemy" + enemy, 0);
        var t = new DateTime(2026, 11, 21, 19, 20, 0);
        var clock = () => t;
        var pvp = new PvpTracker(entities) { Clock = () => t };
        return (entities, pvp, clock, s => t = t.AddSeconds(s));
    }

    [Fact]
    public void YourSession_CountsLikeTheGame()
    {
        var (_, pvp, _, wait) = Setup();
        pvp.OnApTotal(143259);              // totals known before switching on
        pvp.OnRankTotal(4721392);
        pvp.SetActive(true);

        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);    // kill 1: +3780 AP, +5600 RP
        pvp.OnRankTotal(4726992);
        pvp.OnApTotal(147039);
        wait(5);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourDeath))!.Value);   // death: -280 RP
        pvp.OnRankTotal(4726712);

        Assert.Equal((1, 1, 0), (pvp.Kills, pvp.Deaths, pvp.Assists));
        Assert.Equal(3780, pvp.ApGained);
        Assert.Equal(5320, pvp.RankGained);
    }

    [Fact]
    public void ApRightAfterSomeoneElsesKill_IsAnAssist()
    {
        var (_, pvp, _, wait) = Setup();
        pvp.OnApTotal(100000);
        pvp.SetActive(true);

        pvp.OnDeath(PvpParser.ParseDeath(Hex(OthersKill))!.Value);
        wait(1);
        pvp.OnApTotal(101000);              // your share: assist
        Assert.Equal(1, pvp.Assists);

        wait(10);
        pvp.OnApTotal(101005);              // passive AP with no kill nearby: not an assist
        Assert.Equal(1, pvp.Assists);
        Assert.Equal(1005, pvp.ApGained);
    }

    /// <summary>Owner's session 19:44:07: your kill (+877 AP), you die, someone else's kill, +1346 AP 0.1 s later.</summary>
    [Fact]
    public void AssistRightAfterYourOwnKill_StillCounts()
    {
        var (_, pvp, _, wait) = Setup();
        pvp.OnApTotal(225284);
        pvp.SetActive(true);

        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);
        pvp.OnApTotal(226161);
        wait(0.4);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourDeath))!.Value);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(OthersKill))!.Value);
        wait(0.1);
        pvp.OnApTotal(227507);

        Assert.Equal((1, 1, 1), (pvp.Kills, pvp.Deaths, pvp.Assists));
        Assert.Equal(2223, pvp.ApGained);
    }

    /// <summary>Someone else's kill 0.6 s before yours, then your AP: that AP is your kill's, not an assist.</summary>
    [Fact]
    public void ApAfterYourKill_IsNotAnAssist_EvenWithAnotherKillJustBefore()
    {
        var (_, pvp, _, wait) = Setup();
        pvp.OnApTotal(218684);
        pvp.SetActive(true);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(OthersKill))!.Value);
        wait(0.6);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);
        pvp.OnApTotal(222003);
        Assert.Equal((1, 0), (pvp.Kills, pvp.Assists));
    }

    /// <summary>Owner's session 19:46: a pull of Abyss mobs, each death naming him as killer. Not kills.</summary>
    [Fact]
    public void MobKills_DoNotCount_NorTheirApAsAssists()
    {
        var (_, pvp, _, wait) = Setup();
        pvp.OnApTotal(230000);
        pvp.SetActive(true);
        // Mob 41030 (never announced as a player), killed by you; then a mob killed by someone else.
        const string YourMobKill = "29048DC6C00287D4E100EE611805" + "05" + "416C706861" + "08" + "4775696C646F6E65" + "010000000000000100";
        const string OthersMobKill = "22048DC7C00287D4E100E7071805" + "05" + "4F74686572" + "02" + "5859" + "010000000000000100";
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourMobKill))!.Value);
        pvp.OnApTotal(230044);
        wait(0.3);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(OthersMobKill))!.Value);
        pvp.OnApTotal(230050);

        Assert.Equal((0, 0), (pvp.Kills, pvp.Assists));
        Assert.Equal(50, pvp.ApGained);   // AP is AP, whatever paid it
    }

    /// <summary>End session: who it was about goes with it; enemy victims get the enemy server learned from killers.</summary>
    [Fact]
    public void EndSession_KeepsWhoItWasAbout()
    {
        var (_, pvp, _, wait) = Setup();
        pvp.OnApTotal(100000);
        pvp.SetActive(true);

        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourDeath))!.Value);   // killer "Bravo" of "XY", server 0x08FF
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);    // you (server 0x0518) kill 13200
        pvp.OnApTotal(103780);
        wait(2);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(OthersKill))!.Value);  // someone else kills 7999
        wait(0.1);
        pvp.OnApTotal(104000);                                      // assist

        var s = pvp.EndSession()!;
        Assert.Equal((1, 1, 1, 4000L), (s.Kills, s.Deaths, s.Assists, s.Ap));
        var death = s.Events.Single(e => e.Kind == PvpEventKind.Death);
        Assert.Equal(("Bravo", "XY", 2303), (death.Name, death.Legion, death.ServerId));
        var kill = s.Events.Single(e => e.Kind == PvpEventKind.Kill);
        Assert.Equal(("Enemy13200", 2303), (kill.Name, kill.ServerId));
        Assert.Equal("Enemy7999", s.Events.Single(e => e.Kind == PvpEventKind.Assist).Name);

        Assert.Equal((0, 0), (pvp.Kills, pvp.Events.Count));      // fresh count, still on
        Assert.True(pvp.Active);
        Assert.Null(pvp.EndSession());                              // nothing happened: nothing saved
    }

    [Fact]
    public void PvpHistory_SurvivesARestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hawkeye-pvp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "pvp-sessions.json");
            var ev = new PvpEvent(new DateTime(2026, 11, 21, 19, 42, 14), PvpEventKind.Kill, "Enemy", "Legion", 2303, 0, "", 0);
            var a = new PvpSession(Guid.NewGuid(), new DateTime(2026, 11, 21, 19, 41, 0), new DateTime(2026, 11, 21, 19, 49, 0), "Alpha", 13, 4, 2, 22327, 40000, [ev]);
            var b = a with { Id = Guid.NewGuid(), EndedAt = a.EndedAt.AddHours(1) };
            var history = new PvpHistory(file);
            history.Add(a);
            history.Add(b);
            history.Delete(b.Id);
            history.FlushAsync().Wait();

            var reloaded = new PvpHistory(file).Sessions;
            var only = Assert.Single(reloaded);
            Assert.Equal((a.Id, 13, "Enemy", 2303), (only.Id, only.Kills, only.Events[0].Name, only.Events[0].ServerId));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("Triniel EU", "eu-triniel")]
    [InlineData("Kaisinel (EU)", "eu-kaisinel")]
    [InlineData("Siel NA East", "nae-siel")]
    [InlineData("siel (NA West)", "naw-siel")]
    [InlineData("Israphel Asia", "as-israphel")]
    [InlineData("Marchutan South America", "la-marchutan")]
    [InlineData("eu-triniel", "eu-triniel")]
    [InlineData("Triniel", null)]
    [InlineData("", null)]
    public void EnemyServerSetting_BecomesTheShugoSlug(string typed, string? slug) => Assert.Equal(slug, ShugoLinks.Slug(typed));

    [Fact]
    public void ProfileLinks_KnownIdsFirst_ThenTheSetting()
    {
        var known = new PvpEvent(DateTime.Now, PvpEventKind.Death, "Bravo", "XY", 2303, 0, "", 0);
        Assert.Equal("https://shugo.gg/character/global/eu-triniel/Bravo", ShugoLinks.For(known, "Siel NA East"));
        var unknown = known with { ServerId = 9999, Name = "Zoë" };
        Assert.Equal("https://shugo.gg/character/global/nae-siel/Zo%C3%AB", ShugoLinks.For(unknown, "Siel NA East"));
        Assert.Null(ShugoLinks.For(unknown, ""));
    }

        /// <summary>Owner, 2026-11-21: the first kill after starting Hawkeye did not count its AP or rank.</summary>
    [Fact]
    public void FirstKillAfterRestart_CountsAgainstRememberedTotals()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hawkeye-tot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "pvp-totals.json");
            var before = new PvpTotalsStore(file);
            before.Remember("Alpha", ap: 214904, rank: 4836948);  // last seen before Hawkeye closed
            before.FlushAsync().Wait();

            var entities = new EntityTracker();
            entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
            foreach (var enemy in new[] { 13200, 7999 }) entities.SetSessionPlayerName(enemy, "Enemy" + enemy, 0);
            var pvp = new PvpTracker(entities, null, new PvpTotalsStore(file));
            pvp.SetActive(true);

            pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);
            pvp.OnRankTotal(4838948);                             // +2000
            pvp.OnApTotal(218684);                                // +3780
            Assert.Equal((1, 3780L, 2000L), (pvp.Kills, pvp.ApGained, pvp.RankGained));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RememberedTotal_TooFarBehind_IsNotCounted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hawkeye-tot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "pvp-totals.json");
            var store = new PvpTotalsStore(file);
            store.Remember("Alpha", ap: 143254);                  // hours ago; 75k AP earned since, Hawkeye closed
            var entities = new EntityTracker();
            entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
            var pvp = new PvpTracker(entities, null, store);
            pvp.SetActive(true);
            pvp.OnApTotal(218684);
            Assert.Equal(0, pvp.ApGained);
            pvp.OnApTotal(222003);                                // from here on, normal
            Assert.Equal(3319, pvp.ApGained);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void KillBeforeEnemyServerKnown_GetsItOnceLearned()
    {
        var (_, pvp, _, _) = Setup();
        pvp.SetActive(true);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);   // enemy server not known yet
        Assert.Equal(0, pvp.Events.Single().ServerId);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourDeath))!.Value);  // an enemy (server 2303) kills you
        Assert.Equal(2303, pvp.Events.First(e => e.Kind == PvpEventKind.Kill).ServerId);
    }

        [Fact]
    public void Off_CountsNothing_AndResetStartsAgain()
    {
        var (_, pvp, _, _) = Setup();
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);
        Assert.Equal(0, pvp.Kills);

        pvp.SetActive(true);
        pvp.OnDeath(PvpParser.ParseDeath(Hex(YourKill))!.Value);
        Assert.Equal(1, pvp.Kills);
        pvp.Reset();
        Assert.Equal(0, pvp.Kills);
    }
}
