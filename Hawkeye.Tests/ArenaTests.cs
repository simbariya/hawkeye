using System.Reflection;
using Hawkeye.Engine.PacketProcessing.Routing;
using Hawkeye.Engine.Services.Arena;
using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Session;
using Hawkeye.Engine.Services.Timed;
using Hawkeye.Engine.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hawkeye.Tests;

/// <summary>
/// Arena mode (v0.2.26), from the owner's recorded 1v1 (2026-11-23, won 3-0, rating 1500 to 1525, the opponent a
/// Sorcerer who used Defiance once). Real messages; names and character numbers swapped for made-up ones
/// (same lengths), so "OwnerName" is you and "Rival123" the opponent.
/// </summary>
public sealed class ArenaTests : IDisposable
{
    private const string Roster = "B90119E3BCD309010000000B00000000000000070000000201A205000000000000A20500000100000000000000000A0B0C0000001F0508526976616C3132331A000000012D000000000000001F05A20500000000000001A205000000000000000200A20500000002DC05000000000000DC0500000100000000000000000D0E0F0000001805094F776E65724E616D6522000000012D000000000000001805DC0500000000000001DC05000000000000000000DC05000000";
    private const string YouKillThem = "1625E30D0E0F00000018050A0B0C0000001F05";
    private const string Result1525 = "1928E302F505000003010D0E0F000000180503000003";
    private const string StandingsBefore = "312AE30301DC05000000000000000000000002DC05000000000000000000000003FC050000000000000907000004";
    private const string StandingsAfter = "312AE30301F505000000000000010000000102DC05000000000000000000000003FC050000000000000907000004";
    // The opponent (entity 3311) casting Defiance (Sorcerer's, 15240000); the cast says its cooldown: 51,300 ms.
    private const string TheirDefiance = "1B0238EF1900408BE8004A00EF19C05CB042EE5501E49003";
    private const int Opponent = 3311;

    private readonly string path = Path.Combine(Path.GetTempPath(), $"hawkeye-test-{Guid.NewGuid():N}.json");
    private readonly EntityTracker entities = new();
    private DateTime clock = new(2026, 11, 23, 15, 17, 59);
    private readonly ArenaTracker arena;

    public ArenaTests()
    {
        entities.SetSessionPlayerName(1, "OwnerName", 0, isUser: true);
        entities.SetSessionPlayerName(Opponent, "Rival123", 0);
        arena = new ArenaTracker(entities, null, () => clock);
    }

    public void Dispose() { if (File.Exists(path)) File.Delete(path); }

    private static byte[] Hex(string h) => Convert.FromHexString(h);

    [Fact]
    public void Messages_AreRead()
    {
        var players = ArenaParser.ParseRoster(Hex(Roster))!;
        Assert.Equal(2, players.Count);
        Assert.Equal(("Rival123", 1442, 1, 1311), (players[0].Name, players[0].Rating, players[0].Team, players[0].Server));
        Assert.Equal(("OwnerName", 1500, 2), (players[1].Name, players[1].Rating, players[1].Team));

        var kill = ArenaParser.ParseKill(Hex(YouKillThem))!.Value;
        Assert.Equal((players[1].Key, players[0].Key), (kill.Killer, kill.Victim));
        Assert.Equal(1525, ArenaParser.ParseResultRating(Hex(Result1525)));

        var after = ArenaParser.ParseStandings(Hex(StandingsAfter))!;
        Assert.Equal(3, after.Count);
        Assert.Equal(new ArenaParser.Standing(1, 1525, 1, 0), after[0]);
        Assert.Equal(new ArenaParser.Standing(3, 1532, 9, 7), after[2]);
    }

    [Fact]
    public void AMatch_ScoreDefianceAndResult()
    {
        arena.OnStandings(ArenaParser.ParseStandings(Hex(StandingsBefore))!);
        Assert.False(arena.Showing);

        arena.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        Assert.True(arena.Showing);                       // a match loading switches the arena view on
        var foe = Assert.Single(arena.Match!.Enemies);
        Assert.Equal(("Rival123", 1442), (foe.Name, foe.Rating));
        Assert.Equal(TimeSpan.Zero, arena.DefianceLeft(foe));   // not used yet: ready

        for (int i = 0; i < 3; i++) arena.OnKill(ArenaParser.ParseKill(Hex(YouKillThem))!.Value);
        Assert.Equal((3, 0), (arena.Match.OurKills, arena.Match.TheirKills));

        arena.OnSkillCast(Opponent, ArenaTracker.DefianceName, TimeSpan.FromMilliseconds(51_300));
        Assert.Equal(TimeSpan.FromMilliseconds(51_300), arena.DefianceLeft(foe));
        clock = clock.AddSeconds(30);
        Assert.Equal(TimeSpan.FromMilliseconds(21_300), arena.DefianceLeft(foe));
        clock = clock.AddSeconds(30);
        Assert.Equal(TimeSpan.Zero, arena.DefianceLeft(foe));   // back

        arena.OnResult(1525);
        Assert.Equal((1, 0, 25, 1525), (arena.Wins, arena.Losses, arena.RatingChange, arena.Rating));
        Assert.True(arena.Match.Won);

        arena.OnStandings(ArenaParser.ParseStandings(Hex(StandingsAfter))!);   // leaving the arena
        Assert.False(arena.Showing);                      // back to the view you had
        Assert.Equal(1, arena.Wins);                      // not counted twice
    }

    [Fact]
    public void TheDefianceCast_IsReadWithItsCooldown()
    {
        var settings = new SettingsService(path);
        var sessions = new CombatSessionManager(entities, NullLoggerFactory.Instance, settings, new TimedEventTracker(), new TimedEventTracker());
        var type = typeof(ArenaTracker).Assembly.GetType("Hawkeye.Engine.PacketProcessing.Processors.SkillCastProcessor")!;
        var cast = (IOpcodeProcessor)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [sessions, arena], null)!;

        arena.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        cast.Process(new Packet { Data = Hex(TheirDefiance) });
        var foe = arena.Match!.Enemies[0];
        Assert.Equal(Opponent, foe.EntityId);
        Assert.Equal(TimeSpan.FromMilliseconds(51_300), foe.DefianceCooldown);
    }

    [Fact]
    public void DefianceOutsideAMatch_IsIgnored()
    {
        // In the open Abyss other players cast Defiance too (seen in the same recording after the match).
        arena.OnSkillCast(Opponent, ArenaTracker.DefianceName, TimeSpan.FromSeconds(37));
        Assert.Null(arena.Match);

        arena.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        arena.OnResult(1525);
        arena.OnSkillCast(Opponent, ArenaTracker.DefianceName, TimeSpan.FromSeconds(37));   // match over
        Assert.Null(arena.Match!.Enemies[0].DefianceAt);
    }

    [Fact]
    public void ALoss_AndAMissedResult_AreCounted()
    {
        arena.OnStandings(ArenaParser.ParseStandings(Hex(StandingsBefore))!);
        arena.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        arena.OnResult(1480);                             // rating went down: a loss
        Assert.Equal((0, 1, -20), (arena.Wins, arena.Losses, arena.RatingChange));

        // Next match: the result message is missed, the standings at the zone change settle it (a win).
        var before = ArenaParser.ParseStandings(Hex(StandingsBefore))!;
        arena.OnStandings(before);
        arena.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        arena.OnStandings(ArenaParser.ParseStandings(Hex(StandingsAfter))!);
        Assert.Equal((1, 1), (arena.Wins, arena.Losses));
    }

    // Owner, 2026-11-23: "I just won an arena but it counts 0-5 lost". Hawkeye had restarted mid-game and did not know
    // his name, so he was put on the enemy team. You are now found by your key (0xE343 as you enter, or the result).
    [Fact]
    public void YouAreFoundByYourKey_EvenWithoutYourName()
    {
        var stranger = new EntityTracker();
        var a = new ArenaTracker(stranger, null, () => clock);   // your name not known
        var ownKey = Hex("4643E3030D0E0F000000180501000000005478" + "3D09A20100000D0E0F00000018050200000000" +
                         "54783D09A20100000D0E0F000000180503000000005478" + "3D09A2010000");
        Assert.Equal(ArenaParser.ParseRoster(Hex(Roster))![1].Key, ArenaParser.ParseOwnKey(ownKey));

        a.OnOwnKey(ArenaParser.ParseOwnKey(ownKey)!.Value);
        a.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        Assert.Equal("Rival123", Assert.Single(a.Match!.Enemies).Name);
        a.OnKill(ArenaParser.ParseKill(Hex(YouKillThem))!.Value);
        Assert.Equal((1, 0), (a.Match.OurKills, a.Match.TheirKills));
    }

    [Fact]
    public void WithoutTheEntryMessage_TheResultStillFindsYou()
    {
        var a = new ArenaTracker(new EntityTracker(), null, () => clock);
        a.OnRoster(ArenaParser.ParseRoster(Hex(Roster))!);
        Assert.Equal(2, a.Match!.Enemies.Count);              // not sure who you are yet
        for (int i = 0; i < 3; i++) a.OnKill(ArenaParser.ParseKill(Hex(YouKillThem))!.Value);
        a.OnResult(1525, ArenaParser.ParseResultKey(Hex(Result1525)));
        Assert.Equal("Rival123", Assert.Single(a.Match.Enemies).Name);
        Assert.Equal((3, 0, true, 1, 25), (a.Match.OurKills, a.Match.TheirKills, a.Match.Won, a.Wins, a.RatingChange));
    }

    [Theory]
    [InlineData(1304, "eu-kaisinel")]    // the owner's
    [InlineData(1311, "eu-tahabata")]    // an arena opponent the game showed as TAH
    [InlineData(2301, "eu-israphel")]
    [InlineData(2303, "eu-triniel")]
    [InlineData(9999, null)]
    public void ServerIds_GiveShugoLinks(int server, string? slug) =>
        Assert.Equal(slug, Hawkeye.Engine.Services.Pvp.ShugoLinks.SlugFor(server));

    [Fact]
    public void AppServiceSetup_CanBuildIt()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<EntityTracker>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ArenaTracker>(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        Assert.NotNull(provider.GetService(typeof(ArenaTracker)));
    }
}
