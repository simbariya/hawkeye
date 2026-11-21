using Hawkeye.Engine.Services.Entity;
using Hawkeye.Engine.Services.Odyle;

namespace Hawkeye.Tests;

/// <summary>
/// Odyle from the counters message (0x610B). The fixtures are the real messages from three of the owner's
/// characters, whose numbers the owner read from the game (2026-11-21).
/// </summary>
public sealed class OdyleTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"hawkeye-chars-{Guid.NewGuid():N}.json");
    private DateTime clock = new(2026, 11, 21, 15, 0, 0);

    public void Dispose() => File.Delete(path);

    private static byte[] Fixture(string name) =>
        Convert.FromHexString(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"counters-{name}.hex")).Trim());

    [Theory]
    [InlineData("altb-250-345", 250, 345)]
    [InlineData("alta-480-295", 480, 295)]
    [InlineData("cleric-760-40", 760, 40)]
    [InlineData("main-60-360", 60, 360)]
    public void ReadsOdyle_AsTheGameShowsIt(string fixture, int current, int pool)
    {
        var r = OdyleParser.Parse(Fixture(fixture));
        Assert.NotNull(r);
        Assert.Equal(current, r.Value.Current);
        Assert.Equal(pool, r.Value.Pool);
    }

    [Fact]
    public void SomethingElse_IsNotMistakenForOdyle()
    {
        Assert.Null(OdyleParser.Parse(Convert.FromHexString("0B21 8D92 F802 0001".Replace(" ", ""))));
        Assert.Null(OdyleParser.Parse(new byte[64]));
    }

    [Fact]
    public void Reading_GoesToTheCharacterNamedRightAfter_AndIsRemembered()
    {
        var entities = new EntityTracker();
        var odyle = new OdyleTracker(entities, path, clock: () => clock);

        odyle.OnReading(250, 345);                                 // the game sends Odyle first...
        clock = clock.AddSeconds(4);
        entities.SetSessionPlayerName(1, "AltB", 0, isUser: true);   // ...then says who you are

        clock = clock.AddMinutes(2);
        odyle.OnReading(480, 295);
        clock = clock.AddSeconds(4);
        entities.SetSessionPlayerName(2, "alta", 0, isUser: true);

        var list = odyle.Characters;
        Assert.Equal(2, list.Count);
        Assert.Equal(("alta", 480, 295), (list[0].Name, list[0].Current, list[0].Pool));
        Assert.Equal(("AltB", 250, 345), (list[1].Name, list[1].Current, list[1].Pool));
        Assert.Equal("alta", odyle.Current!.Name);

        Thread.Sleep(300); // saved off the packet thread
        var reloaded = new OdyleTracker(new EntityTracker(), path, clock: () => clock);
        Assert.Equal(2, reloaded.Characters.Count);
    }

    [Fact]
    public void Reading_WithNoSwitchAfter_BelongsToTheCharacterYouAreOn()
    {
        var entities = new EntityTracker();
        entities.SetSessionPlayerName(1, "Main", 0, isUser: true);
        var odyle = new OdyleTracker(entities, path, clock: () => clock);

        odyle.OnReading(100, 50);
        clock = clock.AddSeconds(30);
        odyle.Settle();

        Assert.Equal(("Main", 100, 50), (odyle.Current!.Name, odyle.Current.Current, odyle.Current.Pool));
    }
}
