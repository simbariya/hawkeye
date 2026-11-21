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
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        Assert.NotNull(provider.GetService(typeof(PvpTracker)));
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

    private static (EntityTracker, PvpTracker, Func<DateTime>, Action<double>) Setup()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(Me, "Alpha", 0, isUser: true);
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
